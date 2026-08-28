
using Radians.Orbits.Core.Utilities;
using System;

namespace radantenna
{
    /// <summary>
    /// ITU Antenna Pattern Library (APL) pattern identifiers.
    /// Naming: AP = Antenna Pattern, E = Earth station, S = Space station,
    /// </summary>
    public enum ApType
    {
        /// <summary>Rec. ITU-R S.1428 - GSO ES receive antenna for epfd(down) FSS, D/λ ≥ 20</summary>
        APERR_019V01,
        /// <summary>Rec. ITU-R S.1428 - GSO ES receive antenna for epfd(down) FSS (improved, D/λ > 100)</summary>
        APERR_020V01,
        /// <summary>Rec. ITU-R BO.1443 - GSO ES receive antenna for epfd(down) BSS, with azimuthal dependency</summary>
        APEREC021V01,
        /// <summary>Rec. ITU-R S.672-4 - GSO satellite antenna for epfd(up) and epfd(is)</summary>
        APSREC407V01,
        /// <summary>Rec. ITU-R S.672-4 - GSO satellite antenna for epfd(up) and epfd(is), wider beam variant</summary>
        APSREC408V01
    };


    /// <summary>
    /// Precomputed ES antenna constants for GPU and static gain computation.
    /// </summary>
    public struct EsAntennaParams
    {
        public int PatternType; // 0=APERR019, 1=APERR020, 2=APEREC021
        public double GMax, G1, PhiM, PhiR, PhiB, DOverL;
        public int IsBss; // 1 for APEREC021V01, 0 otherwise
    }

    // Antenna Library have two implementation - internal one and APL external DLL
    // Exact implementation to be used is selected when initialising the class
    public class AntennaLibrary
    {
        public AntennaLibraryImplementationInterface APLib;

        public AntennaLibrary(ApType apType, double freqMHz, double? antDiamM)
        {
            switch (apType)
            {
                case ApType.APERR_019V01:
                    if (antDiamM == null)
                        throw new ArgumentNullException("Antenna diameter is required for APERR_019V01");
                    APLib = new APERR_019V01(freqMHz, antDiamM.Value);
                    break;
                case ApType.APERR_020V01:
                    if (antDiamM == null)
                        throw new ArgumentNullException("Antenna diameter is required for APERR_020V01");
                    APLib = new APERR_020V01(freqMHz, antDiamM.Value);
                    break;
                case ApType.APEREC021V01:
                    if (antDiamM == null)
                        throw new ArgumentNullException("Antenna diameter is required for APEREC021V01");
                    APLib = new APEREC021V01(freqMHz, antDiamM.Value);
                    break;
                case ApType.APSREC407V01:
                    APLib = new APSREC407V01(freqMHz);
                    break;
                case ApType.APSREC408V01:
                    APLib = new APSREC408V01(freqMHz);
                    break;
            }
        }

        public double MaxGain => APLib.GetMaxGain;

        public double GetAntGain(double angle, double theta, double gainMax, double phi3Db, double Ln)
        {
            return APLib.GetAntGain(angle, theta, gainMax, phi3Db, Ln);
        }

        public double GetAntGain(double angle, double theta)
        {
            return APLib.GetAntGain(angle, theta);
        }

        /// <summary>
        /// Returns precomputed antenna constants for ES receive patterns (019/020/021).
        /// Used by GPU host to avoid duplicating precomputation logic.
        /// </summary>
        public EsAntennaParams GetEsParams()
        {
            if (APLib is APERR_019V01 a019)
                return new EsAntennaParams { PatternType = 0, GMax = a019.gmax, G1 = a019.g1, PhiM = a019.phim, PhiR = a019.phir, PhiB = a019.phib, DOverL = a019.dOverl, IsBss = 0 };
            if (APLib is APERR_020V01 a020)
                return new EsAntennaParams { PatternType = 1, GMax = a020.gmax, G1 = a020.g1, PhiM = a020.phim, PhiR = a020.phir, PhiB = a020.phib, DOverL = a020.dOverl, IsBss = 0 };
            if (APLib is APEREC021V01 a021)
                return new EsAntennaParams { PatternType = 2, GMax = a021.gmax, G1 = a021.g1, PhiM = a021.phim, PhiR = a021.phir, PhiB = a021.phib, DOverL = a021.dOverl, IsBss = 1 };
            return default;
        }

