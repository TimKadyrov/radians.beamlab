using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using radians.beamlab;
using Radians.Orbits.Core.Utilities;

namespace radians.beamlab.app;

/// <summary>
/// One repeat-solution row shaped for the grid. IsUserEntry marks the row
/// created by the own-period validator (highlighted); WithinBand is false
/// when its exact altitude falls outside the search band (flagged red).
/// </summary>
public sealed record OrbitSolutionRow(RepeatSolution Solution, bool IsUserEntry = false,
    bool WithinBand = true)
{
    public int Orbits => Solution.Orbits;
    public int NodalDays => Solution.NodalDays;
    public string AltitudeText => Solution.AltitudeKm.ToString("F2", CultureInfo.InvariantCulture);
    public string DeltaText => Solution.AltitudeDeltaKm.ToString("+0.00;-0.00", CultureInfo.InvariantCulture);
    public string CycleText
    {
        get
        {
            var (d, h, m, s) = Solution.RptPrd;
            return $"{d}d {h:00}:{m:00}:{s:00}";
        }
    }
    public string CycleAtTargetText
    {
        get
        {
            var (d, h, m, s) = Solution.RptPrdAtTarget;
            return $"{d}d {h:00}:{m:00}:{s:00}";
        }
    }
    public string DriftText => Solution.DriftDegPerCycleAtTarget.ToString("+0.000;-0.000", CultureInfo.InvariantCulture);
    public string SpacingText => Solution.EquatorSpacingDeg.ToString("F3", CultureInfo.InvariantCulture);
    public string MaxKeepText => Solution.MaxKeepRangeDeg.ToString("F3", CultureInfo.InvariantCulture);
}

/// <summary>
/// The Orbit Design tab: repeating-track candidates near a target altitude
/// (OrbitDesign.RepeatSolutions), the three station-keeping cases' SNS v10
/// field previews for the selected candidate, and its propagated ground
/// track for one full cycle. Pure state -- the view draws the track and
/// owns the clipboard; everything here is headless-testable.
/// </summary>
public sealed class OrbitDesignViewModel : ObservableObject
{
    public OrbitDesignViewModel() => Recompute();

    // ---- inputs --------------------------------------------------------

    private double _targetAltitudeKm = 1200.0;
    public double TargetAltitudeKm
    {
        get => _targetAltitudeKm;
        set { if (SetField(ref _targetAltitudeKm, value)) Recompute(); }
    }

    private double _inclinationDeg = 53.0;
    public double InclinationDeg
    {
        get => _inclinationDeg;
        set { if (SetField(ref _inclinationDeg, value)) Recompute(); }
    }

    private double _eccentricity;
    public double Eccentricity
    {
        get => _eccentricity;
        set { if (SetField(ref _eccentricity, value)) Recompute(); }
    }

    private int _maxOrbitsPerCycle = 120;
    public int MaxOrbitsPerCycle
    {
        get => _maxOrbitsPerCycle;
        set { if (SetField(ref _maxOrbitsPerCycle, value)) Recompute(); }
    }

    private double _searchBandKm = 400.0;
    public double SearchBandKm
    {
        get => _searchBandKm;
        set { if (SetField(ref _searchBandKm, value)) Recompute(); }
    }

    private string _checkOrbitsText = "";
    /// <summary>Own-period validator: whole nodal orbits (k); empty = off.</summary>
    public string CheckOrbitsText
    {
        get => _checkOrbitsText;
        set { if (SetField(ref _checkOrbitsText, value)) Recompute(); }
    }

    private string _checkDaysText = "";
    /// <summary>Own-period validator: whole nodal days (m); empty = off.</summary>
    public string CheckDaysText
    {
        get => _checkDaysText;
        set { if (SetField(ref _checkDaysText, value)) Recompute(); }
    }

    private int _nOrbits = 288;
    public int NOrbits
    {
        get => _nOrbits;
        set { if (SetField(ref _nOrbits, value)) RecomputeDetails(); }
    }

    private string _victimBeamwidthText = "";
    /// <summary>
    /// Victim 3 dB beamwidth (deg); when parseable, NOrbits is derived per
    /// the Recommendation's run rules (eq (3) + D4.6.2, N_tracks = 16) at
    /// the selected candidate's altitude. Empty keeps NOrbits manual.
    /// </summary>
    public string VictimBeamwidthText
    {
        get => _victimBeamwidthText;
        set { if (SetField(ref _victimBeamwidthText, value)) { ApplyBeamwidth(); RecomputeDetails(); } }
    }

