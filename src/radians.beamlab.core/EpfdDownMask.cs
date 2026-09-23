using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using radcompute1503_2;
using radlimits;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>
/// A declared PFD mask readable per satellite state toward an earth
/// station: the S.1503-4 Sec. D5.1.5 read (nearest-latitude table, then
/// bilinear inside it) packaged behind one call. Implemented in the app
/// layer over the imported mask XML; raw dB in the mask's reference
/// bandwidth -- the -1000 "no transmission" floor participates as a
/// plain number, as in the reference implementation.
/// </summary>
public interface IMaskPfdRead
{
    double PfdDb(SatelliteState state, Vec3 satPosKm, Vec3 esPosKm);
}

/// <summary>
/// A mask read with no state of its own: <see cref="IMaskPfdRead.PfdDb"/> is
/// a function of its arguments alone (a mask file read the Sec. D5.1.5 way),
/// so an examination over it depends on nothing but the time of each step
/// and may evaluate its steps concurrently. A read that drives a scheduler
/// or caches per step does not declare this and is examined step by step.
/// </summary>
public interface IPureMaskPfdRead : IMaskPfdRead
{
}

/// <summary>
/// The examination run on the time step of S.1503-4 Sec. D4 (see
/// <see cref="EpfdDownMask.RunD4"/>): the same run length sampled every fine
/// step, and the two readings of the dual time step drawn from those samples.
/// All three use the same fine-step time grid, so their statistics differ
/// only in which samples they keep and how each is weighted.
/// </summary>
public sealed class D4ExamResult
{
    public required S1503TimeStep.Plan Plan { get; init; }
    /// <summary>Fine steps in the run: the time the statistics cover, in fine-step units.</summary>
    public required long FineSteps { get; init; }
    /// <summary>
    /// Dual time step, the fine-step region read as Sec. D4.7.1 defines it: a
    /// satellite with G_RX(phi) &gt; min[Gmax - 30 dB, G_RX(alpha0[lat])] at the
    /// last time step, near the main beam OR the exclusion-zone edge.
    /// </summary>
    public required EpfdDownResult Dual { get; init; }
    public required long DualSamples { get; init; }
    /// <summary>
    /// Dual time step, the fine-step region read as Sec. D5.1.4.1 Sub-step 6.3
    /// words it: a G_RX(phi) within 30 dB of peak at the last time step.
    /// </summary>
    public required EpfdDownResult DualMainBeamOnly { get; init; }
    public required long DualMainBeamOnlySamples { get; init; }
    /// <summary>Every fine step, no coarse steps.</summary>
    public required EpfdDownResult FineOnly { get; init; }
}

/// <summary>
/// epfd(down) the EXAMINATION's way: the declared PFD mask supplies the
/// radiated envelope and the declared operating-parameter set supplies
/// the selection gates -- S.1503-4 Sec. D5.1.4.1 Steps 10-24. Per step,
/// every satellite visible from the GSO earth station reads
/// pfd(lat, alpha, deltaLongitude) or pfd(lat, azimuth, elevation) from
/// the mask (Steps 13-14); satellites at or above the minimum elevation
/// eps0[lat][azimuth] and outside the exclusion zone alpha0[lat] are the
/// operating population, of which the MAX_CO_FREQ[lat] highest epfd_i
/// contribute, thinned by MIN_ANGLE_AT_ES against each selected
/// satellite (Steps 18-21); satellites whose receive gain exceeds
/// min(Gmax - 30 dB, Grx(alpha0)) contribute regardless, without double
/// counting (Steps 18/22). Same accumulator and bins as
/// <see cref="EpfdDown"/>, so the two footprint sources are
/// commensurable CDF for CDF.
///
/// <see cref="Run"/> samples one caller-chosen step size. <see cref="RunD4"/>
/// samples the time step of Sec. D4 and applies the dual time step of
/// Sub-steps 5.1 and 6.1-6.3 with the Step 24 weights. Not modelled:
/// MIN_OPERATING_HEIGHT (the R set here does not carry it). There is no
/// epfd(is) byproduct: an intersatellite run needs the e.i.r.p.(theta)
/// mask, not the pfd mask.
/// </summary>
public static class EpfdDownMask
{
    /// <summary>
    /// One step of the examination at one earth station: the per-step body of
    /// Steps 10-23, shared by <see cref="Run"/> and <see cref="RunD4"/>. Holds
    /// only the victim geometry and the declaration, all read-only, so one
    /// instance serves concurrent steps (each with its own constellation clone
    /// and scratch list).
    /// </summary>
    private sealed class StepExaminer
    {
        private readonly IMaskPfdRead _mask;
        private readonly OperatingParamsSet _declared;
        private readonly EpfdDownVictim _victim;
        private readonly double _simDur;
        private readonly Vec3 _es, _dirEsGso, _esN, _esE;
        private readonly double _gmax;
        private readonly int _n;
        private readonly Dictionary<(int shell, int plane), int> _orbIds = new();

