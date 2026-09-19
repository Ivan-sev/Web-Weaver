using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;

namespace WebWeaver
{
    public partial class ControlPanelWindow : Window
    {
        /// <summary>Итоговые значения всех элементов: id → последнее значение.</summary>
        public Dictionary<string, string> Results { get; } = new();

        // Реестр созданных элементов: id → контрол (для живых правок извне)
        private readonly Dictionary<string, FrameworkElement> _elements = new();

        // Куда бьёт SetText: id → действие «поменять текст этого элемента»
        private readonly Dictionary<string, Action<string>> _textSetters = new();

        // Гасит «эхо» ValueChanged при программной установке текста в TextBox
        private bool _suppress;

        /// <summary>Единое событие изменений: аргумент = "id значение".</summary>
        public event Action<string>? ValueChanged;

        #region ПРАВКА ЭЛЕМЕНТОВ ПОСЛЕ ПОСТРОЕНИЯ
        /// <summary>
        /// Живое обновление текста у любого элемента: TextBlock, TextBox, Button, Button2
        /// (жирная строка), CheckBox, Toggle, Radio. Для Combo — замена списка опций ("A,B,*C").
        /// </summary>
        public void SetText(string id, string text)
        {
            if (_textSetters.TryGetValue(id, out var set)) set(text);
        }

        /// <summary>Доступ к любому элементу по id — на будущее.</summary>
        public T? GetElement<T>(string id) where T : FrameworkElement =>
            _elements.TryGetValue(id, out var el) ? el as T : null;

        /// <summary>Прямой доступ: win["btn.ok"].Visibility = Visibility.Collapsed;</summary>
        public FrameworkElement? this[string id] =>
            _elements.TryGetValue(id, out var el) ? el : null;

        // ── Видимость и доступность ──
        /// <summary>
        /// Установить видимость элемента (Visibility). Если on=false — элемент исчезает и не занимает место.
        /// </summary>
        /// <param name="id">id элемента</param>
        /// <param name="on">видимость</param>
        public void SetVisible(string id, bool on) =>
            SetVisibility(id, on ? Visibility.Visible : Visibility.Collapsed);

        /// <summary>
        /// Установить видимость элемента (Visibility). Если Collapsed — элемент исчезает и не занимает место.
        /// </summary>
        /// <param name="id">id элемента</param>
        /// <param name="v">видимость</param>
        public void SetVisibility(string id, Visibility v)
        {
            if (_elements.TryGetValue(id, out var el)) el.Visibility = v;
        }

        /// <summary>
        /// Установить доступность элемента (IsEnabled). Если false — элемент серый и не реагирует на клики.
        /// </summary>
        /// <param name="id">id элемента</param>
        /// <param name="on">доступность</param>
        public void SetEnabled(string id, bool on)
        {
            if (_elements.TryGetValue(id, out var el)) el.IsEnabled = on;
        }

        // ── Цвет: "#80FF80" или ключ темы "Brush.Accent" ──
        /// <summary>
        /// Установить локальные цвета элемента: Foreground, Background, BorderBrush. Если null — не трогать.
        /// </summary>
        /// <param name="id">id элемента</param>
        /// <param name="foreground">строка с описанием кисти для текста</param>
        /// <param name="background">строка с описанием кисти для фона</param>
        /// <param name="border">строка с описанием кисти для границы</param>
        public void SetColor(string id, string? foreground = null,
                             string? background = null, string? border = null)
        {
            if (!_elements.TryGetValue(id, out var el)) return;

            if (foreground != null && BrushFrom(foreground) is Brush fg)
                el.SetValue(TextElement.ForegroundProperty, fg); // TextBlock и Control — одно свойство

            if (el is Control c)
            {
                if (background != null && BrushFrom(background) is Brush bg) c.Background = bg;
                if (border != null && BrushFrom(border) is Brush br) c.BorderBrush = br;
            }
        }

        /// <summary>Снять локальные цвета — снова работает DynamicResource (живая смена темы).</summary>
        /// <param name="id">id элемента</param>
        public void ResetLook(string id)
        {
            if (!_elements.TryGetValue(id, out var el)) return;
            el.ClearValue(TextElement.ForegroundProperty);
            if (el is Control c)
            {
                c.ClearValue(Control.BackgroundProperty);
                c.ClearValue(Control.BorderBrushProperty);
            }
        }

