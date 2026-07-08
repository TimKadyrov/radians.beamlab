using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace radians.beamlab.app;

/// <summary>
/// Single-curve PFD profile through the mask heatmap at the slider-selected
/// cut (<see cref="PfdMaskViewModel.ProfileCutDeg"/>):
///   AzEl -- vertical cut at an azimuth -> PFD vs sat-frame elevation;
///   alpha/deltaLongitude -- horizontal cut at a signed alpha -> PFD vs deltaLongitude,
///   i.e. one row of the ITU mask table.
///
/// Data is not recomputed here -- the slice is read from the shared
/// <see cref="PfdMaskField"/> (<see cref="PfdMaskField.ProfileAtX"/> /
/// <see cref="PfdMaskField.ProfileAtY"/>). The heatmap renderer rebuilds that
/// field when dirty, so redrawing this plot after the heatmap renderer is enough.
/// </summary>
public sealed class PfdProfileRenderer
{
    private readonly Canvas _canvas;
    private readonly PfdMaskViewModel _vm;
    private readonly PfdMaskField _field;

    private const double LeftMargin = 52.0;
    private const double RightMargin = 16.0;
    private const double TopMargin = 20.0;
    private const double BottomMargin = 36.0;

    public PfdProfileRenderer(Canvas canvas, PfdMaskViewModel vm, PfdMaskField field)
    {
        _canvas = canvas;
        _vm = vm;
        _field = field;
    }

    public void Redraw()
    {
        _canvas.Children.Clear();
        double W = _canvas.ActualWidth;
        double H = _canvas.ActualHeight;
        if (W < 60 || H < 60) return;

        double l = LeftMargin, r = W - RightMargin;
        double t = TopMargin, b = H - BottomMargin;
        if (r - l < 10 || b - t < 10) return;

        // Profile X axis: AzEl slices vertically at an azimuth -> X = the field's
        // Y (mask elevation, +/-90 deg); alpha/deltaLongitude slices horizontally at an alpha ->
        // X = the field's X (deltaLongitude, +/-180 deg) -- one row of the mask table.
        bool alphaDelta = _field.Kind == MaskPlotKind.AlphaDeltaLong;
        double xMin = alphaDelta ? _field.XMin : _field.YMin;
        double xMax = alphaDelta ? _field.XMax : _field.YMax;

        // Y-range: prefer the source's auto-scaled bounds so the two plots match.
        double yMin, yMax;
        if (_field.HasValidRange)
        {
            yMin = _field.PfdFloor;
            yMax = _field.PfdCeil;
        }
        else
        {
            yMin = -180.0;
            yMax = -100.0;
        }
        if (yMax - yMin < 1) { yMin -= 5; yMax += 5; }

        ChartPrimitives.AddBackground(_canvas, l, r, t, b);
        DrawAxes(l, r, t, b, xMin, xMax, yMin, yMax);
        if (!alphaDelta)
        {
            // Closed-form geometric guides in the az/el frame.
            DrawEsElevationGuides(l, r, t, b, xMin, xMax);
            DrawAlphaGuides(l, r, t, b, xMin, xMax);
        }
        else
        {
            DrawExclusionNote(l, t);
            DrawEsElevationGuidesAlphaDelta(l, r, t, b, xMin, xMax);
        }
        DrawCurve(l, r, t, b, xMin, xMax, yMin, yMax);
    }

    /// <summary>
    /// alpha/deltaLongitude mode: the cut sits at a fixed alpha, so the exclusion is a whole
    /// state, not a line -- classify the cut |alpha| against the bands and note it
    /// (off -> red "side lobes only"; attenuate -> orange "beams -N dB").
    /// </summary>
    private void DrawExclusionNote(double l, double t)
    {
        var band = PfdMaskViewModel.BandFor(_vm.ExclusionBandsSorted(), Math.Abs(_vm.ProfileCutDeg));
        if (band is not { } b) return;

        var (stroke, text) = b.IsOff
            ? (new SolidColorBrush(Color.FromArgb(230, 0xd6, 0x4c, 0x4c)),
               $"|α| < {b.OuterDeg:F0}°  — beams off, side lobes only")
            : (new SolidColorBrush(Color.FromArgb(230, 0xe0, 0x9a, 0x44)),
               $"|α| < {b.OuterDeg:F0}°  — beams attenuated −{b.AttenDb:F0} dB");

        var lbl = new TextBlock { Text = text, Foreground = stroke, FontSize = 10 };
        Canvas.SetLeft(lbl, l + 8);
        Canvas.SetTop(lbl, t + 4);
        _canvas.Children.Add(lbl);
    }

