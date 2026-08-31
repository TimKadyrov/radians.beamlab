using System;
using System.Collections.Generic;
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
/// Not modelled (not declarable in this producer yet): the dual time
/// step (Sub-steps 5.1/6.x -- callers pick one step size) and
/// MIN_OPERATING_HEIGHT (the R set here does not carry it). There is no
/// epfd(is) byproduct: an intersatellite run needs the e.i.r.p.(theta)
/// mask, not the pfd mask.
/// </summary>
public static class EpfdDownMask
{
    public static EpfdDownResult Run(Constellation constellation, IMaskPfdRead mask,
        OperatingParamsSet declared, EpfdDownVictim victim, double timeStepSec, long steps,
        List<LimitPoint> limits, double? simulationDurationSec = null)
    {
        double simDur = simulationDurationSec ?? timeStepSec * steps;
        var acc = new EpfdAccumulator(limits);

        var es = GeodeticToEcef(victim.EsLatDeg, victim.EsLonDeg, 0.0);
        double gsoLonRad = victim.GsoLonDeg * Math.PI / 180.0;
        var gso = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(gsoLonRad),
                           GsoGeometry.GsoRadiusKm * Math.Sin(gsoLonRad), 0.0);
        var dirEsGso = (gso - es).Normalized();
        double gmax = victim.Antenna.MaxGain;
        // North/east at the ES for Azimuth_NGSO -- the scheduler's convention.
        var (esN, esE, _) = SatNedBasis(victim.EsLatDeg, victim.EsLonDeg);

        // SRS orb_id numbering, exactly as the Scheduler assigns it.
        int n = constellation.SatelliteCount;
        var orbIds = new Dictionary<(int shell, int plane), int>();
        int orb = 0;
        for (int i = 0; i < n; i++)
        {
            var st = constellation.StateAt(i, 0.0, simDur);
            if (!orbIds.ContainsKey((st.ShellIndex, st.PlaneIndex)))
                orbIds[(st.ShellIndex, st.PlaneIndex)] = ++orb;
        }

        double maxEpfd = double.NegativeInfinity;
        long quiet = 0;
        var entries = new List<(double EpfdDb, bool Operating, bool MainBeam, Vec3 ToSat)>();

        for (long k = 0; k < steps; k++)
        {
            double t = k * timeStepSec;
            entries.Clear();

            for (int i = 0; i < n; i++)
            {
                var state = constellation.StateAt(i, t, simDur);
                var pos = state.PositionEcefKm;
                double elev = ElevationAngleDeg(pos, es);
                if (elev <= 0.0) continue;                    // Step 11 visibility

                double pfd = mask.PfdDb(state, pos, es);      // Steps 13-14

                var toSat = (pos - es).Normalized();
                double phiDeg = Math.Acos(Math.Clamp(Vec3.Dot(dirEsGso, toSat), -1.0, 1.0)) * 180.0 / Math.PI;
                double grx = victim.Antenna.GetAntGain(phiDeg, 0.0);
                double epfdI = pfd + grx - gmax;              // Steps 15-17

                // Step 18 classification against the declared set.
                double azDeg = Math.Atan2(Vec3.Dot(toSat, esE), Vec3.Dot(toSat, esN)) * 180.0 / Math.PI;
                if (azDeg < 0) azDeg += 360.0;
                int orbId = orbIds[(state.ShellIndex, state.PlaneIndex)];
                double alpha0 = DeclaredConstraints.ExclusionAlphaDeg(declared, victim.EsLatDeg, orbId);
                bool operating =
                    GsoGeometry.AlphaMinAbsDeg(es, pos) >= alpha0
                    && elev >= DeclaredConstraints.MinElevDeg(declared, victim.EsLatDeg, azDeg);
                bool mainBeam = grx > Math.Min(gmax - 30.0, victim.Antenna.GetAntGain(alpha0, 0.0));
                if (operating || mainBeam)
                    entries.Add((epfdI, operating, mainBeam, toSat));
            }

            // Steps 19-21: up to MAX_CO_FREQ[lat] operating satellites by
            // highest epfd_i, each pick pruning MIN_ANGLE_AT_ES violators.
            int cap = DeclaredConstraints.MaxCoFreq(declared, victim.EsLatDeg);
            double minSepDeg = DeclaredConstraints.MinAngleAtEsDeg(declared);
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

        return new EpfdDownResult
        {
            Accumulator = acc,
            Steps = steps,
            MaxEpfdDb = maxEpfd,
            QuietSteps = quiet,
        };
    }
}
