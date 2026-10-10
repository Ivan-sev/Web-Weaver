using System;
using System.IO;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WebWeaver.Models;

namespace WebWeaver.Services
{
    public static class SettingsManager
    {
        public const string PathSettings = "Settings.json";
        public const string PathLang = "Language\\";
        public const string DefaultSettings = $@"{{
  ""Language"": ""Русский"",
  ""Theme"": ""Dark"",
  ""NodeDefaults"": {{
    ""Background"": ""#282C34"",
    ""Header"": ""#3C82C8"",
    ""Text"": ""#DCDCDC""
  }},
  ""Autosave"": {{
    ""Enabled"": false,
    ""IntervalSeconds"": 300,
    ""ChangesCount"": 25
  }},
  ""Themes"": {{
    ""Dark"": {{
      ""Form"": {{
        ""WindowBackground"": ""#1C1E24"",
        ""ToolbarBackground"": ""#16181F"",
        ""ToolbarBorder"": ""#2A2D38"",
        ""PanelBackground"": ""#20232B"",
        ""PanelBorder"": ""#3C82C8"",
        ""ButtonBackground"": ""#232630"",
        ""ButtonHover"": ""#303646"",
        ""ButtonBorder"": ""#2A2D38"",
        ""ButtonText"": ""#FFFFFF"",
        ""Accent"": ""#64B4FF"",
        ""AccentButton"": ""#3C82C8"",
        ""TextBoxBackground"": ""#1C1E24"",
        ""TextBoxText"": ""#FFFFFF"",
        ""LabelText"": ""#CCCCCC"",
        ""DescriptionText"": ""#9AA0B0"",
        ""StatusText"": ""#606880"",
        ""ZoomText"": ""#A0C0E0"",
        ""CanvasBackground"": ""#1C1E24"",
        ""GridDot"": ""#373A44""
      }},
      ""Nodes"": {{
        ""Background"": ""#282C34"",
        ""Header"": ""#3C82C8"",
        ""Text"": ""#DCDCDC"",
        ""Border"": ""#50A0F0"",
        ""Connection"": ""#64B4FF"",
        ""ConnectionSelected"": ""#FFDC32"",
        ""Port"": ""#3C82C8"",
        ""PortStroke"": ""#FFFFFF""
      }}
    }},
    ""Light"": {{
      ""Form"": {{
        ""WindowBackground"": ""#F2F3F6"",
        ""ToolbarBackground"": ""#FFFFFF"",
        ""ToolbarBorder"": ""#D0D3DA"",
        ""PanelBackground"": ""#FFFFFF"",
        ""PanelBorder"": ""#3C82C8"",
        ""ButtonBackground"": ""#E8EAEE"",
        ""ButtonHover"": ""#D5DAE2"",
        ""ButtonBorder"": ""#C0C4CC"",
        ""ButtonText"": ""#20232B"",
        ""Accent"": ""#2E6DB4"",
        ""AccentButton"": ""#3C82C8"",
        ""TextBoxBackground"": ""#FFFFFF"",
        ""TextBoxText"": ""#20232B"",
        ""LabelText"": ""#3F4550"",
        ""DescriptionText"": ""#6A7080"",
        ""StatusText"": ""#8A90A0"",
        ""ZoomText"": ""#4A5468"",
        ""CanvasBackground"": ""#EDEFF3"",
        ""GridDot"": ""#C4C9D4""
      }},
      ""Nodes"": {{
        ""Background"": ""#FFFFFF"",
        ""Header"": ""#3C82C8"",
        ""Text"": ""#20232B"",
        ""Border"": ""#7FA8D0"",
        ""Connection"": ""#2E6DB4"",
        ""ConnectionSelected"": ""#E0A800"",
        ""Port"": ""#3C82C8"",
        ""PortStroke"": ""#FFFFFF""
      }}
    }},
    ""Custom"": {{
      ""Form"": {{
        ""WindowBackground"": ""#1A1D21"",
        ""ToolbarBackground"": ""#22262B"",
        ""ToolbarBorder"": ""#3A3F46"",
        ""PanelBackground"": ""#1F2328"",
        ""PanelBorder"": ""#E0A800"",
        ""ButtonBackground"": ""#2A2E34"",
        ""ButtonHover"": ""#383D45"",
        ""ButtonBorder"": ""#3A3F46"",
        ""ButtonText"": ""#E8E6E0"",
        ""Accent"": ""#E0A800"",
        ""AccentButton"": ""#D9A400"",
        ""TextBoxBackground"": ""#16181C"",
        ""TextBoxText"": ""#F0EEE8"",
        ""LabelText"": ""#D0CCC2"",
        ""DescriptionText"": ""#9A968C"",
        ""StatusText"": ""#7A766C"",
        ""ZoomText"": ""#D0B060"",
        ""CanvasBackground"": ""#191C20"",
        ""GridDot"": ""#33373D""
      }},
      ""Nodes"": {{
        ""Background"": ""#26292F"",
        ""Header"": ""#D9A400"",
        ""Text"": ""#E8E6E0"",
        ""Border"": ""#E0A800"",
        ""Connection"": ""#E0B040"",
        ""ConnectionSelected"": ""#FF6B4A"",
        ""Port"": ""#D9A400"",
        ""PortStroke"": ""#16181C""
      }}
    }}
  }},
  ""btSettings"": {{
    ""bt1"": [
      ""BtnNewNode|True"",
      ""AddNodeFromMapFile|True"",
      ""BtnSave|True"",
      ""BtnSaveAs|True"",
      ""BtnOpen|True"",
      ""ShowNodeTree|True""
    ],
    ""bt2"": [
      ""BtnHistory|True"",
      ""BtnClearAll|True"",
      ""BtnFindNode|True"",
      ""BtnSettings|True""
    ],
    ""bt3"": [
      ""BtnResetView|True"",
      ""BtnZoomIn|True"",
      ""BtnZoomOut|True""
    ],
    ""bt4"": [],
    ""bt5"": [],
    ""bt6"": [],
    ""bt7"": [],
    ""bt8"": [],
    ""bt9"": []
  }}
}}";


        public static SettingsData Settings { get; private set; } = new();

        /// <summary>Вызывается после каждого применения темы.</summary>
        public static event Action? ThemeChanged;
        /// <summary>Вызывается после записи файла.</summary>
        public static event Action? Saved;

        public static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PathSettings);

        // ── Чтение (файла может не быть — берутся дефолты) ──

        // <summary>Глубокая копия текущих настроек — для отката изменений.</summary>
        public static SettingsData Snapshot() =>
            System.Text.Json.JsonSerializer.Deserialize<SettingsData>(
                System.Text.Json.JsonSerializer.Serialize(Settings))!;

        // <summary>Вернуть настройки к снимку и перекрасить UI.</summary>
        public static void Restore(SettingsData snapshot)
        {
            Settings = snapshot;
            ApplyTheme(); // ThemeChanged → DynamicResource и код-кисти откатятся сами
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonSerializer.Deserialize<SettingsData>(
                        File.ReadAllText(FilePath));
                    if (loaded != null) Settings = loaded;
                }
                else
                {
                    Language.Set(Settings.Language);
                    File.WriteAllText(PathSettings, DefaultSettings);
                    Load();
                }
            }
            catch 
            {
                Language.Set(Settings.Language);

                var sb = new StringBuilder();
                var txt = Lang.MessageBoxDescErrLoadSettings.Split("\\n");
                foreach (var item in txt) { sb.AppendLine(item); }
                
                var res = MessageBox.Show(sb.ToString(), Lang.MessageBoxCapErrLoadSettings, MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No);

                if (res == MessageBoxResult.Yes) 
                {
                    File.WriteAllText(PathSettings, DefaultSettings);
                    //Save();
                }
            }

            Settings.EnsureThemes();
            Settings.EnsureBtSettings();
            ApplyTheme();
        }

        public static void Save()
        {
            Settings.ButtonWorkarounds();

            // Настраиваем опции сериализации
            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true // по желанию, для красивого форматирования
            };

            File.WriteAllText(FilePath,
                System.Text.Json.JsonSerializer.Serialize(Settings, options));
            Saved?.Invoke();
        }

        /// <summary>
        /// Временный фикс бага
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static SettingsData ButtonWorkarounds(this SettingsData settings)
        {
            var but = settings.BtSettings;

            foreach (var key in but.Keys)
            {
                for (int i = 0; i < but[key].Count; i++)
                {
                    var buffarr = but[key][i].Split("|");
                    if (buffarr.Length > 2) 
                    {
                        but[key][i] = String.Join('|', buffarr[^2..]);
                    }
                }
            }

            return settings;
        }

        // ── Применить палитру к приложению ──
        public static void ApplyTheme()
        {
            var f = Settings.Current.Form;
            var n = Settings.Current.Nodes;

            // Кисти для XAML (DynamicResource) — обновляются на лету
            SetBrush("Brush.WindowBg", f.WindowBackground);
            SetBrush("Brush.ToolbarBg", f.ToolbarBackground);
            SetBrush("Brush.ToolbarBorder", f.ToolbarBorder);
            SetBrush("Brush.PanelBg", f.PanelBackground);
            SetBrush("Brush.PanelBorder", f.PanelBorder);
            SetBrush("Brush.BtnBg", f.ButtonBackground);
            SetBrush("Brush.BtnHover", f.ButtonHover);
            SetBrush("Brush.BtnBorder", f.ButtonBorder);
            SetBrush("Brush.BtnText", f.ButtonText);
            SetBrush("Brush.Accent", f.Accent);
            SetBrush("Brush.AccentBtn", f.AccentButton);
            SetBrush("Brush.TextBoxBg", f.TextBoxBackground);
            SetBrush("Brush.TextBoxFg", f.TextBoxText);
            SetBrush("Brush.LabelFg", f.LabelText);
            SetBrush("Brush.DescFg", f.DescriptionText);
            SetBrush("Brush.StatusFg", f.StatusText);
            SetBrush("Brush.ZoomFg", f.ZoomText);
            SetBrush("Brush.CanvasBg", f.CanvasBackground);
            SetBrush("Brush.GridDot", f.GridDot);

            SetBrush("Brush.NodePort", n.Port);
            SetBrush("Brush.NodePortStroke", n.PortStroke);
            SetBrush("Brush.NodeBorder", n.Border);

            // Цвета для кода: все существующие обращения к AppSettings
            // начинают отдавать новые значения
            AppSettings.CanvasBackground = ParseColor(f.CanvasBackground);
            AppSettings.GridDotColor = ParseColor(f.GridDot);
            AppSettings.InfoPanelBackground = ParseColor(f.PanelBackground);
            AppSettings.InfoPanelBorder = ParseColor(f.PanelBorder);

            AppSettings.NodeBorderColor = ParseColor(n.Border);
            AppSettings.ConnectionColor = ParseColor(n.Connection);
            AppSettings.ConnectionSelectedColor = ParseColor(n.ConnectionSelected);

            ThemeChanged?.Invoke();
        }

        private static void SetBrush(string key, string hex) =>
            Application.Current.Resources[key] = new SolidColorBrush(ParseColor(hex));

        public static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.Magenta; } // битый hex в файле видно сразу
        }
    }
}