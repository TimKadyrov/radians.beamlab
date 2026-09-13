using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using radians.beamlab;
using radians.beamlab.app;

namespace radians.beamlab.dataset;

/// <summary>
/// Both curves of a family case, and the record that sets them side by side.
/// The TRUTH curve is the case's expectation: the simulated CDF at the victim
/// under the declared set. The EXAMINATION-READ curve is S.1503-4 Sec. D5.1.4.1
/// over the case's own masks and set at the same victim and depth -- this
/// producer's reading of what an administration's examination would produce
/// -- and it exists only where this producer has the examination side: the
/// classic downlink algorithm. A set that files MIN_DURATION selects the
/// track-duration algorithm, which this producer does not implement; the
/// epfd(is) and epfd(up) examinations are not implemented either. The record
/// says so per direction rather than substituting a reading of the wrong
/// algorithm.
///
/// The acceptance direction (design brief Sec. 2) is examination at or above
/// truth at every percentile; the gap is the projection margin. Both curves
/// carry their extension pair (the 24 h prefix of the 48 h run) so that every
/// quoted level says how far from converged it is.
/// </summary>
public static class FamilyCurves
{
    /// <summary>A CDF with its depth: the examination's own 0.1 dB bins, percent of time exceeded.</summary>
    public sealed record Curve(string Label, long Steps, long QuietSteps, double MaxEpfdDb, double[] Epfd, double[] Pct);

    /// <summary>One direction of a case: the truth pair and, where it exists, the examination pair.</summary>
    public sealed record Entry(string Direction, Curve TruthFull, Curve TruthHalf, Curve ExamFull, Curve ExamHalf,
        string ExaminationNote, string LimitRowContext);

    public const string TrackDurationNote =
        "no examination-read curve: the set files MIN_DURATION, which selects the track-duration algorithm; this producer implements the classic algorithm only (Sec. D5.1.4.1 with MIN_ANGLE_AT_ES), and a reading under the wrong algorithm would not be the consumer's.";
    public const string IsNote =
        "no examination-read curve: this producer has no epfd(is) examination side (it needs the satellite e.i.r.p. mask read, not the pfd mask).";
    public const string UpNote =
        "no examination-read curve: this producer has no Sec. D5.2 epfd(up) examination side.";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static Curve FromDown(string label, EpfdDownResult r, long steps)
    {
        var (e, p) = r.Accumulator.BuildCdf();
        return new Curve(label, steps, r.QuietSteps, r.MaxEpfdDb, e, p);
    }

    public static Curve FromIs(string label, EpfdDownResult r, long steps)
    {
        var (e, p) = r.IsAccumulator.BuildCdf();
        return new Curve(label, steps, r.IsQuietSteps, r.MaxEpfdIsDb, e, p);
    }

    public static Curve FromUp(string label, EpfdUpResult r, long steps)
    {
        var (e, p) = r.Accumulator.BuildCdf();
        return new Curve(label, steps, r.QuietSteps, r.MaxEpfdDb, e, p);
    }

