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
/// <see cref="OrbitDesignViewModel"/>.
/// </summary>
public partial class OrbitDesignView : UserControl
{
    private readonly OrbitDesignViewModel _vm = new();
    private CoastlineDataProvider? _coastlines;

    private readonly string? _casesGuidePath;

    public OrbitDesignView()
    {
        InitializeComponent();
        DataContext = _vm;
        string? docs = HomeViewModel.FindDocsDir(System.AppContext.BaseDirectory);
        string? guide = docs is null ? null : System.IO.Path.Combine(docs, "orbit-design-cases.html");
        _casesGuidePath = guide is not null && System.IO.File.Exists(guide) ? guide : null;
        CasesGuideButton.IsEnabled = _casesGuidePath is not null;
        WireToolTips();
        Loaded += (_, _) =>
        {
            _coastlines ??= new CoastlineDataProvider();
            Redraw();
        };
        SizeChanged += (_, _) => Redraw();
        _vm.TrackChanged += () => Dispatcher.Invoke(Redraw);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        string text = _vm.BuildCopyText();
        if (text.Length > 0) Clipboard.SetText(text);
    }

    private void OnCasesGuideClick(object sender, RoutedEventArgs e)
    {
        if (_casesGuidePath is null) return;
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(_casesGuidePath) { UseShellExecute = true });
    }

    /// <summary>
    /// Parameter help: filing parameters read the shared ParameterCatalog
    /// (the card deck's twin), tool inputs get authored explanations.
    /// </summary>
    private void WireToolTips()
    {
        static string? Cat(string name) => radians.beamlab.ParameterCatalog.Find(name)?.ToolTipText;
        AltBox.ToolTip = "Mean altitude of the target orbit (km). The solver lists repeating-track candidates near it.";
        IncBox.ToolTip = "Orbit inclination (deg). Sets the westward step per lap and the reachable latitudes.";
        EccBox.ToolTip = "Orbit eccentricity (0 = circular). Elliptical designs also declare the argument of perigee and an operating height.";
        MaxOrbBox.ToolTip = "Longest repeat cycle the solver searches, in orbits per cycle (k). Larger values find finer track grids at the cost of longer cycles.";
        BandBox.ToolTip = "How far above and below the target altitude the solver may move (km) to close a cycle exactly. Candidates outside the band are skipped.";
        BwBox.ToolTip = "Victim 3 dB beamwidth (deg). When set, NOrbits is derived from the run rules (eq (3), N_tracks = 16) at the selected candidate's altitude; leave empty to set NOrbits by hand.";
        NOrbitsBox.ToolTip = Cat("NOrbits") ?? "Case-1 run length in equatorial passes.";
        KeepBox.ToolTip = Cat("StationKeeping · WDeltaDeg · RepeatPeriod")
            ?? "Longitude deadband half-width the station keeping holds (deg).";
        WalkerFBox.ToolTip = "Walker phasing parameter F: satellites in adjacent planes are offset by F x 360 / (planes x sats per plane) degrees.";
        LanSpreadBox.ToolTip = "Longitude span the planes divide: 360 = Walker delta, 180 = Walker star.";
        OpHeightBox.ToolTip = Cat("Eccentricity · ArgumentOfPerigee · OperatingHeightKm")
            ?? "Minimum operating height (km); empty = the perigee altitude.";
    }

    private void OnSaveDesignClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Orbit design (*.orbitdesign.json)|*.orbitdesign.json",
            FileName = _vm.SatNameText + ".orbitdesign.json",
        };
        if (dlg.ShowDialog() != true) return;
        System.IO.File.WriteAllText(dlg.FileName, _vm.BuildDesignJson());
        _vm.SnsStatusText = "design saved: " + dlg.FileName;
    }

    private void OnLoadDesignClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Orbit design (*.orbitdesign.json)|*.orbitdesign.json|JSON|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _vm.LoadDesignJson(System.IO.File.ReadAllText(dlg.FileName));
            _vm.SnsStatusText = "design loaded: " + dlg.FileName;
        }
        catch (System.Exception ex) { _vm.SnsStatusText = "load failed: " + ex.Message; }
    }

    private void OnBuildSnsClick(object sender, RoutedEventArgs e)
    {
        const string defaultDonor = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 SRS.MDB";
        string donor = defaultDonor;
        if (!System.IO.File.Exists(donor))
        {
            var pick = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a donor SNS v10 SRS database (schema source)",
                Filter = "SRS database (*.mdb)|*.mdb",
            };
            if (pick.ShowDialog() != true) return;
            donor = pick.FileName;
        }
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "SRS database (*.mdb)|*.mdb",
            FileName = $"{_vm.NtcId} SRS.MDB",
        };
        if (save.ShowDialog() != true) return;
        try
        {
            var notice = _vm.BuildNotice();
            notice.Validate();
            SrsMdbWriter.WriteSrs(donor, save.FileName, notice);
            _vm.SnsStatusText = $"SNS v10 SRS written: {save.FileName} " +
                $"({notice.Orbits.Count} orbit, {notice.Phases.Count} phase rows)";
        }
        catch (System.Exception ex) { _vm.SnsStatusText = "SNS build failed: " + ex.Message; }
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

        // ground track (one full cycle)
        var trackBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x76, 0xA1));
        foreach (var seg in _vm.TrackSegments)
        {
            var pl = new Polyline { Stroke = trackBrush, StrokeThickness = 1.4, Opacity = 0.9 };
            foreach (var (lat, lon) in seg)
                pl.Points.Add(new Point(X(lon), Y(lat)));
            if (pl.Points.Count > 1) TrackCanvas.Children.Add(pl);
        }

        // start / end markers: a filled dot at the start, a ring at the end --
        // coincident when the cycle closes.
        if (_vm.TrackSegments.Count > 0)
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
