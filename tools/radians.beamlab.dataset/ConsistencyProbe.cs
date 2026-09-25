using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using radians.beamlab;
using radians.beamlab.app;

namespace radians.beamlab.dataset;

/// <summary>
/// The section 3.10 declaration-consistency probe of the dataset design brief:
/// BL-C1 pairs an operating-parameter set that declares operational shaping
/// (an exclusion zone and an elevation floor) with pfd masks whose values
/// ignore it -- the reachable envelope of the same payload composed with no
/// boresight gate and no elevation floor: full load, no victim avoidance. The
/// pair is self-inconsistent; gates act on boresights and cannot be applied to
/// a per-direction envelope after the fact, so the correct consumer behaviour
/// is detection (a mask-versus-parameters consistency check), not adaptation.
///
/// The expectation record names the inconsistency in this producer's grade
/// vocabulary -- expected grade SATURATED on the exclusion axis for every
/// mask; on the elevation axis the near-peak grade reads CONSISTENT for a
/// range-shaped envelope with or without a floor, a limitation the record
/// states and supplements with the lit reach (power within 20 dB of the block
/// peak), which on this family does not separate them either: the beams spill
/// to the horizon with or without the floor, and the record prints both values
/// (measured 2026-09-12 and again 2026-09-20) -- and states the conservative
/// verdict a consumer that examines the pair anyway must reproduce: the
/// epfd(down) examination at seven victims, per limit point, with the
/// family's own boresight-gated D2 masks as the control at the same payload
/// level. The control has a limit the record states too: the family declares
/// no exclusion zone (its 450 km cells leave a boresight gate no trace in the
/// envelope -- the finding that led to that declaration, 2026-09-13), so the
/// control carries the elevation floor only, and the lit reach shows that
/// floor is not carried as an edge either.
/// </summary>
public static class ConsistencyProbe
{
    public const double DeclaredAlphaDeg = 8.0, DeclaredMinElevDeg = 10.0, MinAngleAtEsDeg = 2.5;
    public const int Nco = 2;
    /// <summary>Payload offset of the saturated masks against mask 1's: the body of the CDF inside the limit, the main-beam pass above it.</summary>
    public const double TxDeltaDb = -45.0;
    public static readonly double[] SweepLatsDeg = { 0.0, 10.0, 20.0, 30.0, 40.0, 50.0, 60.0 };
    /// <summary>The family's truth victim, whose examination CDF is written beside the record.</summary>
    public const double FamilyVictimLatDeg = 40.0;
    public const double EsLonDeg = 0.0, GsoLonDeg = 10.0;

