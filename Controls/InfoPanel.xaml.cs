using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WebWeaver.Models;

namespace WebWeaver.Controls;

public class NodeLinkItem
{
    public Guid NodeId { get; set; }
    public string DisplayName { get; set; } = "";
}

public partial class InfoPanel : UserControl
{
    public event Action<NodeModel>? SaveRequested;
    public event Action? CancelRequested;
    public event Action<Guid>? LinkNodeRequested;

    private NodeModel? _current;
    private bool _isViewMode;

    public InfoPanel()
    {
        InitializeComponent();
        LoadFonts();
    }

    // ── Режим просмотра (3/4) ────────────────────────────────────
    public void LoadForView(NodeModel model, IEnumerable<NodeModel> allNodes)
    {
        _current = model;
        _isViewMode = true;
        tbPanelTitle.Text = $"📖 Блокнот: {model.Name}";
        spViewMode.Visibility = Visibility.Visible;
        spEditMode.Visibility = Visibility.Collapsed;

        Width = AppSettings.InfoNotepadWidth;
        Height = AppSettings.InfoNotepadHeight;

        txName.Text = model.Name;
        txText.Text = model.Text;
        txText.TextChanged += (_, _) => RebuildPreview(allNodes);

        // Ссылки
        var links = model.ConnectedTo
            .Select(id => allNodes.FirstOrDefault(n => n.Id == id))
            .Where(n => n != null)
            .Select(n => new NodeLinkItem { NodeId = n!.Id, DisplayName = $"→ {n.Name}" })
            .ToList();
        icLinks.ItemsSource = links;

        RebuildPreview(allNodes);
    }

    public void LoadForEdit(NodeModel model) => 
        LoadForEditMode(model, "✏️ Редактировать ноду");

    public void LoadForCreate(NodeModel model) => 
        LoadForEditMode(model, "✨ Новая нода");

    private void LoadForEditMode(NodeModel model, string title)
    {
        _current = model;
        _isViewMode = false;
        tbPanelTitle.Text = title;
        spViewMode.Visibility = Visibility.Collapsed;
        spEditMode.Visibility = Visibility.Visible;
        Width = AppSettings.InfoPanelWidth;
        Height = AppSettings.InfoPanelHeight;
        FillEditFields(model);
    }

    private void FillEditFields(NodeModel m)
    {
        txName.Text = m.Name;
        txTextShort.Text = m.Text;
        txFontSize.Text = m.FontSize.ToString();
        txBgColor.Text = m.BackgroundColorHex;
        txHeaderColor.Text = m.HeaderColorHex;
        txTextColor.Text = m.TextColorHex;
        txImagePath.Text = m.ImagePath;

        cbFont.SelectedItem = cbFont.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == m.FontFamily)
            ?? cbFont.Items[0];

