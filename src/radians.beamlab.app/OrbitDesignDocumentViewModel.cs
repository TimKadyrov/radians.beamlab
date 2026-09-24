using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// The Orbit Design document: the constellation's shells, each an
/// <see cref="OrbitDesignViewModel"/> (one target orbit, one case, one
/// Walker shell), plus the selection every working sub-tab edits. Save and
/// Load move the whole document through one *.orbitdesign.json (schema 4);
/// older single-shell files load as a one-shell document. The tab's
/// buttons are commands here; the view supplies the dialogs, the clipboard
/// and the builder window, and owns the drawing.
/// </summary>
public sealed class OrbitDesignDocumentViewModel : ObservableObject
{
    public ObservableCollection<OrbitDesignViewModel> Shells { get; } = new();

    private OrbitDesignViewModel _selectedShell;
    /// <summary>The shell the sub-tabs edit; never null (list selectors may push null while items churn -- ignored).</summary>
    public OrbitDesignViewModel SelectedShell
    {
        get => _selectedShell;
        set
        {
            if (value is null || !SetField(ref _selectedShell, value)) return;
            OnPropertyChanged(nameof(ShellHeaderText));
            RecomputePreview();
        }
    }

    public OrbitDesignDocumentViewModel()
    {
        _selectedShell = NewShell();
        Shells.Add(_selectedShell);
        Shells.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShellHeaderText));
            RecomputePreview();
            if (_showConstellationTrack) RecomputeOverlay();
        };
        RecomputePreview();
    }

    // ---- the tab's buttons ----

    /// <summary>Supplied by the view: an open-file dialog (filter -> path, null when cancelled).</summary>
    public Func<string, string?>? PickOpenFile { get; set; }
    /// <summary>Supplied by the view: a save-file dialog (filter, file name -> path, null when cancelled).</summary>
    public Func<string, string, string?>? PickSaveFile { get; set; }
    /// <summary>Supplied by the view: opens a document with the shell's default application.</summary>
    public Action<string>? OpenDocument { get; set; }
    /// <summary>Supplied by the view: puts text on the clipboard.</summary>
    public Action<string>? SetClipboardText { get; set; }
    /// <summary>Raised by Open SNS v10 builder; the view opens the window.</summary>
    public event Action? OpenSnsBuilderRequested;

    /// <summary>The two guide pages, null when the docs folder is not found (their buttons disable).</summary>
    public string? CasesGuidePath { get; } = GuideFile("orbit-design-cases.html");
    public string? SolverGuidePath { get; } = GuideFile("repeat-solver.html");

    private static string? GuideFile(string name)
    {
        string? docs = HomeViewModel.FindDocsDir(AppContext.BaseDirectory);
        string? path = docs is null ? null : System.IO.Path.Combine(docs, name);
        return path is not null && System.IO.File.Exists(path) ? path : null;
    }

    private int _innerTabIndex;
    /// <summary>The selected sub-tab (0 Start here, 1 Repeat solver, 2 Station-keeping cases, 3 Constellation).</summary>
    public int InnerTabIndex { get => _innerTabIndex; set => SetField(ref _innerTabIndex, value); }

    private ICommand? _casesGuide, _solverGuide, _goSolver, _goCases, _goConstellation, _addShell, _duplicateShell,
        _moveShellUp, _moveShellDown, _removeShell, _harmonize, _copyCaseSummary, _saveDesign, _loadDesign, _openSnsBuilder;
    public ICommand CasesGuideCommand => _casesGuide ??= new RelayCommand(
        () => { if (CasesGuidePath is string g) OpenDocument?.Invoke(g); }, () => CasesGuidePath is not null);
    public ICommand SolverGuideCommand => _solverGuide ??= new RelayCommand(
        () => { if (SolverGuidePath is string g) OpenDocument?.Invoke(g); }, () => SolverGuidePath is not null);
    public ICommand GoSolverCommand => _goSolver ??= new RelayCommand(() => InnerTabIndex = 1);
    public ICommand GoCasesCommand => _goCases ??= new RelayCommand(() => InnerTabIndex = 2);
    public ICommand GoConstellationCommand => _goConstellation ??= new RelayCommand(() => InnerTabIndex = 3);
    public ICommand AddShellCommand => _addShell ??= new RelayCommand(AddShell);
    public ICommand DuplicateShellCommand => _duplicateShell ??= new RelayCommand(DuplicateSelected);
    public ICommand MoveShellUpCommand => _moveShellUp ??= new RelayCommand(MoveSelectedUp);
    public ICommand MoveShellDownCommand => _moveShellDown ??= new RelayCommand(MoveSelectedDown);
    public ICommand RemoveShellCommand => _removeShell ??= new RelayCommand(RemoveSelected);
    public ICommand HarmonizeCommand => _harmonize ??= new RelayCommand(
        () => SelectedShell.SnsStatusText = HarmonizeRptPrd());
    public ICommand CopyCaseSummaryCommand => _copyCaseSummary ??= new RelayCommand(CopyCaseSummary);
    public ICommand SaveDesignCommand => _saveDesign ??= new RelayCommand(SaveDesign);
    public ICommand LoadDesignCommand => _loadDesign ??= new RelayCommand(LoadDesign);
    public ICommand OpenSnsBuilderCommand => _openSnsBuilder ??= new RelayCommand(() => OpenSnsBuilderRequested?.Invoke());

    private void CopyCaseSummary()
    {
        string text = SelectedShell.BuildCopyText();
        if (text.Length == 0 || SetClipboardText is null) return;
        SetClipboardText(text);
        SelectedShell.SnsStatusText = "case summary copied to the clipboard";
    }

    private void SaveDesign()
    {
        if (SaveBlocker() is string why) { SelectedShell.SnsStatusText = "design not saved: " + why; return; }
        if (PickSaveFile?.Invoke("Orbit design (*.orbitdesign.json)|*.orbitdesign.json",
                "design.orbitdesign.json") is not string path) return;
        System.IO.File.WriteAllText(path, BuildDocumentJson());
        SelectedShell.SnsStatusText = "design saved (" + Shells.Count + " shell(s)): " + path;
    }

    private void LoadDesign()
    {
        if (PickOpenFile?.Invoke("Orbit design (*.orbitdesign.json)|*.orbitdesign.json|JSON|*.json")
                is not string path) return;
        try
        {
            LoadDocumentJson(System.IO.File.ReadAllText(path));
            SelectedShell.SnsStatusText = "design loaded (" + Shells.Count + " shell(s)): " + path;
        }
        catch (Exception ex) { SelectedShell.SnsStatusText = "load failed: " + ex.Message; }
    }

    private OrbitDesignViewModel NewShell()
    {
        var vm = new OrbitDesignViewModel();
        vm.PropertyChanged += OnShellChanged;
        return vm;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OrbitDesignViewModel.OrbitRows)
            or nameof(OrbitDesignViewModel.PhaseRows))
            RecomputePreview();
        if (e.PropertyName == nameof(OrbitDesignViewModel.OrbitRows)
            && _showConstellationTrack)
            RecomputeOverlay();
    }

    public string ShellHeaderText
    {
        get
        {
            int i = Shells.IndexOf(_selectedShell);
            return $"editing shell {(i < 0 ? 1 : i + 1)} of {Shells.Count}";
        }
    }

    public void AddShell()
    {
        var vm = NewShell();
        Shells.Add(vm);
        SelectedShell = vm;
    }

    /// <summary>Deep copy of the selected shell through its own file form.</summary>
    public void DuplicateSelected()
    {
        var vm = NewShell();
        vm.LoadDesignJson(_selectedShell.BuildDesignJson());
        Shells.Add(vm);
        SelectedShell = vm;
    }

    /// <summary>Moves the selected shell one slot up; order numbers orb_id across shells.</summary>
    public void MoveSelectedUp()
    {
        int i = Shells.IndexOf(_selectedShell);
        if (i > 0) Shells.Move(i, i - 1);
    }

    /// <summary>Moves the selected shell one slot down.</summary>
    public void MoveSelectedDown()
    {
        int i = Shells.IndexOf(_selectedShell);
        if (i >= 0 && i < Shells.Count - 1) Shells.Move(i, i + 1);
    }

    /// <summary>Removes the selected shell; a document always keeps one.</summary>
    public void RemoveSelected()
    {
        if (Shells.Count <= 1) return;
        int i = Shells.IndexOf(_selectedShell);
        var removed = _selectedShell;
        removed.PropertyChanged -= OnShellChanged;
        Shells.Remove(removed);
        SelectedShell = Shells[Math.Clamp(i, 0, Shells.Count - 1)];
    }

    // ---- constellation-tab preview: selected shell or the whole document

    private bool _previewAllShells;
    /// <summary>false = the selected shell's tables; true = all shells combined the way the builder emits them.</summary>
    public bool PreviewAllShells
    {
        get => _previewAllShells;
        set { if (SetField(ref _previewAllShells, value)) RecomputePreview(); }
    }

    private IReadOnlyList<SrsOrbitRow> _previewOrbitRows = Array.Empty<SrsOrbitRow>();
    public IReadOnlyList<SrsOrbitRow> PreviewOrbitRows
    { get => _previewOrbitRows; private set => SetField(ref _previewOrbitRows, value); }

    private IReadOnlyList<SrsPhaseRow> _previewPhaseRows = Array.Empty<SrsPhaseRow>();
    public IReadOnlyList<SrsPhaseRow> PreviewPhaseRows
    { get => _previewPhaseRows; private set => SetField(ref _previewPhaseRows, value); }

    private string _previewStatusText = "";
    public string PreviewStatusText
    { get => _previewStatusText; private set => SetField(ref _previewStatusText, value); }

    private void RecomputePreview()
    {
        ConstellationRepeatText = BuildRepeatText();
        if (!_previewAllShells)
        {
            PreviewOrbitRows = _selectedShell.OrbitRows;
            PreviewPhaseRows = _selectedShell.PhaseRows;
            PreviewStatusText = "";
            return;
        }
        try
        {
            var n = BuildCombinedNotice();
            PreviewOrbitRows = n.Orbits;
            PreviewPhaseRows = n.Phases;
            PreviewStatusText = string.Create(CultureInfo.InvariantCulture,
                $"{Shells.Count} shell(s) -> {n.Orbits.Count} orbit row(s), {n.Phases.Count} phase row(s)");
        }
        catch (Exception ex)
        {
            PreviewOrbitRows = Array.Empty<SrsOrbitRow>();
            PreviewPhaseRows = Array.Empty<SrsPhaseRow>();
            PreviewStatusText = ex.Message;
        }
    }

    // ---- constellation-track overlay: every shell's pattern -------------

    private bool _showConstellationTrack;
    /// <summary>Track-map overlay: one declared cycle of every satellite of every shell.</summary>
    public bool ShowConstellationTrack
    {
        get => _showConstellationTrack;
        set { if (SetField(ref _showConstellationTrack, value)) RecomputeOverlay(); }
    }

    private IReadOnlyList<IReadOnlyList<(double LatDeg, double LonDeg)>> _overlaySegments
        = Array.Empty<IReadOnlyList<(double, double)>>();
    /// <summary>The overlay polylines; empty while the toggle is off.</summary>
    public IReadOnlyList<IReadOnlyList<(double LatDeg, double LonDeg)>> OverlaySegments
    {
        get => _overlaySegments;
        private set => SetField(ref _overlaySegments, value);
    }

    // Each shell flies its OWN declared cycle (its altitude, its rpt_prd);
    // the union is the constellation's ground pattern.
    private void RecomputeOverlay()
    {
        if (!_showConstellationTrack)
        {
            OverlaySegments = Array.Empty<IReadOnlyList<(double, double)>>();
            return;
        }
        var segs = new List<IReadOnlyList<(double, double)>>();
        int budget = 150000 / Math.Max(1, Shells.Count);
        foreach (var sh in Shells) segs.AddRange(sh.BuildShellTrackSegments(budget));
        OverlaySegments = segs;
    }

    // ---- constellation repeat period (Rec. A2.4 / D4.6) -----------------

    private string _constellationRepeatText = "";
    /// <summary>P_repeat readout: the LCM of the shells' declared cycles, or the A2.4/B5.1 mixed warning.</summary>
    public string ConstellationRepeatText
    { get => _constellationRepeatText; private set => SetField(ref _constellationRepeatText, value); }

    // P_repeat is the time for EVERY satellite, across every shell, to
    // return to the same position relative to the Earth: within a shell
    // that is its own declared cycle; across shells the LCM of them.
    private string BuildRepeatText()
    {
        var declared = Shells.Select(s => s.DeclaredRptSeconds).ToList();
        int nRep = declared.Count(v => v is not null);
        if (nRep == 0) return "";
        if (nRep < Shells.Count)
            return "shells mix repeating and non-repeating -- Rec. S.1503-4 A2.4/B5.1 wants all one or the other";
        long p = 1;
        foreach (var v in declared) p = Lcm(p, v!.Value);
        var (d, h, m, s2) = OrbitDesign.DecomposePeriod(p);
        string counts = string.Join(", ", Shells.Select(
            (sh, i) => FormattableString.Invariant($"{p / sh.DeclaredRptSeconds!.Value}x shell {i + 1}")));
        string text = FormattableString.Invariant(
            $"constellation repeat P_repeat = {d}d {h:00}:{m:00}:{s2:00} ({counts})");
        return d > 100
            ? text + " -- impractically long: harmonize or align the declared pairs"
            : text;
    }

    private static long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return a; }
    private static long Lcm(long a, long b) => a / Gcd(a, b) * b;

    /// <summary>
    /// Declares the common constellation period -- the LCM of every
    /// shell's own cycle -- as rpt_prd on every shell (A2.4: one repeat
    /// period appropriate for all satellites, including all
    /// sub-constellations). No-op unless every shell is Case 2 or 3 with a
    /// candidate; the returned line says what happened, or why nothing did.
    /// </summary>
    public string HarmonizeRptPrd()
    {
        var own = Shells.Select(s => s.OwnRptSeconds).ToList();
        int missing = own.FindIndex(v => v is null);
        if (own.Count == 0 || missing >= 0)
            return own.Count == 0 ? "nothing to harmonize: no shells"
                : $"rpt_prd not harmonized: shell {missing + 1} is not Case 2 or 3 with a repeating candidate, and one repeat period must hold for every shell";
        long p = 1;
        foreach (var v in own) p = Lcm(p, v!.Value);
        foreach (var s in Shells) s.HarmonizedRptSeconds = p;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"rpt_prd harmonized to {p} s on all {Shells.Count} shell(s)");
    }

    /// <summary>All shells in one preview notice, orb_id continuing across shells.</summary>
    public SrsNotice BuildCombinedNotice()
    {
        var n = new SrsNotice { NtcId = 0, SatName = "DESIGN", Adm = "XXX" };
        foreach (var s in Shells) n.AddShell(s.BuildShell());
        return n;
    }

    // ---- the document file (schema 4) -----------------------------------

    /// <summary>
    /// Why the document cannot be saved, or null. A saved design is what the
    /// SNS builder files, so a Case 2 shell is refused here instead of being
    /// shown red and filed anyway: it needs a selected repeating candidate
    /// (without one the preview and the builder read the shell differently)
    /// and a keep range inside the candidate's bounds.
    /// </summary>
    public string? SaveBlocker()
    {
        for (int i = 0; i < Shells.Count; i++)
        {
            var s = Shells[i];
            if (s.CaseChoice is not (1 or 2)) continue;
            if (s.SelectedSolution is null)
                return $"shell {i + 1} is Case {s.CaseChoice + 1} with no repeating candidate selected -- pick one on the Repeat solver";
            if (!s.KeepRangeValid)
                return $"shell {i + 1}: {s.KeepRangeHintText}";
            if (s.CaseChoice != 2) continue;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            // The filing carries the rate's magnitude; the inclination sets its direction.
            if (s.Case3RateDegPerSec is double r && !OrbitDesign.PrecessionMatchesInclination(r, s.InclinationDeg))
                return string.Create(inv,
                    $"shell {i + 1} is Case 3 with a rate turning {(r > 0 ? "east" : "west")} at i = {s.InclinationDeg:F1} deg, where the filed magnitude turns the other way (or, at 90 deg, no way): it cannot be filed -- use Case 2");
            // A typed rate must close the declared repeat within keep_rnge per cycle.
            if (s.Case3MismatchDegPerCycle is double mm && Math.Abs(mm) > s.KeepRangeDeg)
                return string.Create(inv,
                    $"shell {i + 1}: the typed Case 3 rate leaves the track {Math.Abs(mm):F3} deg from closing its repeat every cycle, more than keep_rnge {s.KeepRangeDeg:F3} deg -- clear it to use the closing rate, or correct it");
        }
        return null;
    }

    public string BuildDocumentJson()
        => OrbitDesignFileCodec.SaveDocument(new OrbitDesignDocument(4,
            Shells.Select(s => s.BuildDesignData()).ToList()));

    public void LoadDocumentJson(string json)
    {
        var doc = OrbitDesignFileCodec.LoadDocument(json);
        foreach (var s in Shells) s.PropertyChanged -= OnShellChanged;
        Shells.Clear();
        foreach (var d in doc.Shells)
        {
            var vm = NewShell();
            vm.LoadDesignJson(OrbitDesignFileCodec.Save(d));
            Shells.Add(vm);
        }
        SelectedShell = Shells[0];
    }
}
