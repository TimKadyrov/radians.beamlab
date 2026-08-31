using System;
using System.Collections.Generic;
using System.Linq;
using Radians.Orbits.Core.Utilities;

namespace radians.beamlab;

/// <summary>A repeating ground-track candidate: k nodal orbits in m node-relative Earth turns.</summary>
public sealed record RepeatSolution
{
    public required int Orbits { get; init; }
    public required int NodalDays { get; init; }
    /// <summary>Mean altitude (km) at which the repeat is exact for the given e, i.</summary>
    public required double AltitudeKm { get; init; }
    public required double AltitudeDeltaKm { get; init; }
    /// <summary>Cycle duration k * T_nodal at the exact altitude (s).</summary>
    public required double RepeatSeconds { get; init; }
    /// <summary>Cycle duration k * T_nodal at the TARGET altitude (s) -- what an at-altitude declaration files.</summary>
    public required double RepeatSecondsAtTarget { get; init; }
    /// <summary>Residual node drift per cycle when flown at the TARGET altitude instead (deg).</summary>
    public required double DriftDegPerCycleAtTarget { get; init; }
    /// <summary>Equator spacing between adjacent tracks, 360/k (deg).</summary>
    public required double EquatorSpacingDeg { get; init; }
    /// <summary>SNS rpt_prd_dd/hh/mm/ss decomposition of RepeatSeconds.</summary>
    public required (int Days, int Hours, int Minutes, int Seconds) RptPrd { get; init; }
    /// <summary>SNS rpt_prd_dd/hh/mm/ss decomposition of RepeatSecondsAtTarget.</summary>
    public required (int Days, int Hours, int Minutes, int Seconds) RptPrdAtTarget { get; init; }
    /// <summary>Largest keep_rnge that keeps swept tracks distinct: half the spacing (deg).</summary>
    public required double MaxKeepRangeDeg { get; init; }
}

/// <summary>
/// Outcome of validating a user-entered repeat pair. The entered pair is
/// reduced to coprime terms first (the true cycle); Solution is null when
/// no altitude in the whole 100..30000 km band closes the reduced pair,
/// and WithinBand says whether its exact altitude lies inside the user's
/// search band around the target.
/// </summary>
public sealed record RepeatCheck
{
    public required int OrbitsEntered { get; init; }
    public required int NodalDaysEntered { get; init; }
    /// <summary>Reduced coprime orbit count actually solved.</summary>
    public required int Orbits { get; init; }
    /// <summary>Reduced coprime nodal-day count actually solved.</summary>
    public required int NodalDays { get; init; }
    public required bool Reduced { get; init; }
    public required bool WithinBand { get; init; }
    public required RepeatSolution? Solution { get; init; }
}

/// <summary>The Case-1 artificial-precession numbers for a given run length.</summary>
public sealed record ArtificialPrecessionPlan
{
    public required double SPassDeg { get; init; }
    public required double SGridDeg { get; init; }
    public required double RateRadPerSec { get; init; }
    public required double RateDegPerSec { get; init; }
    /// <summary>
    /// The spacing the propagated run actually measures: 2*S_pass - S_grid --
    /// the D6.3.2 Steps 8-11 prescription lands one adjustment past the grid
    /// (see the upstream spacing note); bounded within two grid cells.
    /// </summary>
    public required double MeasuredSpacingDeg { get; init; }
    public required double NodalPeriodSec { get; init; }
    public required double RunDurationSec { get; init; }
}

/// <summary>Preview of the SNS v10 orbit-row fields a station-keeping choice implies.</summary>
public sealed record SnsOrbitFieldsPreview(
    char FStnKeep, double? KeepRngeDeg,
    char FPrecess, double? PrecessionDegPerSec,
    (int Days, int Hours, int Minutes, int Seconds)? RptPrd);

