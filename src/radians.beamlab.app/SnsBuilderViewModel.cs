using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>One loaded orbit design (a shell of the notice).</summary>
public sealed record ShellEntry(string FilePath, OrbitDesignData Data)
{
    public string FileName => Path.GetFileName(FilePath);
    public string Summary => Data.Summary;
}

/// <summary>One mask registration: the XML source plus its mask_info row.</summary>
public sealed class MaskEntry : ObservableObject
{
    private int _maskId = 1;
    public int MaskId { get => _maskId; set => SetField(ref _maskId, value); }

    private string _filePath = "";
    public string FilePath { get => _filePath; set => SetField(ref _filePath, value); }

    private string _fMask = "P";
    /// <summary>P (pfd), E (ES eirp), S (satellite eirp) or R (operating parameters).</summary>
    public string FMask { get => _fMask; set => SetField(ref _fMask, value); }

    private string _fMaskType = "";
    public string FMaskType { get => _fMaskType; set => SetField(ref _fMaskType, value); }

    private double _freqMinMhz = 10700;
    public double FreqMinMhz { get => _freqMinMhz; set => SetField(ref _freqMinMhz, value); }

    private double _freqMaxMhz = 12750;
    public double FreqMaxMhz { get => _freqMaxMhz; set => SetField(ref _freqMaxMhz, value); }

    private string _linkOrbIdText = "";
    /// <summary>Link scope: orb_id (plane) this mask serves; empty = the whole constellation.</summary>
    public string LinkOrbIdText { get => _linkOrbIdText; set => SetField(ref _linkOrbIdText, value); }

    private string _linkSatIdText = "";
    /// <summary>Link scope: satellite number within the plane; empty = every satellite of the plane.</summary>
    public string LinkSatIdText { get => _linkSatIdText; set => SetField(ref _linkSatIdText, value); }

    private string _linkEsIdText = "";
    /// <summary>E masks: the specific earth station (e_as_id) this mask belongs to; empty = typical/all (-1).</summary>
    public string LinkEsIdText { get => _linkEsIdText; set => SetField(ref _linkEsIdText, value); }
}

/// <summary>One declared earth station row (e_as_stn) for specific-ES mask links.</summary>
public sealed class EsEntry : ObservableObject
{
    private int _eAsId = 1;
    public int EAsId { get => _eAsId; set => SetField(ref _eAsId, value); }

    private string _stnName = "ES-1";
    public string StnName { get => _stnName; set => SetField(ref _stnName, value); }

    private string _stnType = "S";
    /// <summary>'S' = specific (needs coordinates), 'T' = typical.</summary>
    public string StnType { get => _stnType; set => SetField(ref _stnType, value); }

    private string _latText = "45";
    public string LatText { get => _latText; set => SetField(ref _latText, value); }

    private string _lonText = "0";
    public string LonText { get => _lonText; set => SetField(ref _lonText, value); }

    private string _antDiamText = "";
    public string AntDiamText { get => _antDiamText; set => SetField(ref _antDiamText, value); }
}

/// <summary>One examination frequency range of the single built scenario.</summary>
public sealed class FreqEntry : ObservableObject
{
    private string _emiRcp = "E";
    /// <summary>E = emission (down / inter-satellite), R = reception (up).</summary>
    public string EmiRcp { get => _emiRcp; set => SetField(ref _emiRcp, value); }

    private double _freqMinMhz = 10700;
    public double FreqMinMhz { get => _freqMinMhz; set => SetField(ref _freqMinMhz, value); }

    private double _freqMaxMhz = 12750;
    public double FreqMaxMhz { get => _freqMaxMhz; set => SetField(ref _freqMaxMhz, value); }
}

/// <summary>
/// The SNS v10 builder: assembles a whole notice from separate elements --
/// orbit designs (shells), mask XMLs and derived operating-parameter sets
/// -- and writes the complete dataset (SRS + Masks databases). Version 1
/// links every space mask (P/S) and every ES mask (E) into one scenario at
/// whole-constellation granularity; R rows register as operating-parameter
/// sets. Pure state -- the window owns dialogs and the database writes.
/// </summary>
public sealed class SnsBuilderViewModel : ObservableObject
{
    private int _ntcId = 900000001;
    public int NtcId { get => _ntcId; set => SetField(ref _ntcId, value); }

