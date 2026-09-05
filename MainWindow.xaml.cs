using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;
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

        public MainWindow()
        {
            // ВАЖНО: до InitializeComponent
            _gridHost = new VisualHost(_gridVisual);

            InitializeComponent();

            Loaded += MainWindow_Loaded;
            SizeChanged += (_, _) => RedrawGrid();
            KeyDown += MainWindow_KeyDown;

            infoPanel.SaveRequested += InfoPanel_SaveRequested;
            infoPanel.CancelRequested += HideInfoPanel;
            infoPanel.LinkNodeRequested += id =>
            {
                var ctrl = _nodes.FirstOrDefault(n => n.Model.Id == id);
                if (ctrl != null) FocusNode(ctrl);
            };

            // Карты-узлы: начальный уровень + верхняя панель навигации
            _mapStack.Add(new MapLevel { Map = new MapData(), Title = "Корень" });

            //ButtonAnimator.Attach(BtnNewNode, "➕", "➕ Новая нода");
            //ButtonAnimator.Attach(BtnTree, "🌳", "🌳 Дерево");
            //ButtonAnimator.Attach(BtnSave, "💾", "💾 Сохранить");
            //ButtonAnimator.Attach(BtnOpen, "📂", "📂 Открыть");
            //ButtonAnimator.Attach(BtnClearAll, "🗑", "🗑 Очистить всё");
            //ButtonAnimator.Attach(BtnFindNode, "🔍", "🔍 Найти нodу");
            //ButtonAnimator.Attach(BtnZoomIn, "🔍", "Приблизить");
            //ButtonAnimator.Attach(BtnZoomOut, "🔍", "Отдалить");
            //ButtonAnimator.Attach(BtnResetView, "⊡", "⊡ Сброс вида");
            //ButtonAnimator.Attach(BtnHistory, "⏳", "⏳ История");

            InitHistory();
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
                BtnNewNode_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }

            // Горячие клавиши с Ctrl
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.S: // Ctrl + S -> Сохранить
                        BtnSave_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;

                    case Key.O: // Ctrl + O -> Открыть
                        BtnOpen_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;

                    case Key.F: // Ctrl + F -> Найти ноду
                        BtnFindNode_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;
                    case Key.Delete: // Ctrl + Delete -> Очистить всё
                        BtnClearAll_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;

                    case Key.OemPlus: // Ctrl + "+" -> Приблизить
                        BtnZoomIn_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;

                    case Key.OemMinus: // Ctrl + "-" -> Отдалить
                        BtnZoomOut_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;

                    case Key.Home: // Ctrl + Home -> Сбросить вид
                        BtnResetView_Click(this, new RoutedEventArgs());
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
            if (_isPanning)
            {
                _isPanning = false;
                mainCanvas.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
            }

            PushHistory("Перемещение ноды");
        }

        private void MainCanvas_MouseMove(object s, MouseEventArgs e)
        {
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

            //var screenPos = e.GetPosition(canvasBorder);
            //var canvasPos = ScreenToCanvas(screenPos);

            //var cm = new ContextMenu();
            //var mi = new MenuItem { Header = "➕ Создать ноду" };
            //mi.Click += (_, _) =>
            //{
            //var model = new NodeModel
            //{
            //    X = canvasPos.X,
            //    Y = canvasPos.Y,
            //    Name = "Новая нода",
            //    BackgroundColorHex = $"#{AppSettings.NodeDefaultBackground.R:X2}{AppSettings.NodeDefaultBackground.G:X2}{AppSettings.NodeDefaultBackground.B:X2}",
            //    HeaderColorHex = $"#{AppSettings.NodeHeaderBackground.R:X2}{AppSettings.NodeHeaderBackground.G:X2}{AppSettings.NodeHeaderBackground.B:X2}",
            //    TextColorHex = $"#{AppSettings.NodeDefaultText.R:X2}{AppSettings.NodeDefaultText.G:X2}{AppSettings.NodeDefaultText.B:X2}",
            //    FontFamily = AppSettings.NodeDefaultFontFamily,
            //    FontSize = AppSettings.NodeDefaultFontSize,

            //    Width = AppSettings.NodeDefaultWidth,
            //    Height = AppSettings.NodeDefaultHeight,
            //};
            //ShowInfoPanelForCreate(model);
            //};
            //cm.Items.Add(mi);
            //cm.IsOpen = true;
            //e.Handled = true;
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
        private void CreateNode(Point canvasPos)
        {
            // Учитываем текущий масштаб и смещение
            var model = new NodeModel
            {
                X = (canvasPos.X - _offsetX / _scale),
                Y = (canvasPos.Y - _offsetY / _scale),
            };
            ShowInfoPanelForCreate(model);
        }

        private void BtnNewNode_Click(object s, RoutedEventArgs e)
        {
            // Создать в центре видимой области
            double cx = (canvasBorder.ActualWidth / 2 - _offsetX) / _scale;
            double cy = (canvasBorder.ActualHeight / 2 - _offsetY) / _scale;
            CreateNode(new Point(cx, cy));
        }

        private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            MessageBox.Show("Я еще не сделал!");
        }

        private void AddNodeControl(NodeModel model)
        {
            var ctrl = new NodeControl(model);

            ctrl.NodeMoved += NodeCtrl_NodeMoved;
            ctrl.Resized += (c, _) => NodeCtrl_NodeMoved(c);
            ctrl.RequestConnectFrom += NodeCtrl_RequestConnectFrom;
            ctrl.RequestConnectFromLeft += NodeCtrl_RequestConnectFromLeft;
            //ctrl.DoubleClicked += c => ShowInfoPanelForView(c.Model);
            ctrl.RightClicked += (c, _) => ShowNodeContextMenu(c);
            ctrl.DoubleClicked += c =>
            {
                if (c.Model.EmbeddedMap != null)
                    EnterMap(c);                      // карта-узел → открываем вложенную карту
                else
                    ShowInfoPanelForView(c.Model);    // обычная нода → блокнот
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
        private void StartConnection(NodeControl source)
        {
            _connectSource = source;
            var sp = source.GetRightPortCenter();
            _tempLine = new Line
            {
                X1 = sp.X,
                Y1 = sp.Y,
                X2 = sp.X,
                Y2 = sp.Y,
                Stroke = new SolidColorBrush(AppSettings.ConnectionColor),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            mainCanvas.Children.Add(_tempLine);
            SetStatus("Кликните по другой ноде для соединения...");
        }

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
        }

        private void HideInfoPanel()
        {
            AnimateInfoPanel(show: false, large: _infoPanelIsLarge);
            _infoPanelVisible = false;
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

        private void ResetView()
        {
            _scale = 1.0;
            _offsetX = 0;
            _offsetY = 0;
            ApplyTransform();
        }

        // ═══════════════════════════════════════════════════════════════
        // БУФЕР ОБМЕНА
        // ═══════════════════════════════════════════════════════════════
        private NodeModel? _clipboard;

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
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            {
                RedoHistory(); // бонус: привычный Ctrl+Y
                e.Handled = true;
            }
        }

        private void ZoomAt(double delta)
        {
            _scale = Math.Clamp(_scale + delta, AppSettings.ZoomMin, AppSettings.ZoomMax);
            ApplyTransform();
        }

        // ═══════════════════════════════════════════════════════════════
        // СОХРАНЕНИЕ / ЗАГРУЗКА
        // ═══════════════════════════════════════════════════════════════
        private void BtnSave_Click(object s, RoutedEventArgs e)
        {
            if (_currentFilePath != null)
                SaveToPath(_currentFilePath);   // перезапись
            else
                SaveAs();
        }

        private void BtnSaveAs_Click(object s, RoutedEventArgs e) => SaveAs();

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

        private void BtnOpen_Click(object s, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Открыть карту",
                Filter = "Карты узлов (*.wwmap;*.gnmap)|*.wwmap;*.gnmap|Все файлы|*.*"
            };
            if (dlg.ShowDialog() == true)
                OpenMapFromPath(dlg.FileName);
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

            var wrapper = new NodeModel
            {
                Name = _mapStack.Count > 1 ? CurrentLevel.Title : "Сжатая карта",
                X = cx,
                Y = cy,
                Width = AppSettings.NodeDefaultWidth,
                Height = AppSettings.NodeDefaultHeight,
                FontFamily = AppSettings.NodeDefaultFontFamily,
                FontSize = AppSettings.NodeDefaultFontSize,
                BackgroundColorHex = "#282C34",
                HeaderColorHex = "#8A5AC8",
                TextColorHex = "#DCDCDC",
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

            try
            {
                var json = System.IO.File.ReadAllText(dlg.FileName);
                var map = System.Text.Json.JsonSerializer.Deserialize<MapData>(json);
                if (map == null || map.Nodes.Count == 0)
                {
                    SetStatus("Файл пуст или не является картой узлов.");
                    return;
                }

                double cx = (canvasBorder.ActualWidth / 2 - _offsetX) / _scale - AppSettings.NodeDefaultWidth / 2;
                double cy = (canvasBorder.ActualHeight / 2 - _offsetY) / _scale - AppSettings.NodeDefaultHeight / 2;

                var model = new NodeModel
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
                    X = cx,
                    Y = cy,
                    Width = AppSettings.NodeDefaultWidth,
                    Height = AppSettings.NodeDefaultHeight,
                    FontFamily = AppSettings.NodeDefaultFontFamily,
                    FontSize = AppSettings.NodeDefaultFontSize,
                    BackgroundColorHex = "#282C34",
                    HeaderColorHex = "#8A5AC8",
                    TextColorHex = "#DCDCDC",
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

        // ── Заголовок окна ────────────────────────────────────────────
        private void UpdateTitleBar()
        {
            Title = _currentFilePath != null
                ? $"Interactive Whiteboard — {System.IO.Path.GetFileName(_currentFilePath)}"
                : "Interactive Whiteboard";
        }

        private void BtnClearAll_Click(object s, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "Очистить всю карту? Несохранённые данные будут потеряны.",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes) ClearAll();
        }

        // ── ПОИСК НОДЫ ────────────----────────────────────────────────
        private void BtnFindNode_Click(object s, RoutedEventArgs e)
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
            var sp = new StackPanel { Margin = new Thickness(16) };
            var lbl = new TextBlock
            {
                Text = "Введите имя ноды:",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var tb = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 30, 36)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 130, 200)),
                Padding = new Thickness(6, 4, 6, 4)
            };
            var btn = new Button
            {
                Content = "Найти",
                Margin = new Thickness(0, 8, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 130, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 6, 12, 6)
            };
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

        private void ClearMap()
        {
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
        private void BtnTree_Click(object s, RoutedEventArgs e) => ShowNodeTree();

        private void ShowNodeTree()
        {
            SyncCurrentLevelFromCanvas();

            var win = new Window
            {
                Title = "Дерево нод",
                Width = 440,
                Height = 540,
                Background = new SolidColorBrush(Color.FromRgb(32, 35, 43)),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

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

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var btnUp = new Button
            {
                Content = "⬆ Уровень выше",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 130, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnUp.Click += (_, _) => { ExitMap(); /*win.Close();*/ };

            var btnRoot = new Button
            {
                Content = "🏠 В корень",
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromRgb(60, 130, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
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
            DockPanel.SetDock(hint, Dock.Bottom);
            root.Children.Add(hint);

            // ── Само дерево ──
            var tv = new TreeView { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };

            void Fill(TreeViewItem parent, MapData map, List<Guid> path)
            {
                foreach (var n in map.Nodes)
                {
                    if (n.EmbeddedMap != null)
                    {
                        var childPath = new List<Guid>(path) { n.Id };

                        var header = new TextBlock
                        {
                            Text = "🗺 " + n.Name,
                            Foreground = new SolidColorBrush(Color.FromRgb(90, 179, 255)),
                            Cursor = Cursors.Hand
                        };
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
                        parent.Items.Add(new TreeViewItem
                        {
                            Header = new TextBlock
                            {
                                Text = "• " + n.Name,
                                Foreground = new SolidColorBrush(Color.FromRgb(150, 155, 170))
                            }
                        });
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
        // ЗУМИРОВАНИЕ (кнопки)
        // ═══════════════════════════════════════════════════════════════
        private void BtnZoomIn_Click(object s, RoutedEventArgs e) => ZoomAt(AppSettings.ZoomStep);
        private void BtnZoomOut_Click(object s, RoutedEventArgs e) => ZoomAt(-AppSettings.ZoomStep);
        private void BtnResetView_Click(object s, RoutedEventArgs e) => ResetView();

        // ═══════════════════════════════════════════════════════════════
        // СТАТУС-БАР
        // ═══════════════════════════════════════════════════════════════
        private void SetStatus(string msg) => tbStatus.Text = msg;

        private void NodeCtrl_NodeMoved(NodeControl ctrl)
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

                Point from = conn.FromPort == "left"
                    ? fromCtrl.GetLeftPortCenter()
                    : fromCtrl.GetRightPortCenter();

                Point to = conn.ToPort == "left"
                    ? toCtrl.GetLeftPortCenter()
                    : toCtrl.GetRightPortCenter();

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

        private void CanvasBorder_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
        {
            // Не отменяем соединение по ЛКМ — только панорамирование
            // Соединение отменяется только через ПКМ или Escape

            _isPanning = true;
            _panStart = e.GetPosition(canvasBorder);
            canvasBorder.CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
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

        private void CanvasBorder_MouseLeftButtonUp(object s, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                canvasBorder.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                RedrawArrows();
                e.Handled = true;
            }

            PushHistory("Перемещение ноды");
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
            mi.Click += (_, _) =>
            {
                var model = new NodeModel
                {
                    X = canvasPos.X,
                    Y = canvasPos.Y,
                    Name = "Новая нода",
                    BackgroundColorHex = $"#{AppSettings.NodeDefaultBackground.R:X2}{AppSettings.NodeDefaultBackground.G:X2}{AppSettings.NodeDefaultBackground.B:X2}",
                    HeaderColorHex = $"#{AppSettings.NodeHeaderBackground.R:X2}{AppSettings.NodeHeaderBackground.G:X2}{AppSettings.NodeHeaderBackground.B:X2}",
                    TextColorHex = $"#{AppSettings.NodeDefaultText.R:X2}{AppSettings.NodeDefaultText.G:X2}{AppSettings.NodeDefaultText.B:X2}",
                    FontFamily = AppSettings.NodeDefaultFontFamily,
                    FontSize = AppSettings.NodeDefaultFontSize,
                    Width = AppSettings.NodeDefaultWidth,
                    Height = AppSettings.NodeDefaultHeight
                };
                ShowInfoPanelForCreate(model);
            };
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

        private void BtnHistory_Click(object sender, RoutedEventArgs e)
        {
            var win = new Window
            {
                Title = "История операций",
                Width = 360,
                Height = 440,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(30, 33, 40))
            };

            var list = new ListBox
            {
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(8)
            };

            for (int i = _history.Count - 1; i >= 0; i--) // свежие сверху
            {
                int idx = i; // важно: захватываем копию, иначе лямбда увидит последнее i
                var item = new ListBoxItem
                {
                    Content = (idx == _historyIndex ? "▸ " : idx > _historyIndex ? "↷ " : "") + _history[idx].Title,
                    Tag = idx
                };
                if (idx > _historyIndex) item.Opacity = 0.45; // будущее — то, что можно повторить
                if (idx == _historyIndex) item.Background = new SolidColorBrush(Color.FromRgb(60, 80, 120));
                item.MouseDoubleClick += (_, _) => { JumpToHistory(idx); win.Close(); };
                list.Items.Add(item);
            }

            var hint = new TextBlock
            {
                Text = "Двойной клик — перейти к этому состоянию",
                Foreground = Brushes.Gray,
                Margin = new Thickness(10, 6, 0, 6)
            };

            var root = new DockPanel();
            DockPanel.SetDock(hint, Dock.Bottom);
            root.Children.Add(hint);
            root.Children.Add(list);

            win.Content = root;
            win.ShowDialog();
        }

        private void JumpToHistory(int index)
        {
            if (index < 0 || index >= _history.Count || index == _historyIndex) return;
            _historyIndex = index;
            RestoreHistory(_history[index]);
            SetStatus($"Возврат к: {_history[index].Title}");
            PushHistory($"Возвращено к: {_history[index].Title}");
        }
    }

    public static class ButtonAnimator
    {
        private const int DelayMs = 30; // скорость появления/исчезания букв

        public static void Attach(Button btn, string emoji, string fullText)
        {
            btn.Content = emoji;
            int runId = 0;

            async void TypeIn(int id)
            {
                for (int i = 1; i <= fullText.Length; i++)
                {
                    if (id != runId) return;
                    btn.Content = fullText.Substring(0, i);
                    await Task.Delay(DelayMs);
                }
            }

            async void TypeOut(int id)
            {
                for (int i = (btn.Content as string ?? "").Length - 1; i >= 0; i--)
                {
                    if (id != runId) return;
                    btn.Content = fullText.Substring(0, i); // буквы исчезают с конца
                    await Task.Delay(DelayMs);
                }
                if (id == runId) btn.Content = emoji; // текст стёрся — возвращаем эмодзи
            }

            btn.MouseEnter += (_, _) => TypeIn(++runId);
            btn.MouseLeave += (_, _) =>
            {
                if (btn.Content as string == emoji) return; // анимировать нечего
                TypeOut(++runId);
            };
        }
    }
}