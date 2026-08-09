using System.Windows;
using System.Windows.Media;

namespace WebWeaver.Controls;

/// <summary>
/// Лёгкий хост для DrawingVisual — не создаёт тысячи UIElement-ов.
/// </summary>
public sealed class VisualHost : FrameworkElement
{
    private readonly DrawingVisual _visual;

    public VisualHost(DrawingVisual visual)
    {
        _visual = visual;
        AddVisualChild(_visual);
        AddLogicalChild(_visual);
        IsHitTestVisible = false;
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;
}