        UpdatePreviews();
    }

    // ── Предпросмотр текста блокнота ──────────────────────────────
    private void RebuildPreview(IEnumerable<NodeModel> allNodes)
    {
        rtbPreview.Document.Blocks.Clear();
        var para = new Paragraph();
        var text = txText.Text ?? "";
        var lines = text.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine;
            int start = 0;
            while (start < line.Length)
            {
                // Ссылка на ноду [имя]
                int bracketOpen = line.IndexOf('[', start);
                int httpIdx = line.IndexOf("http", start, StringComparison.OrdinalIgnoreCase);

                // Выбрать ближайший маркер
                int markerIdx = -1;
                bool isHttp = false;

                if (bracketOpen >= 0 && (httpIdx < 0 || bracketOpen <= httpIdx))
                    markerIdx = bracketOpen;
                else if (httpIdx >= 0)
                { markerIdx = httpIdx; isHttp = true; }

                if (markerIdx < 0)
                {
                    // Обычный текст до конца
                    para.Inlines.Add(new Run(line[start..]));
                    break;
                }

                // Текст до маркера
                if (markerIdx > start)
                    para.Inlines.Add(new Run(line[start..markerIdx]));

                if (!isHttp)
                {
                    // Ссылка на ноду [имя]
                    int bracketClose = line.IndexOf(']', bracketOpen + 1);
                    if (bracketClose < 0)
                    {
                        para.Inlines.Add(new Run(line[markerIdx..]));
                        break;
                    }
                    var nodeName = line[(bracketOpen + 1)..bracketClose];
                    var target = allNodes.FirstOrDefault(n =>
                        n.Name.Equals(nodeName, StringComparison.OrdinalIgnoreCase));

                    var hl = new Hyperlink(new Run($"[{nodeName}]"))
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(90, 179, 255)),
                        Cursor = Cursors.Hand,
                        TextDecorations = null
                    };
                    if (target != null)
                        hl.Click += (_, _) => LinkNodeRequested?.Invoke(target.Id);
                    else
                    {
                        hl.Foreground = Brushes.OrangeRed;
                        hl.ToolTip = "Нода не найдена";
                    }
                    para.Inlines.Add(hl);
                    start = bracketClose + 1;
                }
                else
                {
                    // Интернет-ссылка https://...
                    int end = line.IndexOf(' ', markerIdx);
                    if (end < 0) end = line.Length;
                    var url = line[markerIdx..end];

                    var hl = new Hyperlink(new Run(url))
                    {
                        NavigateUri = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null,
                        Foreground = new SolidColorBrush(Color.FromRgb(90, 200, 120)),
                        Cursor = Cursors.Hand,
                        TextDecorations = TextDecorations.Underline
                    };
                    hl.RequestNavigate += (_, e) =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true }); }
                        catch { }
                    };
                    para.Inlines.Add(hl);
                    start = end;
                }
            }
            para.Inlines.Add(new LineBreak());
        }
        rtbPreview.Document.Blocks.Add(para);
    }

    // ── Шрифты ───────────────────────────────────────────────────
    private void LoadFonts()
    {
        var fonts = new[]
        {
            "Segoe UI", "Arial", "Calibri", "Consolas", "Courier New",
            "Georgia", "Times New Roman", "Verdana", "Tahoma",
            "Comic Sans MS", "Impact", "Lucida Console", "Trebuchet MS",
            "Century Gothic", "Franklin Gothic Medium", "Palatino Linotype"
        };
        cbFont.Items.Clear();
        foreach (var f in fonts)
        {
            cbFont.Items.Add(new ComboBoxItem
            {
                Content = f,
                FontFamily = new FontFamily(f),
                // Явный белый цвет — чтобы текст не был бледным
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(28, 30, 36)),
                FontSize = 14
            });
        }
        if (cbFont.Items.Count > 0) cbFont.SelectedIndex = 0;

        // Принудительный стиль ComboBox
        cbFont.Background = new SolidColorBrush(Color.FromRgb(28, 30, 36));
        cbFont.Foreground = Brushes.White;
        cbFont.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 130, 200));
    }

    // ── Превью цветов ────────────────────────────────────────────
    private void ColorBox_Changed(object s, TextChangedEventArgs e) => UpdatePreviews();

    private void UpdatePreviews()
    {
        SetPreview(prevBg, txBgColor.Text);
        SetPreview(prevHeader, txHeaderColor.Text);
        SetPreview(prevText, txTextColor.Text);
    }

    private static void SetPreview(Border b, string hex)
    {
        try { b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { b.Background = Brushes.Transparent; }
    }

    // ── Кнопки ───────────────────────────────────────────────────
    private void BtnClose_Click(object s, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void BtnCancel_Click(object s, RoutedEventArgs e) => CancelRequested?.Invoke();

    private void BtnSave_Click(object s, RoutedEventArgs e)
    {
        if (_current == null) return;
        _current.Name = txName.Text.Trim();

        if (_isViewMode)
        {
            // Сохранить только текст из блокнота
            _current.Text = txText.Text;
        }
        else
        {
            _current.Text = txTextShort.Text;
            _current.BackgroundColorHex = txBgColor.Text;
            _current.HeaderColorHex = txHeaderColor.Text;
            _current.TextColorHex = txTextColor.Text;
            _current.ImagePath = txImagePath.Text;

            if (double.TryParse(txFontSize.Text, out double fs))
                _current.FontSize = fs;

            if (cbFont.SelectedItem is ComboBoxItem ci)
                _current.FontFamily = ci.Content?.ToString() ?? "Segoe UI";
        }

        SaveRequested?.Invoke(_current);
    }

    private void BtnPickImage_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выбрать картинку",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Все файлы|*.*"
        };
        if (dlg.ShowDialog() == true) txImagePath.Text = dlg.FileName;
    }

    private void LinkBtn_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is Guid id)
            LinkNodeRequested?.Invoke(id);
    }

         private void cbFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
         {

         }

         // ── Выбор цвета через палитру ────────────────────────────────
         private void OpenColorPicker(TextBox targetTextBox)
         {
             var dlg = new ColorPickerDialog(targetTextBox.Text);
             if (dlg.ShowDialog() == true)
             {
                 targetTextBox.Text = dlg.SelectedColor;
             }
         }

         private void PrevBg_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => OpenColorPicker(txBgColor);
         private void PrevHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => OpenColorPicker(txHeaderColor);
         private void PrevText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => OpenColorPicker(txTextColor);
    }