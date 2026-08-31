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

// The first margin figure (simulation-debate Q6, operator-approved
// 2026-08-31): one engineering profile, truth in full, its declarations
// derived as envelopes, and the same victim run on one comb through both
// projections -- the live composition (truth) and the declared mask + R
// set read the examination's way (S.1503-4 D5.1.4.1). The differenced
// CDFs are the projection margin: what the examination's view of the
// filing adds on top of what the system actually does.
//
// Run:  dotnet run --project tests/radians.beamlab.checks -- margin
//
// Decisions in force: global exclusion angle only (the per-latitude
// mask-inheritance component is absent by construction); linear advisor
// walk; limits from the BR database, never hand-guessed. Artefacts land
// in dataset/margin/ (gitignored); the figure itself in
// docs/margin-figure.md.
internal static class MarginFigure
{
    public static int Run()
    {
        var inv = CultureInfo.InvariantCulture;
        var t0 = Stopwatch.StartNew();
        string repo = Directory.Exists(@"C:\Projects\radians.beamlab")
            ? @"C:\Projects\radians.beamlab" : AppContext.BaseDirectory;
        string outDir = Path.Combine(repo, "dataset", "margin");
        Directory.CreateDirectory(outDir);
        Console.WriteLine("margin figure -- artefacts: " + outDir);

        // ---- 1. The limit: real Article 22 rows from the BR database ----
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
        string limitsDb = limitsDbs.FirstOrDefault(File.Exists);
        string dllDir = dllDirs.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdLimitsApi64.dll")));
        if (limitsDb is null || dllDir is null)
        {
            Console.WriteLine("ABORT: the BR limits database or EpfdLimitsApi64.dll is not present -- the figure needs real limits.");
            return 2;
        }
        LimitsDbReader.DllDirectory = dllDir;
        var rows = LimitsDbReader.Read(limitsDb, 19700.0 - 0.02, 19700.0 + 0.02, 40.0, 1200.0);
        // The smallest-dish plain FSS row: the tightest long-term table of
        // the band. Its reference dish IS the victim antenna -- the
        // examination pairs each row with its rf_diam.
        var lim = rows.Where(l => !l.ShortTermLatDependent && l.Points.Count > 0 && l.Rf_diam is not null)
            .OrderByDescending(l => l.Service == "FSS")
            .ThenBy(l => l.Rf_diam!.Value)
            .FirstOrDefault();
        if (lim is null)
        {
            Console.WriteLine("ABORT: no plain (non-lat-dependent) limit row for 19.7 GHz down in the database.");
            return 2;
        }
        double dishM = lim.Rf_diam!.Value;
        var limitPoints = lim.Points.ToList();
        Console.WriteLine($"limit row: {ComplianceViewModel.DescribeLimit(lim)}");
        Console.WriteLine("limit points (epfd dB / % time it may be exceeded): "
            + string.Join(", ", limitPoints.Select(p => string.Create(inv, $"{p.EPFD}@{p.Perc:G6}"))));

        // ---- 2. The system, on engineering grounds -----------------------
        // The session-canonical shell (1200 km / 53 deg, 3x4 Walker) and a
        // profile of explicit defaults: S.1528-1 Annex-2 Taylor payload,
        // 19.7 GHz / 40 kHz, min elev 10, service 30-60N +/-20E at 450 km
        // cells, highest-elevation tracking, full activity, no duty
        // thinning. Exclusion starts at 0 -- it is the loop's output.
        var shells0 = new[] { new ConstellationShell
        {
            AltitudeKm = 1200.0, InclinationDeg = 53.0,
            PlaneCount = 3, SatsPerPlane = 4, WalkerPhasingF = 1, NOrbits = 288,
        } };
        var prof0 = new OperationProfile(Name: "margin-baseline");
        double sceneAlt = 1200.0;

        // ---- 3. The advisor finds the declared exclusion (linear walk) ----
        Console.WriteLine("advisor: walking the global exclusion angle (sweep lat 0..70 step 10, 0.1 d at 60 s)...");
        var sweep = new ComplianceViewModel.Sweep(shells0, prof0,
            EsLon: 0.0, GsoOffset: 10.0, DishM: dishM,
            LatFrom: 0.0, LatTo: 70.0, LatStep: 10.0,
            Steps: (long)(0.1 * 86400.0 / 60.0), StepSec: 60.0, Limits: limitPoints);
        var advice = ComplianceViewModel.Advise(sweep, 1.0, 20.0);
        bool compliant = advice.FoundAlpha is not null;
        double alphaStar = advice.FoundAlpha ?? 20.0;
        var worstRow = advice.FinalRows.OrderBy(r => r.WorstMarginDb).First();
        Console.WriteLine(string.Create(inv,
            $"advisor: {(compliant ? "compliant" : "NOT compliant at the cap")} at alpha = {alphaStar:F1} deg after {advice.Iterations} sweep(s); "
            + $"worst latitude {worstRow.LatDeg:F0} (margin {worstRow.WorstMarginDb:F1} dB)")
            + (compliant ? "" : "; " + ComplianceViewModel.TrendText(
                advice.WorstMarginStartDb, advice.WorstMarginEndDb)));

        // ---- 4. The committed profile and its composition ----------------
        var prof = prof0 with { AlphaExclDeg = alphaStar };
        File.WriteAllText(Path.Combine(outDir, "margin-baseline.opprofile.json"),
            OperationProfileCodec.Save(prof));
        var comp = OperationComposer.Compose(prof, sceneAlt);
        var shells = OperationComposer.ApplyToShells(prof, shells0);
        var con = new Constellation(shells);

        // ---- 5. The R set: envelope of the flown operation ---------------
        Console.WriteLine("deriving the R set (0.5 d at 60 s, 10 deg bands)...");
        var derived = OpParamsDeriver.Derive(con, comp.Geography, comp.Enforced, comp.Scene,
            0.5 * 86400.0, 60.0, 10.0, "MARGIN", 1, 1, 19700.0, 19700.0,
            comp.Policy, comp.CoverageRadiusKm, comp.IlluminationDutyCycle);
        var rSet = derived.Set;
        File.WriteAllText(Path.Combine(outDir, "margin.opparams.json"),
            OpParamsFileCodec.Save(OpParamsFileCodec.FromSet(rSet)));
        OperParamsXmlWriter.Write(Path.Combine(outDir, "margin.rset.xml"), rSet);
        Console.WriteLine(string.Create(inv,
            $"R set: {derived.LinkSamples} link samples over {derived.Steps} steps; "
            + $"es_lat {rSet.EsLatMinDeg:F0}..{rSet.EsLatMaxDeg:F0}, min_elev rows {rSet.MinElev.Count}, "
            + $"exclusion rows {(rSet.MinExclude.Count > 0 ? rSet.MinExclude[0].ByLat.Count : 0)}, "
            + $"nco rows {rSet.MaxCoFreqByLat.Count}, max/sat {rSet.MaxCoFreqSat}"));

        // ---- 6. The mask: envelope of the payload, exclusion baked -------
        string maskPath = Path.Combine(outDir, "margin.mask.xml");
        var opts = new MaskXmlExportOptions
        {
            SatName = "MARGIN", NtcId = 1, MaskId = 1,
            LowFreqMhz = 19700.0, HighFreqMhz = 19700.0, RefBwKHz = 40.0,
            LatMinDeg = -53.0, LatMaxDeg = 53.0, LatStepDeg = 10.0,
            BStepDeg = 5.0, CStepDeg = 5.0,
            Kind = MaskPlotKind.AlphaDeltaLong, Format = MaskExportFormat.Xml,
            OutputPath = maskPath,
        };
        Console.WriteLine("exporting the alpha/deltaLongitude mask (lat -53..53 step 10, b/c 5 deg)...");
        int lastPct = -10;
        var progress = new Progress<double>(p =>
        {
            int pct = (int)(p * 100);
            if (pct >= lastPct + 10) { lastPct = pct; Console.WriteLine($"  mask export {pct}%"); }
        });
        MaskXmlExport.GenerateAsync(new ReachableEnvelopeSampler(comp.Scene, opts, 53.0),
            opts, progress, CancellationToken.None).GetAwaiter().GetResult();
        var mask = MaskFootprint.LoadFile(maskPath);
        Console.WriteLine($"mask: {mask.BlockCount} latitude blocks, kind {mask.Kind}");

        // ---- 7. One comb for all three runs, calibrated ------------------
        var victim = new EpfdDownVictim
        {
            EsLatDeg = worstRow.LatDeg, EsLonDeg = 0.0, GsoLonDeg = 10.0,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 19700.0, dishM),
        };
        const double stepSec = 60.0;
        var cal = Stopwatch.StartNew();
        EpfdDown.Run(con,
            new ScheduledPointing(con, comp.Geography, comp.Enforced, comp.Scene,
                50 * stepSec, comp.CoverageRadiusKm, comp.Policy, comp.IlluminationDutyCycle),
            victim, stepSec, 50, limitPoints, 50 * stepSec);
        cal.Stop();
        double msPerStep = cal.Elapsed.TotalMilliseconds / 50.0;
        long steps = Math.Clamp((long)(8 * 60 * 1000 / Math.Max(0.1, msPerStep)), 720, 2880);
        double simDur = steps * stepSec;
        Console.WriteLine(string.Create(inv,
            $"comb: {steps} steps of {stepSec:F0} s ({simDur / 86400.0:F2} d) at victim lat {victim.EsLatDeg:F0}; "
            + $"~{msPerStep:F0} ms/step measured; resolvable percentile floor {100.0 / steps:F3}%"));

