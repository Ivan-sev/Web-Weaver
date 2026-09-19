using System.Linq;
using System.Text.Json.Serialization;

namespace WebWeaver.Models
{
    /// <summary>Корневой объект Settings.json.</summary>
    public class SettingsData
    {
        public string Language { get; set; } = "ru";   // "ru" | "en" (локализация — позже)
        public string Theme { get; set; } = "Dark";    // ключ из Themes

        public NodeDefaults NodeDefaults { get; set; } = new();
        public AutosaveSettings Autosave { get; set; } = new();

        // Параметры тем лежат здесь же — файл можно править руками
        public Dictionary<string, ThemePalette> Themes { get; set; } = DefaultThemes();

        [JsonPropertyName("btSettings")]
        public Dictionary<string, ToolbarGroup> BtSettings { get; set; } = DefaultToolbar();

        public ThemePalette Current =>
            Themes.TryGetValue(Theme, out var t) ? t : Themes["Dark"];

        // Дозаполнить недостающие темы (если файл старый/урезанный)
        public void EnsureThemes()
        {
            Themes ??= DefaultThemes();
            foreach (var kv in DefaultThemes())
                Themes.TryAdd(kv.Key, kv.Value);
        }

        public static Dictionary<string, ThemePalette> DefaultThemes() => new()
        {
            ["Dark"] = Dark(),
            ["Light"] = Light(),
            ["Custom"] = Dark() // стартовая точка кастомной темы
        };

        public static ThemePalette Dark() => new() { Form = new FormTheme(), Nodes = new NodeTheme() };

        public static ThemePalette Light() => new()
        {
            Form = new FormTheme
            {
                WindowBackground = "#F2F3F6",
                ToolbarBackground = "#FFFFFF",
                ToolbarBorder = "#D0D3DA",
                PanelBackground = "#FFFFFF",
                PanelBorder = "#3C82C8",
                ButtonBackground = "#E8EAEE",
                ButtonHover = "#D5DAE2",
                ButtonBorder = "#C0C4CC",
                ButtonText = "#20232B",
                Accent = "#2E6DB4",
                AccentButton = "#3C82C8",
                TextBoxBackground = "#FFFFFF",
                TextBoxText = "#20232B",
                LabelText = "#3F4550",
                DescriptionText = "#6A7080",
                StatusText = "#8A90A0",
                ZoomText = "#4A5468",
                CanvasBackground = "#EDEFF3",
                GridDot = "#C4C9D4"
            },
            Nodes = new NodeTheme
            {
                Background = "#FFFFFF",
                Header = "#3C82C8",
                Text = "#20232B",
                Border = "#7FA8D0",
                Connection = "#2E6DB4",
                ConnectionSelected = "#E0A800",
                Port = "#3C82C8",
                PortStroke = "#FFFFFF"
            }
        };

        public void EnsureBtSettings()
        {
            BtSettings ??= DefaultToolbar();
            foreach (var kv in DefaultToolbar())
                BtSettings.TryAdd(kv.Key, kv.Value);
        }

        public static Dictionary<string, ToolbarGroup> DefaultToolbar()
        {
            var d = new Dictionary<string, ToolbarGroup>();
            for (int i = 1; i <= 9; i++)
                d[$"bt{i}"] = new ToolbarGroup();
            return d;
        }

        /// <summary>
        /// Заполненные группы по порядку bt1..bt9.
        /// </summary>
        public List<(string Key, ToolbarGroup Group)> GetToolbarGroups()
        {
            if (BtSettings == null) return new();

            return BtSettings
                .OrderBy(kv => kv.Key.Length > 2 && int.TryParse(kv.Key.Substring(2), out int n)
                             ? n : int.MaxValue)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }
    }

    public class AutosaveSettings
    {
        public bool Enabled { get; set; } = false;
        public int IntervalSeconds { get; set; } = 300; // автосохранение по времени
        public int ChangesCount { get; set; } = 25;     // …по количеству изменений
    }

    /// <summary>Палитра: отдельно ФОРМА (окно и все панели) и НОДЫ.</summary>
    public class ThemePalette
    {
        public FormTheme Form { get; set; } = new();
        public NodeTheme Nodes { get; set; } = new();
    }

    public class FormTheme
    {
        public string WindowBackground { get; set; } = "#1C1E24";
        public string ToolbarBackground { get; set; } = "#16181F";
        public string ToolbarBorder { get; set; } = "#2A2D38";
        public string PanelBackground { get; set; } = "#20232B";  // инфо-панель, диалоги, попапы
        public string PanelBorder { get; set; } = "#3C82C8";
        public string ButtonBackground { get; set; } = "#232630";
        public string ButtonHover { get; set; } = "#303646";
        public string ButtonBorder { get; set; } = "#2A2D38";
        public string ButtonText { get; set; } = "#FFFFFF";
        public string Accent { get; set; } = "#64B4FF";           // заголовки панелей, акценты
        public string AccentButton { get; set; } = "#3C82C8";     // синие кнопки
        public string TextBoxBackground { get; set; } = "#1C1E24";
        public string TextBoxText { get; set; } = "#FFFFFF";
        public string LabelText { get; set; } = "#CCCCCC";
        public string DescriptionText { get; set; } = "#9AA0B0";
        public string StatusText { get; set; } = "#606880";
        public string ZoomText { get; set; } = "#A0C0E0";
        public string CanvasBackground { get; set; } = "#1C1E24";
        public string GridDot { get; set; } = "#373A44";
    }

    public class NodeTheme
    {
        public string Background { get; set; } = "#282C34";       // фон новых нод
        public string Header { get; set; } = "#3C82C8";           // заголовок новых нод
        public string Text { get; set; } = "#DCDCDC";
        public string Border { get; set; } = "#50A0F0";
        public string Connection { get; set; } = "#64B4FF";
        public string ConnectionSelected { get; set; } = "#FFDC32";
        public string Port { get; set; } = "#3C82C8";
        public string PortStroke { get; set; } = "#FFFFFF";
    }

    /// <summary>Группа кнопок тулбара.</summary>
    public class ToolbarGroup
    {
        public string Content { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> Button { get; set; } = new();

        [JsonIgnore] // иначе System.Text.Json сериализует getter-свойства и засорит файл
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Content) &&
            (Button == null || Button.Count == 0 || Button.All(b => string.IsNullOrWhiteSpace(b)));

    }

    /// <summary>Внешний вид новой ноды по умолчанию (hex #RRGGBB).</summary>
    public class NodeDefaults
    {
        public string Background { get; set; } = "#282C34";
        public string Header { get; set; } = "#3C82C8";
        public string Text { get; set; } = "#DCDCDC";
    }
}