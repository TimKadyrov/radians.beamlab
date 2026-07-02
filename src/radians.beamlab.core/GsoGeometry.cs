using System;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>
/// GSO-arc geometry helpers used by the PFD-mask exclusion pass. Sphere of
/// radius <see cref="GeoMath.EarthRadiusKm"/>; GSO taken as a circle of radius
/// <see cref="GsoRadiusKm"/> in the ECEF equatorial plane.
///
/// The α angle here is the S.1503-4 §D6 avoidance angle: the angle, measured
/// at an earth station, between the line ES→NGSO and the ES→(nearest visible
/// GSO satellite) line. Only the magnitude is needed for the on/off gating and
/// for drawing the exclusion contour, so the sign machinery of §D6.4.4.1 is
/// not implemented here.
/// </summary>
public static class GsoGeometry
{
    /// <summary>Geostationary orbit radius (km) from Earth centre. ITU-R canonical.</summary>
    public const double GsoRadiusKm = 42_164.2;

    /// <summary>
    /// GSO receive-antenna minimum operational elevation angle (deg), from
    /// Recommendation ITU-R S.1503-4 §D3.2.2 Table 8: 10° below 17 GHz, 20°
    /// at 17 GHz and above.
    /// </summary>
    public static double GsoMinElevationDeg(double frequencyGHz)
        => frequencyGHz >= 17.0 ? 20.0 : 10.0;

