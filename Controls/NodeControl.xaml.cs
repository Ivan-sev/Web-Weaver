using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WebWeaver.Models;

namespace WebWeaver.Controls;

public partial class NodeControl : UserControl
{
    public NodeModel Model { get; private set; }

    // Флаг — нода перемещается прямо сейчас
    private bool _isDragging;
    private Point _dragStart;
    private Point _nodeOrigin;

    // ── События ──────────────────────────────────────────────────
    public event Action<NodeControl>? NodeMoved;
    public event Action<NodeControl, SizeChangedEventArgs>? Resized;
    public event Action<NodeControl>? RequestConnectFrom;   // правый порт
    public event Action<NodeControl>? RequestConnectFromLeft; // левый порт
    public event Action<NodeControl>? DoubleClicked;
    public event Action<NodeControl, MouseButtonEventArgs>? RightClicked;

    public NodeControl(NodeModel model)
    {
        InitializeComponent();
        Model = model;
        Refresh();
        AttachPortEvents();
        AttachResizeEvents();
        AttachDragEvents();
    }

    // ── Обновление внешнего вида ──────────────────────────────────
    public void Refresh()
    {
        tbTitle.Text = Model.Name;
        tbText.Text = Model.Text;

        // Цвета
        TrySetColor(borderMain, "Background", Model.BackgroundColorHex);
        TrySetColor(borderHeader, "Background", Model.HeaderColorHex);
        TrySetColor(tbTitle, "Foreground", Model.TextColorHex);
        TrySetColor(tbText, "Foreground", Model.TextColorHex);

        // Шрифт
        try
        {
            tbText.FontFamily = new FontFamily(Model.FontFamily);
            tbText.FontSize = Model.FontSize;
        }
        catch { /* оставить дефолт */ }

        // Картинка /
        // Размер
        Width = Model.Width;
        Height = Model.Height;

        // Картинка
        if (!string.IsNullOrWhiteSpace(Model.ImagePath))
        {
            try
            {
                imgContent.Source = new BitmapImage(new Uri(Model.ImagePath));
                imgContent.Visibility = Visibility.Visible;
                tbText.Visibility = Visibility.Collapsed;
            }
            catch
            {
                imgContent.Visibility = Visibility.Collapsed;
                tbText.Visibility = Visibility.Visible;
            }
        }
        else
        {
            imgContent.Visibility = Visibility.Collapsed;
            tbText.Visibility = Visibility.Visible;
        }
    }

    private static void TrySetColor(FrameworkElement el, string prop, string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var brush = new SolidColorBrush(color);
            if (prop == "Background" && el is Control c) c.Background = brush;
            if (prop == "Background" && el is Border b) b.Background = brush;
            if (prop == "Foreground" && el is TextBlock t) t.Foreground = brush;
        }
        catch { }
    }

    // ── Порты соединений ─────────────────────────────────────────
    private void AttachPortEvents()
    {
        portRight.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            RequestConnectFrom?.Invoke(this);
        };

        portLeft.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            RequestConnectFromLeft?.Invoke(this);
        };

        AttachPortHoverEvents(portRight);
        AttachPortHoverEvents(portLeft);
    }

    private void AttachPortHoverEvents(Ellipse port)
    {
        var defaultBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Model.HeaderColorHex));
        port.MouseEnter += (_, _) => port.Fill = Brushes.White;
        port.MouseLeave += (_, _) => port.Fill = defaultBrush;
    }

    // ── Центры портов (в координатах mainCanvas) ─────────────────
    public Point GetRightPortCenter()
    {
        var pos = TransformToAncestor(GetCanvas()).Transform(new Point(0, 0));
        return new Point(pos.X + ActualWidth, pos.Y + ActualHeight / 2);
    }

    public Point GetLeftPortCenter()
    {
        var pos = TransformToAncestor(GetCanvas()).Transform(new Point(0, 0));
        return new Point(pos.X, pos.Y + ActualHeight / 2);
    }

    private Canvas GetCanvas()
    {
        DependencyObject p = this;
        while (p != null)
        {
            p = System.Windows.Media.VisualTreeHelper.GetParent(p);
            if (p is Canvas cv) return cv;
        }
        throw new InvalidOperationException("NodeControl не находится на Canvas");
    }

    // ── Выделение ─────────────────────────────────────────────────
    public void SetSelected(bool value)
    {
        borderMain.BorderBrush = value
            ? new SolidColorBrush(AppSettings.ConnectionSelectedColor)
            : new SolidColorBrush(AppSettings.NodeBorderColor);
        borderMain.BorderThickness = new Thickness(value ? 2.5 : AppSettings.NodeBorderThickness);
    }

    // ── Перетаскивание ────────────────────────────────────────────
    private void AttachDragEvents()
    {
        borderHeader.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { DoubleClicked?.Invoke(this); return; }
            _isDragging = true;
            _dragStart = e.GetPosition(GetCanvas());
            _nodeOrigin = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
            borderHeader.CaptureMouse();
            e.Handled = true;
        };

        borderHeader.MouseMove += (_, e) =>
        {
            if (!_isDragging) return;
            var cur = e.GetPosition(GetCanvas());
            double nx = _nodeOrigin.X + (cur.X - _dragStart.X);
            double ny = _nodeOrigin.Y + (cur.Y - _dragStart.Y);
            Canvas.SetLeft(this, nx);
            Canvas.SetTop(this, ny);
            Model.X = nx;
            Model.Y = ny;
            NodeMoved?.Invoke(this);
        };

        borderHeader.MouseLeftButtonUp += (_, e) =>
        {
            if (!_isDragging) return;
            _isDragging = false;
            borderHeader.ReleaseMouseCapture();
        };

        MouseRightButtonUp += (_, e) =>
        {
            RightClicked?.Invoke(this, e);
            e.Handled = true;
        };
    }

    // ── Изменение размера ─────────────────────────────────────────
    private void AttachResizeEvents()
    {
        resizeThumb.DragDelta += (_, e) =>
        {
            double nw = Math.Max(AppSettings.NodeMinWidth, ActualWidth + e.HorizontalChange);
            double nh = Math.Max(AppSettings.NodeMinHeight, ActualHeight + e.VerticalChange);
            Width = Model.Width = nw;
            Height = Model.Height = nh;
        };

        SizeChanged += (s, e) => Resized?.Invoke(this, e);
    }
}
