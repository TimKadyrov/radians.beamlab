using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// The mask-versus-declaration consistency check, for the case where the pfd
/// mask is GIVEN rather than derived. A pfd mask is a per-direction envelope:
/// gates that act on boresights (MIN_EXCLUDE, MIN_ELEV) cannot be applied to
/// it after the fact, because the maximum has collapsed which configuration
/// produced each cell. What can be asked is whether the mask already carries
/// the shaping the R set declares. Per latitude block of an az/el mask, the
/// dissection's near-peak region (within 3 dB of the block peak) is compared
/// with the declared gates at that latitude: how far inside the declared
/// exclusion zone, and how far below the declared elevation floor, near-peak
/// power reaches. Verdicts are graded: CONSISTENT (the mask's own edge sits at
/// the gate), MASK TIGHTER (dark beyond the gate: the gate is epfd-inert), LIT
/// INSIDE (near-peak power reaches inside the gate and stops short of the arc
/// or horizon: a gate declared wider than the mask's edge, or the main-lobe
/// edge of beams gated at their boresight), SATURATED (near-peak power reaches
/// the arc or the horizon: no shaping at all -- beside declared gates, the
/// saturation-shaped mask the dataset's consistency probe is built to detect).
/// The examination over-charges such masks and never under-protects; the
/// check flags, it does not repair.
///
/// Lives in the app assembly so that the harness and the dataset generator
/// grade with one implementation.
/// </summary>
public static class MaskConsistency
{
    /// <summary>One az/el cell of the mask grid: differences inside it are not evidence.</summary>
    public const double CellTolDeg = 1.0;

    public enum Verdict { Consistent, MaskTighter, LitInside, Saturated, NotExercised, Dark }

    public sealed record RowResult(
        double LatDeg,
        double ReachAlpha, double DarkAlpha, double DeclaredAlpha, Verdict Alpha,
        double ReachElev, double DarkElev, double DeclaredElev, Verdict Elev);

    public sealed record Report(IReadOnlyList<RowResult> Rows, Verdict Overall, string Summary, string Caveat, string Note);

    /// <summary>Load the mask and check it; masks not in the az/el form are reported as not applicable.</summary>
    public static Report Check(string maskPath, double altitudeKm, OperatingParamsSet declared)
    {
        var mask = MaskXmlImport.Load(maskPath);
        if (mask.Kind != MaskPlotKind.AzEl)
            return new Report(Array.Empty<RowResult>(), Verdict.NotExercised,
                "not applicable: the consistency check reads the satellite-frame (azimuth/elevation) form only",
                CaveatText, "");
        return Check(MaskDissect.Analyze(mask, altitudeKm), declared);
    }

    /// <summary>
    /// The exclusion axis of a mask in the alpha/deltaLongitude form, which needs
    /// no geometry: the b axis IS the angle to the arc seen from the earth
    /// station. Per latitude block, the smallest |alpha| node still within 3 dB
    /// of the block peak is the near-peak reach; the grade follows the same
    /// thresholds as the az/el check. The elevation axis is not readable in this
    /// form without the ground mapping and is reported NOT EXERCISED; the dark
    /// edge is not evaluated either, so MASK TIGHTER is never returned here.
    /// </summary>
    public static Report CheckAlphaForm(LoadedPfdMask mask, OperatingParamsSet declared)
    {
        if (mask.Kind != MaskPlotKind.AlphaDeltaLong)
            return new Report(Array.Empty<RowResult>(), Verdict.NotExercised,
                "not applicable: CheckAlphaForm reads the alpha/deltaLongitude form only", CaveatText, "");
        var rows = new List<RowResult>();
        foreach (var blk in mask.Blocks)
        {
            double peak = double.NegativeInfinity;
            foreach (var row in blk.Rows) foreach (var v in row.Values) if (v > MaskLatBlock.UnreachableDb + 1) peak = Math.Max(peak, v);
            double a0 = DeclaredAlphaDeg(declared, blk.LatDeg);
            double e0 = DeclaredElevDeg(declared, blk.LatDeg);
            if (double.IsNegativeInfinity(peak))
            {
                rows.Add(new RowResult(blk.LatDeg, 999, -1, a0, Verdict.Dark, 999, -1, e0, Verdict.Dark));
                continue;
            }
            double reachA = 999;
            foreach (var row in blk.Rows)
                if (row.Values.Any(v => v >= peak - 3.0)) reachA = Math.Min(reachA, Math.Abs(row.B));
            Verdict va = a0 <= 0.0 ? Verdict.Consistent
                : reachA >= a0 - CellTolDeg ? Verdict.Consistent
                : reachA <= CellTolDeg ? Verdict.Saturated
                : Verdict.LitInside;
            rows.Add(new RowResult(blk.LatDeg, reachA, -1, a0, va, 999, -1, e0, Verdict.NotExercised));
        }
        var lit = rows.Where(x => x.Alpha != Verdict.Dark).ToList();
        int CountA(Verdict v) => lit.Count(x => x.Alpha == v);
        Verdict overall = lit.Any(x => x.Alpha == Verdict.Saturated) ? Verdict.Saturated
            : lit.Any(x => x.Alpha == Verdict.LitInside) ? Verdict.LitInside
            : lit.Count == 0 ? Verdict.Dark : Verdict.Consistent;
        var inv = CultureInfo.InvariantCulture;
        var worst = lit.OrderBy(x => x.ReachAlpha).FirstOrDefault();
        string sum = string.Create(inv,
            $"exclusion (alpha form): consistent {CountA(Verdict.Consistent)}, lit inside {CountA(Verdict.LitInside)}, saturated {CountA(Verdict.Saturated)}; dark blocks {rows.Count - lit.Count} of {rows.Count}")
            + (worst is null ? "" : string.Create(inv, $"; near-peak power reaches alpha {worst.ReachAlpha:F1} deg against a declared {worst.DeclaredAlpha:F1} (block {worst.LatDeg:0.#})"))
            + "; elevation axis not readable in this form -> " + Word(overall);
        return new Report(rows, overall, sum, CaveatText, "");
    }

