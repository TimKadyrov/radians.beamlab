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

// Mask parity: does the beam composition we wrote down for a case reproduce
// the operator's filed pfd mask? The case's operation profile and design
// document are composed exactly as a simulation would compose them, beamlab
// exports its own satellite-frame (az/el) mask from that composition, and
// both masks -- ours and the filed one -- go through the same dissection
// (MaskDissect.Analyze), so the operating rules read off each can be set side
// by side: minimum elevation, exclusion alpha and its latitude span, the pfd
// cap and whether it is flat, the side-lobe floor. A cell-wise diff on the
// common (latitude, az, el) grid then prices the residue: how far our plateau
// and floor sit from theirs, and where the two disagree on what is radiated
// at all. This is the "plausible envelope" question made numeric on a real
// filing: a composition whose derived mask lands on the filed one is an
// operable system consistent with the declaration.
//
// Run:  dotnet run --project tests/radians.beamlab.checks -- parity <filedMask.xml> [latStepDeg]
// Output: docs/mask-parity-<sat>.md; the exported mask in dataset/margin/.
internal static class MaskParity
{
    public static int Run(string filedMaskPath, double latStepDeg)
    {
        var inv = CultureInfo.InvariantCulture;
        var t0 = Stopwatch.StartNew();
        string repo = Directory.Exists(@"C:\Projects\radians.beamlab")
            ? @"C:\Projects\radians.beamlab" : AppContext.BaseDirectory;
        string src = Path.Combine(repo, "dataset", "_src");
        string profPath = Path.Combine(src, "STEAM-2.opprofile.json");
        string designPath = Path.Combine(src, "STEAM-2.orbitdesign.json");
        if (!File.Exists(filedMaskPath) || !File.Exists(profPath) || !File.Exists(designPath))
        {
            Console.WriteLine("ABORT: need the filed mask, dataset/_src/STEAM-2.opprofile.json and STEAM-2.orbitdesign.json");
            return 2;
        }

        // ---- 1. The case, composed as a run would compose it -----------------
        var prof = OperationProfileCodec.Load(File.ReadAllText(profPath));
        var design = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(designPath));
        var shell = OrbitDesignFileCodec.ToShell(design.Shells[0]);
        double alt = shell.AltitudeKm;
        var comp = OperationComposer.Compose(prof, alt);
        Console.WriteLine(string.Create(inv,
            $"case: {prof.Name}; shell {shell.PlaneCount}x{shell.SatsPerPlane} @ {alt:F0} km / i {shell.InclinationDeg:F1}; min elev {prof.MinElevDeg:F0}, alpha {prof.AlphaExclDeg:F0}, power mode '{prof.Down.PowerMode}', Gm {prof.Down.GainPeakDbi}, Tx {prof.Down.TxEirpDbw} dBW/ref BW, floor {prof.Down.PatternFloorDbi} dBi"));

        // ---- 2. Beamlab's own mask from that composition -----------------------
        var theirs = MaskXmlImport.Load(filedMaskPath);
        double maxLat = MaskXmlExport.MaxLatitudeForInclination(shell.InclinationDeg);
        double latMax = Math.Floor(maxLat / latStepDeg) * latStepDeg;
        string outDir = Path.Combine(repo, "dataset", "margin");
        Directory.CreateDirectory(outDir);
        string ourPath = Path.Combine(outDir, "steam2-beamlab.mask.xml");
        var opts = new MaskXmlExportOptions
        {
            SatName = "STEAM-2-beamlab", NtcId = theirs.NtcId, MaskId = theirs.MaskId,
            LowFreqMhz = theirs.LowFreqMhz, HighFreqMhz = theirs.HighFreqMhz, RefBwKHz = theirs.RefBwKHz,
            LatMinDeg = -latMax, LatMaxDeg = latMax, LatStepDeg = latStepDeg,
            BStepDeg = 1.0, CStepDeg = 1.0,
            Kind = MaskPlotKind.AzEl, Format = MaskExportFormat.Xml,
            OutputPath = ourPath,
        };
        Console.WriteLine(string.Create(inv, $"exporting beamlab's az/el mask: lat {-latMax:F0}..{latMax:F0} step {latStepDeg:F0}, az/el 1 deg..."));
        int lastPct = -10;
        var progress = new Progress<double>(p =>
        {
            int pct = (int)(p * 100);
            if (pct >= lastPct + 10) { lastPct = pct; Console.WriteLine($"  export {pct}%"); }
        });
        MaskXmlExport.GenerateAsync(new ReachableEnvelopeSampler(comp.Scene, opts, maxLat),
            opts, progress, CancellationToken.None).GetAwaiter().GetResult();
        var ours = MaskXmlImport.Load(ourPath);
        Console.WriteLine(string.Create(inv, $"ours: {ours.Blocks.Count} blocks; theirs: {theirs.Blocks.Count} blocks; export {t0.Elapsed.TotalMinutes:F1} min"));