    /// <summary>
    /// The examination-read curve: Sec. D5.1.4.1 over the case's masks and set at
    /// the truth's victim (the family's: earth station at 45 N 0 E, GSO satellite
    /// at 10 E, S.1428 0.6 m), the same step and depth, permissive limits (the
    /// deliverable is the curve, not a verdict).
    /// </summary>
    public static Curve Examination(string label, Constellation con, IMaskPfdRead masks, OperatingParamsSet set,
        double antFreqMhz, double dishM, double esLat, double esLon, double gsoLon, double stepSec, long steps,
        double simDurSec, List<radlimits.LimitPoint> permissive)
    {
        var victim = new EpfdDownVictim
        {
            EsLatDeg = esLat, EsLonDeg = esLon, GsoLonDeg = gsoLon,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, antFreqMhz, dishM),
        };
        var r = EpfdDownMask.Run(con, masks, set, victim, stepSec, steps, permissive, simDurSec);
        return FromDown(label, r, steps);
    }

    /// <summary>The epfd exceeded for at most perc percent of the time: the first bin at or below perc, else the last.</summary>
    public static double EpfdAt(Curve c, double perc)
    {
        int i = Array.FindIndex(c.Pct, v => v <= perc);
        return i < 0 ? c.Epfd[^1] : c.Epfd[i];
    }

    /// <summary>The percentiles tabulated: decades and half-decades down to the run's resolvable floor, plus the maximum.</summary>
    public static IReadOnlyList<double> Percentiles(long steps)
    {
        double floor = 100.0 / steps;
        var ps = new List<double> { 50, 20, 10, 5, 2, 1, 0.5, 0.2, 0.1, 0.05, 0.02, 0.01, 0.005, 0.002, 0.001 };
        return ps.Where(p => p >= floor - 1e-12).ToList();
    }

    /// <summary>The direction check: the smallest gap E - T over the resolvable percentiles, and where.</summary>
    public static (double MinGapDb, double AtPerc, int Violations, int Points) DirectionCheck(Curve truth, Curve exam)
    {
        var ps = Percentiles(Math.Min(truth.Steps, exam.Steps));
        double min = double.PositiveInfinity, at = double.NaN; int bad = 0;
        foreach (double p in ps)
        {
            double gap = EpfdAt(exam, p) - EpfdAt(truth, p);
            if (gap < min) { min = gap; at = p; }
            if (gap < -0.05) bad++;      // half a bin: quantisation, not a violation
        }
        return (min, at, bad, ps.Count);
    }

    /// <summary>The examination-read CDF beside the truth CDF, in the family's CSV form.</summary>
    public static void WriteExaminationCsv(string path, Curve c, string victimDesc, double fMin, double fMax, double stepSec, string setDesc)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# epfd(down) CDF -- the EXAMINATION-READ curve: S.1503-4 D5.1.4.1 over this case's own pfd masks and operating-parameter set, at the same victim and depth as the truth curve beside it; D7.1.2 bins (0.1 dB).");
        sb.AppendLine("# This is the producer's reading of what an administration's examination produces; the acceptance direction (design brief section 2) is examination >= truth at every percentile.");
        sb.AppendLine(string.Create(inv, $"# band={fMin}-{fMax} MHz  {victimDesc}  {setDesc}"));
        sb.AppendLine(string.Create(inv, $"# step_s={stepSec}  steps={c.Steps}  quiet_steps={c.QuietSteps}  max_epfd_db={c.MaxEpfdDb:F3}"));
        sb.AppendLine("epfd_dbw_m2_40khz,percent_time_exceeded");
        int first = Array.FindIndex(c.Pct, p => p < 100.0);
        int last = Array.FindLastIndex(c.Pct, p => p > 0.0);
        if (first < 0) { first = 0; last = c.Pct.Length - 1; }
        first = Math.Max(0, first - 1);
        last = Math.Min(c.Pct.Length - 1, last + 1);
        for (int i = first; i <= last; i++)
            sb.AppendLine(string.Create(inv, $"{c.Epfd[i]:F1},{c.Pct[i]:G9}"));
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    /// <summary>expected/curves.md: per direction the truth pair, the examination pair where it exists, the gap and the direction check.</summary>
    public static string WriteRecord(string path, string caseName, IReadOnlyList<Entry> entries, double stepSec, bool quick, string provenance)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"# The curves: {caseName}");
        sb.AppendLine();
        sb.AppendLine("Each direction of this case has its TRUTH curve -- the simulated CDF at the victim under the declared set, the expectation a consumer's examination is compared with -- and, where this producer has the examination side, the EXAMINATION-READ curve: S.1503-4 Sec. D5.1.4.1 over the case's own masks and set at the same victim and depth. The acceptance direction (design brief Sec. 2) is examination at or above truth at every percentile; the gap is the projection margin, and a consumer's own examination should sit near this producer's reading of it. Where the producer has no examination side for a direction, the record says so instead of substituting a reading under another algorithm.");
        sb.AppendLine();
        long steps = entries[0].TruthFull.Steps, half = entries[0].TruthHalf.Steps;
        sb.AppendLine(string.Create(inv, $"Depth and pair: {steps} steps of {stepSec:F0} s ({stepSec * steps / 3600.0:F0} h); the {stepSec * half / 3600.0:F0} h prefix ({half} steps) is the EXTENSION pair -- the shorter run is the first half of the longer, not an independent draw. The resolvable floor is {100.0 / steps:F3}% of time; percentiles finer than that are not tabulated. Levels are quotable to the tolerance of their \"moved\" column (0.5 dB is the family's default); the maximum is a single-event statistic and converges slowly by nature.{(quick ? " QUICK profile: structure verification only, the numbers are not delivery numbers." : "")}"));
        sb.AppendLine();
        foreach (var e in entries)
        {
            sb.AppendLine("## " + e.Direction);
            sb.AppendLine();
            bool hasExam = e.ExamFull is not null;
            var ps = Percentiles(steps);
            if (hasExam)
            {
                sb.AppendLine("| % time exceeded | truth epfd (48 h) | truth (24 h) | moved | examination epfd (48 h) | examination (24 h) | moved | gap E - T (dB) |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (double p in ps)
                {
                    double t = EpfdAt(e.TruthFull, p), th = EpfdAt(e.TruthHalf, p), x = EpfdAt(e.ExamFull, p), xh = EpfdAt(e.ExamHalf, p);
                    sb.AppendLine(string.Create(inv, $"| {p:G4} | {t:F1} | {th:F1} | {t - th:+0.0;-0.0;0.0} | {x:F1} | {xh:F1} | {x - xh:+0.0;-0.0;0.0} | {x - t:+0.0;-0.0;0.0} |"));
                }
                sb.AppendLine(string.Create(inv, $"| max | {e.TruthFull.MaxEpfdDb:F1} | {e.TruthHalf.MaxEpfdDb:F1} | {e.TruthFull.MaxEpfdDb - e.TruthHalf.MaxEpfdDb:+0.0;-0.0;0.0} | {e.ExamFull.MaxEpfdDb:F1} | {e.ExamHalf.MaxEpfdDb:F1} | {e.ExamFull.MaxEpfdDb - e.ExamHalf.MaxEpfdDb:+0.0;-0.0;0.0} | {e.ExamFull.MaxEpfdDb - e.TruthFull.MaxEpfdDb:+0.0;-0.0;0.0} |"));
                sb.AppendLine();
                var (minGap, at, bad, n) = DirectionCheck(e.TruthFull, e.ExamFull);
                if (bad == 0)
                    sb.AppendLine(string.Create(inv, $"Direction check: examination >= truth at every one of the {n} resolvable percentiles tabulated -- HOLDS; the smallest gap is {minGap:+0.0;-0.0;0.0} dB at {at:G4}%."));
                else
                    sb.AppendLine(string.Create(inv, $"Direction check: examination below truth at {bad} of the {n} resolvable percentiles -- VIOLATED; the largest shortfall is {minGap:+0.0;-0.0;0.0} dB at {at:G4}%. A non-conservative reading is a defect (design brief Sec. 2): in the mask, in the declaration, or in this producer's examination -- to be found, not accepted."));
                sb.AppendLine(string.Create(inv, $"Quiet steps (no visible contribution): truth {e.TruthFull.QuietSteps}, examination {e.ExamFull.QuietSteps} of {steps}."));
            }
            else
            {
                sb.AppendLine("| % time exceeded | truth epfd (48 h) | truth (24 h) | moved |");
                sb.AppendLine("|---|---|---|---|");
                foreach (double p in ps)
                {
                    double t = EpfdAt(e.TruthFull, p), th = EpfdAt(e.TruthHalf, p);
                    sb.AppendLine(string.Create(inv, $"| {p:G4} | {t:F1} | {th:F1} | {t - th:+0.0;-0.0;0.0} |"));
                }
                sb.AppendLine(string.Create(inv, $"| max | {e.TruthFull.MaxEpfdDb:F1} | {e.TruthHalf.MaxEpfdDb:F1} | {e.TruthFull.MaxEpfdDb - e.TruthHalf.MaxEpfdDb:+0.0;-0.0;0.0} |"));
                sb.AppendLine();
                sb.AppendLine("Examination-read curve: " + e.ExaminationNote);
                sb.AppendLine(string.Create(inv, $"Quiet steps (no visible contribution): truth {e.TruthFull.QuietSteps} of {steps}."));
            }
            if (!string.IsNullOrEmpty(e.LimitRowContext))
                sb.AppendLine("Context, not a verdict: the band's Article 22 row is " + e.LimitRowContext + ". The curves' victim keeps the family's convention (S.1428 0.6 m for epfd(down), the S.672 beam for epfd(is) and epfd(up)); no verdict is drawn in this record.");
            sb.AppendLine();
        }
        sb.AppendLine("Provenance: " + provenance);
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
        return path;
    }
}
