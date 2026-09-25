using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using radians.beamlab;
using radians.beamlab.app;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.dataset;

/// <summary>
/// The examination at ONE victim earth station, verdicted: S.1503-4
/// Sec. D5.1.4.1 over a declared pfd mask and a declared operating-parameter
/// set (<see cref="EpfdDownMask"/>), the CDF compared with the Article 22 row
/// of the band read from the BR limits database exactly as the compliance
/// window reads it. This is the primitive the section 3.9 read-rule probes
/// are measured and emitted with. The set is read through
/// <see cref="DeclaredConstraints"/>, i.e. under the Recommendation's read
/// rules -- nearest row for MIN_ELEV, MAX_CO_FREQ and MIN_DURATION, linear
/// interpolation for MIN_EXCLUDE. A WRONG read is measured by handing in a
/// one-row set that pins the quantity to the value the wrong read would
/// produce: under the total nearest-row read a single row is a global
/// constant, so such a set says "this value, whatever the latitude".
/// </summary>
public static class ProbeExamination
{
    /// <summary>The margin at one limit point: the limit epfd minus the epfd exceeded for at most the point's percentage.</summary>
    public sealed record PointMargin(double Perc, double LimitEpfdDb, double MeasuredEpfdDb, double MarginDb);

    /// <summary>One examination at one victim: the statistic, the verdict, the margin at every limit point, and the binned CDF behind them.</summary>
    public sealed record Verdict(double LatDeg, double MaxEpfdDb, double WorstMarginDb, bool Pass,
        long QuietSteps, long Steps, double[] Epfd, double[] Pct, IReadOnlyList<PointMargin> Points,
        LimitCurveRule.Crossing Crossing, double CurveMarginDb)
    {
        /// <summary>The margin under the verdict rule: the smaller of the point-wise and the curve margin.</summary>
        public double RuleMarginDb => Math.Min(WorstMarginDb, CurveMarginDb);
        public string CurveMarginText => CurveMarginDb.ToString("+0.0;-0.0", System.Globalization.CultureInfo.InvariantCulture);
        public string CrossingText => Crossing is null ? "none" : Crossing.Text;
    }

    /// <summary>The Article 22 row a probe verdicts against.</summary>
    public sealed record LimitRow(string Label, double DishM, List<radlimits.LimitPoint> Points);

    private static readonly string[] KnownLimitsDbs =
    {
        @"C:\Projects\_EPFD\epfd-reference\Cases\EPFD_limits_RES85_WRC23.mdb",
        @"C:\Projects\_EPFD\radians\radians\Resources\EPFD_limits_RES85_WRC23.mdb",
    };

    /// <summary>The BR limits database: the given path when set, else the known locations; null when none exists.</summary>
    public static string ResolveLimitsDb(string given)
        => given is not null && File.Exists(given) ? given : KnownLimitsDbs.FirstOrDefault(File.Exists);

    /// <summary>
    /// The plain (non-latitude-dependent) FSS row with the smallest reference
    /// dish at the band's centre frequency -- the same choice the compliance
    /// loop makes, so the probes and the loop can never verdict against
    /// different rows. dllDir holds EpfdLimitsApi64.dll (the masks DLL's
    /// directory carries it too).
    /// </summary>
    public static LimitRow LoadLimitRow(string limitsDb, string dllDir, double fMinMhz, double fMaxMhz,
        double refBwKHz, double operatingHeightKm)
    {
        LimitsDbReader.DllDirectory = dllDir;
        double fMhz = 0.5 * (fMinMhz + fMaxMhz);
        var rows = LimitsDbReader.Read(limitsDb, fMhz - 0.02, fMhz + 0.02, refBwKHz, operatingHeightKm);
        var lim = rows.Where(l => !l.ShortTermLatDependent && l.Points.Count > 0 && l.Rf_diam is not null)
            .OrderByDescending(l => l.Service == "FSS")
            .ThenBy(l => l.Rf_diam.Value)
            .FirstOrDefault();
        if (lim is null)
            throw new InvalidOperationException(FormattableString.Invariant(
                $"no plain (non-lat-dependent) limit row at {fMhz:F0} MHz down in {limitsDb}"));
        return new LimitRow(ComplianceViewModel.DescribeLimit(lim), lim.Rf_diam.Value, lim.Points.ToList());
    }

