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
/// Renders aggregate PFD as a heatmap in satellite-frame azimuth-elevation
/// coordinates (S.1503-4 §D6.4.5) for the "PFD mask (Az/El)" tab. The data —
/// PFD and α grids, auto-scaled ramp bounds — lives in the shared
/// <see cref="PfdMaskField"/>; this class rebuilds the field when dirty, blits
/// it to a WriteableBitmap (α exclusion tinted red when the overlay is on),
/// and draws axes, azimuth cursor and colour legend.
///
/// The rasterised bitmap is cached on the instance and only recomputed when
/// <see cref="Invalidate"/> is called — this keeps canvas-resize and
/// azimuth-cursor redraws snappy (Redraw just re-stretches the cached image).
/// </summary>
public sealed class PfdHeatmapRenderer
{
    private readonly Canvas _canvas;
    private readonly PfdMaskViewModel _vm;
    private readonly PfdMaskField _field;

    private const double LeftMargin = 46.0;
    private const double RightMargin = 100.0;
    private const double TopMargin = 20.0;
    private const double BottomMargin = 36.0;

    private WriteableBitmap? _bmp;
    private bool _dirty = true;

    public PfdHeatmapRenderer(Canvas canvas, PfdMaskViewModel vm, PfdMaskField field)
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

        ChartPrimitives.AddBackground(_canvas, plotL, plotR, plotT, plotB);
        // DrawHeatmap rebuilds the field when dirty — axes / cursor read the
        // field's axis metadata afterwards, so they always match the grids.
        DrawHeatmap(plotL, plotR, plotT, plotB);
        DrawAxes(plotL, plotR, plotT, plotB);
        DrawCutCursor(plotL, plotR, plotT, plotB);
        DrawColorLegend(W - RightMargin + 12, plotT, plotB);
    }

    /// <summary>
    /// Line marking the cut the companion profile plot is slicing: vertical at
    /// X = azimuth (AzEl) or horizontal at Y = α (α/ΔLongitude — one mask row).
    /// </summary>
    private void DrawCutCursor(double l, double r, double t, double b)
    {
        var stroke = new SolidColorBrush(Color.FromArgb(220, 0xff, 0xff, 0xff));
        var lbl = new TextBlock
        {
            Text = _vm.ProfileCutReadout,
            Foreground = stroke,
            FontSize = 10,
        };

        if (_field.Kind == MaskPlotKind.AlphaDeltaLong)
        {
            double yMin = _field.YMin, yMax = _field.YMax;
            double cut = Math.Clamp(_vm.ProfileCutDeg, yMin, yMax);
            double y = b - (cut - yMin) / (yMax - yMin) * (b - t);
            _canvas.Children.Add(new Line
            {
                X1 = l, Y1 = y, X2 = r, Y2 = y,
                Stroke = stroke,
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false,
            });
            Canvas.SetLeft(lbl, l + 4);
            Canvas.SetTop(lbl, Math.Max(t + 2, y - 15));
        }
        else
        {
            double xMin = _field.XMin, xMax = _field.XMax;
            double cut = Math.Clamp(_vm.ProfileCutDeg, xMin, xMax);
            double x = l + (cut - xMin) / (xMax - xMin) * (r - l);
            _canvas.Children.Add(new Line
            {
                X1 = x, Y1 = t, X2 = x, Y2 = b,
                Stroke = stroke,
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false,
            });
            Canvas.SetLeft(lbl, Math.Min(x + 3, r - 52));
            Canvas.SetTop(lbl, t + 2);
        }
        _canvas.Children.Add(lbl);
    }

    private void DrawHeatmap(double l, double r, double t, double b)
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
    /// dirty (after <see cref="PfdMaskField.Rebuild"/>).
    /// </summary>
    private void BuildBitmap()
    {
        int pixW = _field.PixW, pixH = _field.PixH;
        var pfdBuf = _field.PfdGrid;
        var alphaBuf = _field.AlphaGrid;
        if (pfdBuf is null || alphaBuf is null || pixW == 0 || pixH == 0) { _bmp = null; return; }

        double floor = _field.PfdFloor;
        double range = Math.Max(1e-6, _field.PfdCeil - _field.PfdFloor);
        bool showAlpha = _vm.ShowAlphaContour;
        // Exclusion bands (sorted) snapshot once — off bands tint red, attenuate
        // bands tint orange. Basic mode reduces to a single off band at α_excl.
        var bands = _vm.ExclusionBandsSorted();

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

                    if (showAlpha && PfdMaskViewModel.BandFor(bands, alphaBuf[idx]) is { } band)
                    {
                        if (band.IsOff)
                        {
                            rr = (byte)Math.Min(255, rr + 60);
                            gg = (byte)(gg * 0.6);
                            bb = (byte)(bb * 0.6);
                        }
                        else
                        {
                            // Attenuate band — lighter orange wash, deeper with more dB.
                            double f = Math.Clamp(band.AttenDb / 20.0, 0.15, 0.6);
                            rr = (byte)Math.Min(255, rr + (int)(50 * f));
                            gg = (byte)Math.Min(255, gg + (int)(25 * f));
                            bb = (byte)(bb * (1.0 - 0.5 * f));
                        }
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

    private void DrawAxes(double l, double r, double t, double b)
    {
        var gridStroke = new SolidColorBrush(Color.FromArgb(50, 0xff, 0xff, 0xff));
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));
        double xMin = _field.XMin, xMax = _field.XMax;
        double yMin = _field.YMin, yMax = _field.YMax;

        for (double x = xMin; x <= xMax + 1e-6; x += 30.0)
        {
            double px = l + (x - xMin) / (xMax - xMin) * (r - l);
            _canvas.Children.Add(new Line { X1 = px, Y1 = t, X2 = px, Y2 = b, Stroke = gridStroke, StrokeThickness = 0.5, IsHitTestVisible = false });
            var lbl = new TextBlock { Text = $"{x:+0;-0;0}°", Foreground = labelBrush, FontSize = 10 };
            Canvas.SetLeft(lbl, px - 12);
            Canvas.SetTop(lbl, b + 3);
            _canvas.Children.Add(lbl);
        }
        for (double y = yMin; y <= yMax + 1e-6; y += 30.0)
        {
            double py = b - (y - yMin) / (yMax - yMin) * (b - t);
            _canvas.Children.Add(new Line { X1 = l, Y1 = py, X2 = r, Y2 = py, Stroke = gridStroke, StrokeThickness = 0.5, IsHitTestVisible = false });
            var lbl = new TextBlock { Text = $"{y:+0;-0;0}°", Foreground = labelBrush, FontSize = 10 };
            Canvas.SetLeft(lbl, l - 28);
            Canvas.SetTop(lbl, py - 7);
            _canvas.Children.Add(lbl);
        }

        bool alphaDelta = _field.Kind == MaskPlotKind.AlphaDeltaLong;
        var xTitle = new TextBlock
        {
            Text = alphaDelta
                ? "ΔLongitude = NGSO sub-sat long − GSO arc point long (deg) — S.1503-4 §D6.4.4"
                : "azimuth (sat frame, from nadir toward East — S.1503-4 §D6.4.5)",
            Foreground = labelBrush,
            FontSize = 11,
        };
        Canvas.SetLeft(xTitle, (l + r) / 2 - (alphaDelta ? 210 : 155));
        Canvas.SetTop(xTitle, b + 18);
        _canvas.Children.Add(xTitle);
        ChartPrimitives.AddRotatedYTitle(_canvas,
            alphaDelta
                ? "α (signed, deg — S.1503-4 §D6.4.4.1)"
                : "elevation (sat frame, out of East-Down plane toward North)",
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
            var offBrush   = new SolidColorBrush(Color.FromRgb(0xd6, 0x4c, 0x4c));
            var attenBrush = new SolidColorBrush(Color.FromRgb(0xe0, 0x9a, 0x44));
            bool advanced = _vm.UseAdvancedExclusion;
            bool anyAtten = advanced && _vm.ExclusionRings.Any(r => !r.IsOff && r.OuterDeg > 0);

            void Chip(Brush fill, string text, double yOff)
            {
                var swatch = new Rectangle { Width = 12, Height = 12, Fill = fill };
                Canvas.SetLeft(swatch, x0);
                Canvas.SetTop(swatch, b + yOff);
                _canvas.Children.Add(swatch);
                var lbl = new TextBlock { Text = text, Foreground = labelBrush, FontSize = 10 };
                Canvas.SetLeft(lbl, x0 + 16);
                Canvas.SetTop(lbl, b + yOff);
                _canvas.Children.Add(lbl);
            }

            Chip(offBrush, advanced ? "off ring" : $"|α| < {_vm.AlphaExclDeg:F1}°", 10);
            if (anyAtten) Chip(attenBrush, "atten ring", 26);
        }
    }
}
