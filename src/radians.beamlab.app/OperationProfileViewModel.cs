using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Editor state for the Operation profile: scalar fields as text (empty
/// optional field = null, keeping the scene default), per-latitude arrays
/// as one "lat value" node per line. Pure state -- the window owns
/// dialogs. See docs/compliance-loop-plan.md, stage A.
/// </summary>
public sealed class OperationProfileViewModel : ObservableObject
{
    public OperationProfileViewModel()
    {
        // First presentation mirrors the PFD-mask generator's current
        // inputs as explicit values (Taylor elliptical and friends), so
        // a fresh profile starts from the same payload the generator
        // shows -- not from blanks. Loading a profile overwrites these.
        var t = new PfdMaskViewModel();
        var inv = CultureInfo.InvariantCulture;
        _gainPeakText = t.GmDbi.ToString(inv);
        _beamCellRadiusText = t.CellRadiusKm.ToString(inv);
        _taylorSlrText = t.TaylorSlrDb.ToString(inv);
        _taylorNbarText = t.TaylorNbar.ToString(inv);
        _patternFloorText = t.LfDbi.ToString(inv);
        _txEirpText = t.TxEirpDbw.ToString(inv);
        _powerMode = t.IsConstantPfdMode ? "pfd" : "eirp";
        _aggregation = t.IsCoChannelMode ? "cochannel" : "powersum";
        _reuseClusterText = t.ReuseClusterIndex.ToString(inv);
        _refBwText = t.RefBwKHz.ToString(inv);
        _ellRollOffText = t.EllRollOffDb.ToString(inv);
        _patternKind = t.Scene.PatternKind.ToString();
        _thetaBText = t.Scene.ThetaBDeg.ToString(inv);
        _autoHex = t.Scene.AutoMode;
        _uvArrayBeams = t.Scene.UvArrayBeams;
        _ellAlphaText = t.Scene.EllAlphaDeg.ToString(inv);
        _ellBetaText = t.Scene.EllBetaDeg.ToString(inv);
        _lnText = t.Scene.LnDb.ToString(inv);
        _crossoverText = t.Scene.CrossoverDb.ToString(inv);
        Recompute();
    }

    // ---- identity / payload --------------------------------------------

    private string _name = "profile";
    public string Name { get => _name; set { if (SetField(ref _name, value)) Recompute(); } }

    private string _frequencyGhzText = "19.7";
    public string FrequencyGhzText { get => _frequencyGhzText; set { if (SetField(ref _frequencyGhzText, value)) Recompute(); } }

    private string _footprintSource = "composition";
    /// <summary>"composition" = live shaped beams; "mask" = the declared PFD mask XML below.</summary>
    public string FootprintSource
    {
        get => _footprintSource;
        set
        {
            if (!SetField(ref _footprintSource, value)) return;
            OnPropertyChanged(nameof(IsMaskFootprint));
            Recompute();
        }
    }

    public bool IsMaskFootprint => _footprintSource == "mask";

    private string _maskXmlPathText = "";
    /// <summary>Path of the declared PFD mask XML (used when the source is "mask").</summary>
    public string MaskXmlPathText { get => _maskXmlPathText; set { if (SetField(ref _maskXmlPathText, value)) Recompute(); } }

    private string _gainPeakText = "";
    public string GainPeakText { get => _gainPeakText; set { if (SetField(ref _gainPeakText, value)) Recompute(); } }

    private string _beamCellRadiusText = "";
    public string BeamCellRadiusText { get => _beamCellRadiusText; set { if (SetField(ref _beamCellRadiusText, value)) Recompute(); } }

    private string _taylorSlrText = "";
    public string TaylorSlrText { get => _taylorSlrText; set { if (SetField(ref _taylorSlrText, value)) Recompute(); } }

    private string _taylorNbarText = "";
    public string TaylorNbarText { get => _taylorNbarText; set { if (SetField(ref _taylorNbarText, value)) Recompute(); } }

    private string _patternFloorText = "";
    public string PatternFloorText { get => _patternFloorText; set { if (SetField(ref _patternFloorText, value)) Recompute(); } }

    private string _txEirpText = "";
    public string TxEirpText { get => _txEirpText; set { if (SetField(ref _txEirpText, value)) Recompute(); } }

    private string _powerMode = "";
    /// <summary>"" = scene default, "eirp" = constant e.i.r.p., "pfd" = constant boresight PFD.</summary>
    public string PowerMode { get => _powerMode; set { if (SetField(ref _powerMode, value)) Recompute(); } }

    private string _aggregation = "";
    /// <summary>"" = scene default, "powersum" or "cochannel".</summary>
    public string Aggregation { get => _aggregation; set { if (SetField(ref _aggregation, value)) Recompute(); } }

    private string _reuseClusterText = "";
    public string ReuseClusterText
    {
        get => _reuseClusterText;
        set
        {
            if (!SetField(ref _reuseClusterText, value)) return;
            OnPropertyChanged(nameof(ReuseSelIndex));
            Recompute();
        }
    }

    /// <summary>
    /// The generator tab's reuse selector: 0 = scene default, then the
    /// cluster sizes 3 / 4 / 7 (stored as the index 0..2, exactly as the
    /// tab's ReuseClusterIndex stores it).
    /// </summary>
    public int ReuseSelIndex
    {
        get => _reuseClusterText.Trim() switch { "0" => 1, "1" => 2, "2" => 3, _ => 0 };
        set
        {
            string t = value switch { 1 => "0", 2 => "1", 3 => "2", _ => "" };
            if (t == _reuseClusterText) return;
            _reuseClusterText = t;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReuseClusterText));
            Recompute();
        }
    }

    private string _refBwText = "40";
    public string RefBwText { get => _refBwText; set { if (SetField(ref _refBwText, value)) Recompute(); } }

    private string _ellRollOffText = "";
    /// <summary>Edge-of-cell roll-off (dB); empty keeps the scene default.</summary>
    public string EllRollOffText { get => _ellRollOffText; set { if (SetField(ref _ellRollOffText, value)) Recompute(); } }

    private string _patternKind = "";
    /// <summary>"" = scene default; else a BeamPatternKind name.</summary>
    public string PatternKind { get => _patternKind; set { if (SetField(ref _patternKind, value)) Recompute(); } }

    private string _thetaBText = "";
    public string ThetaBText { get => _thetaBText; set { if (SetField(ref _thetaBText, value)) Recompute(); } }

    private bool? _autoHex;
    /// <summary>Auto hex tessellation; indeterminate keeps the scene default.</summary>
    public bool? AutoHex { get => _autoHex; set { if (SetField(ref _autoHex, value)) Recompute(); } }

    private bool? _uvArrayBeams;
    /// <summary>Array-steered UV beams; indeterminate keeps the scene default.</summary>
    public bool? UvArrayBeams { get => _uvArrayBeams; set { if (SetField(ref _uvArrayBeams, value)) Recompute(); } }

    private string _ellAlphaText = "";
    public string EllAlphaText { get => _ellAlphaText; set { if (SetField(ref _ellAlphaText, value)) Recompute(); } }

    private string _ellBetaText = "";
    public string EllBetaText { get => _ellBetaText; set { if (SetField(ref _ellBetaText, value)) Recompute(); } }

    private string _lnText = "";
    public string LnText { get => _lnText; set { if (SetField(ref _lnText, value)) Recompute(); } }

    private string _crossoverText = "";
    /// <summary>Rings-layout crossover level (dB, negative); empty keeps the scene default.</summary>
    public string CrossoverText { get => _crossoverText; set { if (SetField(ref _crossoverText, value)) Recompute(); } }

    private string _previewAltText = "1200";
    /// <summary>Reference altitude (km) the composition preview is built at.</summary>
    public string PreviewAltText { get => _previewAltText; set { if (SetField(ref _previewAltText, value)) Recompute(); } }

    private string _ulFrequencyGhzText = "29.5";
    public string UlFrequencyGhzText { get => _ulFrequencyGhzText; set { if (SetField(ref _ulFrequencyGhzText, value)) Recompute(); } }

    private string _esDishText = "";
    public string EsDishText { get => _esDishText; set { if (SetField(ref _esDishText, value)) Recompute(); } }

    // ---- coverage / service --------------------------------------------

    private string _minElevText = "10";
    public string MinElevText { get => _minElevText; set { if (SetField(ref _minElevText, value)) Recompute(); } }

    private string _latMinText = "30";
    public string LatMinText { get => _latMinText; set { if (SetField(ref _latMinText, value)) Recompute(); } }

    private string _latMaxText = "60";
    public string LatMaxText { get => _latMaxText; set { if (SetField(ref _latMaxText, value)) Recompute(); } }

    private string _lonMinText = "-20";
    public string LonMinText { get => _lonMinText; set { if (SetField(ref _lonMinText, value)) Recompute(); } }

    private string _lonMaxText = "20";
    public string LonMaxText { get => _lonMaxText; set { if (SetField(ref _lonMaxText, value)) Recompute(); } }

    private string _cellKmText = "450";
    public string CellKmText { get => _cellKmText; set { if (SetField(ref _cellKmText, value)) Recompute(); } }

    private string _coverageRadiusText = "";
    public string CoverageRadiusText { get => _coverageRadiusText; set { if (SetField(ref _coverageRadiusText, value)) Recompute(); } }

    // ---- operation / scheduling ----------------------------------------

    private string _trackingPolicy = "HighestElevation";
    public string TrackingPolicy { get => _trackingPolicy; set { if (SetField(ref _trackingPolicy, value)) Recompute(); } }

    private string _minHoldText = "";
    /// <summary>Hold time before a voluntary handover (s); the dwell of the elevation/alpha strategies.</summary>
    public string MinHoldText { get => _minHoldText; set { if (SetField(ref _minHoldText, value)) Recompute(); } }

    private string _ncoText = "";
    public string NcoText { get => _ncoText; set { if (SetField(ref _ncoText, value)) Recompute(); } }

    private string _maxCoFreqSatText = "";
    public string MaxCoFreqSatText { get => _maxCoFreqSatText; set { if (SetField(ref _maxCoFreqSatText, value)) Recompute(); } }

    private string _dlAngleSatText = "";
    public string DlAngleSatText { get => _dlAngleSatText; set { if (SetField(ref _dlAngleSatText, value)) Recompute(); } }

    private string _dlAngleEsText = "";
    public string DlAngleEsText { get => _dlAngleEsText; set { if (SetField(ref _dlAngleEsText, value)) Recompute(); } }

    private string _ulAngleSatText = "";
    public string UlAngleSatText { get => _ulAngleSatText; set { if (SetField(ref _ulAngleSatText, value)) Recompute(); } }

    private string _ulAngleEsText = "";
    public string UlAngleEsText { get => _ulAngleEsText; set { if (SetField(ref _ulAngleEsText, value)) Recompute(); } }

    private string _demandLinksText = "1";
    public string DemandLinksText { get => _demandLinksText; set { if (SetField(ref _demandLinksText, value)) Recompute(); } }

    private string _activityFactorText = "1";
    public string ActivityFactorText { get => _activityFactorText; set { if (SetField(ref _activityFactorText, value)) Recompute(); } }

    private string _activityPeriodText = "300";
    public string ActivityPeriodText { get => _activityPeriodText; set { if (SetField(ref _activityPeriodText, value)) Recompute(); } }

    private string _operationalFractionText = "1";
    public string OperationalFractionText { get => _operationalFractionText; set { if (SetField(ref _operationalFractionText, value)) Recompute(); } }

    private string _dutyText = "1";
    public string DutyText { get => _dutyText; set { if (SetField(ref _dutyText, value)) Recompute(); } }

    private string _esPowerText = "";
    public string EsPowerText { get => _esPowerText; set { if (SetField(ref _esPowerText, value)) Recompute(); } }

    private string _powerRefElevText = "";
    public string PowerRefElevText { get => _powerRefElevText; set { if (SetField(ref _powerRefElevText, value)) Recompute(); } }

    // ---- exclusion + per-latitude arrays -------------------------------

    private string _alphaText = "0";
    public string AlphaText { get => _alphaText; set { if (SetField(ref _alphaText, value)) Recompute(); } }

    private string _minElevByLatText = "";
    /// <summary>Lines "lat elev".</summary>
    public string MinElevByLatText { get => _minElevByLatText; set { if (SetField(ref _minElevByLatText, value)) Recompute(); } }

    private string _ncoByLatText = "";
    /// <summary>Lines "lat n".</summary>
    public string NcoByLatText { get => _ncoByLatText; set { if (SetField(ref _ncoByLatText, value)) Recompute(); } }

    private string _alphaByLatText = "";
    /// <summary>Lines "lat alpha".</summary>
    public string AlphaByLatText { get => _alphaByLatText; set { if (SetField(ref _alphaByLatText, value)) Recompute(); } }

    private string _selectedDirection = "Downlink";
    /// <summary>Which direction's inputs the editor shows; the file always carries both.</summary>
    public string SelectedDirection
    {
        get => _selectedDirection;
        set
        {
            if (!SetField(ref _selectedDirection, value)) return;
            OnPropertyChanged(nameof(IsDownlinkSelected));
            OnPropertyChanged(nameof(IsUplinkSelected));
        }
    }

    public bool IsDownlinkSelected => _selectedDirection != "Uplink";
    public bool IsUplinkSelected => _selectedDirection == "Uplink";

    // ---- outputs --------------------------------------------------------

    private string _summaryText = "";
    public string SummaryText { get => _summaryText; private set => SetField(ref _summaryText, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private string _compositionText = "";
    /// <summary>The final beam composition the payload fields produce, at the preview altitude.</summary>
    public string CompositionText { get => _compositionText; private set => SetField(ref _compositionText, value); }

    /// <summary>
    /// One beam of the in-place composition sketch: boresight ground
    /// point and its 3-dB outline, in local km east/north of the
    /// sub-satellite point (no geographic map involved). Color is the
    /// beam's reuse colour under a declared co-channel N plan, -1 when
    /// the aggregation is the plain power sum.
    /// </summary>
    public sealed record PreviewBeam(double EKm, double NKm, bool On, int Color,
        IReadOnlyList<double> OutlineEKm, IReadOnlyList<double> OutlineNKm);

    private IReadOnlyList<PreviewBeam> _previewBeams = Array.Empty<PreviewBeam>();
    public IReadOnlyList<PreviewBeam> PreviewBeams { get => _previewBeams; private set => SetField(ref _previewBeams, value); }

    private double _previewFovKm;
    /// <summary>Ground radius (km) of the served field of view at the profile's minimum elevation.</summary>
    public double PreviewFovKm { get => _previewFovKm; private set => SetField(ref _previewFovKm, value); }

    private double _previewHorizonKm;
    /// <summary>Ground radius (km) of the 0-elevation contour (the horizon).</summary>
    public double PreviewHorizonKm { get => _previewHorizonKm; private set => SetField(ref _previewHorizonKm, value); }

    // Conditional visibility mirroring the composite gain tab, computed
    // from the EFFECTIVE (composed) scene so "(scene default)" choices
    // show the right inputs.
    private bool _isEllipticalPattern;
    public bool IsEllipticalPattern { get => _isEllipticalPattern; private set => SetField(ref _isEllipticalPattern, value); }

    private bool _isEllipticalAutoMode;
    public bool IsEllipticalAutoMode { get => _isEllipticalAutoMode; private set => SetField(ref _isEllipticalAutoMode, value); }

    private bool _isEllipticalManualMode;
    public bool IsEllipticalManualMode { get => _isEllipticalManualMode; private set => SetField(ref _isEllipticalManualMode, value); }

    private void Recompute()
    {
        try
        {
            StatusText = "";
            var p = Build();
            SummaryText = p.Summary;
            // The final composition these fields produce: compose the scene
            // at the preview altitude and count the built and active beams
            // -- the same rebuild every simulation run performs per state.
            double alt = Req(_previewAltText, "preview altitude");
            var comp = OperationComposer.Compose(p, alt);
            bool ell = comp.Scene.Scene.PatternKind == BeamPatternKind.Taylor_1p4_Ell;
            IsEllipticalPattern = ell;
            IsEllipticalAutoMode = ell && comp.Scene.Scene.AutoMode;
            IsEllipticalManualMode = ell && !comp.Scene.Scene.AutoMode;
            // A clean local frame for the sketch: sub-satellite (0, 0).
            comp.Scene.Scene.SubSatLatDeg = 0.0;
            comp.Scene.Scene.SubSatLonDeg = 0.0;
            comp.Scene.RebuildForCompute();
            var beams = comp.Scene.Scene.Beams;
            int on = beams.Count(b => b.Weight > 0.0);
            int? reuseN = comp.Scene.Aggregation == PfdAggregation.CoChannelSum
                ? comp.Scene.ReuseClusterSize : null;
            BuildPreviewBeams(beams, alt, p.MinElevDeg, reuseN);
            CompositionText = FormattableString.Invariant(
                $"final composition at {alt:F0} km: {beams.Count} spot beams built, {on} active (exclusion {p.AlphaExclDeg:F1} deg) - cell {comp.Scene.CellRadiusKm:F0} km, roll-off {comp.Scene.EllRollOffDb:F1} dB, {comp.Scene.Scene.PatternKind}, min elev {p.MinElevDeg:F1} deg");
        }
        catch (Exception ex)
        {
            SummaryText = "";
            CompositionText = "";
            PreviewBeams = Array.Empty<PreviewBeam>();
            StatusText = ex.Message;
        }
    }

    /// <summary>
    /// The in-place sketch data: per beam, the boresight's ground point
    /// and a 24-point 3-dB outline, both as ray-sphere hits from the
    /// satellite, in local km east/north of the sub-satellite point.
    /// The half-power angle per azimuth comes from the beam's OWN
    /// pattern by bisection, so every pattern kind draws its true
    /// footprint without per-model width formulas.
    /// </summary>
    private void BuildPreviewBeams(IReadOnlyList<Beam> beams,
        double altitudeKm, double minElevDeg, int? reuseN)
    {
        double R = EarthRadiusKm;
        var sat = GeodeticToEcef(0.0, 0.0, altitudeKm);
        var colors = reuseN is int rn ? BeamComposer.ReuseColors(beams, rn) : null;
        var list = new List<PreviewBeam>(beams.Count);
        for (int bi = 0; bi < beams.Count; bi++)
        {
            var b = beams[bi];
            if (RaySphereHit(sat, b.Boresight) is not { } g) continue;
            double ce = R * Math.Atan2(g.Y, g.X);
            double cn = R * Math.Asin(Math.Clamp(g.Z / g.Length, -1.0, 1.0));

            var bs = b.Boresight;
            var refR = b.RadialAxisEcef ?? PerpOf(bs);
            var tr = Vec3.Cross(bs, refR);
            // Two bisections (radial and transverse half-power angles),
            // then the analytic ellipse between them -- exact for the
            // two axes, a faithful sketch everywhere else, and an order
            // of magnitude cheaper than bisecting every azimuth.
            double thR = HalfPowerDeg(b, refR);
            double thT = b.RadialAxisEcef is null ? thR : HalfPowerDeg(b, tr);
            var oe = new List<double>(24);
            var onl = new List<double>(24);
            for (int k = 0; k < 24; k++)
            {
                double psi = 2.0 * Math.PI * k / 24.0;
                double c = Math.Cos(psi), sn = Math.Sin(psi);
                double thDeg = 1.0 / Math.Sqrt(c * c / (thR * thR) + sn * sn / (thT * thT));
                var side = (refR * c + tr * sn).Normalized();
                double thRad = thDeg * Math.PI / 180.0;
                var dir = (bs * Math.Cos(thRad) + side * Math.Sin(thRad)).Normalized();
                if (RaySphereHit(sat, dir) is { } h)
                {
                    oe.Add(R * Math.Atan2(h.Y, h.X));
                    onl.Add(R * Math.Asin(Math.Clamp(h.Z / h.Length, -1.0, 1.0)));
                }
            }
            list.Add(new PreviewBeam(ce, cn, b.Weight > 0.0,
                colors is null ? -1 : colors[bi], oe, onl));
        }
        PreviewBeams = list;

        double eps = minElevDeg * Math.PI / 180.0;
        double gamma = Math.Acos(Math.Clamp(R / (R + altitudeKm) * Math.Cos(eps), -1.0, 1.0)) - eps;
        PreviewFovKm = R * gamma;
        // The same formula at eps = 0: the horizon (0-elevation) contour.
        PreviewHorizonKm = R * Math.Acos(Math.Clamp(R / (R + altitudeKm), -1.0, 1.0));
    }

    /// <summary>Half-power (Gm - 3 dB) off-axis angle along one azimuth, by bisection on the beam's own gain.</summary>
    private static double HalfPowerDeg(Beam b, Vec3 side)
    {
        double target = b.Pattern.Gm - 3.0;
        double DirGain(double thDeg)
        {
            double th = thDeg * Math.PI / 180.0;
            var d = (b.Boresight * Math.Cos(th) + side * Math.Sin(th)).Normalized();
            return b.GainDbi(d);
        }
        double hi = 0.25;
        while (hi < 45.0 && DirGain(hi) > target) hi *= 2.0;
        double lo = hi / 2.0;
        for (int i = 0; i < 24; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (DirGain(mid) > target) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    private static Vec3 PerpOf(Vec3 v)
    {
        var z = new Vec3(0.0, 0.0, 1.0);
        var c = Vec3.Cross(v, z);
        if (c.Length < 1e-9) c = Vec3.Cross(v, new Vec3(0.0, 1.0, 0.0));
        return c.Normalized();
    }

    /// <summary>The profile the fields describe; throws with a precise message on bad input.</summary>
    public OperationProfile Build()
        => new(1, _name,
            new DownlinkProfile(
                Req(_frequencyGhzText, "downlink frequency"),
                Opt(_gainPeakText, "peak gain"), Opt(_beamCellRadiusText, "beam cell radius"),
                Opt(_taylorSlrText, "Taylor SLR"), OptInt(_taylorNbarText, "Taylor nbar"),
                Opt(_patternFloorText, "pattern floor"),
                Opt(_txEirpText, "tx power density"), _powerMode.Trim(), _aggregation.Trim(),
                OptInt(_reuseClusterText, "reuse cluster"), Req(_refBwText, "ref bandwidth"),
                Opt(_dlAngleSatText, "downlink min angle at sat"),
                Opt(_dlAngleEsText, "downlink min angle at ES"),
                _footprintSource.Trim(), _maskXmlPathText.Trim(),
                Opt(_ellRollOffText, "edge roll-off"), _patternKind.Trim(),
                Opt(_thetaBText, "beamwidth"), _autoHex, _uvArrayBeams,
                Opt(_ellAlphaText, "ell alpha"), Opt(_ellBetaText, "ell beta"),
                Opt(_lnText, "near-in side-lobe"), Opt(_crossoverText, "crossover")),
            new UplinkProfile(
                Req(_ulFrequencyGhzText, "uplink frequency"),
                Opt(_esPowerText, "ES power"), Opt(_powerRefElevText, "power control ref elev"),
                Opt(_esDishText, "ES dish"),
                Opt(_ulAngleSatText, "uplink min angle at sat"),
                Opt(_ulAngleEsText, "uplink min angle at ES")),
            Req(_minElevText, "min elevation"),
            Req(_latMinText, "lat min"), Req(_latMaxText, "lat max"),
            Req(_lonMinText, "lon min"), Req(_lonMaxText, "lon max"),
            Req(_cellKmText, "cell size"), Opt(_coverageRadiusText, "coverage radius"),
            _trackingPolicy.Trim(),
            Opt(_minHoldText, "hold time"),
            OptInt(_ncoText, "Nco"), OptInt(_maxCoFreqSatText, "max co-freq sat"),
            (int)Req(_demandLinksText, "demand links"),
            Req(_activityFactorText, "activity factor"), Req(_activityPeriodText, "activity period"),
            Req(_operationalFractionText, "operational fraction"), Req(_dutyText, "illumination duty"),
            Req(_alphaText, "exclusion alpha"),
            Rows(_minElevByLatText, "min_elev by lat"),
            Rows(_ncoByLatText, "Nco by lat"),
            Rows(_alphaByLatText, "alpha by lat"));

    public string BuildJson() => OperationProfileCodec.Save(Build());

    public void LoadJson(string json) => Apply(OperationProfileCodec.Load(json));

    /// <summary>Populates every field from a profile (load, or the advisor's result).</summary>
    public void Apply(OperationProfile p)
    {
        var inv = CultureInfo.InvariantCulture;
        Name = p.Name;
        var dl = p.Down;
        FrequencyGhzText = dl.FrequencyGhz.ToString(inv);
        GainPeakText = dl.GainPeakDbi?.ToString(inv) ?? "";
        BeamCellRadiusText = dl.BeamCellRadiusKm?.ToString(inv) ?? "";
        TaylorSlrText = dl.TaylorSlrDb?.ToString(inv) ?? "";
        TaylorNbarText = dl.TaylorNbar?.ToString(inv) ?? "";
        PatternFloorText = dl.PatternFloorDbi?.ToString(inv) ?? "";
        TxEirpText = dl.TxEirpDbw?.ToString(inv) ?? "";
        PowerMode = dl.PowerMode;
        Aggregation = dl.Aggregation;
        ReuseClusterText = dl.ReuseClusterIndex?.ToString(inv) ?? "";
        RefBwText = dl.RefBwKHz.ToString(inv);
        DlAngleSatText = dl.MinAngleAtSatDeg?.ToString(inv) ?? "";
        DlAngleEsText = dl.MinAngleAtEsDeg?.ToString(inv) ?? "";
        FootprintSource = dl.FootprintSource;
        MaskXmlPathText = dl.MaskXmlPath;
        EllRollOffText = dl.EllRollOffDb?.ToString(inv) ?? "";
        PatternKind = dl.PatternKind;
        ThetaBText = dl.ThetaBDeg?.ToString(inv) ?? "";
        AutoHex = dl.AutoHex;
        UvArrayBeams = dl.UvArrayBeams;
        EllAlphaText = dl.EllAlphaDeg?.ToString(inv) ?? "";
        EllBetaText = dl.EllBetaDeg?.ToString(inv) ?? "";
        LnText = dl.LnDb?.ToString(inv) ?? "";
        CrossoverText = dl.CrossoverDb?.ToString(inv) ?? "";
        var ul = p.Up;
        UlFrequencyGhzText = ul.FrequencyGhz.ToString(inv);
        EsPowerText = ul.EsPowerDbw?.ToString(inv) ?? "";
        PowerRefElevText = ul.PowerControlRefElevDeg?.ToString(inv) ?? "";
        EsDishText = ul.EsDishM?.ToString(inv) ?? "";
        UlAngleSatText = ul.MinAngleAtSatDeg?.ToString(inv) ?? "";
        UlAngleEsText = ul.MinAngleAtEsDeg?.ToString(inv) ?? "";
        MinElevText = p.MinElevDeg.ToString(inv);
        LatMinText = p.ServiceLatMinDeg.ToString(inv);
        LatMaxText = p.ServiceLatMaxDeg.ToString(inv);
        LonMinText = p.ServiceLonMinDeg.ToString(inv);
        LonMaxText = p.ServiceLonMaxDeg.ToString(inv);
        CellKmText = p.CellKm.ToString(inv);
        CoverageRadiusText = p.CoverageRadiusKm?.ToString(inv) ?? "";
        TrackingPolicy = p.TrackingPolicy;
        MinHoldText = p.MinHoldSec?.ToString(inv) ?? "";
        NcoText = p.NcoPerCell?.ToString(inv) ?? "";
        MaxCoFreqSatText = p.MaxCoFreqSat?.ToString(inv) ?? "";
        DemandLinksText = p.DemandLinksPerCell.ToString(inv);
        ActivityFactorText = p.ActivityFactor.ToString(inv);
        ActivityPeriodText = p.ActivityPeriodSec.ToString(inv);
        OperationalFractionText = p.OperationalFraction.ToString(inv);
        DutyText = p.IlluminationDutyCycle.ToString(inv);
        AlphaText = p.AlphaExclDeg.ToString(inv);
        MinElevByLatText = RowsText(p.MinElevByLat);
        NcoByLatText = RowsText(p.NcoByLat);
        AlphaByLatText = RowsText(p.AlphaByLat);
    }

    // ---- parsing helpers ------------------------------------------------

    private static string RowsText(IReadOnlyList<ProfileLatRow>? rows)
        => rows is null ? "" : string.Join("\n", rows.Select(r =>
            FormattableString.Invariant($"{r.LatDeg} {r.Value}")));

    private static double Req(string text, string what)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : throw new FormatException($"{what}: '{text.Trim()}' is not a number");

    private static double? Opt(string text, string what)
        => text.Trim().Length == 0 ? null : Req(text, what);

    private static int? OptInt(string text, string what)
        => text.Trim().Length == 0 ? null
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : throw new FormatException($"{what}: '{text.Trim()}' is not a whole number");

    private static IReadOnlyList<ProfileLatRow> Rows(string text, string array)
    {
        var rows = new List<ProfileLatRow>();
        var raw = text.Split('\n');
        for (int i = 0; i < raw.Length; i++)
        {
            string line = raw[i].Trim();
            if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new FormatException($"{array} line {i + 1}: expected 'lat value'");
            rows.Add(new ProfileLatRow(Req(parts[0], $"{array} line {i + 1} lat"),
                                       Req(parts[1], $"{array} line {i + 1} value")));
        }
        return rows;
    }
}