    /// <summary>
    /// The examination at one victim (earth station at victimLatDeg, esLonDeg;
    /// wanted GSO satellite at gsoLonDeg; the row's own reference dish in the
    /// S.1428 pattern at freqMhz), stepSec x steps deep. Worst margin = the
    /// minimum over the row's points of (limit epfd minus the epfd exceeded
    /// for at most the point's percentage); positive is room to spare.
    /// </summary>
    public static Verdict Examine(Constellation con, IMaskPfdRead mask, OperatingParamsSet set, LimitRow lim,
        double freqMhz, double victimLatDeg, double esLonDeg, double gsoLonDeg, double stepSec, long steps)
    {
        var victim = new EpfdDownVictim
        {
            EsLatDeg = victimLatDeg, EsLonDeg = esLonDeg, GsoLonDeg = gsoLonDeg,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, freqMhz, lim.DishM),
        };
        var res = EpfdDownMask.Run(con, mask, set, victim, stepSec, steps, lim.Points, stepSec * steps);
        return VerdictOf(res, lim, victimLatDeg, steps);
    }

    /// <summary>
    /// The examination at one victim on the time step of S.1503-4 Sec. D4: the
    /// plan for the family's shells and the row's reference dish, every fine
    /// step of the run. Fine is the verdict; FineHalf the first half of the
    /// same fine grid (the extension pair); Dual and DualMainBeamOnly the dual
    /// time step's readings under the two wordings of its fine-step region.
    /// </summary>
    public sealed record D4Verdicts(Verdict Fine, Verdict FineHalf, Verdict Dual, Verdict DualMainBeamOnly,
        S1503TimeStep.Plan Plan, long FineSteps);

    public static D4Verdicts ExamineD4(Constellation con, IReadOnlyList<ConstellationShell> shells, IMaskPfdRead mask,
        OperatingParamsSet set, LimitRow lim, double freqMhz, double victimLatDeg, double esLonDeg, double gsoLonDeg,
        double durationSec)
    {
        var victim = new EpfdDownVictim
        {
            EsLatDeg = victimLatDeg, EsLonDeg = esLonDeg, GsoLonDeg = gsoLonDeg,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, freqMhz, lim.DishM),
        };
        var plan = S1503TimeStep.Downlink(shells, radantenna.AntennaLibrary.Compute3dBDeg(freqMhz, lim.DishM), durationSec);
        long nFine = Math.Max(1, (long)Math.Round(durationSec / plan.FineStepSec));
        var r = EpfdDownMask.RunD4(con, mask, set, victim, plan, durationSec, lim.Points, prefixFineSteps: nFine / 2);
        return new D4Verdicts(
            VerdictOf(r.FineOnly, lim, victimLatDeg, r.FineSteps),
            VerdictOf(r.Prefix!.FineOnly, lim, victimLatDeg, r.Prefix.FineSteps),
            VerdictOf(r.Dual, lim, victimLatDeg, r.FineSteps),
            VerdictOf(r.DualMainBeamOnly, lim, victimLatDeg, r.FineSteps),
            plan, r.FineSteps);
    }

    /// <summary>
    /// One sentence for a record: whether the dual time step reaches the
    /// fine-step verdicts in every examination listed, and how far its margins
    /// sit from theirs. Each examination is named by where it was made.
    /// </summary>
    public static string DualSentence(IReadOnlyList<(string Where, D4Verdicts V)> exams)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var flips = exams.Where(e => e.V.Dual.Pass != e.V.Fine.Pass || e.V.DualMainBeamOnly.Pass != e.V.Fine.Pass).ToList();
        double Delta(Func<Verdict, double> m) => exams.Max(e => Math.Max(Math.Abs(m(e.V.Dual) - m(e.V.Fine)), Math.Abs(m(e.V.DualMainBeamOnly) - m(e.V.Fine))));
        double dPoint = Delta(v => v.WorstMarginDb), dCurve = Delta(v => v.CurveMarginDb);
        string head = "The verdicts are the fine-step examination's. The dual time step of Sec. D4.7 (Sub-steps 6.1-6.3 with the Step 24 weights; the fine-step region as Sec. D4.7.1 defines it and as Sub-step 6.3 words it) ";
        string body = flips.Count == 0
            ? string.Create(inv, $"reaches the same verdict in all {exams.Count} examinations")
            : string.Create(inv, $"reaches a different verdict in {flips.Count} of the {exams.Count} examinations ({string.Join(", ", flips.Select(f => f.Where))})");
        return head + body + string.Create(inv, $"; its point margins sit within {dPoint:0.0} dB and its curve margins within {dCurve:0.0} dB of the fine-step ones.");
    }

    private static Verdict VerdictOf(EpfdDownResult res, LimitRow lim, double victimLatDeg, long steps)
    {
        var (passResults, _) = res.Accumulator.CompareWithLimits(lim.Points);
        var (epfd, pct) = res.Accumulator.BuildCdf();
        var points = lim.Points.Select(l =>
        {
            int i = Array.FindIndex(pct, v => v <= l.Perc);
            double measured = i < 0 ? epfd[^1] : epfd[i];
            return new PointMargin(l.Perc, l.EPFD, measured, l.EPFD - measured);
        }).ToList();
        double worst = points.Min(pm => pm.MarginDb);
        // The verdict rule (LimitCurveRule): every tabulated point AND no crossing of the
        // log-linear curve between them; the crossing and the curve margin are reported.
        var curve = LimitCurveRule.Curve(lim.Points);
        var crossing = LimitCurveRule.Scan(epfd, pct, curve, lim.Points);
        double curveMargin = LimitCurveRule.CurveMarginDb(epfd, pct, curve, lim.Points);
        bool pass = passResults.All(p => p) && res.Accumulator.FindWorstMaskViolation(lim.Points) is null;
        return new Verdict(victimLatDeg, res.MaxEpfdDb, worst, pass,
            res.QuietSteps, steps, epfd, pct, points, crossing, curveMargin);
    }

    // ---- composite reads ---------------------------------------------------------

    /// <summary>
    /// One mask per shell, dispatched by the satellite's shell index: the
    /// examination of a notice whose pfd masks are linked per orbital-plane
    /// range (mask_lnk1 per orb_id), as BL-D2 and the consistency probe file
    /// them. Shell order = the constellation's shell order.
    /// </summary>
    public sealed class ShellMaskRead : IPureMaskPfdRead
    {
        private readonly IPureMaskPfdRead[] _byShell;
        public ShellMaskRead(IEnumerable<IPureMaskPfdRead> byShell) => _byShell = byShell.ToArray();
        public double PfdDb(SatelliteState state, Vec3 satPosKm, Vec3 esPosKm)
            => _byShell[state.ShellIndex].PfdDb(state, satPosKm, esPosKm);
    }

    /// <summary>A mask read shifted by a constant (dB) -- a control at another payload level; the -1000 null stays a null.</summary>
    public sealed class OffsetMaskRead : IPureMaskPfdRead
    {
        private readonly IPureMaskPfdRead _inner;
        private readonly double _deltaDb;
        public OffsetMaskRead(IPureMaskPfdRead inner, double deltaDb) { _inner = inner; _deltaDb = deltaDb; }
        public double PfdDb(SatelliteState state, Vec3 satPosKm, Vec3 esPosKm)
        {
            double v = _inner.PfdDb(state, satPosKm, esPosKm);
            return v <= MaskLatBlock.UnreachableDb + 1 ? v : v + _deltaDb;
        }
    }

    // ---- one-row sets: the value a given read would produce, pinned globally ----

    /// <summary>A copy of the set's scalar fields with empty arrays, ready to receive one pinned row per quantity.</summary>
    public static OperatingParamsSet Shell(OperatingParamsSet like) => new()
    {
        SatName = like.SatName, NtcId = like.NtcId, ParamId = like.ParamId,
        LowFreqMhz = like.LowFreqMhz, HighFreqMhz = like.HighFreqMhz,
        EsDensityPerKm2 = like.EsDensityPerKm2, EsDistanceKm = like.EsDistanceKm,
        EsLatMinDeg = like.EsLatMinDeg, EsLatMaxDeg = like.EsLatMaxDeg,
        MinAngleAtEsDeg = like.MinAngleAtEsDeg, MinAngleAtSatDeg = like.MinAngleAtSatDeg,
        MaxCoFreqSat = like.MaxCoFreqSat,
    };

    /// <summary>Pins MAX_CO_FREQ to one value at every latitude (a single row under the total read).</summary>
    public static OperatingParamsSet WithNco(OperatingParamsSet s, int nco)
    {
        s.MaxCoFreqByLat.Add((0.0, nco));
        return s;
    }

    /// <summary>Pins MIN_ELEV to one value at every latitude and azimuth.</summary>
    public static OperatingParamsSet WithMinElev(OperatingParamsSet s, double elevDeg)
    {
        s.MinElev.Add(new MinElevByLat { LatDeg = 0.0, ByAz = { (0.0, elevDeg), (360.0, elevDeg) } });
        return s;
    }

    /// <summary>Pins the all-orbits MIN_EXCLUDE to one value at every latitude.</summary>
    public static OperatingParamsSet WithAlpha(OperatingParamsSet s, double alphaDeg)
    {
        s.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, alphaDeg) } });
        return s;
    }
}
