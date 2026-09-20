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
/// The section 3.9 read-rule probes of the dataset design brief: three cases
/// whose verdict (or resolved value) is decided by HOW a per-latitude array is
/// read -- nearest row (MIN_ELEV, MAX_CO_FREQ, MIN_DURATION and the mask's
/// latitude blocks, S.1503-4 Sec. D5.1.5 step 1) or linear interpolation
/// (MIN_EXCLUDE, Part B). Rows, values, victims and the masks' power offsets
/// were chosen by measurement on this family (the harness's probescan mode,
/// 2026-09-13) so that the read alone moves the verdict; the generator
/// re-measures everything at emission and writes what it measured.
///
/// BL-R1, nearest-read probe: MIN_ELEV rows at 20 N (10 deg) and 40 N (55 deg),
/// victims at 25 N and 35 N -- half a 10-degree sweep step either side of the
/// midpoint. The correct read gives FAIL at 25 N and PASS at 35 N; a consumer
/// that interpolates, or that reads only rows at their own latitude, fails at
/// 35 N as well.
///
/// BL-R2, interpolation probe: all-orbits MIN_EXCLUDE rows at 20 N (6 deg) and
/// 40 N (14 deg), victims at 25/30/35 N whose interpolated values 8/10/12 deg
/// differ from both rows. MEASURED FINDING: on this family the epfd(down)
/// examination's verdict does not discriminate the exclusion read (all reads
/// within 0.9 dB of each other), so the probe's discriminator is the RESOLVED
/// value a consumer reports, and the record says so; see the record text.
///
/// BL-R3, sweep-grid disclosure probe: MAX_CO_FREQ 8 in the band 63.75-66.25 N
/// (rows at 62.5, 65 and 67.5 N; 1 elsewhere) puts the worst victim between
/// the 10-degree sweep points. The record states the worst margin per sweep
/// step (10, 5, 2, 1 deg) with its latitude; the whole sweep passes at 10 deg
/// and fails at 5 deg and finer.
/// </summary>
public static class ReadRuleProbes
{
    // ---- construction (chosen by measurement; see the class comment) -------------

    public const double RowLoLatDeg = 20.0, RowHiLatDeg = 40.0;
    public static readonly double[] NearestVictimsDeg = { 25.0, 35.0 };
    public static readonly double[] InterpVictimsDeg = { 25.0, 30.0, 35.0 };
    public const double R1ElevLoDeg = 10.0, R1ElevHiDeg = 55.0;
    public const double R2AlphaLoDeg = 6.0, R2AlphaHiDeg = 14.0;
    public const double R3SpikeLatDeg = 65.0, R3RowHalfStepDeg = 2.5;
    public const int R3NcoSpike = 8, R3NcoBase = 1;
    public const int R1Nco = 3, R2Nco = 1;
    public const double ProbeMinElevDeg = 10.0, ProbeAlphaDeg = 8.0;

    /// <summary>Sweep latitudes the disclosure probe is measured at: every whole degree of -70..70 (all the grids named in the record are subsets).</summary>
    public static IEnumerable<double> R3SweepLats => Enumerable.Range(-70, 141).Select(i => (double)i);
    public static readonly double[] R3StepsDeg = { 10.0, 5.0, 2.0, 1.0 };
    /// <summary>Full emission: every tenth of a degree 70 S..70 N (1401 victims), so the record can quote the worst margin at 0.5 and 0.1 deg beside the default 1 deg (user's choice, 2026-09-18).</summary>
    public static IEnumerable<double> R3FineSweepLats => Enumerable.Range(0, 1401).Select(i => Math.Round(-70.0 + i * 0.1, 1));
    public static readonly double[] R3FineStepsDeg = { 10.0, 5.0, 2.0, 1.0, 0.5, 0.1 };

    /// <summary>The probe masks: mask 1's construction with a rule notch on the alpha axis and a power offset that puts the body of the CDF at the limit.</summary>
    // Power offsets re-tuned 2026-09-18 for the limit-curve verdict rule (measured by probescan
    // at 48 h; margins move dB for dB with the offset): R1 -35.5 -> -37.0 so the correct read
    // clears the curve at 35 N (+0.9 dB rule margin) while every wrong read still fails at both
    // victims; R2 -40.6 -> -42.1 so every read passes under the rule (+0.6 dB or more).
    public static DatasetGenerator.ProbeMaskSpec MaskSpecR1 => new(GateAlphaDeg: 8.0, MinElevDeg: 10.0, TxDeltaDb: -37.0, NotchAlphaDeg: 8.0, BStepDeg: 2.0);
    public static DatasetGenerator.ProbeMaskSpec MaskSpecR2 => new(GateAlphaDeg: 6.0, MinElevDeg: 10.0, TxDeltaDb: -42.1, NotchAlphaDeg: 6.0, BStepDeg: 2.0);
    public static DatasetGenerator.ProbeMaskSpec MaskSpecR3 => new(GateAlphaDeg: 8.0, MinElevDeg: 10.0, TxDeltaDb: -43.2, NotchAlphaDeg: 8.0, BStepDeg: 2.0);