        // BO.1443 §2.1.2 azimuthal sector table for the BSS earth-station
        // pattern (APEREC021V01). Each row covers azimuths in
        // [previousRow.thetaMax, thisRow.thetaMax) and yields:
        //   m1 = (m1Const + m1SinCoef · sin(θ_deg)) · m1InvLog
        //   m2 = (m2Const + m2SinCoef · sin(θ_deg)) · m2InvLog
        // The 180°–360° back-half row uses zero sin coefficients because the
        // pattern is mirror-symmetric about the boresight plane. Inverse-log
        // factors are precomputed once at class-init time so the per-call cost
        // is a small loop with multiply-adds (no divisions, no log evaluations).
        // Inlined as compile-time constants rather than computed at class-init:
        // ILGPU cannot lower a static field read inside a kernel, and this method
        // is reached from the epfd(down) kernel. ApLibBssTests asserts each literal
        // equals 1.0 / Math.Log10(x) at runtime.
        private const double BssInvLog18 = 3.9173823267621817;  // 1 / log10(1.8)
        private const double BssInvLog20 = 3.321928094887362;   // 1 / log10(2.0)
        private const double BssInvLog24 = 2.630116867397913;   // 1 / log10(2.4)
        private const double BssInvLog15 = 5.678873587267573;   // 1 / log10(1.5)

        private static (double m1, double m2, double phit) LookupBssSector(double theta)
        {
            double normTheta = ((theta % 360.0) + 360.0) % 360.0;
            double sinTh = normTheta < 180.0
                ? Math.Sin(normTheta * (Math.PI / 180.0))
                : 0.0;
            // Sector rows in ascending thetaMax, as branches rather than a table:
            // a static managed array cannot be compiled into an ILGPU kernel. The
            // arithmetic is the table rows verbatim so results are unchanged.
            if (normTheta < 56.25)
                return ((2.0 + 8.0 * sinTh) * BssInvLog24,
                        (-9.0 + -8.0 * sinTh) * BssInvLog15, 120.0);
            if (normTheta < 123.75)
                return ((2.0 + 8.0 * sinTh) * BssInvLog18,
                        (-9.0 + -8.0 * sinTh) * BssInvLog20, 90.0);
            if (normTheta < 180.00)
                return ((2.0 + 8.0 * sinTh) * BssInvLog24,
                        (-9.0 + -8.0 * sinTh) * BssInvLog15, 120.0);
            return ((2.0 + 0.0 * sinTh) * BssInvLog24,
                    (-9.0 + 0.0 * sinTh) * BssInvLog15, 120.0);
        }

        // fp32 mirror of the BSS sector table for GPU-targeted single-precision.
        private const float BssInvLog18F = 3.9173822f;  // (float)(1 / log10(1.8))
        private const float BssInvLog20F = 3.321928f;   // (float)(1 / log10(2.0))
        private const float BssInvLog24F = 2.630117f;   // (float)(1 / log10(2.4))
        private const float BssInvLog15F = 5.6788735f;  // (float)(1 / log10(1.5))

        private static (float m1, float m2, float phit) LookupBssSectorF(float theta)
        {
            float normTheta = ((theta % 360.0f) + 360.0f) % 360.0f;
            float sinTh = normTheta < 180.0f
                ? MathF.Sin(normTheta * (MathF.PI / 180.0f))
                : 0.0f;
            if (normTheta < 56.25f)
                return ((2.0f + 8.0f * sinTh) * BssInvLog24F,
                        (-9.0f + -8.0f * sinTh) * BssInvLog15F, 120.0f);
            if (normTheta < 123.75f)
                return ((2.0f + 8.0f * sinTh) * BssInvLog18F,
                        (-9.0f + -8.0f * sinTh) * BssInvLog20F, 90.0f);
            if (normTheta < 180.00f)
                return ((2.0f + 8.0f * sinTh) * BssInvLog24F,
                        (-9.0f + -8.0f * sinTh) * BssInvLog15F, 120.0f);
            return ((2.0f + 0.0f * sinTh) * BssInvLog24F,
                    (-9.0f + 0.0f * sinTh) * BssInvLog15F, 120.0f);
        }

