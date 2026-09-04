using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using radians.beamlab;
using radians.beamlab.app;

/// <summary>
/// Collects compliance progress reports; optionally echoes them to the
/// console. Synchronous on purpose (Progress&lt;T&gt; posts asynchronously,
/// which a check cannot assert on).
/// </summary>
internal sealed class ProgressCollector : IProgress<ComplianceViewModel.SweepProgress>
{
    private readonly bool _echo;
    private readonly Stopwatch _since = Stopwatch.StartNew();
    private double _lastEchoSec = -1;
    public List<(string Text, double Fraction)> Reports { get; } = new();

    public ProgressCollector(bool echo = false) => _echo = echo;

    public void Report(ComplianceViewModel.SweepProgress value)
    {
        Reports.Add((value.Text, value.Fraction));
        if (!_echo) return;
        // Latitude/walk headlines always; the "% of steps" ticks at most once a second.
        bool tick = value.Text.Contains("% of ");
        if (tick && _since.Elapsed.TotalSeconds - _lastEchoSec < 1.0) return;
        _lastEchoSec = _since.Elapsed.TotalSeconds;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  [{value.Fraction * 100,5:F1}%] {value.Text}"));
    }
}

/// <summary>
/// Headless compliance loop: the same Sweep / RunSweep / Advise the window
/// calls, driven from case files, with the progress reports echoed. This is
/// how a long sweep on a large constellation is verified without clicking --
/// and how the loop's numbers reach a document.
///
/// Run:  dotnet run --project tests/radians.beamlab.checks -- loop
///           [profile.json] [design.json] [days] [stepSec] [latFrom] [latTo] [latStep] [walk]
/// Defaults: the STEAM-2 case files, 0.1 d at 60 s, latitudes 0..60 step 10,
/// no advisor walk ("walk" as the last argument adds it).
/// </summary>
internal static class ComplianceLoop
{
    public static int Run(string profilePath, string designPath, double days, double stepSec,
        double latFrom, double latTo, double latStep, bool walk)
    {
        var inv = CultureInfo.InvariantCulture;
        var t0 = Stopwatch.StartNew();
        string repo = Directory.Exists(@"C:\Projects\radians.beamlab")
            ? @"C:\Projects\radians.beamlab" : AppContext.BaseDirectory;
        if (!File.Exists(profilePath) || !File.Exists(designPath))
        {
            Console.WriteLine("ABORT: profile or design document not found: " + profilePath + " / " + designPath);
            return 2;
        }

        var prof = OperationProfileCodec.Load(File.ReadAllText(profilePath));
        var doc = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(designPath));
        var shells = doc.Shells.Select(OrbitDesignFileCodec.ToShell).ToArray();
        double altKm = shells[0].OperatingHeightKm ?? shells[0].AltitudeKm;
        double freqMhz = prof.Down.FrequencyGhz * 1000.0;

        // ---- The limits: real Article 22 rows from the BR database ----------
        string[] limitsDbs =
        {
            @"C:\Projects\_EPFD\epfd-reference\Cases\EPFD_limits_RES85_WRC23.mdb",
            @"C:\Projects\_EPFD\radians\radians\Resources\EPFD_limits_RES85_WRC23.mdb",
        };
        string[] dllDirs =
        {
            @"C:\Projects\_EPFD\radians\radians\dlls",
            @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
        };
        string? limitsDb = limitsDbs.FirstOrDefault(File.Exists);
        string? dllDir = dllDirs.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdLimitsApi64.dll")));
        if (limitsDb is null || dllDir is null)
        {
            Console.WriteLine("ABORT: the BR limits database or EpfdLimitsApi64.dll is not present -- the loop needs real limits.");
            return 2;
        }
        LimitsDbReader.DllDirectory = dllDir;
        var limRows = LimitsDbReader.Read(limitsDb, freqMhz - 0.02, freqMhz + 0.02, prof.Down.RefBwKHz, altKm);
        var lim = limRows.Where(l => !l.ShortTermLatDependent && l.Points.Count > 0 && l.Rf_diam is not null)
            .OrderByDescending(l => l.Service == "FSS")
            .ThenBy(l => l.Rf_diam!.Value)
            .FirstOrDefault();
        if (lim is null)
        {
            Console.WriteLine(string.Create(inv, $"ABORT: no plain (non-lat-dependent) limit row at {freqMhz:F0} MHz down in the database."));
            return 2;
        }
        double dishM = lim.Rf_diam!.Value;
        var limitPoints = lim.Points.ToList();

