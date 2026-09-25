using System;
using System.Globalization;
using System.IO;
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
    public OpParamsViewModel()
    {
        Recompute();
        string? docs = HomeViewModel.FindDocsDir(AppContext.BaseDirectory);
        string? guide = docs is null ? null : Path.Combine(docs, "r-set-designer.html");
        GuidePath = guide is not null && File.Exists(guide) ? guide : null;
        OpenGuideCommand = new RelayCommand(() => { if (GuidePath is string g) OpenDocument?.Invoke(g); },
            () => GuidePath is not null);
        SaveCommand = new RelayCommand(SaveSet);
        LoadCommand = new RelayCommand(LoadSet);
        BrowseDeriveProfileCommand = new RelayCommand(() =>
        {
            if (PickOpenFile?.Invoke("Operation profile (*.opprofile.json)|*.opprofile.json|JSON|*.json") is string p)
                DeriveProfilePath = p;
        });
        DeriveCommand = new AsyncRelayCommand(DeriveAsync);
        ExportCommand = new RelayCommand(ExportSet);
    }

    // ---- the window's buttons: commands, with dialogs asked of the view ----

    /// <summary>Supplied by the window: an open-file dialog (filter -> path, null when cancelled).</summary>
    public Func<string, string?>? PickOpenFile { get; set; }
    /// <summary>Supplied by the window: a save-file dialog (filter, file name -> path).</summary>
    public Func<string, string, string?>? PickSaveFile { get; set; }
    /// <summary>Supplied by the window: opens a document with the shell.</summary>
    public Action<string>? OpenDocument { get; set; }

    /// <summary>The accompanying page, when the docs folder is found beside the app.</summary>
    public string? GuidePath { get; }

    public System.Windows.Input.ICommand OpenGuideCommand { get; }
    public System.Windows.Input.ICommand SaveCommand { get; }
    public System.Windows.Input.ICommand LoadCommand { get; }
    public System.Windows.Input.ICommand BrowseDeriveProfileCommand { get; }
    public System.Windows.Input.ICommand DeriveCommand { get; }
    public System.Windows.Input.ICommand ExportCommand { get; }

    /// <summary>Save: the set in the designer's format, a set filed in both forms flagged.</summary>
    private void SaveSet()
    {
        try
        {
            string json = BuildJson();
            if (PickSaveFile?.Invoke("Operating parameters (*.opparams.json)|*.opparams.json", "set.opparams.json") is not string path) return;
            File.WriteAllText(path, json);
            string conflict = FormConflictNote();
            StatusText = "saved: " + path + (conflict.Length > 0 ? " -- " + conflict : "");
        }
        catch (Exception ex) { StatusText = "save failed: " + ex.Message; }
    }

    /// <summary>Load: a set in the designer's format, a loop run's *.operparams.json included.</summary>
    private void LoadSet()
    {
        if (PickOpenFile?.Invoke("Operating parameters (*.opparams.json; a loop run's *.operparams.json)|*.opparams.json;*.operparams.json|JSON|*.json") is not string path) return;
        try
        {
            LoadJson(File.ReadAllText(path));
            StatusText = "loaded: " + path;
        }
        catch (Exception ex) { StatusText = "load failed: " + ex.Message; }
    }

    /// <summary>Export: the R-set XML, refused for an invalid set by the writer.</summary>
    private void ExportSet()
    {
        if (PickSaveFile?.Invoke("R-set XML (*.xml)|*.xml", "opparams.xml") is not string path) return;
        try
        {
            ExportXml(path);
            StatusText = "R XML written: " + path + " — register it in the SNS builder as an f_mask R row";
        }
        catch (Exception ex) { StatusText = "export failed: " + ex.Message; }
    }

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
            StatusText = FormConflictNote(p);
        }
        catch (Exception ex)
        {
            SummaryText = "";
            StatusText = ex.Message;
        }
    }

    /// <summary>
    /// The warning for a set that files a quantity in both header and array
    /// form -- an invalid filing, which Export refuses -- or empty.
    /// </summary>
    public static string FormConflictNote(OperatingParamsSet p)
        => DeclaredConstraints.FormConflicts(p) is { Count: > 0 } both
            ? "INVALID filing: filed in both header and array form -- " + string.Join("; ", both)
              + "; declare each quantity in one form only (Export refuses this set)"
            : "";

    /// <summary>The conflict warning for the set the texts describe now; empty when valid or unparseable.</summary>
    public string FormConflictNote()
    {
        try { return FormConflictNote(BuildSet()); }
        catch { return ""; }
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

    // Depth and latitude band are NOT designer inputs. They decide how much
    // of the system the envelope saw, which makes them part of the derivation
    // the compliance loop owns (measured exactly depth-stable on STEAM-2, but
    // that is a property of that system), and nothing in a text box would
    // tell the reader that.

    private bool _isDeriving;
    public bool IsDeriving
    {
        get => _isDeriving;
        private set { if (SetField(ref _isDeriving, value)) OnPropertyChanged(nameof(DeriveEnabled)); }
    }

    public bool DeriveEnabled => !_isDeriving;

    /// <summary>
    /// The compliance loop's own derived set for the selected profile, when one
    /// is on disk, newer than the profile it describes and made from that
    /// profile; otherwise null.
    ///
    /// The loop derives once, saturated, for the whole projection. Re-simulating
    /// here would spend minutes to produce a SECOND OPINION of the same system --
    /// and at a different depth, possibly a different one. So the button reads
    /// the run, and without one it says so.
    /// </summary>
    public string? FindLoopRunSet()
        => ResolveRunContext() is { } c ? LoopRunSetFor(c.Repo, c.Prof, _deriveProfilePath) : null;

    // The folder a loop run of the profile lives under, and the profile; null without a profile.
    private (string Repo, OperationProfile Prof)? ResolveRunContext()
    {
        if (_deriveProfilePath.Trim().Length == 0 || !File.Exists(_deriveProfilePath)) return null;
        // The folder Run loop writes into (ComplianceViewModel.RunRootFor).
        string repo = ComplianceViewModel.RunRootFor(_deriveProfilePath);
        try { return (repo, OperationProfileCodec.Load(File.ReadAllText(_deriveProfilePath))); }
        catch { return null; }
    }

    /// <summary>
    /// The decision on its own, with every path given: a loop run counts only
    /// when it exists AND is newer than the profile it claims to describe -- an
    /// older run describes a system that has since been edited -- AND, when the
    /// run keeps a copy of the profile it flew, that copy is this profile. Runs
    /// are found by profile name, so two profiles of one name would otherwise
    /// share a run.
    /// </summary>
    public static string? LoopRunSetFor(string repoDir, OperationProfile prof, string profilePath)
    {
        string path = ComplianceViewModel.RunSetJsonPath(repoDir, prof);
        if (!File.Exists(path) || !File.Exists(profilePath)) return null;
        if (File.GetLastWriteTimeUtc(path) <= File.GetLastWriteTimeUtc(profilePath)) return null;
        return RunIsOfOtherProfile(repoDir, prof, profilePath) ? null : path;
    }

    /// <summary>
    /// True when the run under this profile's name keeps a copy of a DIFFERENT
    /// profile (or one that cannot be read). A run without a copy is not
    /// judged here.
    /// </summary>
    public static bool RunIsOfOtherProfile(string repoDir, OperationProfile prof, string profilePath)
    {
        string copy = ComplianceViewModel.RunProfilePath(repoDir, prof);
        if (!File.Exists(copy) || !File.Exists(profilePath)) return false;
        try
        {
            string mine = OperationProfileCodec.Save(OperationProfileCodec.Load(File.ReadAllText(profilePath)));
            string flown = OperationProfileCodec.Save(OperationProfileCodec.Load(File.ReadAllText(copy)));
            return mine != flown;
        }
        catch { return true; }
    }

    /// <summary>
    /// Fills the designer from the compliance loop's derived set for the
    /// selected profile. It does NOT simulate: the loop derives once, saturated,
    /// for the whole projection, and a second derivation here -- at whatever
    /// depth a text box happened to hold -- would be a second opinion about the
    /// same system, free to disagree with the one the projection actually used.
    /// The identity (sat_name, ntc_id, param_id) and the band stay the
    /// designer's own: they belong to the filing, and the run carries only
    /// placeholders for them.
    /// </summary>
    public System.Threading.Tasks.Task DeriveAsync()
    {
        string? runSet = FindLoopRunSet();
        if (runSet is null)
        {
            StatusText = _deriveProfilePath.Trim().Length == 0
                ? "pick the operation profile whose compliance-loop run you want to fill from"
                : ResolveRunContext() is { } c && RunIsOfOtherProfile(c.Repo, c.Prof, _deriveProfilePath)
                    ? $"the compliance-loop run under the name '{ComplianceViewModel.RunName(c.Prof)}' was made from a different profile -- "
                      + "give this profile its own name, or run the loop for it"
                    : "no compliance-loop run for this profile (or it is older than the profile) -- "
                      + "run the compliance loop, which derives the declaration as its first step";
            return System.Threading.Tasks.Task.CompletedTask;
        }
        try
        {
            FillFromRun(File.ReadAllText(runSet));
        }
        catch (Exception ex)
        {
            StatusText = "could not read the run: " + ex.Message;
            return System.Threading.Tasks.Task.CompletedTask;
        }
        StatusText = string.Create(CultureInfo.InvariantCulture,
            $"filled from the compliance-loop run of {File.GetLastWriteTime(runSet):yyyy-MM-dd HH:mm} "
            + $"({Path.GetFileName(runSet)}) -- no simulation; this is the set the projection used, "
            + $"with the identity and band fields above kept as they were. Review, save or export");
        return System.Threading.Tasks.Task.CompletedTask;
    }
    /// <summary>
    /// Fills every field from a loop run's set, keeping the identity
    /// (sat_name, ntc_id, param_id) and the band: those belong to the filing,
    /// and the run carries only placeholders for them.
    /// </summary>
    public void FillFromRun(string runSetJson)
    {
        var identity = (_satName, _ntcIdText, _paramIdText, _lowFreqText, _highFreqText);
        LoadJson(runSetJson);
        (SatName, NtcIdText, ParamIdText, LowFreqText, HighFreqText) = identity;
    }

    /// <summary>Synchronous derivation (the check harness calls this directly).</summary>
    /// <param name="simDurSec">Probe duration. The caller owns it because it
    /// decides how much of the system the envelope actually saw.</param>
    public OpParamsDeriver.Result DeriveCore(double simDurSec, double stepSec, double latBandDeg)
    {
        if (_deriveDesignPath.Trim().Length == 0)
            throw new InvalidOperationException("pick an orbit design document first");
        if (_deriveProfilePath.Trim().Length == 0)
            throw new InvalidOperationException(
                "pick an operation profile -- the system side (payload, gates, geography) comes from it");
        var doc = OrbitDesignFileCodec.LoadDocument(System.IO.File.ReadAllText(_deriveDesignPath));
        var shells = doc.Shells.Select(OrbitDesignFileCodec.ToShell).ToArray();

        if (simDurSec <= 0.0 || stepSec <= 0.0 || latBandDeg <= 0.0)
            throw new InvalidOperationException("duration, step and band must be positive");

        // The profile IS the system: measure its emergent behaviour. ONE
        // derivation implementation, shared with the compliance loop -- the
        // designer used to compose and step its own, so the two could answer
        // differently about the same system. It is a SATURATED probe, because
        // a declaration is an envelope of what the system may do, not a
        // record of what one traffic sample happened to ask for.
        var prof = OperationProfileCodec.Load(System.IO.File.ReadAllText(_deriveProfilePath));
        return ComplianceViewModel.DeriveDeclared(shells, prof, simDurSec, stepSec, latBandDeg,
            _satName, ParseInt(_ntcIdText, "ntc_id") ?? 0, ParseInt(_paramIdText, "param_id") ?? 1,
            // the designer declares one direction: the band is the profile's downlink carrier
            prof.Down.FrequencyGhz * 1000.0, prof.Down.FrequencyGhz * 1000.0);
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
