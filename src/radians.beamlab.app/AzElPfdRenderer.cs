using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Renders aggregate PFD as a heatmap in azimuth-elevation coordinates for
/// the "PFD mask (Az/El)" tab.
///
/// Sampling strategy (forward rasterisation):
///   * walk out from the sub-satellite point on the ground in polar
///     coordinates (β_sub, γ) — bearing at sub-sat, great-circle central
///     angle to the ES;
///   * γ ranges from 0 to γ_max where γ_max is the central angle at which
///     the user-elevation equals ε_min (so we never sample below the min
///     elevation cut-off);
///   * for each ES = destination(sub-sat, γ, β_sub) compute az/el of the
///     NGSO in the ES horizon frame and the aggregate PFD produced by all
///     active beams; bin (max) into an az/el pixel.
///
/// This gives an areal fill over the (az, el) region actually reachable by
/// an ES on the ground under the current sat, unlike the earlier 1-D ES
/// longitude sweep which degenerated to a curve.
///
/// α overlay: pixels whose ES lies inside |α| &lt; α_excl of the nearest
/// visible GSO satellite are drawn with a red tint mixed over the PFD colour.
///
/// The rasterised bitmap is cached on the instance and only recomputed when
/// <see cref="Invalidate"/> is called — this keeps canvas-resize handling
/// snappy (Redraw just re-stretches the cached image).
/// </summary>
public sealed class AzElPfdRenderer
{
    private readonly Canvas _canvas;
    private readonly PfdMaskViewModel _vm;
    private readonly AzElPfdField _field;

    private const double LeftMargin = 46.0;
    private const double RightMargin = 100.0;
    private const double TopMargin = 20.0;
    private const double BottomMargin = 36.0;

    private WriteableBitmap? _bmp;
    private bool _dirty = true;

    public AzElPfdRenderer(Canvas canvas, PfdMaskViewModel vm, AzElPfdField field)
    {
        _canvas = canvas;
        _vm = vm;
        _field = field;
    }

    /// <summary>Flag the cached heatmap bitmap as stale — the next <see cref="Redraw"/> will recompute it.</summary>
    public void Invalidate() { _dirty = true; }

    public void Redraw()
    {
        _canvas.Children.Clear();
        double W = _canvas.ActualWidth;
        double H = _canvas.ActualHeight;
        if (W < 100 || H < 100) return;

        double plotL = LeftMargin;
        double plotR = W - RightMargin;
        double plotT = TopMargin;
        double plotB = H - BottomMargin;
        if (plotR - plotL < 10 || plotB - plotT < 10) return;

        // S.1503-4 §D6.4.5 sat-frame axes: az from nadir toward East ∈ [-90°, +90°];
        // el from the East-Down plane toward North ∈ [-90°, +90°].
        double azMin = -90.0, azMax = 90.0;
        double elMin = -90.0, elMax = 90.0;

        ChartPrimitives.AddBackground(_canvas, plotL, plotR, plotT, plotB);
        DrawHeatmap(plotL, plotR, plotT, plotB, elMin, elMax);
        DrawAxes(plotL, plotR, plotT, plotB, azMin, azMax, elMin, elMax);
        DrawAzimuthCursor(plotL, plotR, plotT, plotB, azMin, azMax);
        DrawColorLegend(W - RightMargin + 12, plotT, plotB);
    }

    /// <summary>Vertical line marking the azimuth the companion elevation-profile plot is slicing.</summary>
    private void DrawAzimuthCursor(double l, double r, double t, double b, double azMin, double azMax)
    {
        double az = Math.Clamp(_vm.ProfileAzimuthDeg, azMin, azMax);
        double x = l + (az - azMin) / (azMax - azMin) * (r - l);
        var stroke = new SolidColorBrush(Color.FromArgb(220, 0xff, 0xff, 0xff));
        _canvas.Children.Add(new Line
        {
            X1 = x, Y1 = t, X2 = x, Y2 = b,
            Stroke = stroke,
            StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            IsHitTestVisible = false,
        });
        var lbl = new TextBlock
        {
            Text = $"az = {az:+0;-0;0}°",
            Foreground = stroke,
            FontSize = 10,
        };
        Canvas.SetLeft(lbl, Math.Min(x + 3, r - 46));
        Canvas.SetTop(lbl, t + 2);
        _canvas.Children.Add(lbl);
    }

