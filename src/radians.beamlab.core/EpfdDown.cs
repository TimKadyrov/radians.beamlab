using System;
using System.Collections.Generic;
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

/// <summary>Result of an epfd(down) run: the examination-binned statistics.</summary>
public sealed class EpfdDownResult
{
    public required EpfdAccumulator Accumulator { get; init; }
    public required long Steps { get; init; }
    public required double MaxEpfdDb { get; init; }
    /// <summary>Steps in which no visible satellite contributed (accumulated as no-epfd).</summary>
    public required long QuietSteps { get; init; }
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
    public static EpfdDownResult Run(Constellation constellation, IBeamPointing pointing,
        EpfdDownVictim victim, double timeStepSec, long steps, List<LimitPoint> limits,
        double? simulationDurationSec = null)
    {
        double simDur = simulationDurationSec ?? timeStepSec * steps;
        var acc = new EpfdAccumulator(limits);

        var es = GeodeticToEcef(victim.EsLatDeg, victim.EsLonDeg, 0.0);
        double gsoLonRad = victim.GsoLonDeg * Math.PI / 180.0;
        var gso = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(gsoLonRad),
                           GsoGeometry.GsoRadiusKm * Math.Sin(gsoLonRad), 0.0);
        var dirEsGso = (gso - es).Normalized();
        double gmax = victim.Antenna.MaxGain;

        double maxEpfd = double.NegativeInfinity;
        long quiet = 0;

        for (long k = 0; k < steps; k++)
        {
            double t = k * timeStepSec;
            var snap = constellation.SnapshotAt(t, simDur, pointing);

            double linear = 0.0;
            foreach (var sat in snap.Satellites)
            {
                if (sat.Beams is null || sat.Beams.Beams.Count == 0) continue;
                var pos = sat.State.PositionEcefKm;
                if (ElevationAngleDeg(pos, es) <= 0.0) continue;   // below the ES horizon

                var toEs = (es - pos).Normalized();
                double eirp = BeamComposer.CompositeEirpDbw(sat.Beams.Beams, toEs, sat.Beams.PowersDbw);
                if (double.IsNegativeInfinity(eirp)) continue;

                double distM = (es - pos).Length * 1000.0;
                double pfd = eirp - 10.0 * Math.Log10(4.0 * Math.PI * distM * distM);

                var toSat = (pos - es).Normalized();
                double phiDeg = Math.Acos(Math.Clamp(Vec3.Dot(dirEsGso, toSat), -1.0, 1.0)) * 180.0 / Math.PI;
                double grx = victim.Antenna.GetAntGain(phiDeg, 0.0);

                linear += Math.Pow(10.0, (pfd + grx - gmax) / 10.0);
            }

            if (linear > 0.0)
            {
                double epfd = 10.0 * Math.Log10(linear);
                acc.AccumulateSample(epfd, 1);
                if (epfd > maxEpfd) maxEpfd = epfd;
            }
            else
            {
                // Below-range samples classify as no-epfd inside the accumulator.
                acc.AccumulateSample(double.NegativeInfinity, 1);
                quiet++;
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
}
