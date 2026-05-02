using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Renders the map: background rectangle, coastlines, graticule, horizon disc,
/// sub-satellite marker, beam crosses + (optionally) 3-dB cone footprints, and
/// (optionally) the composite-gain heatmap. Reads scene state from
/// <see cref="MainViewModel"/>; reads projection state from
/// <see cref="MapViewport"/>; writes to a <see cref="Canvas"/> by clearing
/// + re-adding children on every <see cref="Redraw"/>.
///
/// <see cref="IsInteracting"/> suppresses the heatmap (the slow part) during
/// pan/zoom/sat-drag so the canvas stays fluid; on release the controller
/// flips it back and calls <see cref="Redraw"/>.
/// </summary>
public sealed class MapRenderer
{
    private readonly Canvas _canvas;
    private readonly MapViewport _vp;
    private readonly MainViewModel _vm;
    private readonly SceneModel _scene;

    public bool IsInteracting { get; set; }

    private const double BeamMarkerRadiusPx = 2.5;
    private const int    HeatmapPixelW = 360, HeatmapPixelH = 180;
    // Graticule grid: lines every 30° in lat & lon. Lines exactly at ±90° lat
    // and ±180° lon are excluded (they coincide with the map rect / pole).
    private const double GraticuleStepDeg = 30.0;

    public MapRenderer(Canvas canvas, MapViewport viewport, MainViewModel vm)
    {
        _canvas = canvas;
        _vp = viewport;
        _vm = vm;
        _scene = vm.Scene;
    }

    public void Redraw()
    {
        _canvas.Children.Clear();
        if (!_vp.TryRecomputePlacement(_canvas.ActualWidth, _canvas.ActualHeight)) return;
        _canvas.Clip = new RectangleGeometry(new Rect(_vp.MapX, _vp.MapY, _vp.MapW, _vp.MapH));

        DrawMapBackground();
        if (_vm.HeatmapEnabled && !IsInteracting) DrawHeatmap();
        DrawCoastlines();
        DrawGraticule();
        DrawHorizonCircle();
        DrawSubSatMarker();
        DrawBeams();
    }

    // ----- Layers -----

    private void DrawMapBackground()
    {
        var bg = new Rectangle
        {
            Width = _vp.MapW,
            Height = _vp.MapH,
            Fill = new SolidColorBrush(Color.FromRgb(0x14, 0x1a, 0x22)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x3a, 0x40, 0x47)),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(bg, _vp.MapX);
        Canvas.SetTop(bg, _vp.MapY);
        _canvas.Children.Add(bg);
    }

    private void DrawCoastlines()
    {
        var stroke = new SolidColorBrush(Color.FromRgb(0x6b, 0x88, 0xa8));
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            foreach (var poly in _vm.Coastlines.Polylines)
                AddRingToContext(ctx, poly);
        }
        geom.Freeze();

