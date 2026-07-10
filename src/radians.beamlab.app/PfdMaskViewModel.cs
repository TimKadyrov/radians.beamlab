using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// State + operations for the "PFD mask (Az/El)" tab. Fully independent of
/// <see cref="MainViewModel"/> -- owns its own <see cref="SceneModel"/> so
/// altitude / lat/lon / antenna edits here don't touch the composite-gain-map
/// tab, and vice-versa.
///
/// Pattern kind is locked to S.1528-1 Sec. 1.4 Taylor elliptical with 3GPP-style
/// hex tessellation (Scene.AutoMode = true). Beam gating replaces the box /
/// country exclusion of the first tab:
///   * beams whose footprint's user-elevation angle is below <see cref="MinElevDeg"/>
///     are already dropped inside <see cref="SceneModel.RebuildBeams"/>;
///   * a post-pass here also switches off beams whose ground footprint sits
///     within <see cref="AlphaExclDeg"/> of the nearest visible GSO satellite
///     (S.1503-4 Sec. D6 avoidance angle).
/// </summary>
public sealed class PfdMaskViewModel : ObservableObject
{
    public SceneModel Scene { get; } = new()
    {
        PatternKind = BeamPatternKind.Taylor_1p4_Ell,
        AutoMode = true,
        FrequencyGHz = 12.0,      // Ku downlink -- where the S.1503 PFD-mask concept typically bites
        GmDbi = 35.0,
        CellRadiusKm = 250.0,
        EllRollOffDb = 3.0,
        TaylorSlrDb = 20.0,
        TaylorNbar = 4,
        LfDbi = 0.0,
        MinElevDeg = 10.0,
        AltitudeKm = 1200.0,
        SubSatLatDeg = 0.0,
        SubSatLonDeg = 0.0,
        Mode = HeatmapMode.PowerSum,
    };

    /// <summary>Coastline / country data -- shared with tab 1 when supplied, else lazily loaded here.</summary>
    public CoastlineDataProvider Coastlines { get; }

    /// <summary>Raised whenever the beam set or a plot input changes.</summary>
    public event Action? SceneChanged;

    public PfdMaskViewModel(CoastlineDataProvider? coastlines = null)
    {
        Coastlines = coastlines ?? new CoastlineDataProvider();

        // Seed the advanced-exclusion list with a sensible starter (matches the
        // basic alpha_excl default) so the dialog is never empty.
        ExclusionRings.Add(new AlphaExclusionRing { OuterDeg = 10.0, IsOff = true });
        ExclusionRings.CollectionChanged += OnExclusionRingsChanged;
        HookRing(ExclusionRings[0]);

        Rebuild();
        RefreshCommand           = new RelayCommand(Rebuild);
        EditExclusionRingsCommand = new RelayCommand(() => EditExclusionRingsRequested?.Invoke());
        AddExclusionRingCommand   = new RelayCommand(AddExclusionRing);
        RemoveExclusionRingCommand = new RelayCommand(RemoveSelectedExclusionRing);
        GenerateXmlCommand        = new RelayCommand(() => GenerateXmlRequested?.Invoke());
    }

    public ICommand RefreshCommand { get; }
    /// <summary>Ask the view to open the exclusion-rings dialog.</summary>
    public ICommand EditExclusionRingsCommand { get; }
    /// <summary>Append a new ring beyond the current outermost.</summary>
    public ICommand AddExclusionRingCommand { get; }
    /// <summary>Remove <see cref="SelectedExclusionRing"/>.</summary>
    public ICommand RemoveExclusionRingCommand { get; }
    /// <summary>Ask the view to open the mask-XML export dialog.</summary>
    public ICommand GenerateXmlCommand { get; }

    /// <summary>Raised when the user clicks "Edit alpha rings..."; the view opens the modal dialog.</summary>
    public event Action? EditExclusionRingsRequested;
    /// <summary>Raised when the user clicks "Generate mask XML..."; the view opens the export dialog.</summary>
    public event Action? GenerateXmlRequested;

