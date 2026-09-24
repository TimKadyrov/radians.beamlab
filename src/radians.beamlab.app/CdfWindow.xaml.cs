using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace radians.beamlab.app;

/// <summary>
/// One CDF curve: epfd level against percent of time exceeded, as the
/// runner's CSV files carry it (S.1503-4 D7.1.2 bins).
/// </summary>
public sealed record CdfSeries(string Label, double[] EpfdDb, double[] Pct)
{
    /// <summary>
    /// The reference bandwidth the file states (a "refbw_khz=" token in its
    /// comment lines); null for files that do not say.
    /// </summary>
    public double? RefBwKHz { get; init; }

    /// <summary>
    /// Reads a runner CDF CSV: '#' comment lines and the header line are
    /// skipped; every other line is "epfd_db,percent".
    /// </summary>
    public static CdfSeries LoadCsv(string path, string label)
    {
        var e = new List<double>();
        var p = new List<double>();
        double? refBw = null;
        foreach (string raw in System.IO.File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith('#'))
            {
                foreach (string tok in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (tok.StartsWith("refbw_khz=") && double.TryParse(tok["refbw_khz=".Length..],
                            NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
                        refBw = r;
                continue;
            }
            if (line.Length == 0 || line.StartsWith("epfd")) continue;
            var parts = line.Split(',');
            if (parts.Length != 2)
                throw new FormatException($"{path}: expected 'epfd,percent', got '{line}'");
            e.Add(double.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture));
            p.Add(double.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture));
        }
        if (e.Count == 0) throw new InvalidOperationException($"{path}: no data rows");
        return new CdfSeries(label, e.ToArray(), p.ToArray()) { RefBwKHz = refBw };
    }
}

/// <summary>
/// Plots CDF curves: epfd on a linear dB axis, percent of time exceeded
/// on a log axis. Opened by the simulation runner on the files a run just
/// wrote; any caller with (label, epfd[], percent[]) series can use it.
/// </summary>
public partial class CdfWindow : Window
{
    private readonly IReadOnlyList<CdfSeries> _series;

    // House palette: down, is, up, then spares for other callers.
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x00, 0x76, 0xA1),
        Color.FromRgb(0x8A, 0x5C, 0xA8),
        Color.FromRgb(0x15, 0x80, 0x7B),
        Color.FromRgb(0xC0, 0x6E, 0x27),
        Color.FromRgb(0xB2, 0x3A, 0x64),
    };

    /// <summary>
    /// The bandwidth the curves are per: the one their files state when they
    /// agree; 40 kHz for files that state none (the runner's earlier files,
    /// which were labelled so); the reference bandwidth when they differ.
    /// </summary>
    public static string BandwidthLabel(IReadOnlyList<CdfSeries> series)
    {
        var stated = series.Select(s => s.RefBwKHz).Distinct().ToList();
        if (stated.Count == 1 && stated[0] is double r)
            return r.ToString("0.###", CultureInfo.InvariantCulture) + " kHz";
        if (stated.Count == 1) return "40 kHz";
        return "the reference bandwidth";
    }

    public CdfWindow(IEnumerable<CdfSeries> series)
    {
        InitializeComponent();
        _series = series.ToList();
        for (int i = 0; i < _series.Count; i++)
        {
            HeaderText.Inlines.Add(new Run("■ ")
            { Foreground = new SolidColorBrush(Palette[i % Palette.Length]) });
            HeaderText.Inlines.Add(new Run(_series[i].Label + "    "));
        }
        PlotCanvas.SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    private void Redraw()
    {
        var canvas = PlotCanvas;
        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 100 || h < 80 || _series.Count == 0) return;
        const double ml = 62, mr = 16, mt = 12, mb = 40;
        double pw = w - ml - mr, ph = h - mt - mb;
        if (pw < 40 || ph < 40) return;

        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity, pMin = 100.0;
        foreach (var s in _series)
            for (int i = 0; i < s.EpfdDb.Length; i++)
            {
                xMin = Math.Min(xMin, s.EpfdDb[i]);
                xMax = Math.Max(xMax, s.EpfdDb[i]);
                if (s.Pct[i] > 0.0) pMin = Math.Min(pMin, s.Pct[i]);
            }
        if (!double.IsFinite(xMin) || xMax <= xMin) return;
        xMin = Math.Floor(xMin / 10.0) * 10.0;
        xMax = Math.Ceiling(xMax / 10.0) * 10.0;
        const double logTop = 2.0;                                    // 100 percent
        double logBot = Math.Min(logTop - 1.0, Math.Floor(Math.Log10(pMin)));
        double pFloor = Math.Pow(10.0, logBot);

        double X(double e) => ml + (e - xMin) / (xMax - xMin) * pw;
        double Y(double p) => mt + (logTop - Math.Log10(Math.Max(p, pFloor))) / (logTop - logBot) * ph;

        var inv = CultureInfo.InvariantCulture;
        var gridBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xF1, 0xF8));
        var axisBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0xC4, 0xCD));
        var textBrush = new SolidColorBrush(Color.FromRgb(0x69, 0x80, 0x89));

        void Label(string text, double x, double y, bool centerX)
        {
            var tb = new System.Windows.Controls.TextBlock
            { Text = text, Foreground = textBrush, FontSize = 10.5 };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            System.Windows.Controls.Canvas.SetLeft(tb, centerX ? x - tb.DesiredSize.Width / 2.0 : x - tb.DesiredSize.Width);
            System.Windows.Controls.Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
        }

        // Vertical grid: 10 dB steps, doubled until at most ~12 lines.
        double xStep = 10.0;
        while ((xMax - xMin) / xStep > 12.0) xStep *= 2.0;
        for (double x = xMin; x <= xMax + 1e-9; x += xStep)
        {
            canvas.Children.Add(new Line
            { X1 = X(x), Y1 = mt, X2 = X(x), Y2 = mt + ph, Stroke = gridBrush, StrokeThickness = 1 });
            Label(x.ToString("F0", inv), X(x), mt + ph + 4, centerX: true);
        }
        // Horizontal grid: one line per decade of percent.
        for (double d = logTop; d >= logBot - 1e-9; d -= 1.0)
        {
            double p = Math.Pow(10.0, d);
            canvas.Children.Add(new Line
            { X1 = ml, Y1 = Y(p), X2 = ml + pw, Y2 = Y(p), Stroke = gridBrush, StrokeThickness = 1 });
            Label(p.ToString("G2", inv), ml - 6, Y(p) - 7, centerX: false);
        }
        canvas.Children.Add(new Line
        { X1 = ml, Y1 = mt + ph, X2 = ml + pw, Y2 = mt + ph, Stroke = axisBrush, StrokeThickness = 1 });
        canvas.Children.Add(new Line
        { X1 = ml, Y1 = mt, X2 = ml, Y2 = mt + ph, Stroke = axisBrush, StrokeThickness = 1 });
        Label("epfd (dBW/m² in " + BandwidthLabel(_series) + ")", ml + pw / 2.0, mt + ph + 20, centerX: true);
        Label("% time exceeded", ml + 110, mt - 10, centerX: true);

        for (int si = 0; si < _series.Count; si++)
        {
            var s = _series[si];
            var pl = new Polyline
            {
                Stroke = new SolidColorBrush(Palette[si % Palette.Length]),
                StrokeThickness = 1.6,
            };
            for (int i = 0; i < s.EpfdDb.Length; i++)
                pl.Points.Add(new Point(X(s.EpfdDb[i]), Y(s.Pct[i])));
            canvas.Children.Add(pl);
        }
    }
}
