using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;

namespace WebWeaver.Models
{
    public class NodeModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Новая нода";
        public string Text { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = AppSettings.NodeDefaultWidth;
        public double Height { get; set; } = AppSettings.NodeDefaultHeight;

        // Цвета сериализуются как hex-строки
        public string BackgroundColorHex { get; set; } = "#282C34";
        public string TextColorHex { get; set; } = "#DCDCDC";
        public string HeaderColorHex { get; set; } = "#3C82C8";

        public string FontFamily { get; set; } = AppSettings.NodeDefaultFontFamily;
        public double FontSize { get; set; } = AppSettings.NodeDefaultFontSize;
        public string ImagePath { get; set; } = "";

        public List<Guid> ConnectedTo { get; set; } = new();

        // Вспомогательные методы
        public Color GetBackgroundColor() => ParseHex(BackgroundColorHex);
        public Color GetTextColor() => ParseHex(TextColorHex);
        public Color GetHeaderColor() => ParseHex(HeaderColorHex);

        public void SetBackgroundColor(Color c) => BackgroundColorHex = ToHex(c);
        public void SetTextColor(Color c) => TextColorHex = ToHex(c);
        public void SetHeaderColor(Color c) => HeaderColorHex = ToHex(c);

        private static Color ParseHex(string hex)
        {
            try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!; }
            catch { return Colors.Gray; }
        }

        private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public NodeModel Clone() => new NodeModel
        {
            Id = Id,
            Name = Name,
            Text = Text,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            FontFamily = FontFamily,
            FontSize = FontSize,
            BackgroundColorHex = BackgroundColorHex,
            HeaderColorHex = HeaderColorHex,
            TextColorHex = TextColorHex,
            ImagePath = ImagePath,
            ConnectedTo = new List<Guid>(ConnectedTo)
        };
    }
}