    private string _satName = "DESIGN";
    public string SatName { get => _satName; set => SetField(ref _satName, value); }

    private string _adm = "XXX";
    public string Adm { get => _adm; set => SetField(ref _adm, value); }

    private string _scenarioName = "Scenario 1";
    public string ScenarioName { get => _scenarioName; set => SetField(ref _scenarioName, value); }

    public ObservableCollection<ShellEntry> Shells { get; } = new();
    public ObservableCollection<MaskEntry> Masks { get; } = new();
    public ObservableCollection<FreqEntry> Frequencies { get; } = new();
    public ObservableCollection<EsEntry> EarthStations { get; } = new();

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    // ---- the window's buttons; the window supplies the dialogs and opens the designer ----

    private const string DefaultDonorSrs =
        @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 SRS.MDB";
    private const string DefaultDonorMasks =
        @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 Masks.MDB";

    /// <summary>Supplied by the window: an open-file dialog over several files (filter -> paths, null when cancelled).</summary>
    public Func<string, string[]?>? PickOpenFiles { get; set; }
    /// <summary>Supplied by the window: a titled open-file dialog (title, filter -> path, null when cancelled).</summary>
    public Func<string, string, string?>? PickOpenFileTitled { get; set; }
    /// <summary>Supplied by the window: a save-file dialog (filter, file name -> path, null when cancelled).</summary>
    public Func<string, string, string?>? PickSaveFile { get; set; }
    /// <summary>Raised by Design operating parameters; the window opens the designer.</summary>
    public event Action? OpenOpParamsRequested;

    /// <summary>The grids' selected rows (object: a grid may select a row of another type while editing).</summary>
    public object? SelectedShellEntry { get; set; }
    public object? SelectedMask { get; set; }
    public object? SelectedEarthStation { get; set; }
    public object? SelectedFrequency { get; set; }

    private ICommand? _addShells, _removeShell, _addMasks, _removeMask, _openOpParams, _addEs, _removeEs,
        _addFreq, _removeFreq, _preview, _build;
    public ICommand AddShellsCommand => _addShells ??= new RelayCommand(AddShellFiles);
    public ICommand RemoveShellCommand => _removeShell ??= new RelayCommand(
        () => { if (SelectedShellEntry is ShellEntry s) Shells.Remove(s); });
    public ICommand AddMasksCommand => _addMasks ??= new RelayCommand(AddMaskFiles);
    public ICommand RemoveMaskCommand => _removeMask ??= new RelayCommand(
        () => { if (SelectedMask is MaskEntry m) Masks.Remove(m); });
    public ICommand OpenOpParamsCommand => _openOpParams ??= new RelayCommand(() => OpenOpParamsRequested?.Invoke());
    public ICommand AddEarthStationCommand => _addEs ??= new RelayCommand(AddEarthStation);
    public ICommand RemoveEarthStationCommand => _removeEs ??= new RelayCommand(
        () => { if (SelectedEarthStation is EsEntry s) EarthStations.Remove(s); });
    public ICommand AddFrequencyCommand => _addFreq ??= new RelayCommand(() => Frequencies.Add(new FreqEntry()));
    public ICommand RemoveFrequencyCommand => _removeFreq ??= new RelayCommand(
        () => { if (SelectedFrequency is FreqEntry f) Frequencies.Remove(f); });
    public ICommand PreviewCommand => _preview ??= new RelayCommand(() => StatusText = SummaryText());
    public ICommand BuildCommand => _build ??= new RelayCommand(Build);

    private void AddShellFiles()
    {
        if (PickOpenFiles?.Invoke("Orbit design (*.orbitdesign.json)|*.orbitdesign.json|JSON|*.json")
                is not string[] files) return;
        foreach (string f in files)
        {
            try { AddShellFile(f); }
            catch (Exception ex) { StatusText = $"{Path.GetFileName(f)}: {ex.Message}"; return; }
        }
        StatusText = SummaryText();
    }