    /// <summary>
    /// Copy every compute-affecting setting into another VM instance (used to
    /// build an independent generation VM for the XML exporter, so the live
    /// view is not disturbed). Exclusion rings are deep-copied.
    /// </summary>
    public void CopySettingsTo(PfdMaskViewModel d)
    {
        d.Scene.AltitudeKm   = Scene.AltitudeKm;
        d.Scene.SubSatLonDeg = Scene.SubSatLonDeg;
        d.Scene.FrequencyGHz = Scene.FrequencyGHz;
        d.Scene.GmDbi        = Scene.GmDbi;
        d.Scene.CellRadiusKm = Scene.CellRadiusKm;
        d.Scene.EllRollOffDb = Scene.EllRollOffDb;
        d.Scene.TaylorSlrDb  = Scene.TaylorSlrDb;
        d.Scene.TaylorNbar   = Scene.TaylorNbar;
        d.Scene.LfDbi        = Scene.LfDbi;
        d.Scene.MinElevDeg   = Scene.MinElevDeg;

        d._alphaExclDeg        = _alphaExclDeg;
        d._txEirpDbw           = _txEirpDbw;
        d._refBwKHz            = _refBwKHz;
        d._maskStepDeg         = _maskStepDeg;
        d._powerMode           = _powerMode;
        d._aggregation         = _aggregation;
        d._reuseClusterIndex         = _reuseClusterIndex;
        d._maskKind            = _maskKind;
        d._useAdvancedExclusion = _useAdvancedExclusion;

        d.ExclusionRings.Clear();
        foreach (var r in ExclusionRings)
            d.ExclusionRings.Add(new AlphaExclusionRing { OuterDeg = r.OuterDeg, IsOff = r.IsOff, AttenDb = r.AttenDb });
    }

    /// <summary>Rebuild beams + exclusion without raising events -- for the XML exporter's per-latitude sweep.</summary>
    public void RebuildForCompute()
    {
        Scene.RebuildBeams();
        ApplyAlphaExclusion();
    }

    // --- Orbit / antenna, mirrored to Scene ---

    public double AltitudeKm
    {
        get => Scene.AltitudeKm;
        set { if (Scene.AltitudeKm != value) { Scene.AltitudeKm = value; OnSceneChanged(rebuild: true); } }
    }

    public double SubSatLatDeg
    {
        get => Scene.SubSatLatDeg;
        set { if (Scene.SubSatLatDeg != value) { Scene.SubSatLatDeg = value; OnSceneChanged(rebuild: true); } }
    }

    public double SubSatLonDeg
    {
        get => Scene.SubSatLonDeg;
        set { if (Scene.SubSatLonDeg != value) { Scene.SubSatLonDeg = value; OnSceneChanged(rebuild: true); } }
    }