        public StepExaminer(Constellation constellation, IMaskPfdRead mask,
            OperatingParamsSet declared, EpfdDownVictim victim, double simDur)
        {
            _mask = mask; _declared = declared; _victim = victim; _simDur = simDur;
            _es = GeodeticToEcef(victim.EsLatDeg, victim.EsLonDeg, 0.0);
            double gsoLonRad = victim.GsoLonDeg * Math.PI / 180.0;
            var gso = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(gsoLonRad),
                               GsoGeometry.GsoRadiusKm * Math.Sin(gsoLonRad), 0.0);
            _dirEsGso = (gso - _es).Normalized();
            _gmax = victim.Antenna.MaxGain;
            // North/east at the ES for Azimuth_NGSO -- the scheduler's convention.
            (_esN, _esE, _) = SatNedBasis(victim.EsLatDeg, victim.EsLonDeg);

            // SRS orb_id numbering, exactly as the Scheduler assigns it.
            _n = constellation.SatelliteCount;
            int orb = 0;
            for (int i = 0; i < _n; i++)
            {
                var st = constellation.StateAt(i, 0.0, simDur);
                if (!_orbIds.ContainsKey((st.ShellIndex, st.PlaneIndex)))
                    _orbIds[(st.ShellIndex, st.PlaneIndex)] = ++orb;
            }
        }

