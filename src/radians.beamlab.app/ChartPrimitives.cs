using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace radians.beamlab.app;

/// <summary>
/// Small drawing helpers shared by the Canvas-based plots (az/el heatmap and
/// the PFD-vs-elevation profile). Axis-tick loops stay in each renderer because
/// their ranges, tick steps and label formats differ; only the truly identical
/// primitives live here.
/// </summary>
public static class ChartPrimitives
{
    private static readonly Brush PlotFill   = new SolidColorBrush(Color.FromRgb(0x14, 0x1a, 0x22));
    private static readonly Brush PlotStroke = new SolidColorBrush(Color.FromRgb(0x3a, 0x40, 0x47));

    /// <summary>Dark plot-area rectangle with a subtle border, filling (l,t)-(r,b).</summary>
    public static void AddBackground(Canvas canvas, double l, double r, double t, double b)
    {
        var bg = new Rectangle
        {
            Width = r - l, Height = b - t,
            Fill = PlotFill,
            Stroke = PlotStroke,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(bg, l);
        Canvas.SetTop(bg, t);
        canvas.Children.Add(bg);
    }

    /// <summary>
    /// A −90°-rotated (vertical) text label whose rotated height is centred on
    /// <paramref name="yCenter"/>, left edge at <paramref name="x"/>. Used for
    /// y-axis titles and the vertical colour-bar caption.
    /// </summary>
    public static void AddRotatedYTitle(Canvas canvas, string text, Brush foreground,
                                        double x, double yCenter, double fontSize = 11)
    {
        var block = new TextBlock { Text = text, Foreground = foreground, FontSize = fontSize };
        block.LayoutTransform = new RotateTransform(-90);
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double rotatedH = block.DesiredSize.Height;    // = original (unrotated) text width
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, yCenter - rotatedH / 2);
        canvas.Children.Add(block);
    }
}