        long steps = (long)Math.Round(days * 86400.0 / stepSec);
        var sweep = new ComplianceViewModel.Sweep(shells, prof,
            EsLon: 0.0, GsoOffset: 10.0, DishM: dishM,
            LatFrom: latFrom, LatTo: latTo, LatStep: latStep,
            Steps: steps, StepSec: stepSec, Limits: limitPoints);

        Console.WriteLine(string.Create(inv, $"profile: {prof.Name}"));
        Console.WriteLine(string.Create(inv,
            $"system: {shells.Length} shell(s), {new Constellation(shells).SatelliteCount} satellites; min elev {prof.MinElevDeg:F0} deg, alpha {prof.AlphaExclDeg:F1} deg, Nco {prof.NcoPerCell}, selection {prof.TrackingPolicy}, footprint {prof.Down.FootprintSource}"));
        Console.WriteLine("limit row: " + ComplianceViewModel.DescribeLimit(lim));
        Console.WriteLine(string.Create(inv,
            $"sweep: lat {latFrom:F0}..{latTo:F0} step {latStep:F0}; {steps} steps of {stepSec:F0} s ({days:F3} d) per latitude; resolvable percentile floor {100.0 / steps:F3}%"));

        // ---- The sweep at the profile's own alpha ---------------------------
        var col = new ProgressCollector(echo: true);
        var rows = ComplianceViewModel.RunSweep(sweep, prof.AlphaExclDeg, col);
        Console.WriteLine();
        Console.WriteLine("lat | max epfd | worst margin | verdict | quiet steps");
        foreach (var r in rows)
            Console.WriteLine(string.Create(inv,
                $"{r.LatDeg,4:F0} | {r.MaxEpfdDb,9:F1} | {r.WorstMarginDb,+9:F1} | {(r.Pass ? "PASS" : "FAIL"),4} | {r.QuietSteps}"));
        double worstAll = rows.Min(r => r.WorstMarginDb);
        string headroom = prof.Down.FootprintSource != "mask" && double.IsFinite(worstAll)
            ? string.Create(inv, $" -- power headroom {worstAll:+0.0;-0.0} dB on per-beam TxEirpDbw (dB-for-dB)")
            : "";
        Console.WriteLine(ComplianceViewModel.SummarizeRows(rows) + headroom);

        // ---- Optional: the exclusion walk -----------------------------------
        ComplianceViewModel.Advice? advice = null;
        if (walk)
        {
            Console.WriteLine();
            Console.WriteLine(string.Create(inv, $"advisor: walking the exclusion angle from {prof.AlphaExclDeg:F1} deg..."));
            var colW = new ProgressCollector(echo: true);
            advice = ComplianceViewModel.Advise(sweep, 1.0, Math.Max(prof.AlphaExclDeg + 10.0, 30.0), colW);
            Console.WriteLine(advice.FoundAlpha is double a
                ? string.Create(inv, $"advisor: compliant at alpha {a:F1} deg after {advice.Iterations} sweep(s)")
                : string.Create(inv, $"advisor: NOT compliant at the cap after {advice.Iterations} sweep(s); worst {advice.WorstMarginEndDb:+0.0;-0.0} dB at lat {advice.WorstLatEndDeg:F0}; ")
                  + ComplianceViewModel.TrendText(advice.WorstMarginStartDb, advice.WorstMarginEndDb));
        }