        // ---- 8. The three runs -------------------------------------------
        Console.WriteLine("run T  (truth: live composition, scheduler-gated)...");
        var runT = EpfdDown.Run(con,
            new ScheduledPointing(con, comp.Geography, comp.Enforced, comp.Scene,
                simDur, comp.CoverageRadiusKm, comp.Policy, comp.IlluminationDutyCycle),
            victim, stepSec, steps, limitPoints, simDur);
        Console.WriteLine("run E1 (examination: declared mask + derived R set)...");
        var runE1 = EpfdDownMask.Run(con, mask, rSet, victim, stepSec, steps, limitPoints, simDur);
        Console.WriteLine("run E2 (examination: declared mask + profile rules)...");
        var runE2 = EpfdDownMask.Run(con, mask, comp.Enforced, victim, stepSec, steps, limitPoints, simDur);

        // ---- 9. The figure -----------------------------------------------
        var (epfdT, pctT) = runT.Accumulator.BuildCdf();
        var (epfdE1, pctE1) = runE1.Accumulator.BuildCdf();
        var (epfdE2, pctE2) = runE2.Accumulator.BuildCdf();
        WriteCdf(Path.Combine(outDir, "margin.T.csv"), epfdT, pctT);
        WriteCdf(Path.Combine(outDir, "margin.E1.csv"), epfdE1, pctE1);
        WriteCdf(Path.Combine(outDir, "margin.E2.csv"), epfdE2, pctE2);

