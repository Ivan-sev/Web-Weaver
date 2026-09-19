using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using WebWeaver.Models;

namespace WebWeaver.Services
{
    public static class SettingsManager
    {
        public static SettingsData Settings { get; private set; } = new();

        /// <summary>Вызывается после каждого применения темы.</summary>
        public static event Action? ThemeChanged;
        /// <summary>Вызывается после записи файла.</summary>
        public static event Action? Saved;

        public static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.json");

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
                    var loaded = System.Text.Json.JsonSerializer.Deserialize<SettingsData>(
                        File.ReadAllText(FilePath));
                    if (loaded != null) Settings = loaded;
                }
            }
            catch { /* битый JSON — работаем с дефолтами */ }

            Settings.EnsureThemes();
            Settings.EnsureBtSettings();
            ApplyTheme();
        }

        public static void Save()
        {
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