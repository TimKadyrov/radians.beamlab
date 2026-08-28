using System;
using System.Runtime.CompilerServices;
using Radians.Orbits.Core.Models;
using Radians.Orbits.Core.Utilities;

namespace Radians.Orbits.Core.Propagation
{
    /// <summary>
    /// Propagates satellite orbits using Keplerian elements with J2 perturbations.
    /// Supports three station keeping modes as documented in ITU-R S.1503-4.
    ///
    /// PROPAGATION METHOD:
    /// 1. Compute mean anomaly: M(t) = M₀ + n·t (where n = n₀ or n̄ depending on orbit case)
    /// 2. Solve Kepler's equation: M = E - e·sin(E) for eccentric anomaly E
    /// 3. Convert to true anomaly: ν = 2·atan(√((1+e)/(1-e))·tan(E/2))
    /// 4. Compute radius: r = p/(1+e·cos(E))
    /// 5. Apply J2 perturbations to LAN and argument of perigee
    /// 6. Apply station keeping adjustments based on orbit case
    /// 7. Transform from orbital plane (PQW) to inertial frame (IJK)
    /// </summary>
    public class OrbitPropagator
    {
        private readonly OrbitalElements _elements;
        
        // PRE-COMPUTED ORBITAL PARAMETERS (computed once in constructor)
        
        /// <summary>Inclination in radians. USED: J2 rate calculations, frame rotations</summary>
        private readonly double _inclinationRad;
        
        /// <summary>Cosine of inclination. USED: J2 LAN rate (∝ cos(i)), frame rotations</summary>
        private readonly double _cosI;
        
        /// <summary>Sine of inclination. USED: J2 mean motion correction (∝ sin²(i)), frame rotations</summary>
        private readonly double _sinI;
        
        /// <summary>
        /// Semi-parameter (semi-latus rectum) p = a(1-e²) in kilometers.
        /// CRITICAL PARAMETER used in ALL J2 perturbation calculations:
        /// - LAN rate: Ω̇ ∝ 1/p²
        /// - Argument of perigee rate: ω̇ ∝ 1/p²
        /// - Mean motion correction: n̄ ∝ 1/p²
        /// - Radius computation: r = p/(1+e·cos(E))
        /// </summary>
        private readonly double _semiParameter;
        
        /// <summary>
        /// Unperturbed mean motion n₀ = √(μ/a³) in rad/sec.
        /// USED: In orbit case 3 only (station keeping with supplied precession)
        /// Represents natural Keplerian motion without J2 effects.
        /// </summary>
        private readonly double _meanMotion;
        
        /// <summary>
        /// J2-corrected mean motion n̄ in rad/sec.
        /// FORMULA: n̄ = n₀[1 + 0.001623954(Rₑ/p)²(1 - 1.5sin²(i))√(1-e²)]
        /// USED: In orbit cases 1 and 2 for realistic orbital evolution
        /// Accounts for Earth's oblateness effect on orbital period.
        /// </summary>
        private readonly double _meanMotionBar;
        
        /// <summary>
        /// J2 LAN (Longitude of Ascending Node) secular drift rate Ω̇ in rad/sec.
        /// S.1503-4 terminology: "Longitude of ascending node" not "RAAN"
        /// FORMULA: Ω̇ = -(3/2)J₂(Rₑ/p)²·n̄·cos(i)
        /// PHYSICAL MEANING: Nodal precession due to Earth's equatorial bulge
        /// - Positive cos(i) → westward drift (retrograde precession)
        /// - Negative cos(i) → eastward drift (prograde precession)
        /// - Zero at i=90° (polar orbits have no nodal precession)
        /// USED: Cases 1 and 2; disabled in case 3 (external control)
        /// </summary>
        private readonly double _lanRateRad;
        
        /// <summary>
        /// J2 argument of perigee secular drift rate ω̇ in rad/sec.
        /// FORMULA: ω̇ = 0.001623954(Rₑ/p)²·n̄·(2 - 2.5sin²(i))
        /// PHYSICAL MEANING: Apsidal precession due to Earth's oblateness
        /// - ω̇ > 0 when 5cos²(i) > 1 (low inclinations)
        /// - ω̇ = 0 at critical inclination ~63.4° (Molniya orbits)
        /// - ω̇ < 0 at high inclinations
        /// USED: Cases 1 and 2; disabled in case 3 (external control)
        /// </summary>
        private readonly double _argumentOfPerigeeRateRad;
        
        /// <summary>
        /// Initial mean anomaly M₀ in radians, converted from true anomaly.
        /// USED: Propagating mean anomaly M(t) = M₀ + n·t
        /// </summary>
        private readonly double _meanAnomaly0;
        
