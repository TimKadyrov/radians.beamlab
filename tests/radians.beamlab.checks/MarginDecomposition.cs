using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using radians.beamlab;
using radians.beamlab.app;
using radians.beamlab.dataset;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.checks;

/// <summary>
/// The examination's selection rules applied to the LIVE beam composition
/// instead of the declared mask: an <see cref="IMaskPfdRead"/> whose value for
/// a satellite is the pfd its resolved beams actually put on the earth station
/// at that step. Feeding it to <see cref="EpfdDownMask"/> gives E_sel -- the
/// Sec. D5.1.4.1 population count (elevation gate, exclusion zone, MAX_CO_FREQ
/// pick, MIN_ANGLE_AT_ES pruning, the main-beam always-include) over the
/// truth's own values -- which sits between the truth T (every visible
/// satellite summed, no selection) and the examination E1 (the same selection
/// over the mask). With no gate declared it reproduces T exactly (V55).
///
/// The pointing is a ScheduledPointing whose schedule is computed once per
/// time step; the examination calls this read per satellite in time order.
/// ONE READ SERVES ONE PASS. The scheduler carries dwell memory and, under
/// the Random policy, a seeded key sequence, so a second pass over the same
/// times (another victim) that resolves a satellite the first pass never saw
/// re-enters the scheduler at an earlier time from a later state and gets a
/// different schedule. The cross-time cache (cacheAll) is therefore only
/// exact within a single pass; a sweep builds a fresh read per victim, and
/// the 14 September decomposition record was re-run for that reason.
/// </summary>
internal sealed class LiveCompositionRead : IMaskPfdRead
{
    private readonly IBeamPointing _pointing;
    private readonly Dictionary<double, Dictionary<int, ResolvedBeamSet>> _cache = new();
    private readonly bool _cacheAll;

    public LiveCompositionRead(IBeamPointing pointing, bool cacheAll = true)
    {
        _pointing = pointing;
        _cacheAll = cacheAll;
    }

    public double PfdDb(SatelliteState state, Vec3 satPosKm, Vec3 esPosKm)
    {
        ResolvedBeamSet set;
        if (_cacheAll)
        {
            if (!_cache.TryGetValue(state.TimeSeconds, out var perSat))
            {
                perSat = new Dictionary<int, ResolvedBeamSet>();
                _cache[state.TimeSeconds] = perSat;
            }
            if (!perSat.TryGetValue(state.SatelliteNumber, out set))
            {
                set = _pointing.Resolve(state);
                perSat[state.SatelliteNumber] = set;
            }
        }
        else set = _pointing.Resolve(state);

        if (set is null || set.Beams.Count == 0) return MaskLatBlock.UnreachableDb;
        var toEs = (esPosKm - satPosKm).Normalized();
        double eirp = BeamComposer.ResolvedEirpDbw(set, toEs);
        if (double.IsNegativeInfinity(eirp)) return MaskLatBlock.UnreachableDb;
        double distM = (esPosKm - satPosKm).Length * 1000.0;
        return eirp - 10.0 * Math.Log10(4.0 * Math.PI * distM * distM);
    }
}

/// <summary>
/// The margin decomposition the design brief asks for (Sec. 2): how much of
/// the gap between the examination and the truth comes from the mask envelope
/// and how much from the selection rules, each isolated by defeating it in
/// turn -- T (no selection, live values), E_sel (selection, live values), E1
/// (selection, mask values). The worst-case geometry component is zero here by
/// construction: all three read the same victim. Differences at matched depth
/// are quotable without a pair (the rule of 7-8 September); absolute levels at
/// this depth are not, and the record says so.
///
/// Run:  -- decompose [profile] [design] [rsetDir] [days] [stepSec] [latFrom] [latTo] [latStep] [tag]
///       defaults: the STEAM-2 case, its recorded declaration (dataset/margin/steam-2),
///       0.1 d at 60 s, latitudes 0..60 step 10.
/// </summary>
internal static class MarginDecomposition
{
    private sealed record Curve(double MaxEpfdDb, long QuietSteps, double[] Epfd, double[] Pct)
    {
        public double At(double perc) { int i = Array.FindIndex(Pct, v => v <= perc); return i < 0 ? Epfd[^1] : Epfd[i]; }
    }

