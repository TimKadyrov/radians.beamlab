using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using radcompute1503_2;
using radians.beamlab;
using radians.beamlab.app;
using radians.beamlab.dataset;

namespace radians.beamlab.checks;

/// <summary>
/// Diagnostic: the dataset's expected examination CDFs and the sweep-grid
/// probe's victims under the CURVE verdict rule -- pass only if every
/// tabulated limit point passes AND the CDF nowhere crosses the log-linear
/// limit curve between the first and last tabulated EPFD (the rule the
/// Bureau-side implementation applies since its commit of 18 September
/// 2026: bins where the curve is 0 skipped, a horizontal tolerance read
/// half a bin to the left, the worst bin by calc/allowed ratio reported).
/// beamlab's own verdicts are point-wise; this scan says whether any
/// expected verdict of the probe cases would flip under the curve rule,
/// so the decision on one shared rule can be taken on measured ground.
/// Nothing is written; the verdict path is untouched.
///
/// Run:  -- curvescan [toleranceDb]        default tolerance 0.05 dB
/// </summary>
internal static class CurveScan
{
    private sealed record Violation(double EpfdDb, double CalcPerc, double AllowedPerc, double LimitPerc, double Ratio);

    public static int Run(string[] args)
    {
        var inv = CultureInfo.InvariantCulture;
        double tol = args.Length > 0 && double.TryParse(args[0], NumberStyles.Float, inv, out double t) ? t : 0.05;
        string repo = Directory.Exists(@"C:\Projects\radians.beamlab") ? @"C:\Projects\radians.beamlab" : AppContext.BaseDirectory;
        string dllDir = new[] { @"C:\Projects\_EPFD\radians\radians\dlls", @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0" }
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdLimitsApi64.dll")));
        string limitsDb = ProbeExamination.ResolveLimitsDb(null);
        if (dllDir is null || limitsDb is null) { Console.WriteLine("ABORT: BR limits database or EpfdLimitsApi64.dll not present."); return 2; }
        double altKm = DatasetGenerator.Shells.Min(s => s.OperatingHeightKm ?? s.AltitudeKm);
        var (d1Lo, d1Hi) = DatasetGenerator.ProbeBandMhz;
        var (d2Lo, d2Hi) = DatasetGenerator.ConsistencyBandMhz;
        var limD1 = ProbeExamination.LoadLimitRow(limitsDb, dllDir, d1Lo, d1Hi, 40.0, altKm);
        var limD2 = ProbeExamination.LoadLimitRow(limitsDb, dllDir, d2Lo, d2Hi, 40.0, altKm);
        Console.WriteLine(string.Create(inv, $"curvescan: tolerance {tol:F2} dB (horizontal, read towards lower epfd); point-wise pass = every tabulated margin >= 0"));
        Console.WriteLine("  D1 row: " + limD1.Label);
        Console.WriteLine("  D2 row: " + limD2.Label);
        Console.WriteLine();
        Console.WriteLine("case      | victim | record | point-wise | curve scan (worst bin)                                   | flip");
        Console.WriteLine("----------|--------|--------|------------|----------------------------------------------------------|-----");

        int flips = 0, scanned = 0;
        // Part A: the expectation CDFs on disk.
        foreach (var (caseName, lim) in new[] { ("BL-R1", limD1), ("BL-R2", limD1), ("BL-C1", limD2) })
        {
            string dir = Path.Combine(repo, "dataset", caseName, "expected");
            if (!Directory.Exists(dir)) { Console.WriteLine($"{caseName,-9} | (no expected/ directory)"); continue; }
            foreach (string csv in Directory.GetFiles(dir, "examination_lat*_cdf.csv").OrderBy(f => f))
            {
                var (epfd, pct, headerVerdict) = ReadCdf(csv);
                string victim = Path.GetFileName(csv).Replace("examination_", "").Replace("_cdf.csv", "");
                // The CSV starts at the accumulator's first bin and stops after the last bin any
                // sample reached; the bins above it hold 0% exceeded, so the arrays are padded
                // to the row's accumulator before the curve is laid over them.
                var acc = new EpfdAccumulator(lim.Points);
                bool aligned = epfd.Length <= acc.NbBins && Math.Abs(acc.EpfdMin - epfd[0]) < 1e-6;
                if (!aligned)
                {
                    Console.WriteLine(string.Create(inv, $"{caseName,-9} | {victim,-6} | {headerVerdict,-6} | bins {epfd.Length} from {epfd[0]:F1} do not fit the row's accumulator ({acc.NbBins} from {acc.EpfdMin:F1}) -- skipped"));
                    continue;
                }
                var epfdFull = new double[acc.NbBins];
                var pctFull = new double[acc.NbBins];
                for (int i = 0; i < acc.NbBins; i++)
                {
                    epfdFull[i] = acc.EpfdMin + i * 0.1;
                    pctFull[i] = i < pct.Length ? pct[i] : 0.0;
                }
                Report(caseName, victim, headerVerdict, epfdFull, pctFull, acc, lim.Points, tol, inv, ref flips);
                scanned++;
            }
        }

        // Part B: the sweep-grid probe (BL-R3) has no per-victim CDFs on disk -- re-examine its
        // 10-degree grid, the grid the record calls compliant, at the record's depth.
        {
            string maskPath = Path.Combine(repo, "dataset", "BL-R3", "xml", "mask13_pfd_alpha_probe_sweep.xml");
            if (File.Exists(maskPath))
            {
                var con = new Constellation(DatasetGenerator.Shells);
                var mask = MaskFootprint.LoadFile(maskPath);
                var set = DatasetGenerator.SetFor(29, 900123479);
                double freqMhz = 0.5 * (set.LowFreqMhz + set.HighFreqMhz);
                var (step, steps, _) = ReadRuleProbes.Depth(false);
                for (double lat = -70.0; lat <= 70.0 + 1e-9; lat += 10.0)
                {
                    var v = ProbeExamination.Examine(con, mask, set, limD1, freqMhz, lat, ReadRuleProbes.EsLonDeg, ReadRuleProbes.GsoLonDeg, step, steps);
                    var acc = new EpfdAccumulator(limD1.Points);
                    Report("BL-R3", string.Create(inv, $"lat{lat:+0;-0}"), v.Pass ? "PASS" : "FAIL", v.Epfd, v.Pct, acc, limD1.Points, tol, inv, ref flips);
                    scanned++;
                }
            }
            else Console.WriteLine("BL-R3     | (mask XML not on disk -- sweep grid not re-examined)");
        }

        Console.WriteLine();
        Console.WriteLine(string.Create(inv, $"{scanned} victims scanned; {flips} expected verdict(s) would flip under the curve rule at {tol:F2} dB tolerance."));
        return 0;
    }

    private static void Report(string caseName, string victim, string recordVerdict, double[] epfd, double[] pct,
        EpfdAccumulator acc, List<radlimits.LimitPoint> points, double tol, CultureInfo inv, ref int flips)
    {
        double worstPoint = points.Min(l => ComplianceViewModel.MarginDb(epfd, pct, l.EPFD, l.Perc));
        bool pointPass = worstPoint >= 0.0;
        var viol = Scan(epfd, pct, acc.BuildLinearizedLimit(points), points, tol);
        bool flip = pointPass && viol is not null;
        if (flip) flips++;
        string scanText = viol is null ? "no crossing"
            : string.Create(inv, $"crosses at {viol.EpfdDb:F1} dB: {viol.CalcPerc:G4}% vs allowed {viol.AllowedPerc:G4}% (limit {viol.LimitPerc:G4}%), x{viol.Ratio:F2}");
        Console.WriteLine(string.Create(inv, $"{caseName,-9} | {victim,-6} | {recordVerdict,-6} | {(pointPass ? "PASS" : "FAIL")} {worstPoint,+5:0.0} | {scanText,-56} | {(flip ? "FLIP" : "-")}"));
    }

    /// <summary>The Bureau-side rule, mirrored: worst bin by calc/allowed over the tabulated span, curve-zero bins skipped.</summary>
    private static Violation? Scan(double[] epfd, double[] pct, double[] curve, List<radlimits.LimitPoint> points, double tolDb)
    {
        double first = points.Min(p => p.EPFD), last = points.Max(p => p.EPFD);
        double shift = tolDb / 0.1;
        Violation? worst = null;
        double worstRatio = 1.0;
        for (int i = 0; i < curve.Length; i++)
        {
            if (epfd[i] < first - 1e-9 || epfd[i] > last + 1e-9) continue;
            double limitHere = curve[i];
            if (limitHere <= 0.0) continue;
            double allowed = limitHere;
            if (shift > 0.0 && i > 0 && curve[i - 1] > 0.0)
                allowed = limitHere * Math.Pow(curve[i - 1] / limitHere, shift);
            double calc = pct[i];
            if (calc <= allowed) continue;
            double ratio = calc / allowed;
            if (ratio > worstRatio)
            {
                worstRatio = ratio;
                worst = new Violation(Math.Round(epfd[i], 1), calc, allowed, limitHere, ratio);
            }
        }
        return worst;
    }

    private static (double[] Epfd, double[] Pct, string Verdict) ReadCdf(string path)
    {
        var e = new List<double>(); var p = new List<double>();
        string verdict = "?";
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#"))
            {
                int k = line.IndexOf("verdict=", StringComparison.Ordinal);
                if (k >= 0) verdict = line.Substring(k + "verdict=".Length).Trim().Split(' ')[0];
                continue;
            }
            var parts = line.Split(',');
            if (parts.Length < 2) continue;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) continue;   // the header row
            e.Add(x);
            p.Add(double.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture));
        }
        return (e.ToArray(), p.ToArray(), verdict);
    }
}