        /// <summary>
        /// Static ES antenna gain computation - single source of truth for CPU and GPU.
        /// patternType: 0=APERR_019V01, 1=APERR_020V01, 2=APEREC021V01.
        /// All constants (gmax, g1, phim, phir, phib, dOverl) must be precomputed.
        /// theta is BSS azimuth in degrees (only used for pattern 2).
        /// </summary>
        public static double ComputeEsGain(
            double phi, double theta, int patternType,
            double gmax, double g1, double phim, double phir, double phib, double dOverl, int isBss)
        {
            if (patternType == 0)
            {
                // APERR_019V01
                if (dOverl >= 42.0)
                {
                    if (phi < phim) return gmax - 1.0 / 400.0 * (dOverl * phi) * (dOverl * phi);
                    if (phi < phir) return g1;
                    if (phi < 20.0) return 29.0 - 25.0 * Math.Log10(phi);
                    if (phi < phib) return Math.Min(-3.5, 32.0 - 25.0 * Math.Log10(phi));
                    return -10.0;
                }
                if (phi < phim) return gmax - 1.0 / 400.0 * (dOverl * phi) * (dOverl * phi);
                if (phi < phir) return g1;
                if (phi < phib) return 32.0 - 25.0 * Math.Log10(phi);
                return -10.0;
            }

            if (patternType == 1)
            {
                // APERR_020V01
                if (phi == 0.0) return gmax;
                if (phi < phim) return gmax - 1.0 / 400.0 * (dOverl * phi) * (dOverl * phi);
                if (phi < phir) return g1;
                if (dOverl > 100.0)
                {
                    if (phi < 10.0) return 29.0 - 25.0 * Math.Log10(phi);
                    if (phi < phib) return 34.0 - 30.0 * Math.Log10(phi);
                    if (phi < 80.0) return -12.0;
                    if (phi < 120.0) return -7.0;
                    return -12.0;
                }
                if (dOverl > 25.0)
                {
                    if (phi <= phib) return 29.0 - 25.0 * Math.Log10(phi);
                    if (phi <= 80.0) return -9.0;
                    if (phi <= 120.0) return -4.0;
                    return -9.0;
                }
                // dOverl >= 20 && dOverl <= 25
                if (phi < phib) return 29.0 - 25.0 * Math.Log10(phi);
                if (phi <= 80.0) return -9.0;
                return -5.0;
            }

            // patternType == 2: APEREC021V01
            if (phi == 0.0) return gmax;

            // BSS azimuthal sector coefficients via precomputed table lookup.
            var (m1, m2, phit) = LookupBssSector(theta);

            if (phi < phim) return gmax - 1.0 / 400.0 * (dOverl * phi) * (dOverl * phi);
            if (dOverl > 100.0)
            {
                if (phi < phir) return g1;
                if (phi < 10.0) return 29.0 - 25.0 * Math.Log10(phi);
                if (phi < phib) return 34.0 - 30.0 * Math.Log10(phi);
                if (phi < 80.0) return -12.0;
                if (phi < 120.0) return -7.0;
                return -12.0;
            }
            if (dOverl > 25.5)
            {
                if (phi < phir) return g1;
                if (phi < phib) return 29.0 - 25.0 * Math.Log10(phi);
                if (phi <= 80.0) return -9.0;
                if (phi <= 120.0) return -4.0;
                return -9.0;
            }
            // dOverl >= 11.0 && dOverl <= 25.5 - theta-dependent
            if (phi < phir) return g1;
            if (phi < phib) return 29.0 - 25.0 * Math.Log10(phi);
            if (phi < 50.0) return -10.0;
            if (phi < phit) return m1 * Math.Log10(phi / 50.0) - 10.0;
            return m2 * Math.Log10(phi / 180.0) - 17.0;
        }

