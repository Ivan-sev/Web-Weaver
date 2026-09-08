using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WebWeaver
{
    public partial class ControlPanelWindow : Window
    {
        /// <summary>Итоговые значения всех элементов: id → последнее значение.</summary>
        public Dictionary<string, string> Results { get; } = new();

        /// <summary>Единое событие изменений: аргумент = "id значение".</summary>
        public event Action<string>? ValueChanged;

        public ControlPanelWindow(string title, params string[] rows)
        {
            InitializeComponent();
            Title = title;
            tbTitle.Text = title;
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
            // ── Замена пустых ячеек на невидимый символ, чтобы не криво было ──
            for (int i = 0; i < rows.Length; i++) rows[i] = rows[i].Replace("| |", "|ㅤ|"); 

            int rowIdx = 0;
            foreach (var row in rows)
            {
                rowIdx++;
                var line = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

                foreach (var def in row.Split(';'))
                {
                    var el = BuildElement(def, "row" + rowIdx);
                    if (el != null) line.Children.Add(el);
                }

                if (line.Children.Count > 0) spRows.Children.Add(line);
            }
        }

        // ── Один элемент по описанию "Тип|Текст|Описание|id|ТипОписания|ДопДанные" ──
        private FrameworkElement? BuildElement(string def, string fallbackGroup)
        {
            var f = def.Split('|');
            if (f.Length < 4) return null; // минимум: Тип|Текст|Описание|id

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

                case "textblock":
                case "text":
                    ctrl = new TextBlock
                    {
                        Text = text,
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    break;

                case "textbox":
                    {
                        var tb = new TextBox { Text = extra, Width = 180 };
                        tb.TextChanged += (_, _) => Report(tb.Text); // подписка после установки Text
                        ctrl = tb;
                        break;
                    }

                case "combo":
                    {
                        var cb = new ComboBox { Width = 180, VerticalAlignment = VerticalAlignment.Center };
                        foreach (var raw in extra.Split(','))
                        {
                            if (raw.Length == 0) continue;
                            bool defSel = raw.StartsWith("*");
                            var item = new ComboBoxItem { Content = defSel ? raw.Substring(1) : raw };
                            cb.Items.Add(item);
                            if (defSel) cb.SelectedItem = item; // до подписки — без стартового отчёта
                        }
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
                            Foreground = Brushes.White,
                            VerticalAlignment = VerticalAlignment.Center,
                            IsChecked = extra.Equals("true", StringComparison.OrdinalIgnoreCase)
                        };
                        chk.Checked += (_, _) => Report("True");
                        chk.Unchecked += (_, _) => Report("False");
                        ctrl = chk;
                        break;
                    }

                case "toggle":
                    {
                        var tg = new ToggleButton
                        {
                            Content = text,
                            VerticalAlignment = VerticalAlignment.Center,
                            IsChecked = extra.Equals("true", StringComparison.OrdinalIgnoreCase),
                            Background = new SolidColorBrush(Color.FromRgb(42, 45, 56)),
                            Foreground = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 68, 82))
                        };
                        tg.Checked += (_, _) => Report("True");
                        tg.Unchecked += (_, _) => Report("False");
                        ctrl = tg;
                        break;
                    }

                case "radio":
                    {
                        var rb = new RadioButton
                        {
                            Content = text,
                            GroupName = extra.Length > 0 ? extra : fallbackGroup,
                            Foreground = Brushes.White,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        rb.Checked += (_, _) => Report(text.Length > 0 ? text : "True");
                        ctrl = rb;
                        break;
                    }

                case "slider":
                    {
                        var p = extra.Split(',');
                        double min = p.Length > 0 && ParseD(p[0], out var a) ? a : 0;
                        double max = p.Length > 1 && ParseD(p[1], out var b) ? b : 100;
                        double val = p.Length > 2 && ParseD(p[2], out var c) ? c : min;
                        var s = new Slider
                        {
                            Minimum = min,
                            Maximum = max,
                            Value = Math.Clamp(val, min, max), // до подписки
                            Width = 150,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        s.ValueChanged += (_, _) => Report(s.Value.ToString("F0"));
                        ctrl = s;
                        break;
                    }

                default:
                    return null; // неизвестный тип — элемент пропускается
            }

            // ── Описание: подпись под элементом или подсказка при наведении ──
            if (desc.Length == 0) return ctrl;

            if (descType is "tooltip" or "hover" or "наведение")
            {
                ctrl.ToolTip = new ToolTip
                {
                    Content = desc,
                    Background = new SolidColorBrush(Color.FromRgb(42, 45, 56)),  // #2A2D38
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(64, 68, 82)),
                    Padding = new Thickness(8, 5, 8, 5),
                    FontSize = 11
                };
                return ctrl;
            }

            var wrap = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            wrap.Children.Add(ctrl);
            wrap.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(154, 160, 176)), // #9AA0B0
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 180,
                Margin = new Thickness(1, 2, 0, 0)
            });
            return wrap;
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