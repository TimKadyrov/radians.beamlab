using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Equirectangular map for the PFD-mask tab: coastlines, graticule, horizon
/// disc, sub-satellite marker, and one small marker per beam. Markers and
/// footprint rings are coloured by on/off status (green/red) -- or, when
/// co-channel aggregation is selected, ON beams are painted by their N-colour
/// frequency-reuse assignment so the plan is visible. Pan / zoom via the
/// shared <see cref="MapViewport"/> (input wired by
/// <see cref="PfdMapInteractionHandler"/>); unlike tab 1's
/// <see cref="MapRenderer"/> there is no probe, beam toggle or satellite drag
/// -- the PFD map stays a read-only situational picture.
/// </summary>
public sealed class PfdMapRenderer
{
    private readonly Canvas _canvas;
    private readonly MapViewport _vp;
    private readonly PfdMaskViewModel _vm;
    private readonly CoastlineDataProvider _coasts;

    private const double GraticuleStepDeg = 30.0;
    private const double BeamMarkerRadiusPx = 2.5;

    // Distinguishable palette for frequency-reuse colours 0..6 (red is reserved
    // for OFF beams). Index = BeamComposer.HexReuseColor(i, j, N).
    private static readonly Color[] ReusePalette =
    {
        Color.FromRgb(0x4c, 0xc6, 0x76),   // green
        Color.FromRgb(0x4c, 0x8a, 0xd6),   // blue
        Color.FromRgb(0xe6, 0xc8, 0x44),   // yellow
        Color.FromRgb(0xb0, 0x6c, 0xd6),   // violet
        Color.FromRgb(0x4c, 0xc6, 0xc6),   // cyan
        Color.FromRgb(0xe6, 0x8a, 0x44),   // orange
        Color.FromRgb(0xd8, 0xd8, 0xd8),   // light grey
    };

    public PfdMapRenderer(Canvas canvas, MapViewport viewport, PfdMaskViewModel vm, CoastlineDataProvider coasts)
    {
        _canvas = canvas;
        _vp = viewport;
        _vm = vm;
        _coasts = coasts;
    }

    public void Redraw()
    {
        _canvas.Children.Clear();
        if (!_vp.TryRecomputePlacement(_canvas.ActualWidth, _canvas.ActualHeight)) return;
        _canvas.Clip = new RectangleGeometry(new Rect(_vp.MapX, _vp.MapY, _vp.MapW, _vp.MapH));

        double mapW = _vp.MapW;
        Func<double, double, (double x, double y)> toCanvas = _vp.ToCanvas;

        DrawBackground(_vp.MapX, _vp.MapY, mapW, _vp.MapH);
        DrawCoastlines(mapW, toCanvas);
        DrawGraticule(mapW, toCanvas);
        DrawHorizon(mapW, toCanvas);
        DrawSubSat(toCanvas);
        DrawBeams(mapW, toCanvas);
    }

    private void DrawBackground(double x, double y, double w, double h)
    {
        var bg = new Rectangle
        {
            Width = w, Height = h,
            Fill = new SolidColorBrush(Color.FromRgb(0x14, 0x1a, 0x22)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x3a, 0x40, 0x47)),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(bg, x);
        Canvas.SetTop(bg, y);
        _canvas.Children.Add(bg);
    }

    private void DrawCoastlines(double mapW, Func<double, double, (double x, double y)> toCanvas)
    {
        var stroke = new SolidColorBrush(Color.FromRgb(0x6b, 0x88, 0xa8));
        double maxSegPx = mapW * 0.5;
        foreach (var ring in _coasts.Polylines)
        {
            if (ring.Count < 2) continue;
            MapDraw.AddSplitPolyline(_canvas, ring, stroke, 0.5, maxSegPx, toCanvas);
        }
    }

