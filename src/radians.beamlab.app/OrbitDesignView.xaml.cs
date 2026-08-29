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

    private void Redraw()
    {
        double w = TrackCanvas.ActualWidth, h = TrackCanvas.ActualHeight;
        TrackCanvas.Children.Clear();
        if (w < 40 || h < 40 || _coastlines is null) return;

        double X(double lonDeg) => (lonDeg + 180.0) / 360.0 * w;
        double Y(double latDeg) => (90.0 - latDeg) / 180.0 * h;

        // graticule
        var gridBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xE6));
        for (int lon = -150; lon <= 150; lon += 30)
            TrackCanvas.Children.Add(new Line
            { X1 = X(lon), Y1 = 0, X2 = X(lon), Y2 = h, Stroke = gridBrush, StrokeThickness = 1 });
        for (int lat = -60; lat <= 60; lat += 30)
            TrackCanvas.Children.Add(new Line
            { X1 = 0, Y1 = Y(lat), X2 = w, Y2 = Y(lat), Stroke = gridBrush, StrokeThickness = 1 });

        // coastlines
        var coastBrush = new SolidColorBrush(Color.FromRgb(0xB5, 0xC2, 0xBF));
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
        var trackBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0x7C, 0x86));
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
                Stroke = new SolidColorBrush(Color.FromRgb(0xA8, 0x5B, 0x12)), StrokeThickness = 2
            };
            Canvas.SetLeft(ring, X(last.LonDeg) - 7); Canvas.SetTop(ring, Y(last.LatDeg) - 7);
            TrackCanvas.Children.Add(dot);
            TrackCanvas.Children.Add(ring);
        }
    }
}