/// <summary>
/// Prototyping helpers for the SNS v10 orbit parameters: repeating
/// ground-track solutions (rpt_prd), the keep_rnge tolerance rule, the
/// Case-1 artificial-precession numbers and the Case-3 declared J2 rate.
/// Everything derives from the same J2 secular rates the vendored
/// propagator integrates (<see cref="ArtificialPrecession.NodalPassGeometry"/>),
/// so a prototyped declaration and the propagated behaviour agree by
/// construction.
/// </summary>
public static class OrbitDesign
{
    /// <summary>Westward node shift per nodal orbit (deg) and the nodal period (s).</summary>
    public static (double SPassDeg, double NodalPeriodSec) NodalPassGeometry(
        double semiMajorAxisKm, double eccentricity, double inclinationDeg)
        => ArtificialPrecession.NodalPassGeometry(semiMajorAxisKm, eccentricity, inclinationDeg);

    /// <summary>
    /// The declaration-side J2 nodal regression rate (deg/s) for a Case-3
    /// filing: -1.5 n J2 (Re/p)^2 cos i with the plain Keplerian mean
    /// motion -- exactly the value the BL dataset declares for shell C.
    /// </summary>
    public static double J2NodalRateDegPerSec(double semiMajorAxisKm, double eccentricity,
        double inclinationDeg)
    {
        double n = Math.Sqrt(OrbitalConstants.MuEarth / Math.Pow(semiMajorAxisKm, 3.0));
        double p = semiMajorAxisKm * (1.0 - eccentricity * eccentricity);
        double rateRad = -1.5 * n * OrbitalConstants.J2
            * Math.Pow(OrbitalConstants.EarthRadiusKm / p, 2.0)
            * Math.Cos(inclinationDeg * Math.PI / 180.0);
        return rateRad * 180.0 / Math.PI;
    }

    /// <summary>Case-1 numbers for nOrbits equatorial passes, including what the run will measure.</summary>
    public static ArtificialPrecessionPlan PrecessionPlan(double semiMajorAxisKm,
        double eccentricity, double inclinationDeg, int nOrbits)
    {
        if (nOrbits <= 0) throw new ArgumentOutOfRangeException(nameof(nOrbits));
        var (spass, tNodal) = NodalPassGeometry(semiMajorAxisKm, eccentricity, inclinationDeg);
        double sgrid = 360.0 * Math.Floor(nOrbits * spass / 360.0) / nOrbits;
        double rateRad = ArtificialPrecession.RadPerSec(semiMajorAxisKm, eccentricity,
            inclinationDeg, nOrbits);
        return new ArtificialPrecessionPlan
        {
            SPassDeg = spass,
            SGridDeg = sgrid,
            RateRadPerSec = rateRad,
            RateDegPerSec = rateRad * 180.0 / Math.PI,
            MeasuredSpacingDeg = 2.0 * spass - sgrid,
            NodalPeriodSec = tNodal,
            RunDurationSec = nOrbits * tNodal,
        };
    }

    /// <summary>
    /// Repeating-track candidates near a target mean altitude: pairs (k, m)
    /// with k coprime nodal orbits per m node-relative Earth turns, each with
    /// the exact altitude solving k * S_pass = 360 m, ordered by distance
    /// from the target. searchBandKm bounds the altitude solve around the
    /// target; candidates whose exact altitude falls outside are skipped.
    /// </summary>
    public static IReadOnlyList<RepeatSolution> RepeatSolutions(double targetAltitudeKm,
        double eccentricity, double inclinationDeg, int maxOrbitsPerCycle,
        int take = 8, double searchBandKm = 400.0)
    {
        double aTarget = OrbitalConstants.EarthRadiusKm + targetAltitudeKm;
        var (spassTarget, tNodalTarget) = NodalPassGeometry(aTarget, eccentricity, inclinationDeg);

        var results = new List<RepeatSolution>();
        for (int k = 1; k <= maxOrbitsPerCycle; k++)
        {
            int m = (int)Math.Round(k * spassTarget / 360.0);
            if (m < 1 || Gcd(k, m) != 1) continue;

            double want = 360.0 * m / k;
            double aExact = SolveAltitudeForSPass(want, aTarget, eccentricity, inclinationDeg,
                searchBandKm);
            if (double.IsNaN(aExact)) continue;

            var (_, tNodalExact) = NodalPassGeometry(aExact, eccentricity, inclinationDeg);
            double repeatSec = k * tNodalExact;
            results.Add(new RepeatSolution
            {
                Orbits = k,
                NodalDays = m,
                AltitudeKm = aExact - OrbitalConstants.EarthRadiusKm,
                AltitudeDeltaKm = aExact - aTarget,
                RepeatSeconds = repeatSec,
                RepeatSecondsAtTarget = k * tNodalTarget,
                DriftDegPerCycleAtTarget = k * spassTarget - 360.0 * m,
                EquatorSpacingDeg = 360.0 / k,
                RptPrd = DecomposePeriod(repeatSec),
                RptPrdAtTarget = DecomposePeriod(k * tNodalTarget),
                MaxKeepRangeDeg = 180.0 / k,
            });
        }
        return results.OrderBy(s => Math.Abs(s.AltitudeDeltaKm)).Take(take).ToList();
    }