        /// <summary>√((1+e)/(1-e)) used in true anomaly calculation. USED: Eccentric to true anomaly conversion</summary>
        private readonly double _sqrtEccentricity;
        
        /// <summary>Initial argument of perigee ω₀ in radians. USED: Frame rotation, ω(t) = ω₀ + ω̇·t</summary>
        private readonly double _omega0Rad;
        
        /// <summary>Initial LAN (Longitude of Ascending Node) Ω₀ in radians. USED: Frame rotation, Ω(t) = Ω₀ + Ω̇·t + station_keeping_terms</summary>
        private readonly double _lan0Rad;
        
        // STATION KEEPING PARAMETERS
        
        /// <summary>
        /// W_delta tolerance in radians (longitudinal tolerance box half-width).
        /// USED:
        /// - Case 2: Ω += W_delta × (2t/T_sim - 1)
        /// - Case 3: Ω += prec_rate·t - W_delta + increment_W_delta·t
        /// </summary>
        private readonly double _wDeltaRad;
        
        /// <summary>
        /// User-supplied precession rate in rad/sec (case 3 only).
        /// USED: Case 3 LAN evolution, overrides natural J2 precession.
        /// </summary>
        private readonly double _precessionRateRad;

        /// <summary>
        /// Artificial precession rate in rad/sec (case 1 only).
        /// Computed externally to ensure ground track distribution.
        /// Stored in OrbitalElements and read at construction time.
        /// </summary>
        private readonly double _artificialPrecessionRad;
        
        // CACHED SIMULATION PARAMETERS
        
        /// <summary>Last simulation duration, cached to avoid recomputing increment_W_delta</summary>
        private double _lastSimulationDuration = double.MinValue;
        
        /// <summary>
        /// Rate of W_delta sweep: increment_W_delta = 2·W_delta/T_sim in rad/sec.
        /// USED: Case 3 to sweep through tolerance box over simulation duration.
        /// </summary>
        private double _incrementWDeltaRad;
        
        // CURRENT STATE
        
        /// <summary>Current orbital radius in km, updated each propagation step</summary>
        private double _currentRadius;

        /// <summary>
        /// Initializes a new instance of the <see cref="OrbitPropagator"/> class.
        /// Implementation follows ITU-R S.1503 standards (both S.1503-2 and S.1503-4 use identical propagation).
        /// </summary>
        /// <param name="elements">The orbital elements including station keeping configuration.</param>
        public OrbitPropagator(OrbitalElements elements)
        {
            _elements = elements ?? throw new ArgumentNullException(nameof(elements));
            
            // Convert angles to radians
            _inclinationRad = AngleUtilities.DegToRad(elements.InclinationDeg);
            _cosI = Math.Cos(_inclinationRad);
            _sinI = Math.Sin(_inclinationRad);
            _omega0Rad = AngleUtilities.DegToRad(elements.ArgumentOfPerigeeDeg);
            _lan0Rad = AngleUtilities.DegToRad(elements.LanDeg);
            
            // Compute semi-parameter (semi-latus rectum)
            _semiParameter = elements.SemiMajorAxisKm * (1.0 - elements.Eccentricity * elements.Eccentricity);
            
            // Compute mean motion (unperturbed)
            _meanMotion = Math.Sqrt(OrbitalConstants.MuEarth / Math.Pow(elements.SemiMajorAxisKm, 3.0));
            
            // Compute perturbed mean motion with J2 effects
            // J₂ = 0.001082636 (Earth's oblateness coefficient from S.1503 Table 2)
            const double J2 = OrbitalConstants.J2;  // Physical constant
            double ratio2 = Math.Pow(OrbitalConstants.EarthRadiusKm / _semiParameter, 2.0);
            
            // Mean motion correction: n̄ = n₀[1 + (3/2)J₂(Rₑ/p)²(1 - 1.5sin²(i))√(1-e²)]
            _meanMotionBar = _meanMotion * (1.0 + 1.5 * J2 * ratio2 *
                            (1.0 - 1.5 * Math.Pow(_sinI, 2.0)) *
                            Math.Pow(1.0 - Math.Pow(elements.Eccentricity, 2.0), 0.5));
            
            // Compute J2 perturbation rates
            // LAN rate: Ω̇ = -(3/2)J₂(Rₑ/p)²·n̄·cos(i)
            // S.1503-4 calls this "longitude of ascending node" not "RAAN"
            _lanRateRad = -1.5 * J2 * ratio2 * _meanMotionBar * _cosI;
            
            // Argument of perigee rate: ω̇ = (3/2)J₂(Rₑ/p)²·n̄·(2 - 2.5sin²(i))
            _argumentOfPerigeeRateRad = 1.5 * J2 * ratio2 * _meanMotionBar *
                                        (2.0 - 2.5 * Math.Pow(_sinI, 2.0));
            
            // Compute initial mean anomaly from true anomaly
            double trueAnomalyRad = AngleUtilities.DegToRad(elements.TrueAnomalyDeg);
            if (elements.Eccentricity == 0.0)
            {
                _meanAnomaly0 = trueAnomalyRad;
            }
            else
            {
                double cosTA = Math.Cos(trueAnomalyRad);
                double eccentricAnomaly = AngleUtilities.SafeAcos((elements.Eccentricity + cosTA) / 
                                                                  (1.0 + elements.Eccentricity * cosTA));
                if (AngleUtilities.NormalizeAnglePi(trueAnomalyRad) < 0.0)
                    eccentricAnomaly = 2.0 * Math.PI - eccentricAnomaly;
                
                _meanAnomaly0 = eccentricAnomaly - elements.Eccentricity * Math.Sin(eccentricAnomaly);
            }
            
            _sqrtEccentricity = Math.Sqrt((1.0 + elements.Eccentricity) / (1.0 - elements.Eccentricity));
            
            // Station keeping parameters
            _wDeltaRad = AngleUtilities.DegToRad(elements.WDeltaDeg);
            _precessionRateRad = AngleUtilities.DegToRad(elements.PrecessionRateDeg);
            _artificialPrecessionRad = elements.ArtificialPrecessionRad;
        }

