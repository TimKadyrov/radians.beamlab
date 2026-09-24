using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace radians.beamlab.app;

/// <summary>
/// UserControl hosting the Orbit Design tab: repeat-solution grid, the
/// three SNS case previews, and the propagated one-cycle ground track over
/// the coastline map. Drawing only -- all computation lives in
/// <see cref="OrbitDesignViewModel"/>, and the buttons are commands of
/// <see cref="OrbitDesignDocumentViewModel"/>; the view supplies the
/// dialogs, the clipboard and the builder window.
/// </summary>
public partial class OrbitDesignView : UserControl
{
    private readonly OrbitDesignDocumentViewModel _doc = new();
    private OrbitDesignViewModel _vm;   // the selected shell, rewired on switch
    private CoastlineDataProvider? _coastlines;

    public OrbitDesignView()
    {
        InitializeComponent();
        _vm = _doc.SelectedShell;
        _doc.PickOpenFile = ViewServices.PickOpenFile;
        _doc.PickSaveFile = ViewServices.PickSaveFile;
        _doc.OpenDocument = ViewServices.OpenDocument;
        _doc.SetClipboardText = Clipboard.SetText;
        _doc.OpenSnsBuilderRequested += () => new SnsBuilderWindow { Owner = Window.GetWindow(this) }.Show();
        DataContext = _doc;
        WireToolTips();
        Loaded += (_, _) =>
        {
            _coastlines ??= new CoastlineDataProvider();
            Redraw();
        };
        SizeChanged += (_, _) => Redraw();
        _vm.TrackChanged += OnTrackChanged;
        _doc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OrbitDesignDocumentViewModel.OverlaySegments))
            {
                Redraw();
                return;
            }
            if (e.PropertyName != nameof(OrbitDesignDocumentViewModel.SelectedShell)) return;
            _vm.TrackChanged -= OnTrackChanged;
            _vm = _doc.SelectedShell;
            _vm.TrackChanged += OnTrackChanged;
            Redraw();
        };
    }

    private void OnTrackChanged() => Dispatcher.Invoke(Redraw);

    /// <summary>
    /// Parameter help: filing parameters read the shared ParameterCatalog
    /// (the card deck's twin), tool inputs get authored explanations.
    /// </summary>
    private void WireToolTips()
    {
        static string? Cat(string name) => radians.beamlab.ParameterCatalog.Find(name)?.ToolTipText;
        AltBox.ToolTip = "Mean altitude of the target orbit (km). Every case starts from it; the solver lists repeating-track candidates near it.";
        IncBox.ToolTip = "Orbit inclination (deg). Sets the westward step per lap and the reachable latitudes.";
        EccBox.ToolTip = "Orbit eccentricity (0 = circular). Elliptical designs also declare the argument of perigee and an operating height.";
        MaxOrbBox.ToolTip = "Longest repeat cycle the solver searches, in orbits per cycle (k). Larger values find finer track grids at the cost of longer cycles.";
        BandBox.ToolTip = "How far above and below the target altitude the solver may move (km) to close a cycle exactly. Candidates outside the band are skipped.";
        CheckOrbitsBox.ToolTip = "Whole nodal orbits (k) of a repeat you already have in mind. With m: the track repeats after k orbits in m node-relative Earth turns; a non-coprime pair reduces to the true cycle.";
        CheckDaysBox.ToolTip = "Whole nodal days (m) of the repeat to validate. The exact closing altitude is solved anywhere in 100-30000 km and flagged when it falls outside the search band.";
        PrecessBox.ToolTip = "Admin-supplied nodal precession rate (deg/s) for Case 3, station keeping with a supplied rate. Empty declares the rate that closes the selected repeat at the target altitude. Filed as f_precess='Y' and the rate's magnitude in degrees/day; the direction is the one the inclination implies (west for prograde, east for retrograde), so a rate turning the other way cannot be filed, nor a typed rate that misses the repeat by more than keep_rnge per cycle.";
        FixedAltRadio.ToolTip = "The Case-2 filing keeps your target altitude and declares rpt_prd as k laps of your own orbit. The EPFD calculation flies the filed orbit on its J2 rates plus the keep_rnge sweep (Rec. S.1503-4 eq (49)), so its track drifts by drift@target every cycle of the run, although the real station keeping holds it.";
        AdjustAltRadio.ToolTip = "The default: the filing adopts the selected candidate's exact closing altitude, where the filed orbit repeats by itself, so the track the EPFD calculation flies is the declared repeat plus the keep_rnge sweep.";
        BwBox.ToolTip = "Victim 3 dB beamwidth (deg). When set, NOrbits is derived from the run rules (eq (3), N_tracks = 16) at the target altitude; leave empty to set NOrbits by hand.";
        NOrbitsBox.ToolTip = Cat("NOrbits") ?? "Case-1 run length in equatorial passes.";
        KeepBox.ToolTip = Cat("StationKeeping · WDeltaDeg · RepeatPeriod")
            ?? "Longitude deadband half-width the station keeping holds (deg).";
        KeepSolverBox.ToolTip = KeepBox.ToolTip;
        WalkerFBox.ToolTip = "Walker phasing parameter F: satellites in adjacent planes are offset by F x 360 / (planes x sats per plane) degrees.";
        LanSpreadBox.ToolTip = "Longitude span the planes divide: 360 = Walker delta, 180 = Walker star.";
        OpHeightBox.ToolTip = Cat("Eccentricity · ArgumentOfPerigee · OperatingHeightKm")
            ?? "Minimum operating height (km); empty = the perigee altitude.";
    }

    private void Redraw()
    {
        double w = TrackCanvas.ActualWidth, h = TrackCanvas.ActualHeight;
        TrackCanvas.Children.Clear();
        if (w < 40 || h < 40 || _coastlines is null) return;

        double X(double lonDeg) => (lonDeg + 180.0) / 360.0 * w;
        double Y(double latDeg) => (90.0 - latDeg) / 180.0 * h;

        // graticule
        var gridBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xEB, 0xF5));
        for (int lon = -150; lon <= 150; lon += 30)
            TrackCanvas.Children.Add(new Line
            { X1 = X(lon), Y1 = 0, X2 = X(lon), Y2 = h, Stroke = gridBrush, StrokeThickness = 1 });
        for (int lat = -60; lat <= 60; lat += 30)
            TrackCanvas.Children.Add(new Line
            { X1 = 0, Y1 = Y(lat), X2 = w, Y2 = Y(lat), Stroke = gridBrush, StrokeThickness = 1 });

        // coastlines
        var coastBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0xC4, 0xCD));
        foreach (var line in _coastlines.Polylines)
        {
            var pl = new Polyline { Stroke = coastBrush, StrokeThickness = 1 };
            double? prevLon = null;
            foreach (var (lat, lon) in line)
            {
                if (prevLon is double pv && Math.Abs(lon - pv) > 180.0)
                {
                    if (pl.Points.Count > 1) TrackCanvas.Children.Add(pl);
                    pl = new Polyline { Stroke = coastBrush, StrokeThickness = 1 };
                }
                pl.Points.Add(new Point(X(lon), Y(lat)));
                prevLon = lon;
            }
            if (pl.Points.Count > 1) TrackCanvas.Children.Add(pl);
        }

        // ground track: one satellite's declared cycle, or the document's
        // whole-constellation overlay (thinner lines for the union grid)
        var trackBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x76, 0xA1));
        bool shellMode = _doc.ShowConstellationTrack;
        foreach (var seg in shellMode ? _doc.OverlaySegments : _vm.TrackSegments)
        {
            var pl = new Polyline
            {
                Stroke = trackBrush,
                StrokeThickness = shellMode ? 1.0 : 1.4,
                Opacity = shellMode ? 0.55 : 0.9,
            };
            foreach (var (lat, lon) in seg)
                pl.Points.Add(new Point(X(lon), Y(lat)));
            if (pl.Points.Count > 1) TrackCanvas.Children.Add(pl);
        }

        // start / end markers: a filled dot at the start, a ring at the end --
        // coincident when the cycle closes. Solo mode only; among a whole
        // shell's tracks they are noise.
        if (!shellMode && _vm.TrackSegments.Count > 0)
        {
            var first = _vm.TrackSegments[0][0];
            var lastSeg = _vm.TrackSegments[^1];
            var last = lastSeg[lastSeg.Count - 1];
            var dot = new Ellipse { Width = 8, Height = 8, Fill = trackBrush };
            Canvas.SetLeft(dot, X(first.LonDeg) - 4); Canvas.SetTop(dot, Y(first.LatDeg) - 4);
            var ring = new Ellipse
            {
                Width = 14, Height = 14,
                Stroke = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x7B)), StrokeThickness = 2
            };
            Canvas.SetLeft(ring, X(last.LonDeg) - 7); Canvas.SetTop(ring, Y(last.LatDeg) - 7);
            TrackCanvas.Children.Add(dot);
            TrackCanvas.Children.Add(ring);
        }
    }
}