    /// <summary>
    /// Validates a user-entered repeat pair (orbits nodal orbits in
    /// nodalDays node-relative Earth turns) against the target orbit. A
    /// non-coprime pair is reduced first -- the true cycle is the reduced
    /// one. The exact altitude is solved over the whole 100..30000 km band
    /// so an out-of-band pair still reports its altitude, flagged through
    /// WithinBand; a null Solution means no altitude in that band closes
    /// the pair at this inclination.
    /// </summary>
    public static RepeatCheck CheckRepeat(double targetAltitudeKm, double eccentricity,
        double inclinationDeg, int orbits, int nodalDays, double searchBandKm)
    {
        if (orbits < 1) throw new ArgumentOutOfRangeException(nameof(orbits));
        if (nodalDays < 1) throw new ArgumentOutOfRangeException(nameof(nodalDays));
        int g = Gcd(orbits, nodalDays);
        int k = orbits / g, m = nodalDays / g;

        double aTarget = OrbitalConstants.EarthRadiusKm + targetAltitudeKm;
        var (spassTarget, tNodalTarget) = NodalPassGeometry(aTarget, eccentricity, inclinationDeg);
        const double loAltKm = 100.0, hiAltKm = 30000.0;
        double aExact = SolveAltitudeForSPass(360.0 * m / k,
            OrbitalConstants.EarthRadiusKm + 0.5 * (loAltKm + hiAltKm),
            eccentricity, inclinationDeg, 0.5 * (hiAltKm - loAltKm));

        RepeatSolution? sol = null;
        if (!double.IsNaN(aExact))
        {
            var (_, tNodalExact) = NodalPassGeometry(aExact, eccentricity, inclinationDeg);
            double repeatSec = k * tNodalExact;
            sol = new RepeatSolution
            {
                Orbits = k,
                NodalDays = m,
                AltitudeKm = aExact - OrbitalConstants.EarthRadiusKm,
                AltitudeDeltaKm = aExact - aTarget,
                RepeatSeconds = repeatSec,
                RepeatSecondsAtTarget = k * tNodalTarget,
                DriftDegPerCycleAtTarget = k * spassTarget - 360.0 * m,
                EquatorSpacingDeg = 360.0 / k,
                RptPrd = DecomposePeriod(repeatSec),
                RptPrdAtTarget = DecomposePeriod(k * tNodalTarget),
                MaxKeepRangeDeg = 180.0 / k,
            };
        }
        return new RepeatCheck
        {
            OrbitsEntered = orbits,
            NodalDaysEntered = nodalDays,
            Orbits = k,
            NodalDays = m,
            Reduced = g != 1,
            WithinBand = sol is not null && Math.Abs(sol.AltitudeDeltaKm) <= searchBandKm,
            Solution = sol,
        };
    }

    /// <summary>SNS ddd/hh/mm/ss decomposition, rounded to whole seconds.</summary>
    public static (int Days, int Hours, int Minutes, int Seconds) DecomposePeriod(double seconds)
    {
        long total = (long)Math.Round(seconds);
        return ((int)(total / 86400), (int)(total % 86400 / 3600),
                (int)(total % 3600 / 60), (int)(total % 60));
    }

