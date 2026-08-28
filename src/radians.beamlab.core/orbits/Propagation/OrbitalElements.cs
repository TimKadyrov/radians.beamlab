using System;

namespace Radians.Orbits.Core.Propagation
{
    /// <summary>
    /// Represents a set of Keplerian orbital elements and station keeping configuration
    /// that fully define a satellite's orbit and propagation behavior.
    /// </summary>

    public class OrbitalElements
    {

        /// <summary>
        /// Gets or sets the semi-major axis in kilometers.
        /// PHYSICAL MEANING: Defines the orbit size and directly determines orbital period.
        /// USAGE IN CALCULATIONS:
        /// - Orbital period: T = 2π√(a³/μ) where μ is Earth's gravitational parameter
        /// - Mean motion: n = √(μ/a³)
        /// - Altitude calculations: altitude = a - Earth_radius (for circular orbits)
        /// - J2 perturbation magnitudes (inversely proportional to a²)
        /// TYPICAL VALUES: LEO ~7000 km, MEO ~26600 km, GEO ~42164 km
        /// </summary>
        public double SemiMajorAxisKm { get; set; }

        /// <summary>
        /// Gets or sets the orbital eccentricity (dimensionless, 0 for circular orbit).
        /// PHYSICAL MEANING: Defines orbit shape from circular (e=0) to parabolic (e=1).
        /// USAGE IN CALCULATIONS:
        /// - Semi-parameter: p = a(1-e²) - used in all J2 perturbation calculations
        /// - Radius computation: r = p/(1+e·cos(ν)) where ν is true anomaly
        /// - Converts true anomaly to eccentric anomaly via Kepler's equation
        /// - Affects magnitude of J2 perturbations
        /// TYPICAL VALUES: Near-circular orbits e < 0.01, elliptical e = 0.1-0.7
        /// </summary>
        public double Eccentricity { get; set; }

        /// <summary>
        /// Gets or sets the orbital inclination in degrees.
        /// PHYSICAL MEANING: Tilt of orbital plane relative to Earth's equator (0° = equatorial, 90° = polar).
        /// USAGE IN CALCULATIONS:
        /// - J2 LAN rate: Ω̇ ∝ cos(i) - determines nodal precession direction and magnitude
        /// - J2 argument of perigee rate: ω̇ ∝ (5cos²(i) - 1) - controls apsidal precession
        /// - Mean motion correction: n̄ ∝ (1 - 1.5sin²(i)) - affects orbital period
        /// - Ground track latitude range: ±i
        /// - Timestep calculations: affects satellite velocity relative to ground stations
        /// TYPICAL VALUES: Sun-sync ~98°, ISS ~51.6°, Molniya ~63.4°, GEO ~0°
        /// </summary>
        public double InclinationDeg { get; set; }

        /// <summary>
        /// Gets or sets the Longitude of Ascending Node (LAN) in degrees.
        /// S.1503-4 terminology: "Longitude of ascending node" (symbol: Ω)
        /// PHYSICAL MEANING: Defines where orbital plane crosses equator going northward, measured eastward from vernal equinox.
        /// USAGE IN CALCULATIONS:
        /// - Orbital plane orientation in inertial space
        /// - J2 nodal precession: Ω(t) = Ω₀ + Ω̇·t where Ω̇ = -(3/2)n̄J₂(Rₑ/p)²cos(i)
        /// - Station keeping: Modified by artificial precession or W_delta sweep
        /// - Coordinate transformations from orbital plane to ECI frame
        /// - Ground track distribution: LAN determines longitude of ascending node
        /// STATION KEEPING IMPACT:
        ///   Case 1 (no SK): Ω += artificial_precession_rate × t
        ///   Case 2 (SK no prec): Ω += W_delta × (2t/T_sim - 1)  [sweeps -W_delta to +W_delta]
        ///   Case 3 (SK with prec): Ω += prec_rate×t - W_delta + (2W_delta/T_sim)×t
        /// </summary>
        public double LanDeg { get; set; }

        /// <summary>
        /// Gets or sets the argument of perigee in degrees.
        /// PHYSICAL MEANING: Angle from ascending node to perigee, defines orbit orientation within orbital plane.
        /// USAGE IN CALCULATIONS:
        /// - J2 apsidal precession: ω(t) = ω₀ + ω̇·t where ω̇ = 1.5n̄J₂(Rₑ/p)²(2 - 2.5sin²(i))
        /// - Rotation from orbital plane (PQW) to inertial frame (IJK)
        /// - Combined with LAN to fully orient orbit in space
        /// - For circular orbits (e≈0), argument of perigee is undefined/arbitrary
        /// TYPICAL VALUES: Can be any angle 0-360°; for sun-sync orbits near 90° or 270°
        /// NOTE: In orbit case 3 (station keeping with precession), ω̇ is set to zero (external control)
        /// </summary>
        public double ArgumentOfPerigeeDeg { get; set; }

        /// <summary>
        /// Gets or sets the true anomaly in degrees.
        /// PHYSICAL MEANING: Satellite's position along orbit, measured from perigee.
        /// USAGE IN CALCULATIONS:
        /// - Converted to mean anomaly M₀ via eccentric anomaly for initial epoch
        /// - Mean anomaly propagated: M(t) = M₀ + n·t (where n is mean motion)
        /// - Kepler's equation solved: M = E - e·sin(E) to get eccentric anomaly E
        /// - Then converted back to true anomaly: ν = 2·atan(√((1+e)/(1-e))·tan(E/2))
        /// - Determines initial satellite position in orbit
        /// TYPICAL VALUES: 0° at perigee, 180° at apogee, arbitrary for circular orbits
        /// </summary>
        public double TrueAnomalyDeg { get; set; }

