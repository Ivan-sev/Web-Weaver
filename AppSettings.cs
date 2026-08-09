using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace WebWeaver
{
    public static class AppSettings
    {
        // ═══════════════════════════════════════════════════════════════
        // ОКНО
        // ═══════════════════════════════════════════════════════════════
        public const double DefaultWindowWidth = 1280;
        public const double DefaultWindowHeight = 800;

        // ═══════════════════════════════════════════════════════════════
        // НОДА — РАЗМЕРЫ И ВНЕШНИЙ ВИД
        // ═══════════════════════════════════════════════════════════════
        public const double NodeDefaultWidth = 200;
        public const double NodeDefaultHeight = 120;
        public const double NodeMinWidth = 120;
        public const double NodeMinHeight = 80;
        public const double NodeBorderThickness = 1.5;
        public const double NodeCornerRadius = 8;

        // Цвета по умолчанию
        public static Color NodeDefaultBackground => Color.FromRgb(40, 44, 52);
        public static Color NodeDefaultText => Color.FromRgb(220, 220, 220);
        public static Color NodeHeaderBackground => Color.FromRgb(60, 130, 200);
        public static Color NodeBorderColor => Color.FromRgb(80, 160, 240);

        // Шрифт по умолчанию
        public const string NodeDefaultFontFamily = "Segoe UI";
        public const double NodeDefaultFontSize = 12;

        // ═══════════════════════════════════════════════════════════════
        // СВЯЗИ (ЛИНИИ)
        // ═══════════════════════════════════════════════════════════════
        public static Color ConnectionColor => Color.FromRgb(100, 180, 255);
        public const double ConnectionThickness = 2;
        public static Color ConnectionSelectedColor => Color.FromRgb(255, 220, 50);

        // ═══════════════════════════════════════════════════════════════
        // КАРТА И СЕТКА
        // ═══════════════════════════════════════════════════════════════
        public static Color CanvasBackground => Color.FromRgb(28, 30, 36);
        public static Color GridDotColor => Color.FromRgb(55, 58, 68);
        public const double GridSpacing = 30;

        // ═══════════════════════════════════════════════════════════════
        // МАСШТАБИРОВАНИЕ
        // ═══════════════════════════════════════════════════════════════
        public const double ZoomMin = 0.35;
        public const double ZoomMax = 4.0;
        public const double ZoomStep = 0.05;

        // ═══════════════════════════════════════════════════════════════
        // ИНФОРМАЦИОННАЯ ПАНЕЛЬ
        // ═══════════════════════════════════════════════════════════════
        public static Color InfoPanelBackground => Color.FromRgb(32, 35, 43);
        public static Color InfoPanelBorder => Color.FromRgb(60, 130, 200);
        public const double InfoPanelAnimationMs = 220;
        public static double InfoPanelWidth => SystemParameters.WorkArea.Width * 0.25;
        public static double InfoPanelHeight => SystemParameters.WorkArea.Height - 55;
        public static double InfoNotepadWidth => SystemParameters.WorkArea.Width * 0.75;
        public static double InfoNotepadHeight => SystemParameters.WorkArea.Height - 55;
    }
}