        // ---- The record ------------------------------------------------------
        var sb = new StringBuilder();
        string stem = prof.Name.Split('(')[0].Trim();
        string safe = new string(stem.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray()).Trim('-');
        sb.AppendLine($"# Compliance loop: {prof.Name}");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"*Produced by `dotnet run --project tests/radians.beamlab.checks -- loop \"{Path.GetFileName(profilePath)}\" \"{Path.GetFileName(designPath)}\" {days} {stepSec:F0} {latFrom:F0} {latTo:F0} {latStep:F0}{(walk ? " walk" : "")}`.*"));
        sb.AppendLine(string.Create(inv, $"*Date: {DateTime.Now:yyyy-MM-dd}. Wall clock {t0.Elapsed.TotalMinutes:F1} min.*"));
        sb.AppendLine();
        sb.AppendLine("## The system under test");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"- Shell(s): {shells.Length}, {new Constellation(shells).SatelliteCount} satellites at {altKm:F0} km / inclination {shells[0].InclinationDeg:F1} deg."));
        sb.AppendLine(string.Create(inv, $"- Enforced rules: minimum elevation {prof.MinElevDeg:F0} deg, exclusion alpha {prof.AlphaExclDeg:F1} deg, Nco {prof.NcoPerCell}, selection {prof.TrackingPolicy}."));
        sb.AppendLine(string.Create(inv, $"- Footprint source: {prof.Down.FootprintSource}{(prof.Down.FootprintSource == "mask" ? " (" + Path.GetFileName(prof.Down.MaskXmlPath) + ")" : " (live beam composition -- the truth)")}."));
        sb.AppendLine(string.Create(inv, $"- Victim: earth station at longitude 0, GSO satellite +10 deg, dish {dishM:F2} m (the limit row's own reference diameter)."));
        sb.AppendLine();
        sb.AppendLine("## The limit");
        sb.AppendLine();
        sb.AppendLine("- " + ComplianceViewModel.DescribeLimit(lim));
        sb.AppendLine("- Points (epfd dB / % of time): " + string.Join(", ", limitPoints.Select(p => string.Create(inv, $"{p.EPFD}@{p.Perc:G6}"))));
        sb.AppendLine();
        sb.AppendLine("## Verdicts");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"Depth: {steps} steps of {stepSec:F0} s per latitude ({days:F3} d) -- resolvable percentile floor {100.0 / steps:F3}%, so short-term points below that floor are located, not decided, at this depth."));
        sb.AppendLine();
        sb.AppendLine("| latitude | max epfd (dB) | worst margin (dB) | verdict | quiet steps |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine(string.Create(inv, $"| {r.LatDeg:F0} | {r.MaxEpfdDb:F1} | {r.WorstMarginDb:+0.0;-0.0} | {(r.Pass ? "PASS" : "FAIL")} | {r.QuietSteps} |"));
        sb.AppendLine();
        sb.AppendLine("**" + ComplianceViewModel.SummarizeRows(rows) + headroom + "**");
        if (advice is not null)
        {
            sb.AppendLine();
            sb.AppendLine("## The exclusion walk");
            sb.AppendLine();
            sb.AppendLine(advice.FoundAlpha is double a2
                ? string.Create(inv, $"Compliant at alpha **{a2:F1} deg** after {advice.Iterations} sweep(s).")
                : string.Create(inv, $"NOT compliant at the cap after {advice.Iterations} sweep(s): worst margin {advice.WorstMarginStartDb:+0.0;-0.0} -> {advice.WorstMarginEndDb:+0.0;-0.0} dB at latitude {advice.WorstLatEndDeg:F0} -- ")
                  + ComplianceViewModel.TrendText(advice.WorstMarginStartDb, advice.WorstMarginEndDb) + ".");
        }
        string outPath = Path.Combine(repo, "docs", $"compliance-{safe}.md");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine("figure: " + Path.GetRelativePath(repo, outPath));
        Console.WriteLine(string.Create(inv, $"progress reports: {col.Reports.Count}; wall clock {t0.Elapsed.TotalMinutes:F1} min"));
        return 0;
    }
}
