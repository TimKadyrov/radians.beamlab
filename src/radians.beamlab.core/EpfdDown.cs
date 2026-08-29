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
        double? simulationDurationSec = null,
        EpfdGsoSatVictim? isVictim = null, List<LimitPoint>? isLimits = null)
    {
        double simDur = simulationDurationSec ?? timeStepSec * steps;
        var acc = new EpfdAccumulator(limits);

        var es = GeodeticToEcef(victim.EsLatDeg, victim.EsLonDeg, 0.0);
        double gsoLonRad = victim.GsoLonDeg * Math.PI / 180.0;
        var gso = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(gsoLonRad),
                           GsoGeometry.GsoRadiusKm * Math.Sin(gsoLonRad), 0.0);
        var dirEsGso = (gso - es).Normalized();
        double gmax = victim.Antenna.MaxGain;

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

        double maxEpfd = double.NegativeInfinity, maxEpfdIs = double.NegativeInfinity;
        long quiet = 0, quietIs = 0;

        for (long k = 0; k < steps; k++)
        {
            double t = k * timeStepSec;
            var snap = constellation.SnapshotAt(t, simDur, pointing);

            double linear = 0.0, linearIs = 0.0;
            foreach (var sat in snap.Satellites)
            {
                if (sat.Beams is null || sat.Beams.Beams.Count == 0) continue;
                var pos = sat.State.PositionEcefKm;

                if (accIs is not null && !EarthBlocked(pos, gsoIs))
                {
                    var toGso = (gsoIs - pos).Normalized();
                    double eirpIs = BeamComposer.CompositeEirpDbw(sat.Beams.Beams, toGso, sat.Beams.PowersDbw);
                    if (!double.IsNegativeInfinity(eirpIs))
                    {
                        double dIsM = (gsoIs - pos).Length * 1000.0;
                        var toSatIs = (pos - gsoIs).Normalized();
                        double psiDeg = Math.Acos(Math.Clamp(
                            Vec3.Dot(isBoresightDir, toSatIs), -1.0, 1.0)) * 180.0 / Math.PI;
                        linearIs += Math.Pow(10.0,
                            (eirpIs - 10.0 * Math.Log10(4.0 * Math.PI * dIsM * dIsM)
                             + isVictim!.RelativeGainDb(psiDeg)) / 10.0);
                    }
                }

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
        }

        return new EpfdDownResult
        {
            Accumulator = acc,
            Steps = steps,
            MaxEpfdDb = maxEpfd,
            QuietSteps = quiet,
            IsAccumulator = accIs,
            MaxEpfdIsDb = maxEpfdIs,
            IsQuietSteps = quietIs,
        };
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
