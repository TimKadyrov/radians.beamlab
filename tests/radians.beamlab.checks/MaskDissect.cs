using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using radians.beamlab;
using radians.beamlab.app;

// The console and document front end of the mask dissection. The analysis
// itself (MaskDissect.Analyze / RulesSummary) lives in the app assembly, where
// the dataset generator's consistency probe reads it too.
//
// Run:  dotnet run --project tests/radians.beamlab.checks -- dissect <mask.xml> [altitudeKm]
// Output: docs/mask-dissection-<sat>.md (+ console table). MaskParity runs
// the same Analyze() on beamlab's own export of a case and diffs the rules.
internal static class MaskDissectCli
{
    public static int Run(string maskPath, double altitudeKm)
    {
        var inv = CultureInfo.InvariantCulture;
        var t0 = Stopwatch.StartNew();
        string repo = Directory.Exists(@"C:\Projects\radians.beamlab")
            ? @"C:\Projects\radians.beamlab" : AppContext.BaseDirectory;
        if (!File.Exists(maskPath)) { Console.WriteLine("ABORT: mask not found: " + maskPath); return 2; }

        Console.WriteLine("loading " + maskPath + " ...");
        var mask = MaskXmlImport.Load(maskPath);
        Console.WriteLine(string.Create(inv,
            $"{mask.SatName} ntc {mask.NtcId} mask {mask.MaskId}: {mask.Kind}, {mask.LowFreqMhz}-{mask.HighFreqMhz} MHz, refbw {mask.RefBwKHz} kHz, {mask.Blocks.Count} latitude blocks"));
        if (mask.Kind != MaskPlotKind.AzEl)
        {
            Console.WriteLine("ABORT: this dissection reads the satellite-frame (azimuth/elevation) form only.");
            return 2;
        }
        var res = MaskDissect.Analyze(mask, altitudeKm);

        Console.WriteLine("lat | peak | plateau cells | plateau: min ground elev / min alpha / max off-nadir | floor: max alpha (elev ok) / max elev (alpha ok) | plateau N-S pointing extent");
        foreach (var r in res.Lats)
        {
            if (Math.Abs(r.Lat) % 10 > 0.5 && Math.Abs(Math.Abs(r.Lat) - 55) > 0.5 && Math.Abs(Math.Abs(r.Lat) - 25) > 0.5) continue;
            Console.WriteLine(string.Create(inv,
                $"{r.Lat,4:F0} | {r.Peak:F1} | {r.Plateau,5} | {r.PlateauMinElev:F1} / {r.PlateauMinAlpha:F1} / {r.PlateauMaxOffNadir:F1} | {r.FloorMaxAlphaAboveElev:F1} / {r.FloorMaxElevAboveAlpha:F1} | {r.PlateauElMin:F0}..{r.PlateauElMax:F0}"));
        }
        Console.WriteLine(MaskDissect.RulesSummary(res, inv));

        var sb = new StringBuilder();
        string safe = new string(mask.SatName.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());
        sb.AppendLine($"# Mask dissection: {mask.SatName} (ntc_id {mask.NtcId}, mask_id {mask.MaskId})");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"*Produced by `dotnet run --project tests/radians.beamlab.checks -- dissect \"{Path.GetFileName(maskPath)}\" {altitudeKm:F0}`.*"));
        sb.AppendLine(string.Create(inv, $"*Date: {DateTime.Now:yyyy-MM-dd}. Wall clock {t0.Elapsed.TotalMinutes:F1} min.*"));
        sb.AppendLine();
        sb.AppendLine("## What is read, and how");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"A filed S.1503-4 pfd mask in the satellite-frame (azimuth/elevation) form: {mask.Blocks.Count} latitude blocks, {mask.LowFreqMhz}-{mask.HighFreqMhz} MHz, reference bandwidth {mask.RefBwKHz} kHz. Each cell is a direction the satellite may radiate toward at a given sub-satellite latitude. The cells are mapped to the ground through the same frame the examination reads the mask with (NED at the sub-satellite point: azimuth = atan2(east, down), elevation = asin(north)), for a satellite at {altitudeKm:F0} km, and each ground point is given its satellite elevation angle and its GSO-arc alpha. The mask's main-beam plateau (within 3 dB of the block peak) is the set of allowed targets; the floor (more than 20 dB below the peak) is where no beam points. The plateau's boundaries in ground elevation and in alpha are the operating rules -- read per latitude, so a latitude-dependent exclusion would show as a varying alpha boundary, a constant rule as a constant one seen through geometry."));
        sb.AppendLine();
        sb.AppendLine("## Structure");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"- Cells reaching the Earth: {res.Reaching}; on the plateau {res.PlateauAll}, intermediate {res.MidAll} -- {(res.MidAll == 0 ? "a **two-level mask**" : "a shaped mask")}: main beam at {res.PeakAll:F1} dB, side-lobe floor {res.FloorMinAll:F1}..{res.FloorMaxAll:F1} dB ({res.PeakAll - res.FloorMaxAll:F0} dB below the peak, falling toward the horizon with range). Every block radiates over the whole visible Earth; the operating rules are expressed as levels, not as the -1000 hole."));
        sb.AppendLine(string.Create(inv, $"- Distinct integer levels: {res.Levels.Count}."));
        if (res.IdenticalSpans.Count > 0)
            sb.AppendLine("- Byte-identical block runs (the rules stop depending on latitude there): "
                + string.Join("; ", res.IdenticalSpans.Select(s => string.Create(inv, $"{s.from:F0}..{s.to:F0}"))) + ".");
        sb.AppendLine();
        sb.AppendLine("## Per latitude");
        sb.AppendLine();
        sb.AppendLine("Plateau = allowed targets. \"floor: max alpha (elev ok)\" is the largest alpha among excluded targets that clear the elevation floor -- the exclusion boundary seen from below; \"max elev (alpha ok)\" the largest elevation among excluded targets that clear the alpha floor -- the elevation boundary seen from below.");
        sb.AppendLine();
        sb.AppendLine("| lat | peak (dB) | plateau cells | plateau min ground elev | plateau min alpha | plateau max off-nadir | floor max alpha (elev ok) | floor max elev (alpha ok) | plateau N-S pointing extent |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var r in res.Lats)
        {
            if (Math.Abs(r.Lat) % 5 > 0.5) continue;
            sb.AppendLine(string.Create(inv,
                $"| {r.Lat:F0} | {r.Peak:F1} | {r.Plateau} | {r.PlateauMinElev:F1} | {r.PlateauMinAlpha:F1} | {r.PlateauMaxOffNadir:F1} | {(r.FloorMaxAlphaAboveElev < 0 ? "-" : r.FloorMaxAlphaAboveElev.ToString("F1", inv))} | {(r.FloorMaxElevAboveAlpha < 0 ? "-" : r.FloorMaxElevAboveAlpha.ToString("F1", inv))} | {r.PlateauElMin:F0}..{r.PlateauElMax:F0} |"));
        }
        sb.AppendLine();
        sb.AppendLine("## The operating rules this mask encodes");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"- **Minimum elevation ~ {res.ElevMinLo:F1}-{res.ElevMinHi:F1} deg** (bracketed by the grid): the plateau's outer edge sits at the same ground elevation at every latitude."));
        sb.AppendLine(string.Create(inv, $"- **GSO exclusion alpha ~ {res.Alpha0Lo:F1}-{res.Alpha0Hi:F1} deg**, alpha-limited at latitudes {res.AlphaLimitedFrom:F0}..{res.AlphaLimitedTo:F0} and inert beyond (there every target clearing the elevation floor also clears alpha). {(res.AlphaConstant ? "The boundary alpha is the SAME at every alpha-limited latitude: one constant rule, whose hole in (az, el) changes shape with latitude purely through geometry -- a per-latitude MIN_EXCLUDE table would be flat." : "The boundary alpha VARIES with latitude: this operator's exclusion is latitude-dependent, i.e. a genuine per-latitude MIN_EXCLUDE.")}"));
        sb.AppendLine(res.FlatCap
            ? string.Create(inv, $"- **A flat pfd cap of {res.PeakAll:F1} dB(W/m2) per {mask.RefBwKHz:F0} kHz, independent of range** (plateau spread {res.PlateauSpread:F2} dB out to the elevation edge): constant-boresight-PFD power control, not a constant e.i.r.p. seen through spreading. The boresight e.i.r.p. density therefore runs from {res.EirpAtNadir:F1} dBW/{mask.RefBwKHz:F0} kHz at nadir to {res.EirpAtEdge:F1} at the edge (slant range {res.EdgeSlantKm:F0} km), with the side-lobe envelope {res.PeakAll - res.FloorMaxAll:F0} dB down.")
            : string.Create(inv, $"- **Range-shaped plateau** (spread {res.PlateauSpread:F2} dB): a constant e.i.r.p. seen through spreading; boresight e.i.r.p. density ~ {res.EirpAtNadir:F1} dBW/{mask.RefBwKHz:F0} kHz at nadir, {res.EirpAtEdge:F1} at the peak cell (slant range {res.EdgeSlantKm:F0} km), side-lobe envelope {res.PeakAll - res.FloorMaxAll:F0} dB down."));
        sb.AppendLine();
        sb.AppendLine("## Against the filed R set");
        sb.AppendLine();
        sb.AppendLine("The operating-parameter XML of the same filing declares min_exclude 22 deg (all orbits, one latitude row) and no elev_angle at all; the contribution text states a 40 deg minimum elevation as its simulation assumption. The mask above says which of those the payload actually enforces -- and per latitude.");
        string outPath = Path.Combine(repo, "docs", $"mask-dissection-{safe}.md");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine("figure: " + Path.GetRelativePath(repo, outPath));
        return 0;
    }
}
