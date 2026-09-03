using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using radians.beamlab;
using static radians.beamlab.GeoMath;

// External oracle for the geometry-and-selection chain (simulation-debate Q4):
// WP 4A Document 4A/653 (United Kingdom, May 2022) published a fully
// specified simulation -- constellation STEAM-2, eligibility elevation >= 40
// deg AND alpha >= 22 deg, selection at RANDOM among the eligible satellites,
// 1e6 steps of 1 s -- and the CDF of the selected satellite's alpha at six
// latitudes, digitised in 5 deg bins. The proposal (an alpha table) was
// rejected; the physics is unaffected. Nothing in beamlab's vendored files
// covers scheduling and selection, so this is the one external check of
// that half of the chain.
//
// What runs here: beamlab's own Scheduler computes the feasible set per step
// against a declared parameter set carrying exactly the two gates (header
// elev_angle 40, min_exclude 22 for all orbits); the oracle then draws one
// eligible satellite uniformly at random (the document's rule) and records
// its alpha. Beams are made irrelevant on purpose -- one nadir beam per
// satellite and a covering radius larger than any footprint -- because the
// oracle has no beam concept: only propagation, geometry and the gates are
// under test. The document's second constellation (L5) gives a cheaper
// eligible-count check (3-8 satellites eligible at 50 N at any step) that
// separates a phasing fault from a selection fault, so it runs first.
//
// Run:  dotnet run --project tests/radians.beamlab.checks -- oracle [steps] [stepSec]
// Output: docs/oracle-steam2.md (+ the console summary). The harness check
// V35 calls Measure() on a short comb to pin the agreement permanently.
internal static class Oracle
{
    // Published CDF of the selected satellite's alpha, 4A/653 attachment
    // (verified symmetric: -k deg equals +k deg). Rows: alpha thresholds;
    // columns: 0, 10, 20, 30, 40, 50 N.
    internal static readonly double[] Thresholds =
        { 22, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 100 };
    internal static readonly double[] Latitudes = { 0, 10, 20, 30, 40, 50 };
    internal static readonly double[,] Published =
    {
        { 0,     0,     0,     0,     0,     0     },
        { 0.127, 0.121, 0.094, 0.055, 0.042, 0.036 },
        { 0.345, 0.303, 0.211, 0.149, 0.114, 0.102 },
        { 0.544, 0.470, 0.320, 0.244, 0.188, 0.176 },
        { 0.739, 0.626, 0.428, 0.338, 0.265, 0.257 },
        { 0.906, 0.760, 0.538, 0.433, 0.345, 0.348 },
        { 1,     0.871, 0.649, 0.526, 0.424, 0.459 },
        { 1,     0.945, 0.754, 0.614, 0.502, 0.602 },
        { 1,     1,     0.840, 0.699, 0.580, 0.722 },
        { 1,     1,     0.917, 0.783, 0.657, 0.821 },
        { 1,     1,     0.979, 0.860, 0.732, 0.906 },
        { 1,     1,     1,     0.925, 0.805, 0.9806},
        { 1,     1,     1,     0.975, 0.872, 1     },
        { 1,     1,     1,     1,     0.932, 1     },
        { 1,     1,     1,     1,     0.977, 1     },
        { 1,     1,     1,     1,     1,     1     },
    };

    /// <summary>Everything one oracle run measures; Run() writes it, V35 asserts on it.</summary>
    internal sealed record Result(
        long Steps, double StepSec, long L5Steps,
        int L5Min, int L5Max, double L5Mean, double L5FracInside,
        double[,] SimCdf, double[] MaxDev, double[] MeanEligible, long[] Outage,
        double MsPerStep)
    {
        public bool L5Agrees => L5Min >= 3 && L5Max <= 8;
        public double WorstDev => MaxDev.Max();
    }

    /// <summary>One nadir beam per satellite: the oracle has no beam concept.</summary>
    private sealed class NadirPointing : IBeamPointing
    {
        private readonly ISinglePattern _pattern = new Rec1528_1p4(30.0, 5.0);
        public ResolvedBeamSet Resolve(SatelliteState state)
        {
            var nadir = (new Vec3(0, 0, 0) - state.PositionEcefKm).Normalized();
            return new ResolvedBeamSet(new[] { new Beam("nadir", nadir, _pattern) }, new[] { 0.0 });
        }
    }

    private static OperatingParamsSet Gates(double minElevDeg, double alphaDeg)
    {
        var p = new OperatingParamsSet { ElevAngleHeaderDeg = minElevDeg, MaxCoFreqHeader = 1 };
        var ex = new MinExcludeByOrbit { OrbId = 0 };
        ex.ByLat.Add((0.0, alphaDeg));
        p.MinExclude.Add(ex);
        return p;
    }

