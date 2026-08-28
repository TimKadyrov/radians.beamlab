using System;
using System.Collections.Generic;
using Radians.Orbits.Core.Propagation;
using Radians.Orbits.Core.Utilities;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>
/// Case-1 (free drift) artificial precession per Rec. ITU-R S.1503-4
/// Sec. D6.3.2 -- transcribed from the reference implementation
/// (radians Compute.ComputeArtificialPrecession + computeNonRepeatingOrbitData,
/// radcompute1503-2/Compute.cs; the S.1503-4 nodal-period branch). The rate
/// nudges the per-orbit westward equatorial pass shift onto an exact
/// 360/nOrbits grid so the ground track repeats after nOrbits nodal orbits.
/// </summary>
public static class ArtificialPrecession
{
    /// <summary>
    /// Westward equatorial pass shift per nodal orbit (deg) and the nodal
    /// period (s), from the same J2 secular rates the propagator uses.
    /// </summary>
    public static (double spassDeg, double nodalPeriodSec) NodalPassGeometry(
        double semiMajorAxisKm, double eccentricity, double inclinationDeg)
    {
        double n0 = Math.Sqrt(OrbitalConstants.MuEarth / Math.Pow(semiMajorAxisKm, 3.0));
        double p = semiMajorAxisKm * (1.0 - eccentricity * eccentricity);
        double sinI = Math.Sin(AngleUtilities.DegToRad(inclinationDeg));
        double cosI = Math.Cos(AngleUtilities.DegToRad(inclinationDeg));
        double ratio2 = Math.Pow(OrbitalConstants.EarthRadiusKm, 2.0) / Math.Pow(p, 2.0);

        double nBar = n0 * (1.0 + 1.5 * OrbitalConstants.EarthOblatenessJ2 * ratio2
                            * (1.0 - 1.5 * Math.Pow(sinI, 2.0))
                            * Math.Pow(1.0 - Math.Pow(eccentricity, 2.0), 0.5));
        double lanRate = -1.5 * OrbitalConstants.EarthOblatenessJ2 * ratio2 * nBar * cosI;
        double argPerigeeRate = 1.5 * OrbitalConstants.EarthOblatenessJ2 * ratio2 * nBar
                                * (2.0 - 2.5 * Math.Pow(sinI, 2.0));

        double nBarPerMin = AngleUtilities.RadToDeg(nBar) * 60.0;
        double lanRatePerMin = AngleUtilities.RadToDeg(lanRate) * 60.0;
        double argPerigeeRatePerMin = AngleUtilities.RadToDeg(argPerigeeRate) * 60.0;
        double earthRotPerMin = AngleUtilities.RadToDeg(OrbitalConstants.EarthRotationRate) * 60.0;

        // S.1503-4 Sec. D6.3.2 eq (25): nodal period 360/(omega_r + n_bar).
        double nodalPeriodMin = 360.0 / (argPerigeeRatePerMin + nBarPerMin);
        double spassDeg = (earthRotPerMin - lanRatePerMin) * nodalPeriodMin;
        return (spassDeg, nodalPeriodMin * 60.0);
    }

    /// <summary>
    /// Artificial precession rate (rad/s) for a free-drift orbit so that
    /// nOrbits nodal orbits cover the equator uniformly and then repeat:
    /// rate = ((360 * floor(nOrbits * spass / 360) / nOrbits) - spass) / T_nodal.
    /// nOrbits comes from the examination geometry (S.1503-4: number of
    /// equatorial passes needed to resolve the GSO earth-station beam,
    /// ceil(180 / (2 phi / Ntracks))); it is a declared input here.
    /// </summary>
    public static double RadPerSec(double semiMajorAxisKm, double eccentricity,
                                   double inclinationDeg, int nOrbits)
    {
        if (nOrbits <= 0) return 0.0;
        var (spass, tPeriodSec) = NodalPassGeometry(semiMajorAxisKm, eccentricity, inclinationDeg);
        double adjusted = 360.0 * Math.Floor(nOrbits * spass / 360.0) / nOrbits - spass;
        return AngleUtilities.DegToRad(adjusted / tPeriodSec);
    }
}

/// <summary>
/// One Walker-style shell of a constellation (simulation spec Sec. 4.2).
/// Circular free drift is the default; station keeping (orbit case 2, with
/// an optional declared repeat period), a supplied precession rate (case 3)
/// and elliptical geometry are declared per shell -- the EPS places the
/// orbit-model flags per plane (Sec. 6.4.1.1), so shells with different
/// models may share one notice (dataset brief Sec. 5.3; note the S.1503-4
/// Sec. B5.1 all-repeating-or-all-not text this deliberately exercises).
/// </summary>
public sealed record ConstellationShell
{
    /// <summary>Mean orbit altitude above the S.1503 Earth radius (km); a = Re + this.</summary>
    public required double AltitudeKm { get; init; }
    public required double InclinationDeg { get; init; }
    public required int PlaneCount { get; init; }
    public required int SatsPerPlane { get; init; }