        /// <summary>
        /// Попытка получить кисть из строки: "#RRGGBB" или ключ темы "Brush.Имя".
        /// </summary>
        /// <param name="spec">строка с описанием кисти</param>
        /// <returns></returns>
        private static Brush? BrushFrom(string spec)
        {
            if (spec.StartsWith("Brush.", StringComparison.OrdinalIgnoreCase))
                return Application.Current.Resources[spec] as Brush;
            try { return (Brush)new BrushConverter().ConvertFromString(spec); }
            catch { return null; }
        }

        // ── Геометрия: в вертикальном стеке «координаты» = отступы, выравнивание, размер ──
        /// <summary>
        /// Установить отступы элемента в родительском стеке. Если null — не трогать.
        /// </summary>
        /// <param name="id">id элемента</param>
        /// <param name="left">отступ слева</param>
        /// <param name="top">отступ сверху</param>
        /// <param name="right">отступ справа</param>
        /// <param name="bottom">отступ снизу</param>
        public void SetMargin(string id, double left, double top, double right, double bottom) =>
            SetMargin(id, new Thickness(left, top, right, bottom));

        /// <summary>
        /// Установить отступы элемента в родительском стеке. Если null — не трогать.
        /// </summary>
        /// <param name="id">id элемента</param>
        /// <param name="m">отступы</param>
        public void SetMargin(string id, Thickness m)
        {
            if (_elements.TryGetValue(id, out var el)) el.Margin = m;
        }

        /// <summary>Выравнивание всего ряда (строки панели) по горизонтали.</summary>
        public void SetRowAlignment(int rowIndex, HorizontalAlignment h)
        {
            if (rowIndex >= 0 && rowIndex < spRows.Children.Count &&
                spRows.Children[rowIndex] is FrameworkElement row)
                row.HorizontalAlignment = h;
        }

        /// <summary>
        /// Установить выравнивание элемента в родительском стеке. Если null — не трогать.
        /// </summary>
        /// <param name="id">id элемента</param>
        /// <param name="horizontal">горизонтальное выравнивание</param>
        /// <param name="vertical">вертикальное выравнивание</param>
        public void SetAlignment(string id,
            HorizontalAlignment? horizontal = null, VerticalAlignment? vertical = null)
        {
            if (!_elements.TryGetValue(id, out var el)) return;
            if (horizontal.HasValue) el.HorizontalAlignment = horizontal.Value;
            if (vertical.HasValue) el.VerticalAlignment = vertical.Value;
        }

        /// <summary>
        /// Установить размер элемента (Width, Height). Если null — не трогать.
        /// </summary>
        /// <param name="id">id элемента</param>
        /// <param name="width">ширина</param>
        /// <param name="height">высота</param>
        public void SetSize(string id, double? width = null, double? height = null)
        {
            if (!_elements.TryGetValue(id, out var el)) return;
            if (width.HasValue) el.Width = width.Value;
            if (height.HasValue) el.Height = height.Value;
        }

        // ── Позиция = порядок в стеке ──
        /// <summary>
        /// Переместить элемент в родительском стеке на индекс index (0..Count-1).
        /// </summary>
        /// <param name="id">id элемента</param>
        /// <param name="index">индекс</param>
        public void MoveTo(string id, int index)
        {
            if (_elements.TryGetValue(id, out var el) && el.Parent is Panel p)
            {
                index = Math.Clamp(index, 0, p.Children.Count - 1);
                if (p.Children.IndexOf(el) == index) return;
                p.Children.Remove(el);
                p.Children.Insert(index, el);
            }
        }

        /// <summary>
        /// Переместить элемент вверх
        /// </summary>
        /// <param name="id">id элемента</param>
        public void MoveUp(string id)
        {
            if (_elements.TryGetValue(id, out var el) && el.Parent is Panel p)
                MoveTo(id, p.Children.IndexOf(el) - 1);
        }

        /// <summary>
        /// Переместить элемент вниз
        /// </summary>
        /// <param name="id">id элемента</param>
        public void MoveDown(string id)
        {
            if (_elements.TryGetValue(id, out var el) && el.Parent is Panel p)
                MoveTo(id, p.Children.IndexOf(el) + 1);
        }

        // ── Текст описания (подпись под элементом) ──
        private readonly Dictionary<string, TextBlock> _descs = new();

        /// <summary>Установить описание: подпись под элементом, внутри button2 или tooltip-подсказку.</summary>
        public void SetDesc(string id, string text)
        {
            if (_descs.TryGetValue(id, out var tb)) { tb.Text = text; return; }

            // описание типа tooltip тоже живое
            if (_elements.TryGetValue(id, out var el) &&
                el.ToolTip is ToolTip tip && tip.Content is string)
                tip.Content = text;
        }
        #endregion