    /// <summary>
    /// The measurement itself: the L5 eligible count at 50 N, then the STEAM-2
    /// alpha CDF of a uniformly drawn eligible satellite at 0..50 N, both on
    /// beamlab's own scheduler gates. Deterministic (seed 4653).
    /// </summary>
    internal static Result Measure(long steps, double stepSec, long l5Steps, bool progress)
    {
        var inv = CultureInfo.InvariantCulture;
        double simDur = steps * stepSec;

        // ---- 1. L5: 1200 km, 87.9 deg, 18 x 40, phase 4.5 deg, plane spacing
        // 10.5 deg (LAN span 18 x 10.5); gates elev 45 / alpha 8.4; stated
        // result 3-8 eligible at 50 N at any step.
        var l5 = new Constellation(new[] { new ConstellationShell
        {
            AltitudeKm = 1200.0, InclinationDeg = 87.9, PlaneCount = 18, SatsPerPlane = 40,
            InterPlanePhaseDeg = 4.5, LanSpreadDeg = 18 * 10.5,
        } });
        double l5Dur = l5Steps * stepSec;
        var l5Geo = new ServiceGeography(new[] { new ServiceCell(1, 50.0, 0.0) }, 500.0);
        var l5Sched = new Scheduler(l5, l5Geo, Gates(45.0, 8.4), new NadirPointing(), l5Dur, 5000.0);
        int l5Min = int.MaxValue, l5Max = 0; long l5Sum = 0, l5In = 0;
        if (progress)
            Console.WriteLine(string.Create(inv, $"L5 eligible-count check: {l5.SatelliteCount} satellites, {l5Steps} steps of {stepSec:F0} s at 50 N..."));
        for (long k = 0; k < l5Steps; k++)
        {
            var st = l5Sched.Step(k * stepSec);
            int n = st.CandidateLinks.Select(c => c.SatelliteNumber).Distinct().Count();
            l5Min = Math.Min(l5Min, n); l5Max = Math.Max(l5Max, n); l5Sum += n;
            if (n >= 3 && n <= 8) l5In++;
        }
        double l5Mean = (double)l5Sum / l5Steps, l5Frac = (double)l5In / l5Steps;
        if (progress)
            Console.WriteLine(string.Create(inv,
                $"L5: eligible per step min {l5Min} / mean {l5Mean:F2} / max {l5Max}; inside [3, 8] on {100 * l5Frac:F2}% of steps -> {(l5Min >= 3 && l5Max <= 8 ? "AGREES" : "DISAGREES")} with 4A/653 (3-8)"));

        // ---- 2. STEAM-2: 1150 km, 53 deg, 32 x 50, phase 1.9 deg (exact),
        // plane spacing 11.25 deg; gates elev 40 / alpha 22; random selection.
        var steam = new Constellation(new[] { new ConstellationShell
        {
            AltitudeKm = 1150.0, InclinationDeg = 53.0, PlaneCount = 32, SatsPerPlane = 50,
            InterPlanePhaseDeg = 1.9, LanSpreadDeg = 360.0,
        } });
        var cells = Latitudes.Select((lat, i) => new ServiceCell(i + 1, lat, 0.0)).ToList();
        var geo = new ServiceGeography(cells, 500.0);
        var sched = new Scheduler(steam, geo, Gates(40.0, 22.0), new NadirPointing(), simDur, 5000.0);
        var rng = new Random(4653);
        int nl = Latitudes.Length;
        var counts = new long[nl, Thresholds.Length];   // selected alpha <= threshold
        var samples = new long[nl];
        var outage = new long[nl];
        var eligSum = new long[nl];
        if (progress)
            Console.WriteLine(string.Create(inv,
                $"STEAM-2 alpha CDF: {steam.SatelliteCount} satellites, {steps} steps of {stepSec:F0} s, random selection among eligible, latitudes 0..50 N..."));
        var cal = Stopwatch.StartNew();
        double msPerStep = 0.0;
        for (long k = 0; k < steps; k++)
        {
            var st = sched.Step(k * stepSec);
            for (int li = 0; li < nl; li++)
            {
                int cellId = li + 1;
                var elig = st.CandidateLinks.Where(c => c.CellId == cellId)
                    .GroupBy(c => c.SatelliteNumber).Select(g => g.First()).ToList();
                eligSum[li] += elig.Count;
                if (elig.Count == 0) { outage[li]++; continue; }
                var pick = elig[rng.Next(elig.Count)];
                samples[li]++;
                for (int ti = 0; ti < Thresholds.Length; ti++)
                    if (pick.AlphaDeg <= Thresholds[ti]) counts[li, ti]++;
            }
            if (k == 199)
            {
                msPerStep = cal.Elapsed.TotalMilliseconds / 200.0;
                if (progress)
                    Console.WriteLine(string.Create(inv, $"  ~{msPerStep:F1} ms/step -> about {msPerStep * steps / 60000.0:F0} min total"));
            }
            else if (progress && k > 0 && k % 100000 == 0)
                Console.WriteLine(string.Create(inv, $"  step {k}/{steps}"));
        }
        if (msPerStep == 0.0 && steps > 0) msPerStep = cal.Elapsed.TotalMilliseconds / steps;

        var simCdf = new double[Thresholds.Length, nl];
        var maxDev = new double[nl];
        var meanElig = new double[nl];
        for (int li = 0; li < nl; li++)
        {
            meanElig[li] = (double)eligSum[li] / Math.Max(1, steps);
            for (int ti = 0; ti < Thresholds.Length; ti++)
            {
                double sim = samples[li] > 0 ? (double)counts[li, ti] / samples[li] : double.NaN;
                simCdf[ti, li] = sim;
                if (!double.IsNaN(sim)) maxDev[li] = Math.Max(maxDev[li], Math.Abs(sim - Published[ti, li]));
            }
        }
        return new Result(steps, stepSec, l5Steps, l5Min, l5Max, l5Mean, l5Frac, simCdf, maxDev, meanElig, outage, msPerStep);
    }