        /// <summary>
        /// Single-precision variant for GPU fp32 kernels.
        /// </summary>
        public static float ComputeEsGainF(
            float phi, float theta, int patternType,
            float gmax, float g1, float phim, float phir, float phib, float dOverl, int isBss)
        {
            if (patternType == 0)
            {
                if (dOverl >= 42.0f)
                {
                    if (phi < phim) return gmax - 1.0f / 400.0f * (dOverl * phi) * (dOverl * phi);
                    if (phi < phir) return g1;
                    if (phi < 20.0f) return 29.0f - 25.0f * MathF.Log10(phi);
                    if (phi < phib) return MathF.Min(-3.5f, 32.0f - 25.0f * MathF.Log10(phi));
                    return -10.0f;
                }
                if (phi < phim) return gmax - 1.0f / 400.0f * (dOverl * phi) * (dOverl * phi);
                if (phi < phir) return g1;
                if (phi < phib) return 32.0f - 25.0f * MathF.Log10(phi);
                return -10.0f;
            }

            if (patternType == 1)
            {
                if (phi == 0.0f) return gmax;
                if (phi < phim) return gmax - 1.0f / 400.0f * (dOverl * phi) * (dOverl * phi);
                if (phi < phir) return g1;
                if (dOverl > 100.0f)
                {
                    if (phi < 10.0f) return 29.0f - 25.0f * MathF.Log10(phi);
                    if (phi < phib) return 34.0f - 30.0f * MathF.Log10(phi);
                    if (phi < 80.0f) return -12.0f;
                    if (phi < 120.0f) return -7.0f;
                    return -12.0f;
                }
                if (dOverl > 25.0f)
                {
                    if (phi <= phib) return 29.0f - 25.0f * MathF.Log10(phi);
                    if (phi <= 80.0f) return -9.0f;
                    if (phi <= 120.0f) return -4.0f;
                    return -9.0f;
                }
                if (phi < phib) return 29.0f - 25.0f * MathF.Log10(phi);
                if (phi <= 80.0f) return -9.0f;
                return -5.0f;
            }

            // APEREC021V01
            if (phi == 0.0f) return gmax;

            // BSS azimuthal sector coefficients via precomputed table lookup (fp32).
            var (m1, m2, phit) = LookupBssSectorF(theta);

            if (phi < phim) return gmax - 1.0f / 400.0f * (dOverl * phi) * (dOverl * phi);
            if (dOverl > 100.0f)
            {
                if (phi < phir) return g1;
                if (phi < 10.0f) return 29.0f - 25.0f * MathF.Log10(phi);
                if (phi < phib) return 34.0f - 30.0f * MathF.Log10(phi);
                if (phi < 80.0f) return -12.0f;
                if (phi < 120.0f) return -7.0f;
                return -12.0f;
            }
            if (dOverl > 25.5f)
            {
                if (phi < phir) return g1;
                if (phi < phib) return 29.0f - 25.0f * MathF.Log10(phi);
                if (phi <= 80.0f) return -9.0f;
                if (phi <= 120.0f) return -4.0f;
                return -9.0f;
            }
            if (phi < phir) return g1;
            if (phi < phib) return 29.0f - 25.0f * MathF.Log10(phi);
            if (phi < 50.0f) return -10.0f;
            if (phi < phit) return m1 * MathF.Log10(phi / 50.0f) - 10.0f;
            return m2 * MathF.Log10(phi / 180.0f) - 17.0f;
        }

        /// <summary>
        /// GSO satellite minimum elevation angle per S.1503-4 §D3.2.2 Table 8.
        /// </summary>
        public static double GetGsoMinElevationRad(double freqMhz)
            => AngleUtilities.DegToRad((freqMhz >= 17000.0) ? 20.0 : 10.0);

        /// <summary>
        /// GSO satellite receive antenna beamwidth per S.1503-4 §D3.2.2 Table 8.
        /// </summary>
        public static double GetGsoBeamwidthRad(double freqMhz)
        {
            if (freqMhz < 10000)
                return AngleUtilities.DegToRad(1.5);
            if (freqMhz < 17000)
                return AngleUtilities.DegToRad(4.0);
            return AngleUtilities.DegToRad(1.55);
        }

        /// <summary>
        /// Computes the 3 dB beamwidth in degrees from frequency (MHz) and antenna diameter (m).
        /// Formula: φ₃dB = 70 λ / D
        /// </summary>
        public static double Compute3dBDeg(double frequencyMHz, double antDiamM)
        {
            return 70.0 * (299792458.0 / (frequencyMHz * 1000000.0)) / antDiamM;
        }

        /// <summary>
        /// Returns pattern-specific gainMax and Ln for uplink antenna patterns.
        /// Values match ITU-R S.1503-4 Table 22-2/22-3 reference patterns.
        /// </summary>
        public static void GetPatternParameters(string pattern, double freqMin, out double gainMax, out double Ln)
        {
            switch (pattern)
            {
                case "APSREC408V01":
                    gainMax = 32.4;
                    Ln = -20.0;
                    break;
                case "APSREC407V01":
                    gainMax = 40.6;
                    Ln = freqMin < 27500.0 ? -20.0 : -10.0;
                    break;
                default:
                    gainMax = 0.0;
                    Ln = 0.0;
                    break;
            }
        }
    }


    // Interface class for antenna library implementation
    public interface AntennaLibraryImplementationInterface
    {
        public double GetAntGain(double angle, double theta, double gainMax, double phi3Db, double Ln);
        public double GetAntGain(double angle, double theta);

        public double GetMaxGain { get; }
    }

