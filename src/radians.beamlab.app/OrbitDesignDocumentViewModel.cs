using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// The Orbit Design document: the constellation's shells, each an
/// <see cref="OrbitDesignViewModel"/> (one target orbit, one case, one
/// Walker shell), plus the selection every working sub-tab edits. Save and
/// Load move the whole document through one *.orbitdesign.json (schema 4);
/// older single-shell files load as a one-shell document. Pure state --
/// the view owns dialogs and drawing.
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
    /// sub-constellations). No-op unless every shell is Case 2.
    /// </summary>
    public void HarmonizeRptPrd()
    {
        var own = Shells.Select(s => s.OwnRptSeconds).ToList();
        if (own.Count == 0 || own.Any(v => v is null)) return;
        long p = 1;
        foreach (var v in own) p = Lcm(p, v!.Value);
        foreach (var s in Shells) s.HarmonizedRptSeconds = p;
    }

    /// <summary>All shells in one preview notice, orb_id continuing across shells.</summary>
    public SrsNotice BuildCombinedNotice()
    {
        var n = new SrsNotice { NtcId = 0, SatName = "DESIGN", Adm = "XXX" };
        foreach (var s in Shells) n.AddShell(s.BuildShell());
        return n;
    }

    // ---- the document file (schema 4) -----------------------------------

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
