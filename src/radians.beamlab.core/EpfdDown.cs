using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using radantenna;
using radcompute1503_2;
using radlimits;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>The epfd(down) victim: a GSO earth station tracking a GSO satellite.</summary>
public sealed class EpfdDownVictim
{
    public double EsLatDeg { get; init; }
    public double EsLonDeg { get; init; }
    /// <summary>Longitude of the wanted GSO satellite (deg).</summary>
    public double GsoLonDeg { get; init; }
    /// <summary>
    /// Receive antenna (vendored radians library) -- e.g.
    /// new AntennaLibrary(ApType.APERR_019V01, freqMHz, diameterM) for the
    /// Rec. ITU-R S.1428 FSS pattern the epfd(down) examination uses.
    /// </summary>
    public required AntennaLibrary Antenna { get; init; }
}

/// <summary>
/// The epfd(up)/epfd(is) victim: a GSO satellite with its receive beam
/// pointed at a boresight test point on the Earth. The pattern is the
/// Rec. ITU-R S.672-4 reference of S.1503-4 Sec. D6.5.2, parameterised by
/// peak gain and half-power beamwidth (Table 16 pairs) and the near-in
/// side-lobe level Ls (RR Article 22 uses -20).
/// </summary>
public sealed class EpfdGsoSatVictim
{
    /// <summary>Longitude of the victim GSO satellite (deg).</summary>
    public double GsoLonDeg { get; init; }
    public double BoresightLatDeg { get; init; }
    public double BoresightLonDeg { get; init; }
    /// <summary>S.672 pattern instance, e.g. new AntennaLibrary(ApType.APSREC408V01, freqMHz, null).</summary>
    public required AntennaLibrary Antenna { get; init; }
    public required double GmaxDbi { get; init; }
    public required double Phi3DbDeg { get; init; }
    public double LsDb { get; init; } = -20.0;

    /// <summary>Relative receive gain (dB) at off-boresight angle psi.</summary>
    public double RelativeGainDb(double psiDeg)
        => Antenna.GetAntGain(psiDeg, 0.0, GmaxDbi, Phi3DbDeg, LsDb) - GmaxDbi;
}

/// <summary>Result of an epfd(down) run: the examination-binned statistics.</summary>
public sealed class EpfdDownResult
{
    public required EpfdAccumulator Accumulator { get; init; }
    public required long Steps { get; init; }
    public required double MaxEpfdDb { get; init; }
    /// <summary>Steps in which no visible satellite contributed (accumulated as no-epfd).</summary>
    public required long QuietSteps { get; init; }

    /// <summary>epfd(is) byproduct statistics; null unless an isVictim was supplied.</summary>
    public EpfdAccumulator? IsAccumulator { get; init; }
    public double MaxEpfdIsDb { get; init; } = double.NegativeInfinity;
    public long IsQuietSteps { get; init; }

    /// <summary>
    /// The same statistics over the run's first prefixSteps steps, when the
    /// caller asked for them: exactly what a separate run of that many steps
    /// over the same horizon accumulates, taken from this run. Null otherwise.
    /// </summary>
    public EpfdDownResult? Prefix { get; init; }
}

/// <summary>
/// WP8: beamlab's own epfd(down) statistics over the simulated system -- the
/// live beam composition, not the declared mask. Per step and per visible
/// satellite: pfd at the earth station from the actual resolved beam set,
/// plus the receive-gain discrimination toward the wanted GSO satellite,
/// power-summed over satellites (RR Article 22 definition):
///
///   epfd = 10 log10  SUM_i  10^((pfd_i + Grx(phi_i) - Grx,max) / 10)
///
/// Samples land in the vendored examination accumulator (S.1503-4
/// Sec. D7.1.2, 0.1 dB bins), so the simulated CDF and the examination CDF
/// are commensurable bin for bin -- the margin measurement of spec Sec. 8.
/// Sampling depth per spec Sec. 7 is the caller's choice of step and count.
/// </summary>
public static class EpfdDown
{
    /// <summary>
    /// One victim: a thin wrapper over <see cref="RunMany"/>, so the single-
    /// and multi-victim paths cannot drift apart.
    /// </summary>
    public static EpfdDownResult Run(Constellation constellation, IBeamPointing pointing,
        EpfdDownVictim victim, double timeStepSec, long steps, List<LimitPoint> limits,
        double? simulationDurationSec = null,
        EpfdGsoSatVictim? isVictim = null, List<LimitPoint>? isLimits = null,
        IProgress<double>? progress = null, long? prefixSteps = null)
        => RunMany(constellation, pointing, new[] { victim }, timeStepSec, steps, limits,
            simulationDurationSec, isVictim, isLimits, progress, prefixSteps)[0];

