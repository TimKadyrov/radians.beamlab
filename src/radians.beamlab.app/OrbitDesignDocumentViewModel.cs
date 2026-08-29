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