    public static int Run(long steps, double stepSec)
    {
        var inv = CultureInfo.InvariantCulture;
        var t0 = Stopwatch.StartNew();
        string repo = Directory.Exists(@"C:\Projects\radians.beamlab")
            ? @"C:\Projects\radians.beamlab" : AppContext.BaseDirectory;
        var r = Measure(steps, stepSec, Math.Min(steps, 21600), progress: true);
        int nl = Latitudes.Length;

        Console.WriteLine("STEAM-2 max |CDF deviation| per latitude: "
            + string.Join(", ", Latitudes.Select((lat, li) => string.Create(inv, $"{lat:F0}N {r.MaxDev[li]:0.000}"))));
        Console.WriteLine("mean eligible satellites per step: "
            + string.Join(", ", Latitudes.Select((lat, li) => string.Create(inv, $"{lat:F0}N {r.MeanEligible[li]:F1}")))
            + "; outage steps: " + string.Join(", ", r.Outage.Select(o => o.ToString(inv))));
        double worst = r.WorstDev;
        string verdict = worst <= 0.03 ? "AGREES (within digitisation and sampling noise)"
            : worst <= 0.06 ? "CLOSE (systematic offsets to inspect)" : "DISAGREES (diagnose: phasing, gate or alpha metric)";
        Console.WriteLine(string.Create(inv, $"VERDICT: worst max deviation {worst:0.000} -> {verdict}"));

        // ---- The record ------------------------------------------------------
        var sb = new StringBuilder();
        sb.AppendLine("# External oracle: WP 4A Doc 4A/653 (STEAM-2 alpha CDF, L5 eligible count)");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"*Produced by `dotnet run --project tests/radians.beamlab.checks -- oracle {steps} {stepSec:F0}`.*"));
        sb.AppendLine(string.Create(inv, $"*Date: {DateTime.Now:yyyy-MM-dd}. Wall clock {t0.Elapsed.TotalMinutes:F1} min ({r.MsPerStep:F1} ms/step).*"));
        sb.AppendLine();
        sb.AppendLine("## What is tested");
        sb.AppendLine();
        sb.AppendLine("The half of the chain the vendored radians files do not cover: propagation");
        sb.AppendLine("of a full Walker shell into sky positions, the alpha geometry at a ground");
        sb.AppendLine("point, the joint eligibility gate (elevation AND alpha), and the selection");
        sb.AppendLine("step. The eligible set per step is beamlab's own Scheduler's (declared set:");
        sb.AppendLine("header elev_angle = min elevation, min_exclude = the alpha floor for all");
        sb.AppendLine("orbits); the selection is the document's rule -- one eligible satellite drawn");
        sb.AppendLine("uniformly at random per step (seed 4653). Beams are irrelevant by construction");
        sb.AppendLine("(one nadir beam per satellite, covering radius 5000 km): the document has no");
        sb.AppendLine("beam concept. Nothing about power, masks, composition, the accumulator or the");
        sb.AppendLine("limits is touched.");
        sb.AppendLine();
        sb.AppendLine("The source is one team's published simulation, not a reference implementation:");
        sb.AppendLine("a disagreement means someone is wrong, not necessarily beamlab -- but the setup");
        sb.AppendLine("is specified tightly enough that a disagreement is diagnosable.");
        sb.AppendLine();
        sb.AppendLine("## L5 -- the eligible-count check (runs first)");
        sb.AppendLine();
        sb.AppendLine("1200 km, 87.9 deg, 18 planes x 40 satellites, inter-plane phase 4.5 deg, plane");
        sb.AppendLine("spacing 10.5 deg; minimum elevation 45 deg, GSO avoidance 8.4 deg. Stated result:");
        sb.AppendLine("between 3 and 8 satellites meet the eligibility criteria at 50 N at any time step.");
        sb.AppendLine("This separates a constellation-phasing fault from a selection fault, since the");
        sb.AppendLine("count does not depend on selection at all.");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"- Measured over {r.L5Steps} steps of {stepSec:F0} s: eligible per step **min {r.L5Min} / mean {r.L5Mean:F2} / max {r.L5Max}**; inside [3, 8] on {100 * r.L5FracInside:F2}% of steps."));
        sb.AppendLine(string.Create(inv, $"- **{(r.L5Agrees ? "AGREES" : "DISAGREES")}** with 4A/653."));
        sb.AppendLine();
        sb.AppendLine("## STEAM-2 -- the alpha CDF of the selected satellite");
        sb.AppendLine();
        sb.AppendLine("1150 km, 53 deg, 32 planes x 50 satellites, inter-plane phase 1.9 deg (exact, not");
        sb.AppendLine("an integer Walker F), plane spacing 11.25 deg (11.3 in the document); eligible =");
        sb.AppendLine("elevation >= 40 deg and alpha >= 22 deg; selection at random; test latitudes 0-50 N");
        sb.AppendLine(string.Create(inv, $"at longitude 0. Run here: {steps} steps of {stepSec:F0} s ({steps * stepSec / 86400.0:F2} d); the document's run was 1e6 x 1 s."));
        sb.AppendLine();
        sb.AppendLine("Columns per latitude: published CDF | simulated CDF (fraction of steps whose selected");
        sb.AppendLine("satellite has alpha <= the row threshold).");
        sb.AppendLine();
        sb.AppendLine("| alpha (deg) | 0N pub | 0N sim | 10N pub | 10N sim | 20N pub | 20N sim | 30N pub | 30N sim | 40N pub | 40N sim | 50N pub | 50N sim |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        for (int ti = 0; ti < Thresholds.Length; ti++)
        {
            var cellsTxt = new StringBuilder();
            for (int li = 0; li < nl; li++)
                cellsTxt.Append(string.Create(inv, $" {Published[ti, li]:0.000} | {r.SimCdf[ti, li]:0.000} |"));
            sb.AppendLine(string.Create(inv, $"| {Thresholds[ti]:F0} |") + cellsTxt);
        }
        sb.AppendLine();
        sb.AppendLine("| latitude | max abs CDF deviation | mean eligible / step | outage steps |");
        sb.AppendLine("|---|---|---|---|");
        for (int li = 0; li < nl; li++)
            sb.AppendLine(string.Create(inv, $"| {Latitudes[li]:F0} N | {r.MaxDev[li]:0.000} | {r.MeanEligible[li]:F1} | {r.Outage[li]} |"));
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"**Verdict: worst max deviation {worst:0.000} -- {verdict}.** The published table is digitised to 5 deg bins and 3 decimals; sampling noise at these sample counts is well below 0.01, so deviations are dominated by digitisation and by any true geometric difference."));
        sb.AppendLine();
        sb.AppendLine("## Reading it");
        sb.AppendLine();
        sb.AppendLine("- Agreement at every latitude validates propagation, the alpha metric, the joint gate");
        sb.AppendLine("  and the candidate enumeration the scheduler feeds every policy from.");
        sb.AppendLine("- A latitude-dependent offset with the L5 count correct points at the alpha geometry");
        sb.AppendLine("  (arc sampling or ES frame), not at the constellation.");
        sb.AppendLine("- An L5 count outside 3-8 points at the constellation build (phase, plane spacing,");
        sb.AppendLine("  inclination convention) before anything downstream is blamed.");
        sb.AppendLine("- The check harness pins this agreement (V35) on a short comb so a selection-side");
        sb.AppendLine("  regression cannot move a margin figure unseen.");
        File.WriteAllText(Path.Combine(repo, "docs", "oracle-steam2.md"), sb.ToString());
        Console.WriteLine("figure: docs/oracle-steam2.md");
        return 0;
    }
}
