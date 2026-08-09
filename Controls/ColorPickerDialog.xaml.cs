using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WebWeaver.Controls;

public class ColorItem
{
    public string HexCode { get; set; } = "";
}

public partial class ColorPickerDialog : Window
{
    public string SelectedColor { get; private set; } = "#FFFFFF";
    private bool _isUpdatingPreview;
    private readonly List<string> _recentColors = new();
    private readonly ObservableCollection<ColorItem> _recentCollection = new();

    public ColorPickerDialog(string initialColor = "#FFFFFF")
    {
        InitializeComponent();
        SelectedColor = initialColor;
        LoadColorPalette();
        LoadRecentColors();
        recentColorsGrid.ItemsSource = _recentCollection;
        txHexCode.Text = SelectedColor;
        UpdatePreview();
    }

    private void LoadColorPalette()
    {
        var colors = new List<ColorItem>
        {
            // Основные цвета
            new() { HexCode = "#FF0000" }, // Красный
            new() { HexCode = "#FF7F00" }, // Оранжевый
            new() { HexCode = "#FFFF00" }, // Жёлтый
            new() { HexCode = "#00FF00" }, // Зелёный
            new() { HexCode = "#0000FF" }, // Синий
            new() { HexCode = "#4B0082" }, // Индиго
            new() { HexCode = "#9400D3" }, // Фиолетовый

            // Пастельные
            new() { HexCode = "#FFB3BA" }, // Светло-розовый
            new() { HexCode = "#FFCCCB" }, // Светло-красный
            new() { HexCode = "#FFE5B4" }, // Персиковый
            new() { HexCode = "#FFFFBA" }, // Светло-жёлтый
            new() { HexCode = "#BAFFC9" }, // Светло-зелёный
            new() { HexCode = "#BAE1FF" }, // Светло-синий
            new() { HexCode = "#E0BBE4" }, // Лавандовый

            // Тёмные
            new() { HexCode = "#800000" }, // Тёмно-красный
            new() { HexCode = "#808000" }, // Оливковый
            new() { HexCode = "#008000" }, // Тёмно-зелёный
            new() { HexCode = "#000080" }, // Тёмно-синий
            new() { HexCode = "#800080" }, // Фиолетовый
            new() { HexCode = "#008080" }, // Бирюзовый

            // Серые
            new() { HexCode = "#FFFFFF" }, // Белый
            new() { HexCode = "#F0F0F0" }, // Очень светлый серый
            new() { HexCode = "#D3D3D3" }, // Светлый серый
            new() { HexCode = "#A9A9A9" }, // Тёмный серый
            new() { HexCode = "#808080" }, // Серый
            new() { HexCode = "#505050" }, // Тёмнее серый
            new() { HexCode = "#303030" }, // Очень тёмный серый
            new() { HexCode = "#000000" }  // Чёрный
        };

        colorGrid.ItemsSource = colors;
    }

    private void LoadRecentColors()
    {
        // Начальные недавно использованные цвета — можно заполнить дефолтными или пустыми
        _recentColors.Clear();
        // По умолчанию пусто; позже будет заполняться во время использования
        RefreshRecentCollection();
    }

    private void RefreshRecentCollection()
    {
        _recentCollection.Clear();
        foreach (var c in _recentColors)
            _recentCollection.Add(new ColorItem { HexCode = c });
    }

    private void AddToRecent(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        if (!hex.StartsWith("#")) hex = "#" + hex;
        hex = hex.ToUpperInvariant();

        // убрать, если уже есть
        _recentColors.RemoveAll(x => x.Equals(hex, StringComparison.OrdinalIgnoreCase));
        // добавить в начало
        _recentColors.Insert(0, hex);
        // ограничить размер
        if (_recentColors.Count > 8) _recentColors.RemoveRange(8, _recentColors.Count - 8);
        RefreshRecentCollection();
    }

    private void TxHexCode_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUpdatingPreview)
            UpdatePreview();
    }

    private void UpdatePreview()
    {
        _isUpdatingPreview = true;
        try
        {
            var hex = txHexCode.Text.Trim();
            var brush = TryParseColorBrush(hex);
            prevColor.Background = brush ?? Brushes.White;
            SelectedColor = hex;
        }
        catch { }
        finally
        {
            _isUpdatingPreview = false;
        }
    }

    private void ColorBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string hex)
        {
            txHexCode.Text = hex;
            SelectedColor = hex;
        }
    }

    // Обработчик для клика по недавно выбранному цвету (тот же обработчик подходит)
    private void RecentColor_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string hex)
        {
            txHexCode.Text = hex;
            SelectedColor = hex;
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        SelectedColor = txHexCode.Text.Trim();
        if (!SelectedColor.StartsWith("#"))
            SelectedColor = "#" + SelectedColor;

        if (SelectedColor.Length != 7)
        {
            MessageBox.Show("Пожалуйста, введите корректный hex код (например: #FF00FF)", 
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Сохранить в историю недавно выбранных
        AddToRecent(SelectedColor);

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static SolidColorBrush? TryParseColorBrush(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            {
                var color = Color.FromRgb(
                    (byte)((rgb >> 16) & 0xFF),
                    (byte)((rgb >> 8) & 0xFF),
                    (byte)(rgb & 0xFF)
                );
                return new SolidColorBrush(color);
            }
        }
        catch { }
        return null;
    }
}
