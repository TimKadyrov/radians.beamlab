using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Radians.Orbits.Core.Utilities;

namespace radians.beamlab;

/// <summary>
/// The examination's time step for epfd(down), Rec. ITU-R S.1503-4 Sec. D4.
///
/// Sec. D4.2: the fine step is the time a non-GSO satellite needs to cross the
/// GSO earth station's 3 dB beam where that crossing is fastest (the earth
/// station directly under the GSO satellite), sampled N_hit = 16 times
/// (Sec. D4.5), rounded to the nearest non-zero millisecond:
///
///   dt_ref = dt / N_hit,   dt = 2 phi / omega                         (1), (2)
///   phi    = theta3dB/2 - asin[ Re/(Re+h) sin(theta3dB/2) ]
///   omega  = sqrt( (omega_s cos i - omega_e)^2 + (omega_s sin i)^2 )    (3)
///   omega_s = 0.071 / ((Re+h)/Re)^1.5 deg/s
///
/// h is the lowest orbit altitude, the minimum operating height for an
/// elliptical orbit (Note 1); with several orbit types the smallest step
/// governs. The form of omega_s is the one both implementations in hand
/// use (the S.1503-2-based examination software and SHARC-Orbit); its
/// constant differs from the Kepler rate at the Earth's radius
/// (0.0710152 deg/s with the Table 2 constants) by 0.02 per cent.
///
/// Sec. D4.7.1, the dual-time-step option: a coarse step of 1.5 deg
/// topocentric, held to an integer number of fine steps,
/// N_coarse = floor(N_hit * 1.5 / theta3dB).
///
/// Sec. D4.1: for a non-repeating orbit whose run would exceed 1e8 steps,
/// N_hit is reduced to N_hit / min(N_coarse, sqrt(N_satellites)), the step
/// recomputed, and N_coarse becomes floor(N'_hit / N_hit * N_coarse).
///
/// The total run time of Sec. D4.6 is computed elsewhere (OrbitDesign); the
/// callers here choose their own run length, which is passed in only to
/// test the Sec. D4.1 condition.
/// </summary>
public static class S1503TimeStep
{
    /// <summary>Samples across one main-beam pass (Sec. D4.5).</summary>
    public const double Nhit = 16.0;
    /// <summary>Coarse step as a topocentric angle, deg (Sec. D4.7.1).</summary>
    public const double PhiCoarseDeg = 1.5;
    /// <summary>Step count above which a non-repeating run reduces N_hit (Sec. D4.1).</summary>
    public const double LongRunSteps = 1e8;
    /// <summary>omega_s at the Earth's surface radius, deg/s (Sec. D4.2 eq (3)).</summary>
    public const double SurfaceRateDegPerSec = 0.071;

    /// <summary>The step plan for one examination.</summary>
    public sealed record Plan(
        double FineStepSec, int NCoarse, double PassTimeSec, double Theta3dBDeg,
        double NhitUsed, double GoverningAltitudeKm, double GoverningInclinationDeg,
        bool LongRunReduced)
    {
        /// <summary>Coarse step, s: N_coarse fine steps.</summary>
        public double CoarseStepSec => NCoarse * FineStepSec;

        public string Text
        {
            get
            {
                var inv = CultureInfo.InvariantCulture;
                return string.Create(inv, $"fine step {FineStepSec:0.000} s (S.1503-4 Sec. D4.2: a {PassTimeSec:F2} s pass across the {Theta3dBDeg:F3} deg beam at {GoverningAltitudeKm:F0} km / {GoverningInclinationDeg:F1} deg, N_hit {NhitUsed:0.###}")
                    + (LongRunReduced ? ", reduced for a long non-repeating run, Sec. D4.1" : "")
                    + string.Create(inv, $"); coarse step {CoarseStepSec:0.000} s = {NCoarse} fine steps (Sec. D4.7.1)");
            }
        }
    }

