using System.Windows;
using System.Windows.Media;

namespace WebWeaver
{
    public static class AppSettings
    {
        public const double DefaultWindowWidth = 1280;
        public const double DefaultWindowHeight = 800;

        public const double NodeDefaultWidth = 200;
        public const double NodeDefaultHeight = 120;
        public const double NodeMinWidth = 120;
        public const double NodeMinHeight = 80;
        public const double NodeBorderThickness = 1.5;
        public const double NodeCornerRadius = 8;

        public static Color NodeBorderColor { get; set; } = Color.FromRgb(80, 160, 240);

        public const string NodeDefaultFontFamily = "Segoe UI";
        public const double NodeDefaultFontSize = 12;

        public static Color ConnectionColor { get; set; } = Color.FromRgb(100, 180, 255);
        public const double ConnectionThickness = 2;
        public static Color ConnectionSelectedColor { get; set; } = Color.FromRgb(255, 220, 50);

        public static Color CanvasBackground { get; set; } = Color.FromRgb(28, 30, 36);
        public static Color GridDotColor { get; set; } = Color.FromRgb(55, 58, 68);
        public static double GridSpacing { get; set; } = 30;

        public const double ZoomMin = 0.35;
        public const double ZoomMax = 4.0;
        public const double ZoomStep = 0.05;

        public static Color InfoPanelBackground { get; set; } = Color.FromRgb(32, 35, 43);
        public static Color InfoPanelBorder { get; set; } = Color.FromRgb(60, 130, 200);
        public const double InfoPanelAnimationMs = 220;
        public static double InfoPanelWidth => SystemParameters.WorkArea.Width * 0.25;
        public static double InfoPanelHeight => SystemParameters.WorkArea.Height - 55;
        public static double InfoNotepadWidth => SystemParameters.WorkArea.Width * 0.75;
        public static double InfoNotepadHeight => SystemParameters.WorkArea.Height - 55;

        // Вспомогательное: Color → "#RRGGBB" для дефолтов новых нод
        public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}