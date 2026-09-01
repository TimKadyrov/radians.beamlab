using System;
using System.Globalization;
using System.Linq;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// The operating-parameters designer: authors one R set (S.1503-4 Part B
/// non_gso_operating_parameters). Header quantities are single fields
/// (empty = omitted); the four arrays are edited as plain text, one node
/// per line. Save/Load round-trips *.opparams.json; Export writes the
/// R-set XML through <see cref="OperParamsXmlWriter"/>, whose validation
/// rules (min_duration never 0, min_angle_at_es incompatible with
/// min_duration, es_density/es_distance together) are surfaced verbatim.
/// Pure state -- the window owns dialogs.
/// </summary>
public sealed class OpParamsViewModel : ObservableObject
{
    public OpParamsViewModel() => Recompute();

    // ---- header fields (empty text = attribute omitted) -----------------

    private string _satName = "NGSO-SAT";
    public string SatName { get => _satName; set { if (SetField(ref _satName, value)) Recompute(); } }

    private string _ntcIdText = "0";
    public string NtcIdText { get => _ntcIdText; set { if (SetField(ref _ntcIdText, value)) Recompute(); } }

    private string _paramIdText = "1";
    public string ParamIdText { get => _paramIdText; set { if (SetField(ref _paramIdText, value)) Recompute(); } }

    private string _lowFreqText = "10700";
    public string LowFreqText { get => _lowFreqText; set { if (SetField(ref _lowFreqText, value)) Recompute(); } }

    private string _highFreqText = "12750";
    public string HighFreqText { get => _highFreqText; set { if (SetField(ref _highFreqText, value)) Recompute(); } }

    private string _esDensityText = "";
    public string EsDensityText { get => _esDensityText; set { if (SetField(ref _esDensityText, value)) Recompute(); } }

    private string _esDistanceText = "";
    public string EsDistanceText { get => _esDistanceText; set { if (SetField(ref _esDistanceText, value)) Recompute(); } }

    private string _esLatMinText = "-90";
    public string EsLatMinText { get => _esLatMinText; set { if (SetField(ref _esLatMinText, value)) Recompute(); } }

    private string _esLatMaxText = "90";
    public string EsLatMaxText { get => _esLatMaxText; set { if (SetField(ref _esLatMaxText, value)) Recompute(); } }

    private string _minAngleAtSatText = "";
    public string MinAngleAtSatText { get => _minAngleAtSatText; set { if (SetField(ref _minAngleAtSatText, value)) Recompute(); } }

    private string _minAngleAtEsText = "";
    public string MinAngleAtEsText { get => _minAngleAtEsText; set { if (SetField(ref _minAngleAtEsText, value)) Recompute(); } }

    private string _maxCoFreqHeaderText = "";
    public string MaxCoFreqHeaderText { get => _maxCoFreqHeaderText; set { if (SetField(ref _maxCoFreqHeaderText, value)) Recompute(); } }

    private string _maxCoFreqSatText = "";
    public string MaxCoFreqSatText { get => _maxCoFreqSatText; set { if (SetField(ref _maxCoFreqSatText, value)) Recompute(); } }

    private string _minDurationHeaderText = "";
    public string MinDurationHeaderText { get => _minDurationHeaderText; set { if (SetField(ref _minDurationHeaderText, value)) Recompute(); } }

    private string _elevAngleHeaderText = "";
    public string ElevAngleHeaderText { get => _elevAngleHeaderText; set { if (SetField(ref _elevAngleHeaderText, value)) Recompute(); } }

    // ---- arrays as text, one node per line ------------------------------

    private string _minExcludeText = "";
    /// <summary>Lines "orb lat alpha" (orb 0 = all orbits).</summary>
    public string MinExcludeText { get => _minExcludeText; set { if (SetField(ref _minExcludeText, value)) Recompute(); } }

    private string _maxCoFreqText = "";
    /// <summary>Lines "lat value".</summary>
    public string MaxCoFreqText { get => _maxCoFreqText; set { if (SetField(ref _maxCoFreqText, value)) Recompute(); } }

    private string _minDurationText = "";
    /// <summary>Lines "lat seconds".</summary>
    public string MinDurationText { get => _minDurationText; set { if (SetField(ref _minDurationText, value)) Recompute(); } }

