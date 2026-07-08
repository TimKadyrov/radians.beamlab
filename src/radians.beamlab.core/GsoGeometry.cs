using System;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>
/// GSO-arc geometry helpers used by the PFD-mask exclusion pass and the
/// alpha/deltaLongitude mask plot. Sphere of radius <see cref="GeoMath.EarthRadiusKm"/>;
/// GSO taken as a circle of radius <see cref="GsoRadiusKm"/> in the ECEF
/// equatorial plane.
///
/// The alpha angle is the S.1503-4 Sec. D6 avoidance angle: the angle, measured at an
/// earth station, between the line ES->NGSO and the ES->(nearest visible GSO
/// satellite) line, signed per Sec. D6.4.4.1 (positive when the ES->NGSO ray
/// crosses the equatorial plane inside the GSO radius, northern-hemisphere
/// convention; sign flipped in the southern hemisphere).
/// </summary>
public static class GsoGeometry
{
    /// <summary>Geostationary orbit radius (km) from Earth centre. ITU-R canonical.</summary>
    public const double GsoRadiusKm = 42_164.2;

    /// <summary>
    /// GSO receive-antenna minimum operational elevation angle (deg), from
    /// Recommendation ITU-R S.1503-4 Sec. D3.2.2 Table 8: 10 deg below 17 GHz, 20 deg
    /// at 17 GHz and above.
    /// </summary>
    public static double GsoMinElevationDeg(double frequencyGHz)
        => frequencyGHz >= 17.0 ? 20.0 : 10.0;

    /// <summary>
    /// Minimum magnitude of alpha (deg) between the ES->NGSO line and the
    /// ES->(any visible GSO satellite) line. 180 deg when the visible GSO arc is
    /// empty (ES too close to a pole). Unsigned convenience wrapper over
    /// <see cref="AlphaSignedDeg"/>.
    /// </summary>
    public static double AlphaMinAbsDeg(Vec3 esEcef, Vec3 ngsoEcef)
        => AlphaSignedDeg(esEcef, ngsoEcef) is { } r ? Math.Abs(r.alphaDeg) : 180.0;

