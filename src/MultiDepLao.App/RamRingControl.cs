using System.Windows;
using System.Windows.Media;
using MultiDepLao.Core;

namespace MultiDepLao.App;

/// <summary>Vong RAM 64x64: ranh nen (Cont2, day 8) + cac cung mau noi tiep tu 12 gio theo chieu kim dong ho. Ve lai bang <see cref="Set"/>.</summary>
public sealed class RamRingControl : FrameworkElement
{
    private const double Thickness = 8, Radius = 26;
    private IReadOnlyList<(Brush brush, RamArc arc)> _arcs = Array.Empty<(Brush, RamArc)>();

    protected override Size MeasureOverride(Size availableSize) => new(64, 64);

    /// <summary>brushes[i] ung voi bytes[i]; scaleBytes la tong RAM may (thang do cua vong).</summary>
    public void Set(IReadOnlyList<long> bytes, IReadOnlyList<Brush> brushes, long scaleBytes)
    {
        _arcs = RamRing.Arcs(bytes, scaleBytes).Select(a => (brushes[a.Index], a)).ToList();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var track = TryFindResource("Cont2") as Brush ?? Brushes.Gray;
        dc.DrawEllipse(null, new Pen(track, Thickness), c, Radius, Radius);
        foreach (var (brush, arc) in _arcs)
        {
            double sweep = Math.Min(arc.Sweep, 359.99);
            var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(At(c, arc.Start), false, false);
                ctx.ArcTo(At(c, arc.Start + sweep), new Size(Radius, Radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }

    private static Point At(Point c, double deg)
    {
        double r = (deg - 90) * Math.PI / 180;   // 0 do = 12 gio
        return new Point(c.X + Radius * Math.Cos(r), c.Y + Radius * Math.Sin(r));
    }
}