        static bool Verdict(radcompute1503_2.EpfdAccumulator acc, List<radlimits.LimitPoint> lp)
        { var (p, _) = acc.CompareWithLimits(lp); return p.All(x => x); }
        bool passT = Verdict(runT.Accumulator, limitPoints);
        bool passE1 = Verdict(runE1.Accumulator, limitPoints);
        bool passE2 = Verdict(runE2.Accumulator, limitPoints);

        var sb = new StringBuilder();
        sb.AppendLine("# The first margin figure");
        sb.AppendLine();
        sb.AppendLine("*Produced by `dotnet run --project tests/radians.beamlab.checks -- margin`.*");
        sb.AppendLine(string.Create(inv, $"*Date: {2026:D4}-08-31. Wall clock {t0.Elapsed.TotalMinutes:F1} min.*"));
        sb.AppendLine();
        sb.AppendLine("## What is measured");
        sb.AppendLine();
        sb.AppendLine("The projection margin of docs/simulation-debate.md (Q6/Q8): the same");
        sb.AppendLine("fully specified system, the same victim, the same time comb -- computed");
        sb.AppendLine("once as the truth (the live scheduled beam composition) and once as the");
        sb.AppendLine("examination would compute the filing (declared PFD mask + declared R set");
        sb.AppendLine("through S.1503-4 D5.1.4.1). The difference is what the declaration");
        sb.AppendLine("granularity plus the examination's reading add on top of reality. It is");
        sb.AppendLine("NOT the margin to the Article 22 limit (that is the compliance loop's");
        sb.AppendLine("number and is minimised by design).");
        sb.AppendLine();
        sb.AppendLine("## The system (truth, in full)");
        sb.AppendLine();
        sb.AppendLine("- Shell: 1200 km / 53 deg, Walker 3 planes x 4 satellites, F = 1.");
        sb.AppendLine("- Payload: S.1528-1 sec. 1.4 Taylor scene defaults (SLR 20 dB, nbar 4),");
        sb.AppendLine("  19.7 GHz, 40 kHz reference bandwidth.");
        sb.AppendLine("- Operation: min elevation 10 deg, service 30-60 N / +/-20 E at 450 km");
        sb.AppendLine("  cells, highest-elevation tracking, demand 1 link/cell, full activity,");
        sb.AppendLine("  operational fraction 1, illumination duty 1.");
        sb.AppendLine(string.Create(inv,
            $"- Declared exclusion: global alpha = {alphaStar:F1} deg -- {(compliant ? "the advisor's smallest compliant angle" : "the advisor CAP (no compliant angle found up to 20 deg)")} "));
        sb.AppendLine(string.Create(inv, $"  after {advice.Iterations} linear sweep(s) against the limit row below."));
        if (!compliant)
            sb.AppendLine(string.Create(inv,
                $"  Walk trajectory: worst margin {advice.WorstMarginStartDb:+0.0;-0.0} -> {advice.WorstMarginEndDb:+0.0;-0.0} dB, ")
                + ComplianceViewModel.TrendText(advice.WorstMarginStartDb, advice.WorstMarginEndDb) + ".");
        sb.AppendLine();
        sb.AppendLine("## The declarations (derived from the truth, never fitted to the verdict)");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"- PFD mask: alpha/deltaLongitude, latitude table -53..53 step 10 (pinned), b/c 5 deg, exclusion baked ({mask.BlockCount} blocks) -- dataset/margin/margin.mask.xml"));
        sb.AppendLine(string.Create(inv,
            $"- R set: envelope of the flown operation, 10 deg latitude bands, {derived.LinkSamples} link samples -- dataset/margin/margin.rset.xml"));
        sb.AppendLine();
        sb.AppendLine("## The limit (from the BR database, not hand-guessed)");
        sb.AppendLine();
        sb.AppendLine($"- {ComplianceViewModel.DescribeLimit(lim)}");
        sb.AppendLine("- Points (epfd dB(W/m2/40kHz) / % of time it may be exceeded): "
            + string.Join(", ", limitPoints.Select(p => string.Create(inv, $"{p.EPFD}@{p.Perc:G6}"))));
        sb.AppendLine();
        sb.AppendLine("## The victim and the comb");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"- GSO ES at lat {victim.EsLatDeg:F0} / lon 0 (the sweep's worst-margin latitude), wanted GSO at lon 10, S.1428 {dishM:F2} m (the limit row's reference dish)."));
        sb.AppendLine(string.Create(inv,
            $"- One shared comb for all three runs: {steps} steps of {stepSec:F0} s ({simDur / 86400.0:F2} d); resolvable percentile floor {100.0 / steps:F3}%."));
        sb.AppendLine();
        sb.AppendLine("## The three runs");
        sb.AppendLine();
        sb.AppendLine("| run | projection | gates | max epfd (dB) | quiet steps | verdict vs the row |");
        sb.AppendLine("|---|---|---|---|---|---|");
        sb.AppendLine(string.Create(inv, $"| T | live composition (occurring) | profile rules, scheduler-enforced | {runT.MaxEpfdDb:F2} | {runT.QuietSteps} | {(passT ? "PASS" : "FAIL")} |"));
        sb.AppendLine(string.Create(inv, $"| E1 | declared mask, D5.1.4.1 | derived R set (the filing) | {runE1.MaxEpfdDb:F2} | {runE1.QuietSteps} | {(passE1 ? "PASS" : "FAIL")} |"));
        sb.AppendLine(string.Create(inv, $"| E2 | declared mask, D5.1.4.1 | profile-composed rules | {runE2.MaxEpfdDb:F2} | {runE2.QuietSteps} | {(passE2 ? "PASS" : "FAIL")} |"));
        sb.AppendLine();
        sb.AppendLine("## The margin, point by point");
        sb.AppendLine();
        sb.AppendLine("Measured epfd at each limit percentage (dB); margin = limit - measured");
        sb.AppendLine("(positive = room). The projection margin is E1 - T: the conservatism the");
        sb.AppendLine("examination's view of the filing adds. E2 - E1 names the R-set derivation");
        sb.AppendLine("component (measured envelope vs declared rules).");
        sb.AppendLine();
        sb.AppendLine("| limit point (dB @ %) | T epfd | E1 epfd | E2 epfd | T margin | E1 margin | E1-T (projection) | E2-E1 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        double worstProj = double.NegativeInfinity, worstProjPerc = double.NaN;
        foreach (var p in limitPoints.OrderBy(p => p.Perc))
        {
            double mT = ComplianceViewModel.MarginDb(epfdT, pctT, p.EPFD, p.Perc);
            double mE1 = ComplianceViewModel.MarginDb(epfdE1, pctE1, p.EPFD, p.Perc);
            double mE2 = ComplianceViewModel.MarginDb(epfdE2, pctE2, p.EPFD, p.Perc);
            double vT = p.EPFD - mT, vE1 = p.EPFD - mE1, vE2 = p.EPFD - mE2;
            double proj = vE1 - vT;
            if (proj > worstProj) { worstProj = proj; worstProjPerc = p.Perc; }
            sb.AppendLine(string.Create(inv,
                $"| {p.EPFD} @ {p.Perc:G6} | {vT:F2} | {vE1:F2} | {vE2:F2} | {mT:F2} | {mE1:F2} | {proj:F2} | {vE2 - vE1:F2} |"));
        }
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"**Headline: the projection margin (E1 - T) is {worstProj:F2} dB at its largest limit point ({worstProjPerc}% of time); "
            + $"max-epfd difference {runE1.MaxEpfdDb - runT.MaxEpfdDb:F2} dB.**"));
        sb.AppendLine();
        sb.AppendLine("## Named caveats and knobs (the granularity study starts here)");
        sb.AppendLine();
        sb.AppendLine("- Single victim geometry: the sweep's worst latitude at one ES longitude");
        sb.AppendLine("  and one GSO offset. The worst-case geometry handshake with the");
        sb.AppendLine("  examination side is pending; a GSO-offset sweep is the tracked next");
        sb.AppendLine("  exploration axis.");
        sb.AppendLine("- Global exclusion only, by decision: the per-latitude mask-inheritance");
        sb.AppendLine("  component is absent from this figure by construction.");
        sb.AppendLine("- Payload power budget: contingent, unmodelled (the truth-side");
        sb.AppendLine("  measurement is tracked separately); power control is in the model.");
        sb.AppendLine(string.Create(inv,
            $"- Sampling: percentiles finer than {100.0 / steps:F3}% are not resolved on this comb;"));
        sb.AppendLine("  the deepest-event wobble study says tail agreement is bin-class.");
        sb.AppendLine("- Declaration granularity knobs measurable next: mask latitude step and");
        sb.AppendLine("  b/c grid, R-set latitude banding, per-latitude alpha rows.");
        sb.AppendLine("- epfd(is)/(up) are out of scope here (down only).");
        sb.AppendLine("- The 100% limit point reads the accumulator's lowest bin (range floor");
        sb.AppendLine("  = min limit - 100 dB), so its margin is a range artefact, not a");
        sb.AppendLine("  measurement. The examination runs are quiet more often than the truth");
        sb.AppendLine("  (exclusion switches satellites off entirely where the composition");
        sb.AppendLine("  still radiates sidelobes) yet peak louder (the mask envelope plus its");
        sb.AppendLine("  bin granularity exceed any instantaneous composite) -- both faithful.");
        sb.AppendLine();
        sb.AppendLine("CDFs: dataset/margin/margin.{T,E1,E2}.csv (epfd dB, % time exceeded).");
        File.WriteAllText(Path.Combine(repo, "docs", "margin-figure.md"), sb.ToString(), new UTF8Encoding(false));

        Console.WriteLine();
        Console.WriteLine(string.Create(inv,
            $"HEADLINE: projection margin E1-T = {worstProj:F2} dB at {worstProjPerc}%; "
            + $"max-epfd E1-T = {runE1.MaxEpfdDb - runT.MaxEpfdDb:F2} dB; "
            + $"T {(passT ? "PASS" : "FAIL")} / E1 {(passE1 ? "PASS" : "FAIL")} / E2 {(passE2 ? "PASS" : "FAIL")} vs {lim.RrRef}"));
        Console.WriteLine("figure: docs/margin-figure.md");
        return 0;
    }

    // ------------------------------------------------------------------
    // The self-study (operator has no real payload numbers yet): make the
    // tool derive the envelope compliance requires. The per-beam transmit
    // power density (profile TxEirpDbw, dBW in the 40 kHz reference
    // bandwidth; the composite adds the pattern gain on top) is the one
    // knob that moves every epfd dB for dB -- sweep it downward, locate
    // the compliance frontier against the same TABLE 22-1C row, take the
    // margin figure at a compliant point with a BINDING exclusion, and
    // measure the mask-grid granularity component the first figure
    // flagged (b/c 5 deg vs 2 deg).
    //
    // Run:  dotnet run --project tests/radians.beamlab.checks -- study
    // ------------------------------------------------------------------
    public static int Study()
    {
        var inv = CultureInfo.InvariantCulture;
        var t0 = Stopwatch.StartNew();
        string repo = Directory.Exists(@"C:\Projects\radians.beamlab")
            ? @"C:\Projects\radians.beamlab" : AppContext.BaseDirectory;
        string outDir = Path.Combine(repo, "dataset", "margin");
        Directory.CreateDirectory(outDir);
        Console.WriteLine("payload envelope study -- artefacts: " + outDir);

        // The same limit pick as the figure.
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
        string limitsDb = limitsDbs.FirstOrDefault(File.Exists);
        string dllDir = dllDirs.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdLimitsApi64.dll")));
        if (limitsDb is null || dllDir is null)
        { Console.WriteLine("ABORT: limits database or EpfdLimitsApi64.dll not present."); return 2; }
        LimitsDbReader.DllDirectory = dllDir;
        var rows = LimitsDbReader.Read(limitsDb, 19700.0 - 0.02, 19700.0 + 0.02, 40.0, 1200.0);
        var lim = rows.Where(l => !l.ShortTermLatDependent && l.Points.Count > 0 && l.Rf_diam is not null)
            .OrderByDescending(l => l.Service == "FSS").ThenBy(l => l.Rf_diam!.Value).FirstOrDefault();
        if (lim is null) { Console.WriteLine("ABORT: no plain limit row."); return 2; }
        double dishM = lim.Rf_diam!.Value;
        var limitPoints = lim.Points.ToList();
        Console.WriteLine($"limit row: {ComplianceViewModel.DescribeLimit(lim)}");

        var shells0 = new[] { new ConstellationShell
        {
            AltitudeKm = 1200.0, InclinationDeg = 53.0,
            PlaneCount = 3, SatsPerPlane = 4, WalkerPhasingF = 1, NOrbits = 288,
        } };
        double sceneAlt = 1200.0;

        OperationProfile ProfAt(double eirpDbw, double alphaDeg) => new(
            Name: string.Create(inv, $"study-eirp{eirpDbw:F0}"),
            Downlink: new DownlinkProfile(TxEirpDbw: eirpDbw),
            AlphaExclDeg: alphaDeg);
        ComplianceViewModel.Sweep SweepAt(OperationProfile p) => new(shells0, p,
            EsLon: 0.0, GsoOffset: 10.0, DishM: dishM,
            LatFrom: 0.0, LatTo: 70.0, LatStep: 10.0,
            Steps: (long)(0.1 * 86400.0 / 60.0), StepSec: 60.0, Limits: limitPoints);

        double gm = OperationComposer.Compose(ProfAt(0.0, 0.0), sceneAlt).Scene.GmDbi;
        Console.WriteLine(string.Create(inv,
            $"anchor: per-beam peak gain {gm:F1} dBi (scene default) -- per-beam boresight e.i.r.p. density = power density + {gm:F1} dB"));

        // ---- 1. The compliance frontier at alpha = 0 ---------------------
        var eirps = new[] { 0.0, -10.0, -20.0, -30.0, -40.0 };
        var frontier = new List<(double Eirp, double Worst, double Lat, bool Pass)>();
        foreach (double e in eirps)
        {
            var r0 = ComplianceViewModel.RunSweep(SweepAt(ProfAt(e, 0.0)), 0.0);
            var w = r0.OrderBy(r => r.WorstMarginDb).First();
            bool ok = r0.All(r => r.Pass);
            frontier.Add((e, w.WorstMarginDb, w.LatDeg, ok));
            Console.WriteLine(string.Create(inv,
                $"frontier: power {e,4:F0} dBW/40kHz (boresight eirp {e + gm:F1}) -> worst margin {w.WorstMarginDb:+0.0;-0.0} dB at lat {w.LatDeg:F0}, {(ok ? "PASS" : "FAIL")} at alpha 0"));
        }
        int idx0 = frontier.FindIndex(f => f.Pass);
        if (idx0 < 0)
        {
            Console.WriteLine("no compliant power density down to -40 dBW/40kHz at alpha 0 -- frontier only, no figure point.");
            return 2;
        }

        // ---- 2. A binding-exclusion point: the louder neighbour ----------
        double pointEirp = frontier[idx0].Eirp, pointAlpha = 0.0;
        ComplianceViewModel.Advice adviceL = null;
        if (idx0 > 0)
        {
            double eL = frontier[idx0 - 1].Eirp;
            Console.WriteLine(string.Create(inv, $"advisor at power {eL:F0} dBW/40kHz (walking alpha 0..20 by 1)..."));
            adviceL = ComplianceViewModel.Advise(SweepAt(ProfAt(eL, 0.0)), 1.0, 20.0);
            if (adviceL.FoundAlpha is double aL)
            { pointEirp = eL; pointAlpha = aL; }
            Console.WriteLine(string.Create(inv,
                $"advisor: {(adviceL.FoundAlpha is double af ? $"compliant at alpha = {af:F1} deg" : "no compliant alpha up to 20 deg")} after {adviceL.Iterations} sweep(s); worst margin {adviceL.WorstMarginStartDb:+0.0;-0.0} -> {adviceL.WorstMarginEndDb:+0.0;-0.0} dB, ")
                + ComplianceViewModel.TrendText(adviceL.WorstMarginStartDb, adviceL.WorstMarginEndDb));
        }
        Console.WriteLine(string.Create(inv,
            $"figure point: power {pointEirp:F0} dBW/40kHz, declared alpha {pointAlpha:F1} deg"));

        // ---- 3. The margin figure at the point ---------------------------
        var prof = ProfAt(pointEirp, pointAlpha);
        File.WriteAllText(Path.Combine(outDir, "study.opprofile.json"), OperationProfileCodec.Save(prof));
        var comp = OperationComposer.Compose(prof, sceneAlt);
        var con = new Constellation(OperationComposer.ApplyToShells(prof, shells0));
        var rowsP = ComplianceViewModel.RunSweep(SweepAt(prof), pointAlpha);
        double victimLat = rowsP.OrderBy(r => r.WorstMarginDb).First().LatDeg;

        Console.WriteLine("deriving the R set (0.5 d at 60 s, 10 deg bands)...");
        var derived = OpParamsDeriver.Derive(con, comp.Geography, comp.Enforced, comp.Scene,
            0.5 * 86400.0, 60.0, 10.0, "STUDY", 1, 1, 19700.0, 19700.0,
            comp.Policy, comp.CoverageRadiusKm, comp.IlluminationDutyCycle);
        File.WriteAllText(Path.Combine(outDir, "study.opparams.json"),
            OpParamsFileCodec.Save(OpParamsFileCodec.FromSet(derived.Set)));
        OperParamsXmlWriter.Write(Path.Combine(outDir, "study.rset.xml"), derived.Set);

        MaskFootprint ExportMask(double bcStep, string path)
        {
            var o = new MaskXmlExportOptions
            {
                SatName = "STUDY", NtcId = 1, MaskId = 1,
                LowFreqMhz = 19700.0, HighFreqMhz = 19700.0, RefBwKHz = 40.0,
                LatMinDeg = -53.0, LatMaxDeg = 53.0, LatStepDeg = 10.0,
                BStepDeg = bcStep, CStepDeg = bcStep,
                Kind = MaskPlotKind.AlphaDeltaLong, Format = MaskExportFormat.Xml,
                OutputPath = path,
            };
            Console.WriteLine(string.Create(inv, $"exporting mask at b/c {bcStep:F0} deg..."));
            int last = -20;
            var pr = new Progress<double>(p =>
            { int pc = (int)(p * 100); if (pc >= last + 20) { last = pc; Console.WriteLine($"  {pc}%"); } });
            MaskXmlExport.GenerateAsync(new ReachableEnvelopeSampler(comp.Scene, o, 53.0),
                o, pr, CancellationToken.None).GetAwaiter().GetResult();
            return MaskFootprint.LoadFile(path);
        }
        var m5 = ExportMask(5.0, Path.Combine(outDir, "study.mask.bc5.xml"));
        var m2 = ExportMask(2.0, Path.Combine(outDir, "study.mask.bc2.xml"));

        var victim = new EpfdDownVictim
        {
            EsLatDeg = victimLat, EsLonDeg = 0.0, GsoLonDeg = 10.0,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 19700.0, dishM),
        };
        const double stepSec = 60.0;
        var cal = Stopwatch.StartNew();
        EpfdDown.Run(con, new ScheduledPointing(con, comp.Geography, comp.Enforced, comp.Scene,
            50 * stepSec, comp.CoverageRadiusKm, comp.Policy, comp.IlluminationDutyCycle),
            victim, stepSec, 50, limitPoints, 50 * stepSec);
        cal.Stop();
        long steps = Math.Clamp((long)(8 * 60 * 1000 / Math.Max(0.1, cal.Elapsed.TotalMilliseconds / 50.0)), 720, 2880);
        double simDur = steps * stepSec;
        Console.WriteLine(string.Create(inv,
            $"comb: {steps} steps of 60 s at victim lat {victimLat:F0}; percentile floor {100.0 / steps:F3}%"));

        Console.WriteLine("run T  (truth)...");
        var runT = EpfdDown.Run(con, new ScheduledPointing(con, comp.Geography, comp.Enforced,
            comp.Scene, simDur, comp.CoverageRadiusKm, comp.Policy, comp.IlluminationDutyCycle),
            victim, stepSec, steps, limitPoints, simDur);
        Console.WriteLine("run E1 (mask bc5 + derived R)...");
        var runE1 = EpfdDownMask.Run(con, m5, derived.Set, victim, stepSec, steps, limitPoints, simDur);
        Console.WriteLine("run E2 (mask bc5 + profile rules)...");
        var runE2 = EpfdDownMask.Run(con, m5, comp.Enforced, victim, stepSec, steps, limitPoints, simDur);
        Console.WriteLine("run E1f (mask bc2 + derived R)...");
        var runE1f = EpfdDownMask.Run(con, m2, derived.Set, victim, stepSec, steps, limitPoints, simDur);

        var (eT, pT) = runT.Accumulator.BuildCdf();
        var (eE1, pE1) = runE1.Accumulator.BuildCdf();
        var (eE2, pE2) = runE2.Accumulator.BuildCdf();
        var (eE1f, pE1f) = runE1f.Accumulator.BuildCdf();
        WriteCdf(Path.Combine(outDir, "study.T.csv"), eT, pT);
        WriteCdf(Path.Combine(outDir, "study.E1.csv"), eE1, pE1);
        WriteCdf(Path.Combine(outDir, "study.E2.csv"), eE2, pE2);
        WriteCdf(Path.Combine(outDir, "study.E1fine.csv"), eE1f, pE1f);
        static bool Pass(radcompute1503_2.EpfdAccumulator a, List<radlimits.LimitPoint> lp)
        { var (p, _) = a.CompareWithLimits(lp); return p.All(x => x); }

        // ---- 4. The study document ---------------------------------------
        var sb = new StringBuilder();
        sb.AppendLine("# Payload envelope study");
        sb.AppendLine();
        sb.AppendLine("*Produced by `dotnet run --project tests/radians.beamlab.checks -- study`.*");
        sb.AppendLine(string.Create(inv, $"*Date: 2026-08-31. Wall clock {t0.Elapsed.TotalMinutes:F1} min.*"));
        sb.AppendLine();
        sb.AppendLine("No real system parameters exist yet, so this study answers the inverse");
        sb.AppendLine("question: what payload envelope does TABLE 22-1C compliance REQUIRE of");
        sb.AppendLine("the baseline system? The knob is the per-beam transmit power density");
        sb.AppendLine("(dBW in the 40 kHz reference bandwidth; the S.1528-1 pattern gain rides");
        sb.AppendLine("on top), which moves every epfd dB for dB. The system is otherwise the");
        sb.AppendLine("first margin figure's baseline (docs/margin-figure.md): 1200 km / 53 deg");
        sb.AppendLine("Walker 3x4, 19.7 GHz, min elev 10, service 30-60 N / +/-20 E, 450 km");
        sb.AppendLine("cells, full activity. When the real numbers arrive they replace this");
        sb.AppendLine("envelope; nothing here is fitted to the verdict -- the frontier IS the");
        sb.AppendLine("deliverable.");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"Anchor: scene per-beam peak gain {gm:F1} dBi, so boresight e.i.r.p. density = power density + {gm:F1} dB."));
        sb.AppendLine();
        sb.AppendLine("## The compliance frontier (alpha = 0, sweep lat 0-70)");
        sb.AppendLine();
        sb.AppendLine("| power density (dBW/40kHz) | boresight e.i.r.p. (dBW/40kHz) | worst margin (dB) | at lat | verdict |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var f in frontier)
            sb.AppendLine(string.Create(inv,
                $"| {f.Eirp:F0} | {f.Eirp + gm:F1} | {f.Worst:+0.0;-0.0} | {f.Lat:F0} | {(f.Pass ? "PASS" : "FAIL")} |"));
        sb.AppendLine();
        if (adviceL is not null)
        {
            sb.AppendLine(string.Create(inv,
                $"Advisor at the louder neighbour ({frontier[idx0 - 1].Eirp:F0} dBW/40kHz): "
                + $"{(adviceL.FoundAlpha is double af2 ? string.Create(inv, $"compliant at alpha = {af2:F1} deg") : "no compliant alpha up to 20 deg")} "
                + $"after {adviceL.Iterations} sweep(s); worst margin {adviceL.WorstMarginStartDb:+0.0;-0.0} -> {adviceL.WorstMarginEndDb:+0.0;-0.0} dB, ")
                + ComplianceViewModel.TrendText(adviceL.WorstMarginStartDb, adviceL.WorstMarginEndDb) + ".");
            sb.AppendLine();
        }
        sb.AppendLine(string.Create(inv,
            $"**Figure point: power density {pointEirp:F0} dBW/40kHz (boresight e.i.r.p. {pointEirp + gm:F1}), declared alpha {pointAlpha:F1} deg.**"));
        sb.AppendLine();
        sb.AppendLine("## The margin figure at the point");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"Victim: GSO ES lat {victimLat:F0} / lon 0 (worst sweep latitude at the point), GSO lon 10, "
            + $"S.1428 {dishM:F2} m; comb {steps} x 60 s (floor {100.0 / steps:F3}%). Declarations: "
            + $"alpha/deltaLong mask lat -53..53 step 10, R set 10-deg bands ({derived.LinkSamples} samples)."));
        sb.AppendLine();
        sb.AppendLine("| run | what | max epfd (dB) | quiet | verdict |");
        sb.AppendLine("|---|---|---|---|---|");
        sb.AppendLine(string.Create(inv, $"| T | truth (live composition) | {runT.MaxEpfdDb:F2} | {runT.QuietSteps} | {(Pass(runT.Accumulator, limitPoints) ? "PASS" : "FAIL")} |"));
        sb.AppendLine(string.Create(inv, $"| E1 | mask b/c 5 deg + derived R | {runE1.MaxEpfdDb:F2} | {runE1.QuietSteps} | {(Pass(runE1.Accumulator, limitPoints) ? "PASS" : "FAIL")} |"));
        sb.AppendLine(string.Create(inv, $"| E2 | mask b/c 5 deg + profile rules | {runE2.MaxEpfdDb:F2} | {runE2.QuietSteps} | {(Pass(runE2.Accumulator, limitPoints) ? "PASS" : "FAIL")} |"));
        sb.AppendLine(string.Create(inv, $"| E1f | mask b/c 2 deg + derived R | {runE1f.MaxEpfdDb:F2} | {runE1f.QuietSteps} | {(Pass(runE1f.Accumulator, limitPoints) ? "PASS" : "FAIL")} |"));
        sb.AppendLine();
        sb.AppendLine("| limit point (dB @ %) | T | E1 | E1f | E1-T (projection, 5 deg) | E1f-T (2 deg) | grid component E1-E1f | E2-E1 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        double worstProj5 = double.NegativeInfinity, worstProj2 = double.NegativeInfinity;
        foreach (var p in limitPoints.OrderBy(p => p.Perc))
        {
            double vT = p.EPFD - ComplianceViewModel.MarginDb(eT, pT, p.EPFD, p.Perc);
            double vE1 = p.EPFD - ComplianceViewModel.MarginDb(eE1, pE1, p.EPFD, p.Perc);
            double vE2 = p.EPFD - ComplianceViewModel.MarginDb(eE2, pE2, p.EPFD, p.Perc);
            double vE1f = p.EPFD - ComplianceViewModel.MarginDb(eE1f, pE1f, p.EPFD, p.Perc);
            worstProj5 = Math.Max(worstProj5, vE1 - vT);
            worstProj2 = Math.Max(worstProj2, vE1f - vT);
            sb.AppendLine(string.Create(inv,
                $"| {p.EPFD} @ {p.Perc:G6} | {vT:F2} | {vE1:F2} | {vE1f:F2} | {vE1 - vT:F2} | {vE1f - vT:F2} | {vE1 - vE1f:F2} | {vE2 - vE1:F2} |"));
        }
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"**Headline: at the compliant point the projection margin is {worstProj5:F2} dB with the 5-deg mask grid "
            + $"and {worstProj2:F2} dB with the 2-deg grid (max-epfd E1-T {runE1.MaxEpfdDb - runT.MaxEpfdDb:F2} / E1f-T {runE1f.MaxEpfdDb - runT.MaxEpfdDb:F2} dB) -- "
            + $"the grid step accounts for {runE1.MaxEpfdDb - runE1f.MaxEpfdDb:F2} dB of it at max-epfd.**"));
        sb.AppendLine();
        sb.AppendLine("Caveats as in docs/margin-figure.md (single victim, global alpha by");
        sb.AppendLine("decision, power budget contingent, tail floor as stated); additionally");
        sb.AppendLine("the payload here is a REQUIRED-envelope stand-in, not a real system --");
        sb.AppendLine("the 100% limit point's margin is a range artefact. CDFs:");
        sb.AppendLine("dataset/margin/study.{T,E1,E2,E1fine}.csv.");
        File.WriteAllText(Path.Combine(repo, "docs", "margin-study.md"), sb.ToString(), new UTF8Encoding(false));

        Console.WriteLine();
        Console.WriteLine(string.Create(inv,
            $"HEADLINE: compliant at power {frontier[idx0].Eirp:F0} dBW/40kHz (alpha 0){(pointAlpha > 0 ? string.Create(inv, $"; with alpha {pointAlpha:F1} deg the {pointEirp:F0} dBW/40kHz point is compliant") : "")}; "
            + $"projection margin {worstProj5:F2} dB (5 deg grid) / {worstProj2:F2} dB (2 deg grid)"));
        Console.WriteLine("study: docs/margin-study.md");
        return 0;
    }

    private static void WriteCdf(string path, double[] epfd, double[] pct)
    {
        var sb = new StringBuilder("epfd_dbw_m2_40khz,percent_time_exceeded\n");
        int first = Math.Max(0, Array.FindIndex(pct, p => p < 100.0) - 1);
        int last = Math.Min(pct.Length - 1, Array.FindLastIndex(pct, p => p > 0.0) + 1);
        if (last < first) { first = 0; last = pct.Length - 1; }
        for (int i = first; i <= last; i++)
            sb.AppendLine(FormattableString.Invariant($"{epfd[i]:F1},{pct[i]:G9}"));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}