        public ControlPanelWindow(string title, params string[] rows)
        {
            InitializeComponent();
            Title = title;
            tbTitle.Text = title;

            MinWidth = 380;
            MaxWidth = 560;
            SizeToContent = SizeToContent.Height;                    // компактно при малом числе элементов
            MaxHeight = SystemParameters.WorkArea.Height * 0.9;      // упёрлось в потолок → включился скролл

            BuildRows(rows);
        }

        // ── Быстрый вызов: Owner и ShowDialog внутри ──
        public static void Show(string title, string[] rows,
                                Window? owner = null,
                                Action<string>? onValueChanged = null)
        {
            var win = new ControlPanelWindow(title, rows);
            if (onValueChanged != null) win.ValueChanged += onValueChanged;
            if (owner != null) win.Owner = owner;
            win.ShowDialog();
        }

        // ═══════════════════ ПОСТРОЕНИЕ ═══════════════════
        private void BuildRows(string[] rows)
        {
            spRows.Children.Clear();
            _elements.Clear();
            _descs.Clear();
            _textSetters.Clear();

            int rowIdx = 0;
            foreach (var row in rows)
            {
                rowIdx++;
                var defs = row.Split(';');

                // ── Одиночный элемент — сразу в вертикальный стек ──
                // Stretch = вся ширина, Center = середина строки — оба работают
                if (defs.Length == 1)
                {
                    var single = BuildElement(defs[0].Trim(), "row" + rowIdx); // ваша сигнатура
                    if (single == null) continue;
                    single.Margin = new Thickness(0, 0, 0, 10); // если BuildElement уже ставит Margin — уберите эту строку
                    spRows.Children.Add(single);
                    continue;
                }

                // ── Несколько элементов (;) — горизонтальный ряд, как было ──
                var line = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
                foreach (var def in defs)
                {
                    var el = BuildElement(def.Trim(), "row" + rowIdx);
                    if (el != null) line.Children.Add(el);
                }
                if (line.Children.Count > 0) spRows.Children.Add(line);
            }
        }


        // ── Один элемент по описанию "Тип|Текст|Описание|id|ТипОписания|ДопДанные" ──
        private FrameworkElement? BuildElement(string def, string fallbackGroup)
        {
            var f = def.Split('|');
            if (f.Length < 4) return null;

            string type = f[0].Trim().ToLowerInvariant();
            string text = f[1].Trim();
            string desc = f[2].Trim();
            string id = f[3].Trim();
            string descType = f.Length > 4 ? f[4].Trim().ToLowerInvariant() : "label";
            string extra = f.Length > 5 ? f[5].Trim() : "";

            if (id.Length == 0) return null;

            void Report(string value)
            {
                Results[id] = value;
                ValueChanged?.Invoke($"{id} {value}");
            }

            FrameworkElement ctrl;
            switch (type)
            {
                case "button":
                    {
                        var b = new Button { Content = text, MinWidth = 90 };
                        bool close = extra.Equals("close", StringComparison.OrdinalIgnoreCase);
                        b.Click += (_, _) => { Report("Click"); if (close) Close(); };
                        ctrl = b;
                        break;
                    }

                case "button2":
                    {
                        var stack = new StackPanel();
                        var content = new StackPanel();

                        var nameTb = new TextBlock
                        {
                            Text = text,
                            FontWeight = FontWeights.Bold,
                            TextWrapping = TextWrapping.Wrap
                        };
                        nameTb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.BtnText");
                        content.Children.Add(nameTb);

                        var d = new TextBlock
                        {
                            Text = desc,
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 2, 0, 0)
                        };
                        d.SetResourceReference(TextBlock.ForegroundProperty, "Brush.DescFg");
                        content.Children.Add(d);

                        var btn = new Button
                        {
                            Content = content,
                            Tag = id,
                            Margin = new Thickness(0, 0, 0, 8),
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            Style = MainWindow.BuildActionPanelButtonStyle()
                        };
                        btn.Click += (_, _) => Report("Click");

                        stack.Children.Add(btn);

                        _textSetters[id] = v => nameTb.Text = v;
                        _descs[id] = d;

                        ctrl = stack;
                        break;
                    }

                case "textblock":
                case "text":
                    {
                        var lbl = new TextBlock
                        {
                            Text = text,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 8, 0)
                        };
                        lbl.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextBoxFg");
                        ctrl = lbl;
                        break;
                    }

