using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
        double latFrom, double latTo, double latStep, bool walk, bool minimise = false)
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
        string stem = prof.Name.Split('(')[0].Trim();
        // Everything one loop run produces lands together, so profile, R set
        // and mask are read back as one set rather than reassembled by hand.
        // The name comes from the app, so the designer looks in the same place.
        string safe = ComplianceViewModel.RunName(prof);
        string runDir = ComplianceViewModel.RunDir(repo, prof);
        Directory.CreateDirectory(runDir);

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

        // ---- Position 1: the derivation probe (saturated, no victim) --------
        // A declaration is an envelope of what the system MAY do, so it is
        // measured with traffic taken out and with no victim present. Being a
        // different run from the truth sweep below, it also keeps E1 >= T an
        // adequacy test rather than a tautology: a set derived from the very
        // run that later verifies it would envelope that run trivially.
        Console.WriteLine();
        Console.WriteLine(string.Create(inv,
            $"derivation probe: saturated, no victim; {steps} steps of {stepSec:F0} s, latitude band {latStep:F0} deg..."));
        var probe = ComplianceViewModel.Saturate(prof,
            OperationComposer.Compose(prof, altKm).Enforced);
        var derived = ComplianceViewModel.DeriveDeclared(shells, prof,
            steps * stepSec, stepSec, latBandDeg: latStep,
            // A set governs ONE band. The composition spans up and down
            // together, which is right for the gates and wrong for a
            // declaration, so the downlink set carries the downlink band.
            lowFreqMhz: freqMhz, highFreqMhz: freqMhz);
        Console.WriteLine(string.Create(inv,
            $"  demand {prof.DemandLinksPerCell} -> {probe.DemandLinksPerCell}, activity {prof.ActivityFactor:F2} -> {probe.ActivityFactor:F2}, "
            + $"duty {prof.IlluminationDutyCycle:F2} -> {probe.IlluminationDutyCycle:F2}, operating fraction {prof.OperationalFraction:F2} -> {probe.OperationalFraction:F2}"));
        Console.WriteLine(string.Create(inv,
            $"  {derived.Steps} steps / {derived.LinkSamples} link samples -> {DescribeSet(derived.Set, inv)}"));

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
        // The reachable-envelope mask at one grid, exported once and cached by
        // that grid: the mask depends on the composition, which v3 holds fixed,
        // so the same grid never needs exporting twice in a session.
        string EnsureMask(double maskLatStepDeg, double azElStepDeg)
        {
            string tag = string.Create(inv, $"lat{maskLatStepDeg:F1}-ae{azElStepDeg:F1}").Replace(".", "p");
            string path = Path.Combine(runDir, string.Create(inv, $"{safe}.mask.{tag}.xml"));
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > File.GetLastWriteTimeUtc(profilePath))
            {
                Console.WriteLine("  reusing the exported reachable-envelope mask: " + Path.GetFileName(path));
                return path;
            }
            var compMask = OperationComposer.Compose(prof, altKm);
            double maxLat = MaskXmlExport.MaxLatitudeForInclination(shells[0].InclinationDeg);
            double latMax = Math.Floor(maxLat / maskLatStepDeg) * maskLatStepDeg;
            var opts = new MaskXmlExportOptions
            {
                SatName = stem, NtcId = 0, MaskId = 1,
                LowFreqMhz = freqMhz, HighFreqMhz = freqMhz, RefBwKHz = prof.Down.RefBwKHz,
                LatMinDeg = -latMax, LatMaxDeg = latMax, LatStepDeg = maskLatStepDeg,
                BStepDeg = azElStepDeg, CStepDeg = azElStepDeg,
                Kind = MaskPlotKind.AzEl, Format = MaskExportFormat.Xml,
                OutputPath = path,
            };
            Console.WriteLine(string.Create(inv,
                $"  exporting the reachable-envelope mask: lat {-latMax:F0}..{latMax:F0} step {maskLatStepDeg:F1}, az/el {azElStepDeg:F1} deg..."));
            int lastPct = -25;
            var maskProgress = new Progress<double>(p =>
            {
                int pct = (int)(p * 100);
                if (pct >= lastPct + 25) { lastPct = pct; Console.WriteLine($"    export {pct}%"); }
            });
            MaskXmlExport.GenerateAsync(new ReachableEnvelopeSampler(compMask.Scene, opts, maxLat),
                opts, maskProgress, CancellationToken.None).GetAwaiter().GetResult();
            return path;
        }

        // One examination sweep: the declared mask read against a declared R set.
        List<ComplianceRow> Examine(OperatingParamsSet declared, string maskPath,
            IProgress<ComplianceViewModel.SweepProgress>? p)
            => ComplianceViewModel.RunSweepProfile(
                sweep with { Declared = declared },
                prof with
                {
                    AlphaByLat = null,
                    Downlink = prof.Down with { FootprintSource = "mask", MaskXmlPath = maskPath },
                }, p);

        // ---- Position 3: the examination against the DERIVED declaration ----
        // E1 reads the declared pfd mask and the derived R set. The truth run
        // above keeps composing live, so T does not move -- the whole v3
        // objective is to bring E1 down onto a truth that stays put.
        List<ComplianceRow>? rowsE1 = null;
        string e1Note = "";
        string declaredMask = prof.Down.MaskXmlPath;
        if (declaredMask.Length == 0 || !File.Exists(declaredMask))
        {
            // No declared mask: export beamlab's own from the REACHABLE
            // envelope -- the ungated configuration space, which is the mask's
            // saturated counterpart to the R set's saturated probe. It covers
            // every permitted beam position on a grid, including ones this
            // orbit never visits, so it is conservative by construction.
            declaredMask = EnsureMask(latStep, 1.0);
        }
        if (File.Exists(declaredMask))
        {
            Console.WriteLine();
            Console.WriteLine("examination sweep: declared mask + the derived R set (E1)...");
            // FootprintSource says what the TRUTH run composes; the mask path is
            // the DECLARATION. Only the examination reads the latter.
            var colE1 = new ProgressCollector(echo: true);
            rowsE1 = Examine(derived.Set, declaredMask, colE1);
            Console.WriteLine();
            Console.WriteLine("lat | T margin | E1 margin | gap | E1 >= T");
            for (int i = 0; i < rowsE1.Count; i++)
                Console.WriteLine(string.Create(inv,
                    $"{rows[i].LatDeg,4:F0} | {rows[i].WorstMarginDb,8:+0.0;-0.0} | {rowsE1[i].WorstMarginDb,9:+0.0;-0.0} | "
                    + $"{rows[i].WorstMarginDb - rowsE1[i].WorstMarginDb,5:F1} | {(rowsE1[i].WorstMarginDb <= rows[i].WorstMarginDb + 1e-9 ? "yes" : "NO")}"));
            Console.WriteLine(E1Summary(rows, rowsE1, inv));
        }
        else
        {
            e1Note = "E1 not computed: no pfd mask was declared and the reachable-envelope export did not produce one.";
            Console.WriteLine();
            Console.WriteLine(e1Note);
        }

        // ---- Optional: the granularity walk (minimise E1 over a fixed truth) -
        // The v3 objective. The truth does not move: these levers change what the
        // DECLARATION says, not what the system does -- the latitude band of the
        // derived rows, and the grid the mask is sampled on. Every dB taken off
        // E1 here is a dB of operating power the system may be licensed for, and
        // it is only admissible while E1 still sits at or above T.
        var grainRows = new List<(string Name, double WorstE1, double WidestGap, bool Adequate)>();
        if (minimise && rowsE1 is not null)
        {
            var grains = new (string Name, double LatBand, double MaskLat, double AzEl)[]
            {
                ("coarse", latStep, latStep, 2.0),
                ("baseline", latStep, latStep, 1.0),
                ("fine", latStep / 2.0, latStep / 2.0, 1.0),
            };
            Console.WriteLine();
            Console.WriteLine("granularity walk: minimising E1 over a fixed truth...");
            foreach (var g in grains)
            {
                Console.WriteLine(string.Create(inv,
                    $"  {g.Name}: lat band {g.LatBand:F1} deg, mask lat {g.MaskLat:F1} deg, az/el {g.AzEl:F1} deg"));
                var dg = ComplianceViewModel.DeriveDeclared(shells, prof, steps * stepSec, stepSec,
                    latBandDeg: g.LatBand, lowFreqMhz: freqMhz, highFreqMhz: freqMhz);
                var rg = Examine(dg.Set, EnsureMask(g.MaskLat, g.AzEl), null);
                double worst = rg.Min(r => r.WorstMarginDb);
                double widest = double.NegativeInfinity;
                bool adequate = true;
                for (int i = 0; i < rg.Count && i < rows.Count; i++)
                {
                    widest = Math.Max(widest, rows[i].WorstMarginDb - rg[i].WorstMarginDb);
                    if (rg[i].WorstMarginDb > rows[i].WorstMarginDb + 1e-9) adequate = false;
                }
                grainRows.Add((g.Name, worst, widest, adequate));
                Console.WriteLine(string.Create(inv,
                    $"    worst E1 {worst:+0.0;-0.0} dB, widest gap {widest:F1} dB, {(adequate ? "adequate" : "NOT ADEQUATE")}"));
            }
            var best = grainRows.Where(g => g.Adequate).OrderByDescending(g => g.WorstE1).FirstOrDefault();
            Console.WriteLine(best.Name is null
                ? "granularity walk: no candidate stayed adequate -- none of them enveloped the truth."
                : string.Create(inv,
                    $"granularity walk: best adequate candidate '{best.Name}' at worst E1 {best.WorstE1:+0.0;-0.0} dB "
                    + $"({grainRows[0].WorstE1 - best.WorstE1:F1} dB recovered against the coarsest)"));
        }

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
        sb.AppendLine();
        sb.AppendLine("## The declaration, derived");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"Measured on a SATURATED probe with no victim -- demand {prof.DemandLinksPerCell} -> {probe.DemandLinksPerCell}, "
            + $"activity {prof.ActivityFactor:F2} -> {probe.ActivityFactor:F2}, duty {prof.IlluminationDutyCycle:F2} -> {probe.IlluminationDutyCycle:F2}, "
            + $"operating fraction {prof.OperationalFraction:F2} -> {probe.OperationalFraction:F2}. A declaration is an envelope of what the "
            + $"system MAY do, so traffic is taken out before it is measured; and being a different run from the truth sweep, it keeps "
            + $"E1 >= T an adequacy test rather than a tautology."));
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"- Depth: {derived.Steps} steps / {derived.LinkSamples} link samples, latitude band {latStep:F0} deg."));
        sb.AppendLine("- Derived set: " + DescribeSet(derived.Set, inv));
        sb.AppendLine();
        sb.AppendLine("## E1 -- the examination against that declaration");
        sb.AppendLine();
        if (rowsE1 is null)
        {
            sb.AppendLine(e1Note);
        }
        else
        {
            sb.AppendLine("The truth above does not move; E1 is what the examination sees when it reads the declared "
                + "mask and the derived R set instead. The gap is the margin the declaration gives away -- and, at "
                + "the same dB-for-dB rate, the operating power the system could have been licensed for.");
            sb.AppendLine();
            sb.AppendLine("| latitude | T margin (dB) | E1 margin (dB) | gap (dB) | E1 >= T |");
            sb.AppendLine("|---|---|---|---|---|");
            for (int i = 0; i < rowsE1.Count; i++)
                sb.AppendLine(string.Create(inv,
                    $"| {rows[i].LatDeg:F0} | {rows[i].WorstMarginDb:+0.0;-0.0} | {rowsE1[i].WorstMarginDb:+0.0;-0.0} | "
                    + $"{rows[i].WorstMarginDb - rowsE1[i].WorstMarginDb:F1} | {(rowsE1[i].WorstMarginDb <= rows[i].WorstMarginDb + 1e-9 ? "yes" : "**NO**")} |"));
            sb.AppendLine();
            sb.AppendLine("**" + E1Summary(rows, rowsE1, inv) + "**");
        }
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
        if (grainRows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## The granularity walk");
            sb.AppendLine();
            sb.AppendLine("The truth does not move across these rows. Each one changes what the DECLARATION says "
                + "-- the latitude band of the derived R-set rows, and the grid the mask is sampled on -- never what "
                + "the system does. Every dB taken off E1 is a dB of operating power the system may be licensed for, "
                + "admissible only while E1 still sits at or above T.");
            sb.AppendLine();
            sb.AppendLine("| granularity | worst E1 (dB) | widest gap to T (dB) | E1 >= T |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var g in grainRows)
                sb.AppendLine(string.Create(inv,
                    $"| {g.Name} | {g.WorstE1:+0.0;-0.0} | {g.WidestGap:F1} | {(g.Adequate ? "yes" : "**NO**")} |"));
        }

        // ---- The artefacts, emitted together from this run ------------------
        string profOut = Path.Combine(runDir, safe + ".opprofile.json");
        string setOut = Path.Combine(runDir, safe + ".operparams.xml");
        File.WriteAllText(profOut, OperationProfileCodec.Save(prof));
        OperParamsXmlWriter.Write(setOut, derived.Set);
        // ...and in the designer's own format, so its "derive & fill" can LOAD
        // this run rather than simulate a second opinion of the same system.
        string setJson = ComplianceViewModel.RunSetJsonPath(repo, prof);
        File.WriteAllText(setJson, OpParamsFileCodec.Save(OpParamsFileCodec.FromSet(derived.Set)));
        sb.AppendLine();
        sb.AppendLine("## Artefacts");
        sb.AppendLine();
        sb.AppendLine("Profile, R set and pfd mask come out of this one run, so they describe the same system:");
        sb.AppendLine();
        sb.AppendLine("- Operation profile (the truth as run): `" + Path.GetRelativePath(repo, profOut) + "`");
        sb.AppendLine("- Derived R set (S.1503-4 Part B): `" + Path.GetRelativePath(repo, setOut) + "`");
        sb.AppendLine("- Derived R set, designer format (open with the operating-parameters designer): `"
            + Path.GetRelativePath(repo, setJson) + "`");
        sb.AppendLine(File.Exists(declaredMask)
            ? "- Declared pfd mask: `" + Path.GetRelativePath(repo, declaredMask) + "`"
            : "- Declared pfd mask: none");
        Console.WriteLine("artefacts: " + Path.GetRelativePath(repo, runDir));

        string outPath = Path.Combine(repo, "docs", $"compliance-{safe}.md");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine("figure: " + Path.GetRelativePath(repo, outPath));
        Console.WriteLine(string.Create(inv, $"progress reports: {col.Reports.Count}; wall clock {t0.Elapsed.TotalMinutes:F1} min"));
        return 0;
    }

    /// <summary>One-line rendering of a derived R set, for the console and the record.</summary>
    private static string DescribeSet(OperatingParamsSet p, CultureInfo inv)
    {
        var bits = new List<string>();
        var ex = p.MinExclude.FirstOrDefault(m => m.ByLat.Count > 0);
        bits.Add(ex is null ? "min_exclude none"
            : "min_exclude " + string.Join("/", ex.ByLat.Select(r =>
                string.Create(inv, $"{r.LatDeg:F0}:{r.AlphaDeg:F1}"))));
        bits.Add(p.MinElev.Count == 0 ? "min_elev none"
            : "min_elev " + string.Join("/", p.MinElev.Where(m => m.ByAz.Count > 0).Select(m =>
                string.Create(inv, $"{m.LatDeg:F0}:{m.ByAz[0].ElevDeg:F1}"))));
        bits.Add(p.MaxCoFreqByLat.Count == 0 ? "max_co_freq none"
            : "max_co_freq " + string.Join("/", p.MaxCoFreqByLat.Select(r =>
                string.Create(inv, $"{r.LatDeg:F0}:{r.Value}"))));
        bits.Add(string.Create(inv, $"max_co_freq_sat {p.MaxCoFreqSat?.ToString(inv) ?? "-"}"));
        bits.Add(string.Create(inv,
            $"min_angle es {p.MinAngleAtEsDeg?.ToString("F1", inv) ?? "-"} / sat {p.MinAngleAtSatDeg?.ToString("F1", inv) ?? "-"}"));
        bits.Add(string.Create(inv, $"es_lat {p.EsLatMinDeg:F0}..{p.EsLatMaxDeg:F0}"));
        return string.Join("; ", bits);
    }

    /// <summary>
    /// The acceptance statement: E1 must sit at or above T everywhere, or the
    /// derivation did not envelope the system it describes -- which is a
    /// granularity failure of the probe, not a compliance failure of the system.
    /// </summary>
    private static string E1Summary(IReadOnlyList<ComplianceRow> t,
        IReadOnlyList<ComplianceRow> e1, CultureInfo inv)
    {
        var below = new List<double>();
        for (int i = 0; i < e1.Count && i < t.Count; i++)
            if (e1[i].WorstMarginDb > t[i].WorstMarginDb + 1e-9) below.Add(t[i].LatDeg);
        double worstGap = double.NegativeInfinity;
        for (int i = 0; i < e1.Count && i < t.Count; i++)
            worstGap = Math.Max(worstGap, t[i].WorstMarginDb - e1[i].WorstMarginDb);
        string gap = string.Create(inv, $"widest gap {worstGap:F1} dB");
        string lats = string.Join(", ", below.Select(l => l.ToString("F0", inv)));
        return below.Count == 0
            ? "ADEQUATE: E1 >= T at every latitude; " + gap
            : "ADEQUACY FAILURE: E1 sits BELOW T at latitude(s) " + lats
                + " -- the probe did not envelope the system, so the declaration is not conservative; " + gap;
    }
}