    public static Report Check(MaskDissect.Result d, OperatingParamsSet declared)
    {
        var rows = new List<RowResult>();
        foreach (var r in d.Lats)
        {
            double a0 = DeclaredAlphaDeg(declared, r.Lat);
            double e0 = DeclaredElevDeg(declared, r.Lat);
            // Near-peak reach: the smallest alpha / lowest ground elevation at
            // which the block still radiates within 3 dB of its peak. Dark
            // edge: the largest alpha / elevation among cells 20 dB or more
            // down, seen from the excluded side.
            double reachA = r.PlateauMinAlpha, darkA = r.FloorMaxAlphaAboveElev;
            double reachE = r.PlateauMinElev, darkE = r.FloorMaxElevAboveAlpha;
            if (r.Plateau == 0)
            {
                rows.Add(new RowResult(r.Lat, reachA, darkA, a0, Verdict.Dark, reachE, darkE, e0, Verdict.Dark));
                continue;
            }
            Verdict va;
            if (a0 <= 0.0)
                va = darkA > CellTolDeg ? Verdict.MaskTighter : Verdict.Consistent;      // no zone declared
            else if (reachA >= a0 - CellTolDeg)
                va = darkA > a0 + CellTolDeg ? Verdict.MaskTighter                       // dark beyond the zone
                   : darkA < 0.0 && reachA > a0 + CellTolDeg ? Verdict.NotExercised      // no cell reaches the zone
                   : Verdict.Consistent;
            else if (reachA <= CellTolDeg)
                va = Verdict.Saturated;                                                   // lit to the arc itself
            else
                va = Verdict.LitInside;                                                   // lit inside, stops short

            Verdict ve;
            if (e0 <= 0.0)
                ve = darkE > CellTolDeg ? Verdict.MaskTighter : Verdict.Consistent;      // no floor declared
            else if (reachE >= e0 - CellTolDeg)
                ve = darkE > e0 + CellTolDeg ? Verdict.MaskTighter : Verdict.Consistent; // dark above the floor
            else if (reachE <= CellTolDeg)
                ve = Verdict.Saturated;                                                   // lit to the horizon
            else
                ve = Verdict.LitInside;                                                   // lit below, stops short

            rows.Add(new RowResult(r.Lat, reachA, darkA, a0, va, reachE, darkE, e0, ve));
        }

        var lit = rows.Where(x => x.Alpha != Verdict.Dark).ToList();
        int CountA(Verdict v) => lit.Count(x => x.Alpha == v);
        int CountE(Verdict v) => lit.Count(x => x.Elev == v);
        bool Any(Verdict v) => lit.Any(x => x.Alpha == v || x.Elev == v);
        Verdict overall =
            Any(Verdict.Saturated) ? Verdict.Saturated
            : Any(Verdict.LitInside) ? Verdict.LitInside
            : Any(Verdict.MaskTighter) ? Verdict.MaskTighter
            : lit.Count == 0 ? Verdict.Dark
            : Verdict.Consistent;

        var inv = CultureInfo.InvariantCulture;
        var sum = new StringBuilder();
        sum.Append(string.Create(inv,
            $"exclusion: consistent {CountA(Verdict.Consistent)}, not exercised {CountA(Verdict.NotExercised)}, mask tighter {CountA(Verdict.MaskTighter)}, "
            + $"lit inside {CountA(Verdict.LitInside)}, saturated {CountA(Verdict.Saturated)}; "
            + $"elevation: consistent {CountE(Verdict.Consistent)}, mask tighter {CountE(Verdict.MaskTighter)}, lit inside {CountE(Verdict.LitInside)}, "
            + $"saturated {CountE(Verdict.Saturated)}; dark blocks {rows.Count - lit.Count} of {rows.Count}"));
        var inA = lit.Where(x => x.Alpha is Verdict.LitInside or Verdict.Saturated).ToList();
        if (inA.Count > 0)
        {
            var worst = inA.OrderBy(x => x.ReachAlpha).First();
            sum.Append(string.Create(inv, $"; near-peak power reaches alpha {worst.ReachAlpha:F1} deg against a declared {worst.DeclaredAlpha:F1} (block {worst.LatDeg:0.#})"));
        }
        var inE = lit.Where(x => x.Elev is Verdict.LitInside or Verdict.Saturated).ToList();
        if (inE.Count > 0)
        {
            var worst = inE.OrderBy(x => x.ReachElev).First();
            sum.Append(string.Create(inv, $"; near-peak power reaches ground elevation {worst.ReachElev:F1} deg against a declared {worst.DeclaredElev:F1} (block {worst.LatDeg:0.#})"));
        }
        sum.Append(" -> " + Word(overall));

        var notes = new List<string>();
        foreach (var v in new[] { Verdict.Saturated, Verdict.LitInside, Verdict.MaskTighter, Verdict.NotExercised })
        {
            string ra = Ranges(lit.Where(x => x.Alpha == v).Select(x => x.LatDeg), inv);
            if (ra.Length > 0) notes.Add($"exclusion {Word(v).ToLowerInvariant()} at sub-satellite latitude {ra}");
            string re = Ranges(lit.Where(x => x.Elev == v).Select(x => x.LatDeg), inv);
            if (re.Length > 0 && v != Verdict.NotExercised) notes.Add($"elevation {Word(v).ToLowerInvariant()} at sub-satellite latitude {re}");
        }
        return new Report(rows, overall, sum.ToString(), CaveatText, string.Join("; ", notes));
    }

