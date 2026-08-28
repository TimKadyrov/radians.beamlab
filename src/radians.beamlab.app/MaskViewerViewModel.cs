using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;

namespace radians.beamlab.app;

/// <summary>
/// State for the "Mask Viewer" tab: loads an S.1503-4 mask XML (the schema the
/// generator exports) and displays one latitude block at a time with the same
/// heatmap / profile plots as the generator tab. <see cref="Plot"/> is a
/// plot-options-only <see cref="PfdMaskViewModel"/> (IsExternalMask): its scene
/// never computes anything here, exclusion overlays are cleared, and the
/// profile-cut slider / axis labelling work unchanged.
/// </summary>
public sealed class MaskViewerViewModel : ObservableObject
{
    /// <summary>Plot options consumed by the shared renderers.</summary>
    public PfdMaskViewModel Plot { get; }

    /// <summary>The field the renderers draw -- filled from the loaded table.</summary>
    public PfdMaskField Field { get; } = new();

    /// <summary>Raised when a new mask / latitude block has been rasterised.</summary>
    public event Action? MaskChanged;

    private LoadedPfdMask? _mask;
    private string _fileName = "";

    public MaskViewerViewModel()
    {
        Plot = new PfdMaskViewModel { IsExternalMask = true };
        Plot.UseAdvancedExclusion = false;
        Plot.ExclusionRings.Clear();
        Plot.AlphaExclDeg = 0.0;      // no exclusion overlays on an imported mask
        Field.ExternalOnly = true;    // never compute from the plot VM's scene
        LoadCommand = new RelayCommand(BrowseAndLoad);
    }

    public ICommand LoadCommand { get; }

