using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Xml;
using System.Xml.Linq;
using WebWeaver.Controls;
using WebWeaver.Models;
using WebWeaver.Services;

namespace WebWeaver
{
    /// <summary>
    /// Логика взаимодействия для файла MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // ── Состояние ────────────────────────────────────────────────────
        private readonly List<NodeControl> _nodes = new();
        private readonly List<ConnectionModel> _connections = new();
        private readonly List<ConnectionArrow> _arrows = new();

        private static void T(FrameworkElement el, DependencyProperty p, string key) => el.SetResourceReference(p, key);

        private double _scale = 1.0;
        private double _offsetX = 0;
        private double _offsetY = 0;

        // Панорамирование
        private bool _isPanning;
        private Point _panStart;

        // Соединение
        private NodeControl? _connectSource;
        private bool _connectFromLeft; // true = начали с левого порта
        private System.Windows.Shapes.Line? _tempLine;

        // Выделение
        private NodeControl? _selectedNode;

        // Инфопанель
        private bool _infoPanelVisible;
        private bool _infoPanelIsLarge;

        // ── Текущий файл (для перезаписи) ────────────────────────────────
        private string? _currentFilePath;

        // Нода, которую сейчас смотрят в блокноте — для центрирования при закрытии панели
        private NodeControl? _lastViewedNode;

        public MainWindow()
        {
            SettingsManager.Load();            

            // ВАЖНО: до InitializeComponent
            _gridHost = new VisualHost(_gridVisual);

            InitializeComponent();

            // ВАЖНО: после  InitializeComponent
            LoadButtonPanel(this);

            SettingsManager.ThemeChanged += ApplyThemeToUI;
            AutosaveManager.SaveRequested += AutosaveSave;
            Closed += (_, _) => { SettingsManager.ThemeChanged -= ApplyThemeToUI; AutosaveManager.Stop(); };

            Loaded += MainWindow_Loaded;
            SizeChanged += (_, _) => RedrawGrid();
            KeyDown += MainWindow_KeyDown;

            PreviewKeyDown += (_, _) => UpdateCtrlCursor();
            PreviewKeyUp += (_, _) => UpdateCtrlCursor();
            AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(MainWindow_PreviewMouseUp), true);

            infoPanel.SaveRequested += InfoPanel_SaveRequested;
            infoPanel.CancelRequested += HideInfoPanel;
            infoPanel.LinkNodeRequested += id =>
            {
                var ctrl = _nodes.FirstOrDefault(n => n.Model.Id == id);
                if (ctrl == null) return;

                SelectNode(ctrl);
                ShowInfoPanelForView(ctrl.Model); // уже вызывает EnsureNodeVisibleInFreeZone
            };

            // Карты-узлы: начальный уровень + верхняя панель навигации
            _mapStack.Add(new MapLevel { Map = new MapData(), Title = "Корень" });

