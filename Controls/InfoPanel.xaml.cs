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

    private bool _viewEditing;

    private List<NodeModel>? _viewNodes;

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

        _viewNodes = allNodes.ToList();

        var childLinks = model.ConnectedTo
            .Select(id => allNodes.FirstOrDefault(n => n.Id == id))
            .Where(n => n != null)
            .Select(n => new NodeLinkItem { NodeId = n!.Id, DisplayName = $"→ {n.Name}" });

        var parentLinks = allNodes
            .Where(n => n.ConnectedTo.Contains(model.Id))
            .Select(n => new NodeLinkItem { NodeId = n.Id, DisplayName = $"{n.Name} →" });

        icLinks.ItemsSource = parentLinks.Concat(childLinks).ToList();

        ShowViewPreview(); // при каждом открытии — режим «Текст ноды»
    }

    private void BtnToggleText_Click(object s, RoutedEventArgs e)
    {
        if (_viewEditing) ShowViewPreview();
        else ShowViewEditor();
    }

    private void ShowViewEditor()
    {
        _viewEditing = true;
        tbTextLabel.Text = "Блокнот";
        tbLinkHint.Visibility = Visibility.Visible;
        rtbPreview.Visibility = Visibility.Collapsed;
        txText.Visibility = Visibility.Visible;
        btnToggleText.Content = "Предпросмотр";
        txText.Focus();
        txText.CaretIndex = txText.Text.Length;
    }

    private void ShowViewPreview()
    {
        _viewEditing = false;
        tbTextLabel.Text = "Текст ноды";
        tbLinkHint.Visibility = Visibility.Collapsed;
        txText.Visibility = Visibility.Collapsed;
        rtbPreview.Visibility = Visibility.Visible;
        btnToggleText.Content = "Изменить";
        if (_viewNodes != null) RebuildPreview(_viewNodes);
    }

    private void TxText_ViewChanged(object sender, TextChangedEventArgs e)
    {
        if (_viewNodes != null) RebuildPreview(_viewNodes);
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
    private static readonly SolidColorBrush LinkBrush = new(Color.FromRgb(0x5A, 0xB3, 0xFF));

    private void RebuildPreview(IReadOnlyList<NodeModel> nodes)
    {
        rtbPreview.Document.Blocks.Clear();
        var para = new Paragraph();

        foreach (var rawLine in (txText.Text ?? "").Split('\n'))
            ParseLine(rawLine, nodes, para);

        rtbPreview.Document.Blocks.Add(para);
    }

    private void ParseLine(string line, IReadOnlyList<NodeModel> nodes, Paragraph para)
    {
        int i = 0;
        while (i < line.Length)
        {
            int bracket = line.IndexOf('[', i);
            int http = line.IndexOf("http", i, StringComparison.OrdinalIgnoreCase);

            int next;
            bool isBracket;
            if (bracket >= 0 && (http < 0 || bracket < http)) { next = bracket; isBracket = true; }
            else if (http >= 0) { next = http; isBracket = false; }
            else { para.Inlines.Add(new Run(line[i..])); break; }

            if (next > i) para.Inlines.Add(new Run(line[i..next]));

            if (isBracket)
            {
                int close = line.IndexOf(']', next + 1);
                if (close < 0) { para.Inlines.Add(new Run(line[next..])); break; }

                // Новый синтаксис: [текст]:[цель]
                if (close + 2 < line.Length && line[close + 1] == ':' && line[close + 2] == '[')
                {
                    int close2 = line.IndexOf(']', close + 3);
                    if (close2 < 0) { para.Inlines.Add(new Run(line[next..])); break; }

                    AddSmartLink(para,
                        line[(next + 1)..close].Trim(),
                        line[(close + 3)..close2].Trim(),
                        nodes);
                    i = close2 + 1;
                }
                else
                {
                    // Прежний синтаксис: [имя ноды] — показываем со скобками, как раньше
                    string name = line[(next + 1)..close];
                    AddSmartLink(para, $"[{name}]", name, nodes);
                    i = close + 1;
                }
            }
            else
            {
                // Голый URL до пробела
                int end = line.IndexOf(' ', next);
                if (end < 0) end = line.Length;
                AddSmartLink(para, line[next..end], line[next..end], nodes);
                i = end;
            }
        }
        para.Inlines.Add(new LineBreak());
    }

    private void AddSmartLink(Paragraph para, string display, string target,
                              IReadOnlyList<NodeModel> nodes)
    {
        if (display.Length == 0) display = target;

        bool isUrl = target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                  || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                  || target.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

        var hl = new Hyperlink(new Run(display))
        {
            Foreground = LinkBrush,
            FontWeight = FontWeights.Bold,   // жирный синий, как в задании
            Cursor = Cursors.Hand,
            TextDecorations = null
        };

        if (isUrl)
        {
            string url = target.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                       ? "https://" + target
                       : target;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                hl.NavigateUri = uri;
            hl.RequestNavigate += (_, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(e.Uri.ToString())
                        { UseShellExecute = true });
                }
                catch { }
            };
        }
        else
        {
            var node = nodes.FirstOrDefault(n =>
                n.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (node != null)
                hl.Click += (_, _) => LinkNodeRequested?.Invoke(node.Id);
            else
            {
                hl.Foreground = Brushes.OrangeRed;
                hl.ToolTip = "Нода не найдена";
            }
        }

        para.Inlines.Add(hl);
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

    private void LinkItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NodeLinkItem item)
            LinkNodeRequested?.Invoke(item.NodeId);
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