using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Application-level state + operations, bindable from XAML. The View binds
/// directly to these properties (TwoWay where editable), so there's no
/// "read controls into the model" step — every textbox/combo/radio change
/// flows straight into the VM, which mutates the SceneModel and rebuilds
/// beams, and raises <see cref="SceneChanged"/> so the View can redraw.
///
/// Drawing, mouse handling, and viewport state stay in MainWindow code-behind
/// because they're inherently tied to MapCanvas.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    public SceneModel Scene { get; } = new();

    /// <summary>Coastline / country data — loaded once at construction.</summary>
    public CoastlineDataProvider Coastlines { get; }

    private CoastlineDataProvider _coastlines => Coastlines;

    /// <summary>Raised whenever a property change touched the scene
    /// (rebuilt the beam set or otherwise needs a redraw).</summary>
    public event Action? SceneChanged;

    /// <summary>
    /// Raised when the user clicks "Show pattern" — the View opens a plot
    /// window for the supplied <see cref="ISinglePattern"/>. Decoupled via
    /// event rather than a direct WPF reference so the VM stays UI-agnostic.
    /// </summary>
    public event Action<ISinglePattern, string>? ShowPatternRequested;

    public MainViewModel(CoastlineDataProvider? coastlines = null)
    {
        Coastlines = coastlines ?? new CoastlineDataProvider();

        // Build the initial beam set from the default scene state.
        Scene.RebuildBeams();
        // Populate the country list from the coastline data.
        foreach (var c in Coastlines.Countries) Countries.Add(c.Name);
        if (Countries.Contains("United States of America"))
            SelectedCountry = "United States of America";

        RefreshCommand        = new RelayCommand(ForceRebuild);
        AllOnCommand          = new RelayCommand(SwitchAllBeamsOn);
        ExcludeCountryCommand = new RelayCommand(SwitchOffInCountry);
        ExcludeBoxCommand     = new RelayCommand(SwitchOffInBox);
        FillThetaCommand      = new RelayCommand(FillThetaBFromGm);
        PfdAdjustCommand      = new AsyncRelayCommand(PfdAdjustAsync);
        ShowPatternCommand    = new RelayCommand(RequestShowPattern);
    }

    /// <summary>Build a fresh single-beam pattern from the current inputs and ask the View to plot it.</summary>
    private void RequestShowPattern()
    {
        var pattern = Scene.BuildPattern(GmDbi);
        string header = PatternKind switch
        {
            BeamPatternKind.Envelope_1p2  => $"S.1528-0 §1.2 envelope (APL APSREC409V01) — Gm = {GmDbi:F1} dBi,  θb = {ThetaBDeg:F2}°,  LN = {LnDb:F1} dB,  LF = {LfDbi:F1} dBi",
            BeamPatternKind.Leo_1p3       => $"S.1528-0 §1.3 LEO  (APL APSREC411V01) — Gm = {GmDbi:F1} dBi,  θb = {ThetaBDeg:F2}°,  Ls = −6.75 dB,  LF = {LfDbi:F1} dBi",
            BeamPatternKind.Meo_1p3       => $"S.1528-0 §1.3 MEO  (APL APSREC410V01) — Gm = {GmDbi:F1} dBi,  θb = {ThetaBDeg:F2}°,  Ls = −12 dB,  LF = {LfDbi:F1} dBi",
            BeamPatternKind.Heo_1p3       => $"S.1528-0 §1.3 HEO  (APL APSREC4XXV01) — Gm = {GmDbi:F1} dBi,  θb = {ThetaBDeg:F2}°,  Ls = −20 dB,  LF = {LfDbi:F1} dBi",
            BeamPatternKind.Taylor_1p4_Ell => EllipticalHeader(pattern),
            _                             => $"S.1528-1 §1.4 Taylor — Gm = {GmDbi:F1} dBi,  θb = {ThetaBDeg:F2}°,  SLR = {TaylorSlrDb:F0} dB,  n̄ = {TaylorNbar},  LF = {LfDbi:F1} dBi",
        };
        ShowPatternRequested?.Invoke(pattern, header);
    }

    private string EllipticalHeader(ISinglePattern pattern)
    {
        if (pattern is Rec1528_1p4_Ell e)
        {
            return $"S.1528-1 §1.4 Taylor (elliptical) — Gm = {GmDbi:F1} dBi,  α = {EllAlphaDeg:F2}°, β = {EllBetaDeg:F2}°, " +
                   $"roll-off = {EllRollOffDb:F1} dB → Lr = {e.LrMeters:F2} m, Lt = {e.LtMeters:F2} m,  " +
                   $"θb_rad = {e.ThetaB:F2}°, θb_tr = {e.ThetaBTransverseDeg:F2}°,  " +
                   $"SLR = {TaylorSlrDb:F0} dB, n̄ = {TaylorNbar},  LF = {LfDbi:F1} dBi";
        }
        return "S.1528-1 §1.4 Taylor (elliptical)";
    }

    /// <summary>Rebuild the beam set with the current input values.</summary>
    public ICommand RefreshCommand { get; }
    /// <summary>Restore weight = 1 and original G_m on every beam.</summary>
    public ICommand AllOnCommand { get; }
    /// <summary>Switch off every beam whose footprint centre is inside the selected country.</summary>
    public ICommand ExcludeCountryCommand { get; }
    /// <summary>Switch off every beam whose footprint centre is inside the lat/lon box.</summary>
    public ICommand ExcludeBoxCommand { get; }
    /// <summary>Stamp θ_b derived from the current G_m into the bound property.</summary>
    public ICommand FillThetaCommand { get; }
    /// <summary>Run the PFD adjuster off the UI thread; greys the button while running.</summary>
    public ICommand PfdAdjustCommand { get; }
    /// <summary>Open a 2-D plot of G(θ) for the current beam-pattern parameters.</summary>
    public ICommand ShowPatternCommand { get; }

    // ----- Scene-backed input properties (bindable; setters mirror to Scene) -----

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
        set { if (Scene.FrequencyGHz != value) { Scene.FrequencyGHz = value; OnSceneChanged(rebuild: false, derived: true); } }
    }

    public double GmDbi
    {
        get => Scene.GmDbi;
        set { if (Scene.GmDbi != value) { Scene.GmDbi = value; OnSceneChanged(rebuild: true, derived: true); } }
    }

    public double ThetaBDeg
    {
        get => Scene.ThetaBDeg;
        set { if (Scene.ThetaBDeg != value && value > 0) { Scene.ThetaBDeg = value; OnSceneChanged(rebuild: true); } }
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

    public BeamPatternKind PatternKind
    {
        get => Scene.PatternKind;
        set
        {
            if (Scene.PatternKind != value)
            {
                Scene.PatternKind = value;
                OnPropertyChanged(nameof(PatternKindIndex));
                OnPropertyChanged(nameof(IsEllipticalPattern));
                OnPropertyChanged(nameof(IsEllipticalManualMode));
                OnPropertyChanged(nameof(IsEllipticalAutoMode));
                OnSceneChanged(rebuild: true);
            }
        }
    }

    /// <summary>Index-form of <see cref="PatternKind"/> for ComboBox SelectedIndex binding.</summary>
    public int PatternKindIndex
    {
        get => (int)Scene.PatternKind;
        set { if (value >= 0 && value < 6) PatternKind = (BeamPatternKind)value; }
    }

    public double LnDb
    {
        get => Scene.LnDb;
        set { if (Scene.LnDb != value) { Scene.LnDb = value; OnSceneChanged(rebuild: true); } }
    }

    // ----- §1.4 elliptical inputs (Annex 2 cell parameterisation) -----

    public double EllAlphaDeg
    {
        get => Scene.EllAlphaDeg;
        set { if (Scene.EllAlphaDeg != value && value > 0) { Scene.EllAlphaDeg = value; OnSceneChanged(rebuild: true); } }
    }

    public double EllBetaDeg
    {
        get => Scene.EllBetaDeg;
        set { if (Scene.EllBetaDeg != value && value > 0) { Scene.EllBetaDeg = value; OnSceneChanged(rebuild: true); } }
    }

    public double EllRollOffDb
    {
        get => Scene.EllRollOffDb;
        set { if (Scene.EllRollOffDb != value && value > 0) { Scene.EllRollOffDb = value; OnSceneChanged(rebuild: true); } }
    }

    /// <summary>Auto layout mode (tangent-plane hex tessellation), applicable to any pattern.</summary>
    public bool AutoMode
    {
        get => Scene.AutoMode;
        set
        {
            if (Scene.AutoMode != value)
            {
                Scene.AutoMode = value;
                OnPropertyChanged(nameof(IsAutoMode));
                OnPropertyChanged(nameof(IsEllipticalAutoMode));
                OnPropertyChanged(nameof(IsEllipticalManualMode));
                OnSceneChanged(rebuild: true);
            }
        }
    }

    public double CellRadiusKm
    {
        get => Scene.CellRadiusKm;
        set { if (Scene.CellRadiusKm != value && value > 0) { Scene.CellRadiusKm = value; OnSceneChanged(rebuild: true); } }
    }

    /// <summary>True iff the currently selected pattern kind is the elliptical §1.4 form.</summary>
    public bool IsEllipticalPattern => Scene.PatternKind == BeamPatternKind.Taylor_1p4_Ell;

    /// <summary>Elliptical pattern + manual α/β (drives the manual α/β input visibility).</summary>
    public bool IsEllipticalManualMode => IsEllipticalPattern && !Scene.AutoMode;

    /// <summary>Auto layout active. Drives the cell-radius input visibility (which only matters for elliptical patterns).</summary>
    public bool IsAutoMode => Scene.AutoMode;

    /// <summary>Elliptical pattern + auto layout — drives R_cell input visibility (R_cell only used by elliptical auto).</summary>
    public bool IsEllipticalAutoMode => IsEllipticalPattern && Scene.AutoMode;

    public double LfDbi
    {
        get => Scene.LfDbi;
        set { if (Scene.LfDbi != value) { Scene.LfDbi = value; OnSceneChanged(rebuild: true); } }
    }

    public double MinElevDeg
    {
        get => Scene.MinElevDeg;
        set { if (Scene.MinElevDeg != value && value >= 0 && value < 90) { Scene.MinElevDeg = value; OnSceneChanged(rebuild: true); } }
    }

    public double CrossoverDb
    {
        get => Scene.CrossoverDb;
        set { if (Scene.CrossoverDb != value && value < 0) { Scene.CrossoverDb = value; OnSceneChanged(rebuild: true); } }
    }

    /// <summary>Heatmap / probe aggregation mode, exposed via two booleans
    /// for radio-button binding.</summary>
    public HeatmapMode Mode
    {
        get => Scene.Mode;
        set
        {
            if (Scene.Mode != value)
            {
                Scene.Mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPowerSumMode));
                OnPropertyChanged(nameof(IsMaxSingleMode));
                SceneChanged?.Invoke();
            }
        }
    }

    public bool IsPowerSumMode
    {
        get => Mode == HeatmapMode.PowerSum;
        set { if (value) Mode = HeatmapMode.PowerSum; }
    }

    public bool IsMaxSingleMode
    {
        get => Mode == HeatmapMode.MaxSingleBeam;
        set { if (value) Mode = HeatmapMode.MaxSingleBeam; }
    }

    // ----- Non-scene inputs (PFD adjustment + region exclusion + display) -----

    private double _txPowerDbw = -14.0;
    public double TxPowerDbw { get => _txPowerDbw; set { if (SetField(ref _txPowerDbw, value)) SceneChanged?.Invoke(); } }

    private double _flatPfdLimit = -180.0;
    public double FlatPfdLimitDbWm2 { get => _flatPfdLimit; set { if (SetField(ref _flatPfdLimit, value)) SceneChanged?.Invoke(); } }

    /// <summary>Absolute minimum peak gain (dBi) any beam may be reduced to before being switched off by the PFD adjuster.</summary>
    private double _minGmaxDbi = 5.0;
    public double MinGmaxDbi { get => _minGmaxDbi; set => SetField(ref _minGmaxDbi, value); }

    private int _pfdMaskIndex = 0;
    /// <summary>0 = US MSS L-band piecewise mask, 1 = flat (use <see cref="FlatPfdLimitDbWm2"/>).</summary>
    public int PfdMaskIndex { get => _pfdMaskIndex; set { if (SetField(ref _pfdMaskIndex, value)) SceneChanged?.Invoke(); } }

    public PfdMaskKind PfdMask => PfdMaskIndex == 0 ? PfdMaskKind.UsMssLBand : PfdMaskKind.Flat;

    public ObservableCollection<string> Countries { get; } = new();

    private string _selectedCountry = "";
    public string SelectedCountry { get => _selectedCountry; set => SetField(ref _selectedCountry, value); }

    private double _excludeLatMin = -30; public double ExcludeLatMin { get => _excludeLatMin; set => SetField(ref _excludeLatMin, value); }
    private double _excludeLatMax =  30; public double ExcludeLatMax { get => _excludeLatMax; set => SetField(ref _excludeLatMax, value); }
    private double _excludeLonMin = -10; public double ExcludeLonMin { get => _excludeLonMin; set => SetField(ref _excludeLonMin, value); }
    private double _excludeLonMax =  40; public double ExcludeLonMax { get => _excludeLonMax; set => SetField(ref _excludeLonMax, value); }

    private bool _heatmapEnabled = false;
    public bool HeatmapEnabled { get => _heatmapEnabled; set { if (SetField(ref _heatmapEnabled, value)) SceneChanged?.Invoke(); } }

    private bool _footprintsEnabled = true;
    public bool FootprintsEnabled { get => _footprintsEnabled; set { if (SetField(ref _footprintsEnabled, value)) SceneChanged?.Invoke(); } }

    private double _floorDbi = -10.0;
    public double FloorDbi { get => _floorDbi; set { if (SetField(ref _floorDbi, value)) SceneChanged?.Invoke(); } }


    // ----- Status bar (bindable) -----

    private string _statusText = "ready";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    // ----- Read-outs (computed, bindable) -----

    public string AltReadoutText =>
        $"alt = {Scene.AltitudeKm:F0} km   horizon (off-nadir) = {HorizonOffNadirDeg(Scene.AltitudeKm):F1}°";

    public string DerivedReadoutText =>
        $"λ = {Scene.WavelengthM * 1000:F1} mm\nθb from Gm ≈ {Scene.DerivedHalfBeamwidthDeg:F2}°  (90°·10^(−Gm/20))";

    public string LayoutReadoutText =>
        (Scene.PatternKind == BeamPatternKind.Taylor_1p4_Ell
            ? $"spacing radial Δ_r = {Scene.SpacingRadialDeg:F2}°,  transverse Δ_t = {Scene.SpacingTransverseDeg:F2}°  (at L={Scene.CrossoverDb:F1} dB)\n"
            : $"spacing Δ = {Scene.SpacingDeg:F2}°  (= 2·θb·√(−L/3) at L={Scene.CrossoverDb:F1} dB)\n") +
        $"outer ring: off-nadir = {Scene.OuterOffNadirDeg:F2}° (elev {Scene.MinElevDeg:F1}°)\n" +
        $"{Scene.Beams.Count} beams in {RingCount()} rings";

    /// <summary>
    /// Called by Scene-backed property setters. Raises PropertyChanged for the
    /// calling property (so a programmatic setter — e.g. <see cref="FillThetaBFromGm"/>
    /// — flows back to the bound textbox), optionally rebuilds the beam set,
    /// refreshes derived read-outs, and raises <see cref="SceneChanged"/>.
    /// </summary>
    private void OnSceneChanged(bool rebuild, bool derived = false,
        [System.Runtime.CompilerServices.CallerMemberName] string? changedProperty = null)
    {
        if (changedProperty != null) OnPropertyChanged(changedProperty);
        if (rebuild) Scene.RebuildBeams();
        OnPropertyChanged(nameof(AltReadoutText));
        OnPropertyChanged(nameof(LayoutReadoutText));
        if (derived) OnPropertyChanged(nameof(DerivedReadoutText));
        SceneChanged?.Invoke();
    }

    /// <summary>Force a full rebuild + redraw (used by the explicit Refresh button).</summary>
    public void ForceRebuild()
    {
        Scene.RebuildBeams();
        OnPropertyChanged(nameof(AltReadoutText));
        OnPropertyChanged(nameof(DerivedReadoutText));
        OnPropertyChanged(nameof(LayoutReadoutText));
        SceneChanged?.Invoke();
    }

    /// <summary>
    /// Set the satellite position from a map-drag without going through public
    /// setters that each trigger their own rebuild — does one rebuild + one
    /// SceneChanged for the pair update.
    /// </summary>
    public void SetSatPosition(double latDeg, double lonDeg)
    {
        Scene.SubSatLatDeg = latDeg;
        Scene.SubSatLonDeg = lonDeg;
        Scene.RebuildBeams();
        OnPropertyChanged(nameof(SubSatLatDeg));
        OnPropertyChanged(nameof(SubSatLonDeg));
        OnPropertyChanged(nameof(AltReadoutText));
        OnPropertyChanged(nameof(LayoutReadoutText));
        SceneChanged?.Invoke();
    }

    // ----- Operations (write StatusText, raise SceneChanged) -----

    /// <summary>Flip a beam's on/off state and report it.</summary>
    public void ToggleBeam(Beam beam)
    {
        beam.Weight = beam.Weight > 0 ? 0.0 : 1.0;
        StatusText = $"toggled {beam.Name} -> weight={beam.Weight:F1}";
        SceneChanged?.Invoke();
    }

    public void SwitchAllBeamsOn()
    {
        foreach (var b in Scene.Beams)
        {
            b.Weight = 1.0;
            if (b.IsGmAdjusted)
                b.Pattern = Scene.BuildPatternFor(b.OriginalGmDbi, b.OffNadirDeg);
        }
        StatusText = $"all {Scene.Beams.Count} beams ON, G_m restored";
        SceneChanged?.Invoke();
    }

    public void SwitchOffInCountry()
    {
        if (string.IsNullOrWhiteSpace(SelectedCountry)) { StatusText = "no country selected"; return; }
        var country = _coastlines.Countries
            .FirstOrDefault(c => string.Equals(c.Name, SelectedCountry, StringComparison.OrdinalIgnoreCase));
        if (country is null) { StatusText = $"country '{SelectedCountry}' not found in loaded data"; return; }

        int off = 0;
        foreach (var beam in Scene.Beams)
        {
            var fp = Scene.GroundFootprint(beam);
            if (fp is null) continue;
            if (country.Contains(fp.Value.lat, fp.Value.lon)) { beam.Weight = 0.0; off++; }
        }
        StatusText = $"switched off {off} beams whose footprint is inside {country.Name}";
        SceneChanged?.Invoke();
    }

    public void SwitchOffInBox()
    {
        int off = 0;
        foreach (var beam in Scene.Beams)
        {
            var fp = Scene.GroundFootprint(beam);
            if (fp is null) continue;
            var (lat, lon) = fp.Value;
            if (lat >= ExcludeLatMin && lat <= ExcludeLatMax &&
                lon >= ExcludeLonMin && lon <= ExcludeLonMax) { beam.Weight = 0.0; off++; }
        }
        StatusText = $"switched off {off} beams whose footprint is in lat[{ExcludeLatMin},{ExcludeLatMax}] lon[{ExcludeLonMin},{ExcludeLonMax}]";
        SceneChanged?.Invoke();
    }

    /// <summary>
    /// Run the PFD adjuster off the UI thread (it iterates ~20 000 samples × N
    /// beams in two passes). Status text gets a "running" indicator while the
    /// computation is in flight; the result + redraw fire on completion.
    /// </summary>
    public async System.Threading.Tasks.Task PfdAdjustAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedCountry))
        { StatusText = "PFD adjust: no country selected"; return; }
        var country = _coastlines.Countries
            .FirstOrDefault(c => string.Equals(c.Name, SelectedCountry, StringComparison.OrdinalIgnoreCase));
        if (country is null)
        { StatusText = $"PFD adjust: country '{SelectedCountry}' not found"; return; }

        var opts = new PfdAdjuster.Options
        {
            TxPowerDbw = TxPowerDbw,
            LimitAtElevation = PfdLimitAt,
            Mode = Mode,
            // Absolute minimum peak gain a beam may be reduced to before the
            // PFD adjuster gives up and switches it off.
            MinGmDbi = MinGmaxDbi,
        };

        StatusText = $"PFD adjust over {country.Name}: running…";
        var r = await System.Threading.Tasks.Task.Run(() => PfdAdjuster.Run(Scene, country, opts));
        StatusText = r.Status switch
        {
            PfdAdjustStatus.NoSamplesProduced => "PFD adjust: country sampler produced no points",
            PfdAdjustStatus.NoContributors    => "PFD adjust: no beam contributes within 30 dB of any sample's limit",
            PfdAdjustStatus.CountryNotFound   => $"PFD adjust: country '{SelectedCountry}' not found",
            _ => $"PFD adjust ({r.ModeLabel}): {r.Adjusted} Gm-reduced, {r.SwitchedOff} switched off, of {r.Contributors} contributors  " +
                 $"max (PFD − limit) over {r.CountryName}: {r.MaxMarginAfterDb:+0.0;-0.0;0.0} dB",
        };
        SceneChanged?.Invoke();
    }

    public void Probe(double lat, double lon)
    {
        int active = Scene.Beams.Count(b => b.Weight > 0);
        if (!Scene.IsVisible(lat, lon))
        {
            StatusText = $"probe (lat={lat:F2}, lon={lon:F2})  below horizon — no line of sight  ({active}/{Scene.Beams.Count} on)";
            return;
        }
        double g = Scene.GainTowardsGround(lat, lon);
        var ground = GeodeticToEcef(lat, lon, 0.0);
        double dKm = (ground - Scene.SatEcef).Length;
        double dM = dKm * 1000.0;
        double pathLossDb = 10.0 * Math.Log10(4.0 * Math.PI * dM * dM);
        double elev = ElevationAngleDeg(Scene.SatEcef, ground);
        string modeLabel = Mode == HeatmapMode.MaxSingleBeam ? "single-beam" : "aggregate";
        double pfd = TxPowerDbw + g - pathLossDb;
        double limit = PfdLimitAt(elev);
        double margin = pfd - limit;
        StatusText =
            $"probe (lat={lat:F2}, lon={lon:F2})  elev={elev:F1}°  G ({modeLabel})={g:F2} dBi  " +
            $"d={dKm:F0} km  PFD={pfd:F2}  limit={limit:F2}  margin={margin:+0.00;-0.00;0.00} dB  " +
            $"({active}/{Scene.Beams.Count} on)";
    }

    public void ReportSatPosition() =>
        StatusText = $"satellite at lat={Scene.SubSatLatDeg:F2}, lon={Scene.SubSatLonDeg:F2}, alt={Scene.AltitudeKm:F0} km";

    /// <summary>Stamp θ_b derived from G_m (≈ 90°·10^(−G_m/20)) into the bound property.</summary>
    public void FillThetaBFromGm() => ThetaBDeg = Scene.DerivedHalfBeamwidthDeg;

    // ----- Math helpers -----

    public double PfdLimitAt(double elevDeg)
    {
        if (PfdMask == PfdMaskKind.UsMssLBand)
        {
            if (elevDeg < 0) return double.PositiveInfinity;
            if (elevDeg <= 4.0)  return -181.0;
            if (elevDeg <= 20.0) return -193.0 + 20.0  * Math.Log10(elevDeg);
            if (elevDeg <= 60.0) return -213.3 + 35.6  * Math.Log10(elevDeg);
            return -150.0;
        }
        return FlatPfdLimitDbWm2;
    }

    public int RingCount()
    {
        var seen = new HashSet<double>();
        foreach (var b in Scene.Beams) seen.Add(Math.Round(b.OffNadirDeg, 3));
        return seen.Count;
    }

}

public enum PfdMaskKind { UsMssLBand, Flat }