    public static string Word(Verdict v) => v switch
    {
        Verdict.Consistent => "CONSISTENT",
        Verdict.MaskTighter => "MASK TIGHTER THAN DECLARED",
        Verdict.LitInside => "LIT INSIDE THE DECLARED GATE",
        Verdict.Saturated => "SATURATED (no shaping)",
        Verdict.NotExercised => "NOT EXERCISED",
        _ => "DARK",
    };

    private const string CaveatText =
        "Mask blocks are indexed by sub-satellite latitude; the R set's arrays by earth-station latitude. "
        + "The comparison is exact where the declared arrays are flat and approximate within the coverage "
        + "half-angle where they vary. Near-peak means within 3 dB of the block's peak; dark means 20 dB or "
        + "more below it; tolerance one mask cell (1.0 deg).";

    /// <summary>
    /// The declared exclusion angle at a latitude: the Rec's linear interpolation
    /// (Part B) through the same resolver the examination uses, the least
    /// restrictive table when several orbits declare their own.
    /// </summary>
    public static double DeclaredAlphaDeg(OperatingParamsSet p, double latDeg)
    {
        var tables = p.MinExclude.Where(e => e.ByLat.Count > 0).ToList();
        if (tables.Count == 0) return 0.0;
        return tables.Min(e => DeclaredConstraints.ExclusionAlphaDeg(p, latDeg, e.OrbId));
    }

    /// <summary>
    /// The declared elevation floor at a latitude: the nearest declared row
    /// (Sec. D5.1.5 step 1), the end rows governing beyond the array, the
    /// smallest azimuth value of that row; else the header value; else none.
    /// </summary>
    public static double DeclaredElevDeg(OperatingParamsSet p, double latDeg)
    {
        var blocks = p.MinElev.Where(b => b.ByAz.Count > 0).ToList();
        if (blocks.Count == 0) return p.ElevAngleHeaderDeg ?? 0.0;
        var nearest = blocks.OrderBy(b => Math.Abs(b.LatDeg - latDeg)).ThenBy(b => b.LatDeg).First();
        return nearest.ByAz.Min(r => r.ElevDeg);
    }