    /// <summary>
    /// Many victims, ONE simulation. A victim is only an accumulator: the
    /// system’s behaviour -- propagation, scheduling, beam resolution -- does
    /// not depend on it, so a latitude grid costs one pass over the
    /// constellation instead of one pass per grid point. Each victim keeps its
    /// own accumulator and, per victim, satellites are summed in the same
    /// order as the single-victim path, so results are identical to running
    /// the victims separately.
    ///
    /// The epfd(is) byproduct concerns the GSO SATELLITE victim rather than the
    /// earth stations, so it is computed once and reported on the first result.
    ///
    /// With <paramref name="prefixSteps"/>, each result also carries the
    /// statistics of the first prefixSteps steps (<see cref="EpfdDownResult.Prefix"/>),
    /// copied from the accumulators once that step is done: the samples, their
    /// order and the horizon are those of a separate run of prefixSteps steps.
    /// </summary>
    public static IReadOnlyList<EpfdDownResult> RunMany(Constellation constellation,
        IBeamPointing pointing, IReadOnlyList<EpfdDownVictim> victims,
        double timeStepSec, long steps, List<LimitPoint> limits,
        double? simulationDurationSec = null,
        EpfdGsoSatVictim? isVictim = null, List<LimitPoint>? isLimits = null,
        IProgress<double>? progress = null, long? prefixSteps = null)
    {
        if (victims.Count == 0)
            throw new ArgumentException("at least one victim", nameof(victims));
        if (prefixSteps is long np0 && (np0 <= 0 || np0 > steps))
            throw new ArgumentOutOfRangeException(nameof(prefixSteps), "a prefix is 1 to steps steps");
        double simDur = simulationDurationSec ?? timeStepSec * steps;
        long progressEvery = Math.Max(1, steps / 100);   // ~1% granularity for callers that listen

        int nv = victims.Count;
        var acc = new EpfdAccumulator[nv];
        var es = new Vec3[nv];
        var dirEsGso = new Vec3[nv];
        var gmax = new double[nv];
        var maxEpfd = new double[nv];
        var quiet = new long[nv];
        var linear = new double[nv];
        for (int v = 0; v < nv; v++)
        {
            acc[v] = new EpfdAccumulator(limits);
            es[v] = GeodeticToEcef(victims[v].EsLatDeg, victims[v].EsLonDeg, 0.0);
            double gsoLonRad = victims[v].GsoLonDeg * Math.PI / 180.0;
            var gso = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(gsoLonRad),
                               GsoGeometry.GsoRadiusKm * Math.Sin(gsoLonRad), 0.0);
            dirEsGso[v] = (gso - es[v]).Normalized();
            gmax[v] = victims[v].Antenna.MaxGain;
            maxEpfd[v] = double.NegativeInfinity;
        }