                case "textbox":
                    {
                        var tb = new TextBox { Text = extra, Width = 155 };
                        tb.TextChanged += (_, _) => { if (!_suppress) Report(tb.Text); };
                        ctrl = tb;
                        break;
                    }

                case "combo":
                    {
                        var cb = new ComboBox { Width = 180, VerticalAlignment = VerticalAlignment.Center };
                        FillCombo(cb, extra); // выбор ставится ДО подписки — без стартового отчёта
                        cb.SelectionChanged += (_, _) =>
                            Report((cb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "");
                        ctrl = cb;
                        break;
                    }

                case "check":
                    {
                        var chk = new CheckBox
                        {
                            Content = text,
                            VerticalAlignment = VerticalAlignment.Center,
                            IsChecked = extra.Equals("true", StringComparison.OrdinalIgnoreCase)
                        };
                        chk.Checked += (_, _) => Report("True");
                        chk.Unchecked += (_, _) => Report("False");
                        chk.SetResourceReference(Control.ForegroundProperty, "Brush.TextBoxFg");
                        ctrl = chk;
                        break;
                    }

                case "toggle":
                    {
                        var tg = new ToggleButton
                        {
                            Content = text,
                            VerticalAlignment = VerticalAlignment.Center,
                            IsChecked = extra.Equals("true", StringComparison.OrdinalIgnoreCase)
                        };
                        tg.Checked += (_, _) => Report("True");
                        tg.Unchecked += (_, _) => Report("False");
                        tg.SetResourceReference(Control.BackgroundProperty, "Brush.BtnBg");
                        tg.SetResourceReference(Control.ForegroundProperty, "Brush.BtnText");
                        tg.SetResourceReference(Control.BorderBrushProperty, "Brush.BtnBorder");
                        ctrl = tg;
                        break;
                    }

                case "radio":
                    {
                        var rb = new RadioButton
                        {
                            Content = text,
                            GroupName = extra.Length > 0 ? extra : fallbackGroup,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        rb.Checked += (_, _) => Report(text.Length > 0 ? text : "True");
                        rb.SetResourceReference(Control.ForegroundProperty, "Brush.TextBoxFg");
                        ctrl = rb;
                        break;
                    }

                case "slider":
                    {
                        var p = extra.Split(',');
                        double min = p.Length > 0 && ParseD(p[0], out var a) ? a : 0;
                        double max = p.Length > 1 && ParseD(p[1], out var b) ? b : 100;
                        double val = p.Length > 2 && ParseD(p[2], out var c) ? c : min;
                        double fre = p.Length > 3 && ParseD(p[3], out var d2) ? d2 : 1;
                        var s = new Slider
                        {
                            Minimum = min,
                            Maximum = max,
                            TickFrequency = fre,
                            Value = Math.Clamp(val, min, max),
                            Width = 150,
                            VerticalAlignment = VerticalAlignment.Center,
                            IsSnapToTickEnabled = true
                        };
                        s.ValueChanged += (_, _) => Report(s.Value.ToString("F0"));
                        ctrl = s;
                        break;
                    }

                default:
                    return null;
            }

            // ── Куда бьёт SetText у этого элемента ──
            switch (ctrl)
            {
                case TextBlock tb:
                    _textSetters[id] = v => tb.Text = v;
                    break;

                case TextBox box:
                    // программа ставит текст молча: без эха в ValueChanged, но Results синхронен
                    _textSetters[id] = v =>
                    {
                        _suppress = true; box.Text = v; _suppress = false;
                        Results[id] = v;
                    };
                    break;

                case ComboBox cb:
                    _textSetters[id] = v => FillCombo(cb, v);
                    break;

                case ContentControl cc when cc.Content is string:
                    _textSetters[id] = v => cc.Content = v; // Button, CheckBox, Toggle, Radio
                    break;
            }

            // ── Описание: подпись под элементом или подсказка при наведении ──
            if (desc.Length == 0 || type == "button2")
            {
                _elements[id] = ctrl;
                return ctrl;
            }

            if (descType is "tooltip" or "hover" or "наведение")
            {
                var tip = new ToolTip { Content = desc, Padding = new Thickness(8, 5, 8, 5), FontSize = 11 };
                tip.SetResourceReference(Control.BackgroundProperty, "Brush.PanelBg");
                tip.SetResourceReference(Control.ForegroundProperty, "Brush.TextBoxFg");
                tip.SetResourceReference(Control.BorderBrushProperty, "Brush.BtnBorder");
                ctrl.ToolTip = tip;
                _elements[id] = ctrl;
                return ctrl;
            }

            var wrap = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            wrap.Children.Add(ctrl);

            var descLabel = new TextBlock
            {
                Text = desc,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(154, 160, 176)), // #9AA0B0
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 180,
                Margin = new Thickness(1, 2, 0, 0)
            };
            _descs[id] = descLabel;
            wrap.Children.Add(descLabel);

