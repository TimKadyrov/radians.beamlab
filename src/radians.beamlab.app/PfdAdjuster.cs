using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Result of one <see cref="PfdAdjuster.Run"/> call. <c>Status</c> is OK when
/// the adjustment ran (even if 0 beams needed change); the various counts /
/// margin describe what changed.
/// </summary>
public readonly record struct PfdAdjustResult(
    PfdAdjustStatus Status,
    int Contributors,
    int Adjusted,
    int SwitchedOff,
    double MaxMarginAfterDb,
    string CountryName,
    string ModeLabel)
{
    public static PfdAdjustResult Failure(PfdAdjustStatus s) =>
        new(s, 0, 0, 0, double.NaN, "", "");
}

public enum PfdAdjustStatus
{
    Ok,
    NoCountrySelected,
    CountryNotFound,
    NoSamplesProduced,
    NoContributors,
}

/// <summary>
/// Per-beam G_m adjustment to keep aggregate (or single-beam) PFD over a
/// protected country at or below an elevation-dependent mask. Pure logic — no
/// UI or VM dependencies.
/// </summary>
public static class PfdAdjuster
{
    public delegate double PfdLimitFunc(double elevDeg);

    public sealed class Options
    {
        public double TxPowerDbw { get; init; }
        public required PfdLimitFunc LimitAtElevation { get; init; }
        public HeatmapMode Mode { get; init; }

        /// <summary>
        /// Absolute peak-gain floor (dBi). A beam is switched off (instead of
        /// having G_m reduced further) once the reduction would push the new
        /// G_m below this value. Independent of pattern type and LF — set as a
        /// single project-wide value.
        /// </summary>
        public double MinGmDbi { get; init; } = 5.0;

        public int TargetSamples { get; init; } = 20000;
        /// <summary>Beams with worst-case excess below this many dB are ignored as negligible.</summary>
        public double NegligibleExcessDb { get; init; } = -30.0;
    }

    /// <summary>
    /// Run the adjustment. Mutates <see cref="Beam.Pattern"/> and
    /// <see cref="Beam.Weight"/> on contributing beams of <paramref name="scene"/>.
    /// Returns metrics for the caller to display.
    /// </summary>
    public static PfdAdjustResult Run(SceneModel scene, CountryGeometry country, Options opts)
    {
        if (country is null) return PfdAdjustResult.Failure(PfdAdjustStatus.CountryNotFound);

        var samples = country.SampleInterior(opts.TargetSamples);
        if (samples.Count == 0) return PfdAdjustResult.Failure(PfdAdjustStatus.NoSamplesProduced);

        // Pre-compute per-sample geometry + applicable PFD limit.
        var sat = scene.SatEcef;
        int M = samples.Count;
        var looks = new Vec3[M];
        var pathLossDb = new double[M];
        var pfdLimitArr = new double[M];
        for (int i = 0; i < M; i++)
        {
            var (lat, lon) = samples[i];
            var ground = GeodeticToEcef(lat, lon, 0.0);
            var dVec = ground - sat;
            double dM = dVec.Length * 1000.0;
            looks[i] = dVec.Normalized();
            pathLossDb[i] = 10.0 * Math.Log10(4.0 * Math.PI * dM * dM);
            pfdLimitArr[i] = opts.LimitAtElevation(ElevationAngleDeg(sat, ground));
        }

        // Identify contributors. Each beam's worst-case search is independent
        // and read-only on the scene; parallelise across beams.
        var contributorBag = new ConcurrentBag<Beam>();
        var allBeams = scene.Beams;
        Parallel.For(0, allBeams.Count, k =>
        {
            var beam = allBeams[k];
            if (beam.Weight <= 0) return;
            double maxExcess = double.NegativeInfinity;
            for (int i = 0; i < M; i++)
            {
                double g = beam.GainDbi(looks[i]) + 10.0 * Math.Log10(beam.Weight);
                double pfd = opts.TxPowerDbw + g - pathLossDb[i];
                double excess = pfd - pfdLimitArr[i];
                if (excess > maxExcess) maxExcess = excess;
            }
            if (maxExcess > opts.NegligibleExcessDb) contributorBag.Add(beam);
        });
        var contributors = new List<Beam>(contributorBag);
        if (contributors.Count == 0)
            return new PfdAdjustResult(PfdAdjustStatus.NoContributors, 0, 0, 0, double.NaN, country.Name, "");

        int n = contributors.Count;
        bool aggregateMode = opts.Mode != HeatmapMode.MaxSingleBeam;
        double allocBias = aggregateMode ? 10.0 * Math.Log10(n) : 0.0;

        // Parallel compute per-beam max excess vs allocation; sequential apply
        // so the (Weight, Pattern) writes don't race.
        var perBeamExcess = new double[contributors.Count];
        Parallel.For(0, contributors.Count, k =>
        {
            var beam = contributors[k];
            double maxExcess = double.NegativeInfinity;
            for (int i = 0; i < M; i++)
            {
                double g = beam.GainDbi(looks[i]) + 10.0 * Math.Log10(beam.Weight);
                double pfd = opts.TxPowerDbw + g - pathLossDb[i];
                double excess = pfd - (pfdLimitArr[i] - allocBias);
                if (excess > maxExcess) maxExcess = excess;
            }
            perBeamExcess[k] = maxExcess;
        });

        int adjusted = 0;
        int switchedOff = 0;
        for (int k = 0; k < contributors.Count; k++)
        {
            double maxExcess = perBeamExcess[k];
            if (maxExcess <= 0) continue;
            var beam = contributors[k];

            double prior = beam.OriginalGmDbi - beam.Pattern.Gm;
            double total = prior + maxExcess;
            double newGm = beam.OriginalGmDbi - total;
            // Switch off when the reduction would drop G_m below the absolute
            // minimum gain floor configured for the analysis.
            if (newGm < opts.MinGmDbi)
            {
                beam.Weight = 0.0;
                switchedOff++;
                continue;
            }

            beam.Pattern = scene.BuildPatternFor(newGm, beam.OffNadirDeg);
            adjusted++;
        }

        // Post-adjust: max (PFD − limit) over the country, in the active mode.
        // Per-sample work is independent and read-only; parallelise across samples.
        var perSampleMargin = new double[M];
        var beamsArray = scene.Beams;
        Parallel.For(0, M, i =>
        {
            double linearSum = 0.0;
            double maxSinglePfd = double.NegativeInfinity;
            for (int k = 0; k < beamsArray.Count; k++)
            {
                var beam = beamsArray[k];
                if (beam.Weight <= 0) continue;
                double g = beam.GainDbi(looks[i]) + 10.0 * Math.Log10(beam.Weight);
                double pfd = opts.TxPowerDbw + g - pathLossDb[i];
                linearSum += Math.Pow(10.0, pfd / 10.0);
                if (pfd > maxSinglePfd) maxSinglePfd = pfd;
            }
            double current = aggregateMode
                ? (linearSum > 0 ? 10.0 * Math.Log10(linearSum) : double.NegativeInfinity)
                : maxSinglePfd;
            perSampleMargin[i] = current > double.NegativeInfinity
                ? current - pfdLimitArr[i]
                : double.NegativeInfinity;
        });
        double maxMarginAfter = double.NegativeInfinity;
        foreach (var m in perSampleMargin) if (m > maxMarginAfter) maxMarginAfter = m;

        string modeLabel = aggregateMode ? "aggregate" : "single-beam max";
        return new PfdAdjustResult(
            PfdAdjustStatus.Ok, n, adjusted, switchedOff, maxMarginAfter, country.Name, modeLabel);
    }
}