    private void ApplyBeamwidth()
    {
        if (!double.TryParse(_victimBeamwidthText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double bw) || bw <= 0.0)
            return;
        // NOrbits belongs to Case 1, which flies the target orbit.
        int n = OrbitDesign.SuggestedNOrbits(bw, _targetAltitudeKm);
        if (n != _nOrbits) { _nOrbits = n; OnPropertyChanged(nameof(NOrbits)); }
    }

    private double _keepRangeDeg = 0.5;
    public double KeepRangeDeg
    {
        get => _keepRangeDeg;
        set { if (SetField(ref _keepRangeDeg, value)) RecomputeDetails(); }
    }

    private bool _declareAtTargetAltitude = true;
    /// <summary>
    /// Case-2 default ("fixed altitude"): the repeat is declared at the
    /// target altitude and station keeping absorbs the natural drift (the
    /// EPFD calculation flies the declared repeat and sweeps the deadband
    /// either way). False ("adjusting altitude") adopts the selected
    /// candidate's exact closing altitude, where the repeat costs zero
    /// correction.
    /// </summary>
    public bool DeclareAtTargetAltitude
    {
        get => _declareAtTargetAltitude;
        set
        {
            if (!SetField(ref _declareAtTargetAltitude, value)) return;
            _harmonizedRptSeconds = null;   // the declared cycle changed
            OnPropertyChanged(nameof(AdjustAltitudeChoice));
            // Recompute so entering fixed mode auto-fills an empty pair;
            // then refresh unconditionally -- the reselect inside may be a
            // no-op while the mode still changes texts and the track.
            Recompute();
            RecomputeDetails();
            RecomputeTrack();
        }
    }

    /// <summary>The inverse view of the choice, for the second radio button.</summary>
    public bool AdjustAltitudeChoice
    {
        get => !_declareAtTargetAltitude;
        set => DeclareAtTargetAltitude = !value;
    }

    private long? _harmonizedRptSeconds;
    /// <summary>
    /// Case-2 override: the constellation repeat period (s) declared as
    /// this shell's rpt_prd instead of its own cycle -- any multiple of
    /// the track's cycle is a valid repeat period for it (Rec. A2.4: one
    /// period appropriate for all). Cleared whenever the declared cycle
    /// changes (a new pair or altitude mode).
    /// </summary>
    public long? HarmonizedRptSeconds
    {
        get => _harmonizedRptSeconds;
        set { if (SetField(ref _harmonizedRptSeconds, value)) RecomputeDetails(); }
    }

    /// <summary>This shell's own declared cycle in whole seconds (Case 2 with a selection; else null).</summary>
    public long? OwnRptSeconds
        => _caseChoice == 1 && _selectedSolution is { } r
            ? (long)Math.Round(_declareAtTargetAltitude
                ? r.Solution.RepeatSecondsAtTarget
                : r.Solution.RepeatSeconds)
            : null;

    /// <summary>The rpt_prd this shell declares: the harmonized period or its own cycle.</summary>
    public long? DeclaredRptSeconds
        => _caseChoice == 1 ? _harmonizedRptSeconds ?? OwnRptSeconds : null;

    private string _precessionText = "";
    /// <summary>
    /// Case-3 admin-supplied precession rate (deg/s, any sign); empty
    /// declares the plain-J2 default for the target orbit. Unparsable text
    /// behaves as empty and the Case-3 preview says so.
    /// </summary>
    public string PrecessionText
    {
        get => _precessionText;
        set { if (SetField(ref _precessionText, value)) RecomputeDetails(); }
    }

    private double? ParsedPrecession()
        => double.TryParse(_precessionText, NumberStyles.Float, CultureInfo.InvariantCulture,
            out double v) ? v : null;

    // ---- outputs -------------------------------------------------------

    private IReadOnlyList<OrbitSolutionRow> _solutions = Array.Empty<OrbitSolutionRow>();
    public IReadOnlyList<OrbitSolutionRow> Solutions
    {
        get => _solutions;
        private set => SetField(ref _solutions, value);
    }

    private OrbitSolutionRow? _selectedSolution;
    public OrbitSolutionRow? SelectedSolution
    {
        get => _selectedSolution;
        set
        {
            if (!SetField(ref _selectedSolution, value)) return;
            _harmonizedRptSeconds = null;   // the declared cycle changed
            ApplyBeamwidth(); RecomputeDetails(); RecomputeTrack();
            SyncOwnPeriod(value);
        }
    }

