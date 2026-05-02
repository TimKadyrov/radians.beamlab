using System;

namespace radians.beamlab;

/// <summary>
/// Bessel function J1, polynomial approximation from Abramowitz &amp; Stegun
/// 9.4.4 (|x| ≤ 3) and 9.4.6 (x ≥ 3). Accuracy ≈ 1.3e-8. Sufficient for the
/// Taylor-illumination pattern in S.1528 §1.4.
/// </summary>
internal static class BesselJ1
{
    public static double J1(double x)
    {
        if (x == 0.0) return 0.0;
        double abs = Math.Abs(x);
        double sign = x >= 0 ? 1.0 : -1.0;

        if (abs <= 3.0)
        {
            double y = abs / 3.0;
            double y2 = y * y;
            // J1(x) = x · {0.5 - 0.5625·(x/3)² + ... }
            double poly =
                0.5
                + y2 * (-0.56249985
                + y2 * (0.21093573
                + y2 * (-0.03954289
                + y2 * (0.00443319
                + y2 * (-0.00031761
                + y2 * 0.00001109)))));
            return sign * abs * poly;
        }
        else
        {
            double t = 3.0 / abs;
            // f1(3/x), θ1(3/x) per A&S 9.4.6
            double f1 =
                0.79788456
                + t * (0.00000156
                + t * (0.01659667
                + t * (0.00017105
                + t * (-0.00249511
                + t * (0.00113653
                + t * -0.00020033)))));
            double theta1 = t * (
                  0.12499612
                + t * (-0.00005650
                + t * (-0.00637879
                + t * (0.00074348
                + t * (0.00079824
                + t * -0.00029166)))));
            double arg = abs - 0.75 * Math.PI + theta1;
            return sign * f1 / Math.Sqrt(abs) * Math.Cos(arg);
        }
    }
}