    /// <summary>
    /// alpha/deltaLongitude mode: ES-elevation guides derived from the sampled data
    /// (no closed form in these coordinates), along the deltaLongitude axis at the
    /// cut alpha. Cyan verticals where the slice's ES elevation crosses eps_min;
    /// amber verticals at the slice's data edges -- the visible-disc boundary,
    /// where ES elevation reaches ~ 0 deg.
    /// </summary>
    private void DrawEsElevationGuidesAlphaDelta(double l, double r, double t, double b, double xMin, double xMax)
    {
        var slice = _field.EsElevProfileAtY(_vm.ProfileCutDeg);
        if (slice.Count < 2) return;

        double xRange = xMax - xMin;
        // Gap threshold scales with bin width so coarse mask steps don't break every pair.
        double binW = _field.PixW > 0 ? (_field.XMax - _field.XMin) / _field.PixW : 1.0;
        double gap = Math.Max(3.0, 2.5 * binW);

        void Vertical(double xv, Brush stroke)
        {
            if (xv < xMin || xv > xMax) return;
            double x = l + (xv - xMin) / xRange * (r - l);
            _canvas.Children.Add(new Line
            {
                X1 = x, Y1 = t, X2 = x, Y2 = b,
                Stroke = stroke,
                StrokeThickness = 1.1,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                IsHitTestVisible = false,
            });
        }

        void Label(string text, Brush stroke, double xv, double labelY)
        {
            double x = l + (xv - xMin) / xRange * (r - l);
            var lbl = new TextBlock { Text = text, Foreground = stroke, FontSize = 9 };
            Canvas.SetLeft(lbl, Math.Min(x + 2, r - 46));
            Canvas.SetTop(lbl, labelY);
            _canvas.Children.Add(lbl);
        }

        // Horizon (ES eps ~ 0 deg): the data edges of the slice ARE the visible-disc
        // boundary -- mark the outermost samples in amber.
        var amber = new SolidColorBrush(Color.FromArgb(200, 0xff, 0xc8, 0x66));
        Vertical(slice[0].xDeg, amber);
        Vertical(slice[^1].xDeg, amber);
        Label("ES ε≈0°", amber, slice[^1].xDeg, b - 40);

        // eps_min: crossing detection along the slice, skipping pairs that span a
        // data gap (a jump much larger than the bin width means a discontinuity).
        var cyan = new SolidColorBrush(Color.FromArgb(200, 0x5a, 0xd0, 0xe0));
        double target = _vm.MinElevDeg;
        bool labelled = false;
        for (int i = 1; i < slice.Count; i++)
        {
            double x0 = slice[i - 1].xDeg, v0 = slice[i - 1].esElevDeg;
            double x1 = slice[i].xDeg,     v1 = slice[i].esElevDeg;
            if (x1 - x0 > gap) continue;               // gap in the slice
            double d0 = v0 - target, d1 = v1 - target;
            if (d0 == 0.0) d0 = -1e-9;
            if (d0 * d1 >= 0.0) continue;              // no crossing between these samples

            double frac = d0 / (d0 - d1);
            double xCross = x0 + frac * (x1 - x0);
            Vertical(xCross, cyan);
            if (!labelled)
            {
                Label($"ES ε={target:F0}°", cyan, xCross, b - 26);
                labelled = true;
            }
        }
    }

    /// <summary>
    /// Vertical guide lines where the GSO avoidance angle |alpha| crosses each
    /// exclusion-band outer edge along the current azimuth slice -- the mask-el
    /// boundaries of the exclusion zone on this cut. Off bands red, attenuate
    /// bands orange, to match the heatmap tint. Always drawn (independent of the
    /// heatmap's "Mark alpha" toggle).
    /// </summary>
    private void DrawAlphaGuides(double l, double r, double t, double b, double xMin, double xMax)
    {
        var slice = _field.AlphaProfileAtX(_vm.ProfileCutDeg);
        if (slice.Count < 2) return;

        var offStroke   = new SolidColorBrush(Color.FromArgb(220, 0xd6, 0x4c, 0x4c));
        var attenStroke = new SolidColorBrush(Color.FromArgb(220, 0xe0, 0x9a, 0x44));

        foreach (var band in _vm.ExclusionBandsSorted())
        {
            double excl = band.OuterDeg;
            var stroke = band.IsOff ? offStroke : attenStroke;
            string label = band.IsOff ? $"|α|={excl:F0}° off" : $"|α|={excl:F0}° −{band.AttenDb:F0}dB";
            bool labelled = false;

            for (int i = 1; i < slice.Count; i++)
            {
                double e0 = slice[i - 1].yDeg, a0 = slice[i - 1].alphaDeg;
                double e1 = slice[i].yDeg,     a1 = slice[i].alphaDeg;
                double d0 = a0 - excl, d1 = a1 - excl;
                if (d0 == 0.0) d0 = -1e-9;                 // treat exact hits as just-below
                if (d0 * d1 >= 0.0) continue;              // no crossing between these samples

                // Linear interpolation of the elevation where |alpha| == the band edge.
                double frac = d0 / (d0 - d1);
                double elCross = e0 + frac * (e1 - e0);
                if (elCross < xMin || elCross > xMax) continue;

                double x = l + (elCross - xMin) / (xMax - xMin) * (r - l);
                _canvas.Children.Add(new Line
                {
                    X1 = x, Y1 = t, X2 = x, Y2 = b,
                    Stroke = stroke,
                    StrokeThickness = 1.1,
                    StrokeDashArray = new DoubleCollection { 2, 3 },
                    IsHitTestVisible = false,
                });
                if (!labelled)
                {
                    var lbl = new TextBlock { Text = label, Foreground = stroke, FontSize = 9 };
                    Canvas.SetLeft(lbl, Math.Min(x + 2, r - 70));
                    Canvas.SetTop(lbl, t + 2);
                    _canvas.Children.Add(lbl);
                    labelled = true;
                }
            }
        }
    }