    /// <summary>omega_s, deg/s, at altitude h (Sec. D4.2 eq (3)).</summary>
    public static double SatelliteRateDegPerSec(double altitudeKm)
        => SurfaceRateDegPerSec / Math.Pow((OrbitalConstants.EarthRadiusKm + altitudeKm) / OrbitalConstants.EarthRadiusKm, 1.5);

    /// <summary>The pass time dt, s, of eq (2) for one orbit type.</summary>
    public static double PassTimeSec(double altitudeKm, double inclinationDeg, double theta3dBDeg)
    {
        double re = OrbitalConstants.EarthRadiusKm;
        double half = theta3dBDeg / 2.0;
        double phiDeg = half - Math.Asin(Math.Clamp(re / (re + altitudeKm) * Math.Sin(half * Math.PI / 180.0), -1.0, 1.0)) * 180.0 / Math.PI;
        double ws = SatelliteRateDegPerSec(altitudeKm);
        double inc = inclinationDeg * Math.PI / 180.0;
        double omega = Math.Sqrt(Math.Pow(ws * Math.Cos(inc) - OrbitalConstants.EarthRotationRateDeg, 2.0)
                                 + Math.Pow(ws * Math.Sin(inc), 2.0));
        return 2.0 * phiDeg / omega;
    }

    /// <summary>dt_ref rounded to the nearest non-zero millisecond (Sec. D4.2).</summary>
    public static double RoundToMillisecond(double seconds)
        => Math.Max(0.001, Math.Round(seconds, 3, MidpointRounding.AwayFromZero));

    /// <summary>N_coarse of Sec. D4.7.1; at least 1.</summary>
    public static int NCoarseFor(double theta3dBDeg, double nhit = Nhit)
        => Math.Max(1, (int)Math.Floor(nhit * PhiCoarseDeg / theta3dBDeg));

    /// <summary>The altitude eq (2) uses for a shell: the minimum operating height when elliptical (Note 1).</summary>
    public static double StepAltitudeKm(ConstellationShell shell)
        => shell.Eccentricity > 0.0 ? (shell.OperatingHeightKm ?? shell.AltitudeKm) : shell.AltitudeKm;

    /// <summary>
    /// The Sec. D4 plan for epfd(down) over the given shells and a GSO earth
    /// station of 3 dB beamwidth <paramref name="theta3dBDeg"/>. When
    /// <paramref name="runDurationSec"/> is given, the Sec. D4.1 reduction is
    /// applied if the constellation does not repeat (a shell not
    /// station-kept) and the run would exceed 1e8 fine steps.
    /// </summary>
    public static Plan Downlink(IReadOnlyList<ConstellationShell> shells, double theta3dBDeg,
        double runDurationSec = double.NaN)
    {
        if (shells.Count == 0) throw new ArgumentException("no shells");
        if (!(theta3dBDeg > 0.0)) throw new ArgumentException("theta3dB must be positive");

        (double Pass, ConstellationShell Shell) governing = shells
            .Select(s => (Pass: PassTimeSec(StepAltitudeKm(s), s.InclinationDeg, theta3dBDeg), Shell: s))
            .OrderBy(x => x.Pass).First();
        double pass = governing.Pass;
        double fine = RoundToMillisecond(pass / Nhit);
        int nCoarse = NCoarseFor(theta3dBDeg);
        double nhit = Nhit;
        bool reduced = false;

        bool repeating = shells.All(s => s.StationKeeping);
        if (!repeating && double.IsFinite(runDurationSec) && runDurationSec / fine > LongRunSteps)
        {
            int nSat = shells.Sum(s => s.PlaneCount * s.SatsPerPlane);
            double nhitReduced = Nhit / Math.Min(nCoarse, Math.Sqrt(nSat));
            fine = RoundToMillisecond(pass / nhitReduced);
            nCoarse = Math.Max(1, (int)Math.Floor(nhitReduced / Nhit * nCoarse));
            nhit = nhitReduced;
            reduced = true;
        }
        return new Plan(fine, nCoarse, pass, theta3dBDeg, nhit,
            StepAltitudeKm(governing.Shell), governing.Shell.InclinationDeg, reduced);
    }
}
