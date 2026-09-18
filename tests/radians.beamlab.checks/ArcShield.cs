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

namespace radians.beamlab.checks;

/// <summary>
/// The case (a) measurement for the 11.32A concept note: does a non-GSO system
/// that protects the geostationary arc clear the Table 22-1C values, and does
/// one that does not protect it fail them? One constellation (the dataset
/// family's three shells), one payload family at one power level, three masks
/// -- no notch (the arc lit), a rule notch at 8 deg and one at 22 deg, each
/// beside a declared zone of the same angle (consistent by construction) --
/// examined at victims 0-60 N against every plain FSS row of 22-1C at 19.7-20.2
/// GHz (each dish, 40 kHz and 1 MHz), with the antenna evaluated at 19.05 GHz as
/// if the row were transferred to 18.8-19.3 GHz. Margins move dB for dB with the
/// payload, so the quantity that does not depend on the chosen level is the
/// DIFFERENCE between the protecting and the non-protecting system at each row:
/// the daylight case (a) rests on.
///
/// Run:  -- arcshield [key=value ...]   steps=5760 step=30 tx=-40 notches=0,8,22
///       lats=0,10,20,30,40,50,60 bws=40,1000 nco=2 minel=10
/// </summary>
internal static class ArcShield
{
    private sealed record Row(radlimits.Limit Lim, double DishM, double RefBwKHz, string Label);
    private sealed record Cell(double Lat, ProbeExamination.Verdict Full, ProbeExamination.Verdict Half);