        _canvas.Children.Add(new Path
        {
            Data = geom,
            Stroke = stroke,
            StrokeThickness = 0.5,
            IsHitTestVisible = false,
        });
    }

    private void DrawGraticule()
    {
        var stroke = new SolidColorBrush(Color.FromArgb(40, 0xff, 0xff, 0xff));
        for (double lat = -90.0 + GraticuleStepDeg; lat <= 90.0 - GraticuleStepDeg + 1e-6; lat += GraticuleStepDeg)
        {
            var (x1, y1) = _vp.ToCanvas(lat, -180);
            var (x2, y2) = _vp.ToCanvas(lat,  180);
            _canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = 0.5, IsHitTestVisible = false });
        }
        for (double lon = -180.0 + GraticuleStepDeg; lon <= 180.0 - GraticuleStepDeg + 1e-6; lon += GraticuleStepDeg)
        {
            var (x1, y1) = _vp.ToCanvas(-90, lon);
            var (x2, y2) = _vp.ToCanvas( 90, lon);
            _canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = 0.5, IsHitTestVisible = false });
        }
    }

    private void DrawHorizonCircle()
    {
        // The visible-disc boundary on the equirectangular map is a transcendental
        // curve; sample 360 points at fixed Earth-central distance from sub-sat.
        double alpha = HorizonHalfAngleDeg(_scene.AltitudeKm);
        var pts = SampleSphericalCircle(_scene.SubSatLatDeg, _scene.SubSatLonDeg, alpha, 360);
        var stroke = new SolidColorBrush(Color.FromArgb(180, 0xff, 0xc8, 0x66));
        AddSplitPolyline(pts, stroke, 1.2);
    }

    private void DrawSubSatMarker()
    {
        var (x, y) = _vp.ToCanvas(_scene.SubSatLatDeg, _scene.SubSatLonDeg);
        var fill = new SolidColorBrush(Color.FromRgb(0xff, 0xc8, 0x66));
        var dot = new Ellipse
        {
            Width = 10, Height = 10, Fill = fill,
            Stroke = Brushes.Black, StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, x - 5);
        Canvas.SetTop(dot, y - 5);
        _canvas.Children.Add(dot);
        _canvas.Children.Add(new Line { X1 = x - 14, Y1 = y, X2 = x + 14, Y2 = y, Stroke = fill, StrokeThickness = 0.7, IsHitTestVisible = false });
        _canvas.Children.Add(new Line { X1 = x, Y1 = y - 14, X2 = x, Y2 = y + 14, Stroke = fill, StrokeThickness = 0.7, IsHitTestVisible = false });
    }

    private void DrawBeams()
    {
        foreach (var beam in _scene.Beams)
        {
            var fp = _scene.GroundFootprint(beam);
            if (fp is null) continue;
            var (lat, lon) = fp.Value;
            var (x, y) = _vp.ToCanvas(lat, lon);

            // Beam status drives marker / contour colour:
            //   green = ON full G_m, amber = ON adjusted, red = OFF.
            string status;
            Color rgb;
            if (beam.Weight <= 0)        { status = "OFF";      rgb = Color.FromRgb(0xd6, 0x4c, 0x4c); }
            else if (beam.IsGmAdjusted)  { status = "ADJUSTED"; rgb = Color.FromRgb(0xe6, 0xb0, 0x44); }
            else                         { status = "ON";       rgb = Color.FromRgb(0x4c, 0xc6, 0x76); }
            var armBrush = new SolidColorBrush(Color.FromArgb(0xd0, rgb.R, rgb.G, rgb.B));

            double r = BeamMarkerRadiusPx;
            _canvas.Children.Add(new Line { X1 = x - r, Y1 = y, X2 = x + r, Y2 = y, Stroke = armBrush, StrokeThickness = 0.9, IsHitTestVisible = false });
            _canvas.Children.Add(new Line { X1 = x, Y1 = y - r, X2 = x, Y2 = y + r, Stroke = armBrush, StrokeThickness = 0.9, IsHitTestVisible = false });

            // Invisible click target slightly larger than the cross.
            double hr = r + 2.0;
            var hit = new Ellipse
            {
                Width = hr * 2, Height = hr * 2,
                Fill = Brushes.Transparent,
                Tag = beam,
                Cursor = Cursors.Hand,
                ToolTip = $"{beam.Name}  off-nadir {beam.OffNadirDeg:F1}°  status: {status}\n" +
                          $"Gm: original {beam.OriginalGmDbi:F1} → current {beam.Pattern.Gm:F1} dBi  (Δ {beam.Pattern.Gm - beam.OriginalGmDbi:+0.0;-0.0;0.0} dB)\n" +
                          $"weight = {beam.Weight:F2}   footprint ≈ ({lat:F2}, {lon:F2})",
            };
            Canvas.SetLeft(hit, x - hr);
            Canvas.SetTop(hit, y - hr);
            hit.MouseLeftButtonDown += BeamMarker_Click;
            _canvas.Children.Add(hit);

            if (_vm.FootprintsEnabled)
            {
                var stroke = new SolidColorBrush(Color.FromArgb(110, rgb.R, rgb.G, rgb.B));
                // Elliptical patterns: half-angle θ(φ) = asin(sin(θb_rad)·sin(θb_tr)/√…)
                // — equivalent to sinθ(φ) = u_half·λ / √((Lr·cosφ)² + (Lt·sinφ)²),
                // i.e. ellipse on the unit-sphere boresight cone.
                Func<double, double> halfAngleAt;
                if (beam.Pattern is Rec1528_1p4_Ell ell)
                {
                    double sinR = Math.Sin(ell.ThetaB * Math.PI / 180.0);
                    double sinT = Math.Sin(ell.ThetaBTransverseDeg * Math.PI / 180.0);
                    halfAngleAt = phiDeg =>
                    {
                        double cp = Math.Cos(phiDeg * Math.PI / 180.0);
                        double sp = Math.Sin(phiDeg * Math.PI / 180.0);
                        // 1/sin²θ = cos²φ/sin²θ_r + sin²φ/sin²θ_t  (ellipse in sinθ-space)
                        double inv = (cp * cp) / (sinR * sinR) + (sp * sp) / (sinT * sinT);
                        double sinTheta = 1.0 / Math.Sqrt(inv);
                        return Math.Asin(Math.Min(1.0, sinTheta)) * 180.0 / Math.PI;
                    };
                }
                else
                {
                    // Use the *beam's* pattern θ_b — in auto-mode this is auto-derived
                    // per-beam from CellRadiusKm + slant range, so contours match the
                    // intended ground cell. Outside auto-mode it equals the user's input.
                    double thetaB = beam.Pattern.ThetaB;
                    halfAngleAt = _ => thetaB;
                }
                foreach (var seg in SampleBeamConeOnGround(beam, halfAngleAt, samples: 64))
                {
                    if (seg.Count < 3) continue;
                    AddSplitPolyline(seg, stroke, 0.5);
                }
            }
        }
    }

    private void BeamMarker_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Ellipse el && el.Tag is Beam beam)
        {
            _vm.ToggleBeam(beam);
            e.Handled = true;
        }
    }

    private void DrawHeatmap()
    {
        var bmp = new WriteableBitmap(HeatmapPixelW, HeatmapPixelH, 96, 96, PixelFormats.Pbgra32, null);
        bmp.Lock();
        unsafe
        {
            byte* basePtr = (byte*)bmp.BackBuffer;
            int stride = bmp.BackBufferStride;
            double floor = _vm.FloorDbi;
            double peak = _scene.GmDbi;
            int W = HeatmapPixelW, H = HeatmapPixelH;

            // Each row is independent: composite-gain queries are read-only on
            // SceneModel + per-beam state. ~65 k pixels × ~120 beams; parallel
            // over rows brings the per-frame cost down ~3-4× on a quad-core.
            byte* basePtrLocal = basePtr;
            int strideLocal = stride;
            Parallel.For(0, H, j =>
            {
                double lat = 90.0 - (j + 0.5) / H * 180.0;
                byte* row = basePtrLocal + j * strideLocal;
                for (int i = 0; i < W; i++)
                {
                    double lon = (i + 0.5) / W * 360.0 - 180.0;
                    double g = _scene.GainTowardsGround(lat, lon);
                    if (double.IsNegativeInfinity(g) || double.IsNaN(g))
                    {
                        row[i * 4 + 0] = 0; row[i * 4 + 1] = 0; row[i * 4 + 2] = 0; row[i * 4 + 3] = 0;
                        continue;
                    }
                    double t = (g - floor) / Math.Max(1e-6, peak - floor);
                    if (t < 0) t = 0; else if (t > 1) t = 1;

                    GainColor(t, out byte r, out byte gg, out byte b);
                    byte alpha = (byte)(160 + 95 * t);
                    row[i * 4 + 0] = (byte)(b * alpha / 255);
                    row[i * 4 + 1] = (byte)(gg * alpha / 255);
                    row[i * 4 + 2] = (byte)(r * alpha / 255);
                    row[i * 4 + 3] = alpha;
                }
            });
        }
        bmp.AddDirtyRect(new Int32Rect(0, 0, HeatmapPixelW, HeatmapPixelH));
        bmp.Unlock();

        // Place the full-Earth bitmap in viewport coordinates: lon=±180 maps to
        // two canvas-x values that may be far outside the map rect when zoomed.
        // The Canvas.Clip set in Redraw crops the image to the visible region.
        double lonRange = _vp.ViewLonMax - _vp.ViewLonMin;
        double latRange = _vp.ViewLatMax - _vp.ViewLatMin;
        double imgLeft   = _vp.MapX + (-180.0 - _vp.ViewLonMin) / lonRange * _vp.MapW;
        double imgRight  = _vp.MapX + ( 180.0 - _vp.ViewLonMin) / lonRange * _vp.MapW;
        double imgTop    = _vp.MapY + (_vp.ViewLatMax -   90.0) / latRange * _vp.MapH;
        double imgBottom = _vp.MapY + (_vp.ViewLatMax - (-90.0)) / latRange * _vp.MapH;

        var img = new Image
        {
            Source = bmp,
            Width  = Math.Max(1, imgRight - imgLeft),
            Height = Math.Max(1, imgBottom - imgTop),
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Linear);
        Canvas.SetLeft(img, imgLeft);
        Canvas.SetTop(img, imgTop);
        _canvas.Children.Add(img);
    }

    // ----- Helpers (geometry + drawing primitives) -----

    private void AddRingToContext(StreamGeometryContext ctx, IReadOnlyList<(double lat, double lon)> poly)
    {
        if (poly.Count < 2) return;

        // Split a ring whenever the projected segment exceeds half the map
        // width. Catches both real antimeridian crossings AND parasitic
        // edges produced by polygon-clipping artifacts in source GeoJSON
        // (e.g. polygons that span the antimeridian via one long edge).
        // Threshold scales with zoom: at full earth view it's mapW/2 = 180°
        // of lon; at higher zoom it's tighter, but normal coastline edges
        // are < 1° lon so they always pass.
        double maxSegPx = _vp.MapW * 0.5;

        var first = _vp.ToCanvas(poly[0].lat, poly[0].lon);
        var prev = first;
        ctx.BeginFigure(new Point(first.x, first.y), isFilled: false, isClosed: false);
        var batch = new List<Point>(poly.Count);

        for (int i = 1; i < poly.Count; i++)
        {
            var p = _vp.ToCanvas(poly[i].lat, poly[i].lon);
            double dx = p.x - prev.x;
            double dy = p.y - prev.y;
            if (Math.Abs(dx) > maxSegPx || Math.Abs(dy) > maxSegPx)
            {
                if (batch.Count > 0)
                {
                    ctx.PolyLineTo(batch, isStroked: true, isSmoothJoin: false);
                    batch = new List<Point>(poly.Count - i);
                }
                ctx.BeginFigure(new Point(p.x, p.y), isFilled: false, isClosed: false);
            }
            else
            {
                batch.Add(new Point(p.x, p.y));
            }
            prev = p;
        }
        if (batch.Count > 0)
            ctx.PolyLineTo(batch, isStroked: true, isSmoothJoin: false);
    }

    /// <summary>Sample N points on a small circle of given Earth-central radius around (centreLat, centreLon).</summary>
    private static List<(double lat, double lon)> SampleSphericalCircle(double centreLat, double centreLon, double radiusCentralDeg, int n)
    {
        var res = new List<(double, double)>(n + 1);
        double cLat = centreLat * Math.PI / 180.0;
        double cLon = centreLon * Math.PI / 180.0;
        double r = radiusCentralDeg * Math.PI / 180.0;
        double sinR = Math.Sin(r), cosR = Math.Cos(r);
        for (int i = 0; i <= n; i++)
        {
            double brg = (2.0 * Math.PI) * i / n;
            double sinLat = Math.Sin(cLat) * cosR + Math.Cos(cLat) * sinR * Math.Cos(brg);
            double lat2 = Math.Asin(Math.Clamp(sinLat, -1, 1));
            double lon2 = cLon + Math.Atan2(
                Math.Sin(brg) * sinR * Math.Cos(cLat),
                cosR - Math.Sin(cLat) * Math.Sin(lat2));
            res.Add((lat2 * 180.0 / Math.PI, ((lon2 * 180.0 / Math.PI + 540.0) % 360.0) - 180.0));
        }
        return res;
    }

    private void AddSplitPolyline(List<(double lat, double lon)> pts, Brush stroke, double thickness)
    {
        // Same canvas-space split as AddRingToContext: a single segment
        // longer than half the map width is treated as a wrap-around and
        // breaks the polyline rather than spanning the whole canvas.
        double maxSegPx = _vp.MapW * 0.5;

        Polyline? cur = null;
        Point? prev = null;
        foreach (var (lat, lon) in pts)
        {
            var (x, y) = _vp.ToCanvas(lat, lon);
            var p = new Point(x, y);
            if (prev is Point q)
            {
                double dx = p.X - q.X, dy = p.Y - q.Y;
                if (Math.Abs(dx) > maxSegPx || Math.Abs(dy) > maxSegPx) cur = null;
            }
            if (cur is null)
            {
                cur = new Polyline { Stroke = stroke, StrokeThickness = thickness, IsHitTestVisible = false };
                _canvas.Children.Add(cur);
            }
            cur.Points.Add(p);
            prev = p;
        }
    }

    /// <summary>
    /// Sample the beam's 3-dB cone (half-angle θ_b around its boresight) in 3D
    /// and project each sample to ground via ray-Earth intersection. Returns
    /// connected segments — when the cone partially overshoots the horizon,
    /// missed samples split the contour into open arcs (no spurious chord).
    /// </summary>
    private List<List<(double lat, double lon)>> SampleBeamConeOnGround(Beam beam, Func<double, double> halfAngleAtPhiDeg, int samples)
    {
        var segments = new List<List<(double lat, double lon)>>();
        var sat = _scene.SatEcef;
        var b = beam.Boresight;

        // φ = 0 axis: use the beam's radial axis when defined (elliptical patterns),
        // otherwise pick any axis ⊥ boresight. e2 is the transverse axis (Lt direction).
        Vec3 e1;
        if (beam.RadialAxisEcef is Vec3 r)
        {
            e1 = r;
        }
        else
        {
            var ref0 = (Math.Abs(b.Z) < 0.9) ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
            var c = Vec3.Cross(b, ref0);
            double clen = Math.Sqrt(Vec3.Dot(c, c));
            if (clen < 1e-9) return segments;
            e1 = new Vec3(c.X / clen, c.Y / clen, c.Z / clen);
        }
        var e2 = Vec3.Cross(b, e1);

        var hits = new (double lat, double lon)?[samples];
        for (int i = 0; i < samples; i++)
        {
            double phiDeg = 360.0 * i / samples;
            double phi = phiDeg * Math.PI / 180.0;
            double halfAngleDeg = halfAngleAtPhiDeg(phiDeg);
            double cs = Math.Cos(halfAngleDeg * Math.PI / 180.0);
            double sn = Math.Sin(halfAngleDeg * Math.PI / 180.0);
            double cp = Math.Cos(phi), sp = Math.Sin(phi);
            var d = new Vec3(
                b.X * cs + e1.X * sn * cp + e2.X * sn * sp,
                b.Y * cs + e1.Y * sn * cp + e2.Y * sn * sp,
                b.Z * cs + e1.Z * sn * cp + e2.Z * sn * sp);
            var hit = RaySphereHit(sat, d);
            if (hit is null) hits[i] = null;
            else
            {
                var (lat, lon, _) = EcefToGeodetic(hit.Value);
                hits[i] = (lat, lon);
            }
        }

        bool anyMiss = false;
        foreach (var h in hits) if (h is null) { anyMiss = true; break; }
        if (!anyMiss)
        {
            var seg = new List<(double, double)>(samples + 1);
            for (int i = 0; i < samples; i++) seg.Add(hits[i]!.Value);
            seg.Add(hits[0]!.Value);
            segments.Add(seg);
            return segments;
        }

        int firstMiss = -1;
        for (int k = 0; k < samples; k++) if (hits[k] is null) { firstMiss = k; break; }
        int start = (firstMiss + 1) % samples;

        List<(double, double)>? cur = null;
        for (int k = 0; k < samples; k++)
        {
            int idx = (start + k) % samples;
            var h = hits[idx];
            if (h is null) { cur = null; continue; }
            if (cur is null) { cur = new List<(double, double)>(); segments.Add(cur); }
            cur.Add(h.Value);
        }
        return segments;
    }

    /// <summary>Viridis-ish colour ramp: dark blue → teal → green → yellow → white as t: 0 → 1.</summary>
    private static void GainColor(double t, out byte r, out byte g, out byte b)
    {
        // Anchor stops.
        var stops = new (double t, double r, double g, double b)[]
        {
            (0.00, 0.10, 0.05, 0.30),
            (0.25, 0.10, 0.45, 0.65),
            (0.50, 0.20, 0.75, 0.45),
            (0.75, 0.95, 0.85, 0.20),
            (1.00, 1.00, 1.00, 0.95),
        };
        for (int i = 0; i < stops.Length - 1; i++)
        {
            if (t <= stops[i + 1].t)
            {
                double f = (t - stops[i].t) / (stops[i + 1].t - stops[i].t);
                double rr = stops[i].r + f * (stops[i + 1].r - stops[i].r);
                double gg = stops[i].g + f * (stops[i + 1].g - stops[i].g);
                double bb = stops[i].b + f * (stops[i + 1].b - stops[i].b);
                r = (byte)Math.Clamp(rr * 255, 0, 255);
                g = (byte)Math.Clamp(gg * 255, 0, 255);
                b = (byte)Math.Clamp(bb * 255, 0, 255);
                return;
            }
        }
        r = g = b = 255;
    }
}