    /// <summary>Walker phasing parameter F: inter-plane phase offset = F * 360 / (P * S) deg.</summary>
    public int WalkerPhasingF { get; init; }
    /// <summary>LAN of plane 0 (deg).</summary>
    public double Lan0Deg { get; init; }
    /// <summary>LAN span the planes divide (deg): 360 = Walker delta, 180 = Walker star.</summary>
    public double LanSpreadDeg { get; init; } = 360.0;
    /// <summary>Extra in-plane phase offset applied to every satellite (deg).</summary>
    public double InPlaneOffsetDeg { get; init; }

    /// <summary>
    /// Case-1 artificial-precession track count (S.1503-4 Sec. D6.3.2);
    /// 0 disables artificial precession. Ignored for station-kept shells.
    /// </summary>
    public int NOrbits { get; init; }

    /// <summary>Orbit eccentricity; 0 = circular.</summary>
    public double Eccentricity { get; init; }
    /// <summary>Argument of perigee (deg); meaningful when eccentric.</summary>
    public double ArgumentOfPerigeeDeg { get; init; }
    /// <summary>
    /// Minimum operating height (km, EPS op_ht_km / H_MIN): satellites below
    /// it do not transmit. Defaults to the perigee altitude.
    /// </summary>
    public double? OperatingHeightKm { get; init; }

    /// <summary>Station keeping (orbit case 2; case 3 when a precession rate is supplied).</summary>
    public bool StationKeeping { get; init; }
    /// <summary>Longitudinal tolerance half-width W_delta (deg, keep_rnge).</summary>
    public double WDeltaDeg { get; init; }
    /// <summary>Case 3: an administration-supplied precession rate is declared.</summary>
    public bool PrecessionSupplied { get; init; }
    /// <summary>Case 3 precession rate (deg/s).</summary>
    public double PrecessionRateDegPerSec { get; init; }
    /// <summary>Declared repeating ground-track period (station-kept shells), for the SRS rpt_prd fields.</summary>
    public (int Days, int Hours, int Minutes, int Seconds)? RepeatPeriod { get; init; }
}

/// <summary>
/// Position and identity of one satellite at one instant (ECF).
/// <paramref name="HeadingDeg"/> is the inertial ground-track heading (deg
/// from north toward east) -- the direction a body-stabilised layout flies
/// (finite-differenced from the propagator's ECI velocity, the same
/// convention <see cref="GroundTrack.HeadingsAtLatitude"/> describes).
/// </summary>
public sealed record SatelliteState(
    int SatelliteNumber, int ShellIndex, int PlaneIndex, int IndexInPlane,
    Vec3 PositionEcefKm, double SubSatLatDeg, double SubSatLonDeg,
    double AltitudeKm, double RadiusKm, double HeadingDeg, double TimeSeconds);

/// <summary>A satellite's beam set with per-beam powers, resolved at one instant.</summary>
public sealed record ResolvedBeamSet(IReadOnlyList<Beam> Beams, IReadOnlyList<double> PowersDbw);

/// <summary>
/// Yields the beam set for a satellite state (simulation spec Sec. 4.1:
/// beam = pattern, boresight(t), power(t), gate(t); Beam stays immutable and
/// BeamComposer consumes whatever set this produces). The fixed
/// body-stabilised layout is the constant case.
/// </summary>
public interface IBeamPointing
{
    ResolvedBeamSet Resolve(SatelliteState state);
}

/// <summary>One satellite in a snapshot: state plus (optionally) resolved beams.</summary>
public sealed record SatelliteSnapshot(SatelliteState State, ResolvedBeamSet? Beams);

/// <summary>The system at time t (simulation spec WP1 SystemState).</summary>
public sealed class SystemSnapshot
{
    public required double TimeSeconds { get; init; }
    public required IReadOnlyList<SatelliteSnapshot> Satellites { get; init; }
}

/// <summary>
/// A constellation of Walker shells propagated with the vendored S.1503-4
/// propagator (orbits/ -- byte-identical to the examination's). Positions
/// come out in the propagator's ECF frame, which is beamlab's ECEF: the
/// sub-satellite direction is taken from the position vector (radius-free)
/// and the altitude is referenced to beamlab's spherical Earth so scene
/// geometry stays internally consistent (see orbits/README.md).
/// </summary>
public sealed class Constellation
{
    private readonly List<OrbitPropagator> _propagators = new();
    private readonly List<OrbitalElements> _elements = new();
    private readonly List<(int shell, int plane, int slot)> _identity = new();