    /// <summary>Compact "a..b, c, d..e" rendering of a set of block latitudes (1 deg blocks).</summary>
    public static string Ranges(IEnumerable<double> lats, CultureInfo inv)
    {
        var s = lats.OrderBy(x => x).ToList();
        if (s.Count == 0) return "";
        var parts = new List<string>();
        int i = 0;
        while (i < s.Count)
        {
            int j = i;
            while (j + 1 < s.Count && s[j + 1] - s[j] <= 1.0 + 1e-9) j++;
            parts.Add(j > i ? string.Create(inv, $"{s[i]:0.#}..{s[j]:0.#}") : string.Create(inv, $"{s[i]:0.#}"));
            i = j + 1;
        }
        return string.Join(", ", parts);
    }

    /// <summary>The record section: what was compared, the verdicts, what they mean.</summary>
    public static void AppendSection(StringBuilder sb, Report rep, CultureInfo inv, string declaredLabel)
    {
        sb.AppendLine("## Mask consistency");
        sb.AppendLine();
        sb.AppendLine("The mask was given, not derived, so the question is whether it already carries the shaping the "
            + declaredLabel + " declares. Per latitude block of the mask: how far inside the declared MIN_EXCLUDE zone, "
            + "and how far below the declared MIN_ELEV floor, power within 3 dB of the block's peak reaches.");
        sb.AppendLine();
        sb.AppendLine("- " + rep.Summary);
        if (rep.Note.Length > 0) sb.AppendLine("- " + rep.Note + ".");
        sb.AppendLine("- **Overall: " + Word(rep.Overall) + ".** " + Meaning(rep.Overall));
        sb.AppendLine("- " + rep.Caveat);
        if (rep.Rows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("| sub-satellite latitude | near-peak reaches alpha (deg) | declared MIN_EXCLUDE | exclusion | near-peak reaches elevation (deg) | declared MIN_ELEV | elevation |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var r in rep.Rows.Where(x => x.Alpha != Verdict.Dark && Math.Abs(x.LatDeg) % 10 < 0.5))
                sb.AppendLine(string.Create(inv,
                    $"| {r.LatDeg:0.#} | {r.ReachAlpha:F1} | {r.DeclaredAlpha:F1} | {Word(r.Alpha)} | {r.ReachElev:F1} | {r.DeclaredElev:F1} | {Word(r.Elev)} |"));
            sb.AppendLine();
            sb.AppendLine("Every tenth block is tabulated; the counts above cover all of them.");
        }
    }

    /// <summary>What a grade means, in the words the records use.</summary>
    public static string Meaning(Verdict v) => v switch
    {
        Verdict.Saturated => "Near-peak power reaches the GSO arc or the horizon: the mask carries no exclusion or "
            + "elevation shaping at all. Beside declared gates this is a saturation-shaped mask. The examination counts "
            + "in-zone satellites near the victim's boresight regardless of the zone (Step 22, main-beam satellites), so "
            + "it over-charges the operator and never under-protects the GSO; the declaration pair is self-inconsistent "
            + "and is flagged here, not repaired.",
        Verdict.LitInside => "Near-peak power reaches inside the declared gate and stops short of the arc or horizon. Two "
            + "readings, which the mask alone cannot separate: a gate declared wider than the mask's own edge (a hard edge "
            + "inside the zone), or the main-lobe edge of beams gated at their boresight reaching neighbouring ground (a "
            + "soft reach of about one footprint, what a physically derived mask shows at its cell size). Either way the "
            + "gate does not describe where the mask's power goes. In the examination these satellites are counted "
            + "whether the zone admits them or not -- as main-beam satellites when inside it (Step 22), first in the "
            + "capped pick when not -- which is why a declared exclusion measures epfd-inert against such a mask.",
        Verdict.MaskTighter => "The mask is dark beyond the declared gate, so the gate is epfd-inert for this mask: "
            + "the R set relabels satellites the mask already suppresses.",
        Verdict.Consistent => "The mask's own edges sit at the declared gates within one cell wherever the geometry "
            + "exercises them.",
        Verdict.NotExercised => "",
        _ => "No lit block to compare.",
    };
}
