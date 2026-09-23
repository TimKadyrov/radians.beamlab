using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>One latitude grid point's verdict.</summary>
public sealed record ComplianceRow(double LatDeg, double MaxEpfdDb, double WorstMarginDb,
    bool Pass, long QuietSteps)
{
    /// <summary>
    /// The limit point that decides the worst margin: its percentage of time
    /// (0 = the maximum sample against the 0% row). NaN when no limit was
    /// compared. Names where a verdict lives -- the short-term end moves with
    /// the run length, the body does not -- so a record can say which.
    /// </summary>
    public double DecidingPercent { get; init; } = double.NaN;

    /// <summary>The deciding point as text: "max" for the 0% row, else the percentage.</summary>
    public string DecidingText => double.IsNaN(DecidingPercent) ? "-"
        : DecidingPercent <= 0.0 ? "max"
        : DecidingPercent.ToString("G4", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// The worst crossing of the limit curve between the tabulated points --
    /// the rule's second test (<see cref="LimitCurveRule"/>); null when the
    /// distribution clears the curve everywhere.
    /// </summary>
    public LimitCurveRule.Crossing? CurveCrossing { get; init; }

    /// <summary>
    /// The curve margin: the dB shift of the whole distribution that just
    /// clears the curve, negative when it crosses as it stands. NaN when no
    /// limit was compared.
    /// </summary>
    public double CurveMarginDb { get; init; } = double.NaN;

    /// <summary>The margin under the rule: the smaller of the point-wise and the curve margin.</summary>
    public double RuleMarginDb => double.IsNaN(CurveMarginDb) ? WorstMarginDb : Math.Min(WorstMarginDb, CurveMarginDb);

    public string CurveMarginText => double.IsFinite(CurveMarginDb)
        ? CurveMarginDb.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) : "-";
    public string CrossingText => CurveCrossing is null ? "none" : CurveCrossing.Text;

    public string LatText => LatDeg.ToString("F0", CultureInfo.InvariantCulture);
    public string MaxText => double.IsFinite(MaxEpfdDb)
        ? MaxEpfdDb.ToString("F1", CultureInfo.InvariantCulture) : "quiet";
    public string MarginText => double.IsFinite(WorstMarginDb)
        ? WorstMarginDb.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) : "";
    public string PassText => Pass ? "PASS" : "FAIL";
}

/// <summary>
/// Stage B of the compliance loop (docs/compliance-loop-plan.md): sweep
/// epfd(down) victims across a latitude grid, verdict each point with the
/// examination's own limit comparison, and report the worst dB margin.
/// The system side comes from the operation profile; the limits are
/// hand-entered points of the applicable Article 22 table.
/// </summary>
public sealed class ComplianceViewModel : ObservableObject
{
    private string _designPath = "";
    public string DesignPath { get => _designPath; set => SetField(ref _designPath, value); }

    private string _profilePath = "";
    public string ProfilePath { get => _profilePath; set => SetField(ref _profilePath, value); }

    private string _esLonText = "0";
    /// <summary>Victim ES longitude (deg) for every grid point.</summary>
    public string EsLonText { get => _esLonText; set => SetField(ref _esLonText, value); }

    private string _gsoOffsetText = "10";
    /// <summary>Wanted GSO longitude = ES longitude + this offset (deg).</summary>
    public string GsoOffsetText { get => _gsoOffsetText; set => SetField(ref _gsoOffsetText, value); }

    private string _dishMText = "0.6";
    public string DishMText { get => _dishMText; set => SetField(ref _dishMText, value); }

    private string _latFromText = "0";
    public string LatFromText { get => _latFromText; set => SetField(ref _latFromText, value); }

    private string _latToText = "70";
    public string LatToText { get => _latToText; set => SetField(ref _latToText, value); }

    private string _latStepText = "10";
    public string LatStepText { get => _latStepText; set => SetField(ref _latStepText, value); }

    private string _durationDaysText = "0.1";
    public string DurationDaysText { get => _durationDaysText; set => SetField(ref _durationDaysText, value); }

    private string _stepSecText = "60";
    public string StepSecText { get => _stepSecText; set => SetField(ref _stepSecText, value); }

    // The template pair is wide (it sets the accumulator's bin range) and
    // verdict-permissive under the D7.1.3 rule Pt <= Pi; replace it with
    // the real Article 22 rows for the band and dish.
    // The verdict-permissive template: 100% of the time allowed at every level up to 0 dB,
    // a flat limit curve. Under the limit-curve rule a two-point list is a CURVE between
    // its points, so the earlier template (-300 at 100%, 0 at 0.0001%) was a steep line
    // that every real distribution crossed; it was permissive only point-wise.
    private string _limitsText = "-300 100\n0 100";
    /// <summary>Limit points, one "epfd_db percent_time" per line (the applicable Article 22 table rows).</summary>
    public string LimitsText { get => _limitsText; set => SetField(ref _limitsText, value); }