        // ---- 3. The same dissection on both ----------------------------------------
        var ra = MaskDissect.Analyze(ours, alt);
        var rb = MaskDissect.Analyze(theirs, alt);
        Console.WriteLine("ours:   " + MaskDissect.RulesSummary(ra, inv));
        Console.WriteLine("theirs: " + MaskDissect.RulesSummary(rb, inv));

        // ---- 4. Cell-wise diff on the common grid ------------------------------------
        // Their blocks sit at every integer latitude; ours at latStep. Compare each of
        // our blocks against their block at the same latitude, at the (az, el) nodes
        // both carry. Classes follow THEIR mask: plateau (>= their peak - 3), floor
        // (< their peak - 20); cells one side reaches and the other does not are
        // counted separately (the disc edge and any coverage disagreement).
        var theirBlocks = theirs.Blocks.ToDictionary(b => Math.Round(b.LatDeg, 3));
        long nPlateau = 0, nFloor = 0, nMid = 0, onlyOurs = 0, onlyTheirs = 0;
        double sumPl = 0, maxPl = double.NegativeInfinity, minPl = double.PositiveInfinity;
        double sumFl = 0, maxFl = double.NegativeInfinity, minFl = double.PositiveInfinity;
        long within1Pl = 0, within3Fl = 0;
        var perLat = new List<(double lat, long pl, double meanPl, long fl, double meanFl, long onlyO, long onlyT)>();
        foreach (var ob in ours.Blocks)
        {
            if (!theirBlocks.TryGetValue(Math.Round(ob.LatDeg, 3), out var tb)) continue;
            double tPeak = tb.Rows.SelectMany(r => r.Values).Where(v => v > MaskLatBlock.UnreachableDb + 1).DefaultIfEmpty(double.NegativeInfinity).Max();
            var tCells = new Dictionary<(int, int), double>();
            foreach (var row in tb.Rows)
                for (int k = 0; k < row.CNodes.Length; k++)
                    tCells[((int)Math.Round(row.B), (int)Math.Round(row.CNodes[k]))] = row.Values[k];
            long pl = 0, fl = 0, oO = 0, oT = 0; double sPl = 0, sFl = 0;
            var seen = new HashSet<(int, int)>();
            foreach (var row in ob.Rows)
                for (int k = 0; k < row.CNodes.Length; k++)
                {
                    var key = ((int)Math.Round(row.B), (int)Math.Round(row.CNodes[k]));
                    if (!tCells.TryGetValue(key, out double tv)) continue;   // outside their grid
                    seen.Add(key);
                    double ov = row.Values[k];
                    bool oReach = ov > MaskLatBlock.UnreachableDb + 1, tReach = tv > MaskLatBlock.UnreachableDb + 1;
                    if (oReach && !tReach) { oO++; continue; }
                    if (!oReach && tReach) { oT++; continue; }
                    if (!oReach) continue;
                    double diff = ov - tv;
                    if (tv >= tPeak - 3.0)
                    {
                        pl++; sPl += diff; maxPl = Math.Max(maxPl, diff); minPl = Math.Min(minPl, diff);
                        if (Math.Abs(diff) <= 1.0) within1Pl++;
                    }
                    else if (tv < tPeak - 20.0)
                    {
                        fl++; sFl += diff; maxFl = Math.Max(maxFl, diff); minFl = Math.Min(minFl, diff);
                        if (Math.Abs(diff) <= 3.0) within3Fl++;
                    }
                    else nMid++;
                }
            // their reachable cells at nodes we did not emit at all count as theirs-only
            foreach (var kv in tCells)
                if (!seen.Contains(kv.Key) && kv.Value > MaskLatBlock.UnreachableDb + 1) oT++;
            nPlateau += pl; nFloor += fl; onlyOurs += oO; onlyTheirs += oT; sumPl += sPl; sumFl += sFl;
            perLat.Add((ob.LatDeg, pl, pl > 0 ? sPl / pl : double.NaN, fl, fl > 0 ? sFl / fl : double.NaN, oO, oT));
        }
        double meanPl = nPlateau > 0 ? sumPl / nPlateau : double.NaN, meanFl = nFloor > 0 ? sumFl / nFloor : double.NaN;
        Console.WriteLine(string.Create(inv,
            $"cells vs theirs: plateau {nPlateau} (ours - theirs mean {meanPl:+0.0;-0.0} dB, range {minPl:+0.0;-0.0}..{maxPl:+0.0;-0.0}, {100.0 * within1Pl / Math.Max(1, nPlateau):F1}% within 1 dB); floor {nFloor} (mean {meanFl:+0.0;-0.0} dB, range {minFl:+0.0;-0.0}..{maxFl:+0.0;-0.0}, {100.0 * within3Fl / Math.Max(1, nFloor):F1}% within 3 dB); intermediate {nMid}; radiated only by us {onlyOurs}, only by them {onlyTheirs}"));

