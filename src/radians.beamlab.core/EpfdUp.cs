using System;
using System.Collections.Generic;
using System.Linq;
using radantenna;
using radcompute1503_2;
using radlimits;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>
/// The transmitting non-GSO earth-station model for epfd(up): power into
/// the antenna per reference bandwidth, and the transmit pattern (gain
/// included). Using the same antenna family the declared E masks envelope
/// keeps simulated eirp at or below the mask by construction.
/// </summary>
public sealed class EpfdUpEsModel
{
    /// <summary>Power ceiling (dBW per reference bandwidth) -- the declared mask base.</summary>
    public required double PowerDbw { get; init; }
    public required AntennaLibrary Antenna { get; init; }

    /// <summary>
    /// Range-based closed-loop power control (Rec. S.1325 "power control on
    /// range"): when set, the ceiling corresponds to the slant range at this
    /// elevation, and each link transmits 20 log10(d_ref / d_link) below it
    /// -- constant flux at the serving satellite. Null keeps the ceiling.
    /// </summary>
    public double? PowerControlRefElevDeg { get; init; }
}

/// <summary>Result of an epfd(up) run: the examination-binned statistics.</summary>
public sealed class EpfdUpResult
{
    public required EpfdAccumulator Accumulator { get; init; }
    public required long Steps { get; init; }
    public required double MaxEpfdDb { get; init; }
    /// <summary>Steps with no contributing link (accumulated as no-epfd).</summary>
    public required long QuietSteps { get; init; }
}

/// <summary>
/// beamlab's own epfd(up) statistics over the simulated system -- the
/// truth-side counterpart of S.1503-4 Sec. D5.2.6. The transmitting earth
/// stations are the scheduler's active links (the same scheduler that
/// drives the downlink dwell/Nco behaviour): each served cell transmits
/// toward its serving satellite, and the off-axis gain toward the GSO
/// victim satellite gives the eirp per RR Article 22:
///
///   epfd(up) = 10 log10  SUM_links  10^((eirp_i - Lfs_i + Grel_i) / 10)
///
/// Every declared bound -- including MAX_CO_FREQ_SAT and the angular
/// separation gates -- is enforced by the Scheduler during assignment, so
/// the links arrive here already declaration-compliant and are consumed as
/// granted. Samples land in the vendored examination accumulator
/// (Sec. D7.1.2, 0.1 dB bins).
/// </summary>
public static class EpfdUp
{
    public static EpfdUpResult Run(Constellation constellation, Scheduler scheduler,
        ServiceGeography geography, EpfdGsoSatVictim victim,
        EpfdUpEsModel es, double timeStepSec, long steps, List<LimitPoint> limits,
        double? simulationDurationSec = null)
    {
        double simDur = simulationDurationSec ?? timeStepSec * steps;
        var acc = new EpfdAccumulator(limits);

        double gsoLonRad = victim.GsoLonDeg * Math.PI / 180.0;
        var gso = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(gsoLonRad),
                           GsoGeometry.GsoRadiusKm * Math.Sin(gsoLonRad), 0.0);
        var bs = GeodeticToEcef(victim.BoresightLatDeg, victim.BoresightLonDeg, 0.0);
        var boresightDir = (bs - gso).Normalized();

        var cellEcef = new Dictionary<int, Vec3>();
        foreach (var c in geography.Cells)
            cellEcef[c.CellId] = GeodeticToEcef(c.LatDeg, c.LonDeg, 0.0);

        double maxEpfd = double.NegativeInfinity;
        long quiet = 0;

        for (long k = 0; k < steps; k++)
        {
            double t = k * timeStepSec;
            var step = scheduler.Step(t);

            double linear = 0.0;
            foreach (var group in step.Links.GroupBy(l => l.SatelliteNumber))
            {
                var satState = constellation.StateAt(group.Key - 1, t, simDur);
                var satPos = satState.PositionEcefKm;

                foreach (var link in group)
                {
                    var esPos = cellEcef[link.CellId];
                    if (ElevationAngleDeg(gso, esPos) <= 0.0) continue;   // GSO below the ES horizon

                    var toSat = (satPos - esPos).Normalized();
                    var toGso = (gso - esPos).Normalized();
                    double phiDeg = Math.Acos(Math.Clamp(Vec3.Dot(toSat, toGso), -1.0, 1.0))
                                  * 180.0 / Math.PI;
                    double power = es.PowerDbw;
                    if (es.PowerControlRefElevDeg is double refElev)
                    {
                        double dRefKm = SlantRangeKm(satState.AltitudeKm, refElev);
                        double dLinkKm = (satPos - esPos).Length;
                        power -= Math.Max(0.0, 20.0 * Math.Log10(dRefKm / dLinkKm));
                    }
                    double eirp = power + es.Antenna.GetAntGain(phiDeg, 0.0);

                    double dM = (gso - esPos).Length * 1000.0;
                    var toEs = (esPos - gso).Normalized();
                    double psiDeg = Math.Acos(Math.Clamp(Vec3.Dot(boresightDir, toEs), -1.0, 1.0))
                                  * 180.0 / Math.PI;

                    linear += Math.Pow(10.0,
                        (eirp - 10.0 * Math.Log10(4.0 * Math.PI * dM * dM)
                         + victim.RelativeGainDb(psiDeg)) / 10.0);
                }
            }

            if (linear > 0.0)
            {
                double epfd = 10.0 * Math.Log10(linear);
                acc.AccumulateSample(epfd, 1);
                if (epfd > maxEpfd) maxEpfd = epfd;
            }
            else
            {
                acc.AccumulateSample(double.NegativeInfinity, 1);
                quiet++;
            }
        }

        return new EpfdUpResult
        {
            Accumulator = acc,
            Steps = steps,
            MaxEpfdDb = maxEpfd,
            QuietSteps = quiet,
        };
    }

    /// <summary>Slant range (km) to a satellite at altitude h seen at elevation eps (spherical Earth).</summary>
    public static double SlantRangeKm(double altitudeKm, double elevDeg)
    {
        double r = EarthRadiusKm + altitudeKm;
        double eps = elevDeg * Math.PI / 180.0;
        double cosE = Math.Cos(eps);
        return Math.Sqrt(r * r - EarthRadiusKm * EarthRadiusKm * cosE * cosE)
             - EarthRadiusKm * Math.Sin(eps);
    }
}
