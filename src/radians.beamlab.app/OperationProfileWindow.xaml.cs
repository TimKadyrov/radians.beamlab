using System;
using System.Windows;

namespace radians.beamlab.app;

/// <summary>Dialogs over <see cref="OperationProfileViewModel"/>.</summary>
public partial class OperationProfileWindow : Window
{
    private readonly OperationProfileViewModel _vm = new();

    public OperationProfileWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        WireToolTips();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperationProfileViewModel.PreviewBeams))
                RedrawPreview();
        };
        PreviewCanvas.SizeChanged += (_, _) => RedrawPreview();
        Loaded += (_, _) => RedrawPreview();
    }

    /// <summary>
    /// The in-place composition sketch: 3-dB outlines (translucent-filled
    /// by reuse colour) and boresight dots in local km, the inner dashed
    /// rim marking the served field of view at the minimum elevation and
    /// the outer one the 0-elevation horizon. No geographic map -- the
    /// frame is the sub-satellite tangent plane.
    /// </summary>
    private void RedrawPreview()
    {
        var canvas = PreviewCanvas;
        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        var beams = _vm.PreviewBeams;
        if (w < 40 || h < 40 || beams.Count == 0 || _vm.PreviewFovKm <= 0) return;

        double outerKm = Math.Max(_vm.PreviewHorizonKm, _vm.PreviewFovKm);
        double scale = Math.Min(w, h) / 2.0 / (outerKm * 1.06);
        double cx = w / 2.0, cy = h / 2.0;
        double X(double eKm) => cx + eKm * scale;
        double Y(double nKm) => cy - nKm * scale;

        var ringBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x8B, 0xC4, 0xCD));
        void Ring(double radiusKm, System.Windows.Media.DoubleCollection dash, double opacity)
        {
            double px = radiusKm * scale;
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = 2 * px, Height = 2 * px,
                Stroke = ringBrush, StrokeThickness = 1,
                StrokeDashArray = dash, Opacity = opacity,
            };
            System.Windows.Controls.Canvas.SetLeft(ring, cx - px);
            System.Windows.Controls.Canvas.SetTop(ring, cy - px);
            canvas.Children.Add(ring);
        }
        // Outer: the 0-elevation horizon; inner: the served rim at min elev.
        Ring(_vm.PreviewHorizonKm, new System.Windows.Media.DoubleCollection { 2, 3 }, 0.75);
        Ring(_vm.PreviewFovKm, new System.Windows.Media.DoubleCollection { 4, 3 }, 1.0);

        var onBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x15, 0x80, 0x7B));
        var onDot = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x00, 0x76, 0xA1));
        var offBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xB9, 0xCD, 0xD4));
        // Reuse colours (up to N = 7) when co-channel reuse is declared.
        var reuse = new System.Windows.Media.SolidColorBrush[]
        {
            new(System.Windows.Media.Color.FromRgb(0x15, 0x80, 0x7B)),
            new(System.Windows.Media.Color.FromRgb(0x00, 0x76, 0xA1)),
            new(System.Windows.Media.Color.FromRgb(0x8A, 0x5C, 0xA8)),
            new(System.Windows.Media.Color.FromRgb(0xC0, 0x6E, 0x27)),
            new(System.Windows.Media.Color.FromRgb(0xB2, 0x3A, 0x64)),
            new(System.Windows.Media.Color.FromRgb(0x6B, 0x8E, 0x23)),
            new(System.Windows.Media.Color.FromRgb(0x7A, 0x5C, 0x43)),
        };
        // Translucent fill from the stroke colour so neighbouring reuse
        // colours read as patches, not just hairlines.
        static System.Windows.Media.SolidColorBrush FillOf(
            System.Windows.Media.SolidColorBrush b, byte alpha)
        {
            var c = b.Color;
            return new(System.Windows.Media.Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        foreach (bool onPass in new[] { false, true })
            foreach (var b in beams)
            {
                if (b.On != onPass) continue;
                var stroke = !b.On ? offBrush
                    : b.Color >= 0 ? reuse[b.Color % reuse.Length]
                    : onBrush;
                var pl = new System.Windows.Shapes.Polygon
                {
                    Stroke = stroke,
                    StrokeThickness = b.On ? 1.2 : 0.8,
                    Fill = FillOf(stroke, b.On ? (byte)0x40 : (byte)0x1C),
                };
                for (int i = 0; i < b.OutlineEKm.Count; i++)
                    pl.Points.Add(new Point(X(b.OutlineEKm[i]), Y(b.OutlineNKm[i])));
                canvas.Children.Add(pl);

                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 3, Height = 3,
                    Fill = !b.On ? offBrush : b.Color >= 0 ? stroke : onDot,
                };
                System.Windows.Controls.Canvas.SetLeft(dot, X(b.EKm) - 1.5);
                System.Windows.Controls.Canvas.SetTop(dot, Y(b.NKm) - 1.5);
                canvas.Children.Add(dot);
            }
    }

    /// <summary>
    /// Every field's help comes from the shared ParameterCatalog (the
    /// card deck's twin) -- one card per parameter, UI and documentation
    /// unable to drift.
    /// </summary>
    private void WireToolTips()
    {
        static string? Cat(string name) => radians.beamlab.ParameterCatalog.Find(name)?.ToolTipText;
        FreqBox.ToolTip = Cat("FREQ_MIN / FREQ_MAX");
        UlFreqBox.ToolTip = Cat("FREQ_MIN / FREQ_MAX");
        FootprintCombo.ToolTip = Cat("FootprintSource");
        MaskPathBox.ToolTip = Cat("FootprintSource");
        GainBox.ToolTip = Cat("GainPeakDbi");
        CellRadBox.ToolTip = Cat("BeamCellRadiusKm");
        SlrBox.ToolTip = Cat("TaylorSlrDb · TaylorNbar");
        NbarBox.ToolTip = Cat("TaylorSlrDb · TaylorNbar");
        FloorBox.ToolTip = Cat("PatternFloorDbi");
        EirpBox.ToolTip = Cat("TxEirpDbw");
        PowerModeCombo.ToolTip = Cat("PowerMode");
        AggCombo.ToolTip = Cat("Aggregation · ReuseClusterIndex");
        ReuseBox.ToolTip = Cat("Aggregation · ReuseClusterIndex");
        RefBwBox.ToolTip = Cat("RefBwKHz");
        PatternCombo.ToolTip = Cat("PatternKind");
        ThetaBBox.ToolTip = Cat("ThetaBDeg");
        AutoHexCheck.ToolTip = Cat("AutoHex · UvArrayBeams");
        UvCheck.ToolTip = Cat("AutoHex · UvArrayBeams");
        EllABox.ToolTip = Cat("EllAlphaDeg · EllBetaDeg");
        EllBBox.ToolTip = Cat("EllAlphaDeg · EllBetaDeg");
        RollOffBox.ToolTip = Cat("EllRollOffDb");
        LnBox.ToolTip = Cat("LnDb");
        CrossBox.ToolTip = Cat("CrossoverDb");
        EsDishBox.ToolTip = Cat("EsDishM");
        MinElevBox.ToolTip = Cat("MIN_ELEV");
        MinElevByLatBox.ToolTip = Cat("MIN_ELEV");
        LatBox.ToolTip = Cat("Service area");
        LonBox.ToolTip = Cat("Service area");
        CellBox.ToolTip = Cat("CellPitchKm / coverageRadiusKm");
        PolicyCombo.ToolTip = Cat("SelectionPolicy");
        NcoBox.ToolTip = Cat("MAX_CO_FREQ");
        NcoByLatBox.ToolTip = Cat("MAX_CO_FREQ");
        NcoSatBox.ToolTip = Cat("MAX_CO_FREQ_SAT");
        DlAngleSatBox.ToolTip = Cat("MIN_ANGLE_AT_SAT");
        DlAngleEsBox.ToolTip = Cat("MIN_ANGLE_AT_ES");
        UlAngleSatBox.ToolTip = Cat("MIN_ANGLE_AT_SAT");
        UlAngleEsBox.ToolTip = Cat("MIN_ANGLE_AT_ES");
        HoldBox.ToolTip = Cat("MIN_DURATION");
        DemandBox.ToolTip = Cat("DemandLinks");
        ActivityBox.ToolTip = Cat("ActivityFactor");
        FractionBox.ToolTip = Cat("OperationalFraction");
        DutyBox.ToolTip = Cat("IlluminationDutyCycle");
        AlphaBox.ToolTip = Cat("MIN_EXCLUDE");
        AlphaByLatBox.ToolTip = Cat("MIN_EXCLUDE");
        EsPowerBox.ToolTip = Cat("PowerDbw");
        PowerRefBox.ToolTip = Cat("PowerControlRefElevDeg");
    }

    private void OnBrowseMaskClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "S.1503-4 PFD mask (*.xml)|*.xml",
        };
        if (dlg.ShowDialog() != true) return;
        _vm.MaskXmlPathText = dlg.FileName;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = _vm.BuildJson();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Operation profile (*.opprofile.json)|*.opprofile.json",
                FileName = "system.opprofile.json",
            };
            if (dlg.ShowDialog() != true) return;
            System.IO.File.WriteAllText(dlg.FileName, json);
            _vm.StatusText = "saved: " + dlg.FileName;
        }
        catch (Exception ex) { _vm.StatusText = "save failed: " + ex.Message; }
    }

    private void OnLoadClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Operation profile (*.opprofile.json)|*.opprofile.json|JSON|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _vm.LoadJson(System.IO.File.ReadAllText(dlg.FileName));
            _vm.StatusText = "loaded: " + dlg.FileName;
        }
        catch (Exception ex) { _vm.StatusText = "load failed: " + ex.Message; }
    }
}