    /// <summary>Victim geometry shared with the family: earth station at longitude 0, wanted GSO satellite at 10 E.</summary>
    public const double EsLonDeg = 0.0, GsoLonDeg = 10.0;

    // ---- the operating-parameter sets -----------------------------------------

    private static OperatingParamsSet Common(int ntcId, int paramId)
    {
        var (fMin, fMax) = DatasetGenerator.ProbeBandMhz;
        return new OperatingParamsSet
        {
            SatName = DatasetGenerator.SatName, NtcId = ntcId, ParamId = paramId,
            LowFreqMhz = fMin, HighFreqMhz = fMax,
            EsDensityPerKm2 = 0.00012, EsDistanceKm = 300, EsLatMinDeg = -70, EsLatMaxDeg = 70,
        };
    }

    private static MinElevByLat ElevRow(double latDeg, double elevDeg)
        => new() { LatDeg = latDeg, ByAz = { (0.0, elevDeg), (360.0, elevDeg) } };

    /// <summary>Set 27 (BL-R1): MIN_ELEV rows at 20 N and 40 N with different values; everything else one row (a global constant under the total read).</summary>
    public static OperatingParamsSet Set27(int ntcId)
    {
        var s = Common(ntcId, 27);
        s.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, ProbeAlphaDeg) } });
        s.MaxCoFreqByLat.Add((0.0, R1Nco));
        s.MinElev.Add(ElevRow(RowLoLatDeg, R1ElevLoDeg));
        s.MinElev.Add(ElevRow(RowHiLatDeg, R1ElevHiDeg));
        return s;
    }

    /// <summary>Set 28 (BL-R2): all-orbits MIN_EXCLUDE rows at 20 N and 40 N; the other quantities one row each.</summary>
    public static OperatingParamsSet Set28(int ntcId)
    {
        var s = Common(ntcId, 28);
        s.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (RowLoLatDeg, R2AlphaLoDeg), (RowHiLatDeg, R2AlphaHiDeg) } });
        s.MaxCoFreqByLat.Add((0.0, R2Nco));
        s.MinElev.Add(ElevRow(0.0, ProbeMinElevDeg));
        return s;
    }

    /// <summary>Set 29 (BL-R3): MAX_CO_FREQ 8 at 65 N between rows of 1 at 62.5 N and 67.5 N; the other quantities one row each.</summary>
    public static OperatingParamsSet Set29(int ntcId)
    {
        var s = Common(ntcId, 29);
        s.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, ProbeAlphaDeg) } });
        s.MinElev.Add(ElevRow(0.0, ProbeMinElevDeg));
        s.MaxCoFreqByLat.Add((R3SpikeLatDeg - R3RowHalfStepDeg, R3NcoBase));
        s.MaxCoFreqByLat.Add((R3SpikeLatDeg, R3NcoSpike));
        s.MaxCoFreqByLat.Add((R3SpikeLatDeg + R3RowHalfStepDeg, R3NcoBase));
        return s;
    }

    // ---- the alternative reads, as one-row sets ----------------------------------

    /// <summary>The value a consumer that interpolates a nearest-read array would resolve at lat.</summary>
    public static double Interpolated(double latDeg, double loLat, double loVal, double hiLat, double hiVal)
        => latDeg <= loLat ? loVal : latDeg >= hiLat ? hiVal : loVal + (latDeg - loLat) / (hiLat - loLat) * (hiVal - loVal);

    /// <summary>A read of set 27 with MIN_ELEV pinned to one value (the alternative reads at a victim).</summary>
    public static OperatingParamsSet Set27Pinned(OperatingParamsSet s27, double elevDeg)
        => ProbeExamination.WithMinElev(ProbeExamination.WithNco(ProbeExamination.WithAlpha(ProbeExamination.Shell(s27), ProbeAlphaDeg), R1Nco), elevDeg);

    /// <summary>A read of set 28 with the exclusion angle pinned to one value.</summary>
    public static OperatingParamsSet Set28Pinned(OperatingParamsSet s28, double alphaDeg)
    {
        var s = ProbeExamination.WithMinElev(ProbeExamination.WithNco(ProbeExamination.Shell(s28), R2Nco), ProbeMinElevDeg);
        // alpha 0 = nothing declared: no exclusion, as a point reader that finds no row at its latitude would have it.
        return alphaDeg > 0.0 ? ProbeExamination.WithAlpha(s, alphaDeg) : s;
    }

    // ---- emission ----------------------------------------------------------------

    /// <summary>What one probe emission measured and wrote (for the generator's log and the harness).</summary>
    public sealed record Emitted(string RecordPath, IReadOnlyList<string> Files, string Headline);

    private sealed record Read(string Label, string ValueText, ProbeExamination.Verdict Full, ProbeExamination.Verdict Half);

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>The depths: the family's 48 h at 30 s and its 24 h prefix (the extension pair); quick mode 2 h / 1 h.</summary>
    public static (double StepSec, long Steps, long HalfSteps) Depth(bool quick)
        => quick ? (30.0, 240, 120) : (30.0, 5760, 2880);

    private static Read Measure(Constellation con, MaskFootprint mask, OperatingParamsSet set, ProbeExamination.LimitRow lim,
        double freqMhz, double lat, bool quick, string label, string valueText)
    {
        var (step, steps, half) = Depth(quick);
        var full = ProbeExamination.Examine(con, mask, set, lim, freqMhz, lat, EsLonDeg, GsoLonDeg, step, steps);
        var h = ProbeExamination.Examine(con, mask, set, lim, freqMhz, lat, EsLonDeg, GsoLonDeg, step, half);
        return new Read(label, valueText, full, h);
    }

    /// <summary>BL-R1: the nearest-read probe's expectation record and the examination CDFs at its two victims.</summary>
    public static Emitted EmitR1(string caseDir, string maskPath, string paramPath, OperatingParamsSet s27,
        ProbeExamination.LimitRow lim, bool quick, string provenance)
    {
        var inv = CultureInfo.InvariantCulture;
        var con = new Constellation(DatasetGenerator.Shells);
        var mask = MaskFootprint.LoadFile(maskPath);
        double freqMhz = 0.5 * (s27.LowFreqMhz + s27.HighFreqMhz);
        var rows = new List<(double Lat, List<Read> Reads)>();
        foreach (double lat in NearestVictimsDeg)
        {
            double nearest = DeclaredConstraints.MinElevDeg(s27, lat, 0.0);
            double other = nearest == R1ElevLoDeg ? R1ElevHiDeg : R1ElevLoDeg;
            double interp = Interpolated(lat, RowLoLatDeg, R1ElevLoDeg, RowHiLatDeg, R1ElevHiDeg);
            var reads = new List<Read>
            {
                Measure(con, mask, s27, lim, freqMhz, lat, quick, "nearest row (the correct read)", F(nearest)),
                Measure(con, mask, Set27Pinned(s27, interp), lim, freqMhz, lat, quick, "interpolation between the rows", F(interp)),
                Measure(con, mask, Set27Pinned(s27, 0.0), lim, freqMhz, lat, quick, "point read (no row at this latitude: nothing declared, 0 deg)", "0"),
                Measure(con, mask, Set27Pinned(s27, other), lim, freqMhz, lat, quick, "the other row", F(other)),
            };
            rows.Add((lat, reads));
        }

        var files = new List<string>();
        string expDir = Path.Combine(caseDir, "expected");
        Directory.CreateDirectory(expDir);
        foreach (var (lat, reads) in rows)
        {
            string csv = Path.Combine(expDir, string.Create(inv, $"examination_lat{lat:F0}_cdf.csv"));
            WriteExaminationCdf(csv, reads[0].Full, lat, s27, lim, "min_elev read " + reads[0].ValueText + " deg (nearest row)");
            files.Add(Path.GetFileName(csv));
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Expected outcome: the NEAREST-ROW read of MIN_ELEV decides the verdict (design brief Sec. 3.9, nearest-read probe)");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"Operating-parameter set param_id {s27.ParamId} ({s27.LowFreqMhz}-{s27.HighFreqMhz} MHz) files MIN_ELEV in two latitude rows with different values: {F(R1ElevLoDeg)} deg at {F(RowLoLatDeg)} N and {F(R1ElevHiDeg)} deg at {F(RowHiLatDeg)} N (constant over azimuth). MAX_CO_FREQ ({R1Nco}) and the all-orbits MIN_EXCLUDE ({F(ProbeAlphaDeg)} deg) are single rows, i.e. global constants under the total nearest-row read. No header attribute is filed for any array quantity (one form per quantity)."));
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"The victims are earth stations at {F(NearestVictimsDeg[0])} N and {F(NearestVictimsDeg[1])} N, longitude {F(EsLonDeg)}, wanted GSO satellite at {F(GsoLonDeg)} E -- half a 10-degree sweep step either side of the midpoint {F(0.5 * (RowLoLatDeg + RowHiLatDeg))} N between the rows. The midpoint itself is not a victim: it is equidistant from both rows and the nearest-row read is undefined there."));
        sb.AppendLine();
        sb.AppendLine("The correct read (S.1503-4 Sec. D5.1.5 step 1; design brief Sec. 3.8 and 3.9): a MIN_ELEV row governs the half-step band either side of it, so the victim at " + F(NearestVictimsDeg[0]) + " N reads the " + F(RowLoLatDeg) + " N row and the victim at " + F(NearestVictimsDeg[1]) + " N reads the " + F(RowHiLatDeg) + " N row. Three wrong reads are measured beside it: linear interpolation between the rows, a point read that finds no row at the victim's own latitude (and therefore nothing declared: 0 deg), and the other row.");
        sb.AppendLine();
        sb.AppendLine("| victim | read | min_elev used (deg) | max epfd (dB(W/m2) in 40 kHz) | point margin (dB) | curve margin (dB) | verdict | 24 h prefix: margin | moved (dB) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var (lat, reads) in rows)
            foreach (var r in reads)
                sb.AppendLine(string.Create(inv, $"| {LatText(lat)} | {r.Label} | {r.ValueText} | {r.Full.MaxEpfdDb:F2} | {r.Full.WorstMarginDb:+0.0;-0.0;0.0} | {r.Full.CurveMarginText} | {Word(r.Full.Pass)} | {r.Half.WorstMarginDb:+0.0;-0.0;0.0} | {r.Full.WorstMarginDb - r.Half.WorstMarginDb:+0.0;-0.0;0.0} |"));
        sb.AppendLine();
        var v25 = rows[0].Reads[0].Full; var v35 = rows[1].Reads[0].Full;
        string headline = string.Create(inv, $"correct read: {Word(v25.Pass)} at {LatText(rows[0].Lat)} ({v25.WorstMarginDb:+0.0;-0.0} dB), {Word(v35.Pass)} at {LatText(rows[1].Lat)} ({v35.WorstMarginDb:+0.0;-0.0} dB)");
        sb.AppendLine("Verdicts a consumer must reproduce: **" + headline + "**. The two victims sit in near-identical geometry, so the difference between them is the read: a consumer whose two verdicts agree with the interpolation rows, or with the point-read rows, has read the array wrongly; a consumer that reproduces the first row of each victim reads it as the Recommendation does. Margins are quotable to the tolerance of the pair column: the 24 h run is the first half of the 48 h run (an extension pair, not an independent draw).");
        sb.AppendLine();
        AppendCommon(sb, lim, maskPath, paramPath, MaskSpecR1, quick, provenance, inv,
            "The mask is a rule mask: mask 1's reachable-envelope construction over the three shells with the declared exclusion zone written into the alpha axis (nodes strictly inside |alpha| < " + F(MaskSpecR1.NotchAlphaDeg) + " deg carry the Sec. C1 -1000 null), alpha nodes every " + F(MaskSpecR1.BStepDeg) + " deg, and the payload " + F(-MaskSpecR1.TxDeltaDb) + " dB below mask 1's so that the BODY of the CDF sits at the limit -- the notch keeps the main-beam pass (which no read rule touches: Sec. D5.1.4.1 Step 22 counts it regardless) from deciding the verdict. The mask is consistent with the declared exclusion (notch = declared alpha0) and tighter than nothing on elevation: its envelope was composed at " + F(MaskSpecR1.MinElevDeg) + " deg minimum elevation, below the row values, so the elevation gate is the R set's alone -- which is what this probe tests.");
        string rec = Path.Combine(expDir, "read-rule-probe.md");
        File.WriteAllText(rec, sb.ToString(), Utf8NoBom);
        files.Insert(0, Path.GetFileName(rec));
        return new Emitted(rec, files, headline);
    }

    /// <summary>BL-R2: the interpolation probe's expectation record (resolved values and the measured verdict insensitivity) and the examination CDFs at its three victims.</summary>
    public static Emitted EmitR2(string caseDir, string maskPath, string paramPath, OperatingParamsSet s28,
        ProbeExamination.LimitRow lim, bool quick, string provenance)
    {
        var inv = CultureInfo.InvariantCulture;
        var con = new Constellation(DatasetGenerator.Shells);
        var mask = MaskFootprint.LoadFile(maskPath);
        double freqMhz = 0.5 * (s28.LowFreqMhz + s28.HighFreqMhz);
        var rows = new List<(double Lat, double Resolved, List<Read> Reads)>();
        foreach (double lat in InterpVictimsDeg)
        {
            double resolved = DeclaredConstraints.ExclusionAlphaDeg(s28, lat, 1);
            var reads = new List<Read>
            {
                Measure(con, mask, s28, lim, freqMhz, lat, quick, "linear interpolation (the correct read)", F(resolved)),
                Measure(con, mask, Set28Pinned(s28, R2AlphaLoDeg), lim, freqMhz, lat, quick, "nearest row taken as the " + F(RowLoLatDeg) + " N row", F(R2AlphaLoDeg)),
                Measure(con, mask, Set28Pinned(s28, R2AlphaHiDeg), lim, freqMhz, lat, quick, "nearest row taken as the " + F(RowHiLatDeg) + " N row", F(R2AlphaHiDeg)),
                Measure(con, mask, Set28Pinned(s28, 0.0), lim, freqMhz, lat, quick, "point read (no row at this latitude: no exclusion zone)", "0"),
            };
            rows.Add((lat, resolved, reads));
        }
        double spread = rows.Max(r => r.Reads.Max(x => x.Full.WorstMarginDb) - r.Reads.Min(x => x.Full.WorstMarginDb));

        var files = new List<string>();
        string expDir = Path.Combine(caseDir, "expected");
        Directory.CreateDirectory(expDir);
        foreach (var (lat, resolved, reads) in rows)
        {
            string csv = Path.Combine(expDir, string.Create(inv, $"examination_lat{lat:F0}_cdf.csv"));
            WriteExaminationCdf(csv, reads[0].Full, lat, s28, lim, "min_exclude read " + F(resolved) + " deg (linear interpolation)");
            files.Add(Path.GetFileName(csv));
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Expected outcome: MIN_EXCLUDE is read by LINEAR INTERPOLATION -- the resolved value is the discriminator (design brief Sec. 3.9, interpolation probe)");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"Operating-parameter set param_id {s28.ParamId} ({s28.LowFreqMhz}-{s28.HighFreqMhz} MHz) files the all-orbits MIN_EXCLUDE (orb_id 0) in two latitude rows: {F(R2AlphaLoDeg)} deg at {F(RowLoLatDeg)} N and {F(R2AlphaHiDeg)} deg at {F(RowHiLatDeg)} N. MIN_ELEV ({F(ProbeMinElevDeg)} deg) and MAX_CO_FREQ ({R2Nco}) are single rows. No header attribute is filed for any array quantity."));
        sb.AppendLine();
        sb.AppendLine("The correct read (S.1503-4 Part B: the exclusion zone angle at a latitude between rows is derived by linear interpolation between the data points; design brief Sec. 3.8: beyond the end rows the end-row value applies):");
        sb.AppendLine();
        sb.AppendLine("| victim | resolved alpha0 (deg) a consumer must report | nearest-row read would give | point read would give |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var (lat, resolved, _) in rows)
        {
            string nearest = Math.Abs(lat - RowLoLatDeg) < Math.Abs(lat - RowHiLatDeg) ? F(R2AlphaLoDeg)
                : Math.Abs(lat - RowLoLatDeg) > Math.Abs(lat - RowHiLatDeg) ? F(R2AlphaHiDeg) : F(R2AlphaLoDeg) + " or " + F(R2AlphaHiDeg) + " (a tie)";
            sb.AppendLine(string.Create(inv, $"| {LatText(lat)} | {resolved:F1} | {nearest} | none (no row at this latitude) |"));
        }
        sb.AppendLine();
        sb.AppendLine("MEASURED FINDING -- the examination's verdict does NOT discriminate this read on this family. The epfd(down) examination (Sec. D5.1.4.1) removes satellites inside the exclusion zone from the operating population but counts them regardless when they sit in the earth station's main beam (Step 22, threshold min(Gmax - 30 dB, Grx(alpha0))), and elsewhere the removed satellites are replaced by others of like receive gain under the MAX_CO_FREQ pick. The table below measures every read at every victim: the margins differ by at most " + string.Create(inv, $"{spread:F1}") + " dB across reads, all within the same verdict. The discriminator of this probe is therefore the RESOLVED VALUE: a consumer is expected to report the exclusion angle it applies at each victim (its Sec. 6.7.2.2 resolution layer) and to match the second column above; its verdict is expected to be PASS at every victim whatever it resolves. A verdict-keyed interpolation probe would need a direction in which the resolved exclusion angle steers the geometry -- the epfd(up) examination, where earth stations point at satellites outside the zone -- which this producer does not emit.");
        sb.AppendLine();
        sb.AppendLine("| victim | read | alpha0 used (deg) | max epfd (dB(W/m2) in 40 kHz) | point margin (dB) | curve margin (dB) | verdict | 24 h prefix: margin | moved (dB) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var (lat, _, reads) in rows)
            foreach (var r in reads)
                sb.AppendLine(string.Create(inv, $"| {LatText(lat)} | {r.Label} | {r.ValueText} | {r.Full.MaxEpfdDb:F2} | {r.Full.WorstMarginDb:+0.0;-0.0;0.0} | {r.Full.CurveMarginText} | {Word(r.Full.Pass)} | {r.Half.WorstMarginDb:+0.0;-0.0;0.0} | {r.Full.WorstMarginDb - r.Half.WorstMarginDb:+0.0;-0.0;0.0} |"));
        sb.AppendLine();
        var mid = rows.First(r => r.Lat == 30.0);
        string headline = string.Create(inv, $"resolved 8/10/12 deg at 25/30/35 N; verdict {Word(mid.Reads[0].Full.Pass)} under every read (spread {spread:F1} dB)");
        sb.AppendLine("Expected: the resolved values 8.0 / 10.0 / 12.0 deg at 25 / 30 / 35 N; the examination CDFs at the three victims (expected/examination_lat*_cdf.csv) under the correct read, quotable to the pair column's tolerance (the 24 h run is the first half of the 48 h run: an extension pair).");
        sb.AppendLine();
        AppendCommon(sb, lim, maskPath, paramPath, MaskSpecR2, quick, provenance, inv,
            "The mask is a rule mask: mask 1's construction with the exclusion zone written into the alpha axis at " + F(MaskSpecR2.NotchAlphaDeg) + " deg -- the SMALLER row's value, so the mask is consistent with the declaration where the " + F(RowLoLatDeg) + " N row governs and lit inside the declared zone toward the " + F(RowHiLatDeg) + " N row (this producer's consistency grading: LIT INSIDE there). That is deliberate: the exclusion read can only matter where the mask carries power the gate removes. Payload " + F(-MaskSpecR2.TxDeltaDb) + " dB below mask 1's so the body of the CDF sits a few dB inside the limit under every read.");
        string rec = Path.Combine(expDir, "read-rule-probe.md");
        File.WriteAllText(rec, sb.ToString(), Utf8NoBom);
        files.Insert(0, Path.GetFileName(rec));
        return new Emitted(rec, files, headline);
    }

    /// <summary>BL-R3: the sweep-grid disclosure probe's record and the per-latitude margin table.</summary>
    public static Emitted EmitR3(string caseDir, string maskPath, string paramPath, OperatingParamsSet s29,
        ProbeExamination.LimitRow lim, bool quick, string provenance)
    {
        var inv = CultureInfo.InvariantCulture;
        var con = new Constellation(DatasetGenerator.Shells);
        var mask = MaskFootprint.LoadFile(maskPath);
        double freqMhz = 0.5 * (s29.LowFreqMhz + s29.HighFreqMhz);
        var (step, steps, half) = Depth(quick);
        var table = new List<(double Lat, int Nco, ProbeExamination.Verdict Full, ProbeExamination.Verdict Half)>();
        foreach (double lat in (quick ? R3SweepLats : R3FineSweepLats))
        {
            var full = ProbeExamination.Examine(con, mask, s29, lim, freqMhz, lat, EsLonDeg, GsoLonDeg, step, steps);
            var h = ProbeExamination.Examine(con, mask, s29, lim, freqMhz, lat, EsLonDeg, GsoLonDeg, step, half);
            table.Add((lat, DeclaredConstraints.MaxCoFreq(s29, lat), full, h));
        }

        string expDir = Path.Combine(caseDir, "expected");
        Directory.CreateDirectory(expDir);
        string csv = Path.Combine(expDir, "sweep_margins.csv");
        var cs = new StringBuilder();
        cs.AppendLine("# epfd(down) examination (S.1503-4 D5.1.4.1) per victim latitude, ES lon 0, GSO 10 E; the row's own reference dish.");
        cs.AppendLine("# " + lim.Label);
        cs.AppendLine(string.Create(inv, $"# depth {step:F0} s x {steps} steps; the 24 h columns are the first half of the run (extension pair)."));
        cs.AppendLine("lat_deg,max_co_freq_read,max_epfd_db,worst_margin_db,curve_margin_db,pass,quiet_steps,half_worst_margin_db,half_pass");
        foreach (var (lat, nco, full, h) in table)
            cs.AppendLine(string.Create(inv, $"{lat:F1},{nco},{full.MaxEpfdDb:F2},{full.WorstMarginDb:F2},{full.CurveMarginDb:F2},{(full.Pass ? 1 : 0)},{full.QuietSteps},{h.WorstMarginDb:F2},{(h.Pass ? 1 : 0)}"));
        File.WriteAllText(csv, cs.ToString(), Utf8NoBom);

        var sb = new StringBuilder();
        sb.AppendLine("# Expected outcome: the worst margin depends on the sweep grid, and must be quoted with it (design brief Sec. 3.9, sweep-grid disclosure probe)");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"Operating-parameter set param_id {s29.ParamId} ({s29.LowFreqMhz}-{s29.HighFreqMhz} MHz) files MAX_CO_FREQ in three rows: {R3NcoBase} at {F(R3SpikeLatDeg - R3RowHalfStepDeg)} N, {R3NcoSpike} at {F(R3SpikeLatDeg)} N and {R3NcoBase} at {F(R3SpikeLatDeg + R3RowHalfStepDeg)} N. Under the nearest-row read the value {R3NcoSpike} governs the band {F(R3SpikeLatDeg - R3RowHalfStepDeg / 2)}-{F(R3SpikeLatDeg + R3RowHalfStepDeg / 2)} N and {R3NcoBase} governs every other latitude (the outermost rows govern outward: the total read). MIN_ELEV ({F(ProbeMinElevDeg)} deg) and the all-orbits MIN_EXCLUDE ({F(ProbeAlphaDeg)} deg) are single rows. No header attribute is filed for any array quantity."));
        sb.AppendLine();
        sb.AppendLine("The worst victim therefore lies between the 10-degree sweep points. A consumer quoting a gridless \"worst margin\" fails this expectation; the quotable statement is the worst margin AT A NAMED SWEEP STEP, and it is listed here for the steps below. The whole examination is measured at every whole degree of latitude from 70 S to 70 N (expected/sweep_margins.csv) so any grid that is a subset of the 1-degree grid can be looked up.");
        sb.AppendLine();
        sb.AppendLine("| sweep step (deg) | victims | worst point margin (dB) | curve margin there (dB) | at latitude | sweep verdict | 24 h prefix: worst margin | moved (dB) |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        var lines = new List<string>();
        foreach (double s in (quick ? R3StepsDeg : R3FineStepsDeg))
        {
            var grid = table.Where(t => Math.Abs(t.Lat / s - Math.Round(t.Lat / s)) < 1e-6).ToList();
            var worst = grid.OrderBy(t => t.Full.RuleMarginDb).First();
            var worstH = grid.OrderBy(t => t.Half.RuleMarginDb).First();
            bool sweepPass = grid.All(t => t.Full.Pass);
            sb.AppendLine(string.Create(inv, $"| {s:G} | {grid.Count} | {worst.Full.WorstMarginDb:+0.0;-0.0;0.0} | {worst.Full.CurveMarginText} | {LatText(worst.Lat)} | {(sweepPass ? "COMPLIANT" : "EXCEEDED")} | {worstH.Half.WorstMarginDb:+0.0;-0.0;0.0} | {worst.Full.WorstMarginDb - worstH.Half.WorstMarginDb:+0.0;-0.0;0.0} |"));
            lines.Add(string.Create(inv, $"{s:G} deg: {worst.Full.WorstMarginDb:+0.0;-0.0} at {LatText(worst.Lat)} {(sweepPass ? "COMPLIANT" : "EXCEEDED")}"));
        }
        sb.AppendLine();
        if (!quick)
        {
            sb.AppendLine("The 1-degree grid is the sweep step an examination uses by default; the 0.5 and 0.1-degree rows say whether a finer grid finds a worse victim between its points, and by how much.");
            sb.AppendLine();
        }
        sb.AppendLine("Latitudes reading " + R3NcoSpike + ": " + string.Join(", ", table.Where(t => t.Nco == R3NcoSpike).Select(t => LatText(t.Lat))) + ". Margins move dB-for-dB with the mask's power; the pair column says how far each figure is from converged (the 24 h run is the first half of the 48 h run: an extension pair, not an independent draw).");
        sb.AppendLine();
        AppendCommon(sb, lim, maskPath, paramPath, MaskSpecR3, quick, provenance, inv,
            "The mask is a rule mask: mask 1's construction with the declared exclusion zone written into the alpha axis (" + F(MaskSpecR3.NotchAlphaDeg) + " deg = the declared alpha0, so mask and declaration are consistent), alpha nodes every " + F(MaskSpecR3.BStepDeg) + " deg, payload " + F(-MaskSpecR3.TxDeltaDb) + " dB below mask 1's so that the 10-degree sweep passes and the finer sweeps fail.");
        string rec = Path.Combine(expDir, "sweep-grid-probe.md");
        File.WriteAllText(rec, sb.ToString(), Utf8NoBom);
        return new Emitted(rec, new[] { Path.GetFileName(rec), Path.GetFileName(csv) }, string.Join("; ", lines));
    }

    // ---- shared text -----------------------------------------------------------

    private static void AppendCommon(StringBuilder sb, ProbeExamination.LimitRow lim, string maskPath, string paramPath,
        DatasetGenerator.ProbeMaskSpec spec, bool quick, string provenance, CultureInfo inv, string maskText)
    {
        var (step, steps, half) = Depth(quick);
        sb.AppendLine(LimitCurveRule.Name());
        sb.AppendLine();
        sb.AppendLine("Limit row (from the BR limits database, the same choice the compliance loop makes: the plain FSS row with the smallest reference dish): " + lim.Label + ". Points: " + string.Join("; ", lim.Points.Select(p => string.Create(inv, $"{p.EPFD:F1} dB(W/m2) in 40 kHz for {p.Perc:G4}% of time"))) + ". Worst margin = the minimum over the points of (limit epfd minus the epfd exceeded for at most the point's percentage), in the examination's 0.1 dB bins; positive is room to spare.");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"Depth: {step:F0} s steps x {steps} = {step * steps / 3600.0:F0} h, and the {step * half / 3600.0:F0} h prefix as the extension pair.{(quick ? " QUICK profile: structure verification only, the numbers are not delivery numbers." : "")}"));
        sb.AppendLine();
        sb.AppendLine(maskText);
        sb.AppendLine();
        sb.AppendLine("Artefacts (frozen; checked by identity): mask " + Path.GetFileName(maskPath) + " SHA-256 " + Provenance.Sha256Hex(maskPath) + "; operating-parameter set " + Path.GetFileName(paramPath) + " SHA-256 " + Provenance.Sha256Hex(paramPath) + ".");
        sb.AppendLine();
        sb.AppendLine("Provenance: " + provenance);
    }

    private static void WriteExaminationCdf(string path, ProbeExamination.Verdict v, double lat, OperatingParamsSet set,
        ProbeExamination.LimitRow lim, string readText)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# epfd(down) CDF -- the EXAMINATION (S.1503-4 D5.1.4.1 over the declared mask and set) at the victim, D7.1.2 bins (0.1 dB).");
        sb.AppendLine(string.Create(inv, $"# band={set.LowFreqMhz}-{set.HighFreqMhz} MHz  victim ES lat={lat:F0} lon={EsLonDeg:F0}, GSO lon={GsoLonDeg:F0}, dish {lim.DishM:F2} m (the limit row's)  {readText}"));
        sb.AppendLine(string.Create(inv, $"# steps={v.Steps}  quiet_steps={v.QuietSteps}  max_epfd_db={v.MaxEpfdDb:F3}  worst_margin_db={v.WorstMarginDb:F2}  curve_margin_db={v.CurveMarginDb:F2}  verdict={Word(v.Pass)}  rule=limit-curve(tol 0.05 dB)"));
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
    private static string LatText(double lat) => lat == 0.0 ? "0" : F(Math.Abs(lat)) + (lat > 0 ? " N" : " S");
    private static string Word(bool pass) => pass ? "PASS" : "FAIL";

}