    /// <summary>
    /// Geocentric half-angle (deg) of the victim main-beam crossing at the
    /// non-GSO altitude -- Rec. S.1503-4 Part D eq (3):
    /// phi = theta3dB/2 - arcsin[Re/(Re+h) * sin(theta3dB/2)].
    /// </summary>
    public static double BeamCrossingHalfAngleDeg(double beamwidth3dBDeg, double altitudeKm)
    {
        if (beamwidth3dBDeg <= 0.0 || altitudeKm <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(beamwidth3dBDeg));
        double halfRad = beamwidth3dBDeg * Math.PI / 360.0;
        double k = OrbitalConstants.EarthRadiusKm
                 / (OrbitalConstants.EarthRadiusKm + altitudeKm);
        return (halfRad - Math.Asin(k * Math.Sin(halfRad))) * 180.0 / Math.PI;
    }

    /// <summary>
    /// Case-1 run length from the victim beam (Sec. D4.6.2 Steps 5-7):
    /// S_req = 2 phi / N_tracks and N_orbits = ceil(180 / S_req), with
    /// N_tracks = 16 per Sec. D4.5.
    /// </summary>
    public static int SuggestedNOrbits(double beamwidth3dBDeg, double altitudeKm, int nTracks = 16)
    {
        if (nTracks < 1) throw new ArgumentOutOfRangeException(nameof(nTracks));
        double phi = BeamCrossingHalfAngleDeg(beamwidth3dBDeg, altitudeKm);
        double sReq = 2.0 * phi / nTracks;
        return (int)Math.Ceiling(180.0 / sReq);
    }

    /// <summary>Case 1 (free drift): neither flag; the examination derives its own precession.</summary>
    public static SnsOrbitFieldsPreview Case1Fields() => new('N', null, 'N', null, null);

    /// <summary>
    /// Case 2 (station-kept repeating track): keep_rnge must stay below half
    /// the track spacing or the swept deadbands of adjacent tracks overlap.
    /// atTargetAltitude declares the cycle at the target altitude (station
    /// keeping absorbs the drift) instead of the exact closing altitude.
    /// </summary>
    public static SnsOrbitFieldsPreview Case2Fields(RepeatSolution solution, double keepRangeDeg,
        bool atTargetAltitude = false)
    {
        if (keepRangeDeg <= 0.0 || keepRangeDeg >= solution.MaxKeepRangeDeg)
            throw new ArgumentOutOfRangeException(nameof(keepRangeDeg),
                $"keep_rnge must lie in (0, {solution.MaxKeepRangeDeg:F3}) deg for a " +
                $"{solution.Orbits}-orbit cycle (half the {solution.EquatorSpacingDeg:F3} deg spacing)");
        return new('Y', keepRangeDeg, 'N', null,
            atTargetAltitude ? solution.RptPrdAtTarget : solution.RptPrd);
    }

    /// <summary>Case 3 (administration-supplied precession).</summary>
    public static SnsOrbitFieldsPreview Case3Fields(double precessionDegPerSec)
        => new('N', null, 'Y', precessionDegPerSec, null);

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }

    // S_pass grows monotonically with the semi-major axis across the LEO/MEO
    // band (longer nodal period, more Earth turn per orbit), so a bracketed
    // bisection is robust.
    private static double SolveAltitudeForSPass(double wantSPassDeg, double aCentreKm,
        double eccentricity, double inclinationDeg, double bandKm)
    {
        double F(double a) => NodalPassGeometry(a, eccentricity, inclinationDeg).SPassDeg
                            - wantSPassDeg;
        double lo = aCentreKm - bandKm, hi = aCentreKm + bandKm;
        double flo = F(lo), fhi = F(hi);
        if (flo > 0.0 || fhi < 0.0) return double.NaN;   // repeat lies outside the band
        for (int i = 0; i < 90; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (F(mid) < 0.0) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}