    public static int Run(string profilePath, string designPath, string rsetDir, double days, double stepSec,
        double latFrom, double latTo, double latStep, string tag)
    {
        var inv = CultureInfo.InvariantCulture;
        var t0 = Stopwatch.StartNew();
        string repo = Directory.Exists(@"C:\Projects\radians.beamlab") ? @"C:\Projects\radians.beamlab" : AppContext.BaseDirectory;
        if (!File.Exists(profilePath) || !File.Exists(designPath) || !Directory.Exists(rsetDir))
        { Console.WriteLine("ABORT: profile, design or declaration directory not found."); return 2; }

        var prof = OperationProfileCodec.Load(File.ReadAllText(profilePath));
        var doc = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(designPath));
        var shells = doc.Shells.Select(OrbitDesignFileCodec.ToShell).ToArray();
        double altKm = shells[0].OperatingHeightKm ?? shells[0].AltitudeKm;
        double freqMhz = prof.Down.FrequencyGhz * 1000.0;
        var con = new Constellation(shells);
        var comp = OperationComposer.Compose(prof, altKm);
        var (declared, setPath, maskPath) = ComplianceLoop.LoadReusedDeclaration(rsetDir);

        string dllDir = new[] { @"C:\Projects\_EPFD\radians\radians\dlls", @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0" }
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdLimitsApi64.dll")));
        string limitsDb = ProbeExamination.ResolveLimitsDb(null);
        if (dllDir is null || limitsDb is null) { Console.WriteLine("ABORT: BR limits database or EpfdLimitsApi64.dll not present."); return 2; }
        var lim = ProbeExamination.LoadLimitRow(limitsDb, dllDir, freqMhz - 0.02, freqMhz + 0.02, prof.Down.RefBwKHz, altKm);

        long steps = (long)Math.Round(days * 86400.0 / stepSec);
        double simDur = steps * stepSec;
        var lats = new List<double>();
        for (double l = latFrom; l <= latTo + 1e-9; l += latStep) lats.Add(l);
        EpfdDownVictim Victim(double lat) => new()
        {
            EsLatDeg = lat, EsLonDeg = 0.0, GsoLonDeg = 10.0,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, freqMhz, lim.DishM),
        };
        Console.WriteLine(string.Create(inv, $"decompose [{tag}]: {prof.Name}; {con.SatelliteCount} satellites; {steps} steps of {stepSec:F0} s ({days:F3} d); lats {latFrom:F0}..{latTo:F0} step {latStep:F0}"));
        Console.WriteLine("  declaration: " + Path.GetFileName(setPath) + " + " + Path.GetFileName(maskPath));
        Console.WriteLine("  limit row  : " + lim.Label);

        // ---- T: every visible satellite, live values, no selection -----------------
        Console.WriteLine("  T (truth, one pass over the grid)...");
        var pointingT = new ScheduledPointing(con, comp.Geography, comp.Enforced, comp.Scene, simDur, comp.CoverageRadiusKm, comp.Policy, comp.IlluminationDutyCycle);
        var resT = EpfdDown.RunMany(con, pointingT, lats.Select(Victim).ToList(), stepSec, steps, lim.Points, simDur);
        var T = resT.Select(r => { var (e, p) = r.Accumulator.BuildCdf(); return new Curve(r.MaxEpfdDb, r.QuietSteps, e, p); }).ToList();
        Console.WriteLine(string.Create(inv, $"  T done ({t0.Elapsed.TotalMinutes:F1} min)"));

        // ---- E_sel: the declared set's selection over the live values ---------------
        // A fresh read, and so a fresh scheduler, per victim: each pass then steps
        // the same seeded schedule the truth stepped (V55), instead of re-entering
        // a scheduler that has already run to the end for an earlier victim.
        var Esel = new List<Curve>();
        foreach (double lat in lats)
        {
            var live = new LiveCompositionRead(new ScheduledPointing(con, comp.Geography, comp.Enforced, comp.Scene, simDur, comp.CoverageRadiusKm, comp.Policy, comp.IlluminationDutyCycle), cacheAll: false);
            var r = EpfdDownMask.Run(con, live, declared, Victim(lat), stepSec, steps, lim.Points, simDur);
            var (e, p) = r.Accumulator.BuildCdf();
            Esel.Add(new Curve(r.MaxEpfdDb, r.QuietSteps, e, p));
            Console.WriteLine(string.Create(inv, $"  E_sel lat {lat:F0} done ({t0.Elapsed.TotalMinutes:F1} min)"));
        }