        /// <summary>
        /// The epfd (dB) at time <paramref name="t"/>, or -inf when nothing
        /// contributes. <paramref name="anyZoneEdge"/> reports a visible
        /// satellite with G_RX(phi) &gt; min[Gmax - 30 dB, G_RX(alpha0)] (the
        /// Sec. D4.7.1 fine-step region), <paramref name="anyMainBeam"/> one
        /// with G_RX(phi) within 30 dB of peak (Sub-step 6.3 as worded).
        /// </summary>
        public double Step(double t, Constellation con,
            List<(double EpfdDb, bool Operating, bool MainBeam, Vec3 ToSat)> entries,
            out bool anyZoneEdge, out bool anyMainBeam)
        {
            entries.Clear();
            anyZoneEdge = false;
            anyMainBeam = false;

            for (int i = 0; i < _n; i++)
            {
                var state = con.StateAt(i, t, _simDur);
                var pos = state.PositionEcefKm;
                double elev = ElevationAngleDeg(pos, _es);
                if (elev <= 0.0) continue;                    // Step 11 visibility

                double pfd = _mask.PfdDb(state, pos, _es);    // Steps 13-14

                var toSat = (pos - _es).Normalized();
                double phiDeg = Math.Acos(Math.Clamp(Vec3.Dot(_dirEsGso, toSat), -1.0, 1.0)) * 180.0 / Math.PI;
                double grx = _victim.Antenna.GetAntGain(phiDeg, 0.0);
                double epfdI = pfd + grx - _gmax;             // Steps 15-17

                // Step 18 classification against the declared set.
                double azDeg = Math.Atan2(Vec3.Dot(toSat, _esE), Vec3.Dot(toSat, _esN)) * 180.0 / Math.PI;
                if (azDeg < 0) azDeg += 360.0;
                int orbId = _orbIds[(state.ShellIndex, state.PlaneIndex)];
                double alpha0 = DeclaredConstraints.ExclusionAlphaDeg(_declared, _victim.EsLatDeg, orbId);
                bool operating =
                    GsoGeometry.AlphaMinAbsDeg(_es, pos) >= alpha0
                    && elev >= DeclaredConstraints.MinElevDeg(_declared, _victim.EsLatDeg, azDeg);
                bool mainBeam = grx > Math.Min(_gmax - 30.0, _victim.Antenna.GetAntGain(alpha0, 0.0));
                if (mainBeam) anyZoneEdge = true;
                if (grx > _gmax - 30.0) anyMainBeam = true;
                if (operating || mainBeam)
                    entries.Add((epfdI, operating, mainBeam, toSat));
            }

            // Steps 19-21: up to MAX_CO_FREQ[lat] operating satellites by
            // highest epfd_i, each pick pruning MIN_ANGLE_AT_ES violators.
            int cap = DeclaredConstraints.MaxCoFreq(_declared, _victim.EsLatDeg);
            double minSepDeg = DeclaredConstraints.MinAngleAtEsDeg(_declared);
            var operatingSet = new List<int>();
            for (int e = 0; e < entries.Count; e++)
                if (entries[e].Operating) operatingSet.Add(e);
            operatingSet.Sort((a, b) => entries[b].EpfdDb.CompareTo(entries[a].EpfdDb));

            double linear = 0.0;
            var counted = new HashSet<int>();
            var remaining = new List<int>(operatingSet);
            while (counted.Count < cap && remaining.Count > 0)
            {
                int picked = remaining[0];
                remaining.RemoveAt(0);
                linear += Math.Pow(10.0, entries[picked].EpfdDb / 10.0);   // Step 23
                counted.Add(picked);
                if (minSepDeg > 0.0)
                    remaining.RemoveAll(r => Math.Acos(Math.Clamp(
                        Vec3.Dot(entries[r].ToSat, entries[picked].ToSat), -1.0, 1.0))
                        * 180.0 / Math.PI < minSepDeg);
            }

            // Step 22: main-beam satellites contribute regardless, once.
            for (int e = 0; e < entries.Count; e++)
                if (entries[e].MainBeam && !counted.Contains(e))
                    linear += Math.Pow(10.0, entries[e].EpfdDb / 10.0);

            return linear > 0.0 ? 10.0 * Math.Log10(linear) : double.NegativeInfinity;
        }
    }

    public static EpfdDownResult Run(Constellation constellation, IMaskPfdRead mask,
        OperatingParamsSet declared, EpfdDownVictim victim, double timeStepSec, long steps,
        List<LimitPoint> limits, double? simulationDurationSec = null,
        IProgress<double>? progress = null)
    {
        double simDur = simulationDurationSec ?? timeStepSec * steps;
        long progressEvery = Math.Max(1, steps / 100);   // ~1% granularity for callers that listen
        var acc = new EpfdAccumulator(limits);
        var examiner = new StepExaminer(constellation, mask, declared, victim, simDur);

        double maxEpfd = double.NegativeInfinity;
        long quiet = 0;

        // One step's epfd (dB) at step k, or -inf when nothing contributes.
        // `con` supplies the states (the caller's constellation, or a worker's
        // own clone on the parallel path) and `entries` is reusable scratch.
        double StepEpfdDb(long k, Constellation con,
            List<(double EpfdDb, bool Operating, bool MainBeam, Vec3 ToSat)> entries)
            => examiner.Step(k * timeStepSec, con, entries, out _, out _);

        void Accumulate(double epfd)
        {
            if (!double.IsNegativeInfinity(epfd))
            {
                acc.AccumulateSample(epfd, 1);
                if (epfd > maxEpfd) maxEpfd = epfd;
            }
            else
            {
                acc.AccumulateSample(double.NegativeInfinity, 1);
                quiet++;
            }
        }

        if (SimulationParallel.Enabled && mask is IPureMaskPfdRead)
        {
            // No scheduler and a stateless read: every step is a function of
            // its time alone, so the steps are computed over time in parallel,
            // each worker on its own constellation clone (the propagators keep
            // per-call scratch state). The accumulator then takes the values in
            // step order -- the same sequence of samples as the sequential loop.
            var stepEpfd = new double[steps];
            long done = 0;
            Parallel.For(0L, steps, SimulationParallel.Options,
                () => (Con: constellation.Clone(),
                       Entries: new List<(double EpfdDb, bool Operating, bool MainBeam, Vec3 ToSat)>()),
                (k, _, local) =>
                {
                    stepEpfd[k] = StepEpfdDb(k, local.Con, local.Entries);
                    if (progress is not null)
                    {
                        long d = Interlocked.Increment(ref done);
                        if (d % progressEvery == 0) progress.Report((double)d / steps);
                    }
                    return local;
                },
                _ => { });
            for (long k = 0; k < steps; k++) Accumulate(stepEpfd[k]);
        }
        else
        {
            var entries = new List<(double EpfdDb, bool Operating, bool MainBeam, Vec3 ToSat)>();
            for (long k = 0; k < steps; k++)
            {
                if (progress is not null && k % progressEvery == 0) progress.Report((double)k / steps);
                Accumulate(StepEpfdDb(k, constellation, entries));
            }
        }

        return new EpfdDownResult
        {
            Accumulator = acc,
            Steps = steps,
            MaxEpfdDb = maxEpfd,
            QuietSteps = quiet,
        };
    }

    /// <summary>
    /// The samples of the dual time step over a run of
    /// <c>critical.Count</c> fine steps, as fine-step index and Step 24 weight
    /// (T_step / T_fine). Sub-step 6.1: the first step is fine. Sub-step 6.2:
    /// a fine step whenever fewer than N_coarse fine steps remain. Sub-step
    /// 6.3: a fine step when the LAST sample was critical, otherwise a coarse
    /// step of N_coarse fine steps. Each sample carries the step that reached
    /// it, so the weights sum to the number of fine steps.
    /// </summary>
    public static List<(long Index, int Weight)> DualChain(IReadOnlyList<bool> critical, int nCoarse)
    {
        var chain = new List<(long Index, int Weight)>();
        long n = critical.Count;
        if (n == 0) return chain;
        long m = 0;
        chain.Add((0, 1));                                   // Sub-step 6.1
        while (true)
        {
            long remaining = n - 1 - m;
            if (remaining <= 0) break;
            int step = nCoarse <= 1 || remaining < nCoarse || critical[(int)m] ? 1 : nCoarse;
            m += step;
            chain.Add((m, step));
        }
        return chain;
    }

    /// <summary>
    /// The examination on the time step of Sec. D4 over a run of
    /// <paramref name="runDurationSec"/>: every fine step of the plan is
    /// evaluated (in parallel over time when the read is pure), each with the
    /// two readings of the fine-step region; the fine-only statistics take
    /// every sample, and each dual-time-step reading takes the samples its
    /// chain visits with their Step 24 weights. The dual chains depend only on
    /// the geometry at each sample, so evaluating the whole fine grid gives
    /// exactly the samples a dual-step run would compute; the cost is the
    /// fine-only cost. The constellation is propagated with the same run
    /// duration as a <see cref="Run"/> over the same interval, so the
    /// trajectories are the same as the caller's other examinations.
    /// </summary>
    public static D4ExamResult RunD4(Constellation constellation, IMaskPfdRead mask,
        OperatingParamsSet declared, EpfdDownVictim victim, S1503TimeStep.Plan plan,
        double runDurationSec, List<LimitPoint> limits, IProgress<double>? progress = null)
    {
        double fine = plan.FineStepSec;
        long nFine = Math.Max(1, (long)Math.Round(runDurationSec / fine));
        if (nFine > int.MaxValue) throw new ArgumentException("run too long for one fine-step grid");
        var examiner = new StepExaminer(constellation, mask, declared, victim, runDurationSec);
        var epfd = new double[nFine];
        var zoneEdge = new bool[nFine];
        var mainBeam = new bool[nFine];
        long progressEvery = Math.Max(1, nFine / 100);

        if (SimulationParallel.Enabled && mask is IPureMaskPfdRead)
        {
            long done = 0;
            Parallel.For(0L, nFine, SimulationParallel.Options,
                () => (Con: constellation.Clone(),
                       Entries: new List<(double EpfdDb, bool Operating, bool MainBeam, Vec3 ToSat)>()),
                (m, _, local) =>
                {
                    epfd[m] = examiner.Step(m * fine, local.Con, local.Entries, out zoneEdge[m], out mainBeam[m]);
                    if (progress is not null)
                    {
                        long d = Interlocked.Increment(ref done);
                        if (d % progressEvery == 0) progress.Report((double)d / nFine);
                    }
                    return local;
                },
                _ => { });
        }
        else
        {
            var entries = new List<(double EpfdDb, bool Operating, bool MainBeam, Vec3 ToSat)>();
            for (long m = 0; m < nFine; m++)
            {
                if (progress is not null && m % progressEvery == 0) progress.Report((double)m / nFine);
                epfd[m] = examiner.Step(m * fine, constellation, entries, out zoneEdge[m], out mainBeam[m]);
            }
        }

        EpfdDownResult Accumulate(IEnumerable<(long Index, int Weight)> samples)
        {
            var acc = new EpfdAccumulator(limits);
            double maxEpfd = double.NegativeInfinity;
            long quiet = 0;
            foreach (var (m, w) in samples)
            {
                double e = epfd[m];
                if (!double.IsNegativeInfinity(e))
                {
                    acc.AccumulateSample(e, w);
                    if (e > maxEpfd) maxEpfd = e;
                }
                else
                {
                    acc.AccumulateSample(double.NegativeInfinity, w);
                    quiet += w;
                }
            }
            return new EpfdDownResult { Accumulator = acc, Steps = nFine, MaxEpfdDb = maxEpfd, QuietSteps = quiet };
        }

        IEnumerable<(long Index, int Weight)> EveryFineStep()
        {
            for (long m = 0; m < nFine; m++) yield return (m, 1);
        }

        var chainEdge = DualChain(zoneEdge, plan.NCoarse);
        var chainMain = DualChain(mainBeam, plan.NCoarse);
        return new D4ExamResult
        {
            Plan = plan,
            FineSteps = nFine,
            Dual = Accumulate(chainEdge),
            DualSamples = chainEdge.Count,
            DualMainBeamOnly = Accumulate(chainMain),
            DualMainBeamOnlySamples = chainMain.Count,
            FineOnly = Accumulate(EveryFineStep()),
        };
    }
}