    private void DrawHeatmap(double l, double r, double t, double b, double elMin, double elMax)
    {
        if (_dirty || _bmp is null)
        {
            _field.Rebuild(_vm);
            BuildBitmap();
            _dirty = false;
        }
        if (_bmp is null) return;

        // The bitmap covers the full ±90° square. Stretch to the plot rect.
        var img = new Image
        {
            Source = _bmp,
            Width = r - l,
            Height = b - t,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Linear);
        Canvas.SetLeft(img, l);
        Canvas.SetTop(img, t);
        _canvas.Children.Add(img);
    }

    /// <summary>
    /// Rasterise the field's PFD grid into the cached WriteableBitmap: colour by
    /// the auto-scaled ramp, tint pixels inside the α exclusion when the overlay
    /// is on, leave no-data pixels transparent. Called only when the cache is
    /// dirty (after <see cref="AzElPfdField.Rebuild"/>).
    /// </summary>
    private void BuildBitmap()
    {
        int pixW = _field.PixW, pixH = _field.PixH;
        var pfdBuf = _field.PfdGrid;
        var alphaBuf = _field.AlphaGrid;
        if (pfdBuf is null || alphaBuf is null || pixW == 0 || pixH == 0) { _bmp = null; return; }

        double floor = _field.PfdFloor;
        double range = Math.Max(1e-6, _field.PfdCeil - _field.PfdFloor);
        double alphaExcl = _vm.AlphaExclDeg;
        bool showAlpha = _vm.ShowAlphaContour;

        var bmp = new WriteableBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32, null);
        bmp.Lock();
        unsafe
        {
            byte* basePtr = (byte*)bmp.BackBuffer;
            int stride = bmp.BackBufferStride;
            for (int j = 0; j < pixH; j++)
            {
                byte* row = basePtr + j * stride;
                for (int i = 0; i < pixW; i++)
                {
                    int idx = j * pixW + i;
                    double pfd = pfdBuf[idx];
                    if (double.IsNegativeInfinity(pfd))
                    {
                        row[i * 4 + 0] = 0; row[i * 4 + 1] = 0; row[i * 4 + 2] = 0; row[i * 4 + 3] = 0;
                        continue;
                    }
                    double tRamp = Math.Clamp((pfd - floor) / range, 0.0, 1.0);
                    ColorRamp.Pfd.Sample(tRamp, out byte rr, out byte gg, out byte bb);

                    if (showAlpha && alphaBuf[idx] < alphaExcl)
                    {
                        rr = (byte)Math.Min(255, rr + 60);
                        gg = (byte)(gg * 0.6);
                        bb = (byte)(bb * 0.6);
                    }

                    byte alpha = 240;
                    row[i * 4 + 0] = (byte)(bb * alpha / 255);
                    row[i * 4 + 1] = (byte)(gg * alpha / 255);
                    row[i * 4 + 2] = (byte)(rr * alpha / 255);
                    row[i * 4 + 3] = alpha;
                }
            }
        }
        bmp.AddDirtyRect(new Int32Rect(0, 0, pixW, pixH));
        bmp.Unlock();
        _bmp = bmp;
    }

    private void DrawAxes(double l, double r, double t, double b,
                          double azMin, double azMax, double elMin, double elMax)
    {
        var gridStroke = new SolidColorBrush(Color.FromArgb(50, 0xff, 0xff, 0xff));
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));

        for (double az = -90.0; az <= 90.0 + 1e-6; az += 30.0)
        {
            double x = l + (az - azMin) / (azMax - azMin) * (r - l);
            _canvas.Children.Add(new Line { X1 = x, Y1 = t, X2 = x, Y2 = b, Stroke = gridStroke, StrokeThickness = 0.5, IsHitTestVisible = false });
            var lbl = new TextBlock { Text = $"{az:+0;-0;0}°", Foreground = labelBrush, FontSize = 10 };
            Canvas.SetLeft(lbl, x - 12);
            Canvas.SetTop(lbl, b + 3);
            _canvas.Children.Add(lbl);
        }
        for (double el = -90.0; el <= 90.0 + 1e-6; el += 30.0)
        {
            double y = b - (el - elMin) / (elMax - elMin) * (b - t);
            _canvas.Children.Add(new Line { X1 = l, Y1 = y, X2 = r, Y2 = y, Stroke = gridStroke, StrokeThickness = 0.5, IsHitTestVisible = false });
            var lbl = new TextBlock { Text = $"{el:+0;-0;0}°", Foreground = labelBrush, FontSize = 10 };
            Canvas.SetLeft(lbl, l - 28);
            Canvas.SetTop(lbl, y - 7);
            _canvas.Children.Add(lbl);
        }
        var xTitle = new TextBlock
        {
            Text = "azimuth (sat frame, from nadir toward East — S.1503-4 §D6.4.5)",
            Foreground = labelBrush,
            FontSize = 11,
        };
        Canvas.SetLeft(xTitle, (l + r) / 2 - 155);
        Canvas.SetTop(xTitle, b + 18);
        _canvas.Children.Add(xTitle);
        ChartPrimitives.AddRotatedYTitle(_canvas,
            "elevation (sat frame, out of East-Down plane toward North)",
            labelBrush, x: 4, yCenter: (t + b) / 2);
    }

    private void DrawColorLegend(double x0, double t, double b)
    {
        const double barWidth = 14.0;
        int n = 64;
        double h = (b - t) / n;
        double pfdFloor = _field.PfdFloor;
        double pfdCeil = _field.PfdCeil;

        for (int i = 0; i < n; i++)
        {
            double tRamp = 1.0 - (double)i / (n - 1);
            ColorRamp.Pfd.Sample(tRamp, out byte r, out byte g, out byte bcol);
            var brush = new SolidColorBrush(Color.FromRgb(r, g, bcol));
            var rect = new Rectangle { Width = barWidth, Height = h + 1, Fill = brush };
            Canvas.SetLeft(rect, x0);
            Canvas.SetTop(rect, t + i * h);
            _canvas.Children.Add(rect);
        }
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));
        int labels = 5;
        for (int i = 0; i < labels; i++)
        {
            double frac = (double)i / (labels - 1);
            double y = b - frac * (b - t);
            double v = pfdFloor + frac * (pfdCeil - pfdFloor);
            var lbl = new TextBlock { Text = $"{v:F0}", Foreground = labelBrush, FontSize = 10 };
            Canvas.SetLeft(lbl, x0 + barWidth + 3);
            Canvas.SetTop(lbl, y - 7);
            _canvas.Children.Add(lbl);
        }
        // Vertical title centred along the colour bar, to the right of the tick labels.
        string headline = _field.HasValidRange
            ? $"PFD  dB(W/m²) in {_vm.RefBwKHz:F0} kHz (auto)"
            : $"PFD  dB(W/m²) in {_vm.RefBwKHz:F0} kHz (no data)";
        ChartPrimitives.AddRotatedYTitle(_canvas, headline, labelBrush, x: x0 + barWidth + 30, yCenter: (t + b) / 2);

        if (_vm.ShowAlphaContour)
        {
            var swatch = new Rectangle
            {
                Width = 12, Height = 12,
                Fill = new SolidColorBrush(Color.FromRgb(0xd6, 0x4c, 0x4c)),
            };
            Canvas.SetLeft(swatch, x0);
            Canvas.SetTop(swatch, b + 10);
            _canvas.Children.Add(swatch);
            var lbl = new TextBlock { Text = $"|α| < {_vm.AlphaExclDeg:F1}°", Foreground = labelBrush, FontSize = 10 };
            Canvas.SetLeft(lbl, x0 + 16);
            Canvas.SetTop(lbl, b + 10);
            _canvas.Children.Add(lbl);
        }
    }
}