    /// <summary>
    /// Signed alpha (deg, S.1503-4 Sec. D6.4.4.1) between the ES->NGSO line and the
    /// nearest visible GSO arc point, plus the ECEF longitude (deg) of that
    /// arc point -- the two quantities the alpha/deltaLongitude PFD mask is keyed on
    /// (deltaLongitude = NGSO sub-sat longitude - returned GSO longitude, wrapped).
    ///
    /// Analytic method of Sec. D6.4.4.4 (preferred per Sec. D1.4): setting
    /// d/dtheta[cos alpha] = 0 with x = sin theta reduces to a quartic; Newton-Raphson from
    /// x = +/-1 yields four candidate theta, augmented by the two visible-arc
    /// endpoints +/-theta_max where cos theta_max = R_earth / (R_gso * cos phi_ES). The
    /// minimum |alpha| over the visible candidates wins.
    ///
    /// Sign per Sec. D6.4.4.1: extend the ES->NGSO ray to the equatorial plane; a
    /// crossing inside the GSO radius (with positive ray parameter) is
    /// positive in the northern hemisphere, negative outside or backwards;
    /// the sign flips in the southern hemisphere. At the equator the sign is
    /// -sign(deltaZ of the ray).
    ///
    /// Returns null when no GSO satellite is visible from the ES.
    /// </summary>
    public static (double alphaDeg, double gsoLonDeg)? AlphaSignedDeg(Vec3 esEcef, Vec3 ngsoEcef)
    {
        double esMag = esEcef.Length;
        if (esMag < 1e-6) return null;

        double esLatRad = Math.Asin(Math.Clamp(esEcef.Z / esMag, -1.0, 1.0));
        double esLonRad = Math.Atan2(esEcef.Y, esEcef.X);
        double cosLat = Math.Cos(esLatRad);
        if (cosLat <= 1e-9) return null;

        // Visible-arc limit: cos theta_max = R / (R_gso * cos phi_ES).
        double kOverCos = EarthRadiusKm / (GsoRadiusKm * cosLat);
        if (kOverCos >= 1.0) return null;       // whole GSO arc below the ES horizon
        double thMax = Math.Acos(kOverCos);

        // Vector ES->NGSO.
        double dx = ngsoEcef.X - esEcef.X;
        double dy = ngsoEcef.Y - esEcef.Y;
        double dz = ngsoEcef.Z - esEcef.Z;

        // Numerator (PN*PG) = A + B*cos theta + C*sin theta, with GSO point G(theta) = R_gso*(cos theta, sin theta, 0).
        double A = -(dx * esEcef.X + dy * esEcef.Y + dz * esEcef.Z);
        double B = dx * GsoRadiusKm;
        double C = dy * GsoRadiusKm;

        // Denominator^2 |PG|^2 = E + F*cos theta + G_*sin theta.
        double E = GsoRadiusKm * GsoRadiusKm + esMag * esMag;
        double F = -2.0 * esEcef.X * GsoRadiusKm;
        double G_ = -2.0 * esEcef.Y * GsoRadiusKm;

        // Substituting x = sin theta, cos theta = +/-sqrt(1-x^2), the extremum condition reduces to a quartic
        // a_4x^4 + a_3x^3 + a_2x^2 + a_1x + a_0 = 0 with the following coefficients.
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

        // Newton-Raphson from x = +1 and x = -1 gives up to 4 candidate theta (each root x gives
        // arcsin x and pi - arcsin x). Sec. D6.4.4.4 stops when deltax < 1e-6 or after 200 iterations.
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
            // arcsin can throw NaN when |res| > 1 due to Newton overshoot -- clamp then arcsin.
            double th = Math.Asin(Math.Clamp(res, -1.0, 1.0));
            if (startIdx == 0) { th0 = th; th1 = Math.PI - th; }
            else               { th2 = th; th3 = Math.PI - th; }
        }

        double th4 = esLonRad - thMax;
        double th5 = esLonRad + thMax;

        double minAlpha = double.PositiveInfinity;
        double minTheta = 0.0;

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

            double dot = dx * vx + dy * vy + dz * vz;
            double magP = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double magV = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (magP < 1e-12 || magV < 1e-12) continue;

            double alpha = Math.Acos(Math.Clamp(dot / (magP * magV), -1.0, 1.0));
            if (alpha < minAlpha) { minAlpha = alpha; minTheta = th; }
        }

        if (double.IsPositiveInfinity(minAlpha)) return null;

        // Sign per S.1503-4 Sec. D6.4.4.1 -- port of the reference implementation.
        double alphaSign;
        if (Math.Abs(esEcef.Z) < 1e-6)
        {
            // ES at the equator: alpha sign is the negative of the ray's deltaZ sign.
            // dz = 0 (both at equator) gives 0 -- correct: the ray lies in the
            // GSO arc plane, so min |alpha| is 0 for any visible NGSO.
            alphaSign = -Math.Sign(dz);
        }
        else
        {
            // Extend R = R_ES + lambda*(ES->NGSO) to the equatorial plane Z = 0.
            double lambdaZ0 = -esEcef.Z / dz;
            double rxz0 = esEcef.X + lambdaZ0 * dx;
            double ryz0 = esEcef.Y + lambdaZ0 * dy;
            double rz0Mag = Math.Sqrt(rxz0 * rxz0 + ryz0 * ryz0);

            // Northern hemisphere: crossing inside the GSO radius (forwards) -> positive.
            alphaSign = (rz0Mag > GsoRadiusKm || lambdaZ0 <= 0.0) ? -1.0
                      : (rz0Mag != GsoRadiusKm) ? 1.0
                      : 0.0;

            if (esEcef.Z < 0.0) alphaSign = -alphaSign;
        }

        double gsoLonDeg = ((minTheta * 180.0 / Math.PI + 540.0) % 360.0) - 180.0;
        return (alphaSign * minAlpha * 180.0 / Math.PI, gsoLonDeg);
    }
}