    public Constellation(IReadOnlyList<ConstellationShell> shells)
    {
        int satNumber = 1;
        for (int sh = 0; sh < shells.Count; sh++)
        {
            var shell = shells[sh];
            double a = OrbitalConstants.EarthRadiusKm + shell.AltitudeKm;
            double perigAltKm = a * (1.0 - shell.Eccentricity) - OrbitalConstants.EarthRadiusKm;
            double artPrec = !shell.StationKeeping && shell.NOrbits > 0
                ? ArtificialPrecession.RadPerSec(a, shell.Eccentricity, shell.InclinationDeg, shell.NOrbits)
                : 0.0;
            for (int p = 0; p < shell.PlaneCount; p++)
            {
                double lan = shell.Lan0Deg + shell.LanSpreadDeg * p / shell.PlaneCount;
                for (int s = 0; s < shell.SatsPerPlane; s++)
                {
                    // Walker spacing is declared in PHASE (angle from the
                    // ascending node, the SRS phase_ang); the examination
                    // recovers the true anomaly as phase - omega, so the same
                    // transform is applied here (declared == simulated).
                    double phase = 360.0 * s / shell.SatsPerPlane
                                 + 360.0 * shell.WalkerPhasingF * p / (shell.PlaneCount * shell.SatsPerPlane)
                                 + shell.InPlaneOffsetDeg;
                    var el = new OrbitalElements(
                        semiMajorAxisKm: a,
                        eccentricity: shell.Eccentricity,
                        inclinationDeg: shell.InclinationDeg,
                        lanDeg: lan,
                        argumentOfPerigeeDeg: shell.ArgumentOfPerigeeDeg,
                        trueAnomalyDeg: Norm360(phase - shell.ArgumentOfPerigeeDeg),
                        stationKeeping: shell.StationKeeping,
                        wDeltaDeg: shell.WDeltaDeg,
                        precessionMechanismSupplied: shell.PrecessionSupplied,
                        precessionRateDeg: shell.PrecessionRateDegPerSec)
                    {
                        ArtificialPrecessionRad = artPrec,
                        SatelliteNumber = satNumber,
                        OrbitId = sh + 1,
                        SatelliteOrbitId = s + 1,
                        OperatingHeightKm = shell.OperatingHeightKm ?? perigAltKm,
                    };
                    _elements.Add(el);
                    _propagators.Add(new OrbitPropagator(el));
                    _identity.Add((sh, p, s));
                    satNumber++;
                }
            }
        }
    }

    private static double Norm360(double v)
    {
        double r = v % 360.0;
        return r < 0 ? r + 360.0 : r;
    }

    public int SatelliteCount => _propagators.Count;

    /// <summary>Per-satellite elements, in satellite-number order (for tests / SRS authoring).</summary>
    public IReadOnlyList<OrbitalElements> Elements => _elements;

    /// <summary>Finite-difference step for the inertial heading (s).</summary>
    private const double HeadingDtSec = 0.1;

    /// <summary>State of one satellite at time t, in the given frame's coordinates.</summary>
    public SatelliteState StateAt(int index, double timeSeconds, double simulationDurationSeconds,
                                  CoordinateFrame frame = CoordinateFrame.ECF)
    {
        // Heading from the ECI velocity (finite difference), evaluated in the
        // local NED of the inertial position -- frame-internal and identical
        // to the convention test L1 validates against GroundTrack.
        var e0 = _propagators[index].Propagate(timeSeconds, simulationDurationSeconds, CoordinateFrame.ECI);
        var e1 = _propagators[index].Propagate(timeSeconds + HeadingDtSec, simulationDurationSeconds, CoordinateFrame.ECI);
        var p0 = new Vec3(e0.Position.X, e0.Position.Y, e0.Position.Z);
        var p1 = new Vec3(e1.Position.X, e1.Position.Y, e1.Position.Z);
        var v = (p1 - p0) * (1.0 / HeadingDtSec);
        double lat0 = Math.Asin(Math.Clamp(p0.Z / p0.Length, -1.0, 1.0)) * 180.0 / Math.PI;
        double lon0 = Math.Atan2(p0.Y, p0.X) * 180.0 / Math.PI;
        var (nB, eB, dB) = SatNedBasis(lat0, lon0);
        double headingDeg = Math.Atan2(Vec3.Dot(v, eB), Vec3.Dot(v, nB)) * 180.0 / Math.PI;

        var sv = _propagators[index].Propagate(timeSeconds, simulationDurationSeconds, frame);
        var pos = new Vec3(sv.Position.X, sv.Position.Y, sv.Position.Z);
        double r = pos.Length;
        double latDeg = Math.Asin(Math.Clamp(pos.Z / r, -1.0, 1.0)) * 180.0 / Math.PI;
        double lonDeg = Math.Atan2(pos.Y, pos.X) * 180.0 / Math.PI;
        var (sh, p, s) = _identity[index];
        return new SatelliteState(
            _elements[index].SatelliteNumber, sh, p, s,
            pos, latDeg, lonDeg,
            AltitudeKm: r - EarthRadiusKm,
            RadiusKm: r,
            HeadingDeg: headingDeg,
            TimeSeconds: timeSeconds);
    }

    /// <summary>
    /// The system at time t: every satellite's ECF state, with beams resolved
    /// through <paramref name="pointing"/> when one is supplied.
    /// </summary>
    public SystemSnapshot SnapshotAt(double timeSeconds, double simulationDurationSeconds,
                                     IBeamPointing? pointing = null)
    {
        var sats = new List<SatelliteSnapshot>(_propagators.Count);
        for (int i = 0; i < _propagators.Count; i++)
        {
            var state = StateAt(i, timeSeconds, simulationDurationSeconds);
            sats.Add(new SatelliteSnapshot(state, pointing?.Resolve(state)));
        }
        return new SystemSnapshot { TimeSeconds = timeSeconds, Satellites = sats };
    }
}