    /// <summary>Set 30 (BL-C1): the shaping declared -- one row per quantity, global constants; classic algorithm.</summary>
    public static OperatingParamsSet Set30(int ntcId)
    {
        var (fMin, fMax) = DatasetGenerator.ConsistencyBandMhz;
        var s = new OperatingParamsSet
        {
            SatName = DatasetGenerator.SatName, NtcId = ntcId, ParamId = 30,
            LowFreqMhz = fMin, HighFreqMhz = fMax,
            EsDensityPerKm2 = 0.00012, EsDistanceKm = 300, EsLatMinDeg = -70, EsLatMaxDeg = 70,
            MinAngleAtEsDeg = MinAngleAtEsDeg,
        };
        s.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, DeclaredAlphaDeg) } });
        s.MinElev.Add(new MinElevByLat { LatDeg = 0.0, ByAz = { (0.0, DeclaredMinElevDeg), (360.0, DeclaredMinElevDeg) } });
        s.MaxCoFreqByLat.Add((0.0, Nco));
        return s;
    }

    /// <summary>A probe mask as the emission sees it: its file, the shell it is linked to and the altitude the dissection maps it at.</summary>
    public sealed record ProbeMask(string Path, string Shell, double AltitudeKm, int MaskId);

    public sealed record Emitted(string RecordPath, IReadOnlyList<string> Files, string Headline);

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>BL-C1: grades, the conservative verdict with its control, the record and the tables.</summary>
    public static Emitted Emit(string caseDir, IReadOnlyList<ProbeMask> masks, IReadOnlyList<ProbeMask> controlMasks,
        string paramPath, OperatingParamsSet s30, ProbeExamination.LimitRow lim, bool quick, string provenance)
    {
        var inv = CultureInfo.InvariantCulture;
        // ---- the inconsistency, graded ------------------------------------------
        var grades = masks.Select(m => Grade(m, s30)).ToList();
        var controlGrades = controlMasks.Select(m => Grade(m, s30)).ToList();

        // ---- the conservative verdict ---------------------------------------------
        var con = new Constellation(DatasetGenerator.Shells);
        double freqMhz = 0.5 * (s30.LowFreqMhz + s30.HighFreqMhz);
        double duration = ReadRuleProbes.Duration(quick);
        IMaskPfdRead saturated = new ProbeExamination.ShellMaskRead(masks.Select(m => (IPureMaskPfdRead)MaskFootprint.LoadFile(m.Path)));
        IMaskPfdRead control = new ProbeExamination.ShellMaskRead(controlMasks.Select(m =>
            (IPureMaskPfdRead)new ProbeExamination.OffsetMaskRead(MaskFootprint.LoadFile(m.Path), TxDeltaDb)));
        var rows = new List<(double Lat, ProbeExamination.Verdict Sat, ProbeExamination.Verdict SatHalf, ProbeExamination.Verdict Ctl)>();
        var d4s = new List<(string Where, ProbeExamination.D4Verdicts V)>();
        foreach (double lat in SweepLatsDeg)
        {
            var sat = ProbeExamination.ExamineD4(con, DatasetGenerator.Shells, saturated, s30, lim, freqMhz, lat, EsLonDeg, GsoLonDeg, duration);
            var ctl = ProbeExamination.ExamineD4(con, DatasetGenerator.Shells, control, s30, lim, freqMhz, lat, EsLonDeg, GsoLonDeg, duration);
            rows.Add((lat, sat.Fine, sat.FineHalf, ctl.Fine));
            d4s.Add((string.Create(inv, $"{lat:F0} N, saturated masks"), sat));
            d4s.Add((string.Create(inv, $"{lat:F0} N, control"), ctl));
        }
        var plan = d4s[0].V.Plan;
        long fineSteps = d4s[0].V.FineSteps;
        var family = rows.First(r => r.Lat == FamilyVictimLatDeg);

        // ---- files ----------------------------------------------------------------
        string expDir = Path.Combine(caseDir, "expected");
        Directory.CreateDirectory(expDir);
        var files = new List<string>();

        string csv = Path.Combine(expDir, "sweep_margins.csv");
        var cs = new StringBuilder();
        cs.AppendLine("# epfd(down) examination (S.1503-4 D5.1.4.1) over the saturated masks 14-16 under set 30, per victim latitude, ES lon 0, GSO 10 E; the row's own reference dish.");
        cs.AppendLine("# " + lim.Label);
        cs.AppendLine(string.Create(inv, $"# depth {duration / 3600.0:F0} h on the S.1503-4 time step, every fine step ({fineSteps}): {plan.Text}; half = the first half of the fine grid (extension pair); control = the family's boresight-gated masks 2-4 at the same payload offset."));
        cs.AppendLine("lat_deg,max_epfd_db,worst_margin_db,curve_margin_db,pass," + string.Join(",", lim.Points.Select(p => string.Create(inv, $"margin_at_{p.Perc:G4}pct_db")))
            + ",half_worst_margin_db,control_max_epfd_db,control_worst_margin_db,control_pass");
        foreach (var (lat, v, h, c) in rows)
            cs.AppendLine(string.Create(inv, $"{lat:F0},{v.MaxEpfdDb:F2},{v.WorstMarginDb:F2},{v.CurveMarginDb:F2},{(v.Pass ? 1 : 0)},")
                + string.Join(",", v.Points.Select(p => p.MarginDb.ToString("F2", inv)))
                + string.Create(inv, $",{h.WorstMarginDb:F2},{c.MaxEpfdDb:F2},{c.WorstMarginDb:F2},{(c.Pass ? 1 : 0)}"));
        File.WriteAllText(csv, cs.ToString(), Utf8NoBom);
        files.Add(Path.GetFileName(csv));

        string cdf = Path.Combine(expDir, string.Create(inv, $"examination_lat{FamilyVictimLatDeg:F0}_cdf.csv"));
        WriteCdf(cdf, family.Sat, plan, FamilyVictimLatDeg, s30, lim);
        files.Add(Path.GetFileName(cdf));

        // ---- the record -------------------------------------------------------------
        var sb = new StringBuilder();
        sb.AppendLine("# Expected outcome: the declaration and the masks describe different systems -- grade SATURATED; an examination that proceeds anyway is conservative (design brief Sec. 3.10, declaration-consistency probe)");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"Operating-parameter set param_id {s30.ParamId} ({s30.LowFreqMhz}-{s30.HighFreqMhz} MHz) declares operational shaping: an all-orbits exclusion zone MIN_EXCLUDE of {F(DeclaredAlphaDeg)} deg, an elevation floor MIN_ELEV of {F(DeclaredMinElevDeg)} deg (constant over azimuth), MAX_CO_FREQ {Nco}, MIN_ANGLE_AT_ES {F(MinAngleAtEsDeg)} deg (the classic algorithm, MIN_DURATION absent) -- one row per quantity, global constants under the total read; no header attribute for any array quantity. The three pfd masks ({string.Join(", ", masks.Select(m => m.MaskId))}: one per shell, azimuth/elevation form, linked per orbital-plane range through mask_lnk1) ignore that shaping: each is the reachable envelope of the same payload composed with NO boresight gate and NO elevation floor -- full load, no victim avoidance -- at a payload {F(-TxDeltaDb)} dB below the family's mask 1."));
        sb.AppendLine();
        sb.AppendLine("The pair is self-inconsistent, and the masks cannot be repaired from the set: the gates act on boresights, while a pfd mask is a per-direction envelope that has collapsed which beam produced each cell. The correct consumer behaviour is therefore detection, not adaptation -- a mask-versus-parameters consistency check of the kind WP4A Doc 4A/937 proposes (link feasibility over the field of view where the declared gates say service is possible; France's 4A/945 scenario B is the precedent of a deliberately inconsistent case run both ways) -- and then, if the examination proceeds, the conservative verdict stated below. A consumer that examines the pair silently, with no finding, has failed the case; so has one that alters the masks or the set to reconcile them.");
        sb.AppendLine();
        sb.AppendLine("## The inconsistency, graded");
        sb.AppendLine();
        sb.AppendLine("Grade vocabulary (this producer's mask consistency check; one grade per axis -- exclusion, elevation -- per latitude block of a mask; near-peak = within 3 dB of the block's peak, dark = 20 dB or more below it, tolerance one 1-degree cell):");
        sb.AppendLine();
        foreach (var v in new[] { MaskConsistency.Verdict.Consistent, MaskConsistency.Verdict.MaskTighter, MaskConsistency.Verdict.LitInside, MaskConsistency.Verdict.Saturated, MaskConsistency.Verdict.NotExercised, MaskConsistency.Verdict.Dark })
            sb.AppendLine("- **" + MaskConsistency.Word(v) + "** -- " + Vocabulary(v));
        sb.AppendLine();
        double satLitElev = grades.Min(g => LitReach(g.Dissection).Elev);
        double ctlLitElev = controlGrades.Min(g => LitReach(g.Dissection).Elev);
        string elevNote = Math.Abs(satLitElev - ctlLitElev) <= MaskConsistency.CellTolDeg
            ? string.Create(inv, $"The LIT REACH beside it -- the lowest elevation and the smallest alpha at which a block still radiates within 20 dB of its peak -- does not separate them either on this family: the saturated masks light the ground down to {satLitElev:F1} deg of elevation and the gated control masks down to {ctlLitElev:F1} deg, because at this beam size a beam gated at a 10-degree boresight floor spills to the horizon anyway. The elevation side of the inconsistency is therefore real by construction but NOT detectable by inspecting the masks on this family; its only trace is the control's difference at the victims below.")
            : string.Create(inv, $"The LIT REACH beside it -- the lowest elevation and the smallest alpha at which a block still radiates within 20 dB of its peak -- does see it: the saturated masks light the ground down to {satLitElev:F1} deg of elevation against the gated control masks' {ctlLitElev:F1} deg.");
        sb.AppendLine("Expected grade: **" + MaskConsistency.Word(MaskConsistency.Verdict.Saturated) + "** on the exclusion axis, for every mask -- near-peak power reaches the GSO arc itself where the geometry exercises the zone. On the elevation axis the grade reads CONSISTENT for the saturated masks exactly as for the control: the grade is keyed to near-peak power, and a range-shaped envelope thins by more than 3 dB before it reaches low elevations, so a missing elevation floor is invisible to it -- a limitation of the grader on range-shaped masks, recorded here rather than hidden. " + elevNote + " Measured:");
        sb.AppendLine();
        sb.AppendLine("| mask | shell | mapped at (km) | overall | exclusion: blocks per grade | elevation: blocks per grade | near-peak reaches alpha (deg) / declared | near-peak reaches elevation (deg) / declared | lit reach: alpha (deg) | lit reach: elevation (deg) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var g in grades) sb.AppendLine(GradeRow(g, inv));
        sb.AppendLine();
        foreach (var g in grades)
            sb.AppendLine(string.Create(inv, $"- mask {g.Mask.MaskId} (shell {g.Mask.Shell}): {g.Report.Summary}{(g.Report.Note.Length > 0 ? " (" + g.Report.Note + ")" : "")}."));
        sb.AppendLine();
        sb.AppendLine("## The conservative verdict");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"The epfd(down) examination (Sec. D5.1.4.1) of the pair at seven victims -- earth stations at 0 to 60 N in 10-degree steps, longitude {F(EsLonDeg)}, wanted GSO satellite at {F(GsoLonDeg)} E, the limit row's own dish -- against the row's points. Beside it, the control: the same set 30 examined against the family's own D2 masks (2, 3, 4: composed with no exclusion gate, as the family declares none, and the 10-degree elevation floor on the beams' boresights) shifted to the same payload level, so that the only difference between the two columns is the elevation floor the control masks were composed under."));
        sb.AppendLine();
        sb.AppendLine("| victim | max epfd (dB(W/m2) in 40 kHz) | point margin (dB) | curve margin (dB) | verdict | margin per limit point: " + string.Join(" / ", lim.Points.Select(p => string.Create(inv, $"{p.Perc:G4}%"))) + " | 24 h prefix: worst margin | moved (dB) | control: max epfd | control: worst margin | control verdict |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (lat, v, h, c) in rows)
            sb.AppendLine(string.Create(inv, $"| {lat:F0} N | {v.MaxEpfdDb:F2} | {v.WorstMarginDb:+0.0;-0.0;0.0} | {v.CurveMarginText} | {Word(v.Pass)} | ")
                + string.Join(" / ", v.Points.Select(p => p.MarginDb.ToString("+0.0;-0.0;0.0", inv)))
                + string.Create(inv, $" | {h.WorstMarginDb:+0.0;-0.0;0.0} | {v.WorstMarginDb - h.WorstMarginDb:+0.0;-0.0;0.0} | {c.MaxEpfdDb:F2} | {c.WorstMarginDb:+0.0;-0.0;0.0} | {Word(c.Pass)} |"));
        sb.AppendLine();
        int failing = rows.Count(r => !r.Sat.Pass);
        var worstRow = rows.OrderBy(r => r.Sat.RuleMarginDb).First();
        // On a tie the shortest time binds (the deciding-point rule of ComplianceViewModel.BuildRow).
        var bindingPoint = worstRow.Sat.Points.OrderBy(p => p.MarginDb).ThenBy(p => p.Perc).First();
        double maxDelta = rows.Max(r => r.Sat.MaxEpfdDb - r.Ctl.MaxEpfdDb);
        double minDelta = rows.Min(r => r.Sat.MaxEpfdDb - r.Ctl.MaxEpfdDb);
        string headline = string.Create(inv, $"grades {string.Join("/", grades.Select(g => MaskConsistency.Word(g.Report.Overall).Split(' ')[0]))}; examination {(failing == rows.Count ? "FAIL at every victim" : failing == 0 ? "PASS at every victim" : failing + " of " + rows.Count + " victims FAIL")}, worst {worstRow.Sat.WorstMarginDb:+0.0;-0.0} dB at {worstRow.Lat:F0} N ({bindingPoint.Perc:G4}% point)");
        sb.AppendLine(string.Create(inv, $"Verdict a consumer must reproduce: **{headline}**. The binding point at the worst victim is the {bindingPoint.Perc:G4}% point, i.e. the short-term end of the row: the maximum epfd is a satellite crossing the earth station's main beam -- inside the declared exclusion zone, where the declaration says no service, and counted regardless by Step 22 (main-beam satellites) carrying a main-beam-grade mask value, since the mask is lit there. That is the conservative mechanism the brief names: the examination over-charges the operator for the shaping it declared and did not build into the mask; it never under-protects the GSO. The saturated masks' maxima sit {minDelta:+0.0;-0.0} to {maxDelta:+0.0;-0.0} dB from the control's."));
        sb.AppendLine();
        sb.AppendLine("What the control can and cannot show. " + ControlNote(controlGrades, inv));
        sb.AppendLine();
        var firm = rows.Where(r => Math.Abs(r.Sat.WorstMarginDb - r.SatHalf.WorstMarginDb) <= 0.5).Select(r => r.Lat).ToList();
        var moving = rows.Where(r => Math.Abs(r.Sat.WorstMarginDb - r.SatHalf.WorstMarginDb) > 0.5).ToList();
        bool verdictFirm = rows.All(r => r.Sat.Pass == r.SatHalf.Pass);
        sb.AppendLine("What is firm and what is not. The verdict at every victim is " + (verdictFirm ? "the same in the 24 h prefix as in the 48 h run: firm." : "NOT the same in the 24 h prefix at every victim: provisional where the pair column disagrees.")
            + (firm.Count > 0 ? " The worst margin moved 0.5 dB or less at " + string.Join(", ", firm.Select(l => string.Create(inv, $"{l:F0} N"))) + " (quotable)." : "")
            + (moving.Count > 0 ? " It moved more than 0.5 dB at " + string.Join(", ", moving.Select(r => string.Create(inv, $"{r.Lat:F0} N ({r.Sat.WorstMarginDb - r.SatHalf.WorstMarginDb:+0.0;-0.0} dB)"))) + ": there the binding point is the short-term end of the row, a single-event statistic set by the closest main-beam pass of the run, which converges slowly by nature; those margins are provisional, the verdict is not." : ""));
        sb.AppendLine();
        sb.AppendLine(ProbeExamination.DualSentence(d4s));
        sb.AppendLine();
        sb.AppendLine("Margins are quotable to the tolerance of the pair column (the 24 h run is the first half of the 48 h run: an extension pair, not an independent draw). The examination CDF at " + string.Create(inv, $"{FamilyVictimLatDeg:F0}") + " N under the saturated masks is written beside this record (" + Path.GetFileName(cdf) + "); the per-victim table with every limit point and the control is in " + Path.GetFileName(csv) + ". No truth curve accompanies this case: the pair does not describe one system.");
        sb.AppendLine();
        sb.AppendLine(LimitCurveRule.Name());
        sb.AppendLine();
        sb.AppendLine("Limit row (from the BR limits database, the same choice the compliance loop makes: the plain FSS row with the smallest reference dish): " + lim.Label + ". Points: " + string.Join("; ", lim.Points.Select(p => string.Create(inv, $"{p.EPFD:F1} dB(W/m2) in 40 kHz for {p.Perc:G4}% of time"))) + ". Worst margin = the minimum over the points of (limit epfd minus the epfd exceeded for at most the point's percentage), in the examination's 0.1 dB bins; positive is room to spare.");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"Depth: {duration / 3600.0:F0} h on the S.1503-4 time step, every fine step ({fineSteps} over the run): {plan.Text}; the first half of the fine grid ({duration / 7200.0:F0} h) is the extension pair.{(quick ? " QUICK profile: structure verification only, the numbers are not delivery numbers." : "")}"));
        sb.AppendLine();
        sb.AppendLine("Artefacts (frozen; checked by identity): " + string.Join("; ", masks.Select(m => "mask " + Path.GetFileName(m.Path) + " SHA-256 " + Provenance.Sha256Hex(m.Path))) + "; operating-parameter set " + Path.GetFileName(paramPath) + " SHA-256 " + Provenance.Sha256Hex(paramPath) + ".");
        sb.AppendLine();
        sb.AppendLine("Provenance: " + provenance);
        string rec = Path.Combine(expDir, "consistency-probe.md");
        File.WriteAllText(rec, sb.ToString(), Utf8NoBom);
        files.Insert(0, Path.GetFileName(rec));
        return new Emitted(rec, files, headline);
    }

    /// <summary>A graded mask: the check's report and the dissection it was read from (for the lit reach).</summary>
    private sealed record Graded(ProbeMask Mask, MaskConsistency.Report Report, MaskDissect.Result Dissection);

    private static Graded Grade(ProbeMask m, OperatingParamsSet declared)
    {
        var d = MaskDissect.Analyze(MaskXmlImport.Load(m.Path), m.AltitudeKm);
        return new Graded(m, MaskConsistency.Check(d, declared), d);
    }

    /// <summary>The lit reach of a dissected mask over its blocks with a plateau: the lowest elevation and the smallest alpha still within 20 dB of the block peak.</summary>
    private static (double Alpha, double Elev) LitReach(MaskDissect.Result d)
    {
        var lit = d.Lats.Where(l => l.Plateau > 0).ToList();
        return lit.Count == 0 ? (double.NaN, double.NaN) : (lit.Min(l => l.LitMinAlpha), lit.Min(l => l.LitMinElev));
    }

    private static string GradeRow(Graded g, CultureInfo inv)
    {
        var (m, rep) = (g.Mask, g.Report);
        var lit = rep.Rows.Where(r => r.Alpha != MaskConsistency.Verdict.Dark).ToList();
        var reach = LitReach(g.Dissection);
        string Per(Func<MaskConsistency.RowResult, MaskConsistency.Verdict> pick)
            => string.Join(", ", lit.GroupBy(pick).OrderByDescending(g => g.Count()).Select(g => MaskConsistency.Word(g.Key).Split(' ')[0] + " " + g.Count()));
        var worstA = lit.OrderBy(r => r.ReachAlpha).FirstOrDefault();
        var worstE = lit.OrderBy(r => r.ReachElev).FirstOrDefault();
        return string.Create(inv, $"| {m.MaskId} | {m.Shell} | {m.AltitudeKm:F0} | {MaskConsistency.Word(rep.Overall)} | {Per(r => r.Alpha)} | {Per(r => r.Elev)} | ")
            + (worstA is null ? "-" : string.Create(inv, $"{worstA.ReachAlpha:F1} / {worstA.DeclaredAlpha:F1}"))
            + " | " + (worstE is null ? "-" : string.Create(inv, $"{worstE.ReachElev:F1} / {worstE.DeclaredElev:F1}"))
            + string.Create(inv, $" | {reach.Alpha:F1} | {reach.Elev:F1} |");
    }

    private static string ControlNote(List<Graded> controlGrades, CultureInfo inv)
    {
        var sb = new StringBuilder();
        sb.Append("The control masks are the family's own derived masks, composed with no exclusion gate -- the family declares no zone -- and with the 10-degree elevation floor acting on the beams' boresights. Graded against set 30, whose zone they were never meant to carry, they read: ");
        sb.Append(string.Join("; ", controlGrades.Select(g =>
        {
            var lit = g.Report.Rows.Where(r => r.Alpha != MaskConsistency.Verdict.Dark).ToList();
            var wa = lit.OrderBy(r => r.ReachAlpha).First();
            var we = lit.OrderBy(r => r.ReachElev).First();
            var reach = LitReach(g.Dissection);
            return string.Create(inv, $"mask {g.Mask.MaskId} (shell {g.Mask.Shell}) overall {MaskConsistency.Word(g.Report.Overall)}, exclusion saturated in {lit.Count(r => r.Alpha == MaskConsistency.Verdict.Saturated)} of {lit.Count} blocks (near-peak reaches alpha {wa.ReachAlpha:F1} against {wa.DeclaredAlpha:F1} at block {wa.LatDeg:0.#}), elevation {MaskConsistency.Word(lit.GroupBy(r => r.Elev).OrderByDescending(x => x.Count()).First().Key)} (near-peak reaches {we.ReachElev:F1} against {we.DeclaredElev:F1}; lit reach alpha {reach.Alpha:F1}, elevation {reach.Elev:F1})");
        })));
        sb.Append(". So the control can say nothing about the exclusion side of this probe's inconsistency -- it has no exclusion gate either, by the family's declaration -- and on the elevation side the lit reach shows that a 10-degree boresight floor is not carried as an edge at this beam size: the payload's 450 km cells are some 20 degrees wide from 1200 km, and a beam centred at the floor spills to the horizon. The control differs from the saturated masks only in the beams whose centres the floor removed, which is why its maxima sit a fraction of a decibel to a few decibels below the saturated masks' at every victim and its verdicts are the same. It therefore shows the size of the elevation gate's effect on the examination and nothing that would separate the two pairs by inspection. History, for the record: the family once declared an 8-degree zone its masks did not carry -- an inconsistency of the same kind as this probe's, by geometry rather than by construction -- and now declares none; writing the zone into the family's masks as a rule notch was not open to it, because its truth curves would then exceed the mask inside the zone.");
        return sb.ToString();
    }

    private static string Vocabulary(MaskConsistency.Verdict v) => v switch
    {
        MaskConsistency.Verdict.Consistent => "the mask's own near-peak edge sits at the declared gate, within one cell, wherever the geometry exercises the gate.",
        MaskConsistency.Verdict.MaskTighter => "the mask is dark beyond the declared gate: the gate is epfd-inert, the set relabels what the mask already suppresses.",
        MaskConsistency.Verdict.LitInside => "near-peak power reaches inside the declared gate and stops short of the arc or the horizon: a gate declared wider than the mask's edge, or the main-lobe edge of beams gated at their boresight.",
        MaskConsistency.Verdict.Saturated => "near-peak power reaches the GSO arc itself (exclusion) or the horizon (elevation): the mask carries no shaping on that axis; beside a declared gate, a saturation-shaped mask.",
        MaskConsistency.Verdict.NotExercised => "no cell of the block reaches the declared zone (exclusion axis only): the geometry at that latitude does not exercise the gate.",
        _ => "no lit block to compare.",
    };

    private static void WriteCdf(string path, ProbeExamination.Verdict v, S1503TimeStep.Plan plan, double lat, OperatingParamsSet set, ProbeExamination.LimitRow lim)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# epfd(down) CDF -- the EXAMINATION (S.1503-4 D5.1.4.1 over the saturated masks and the declared set) at the victim, D7.1.2 bins (0.1 dB).");
        sb.AppendLine(string.Create(inv, $"# band={set.LowFreqMhz}-{set.HighFreqMhz} MHz  victim ES lat={lat:F0} lon={EsLonDeg:F0}, GSO lon={GsoLonDeg:F0}, dish {lim.DishM:F2} m (the limit row's)"));
        sb.AppendLine("# time step: every fine step, no coarse steps; " + plan.Text);
        sb.AppendLine(string.Create(inv, $"# step_s={plan.FineStepSec}  steps={v.Steps}  quiet_steps={v.QuietSteps}  max_epfd_db={v.MaxEpfdDb:F3}  worst_margin_db={v.WorstMarginDb:F2}  curve_margin_db={v.CurveMarginDb:F2}  verdict={Word(v.Pass)}  rule=limit-curve(tol 0.05 dB)"));
        sb.AppendLine("epfd_dbw_m2_40khz,percent_time_exceeded");
        int first = Array.FindIndex(v.Pct, p => p < 100.0);
        int last = Array.FindLastIndex(v.Pct, p => p > 0.0);
        if (first < 0) { first = 0; last = v.Pct.Length - 1; }
        first = Math.Max(0, first - 1);
        last = Math.Min(v.Pct.Length - 1, last + 1);
        for (int i = first; i <= last; i++)
            sb.AppendLine(string.Create(inv, $"{v.Epfd[i]:F1},{v.Pct[i]:G9}"));
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Word(bool pass) => pass ? "PASS" : "FAIL";
}
