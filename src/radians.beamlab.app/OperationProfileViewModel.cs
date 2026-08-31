using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace radians.beamlab.app;

/// <summary>
/// Editor state for the Operation profile: scalar fields as text (empty
/// optional field = null, keeping the scene default), per-latitude arrays
/// as one "lat value" node per line. Pure state -- the window owns
/// dialogs. See docs/compliance-loop-plan.md, stage A.
/// </summary>
public sealed class OperationProfileViewModel : ObservableObject
{
    public OperationProfileViewModel() => Recompute();

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
    public string ReuseClusterText { get => _reuseClusterText; set { if (SetField(ref _reuseClusterText, value)) Recompute(); } }

    private string _refBwText = "40";
    public string RefBwText { get => _refBwText; set { if (SetField(ref _refBwText, value)) Recompute(); } }

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

    private void Recompute()
    {
        try
        {
            StatusText = "";
            SummaryText = Build().Summary;
        }
        catch (Exception ex)
        {
            SummaryText = "";
            StatusText = ex.Message;
        }
    }

    /// <summary>The profile the fields describe; throws with a precise message on bad input.</summary>
    public OperationProfile Build()
        => new(1, _name,
            new DownlinkProfile(
                Req(_frequencyGhzText, "downlink frequency"),
                Opt(_gainPeakText, "peak gain"), Opt(_beamCellRadiusText, "beam cell radius"),
                Opt(_taylorSlrText, "Taylor SLR"), OptInt(_taylorNbarText, "Taylor nbar"),
                Opt(_patternFloorText, "pattern floor"),
                Opt(_txEirpText, "tx eirp"), _powerMode.Trim(), _aggregation.Trim(),
                OptInt(_reuseClusterText, "reuse cluster"), Req(_refBwText, "ref bandwidth"),
                Opt(_dlAngleSatText, "downlink min angle at sat"),
                Opt(_dlAngleEsText, "downlink min angle at ES"),
                _footprintSource.Trim(), _maskXmlPathText.Trim()),
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
