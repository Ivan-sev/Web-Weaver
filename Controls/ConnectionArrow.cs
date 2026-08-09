using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WebWeaver.Controls;

/// <summary>
/// Стрелка-соединение между нодами. Наследует Shape → является UIElement.
/// </summary>
public class ConnectionArrow : Shape
{
    private Point _from;
    private Point _to;

    public ConnectionArrow()
    {
        Stroke = new SolidColorBrush(AppSettings.ConnectionColor);
        StrokeThickness = AppSettings.ConnectionThickness;
        Fill = new SolidColorBrush(AppSettings.ConnectionColor);
        IsHitTestVisible = true;
        Cursor = Cursors.Hand;
    }

    public void Update(Point from, Point to)
    {
        _from = from;
        _to = to;
        InvalidateVisual();
    }

    protected override Geometry DefiningGeometry => BuildGeometry();

    private Geometry BuildGeometry()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };

        // Кривая Безье
        double cx = (_from.X + _to.X) / 2.0;
        var path = new PathGeometry();
        var fig = new PathFigure { StartPoint = _from, IsFilled = false };
        fig.Segments.Add(new BezierSegment(
            new Point(cx, _from.Y),
            new Point(cx, _to.Y),
            _to, isStroked: true));
        path.Figures.Add(fig);
        group.Children.Add(path);

        // Наконечник стрелки
        group.Children.Add(ArrowHead(_to, _from));

        return group;
    }

    private static Geometry ArrowHead(Point tip, Point origin)
    {
        double dx = tip.X - origin.X;
        double dy = tip.Y - origin.Y;

        double trueAngle = Math.Atan2(dy, dx);

        double absAngle = Math.Abs(trueAngle);
        double tiltFactor;

        if (absAngle < 1.200)
            tiltFactor = 0.0;
        else if (absAngle < 1.50)
            tiltFactor = 0.30;
        else
            tiltFactor = 0.90;

        // Ближайшее горизонтальное направление
        double horizontal = absAngle > Math.PI / 2
            ? Math.Sign(trueAngle) * Math.PI
            : 0.0;

        double angle = horizontal + (trueAngle - horizontal) * tiltFactor;

        // ============================================================

        const double size = 11;
        const double spread = 0.42;

        var p1 = new Point(
            tip.X - size * Math.Cos(angle - spread),
            tip.Y - size * Math.Sin(angle - spread));

        var p2 = new Point(
            tip.X - size * Math.Cos(angle + spread),
            tip.Y - size * Math.Sin(angle + spread));

        var fig = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment(p1, true));
        fig.Segments.Add(new LineSegment(p2, true));

        return new PathGeometry(new[] { fig });
    }
}