        // ---- 5. The record ----------------------------------------------------------------
        var sb = new StringBuilder();
        string safe = new string(theirs.SatName.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());
        sb.AppendLine($"# Mask parity: beamlab's composition of the STEAM-2 case vs the filed {theirs.SatName} mask");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"*Produced by `dotnet run --project tests/radians.beamlab.checks -- parity \"{Path.GetFileName(filedMaskPath)}\" {latStepDeg:F0}`.*"));
        sb.AppendLine(string.Create(inv, $"*Date: {DateTime.Now:yyyy-MM-dd}. Wall clock {t0.Elapsed.TotalMinutes:F1} min.*"));
        sb.AppendLine();
        sb.AppendLine("## The question");
        sb.AppendLine();
        sb.AppendLine("Is the beam composition written down for this case -- the operation profile plus the design document, composed exactly as a simulation composes them -- an operable system consistent with the operator's filed declaration? Beamlab exports its own satellite-frame mask from that composition; both masks go through the same dissection; the rules read off each are set side by side and the cells differenced on the common grid.");
        sb.AppendLine();
        sb.AppendLine("## The case as composed");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"- Shell: {shell.PlaneCount} x {shell.SatsPerPlane} at {alt:F0} km, inclination {shell.InclinationDeg:F1} deg, inter-plane phase {shell.InterPlanePhaseDeg?.ToString("F1", inv) ?? "Walker F"} deg."));
        sb.AppendLine(string.Create(inv, $"- Rules: minimum elevation {prof.MinElevDeg:F0} deg, exclusion alpha {prof.AlphaExclDeg:F0} deg, Nco {prof.NcoPerCell}, selection {prof.TrackingPolicy}."));
        sb.AppendLine(string.Create(inv, $"- Payload (assumed where the filing is silent): power mode '{prof.Down.PowerMode}', peak gain {prof.Down.GainPeakDbi} dBi, Tx density {prof.Down.TxEirpDbw} dBW/{prof.Down.RefBwKHz:F0} kHz, pattern floor {prof.Down.PatternFloorDbi} dBi, pattern {(string.IsNullOrEmpty(prof.Down.PatternKind) ? "scene default" : prof.Down.PatternKind)}."));
        sb.AppendLine(string.Create(inv, $"- Our export: latitude {-latMax:F0}..{latMax:F0} step {latStepDeg:F0}, az/el 1 deg; theirs: {theirs.Blocks.Count} blocks at 1 deg."));
        sb.AppendLine();
        sb.AppendLine("## Rules read off each mask");
        sb.AppendLine();
        sb.AppendLine("| rule | ours (composition) | theirs (filed) |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine(string.Create(inv, $"| minimum elevation (deg) | {ra.ElevMinLo:F1}-{ra.ElevMinHi:F1} | {rb.ElevMinLo:F1}-{rb.ElevMinHi:F1} |"));
        sb.AppendLine(string.Create(inv, $"| exclusion alpha (deg) | {ra.Alpha0Lo:F1}-{ra.Alpha0Hi:F1}, {(ra.AlphaConstant ? "constant" : "varying")} | {rb.Alpha0Lo:F1}-{rb.Alpha0Hi:F1}, {(rb.AlphaConstant ? "constant" : "varying")} |"));
        sb.AppendLine(string.Create(inv, $"| alpha-limited latitudes | {ra.AlphaLimitedFrom:F0}..{ra.AlphaLimitedTo:F0} ({ra.AlphaLimitedCount}) | {rb.AlphaLimitedFrom:F0}..{rb.AlphaLimitedTo:F0} ({rb.AlphaLimitedCount}) |"));
        sb.AppendLine(string.Create(inv, $"| pfd cap (dB(W/m2)/{theirs.RefBwKHz:F0} kHz) | {ra.PeakAll:F1}, spread {ra.PlateauSpread:F2} ({(ra.FlatCap ? "flat" : "range-shaped")}) | {rb.PeakAll:F1}, spread {rb.PlateauSpread:F2} ({(rb.FlatCap ? "flat" : "range-shaped")}) |"));
        sb.AppendLine(string.Create(inv, $"| e.i.r.p. density nadir / edge (dBW/{theirs.RefBwKHz:F0} kHz) | {ra.EirpAtNadir:F1} / {ra.EirpAtEdge:F1} | {rb.EirpAtNadir:F1} / {rb.EirpAtEdge:F1} |"));
        sb.AppendLine(string.Create(inv, $"| side-lobe floor (dB) | {ra.FloorMinAll:F1}..{ra.FloorMaxAll:F1} ({ra.PeakAll - ra.FloorMaxAll:F0} below peak) | {rb.FloorMinAll:F1}..{rb.FloorMaxAll:F1} ({rb.PeakAll - rb.FloorMaxAll:F0} below peak) |"));
        sb.AppendLine(string.Create(inv, $"| intermediate cells | {ra.MidAll} | {rb.MidAll} |"));
        sb.AppendLine();
        sb.AppendLine("## Cells on the common grid (classes by the filed mask)");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"- Plateau cells: {nPlateau}; ours minus theirs mean {meanPl:+0.0;-0.0} dB, range {minPl:+0.0;-0.0}..{maxPl:+0.0;-0.0}; {100.0 * within1Pl / Math.Max(1, nPlateau):F1}% within 1 dB."));
        sb.AppendLine(string.Create(inv, $"- Floor cells: {nFloor}; mean {meanFl:+0.0;-0.0} dB, range {minFl:+0.0;-0.0}..{maxFl:+0.0;-0.0}; {100.0 * within3Fl / Math.Max(1, nFloor):F1}% within 3 dB."));
        sb.AppendLine(string.Create(inv, $"- Radiated by us only: {onlyOurs}; by them only: {onlyTheirs}; intermediate (theirs between plateau and floor): {nMid}."));
        sb.AppendLine();
        sb.AppendLine("| lat | plateau cells | ours - theirs, plateau mean (dB) | floor cells | ours - theirs, floor mean (dB) | ours only | theirs only |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var p in perLat)
            sb.AppendLine(string.Create(inv, $"| {p.lat:F0} | {p.pl} | {p.meanPl:+0.0;-0.0} | {p.fl} | {p.meanFl:+0.0;-0.0} | {p.onlyO} | {p.onlyT} |"));
        sb.AppendLine();
        sb.AppendLine("## Reading it");
        sb.AppendLine();
        sb.AppendLine("- Rules agreeing (elevation, alpha, cap) means the composition obeys the same operating rules the filing encodes -- the declaration is a plausible envelope of THIS operable system.");
        sb.AppendLine("- Plateau residue is power: a flat offset is the assumed gain/power pair against the filed cap; a range-shaped one is the power-control mode.");
        sb.AppendLine("- Floor residue is the pattern: our side lobes against their 30 dB-down envelope. It prices how much of a margin figure on this case would be pattern assumption rather than rule.");
        sb.AppendLine("- Cells radiated by one side only are the disc edge and any coverage disagreement; they should be a thin rim, not a region.");
        string outPath = Path.Combine(repo, "docs", $"mask-parity-{safe}.md");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine("figure: " + Path.GetRelativePath(repo, outPath));
        return 0;
    }
}
