using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// 2-D plot of a single-beam pattern G(θ) vs θ. Re-rasterises on size change.
/// </summary>
public partial class PatternPlotWindow : Window
{
    private readonly ISinglePattern _pattern;
    private readonly double _thetaMaxDeg;

    private const double MarginLeft   = 60;
    private const double MarginRight  = 20;
    private const double MarginTop    = 14;
    private const double MarginBottom = 38;

    public PatternPlotWindow(ISinglePattern pattern, string headerLine)
    {
        InitializeComponent();
        _pattern = pattern;
        HeaderText.Text = headerLine;

        // Plot the full forward + back hemisphere. Beyond 90° the §1.4 Taylor
        // pattern is clamped to LF in our implementation, so the curve will be
        // a flat floor from ~90° onward — visible and unambiguous.
        _thetaMaxDeg = 180.0;

        Loaded += (_, _) => Draw();
    }

    private void PlotCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();

    private void Draw()
    {
        PlotCanvas.Children.Clear();
        double cw = PlotCanvas.ActualWidth;
        double ch = PlotCanvas.ActualHeight;
        if (cw < 50 || ch < 50) return;

        double plotX = MarginLeft;
        double plotY = MarginTop;
        double plotW = cw - MarginLeft - MarginRight;
        double plotH = ch - MarginTop - MarginBottom;
        if (plotW < 10 || plotH < 10) return;

        // Y range: from min gain (somewhere near LF) up to peak Gm, padded.
        // We use LF − 1 as the floor on the plot to give the LF clamp a tiny gap
        // from the axis, and Gm + 3 on top so the peak isn't right at the edge.
        // Sample the pattern first to find the actual min for a tight range.
        const int N = 1000;
        var samples    = new double[N + 1];           // radial cut (φ = 0)
        var samplesTr  = new double[N + 1];           // transverse cut (φ = 90°), populated only for elliptical
        bool isEll = _pattern is Rec1528_1p4_Ell;
        double minG = double.PositiveInfinity;
        for (int i = 0; i <= N; i++)
        {
            double theta = _thetaMaxDeg * i / N;
            samples[i] = _pattern.GainAt(theta, 0.0);
            if (samples[i] < minG) minG = samples[i];
            if (isEll)
            {
                samplesTr[i] = _pattern.GainAt(theta, 90.0);
                if (samplesTr[i] < minG) minG = samplesTr[i];
            }
        }

        double yMax = _pattern.Gm + 3.0;
        double yMin = Math.Min(minG - 1.0, _pattern.Gm - 3.0 - 30.0); // ensure ≥ ~30 dB dynamic range
        // Always keep the LF reference line on-canvas, even if the sampled
        // pattern minimum sits well above it.
        yMin = Math.Min(yMin, _pattern.LF - 2.0);
        double yRange = Math.Max(1.0, yMax - yMin);

        double XAt(double theta)  => plotX + theta / _thetaMaxDeg * plotW;
        double YAt(double gainDb) => plotY + (yMax - gainDb) / yRange * plotH;

        // ---- Frame, grid, ticks ----
        var axisBrush = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));
        var gridBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x88, 0x88, 0x88));
        var tickBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));

        // Plot rectangle border.
        PlotCanvas.Children.Add(new Rectangle
        {
            Width = plotW, Height = plotH,
            Stroke = axisBrush, StrokeThickness = 1, Fill = Brushes.Transparent,
        });
        Canvas.SetLeft(PlotCanvas.Children[^1], plotX);
        Canvas.SetTop(PlotCanvas.Children[^1], plotY);

        // Y grid + tick labels every 10 dB.
        double yTickStep = ChooseTickStep(yRange, 6);
        double yTick0 = Math.Ceiling(yMin / yTickStep) * yTickStep;
        for (double v = yTick0; v <= yMax + 1e-6; v += yTickStep)
        {
            double y = YAt(v);
            PlotCanvas.Children.Add(new Line { X1 = plotX, Y1 = y, X2 = plotX + plotW, Y2 = y, Stroke = gridBrush, StrokeThickness = 0.5 });
            PlotCanvas.Children.Add(new Line { X1 = plotX - 4, Y1 = y, X2 = plotX, Y2 = y, Stroke = tickBrush, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = v.ToString("F0", CultureInfo.InvariantCulture), Foreground = tickBrush, FontSize = 11 };
            lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(lbl, plotX - 6 - lbl.DesiredSize.Width);
            Canvas.SetTop(lbl, y - lbl.DesiredSize.Height / 2);
            PlotCanvas.Children.Add(lbl);
        }

        // X grid + tick labels.
        double xTickStep = ChooseTickStep(_thetaMaxDeg, 8);
        for (double v = 0; v <= _thetaMaxDeg + 1e-6; v += xTickStep)
        {
            double x = XAt(v);
            PlotCanvas.Children.Add(new Line { X1 = x, Y1 = plotY, X2 = x, Y2 = plotY + plotH, Stroke = gridBrush, StrokeThickness = 0.5 });
            PlotCanvas.Children.Add(new Line { X1 = x, Y1 = plotY + plotH, X2 = x, Y2 = plotY + plotH + 4, Stroke = tickBrush, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = v.ToString("F0", CultureInfo.InvariantCulture) + "°", Foreground = tickBrush, FontSize = 11 };
            lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(lbl, x - lbl.DesiredSize.Width / 2);
            Canvas.SetTop(lbl, plotY + plotH + 6);
            PlotCanvas.Children.Add(lbl);
        }

        // ---- Reference lines: LF floor + θb 3-dB edge ----
        DrawHorizontalReferenceLine(plotX, plotY, plotW, plotH, YAt, _pattern.LF,
            label: $"LF = {_pattern.LF:F1} dBi",
            colour: Color.FromRgb(0xb8, 0x40, 0x40));
        double xThetaB = XAt(_pattern.ThetaB);
        if (xThetaB >= plotX && xThetaB <= plotX + plotW)
        {
            PlotCanvas.Children.Add(new Line
            {
                X1 = xThetaB, Y1 = plotY, X2 = xThetaB, Y2 = plotY + plotH,
                Stroke = new SolidColorBrush(Color.FromArgb(0xa0, 0x40, 0x80, 0xb8)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
            });
            var lbl = new TextBlock
            {
                Text = $"θb = {_pattern.ThetaB:F2}°",
                Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x80, 0xb8)),
                FontSize = 11,
            };
            Canvas.SetLeft(lbl, xThetaB + 4);
            Canvas.SetTop(lbl, plotY + 2);
            PlotCanvas.Children.Add(lbl);
        }

        // ---- Pattern polyline (radial cut) ----
        var poly = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x20, 0x70, 0xc0)),
            StrokeThickness = 1.5,
        };
        for (int i = 0; i <= N; i++)
        {
            double theta = _thetaMaxDeg * i / N;
            poly.Points.Add(new Point(XAt(theta), YAt(samples[i])));
        }
        PlotCanvas.Children.Add(poly);

        // ---- Transverse cut (elliptical only) ----
        if (isEll)
        {
            var polyTr = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0xc0, 0x60, 0x20)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 5, 3 },
            };
            for (int i = 0; i <= N; i++)
            {
                double theta = _thetaMaxDeg * i / N;
                polyTr.Points.Add(new Point(XAt(theta), YAt(samplesTr[i])));
            }
            PlotCanvas.Children.Add(polyTr);

            // Mini legend top-right.
            var legR = new TextBlock { Text = "— radial (φ=0)", Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0x70, 0xc0)), FontSize = 11 };
            var legT = new TextBlock { Text = "-- transverse (φ=90°)", Foreground = new SolidColorBrush(Color.FromRgb(0xc0, 0x60, 0x20)), FontSize = 11 };
            legR.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            legT.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(legR, plotX + plotW - 4 - Math.Max(legR.DesiredSize.Width, legT.DesiredSize.Width));
            Canvas.SetTop(legR, plotY + 4);
            Canvas.SetLeft(legT, plotX + plotW - 4 - Math.Max(legR.DesiredSize.Width, legT.DesiredSize.Width));
            Canvas.SetTop(legT, plotY + 4 + legR.DesiredSize.Height + 1);
            PlotCanvas.Children.Add(legR);
            PlotCanvas.Children.Add(legT);

            // Add transverse θb dashed reference too.
            if (_pattern is Rec1528_1p4_Ell ell)
            {
                double xThetaBTr = XAt(ell.ThetaBTransverseDeg);
                if (xThetaBTr >= plotX && xThetaBTr <= plotX + plotW)
                {
                    PlotCanvas.Children.Add(new Line
                    {
                        X1 = xThetaBTr, Y1 = plotY, X2 = xThetaBTr, Y2 = plotY + plotH,
                        Stroke = new SolidColorBrush(Color.FromArgb(0xa0, 0xc0, 0x60, 0x20)),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 4, 3 },
                    });
                    var lbl = new TextBlock
                    {
                        Text = $"θb_tr = {ell.ThetaBTransverseDeg:F2}°",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xc0, 0x60, 0x20)),
                        FontSize = 11,
                    };
                    Canvas.SetLeft(lbl, xThetaBTr + 4);
                    Canvas.SetTop(lbl, plotY + 18);
                    PlotCanvas.Children.Add(lbl);
                }
            }
        }

        // ---- Axis labels ----
        var xLabel = new TextBlock { Text = "Off-axis angle θ (deg)", Foreground = axisBrush, FontSize = 11 };
        xLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(xLabel, plotX + plotW / 2 - xLabel.DesiredSize.Width / 2);
        Canvas.SetTop(xLabel, plotY + plotH + 22);
        PlotCanvas.Children.Add(xLabel);

        var yLabel = new TextBlock { Text = "G(θ) (dBi)", Foreground = axisBrush, FontSize = 11 };
        yLabel.LayoutTransform = new RotateTransform(-90);
        yLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(yLabel, 6);
        Canvas.SetTop(yLabel, plotY + plotH / 2 + yLabel.DesiredSize.Width / 2);
        PlotCanvas.Children.Add(yLabel);
    }

    /// <summary>
    /// Draw a dashed horizontal reference line at <paramref name="gainDb"/>,
    /// labelled at the right edge. Skipped if the line would fall outside the plot.
    /// </summary>
    private void DrawHorizontalReferenceLine(
        double plotX, double plotY, double plotW, double plotH,
        Func<double, double> yAt, double gainDb, string label, Color colour)
    {
        double y = yAt(gainDb);
        if (y < plotY || y > plotY + plotH) return;
        PlotCanvas.Children.Add(new Line
        {
            X1 = plotX, Y1 = y, X2 = plotX + plotW, Y2 = y,
            Stroke = new SolidColorBrush(Color.FromArgb(0xa0, colour.R, colour.G, colour.B)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
        });
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(colour),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromArgb(0xe0, 0xfa, 0xfa, 0xfa)),
            Padding = new Thickness(2, 0, 2, 0),
        };
        lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(lbl, plotX + plotW - lbl.DesiredSize.Width - 2);
        Canvas.SetTop(lbl, y - lbl.DesiredSize.Height - 1);
        PlotCanvas.Children.Add(lbl);
    }

    /// <summary>Round a tick step to a "nice" 1/2/5×10^k value given the desired range and target tick count.</summary>
    private static double ChooseTickStep(double range, int targetTicks)
    {
        double raw = range / Math.Max(1, targetTicks);
        double pow10 = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double m = raw / pow10;
        double nice = m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10;
        return nice * pow10;
    }
}