    private string _minElevText = "";
    /// <summary>Lines "lat az elev".</summary>
    public string MinElevText { get => _minElevText; set { if (SetField(ref _minElevText, value)) Recompute(); } }

    // ---- outputs --------------------------------------------------------

    private string _summaryText = "";
    public string SummaryText { get => _summaryText; private set => SetField(ref _summaryText, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private void Recompute()
    {
        try
        {
            var p = BuildSet();
            SummaryText = string.Create(CultureInfo.InvariantCulture,
                $"param_id {p.ParamId}: min_exclude {p.MinExclude.Sum(e => e.ByLat.Count)} node(s) in {p.MinExclude.Count} orbit group(s), max_co_freq {p.MaxCoFreqByLat.Count}, min_duration {p.MinDurationByLat.Count}, min_elev {p.MinElev.Sum(e => e.ByAz.Count)} node(s) at {p.MinElev.Count} latitude(s)");
            StatusText = "";
        }
        catch (Exception ex)
        {
            SummaryText = "";
            StatusText = ex.Message;
        }
    }

    /// <summary>The set the texts describe; throws with a line-precise message on bad input.</summary>
    public OperatingParamsSet BuildSet()
    {
        var p = new OperatingParamsSet
        {
            SatName = _satName,
            NtcId = ParseInt(_ntcIdText, "ntc_id") ?? 0,
            ParamId = ParseInt(_paramIdText, "param_id") ?? 1,
            LowFreqMhz = ParseDouble(_lowFreqText, "low_freq_mhz") ?? 0.0,
            HighFreqMhz = ParseDouble(_highFreqText, "high_freq_mhz") ?? 0.0,
            EsDensityPerKm2 = ParseDouble(_esDensityText, "es_density"),
            EsDistanceKm = ParseDouble(_esDistanceText, "es_distance"),
            EsLatMinDeg = ParseDouble(_esLatMinText, "es_lat_min") ?? -90.0,
            EsLatMaxDeg = ParseDouble(_esLatMaxText, "es_lat_max") ?? 90.0,
            MinAngleAtSatDeg = ParseDouble(_minAngleAtSatText, "min_angle_at_sat"),
            MinAngleAtEsDeg = ParseDouble(_minAngleAtEsText, "min_angle_at_es"),
            MaxCoFreqHeader = ParseInt(_maxCoFreqHeaderText, "max_co_freq"),
            MaxCoFreqSat = ParseInt(_maxCoFreqSatText, "max_co_freq_sat"),
            MinDurationSecHeader = ParseInt(_minDurationHeaderText, "min_duration"),
            ElevAngleHeaderDeg = ParseDouble(_elevAngleHeaderText, "elev_angle"),
        };

        foreach (var (parts, n) in Lines(_minExcludeText, "min_exclude", 3))
        {
            int orb = LineInt(parts[0], "min_exclude", n, "orb_id");
            var group = p.MinExclude.FirstOrDefault(e => e.OrbId == orb);
            if (group is null) { group = new MinExcludeByOrbit { OrbId = orb }; p.MinExclude.Add(group); }
            group.ByLat.Add((LineNum(parts[1], "min_exclude", n, "lat"),
                             LineNum(parts[2], "min_exclude", n, "alpha")));
        }
        foreach (var (parts, n) in Lines(_maxCoFreqText, "max_co_freq", 2))
            p.MaxCoFreqByLat.Add((LineNum(parts[0], "max_co_freq", n, "lat"),
                                  LineInt(parts[1], "max_co_freq", n, "value")));
        foreach (var (parts, n) in Lines(_minDurationText, "min_duration", 2))
            p.MinDurationByLat.Add((LineNum(parts[0], "min_duration", n, "lat"),
                                    LineInt(parts[1], "min_duration", n, "seconds")));
        foreach (var (parts, n) in Lines(_minElevText, "min_elev", 3))
        {
            double lat = LineNum(parts[0], "min_elev", n, "lat");
            var group = p.MinElev.FirstOrDefault(e => e.LatDeg == lat);
            if (group is null) { group = new MinElevByLat { LatDeg = lat }; p.MinElev.Add(group); }
            group.ByAz.Add((LineNum(parts[1], "min_elev", n, "az"),
                            LineNum(parts[2], "min_elev", n, "elev")));
        }
        return p;
    }

    public string BuildJson() => OpParamsFileCodec.Save(OpParamsFileCodec.FromSet(BuildSet()));

    public void LoadJson(string json)
        => ApplySet(OpParamsFileCodec.ToSet(OpParamsFileCodec.Load(json)));

    /// <summary>Populates every field and array text from a set (load, or the deriver's result).</summary>
    public void ApplySet(OperatingParamsSet p)
    {
        var inv = CultureInfo.InvariantCulture;
        SatName = p.SatName;
        NtcIdText = p.NtcId.ToString(inv);
        ParamIdText = p.ParamId.ToString(inv);
        LowFreqText = p.LowFreqMhz.ToString(inv);
        HighFreqText = p.HighFreqMhz.ToString(inv);
        EsDensityText = p.EsDensityPerKm2?.ToString(inv) ?? "";
        EsDistanceText = p.EsDistanceKm?.ToString(inv) ?? "";
        EsLatMinText = p.EsLatMinDeg.ToString(inv);
        EsLatMaxText = p.EsLatMaxDeg.ToString(inv);
        MinAngleAtSatText = p.MinAngleAtSatDeg?.ToString(inv) ?? "";
        MinAngleAtEsText = p.MinAngleAtEsDeg?.ToString(inv) ?? "";
        MaxCoFreqHeaderText = p.MaxCoFreqHeader?.ToString(inv) ?? "";
        MaxCoFreqSatText = p.MaxCoFreqSat?.ToString(inv) ?? "";
        MinDurationHeaderText = p.MinDurationSecHeader?.ToString(inv) ?? "";
        ElevAngleHeaderText = p.ElevAngleHeaderDeg?.ToString(inv) ?? "";
        MinExcludeText = string.Join("\n", p.MinExclude.SelectMany(
            e => e.ByLat.Select(v => Row(inv, e.OrbId.ToString(inv), v.LatDeg, v.AlphaDeg))));
        MaxCoFreqText = string.Join("\n", p.MaxCoFreqByLat.Select(
            v => Row(inv, v.LatDeg.ToString(inv), v.Value)));
        MinDurationText = string.Join("\n", p.MinDurationByLat.Select(
            v => Row(inv, v.LatDeg.ToString(inv), v.Seconds)));
        MinElevText = string.Join("\n", p.MinElev.SelectMany(
            e => e.ByAz.Select(v => Row(inv, e.LatDeg.ToString(inv), v.AzDeg, v.ElevDeg))));
    }

    /// <summary>Writes the R-set XML; writer validation errors propagate.</summary>
    public void ExportXml(string path) => OperParamsXmlWriter.Write(path, BuildSet());

    // ---- derivation from the simulated system ---------------------------

    private string _deriveDesignPath = "";
    public string DeriveDesignPath { get => _deriveDesignPath; set => SetField(ref _deriveDesignPath, value); }

    // The profile IS the system under measurement: it carries the
    // transmission basics (payload, power, gates, geography), so the
    // derivation refuses to fly a stand-in system without one.
    private string _deriveProfilePath = "";
    /// <summary>
    /// The operation profile (*.opprofile.json) -- required; it supplies
    /// the whole system side of the derivation.
    /// </summary>
    public string DeriveProfilePath { get => _deriveProfilePath; set => SetField(ref _deriveProfilePath, value); }

    private string _deriveDurationDaysText = "0.25";
    public string DeriveDurationDaysText { get => _deriveDurationDaysText; set => SetField(ref _deriveDurationDaysText, value); }

    private string _deriveStepSecText = "60";
    public string DeriveStepSecText { get => _deriveStepSecText; set => SetField(ref _deriveStepSecText, value); }

    private string _deriveLatBandText = "15";
    /// <summary>Latitude band width (deg) of the derived per-latitude arrays.</summary>
    public string DeriveLatBandText { get => _deriveLatBandText; set => SetField(ref _deriveLatBandText, value); }

    private bool _isDeriving;
    public bool IsDeriving
    {
        get => _isDeriving;
        private set { if (SetField(ref _isDeriving, value)) OnPropertyChanged(nameof(DeriveEnabled)); }
    }

    public bool DeriveEnabled => !_isDeriving;

    /// <summary>
    /// Simulates the real system and fills the whole designer from the
    /// measured envelope; identity and the frequency range come from the
    /// current header fields.
    /// </summary>
    public async System.Threading.Tasks.Task DeriveAsync()
    {
        OpParamsDeriver.Result result;
        IsDeriving = true;
        StatusText = "deriving: simulating the system...";
        try
        {
            result = await System.Threading.Tasks.Task.Run(() => DeriveCore());
        }
        catch (Exception ex)
        {
            StatusText = "derive failed: " + ex.Message;
            IsDeriving = false;
            return;
        }
        ApplySet(result.Set);
        StatusText = string.Create(CultureInfo.InvariantCulture,
            $"derived from {result.Steps} steps / {result.LinkSamples} link samples -- review, save or export");
        IsDeriving = false;
    }

    /// <summary>Synchronous derivation (the check harness calls this directly).</summary>
    public OpParamsDeriver.Result DeriveCore()
    {
        if (_deriveDesignPath.Trim().Length == 0)
            throw new InvalidOperationException("pick an orbit design document first");
        if (_deriveProfilePath.Trim().Length == 0)
            throw new InvalidOperationException(
                "pick an operation profile -- the system side (payload, gates, geography) comes from it");
        var doc = OrbitDesignFileCodec.LoadDocument(System.IO.File.ReadAllText(_deriveDesignPath));
        var shells = doc.Shells.Select(OrbitDesignFileCodec.ToShell).ToArray();

        double days = ParseDouble(_deriveDurationDaysText, "duration") ?? 0.25;
        double step = ParseDouble(_deriveStepSecText, "step") ?? 60.0;
        double latBand = ParseDouble(_deriveLatBandText, "lat band") ?? 15.0;
        if (days <= 0.0 || step <= 0.0 || latBand <= 0.0)
            throw new InvalidOperationException("duration, step and band must be positive");

        // The profile IS the system: measure its emergent behaviour.
        var prof = OperationProfileCodec.Load(System.IO.File.ReadAllText(_deriveProfilePath));
        shells = OperationComposer.ApplyToShells(prof, shells);
        var comp = OperationComposer.Compose(prof,
            shells[0].OperatingHeightKm ?? shells[0].AltitudeKm);
        return OpParamsDeriver.Derive(new Constellation(shells), comp.Geography,
            comp.Enforced, comp.Scene, days * 86400.0, step, latBand,
            _satName, ParseInt(_ntcIdText, "ntc_id") ?? 0, ParseInt(_paramIdText, "param_id") ?? 1,
            prof.Down.FrequencyGhz * 1000.0, prof.Down.FrequencyGhz * 1000.0,
            comp.Policy, comp.CoverageRadiusKm, comp.IlluminationDutyCycle);
    }

    // ---- parsing helpers ------------------------------------------------

    private static string Row(CultureInfo inv, string first, params object[] rest)
        => first + " " + string.Join(" ", rest.Select(r => Convert.ToString(r, inv)));

    private static double? ParseDouble(string text, string field)
    {
        if (text.Trim().Length == 0) return null;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            throw new FormatException($"{field}: '{text.Trim()}' is not a number");
        return v;
    }

    private static int? ParseInt(string text, string field)
    {
        if (text.Trim().Length == 0) return null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            throw new FormatException($"{field}: '{text.Trim()}' is not a whole number");
        return v;
    }

    private static System.Collections.Generic.IEnumerable<(string[] Parts, int LineNo)> Lines(
        string text, string array, int fields)
    {
        var raw = text.Split('\n');
        for (int i = 0; i < raw.Length; i++)
        {
            string line = raw[i].Trim();
            if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != fields)
                throw new FormatException($"{array} line {i + 1}: expected {fields} values, got {parts.Length}");
            yield return (parts, i + 1);
        }
    }

    private static double LineNum(string token, string array, int lineNo, string what)
        => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : throw new FormatException($"{array} line {lineNo}: {what} '{token}' is not a number");

    private static int LineInt(string token, string array, int lineNo, string what)
        => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v : throw new FormatException($"{array} line {lineNo}: {what} '{token}' is not a whole number");
}