            InitHistory();
        }

        private void MainWindow_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            if (_autoPanDragNode == null)
                return;

            _autoPanDragNode = null;

            // При рамочном выделении таймер остановит FinishRubberSelection.
            if (!_rubberActive)
                StopAutoPan();
        }

        private void MainWindow_Loaded(object s, RoutedEventArgs e)
        {
            ApplyTransform();
            RedrawGrid();
            SetStatus("Готово. ПКМ по карте — создать ноду.");

            // Аргумент командной строки
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && System.IO.File.Exists(args[1]))
                OpenMapFromPath(args[1]);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Отмена по Escape
            if (e.Key == Key.Escape)
            {
                // 1. Отмена создания связи
                if (_connectSource != null)
                {
                    _connectSource = null;
                    ClearTempLine(); // если метод называется так
                    SetStatus("Соединение отменено.");
                    e.Handled = true;
                    return;
                }

                // 2. Закрытие информационной панели
                if (_infoPanelVisible)
                {
                    HideInfoPanel();
                    e.Handled = true;
                    return;
                }
            }

            // Создать новую ноду
            if (e.Key == Key.Insert)
            {
                BtnNewNode();
                e.Handled = true;
            }

            // Горячие клавиши с Ctrl
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.S: // Ctrl + S -> Сохранить
                        BtnSave();
                        e.Handled = true;
                        break;

                    case Key.O: // Ctrl + O -> Открыть
                        BtnOpen();
                        e.Handled = true;
                        break;

                    case Key.F: // Ctrl + F -> Найти ноду
                        BtnFindNode2();
                        e.Handled = true;
                        break;
                    case Key.Delete: // Ctrl + Delete -> Очистить всё
                        BtnClearAll();
                        e.Handled = true;
                        break;

                    case Key.OemPlus: // Ctrl + "+" -> Приблизить
                        ZoomAt(AppSettings.ZoomStep);
                        e.Handled = true;
                        break;

                    case Key.OemMinus: // Ctrl + "-" -> Отдалить
                        ZoomAt(-AppSettings.ZoomStep);
                        e.Handled = true;
                        break;

                    case Key.Home: // Ctrl + Home -> Сбросить вид
                        ResetView();
                        e.Handled = true;
                        break;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ТРАНСФОРМАЦИЯ КАРТЫ
        // ═══════════════════════════════════════════════════════════════
        private void ApplyTransform()
        {
            // Ограничение масштаба
            _scale = Math.Max(AppSettings.ZoomMin, Math.Min(AppSettings.ZoomMax, _scale));

            scaleT.ScaleX = _scale;
            scaleT.ScaleY = _scale;
            translateT.X = _offsetX;
            translateT.Y = _offsetY;

            tbZoom.Text = $"{_scale * 100:F0}%";
            RedrawGrid();
            RedrawArrows();
        }

        private readonly DrawingVisual _gridVisual = new();
        private VisualHost? _gridHost;   // инициализируется в конструкторе

        private void RedrawGrid()
        {
            double w = canvasBorder.ActualWidth;
            double h = canvasBorder.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double spacing = AppSettings.GridSpacing * _scale;

            using (var dc = _gridVisual.RenderOpen())
            {
                // При слишком мелкой сетке — не рисовать
                if (spacing < 8)
                {
                    // Просто заливка без точек
                    // dc остаётся пустым — холст чистый
                    return;
                }

                var brush = new SolidColorBrush(AppSettings.GridDotColor);
                brush.Freeze();

                double startX = _offsetX % spacing;
                double startY = _offsetY % spacing;

                int cols = (int)(w / spacing) + 1;
                int rows = (int)(h / spacing) + 1;

                // Защита: не более 40000 точек
                if (cols * rows > 40000)
                {
                    // Разредить сетку в 2 раза
                    spacing *= 2;
                    startX = _offsetX % spacing;
                    startY = _offsetY % spacing;
                    cols = (int)(w / spacing) + 1;
                    rows = (int)(h / spacing) + 1;
                }

                for (int ix = 0; ix <= cols; ix++)
                    for (int iy = 0; iy <= rows; iy++)
                        dc.DrawEllipse(brush, null,
                            new Point(startX + ix * spacing, startY + iy * spacing),
                            1.5, 1.5);
            }

            if (_gridHost != null && !gridCanvas.Children.Contains(_gridHost))
                gridCanvas.Children.Add(_gridHost);
        }

        //private readonly VisualHost _gridHost;

        // ═══════════════════════════════════════════════════════════════
        // СОБЫТИЯ МЫШИ НА КАРТЕ
        // ═══════════════════════════════════════════════════════════════
        private void MainCanvas_MouseWheel(object s, MouseWheelEventArgs e)
        {
            double oldScale = _scale;
            double delta = e.Delta > 0 ? AppSettings.ZoomStep : -AppSettings.ZoomStep;
            double newScale = Math.Max(AppSettings.ZoomMin,
                             Math.Min(AppSettings.ZoomMax, _scale + delta));

            if (Math.Abs(newScale - oldScale) < 0.001) return;

            // Масштабировать относительно позиции курсора
            var mousePos = e.GetPosition(canvasBorder);

            _offsetX = mousePos.X - (mousePos.X - _offsetX) * (newScale / oldScale);
            _offsetY = mousePos.Y - (mousePos.Y - _offsetY) * (newScale / oldScale);
            _scale = newScale;

            ApplyTransform();
            e.Handled = true;
        }

        private void MainCanvas_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
        {
            // Ctrl + ЛКМ по фону — рамка выделения
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                StartRubberSelection(e);
                e.Handled = true;
                return;
            }
            if (e.ClickCount == 2)
            {
                // Прячем панель только при двойном клике по пустому месту.
                // Клик по ноде не трогаем — иначе панель закроется сразу после открытия.
                if (!IsInsideNode(e.OriginalSource as DependencyObject))
                {
                    DeselectAll();
                    HideInfoPanel();
                }
                return;
            }
            // Начать панорамирование средней кнопкой или Alt+ЛКМ
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            {
                _isPanning = true;
                _panStart = e.GetPosition(canvasBorder);
                mainCanvas.CaptureMouse();
                Cursor = Cursors.ScrollAll;
                return;
            }
            DeselectAll();
        }

        private static bool IsInsideNode(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is NodeControl) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private void MainCanvas_MouseLeftButtonUp(object s, MouseButtonEventArgs e)
        {
            if (_rubberActive)
            {
                FinishRubberSelection(e);
                e.Handled = true;
                return;
            }

            if (_isPanning)
            {
                _isPanning = false;
                mainCanvas.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
            }

            PushHistory(_groupSelection.Count > 1 ? $"Перемещение нод ({_groupSelection.Count})" : "Перемещение ноды");
        }

        private void MainCanvas_MouseMove(object s, MouseEventArgs e)
        {
            UpdateCtrlCursor();

            if (_rubberActive)
            {
                _rubberLastScreen = e.GetPosition(canvasBorder);
                UpdateRubberRect();
                return;
            }

            if (_isPanning)
            {
                var cur = e.GetPosition(canvasBorder);
                _offsetX += cur.X - _panStart.X;
                _offsetY += cur.Y - _panStart.Y;
                _panStart = cur;

                // Применять трансформ напрямую без RedrawArrows — быстро
                scaleT.ScaleX = _scale;
                scaleT.ScaleY = _scale;
                translateT.X = _offsetX;
                translateT.Y = _offsetY;
                RedrawGrid();
                return;
            }

            // Временная линия соединения
            if (_tempLine != null && _connectSource != null)
            {
                var pos = e.GetPosition(mainCanvas);
                _tempLine.X2 = pos.X;
                _tempLine.Y2 = pos.Y;
            }
        }

        // В обработчике ПКМ по карте:
        private void MainCanvas_MouseRightButtonUp(object s, MouseButtonEventArgs e)
        {
            if (_connectSource != null) { CancelConnection(); return; }
            ShowCreateNodeMenu(e);
            e.Handled = true;
        }

        private Point ScreenToCanvas(Point screenPoint)
        {
            return new Point(
                (screenPoint.X - _offsetX) / _scale,
                (screenPoint.Y - _offsetY) / _scale);
        }

        // ═══════════════════════════════════════════════════════════════
        // СОЗДАНИЕ / РЕДАКТИРОВАНИЕ НОД
        // ═══════════════════════════════════════════════════════════════
        private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            MessageBox.Show("Я еще не сделал!");
            //MessageBox.Show(Region.GetSystemRegionFromRegistry());
        }

        private void AddNodeControl(NodeModel model)
        {
            // Не даем стакаться в одном месте ноды
            SyncCurrentLevelFromCanvas();
            if (CurrentLevel.Map.Nodes != null)
            {
                foreach (var item in CurrentLevel.Map.Nodes)
                {
                    if ((item.X - model.X <= 10 || item.X - model.X >= -10) &&
                        (item.Y - model.Y <= 10 || item.Y - model.Y >= -10)) 
                    { model.X += 20; model.Y += 20; }
                }
            }            

            var ctrl = new NodeControl(model);

            ctrl.NodeMoved += NodeCtrl_NodeMoved;
            ctrl.Resized += (c, _) => NodeCtrl_NodeMoved(c);
            ctrl.RequestConnectFrom += NodeCtrl_RequestConnectFrom;
            ctrl.RequestConnectFromLeft += NodeCtrl_RequestConnectFromLeft;
            ctrl.RightClicked += (c, _) => ShowNodeContextMenu(c);
            ctrl.DoubleClicked += c =>
            {
                if (c.Model.EmbeddedMap != null)
                    EnterMap(c);                      // карта-узел → открываем вложенную карту
                else
                    ShowInfoPanelForView(c.Model);    // обычная нода → блокнот
            };
            ctrl.PreviewMouseLeftButtonDown += (_, e2) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    // Ctrl+ЛКМ теперь не блокирует перетаскивание.
                    // Если нода ещё не выделена — добавляем её в группу.
                    if (!_groupSelection.Contains(ctrl))
                        AddToGroupSelection(ctrl);

                    SnapshotGroupPositions();
                }
                else
                {
                    // Обычный ЛКМ по ноде вне группы сбрасывает групповое выделение.
                    if (!_groupSelection.Contains(ctrl))
                        ClearGroupSelection();

                    SnapshotGroupPositions();
                }

                _autoPanDragNode = ctrl;
                StartAutoPan();

                // ВАЖНО: e2.Handled = true здесь не ставить.
                // Событие должно дойти до NodeControl, чтобы он начал перетаскивание.
            };

            Canvas.SetLeft(ctrl, model.X);
            Canvas.SetTop(ctrl, model.Y);
            mainCanvas.Children.Add(ctrl);
            _nodes.Add(ctrl);
        }

        private void ShowNodeContextMenu(NodeControl ctrl)
        {
            SelectNode(ctrl);
            var cm = new ContextMenu();

            if (ctrl.Model.EmbeddedMap != null)
            {
                var miOpenMap = new MenuItem { Header = "🗺 Открыть карту ноды" };
                miOpenMap.Click += (_, _) => EnterMap(ctrl);
                cm.Items.Add(miOpenMap);
            }
            else
            {
                var miMakeMap = new MenuItem { Header = "🗺 Сделать картой-узлом" };
                miMakeMap.Click += (_, _) =>
                {
                    ctrl.Model.EmbeddedMap = new MapData();
                    EnterMap(ctrl);
                };
                cm.Items.Add(miMakeMap);
            }

            var miCompressBranch = new MenuItem { Header = "🗜 Сжать ветку в ноду" };
            miCompressBranch.Click += (_, _) => CompressBranchIntoNode(ctrl);
            cm.Items.Add(miCompressBranch);

            var miEdit = new MenuItem { Header = "✏️ Редактировать" };
            miEdit.Click += (_, _) => ShowInfoPanelForEdit(ctrl.Model);
            cm.Items.Add(miEdit);

            if (ctrl.Model.EmbeddedMap == null)
            {
                var miView = new MenuItem { Header = "📖 Открыть блокнот" };
                miView.Click += (_, _) => ShowInfoPanelForView(ctrl.Model);
                cm.Items.Add(miView);
            }

            var miDup = new MenuItem { Header = "📋 Дублировать" };
            miDup.Click += (_, _) => DuplicateNode(ctrl);
            cm.Items.Add(miDup);

            var miConnect = new MenuItem { Header = "🔗 Начать соединение" };
            miConnect.Click += (_, _) => StartConnection(ctrl);
            cm.Items.Add(miConnect);

            var miDisconn = new MenuItem { Header = "✂️ Удалить все связи" };
            miDisconn.Click += (_, _) => RemoveAllConnections(ctrl);
            cm.Items.Add(miDisconn);

            cm.Items.Add(new Separator());

            var miColor = new MenuItem { Header = "🎨 Быстро изменить цвет заголовка" };
            miColor.Click += (_, _) => QuickColorPick(ctrl);
            cm.Items.Add(miColor);

            cm.Items.Add(new Separator());

            var miDel = new MenuItem { Header = "🗑 Удалить ноду", Foreground = Brushes.Salmon };
            miDel.Click += (_, _) => DeleteNode(ctrl);
            cm.Items.Add(miDel);

            cm.IsOpen = true;
        }

        private void DeleteNode(NodeControl ctrl)
        {
            mainCanvas.Children.Remove(ctrl);
            _nodes.Remove(ctrl);

            // Удалить связанные соединения
            var toRemove = _connections
                .Where(c => c.FromNodeId == ctrl.Model.Id || c.ToNodeId == ctrl.Model.Id)
                .ToList();
            foreach (var c in toRemove) RemoveConnection(c);

            // Убрать ссылки из других нод
            foreach (var n in _nodes)
                n.Model.ConnectedTo.Remove(ctrl.Model.Id);

            if (_selectedNode == ctrl) _selectedNode = null;
            SetStatus($"Нода «{ctrl.Model.Name}» удалена.");
            PushHistory($"Нода удалена: «{ctrl.Model.Name}»");
        }

        private void DuplicateNode(NodeControl ctrl)
        {
            MapData? mapCopy = null;
            if (ctrl.Model.EmbeddedMap != null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(ctrl.Model.EmbeddedMap);
                mapCopy = System.Text.Json.JsonSerializer.Deserialize<MapData>(json);
            }

            var m2 = new NodeModel
            {
                Name = ctrl.Model.Name + " (копия)",
                Text = ctrl.Model.Text,
                X = ctrl.Model.X + 30,
                Y = ctrl.Model.Y + 30,
                Width = ctrl.Model.Width,
                Height = ctrl.Model.Height,
                BackgroundColorHex = ctrl.Model.BackgroundColorHex,
                HeaderColorHex = ctrl.Model.HeaderColorHex,
                TextColorHex = ctrl.Model.TextColorHex,
                FontFamily = ctrl.Model.FontFamily,
                FontSize = ctrl.Model.FontSize,
                ImagePath = ctrl.Model.ImagePath,
                EmbeddedMap = mapCopy
            };

            AddNodeControl(m2);
            PushHistory($"Дублирование ноды «{m2.Name}»");
        }

        private void QuickColorPick(NodeControl ctrl)
        {
            var colors = new[]
            {
                "#3C82C8", "#C84040", "#40C870", "#C89040",
                "#8040C8", "#40B8C8", "#C84090", "#808080"
            };
            var cm = new ContextMenu();
            foreach (var hex in colors)
            {
                var mi = new MenuItem
                {
                    Header = "  ",
                    Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(hex)!)
                };
                mi.Click += (_, _) =>
                {
                    ctrl.Model.HeaderColorHex = hex;
                    ctrl.Refresh();
                    RedrawArrows();
                };
                cm.Items.Add(mi);
            }
            cm.IsOpen = true;
        }

        // ═══════════════════════════════════════════════════════════════
        // СОЕДИНЕНИЯ
        // ═══════════════════════════════════════════════════════════════
        private void StartConnection(NodeControl source) => StartConnecting(source, fromLeft: false);

        private void DrawArrow(ConnectionModel conn)
        {
            var fromCtrl = _nodes.FirstOrDefault(n => n.Model.Id == conn.FromNodeId);
            var toCtrl = _nodes.FirstOrDefault(n => n.Model.Id == conn.ToNodeId);
            if (fromCtrl == null || toCtrl == null) return;

            fromCtrl.UpdateLayout();
            toCtrl.UpdateLayout();

            Point from = conn.FromPort == "left"
                ? fromCtrl.GetLeftPortCenter()
                : fromCtrl.GetRightPortCenter();

            Point to = conn.ToPort == "left"
                ? toCtrl.GetLeftPortCenter()
                : toCtrl.GetRightPortCenter();

            var arrow = new ConnectionArrow();
            arrow.Update(from, to);
            arrow.Tag = conn;

            arrow.MouseRightButtonUp += (_, e) =>
            {
                mainCanvas.Children.Remove(arrow);
                _arrows.Remove(arrow);
                _connections.Remove(conn);
                fromCtrl.Model.ConnectedTo.Remove(conn.ToNodeId);
                PushHistory($"Связь удалена: «{fromCtrl.Model.Name}» → «{toCtrl.Model.Name}»");
                SetStatus("Связь удалена (ПКМ по линии).");
                e.Handled = true;
            };

            mainCanvas.Children.Insert(0, arrow);
            _arrows.Add(arrow);
        }

        private void RemoveConnection(ConnectionModel conn)
        {
            int idx = _connections.IndexOf(conn);
            if (idx >= 0 && idx < _arrows.Count)
            {
                mainCanvas.Children.Remove(_arrows[idx]);
                _arrows.RemoveAt(idx);
            }
            _connections.Remove(conn);

            var src = _nodes.FirstOrDefault(n => n.Model.Id == conn.FromNodeId);
            var tgt = _nodes.FirstOrDefault(n => n.Model.Id == conn.ToNodeId);
            src?.Model.ConnectedTo.Remove(conn.ToNodeId);
            tgt?.Model.ConnectedTo.Remove(conn.FromNodeId);
        }

        private void RemoveAllConnections(NodeControl ctrl)
        {
            var toRemove = _connections
                .Where(c => c.FromNodeId == ctrl.Model.Id || c.ToNodeId == ctrl.Model.Id)
                .ToList();
            foreach (var c in toRemove) RemoveConnection(c);
            SetStatus($"Все связи ноды «{ctrl.Model.Name}» удалены.");
            PushHistory($"Все связи ноды «{ctrl.Model.Name}» удалены.");
        }

        private void RedrawArrows()
        {
            // Удалить старые стрелки
            foreach (var a in _arrows)
                mainCanvas.Children.Remove(a);
            _arrows.Clear();

            // Перерисовать
            foreach (var conn in _connections)
                DrawArrow(conn);
        }

        // ═══════════════════════════════════════════════════════════════
        // ИНФО-ПАНЕЛЬ
        // ═══════════════════════════════════════════════════════════════
        private void ShowInfoPanelForCreate(NodeModel model)
        {
            infoPanel.LoadForCreate(model);
            AnimateInfoPanel(show: true, large: false);
            PushHistory("Создание ноды");
        }

        private void ShowInfoPanelForEdit(NodeModel model)
        {
            infoPanel.LoadForEdit(model);
            AnimateInfoPanel(show: true, large: false);
            PushHistory($"Изменение ноды «{model.Name}»");
        }

        private void ShowInfoPanelForView(NodeModel model)
        {
            infoPanel.LoadForView(model, _nodes.Select(n => n.Model));
            AnimateInfoPanel(show: true, large: true);

            var ctrl = _nodes.FirstOrDefault(n => n.Model.Id == model.Id);
            if (ctrl != null)
            {
                _lastViewedNode = ctrl;
                Dispatcher.InvokeAsync(() => EnsureNodeVisibleInFreeZone(ctrl),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void HideInfoPanel()
        {
            AnimateInfoPanel(show: false, large: _infoPanelIsLarge);
            _infoPanelVisible = false;

            // ← всё, что ниже, — добавка
            var ctrl = _lastViewedNode;
            _lastViewedNode = null;
            if (ctrl != null && _nodes.Contains(ctrl))
                CenterNodeOnScreen(ctrl);
        }

        private void AnimateInfoPanel(bool show, bool large)
        {
            _infoPanelIsLarge = large;

            // Высота
            infoPanel.VerticalAlignment = VerticalAlignment.Top;

            double shownMargin = 0;
            double hiddenMargin = -infoPanel.Width - 2;

            double targetMargin = show ? shownMargin : hiddenMargin;
            _infoPanelVisible = show;

            var anim = new ThicknessAnimation
            {
                To = new Thickness(targetMargin, 0, 0, 0),
                Duration = TimeSpan.FromMilliseconds(AppSettings.InfoPanelAnimationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            infoPanel.BeginAnimation(MarginProperty, anim);
        }

        private void InfoPanel_SaveRequested(NodeModel model)
        {
            // Проверить, есть ли уже нода с таким Id
            var existing = _nodes.FirstOrDefault(n => n.Model.Id == model.Id);
            if (existing == null)
            {
                // Новая нода
                AddNodeControl(model);
                PushHistory("Создание ноды");
            }
            else
            {
                // Обновить существующую
                existing.Refresh();
                RedrawArrows();
                SetStatus($"Нода «{model.Name}» обновлена.");
                PushHistory($"Нода обновлена: «{model.Name}»");
            }
            HideInfoPanel();
        }

        // ═══════════════════════════════════════════════════════════════
        // ВЫДЕЛЕНИЕ НОД
        // ═══════════════════════════════════════════════════════════════
        private void SelectNode(NodeControl ctrl)
        {
            DeselectAll();
            _selectedNode = ctrl;
            ctrl.SetSelected(true);
            Panel.SetZIndex(ctrl, 100);
        }

        private void DeselectAll()
        {
            ClearGroupSelection();

            if (_selectedNode != null)
            {
                _selectedNode.SetSelected(false);
                Panel.SetZIndex(_selectedNode, 1);
                _selectedNode = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // НАВИГАЦИЯ ПО КАРТЕ
        // ═══════════════════════════════════════════════════════════════
        private (double cx, double cy) GetCanvasCenter()
        {
            return (canvasBorder.ActualWidth / 2, canvasBorder.ActualHeight / 2);
        }

        private void FocusNode(NodeControl ctrl)
        {
            var (cx, cy) = GetCanvasCenter();
            _offsetX = cx - ctrl.Model.X * _scale - (ctrl.Model.Width * _scale / 2);
            _offsetY = cy - ctrl.Model.Y * _scale - (ctrl.Model.Height * _scale / 2);
            ApplyTransform();
            SelectNode(ctrl);
            SetStatus($"Переход к ноде «{ctrl.Model.Name}».");
            PushHistory($"Переход к ноде: «{ctrl.Model.Name}»");
        }

        // ═══════════════════════════════════════════════════════════════
        // БУФЕР ОБМЕНА
        // ═══════════════════════════════════════════════════════════════
        private sealed class ClipboardData
        {
            public List<NodeModel> Nodes { get; set; } = new();
            public List<ConnectionModel> Connections { get; set; } = new();
        }

        private ClipboardData? _clipboard;
        private int _pasteShift; // чтобы повторные Ctrl+V не вставали точно друг на друга

        private void CopySelectedGroupToClipboard(bool cut = false)
        {
            // если группы нет, но есть одиночная выделенная нода — работаем с ней
            if (_groupSelection.Count == 0 && _selectedNode != null)
                AddToGroupSelection(_selectedNode);

            if (_groupSelection.Count == 0) { SetStatus("Нет выделенных нод."); return; }

            var ids = _groupSelection.Select(n => n.Model.Id).ToHashSet();

            var data = new ClipboardData();
            foreach (var ctrl in _groupSelection)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(ctrl.Model);
                var copy = System.Text.Json.JsonSerializer.Deserialize<NodeModel>(json)!;
                // в ConnectedTo оставляем только ссылки на ноды внутри копии
                copy.ConnectedTo.RemoveAll(id => !ids.Contains(id));
                data.Nodes.Add(copy);
            }

            foreach (var conn in _connections)
                if (ids.Contains(conn.FromNodeId) && ids.Contains(conn.ToNodeId))
                    data.Connections.Add(new ConnectionModel
                    {
                        FromNodeId = conn.FromNodeId,
                        ToNodeId = conn.ToNodeId,
                        FromPort = conn.FromPort,
                        ToPort = conn.ToPort
                    });

            _clipboard = data;

            if (cut)
            {
                int count = ids.Count;
                DeleteSelectedGroup(); // сам пишет историю
                SetStatus($"Вырезано нод: {count} — вставьте Ctrl+V.");
            }
            else
            {
                SetStatus($"Скопировано нод: {data.Nodes.Count} — вставьте Ctrl+V.");
            }
        }

        private void PasteFromClipboard()
        {
            if (_clipboard == null || _clipboard.Nodes.Count == 0)
            {
                SetStatus("Буфер обмена пуст.");
                return;
            }

            // полная копия буфера: каждый Ctrl+V даёт независимые ноды
            var json = System.Text.Json.JsonSerializer.Serialize(_clipboard);
            var data = System.Text.Json.JsonSerializer.Deserialize<ClipboardData>(json)!;

            var idMap = new Dictionary<Guid, Guid>();
            foreach (var n in data.Nodes)
            {
                idMap[n.Id] = Guid.NewGuid();
                n.Id = idMap[n.Id];
            }

            // центр видимой области
            double cx = (canvasBorder.ActualWidth / 2 - _offsetX) / _scale;
            double cy = (canvasBorder.ActualHeight / 2 - _offsetY) / _scale;

            double midX = data.Nodes.Average(n => n.X + n.Width / 2);
            double midY = data.Nodes.Average(n => n.Y + n.Height / 2);
            double shift = _pasteShift % 300;
            _pasteShift += 30;

            var newCtrls = new List<NodeControl>();

            foreach (var n in data.Nodes)
            {
                n.X += cx - midX + shift;
                n.Y += cy - midY + shift;

                n.ConnectedTo.RemoveAll(id => !idMap.ContainsKey(id));
                for (int i = 0; i < n.ConnectedTo.Count; i++)
                    n.ConnectedTo[i] = idMap[n.ConnectedTo[i]];
            }

            DeselectAll();

            foreach (var n in data.Nodes)
            {
                AddNodeControl(n);
                newCtrls.Add(_nodes.First(x => ReferenceEquals(x.Model, n)));
            }

            var newConns = new List<ConnectionModel>();
            foreach (var c in data.Connections)
            {
                if (!idMap.TryGetValue(c.FromNodeId, out var f) ||
                    !idMap.TryGetValue(c.ToNodeId, out var t))
                    continue;

                var nc = new ConnectionModel
                {
                    FromNodeId = f,
                    ToNodeId = t,
                    FromPort = c.FromPort,
                    ToPort = c.ToPort
                };
                _connections.Add(nc);
                var fc = _nodes.First(x => x.Model.Id == f);
                if (!fc.Model.ConnectedTo.Contains(t)) fc.Model.ConnectedTo.Add(t);
                newConns.Add(nc);
            }

            Dispatcher.InvokeAsync(() =>
            {
                foreach (var nc in newConns) DrawArrow(nc);
            }, System.Windows.Threading.DispatcherPriority.Loaded);

            foreach (var ctrl in newCtrls) AddToGroupSelection(ctrl);
            SnapshotGroupPositions();

            PushHistory($"Вставка нод ({data.Nodes.Count})");
            SetStatus($"Вставлено нод: {data.Nodes.Count}");
        }


        // ═══════════════════════════════════════════════════════════════
        // ГОРЯЧИЕ КЛАВИШИ
        // ═══════════════════════════════════════════════════════════════
        private void MainWindow_KeyDown(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _connectSource != null)
            {
                _connectSource = null;
                ClearTempLine();
                SetStatus("Соединение отменено.");
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBoxBase)
            {
                if (_rubberActive)
                {
                    // отменяем незавершённую рамку
                    if (_rubberRect != null)
                    {
                        mainCanvas.Children.Remove(_rubberRect);
                        _rubberRect = null;
                    }
                    _rubberActive = false;
                    StopAutoPan();
                    mainCanvas.ReleaseMouseCapture();
                    canvasBorder.ReleaseMouseCapture();
                    Cursor = Cursors.Arrow;
                }
                else
                {
                    ClearGroupSelection();
                    DeselectAll();
                    HideInfoPanel();
                }

                SetStatus("Выделение снято");
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                UndoHistory();
                e.Handled = true;
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                RedoHistory();
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && Keyboard.FocusedElement is not TextBoxBase) 
            { 
                SaveAs();  
                e.Handled = true;
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                BtnFindNode();
            }
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            {
                RedoHistory(); // бонус: привычный Ctrl+Y
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _rubberActive)
            {
                CancelRubberSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && _groupSelection.Count > 0 && Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
            {
                DeleteSelectedGroup();
                e.Handled = true;
            }
            else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control && _groupSelection.Count > 0 && Keyboard.FocusedElement is not TextBoxBase)
            {
                DeleteSelectedGroup();
                e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && _groupSelection.Count > 0 && Keyboard.FocusedElement is not TextBoxBase)
            {
                CopySelectedGroupToClipboard();          // копировать
                e.Handled = true;
            }
            else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control && _groupSelection.Count > 0 && Keyboard.FocusedElement is not TextBoxBase)
            {
                CopySelectedGroupToClipboard(cut: true); // вырезать
                e.Handled = true;
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBoxBase)
            {
                PasteFromClipboard();
                e.Handled = true;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // КАРТЫ-УЗЛЫ: ВЛОЖЕННЫЕ КАРТЫ И НАВИГАЦИЯ
        // ═══════════════════════════════════════════════════════════════
        private sealed class MapLevel
        {
            public MapData Map = new();
            public string Title = "Корень";
        }

        private readonly List<MapLevel> _mapStack = new();
        private MapLevel CurrentLevel => _mapStack[_mapStack.Count - 1];

        // ── Синхронизация полотна ↔ текущий уровень ────────────────────
        private void SyncCurrentLevelFromCanvas()
        {
            var m = CurrentLevel.Map;
            m.Nodes = _nodes.Select(n => n.Model).ToList();
            m.Connections.Clear();
            m.Connections.AddRange(_connections);
        }

        private void LoadCanvasFromLevel(MapLevel level)
        {
            ClearTempLine();
            _connectSource = null;
            DeselectAll();
            ClearMap();

            foreach (var model in level.Map.Nodes)
                AddNodeControl(model);
            _connections.AddRange(level.Map.Connections);

            Dispatcher.InvokeAsync(() =>
            {
                foreach (var conn in _connections.ToList())
                    DrawArrow(conn);
            }, System.Windows.Threading.DispatcherPriority.Loaded);

            ResetView();
            HideInfoPanel();
        }

        // ── Перемещение по уровням ─────────────────────────────────────
        private void NavigateToLevel(int index)
        {
            if (index < 0 || index >= _mapStack.Count) return;

            SyncCurrentLevelFromCanvas();
            if (index == _mapStack.Count - 1) return;

            _mapStack.RemoveRange(index + 1, _mapStack.Count - index - 1);
            LoadCanvasFromLevel(CurrentLevel);

            SetStatus(_mapStack.Count == 1
                ? "Вы в корневой карте."
                : $"Открыта карта: {CurrentLevel.Title}");
        }

        private void ExitMap()
        {
            if (_mapStack.Count <= 1)
            {
                SetStatus("Вы уже в корневой карте.");
                return;
            }
            NavigateToLevel(_mapStack.Count - 2);
        }

        private void GoToRoot() => NavigateToLevel(0);

        // ── Вход в карту-узел ──────────────────────────────────────────
        private void EnterMap(NodeControl ctrl)
        {
            SyncCurrentLevelFromCanvas();

            var model = ctrl.Model;
            model.EmbeddedMap ??= new MapData();

            _mapStack.Add(new MapLevel { Map = model.EmbeddedMap, Title = model.Name });
            LoadCanvasFromLevel(CurrentLevel);

            SetStatus($"Открыта карта ноды «{model.Name}». ПКМ по фону — создать ноду внутри.");
        }

        // ── Сжать всю текущую карту в одну ноду ────────────────────────
        private void CompressCurrentMapIntoNode()
        {
            if (_nodes.Count == 0)
            {
                SetStatus("Карта пуста — сжимать нечего.");
                return;
            }

            SyncCurrentLevelFromCanvas();

            var packed = new MapData
            {
                Nodes = CurrentLevel.Map.Nodes.ToList(),
                Connections = CurrentLevel.Map.Connections.ToList()
            };

            double cx = (canvasBorder.ActualWidth / 2 - _offsetX) / _scale - AppSettings.NodeDefaultWidth / 2;
            double cy = (canvasBorder.ActualHeight / 2 - _offsetY) / _scale - AppSettings.NodeDefaultHeight / 2;

            var nd = SettingsManager.Settings.NodeDefaults;
            var wrapper = new NodeModel
            {
                Name = _mapStack.Count > 1 ? CurrentLevel.Title : "Сжатая карта",
                X = cx,
                Y = cy,
                Width = AppSettings.NodeDefaultWidth,
                Height = AppSettings.NodeDefaultHeight,
                FontFamily = AppSettings.NodeDefaultFontFamily,
                FontSize = AppSettings.NodeDefaultFontSize,
                BackgroundColorHex = nd.Background,
                HeaderColorHex = "#8A5AC8",
                TextColorHex = nd.Text,
                EmbeddedMap = packed
            };

            ClearMap();
            AddNodeControl(wrapper);
            SyncCurrentLevelFromCanvas();

            SetStatus($"Карта сжата в ноду «{wrapper.Name}» ({packed.Nodes.Count} нод внутри). Двойной клик — открыть.");
            PushHistory($"Карта сжата в нodу: «{wrapper.Name}»");
        }

        // ── Сжать ветку (нода + всё, куда ведут стрелки) в одну ноду ───
        private void CompressBranchIntoNode(NodeControl rootCtrl)
        {
            var ids = new HashSet<Guid> { rootCtrl.Model.Id };

            bool added = true;
            while (added)
            {
                added = false;
                foreach (var c in _connections)
                {
                    if (ids.Contains(c.FromNodeId) && ids.Add(c.ToNodeId))
                        added = true;
                }
            }

            if (ids.Count == 1)
            {
                SetStatus("У ноды нет исходящих связей — сжимать нечего.");
                return;
            }

            SyncCurrentLevelFromCanvas();

            var packedNodes = CurrentLevel.Map.Nodes.Where(n => ids.Contains(n.Id)).ToList();
            var packedConns = CurrentLevel.Map.Connections
                .Where(c => ids.Contains(c.FromNodeId) && ids.Contains(c.ToNodeId))
                .ToList();

            var wrapper = new NodeModel
            {
                Name = rootCtrl.Model.Name,
                X = rootCtrl.Model.X,
                Y = rootCtrl.Model.Y,
                Width = rootCtrl.Model.Width,
                Height = rootCtrl.Model.Height,
                BackgroundColorHex = rootCtrl.Model.BackgroundColorHex,
                HeaderColorHex = "#8A5AC8",
                TextColorHex = rootCtrl.Model.TextColorHex,
                FontFamily = rootCtrl.Model.FontFamily,
                FontSize = rootCtrl.Model.FontSize,
                EmbeddedMap = new MapData { Nodes = packedNodes, Connections = packedConns }
            };

            DeselectAll();

            foreach (var ctrl in _nodes.Where(n => ids.Contains(n.Model.Id)).ToList())
            {
                mainCanvas.Children.Remove(ctrl);
                _nodes.Remove(ctrl);
            }

            _connections.RemoveAll(c => ids.Contains(c.FromNodeId) && ids.Contains(c.ToNodeId));
            foreach (var c in _connections)
            {
                if (ids.Contains(c.ToNodeId)) { c.ToNodeId = wrapper.Id; c.ToPort = "left"; }
                if (ids.Contains(c.FromNodeId)) { c.FromNodeId = wrapper.Id; c.FromPort = "right"; }
            }

            // убрать возможные дубли связей после пересоединения
            var seen = new HashSet<(Guid From, Guid To)>();
            _connections.RemoveAll(c => !seen.Add((c.FromNodeId, c.ToNodeId)));

            foreach (var n in _nodes)
            {
                if (n.Model.ConnectedTo.RemoveAll(ids.Contains) > 0)
                    n.Model.ConnectedTo.Add(wrapper.Id);
            }

            AddNodeControl(wrapper);
            SyncCurrentLevelFromCanvas();
            RedrawArrows();

            SetStatus($"Ветка ({packedNodes.Count} нod) сжата в нodу «{wrapper.Name}».");
            PushHistory($"Ветка сжата в нodу: «{wrapper.Name}»");
        }

        // ── Добавить ноду из сохранённого файла карты ──────────────────
        private void AddNodeFromMapFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Добавить ноду из карты",
                Filter = "Карты узлов (*.wwmap;*.gnmap)|*.wwmap;*.gnmap|Все файлы|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            AddMapFileAsNode(dlg.FileName);
        }

        // ── Заголовок окна ────────────────────────────────────────────
        private void UpdateTitleBar()
        {
            Title = _currentFilePath != null
                ? $"Interactive Whiteboard — {System.IO.Path.GetFileName(_currentFilePath)}"
                : "Interactive Whiteboard";
        }

        // ── ПОИСК НОДЫ ────────────----────────────────────────────────

        /// <summary>
        /// Открывает ноду по Id: на текущем уровне — выделяет и показывает в блокноте,
        /// внутри вложенных карт — входит в них по цепочке и открывает.
        /// </summary>
        public void OpenNodeById(Guid id)
        {
            SyncCurrentLevelFromCanvas(); // полотно → модель, чтобы поиск шёл по актуальным данным

            // 1. Текущий уровень — самый частый случай
            var ctrl = _nodes.FirstOrDefault(n => n.Model.Id == id);
            if (ctrl != null) { OpenOnCurrentLevel(ctrl); return; }

            // 2. Поиск в глубине: строим цепочку карт-узлов, ведущих к ноде
            var path = FindPathToNode(id, CurrentLevel.Map);
            if (path == null)
            {
                SetStatus($"Нода {id} не найдена");
                return;
            }

            // 3. Спуск по цепочке: входим в каждую карту-узел
            foreach (var mapNode in path)
            {
                var mapCtrl = _nodes.FirstOrDefault(n => n.Model == mapNode);
                if (mapCtrl == null) break;
                EnterMap(mapCtrl);   // ← подставьте имя вашего метода входа в карту-узел
            }                        //    (тот, что вызывается двойным кликом по карте-узлу)

            ctrl = _nodes.FirstOrDefault(n => n.Model.Id == id);
            if (ctrl != null) OpenOnCurrentLevel(ctrl);
            else SetStatus($"Нода {id} найдена, но открыть её не удалось");
        }

        private void OpenOnCurrentLevel(NodeControl ctrl)
        {
            DeselectAll();
            SelectNode(ctrl);                 // выделение
            ShowInfoPanelForView(ctrl.Model); // блокнот (внутри он сам вызовет

            SetStatus($"Переход к ноде «{ctrl.Model.Name}».");
            PushHistory($"Переход к ноде: «{ctrl.Model.Name}»");
        }                                     // EnsureNodeVisibleInFreeZone — камера подстроится)

        /// <summary>
        /// Цепочка NodeModel (карт-узлов) от текущей карты до ноды с id.
        /// null — нода не найдена; пустой список — нода лежит прямо в этой карте.
        /// </summary>
        private List<NodeModel>? FindPathToNode(Guid id, MapData map)
        {
            foreach (var n in map.Nodes)
            {
                if (n.Id == id) return new List<NodeModel>();

                if (n.EmbeddedMap == null) continue;

                var inner = FindPathToNode(id, n.EmbeddedMap);
                if (inner != null)
                {
                    inner.Insert(0, n); // добавить текущую карту-узел в начало пути
                    return inner;
                }
            }
            return null;
        }

        public sealed record NodeInfo(Guid Id, string Name, string Text, int Level, bool IsMap);

        private List<NodeInfo> CollectAllNodes()
        {
            SyncCurrentLevelFromCanvas(); // экран → модель (нужен только для текущего уровня)

            var list = new List<NodeInfo>();
            void Walk(MapData map, int level)
            {
                foreach (var n in map.Nodes)
                {
                    list.Add(new NodeInfo(n.Id, n.Name, n.Text, level, n.EmbeddedMap != null));
                    if (n.EmbeddedMap != null) Walk(n.EmbeddedMap, level + 1);
                }
            }
            Walk(_mapStack[0].Map, 0);
            return list;
        }

        private string GetExscindText(string AllText, string SearchTerm)
        {
            int index = AllText.ToLower().IndexOf(SearchTerm.ToLower(), StringComparison.OrdinalIgnoreCase);
            index = (index - 10 < 0 ? 0 : index - 10);

            return (index > 0 ? "..." : "") + AllText.Substring(index, Math.Min(20, AllText.Length - index)) + "...";
        }

        // ═══════════════════════════════════════════════════════════════
        private void ClearMap()
        {
            ClearGroupSelection();
            _savedNodeCursor.Clear();

            _nodes.ForEach(n => mainCanvas.Children.Remove(n));
            _arrows.ForEach(a => mainCanvas.Children.Remove(a));
            _nodes.Clear();
            _arrows.Clear();
            _connections.Clear();
        }

        private void ClearAll()
        {
            _mapStack.RemoveRange(1, _mapStack.Count - 1);
            CurrentLevel.Map.Nodes.Clear();
            CurrentLevel.Map.Connections.Clear();
            ClearMap();
            _selectedNode = null;
            _connectSource = null;
            ClearTempLine();
            HideInfoPanel();
            SetStatus("Карта очищена.");
            PushHistory("Карта очищена.");
        }

        // ═══════════════════════════════════════════════════════════════
        // ДЕРЕВО НОД (кто к кому принадлежит)
        // ═══════════════════════════════════════════════════════════════

        // Переход по цепочке: корень → карта → карта → …
        private void NavigateByPath(List<Guid> path)
        {
            NavigateToLevel(0);
            foreach (var id in path)
            {
                var ctrl = _nodes.FirstOrDefault(n => n.Model.Id == id);
                if (ctrl?.Model.EmbeddedMap == null) break;
                EnterMap(ctrl);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // СТАТУС-БАР
        // ═══════════════════════════════════════════════════════════════
        private void SetStatus(string msg) => tbStatus.Text = msg;

        private void NodeCtrl_NodeMoved(NodeControl ctrl)
        {
            // Группа едет вслед за перетаскиваемой нодой
            if (_groupSelection.Contains(ctrl))
            {
                if (_groupDragPos.TryGetValue(ctrl, out var prev))
                {
                    double dx = ctrl.Model.X - prev.X;
                    double dy = ctrl.Model.Y - prev.Y;

                    if (dx != 0 || dy != 0)
                    {
                        foreach (var other in _groupSelection)
                        {
                            if (ReferenceEquals(other, ctrl)) continue;
                            other.Model.X += dx;
                            other.Model.Y += dy;
                            Canvas.SetLeft(other, other.Model.X);
                            Canvas.SetTop(other, other.Model.Y);
                            _groupDragPos[other] = (other.Model.X, other.Model.Y);
                            UpdateNodeArrows(other);
                        }
                    }
                }
                _groupDragPos[ctrl] = (ctrl.Model.X, ctrl.Model.Y);
            }

            UpdateNodeArrows(ctrl);
        }

        // Бывшее тело NodeCtrl_NodeMoved — обновление стрелок одной ноды
        private void UpdateNodeArrows(NodeControl ctrl)
        {
            var related = _connections
                .Where(c => c.FromNodeId == ctrl.Model.Id || c.ToNodeId == ctrl.Model.Id)
                .ToList();

            foreach (var conn in related)
            {
                var arrow = _arrows.FirstOrDefault(a => a.Tag is ConnectionModel cm && cm == conn);
                if (arrow == null) continue;

                var fromCtrl = _nodes.FirstOrDefault(n => n.Model.Id == conn.FromNodeId);
                var toCtrl = _nodes.FirstOrDefault(n => n.Model.Id == conn.ToNodeId);
                if (fromCtrl == null || toCtrl == null) continue;

                Point from = conn.FromPort == "left" ? fromCtrl.GetLeftPortCenter() : fromCtrl.GetRightPortCenter();
                Point to = conn.ToPort == "left" ? toCtrl.GetLeftPortCenter() : toCtrl.GetRightPortCenter();

                arrow.Update(from, to);
            }
        }

        // ── Начало соединения (правый порт) ──────────────────────────
        private void NodeCtrl_RequestConnectFrom(NodeControl ctrl) =>
            StartConnecting(ctrl, fromLeft: false);

        // ── Начало соединения (левый порт) ───────────────────────────
        private void NodeCtrl_RequestConnectFromLeft(NodeControl ctrl) =>
            StartConnecting(ctrl, fromLeft: true);

        private void StartConnecting(NodeControl ctrl, bool fromLeft)
        {
            if (_connectSource == null)
            {
                _connectSource = ctrl;
                _connectFromLeft = fromLeft;
                var portCenter = fromLeft ? ctrl.GetLeftPortCenter() : ctrl.GetRightPortCenter();
                StartTempLine(portCenter);
                SetStatus($"Выбрана нода «{ctrl.Model.Name}». Кликните на порт другой ноды.");
            }
            else
            {
                CompleteConnection(ctrl, toLeft: fromLeft);
            }
        }

        // ── Временная линия ───────────────────────────────────────────
        private void StartTempLine(Point from)
        {
            ClearTempLine();
            _tempLine = new System.Windows.Shapes.Line
            {
                Stroke = new SolidColorBrush(AppSettings.ConnectionSelectedColor),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 3 },
                X1 = from.X,
                Y1 = from.Y,
                X2 = from.X,
                Y2 = from.Y,
                IsHitTestVisible = false // Линия не перехватывает клики
            };
            mainCanvas.Children.Add(_tempLine);
        }

        private void ClearTempLine()
        {
            if (_tempLine != null)
            {
                mainCanvas.Children.Remove(_tempLine);
                _tempLine = null;
            }
        }

        // ── Завершить соединение ──────────────────────────────────────
        private void CompleteConnection(NodeControl target, bool toLeft)
        {
            if (_connectSource == null) return;

            // Нельзя соединить с собой
            if (_connectSource.Model.Id == target.Model.Id)
            {
                SetStatus("Нельзя соединить ноду саму с собой.");
                CancelConnection();
                return;
            }

            // Дубликат
            if (_connections.Any(c =>
                c.FromNodeId == _connectSource.Model.Id &&
                c.ToNodeId == target.Model.Id))
            {
                SetStatus("Такая связь уже существует.");
                CancelConnection();
                return;
            }

            var conn = new ConnectionModel
            {
                FromNodeId = _connectSource.Model.Id,
                ToNodeId = target.Model.Id,
                FromPort = _connectFromLeft ? "left" : "right",
                ToPort = toLeft ? "left" : "right"
            };

            _connections.Add(conn);

            if (!_connectSource.Model.ConnectedTo.Contains(target.Model.Id))
                _connectSource.Model.ConnectedTo.Add(target.Model.Id);

            string fromName = _connectSource.Model.Name;
            string toName = target.Model.Name;

            CancelConnection();
            DrawArrow(conn);
            SetStatus($"Связь создана: «{fromName}» → «{toName}».");
            PushHistory($"Связь создана: «{fromName}» → «{toName}»");
        }

        private void CancelConnection()
        {
            _connectSource = null;
            ClearTempLine();
        }

        private void CanvasBorder_MouseLeftButtonDown(object s, MouseButtonEventArgs e) // !!!
        {
            // Ctrl + ЛКМ — рамка выделения
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (!_rubberActive) StartRubberSelection(e);
                e.Handled = true;
                return;
            }

            // Клик по пустому месту — снять выделение.
            // Определяем по координатам карты, а не по e.OriginalSource:
            // после панорамирования hit-test по визуальному дереву ненадёжен.
            if (!IsClickOverNode(e))
            {
                ClearGroupSelection();
                DeselectAll();
                HideInfoPanel();
            }

            _isPanning = true;
            _panStart = e.GetPosition(canvasBorder);
            canvasBorder.CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private bool IsClickOverNode(MouseButtonEventArgs e) // !!!
        {
            var p = ScreenToCanvas(e.GetPosition(canvasBorder));
            const double slack = 4; // небольшой запас у края ноды

            foreach (var ctrl in _nodes)
            {
                var m = ctrl.Model;
                if (p.X >= m.X - slack && p.X <= m.X + m.Width + slack &&
                    p.Y >= m.Y - slack && p.Y <= m.Y + m.Height + slack)
                    return true;
            }
            return false;
        }

        private void CanvasBorder_MouseMove(object s, MouseEventArgs e)
        {
            if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
            {
                var cur = e.GetPosition(canvasBorder);
                _offsetX += cur.X - _panStart.X;
                _offsetY += cur.Y - _panStart.Y;
                _panStart = cur;

                scaleT.ScaleX = _scale;
                scaleT.ScaleY = _scale;
                translateT.X = _offsetX;
                translateT.Y = _offsetY;
                tbZoom.Text = $"{_scale * 100:F0}%";
                RedrawGrid();
                return;
            }

            // Двигать временную линию соединения
            if (_tempLine != null && _connectSource != null)
            {
                var pos = e.GetPosition(mainCanvas);
                _tempLine.X2 = pos.X;
                _tempLine.Y2 = pos.Y;
                _tempLine.X2 = pos.X;
                _tempLine.Y2 = pos.Y;
            }
        }

        private void CanvasBorder_MouseLeftButtonUp(object s, MouseButtonEventArgs e) // !!!
        {
            if (_rubberActive)
            {
                FinishRubberSelection(e);
                e.Handled = true;
                return;
            }

            if (_isPanning)
            {
                _isPanning = false;
                canvasBorder.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                RedrawArrows();
                e.Handled = true;
            }

            PushHistory(_groupSelection.Count > 1 ? $"Перемещение нод ({_groupSelection.Count})" : "Перемещение ноды");
        }

        private void CanvasBorder_MouseRightButtonUp(object s, MouseButtonEventArgs e)
        {
            // ПКМ отменяет соединение
            if (_connectSource != null)
            {
                _connectSource = null;
                ClearTempLine();
                SetStatus("Соединение отменено.");
                PushHistory("Соединение отменено.");
                e.Handled = true;
                return;
            }

            ShowCreateNodeMenu(e);
            e.Handled = true;
        }

        private void ShowCreateNodeMenu(MouseButtonEventArgs e)
        {
            var screenPos = e.GetPosition(canvasBorder);
            var canvasPos = ScreenToCanvas(screenPos);

            var cm = new ContextMenu();

            var mi = new MenuItem { Header = "➕ Создать ноду" };
            mi.Click += (_, _) => BtnNewNode(canvasPos);
            cm.Items.Add(mi);

            var miFromMap = new MenuItem { Header = "📂 Добавить ноду из карты (.wwmap)…" };
            miFromMap.Click += (_, _) => AddNodeFromMapFile();
            cm.Items.Add(miFromMap);

            var miCompress = new MenuItem { Header = "🗜 Сжать всю карту в одну ноду" };
            miCompress.Click += (_, _) => CompressCurrentMapIntoNode();
            cm.Items.Add(miCompress);

            cm.IsOpen = true;
        }

        // ═══════════════════════════════════════════════════════════════
        // ИСТОРИЯ ОПЕРАЦИЙ (СНЕПШОТЫ)
        // ═══════════════════════════════════════════════════════════════
        private sealed class HistoryEntry
        {
            public string Title = "";
            public string Json = ""; // слепок всей карты на этот момент
        }

        private readonly List<HistoryEntry> _history = new();
        private int _historyIndex = -1;
        private bool _restoring;
        private const int MaxHistoryEntries = 100;

        // Вызывается при старте, после «Новая карта» и после открытия файла
        private void InitHistory()
        {
            _history.Clear();
            _historyIndex = -1;
            _history.Add(new HistoryEntry
            {
                Title = "Начальное состояние",
                Json = System.Text.Json.JsonSerializer.Serialize(_mapStack[0].Map)
            });
            _historyIndex = 0;
        }

        // Вызывается ПОСЛЕ каждой операции, изменившей карту
        private void PushHistory(string title)
        {
            if (_restoring) return;
            SyncCurrentLevelFromCanvas(); // фиксируем полотно в модели

            string json = System.Text.Json.JsonSerializer.Serialize(_mapStack[0].Map);
            if (_historyIndex >= 0 && _history[_historyIndex].Json == json) return; // ничего не изменилось

            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1); // срезаем redo-ветку
            _history.Add(new HistoryEntry { Title = title, Json = json });
            if (_history.Count > MaxHistoryEntries) _history.RemoveAt(0);
            _historyIndex = _history.Count - 1;
        }

        // ═══════════════════════════════════════════════════════════════
        // Отмена, повтор, восстановление
        // ═══════════════════════════════════════════════════════════════
        private void UndoHistory()
        {
            if (_historyIndex <= 0) { SetStatus("Отменять нечего."); return; }
            string undone = _history[_historyIndex].Title;
            _historyIndex--;
            RestoreHistory(_history[_historyIndex]);
            SetStatus($"Отменено: {undone}");
            PushHistory($"Отменено: {undone}");
        }

        private void RedoHistory()
        {
            if (_historyIndex >= _history.Count - 1) { SetStatus("Повторять нечего."); return; }
            _historyIndex++;
            RestoreHistory(_history[_historyIndex]);
            SetStatus($"Повторено: {_history[_historyIndex].Title}");
            PushHistory($"Повторено: {_history[_historyIndex].Title}");
        }

        private void RestoreHistory(HistoryEntry entry)
        {
            try
            {
                var map = System.Text.Json.JsonSerializer.Deserialize<MapData>(entry.Json);
                if (map == null) return;

                _restoring = true;
                ClearTempLine();
                _connectSource = null;
                DeselectAll();
                HideInfoPanel();

                _mapStack.Clear();
                _mapStack.Add(new MapLevel { Map = map, Title = "Корень" });

                LoadCanvasFromLevel(CurrentLevel); // если такого метода нет — вставьте сюда блок загрузки нод из OpenMapFromPath
                ResetView();
                _restoring = false;
            }
            catch (Exception ex)
            {
                _restoring = false;
                SetStatus("Не удалось восстановить состояние: " + ex.Message);
            }
        }

        private void JumpToHistory(int index)
        {
            if (index < 0 || index >= _history.Count || index == _historyIndex) return;
            _historyIndex = index;
            RestoreHistory(_history[index]);
            SetStatus($"Возврат к: {_history[index].Title}");
            PushHistory($"Возвращено к: {_history[index].Title}");
        }

        // ═══════════════════════════════════════════════════════════════
        // ГРУППОВОЕ ВЫДЕЛЕНИЕ (Ctrl + ЛКМ)
        // ═══════════════════════════════════════════════════════════════
        private static readonly System.Windows.Media.Effects.DropShadowEffect GroupGlow = CreateGroupGlow();
        private static System.Windows.Media.Effects.DropShadowEffect CreateGroupGlow()
        {
            var fx = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(90, 179, 255),
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.9
            };
            fx.Freeze();
            return fx;
        }

        private readonly List<NodeControl> _groupSelection = new();
        private readonly Dictionary<NodeControl, (double X, double Y)> _groupDragPos = new();
        private readonly Dictionary<NodeControl, Cursor> _savedNodeCursor = new();

        private bool _rubberActive;
        private Point _rubberStartCanvas;  // первый угол рамки (координаты карты)
        private Point _rubberLastScreen;   // курсор (координаты canvasBorder)
        private Rectangle? _rubberRect;
        private System.Windows.Threading.DispatcherTimer? _autoPanTimer;
        private NodeControl? _autoPanDragNode;

        private const double EdgeZonePx = 40;  // зона у края, запускающая панораму
        private const double PanSpeedPx = 12;  // скорость панорамы за тик

        // ── Курсор «+» при Ctrl ───────────────────────────────────────
        private void UpdateCtrlCursor()
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || _rubberActive;
            var cur = ctrl ? Cursors.Cross : null;
            canvasBorder.Cursor = cur;
            mainCanvas.Cursor = cur;

            foreach (var n in _nodes)
            {
                if (ctrl)
                {
                    if (!_savedNodeCursor.ContainsKey(n)) _savedNodeCursor[n] = n.Cursor;
                    n.Cursor = Cursors.Cross;
                }
                else if (_savedNodeCursor.TryGetValue(n, out var saved))
                {
                    n.Cursor = saved; // возвращаем исходный курсор ноды
                    _savedNodeCursor.Remove(n);
                }
            }
        }

        // ── Рамка выделения ───────────────────────────────────────────
        private void StartRubberSelection(MouseButtonEventArgs e) // !!!
        {
            // Потерянный MouseUp после панорамирования оставил захват/флаги — сбрасываем
            if (_rubberActive || _isPanning)
            {
                _rubberActive = false;
                _isPanning = false;
                StopAutoPan();

                if (_rubberRect != null)
                {
                    mainCanvas.Children.Remove(_rubberRect);
                    _rubberRect = null;
                }

                mainCanvas.ReleaseMouseCapture();
                canvasBorder.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
            }

            _rubberLastScreen = e.GetPosition(canvasBorder);
            _rubberStartCanvas = ScreenToCanvas(_rubberLastScreen);
            _rubberActive = true;

            // Ctrl+Shift — добавлять к уже выделенному, иначе выделение с нуля
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                ClearGroupSelection();

            _rubberRect = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(90, 179, 255)),
                StrokeThickness = 1.5,
                Fill = new SolidColorBrush(Color.FromArgb(36, 90, 179, 255)),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_rubberRect, 999);
            mainCanvas.Children.Add(_rubberRect);

            mainCanvas.CaptureMouse();
            UpdateRubberRect();
            StartAutoPan();
        }

        private void FinishRubberSelection(MouseButtonEventArgs? e = null)
        {
            _rubberActive = false;
            StopAutoPan();
            mainCanvas.ReleaseMouseCapture();
            if (_rubberRect != null) { mainCanvas.Children.Remove(_rubberRect); _rubberRect = null; }

            var rect = new Rect(_rubberStartCanvas, ScreenToCanvas(e?.GetPosition(canvasBorder) ?? Mouse.GetPosition(canvasBorder)));
            foreach (var ctrl in _nodes)
                if (rect.IntersectsWith(GetNodeRect(ctrl)))   // Contains — только полное попадание
                    AddToGroupSelection(ctrl);

            SnapshotGroupPositions();
            SetStatus($"Выделено нод: {_groupSelection.Count}");
        }

        private void CancelRubberSelection()
        {
            _rubberActive = false;
            StopAutoPan();
            mainCanvas.ReleaseMouseCapture();
            if (_rubberRect != null) { mainCanvas.Children.Remove(_rubberRect); _rubberRect = null; }
            SetStatus("Выделение отменено.");
        }

        private void UpdateRubberRect()
        {
            if (_rubberRect == null) return;
            var r = new Rect(_rubberStartCanvas, ScreenToCanvas(_rubberLastScreen));
            _rubberRect.Width = r.Width;
            _rubberRect.Height = r.Height;
            Canvas.SetLeft(_rubberRect, r.X);
            Canvas.SetTop(_rubberRect, r.Y);
        }

        // ── Автопанорама у края формы ─────────────────────────────────
        private void StartAutoPan()
        {
            // Не создавать несколько таймеров одновременно.
            if (_autoPanTimer != null)
                return;

            _autoPanTimer =
                new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };

            _autoPanTimer.Tick += AutoPanTick;
            _autoPanTimer.Start();
        }

        private void StopAutoPan()
        {
            if (_autoPanTimer == null)
                return;

            _autoPanTimer.Stop();
            _autoPanTimer.Tick -= AutoPanTick;
            _autoPanTimer = null;
        }

        private void AutoPanTick(object? sender, EventArgs e)
        {
            bool draggingNode =
                _autoPanDragNode != null &&
                Mouse.LeftButton == MouseButtonState.Pressed;

            // Таймер нужен либо рамке, либо перетаскиваемой ноде.
            if (!_rubberActive && !draggingNode)
            {
                _autoPanDragNode = null;
                StopAutoPan();
                return;
            }

            // Читаем текущую позицию непосредственно у мыши.
            // Это работает даже тогда, когда MouseMove больше не приходит.
            Point mouse = Mouse.GetPosition(canvasBorder);
            _rubberLastScreen = mouse;

            double dx = 0;
            double dy = 0;

            if (mouse.X <= EdgeZonePx)
                dx = PanSpeedPx;
            else if (mouse.X >= canvasBorder.ActualWidth - EdgeZonePx)
                dx = -PanSpeedPx;

            if (mouse.Y <= EdgeZonePx)
                dy = PanSpeedPx;
            else if (mouse.Y >= canvasBorder.ActualHeight - EdgeZonePx)
                dy = -PanSpeedPx;

            if (dx == 0 && dy == 0)
            {
                // Рамка всё равно должна следовать за курсором.
                if (_rubberActive)
                    UpdateRubberRect();

                return;
            }

            // Перемещаем карту.
            _offsetX += dx;
            _offsetY += dy;

            translateT.X = _offsetX;
            translateT.Y = _offsetY;

            RedrawGrid();

            if (_rubberActive)
            {
                UpdateRubberRect();
            }

            if (draggingNode)
            {
                // Компенсируем смещение карты, чтобы перетаскиваемая
                // нода визуально оставалась под курсором.
                MoveDraggedNodesDuringAutoPan(
                    -dx / _scale,
                    -dy / _scale);
            }
        }


        private void MoveDraggedNodesDuringAutoPan(double canvasDx, double canvasDy)
        {
            if (_autoPanDragNode == null)
                return;

            List<NodeControl> targets;

            if (_groupSelection.Contains(_autoPanDragNode))
            {
                // Если перетаскиваемая нода входит в группу,
                // перемещаем всю группу.
                targets = _groupSelection.ToList();
            }
            else
            {
                targets = new List<NodeControl>
        {
            _autoPanDragNode
        };
            }

            foreach (var node in targets)
            {
                node.Model.X += canvasDx;
                node.Model.Y += canvasDy;

                Canvas.SetLeft(node, node.Model.X);
                Canvas.SetTop(node, node.Model.Y);

                // Обновляем сохранённую позицию группы, чтобы очередное
                // событие NodeMoved не переместило группу повторно.
                if (_groupSelection.Contains(node))
                {
                    _groupDragPos[node] =
                        (node.Model.X, node.Model.Y);
                }

                UpdateNodeArrows(node);
            }
        }

        // ── Управление группой ────────────────────────────────────────
        private void AddToGroupSelection(NodeControl ctrl)
        {
            if (_groupSelection.Contains(ctrl)) return;
            _groupSelection.Add(ctrl);
            ctrl.Effect = GroupGlow;
            _groupDragPos[ctrl] = (ctrl.Model.X, ctrl.Model.Y);
        }

        private void ClearGroupSelection()
        {
            foreach (var n in _groupSelection) n.Effect = null;
            _groupSelection.Clear();
            _groupDragPos.Clear();
        }

        private void SnapshotGroupPositions()
        {
            foreach (var c in _groupSelection)
                _groupDragPos[c] = (c.Model.X, c.Model.Y);
        }

        private Rect GetNodeRect(NodeControl n)
        {
            double w = n.ActualWidth > 0 ? n.ActualWidth : n.Model.Width;
            double h = n.ActualHeight > 0 ? n.ActualHeight : n.Model.Height;
            return new Rect(n.Model.X, n.Model.Y, w, h);
        }

        // ── Удаление группы ───────────────────────────────────────────
        private void DeleteSelectedGroup()
        {
            if (_groupSelection.Count == 0) return;
            var ids = _groupSelection.Select(n => n.Model.Id).ToHashSet();

            var conns = _connections
                .Where(c => ids.Contains(c.FromNodeId) || ids.Contains(c.ToNodeId))
                .ToList();
            foreach (var c in conns) RemoveConnection(c);

            foreach (var ctrl in _groupSelection.ToList())
            {
                mainCanvas.Children.Remove(ctrl);
                _nodes.Remove(ctrl);
                if (_selectedNode == ctrl) _selectedNode = null;
            }

            foreach (var n in _nodes)
                n.Model.ConnectedTo.RemoveAll(ids.Contains);

            int count = ids.Count;
            ClearGroupSelection();
            PushHistory($"Удаление нод ({count})");
            SetStatus($"Удалено нод: {count}");
        }

        // ═══════════════════════════════════════════════════════════════
        // ВИДИМОСТЬ НОДЫ ПРИ ОТКРЫТОЙ ПАНЕЛИ
        // ═══════════════════════════════════════════════════════════════
        private void EnsureNodeVisibleInFreeZone(NodeControl ctrl)
        {
            double W = canvasBorder.ActualWidth;
            double H = canvasBorder.ActualHeight;
            if (W <= 0 || H <= 0) return;

            // ширина, которую панель займёт в открытом состоянии
            // (работает и для 320 px, и для "большого" блокнота — берём факт)
            double zoneLeft = infoPanel.ActualWidth;
            double zoneW = W - zoneLeft;
            if (zoneW < 50) return; // панель заняла почти всё — двигать некуда

            // прямоугольник ноды в экранных координатах
            var r = GetNodeRect(ctrl);
            var screen = new Rect(
                r.X * _scale + _offsetX,
                r.Y * _scale + _offsetY,
                r.Width * _scale,
                r.Height * _scale);

            const double pad = 12; // запас от краёв
            bool fullyVisible =
                screen.Left >= zoneLeft + pad &&
                screen.Right <= W - pad &&
                screen.Top >= pad &&
                screen.Bottom <= H - pad;

            if (fullyVisible) return; // нода и так видна — карту не дёргаем

            // центрируем ноду в свободной (правой) зоне
            double targetX = zoneLeft + zoneW / 2;
            double targetY = H / 2;

            _offsetX = targetX - (r.X + r.Width / 2) * _scale;
            _offsetY = targetY - (r.Y + r.Height / 2) * _scale;

            ApplyTransform(); // обновит translateT, сетку и стрелки
        }

        private void CenterNodeOnScreen(NodeControl ctrl)
        {
            double W = canvasBorder.ActualWidth;
            double H = canvasBorder.ActualHeight;
            if (W <= 0 || H <= 0) return;

            var r = GetNodeRect(ctrl);
            _offsetX = W / 2 - (r.X + r.Width / 2) * _scale;
            _offsetY = H / 2 - (r.Y + r.Height / 2) * _scale;
            ApplyTransform(); // обновит translateT, сетку и стрелки
        }

        private void ApplyThemeToUI()
        {
            RedrawGrid();    // точки сетки читают AppSettings
            RedrawArrows();  // цвет связей
        }

        // ── Сохранение для автосейва: тихо, без прыжка в корень ──
        private void AutosaveSave()
        {
            if (_currentFilePath == null) return; // файл ещё не выбран — пропускаем
            SyncCurrentLevelFromCanvas();
            System.IO.File.WriteAllText(_currentFilePath,
                System.Text.Json.JsonSerializer.Serialize(CurrentLevel.Map,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            SetStatus("Автосохранение выполнено.");
        }

        // ═══════════════════════════════════════════════════════════════
        // Кнопки
        // ═══════════════════════════════════════════════════════════════

        private void BtnNewNode(Point? canvasPos = null)
        {
            if(canvasPos == null)
            {
                canvasPos = new Point(
                    (canvasBorder.ActualWidth / 2 - _offsetX) / _scale,
                    (canvasBorder.ActualHeight / 2 - _offsetY) / _scale);
            }

            var nd = SettingsManager.Settings.NodeDefaults;
            var model = new NodeModel
            {
                X = canvasPos.Value.X,
                Y = canvasPos.Value.Y,
                Name = "Новая нода",
                BackgroundColorHex = nd.Background,
                HeaderColorHex = nd.Header,
                TextColorHex = nd.Text,
                FontFamily = AppSettings.NodeDefaultFontFamily,
                FontSize = AppSettings.NodeDefaultFontSize,
                Width = AppSettings.NodeDefaultWidth,
                Height = AppSettings.NodeDefaultHeight
            };

            ShowInfoPanelForCreate(model);
        }

        private void BtnSave()
        {
            if (_currentFilePath != null)
                SaveToPath(_currentFilePath);   // перезапись
            else
                SaveAs();
        }

        private void ShowNodeTree()
        {
            SyncCurrentLevelFromCanvas();

            var win = new Window
            {
                Title = "Дерево нод",
                Width = 440,
                Height = 540,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            T(win, Window.BackgroundProperty, "Brush.PanelBg");
            var root = new DockPanel { Margin = new Thickness(10) };

            // ── Текущий путь и кнопки навигации ──
            var topSp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var lblPath = new TextBlock
            {
                Text = "Вы здесь: " + string.Join("  ›  ",
                    _mapStack.Select((l, i) => i == 0 ? "🏠 Корень" : l.Title)),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 220, 50)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            T(lblPath, TextBlock.ForegroundProperty, "Brush.Accent");

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var btnUp = new Button
            {
                Content = "⬆ Уровень выше",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(0)
            };
            T(btnUp, Control.BackgroundProperty, "Brush.AccentBtn");
            T(btnUp, Control.ForegroundProperty, "Brush.BtnText");
            btnUp.Click += (_, _) => { ExitMap(); /*win.Close();*/ };

            var btnRoot = new Button
            {
                Content = "🏠 В корень",
                Padding = new Thickness(10, 4, 10, 4),
                BorderThickness = new Thickness(0)
            };
            T(btnRoot, Control.BackgroundProperty, "Brush.AccentBtn");
            T(btnRoot, Control.ForegroundProperty, "Brush.BtnText");
            btnRoot.Click += (_, _) => { GoToRoot(); win.Close(); };

            btnRow.Children.Add(btnUp);
            btnRow.Children.Add(btnRoot);
            topSp.Children.Add(lblPath);
            topSp.Children.Add(btnRow);
            DockPanel.SetDock(topSp, Dock.Top);
            root.Children.Add(topSp);

            var hint = new TextBlock
            {
                Text = "Двойной клик по 🗺 — перейти внутрь этой карты.",
                Foreground = new SolidColorBrush(Color.FromRgb(120, 125, 140)),
                Margin = new Thickness(0, 8, 0, 0)
            };
            T(hint, TextBlock.ForegroundProperty, "Brush.DescFg");
            DockPanel.SetDock(hint, Dock.Bottom);
            root.Children.Add(hint);

            // ── Само дерево ──
            var tv = new TreeView { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };

            void Fill(TreeViewItem parent, MapData map, List<Guid> path)
            {
                TextBlock header, leafText;
                foreach (var n in map.Nodes)
                {
                    if (n.EmbeddedMap != null)
                    {
                        var childPath = new List<Guid>(path) { n.Id };

                        header = new TextBlock { Text = "🗺 " + n.Name, Cursor = Cursors.Hand };
                        T(header, TextBlock.ForegroundProperty, "Brush.Accent");
                        header.MouseLeftButtonDown += (_, e2) =>
                        {
                            if (e2.ClickCount == 2)
                            {
                                e2.Handled = true;
                                NavigateByPath(childPath);
                                win.Close();
                            }
                        };

                        var item = new TreeViewItem { Header = header, Margin = new Thickness(0, 2, 0, 2) };
                        Fill(item, n.EmbeddedMap, childPath);
                        parent.Items.Add(item);
                    }
                    else
                    {
                        leafText = new TextBlock { Text = "• " + n.Name };
                        T(leafText, TextBlock.ForegroundProperty, "Brush.DescFg");
                        parent.Items.Add(new TreeViewItem { Header = leafText });
                    }
                }
            }

            var rootItem = new TreeViewItem
            {
                Header = new TextBlock { Text = "🏠 Корень", Foreground = Brushes.White },
                IsExpanded = true
            };
            Fill(rootItem, _mapStack[0].Map, new List<Guid>());
            tv.Items.Add(rootItem);

            root.Children.Add(tv);
            win.Content = root;
            win.ShowDialog();
        }

        private void SaveAs()
        {
            var dlg = new SaveFileDialog
            {
                Title = "Сохранить карту",
                Filter = "Карта узлов (*.wwmap)|*.wwmap|Все файлы|*.*",
                DefaultExt = ".wwmap",
                AddExtension = true
            };
            if (dlg.ShowDialog() != true) return;
            SaveToPath(dlg.FileName);
        }

        private void SaveToPath(string path)
        {
            GoToRoot();

            var json = System.Text.Json.JsonSerializer.Serialize(CurrentLevel.Map,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(path, json);

            _currentFilePath = path;
            UpdateTitleBar();
            SetStatus($"Сохранено: {System.IO.Path.GetFileName(path)}");
        }

        private void BtnOpen()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Открыть карту",
                Filter = "Карты узлов (*.wwmap;*.gnmap)|*.wwmap;*.gnmap|Все файлы|*.*"
            };
            if (dlg.ShowDialog() == true)
                OpenMapFromPath(dlg.FileName);
        }

        private void BtnHistory()
        {
            var win = new Window
            {
                Title = "История операций",
                Width = 360,
                Height = 440,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            T(win, Window.BackgroundProperty, "Brush.PanelBg");          // вместо #1E2128

            var list = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(8)
            };
            T(list, Control.ForegroundProperty, "Brush.TextBoxFg");      // вместо Brushes.White
            
            for (int i = _history.Count - 1; i >= 0; i--) // свежие сверху
            {
                int idx = i; // важно: захватываем копию, иначе лямбда увидит последнее i
                var item = new ListBoxItem
                {
                    Content = (idx == _historyIndex ? "▸ " : idx > _historyIndex ? "↷ " : "") + _history[idx].Title,
                    Tag = idx
                };
                if (idx > _historyIndex) item.Opacity = 0.45; // будущее — то, что можно повторить
                if (idx == _historyIndex) item.SetResourceReference(ListBoxItem.BackgroundProperty, "Brush.BtnHover"); // вместо #3C5078
                item.MouseDoubleClick += (_, _) => { JumpToHistory(idx); win.Close(); };
                list.Items.Add(item);
            }

            var hint = new TextBlock
            {
                Text = "Двойной клик — перейти к этому состоянию",
                Foreground = Brushes.Gray,
                Margin = new Thickness(10, 6, 0, 6)
            };
            T(hint, TextBlock.ForegroundProperty, "Brush.DescFg");       // вместо Brushes.Gray

            var root = new DockPanel();
            DockPanel.SetDock(hint, Dock.Bottom);
            root.Children.Add(hint);
            root.Children.Add(list);

            win.Content = root;
            win.ShowDialog();
        }

        private void BtnClearAll()
        {
            var r = MessageBox.Show(
                "Очистить всю карту? Несохранённые данные будут потеряны.",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes) ClearAll();
        }

        private void BtnFindNode2()
        {
            (string Bt, Guid Id, string Name, string Text)[] BuffArr = new (string Bt, Guid, string, string)[7];
            var allNodes = CollectAllNodes();
            //var confirmed = false;

            #region MyRegion
            var cpw = new ControlPanelWindow("Поиск по тексту", new[]
            {
                "TextBox|Текст поиска||Search.text||",
                "Button2|Option1|Описание|bt2.v1|label|close;",
                "Button2|Option2|Описание|bt2.v2|label|close;",
                "Button2|Option3|Описание|bt2.v3|label|close;",
                "Button2|Option4|Описание|bt2.v4|label|close;",
                "Button2|Option5|Описание|bt2.v5|label|close;",
                "Button2|Option6|Описание|bt2.v6|label|close;",
                "Button2|Option7|Описание|bt2.v7|label|close;",
            });

            cpw.SetSize("Search.text", 300, 25);
            for (int i = 1; i < 8; i++)
            {
                cpw.SetSize("bt2.v" + i, 520, 50);
                cpw.SetVisibility("bt2.v" + i, Visibility.Collapsed);
            }

            cpw.ValueChanged += result =>
            {
                var parts = result.Split(new[] { ' ' }, 2);
                string id = parts[0];

                // Кнопки закрытия: только фиксируем исход, окно закроется само ("close")
                if (id.Contains("bt2.v"))
                {
                    for (int i = 0; i < BuffArr.Length; i++)
                    {
                        if (BuffArr[i].Bt == id)
                        {
                            OpenNodeById(BuffArr[i].Id);
                            break;
                        }
                    }
                    cpw.Close();
                    return;
                }

                string val = parts.Length > 1 ? parts[1].ToLower() : "";
                if (id == "Search.text" && val != "")
                {
                    BuffArr = new (string, Guid, string, string)[7];
                    int num = 0;

                    foreach (var data in allNodes)
                    {
                        if (data.Text.ToLower().Contains(val))
                        {
                            BuffArr[num] = ("", data.Id, "lvl:" + data.Level + " " + data.Name, GetExscindText(data.Text, val));
                            if (num < BuffArr.Length - 1) num++;
                        }
                        else if (data.Name.ToLower().Contains(val))
                        {
                            BuffArr[num] = ("", data.Id, "lvl:" + data.Level + " " + data.Name, data.Text.Substring(0, Math.Min(20, data.Text.Length)) + "...");
                            if (num < BuffArr.Length - 1) num++;
                        }
                    }

                    for (int i = 0; i < BuffArr.Length; i++)
                    {
                        if (BuffArr[i].Id != Guid.Empty)
                        {
                            cpw.SetText("bt2.v" + (i + 1), BuffArr[i].Name);
                            cpw.SetDesc("bt2.v" + (i + 1), BuffArr[i].Text);
                            cpw.SetVisibility("bt2.v" + (i + 1), Visibility.Visible);
                            BuffArr[i].Bt = "bt2.v" + (i + 1);
                        }
                        else
                        {
                            cpw.SetVisibility("bt2.v" + (i + 1), Visibility.Collapsed);
                        }
                    }
                }
                else
                {
                    for (int i = 1; i < 8; i++)
                    {
                        cpw.SetVisibility("bt2.v" + i, Visibility.Collapsed);
                    }
                }
            };

            cpw.Owner = this;
            cpw.ShowDialog();
            #endregion
        }

        private void BtnFindNode()
        {
            var win = new Window
            {
                Title = "Найти ноду",
                Width = 360,
                Height = 180,
                Background = new SolidColorBrush(Color.FromRgb(32, 35, 43)),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };
            T(win, Window.BackgroundProperty, "Brush.PanelBg");
            
            var sp = new StackPanel { Margin = new Thickness(16) };
            var lbl = new TextBlock
            {
                Text = "Введите имя ноды:",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 6)
            };
            T(lbl, TextBlock.ForegroundProperty, "Brush.LabelFg");       // вместо LightGray

            var tb = new TextBox { Padding = new Thickness(6, 4, 6, 4) };
            T(tb, TextBox.BackgroundProperty, "Brush.TextBoxBg");
            T(tb, TextBox.ForegroundProperty, "Brush.TextBoxFg");
            T(tb, TextBox.BorderBrushProperty, "Brush.AccentBtn");
            T(tb, TextBoxBase.CaretBrushProperty, "Brush.TextBoxFg");

            var btn = new Button
            {
                Content = "Найти",
                Margin = new Thickness(0, 8, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 130, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 6, 12, 6)
            };
            T(btn, Control.BackgroundProperty, "Brush.AccentBtn");
            T(btn, Control.ForegroundProperty, "Brush.BtnText");

            btn.Click += (_, _) =>
            {
                var q = tb.Text.Trim().ToLower();
                var ctrl = _nodes.FirstOrDefault(n => n.Model.Name.ToLower().Contains(q));
                if (ctrl != null) { FocusNode(ctrl); win.Close(); }
                else SetStatus($"Нода «{tb.Text}» не найдена.");
            };
            tb.KeyDown += (_, e2) => { if (e2.Key == Key.Enter) btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            sp.Children.Add(lbl);
            sp.Children.Add(tb);
            sp.Children.Add(btn);
            win.Content = sp;
            win.ShowDialog();
            tb.Focus();
        }

        private void ResetView()
        {
            _scale = 1.0;
            _offsetX = 0;
            _offsetY = 0;
            ApplyTransform();
        }

        private void ShowSettings()
        {
            var s = SettingsManager.Settings;

            string themeRow = s.Theme switch
            {
                "Light" => "Тёмная,*Светлая,Кастомная",
                "Custom" => "Тёмная,Светлая,*Кастомная",
                _ => "*Тёмная,Светлая,Кастомная"
            };
            string langRow = s.Language == "en" ? "Русский,*English" : "*Русский,English";

            // Снимок на входе — из него откатываем при Отмене/закрытии
            var snapshot = SettingsManager.Snapshot();
            bool confirmed = false;

            var win = new ControlPanelWindow("Настройки", new[]
            {
                "TextBlock|Общие||sect.gen|label|",
                $"Combo|Язык|Язык интерфейса (локализация позже)|set.language|label|{langRow};",
                $"Combo|Тема|Применяется сразу. «Кастомная» правится в Settings.json|set.theme|label|{themeRow};",

                "TextBlock|Автосохранение||sect.auto|label|;",
                $"Check|Включить|Сохранять карту автоматически|set.auto.enabled|label|{(s.Autosave.Enabled ? "true" : "false")};",

                $"TextBlock|Интервал: {FormatSecondsOptimized(s.Autosave.IntervalSeconds)}||sect.label.interval|label|;",
                $"Slider|Интервал, сек|Пауза между автосохранениями|set.auto.interval|label|30,1800,{s.Autosave.IntervalSeconds},5;",

                $"TextBlock|После изменений: {s.Autosave.ChangesCount}||sect.label.changes|label|;",
                $"Slider|После изменений|Сохранять после N изменений|set.auto.changes|label|5,200,{s.Autosave.ChangesCount},1;",

                "TextBlock|Цвета нод по умолчанию||sect.node|label|;",
                $"TextBox|Фон (hex)|Фон новой ноды, формат #RRGGBB|set.node.bg|label|{s.NodeDefaults.Background};"+
                $"TextBox|Заголовок (hex)|Цвет шапки новой ноды|set.node.header|label|{s.NodeDefaults.Header};"+
                $"TextBox|Текст (hex)|Цвет текста в ноде|set.node.text|label|{s.NodeDefaults.Text};",

                "Button|Открыть Settings.json|Цвета тем и параметры правятся в файле|set.openfile|tooltip|;",

                "Button|Готово|Применить и записать Settings.json|btn.ok|label|close;" +
                "Button|Отмена|Вернуть сохранённое (перечитывает файл)|btn.cancel|label|close;"
            });

            win.ValueChanged += result =>
            {
                var parts = result.Split(new[] { ' ' }, 2);
                string id = parts[0];

                // Кнопки закрытия: только фиксируем исход, окно закроется само ("close")
                if (id == "btn.ok") { confirmed = true; return; }
                if (id == "btn.cancel") { confirmed = false; return; }

                Settings_ValueChanged(result); // живое применение: тема сразу, значения — в s

                // Живые подписи слайдеров
                string val = parts.Length > 1 ? parts[1] : "";
                if (id == "set.auto.interval" && Num(val, out double sec))
                    win.SetText("sect.label.interval", $"Интервал: {FormatSecondsOptimized((int)Math.Round(sec))}");
                if (id == "set.auto.changes" && Num(val, out double cnt))
                    win.SetText("sect.label.changes", $"После изменений: {(int)Math.Round(cnt)}");
            };

            win.Owner = this;
            win.ShowDialog();

            if (confirmed)
            {
                SettingsManager.Save();           // Готово: актуальное состояние целиком → Settings.json
                SetStatus("Настройки сохранены.");
            }
            else
            {
                SettingsManager.Restore(snapshot); // Отмена / ✕ / Alt+F4 — полный откат, файл не тронут
                SetStatus("Изменения настроек отменены.");
            }

            LoadButtonPanel(this);
        }

        private void Settings_ValueChanged(string result)
        {
            var parts = result.Split(new[] { ' ' }, 2);
            string id = parts[0];
            string value = parts.Length > 1 ? parts[1] : "";
            var s = SettingsManager.Settings;

            switch (id)
            {
                case "set.theme":
                    s.Theme = value switch { "Светлая" => "Light", "Кастомная" => "Custom", _ => "Dark" };
                    SettingsManager.ApplyTheme();  // живой предпросмотр, файл не пишем
                    break;

                case "set.language":
                    s.Language = value == "English" ? "en" : "ru";
                    break;

                case "set.auto.enabled":
                    s.Autosave.Enabled = value == "True";
                    break;

                case "set.auto.interval" when int.TryParse(value, out var sec):
                    s.Autosave.IntervalSeconds = sec;
                    break;

                case "set.auto.changes" when int.TryParse(value, out var cnt):
                    s.Autosave.ChangesCount = cnt;
                    break;

                case "set.node.bg" when NormalizeHex(value) is string bg:
                    s.NodeDefaults.Background = bg; break;

                case "set.node.header" when NormalizeHex(value) is string hd:
                    s.NodeDefaults.Header = hd; break;

                case "set.node.text" when NormalizeHex(value) is string tx:
                    s.NodeDefaults.Text = tx; break;

                case "set.openfile":
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            //FileName = "explorer.exe",
                            //Arguments = $"/select,\"{SettingsManager.FilePath}\"",

                            FileName = SettingsManager.FilePath,
                            UseShellExecute = true
                        });
                    }
                    catch { MessageBox.Show(SettingsManager.FilePath, "Файл настроек"); }
                    break;
            }
        }

        public static string FormatSecondsOptimized(long totalSeconds)
        {
            if (totalSeconds <= 0) return "0 сек.";

            // Прямой расчет без TimeSpan
            long hours = totalSeconds / 3600;
            int remainder = (int)(totalSeconds % 3600);
            int minutes = remainder / 60;
            int seconds = remainder % 60;

            // Выделяем память в стеке (stackalloc) для StringBuilder, чтобы не нагружать GC
            var sb = new System.Text.StringBuilder(32);

            if (hours > 0)
            {
                sb.Append(hours).Append(" ч.");
            }
            if (minutes > 0)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(minutes).Append(" мин.");
            }
            if (seconds > 0)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(seconds).Append(" сек.");
            }

            return sb.ToString();
        }

        private static bool Num(string v, out double d) =>
    double.TryParse(v, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out d) ||
    double.TryParse(v, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.CurrentCulture, out d);

        private static string? NormalizeHex(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            if (v[0] != '#') v = "#" + v;
            try { ColorConverter.ConvertFromString(v); return v; }
            catch { return null; } // некорректный hex — игнорируем, в настройках останется прошлое значение
        }

        private void ZoomAt(double delta)
        {
            _scale = Math.Clamp(_scale + delta, AppSettings.ZoomMin, AppSettings.ZoomMax);
            ApplyTransform();
        }

        private void OpenMapFromPath(string path)
        {
            try
            {
                var json = System.IO.File.ReadAllText(path);
                var map = System.Text.Json.JsonSerializer.Deserialize<MapData>(json);

                if (map == null || map.Nodes.Count == 0)
                {
                    MessageBox.Show("Файл пуст или не является картой узлов.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 1. Сбрасываем стек уровней: загруженная карта становится корнем
                _mapStack.Clear();
                _mapStack.Add(new MapLevel { Map = map, Title = "Корень" });

                // 2. Полностью очищаем полотно
                ClearTempLine();
                _connectSource = null;
                DeselectAll();
                HideInfoPanel();
                ClearMap();

                // 3. Загружаем ноды и связи
                foreach (var model in map.Nodes)
                    AddNodeControl(model);

                _connections.AddRange(map.Connections);

                // 4. Рисуем стрелки после того, как ноды получат реальные размеры
                Dispatcher.InvokeAsync(() =>
                {
                    foreach (var conn in _connections.ToList())
                        DrawArrow(conn);
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                // 5. Обновляем интерфейс
                _currentFilePath = path;
                UpdateTitleBar();
                ResetView();

                SetStatus($"Открыто: {System.IO.Path.GetFileName(path)} ({map.Nodes.Count} нод)");
                PushHistory($"Открыта карта: {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть карту:\n{ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ПАНЕЛЬ КНОПОК: заголовок + кнопки с описаниями
        // Формат строки: "Название|Описание|id|True"
        // ═══════════════════════════════════════════════════════════════

        // Открытая панель кнопок + флаг «закрылась только что»
        private bool _actionPopupJustClosed;

        private void ShowActionPanel(FrameworkElement anchor, string title, params string[] buttons)
        {
            var popup = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade
            };

            // «Тоггл»: попап уже закрылся от этого клика — не открывать заново
            popup.Closed += (_, _) =>
            {
                _actionPopupJustClosed = true;
                Dispatcher.BeginInvoke(new Action(() => _actionPopupJustClosed = false),
                    System.Windows.Threading.DispatcherPriority.Background);
            };

            var root = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                MinWidth = 220,
                MaxWidth = 480
            };
            T(root, Border.BackgroundProperty, "Brush.PanelBg");   // ← вместо #20232B
            T(root, Border.BorderBrushProperty, "Brush.BtnBorder"); // ← вместо #2A2D38

            var stack = new StackPanel();

            // ── Заголовок панели ──
            var ttl = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(2, 0, 0, 10)
            };
            T(ttl, TextBlock.ForegroundProperty, "Brush.Accent");   // ← вместо #64B4FF
            stack.Children.Add(ttl);

            // ── Кнопки: "Имя|Описание|id|True/False" ──
            foreach (var raw in buttons)
            {
                string def = raw.TrimEnd();

                bool asTooltip = false;
                if (def.EndsWith("|True", StringComparison.OrdinalIgnoreCase))
                {
                    asTooltip = true;
                    def = def.Substring(0, def.Length - 5);
                }
                else if (def.EndsWith("|False", StringComparison.OrdinalIgnoreCase))
                {
                    def = def.Substring(0, def.Length - 6);
                }

                var parts = def.Split(new[] { '|' }, 3);
                if (parts.Length < 3) continue;

                string name = parts[0].Trim();
                string desc = parts[1].Trim();
                string id = parts[2].Trim();

                var content = new StackPanel();

                var nameTb = new TextBlock 
                { 
                    Text = name, 
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                };
                T(nameTb, TextBlock.ForegroundProperty, "Brush.BtnText");   // ← вместо Brushes.White
                content.Children.Add(nameTb);

                // Описание под названием: только не в режиме подсказки и не пустое
                if (!asTooltip && desc.Length > 0)
                {
                    var d = new TextBlock
                    {
                        Text = desc,
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    T(d, TextBlock.ForegroundProperty, "Brush.DescFg");     // ← вместо #9AA0B0
                    content.Children.Add(d);
                }

                var btn = new Button
                {
                    Content = content,
                    Tag = id,
                    Margin = new Thickness(0, 0, 0, 8),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Style = BuildActionPanelButtonStyle()
                };

                // Описание всплывает при наведении
                if (asTooltip && desc.Length > 0)
                {
                    var tip = new ToolTip
                    {
                        Content = desc,
                        Padding = new Thickness(8, 5, 8, 5),
                        FontSize = 11,
                        Placement = PlacementMode.Right,
                        HorizontalOffset = 4
                    };
                    T(tip, Control.BackgroundProperty, "Brush.PanelBg");   // ← вместо #2A2D38
                    T(tip, Control.ForegroundProperty, "Brush.TextBoxFg"); // ← вместо Brushes.White
                    T(tip, Control.BorderBrushProperty, "Brush.BtnBorder"); // ← вместо #404452
                    btn.ToolTip = tip;
                }

                btn.Click += (s, e) =>
                {
                    popup.IsOpen = false;
                    ActionPanelButton_Click(s, e);
                };
                stack.Children.Add(btn);
            }

            var host = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = SystemParameters.WorkArea.Height * 0.85
            };

            root.Child = host;
            popup.Child = root;
            popup.IsOpen = true;
        }


        public static Style BuildActionPanelButtonStyle()
        {
            var st = new Style(typeof(Button));
            st.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension("Brush.BtnBg")));          // ← #232630
            st.Setters.Add(new Setter(Control.BorderBrushProperty,
                new DynamicResourceExtension("Brush.BtnBorder")));      // ← #2A2D38
            st.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            st.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            st.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension("Brush.BtnHover")));       // ← #303646
            st.Triggers.Add(hover);

            return st;
        }

        private void ActionPanelButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string id) return;

            switch (id)
            {
                case "BtnNewNode": BtnNewNode(); break; // Создать новую ноду
                case "AddNodeFromMapFile": AddNodeFromMapFile(); break; // Добавить ноду из карты
                case "BtnSave": BtnSave(); break; // Сохранить карту
                case "BtnSaveAs": SaveAs(); break; // Сохранить карту как
                case "BtnOpen": BtnOpen(); break; // Открыть карту
                case "ShowNodeTree": ShowNodeTree(); break; // Вывести панель дерева иерархии

                case "BtnHistory": BtnHistory(); break; // Вывести панель истории
                case "BtnClearAll": BtnClearAll(); break; // Очистить всю карту
                case "BtnFindNode": BtnFindNode2(); break; // Поиск нод
                case "BtnSettings": ShowSettings(); break; // Вывести панель настроек

                case "BtnResetView": ResetView(); break; // Сброс зумм
                case "BtnZoomIn": ZoomAt(AppSettings.ZoomStep); break; // + зумм
                case "BtnZoomOut": ZoomAt(-AppSettings.ZoomStep); break; //  - зумм

                default:
                    SetStatus($"Нажата кнопка панели: {id}");
                    break;
            }
        }

        public static void LoadButtonPanel(MainWindow window)
        {
            foreach (var (key, grp) in SettingsManager.Settings.GetToolbarGroups())
            {
                if (grp.IsEmpty)
                {
                    switch (key)
                    {
                        case "bt1": window.bt1.Visibility = Visibility.Collapsed; break;
                        case "bt2": window.bt2.Visibility = Visibility.Collapsed; break;
                        case "bt3": window.bt3.Visibility = Visibility.Collapsed; break;
                        case "bt4": window.bt4.Visibility = Visibility.Collapsed; break;
                        case "bt5": window.bt5.Visibility = Visibility.Collapsed; break;
                        case "bt6": window.bt6.Visibility = Visibility.Collapsed; break;
                        case "bt7": window.bt7.Visibility = Visibility.Collapsed; break;
                        case "bt8": window.bt8.Visibility = Visibility.Collapsed; break;
                        case "bt9": window.bt9.Visibility = Visibility.Collapsed; break;
                    }
                    continue;
                }

                switch (key)
                {
                    case "bt1": window.bt1.Content = grp.Content; break;
                    case "bt2": window.bt2.Content = grp.Content; break;
                    case "bt3": window.bt3.Content = grp.Content; break;
                    case "bt4": window.bt4.Content = grp.Content; break;
                    case "bt5": window.bt5.Content = grp.Content; break;
                    case "bt6": window.bt6.Content = grp.Content; break;
                    case "bt7": window.bt7.Content = grp.Content; break;
                    case "bt8": window.bt8.Content = grp.Content; break;
                    case "bt9": window.bt9.Content = grp.Content; break;
                }
            }
        }

        private void ButtonPanel(object s, RoutedEventArgs e)
        {
            if (_actionPopupJustClosed) { _actionPopupJustClosed = false; return; } // это был клик по уже открытой панели

            if (s is not Button b) return;
            if (!SettingsManager.Settings.BtSettings.TryGetValue(b.Name, out var grp)) return;

            ShowActionPanel(b, grp.Title,
                grp.Button.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
        }

        // ═══════════════════════════════════════════════════════════════
        // Drag & Drop
        // ═══════════════════════════════════════════════════════════════

        private void CanvasBorder_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void CanvasBorder_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            string path = files[0]; // несколько файлов — берём первый, остальные игнорируем
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

            if (ext != ".wwmap" && ext != ".gnmap")
            {
                SetStatus($"«{System.IO.Path.GetFileName(path)}» — не файл карты (.wwmap/.gnmap).");
                return;
            }

            // точка броска в координатах карты — для варианта «как ноду»
            var dropPos = ScreenToCanvas(e.GetPosition(mainCanvas));
            ShowDropChoice(path, dropPos);
            e.Handled = true;
        }

        private void ShowDropChoice(string path, Point dropPos)
        {
            bool openAsMap = false, asNode = false;

            var win = new Window
            {
                Title = "Файл карты",
                Width = 400,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            T(win, Window.BackgroundProperty, "Brush.PanelBg");

            var sp = new StackPanel { Margin = new Thickness(16) };
            var lbl = new TextBlock
            {
                Text = $"«{System.IO.Path.GetFileName(path)}» — что сделать?",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };
            T(lbl, TextBlock.ForegroundProperty, "Brush.LabelFg");

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            Button MkBtn(string icon, string label, string brushKey, bool accent)
            {
                FrameworkElement content;
                if (icon.Length == 0)
                {
                    content = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
                }
                else
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new TextBlock
                    {
                        Text = icon,
                        FontFamily = new FontFamily("Segoe UI Emoji"), // явный шрифт — без фолбэка
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 7, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                    content = sp;
                }

                var b = new Button
                {
                    Content = content,
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(8, 0, 0, 0),
                    BorderThickness = new Thickness(0)
                };
                T(b, Control.BackgroundProperty, accent ? "Brush.AccentBtn" : "Brush.BtnBg");
                T(b, Control.ForegroundProperty, "Brush.BtnText"); // TextBlock'и унаследуют цвет сами
                return b;
            }

            var btnOpen = MkBtn("📂", "Открыть как карту", "Brush.AccentBtn", accent: true);
            var btnNode = MkBtn("📦", "Добавить как ноду", "Brush.BtnBg", accent: false);
            var btnCancel = MkBtn("", "Отмена", "Brush.BtnBg", accent: false);

            btnOpen.Click += (_, _) => { openAsMap = true; win.Close(); };
            btnNode.Click += (_, _) => { asNode = true; win.Close(); };
            btnCancel.Click += (_, _) => win.Close();     // ✕/Esc → тоже ничего не делаем

            row.Children.Add(btnOpen);
            row.Children.Add(btnNode);
            row.Children.Add(btnCancel);
            sp.Children.Add(lbl);
            sp.Children.Add(row);
            win.Content = sp;
            win.ShowDialog();

            if (openAsMap) OpenMapFromPath(path);
            else if (asNode) AddMapFileAsNode(path, dropPos);
            // иначе — пользователь передумал, файл не трогаем
        }

        //AddNodeFromMapFile()

        private void AddMapFileAsNode(string path, Point? canvasPos = null)
        {
            try
            {
                var json = System.IO.File.ReadAllText(path);
                var map = System.Text.Json.JsonSerializer.Deserialize<MapData>(json);
                if (map == null || map.Nodes.Count == 0)
                {
                    SetStatus("Файл пуст или не является картой узлов.");
                    return;
                }

                // по умолчанию — центр видимой области; при дропе — точка броска,
                // нода центрируется под курсором
                Point pos = canvasPos ?? new Point(
                    (canvasBorder.ActualWidth / 2 - _offsetX) / _scale,
                    (canvasBorder.ActualHeight / 2 - _offsetY) / _scale);

                var nd = SettingsManager.Settings.NodeDefaults;
                var model = new NodeModel
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    X = pos.X - AppSettings.NodeDefaultWidth / 2,
                    Y = pos.Y - AppSettings.NodeDefaultHeight / 2,
                    Width = AppSettings.NodeDefaultWidth,
                    Height = AppSettings.NodeDefaultHeight,
                    FontFamily = AppSettings.NodeDefaultFontFamily,
                    FontSize = AppSettings.NodeDefaultFontSize,
                    BackgroundColorHex = nd.Background,
                    HeaderColorHex = "#8A5AC8",
                    TextColorHex = nd.Text,
                    EmbeddedMap = map
                };

                AddNodeControl(model);
                SyncCurrentLevelFromCanvas();

                SetStatus($"Добавлена нода-карта «{model.Name}» ({map.Nodes.Count} нод внутри).");
                PushHistory($"Добавлена нода-карта «{model.Name}»");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось загрузить карту:\n{ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}