    /// <summary>
    /// Vertical guide lines translating the mask-elevation X axis into physical
    /// earth-station elevation, for the current slice azimuth:
    ///   * ES eps = 0 deg (the geometric horizon), amber;
    ///   * ES eps = eps_min (the user's <see cref="PfdMaskViewModel.MinElevDeg"/>), cyan.
    /// Along a fixed azimuth the ES elevation is symmetric in mask-el sign, so
    /// each target draws a +/- pair. Lines that fall off-plot (target unreachable
    /// at this azimuth) are skipped.
    /// </summary>
    private void DrawEsElevationGuides(double l, double r, double t, double b, double xMin, double xMax)
    {
        double az = _vm.ProfileCutDeg;
        double altKm = _vm.Scene.AltitudeKm;

        void Guide(double esElevDeg, Color color, string label, double labelY)
        {
            double elMag = MaskElevForEsElevation(esElevDeg, az, altKm);
            if (double.IsNaN(elMag)) return;
            var stroke = new SolidColorBrush(color);
            foreach (double el in new[] { -elMag, elMag })
            {
                if (el < xMin || el > xMax) continue;
                double x = l + (el - xMin) / (xMax - xMin) * (r - l);
                _canvas.Children.Add(new Line
                {
                    X1 = x, Y1 = t, X2 = x, Y2 = b,
                    Stroke = stroke,
                    StrokeThickness = 1.1,
                    StrokeDashArray = new DoubleCollection { 4, 4 },
                    IsHitTestVisible = false,
                });
            }
            // Label once, next to the positive-side line if it's on-plot.
            if (elMag <= xMax)
            {
                double x = l + (elMag - xMin) / (xMax - xMin) * (r - l);
                var lbl = new TextBlock { Text = label, Foreground = stroke, FontSize = 9 };
                Canvas.SetLeft(lbl, Math.Min(x + 2, r - 46));
                Canvas.SetTop(lbl, labelY);
                _canvas.Children.Add(lbl);
            }
        }

        Guide(0.0, Color.FromArgb(200, 0xff, 0xc8, 0x66), "ES ε=0°", b - 40);
        Guide(_vm.MinElevDeg, Color.FromArgb(200, 0x5a, 0xd0, 0xe0), $"ES ε={_vm.MinElevDeg:F0}°", b - 26);
    }

    /// <summary>
    /// Magnitude of the mask (sat-frame) elevation at which a ground point on the
    /// given azimuth cut has earth-station elevation <paramref name="esElevDeg"/>.
    /// Uses the law of sines in the (Earth-centre, sat, ground) triangle:
    ///   sin theta = (R/(R+h))*cos eps   (off-nadir for ES elevation eps), and
    ///   cos theta = cos(az)*cos(el)   (sat-frame decomposition), so
    ///   |el| = arccos( cos theta / cos az ).
    /// Returns NaN if the target is unreachable at this azimuth (cos theta / cos az &gt; 1).
    /// </summary>
    private static double MaskElevForEsElevation(double esElevDeg, double azDeg, double altKm)
    {
        // Off-nadir theta for this ES elevation (law of sines, shared with GeoMath),
        // then split by the sat-frame identity cos theta = cos(az)*cos(el).
        double thetaRad = GeoMath.OffNadirForEsElevationDeg(esElevDeg, altKm) * Math.PI / 180.0;
        double cosTheta = Math.Cos(thetaRad);

        double cosAz = Math.Cos(azDeg * Math.PI / 180.0);
        if (Math.Abs(cosAz) < 1e-9) return double.NaN;
        double cosEl = cosTheta / cosAz;
        if (cosEl > 1.0 || cosEl < -1.0) return double.NaN;
        return Math.Acos(cosEl) * 180.0 / Math.PI;
    }