        /// <summary>
        /// Propagates the orbit to the specified time and returns the position.
        /// This implements the complete orbital propagation including J2 perturbations and station keeping.
        /// </summary>
        /// <param name="timeSeconds">Time in seconds from epoch (t in all formulas).</param>
        /// <param name="simulationDuration">Total simulation duration in seconds.
        /// USED: Case 2/3 W_delta sweep calculations to determine sweep rate.</param>
        /// <param name="frame">The desired coordinate frame (ECI or ECF).
        /// ECI = Earth-Centered Inertial (fixed in space), ECF = Earth-Centered Fixed (rotates with Earth).</param>
        /// <returns>The state vector containing position at the specified time.</returns>
        public StateVector Propagate(
            double timeSeconds,
            double simulationDuration,
            CoordinateFrame frame = CoordinateFrame.ECI)
        {
            // STEP 0: Update cached parameters if simulation changed
            if (_lastSimulationDuration != simulationDuration)
            {
                _lastSimulationDuration = simulationDuration;
                // COMPUTE: increment_W_delta = 2·W_delta/T_sim (rad/sec)
                // USED: Case 3 to linearly sweep through tolerance box
                _incrementWDeltaRad = _wDeltaRad * 2.0 / simulationDuration;
            }
            
            // STEP 1: Compute Mean Anomaly M(t)
            // ORBIT CASE SELECTION:
            // Case 3: Use n₀ (unperturbed) - external precession controls orbit
            // Cases 1,2: Use n̄ (J2-corrected) - realistic orbital evolution
            double meanMotionToUse = (_elements.OrbitCase == 3) ? _meanMotion : _meanMotionBar;
            
            // FORMULA: M(t) = M₀ + n·t
            // This is the fundamental equation of orbital motion - mean anomaly increases linearly with time
            double meanAnomaly = _meanAnomaly0 + meanMotionToUse * timeSeconds;
            
            // STEP 2: Solve Kepler's Equation for Eccentric Anomaly E
            // KEPLER'S EQUATION: M = E - e·sin(E)
            // This transcendental equation must be solved iteratively using Newton-Raphson
            // Relates mean position (M) to geometric position (E)
            // Use 1e-8 precision (standard for both S.1503-2 and S.1503-4)
            double eccentricAnomaly = SolveKeplerEquation(meanAnomaly, _elements.Eccentricity, 1e-8);
            
            // STEP 3: Convert Eccentric Anomaly to True Anomaly
            // FORMULA: ν = 2·atan(√((1+e)/(1-e))·tan(E/2))
            // True anomaly ν is the actual angular position of satellite from perigee
            double trueAnomaly = 2.0 * Math.Atan(_sqrtEccentricity * Math.Tan(eccentricAnomaly / 2.0));

            // STEP 4: Compute Orbital Radius
            double cosNu = Math.Cos(trueAnomaly);
            double sinNu = Math.Sin(trueAnomaly);

            // FORMULA: r = p/(1 + e·cos(ν)) where p = a(1-e²)
            // This gives the instantaneous distance from Earth's center
            // Uses true anomaly ν, NOT eccentric anomaly E
            _currentRadius = _semiParameter / (1.0 + _elements.Eccentricity * cosNu);

            // STEP 5: Compute LAN and Argument of Perigee with Secular J2 Perturbations
            // EARTH ROTATION CORRECTION (ECF frame only):
            // ECF frame rotates with Earth at rate 0.00417807... rad/sec (360°/sidereal day)
            // Subtract Earth rotation to express LAN in Earth-fixed coordinates.
            // NOTE: J2000 offset is NOT included here because the input LAN is already
            // in ECF (geographic longitude from DB)
            double earthRotation = frame == CoordinateFrame.ECF
                ? -timeSeconds * OrbitalConstants.EarthRotationRate
                : 0.0;
            
            // ARGUMENT OF PERIGEE EVOLUTION:
            // Case 3: ω held constant (external control, ω̇ = 0)
            // Cases 1,2: ω(t) = ω₀ + ω̇·t where ω̇ from secular J2 perturbations
            double omega = _omega0Rad + (_elements.OrbitCase == 3 ? 0.0 : _argumentOfPerigeeRateRad * timeSeconds);
            
            // BASE LAN EVOLUTION:
            // Ω_base(t) = Ω₀ + earth_rotation + Ω̇_J2·t
            // S.1503-4 terminology: "Longitude of Ascending Node" (LAN), symbol: Ω
            // Case 3: No J2 term (external control)
            // Cases 1,2: Include J2 nodal precession
            double lan = _lan0Rad + earthRotation +
                        (_elements.OrbitCase == 3 ? 0.0 : _lanRateRad * timeSeconds);
            
            // STEP 6: Apply Station Keeping Adjustments to LAN
            // This is where station keeping configuration affects orbital position
            switch (_elements.OrbitCase)
            {
                case 1: // CASE 1: FREE DRIFT NGSO - ADD ARTIFICIAL PRECESSION
                    // PURPOSE: Ensure ground track distribution for adequate service area coverage
                    // FORMULA: Ω = Ω_base + artificial_precession_rate·t
                    // EFFECT: Modifies LAN evolution to distribute tracks over Earth's surface
                    // COMPUTED: artificial_precession externally based on orbital parameters
                    lan += _artificialPrecessionRad * timeSeconds;
                    break;
                    
                case 2: // CASE 2: STATION KEEPING WITHOUT EXTERNAL PRECESSION
                    // PURPOSE: Model GSO satellite sweeping through W_delta tolerance box
                    // FORMULA: Ω = Ω_base + W_delta·(2t/T_sim - 1)
                    // BEHAVIOR:
                    //   t = 0:         Ω += -W_delta (western edge of box)
                    //   t = T_sim/2:   Ω += 0 (nominal longitude)
                    //   t = T_sim:     Ω += +W_delta (eastern edge of box)
                    // EFFECT: Linear sweep from west to east edge over simulation duration
                    lan += _wDeltaRad * (2.0 * timeSeconds / simulationDuration - 1.0);
                    break;
                    
                case 3: // CASE 3: STATION KEEPING WITH SUPPLIED PRECESSION
                    // PURPOSE: Model GSO with active precession control + tolerance box
                    // FORMULA: Ω = Ω_base + precession_rate·t - W_delta + (2W_delta/T_sim)·t
                    // COMPONENTS:
                    //   precession_rate·t: User-supplied precession evolution
                    //   -W_delta: Start at western edge of tolerance box
                    //   (2W_delta/T_sim)·t: Sweep through box over simulation
                    // EFFECT: Combines external precession with tolerance box sweep
                    lan += _precessionRateRad * timeSeconds - _wDeltaRad + _incrementWDeltaRad * timeSeconds;
                    break;
            }
            
            // STEP 7: Transform from Orbital Plane (PQW) to Inertial Frame (IJK)
            // POSITION IN ORBITAL PLANE COORDINATES (PQW):
            // P-axis: points to perigee
            // Q-axis: in orbital plane, 90° ahead of perigee
            // W-axis: perpendicular to orbital plane (angular momentum direction)
            Vector3D positionPQW = new Vector3D(
                _currentRadius * cosNu,  // P component: r·cos(ν)
                _currentRadius * sinNu,  // Q component: r·sin(ν)
                0.0);                    // W component - always zero (in plane)
            
            // ROTATION SEQUENCE: PQW → IJK
            // Uses three Euler angle rotations defined by orbital elements:
            // 1. Rotate by ω (argument of perigee) about W-axis
            // 2. Rotate by i (inclination) about line of nodes
            // 3. Rotate by Ω (LAN) about K-axis (Earth's rotation axis)
            // This orients the orbital plane in inertial space
            Vector3D position = RotateOrbitalPlaneToInertial(positionPQW, omega, lan, _cosI, _sinI);
            
            // STEP 8: Return State Vector
            return new StateVector(position, timeSeconds, frame, _currentRadius);
        }