        // Station Keeping Configuration
        /// <summary>
        /// Gets or sets whether station keeping is enabled.
        /// FALSE → Case 1 (free drift, requires artificial precession)
        /// TRUE  → Case 2 or 3 depending on PrecessionMechanismSupplied
        /// </summary>
        public bool StationKeeping { get; set; }

        /// <summary>
        /// Gets or sets the station keeping tolerance (W_delta) in degrees.
        /// Longitudinal tolerance box half-width.
        /// </summary>
        public double WDeltaDeg { get; set; }

        /// <summary>
        /// Gets or sets whether an external precession mechanism is supplied.
        /// TRUE → Case 3 (uses unperturbed mean motion, disables J2 ω̇)
        /// </summary>
        public bool PrecessionMechanismSupplied { get; set; }

        /// <summary>
        /// Gets or sets the precession rate in degrees per second.
        /// Only used when PrecessionMechanismSupplied is true (orbit case 3).
        /// </summary>
        public double PrecessionRateDeg { get; set; }

        /// <summary>
        /// Gets or sets the artificial precession rate in radians per second.
        /// Only used for Case 1 (free drift) orbits. Computed externally to ensure
        /// ground track distribution. Set in ConvertToOrbitalElements.
        /// </summary>
        public double ArtificialPrecessionRad { get; set; }

        /// <summary>
        /// 1-based satellite number 
        /// (sequential counter across all orbits and phases).
        /// </summary>
        public int SatelliteNumber { get; set; }

        /// <summary>
        /// The orbit ID (orb_id) this satellite belongs to.
        /// Used for mask-to-satellite resolution.
        /// </summary>
        public int OrbitId { get; set; }

        /// <summary>
        /// The satellite orbit ID (orb_sat_id) within its orbit.
        /// Used for per-satellite mask resolution.
        /// </summary>
        public int SatelliteOrbitId { get; set; }

        /// <summary>
        /// The mask ID assigned to this satellite via multi-mask resolution.
        /// Set by PopulateOrbitsMultiMask; propagated through OrbitPropagator.
        /// </summary>
        public int MaskId { get; set; }

        /// <summary>
        /// Operating height in km for this satellite's orbital plane.
        /// For circular orbits: SemiMajorAxisKm - EarthRadiusKm.
        /// For elliptical orbits: from SRS op_ht field (or perigee as fallback).
        /// Used in time-series to skip satellites below operating altitude.
        /// </summary>
        public double OperatingHeightKm { get; set; }

        /// <summary>
        /// Gets the orbit case based on station keeping configuration.
        /// Case 1: No station keeping (free drift)
        /// Case 2: Station keeping without external precession
        /// Case 3: Station keeping with supplied precession
        /// </summary>
        public int OrbitCase
        {
            get
            {
                if (!StationKeeping)
                    return 1;
                else if (!PrecessionMechanismSupplied)
                    return 2;
                else
                    return 3;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OrbitalElements"/> class.
        /// </summary>
        public OrbitalElements()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OrbitalElements"/> class with specified values.
        /// </summary>
        public OrbitalElements(
            double semiMajorAxisKm,
            double eccentricity,
            double inclinationDeg,
            double lanDeg,
            double argumentOfPerigeeDeg,
            double trueAnomalyDeg,
            bool stationKeeping = false,
            double wDeltaDeg = 0.0,
            bool precessionMechanismSupplied = false,
            double precessionRateDeg = 0.0)
        {
            SemiMajorAxisKm = semiMajorAxisKm;
            Eccentricity = eccentricity;
            InclinationDeg = inclinationDeg;
            LanDeg = lanDeg;
            ArgumentOfPerigeeDeg = argumentOfPerigeeDeg;
            TrueAnomalyDeg = trueAnomalyDeg;
            StationKeeping = stationKeeping;
            WDeltaDeg = wDeltaDeg;
            PrecessionMechanismSupplied = precessionMechanismSupplied;
            PrecessionRateDeg = precessionRateDeg;
        }

        /// <summary>
        /// Creates a copy of this orbital elements instance.
        /// </summary>
        /// <returns>A new instance with the same values.</returns>
        public OrbitalElements Clone()
        {
            return new OrbitalElements(
                SemiMajorAxisKm,
                Eccentricity,
                InclinationDeg,
                LanDeg,
                ArgumentOfPerigeeDeg,
                TrueAnomalyDeg,
                StationKeeping,
                WDeltaDeg,
                PrecessionMechanismSupplied,
                PrecessionRateDeg)
            {
                ArtificialPrecessionRad = ArtificialPrecessionRad,
                SatelliteNumber = SatelliteNumber,
                OrbitId = OrbitId,
                SatelliteOrbitId = SatelliteOrbitId,
                MaskId = MaskId,
                OperatingHeightKm = OperatingHeightKm,
            };
        }

        /// <summary>
        /// Returns a string representation of the orbital elements.
        /// </summary>
        public override string ToString()
        {
            var s = $"SMA={SemiMajorAxisKm:F3}km, e={Eccentricity:F6}, i={InclinationDeg:F3}°, " +
                   $"LAN={LanDeg:F3}°, AoP={ArgumentOfPerigeeDeg:F3}°, TA={TrueAnomalyDeg:F3}°, " +
                   $"Case {OrbitCase}: SK={StationKeeping}, WDelta={WDeltaDeg:F3}°";
            if (ArtificialPrecessionRad != 0.0)
                s += $", ArtPrec={ArtificialPrecessionRad:E4} rad/s";
            return s;
        }
    }
}
