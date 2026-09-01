using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// Dialogs and the animated map over <see cref="SimulationViewModel"/>.
/// Play marches the scheduler visibly (satellites, candidate and active
/// links); Quick run is the accelerated statistics run on a worker
/// thread with no UI updates.
/// </summary>
public partial class SimulationWindow : Window
{
    private readonly SimulationViewModel _vm = new();
    private CoastlineDataProvider? _coastlines;
    private SimulationViewModel.PlaySession? _session;
    private DispatcherTimer? _timer;
    private double _tSec;

    private readonly string? _guidePath;

    public SimulationWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        string? docs = HomeViewModel.FindDocsDir(AppContext.BaseDirectory);
        string? guide = docs is null ? null : System.IO.Path.Combine(docs, "simulation-runner.html");
        _guidePath = guide is not null && System.IO.File.Exists(guide) ? guide : null;
        GuideBtn.IsEnabled = _guidePath is not null;
        SizeChanged += (_, _) => { if (_session is not null) DrawStatic(); };
        Closed += (_, _) => _timer?.Stop();
    }

    private void OnGuideClick(object sender, RoutedEventArgs e)
    {
        if (_guidePath is null) return;
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(_guidePath) { UseShellExecute = true });
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Orbit design (*.orbitdesign.json)|*.orbitdesign.json|JSON|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        _vm.DesignPath = dlg.FileName;
        _vm.ValidateInputs();
    }

    private void OnBrowseProfileClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Operation profile (*.opprofile.json)|*.opprofile.json|JSON|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        _vm.ProfilePath = dlg.FileName;
        _vm.ValidateInputs();
    }

    private void OnBrowseOpClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Operating parameters (*.opparams.json)|*.opparams.json|JSON|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        _vm.OpParamsPath = dlg.FileName;
        _vm.ValidateInputs();
    }

    private void OnValidateClick(object sender, RoutedEventArgs e) => _vm.ValidateInputs();

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CDF base name (*.csv)|*.csv",
            FileName = "sim.csv",
        };
        if (dlg.ShowDialog() != true) return;
        string baseName = dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? dlg.FileName[..^4]
            : dlg.FileName;
        await _vm.RunAsync(baseName);
    }

    // ---- the animated timeline: play / accelerated play ---------------

    /// <summary>Simulation steps advanced per tick in accelerated mode.</summary>
    private const int FastForwardStepsPerTick = 50;

    private bool _fastForward;

    private void OnPlayClick(object sender, RoutedEventArgs e) => StartOrSwitch(fastForward: false);

    private void OnFfClick(object sender, RoutedEventArgs e) => StartOrSwitch(fastForward: true);

    /// <summary>
    /// One continuous timeline for both speeds: the first press starts the
    /// session; a press of the other icon mid-run only changes the pace --
    /// the clock, the scheduler state and the drawn frame carry on.
    /// </summary>
    private void StartOrSwitch(bool fastForward)
    {
        _fastForward = fastForward;
        if (_timer is not null) return;   // running: pace switched, nothing else
        try { _session = _vm.BuildPlaySession(); }
        catch (Exception ex) { _vm.StatusText = "invalid: " + ex.Message; return; }
        _coastlines ??= new CoastlineDataProvider();
        _tSec = 0.0;
        DrawStatic();
        StopBtn.IsEnabled = true;
        QuickBtn.IsEnabled = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => StopPlay("stopped");

    private void StopPlay(string why)
    {
        _timer?.Stop();
        _timer = null;
        StopBtn.IsEnabled = false;
        QuickBtn.IsEnabled = true;
        PlayStatus.Text = string.Create(CultureInfo.InvariantCulture,
            $"{why} at t = {_tSec / 3600.0:F2} h");
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_session is not { } s) { StopPlay("stopped"); return; }
        if (_tSec >= s.DurationSec) { StopPlay("finished"); return; }
        // Accelerated mode advances many steps per tick and draws only the
        // last one -- the same timeline, sparsely rendered.
        int n = _fastForward ? FastForwardStepsPerTick : 1;
        for (int i = 0; i < n - 1 && _tSec + s.StepSec < s.DurationSec; i++)
        {
            s.Scheduler.Step(_tSec);
            _tSec += s.StepSec;
        }
        DrawFrame(s, _tSec);
        _tSec += s.StepSec;
    }

    // ---- drawing ------------------------------------------------------

    private double W => StaticCanvas.ActualWidth;
    private double H => StaticCanvas.ActualHeight;
    private double X(double lonDeg) => (lonDeg + 180.0) / 360.0 * W;
    private double Y(double latDeg) => (90.0 - latDeg) / 180.0 * H;

    /// <summary>Graticule, coastlines and the service cells; drawn once per session.</summary>
    private void DrawStatic()
    {
        StaticCanvas.Children.Clear();
        if (W < 40 || H < 40 || _coastlines is null || _session is not { } s) return;

        var gridBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xEB, 0xF5));
        for (int lon = -150; lon <= 150; lon += 30)
            StaticCanvas.Children.Add(new Line
            { X1 = X(lon), Y1 = 0, X2 = X(lon), Y2 = H, Stroke = gridBrush, StrokeThickness = 1 });
        for (int lat = -60; lat <= 60; lat += 30)
            StaticCanvas.Children.Add(new Line
            { X1 = 0, Y1 = Y(lat), X2 = W, Y2 = Y(lat), Stroke = gridBrush, StrokeThickness = 1 });

        var coastBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0xC4, 0xCD));
        foreach (var line in _coastlines.Polylines)
        {
            var pl = new Polyline { Stroke = coastBrush, StrokeThickness = 1 };
            double? prevLon = null;
            foreach (var (lat, lon) in line)
            {
                if (prevLon is double pv && Math.Abs(lon - pv) > 180.0)
                {
                    if (pl.Points.Count > 1) StaticCanvas.Children.Add(pl);
                    pl = new Polyline { Stroke = coastBrush, StrokeThickness = 1 };
                }
                pl.Points.Add(new Point(X(lon), Y(lat)));
                prevLon = lon;
            }
            if (pl.Points.Count > 1) StaticCanvas.Children.Add(pl);
        }

        var cellBrush = new SolidColorBrush(Color.FromRgb(0x69, 0x80, 0x89));
        foreach (var c in s.Geo.Cells)
        {
            var dot = new Ellipse { Width = 3, Height = 3, Fill = cellBrush };
            Canvas.SetLeft(dot, X(c.LonDeg) - 1.5);
            Canvas.SetTop(dot, Y(c.LatDeg) - 1.5);
            StaticCanvas.Children.Add(dot);
        }
    }

    /// <summary>One animation frame: satellites plus candidate and active links.</summary>
    private void DrawFrame(SimulationViewModel.PlaySession s, double tSec)
    {
        LiveCanvas.Children.Clear();
        if (W < 40 || H < 40) return;

        var subsat = new Dictionary<int, (double Lat, double Lon)>();
        for (int i = 0; i < s.SatCount; i++)
        {
            var st = s.Con.StateAt(i, tSec, s.DurationSec);
            subsat[st.SatelliteNumber] = (st.SubSatLatDeg, st.SubSatLonDeg);
        }
        var cellPos = new Dictionary<int, (double Lat, double Lon)>();
        foreach (var c in s.Geo.Cells) cellPos[c.CellId] = (c.LatDeg, c.LonDeg);

        var step = s.Scheduler.Step(tSec);

        var candBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0xC4, 0xCD));
        foreach (var l in step.CandidateLinks)
            if (subsat.TryGetValue(l.SatelliteNumber, out var sp) && cellPos.TryGetValue(l.CellId, out var cp)
                && Math.Abs(sp.Lon - cp.Lon) < 180.0)
                LiveCanvas.Children.Add(new Line
                {
                    X1 = X(cp.Lon), Y1 = Y(cp.Lat), X2 = X(sp.Lon), Y2 = Y(sp.Lat),
                    Stroke = candBrush, StrokeThickness = 0.8, Opacity = 0.6,
                });

        var linkBrush = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x7B));
        foreach (var l in step.Links)
            if (subsat.TryGetValue(l.SatelliteNumber, out var sp) && cellPos.TryGetValue(l.CellId, out var cp)
                && Math.Abs(sp.Lon - cp.Lon) < 180.0)
                LiveCanvas.Children.Add(new Line
                {
                    X1 = X(cp.Lon), Y1 = Y(cp.Lat), X2 = X(sp.Lon), Y2 = Y(sp.Lat),
                    Stroke = linkBrush, StrokeThickness = 2.2, Opacity = 0.9,
                });

        var satBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x76, 0xA1));
        foreach (var (_, p) in subsat)
        {
            var dot = new Ellipse { Width = 9, Height = 9, Fill = satBrush };
            Canvas.SetLeft(dot, X(p.Lon) - 4.5);
            Canvas.SetTop(dot, Y(p.Lat) - 4.5);
            LiveCanvas.Children.Add(dot);
        }

        PlayStatus.Text = (_fastForward ? "⏩ " : "▶ ") + string.Create(CultureInfo.InvariantCulture,
            $"t = {tSec / 3600.0:F2} h · {step.Links.Count} active / {step.CandidateLinks.Count} candidate link(s) · {step.UnservedCellLinks} unserved");
    }
}