        /// <summary>
        /// Gets the current radius in kilometers.
        /// </summary>
        public double GetRadiusKm()
        {
            return _currentRadius;
        }

        /// <summary>Mask ID assigned to this satellite (from OrbitalElements).</summary>
        public int MaskId => _elements.MaskId;

        /// <summary>1-based satellite number (from OrbitalElements).</summary>
        public int SatelliteNumber => _elements.SatelliteNumber;

        /// <summary>Operating height in km for this satellite's orbital plane.</summary>
        public double OperatingHeightKm => _elements.OperatingHeightKm;

        /// <summary>Orbital eccentricity (0 for circular).</summary>
        public double Eccentricity => _elements.Eccentricity;

        /// <summary>
        /// Gets the orbital constants (mean motion bar, LAN rate, argument of perigee rate).
        /// These are the secular J2 perturbation rates used by both S.1503-2 and S.1503-4.
        /// S.1503-4 terminology: LAN (Longitude of Ascending Node) not RAAN.
        /// </summary>
        public void GetOrbitalConstants(out double meanMotionBar, out double lanRate, out double argPerigeeRate)
        {
            meanMotionBar = _meanMotionBar;
            lanRate = _lanRateRad;
            argPerigeeRate = _argumentOfPerigeeRateRad;
        }