    public double FrequencyGHz
    {
        get => Scene.FrequencyGHz;
        set
        {
            if (Scene.FrequencyGHz == value) return;
            Scene.FrequencyGHz = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FrequencyReadout));
            OnSceneChanged(rebuild: true);
        }
    }

    public double GmDbi
    {
        get => Scene.GmDbi;
        set { if (Scene.GmDbi != value) { Scene.GmDbi = value; OnSceneChanged(rebuild: true); } }
    }

    public double CellRadiusKm
    {
        get => Scene.CellRadiusKm;
        set { if (Scene.CellRadiusKm != value && value > 0) { Scene.CellRadiusKm = value; OnSceneChanged(rebuild: true); } }
    }

    public double EllRollOffDb
    {
        get => Scene.EllRollOffDb;
        set { if (Scene.EllRollOffDb != value && value > 0) { Scene.EllRollOffDb = value; OnSceneChanged(rebuild: true); } }
    }

    public double TaylorSlrDb
    {
        get => Scene.TaylorSlrDb;
        set { if (Scene.TaylorSlrDb != value && value > 0) { Scene.TaylorSlrDb = value; OnSceneChanged(rebuild: true); } }
    }

    public int TaylorNbar
    {
        get => Scene.TaylorNbar;
        set { if (Scene.TaylorNbar != value && value >= 2 && value <= 6) { Scene.TaylorNbar = value; OnSceneChanged(rebuild: true); } }
    }

    public double LfDbi
    {
        get => Scene.LfDbi;
        set { if (Scene.LfDbi != value) { Scene.LfDbi = value; OnSceneChanged(rebuild: true); } }
    }

    /// <summary>
    /// Minimum served user elevation eps_min (deg). Gates which beams are ON
    /// (outermost ring / hex extent) and draws the cyan guide on the profile
    /// plot. PFD itself is evaluated over the whole visible disc -- side lobes
    /// radiate below eps_min too.
    /// </summary>
    public double MinElevDeg
    {
        get => Scene.MinElevDeg;
        set { if (Scene.MinElevDeg != value && value >= 0 && value < 90) { Scene.MinElevDeg = value; OnSceneChanged(rebuild: true); } }
    }

    // --- PFD-plot-specific inputs (not shared with SceneModel) ---

    private double _alphaExclDeg = 10.0;
    /// <summary>GSO avoidance angle alpha_excl (deg, S.1503-4 Sec. D6). Basic mode: beams whose footprint sits inside |alpha| &lt; this get switched off.</summary>
    public double AlphaExclDeg
    {
        get => _alphaExclDeg;
        set { if (SetField(ref _alphaExclDeg, value)) OnSceneChanged(rebuild: true); }
    }

    private bool _useAdvancedExclusion;
    /// <summary>
    /// When true the exclusion uses the multi-ring <see cref="ExclusionRings"/>
    /// (each ring switches beams off or attenuates them); when false the single
    /// <see cref="AlphaExclDeg"/> hard cut-off is used (original behaviour).
    /// </summary>
    public bool UseAdvancedExclusion
    {
        get => _useAdvancedExclusion;
        set
        {
            if (SetField(ref _useAdvancedExclusion, value))
            {
                OnPropertyChanged(nameof(ExclusionSummary));
                Rebuild();
            }
        }
    }

    /// <summary>Concentric alpha exclusion rings (advanced mode), sorted by outer edge when applied.</summary>
    public ObservableCollection<AlphaExclusionRing> ExclusionRings { get; } = new();

    private AlphaExclusionRing? _selectedExclusionRing;
    /// <summary>Row selected in the exclusion-rings dialog (for the Remove command).</summary>
    public AlphaExclusionRing? SelectedExclusionRing
    {
        get => _selectedExclusionRing;
        set => SetField(ref _selectedExclusionRing, value);
    }

    /// <summary>One-line description of the active exclusion configuration.</summary>
    public string ExclusionSummary => UseAdvancedExclusion
        ? $"advanced: {ExclusionRings.Count} ring(s)"
        : $"basic: |α| < {AlphaExclDeg:F1}° off";

    /// <summary>
    /// Exclusion bands sorted by outer alpha edge, used by both the beam-gating
    /// pass and the plot tint/guides. Basic mode reduces to a single off band
    /// at <see cref="AlphaExclDeg"/> (empty when alpha_excl <= 0).
    /// </summary>
    public IReadOnlyList<ExclusionBand> ExclusionBandsSorted()
    {
        if (UseAdvancedExclusion)
        {
            return ExclusionRings
                .Where(r => r.OuterDeg > 0.0)
                .OrderBy(r => r.OuterDeg)
                .Select(r => new ExclusionBand(r.OuterDeg, r.IsOff, r.AttenDb))
                .ToList();
        }
        return _alphaExclDeg > 0.0
            ? new[] { new ExclusionBand(_alphaExclDeg, IsOff: true, AttenDb: 0.0) }
            : Array.Empty<ExclusionBand>();
    }

    /// <summary>
    /// The exclusion band a footprint / pixel |alpha| falls in (the innermost band
    /// whose outer edge it is under), or null if unaffected. Concentric bands
    /// from alpha = 0 outward.
    /// </summary>
    public static ExclusionBand? BandFor(IReadOnlyList<ExclusionBand> bands, double alphaDeg)
    {
        for (int i = 0; i < bands.Count; i++)
            if (alphaDeg < bands[i].OuterDeg) return bands[i];   // bands are sorted ascending
        return null;
    }

    private void AddExclusionRing()
    {
        double outer = ExclusionRings.Count > 0 ? ExclusionRings.Max(r => r.OuterDeg) + 5.0 : 10.0;
        ExclusionRings.Add(new AlphaExclusionRing { OuterDeg = outer, IsOff = false, AttenDb = 6.0 });
    }

    private void RemoveSelectedExclusionRing()
    {
        if (SelectedExclusionRing is { } ring) ExclusionRings.Remove(ring);
    }

    private void OnExclusionRingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (AlphaExclusionRing r in e.OldItems) r.PropertyChanged -= OnRingPropertyChanged;
        if (e.NewItems != null) foreach (AlphaExclusionRing r in e.NewItems) HookRing(r);
        OnPropertyChanged(nameof(ExclusionSummary));
        if (UseAdvancedExclusion) Rebuild();
    }

    private void HookRing(AlphaExclusionRing r) => r.PropertyChanged += OnRingPropertyChanged;

    private void OnRingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (UseAdvancedExclusion) Rebuild();
    }

    private double _txEirpDbw = 0.0;
    /// <summary>
    /// Transmit power per beam in the reference bandwidth (dBW). In
    /// <see cref="BeamPowerMode.ConstantEirp"/> every beam gets exactly this;
    /// in <see cref="BeamPowerMode.ConstantBoresightPfd"/> it is the
    /// nadir-reference power and beam k gets +20*log10(slant_k / altitude) so
    /// each beam's boresight PFD stays constant despite spreading loss.
    /// </summary>
    public double TxEirpDbw
    {
        get => _txEirpDbw;
        set { if (SetField(ref _txEirpDbw, value)) SceneChanged?.Invoke(); }
    }

    private BeamPowerMode _powerMode = BeamPowerMode.ConstantEirp;
    /// <summary>Per-beam transmit power policy -- see <see cref="BeamPowerMode"/>.</summary>
    public BeamPowerMode PowerMode
    {
        get => _powerMode;
        set
        {
            if (_powerMode == value) return;
            _powerMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConstantEirpMode));
            OnPropertyChanged(nameof(IsConstantPfdMode));
            SceneChanged?.Invoke();
        }
    }

    public bool IsConstantEirpMode
    {
        get => PowerMode == BeamPowerMode.ConstantEirp;
        set { if (value) PowerMode = BeamPowerMode.ConstantEirp; }
    }

    public bool IsConstantPfdMode
    {
        get => PowerMode == BeamPowerMode.ConstantBoresightPfd;
        set { if (value) PowerMode = BeamPowerMode.ConstantBoresightPfd; }
    }

    private PfdAggregation _aggregation = PfdAggregation.PowerSum;
    /// <summary>
    /// Aggregation across beams for the mask PFD -- see <see cref="PfdAggregation"/>.
    /// </summary>
    public PfdAggregation Aggregation
    {
        get => _aggregation;
        set
        {
            if (_aggregation == value) return;
            _aggregation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPowerSumMode));
            OnPropertyChanged(nameof(IsCoChannelMode));
            SceneChanged?.Invoke();
        }
    }

    public bool IsPowerSumMode
    {
        get => Aggregation == PfdAggregation.PowerSum;
        set { if (value) Aggregation = PfdAggregation.PowerSum; }
    }

    public bool IsCoChannelMode
    {
        get => Aggregation == PfdAggregation.CoChannelSum;
        set { if (value) Aggregation = PfdAggregation.CoChannelSum; }
    }

    private static readonly int[] ReuseClusterSizes = { 3, 4, 7 };
    private int _reuseClusterIndex;
    /// <summary>ComboBox index into the standard hex reuse cluster sizes {3, 4, 7}.</summary>
    public int ReuseClusterIndex
    {
        get => _reuseClusterIndex;
        set
        {
            if (value < 0 || value >= ReuseClusterSizes.Length) return;
            if (SetField(ref _reuseClusterIndex, value))
            {
                OnPropertyChanged(nameof(ReuseClusterSize));
                SceneChanged?.Invoke();
            }
        }
    }

    /// <summary>Hex reuse cluster size N (= number of co-frequency colours) for <see cref="PfdAggregation.CoChannelSum"/>.</summary>
    public int ReuseClusterSize => ReuseClusterSizes[_reuseClusterIndex];

    private double _refBwKHz = 40.0;
    /// <summary>Reference bandwidth (kHz) used to interpret the EIRP. Purely informational for the plot legend.</summary>
    public double RefBwKHz
    {
        get => _refBwKHz;
        set { if (SetField(ref _refBwKHz, value)) SceneChanged?.Invoke(); }
    }

    // --- Mask type ---

    private MaskPlotKind _maskKind = MaskPlotKind.AzEl;
    /// <summary>
    /// Which S.1503-4 PFD-mask coordinate system the heatmap / profile use:
    /// satellite-frame az/el (Sec. D6.4.5) or signed alpha / deltaLongitude (Sec. D6.4.4).
    /// Switching re-samples the whole field.
    /// </summary>
    public MaskPlotKind MaskKind
    {
        get => _maskKind;
        set
        {
            if (_maskKind == value) return;
            _maskKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAzElMask));
            OnPropertyChanged(nameof(IsAlphaDeltaMask));
            OnPropertyChanged(nameof(ProfileCutLabel));
            OnPropertyChanged(nameof(ProfileCutMinDeg));
            OnPropertyChanged(nameof(ProfileCutMaxDeg));
            // Re-clamp the cut into the new kind's range (no-op when already inside).
            ProfileCutDeg = _profileCutDeg;
            SceneChanged?.Invoke();
        }
    }

    public bool IsAzElMask
    {
        get => MaskKind == MaskPlotKind.AzEl;
        set { if (value) MaskKind = MaskPlotKind.AzEl; }
    }

    public bool IsAlphaDeltaMask
    {
        get => MaskKind == MaskPlotKind.AlphaDeltaLong;
        set { if (value) MaskKind = MaskPlotKind.AlphaDeltaLong; }
    }

    // --- Display ---

    private double _maskStepDeg = 1.0;
    /// <summary>
    /// Angular step (deg) for the az/el rasterisation grid. Also drives the
    /// (beta_sub, gamma) sample density so ~1 sample lands per pixel on average.
    /// Smaller = sharper plot but slower rebuild; typical range 0.25 deg..2 deg.
    /// </summary>
    public double MaskStepDeg
    {
        get => _maskStepDeg;
        set { if (value > 0.05 && value <= 5.0 && SetField(ref _maskStepDeg, value)) SceneChanged?.Invoke(); }
    }

    /// <summary>
    /// Sampling guidance shown under the mask-step input: near boresight the
    /// main lobe is ~parabolic in dB, so a grid node at most step/sqrt(2) off
    /// a peak under-reads by ~3*(2d/theta3dB)^2 dB. A step of a quarter of the
    /// narrowest 3 dB beamwidth keeps that under ~0.4 dB. Beam boresights
    /// themselves are always sampled exactly (peak injection in
    /// <see cref="PfdMaskField"/>), so this bounds the error between peaks.
    /// </summary>
    public string MaskStepHint
    {
        get
        {
            double rec = RecommendedStepDeg;
            if (rec <= 0.0) return "";
            return $"Narrowest 3 dB beamwidth ≈ {4.0 * rec:F1}° → step ≤ {rec:F2}° recommended. " +
                   "Beam peaks are always sampled exactly regardless of step.";
        }
    }

    /// <summary>
    /// Quarter of the narrowest active 3 dB beamwidth (deg) -- the sampling
    /// step that keeps the field within ~0.4 dB of the true pattern between
    /// the exactly-sampled boresights. 0 when no active beams.
    /// </summary>
    public double RecommendedStepDeg
    {
        get
        {
            double minWidthDeg = double.PositiveInfinity;
            foreach (var beam in Scene.Beams)
            {
                if (beam.Weight <= 0.0) continue;
                double w = beam.Pattern is Rec1528_1p4_Ell ell
                    ? 2.0 * Math.Min(ell.ThetaB, ell.ThetaBTransverseDeg)
                    : 2.0 * beam.Pattern.ThetaB;
                if (w < minWidthDeg) minWidthDeg = w;
            }
            return double.IsPositiveInfinity(minWidthDeg) ? 0.0 : 0.25 * minWidthDeg;
        }
    }

    private bool _showAlphaContour = true;
    public bool ShowAlphaContour
    {
        get => _showAlphaContour;
        set { if (SetField(ref _showAlphaContour, value)) SceneChanged?.Invoke(); }
    }

    private bool _footprintsEnabled = true;
    /// <summary>Draw 3-dB footprint rings on the geo map (top pane of the PFD tab).</summary>
    public bool FootprintsEnabled
    {
        get => _footprintsEnabled;
        set { if (SetField(ref _footprintsEnabled, value)) SceneChanged?.Invoke(); }
    }

    private double _profileCutDeg;
    /// <summary>
    /// Coordinate the profile plot slices at: sat-frame azimuth for the AzEl
    /// mask (a vertical cut -> PFD vs elevation), signed alpha for the
    /// alpha/deltaLongitude mask (a horizontal cut -> PFD vs deltaLongitude, one mask-table
    /// row). Changing it does NOT rebuild the heatmap -- it only re-slices the
    /// retained grid, so the setter raises the lightweight
    /// <see cref="ProfileCursorChanged"/> rather than <see cref="SceneChanged"/>.
    /// </summary>
    public double ProfileCutDeg
    {
        get => _profileCutDeg;
        set
        {
            double clamped = Math.Clamp(value, ProfileCutMinDeg, ProfileCutMaxDeg);
            if (SetField(ref _profileCutDeg, clamped))
            {
                OnPropertyChanged(nameof(ProfileCutReadout));
                ProfileCursorChanged?.Invoke();
            }
        }
    }

    /// <summary>Slider lower bound for <see cref="ProfileCutDeg"/> (azimuth or alpha -- +/-90 deg in both modes).</summary>
    public double ProfileCutMinDeg => -90.0;
    /// <summary>Slider upper bound for <see cref="ProfileCutDeg"/>.</summary>
    public double ProfileCutMaxDeg => 90.0;

    public string ProfileCutLabel => MaskKind == MaskPlotKind.AlphaDeltaLong ? "α cut" : "Azimuth cut";

    public string ProfileCutReadout =>
        (MaskKind == MaskPlotKind.AlphaDeltaLong ? "α = " : "az = ") + $"{_profileCutDeg:+0;-0;0}°";

    /// <summary>Raised when only the profile cut cursor moved -- cheap redraw, no heatmap rebuild.</summary>
    public event Action? ProfileCursorChanged;

    private string _statusText = "ready";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    // --- Read-outs ---

    public string FrequencyReadout =>
        $"GSO min elev (S.1503-4 Table 8) = {GsoGeometry.GsoMinElevationDeg(FrequencyGHz):F0}°";

    public string LayoutReadout =>
        $"{Scene.Beams.Count} beams total, {ActiveBeamCount} active after α/ε gating   " +
        $"(α_excl = {AlphaExclDeg:F1}°, ε_min = {MinElevDeg:F1}°)";

    public int ActiveBeamCount
    {
        get
        {
            int n = 0;
            foreach (var b in Scene.Beams) if (b.Weight > 0) n++;
            return n;
        }
    }

    // --- Rebuild / gating ---

    private void Rebuild()
    {
        Scene.RebuildBeams();
        ApplyAlphaExclusion();
        StatusText = $"rebuilt: {ActiveBeamCount}/{Scene.Beams.Count} beams active";
        OnPropertyChanged(nameof(LayoutReadout));
        OnPropertyChanged(nameof(ActiveBeamCount));
        OnPropertyChanged(nameof(MaskStepHint));
        SceneChanged?.Invoke();
    }

    /// <summary>
    /// Post-pass: apply the GSO exclusion to each beam by its ground-footprint
    /// |alpha| to the nearest visible GSO arc. Basic mode switches off beams inside
    /// alpha_excl; advanced mode applies the ring the footprint alpha falls in (off, or
    /// attenuate by N dB via the beam weight). Elevation gating already happened
    /// inside <see cref="SceneModel.RebuildBeams"/> (elliptical auto).
    ///
    /// This is the "cell-centre observance of a non-operating zone" mitigation
    /// of Rec. ITU-R S.1503-4 Sec. C2.2: "a beam ... is switched off when the
    /// centre of the cell sees this non-GSO space station at less than alpha_0
    /// from the GSO arc". The graded attenuation rings fall under the same
    /// section's "other mitigation techniques ... provided by the non-GSO
    /// administration".
    /// </summary>
    private void ApplyAlphaExclusion()
    {
        var bands = ExclusionBandsSorted();
        if (bands.Count == 0) return;
        foreach (var beam in Scene.Beams)
        {
            var fp = Scene.GroundFootprint(beam);
            if (fp is null) continue;
            var groundEcef = GeodeticToEcef(fp.Value.lat, fp.Value.lon, 0.0);
            double alphaDeg = GsoGeometry.AlphaMinAbsDeg(groundEcef, Scene.SatEcef);
            if (BandFor(bands, alphaDeg) is { } band) beam.Weight = band.WeightFactor;
        }
    }

    private void OnSceneChanged(bool rebuild,
        [System.Runtime.CompilerServices.CallerMemberName] string? changedProperty = null)
    {
        if (changedProperty != null) OnPropertyChanged(changedProperty);
        if (rebuild) Rebuild();
        else SceneChanged?.Invoke();
    }
}