    private void BrowseAndLoad()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open PFD mask XML",
            Filter = "PFD mask XML (*.xml)|*.xml|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        LoadFile(dlg.FileName);
    }

    /// <summary>Load a mask file; parse errors land in <see cref="StatusText"/>.</summary>
    public void LoadFile(string path)
    {
        try
        {
            var mask = MaskXmlImport.Load(path);
            _mask = mask;
            _fileName = System.IO.Path.GetFileName(path);
            Plot.MaskKind = mask.Kind;
            if (mask.RefBwKHz > 0) Plot.RefBwKHz = mask.RefBwKHz;

            // Default to the block closest to the equator.
            int idx = 0;
            for (int i = 1; i < mask.Blocks.Count; i++)
                if (Math.Abs(mask.Blocks[i].LatDeg) < Math.Abs(mask.Blocks[idx].LatDeg)) idx = i;
            _selectedLatIndex = idx;

            // Auto-tick when the mask carries a detectable off-floor (its
            // minimum cannot be operational PFD). Backing field: ApplyBlock
            // below picks it up without a redundant rebuild.
            _treatMinAsCutoff = DetectedOffFloor(mask.Blocks[idx]) is not null;
            OnPropertyChanged(nameof(TreatMinAsCutoff));

            OnPropertyChanged(nameof(LatitudeItems));
            OnPropertyChanged(nameof(SelectedLatIndex));
            OnPropertyChanged(nameof(InfoReadout));
            StatusText = $"loaded {_fileName}: {mask.Blocks.Count} latitude block(s)";
            ApplyBlock();
        }
        catch (Exception ex)
        {
            StatusText = $"load failed: {ex.Message}";
        }
    }

    /// <summary>
    /// No physical downlink PFD can be this low: a block minimum below this
    /// is an "off" floor by construction (e.g. the -999 some filings use),
    /// while anything above it may be operational PFD and is never blanked.
    /// </summary>
    public const double OperationalFloorDb = -300.0;

    private void ApplyBlock()
    {
        if (_mask is null || _mask.Blocks.Count == 0) return;
        var blk = _mask.Blocks[Math.Clamp(_selectedLatIndex, 0, _mask.Blocks.Count - 1)];
        // Cut-off must be in place before SetMaskSource so the colour range follows.
        Field.UnreachableCutoffDb = _treatMinAsCutoff && DetectedOffFloor(blk) is double floor
            ? floor
            : MaskLatBlock.UnreachableDb;
        MaskXmlImport.ApplyBlockToField(_mask, blk, Field);
        OnPropertyChanged(nameof(MinPfdReadout));
        OnPropertyChanged(nameof(CanTreatMinAsCutoff));
        MaskChanged?.Invoke();
    }

    /// <summary>Smallest declared value above the schema sentinel, or +inf if none.</summary>
    private static double BlockMinPfd(MaskLatBlock blk)
    {
        double min = double.PositiveInfinity;
        foreach (var row in blk.Rows)
            foreach (double v in row.Values)
                if (v > MaskLatBlock.UnreachableDb && v < min) min = v;
        return min;
    }

    /// <summary>The block minimum iff it is an off-floor (below -300), else null.</summary>
    private static double? DetectedOffFloor(MaskLatBlock blk)
    {
        double min = BlockMinPfd(blk);
        return min < OperationalFloorDb ? min : null;
    }

    public string MinPfdReadout
    {
        get
        {
            if (_mask is null) return "";
            var blk = _mask.Blocks[Math.Clamp(_selectedLatIndex, 0, _mask.Blocks.Count - 1)];
            double min = BlockMinPfd(blk);
            if (double.IsPositiveInfinity(min)) return "Min PFD in block: (none)";
            return min < OperationalFloorDb
                ? $"Min PFD in block: {min:F1} dB(W/m²) — off-floor (below −300)"
                : $"Min PFD in block: {min:F1} dB(W/m²)";
        }
    }

    /// <summary>The checkbox only applies when the block minimum is an off-floor.</summary>
    public bool CanTreatMinAsCutoff
    {
        get
        {
            if (_mask is null || _mask.Blocks.Count == 0) return false;
            return DetectedOffFloor(_mask.Blocks[Math.Clamp(_selectedLatIndex, 0, _mask.Blocks.Count - 1)]) is not null;
        }
    }

    private bool _treatMinAsCutoff;
    /// <summary>
    /// Treat the block's detected off-floor (minimum below -300 dB(W/m^2)) as
    /// the unreachable cut-off. Set automatically when a mask with such a
    /// floor is loaded; has no effect when the minimum could be operational
    /// PFD (above -300).
    /// </summary>
    public bool TreatMinAsCutoff
    {
        get => _treatMinAsCutoff;
        set { if (SetField(ref _treatMinAsCutoff, value)) ApplyBlock(); }
    }

    /// <summary>Latitude choices for the ComboBox, one per by_a block.</summary>
    public IReadOnlyList<string> LatitudeItems =>
        _mask?.Blocks.Select(b => $"{b.LatDeg:0.###}°").ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

    private int _selectedLatIndex;
    public int SelectedLatIndex
    {
        get => _selectedLatIndex;
        set
        {
            if (_mask is null || value < 0 || value >= _mask.Blocks.Count) return;
            if (SetField(ref _selectedLatIndex, value)) ApplyBlock();
        }
    }

    public string InfoReadout
    {
        get
        {
            if (_mask is null) return "No mask loaded.";
            string type = _mask.Kind == MaskPlotKind.AlphaDeltaLong ? "alpha_deltaLongitude" : "azimuth_elevation";
            var blk = _mask.Blocks[Math.Clamp(_selectedLatIndex, 0, _mask.Blocks.Count - 1)];
            int minC = int.MaxValue, maxC = 0;
            foreach (var row in blk.Rows)
            {
                minC = Math.Min(minC, row.CNodes.Length);
                maxC = Math.Max(maxC, row.CNodes.Length);
            }
            string cText = minC == maxC ? $"{minC}" : $"{minC}–{maxC}";
            return $"{_fileName}\n" +
                   $"sat: {_mask.SatName}   ntc_id: {_mask.NtcId}   mask_id: {_mask.MaskId}\n" +
                   $"type: {type}\n" +
                   $"freq: {_mask.LowFreqMhz:0.###}–{_mask.HighFreqMhz:0.###} MHz   refBW: {_mask.RefBwKHz:0.###} kHz\n" +
                   $"latitudes: {_mask.Blocks.Count}   rows: {blk.Rows.Count} (b)   c-nodes/row: {cText}";
        }
    }

    private string _statusText = "Load a mask XML to view it.";
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }
}