        /// <summary>
        /// Solves Kepler's equation using Newton-Raphson iteration.
        /// M = E - e*sin(E)
        /// </summary>
        /// <param name="meanAnomaly">Mean anomaly in radians.</param>
        /// <param name="eccentricity">Orbital eccentricity.</param>
        /// <param name="epsilon">Convergence tolerance.</param>
        /// <returns>Eccentric anomaly in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double SolveKeplerEquation(double meanAnomaly, double eccentricity, double epsilon)
        {
            if (eccentricity == 0.0)
                return meanAnomaly;
            
            double E = meanAnomaly;
            double Enew;
            
            do
            {
                Enew = E - (E - eccentricity * Math.Sin(E) - meanAnomaly) / 
                       (1.0 - eccentricity * Math.Cos(E));
                
                if (Math.Abs(Enew - E) <= epsilon)
                    break;
                
                E = Enew;
            } while (true);
            
            return Enew;
        }

        /// <summary>
        /// Rotates a vector from the orbital plane (PQW) to the inertial frame (IJK).
        /// </summary>
        /// <param name="pqw">Position in orbital plane coordinates.</param>
        /// <param name="omega">Argument of perigee in radians.</param>
        /// <param name="lan">Longitude of Ascending Node in radians (S.1503-4 terminology).</param>
        /// <param name="cosI">Cosine of inclination.</param>
        /// <param name="sinI">Sine of inclination.</param>
        /// <returns>Position in inertial frame.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector3D RotateOrbitalPlaneToInertial(
            Vector3D pqw,
            double omega,
            double lan,
            double cosI,
            double sinI)
        {
            double cosLan = Math.Cos(lan);
            double sinLan = Math.Sin(lan);
            double cosOmega = Math.Cos(omega);
            double sinOmega = Math.Sin(omega);
            
            // Rotation matrix elements from PQW to IJK
            double r11 = cosLan * cosOmega - sinLan * sinOmega * cosI;
            double r12 = -cosLan * sinOmega - sinLan * cosOmega * cosI;
            double r13 = sinLan * sinI;
            
            double r21 = sinLan * cosOmega + cosLan * sinOmega * cosI;
            double r22 = -sinLan * sinOmega + cosLan * cosOmega * cosI;
            double r23 = -cosLan * sinI;
            
            double r31 = sinOmega * sinI;
            double r32 = cosOmega * sinI;
            double r33 = cosI;
            
            return new Vector3D(
                r11 * pqw.X + r12 * pqw.Y + r13 * pqw.Z,
                r21 * pqw.X + r22 * pqw.Y + r23 * pqw.Z,
                r31 * pqw.X + r32 * pqw.Y + r33 * pqw.Z);
        }
    }
}