/// <summary>
/// PFD-mask coordinate system, mirroring the S.1503-4 mask types the tool can
/// visualise (radians MaskPFDType AzEl / AlphaDelta; the X/deltaLong variant is
/// not implemented).
/// </summary>
public enum MaskPlotKind
{
    /// <summary>Satellite-frame azimuth / elevation (Sec. D6.4.5).</summary>
    AzEl,
    /// <summary>Signed alpha / deltaLongitude (Sec. D6.4.4).</summary>
    AlphaDeltaLong,
}

/// <summary>Per-beam transmit power policy for the PFD-mask computation.</summary>
public enum BeamPowerMode
{
    /// <summary>Every beam transmits the same power (constant EIRP per beam).</summary>
    ConstantEirp,
    /// <summary>
    /// Each beam's power is raised by 20*log10(boresight slant / altitude) so its
    /// boresight PFD is constant regardless of spreading loss -- typical downlink
    /// power control that flattens the served-area PFD.
    /// </summary>
    ConstantBoresightPfd,
}

/// <summary>How per-beam PFD contributions are aggregated into the mask.</summary>
public enum PfdAggregation
{
    /// <summary>All beams co-frequency (cluster size 1) -- the conservative upper bound.</summary>
    PowerSum,
    /// <summary>
    /// N-colour frequency reuse: only same-colour lattice beams share a channel;
    /// each colour is power-summed and the worst colour is taken per pixel.
    /// The realistic view for hex-lattice payloads with a frequency plan.
    /// </summary>
    CoChannelSum,
}