    private void DrawGraticule(double mapW, Func<double, double, (double x, double y)> toCanvas)
    {
        var stroke = new SolidColorBrush(Color.FromArgb(40, 0xff, 0xff, 0xff));
        for (double lat = -90.0 + GraticuleStepDeg; lat <= 90.0 - GraticuleStepDeg + 1e-6; lat += GraticuleStepDeg)
        {
            var (x1, y1) = toCanvas(lat, -180);
            var (x2, y2) = toCanvas(lat,  180);
            _canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = 0.5, IsHitTestVisible = false });
        }
        for (double lon = -180.0 + GraticuleStepDeg; lon <= 180.0 - GraticuleStepDeg + 1e-6; lon += GraticuleStepDeg)
        {
            var (x1, y1) = toCanvas(-90, lon);
            var (x2, y2) = toCanvas( 90, lon);
            _canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = 0.5, IsHitTestVisible = false });
        }
    }

    private void DrawHorizon(double mapW, Func<double, double, (double x, double y)> toCanvas)
    {
        double alpha = HorizonHalfAngleDeg(_vm.Scene.AltitudeKm);
        var pts = GeoMath.SampleSmallCircle(_vm.Scene.SubSatLatDeg, _vm.Scene.SubSatLonDeg, alpha, 240);
        var stroke = new SolidColorBrush(Color.FromArgb(180, 0xff, 0xc8, 0x66));
        MapDraw.AddSplitPolyline(_canvas, pts, stroke, 1.0, mapW * 0.5, toCanvas);
    }

    private void DrawSubSat(Func<double, double, (double x, double y)> toCanvas)
    {
        var (x, y) = toCanvas(_vm.Scene.SubSatLatDeg, _vm.Scene.SubSatLonDeg);
        var fill = new SolidColorBrush(Color.FromRgb(0xff, 0xc8, 0x66));
        var dot = new Ellipse
        {
            Width = 8, Height = 8, Fill = fill,
            Stroke = Brushes.Black, StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, x - 4);
        Canvas.SetTop(dot, y - 4);
        _canvas.Children.Add(dot);
    }

    private void DrawBeams(double mapW, Func<double, double, (double x, double y)> toCanvas)
    {
        double maxSegPx = mapW * 0.5;
        var sat = _vm.Scene.SatEcef;

        // In co-channel mode, paint ON beams by their reuse colour so the
        // frequency plan is visible; OFF beams stay red in every mode.
        bool showReuse = _vm.Aggregation == PfdAggregation.CoChannelSum;
        int n = _vm.ReuseClusterSize;

        foreach (var beam in _vm.Scene.Beams)
        {
            var fp = _vm.Scene.GroundFootprint(beam);
            if (fp is null) continue;
            var (lat, lon) = fp.Value;
            var (x, y) = toCanvas(lat, lon);

            Color c;
            if (beam.Weight <= 0)
            {
                c = Color.FromRgb(0xd6, 0x4c, 0x4c);
            }
            else if (showReuse)
            {
                int colour = beam.LatticeI is int li && beam.LatticeJ is int lj
                    ? BeamComposer.HexReuseColor(li, lj, n)
                    : 0;
                c = ReusePalette[colour % ReusePalette.Length];
            }
            else
            {
                c = Color.FromRgb(0x4c, 0xc6, 0x76);
            }
            var armBrush = new SolidColorBrush(Color.FromArgb(0xd0, c.R, c.G, c.B));
            double r = BeamMarkerRadiusPx;
            _canvas.Children.Add(new Line { X1 = x - r, Y1 = y, X2 = x + r, Y2 = y, Stroke = armBrush, StrokeThickness = 0.9, IsHitTestVisible = false });
            _canvas.Children.Add(new Line { X1 = x, Y1 = y - r, X2 = x, Y2 = y + r, Stroke = armBrush, StrokeThickness = 0.9, IsHitTestVisible = false });

            if (!_vm.FootprintsEnabled) continue;

            var ringBrush = new SolidColorBrush(Color.FromArgb(110, c.R, c.G, c.B));
            Func<double, double> halfAngleAt;
            if (beam.Pattern is Rec1528_1p4_Ell ell)
            {
                double sinR = Math.Sin(ell.ThetaB * Math.PI / 180.0);
                double sinT = Math.Sin(ell.ThetaBTransverseDeg * Math.PI / 180.0);
                halfAngleAt = phiDeg =>
                {
                    double cp = Math.Cos(phiDeg * Math.PI / 180.0);
                    double sp = Math.Sin(phiDeg * Math.PI / 180.0);
                    double inv = (cp * cp) / (sinR * sinR) + (sp * sp) / (sinT * sinT);
                    double sinTheta = 1.0 / Math.Sqrt(inv);
                    return Math.Asin(Math.Min(1.0, sinTheta)) * 180.0 / Math.PI;
                };
            }
            else
            {
                double thetaB = beam.Pattern.ThetaB;
                halfAngleAt = _ => thetaB;
            }

            foreach (var seg in BeamFootprint.SampleConeOnGround(sat, beam.Boresight, beam.RadialAxisEcef, halfAngleAt, 64))
            {
                if (seg.Count < 3) continue;
                MapDraw.AddSplitPolyline(_canvas, seg, ringBrush, 0.5, maxSegPx, toCanvas);
            }
        }
    }

}