    // In fixed-altitude mode a grid selection auto-fills the declared
    // pair, so it rides to the top as the checked row; the recompute
    // guard keeps the solver's own reselects out, and adjusting mode
    // selects rows without promoting them.
    private void SyncOwnPeriod(OrbitSolutionRow? row)
    {
        if (_inRecompute || !_declareAtTargetAltitude || row is null || row.IsUserEntry) return;
        string k = row.Orbits.ToString(CultureInfo.InvariantCulture);
        string m = row.NodalDays.ToString(CultureInfo.InvariantCulture);
        if (k == _checkOrbitsText.Trim() && m == _checkDaysText.Trim()) return;
        _checkOrbitsText = k;
        _checkDaysText = m;
        OnPropertyChanged(nameof(CheckOrbitsText));
        OnPropertyChanged(nameof(CheckDaysText));
        Recompute();
    }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }

    private string _checkStatusText = "";
    /// <summary>Narration of the own-period validation (empty when off).</summary>
    public string CheckStatusText { get => _checkStatusText; private set => SetField(ref _checkStatusText, value); }

    private string _case1Text = "";
    public string Case1Text { get => _case1Text; private set => SetField(ref _case1Text, value); }

    private string _case2Text = "";
    public string Case2Text { get => _case2Text; private set => SetField(ref _case2Text, value); }

    private bool _keepRangeValid = true;
    public bool KeepRangeValid { get => _keepRangeValid; private set => SetField(ref _keepRangeValid, value); }

    private string _keepRangeHintText = "";
    /// <summary>The solver-tab keep_rnge hint: the bound, and in fixed mode the crossing cadence.</summary>
    public string KeepRangeHintText { get => _keepRangeHintText; private set => SetField(ref _keepRangeHintText, value); }

    private string _case3Text = "";
    public string Case3Text { get => _case3Text; private set => SetField(ref _case3Text, value); }

    /// <summary>Ground track of one full cycle, split at date-line wraps ((lat, lon) per vertex).</summary>
    public IReadOnlyList<IReadOnlyList<(double LatDeg, double LonDeg)>> TrackSegments { get; private set; }
        = Array.Empty<IReadOnlyList<(double, double)>>();

    /// <summary>Great-circle angle (deg) between the track's first and last sample -- 0 for a perfect repeat.</summary>
    public double TrackClosureDeg { get; private set; } = double.NaN;

    /// <summary>Raised after TrackSegments/TrackClosureDeg change (the view redraws).</summary>
    public event Action? TrackChanged;

    // ---- computation ---------------------------------------------------

    private bool _inputsValid = true;
    private bool _inRecompute;

    private void Recompute()
    {
        _inRecompute = true;
        try { RecomputeCore(); }
        finally { _inRecompute = false; }
    }

    private void RecomputeCore()
    {
        if (_targetAltitudeKm < 200.0 || _targetAltitudeKm > 45000.0
            || _inclinationDeg <= 0.0 || _inclinationDeg >= 180.0
            || _eccentricity is < 0.0 or >= 0.9
            || _maxOrbitsPerCycle is < 1 or > 2000 || _searchBandKm <= 0.0)
        {
            _inputsValid = false;
            Solutions = Array.Empty<OrbitSolutionRow>();
            SelectedSolution = null;
            RecomputeDetails();
            StatusText = "inputs out of range";
            CheckStatusText = "";
            return;
        }
        _inputsValid = true;

        var sols = OrbitDesign.RepeatSolutions(_targetAltitudeKm, _eccentricity, _inclinationDeg,
            _maxOrbitsPerCycle, take: 10, searchBandKm: _searchBandKm);
        // Fixed-altitude mode always declares a pair: an empty entry
        // starts from the nearest repeat.
        if (_declareAtTargetAltitude && sols.Count > 0
            && _checkOrbitsText.Trim().Length == 0 && _checkDaysText.Trim().Length == 0)
        {
            _checkOrbitsText = sols[0].Orbits.ToString(CultureInfo.InvariantCulture);
            _checkDaysText = sols[0].NodalDays.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(CheckOrbitsText));
            OnPropertyChanged(nameof(CheckDaysText));
        }
        var rows = sols.Select(s => new OrbitSolutionRow(s)).ToList();
        CheckStatusText = ApplyOwnPeriod(rows);
        Solutions = rows;
        StatusText = sols.Count == 0
            ? "no repeat inside the search band"
            : $"{sols.Count} candidate(s); nearest {sols[0].AltitudeDeltaKm:+0.00;-0.00} km from target";
        SelectedSolution = Solutions.FirstOrDefault();
        RecomputeConstellation();
    }

    // The own-period validator: a parseable k/m pair becomes the top row of
    // the grid (replacing a scan row with the same reduced pair), selected
    // like any candidate; the returned text narrates the validation.
    private string ApplyOwnPeriod(List<OrbitSolutionRow> rows)
    {
        var inv = CultureInfo.InvariantCulture;
        if (_checkOrbitsText.Trim().Length == 0 && _checkDaysText.Trim().Length == 0)
            return "";
        if (!int.TryParse(_checkOrbitsText, NumberStyles.Integer, inv, out int k) || k < 1
            || !int.TryParse(_checkDaysText, NumberStyles.Integer, inv, out int m) || m < 1)
            return "enter whole numbers >= 1 for orbits and nodal days";

        var chk = OrbitDesign.CheckRepeat(_targetAltitudeKm, _eccentricity, _inclinationDeg,
            k, m, _searchBandKm);
        string reduced = chk.Reduced
            ? $"{k}/{m} reduces to {chk.Orbits}/{chk.NodalDays} (the true cycle); "
            : "";
        if (chk.Solution is not { } cs)
            return reduced + "no altitude between 100 and 30000 km closes this pair";

        rows.RemoveAll(r => r.Orbits == chk.Orbits && r.NodalDays == chk.NodalDays);
        rows.Insert(0, new OrbitSolutionRow(cs, IsUserEntry: true, WithinBand: chk.WithinBand));
        // The narration follows the mode: fixed altitude leads with what is
        // declared, the exact closing altitude stays as the reference.
        string closing = string.Create(inv,
            $"closes by itself at {cs.AltitudeKm:F2} km ({cs.AltitudeDeltaKm:+0.00;-0.00} from target)")
            + (chk.WithinBand
                ? ""
                : string.Create(inv, $" -- outside the {_searchBandKm:F0} km search band"));
        return reduced + (_declareAtTargetAltitude
            ? string.Create(inv,
                $"declared at {_targetAltitudeKm:F1} km, drift {Math.Abs(cs.DriftDegPerCycleAtTarget):F4} deg/cycle to hold; ") + closing
            : closing);
    }

    private void RecomputeDetails()
    {
        if (!_inputsValid)
        {
            Case1Text = Case2Text = Case3Text = "";
            KeepRangeValid = true;
            KeepRangeHintText = "";
            RecomputeConstellation();
            return;
        }
        // Cases 1 and 3 describe the TARGET orbit (no solved repeat needed);
        // Case 2 describes the selected candidate.
        double aTarget = OrbitalConstants.EarthRadiusKm + _targetAltitudeKm;
        var inv = CultureInfo.InvariantCulture;

        if (_nOrbits >= 1)
        {
            var plan = OrbitDesign.PrecessionPlan(aTarget, _eccentricity, _inclinationDeg, _nOrbits);
            Case1Text = string.Create(inv,
                $"f_stn_keep='N', f_precess='N' (rate derived by the examination)\n" +
                $"S_pass = {plan.SPassDeg:F4} deg   S_grid = {plan.SGridDeg:F4} deg\n" +
                $"rate = {plan.RateDegPerSec:E4} deg/s ({plan.RateRadPerSec:E4} rad/s)\n" +
                $"measured spacing = {plan.MeasuredSpacingDeg:F4} deg (2*S_pass - S_grid,\n" +
                $"one step past the grid -- the documented D6.3.2 quirk)\n" +
                $"run = {_nOrbits} orbits = {plan.RunDurationSec / 86400.0:F2} days");
        }
        else Case1Text = "NOrbits must be >= 1";

        if (_selectedSolution is { } row)
        {
            var s = row.Solution;
            try
            {
                var f2 = OrbitDesign.Case2Fields(s, _keepRangeDeg, _declareAtTargetAltitude);
                var (d, h, m, sec) = _harmonizedRptSeconds is long hp
                    ? OrbitDesign.DecomposePeriod(hp)
                    : f2.RptPrd!.Value;
                double drift = Math.Abs(s.DriftDegPerCycleAtTarget);
                string altLine = _declareAtTargetAltitude
                    ? drift > 1e-9
                        ? string.Create(inv,
                            $"declared at the target {_targetAltitudeKm:F1} km; station keeping absorbs {drift:F4} deg/cycle\n" +
                            $"(keep_rnge crossed in {_keepRangeDeg / drift:F1} cycle(s) ~ {_keepRangeDeg / drift * s.RepeatSecondsAtTarget / 86400.0:F1} d between corrections)")
                        : string.Create(inv,
                            $"declared at the target {_targetAltitudeKm:F1} km; station keeping absorbs negligible drift")
                    : string.Create(inv,
                        $"declared at the exact closing altitude {s.AltitudeKm:F2} km (zero correction)");
                if (_harmonizedRptSeconds is long hs2)
                    altLine += string.Create(inv,
                        $"\nrpt_prd harmonized to the constellation period ({hs2 / (OwnRptSeconds ?? hs2)} own cycle(s))");
                Case2Text = string.Create(inv,
                    $"f_stn_keep='Y'   keep_rnge = {f2.KeepRngeDeg:F3} deg (max {s.MaxKeepRangeDeg:F3})\n" +
                    $"rpt_prd_dd={d}  hh={h}  mm={m}  ss={sec}\n" +
                    $"track spacing {s.EquatorSpacingDeg:F3} deg, {s.Orbits} orbits / {s.NodalDays} nodal day(s)\n")
                    + altLine;
                KeepRangeValid = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Case2Text = ex.Message.Split('\n')[0];
                KeepRangeValid = false;
            }
        }
        else
        {
            Case2Text = "select a repeating candidate (or validate your own period) on the Repeat solver";
            KeepRangeValid = true;
        }
        KeepRangeHintText = _selectedSolution is { } hr ? BuildKeepHint(hr.Solution) : "";

        double rate3 = OrbitDesign.J2NodalRateDegPerSec(aTarget, _eccentricity, _inclinationDeg);
        Case3Text = ParsedPrecession() is { } custom
            ? string.Create(inv,
                $"f_precess='Y'   precession = {custom:E4} deg/s (admin-supplied)\n" +
                $"(plain-J2 at {_targetAltitudeKm:F1} km / i={_inclinationDeg:F1} would be {rate3:E4} deg/s)")
            : _precessionText.Trim().Length > 0
                ? "precession: not a number -- empty declares the J2 default"
                : string.Create(inv,
                    $"f_precess='Y'   precession = {rate3:E4} deg/s\n" +
                    $"(the plain-J2 declaration rate at {_targetAltitudeKm:F1} km / i={_inclinationDeg:F1})");
        RecomputeConstellation();
    }

    // keep_rnge is the operator's own tolerance promise; the solver only
    // bounds it (half the track spacing) and, at a fixed altitude, prices
    // it (how often the free-flight drift crosses it).
    private string BuildKeepHint(RepeatSolution s)
    {
        var inv = CultureInfo.InvariantCulture;
        string hint = string.Create(inv,
            $"max {s.MaxKeepRangeDeg:F3} deg (half the {s.EquatorSpacingDeg:F3} deg track spacing)");
        if (!_keepRangeValid) return "keep_rnge out of bounds -- " + hint;
        double drift = Math.Abs(s.DriftDegPerCycleAtTarget);
        return _declareAtTargetAltitude && drift > 1e-9
            ? hint + string.Create(inv, $"; drift crosses it in {_keepRangeDeg / drift:F1} cycle(s)")
            : hint;
    }

    private void RecomputeTrack()
    {
        var row = _selectedSolution;
        if (row is null)
        {
            TrackSegments = Array.Empty<IReadOnlyList<(double, double)>>();
            TrackClosureDeg = double.NaN;
            TrackChanged?.Invoke();
            return;
        }
        var s = row.Solution;
        // The map flies the DECLARED orbit: the exact closing altitude, or
        // the target altitude, where the closure gap equals the drift the
        // station keeping absorbs. Solo satellite here; the document owns
        // the whole-constellation overlay across shells.
        double altD = _declareAtTargetAltitude ? _targetAltitudeKm : s.AltitudeKm;
        var shell = new ConstellationShell
        {
            AltitudeKm = altD, InclinationDeg = _inclinationDeg,
            Eccentricity = _eccentricity, PlaneCount = 1, SatsPerPlane = 1,
        };
        var con = new Constellation(new[] { shell });
        double simDur = _declareAtTargetAltitude ? s.RepeatSecondsAtTarget : s.RepeatSeconds;
        int steps = Math.Min(40000, s.Orbits * 180);
        var (segments, first, last) = SampleTracks(con, simDur, steps);
        TrackSegments = segments;

        double la1 = first.LatDeg * Math.PI / 180.0, lo1 = first.LonDeg * Math.PI / 180.0;
        double la2 = last.LatDeg * Math.PI / 180.0, lo2 = last.LonDeg * Math.PI / 180.0;
        TrackClosureDeg = Math.Acos(Math.Clamp(
            Math.Sin(la1) * Math.Sin(la2) + Math.Cos(la1) * Math.Cos(la2) * Math.Cos(lo2 - lo1),
            -1.0, 1.0)) * 180.0 / Math.PI;
        OnPropertyChanged(nameof(TrackClosureText));
        TrackChanged?.Invoke();
    }

    public string TrackClosureText => double.IsNaN(TrackClosureDeg)
        ? ""
        : string.Create(CultureInfo.InvariantCulture,
            $"track closure after one cycle: {TrackClosureDeg:F4} deg")
          + (_declareAtTargetAltitude
              ? " (flown at the target altitude: the gap is the free-flight drift station keeping absorbs)"
              : "");

    // ---- constellation construction (Walker shell -> SNS tables) -------

    private int _planeCount = 4;
    public int PlaneCount { get => _planeCount; set { if (SetField(ref _planeCount, value)) RecomputeConstellation(); } }

    private int _satsPerPlane = 8;
    public int SatsPerPlane { get => _satsPerPlane; set { if (SetField(ref _satsPerPlane, value)) RecomputeConstellation(); } }

    private int _walkerPhasingF = 1;
    public int WalkerPhasingF { get => _walkerPhasingF; set { if (SetField(ref _walkerPhasingF, value)) RecomputeConstellation(); } }

    private double _lan0Deg;
    public double Lan0Deg { get => _lan0Deg; set { if (SetField(ref _lan0Deg, value)) RecomputeConstellation(); } }

    private double _lanSpreadDeg = 360.0;
    public double LanSpreadDeg { get => _lanSpreadDeg; set { if (SetField(ref _lanSpreadDeg, value)) RecomputeConstellation(); } }

    private double _inPlaneOffsetDeg;
    public double InPlaneOffsetDeg { get => _inPlaneOffsetDeg; set { if (SetField(ref _inPlaneOffsetDeg, value)) RecomputeConstellation(); } }

    private double _argPerigeeDeg;
    public double ArgPerigeeDeg { get => _argPerigeeDeg; set { if (SetField(ref _argPerigeeDeg, value)) RecomputeConstellation(); } }

    private string _opHeightText = "";
    /// <summary>Minimum operating height (km); empty = the perigee altitude.</summary>
    public string OpHeightText { get => _opHeightText; set { if (SetField(ref _opHeightText, value)) RecomputeConstellation(); } }

    private int _caseChoice = 1;
    /// <summary>0 = Case 1 free drift, 1 = Case 2 station-kept, 2 = Case 3 declared.</summary>
    public int CaseChoice
    {
        get => _caseChoice;
        set
        {
            if (!SetField(ref _caseChoice, value)) return;
            OnPropertyChanged(nameof(IsCase2));
            RecomputeConstellation();
        }
    }

    /// <summary>True when the station-keeping case is Case 2 (gates the harmonize control).</summary>
    public bool IsCase2 => _caseChoice == 1;

    private IReadOnlyList<SrsOrbitRow> _orbitRows = Array.Empty<SrsOrbitRow>();
    public IReadOnlyList<SrsOrbitRow> OrbitRows { get => _orbitRows; private set => SetField(ref _orbitRows, value); }

    private IReadOnlyList<SrsPhaseRow> _phaseRows = Array.Empty<SrsPhaseRow>();
    public IReadOnlyList<SrsPhaseRow> PhaseRows { get => _phaseRows; private set => SetField(ref _phaseRows, value); }

    private string _snsStatusText = "";
    public string SnsStatusText { get => _snsStatusText; set => SetField(ref _snsStatusText, value); }

    private string _shellName = "";
    /// <summary>Optional user-given shell name; rides in every list and the design file.</summary>
    public string ShellName
    {
        get => _shellName;
        set { if (SetField(ref _shellName, value)) OnPropertyChanged(nameof(ShellSummary)); }
    }

    /// <summary>One-line display for the document's shells list.</summary>
    public string ShellSummary
        => (_shellName.Trim().Length > 0 ? _shellName.Trim() + " — " : "")
           + string.Create(CultureInfo.InvariantCulture,
               $"{_targetAltitudeKm:F1} km / i {_inclinationDeg:F1} · case {_caseChoice + 1} · {_planeCount}x{_satsPerPlane}");

    /// <summary>The designed shell: the declared altitude with the chosen case's fields.</summary>
    public ConstellationShell BuildShell()
    {
        // Cases 1 and 3 fly the target orbit as-is; Case 2 declares at the
        // target altitude too by default (station keeping absorbs the
        // drift) and only adopts the solved candidate's exact altitude
        // when the operator opts out of the default.
        double alt = _caseChoice == 1 && !_declareAtTargetAltitude
            ? _selectedSolution?.Solution.AltitudeKm ?? _targetAltitudeKm
            : _targetAltitudeKm;
        double? opHt = double.TryParse(_opHeightText, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double oh) ? oh : null;
        var shell = new ConstellationShell
        {
            AltitudeKm = alt, InclinationDeg = _inclinationDeg, Eccentricity = _eccentricity,
            PlaneCount = Math.Max(1, _planeCount), SatsPerPlane = Math.Max(1, _satsPerPlane),
            WalkerPhasingF = _walkerPhasingF, Lan0Deg = _lan0Deg, LanSpreadDeg = _lanSpreadDeg,
            InPlaneOffsetDeg = _inPlaneOffsetDeg, ArgumentOfPerigeeDeg = _argPerigeeDeg,
            OperatingHeightKm = opHt,
        };
        return _caseChoice switch
        {
            1 when _selectedSolution is not null => shell with
            {
                StationKeeping = true, WDeltaDeg = _keepRangeDeg,
                RepeatPeriod = _harmonizedRptSeconds is long hs
                    ? OrbitDesign.DecomposePeriod(hs)
                    : _declareAtTargetAltitude
                        ? _selectedSolution.Solution.RptPrdAtTarget
                        : _selectedSolution.Solution.RptPrd,
            },
            2 => shell with
            {
                PrecessionSupplied = true,
                PrecessionRateDegPerSec = ParsedPrecession() ?? OrbitDesign.J2NodalRateDegPerSec(
                    OrbitalConstants.EarthRadiusKm + alt, _eccentricity, _inclinationDeg),
            },
            _ => shell with { NOrbits = Math.Max(1, _nOrbits) },
        };
    }

    /// <summary>A single-shell preview notice (orbit + phase tables only).</summary>
    public SrsNotice BuildNotice()
    {
        var n = new SrsNotice { NtcId = 0, SatName = "DESIGN", Adm = "XXX" };
        n.AddShell(BuildShell());
        return n;
    }

    private void RecomputeConstellation()
    {
        try
        {
            var n = BuildNotice();
            OrbitRows = n.Orbits;
            PhaseRows = n.Phases;
            SnsStatusText = $"{n.Orbits.Count} orbit row(s), {n.Phases.Count} phase row(s)"
                + (_caseChoice == 1 && _selectedSolution is null ? " -- select a repeating candidate for Case 2" : "");
        }
        catch (Exception ex)
        {
            OrbitRows = Array.Empty<SrsOrbitRow>();
            PhaseRows = Array.Empty<SrsPhaseRow>();
            SnsStatusText = ex.Message;
        }
        OnPropertyChanged(nameof(ShellSummary));
    }

    /// <summary>
    /// This shell's full Walker pattern for one declared cycle -- the
    /// building block of the document's constellation-track overlay.
    /// Empty without a selected repeat.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<(double LatDeg, double LonDeg)>> BuildShellTrackSegments(int sampleBudget)
    {
        if (_selectedSolution is not { } row) return Array.Empty<IReadOnlyList<(double, double)>>();
        var s = row.Solution;
        double altD = _declareAtTargetAltitude ? _targetAltitudeKm : s.AltitudeKm;
        var shell = new ConstellationShell
        {
            AltitudeKm = altD, InclinationDeg = _inclinationDeg, Eccentricity = _eccentricity,
            PlaneCount = Math.Max(1, _planeCount), SatsPerPlane = Math.Max(1, _satsPerPlane),
            WalkerPhasingF = _walkerPhasingF, Lan0Deg = _lan0Deg, LanSpreadDeg = _lanSpreadDeg,
            InPlaneOffsetDeg = _inPlaneOffsetDeg, ArgumentOfPerigeeDeg = _argPerigeeDeg,
        };
        var con = new Constellation(new[] { shell });
        double simDur = _declareAtTargetAltitude ? s.RepeatSecondsAtTarget : s.RepeatSeconds;
        // Budgeted sampling with a resolution floor so tracks stay smooth.
        int steps = Math.Min(Math.Min(40000, s.Orbits * 180),
            Math.Max(s.Orbits * 20, Math.Max(1, sampleBudget) / con.SatelliteCount));
        return SampleTracks(con, simDur, steps).Segments;
    }

    private static (List<IReadOnlyList<(double, double)>> Segments,
        (double LatDeg, double LonDeg) First, (double LatDeg, double LonDeg) Last)
        SampleTracks(Constellation con, double simDur, int steps)
    {
        var segments = new List<IReadOnlyList<(double, double)>>();
        (double LatDeg, double LonDeg) first = default, last = default;
        for (int sat = 0; sat < con.SatelliteCount; sat++)
        {
            var current = new List<(double, double)>();
            double? prevLon = null;
            for (int k = 0; k <= steps; k++)
            {
                var st = con.StateAt(sat, simDur * k / steps, simDur);
                var pt = (st.SubSatLatDeg, st.SubSatLonDeg);
                if (sat == 0 && k == 0) first = pt;
                if (sat == 0) last = pt;
                if (prevLon is double pl && Math.Abs(pt.SubSatLonDeg - pl) > 180.0)
                {
                    if (current.Count > 1) segments.Add(current);
                    current = new List<(double, double)>();
                }
                current.Add(pt);
                prevLon = pt.SubSatLonDeg;
            }
            if (current.Count > 1) segments.Add(current);
        }
        return (segments, first, last);
    }

    /// <summary>The design as its file form, including the selected candidate.</summary>
    public OrbitDesignData BuildDesignData()
    {
        var sol = _selectedSolution?.Solution;
        // The stored rpt_prd is the DECLARED one: harmonized to the
        // constellation period when set, else the shell's own cycle at
        // the declared altitude.
        (int Days, int Hours, int Minutes, int Seconds)? rpt = _harmonizedRptSeconds is long hs
            ? OrbitDesign.DecomposePeriod(hs)
            : _declareAtTargetAltitude ? sol?.RptPrdAtTarget : sol?.RptPrd;
        return new OrbitDesignData(6, _targetAltitudeKm, _inclinationDeg, _eccentricity,
            _maxOrbitsPerCycle, _searchBandKm, _planeCount, _satsPerPlane, _walkerPhasingF,
            _lan0Deg, _lanSpreadDeg, _inPlaneOffsetDeg, _argPerigeeDeg, _opHeightText,
            _caseChoice, _keepRangeDeg, _nOrbits, _victimBeamwidthText,
            sol?.AltitudeKm, sol?.Orbits, sol?.NodalDays,
            rpt?.Days, rpt?.Hours, rpt?.Minutes, rpt?.Seconds,
            ParsedPrecession(), _declareAtTargetAltitude, _harmonizedRptSeconds, _shellName);
    }

    public string BuildDesignJson() => OrbitDesignFileCodec.Save(BuildDesignData());

    public void LoadDesignJson(string json)
    {
        var d = OrbitDesignFileCodec.Load(json);
        ShellName = d.ShellName;
        TargetAltitudeKm = d.TargetAltitudeKm; InclinationDeg = d.InclinationDeg;
        Eccentricity = d.Eccentricity; MaxOrbitsPerCycle = d.MaxOrbitsPerCycle;
        SearchBandKm = d.SearchBandKm; PlaneCount = d.PlaneCount; SatsPerPlane = d.SatsPerPlane;
        WalkerPhasingF = d.WalkerPhasingF; Lan0Deg = d.Lan0Deg; LanSpreadDeg = d.LanSpreadDeg;
        InPlaneOffsetDeg = d.InPlaneOffsetDeg; ArgPerigeeDeg = d.ArgPerigeeDeg;
        OpHeightText = d.OpHeightText; CaseChoice = d.CaseChoice; KeepRangeDeg = d.KeepRangeDeg;
        NOrbits = d.NOrbits; VictimBeamwidthText = d.VictimBeamwidthText;
        PrecessionText = d.PrecessionDegPerSec?.ToString(CultureInfo.InvariantCulture) ?? "";
        DeclareAtTargetAltitude = d.DeclareAtTargetAltitude;
        // Reselect the stored candidate when it is still in the solution
        // set; a pair the scan cannot see (saved from the own-period
        // validator) is re-entered through the validator instead.
        if (d.SelectedOrbits is int k)
        {
            var match = Solutions.FirstOrDefault(
                r => r.Orbits == k && r.NodalDays == d.SelectedNodalDays);
            if (match is null && d.SelectedNodalDays is int m)
            {
                CheckOrbitsText = k.ToString(CultureInfo.InvariantCulture);
                CheckDaysText = m.ToString(CultureInfo.InvariantCulture);
                match = Solutions.FirstOrDefault(
                    r => r.Orbits == k && r.NodalDays == d.SelectedNodalDays);
            }
            SelectedSolution = match ?? SelectedSolution;
        }
        // Restore last: the reselect above clears the override.
        HarmonizedRptSeconds = d.HarmonizedRptSeconds;
    }

    /// <summary>The clipboard payload: all three case previews for the selected solution.</summary>
    public string BuildCopyText()
    {
        var row = _selectedSolution;
        if (row is null) return "";
        var s = row.Solution;
        return string.Create(CultureInfo.InvariantCulture,
            $"SNS v10 orbit fields -- target {_targetAltitudeKm:F2} km / i {_inclinationDeg:F1} deg / e {_eccentricity:F3}; " +
            $"candidate {s.Orbits}/{s.NodalDays} @ {s.AltitudeKm:F2} km\n" +
            $"[Case 1 free drift]\n{Case1Text}\n[Case 2 station-kept repeating]\n{Case2Text}\n" +
            $"[Case 3 declared precession]\n{Case3Text}\n");
    }
}