    /// <summary>
    /// Rec. ITU-R S.1428 - Reference receive earth station antenna pattern for FSS.
    /// Used for epfd(down) calculations per S.1503-4 §D3.2.2.
    /// Applicable for D/λ ≥ 20; separate formulations for D/λ ≥ 42 and D/λ less than 42.
    /// Gain regions: main lobe, near sidelobe plateau (G1), far sidelobe rolloff, back lobe.
    /// </summary>
    public class APERR_019V01 : AntennaLibraryImplementationInterface
    {
        internal double gmax;
        internal double lambda;
        internal double dOverl;
        internal double lOverd;
        internal double g1;
        internal double phim, phib, phir;

        public APERR_019V01(double freqMHz, double antDiamM)
        {
            lambda = 299792458.0 / (freqMHz * 1000000.0);
            dOverl = antDiamM / lambda;
            lOverd = lambda / antDiamM;
            gmax = 7.7 + 20.0 * Math.Log10(dOverl);
            g1 = 2.0 + 15.0 * Math.Log10(dOverl);
            phim = 20.0 * lOverd * Math.Sqrt(gmax - g1);
            phib = 48.0;
            if (dOverl >= 100.0)
                phir = 15.85 * Math.Pow(dOverl, -0.6);
            else
                phir = 100.0 * lOverd;
        }

        public double GetMaxGain => gmax;

        public double GetAntGain(double phi, double theta)
            => AntennaLibrary.ComputeEsGain(phi, theta, 0, gmax, g1, phim, phir, phib, dOverl, 0);

        public double GetAntGain(double phi, double theta, double gainMax, double phi3Db, double Ln)
            => AntennaLibrary.ComputeEsGain(phi, theta, 0, gainMax, g1, phim, phir, phib, dOverl, 0);
    }

    /// <summary>
    /// Rec. ITU-R S.1428 - Improved reference receive earth station antenna pattern for FSS.
    /// Used for epfd(down) calculations per S.1503-4 §D3.2.2.
    /// Enhanced model for D/λ > 100 with Gmax = 8.4 + 20 log10(D/λ) and additional
    /// back-lobe structure (80°–120° raised lobe). Falls back to standard model for 20 ≤ D/λ ≤ 100.
    /// </summary>
    public class APERR_020V01 : AntennaLibraryImplementationInterface
    {
        internal double gmax;
        internal double lambda;
        internal double dOverl;
        internal double lOverd;
        internal double g1;
        internal double phim, phib, phir;


        public APERR_020V01(double freqMHz, double antDiamM)
        {
            lambda = 299792458.0 / (freqMHz * 1000000.0);
            dOverl = antDiamM / lambda;
            lOverd = lambda / antDiamM;
            if (dOverl > 100.0)
            {
                gmax = 8.4 + 20.0 * Math.Log10(dOverl);
                phir = 15.85 * Math.Pow(dOverl, -0.6);
                phib = 34.1;
            }
            else
            {
                gmax = 7.7 + 20.0 * Math.Log10(dOverl);
                phir = 95.0 * lOverd;
                phib = 33.1;
            }

            g1 = 29.0 - 25.0 * Math.Log10(phir);
            phim = 20.0 * lOverd * Math.Sqrt(gmax - g1);
        }

        public double GetMaxGain => gmax;

        public double GetAntGain(double phi, double theta)
            => AntennaLibrary.ComputeEsGain(phi, theta, 1, gmax, g1, phim, phir, phib, dOverl, 0);

        public double GetAntGain(double phi, double theta, double gainMax, double phi3Db, double Ln)
            => AntennaLibrary.ComputeEsGain(phi, theta, 1, gainMax, g1, phim, phir, phib, dOverl, 0);
    }


    /// <summary>
    /// Rec. ITU-R BO.1443 - Reference receive earth station antenna pattern for BSS.
    /// Used for epfd(down) BSS calculations per S.1503-4 §D3.2.2.
    /// Includes azimuthal (theta) dependency: gain varies with the angle in the antenna
    /// aperture plane via sin(theta)-weighted slope coefficients m1, m2.
    /// Three theta sectors: 56.25°–123.75° (co-polar), 0°–56.25°/123.75°–180°, and 180°–360° (back).
    /// </summary>
    public class APEREC021V01 : AntennaLibraryImplementationInterface
    {
        internal double gmax;
        internal double lambda;
        internal double dOverl;
        internal double lOverd;
        internal double g1;
        internal double phim, phib, phir, phit;
        internal double m1, m2;