            _elements[id] = ctrl;
            return wrap;
        }

        /// <summary>Наполнение ComboBox из строки "Опция,Опция,*Выбранная". Сохраняет прежний выбор, если опция ещё есть в списке.</summary>
        private static void FillCombo(ComboBox cb, string extra)
        {
            string? prev = (cb.SelectedItem as ComboBoxItem)?.Content?.ToString();
            cb.Items.Clear();

            ComboBoxItem? marked = null, sameAsPrev = null;
            foreach (var raw in extra.Split(','))
            {
                if (raw.Length == 0) continue;
                bool isMarked = raw.StartsWith("*");
                var item = new ComboBoxItem { Content = isMarked ? raw.Substring(1) : raw };
                cb.Items.Add(item);
                if (isMarked) marked = item;
                if (prev != null && item.Content?.ToString() == prev) sameAsPrev = item;
            }
            cb.SelectedItem = sameAsPrev ?? marked;
        }

        private static bool ParseD(string s, out double v) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v);
    }
}

/*
        private void ControlPanel_ValueChanged(string result)
        {
            var parts = result.Split(new[] { ' ' }, 2);
        }

        private void DemoControlPanel()
        {
            ControlPanelWindow.Show("Настройки", new[]
            {
                // ── Строка 1: заголовок секции ──
                "TextBlock|Внешний вид||sect.look|label|",

                // ── Строка 2: список с подписью ──
                "Combo|Шрифт|Шрифт текста нод|font.family|label|*Segoe UI,Arial,Consolas",

                // ── Строка 3: список с описанием при наведении ──
                "Combo|Тема|Цветовая схема интерфейса|theme.name|tooltip|Тёмная,Светлая,Синяя",

                // ── Строка 4: три элемента в одной строке ──
                "Check|Сетка|Показывать точки сетки|grid.show|label|true;" +
                "Slider|Плотность|Шаг сетки, px|grid.spacing|label|4,40,12;" +
                "Toggle|Магнит|Привязка нод к сетке|grid.snap|tooltip|",

                // ── Строка 5: радиокнопки одной группы (общий id) ──
                "Radio|Слева|Порт новых связей по умолчанию|port.side|label|ports;" +
                "Radio|Справа| |port.side|label|ports",

                // ── Строка 6: ввод + кнопки закрытия ──
                "TextBox|Имя карты|Как назвать сохранение|map.name|label|Без имени;" +
                "Button|Готово||btn.ok|label|close;" +
                "Button|Отмена||btn.cancel|label|close"
            }, this, ControlPanel_ValueChanged);
        }

        private void DemoControlPanel2()
        {
            var win = new ControlPanelWindow("Настройки", new[]
            {
                // ── Строка 1: заголовок секции ──
                "TextBlock|Внешний вид||sect.look|label|",

                // ── Строка 2: список с подписью ──
                "Combo|Шрифт|Шрифт текста нод|font.family|label|*Segoe UI,Arial,Consolas",

                // ── Строка 3: список с описанием при наведении ──
                "Combo|Тема|Цветовая схема интерфейса|theme.name|tooltip|Тёмная,Светлая,Синяя",

                // ── Строка 4: три элемента в одной строке ──
                "Check|Сетка|Показывать точки сетки|grid.show|label|true;" +
                "Slider|Плотность|Шаг сетки, px|grid.spacing|label|4,40,12;" +
                "Toggle|Магнит|Привязка нод к сетке|grid.snap|tooltip|",

                // ── Строка 5: радиокнопки одной группы (общий id) ──
                "Radio|Слева|Порт новых связей по умолчанию|port.side|label|ports;" +
                "Radio|Справа| |port.side|label|ports",

                // ── Строка 6: ввод + кнопки закрытия ──
                "TextBox|Имя карты|Как назвать сохранение|map.name|label|Без имени;" +
                "Button|Готово||btn.ok|label|close;" +
                "Button|Отмена||btn.cancel|label|close"
            });
            win.ValueChanged += ControlPanel_ValueChanged;
            win.Owner = this;
            win.ShowDialog();

            if (win.Results.TryGetValue("font.family", out var font))
            {
                SetStatus($"Выбран шрифт: {font}");
            }
*/