        // ---- E1: the same selection over the declared mask ------------------------
        var mask = MaskFootprint.LoadFile(maskPath);
        var E1 = new List<Curve>();
        foreach (double lat in lats)
        {
            var r = EpfdDownMask.Run(con, mask, declared, Victim(lat), stepSec, steps, lim.Points, simDur);
            var (e, p) = r.Accumulator.BuildCdf();
            E1.Add(new Curve(r.MaxEpfdDb, r.QuietSteps, e, p));
        }
        Console.WriteLine(string.Create(inv, $"  E1 done ({t0.Elapsed.TotalMinutes:F1} min)"));

        // ---- the record ----------------------------------------------------------------
        var percs = FamilyCurves.Percentiles(steps).Concat(lim.Points.Select(p => p.Perc)).Where(p => p > 0 && p >= 100.0 / steps - 1e-12).Distinct().OrderByDescending(p => p).ToList();
        double Margin(Curve c) => lim.Points.Min(p => p.EPFD - c.At(p.Perc));
        var sb = new StringBuilder();
        sb.AppendLine($"# Margin decomposition: {tag}");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"*Produced by `dotnet run --project tests/radians.beamlab.checks -- decompose \"{Path.GetFileName(profilePath)}\" \"{Path.GetFileName(designPath)}\" \"{Path.GetFileName(rsetDir)}\" {days} {stepSec:F0} {latFrom:F0} {latTo:F0} {latStep:F0} {tag}`, {DateTime.Now:yyyy-MM-dd}; wall clock {t0.Elapsed.TotalMinutes:F1} min.*"));
        sb.AppendLine();
        sb.AppendLine("## What is decomposed");
        sb.AppendLine();
        sb.AppendLine("The design brief (Sec. 2) asks not for closeness between the examination and the simulation but for a decomposition of their gap: how many decibels come from the mask envelope, how many from the selection rules, how many from the worst-case geometry, each isolated by defeating it in turn. Three runs on one victim do that here:");
        sb.AppendLine();
        sb.AppendLine("- **T** -- the truth: every satellite visible from the earth station, the pfd its resolved beams actually put there, power-summed; no selection.");
        sb.AppendLine("- **E_sel** -- the examination's selection rules (Sec. D5.1.4.1: the elevation gate, the exclusion zone, the MAX_CO_FREQ pick by highest contribution, MIN_ANGLE_AT_ES pruning, the main-beam always-include) applied to the same live values, read from the declared operating-parameter set.");
        sb.AppendLine("- **E1** -- the examination proper: the same selection over the declared pfd mask.");
        sb.AppendLine();
        sb.AppendLine("So **E_sel − T** is what the selection rules do to the count (negative where they remove satellites the truth sums), **E1 − E_sel** is what the mask envelope adds over the live values, and **E1 − T** is the projection margin. The worst-case geometry component is zero by construction: all three runs read the same victim, so a consumer's own geometry search adds to these figures rather than being inside them.");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"System: {prof.Name}; {con.SatelliteCount} satellites. Declaration: `{Path.GetFileName(setPath)}` and `{Path.GetFileName(maskPath)}` ({ComplianceLoop.DescribeSet(declared, inv)}). Victim: earth station at longitude 0, GSO satellite at 10 E, {lim.DishM:F2} m S.1428 dish at {freqMhz / 1000.0:F2} GHz -- the row's own. Row: {lim.Label}. Depth: {steps} steps of {stepSec:F0} s ({days:F3} d), resolvable floor {100.0 / steps:F3}%."));
        sb.AppendLine();
        sb.AppendLine("Quotability: the three runs share one comb, so their DIFFERENCES at matched depth are quotable without a pair (the rule of 7-8 September); the absolute levels at this depth are not converged where the records at 1.0 d say they are not, and are given for orientation only.");
        sb.AppendLine();
        sb.AppendLine("## Per latitude: worst margins and the components at the deciding point");
        sb.AppendLine();
        sb.AppendLine("| latitude | T worst margin | E_sel worst margin | E1 worst margin | selection E_sel − T | envelope E1 − E_sel | total E1 − T | quiet steps T / E_sel / E1 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        for (int i = 0; i < lats.Count; i++)
        {
            double mT = Margin(T[i]), mS = Margin(Esel[i]), mE = Margin(E1[i]);
            // Margins are limit minus level, so a component in LEVEL terms is the negative of the margin difference.
            sb.AppendLine(string.Create(inv, $"| {lats[i]:F0} | {mT:+0.0;-0.0} | {mS:+0.0;-0.0} | {mE:+0.0;-0.0} | {mT - mS:+0.0;-0.0} | {mS - mE:+0.0;-0.0} | {mT - mE:+0.0;-0.0} | {T[i].QuietSteps} / {Esel[i].QuietSteps} / {E1[i].QuietSteps} |"));
        }
        sb.AppendLine();
        sb.AppendLine("Components are in level terms (dB of epfd), positive when the later stage sits higher; the worst margins are the row's minimum over its points.");
        sb.AppendLine();
        sb.AppendLine("## Per percentile");
        sb.AppendLine();
        for (int i = 0; i < lats.Count; i++)
        {
            sb.AppendLine(string.Create(inv, $"### {lats[i]:F0} N"));
            sb.AppendLine();
            sb.AppendLine("| % time exceeded | T | E_sel | E1 | selection | envelope | total |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (double p in percs)
            {
                double t = T[i].At(p), s = Esel[i].At(p), e = E1[i].At(p);
                sb.AppendLine(string.Create(inv, $"| {p:G4} | {t:F1} | {s:F1} | {e:F1} | {s - t:+0.0;-0.0;0.0} | {e - s:+0.0;-0.0;0.0} | {e - t:+0.0;-0.0;0.0} |"));
            }
            sb.AppendLine(string.Create(inv, $"| max | {T[i].MaxEpfdDb:F1} | {Esel[i].MaxEpfdDb:F1} | {E1[i].MaxEpfdDb:F1} | {Esel[i].MaxEpfdDb - T[i].MaxEpfdDb:+0.0;-0.0;0.0} | {E1[i].MaxEpfdDb - Esel[i].MaxEpfdDb:+0.0;-0.0;0.0} | {E1[i].MaxEpfdDb - T[i].MaxEpfdDb:+0.0;-0.0;0.0} |"));
            sb.AppendLine();
        }
        sb.AppendLine("## Reading it");
        sb.AppendLine();
        var body = lats.Select((l, i) => (l, s: Esel[i].At(10.0) - T[i].At(10.0), e: E1[i].At(10.0) - Esel[i].At(10.0))).ToList();
        sb.AppendLine(string.Create(inv, $"- At the 10% point the selection component ranges {body.Min(x => x.s):+0.0;-0.0} to {body.Max(x => x.s):+0.0;-0.0} dB and the envelope component {body.Min(x => x.e):+0.0;-0.0} to {body.Max(x => x.e):+0.0;-0.0} dB across the latitudes. Where the selection component is negative the rules remove satellites the truth sums (the cap and the gates); where it is near zero they leave the count alone and the whole gap is the envelope's."));
        sb.AppendLine("- The envelope component is the price of describing every configuration the system can reach by one per-direction maximum; the selection component is the price, or the credit, of describing the operator's scheduler by a count. Both are what the format discards, in the brief's words, and the first is the one the derivation's granularity levers act on.");
        sb.AppendLine("- With no gate declared, E_sel equals T bin for bin (V55): the decomposition's zero is a checked invariant, not an assumption.");
        string outPath = Path.Combine(repo, "docs", $"margin-decomposition-{tag}.md");
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine(string.Create(inv, $"record: {Path.GetRelativePath(repo, outPath)} ({t0.Elapsed.TotalMinutes:F1} min)"));
        return 0;
    }
}