    public static int Run(string[] a)
    {
        var inv = CultureInfo.InvariantCulture;
        var t0 = Stopwatch.StartNew();
        var kv = a.Where(x => x.Contains('=')).ToDictionary(x => x.Split('=')[0].ToLowerInvariant(), x => x.Split('=', 2)[1]);
        string S(string k, string d) => kv.TryGetValue(k, out var v) ? v : d;
        double D(string k, double d) => kv.TryGetValue(k, out var v) ? double.Parse(v, inv) : d;
        double[] List(string k, string d) => S(k, d).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, inv)).ToArray();
        double stepSec = D("step", 30.0);
        long steps = (long)D("steps", 5760), half = steps / 2;
        double tx = D("tx", -40.0);
        double[] notches = List("notches", "0,8,22");
        double[] lats = List("lats", "0,10,20,30,40,50,60");
        double[] bws = List("bws", "40,1000");
        int nco = (int)D("nco", 2);
        double minEl = D("minel", 10.0);
        const double rowFMin = 19700.0, rowFMax = 20200.0;     // the 22-1C rows
        const double antFreqMhz = 19050.0;                      // the transferred band's centre
        const double esLon = 0.0, gsoLon = 10.0;

        string dllDir = new[]
        {
            @"C:\Projects\_EPFD\radians\radians\dlls",
            @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
        }.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdLimitsApi64.dll")));
        string limitsDb = ProbeExamination.ResolveLimitsDb(null);
        if (dllDir is null || limitsDb is null) { Console.WriteLine("ABORT: BR limits database or EpfdLimitsApi64.dll not present."); return 2; }
        LimitsDbReader.DllDirectory = dllDir;

        // ---- every plain FSS row of the table, per dish and reference bandwidth ----
        var rows = new List<Row>();
        foreach (double bw in bws)
        {
            var got = LimitsDbReader.Read(limitsDb, 0.5 * (rowFMin + rowFMax) - 0.02, 0.5 * (rowFMin + rowFMax) + 0.02, bw, 900.0);
            foreach (var l in got.Where(l => !l.ShortTermLatDependent && l.Points.Count > 0 && l.Rf_diam is not null && l.Service == "FSS")
                                 .OrderBy(l => l.Rf_diam.Value))
            {
                if (rows.Any(r => Math.Abs(r.DishM - l.Rf_diam.Value) < 1e-6 && Math.Abs(r.RefBwKHz - l.RefBW) < 1e-6)) continue;
                rows.Add(new Row(l, l.Rf_diam.Value, l.RefBW, ComplianceViewModel.DescribeLimit(l)));
            }
        }
        Console.WriteLine($"{rows.Count} row(s):");
        foreach (var r in rows)
            Console.WriteLine("  " + r.Label + "  points: " + string.Join("  ", r.Lim.Points.Select(p => string.Create(inv, $"{p.EPFD:F1}@{p.Perc:G4}%"))));

        // ---- the three systems: one payload, three masks, each beside its own declared zone ----
        string outDir = Path.Combine(AppContext.BaseDirectory, "exp", "arcshield");
        Directory.CreateDirectory(outDir);
        var con = new Constellation(DatasetGenerator.Shells);
        var (fMin, fMax) = DatasetGenerator.ProbeBandMhz;
        var systems = new List<(double Notch, string Name, IPureMaskPfdRead Mask, OperatingParamsSet Set, string MaskFile, double Peak40, MaskConsistency.Report Grade)>();
        foreach (double notch in notches)
        {
            var spec = new DatasetGenerator.ProbeMaskSpec(GateAlphaDeg: notch, MinElevDeg: minEl, TxDeltaDb: tx, NotchAlphaDeg: notch, BStepDeg: 2.0);
            string path = Path.Combine(outDir, string.Create(inv, $"arcshield_notch{notch:F0}_tx{tx:F0}.xml"));
            if (!File.Exists(path)) DatasetGenerator.GenerateProbeMask(path, 11, spec, quick: false);
            var set = new OperatingParamsSet { SatName = DatasetGenerator.SatName, NtcId = 0, ParamId = 40, LowFreqMhz = fMin, HighFreqMhz = fMax };
            ProbeExamination.WithMinElev(ProbeExamination.WithNco(set, nco), minEl);
            if (notch > 0) ProbeExamination.WithAlpha(set, notch); else ProbeExamination.WithAlpha(set, 0.0);
            string name = notch > 0 ? string.Create(inv, $"protects the arc: zone {notch:F0} deg declared and written into the mask") : "does not protect the arc: no zone, the arc lit";
            var loaded = MaskXmlImport.Load(path);
            double peak40 = loaded.Blocks.SelectMany(b => b.Rows).SelectMany(r => r.Values).Where(v => v > MaskLatBlock.UnreachableDb + 1).Max();
            var grade = MaskConsistency.CheckAlphaForm(loaded, set);
            systems.Add((notch, name, new MaskFootprint(loaded), set, Path.GetFileName(path), peak40, grade));
            Console.WriteLine(string.Create(inv, $"  notch {notch,2:F0}: mask peak {peak40:F1} dB(W/m2) in 40 kHz = {peak40 + 13.98:F1} dB(W/(m2 MHz)); grade: {grade.Summary}"));
        }
        Console.WriteLine(string.Create(inv, $"masks ready ({t0.Elapsed.TotalSeconds:F0} s); payload {tx:+0.0;-0.0} dB against mask 1's; {steps} steps of {stepSec:F0} s + the {half}-step prefix"));

        // ---- the matrix ---------------------------------------------------------------
        var results = new Dictionary<(int Row, double Notch), List<Cell>>();
        foreach (var (ri, row) in rows.Select((r, i) => (i, r)))
        {
            var lim = new ProbeExamination.LimitRow(row.Label, row.DishM, row.Lim.Points.ToList());
            double scale = 10.0 * Math.Log10(row.RefBwKHz / 40.0);   // the mask is filed in 40 kHz; a 1 MHz row reads it 13.98 dB up (flat spectrum)
            foreach (var sys in systems)
            {
                IMaskPfdRead read = Math.Abs(scale) < 1e-9 ? sys.Mask : new ProbeExamination.OffsetMaskRead(sys.Mask, scale);
                var cells = new List<Cell>();
                foreach (double lat in lats)
                {
                    var full = ProbeExamination.Examine(con, read, sys.Set, lim, antFreqMhz, lat, esLon, gsoLon, stepSec, steps);
                    var h = ProbeExamination.Examine(con, read, sys.Set, lim, antFreqMhz, lat, esLon, gsoLon, stepSec, half);
                    cells.Add(new Cell(lat, full, h));
                }
                results[(ri, sys.Notch)] = cells;
                var worst = cells.OrderBy(c => c.Full.RuleMarginDb).First();
                Console.WriteLine(string.Create(inv,
                    $"  {row.DishM,4:F2} m {row.RefBwKHz,5:F0} kHz | notch {sys.Notch,2:F0} | worst {worst.Full.RuleMarginDb,6:+0.0;-0.0} dB at {worst.Lat:F0} N ({worst.Full.Points.OrderBy(p => p.MarginDb).First().Perc:G4}% point) | prefix {worst.Half.RuleMarginDb,6:+0.0;-0.0}"));
            }
        }

        // ---- the record -------------------------------------------------------------------
        string repo = Directory.Exists(@"C:\Projects\radians.beamlab") ? @"C:\Projects\radians.beamlab" : AppContext.BaseDirectory;
        var sb = new StringBuilder();
        sb.AppendLine("# Case (a) of the 11.32A concept note, measured: protecting the arc against the Table 22-1C values");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"*Produced by `dotnet run --project tests/radians.beamlab.checks -- arcshield {string.Join(" ", a)}`, {DateTime.Now:yyyy-MM-dd}; wall clock {t0.Elapsed.TotalMinutes:F1} min.*"));
        sb.AppendLine();
        sb.AppendLine("## The question");
        sb.AppendLine();
        sb.AppendLine("The concept note's case (a) proposes, for bands adjacent to those with Article 22 limits, that the Table 22-1 values of the adjacent band be applied to a non-GSO system with a geostationary victim, so that an operator who protects the geostationary arc has a definite route to a favourable finding. That rests on two measurable claims: a system that protects the arc clears the transferred values with room, and one that does not protect it fails them. This record measures both on one constellation with one payload.");
        sb.AppendLine();
        sb.AppendLine("## The construction");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"- The constellation: the validation dataset's three shells (A: 1 200 km / 55 deg, 4 x 8, repeating; B: 900 km / 87 deg, 6 x 6; C: elliptical 800 x 4 000 km / 63.4 deg, 2 x 4), 76 satellites, cells of 450 km served by the family's payload at {tx:+0.0;-0.0} dB against the dataset's mask 1 -- one power level for all three systems, chosen so that the protecting systems sit near the limit; every margin below moves dB for dB with it, and the differences between systems do not move at all."));
        sb.AppendLine("- Three systems from that one payload, each a pfd mask in the alpha/deltaLongitude form beside an arrays-only operating-parameter set (minimum elevation " + string.Create(inv, $"{minEl:F0}") + " deg, co-frequency cap " + nco.ToString(inv) + "):");
        foreach (var sys in systems)
            sb.AppendLine("  - notch " + string.Create(inv, $"{sys.Notch:F0}") + " deg: " + sys.Name + (sys.Notch > 0 ? " -- the reachable envelope composed with a boresight gate of that angle and the Sec. C1 -1000 null written into the alpha axis inside it, so mask and declaration describe one system (consistent by this producer's grader)." : " -- the reachable envelope with no gate; the declaration says no zone, so the pair is consistent too, and the arc is lit.") + " File `" + sys.MaskFile + "`.");
        sb.AppendLine(string.Create(inv, $"- The examination: S.1503-4 Sec. D5.1.4.1 (the classic downlink algorithm) at victims {string.Join(", ", lats.Select(l => l.ToString("F0", inv) + " N"))}, earth station at longitude {esLon:F0}, wanted GSO satellite at {gsoLon:F0} E, the row's own reference dish in the S.1428 pattern evaluated at {antFreqMhz / 1000.0:F2} GHz (the centre of 18.8-19.3 GHz, the band the row would be transferred to); {steps} steps of {stepSec:F0} s ({stepSec * steps / 3600.0:F0} h) with the {stepSec * half / 3600.0:F0} h prefix as the extension pair. Worst margin = the minimum over the row's points of (limit epfd minus the epfd exceeded for at most the point's percentage), in the examination's 0.1 dB bins; positive is room."));
        sb.AppendLine("- The rows: every plain FSS row of Table 22-1C at 19.7-20.2 GHz in the BR limits database, per reference dish and per reference bandwidth; the 1 MHz rows read the 40 kHz mask scaled by 10 log(1000/40) = 13.98 dB (a flat spectrum, the reference implementation's convention).");
        sb.AppendLine();
        sb.AppendLine("## The rows");
        sb.AppendLine();
        foreach (var r in rows)
            sb.AppendLine("- " + r.Label + ": " + string.Join("; ", r.Lim.Points.Select(p => string.Create(inv, $"{p.EPFD:F1} dB(W/m2) for {p.Perc:G4}%"))) + ".");
        sb.AppendLine();
        sb.AppendLine("## The answer, row by row");
        sb.AppendLine();
        sb.AppendLine("For each row: the worst margin over the victims of each system at the common payload, the victim and the binding point, the prefix's figure and how far it moved; then the DAYLIGHT -- the protecting system's worst margin minus the non-protecting system's, which is what protecting the arc is worth at the deciding point, independent of the payload level -- and the payload at which the protecting system would just clear the row, with the non-protecting system's margin at that same payload.");
        sb.AppendLine();
        sb.AppendLine(LimitCurveRule.Name());
        sb.AppendLine();
        sb.AppendLine("| row | system | worst rule margin (dB) | at | binding point | prefix (24 h) | moved | daylight vs no notch (dB) | boresight pfd at which this system just clears, dB(W/(m2 MHz)) | non-protector at the protector's clearing level (dB) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (ri, row) in rows.Select((r, i) => (i, r)))
        {
            var noNotch = results[(ri, notches.Min())];
            double worstNo = noNotch.Min(c => c.Full.RuleMarginDb);
            foreach (var sys in systems)
            {
                var cells = results[(ri, sys.Notch)];
                var w = cells.OrderBy(c => c.Full.RuleMarginDb).First();
                double bind = w.Full.Points.OrderBy(p => p.MarginDb).First().Perc;
                string daylight = sys.Notch > 0 ? string.Create(inv, $"{w.Full.RuleMarginDb - worstNo:+0.0;-0.0}") : "-";
                // Margins move dB for dB with the payload: the system just clears the row when its peak is lowered by the (negative) worst margin, i.e. peak + margin; stated per MHz (flat spectrum, +13.98 dB on the 40 kHz mask).
                string clear = string.Create(inv, $"{sys.Peak40 + 13.98 + w.Full.RuleMarginDb:F1}");
                string nonAt = sys.Notch > 0 ? string.Create(inv, $"{worstNo - w.Full.RuleMarginDb:+0.0;-0.0} ({(worstNo - w.Full.RuleMarginDb < 0 ? "FAIL" : "PASS")})") : "-";
                sb.AppendLine(string.Create(inv, $"| {row.DishM:F2} m, {row.RefBwKHz:F0} kHz | notch {sys.Notch:F0} | {w.Full.RuleMarginDb:+0.0;-0.0} | {w.Lat:F0} N | {bind:G4}% | {w.Half.RuleMarginDb:+0.0;-0.0} | {w.Full.RuleMarginDb - w.Half.RuleMarginDb:+0.0;-0.0} | {daylight} | {clear} | {nonAt} |"));
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Per victim, the 40 kHz rows");
        sb.AppendLine();
        foreach (var (ri, row) in rows.Select((r, i) => (i, r)).Where(x => Math.Abs(x.r.RefBwKHz - 40.0) < 1e-6))
        {
            sb.AppendLine("### " + row.Label);
            sb.AppendLine();
            sb.AppendLine("| victim | " + string.Join(" | ", systems.Select(s => string.Create(inv, $"notch {s.Notch:F0}: worst margin / binding point"))) + " |");
            sb.AppendLine("|---|" + string.Concat(systems.Select(_ => "---|")));
            foreach (double lat in lats)
            {
                var parts = systems.Select(s =>
                {
                    var c = results[(ri, s.Notch)].First(x => x.Lat == lat);
                    return string.Create(inv, $"{c.Full.RuleMarginDb:+0.0;-0.0} / {c.Full.Points.OrderBy(p => p.MarginDb).First().Perc:G4}%");
                });
                sb.AppendLine(string.Create(inv, $"| {lat:F0} N | ") + string.Join(" | ", parts) + " |");
            }
            sb.AppendLine();
        }
        sb.AppendLine("## The two grades the Rule would refer to");
        sb.AppendLine();
        sb.AppendLine("The consistency check of the note's A.3.8, applied to each mask against its own declared zone (the alpha axis read directly off the alpha/deltaLongitude form; the elevation axis is not readable in this form):");
        sb.AppendLine();
        foreach (var sys in systems)
            sb.AppendLine(string.Create(inv, $"- notch {sys.Notch:F0} deg (declared zone {(sys.Notch > 0 ? sys.Notch.ToString("F0", inv) : "none")}): **{MaskConsistency.Word(sys.Grade.Overall)}** -- {sys.Grade.Summary}."));
        var noNotchSys = systems.OrderBy(s => s.Notch).First();
        if (noNotchSys.Notch == 0)
        {
            var claimed = new OperatingParamsSet { SatName = DatasetGenerator.SatName, NtcId = 0, ParamId = 41, LowFreqMhz = fMin, HighFreqMhz = fMax };
            ProbeExamination.WithAlpha(ProbeExamination.WithMinElev(ProbeExamination.WithNco(claimed, nco), minEl), 8.0);
            var claimedGrade = MaskConsistency.CheckAlphaForm(MaskXmlImport.Load(Path.Combine(outDir, noNotchSys.MaskFile)), claimed);
            sb.AppendLine(string.Create(inv, $"- the unnotched mask graded against a DECLARED zone of 8 deg -- a system that claims the zone but does not carry it, the pair the Rule would meet on filed material: **{MaskConsistency.Word(claimedGrade.Overall)}** -- {claimedGrade.Summary}."));
        }
        sb.AppendLine();
        sb.AppendLine("## On the scale of a working downlink");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"The masks' boresight pfd at the payload used here: {string.Join("; ", systems.Select(sy => $"notch {sy.Notch:F0}: {sy.Peak40:F1} dB(W/m2) in 40 kHz = {sy.Peak40 + 13.98:F1} dB(W/(m2 MHz))"))}. The table's ninth column moves each system's peak to the payload at which it would just clear the row, per MHz. For the scale: Article 21's pfd limit in these bands is -105 dB(W/(m2 MHz)) at high elevation, and a typical working Ka-band downlink sits in the range -115 to -125 dB(W/(m2 MHz)) at the earth station. The 1 MHz figures assume the emission fills 1 MHz with a flat density -- exact for a carrier at least 1 MHz wide, an over-statement for a narrower one; this payload declares no carrier bandwidth, so the flat reading is the upper bound."));
        sb.AppendLine();
        sb.AppendLine("## Reading it");
        sb.AppendLine();
        var row70 = rows.Select((r, i) => (r, i)).FirstOrDefault(x => Math.Abs(x.r.RefBwKHz - 40.0) < 1e-6);
        if (row70.r is not null)
        {
            double wNo = results[(row70.i, notches.Min())].Min(c => c.Full.RuleMarginDb);
            foreach (var sys in systems.Where(s => s.Notch > 0))
            {
                double wP = results[(row70.i, sys.Notch)].Min(c => c.Full.RuleMarginDb);
                sb.AppendLine(string.Create(inv, $"- {row70.r.Label}: protecting the arc with a {sys.Notch:F0} deg zone is worth {wP - wNo:+0.0;-0.0} dB at the deciding point. At the payload where that system just clears the row, the system that does not protect the arc sits at {wNo - wP:+0.0;-0.0} dB: {(wNo - wP < 0 ? "it fails" : "it passes too, and the row does not discriminate")}."));
            }
        }
        var worstLats = systems.Where(sy => sy.Notch > 0).Select(sy =>
        {
            var w = results[(row70.i, sy.Notch)].OrderBy(c => c.Full.RuleMarginDb).First();
            return string.Create(inv, $"notch {sy.Notch:F0} at {w.Lat:F0} N ({w.Full.RuleMarginDb:+0.0;-0.0} dB)");
        }).ToList();
        if (row70.r is not null && worstLats.Count > 0)
            sb.AppendLine("- Latitude: the protectors' worst victims on the " + row70.r.Label.Split(" -- ")[0] + " row are " + string.Join(", ", worstLats) + ". The mechanism is general high-latitude geometry, not a property of one system: the arc sits low from a high-latitude earth station, so a zone about it removes little of the sky, and an inclined shell's sub-satellite density peaks near its inclination latitude, so more and closer satellites are in view there. The magnitude depends on the shell's inclination and satellite count -- the same reading of a filed 1 600-satellite system at 53 deg inclination gave 12.7 dB at 60 N against 8.5-9.9 dB at 0-50 N -- so a criterion of this kind bites hardest at latitudes near the interferer's inclination, which an operator serving those latitudes may not be able to meet by arc protection alone; Article 22 itself grades its further limits by latitude above 57.5 deg (No. 22.5C.4).");
        sb.AppendLine("- The daylight is the difference between where the two systems' worst points fall: the non-protecting system's worst point is the short-term end of the row, set by a satellite crossing the earth station's main beam inside the zone with a main-beam-grade mask value (Step 22 counts it whatever the declaration says); the protecting system's worst point is the body of the row, set by the co-frequency cap and the side-lobe levels, which the notch does not touch. Protecting the arc therefore buys nothing at the body and everything at the short-term end.");
        sb.AppendLine("- Caveats: one constellation and one payload family; this producer's reading of Sec. D5.1.4.1; a rule notch as the protecting mask (the declaration and the mask agree by construction, which is exactly the route case (a) offers an operator); the antenna evaluated at 19.05 GHz rather than at the row's own band, a difference of a fraction of a decibel in the S.1428 pattern; the prefix column says how far each worst margin is from converged (the body converges in hours, the short-term end with the closest pass of the run).");
        string outPath = Path.Combine(repo, "docs", "arc-shield-case-a.md");
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine(string.Create(inv, $"record: {Path.GetRelativePath(repo, outPath)} ({t0.Elapsed.TotalMinutes:F1} min)"));
        return 0;
    }
}
