using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using radians.beamlab;
using Radians.Orbits.Core.Utilities;

namespace radians.beamlab.app;

/// <summary>One repeat-solution row shaped for the grid.</summary>
public sealed record OrbitSolutionRow(RepeatSolution Solution)
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

    private int _nOrbits = 288;
    public int NOrbits
    {
        get => _nOrbits;
        set { if (SetField(ref _nOrbits, value)) RecomputeDetails(); }
    }

    private double _keepRangeDeg = 0.5;
    public double KeepRangeDeg
    {
        get => _keepRangeDeg;
        set { if (SetField(ref _keepRangeDeg, value)) RecomputeDetails(); }
    }

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
        set { if (SetField(ref _selectedSolution, value)) { RecomputeDetails(); RecomputeTrack(); } }
    }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }

    private string _case1Text = "";
    public string Case1Text { get => _case1Text; private set => SetField(ref _case1Text, value); }

    private string _case2Text = "";
    public string Case2Text { get => _case2Text; private set => SetField(ref _case2Text, value); }

    private bool _keepRangeValid = true;
    public bool KeepRangeValid { get => _keepRangeValid; private set => SetField(ref _keepRangeValid, value); }

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

    private void Recompute()
    {
        if (_targetAltitudeKm < 200.0 || _targetAltitudeKm > 45000.0
            || _inclinationDeg <= 0.0 || _inclinationDeg >= 180.0
            || _eccentricity is < 0.0 or >= 0.9
            || _maxOrbitsPerCycle is < 1 or > 2000 || _searchBandKm <= 0.0)
        {
            Solutions = Array.Empty<OrbitSolutionRow>();
            SelectedSolution = null;
            StatusText = "inputs out of range";
            return;
        }

        var sols = OrbitDesign.RepeatSolutions(_targetAltitudeKm, _eccentricity, _inclinationDeg,
            _maxOrbitsPerCycle, take: 10, searchBandKm: _searchBandKm);
        Solutions = sols.Select(s => new OrbitSolutionRow(s)).ToList();
        StatusText = sols.Count == 0
            ? "no repeat inside the search band"
            : $"{sols.Count} candidate(s); nearest {sols[0].AltitudeDeltaKm:+0.00;-0.00} km from target";
        SelectedSolution = Solutions.FirstOrDefault();
    }

    private void RecomputeDetails()
    {
        var row = _selectedSolution;
        if (row is null)
        {
            Case1Text = Case2Text = Case3Text = "";
            KeepRangeValid = true;
            return;
        }
        var s = row.Solution;
        double aKm = OrbitalConstants.EarthRadiusKm + s.AltitudeKm;
        var inv = CultureInfo.InvariantCulture;

        if (_nOrbits >= 1)
        {
            var plan = OrbitDesign.PrecessionPlan(aKm, _eccentricity, _inclinationDeg, _nOrbits);
            Case1Text = string.Create(inv,
                $"f_stn_keep='N', f_precess='N' (rate derived by the examination)\n" +
                $"S_pass = {plan.SPassDeg:F4} deg   S_grid = {plan.SGridDeg:F4} deg\n" +
                $"rate = {plan.RateDegPerSec:E4} deg/s ({plan.RateRadPerSec:E4} rad/s)\n" +
                $"measured spacing = {plan.MeasuredSpacingDeg:F4} deg (2*S_pass - S_grid,\n" +
                $"one step past the grid -- the documented D6.3.2 quirk)\n" +
                $"run = {_nOrbits} orbits = {plan.RunDurationSec / 86400.0:F2} days");
        }
        else Case1Text = "NOrbits must be >= 1";

        try
        {
            var f2 = OrbitDesign.Case2Fields(s, _keepRangeDeg);
            var (d, h, m, sec) = f2.RptPrd!.Value;
            Case2Text = string.Create(inv,
                $"f_stn_keep='Y'   keep_rnge = {f2.KeepRngeDeg:F3} deg (max {s.MaxKeepRangeDeg:F3})\n" +
                $"rpt_prd_dd={d}  hh={h}  mm={m}  ss={sec}\n" +
                $"track spacing {s.EquatorSpacingDeg:F3} deg, {s.Orbits} orbits / {s.NodalDays} nodal day(s)");
            KeepRangeValid = true;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Case2Text = ex.Message.Split('\n')[0];
            KeepRangeValid = false;
        }

        double rate3 = OrbitDesign.J2NodalRateDegPerSec(aKm, _eccentricity, _inclinationDeg);
        Case3Text = string.Create(inv,
            $"f_precess='Y'   precession = {rate3:E4} deg/s\n" +
            $"(the plain-J2 declaration rate at {s.AltitudeKm:F1} km / i={_inclinationDeg:F1})");
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
        var shell = new ConstellationShell
        {
            AltitudeKm = s.AltitudeKm, InclinationDeg = _inclinationDeg,
            Eccentricity = _eccentricity, PlaneCount = 1, SatsPerPlane = 1,
        };
        var con = new Constellation(new[] { shell });
        double simDur = s.RepeatSeconds;
        int steps = Math.Min(40000, s.Orbits * 180);

        var segments = new List<IReadOnlyList<(double, double)>>();
        var current = new List<(double, double)>();
        double? prevLon = null;
        (double LatDeg, double LonDeg) first = default, last = default;
        for (int k = 0; k <= steps; k++)
        {
            var st = con.StateAt(0, simDur * k / steps, simDur);
            var pt = (st.SubSatLatDeg, st.SubSatLonDeg);
            if (k == 0) first = pt;
            last = pt;
            if (prevLon is double pl && Math.Abs(pt.SubSatLonDeg - pl) > 180.0)
            {
                if (current.Count > 1) segments.Add(current);
                current = new List<(double, double)>();
            }
            current.Add(pt);
            prevLon = pt.SubSatLonDeg;
        }
        if (current.Count > 1) segments.Add(current);
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
            $"track closure after one cycle: {TrackClosureDeg:F4} deg");

    /// <summary>The clipboard payload: all three case previews for the selected solution.</summary>
    public string BuildCopyText()
    {
        var row = _selectedSolution;
        if (row is null) return "";
        var s = row.Solution;
        return string.Create(CultureInfo.InvariantCulture,
            $"SNS v10 orbit fields -- alt {s.AltitudeKm:F2} km, i {_inclinationDeg:F1} deg, e {_eccentricity:F3}\n" +
            $"[Case 1 free drift]\n{Case1Text}\n[Case 2 station-kept repeating]\n{Case2Text}\n" +
            $"[Case 3 declared precession]\n{Case3Text}\n");
    }
}