        // epfd(is) byproduct (Sec. D5.3.5): the same resolved beam sets,
        // composed toward the GSO satellite victim. No exclusion or
        // selection gating -- every non-Earth-blocked station contributes.
        var accIs = isVictim is null ? null : new EpfdAccumulator(isLimits ?? limits);
        Vec3 gsoIs = default, isBoresightDir = default;
        if (isVictim is not null)
        {
            double lonIs = isVictim.GsoLonDeg * Math.PI / 180.0;
            gsoIs = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(lonIs),
                             GsoGeometry.GsoRadiusKm * Math.Sin(lonIs), 0.0);
            var bs = GeodeticToEcef(isVictim.BoresightLatDeg, isVictim.BoresightLonDeg, 0.0);
            isBoresightDir = (bs - gsoIs).Normalized();
        }

        double maxEpfdIs = double.NegativeInfinity;
        long quietIs = 0;
        EpfdDownResult[]? prefix = null;

        // Per-satellite linear terms of one step: terms[i * nv + v] toward
        // victim v and termsIs[i] toward the GSO satellite, 0 where the
        // satellite does not contribute. Satellites are independent once the
        // snapshot is resolved, so the terms are computed in parallel; the
        // sums are then taken in satellite order, the order of the sequential
        // loop, so the accumulated values are identical bit for bit.
        int nSat = constellation.SatelliteCount;
        var terms = new double[nSat * nv];
        var termsIs = new double[nSat];

        for (long k = 0; k < steps; k++)
        {
            if (progress is not null && k % progressEvery == 0) progress.Report((double)k / steps);
            double t = k * timeStepSec;
            var snap = constellation.SnapshotAt(t, simDur, pointing);

            void Terms(int i)
            {
                var sat = snap.Satellites[i];
                double termIs = 0.0;
                int baseIdx = i * nv;
                for (int v = 0; v < nv; v++) terms[baseIdx + v] = 0.0;
                if (sat.Beams is not null && sat.Beams.Beams.Count > 0)
                {
                    var pos = sat.State.PositionEcefKm;

                    if (accIs is not null && !EarthBlocked(pos, gsoIs))
                    {
                        var toGso = (gsoIs - pos).Normalized();
                        double eirpIs = BeamComposer.ResolvedEirpDbw(sat.Beams, toGso);
                        if (!double.IsNegativeInfinity(eirpIs))
                        {
                            double dIsM = (gsoIs - pos).Length * 1000.0;
                            var toSatIs = (pos - gsoIs).Normalized();
                            double psiDeg = Math.Acos(Math.Clamp(
                                Vec3.Dot(isBoresightDir, toSatIs), -1.0, 1.0)) * 180.0 / Math.PI;
                            termIs = Math.Pow(10.0,
                                (eirpIs - 10.0 * Math.Log10(4.0 * Math.PI * dIsM * dIsM)
                                 + isVictim!.RelativeGainDb(psiDeg)) / 10.0);
                        }
                    }

                    for (int v = 0; v < nv; v++)
                    {
                        if (ElevationAngleDeg(pos, es[v]) <= 0.0) continue;   // below this ES horizon

                        var toEs = (es[v] - pos).Normalized();
                        double eirp = BeamComposer.ResolvedEirpDbw(sat.Beams, toEs);
                        if (double.IsNegativeInfinity(eirp)) continue;

                        double distM = (es[v] - pos).Length * 1000.0;
                        double pfd = eirp - 10.0 * Math.Log10(4.0 * Math.PI * distM * distM);

                        var toSat = (pos - es[v]).Normalized();
                        double phiDeg = Math.Acos(Math.Clamp(Vec3.Dot(dirEsGso[v], toSat), -1.0, 1.0)) * 180.0 / Math.PI;
                        double grx = victims[v].Antenna.GetAntGain(phiDeg, 0.0);

                        terms[baseIdx + v] = Math.Pow(10.0, (pfd + grx - gmax[v]) / 10.0);
                    }
                }
                termsIs[i] = termIs;
            }

            if (SimulationParallel.Enabled) Parallel.For(0, nSat, SimulationParallel.Options, Terms);
            else for (int i = 0; i < nSat; i++) Terms(i);

            Array.Clear(linear, 0, nv);
            double linearIs = 0.0;
            for (int i = 0; i < nSat; i++)
            {
                if (termsIs[i] != 0.0) linearIs += termsIs[i];
                int baseIdx = i * nv;
                for (int v = 0; v < nv; v++)
                    if (terms[baseIdx + v] != 0.0) linear[v] += terms[baseIdx + v];
            }

            for (int v = 0; v < nv; v++)
            {
                if (linear[v] > 0.0)
                {
                    double epfd = 10.0 * Math.Log10(linear[v]);
                    acc[v].AccumulateSample(epfd, 1);
                    if (epfd > maxEpfd[v]) maxEpfd[v] = epfd;
                }
                else
                {
                    // Below-range samples classify as no-epfd inside the accumulator.
                    acc[v].AccumulateSample(double.NegativeInfinity, 1);
                    quiet[v]++;
                }
            }

            if (accIs is not null)
            {
                if (linearIs > 0.0)
                {
                    double epfdIs = 10.0 * Math.Log10(linearIs);
                    accIs.AccumulateSample(epfdIs, 1);
                    if (epfdIs > maxEpfdIs) maxEpfdIs = epfdIs;
                }
                else
                {
                    accIs.AccumulateSample(double.NegativeInfinity, 1);
                    quietIs++;
                }
            }

            if (k == prefixSteps - 1)
            {
                prefix = new EpfdDownResult[nv];
                EpfdAccumulator? accIsP = null;
                if (accIs is not null) { accIsP = new EpfdAccumulator(isLimits ?? limits); accIsP.MergeFrom(accIs); }
                for (int v = 0; v < nv; v++)
                {
                    var accP = new EpfdAccumulator(limits);
                    accP.MergeFrom(acc[v]);
                    prefix[v] = new EpfdDownResult
                    {
                        Accumulator = accP,
                        Steps = k + 1,
                        MaxEpfdDb = maxEpfd[v],
                        QuietSteps = quiet[v],
                        IsAccumulator = v == 0 ? accIsP : null,
                        MaxEpfdIsDb = v == 0 ? maxEpfdIs : double.NegativeInfinity,
                        IsQuietSteps = v == 0 ? quietIs : 0,
                    };
                }
            }
        }

        var results = new EpfdDownResult[nv];
        for (int v = 0; v < nv; v++)
            results[v] = new EpfdDownResult
            {
                Accumulator = acc[v],
                Steps = steps,
                MaxEpfdDb = maxEpfd[v],
                QuietSteps = quiet[v],
                IsAccumulator = v == 0 ? accIs : null,
                MaxEpfdIsDb = v == 0 ? maxEpfdIs : double.NegativeInfinity,
                IsQuietSteps = v == 0 ? quietIs : 0,
                Prefix = prefix?[v],
            };
        return results;
    }

    /// <summary>
    /// True when the Earth sphere blocks the segment between two points
    /// (S.1503-4 Sec. D6.4.1 visibility): the closest approach of the
    /// segment to the Earth's centre lies below the surface.
    /// </summary>
    internal static bool EarthBlocked(Vec3 aKm, Vec3 bKm)
    {
        var d = bKm - aKm;
        double len2 = d.LengthSq;
        double tStar = len2 == 0.0 ? 0.0 : Math.Clamp(-Vec3.Dot(aKm, d) / len2, 0.0, 1.0);
        var closest = aKm + d * tStar;
        return closest.Length < EarthRadiusKm;
    }
}