    private void AddMaskFiles()
    {
        if (PickOpenFiles?.Invoke("Mask XML (*.xml)|*.xml") is not string[] files) return;
        int nextId = 1;
        foreach (var m in Masks) nextId = Math.Max(nextId, m.MaskId + 1);
        foreach (string f in files)
            Masks.Add(new MaskEntry { MaskId = nextId++, FilePath = f });
        StatusText = "set f_mask (P/E/S/R), type and the frequency range per row";
    }

    private void AddEarthStation()
    {
        int nextId = 1;
        foreach (var s in EarthStations) nextId = Math.Max(nextId, s.EAsId + 1);
        EarthStations.Add(new EsEntry { EAsId = nextId, StnName = $"ES-{nextId}" });
    }

    /// <summary>Build: the SRS database (and the Masks database when masks are registered) from donor databases.</summary>
    private void Build()
    {
        try
        {
            var notice = BuildNotice();   // validates

            string donorSrs = DefaultDonorSrs;
            if (!File.Exists(donorSrs) && !PickDonor("Select a donor SRS database", ref donorSrs)) return;
            if (PickSaveFile?.Invoke("SRS database (*.mdb)|*.mdb", $"{NtcId} SRS.MDB") is not string srsPath) return;
            SrsMdbWriter.WriteSrs(donorSrs, srsPath, notice);

            string masksNote = "";
            var contents = BuildMaskContents();
            if (contents.Count > 0)
            {
                string donorMasks = DefaultDonorMasks;
                if (!File.Exists(donorMasks) && !PickDonor("Select a donor Masks database", ref donorMasks)) return;
                string masksPath = Path.Combine(Path.GetDirectoryName(srsPath)!, $"{NtcId} Masks.MDB");
                var stored = SrsMdbWriter.WriteMasks(donorMasks, masksPath, NtcId, SatName, contents);
                var bad = stored.Where(r => r.Status != 0).ToList();
                masksNote = bad.Count == 0
                    ? $"; Masks: {stored.Count} row(s) -> {masksPath}"
                    : "; mask store FAILED: " + string.Join(",", bad.Select(r => $"{r.MaskId}:{r.Status}"));
            }
            StatusText = $"SRS written: {srsPath} ({notice.Orbits.Count} orbit / " +
                         $"{notice.Phases.Count} phase rows){masksNote}";
        }
        catch (Exception ex) { StatusText = "build failed: " + ex.Message; }
    }

    private bool PickDonor(string title, ref string path)
    {
        if (PickOpenFileTitled?.Invoke(title, "Database (*.mdb)|*.mdb") is not string picked) return false;
        path = picked;
        return true;
    }

