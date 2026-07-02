using System;
using System.Windows.Input;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// State + operations for the "PFD mask (Az/El)" tab. Fully independent of
/// <see cref="MainViewModel"/> — owns its own <see cref="SceneModel"/> so
/// altitude / lat/lon / antenna edits here don't touch the composite-gain-map
/// tab, and vice-versa.
///
/// Pattern kind is locked to S.1528-1 §1.4 Taylor elliptical with 3GPP-style
/// hex tessellation (Scene.AutoMode = true). Beam gating replaces the box /
/// country exclusion of the first tab:
///   * beams whose footprint's user-elevation angle is below <see cref="MinElevDeg"/>
///     are already dropped inside <see cref="SceneModel.RebuildBeams"/>;
///   * a post-pass here also switches off beams whose ground footprint sits
///     within <see cref="AlphaExclDeg"/> of the nearest visible GSO satellite
///     (S.1503-4 §D6 avoidance angle).
/// </summary>
public sealed class PfdMaskViewModel : ObservableObject
{
    public SceneModel Scene { get; } = new()
    {
        PatternKind = BeamPatternKind.Taylor_1p4_Ell,
        AutoMode = true,
        FrequencyGHz = 12.0,      // Ku downlink — where the S.1503 PFD-mask concept typically bites
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

    /// <summary>Coastline / country data — shared with tab 1 when supplied, else lazily loaded here.</summary>
    public CoastlineDataProvider Coastlines { get; }

    /// <summary>Raised whenever the beam set or a plot input changes.</summary>
    public event Action? SceneChanged;

    public PfdMaskViewModel(CoastlineDataProvider? coastlines = null)
    {
        Coastlines = coastlines ?? new CoastlineDataProvider();
        Rebuild();
        RefreshCommand = new RelayCommand(Rebuild);
    }

    public ICommand RefreshCommand { get; }

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

    /// <summary>User-elevation cut-off (deg). Applies to beam gating and to the plot Y-axis lower bound.</summary>
    public double MinElevDeg
    {
        get => Scene.MinElevDeg;
        set { if (Scene.MinElevDeg != value && value >= 0 && value < 90) { Scene.MinElevDeg = value; OnSceneChanged(rebuild: true); } }
    }

    // --- PFD-plot-specific inputs (not shared with SceneModel) ---

    private double _alphaExclDeg = 10.0;
    /// <summary>GSO avoidance angle α_excl (deg, S.1503-4 §D6). Beams whose footprint sits inside |α| &lt; this get switched off.</summary>
    public double AlphaExclDeg
    {
        get => _alphaExclDeg;
        set { if (SetField(ref _alphaExclDeg, value)) OnSceneChanged(rebuild: true); }
    }

    private double _txEirpDbw = 0.0;
    /// <summary>Transmit EIRP per beam in the reference bandwidth (dBW).</summary>
    public double TxEirpDbw
    {
        get => _txEirpDbw;
        set { if (SetField(ref _txEirpDbw, value)) SceneChanged?.Invoke(); }
    }

    private double _refBwKHz = 40.0;
    /// <summary>Reference bandwidth (kHz) used to interpret the EIRP. Purely informational for the plot legend.</summary>
    public double RefBwKHz
    {
        get => _refBwKHz;
        set { if (SetField(ref _refBwKHz, value)) SceneChanged?.Invoke(); }
    }

    // --- Display ---

    private double _maskStepDeg = 1.0;
    /// <summary>
    /// Angular step (deg) for the az/el rasterisation grid. Also drives the
    /// (β_sub, γ) sample density so ~1 sample lands per pixel on average.
    /// Smaller = sharper plot but slower rebuild; typical range 0.25°..2°.
    /// </summary>
    public double MaskStepDeg
    {
        get => _maskStepDeg;
        set { if (value > 0.05 && value <= 5.0 && SetField(ref _maskStepDeg, value)) SceneChanged?.Invoke(); }
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

    private double _profileAzimuthDeg;
    /// <summary>
    /// Mask (sat-frame) azimuth the elevation-profile plot slices, in [−90°, +90°].
    /// Changing it does NOT rebuild the heatmap — it only re-slices the retained
    /// grid, so the setter raises the lightweight <see cref="ProfileCursorChanged"/>
    /// rather than <see cref="SceneChanged"/>.
    /// </summary>
    public double ProfileAzimuthDeg
    {
        get => _profileAzimuthDeg;
        set
        {
            double clamped = Math.Clamp(value, -90.0, 90.0);
            if (SetField(ref _profileAzimuthDeg, clamped))
            {
                OnPropertyChanged(nameof(ProfileAzimuthReadout));
                ProfileCursorChanged?.Invoke();
            }
        }
    }

    public string ProfileAzimuthReadout => $"azimuth cut: {_profileAzimuthDeg:+0;-0;0}°";

    /// <summary>Raised when only the azimuth cursor moved — cheap redraw, no heatmap rebuild.</summary>
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
        SceneChanged?.Invoke();
    }

    /// <summary>
    /// Post-pass: switch off any beam whose ground-footprint ES sits inside
    /// the α_excl cone from any visible GSO satellite. Elevation gating already
    /// happened inside <see cref="SceneModel.RebuildBeams"/> (elliptical auto).
    /// </summary>
    private void ApplyAlphaExclusion()
    {
        if (AlphaExclDeg <= 0) return;
        foreach (var beam in Scene.Beams)
        {
            var fp = Scene.GroundFootprint(beam);
            if (fp is null) continue;
            var groundEcef = GeodeticToEcef(fp.Value.lat, fp.Value.lon, 0.0);
            double alphaDeg = GsoGeometry.AlphaMinAbsDeg(groundEcef, Scene.SatEcef);
            if (alphaDeg < AlphaExclDeg) beam.Weight = 0.0;
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