        public APEREC021V01(double freqMHz, double antDiamM)
        {
            lambda = 299792458.0 / (freqMHz * 1000000.0);
            dOverl = antDiamM / lambda;
            lOverd = lambda / antDiamM;

            if (dOverl > 100.0)
            {
                phir = 15.85 * Math.Pow(dOverl, -0.6);
                phib = 34.1;
            }
            else
            {
                phir = 95.0 * lOverd;
                phib = dOverl <= 25.5 ? 36.3 : 33.1;
            }
            gmax = 8.1 + 20.0 * Math.Log10(dOverl);
            g1 = 29.0 - 25.0 * Math.Log10(phir);
            phim = 20.0 * lOverd * Math.Sqrt(gmax - g1);
        }

        public double GetMaxGain => gmax;

        public double GetAntGain(double phi, double theta)
            => AntennaLibrary.ComputeEsGain(phi, theta, 2, gmax, g1, phim, phir, phib, dOverl, 1);

        public double GetAntGain(double phi, double theta, double gainMax, double phi3Db, double Ln)
            => AntennaLibrary.ComputeEsGain(phi, theta, 2, gainMax, g1, phim, phir, phib, dOverl, 1);
    }

    /// <summary>
    /// Rec. ITU-R S.672-4 - GSO satellite antenna pattern (narrow beam).
    /// Used for epfd(up) and epfd(is) calculations per S.1503-4 §D3.2.2.
    /// Parametric pattern requiring Gmax, phi_3dB and Ln (near-in sidelobe level).
    /// Gain = Gmax - 12(φ/φ₃dB)² for main lobe, Gmax + Ln plateau, then 25 log rolloff.
    /// Coefficients: a/2 = 0.915, b/2 = 3.16 (half-angle normalized breakpoints).
    /// </summary>
    public class APSREC407V01 : AntennaLibraryImplementationInterface
    {
        private double aDiv2 = 0.915;
        private double bDiv2 = 3.16;

        public APSREC407V01(double freqMHz)
        {
            
        }

        public double GetMaxGain => double.NaN;

        public double GetAntGain(double phi, double theta)
        {
            throw new Exception ("Not enough parameters for antenna APSREC407V01");
        }

        public double GetAntGain(double phi, double theta, double gainMax, double phi3Db, double Ln)
        {
            double arg = phi / phi3Db;
            if (arg <= aDiv2) return gainMax - 12.0 * arg * arg;
            if (arg <= bDiv2) return gainMax + Ln;
            return gainMax + Ln + 20.0 - 25.0 * Math.Log10(2.0 * arg);
        }
    }


    /// <summary>
    /// Rec. ITU-R S.672-4 - GSO satellite antenna pattern (wider beam variant).
    /// Used for epfd(up) and epfd(is) calculations per S.1503-4 §D3.2.2.
    /// Parametric pattern requiring Gmax, phi_3dB and Ln (near-in sidelobe level).
    /// Gain = Gmax - 3(φ/φ₀)² for main lobe (φ₀ = φ₃dB/2), Gmax + Ln plateau, then 25 log rolloff.
    /// Coefficients: a = 2.58, b = 6.32 (full-angle normalized breakpoints).
    /// phi1 marks the floor at 0 dBi beyond the rolloff region.
    /// </summary>
    public class APSREC408V01 : AntennaLibraryImplementationInterface
    {
        private double a = 2.58;
        private double b = 6.32;
        private double phi0 = double.NaN;
        private double aPhi0 = double.NaN;
        private double bPhi0 = double.NaN;
        private double phi1 = double.NaN;
        private bool firstCall;

        public APSREC408V01(double freqMHz)
        {
            firstCall = true;
        }

        public double GetMaxGain => double.NaN;

        public double GetAntGain(double phi, double theta)
        {
            throw new Exception("Not enough parameters for antenna APSREC408V01");
        }

        public double GetAntGain(double phi, double theta, double gainMax, double phi3Db, double Ln)
        {
            if (firstCall)
            {
                phi0 = phi3Db / 2.0;
                aPhi0 = a * phi0;
                bPhi0 = b * phi0;
                phi1 = Math.Pow(10.0, (-gainMax - Ln - 20.0) / -25.0) * phi0;
                firstCall = false;
            }
            double d = phi / phi0;
            if (phi <= aPhi0)
                return gainMax - 3.0 * d * d;
            if (phi <= bPhi0)
                return gainMax + Ln;
            if (phi <= phi1)
                return gainMax + Ln + 20.0 - 25.0 * Math.Log10(d);
            return 0.0;
        }
    }
}
