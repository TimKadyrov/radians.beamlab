using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using radcompute1503_2;
using radlimits;

namespace radians.beamlab;

/// <summary>
/// The verdict rule of the design brief ("Verdict rule: compliance against
/// the Article 22 limit curve", adopted 18 September 2026): a distribution
/// complies only if (1) at every tabulated limit point the computed
/// percentage of time does not exceed the tabulated one and (2) at no
/// result bin between the lowest and highest tabulated epfd does it exceed
/// the log-linear interpolation of the limit between adjacent points.
/// Neither test subsumes the other: the point-wise test owns the
/// never-to-be-exceeded cap (limit percentage zero), the curve test the
/// gaps between points, which in Table 22-1C reach 5.4 to 34 dB with
/// nothing tabulated inside. The curve, the scan range (curve-zero bins
/// skipped) and the tolerance -- a horizontal slack read towards lower
/// epfd, tau dB, default half a bin -- are those of the vendored
/// accumulator's own scan (<see cref="EpfdAccumulator.FindWorstMaskViolation"/>),
/// which the verdict calls; the array forms here reproduce it on a CDF so
/// that a crossing can be reported with its allowed value and so that the
/// margin under the rule can be measured by shifting the distribution.
///
/// Margins: the point-wise margin (limit minus computed level at each
/// tabulated percentage, the figure every record has quoted) is not a
/// bound on a crossing; the CURVE MARGIN is the largest shift of the whole
/// distribution towards higher epfd that still leaves no crossing
/// (negative when it fails as it stands), and the RULE MARGIN is the
/// smaller of the two. Every record names the rule and the tolerance it
/// verdicts under.
/// </summary>
public static class LimitCurveRule
{
    /// <summary>The brief's recommended default: half a result bin.</summary>
    public const double DefaultToleranceDb = EpfdAccumulator.MaskToleranceDb;

    /// <summary>The examination's result bin (Sec. D7.1.2).</summary>
    public const double BinWidthDb = 0.1;

    /// <summary>Widest shift searched for the curve margin (dB, either way).</summary>
    public const double MarginSearchDb = 60.0;

    /// <summary>A crossing of the curve: the bin of largest computed/allowed ratio.</summary>
    public sealed record Crossing(double EpfdDb, double ComputedPerc, double AllowedPerc, double LimitPerc, double Ratio)
    {
        public string Text => string.Create(CultureInfo.InvariantCulture,
            $"crosses at {EpfdDb:F1} dB: {ComputedPerc:G4}% vs {AllowedPerc:G4}% allowed (limit {LimitPerc:G4}%), x{Ratio:F2}");
    }

    /// <summary>The rule, named as every record must name it.</summary>
    public static string Name(double toleranceDb = DefaultToleranceDb) => string.Create(CultureInfo.InvariantCulture,
        $"Verdict rule: compliance against the Article 22 limit curve (design brief) -- PASS only if every tabulated "
        + $"limit point passes AND the distribution nowhere crosses the log-linear curve between the tabulated points, "
        + $"tolerance {toleranceDb:0.00} dB read towards lower epfd. Margins quoted as 'point margin' are the limit minus "
        + $"the computed level at each tabulated percentage; the 'curve margin' is the dB shift of the whole distribution "
        + $"that just clears the curve; the rule margin is the smaller of the two.");

    /// <summary>The limit curve on the bins of an accumulator built from these points.</summary>
    public static double[] Curve(List<LimitPoint> points) => new EpfdAccumulator(points).BuildLinearizedLimit(points);

    /// <summary>The verdict: point-wise AND the accumulator's own curve scan.</summary>
    public static bool Pass(EpfdAccumulator acc, List<LimitPoint> points, double toleranceDb = DefaultToleranceDb)
    {
        var (passResults, _) = acc.CompareWithLimits(points);
        return passResults.All(p => p) && acc.FindWorstMaskViolation(points, toleranceDb) is null;
    }

    /// <summary>
    /// The worst crossing on a CDF (epfd per bin, percentage exceeded per bin)
    /// laid over <paramref name="curve"/> from <see cref="Curve"/> on the same
    /// bins; null when the distribution clears the curve everywhere.
    /// </summary>
    public static Crossing? Scan(double[] epfd, double[] pct, double[] curve, List<LimitPoint> points,
        double toleranceDb = DefaultToleranceDb)
        => ScanShifted(epfd, pct, curve, points, toleranceDb, 0);

    /// <summary>
    /// The curve margin: the largest whole-bin shift of the distribution
    /// towards higher epfd (dB) that leaves no crossing, negative when it
    /// fails as it stands; +-<see cref="MarginSearchDb"/> when the search
    /// range is exhausted. Shifting up can only add crossings, so the
    /// boundary is found by bisection.
    /// </summary>
    public static double CurveMarginDb(double[] epfd, double[] pct, double[] curve, List<LimitPoint> points,
        double toleranceDb = DefaultToleranceDb)
    {
        int kMax = (int)Math.Round(MarginSearchDb / BinWidthDb);
        int lo = -kMax, hi = kMax;
        if (ScanShifted(epfd, pct, curve, points, toleranceDb, lo) is not null) return -MarginSearchDb;
        if (ScanShifted(epfd, pct, curve, points, toleranceDb, hi) is null) return MarginSearchDb;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (ScanShifted(epfd, pct, curve, points, toleranceDb, mid) is null) lo = mid; else hi = mid;
        }
        return lo * BinWidthDb;
    }

    /// <summary>The scan with the distribution shifted by <paramref name="shiftBins"/> bins towards higher epfd.</summary>
    private static Crossing? ScanShifted(double[] epfd, double[] pct, double[] curve, List<LimitPoint> points,
        double toleranceDb, int shiftBins)
    {
        if (points is null || points.Count == 0 || curve.Length == 0) return null;
        double first = points.Min(p => p.EPFD), last = points.Max(p => p.EPFD);
        double shift = toleranceDb / BinWidthDb;
        int n = pct.Length;
        Crossing? worst = null;
        double worstRatio = 1.0;
        for (int i = 0; i < curve.Length && i < epfd.Length; i++)
        {
            if (epfd[i] < first - 1e-9 || epfd[i] > last + 1e-9) continue;
            double limitHere = curve[i];
            if (limitHere <= 0.0) continue;
            double allowed = limitHere;
            if (shift > 0.0 && i > 0 && curve[i - 1] > 0.0)
                allowed = limitHere * Math.Pow(curve[i - 1] / limitHere, shift);
            int j = i - shiftBins;
            double calc = j < 0 ? pct[0] : j >= n ? 0.0 : pct[j];
            if (calc <= allowed) continue;
            double ratio = calc / allowed;
            if (ratio > worstRatio)
            {
                worstRatio = ratio;
                worst = new Crossing(Math.Round(epfd[i], 1), calc, allowed, limitHere, ratio);
            }
        }
        return worst;
    }
}