    public ObservableCollection<ComplianceRow> Rows { get; } = new();

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetField(ref _isRunning, value)) return;
            OnPropertyChanged(nameof(RunEnabled));
            OnPropertyChanged(nameof(ApplyEnabled));
            OnPropertyChanged(nameof(ApplyNcoEnabled));
        }
    }

    public bool RunEnabled => !_isRunning;

    private double _progressPercent;
    /// <summary>Sweep / advisor progress for the window's bar, 0..100.</summary>
    public double ProgressPercent { get => _progressPercent; private set => SetField(ref _progressPercent, value); }

    /// <summary>One progress report: a status line and the fraction done (0..1).</summary>
    public readonly record struct SweepProgress(string Text, double Fraction);

    /// <summary>Synchronous IProgress adapter (no context capture) for re-labelling nested reports.</summary>
    private sealed class Relay<T> : IProgress<T>
    {
        private readonly Action<T> _sink;
        public Relay(Action<T> sink) => _sink = sink;
        public void Report(T value) => _sink(value);
    }

    /// <summary>UI-thread progress sink: status line + bar. Create it on the UI thread.</summary>
    private IProgress<SweepProgress> UiProgress() => new Progress<SweepProgress>(p =>
    {
        StatusText = p.Text;
        ProgressPercent = Math.Clamp(p.Fraction * 100.0, 0.0, 100.0);
    });

    // ---- the sweep ------------------------------------------------------

    public sealed record Sweep(ConstellationShell[] Shells, OperationProfile Profile,
        double EsLon, double GsoOffset, double DishM,
        double LatFrom, double LatTo, double LatStep,
        long Steps, double StepSec, List<radlimits.LimitPoint> Limits)
    {
        /// <summary>
        /// The DECLARED operating-parameter set the examination reads -- a
        /// different thing from the gates the truth run enforces, which stay
        /// the composition's own Enforced set. Null keeps the previous
        /// behaviour, examining against the profile's own rules (E2);
        /// supplying a DERIVED set is what makes the sweep compute E1.
        /// </summary>
        public OperatingParamsSet? Declared { get; init; }
    }

    public Sweep BuildSweep()
    {
        if (_designPath.Trim().Length == 0)
            throw new InvalidOperationException("pick a design document first");
        if (_profilePath.Trim().Length == 0)
            throw new InvalidOperationException("pick an operation profile - it is the system under test");
        var doc = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(_designPath));
        var prof = OperationProfileCodec.Load(File.ReadAllText(_profilePath));
        var shells = OperationComposer.ApplyToShells(prof,
            doc.Shells.Select(OrbitDesignFileCodec.ToShell));

        double days = Num(_durationDaysText, "duration");
        double step = Num(_stepSecText, "time step");
        double latFrom = Num(_latFromText, "lat from");
        double latTo = Num(_latToText, "lat to");
        double latStep = Num(_latStepText, "lat step");
        if (days <= 0.0 || step <= 0.0 || latStep <= 0.0 || latTo < latFrom)
            throw new InvalidOperationException("duration, step and lat step must be positive; lat to >= lat from");

        return new Sweep(shells, prof,
            Num(_esLonText, "ES longitude"), Num(_gsoOffsetText, "GSO offset"),
            Num(_dishMText, "dish diameter"),
            latFrom, latTo, latStep,
            Math.Max(1, (long)(days * 86400.0 / step)), step, ParseLimits(_limitsText));
    }

    public async Task RunAsync()
    {
        Sweep sweep;
        try { sweep = BuildSweep(); }
        catch (Exception ex) { StatusText = "invalid: " + ex.Message; return; }

        IsRunning = true;
        ProgressPercent = 0;
        StatusText = "sweeping latitudes...";
        var progress = UiProgress();
        try
        {
            var rows = await Task.Run(() => RunSweep(sweep, sweep.Profile.AlphaExclDeg, progress));
            ProgressPercent = 100;
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
            string gap = OperationComposer.PerLatExclusionSceneGap(sweep.Profile) is string g
                ? g + " -- " : "";
            // epfd is exactly dB-for-dB in per-beam power (the envelope
            // study's linear frontier), so the worst margin doubles as
            // the TxEirpDbw headroom -- for the live composition only
            // (a declared mask is fixed; power moves need a new mask).
            double worstAll = rows.Min(r => r.WorstMarginDb);
            string headroom = sweep.Profile.Down.FootprintSource != "mask"
                && double.IsFinite(worstAll)
                ? string.Create(CultureInfo.InvariantCulture,
                    $" -- power headroom {worstAll:+0.0;-0.0} dB on per-beam TxEirpDbw (dB-for-dB)")
                : "";
            StatusText = gap + (sweep.Profile.Down.FootprintSource == "mask"
                ? "declared-mask footprint -- " : "") + SummarizeRows(rows) + headroom;
        }
        catch (Exception ex) { StatusText = "sweep failed: " + ex.Message; }
        finally { IsRunning = false; }
    }

    /// <summary>
    /// One full latitude sweep at the given exclusion angle (the profile's
    /// other characteristics unchanged). Synchronous; the advisor and the
    /// check harness call it directly.
    /// </summary>
    public static List<ComplianceRow> RunSweep(Sweep sweep, double alphaExclDeg,
        IProgress<SweepProgress>? progress = null)
        => RunSweepProfile(sweep, sweep.Profile with { AlphaExclDeg = alphaExclDeg, AlphaByLat = null }, progress);

    /// <summary>
    /// One full latitude sweep for an arbitrary profile variant -- the
    /// loop-v2 core: levers build their variant and sweep it.
    /// </summary>
    public static List<ComplianceRow> RunSweepProfile(Sweep sweep, OperationProfile prof,
        IProgress<SweepProgress>? progress = null)
    {
        var inv = CultureInfo.InvariantCulture;
        var lats = new List<double>();
        for (double l = sweep.LatFrom; l <= sweep.LatTo + 1e-9; l += sweep.LatStep) lats.Add(l);
        int nLat = lats.Count;
        var con = new Constellation(sweep.Shells);
        var sh0 = sweep.Shells[0];
        var comp = OperationComposer.Compose(prof, sh0.OperatingHeightKm ?? sh0.AltitudeKm);
        double simDur = sweep.Steps * sweep.StepSec;
        double freqMhz = prof.Down.FrequencyGhz * 1000.0;
        // Declared-mask footprint: the sweep then runs the examination's
        // own down algorithm (D5.1.4.1) against the mask + walked alpha;
        // the mask file itself is fixed while the advisor walks the zone.
        MaskFootprint? downMask = comp.UsesMaskFootprint
            ? MaskFootprint.LoadFile(comp.DownlinkMaskXmlPath)
            : null;

        EpfdDownVictim Victim(double lat) => VictimFor(sweep, freqMhz, lat);

        var rows = new List<ComplianceRow>();
        void AddRow(double lat, int i, double fraction, EpfdDownResult res)
        {
            var row = BuildRow(lat, res, sweep.Limits);
            rows.Add(row);
            progress?.Report(new SweepProgress(string.Create(inv,
                $"lat {lat:F0} ({i + 1}/{nLat}): worst margin {row.WorstMarginDb:+0.0;-0.0} dB {(row.Pass ? "PASS" : "FAIL")}"),
                fraction));
        }

        if (downMask is not null)
        {
            // The declared-mask read is victim-specific at every step -- the
            // footprint pfd, the exclusion zone, the elevation gate and the
            // co-frequency cap all key off the earth station -- so there is no
            // shared pass to hoist here. One run per latitude, as before.
            for (int i = 0; i < nLat; i++)
            {
                double lat = lats[i]; int iNow = i;
                progress?.Report(new SweepProgress(string.Create(inv,
                    $"lat {lat:F0} ({iNow + 1}/{nLat}) -- {sweep.Steps} steps of {sweep.StepSec:F0} s..."),
                    (double)iNow / nLat));
                IProgress<double>? stepProgress = progress is null ? null : new Relay<double>(f =>
                    progress.Report(new SweepProgress(string.Create(inv,
                        $"lat {lat:F0} ({iNow + 1}/{nLat}) -- {f * 100:F0}% of {sweep.Steps} steps"),
                        (iNow + f) / nLat)));
                AddRow(lat, iNow, (double)(iNow + 1) / nLat,
                    EpfdDownMask.Run(con, downMask, sweep.Declared ?? comp.Enforced, Victim(lat),
                        sweep.StepSec, sweep.Steps, sweep.Limits, simDur, stepProgress));
            }
        }
        else
        {
            // ONE simulation for the whole grid. A victim is only an
            // accumulator -- propagation, scheduling and beam resolution do not
            // depend on who is listening -- so N latitudes cost one pass over
            // the constellation instead of N identical ones. Identical results
            // by construction (EpfdDown.Run is the one-victim wrapper) and by
            // check: V37 compares the two forms bin for bin.
            progress?.Report(new SweepProgress(string.Create(inv,
                $"one pass covering {nLat} latitude(s) -- {sweep.Steps} steps of {sweep.StepSec:F0} s..."),
                0.0));
            IProgress<double>? stepProgress = progress is null ? null : new Relay<double>(f =>
                progress.Report(new SweepProgress(string.Create(inv,
                    $"one pass covering {nLat} latitude(s) -- {f * 100:F0}% of {sweep.Steps} steps"), f)));
            var pointing = new ScheduledPointing(con, comp.Geography, comp.Enforced, comp.Scene,
                simDur, comp.CoverageRadiusKm, comp.Policy, comp.IlluminationDutyCycle);
            var res = EpfdDown.RunMany(con, pointing, lats.Select(Victim).ToList(),
                sweep.StepSec, sweep.Steps, sweep.Limits, simDur, progress: stepProgress);
            // The pass is the whole cost; the per-latitude verdicts below are
            // accumulator reads, so they report against a finished bar.
            for (int i = 0; i < nLat; i++) AddRow(lats[i], i, 1.0, res[i]);
        }
        return rows;
    }

    /// <summary>The GSO earth station of a sweep at one latitude: the limit row's dish, S.1428 pattern.</summary>
    public static EpfdDownVictim VictimFor(Sweep sweep, double freqMhz, double lat) => new()
    {
        EsLatDeg = lat, EsLonDeg = sweep.EsLon, GsoLonDeg = sweep.EsLon + sweep.GsoOffset,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, freqMhz, sweep.DishM),
    };

    /// <summary>
    /// One latitude's verdict row from a finished run: the point margins, the
    /// verdict under the limit-curve rule (every tabulated point AND no
    /// crossing of the log-linear curve between them), the crossing and the
    /// curve margin, and the deciding point.
    /// </summary>
    public static ComplianceRow BuildRow(double lat, EpfdDownResult res, List<radlimits.LimitPoint> limits)
    {
        var (passResults, _) = res.Accumulator.CompareWithLimits(limits);
        var (epfd, pct) = res.Accumulator.BuildCdf();
        double worst = limits.Count == 0 ? double.PositiveInfinity
            : limits.Min(l => MarginDb(epfd, pct, l.EPFD, l.Perc));
        bool pass = limits.Count == 0
            ? passResults.All(p => p)
            : LimitCurveRule.Pass(res.Accumulator, limits);
        LimitCurveRule.Crossing crossing = null;
        double curveMargin = double.NaN;
        if (limits.Count > 0)
        {
            var curve = LimitCurveRule.Curve(limits);
            crossing = LimitCurveRule.Scan(epfd, pct, curve, limits);
            curveMargin = LimitCurveRule.CurveMarginDb(epfd, pct, curve, limits);
        }
        // The first limit point at the worst margin names the deciding point.
        double deciding = double.NaN;
        foreach (var l in limits)
            if (MarginDb(epfd, pct, l.EPFD, l.Perc) == worst) { deciding = l.Perc; break; }
        return new ComplianceRow(lat, res.MaxEpfdDb, worst, pass, res.QuietSteps)
            { DecidingPercent = deciding, CurveCrossing = crossing, CurveMarginDb = curveMargin };
    }

    /// <summary>
    /// One latitude of the examination on the S.1503-4 time step: the dual
    /// time step with the fine-step region of Sec. D4.7.1, the same with the
    /// region as Sub-step 6.3 words it, and every fine step; with the samples
    /// each evaluated and the fine steps the run spans.
    /// </summary>
    public sealed record D4Row(double LatDeg, ComplianceRow Dual, ComplianceRow DualMainBeamOnly,
        ComplianceRow FineOnly, long DualSamples, long DualMainBeamOnlySamples, long FineSteps);

    /// <summary>The Sec. D4 plan for a sweep: its shells, and the 3 dB beamwidth of its dish at the downlink frequency.</summary>
    public static S1503TimeStep.Plan D4PlanFor(Sweep sweep, OperationProfile prof)
        => S1503TimeStep.Downlink(sweep.Shells,
            radantenna.AntennaLibrary.Compute3dBDeg(prof.Down.FrequencyGhz * 1000.0, sweep.DishM),
            sweep.Steps * sweep.StepSec);

    /// <summary>
    /// The examination sweep on the time step of S.1503-4 Sec. D4, over the
    /// same run length as the sweep's own step grid and with the same
    /// propagation span, so the trajectories match the sweep's other
    /// examinations and only the sampling differs. Reads the declared mask
    /// of the profile (footprint source "mask") against the sweep's declared
    /// set. One run per latitude, each over the whole fine-step grid.
    /// </summary>
    public static List<D4Row> RunD4ExamSweep(Sweep sweep, OperationProfile prof, S1503TimeStep.Plan plan,
        IProgress<SweepProgress>? progress = null)
    {
        var inv = CultureInfo.InvariantCulture;
        var lats = new List<double>();
        for (double l = sweep.LatFrom; l <= sweep.LatTo + 1e-9; l += sweep.LatStep) lats.Add(l);
        int nLat = lats.Count;
        var con = new Constellation(sweep.Shells);
        var sh0 = sweep.Shells[0];
        var comp = OperationComposer.Compose(prof, sh0.OperatingHeightKm ?? sh0.AltitudeKm);
        if (!comp.UsesMaskFootprint)
            throw new InvalidOperationException("the S.1503-4 time-step examination reads a declared mask; set the footprint source to mask");
        var downMask = MaskFootprint.LoadFile(comp.DownlinkMaskXmlPath);
        double simDur = sweep.Steps * sweep.StepSec;
        double freqMhz = prof.Down.FrequencyGhz * 1000.0;
        var rows = new List<D4Row>();
        for (int i = 0; i < nLat; i++)
        {
            double lat = lats[i]; int iNow = i;
            IProgress<double>? stepProgress = progress is null ? null : new Relay<double>(f =>
                progress.Report(new SweepProgress(string.Create(inv,
                    $"S.1503-4 step: lat {lat:F0} ({iNow + 1}/{nLat}) -- {f * 100:F0}% of the fine-step grid"),
                    (iNow + f) / nLat)));
            var r = EpfdDownMask.RunD4(con, downMask, sweep.Declared ?? comp.Enforced, VictimFor(sweep, freqMhz, lat),
                plan, simDur, sweep.Limits, stepProgress);
            var row = new D4Row(lat, BuildRow(lat, r.Dual, sweep.Limits), BuildRow(lat, r.DualMainBeamOnly, sweep.Limits),
                BuildRow(lat, r.FineOnly, sweep.Limits), r.DualSamples, r.DualMainBeamOnlySamples, r.FineSteps);
            rows.Add(row);
            progress?.Report(new SweepProgress(string.Create(inv,
                $"S.1503-4 step: lat {lat:F0} ({iNow + 1}/{nLat}): worst margin {row.Dual.WorstMarginDb:+0.0;-0.0} dB {(row.Dual.Pass ? "PASS" : "FAIL")}"),
                (double)(iNow + 1) / nLat));
        }
        return rows;
    }

    /// <summary>
    /// The profile as a SATURATED probe: demand, activity, operating
    /// fraction and illumination duty all at their maxima.
    ///
    /// A declaration is an envelope of what the system MAY do, so it has to
    /// be measured with traffic taken out. A value measured under a traffic
    /// sample -- MAX_CO_FREQ above all -- is a commitment the operator never
    /// made and may not be able to honour at peak. Demand rises to the
    /// declared co-frequency cap so that the CAP binds rather than the
    /// traffic model; with no cap declared the profile's own demand stands,
    /// since an unbounded slot count is not a measurement of anything.
    /// </summary>
    /// <summary>
    /// The name one loop run goes by: the profile name up to any bracketed
    /// qualifier, lower-cased and punctuation-folded. ONE definition, because
    /// the loop writes its artefacts under this name and the designer looks
    /// for them under it -- two spellings would silently never meet.
    /// </summary>
    public static string RunName(OperationProfile prof)
    {
        string stem = prof.Name.Split('(')[0].Trim();
        return new string(stem.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray()).Trim('-');
    }

    /// <summary>Directory a loop run writes its profile, R set and mask into.</summary>
    public static string RunDir(string repoDir, OperationProfile prof)
        => Path.Combine(repoDir, "dataset", "margin", RunName(prof));

    /// <summary>The derived R set of a loop run, in the designer's own format.</summary>
    public static string RunSetJsonPath(string repoDir, OperationProfile prof)
        => Path.Combine(RunDir(repoDir, prof), RunName(prof) + ".operparams.json");

    public static OperationProfile Saturate(OperationProfile prof, OperatingParamsSet enforced)
    {
        int cap = 0;
        foreach (var row in enforced.MaxCoFreqByLat) cap = Math.Max(cap, row.Value);
        if (enforced.MaxCoFreqHeader is int header) cap = Math.Max(cap, header);
        return prof with
        {
            DemandLinksPerCell = cap > 0 ? cap : prof.DemandLinksPerCell,
            ActivityFactor = 1.0,
            OperationalFraction = 1.0,
            IlluminationDutyCycle = 1.0,
        };
    }

    /// <summary>
    /// Derive a declared R set from a saturated probe of the same system the
    /// sweep flies -- ONE derivation, owned by the loop, so the R set and the
    /// gates cannot drift apart.
    ///
    /// Victim-less by nature: a derivation observes the system, not an
    /// interference victim. That also makes it independent of the truth sweep
    /// by construction, which is what keeps E1 >= T an adequacy test rather
    /// than a tautology -- a set derived from the very run that later
    /// verifies it would envelope that run trivially.
    /// </summary>
    /// <summary>
    /// A derivation depends on the SYSTEM and the DEPTH -- nothing else. It
    /// has no victim and no limit to compare against, so it deliberately does
    /// NOT take a <see cref="Sweep"/>: coupling it to the examination's inputs
    /// would make callers configure victim geometry that the measurement never
    /// reads. The loop passes its own shells, profile and depth; a caller with
    /// no sweep configured passes the same four things.
    /// </summary>
    public static OpParamsDeriver.Result DeriveDeclared(ConstellationShell[] shells,
        OperationProfile prof, double simDurSec, double stepSec, double latBandDeg = 10.0,
        string satName = "DERIVED", int ntcId = 0, int paramId = 1,
        double? lowFreqMhz = null, double? highFreqMhz = null)
    {
        var sh0 = shells[0];
        double altKm = sh0.OperatingHeightKm ?? sh0.AltitudeKm;
        // Compose once to read the declared cap, then saturate and recompose.
        var enforced0 = OperationComposer.Compose(prof, altKm).Enforced;
        var probe = Saturate(prof, enforced0);
        var comp = OperationComposer.Compose(probe, altKm);
        var con = new Constellation(OperationComposer.ApplyToShells(probe, shells));
        return OpParamsDeriver.Derive(con, comp.Geography, comp.Enforced, comp.Scene,
            simDurSec, stepSec, latBandDeg,
            satName, ntcId, paramId,
            // The band identity of the set: a set governs ONE band, so the
            // caller says which. Absent, the composition's own span stands.
            lowFreqMhz ?? comp.Enforced.LowFreqMhz, highFreqMhz ?? comp.Enforced.HighFreqMhz,
            comp.Policy, comp.CoverageRadiusKm, comp.IlluminationDutyCycle);
    }

    /// <summary>
    /// What the worst margin is worst OVER. A sweep evaluates real victims at
    /// discrete latitudes, so its extremum is the worst of what it sampled --
    /// not the system's worst. On BL-D2 a 5 deg sweep found a latitude 10 dB
    /// worse than anything the 10 deg sweep visited. The figure therefore
    /// travels with its grid, exactly as depth travels with the resolvable
    /// percentile floor.
    /// </summary>
    public static string SamplingNote(IReadOnlyList<ComplianceRow> rows)
    {
        var inv = CultureInfo.InvariantCulture;
        if (rows.Count == 0) return "";
        if (rows.Count == 1)
            return string.Create(inv, $" -- at latitude {rows[0].LatDeg:F0} only");
        double step = rows[1].LatDeg - rows[0].LatDeg;
        return string.Create(inv,
            $" -- sampled every {step:F0} deg over {rows[0].LatDeg:F0}..{rows[^1].LatDeg:F0}, "
            + $"so a finer sweep can find worse between them");
    }

    public static string SummarizeRows(IReadOnlyList<ComplianceRow> rows)
    {
        var failing = rows.Where(r => !r.Pass).ToList();
        // The margins here are rule margins: the smaller of the point-wise and the curve margin.
        return failing.Count == 0
            ? string.Create(CultureInfo.InvariantCulture,
                $"COMPLIANT at all {rows.Count} latitude(s); worst margin {rows.Min(r => r.RuleMarginDb):+0.0;-0.0} dB under the limit-curve rule")
                + SamplingNote(rows)
            : string.Create(CultureInfo.InvariantCulture,
                $"EXCEEDED at {failing.Count} of {rows.Count} latitude(s) ({string.Join(", ", failing.Select(f => f.LatText))}); worst margin {failing.Min(r => r.RuleMarginDb):+0.0;-0.0} dB under the limit-curve rule")
                + SamplingNote(rows);
    }

    /// <summary>
    /// dB margin at one limit point: the limit epfd minus the measured
    /// epfd whose exceedance is at most the allowed percentage. Positive
    /// means room to spare.
    /// </summary>
    public static double MarginDb(double[] epfd, double[] pct, double limitEpfd, double limitPerc)
    {
        int i = Array.FindIndex(pct, v => v <= limitPerc);
        double measured = i < 0 ? epfd[^1] : epfd[i];
        return limitEpfd - measured;
    }

    public static List<radlimits.LimitPoint> ParseLimits(string text)
    {
        var pts = new List<radlimits.LimitPoint>();
        var raw = text.Split('\n');
        for (int i = 0; i < raw.Length; i++)
        {
            string line = raw[i].Trim();
            if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new FormatException($"limits line {i + 1}: expected 'epfd_db percent'");
            pts.Add(new radlimits.LimitPoint
            {
                EPFD = Num(parts[0], $"limits line {i + 1} epfd"),
                Perc = Num(parts[1], $"limits line {i + 1} percent"),
            });
        }
        if (pts.Count == 0) throw new InvalidOperationException("enter at least one limit point");
        return pts;
    }

    // ---- limits from the BR database ------------------------------------

    private string _limitsDbPathText = "";
    /// <summary>Path of the BR limits database (EPFD_limits_*.mdb).</summary>
    public string LimitsDbPathText { get => _limitsDbPathText; set => SetField(ref _limitsDbPathText, value); }

    /// <summary>Display rows of the loaded limits, index-aligned with the internal list.</summary>
    public ObservableCollection<string> LimitChoices { get; } = new();

    private List<radlimits.Limit> _loadedLimits = new();

    private int _selectedLimitIndex = -1;
    public int SelectedLimitIndex { get => _selectedLimitIndex; set => SetField(ref _selectedLimitIndex, value); }

    // The BR native pair's known homes on this machine (same convention
    // as the check harness); used only when the DLL is not already
    // resolvable next to the process.
    private static readonly string[] KnownDllDirs =
    {
        @"C:\Projects\_EPFD\radians\radians\dlls",
        @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
    };

    /// <summary>
    /// Reads the applicable epfd(down) Article 22 rows from the BR
    /// limits database for the profile's downlink carrier (a
    /// reference-bandwidth sliver as the band, the shells' lowest
    /// operating height) via the vendored radlimits interop -- the
    /// radians reader's calling pattern. Fills the choice list; Use
    /// puts the chosen row's points into the limit text.
    /// </summary>
    public void LoadLimitsFromDb()
    {
        try
        {
            var sweep = BuildSweep();   // design + profile supply carrier and height
            if (_limitsDbPathText.Trim().Length == 0)
                throw new InvalidOperationException("pick the limits database (*.mdb) first");
            LimitsDbReader.DllDirectory ??= KnownDllDirs.FirstOrDefault(
                d => File.Exists(Path.Combine(d, "EpfdLimitsApi64.dll")));

            double fMhz = sweep.Profile.Down.FrequencyGhz * 1000.0;
            double bwKhz = sweep.Profile.Down.RefBwKHz;
            double halfMhz = bwKhz / 2000.0;
            double opHt = sweep.Shells.Min(s => s.OperatingHeightKm ?? s.AltitudeKm);
            _loadedLimits = LimitsDbReader.Read(_limitsDbPathText.Trim(),
                fMhz - halfMhz, fMhz + halfMhz, bwKhz, opHt);

            LimitChoices.Clear();
            foreach (var l in _loadedLimits) LimitChoices.Add(DescribeLimit(l));
            SelectedLimitIndex = _loadedLimits.Count > 0 ? 0 : -1;
            StatusText = string.Create(CultureInfo.InvariantCulture,
                $"{_loadedLimits.Count} limit row(s) at {fMhz:F0} MHz, op height {opHt:F0} km -- pick one and Use to fill the points");
        }
        catch (Exception ex) { StatusText = "limits load failed: " + ex.Message; }
    }

    /// <summary>Puts the selected loaded row's points into the limit text.</summary>
    public void UseSelectedLimit()
    {
        if (_selectedLimitIndex < 0 || _selectedLimitIndex >= _loadedLimits.Count)
        { StatusText = "load and pick a limit row first"; return; }
        var l = _loadedLimits[_selectedLimitIndex];
        if (l.ShortTermLatDependent)
        {
            // The flat text cannot express per-latitude short-term rows;
            // show them for hand transcription of the applicable band.
            StatusText = "lat-dependent short-term limit -- transcribe the applicable latitude band by hand: "
                + string.Join("; ", l.Points.Select(p => FormattableString.Invariant(
                    $"lat {p.LatMin}..{p.LatMax}: {p.EPFD} dB at {p.Perc}%")));
            return;
        }
        LimitsText = LimitPointsText(l);
        StatusText = string.Create(CultureInfo.InvariantCulture,
            $"limit points filled from {l.RrRef} ({l.Points.Count} point(s)) -- the sweep verdicts against exactly this text");
    }

    /// <summary>The limit text a loaded row fills in -- one "epfd perc" line per point.</summary>
    public static string LimitPointsText(radlimits.Limit l)
        => string.Join("\n", l.Points.Select(p =>
            FormattableString.Invariant($"{p.EPFD} {p.Perc}")));

    /// <summary>One line describing a loaded limit row.</summary>
    public static string DescribeLimit(radlimits.Limit l)
        => string.Create(CultureInfo.InvariantCulture,
            $"{l.RrRef} -- {l.Service} {l.RegFreq_min:F0}-{l.RegFreq_max:F0} MHz, refbw {l.RefBW:F0} kHz")
           + (l.Rf_diam is double d ? string.Create(CultureInfo.InvariantCulture, $", dish {d:F2} m") : "")
           + $", regions {string.Join("/", l.regions)}"
           + (l.ShortTermLatDependent ? ", lat-dependent short-term" : "");

    // ---- stage C: the exclusion advisor ---------------------------------

    private string _alphaStepText = "1";
    public string AlphaStepText { get => _alphaStepText; set => SetField(ref _alphaStepText, value); }

    private string _alphaMaxText = "15";
    public string AlphaMaxText { get => _alphaMaxText; set => SetField(ref _alphaMaxText, value); }

    private double? _foundAlphaDeg;
    public double? FoundAlphaDeg
    {
        get => _foundAlphaDeg;
        private set { if (SetField(ref _foundAlphaDeg, value)) OnPropertyChanged(nameof(ApplyEnabled)); }
    }

    public bool ApplyEnabled => _foundAlphaDeg is not null && !_isRunning;

    public async Task AdviseAsync()
    {
        Sweep sweep;
        double stepA, maxA;
        try
        {
            sweep = BuildSweep();
            stepA = Num(_alphaStepText, "alpha step");
            maxA = Num(_alphaMaxText, "alpha cap");
            if (stepA <= 0.0) throw new InvalidOperationException("alpha step must be positive");
        }
        catch (Exception ex) { StatusText = "invalid: " + ex.Message; return; }

        IsRunning = true;
        ProgressPercent = 0;
        FoundAlphaDeg = null;
        StatusText = "advising: walking the exclusion angle...";
        var progress = UiProgress();
        // Interim guard (loop v2 design): the walk substitutes a GLOBAL
        // alpha for the profile's declared per-latitude rows -- declared
        // structure is ignored while walking, and the found global can
        // sit below a declared row. v2 walks deltas over the rows.
        string rowsNote = sweep.Profile.AlphaByLat is { Count: > 0 }
            ? "NOTE: the profile declares per-latitude alpha rows; the walk IGNORES them and uses a global value (v2 will walk deltas over the rows) -- "
            : "";
        try
        {
            var advice = await Task.Run(() => Advise(sweep, stepA, maxA, progress));
            ProgressPercent = 100;
            Rows.Clear();
            foreach (var r in advice.FinalRows) Rows.Add(r);
            FoundAlphaDeg = advice.FoundAlpha;
            bool livePower = sweep.Profile.Down.FootprintSource != "mask"
                && double.IsFinite(advice.WorstMarginEndDb);
            StatusText = rowsNote + (advice.FoundAlpha is double a
                ? string.Create(CultureInfo.InvariantCulture,
                    $"compliant at alpha = {a:F1} deg after {advice.Iterations} sweep(s)")
                  + (livePower
                        ? string.Create(CultureInfo.InvariantCulture,
                            $"; power headroom {advice.WorstMarginEndDb:+0.0;-0.0} dB on per-beam TxEirpDbw (dB-for-dB)")
                        : "")
                  + (advice.FailingAtStart.Count > 0
                     && advice.FailingAtStart.Count < advice.FinalRows.Count
                        ? string.Create(CultureInfo.InvariantCulture,
                            $"; at the starting alpha only lat(s) {string.Join(", ", advice.FailingAtStart.Select(l => l.ToString("F0", CultureInfo.InvariantCulture)))} failed -- per-latitude alpha rows are the finer declaration")
                        : "")
                  + " -- Apply writes it into the profile"
                : string.Create(CultureInfo.InvariantCulture,
                    $"no compliant alpha up to {maxA:F1} deg ({advice.Iterations} sweep(s)): worst margin {advice.WorstMarginEndDb:+0.0;-0.0} dB at lat {advice.WorstLatEndDeg:F0}, ")
                  + TrendText(advice.WorstMarginStartDb, advice.WorstMarginEndDb)
                  + (advice.WorstMarginEndDb - advice.WorstMarginStartDb > 0.5
                        ? " -- raise the cap to continue the walk"
                     : advice.WorstMarginStartDb - advice.WorstMarginEndDb > 0.5
                        ? " -- a larger exclusion worsens this geometry"
                        : " -- the exclusion angle is not the binding lever; adjust the system")
                  + (livePower
                        ? string.Create(CultureInfo.InvariantCulture,
                            $"; a {-advice.WorstMarginEndDb:F1} dB per-beam power reduction reaches the limit at the final alpha (dB-for-dB)")
                        : ""));
        }
        catch (Exception ex) { StatusText = "advise failed: " + ex.Message; }
        finally { IsRunning = false; }
    }

    /// <summary>
    /// The walk's outcome plus its trajectory: the worst margin at the
    /// first and last swept alpha (NaN when no sweep ran) and the
    /// latitude holding the final worst margin -- so a failed walk can
    /// say where the margin ended and which way it was moving, not just
    /// "not compliant".
    /// </summary>
    public sealed record Advice(double? FoundAlpha, List<ComplianceRow> FinalRows,
        int Iterations, List<double> FailingAtStart,
        double WorstMarginStartDb = double.NaN, double WorstMarginEndDb = double.NaN,
        double WorstLatEndDeg = double.NaN);

    /// <summary>How the worst margin moved over the walk, as a phrase.</summary>
    public static string TrendText(double startDb, double endDb)
        => !double.IsFinite(startDb) || !double.IsFinite(endDb) ? "trend unknown"
           : Math.Abs(endDb - startDb) < 0.5 ? "not changing with alpha"
           : endDb > startDb
               ? string.Create(CultureInfo.InvariantCulture, $"improving with alpha (+{endDb - startDb:F1} dB over the walk)")
               : string.Create(CultureInfo.InvariantCulture, $"worsening with alpha ({endDb - startDb:F1} dB over the walk)");

    /// <summary>
    /// Walks the global exclusion angle from the profile's value upward
    /// until the sweep is compliant or the cap is reached. Linear walk by
    /// design: a predictable run count under the user's duration/step.
    /// </summary>
    public static Advice Advise(Sweep sweep, double alphaStep, double alphaMax,
        IProgress<SweepProgress>? progress = null)
    {
        var inv = CultureInfo.InvariantCulture;
        double a0 = sweep.Profile.AlphaExclDeg;
        double span = Math.Max(alphaStep, alphaMax - a0) + alphaStep;   // walk length in alpha, for the bar
        var failingAtStart = new List<double>();
        var last = new List<ComplianceRow>();
        int iter = 0;
        double worstStart = double.NaN;
        for (double a = a0; a <= alphaMax + 1e-9; a += alphaStep)
        {
            iter++;
            double aNow = a; int iterNow = iter;
            IProgress<SweepProgress>? inner = progress is null ? null : new Relay<SweepProgress>(p =>
                progress.Report(new SweepProgress(
                    string.Create(inv, $"walk {iterNow} (alpha {aNow:F1}): ") + p.Text,
                    ((aNow - a0) + p.Fraction * alphaStep) / span)));
            last = RunSweep(sweep, a, inner);
            progress?.Report(new SweepProgress(string.Create(inv,
                $"walk {iterNow}: alpha {aNow:F1} -> worst {last.Min(r => r.WorstMarginDb):+0.0;-0.0} dB at lat {last.OrderBy(r => r.WorstMarginDb).First().LatDeg:F0}, {last.Count(r => !r.Pass)} latitude(s) failing"),
                ((aNow - a0) + alphaStep) / span));
            if (iter == 1)
            {
                failingAtStart = last.Where(r => !r.Pass).Select(r => r.LatDeg).ToList();
                worstStart = last.Min(r => r.WorstMarginDb);
            }
            if (last.All(r => r.Pass)) return new Advice(a, last, iter, failingAtStart,
                worstStart, last.Min(r => r.WorstMarginDb),
                last.OrderBy(r => r.WorstMarginDb).First().LatDeg);
        }
        var worstEnd = last.Count > 0 ? last.OrderBy(r => r.WorstMarginDb).First() : null;
        return new Advice(null, last, iter, failingAtStart,
            worstStart, worstEnd?.WorstMarginDb ?? double.NaN, worstEnd?.LatDeg ?? double.NaN);
    }

    /// <summary>Writes the found global exclusion back into the profile file (step 8's hand-off).</summary>
    public void ApplyFoundAlpha()
    {
        if (_foundAlphaDeg is not double a) return;
        var prof = OperationProfileCodec.Load(File.ReadAllText(_profilePath));
        // Interim guard (loop v2 design): applying a global REPLACES any
        // declared per-latitude rows -- say so rather than doing it silently.
        string wiped = prof.AlphaByLat is { Count: > 0 }
            ? " -- NOTE: the profile's per-latitude alpha rows were REPLACED by this global value"
            : "";
        File.WriteAllText(_profilePath,
            OperationProfileCodec.Save(prof with { AlphaExclDeg = a, AlphaByLat = null }));
        StatusText = string.Create(CultureInfo.InvariantCulture,
            $"alpha {a:F1} deg written into the profile -- derive the R set and export the masks next")
            + wiped;
    }

    // ---- loop v2, Nco leg: per-latitude cap synthesis --------------------
    // Design: docs/compliance-loop-plan.md "The loop, v2". The lever is
    // the per-cell co-frequency cap (MAX_CO_FREQ): scheduler-only, no
    // payload expressiveness gap, so the array form may write back today.

    private string _ncoRangeMinText = "1";
    /// <summary>Lower bound of the cap walk (a cap below 1 is no service).</summary>
    public string NcoRangeMinText { get => _ncoRangeMinText; set => SetField(ref _ncoRangeMinText, value); }

    private IReadOnlyList<ProfileLatRow>? _foundNcoRows;
    public bool ApplyNcoEnabled => _foundNcoRows is not null && !_isRunning;

    /// <summary>
    /// The effective cap baseline at one latitude: the declared view
    /// (nearest NcoByLat row inside its span, else the global) clamped by
    /// demand -- caps above demand are inert, so the walk starts at what
    /// the operation actually does.
    /// </summary>
    public static int EffectiveNcoBaseline(OperationProfile p, double latDeg)
    {
        int demand = Math.Max(1, p.DemandLinksPerCell);
        int? declared = null;
        var rows = p.NcoByLat;
        if (rows is { Count: > 0 })
        {
            double lo = rows.Min(r => r.LatDeg), hi = rows.Max(r => r.LatDeg);
            if (latDeg >= lo && latDeg <= hi)
                declared = (int)rows.OrderBy(r => Math.Abs(r.LatDeg - latDeg)).First().Value;
        }
        declared ??= p.NcoPerCell;
        return declared is int d ? Math.Max(1, Math.Min(d, demand)) : demand;
    }

    /// <summary>The v2 Nco advice: synthesized rows plus how they were earned.</summary>
    public sealed record NcoAdvice(bool LeverMoves, bool Converged, int Sweeps,
        int? GlobalCap, IReadOnlyList<ProfileLatRow> Rows, List<ComplianceRow> FinalRows);

    /// <summary>
    /// The v2 walk-synthesize-verify core over a sweep delegate (the
    /// harness tests it with a fake). Walk: a uniform delta DOWN from the
    /// per-latitude baseline, floored at rangeMin, until the sweep passes
    /// or the floor is reached. Synthesis: per latitude the smallest
    /// delta that passes and STAYS passing over the recorded walk.
    /// Verification: one joint sweep under the composed rows; regressed
    /// rows are tightened and the sweep repeats (fixed point, bounded).
    /// </summary>
    public static NcoAdvice NcoAdviseCore(IReadOnlyList<double> lats,
        IReadOnlyList<int> baseline, int rangeMin,
        Func<IReadOnlyList<int>, List<ComplianceRow>> sweepAt, int maxIter = 5,
        IProgress<SweepProgress>? progress = null)
    {
        var inv = CultureInfo.InvariantCulture;
        rangeMin = Math.Max(1, rangeMin);
        int maxDelta = Math.Max(0, baseline.Max() - rangeMin);
        int[] CapsAt(int d) => baseline.Select(b => Math.Max(rangeMin, b - d)).ToArray();
        int budget = maxDelta + 1 + maxIter;   // most sweeps the two phases can take, for the bar

        var outcomes = new List<(int Delta, List<ComplianceRow> Rows)>();
        int sweeps = 0;
        for (int d = 0; d <= maxDelta; d++)
        {
            var capsD = CapsAt(d);
            progress?.Report(new SweepProgress(string.Create(inv,
                $"v2 walk: delta {d} (caps {string.Join("/", capsD)}) -- sweeping..."), (double)sweeps / budget));
            var rows = sweepAt(capsD); sweeps++;
            outcomes.Add((d, rows));
            progress?.Report(new SweepProgress(string.Create(inv,
                $"v2 walk: delta {d} -> worst {rows.Min(r => r.WorstMarginDb):+0.0;-0.0} dB, {rows.Count(r => !r.Pass)} latitude(s) failing"),
                (double)sweeps / budget));
            if (rows.All(r => r.Pass)) break;
        }

        bool moves = outcomes.Count > 1 && outcomes.Zip(outcomes.Skip(1), (a, b) =>
                a.Rows.Zip(b.Rows, (x, y) => Math.Abs(x.WorstMarginDb - y.WorstMarginDb) > 1e-9).Any(x => x))
            .Any(x => x);

        var delta = new int[lats.Count];
        for (int i = 0; i < lats.Count; i++)
        {
            int chosen = -1;
            foreach (var o in outcomes)
            {
                if (!o.Rows[i].Pass) { chosen = -1; continue; }
                if (chosen < 0) chosen = o.Delta;
            }
            delta[i] = chosen >= 0 ? chosen : outcomes[^1].Delta;
        }

        List<ComplianceRow> final = outcomes[^1].Rows;
        bool converged = false;
        for (int it = 0; it < maxIter; it++)
        {
            var caps = lats.Select((_, i) => Math.Max(rangeMin, baseline[i] - delta[i])).ToArray();
            progress?.Report(new SweepProgress(string.Create(inv,
                $"v2 verify {it + 1}: caps {string.Join("/", caps)} -- joint sweep..."), (double)sweeps / budget));
            final = sweepAt(caps); sweeps++;
            progress?.Report(new SweepProgress(string.Create(inv,
                $"v2 verify {it + 1}: worst {final.Min(r => r.WorstMarginDb):+0.0;-0.0} dB, {final.Count(r => !r.Pass)} latitude(s) failing"),
                (double)sweeps / budget));
            if (final.All(r => r.Pass)) { converged = true; break; }
            var tightenable = Enumerable.Range(0, lats.Count)
                .Where(i => !final[i].Pass && baseline[i] - delta[i] > rangeMin).ToList();
            if (tightenable.Count == 0) break;
            foreach (int i in tightenable) delta[i]++;
        }

        var rowsOut = lats.Select((l, i) =>
            new ProfileLatRow(l, Math.Max(rangeMin, baseline[i] - delta[i]))).ToList();
        int? globalCap = outcomes[^1].Rows.All(r => r.Pass)
            ? baseline.Select(b => Math.Max(rangeMin, b - outcomes[^1].Delta)).Min()
            : null;
        return new NcoAdvice(moves, converged, sweeps, globalCap, rowsOut, final);
    }

    /// <summary>
    /// Profile variant with cap rows at the grid latitudes; operator rows
    /// OUTSIDE the grid span are kept (v2 never wipes declared structure).
    /// </summary>
    public static OperationProfile WithNcoRows(OperationProfile p,
        IReadOnlyList<double> lats, IReadOnlyList<int> caps)
    {
        double lo = lats.Min(), hi = lats.Max();
        var merged = new List<ProfileLatRow>();
        if (p.NcoByLat is { } ext)
            merged.AddRange(ext.Where(r => r.LatDeg < lo - 1e-9 || r.LatDeg > hi + 1e-9));
        merged.AddRange(lats.Select((l, i) => new ProfileLatRow(l, caps[i])));
        return p with { NcoByLat = merged.OrderBy(r => r.LatDeg).ToList() };
    }

    public async Task AdviseNcoAsync()
    {
        Sweep sweep; int rangeMin;
        try
        {
            sweep = BuildSweep();
            rangeMin = (int)Num(_ncoRangeMinText, "cap range floor");
        }
        catch (Exception ex) { StatusText = "invalid: " + ex.Message; return; }

        IsRunning = true;
        ProgressPercent = 0;
        _foundNcoRows = null; OnPropertyChanged(nameof(ApplyNcoEnabled));
        StatusText = "advising (v2): walking the per-cell cap down from the baseline...";
        var progress = UiProgress();
        // Nested sweeps relabel their lines under the v2 walk; the walk's own
        // lines (delta, verify) come from NcoAdviseCore.
        var sweepRelay = new Relay<SweepProgress>(p => progress.Report(new SweepProgress("v2 " + p.Text, p.Fraction)));
        try
        {
            var lats = new List<double>();
            for (double lat = sweep.LatFrom; lat <= sweep.LatTo + 1e-9; lat += sweep.LatStep) lats.Add(lat);
            var baseline = lats.Select(l => EffectiveNcoBaseline(sweep.Profile, l)).ToList();
            var advice = await Task.Run(() => NcoAdviseCore(lats, baseline, rangeMin,
                caps => RunSweepProfile(sweep, WithNcoRows(sweep.Profile, lats, caps), sweepRelay), 5, progress));
            ProgressPercent = 100;
            Rows.Clear();
            foreach (var r in advice.FinalRows) Rows.Add(r);
            if (!advice.LeverMoves)
            {
                StatusText = string.Create(CultureInfo.InvariantCulture,
                    $"Nco is not the lever here: no margin moved over the walk ({advice.Sweeps} sweep(s)) -- with demand {Math.Max(1, sweep.Profile.DemandLinksPerCell)} link(s)/cell the cap barely binds");
                return;
            }
            _foundNcoRows = advice.Rows; OnPropertyChanged(nameof(ApplyNcoEnabled));
            string rowsTxt = string.Join(", ", advice.Rows.Select(r =>
                string.Create(CultureInfo.InvariantCulture, $"{r.LatDeg:F0}→{r.Value:F0}")));
            StatusText = (advice.Converged
                    ? "v2 Nco rows VERIFIED (joint sweep passes): "
                    : "v2 Nco: NOT compliant even at the range floor -- tightest caps shown: ")
                + $"[{rowsTxt}]"
                + (advice.GlobalCap is int g
                    ? string.Create(CultureInfo.InvariantCulture, $"; uniform view: cap {g}")
                    : "")
                + string.Create(CultureInfo.InvariantCulture,
                    $" ({advice.Sweeps} sweep(s)) -- Apply Nco rows writes them into the profile");
        }
        catch (Exception ex) { StatusText = "advise failed: " + ex.Message; }
        finally { IsRunning = false; }
    }

    /// <summary>Writes the synthesized cap rows back; operator rows outside the grid span are kept.</summary>
    public void ApplyNcoRows()
    {
        if (_foundNcoRows is not { } found) return;
        var prof = OperationProfileCodec.Load(File.ReadAllText(_profilePath));
        double lo = found.Min(r => r.LatDeg), hi = found.Max(r => r.LatDeg);
        var merged = new List<ProfileLatRow>();
        if (prof.NcoByLat is { } ext)
            merged.AddRange(ext.Where(r => r.LatDeg < lo - 1e-9 || r.LatDeg > hi + 1e-9));
        merged.AddRange(found);
        File.WriteAllText(_profilePath, OperationProfileCodec.Save(
            prof with { NcoByLat = merged.OrderBy(r => r.LatDeg).ToList() }));
        StatusText = string.Create(CultureInfo.InvariantCulture,
            $"Nco rows written into the profile ({found.Count} row(s); operator rows outside the grid span kept) -- derive the R set next");
    }

    public string BuildCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("es_lat_deg,max_epfd_db,worst_margin_db,pass,quiet_steps");
        foreach (var r in Rows)
            sb.AppendLine(FormattableString.Invariant(
                $"{r.LatDeg},{r.MaxEpfdDb:F2},{r.WorstMarginDb:F2},{(r.Pass ? 1 : 0)},{r.QuietSteps}"));
        return sb.ToString();
    }

    private static double Num(string text, string what)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : throw new FormatException($"{what}: '{text.Trim()}' is not a number");
}