    private void DrawAxes(double l, double r, double t, double b,
                          double xMin, double xMax, double yMin, double yMax)
    {
        var gridStroke = new SolidColorBrush(Color.FromArgb(50, 0xff, 0xff, 0xff));
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));

        // X ticks every 30 deg across the range (el/alpha: +/-90 deg; deltaLongitude: +/-180 deg).
        for (double x = xMin; x <= xMax + 1e-6; x += 30.0)
        {
            double px = l + (x - xMin) / (xMax - xMin) * (r - l);
            _canvas.Children.Add(new Line { X1 = px, Y1 = t, X2 = px, Y2 = b, Stroke = gridStroke, StrokeThickness = 0.5, IsHitTestVisible = false });
            var lbl = new TextBlock { Text = $"{x:+0;-0;0}°", Foreground = labelBrush, FontSize = 10 };
            Canvas.SetLeft(lbl, px - 12);
            Canvas.SetTop(lbl, b + 3);
            _canvas.Children.Add(lbl);
        }

        // Y ticks: ~6 evenly spaced.
        int nY = 6;
        double dY = (yMax - yMin) / nY;
        for (int i = 0; i <= nY; i++)
        {
            double v = yMin + i * dY;
            double py = b - (v - yMin) / (yMax - yMin) * (b - t);
            _canvas.Children.Add(new Line { X1 = l, Y1 = py, X2 = r, Y2 = py, Stroke = gridStroke, StrokeThickness = 0.5, IsHitTestVisible = false });
            var lbl = new TextBlock { Text = $"{v:F0}", Foreground = labelBrush, FontSize = 10 };
            Canvas.SetLeft(lbl, l - 34);
            Canvas.SetTop(lbl, py - 7);
            _canvas.Children.Add(lbl);
        }

        var xTitle = new TextBlock
        {
            Text = _vm.MaskKind == MaskPlotKind.AlphaDeltaLong
                ? $"ΔLongitude at α = {_vm.ProfileCutDeg:+0;-0;0}° (deg) — one mask-table row"
                : $"mask elevation at az = {_vm.ProfileCutDeg:+0;-0;0}° (sat frame)",
            Foreground = labelBrush,
            FontSize = 11,
        };
        Canvas.SetLeft(xTitle, (l + r) / 2 - 120);
        Canvas.SetTop(xTitle, b + 18);
        _canvas.Children.Add(xTitle);

        ChartPrimitives.AddRotatedYTitle(_canvas, "PFD  dB(W/m²)", labelBrush, x: 4, yCenter: (t + b) / 2);
    }

    private void DrawCurve(double l, double r, double t, double b,
                           double xMin, double xMax, double yMin, double yMax)
    {
        // Single PFD slice through the heatmap at the selected cut: a column
        // (AzEl, cut = azimuth) or a row (alpha/deltaLongitude, cut = alpha).
        var slice = _field.Kind == MaskPlotKind.AlphaDeltaLong
            ? _field.ProfileAtY(_vm.ProfileCutDeg)
            : _field.ProfileAtX(_vm.ProfileCutDeg);
        var stroke = new SolidColorBrush(Color.FromRgb(0x28, 0xb5, 0x50));

        double xRange = xMax - xMin;
        double yRange = yMax - yMin;
        double PlotX(double el) => l + (el - xMin) / xRange * (r - l);
        double PlotY(double pfd)
        {
            double y = b - (pfd - yMin) / yRange * (b - t);
            if (y < t) y = t; else if (y > b) y = b;
            return y;
        }

        // Slice is already ordered by ascending elevation. Break the polyline at
        // gaps (missing rows past the horizon) so no chord spans them.
        Polyline? cur = null;
        double prevEl = double.NaN;
        double binStep = 180.0 / Math.Max(1, slice.Count);   // rough gap threshold
        foreach (var (elDeg, pfd) in slice)
        {
            if (elDeg < xMin || elDeg > xMax) { cur = null; prevEl = double.NaN; continue; }
            // A jump larger than a few degrees means an occluded stretch -> break.
            if (!double.IsNaN(prevEl) && elDeg - prevEl > 3.0) cur = null;
            if (cur is null)
            {
                cur = new Polyline
                {
                    Stroke = stroke,
                    StrokeThickness = 2.0,
                    StrokeLineJoin = PenLineJoin.Round,
                    IsHitTestVisible = false,
                };
                _canvas.Children.Add(cur);
            }
            cur.Points.Add(new Point(PlotX(elDeg), PlotY(pfd)));
            prevEl = elDeg;
        }
    }
}