    /// <summary>
    /// Minimum magnitude of α (deg) between the ES→NGSO line and the
    /// ES→(any visible GSO satellite) line.
    ///
    /// Uses the analytic method of S.1503-4 §D6.4.4.4 (preferred method per
    /// §D1.4): setting d/dθ[cos α] = 0 with x = sin θ reduces to a quartic;
    /// Newton–Raphson from x = ±1 yields four candidate θ, augmented by the
    /// two visible-arc endpoints ±θ_max where cos θ_max = R_earth / (R_gso ·
    /// cos φ_ES). The returned α is the minimum |α| over the visible
    /// candidates.
    ///
    /// Returns 180° when the visible GSO arc is empty (ES too close to a
    /// pole, i.e. R_earth / (R_gso · cos φ_ES) ≥ 1).
    /// </summary>
    public static double AlphaMinAbsDeg(Vec3 esEcef, Vec3 ngsoEcef)
    {
        double esMag = esEcef.Length;
        if (esMag < 1e-6) return 180.0;

        double esLatRad = Math.Asin(Math.Clamp(esEcef.Z / esMag, -1.0, 1.0));
        double esLonRad = Math.Atan2(esEcef.Y, esEcef.X);
        double cosLat = Math.Cos(esLatRad);
        if (cosLat <= 1e-9) return 180.0;

        // Visible-arc limit: cos θ_max = R / (R_gso · cos φ_ES).
        double kOverCos = EarthRadiusKm / (GsoRadiusKm * cosLat);
        if (kOverCos >= 1.0) return 180.0;      // whole GSO arc below the ES horizon
        double thMax = Math.Acos(kOverCos);

        // Vector ES→NGSO.
        double dx = ngsoEcef.X - esEcef.X;
        double dy = ngsoEcef.Y - esEcef.Y;
        double dz = ngsoEcef.Z - esEcef.Z;

        // Numerator (PN·PG) = A + B·cos θ + C·sin θ, with GSO point G(θ) = R_gso·(cos θ, sin θ, 0).
        double A = -(dx * esEcef.X + dy * esEcef.Y + dz * esEcef.Z);
        double B = dx * GsoRadiusKm;
        double C = dy * GsoRadiusKm;

        // Denominator² |PG|² = E + F·cos θ + G_·sin θ.
        double E = GsoRadiusKm * GsoRadiusKm + esMag * esMag;
        double F = -2.0 * esEcef.X * GsoRadiusKm;
        double G_ = -2.0 * esEcef.Y * GsoRadiusKm;

        // Substituting x = sin θ, cos θ = ±√(1−x²), the extremum condition reduces to a quartic
        // a₄x⁴ + a₃x³ + a₂x² + a₁x + a₀ = 0 with the following coefficients.
        double a = A * G_ - 2.0 * C * E;
        double b = B * F - C * G_;
        double c = 2.0 * C * F - B * G_;
        double d = A * F - 2.0 * B * E;
        double e = -B * G_ - C * F;

        double a4 = e * e + b * b;
        double a3 = 2.0 * d * e + 2.0 * a * b;
        double a2 = d * d + 2.0 * c * e + a * a - b * b;
        double a1 = 2.0 * c * d - 2.0 * a * b;
        double a0 = c * c - a * a;

        // Newton–Raphson from x = +1 and x = −1 gives up to 4 candidate θ (each root x gives
        // arcsin x and π − arcsin x). §D6.4.4.4 stops when Δx < 1e-6 or after 200 iterations.
        double th0 = 0.0, th1 = 0.0, th2 = 0.0, th3 = 0.0;

        for (int startIdx = 0; startIdx < 2; startIdx++)
        {
            double xn1 = startIdx == 0 ? 1.0 : -1.0;
            double res = xn1;
            for (int iter = 0; iter < 200; iter++)
            {
                double x2 = xn1 * xn1;
                double x3 = x2 * xn1;
                double x4 = x2 * x2;

                double fx  = a4 * x4 + a3 * x3 + a2 * x2 + a1 * xn1 + a0;
                double fx1 = 4.0 * a4 * x3 + 3.0 * a3 * x2 + 2.0 * a2 * xn1 + a1;
                if (Math.Abs(fx1) < 1e-15) break;
                double xn2 = xn1 - fx / fx1;
                if (Math.Abs(xn2 - xn1) < 1e-6) { res = xn2; break; }
                xn1 = xn2;
            }
            // arcsin can throw NaN when |res| > 1 due to Newton overshoot — clamp then arcsin.
            double th = Math.Asin(Math.Clamp(res, -1.0, 1.0));
            if (startIdx == 0) { th0 = th; th1 = Math.PI - th; }
            else               { th2 = th; th3 = Math.PI - th; }
        }

        double th4 = esLonRad - thMax;
        double th5 = esLonRad + thMax;

        double minAlpha = double.PositiveInfinity;
        double esToNgsoX = dx, esToNgsoY = dy, esToNgsoZ = dz;

        for (int i = 0; i < 6; i++)
        {
            double th = i switch { 0 => th0, 1 => th1, 2 => th2, 3 => th3, 4 => th4, _ => th5 };

            // Filter Newton roots that fall outside the visible GSO arc.
            if (i < 4)
            {
                double dth = th - esLonRad;
                if (dth > Math.PI) dth -= 2.0 * Math.PI;
                else if (dth < -Math.PI) dth += 2.0 * Math.PI;
                if (Math.Abs(dth) > thMax) continue;
            }

            double gsoX = GsoRadiusKm * Math.Cos(th);
            double gsoY = GsoRadiusKm * Math.Sin(th);
            double vx = gsoX - esEcef.X;
            double vy = gsoY - esEcef.Y;
            double vz = -esEcef.Z;   // GSO Z = 0

            double dot = esToNgsoX * vx + esToNgsoY * vy + esToNgsoZ * vz;
            double magP = Math.Sqrt(esToNgsoX * esToNgsoX + esToNgsoY * esToNgsoY + esToNgsoZ * esToNgsoZ);
            double magV = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (magP < 1e-12 || magV < 1e-12) continue;

            double alpha = Math.Acos(Math.Clamp(dot / (magP * magV), -1.0, 1.0));
            if (alpha < minAlpha) minAlpha = alpha;
        }

        return double.IsPositiveInfinity(minAlpha) ? 180.0 : minAlpha * 180.0 / Math.PI;
    }
}