    /// <summary>Loads a design file; a schema-4 document contributes all its shells.</summary>
    public void AddShellFile(string path)
    {
        var doc = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(path));
        foreach (var d in doc.Shells) Shells.Add(new ShellEntry(path, d));
    }

    /// <summary>The assembled notice: shells + mask registry + one auto-linked scenario.</summary>
    public SrsNotice BuildNotice()
    {
        if (Shells.Count == 0) throw new InvalidOperationException("add at least one orbit design");
        var n = new SrsNotice { NtcId = _ntcId, SatName = _satName, Adm = _adm };
        foreach (var sh in Shells)
        {
            var shell = OrbitDesignFileCodec.ToShell(sh.Data);
            // The filing carries the rate's magnitude; the inclination sets its direction.
            if (shell.PrecessionSupplied
                && !OrbitDesign.PrecessionMatchesInclination(shell.PrecessionRateDegPerSec, shell.InclinationDeg))
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"{sh.FileName}: a precession turning {(shell.PrecessionRateDegPerSec > 0 ? "east" : "west")} at i = {shell.InclinationDeg:F1} deg cannot be filed -- the filed magnitude turns the way the inclination implies"));
            n.AddShell(shell);
        }

        foreach (var m in Masks)
        {
            char fm = (m.FMask.Trim().ToUpperInvariant() + "P")[0];
            char? ft = string.IsNullOrWhiteSpace(m.FMaskType)
                ? null : char.ToUpperInvariant(m.FMaskType.Trim()[0]);
            n.MaskInfo.Add(new SrsMaskInfo(m.MaskId, m.FreqMinMhz, m.FreqMaxMhz, fm, ft));
            if (fm == 'R') n.OperatingParamIds.Add(m.MaskId);
        }

        foreach (var es in EarthStations)
            n.EarthStations.Add(new SrsEarthStation
            {
                EAsId = es.EAsId,
                StnName = es.StnName,
                StnType = (es.StnType.Trim().ToUpperInvariant() + "S")[0],
                LatDeg = OptNum(es.LatText, $"earth station {es.EAsId} latitude"),
                LonDeg = OptNum(es.LonText, $"earth station {es.EAsId} longitude"),
                AntDiamM = OptNum(es.AntDiamText, $"earth station {es.EAsId} dish"),
            });

        // Every pfd and e.i.r.p. mask links into scenario 1, and a scenario
        // needs a frequency range: without one the links would be dropped
        // silently. R sets link through mask_lnk3 and need no scenario.
        if (Frequencies.Count == 0
            && Masks.Any(m => (m.FMask.Trim().ToUpperInvariant() + "P")[0] is 'P' or 'S' or 'E'))
            throw new InvalidOperationException(
                "masks are registered but no scenario frequency range is entered -- every pfd and e.i.r.p. mask links into scenario 1, which needs at least one");

        if (Frequencies.Count > 0)
        {
            var sc = new SrsScenario { ScenId = 1, ScenName = _scenarioName };
            int seq = 1;
            foreach (var f in Frequencies)
                sc.Frequencies.Add(new SrsFreqRange(seq++,
                    (f.EmiRcp.Trim().ToUpperInvariant() + "E")[0], f.FreqMinMhz, f.FreqMaxMhz));
            int s1 = 1, s2 = 1;
            foreach (var m in Masks)
            {
                char fm = (m.FMask.Trim().ToUpperInvariant() + "P")[0];
                // Per-row link scope: empty orb = the whole constellation
                // (-1), empty sat = every satellite of the plane, empty
                // e_as = typical/all (-1).
                int orb = OptInt(m.LinkOrbIdText, $"mask {m.MaskId} orb link") ?? -1;
                int? sat = OptInt(m.LinkSatIdText, $"mask {m.MaskId} sat link");
                if (orb != -1 && n.Orbits.All(o => o.OrbId != orb))
                    throw new InvalidOperationException($"mask {m.MaskId}: orb link {orb} matches no orbit row");
                if (sat is not null && orb == -1)
                    throw new InvalidOperationException($"mask {m.MaskId}: a sat link needs an orb link");
                if (fm is 'P' or 'S')
                    sc.PfdMaskLinks.Add(new SrsMaskLink(s1++, m.MaskId, orb, sat));
                else if (fm == 'E')
                    sc.EsMaskLinks.Add(new SrsMaskLink(s2++, m.MaskId, orb, sat,
                        OptInt(m.LinkEsIdText, $"mask {m.MaskId} e_as link") ?? -1));
            }
            n.Scenarios.Add(sc);
        }

        n.Validate();
        return n;
    }

    private static double? OptNum(string text, string what)
        => text.Trim().Length == 0 ? null
            : double.TryParse(text, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : throw new FormatException($"{what}: '{text.Trim()}' is not a number");

    private static int? OptInt(string text, string what)
        => text.Trim().Length == 0 ? null
            : int.TryParse(text, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : throw new FormatException($"{what}: '{text.Trim()}' is not a whole number");

    /// <summary>The Masks-database content rows for the registered masks.</summary>
    public IReadOnlyList<SrsMdbWriter.MaskContent> BuildMaskContents()
        => Masks.Select(m => new SrsMdbWriter.MaskContent(m.MaskId, m.FilePath,
                (m.FMask.Trim().ToUpperInvariant() + "P")[0], m.FreqMinMhz, m.FreqMaxMhz))
            .ToList();

    public string SummaryText()
    {
        try
        {
            var n = BuildNotice();
            return string.Create(CultureInfo.InvariantCulture,
                $"{Shells.Count} shell(s) -> {n.Orbits.Count} orbit / {n.Phases.Count} phase rows; " +
                $"{n.MaskInfo.Count} mask_info row(s), {n.OperatingParamIds.Count} R set(s); " +
                $"{(n.Scenarios.Count > 0 ? n.Scenarios[0].Frequencies.Count : 0)} frequency range(s); " +
                $"{n.EarthStations.Count} earth station(s)");
        }
        catch (Exception ex) { return ex.Message; }
    }
}
