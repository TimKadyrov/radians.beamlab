using System;
using System.Collections.Generic;
using System.Linq;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Derives a declared operating-parameter set from the SIMULATED system:
/// fly the real constellation and operation model, measure what every
/// granted link actually does, and envelope the measurements the way the
/// pfd/e.i.r.p. masks envelope the payload -- declared minima floor the
/// observed minima (to 0.1 deg), declared maxima carry the observed
/// maxima. Quantities never observed stay undeclared (null / empty), and
/// an exclusion floor is declared only where an exclusion actually shaped
/// operations -- an unconstrained near-zero floor is geometry, not a
/// promise.
/// </summary>
public static class OpParamsDeriver
{
    public sealed record Result(OperatingParamsSet Set, long Steps, long LinkSamples);

    public static Result Derive(Constellation con, ServiceGeography geo,
        OperatingParamsSet enforced, PfdMaskViewModel scene,
        double simDurSec, double stepSec, double latBandDeg,
        string satName, int ntcId, int paramId, double lowFreqMhz, double highFreqMhz,
        SelectionPolicy policy = SelectionPolicy.HighestElevation,
        double? coverageRadiusKm = null, double illuminationDuty = 1.0)
    {
        var scheduler = new Scheduler(con, geo, enforced,
            new ScenePointing(scene, illuminationDuty), simDurSec, coverageRadiusKm, policy);
        long steps = Math.Max(1, (long)(simDurSec / stepSec));

        var minElevByBand = new Dictionary<int, double>();
        var minAlphaByBand = new Dictionary<int, double>();
        var maxCoFreqByBand = new Dictionary<int, int>();
        int maxPerSat = 0;
        double minAngleEs = double.PositiveInfinity;   // between co-serving satellites, at the cell
        double minAngleSat = double.PositiveInfinity;  // between served cells, at the satellite
        double servedLatMin = double.PositiveInfinity, servedLatMax = double.NegativeInfinity;
        long samples = 0;

        int nSats = con.SatelliteCount;
        var cellById = geo.Cells.ToDictionary(c => c.CellId);
        var satPos = new Dictionary<int, Vec3>();
        for (long k = 0; k < steps; k++)
        {
            double t = k * stepSec;
            satPos.Clear();
            for (int i = 0; i < nSats; i++)
            {
                var st = con.StateAt(i, t, simDurSec);
                satPos[st.SatelliteNumber] = st.PositionEcefKm;
            }
            var step = scheduler.Step(t);
            if (step.Links.Count == 0) continue;

            foreach (var byCell in step.Links.GroupBy(l => l.CellId))
            {
                var cell = cellById[byCell.Key];
                int band = Band(cell.LatDeg, latBandDeg);
                var es = CellEcef(cell.LatDeg, cell.LonDeg);
                var links = byCell.ToList();

                foreach (var l in links)
                {
                    samples++;
                    Floor(minElevByBand, band, l.ElevationDeg);
                    Floor(minAlphaByBand, band, Math.Abs(l.AlphaDeg));
                    servedLatMin = Math.Min(servedLatMin, cell.LatDeg);
                    servedLatMax = Math.Max(servedLatMax, cell.LatDeg);
                }
                maxCoFreqByBand[band] = Math.Max(
                    maxCoFreqByBand.TryGetValue(band, out int c0) ? c0 : 0, links.Count);

                for (int a = 0; a < links.Count; a++)
                    for (int b = a + 1; b < links.Count; b++)
                        if (satPos.TryGetValue(links[a].SatelliteNumber, out var pa)
                            && satPos.TryGetValue(links[b].SatelliteNumber, out var pb))
                            minAngleEs = Math.Min(minAngleEs, AngleDeg(pa - es, pb - es));
            }

            foreach (var bySat in step.Links.GroupBy(l => l.SatelliteNumber))
            {
                maxPerSat = Math.Max(maxPerSat, bySat.Count());
                var cells = bySat.Select(l => l.CellId).Distinct().ToList();
                if (cells.Count < 2 || !satPos.TryGetValue(bySat.Key, out var ps)) continue;
                for (int a = 0; a < cells.Count; a++)
                    for (int b = a + 1; b < cells.Count; b++)
                    {
                        var ca = cellById[cells[a]];
                        var cb = cellById[cells[b]];
                        minAngleSat = Math.Min(minAngleSat, AngleDeg(
                            CellEcef(ca.LatDeg, ca.LonDeg) - ps,
                            CellEcef(cb.LatDeg, cb.LonDeg) - ps));
                    }
            }
        }

        var p = new OperatingParamsSet
        {
            SatName = satName, NtcId = ntcId, ParamId = paramId,
            LowFreqMhz = lowFreqMhz, HighFreqMhz = highFreqMhz,
            // Typical-ES population straight from the service geography.
            EsDistanceKm = geo.CellPitchKm,
            EsDensityPerKm2 = 1.0 / (geo.CellPitchKm * geo.CellPitchKm),
            EsLatMinDeg = samples > 0 ? Math.Floor(servedLatMin) : -90.0,
            EsLatMaxDeg = samples > 0 ? Math.Ceiling(servedLatMax) : 90.0,
            MaxCoFreqSat = maxPerSat > 0 ? maxPerSat : null,
            MinAngleAtSatDeg = double.IsFinite(minAngleSat) ? FloorTenth(minAngleSat) : null,
            MinAngleAtEsDeg = double.IsFinite(minAngleEs) ? FloorTenth(minAngleEs) : null,
            // Header and array are mutually exclusive per quantity (design
            // brief Sec. 3.8, ruling of 2026-09-07): min_elev and max_co_freq
            // are declared as the per-latitude tables below and carry no
            // header, and the nearest-row read is total -- the outermost row
            // governs every latitude beyond the table. A set carrying both
            // forms is an invalid filing. MIN_DURATION is not declared at
            // all: declaring it would switch the examination to the
            // track-duration algorithm.
        };
        foreach (var (band, elev) in minElevByBand.OrderBy(kv => kv.Key))
        {
            var me = new MinElevByLat { LatDeg = BandLat(band, latBandDeg) };
            double v = FloorTenth(elev);
            me.ByAz.Add((0.0, v));
            me.ByAz.Add((360.0, v));
            p.MinElev.Add(me);
        }
        // MIN_EXCLUDE is the one array the Recommendation reads by LINEAR
        // INTERPOLATION between latitude rows; the others are read at the
        // nearest row. Labelling a band minimum at its band centre is right
        // for a nearest read and WRONG for an interpolated one -- see
        // InterpolationSafeFloor. The sliding minimum runs BEFORE the
        // near-zero filter, so a band where no exclusion shaped operations
        // pulls its neighbours down with it rather than being punched out of
        // the list for interpolation to span.
        var bandRows = minAlphaByBand.OrderBy(kv => kv.Key)
            .Select(kv => (LatDeg: BandLat(kv.Key, latBandDeg), Value: kv.Value)).ToList();
        var ex = new MinExcludeByOrbit { OrbId = 0 };
        foreach (var (lat, alpha) in InterpolationSafeFloor(bandRows))
            if (alpha > 0.05) ex.ByLat.Add((lat, FloorTenth(alpha)));
        if (ex.ByLat.Count > 0) p.MinExclude.Add(ex);
        foreach (var (band, cnt) in maxCoFreqByBand.OrderBy(kv => kv.Key))
            p.MaxCoFreqByLat.Add((BandLat(band, latBandDeg), cnt));

        return new Result(p, steps, samples);
    }

    /// <summary>
    /// Make a measured FLOOR safe to read by linear interpolation.
    ///
    /// Each input row carries the minimum observed over the latitude BAND it
    /// represents, labelled at the band centre. Read at the nearest row that
    /// is exact. Read by INTERPOLATION it is not: between two rows the value
    /// rises toward the larger of them, so at latitudes inside the lower
    /// band the declared floor can exceed what the system actually did --
    /// declaring an exclusion the operation does not honour, and gating
    /// satellites out of the examination that the truth lets serve.
    ///
    /// Interpolating between rows i and i+1 never exceeds max(v_i, v_i+1),
    /// so it is sufficient that both are at most the smallest band minimum
    /// either of them interpolates with -- a sliding minimum over each row
    /// and its list neighbours. Conservative, and exact where the floor is
    /// flat.
    /// </summary>
    public static List<(double LatDeg, double Value)> InterpolationSafeFloor(
        IReadOnlyList<(double LatDeg, double Value)> bandMinima)
    {
        var outRows = new List<(double, double)>(bandMinima.Count);
        for (int i = 0; i < bandMinima.Count; i++)
        {
            double v = bandMinima[i].Value;
            if (i > 0) v = Math.Min(v, bandMinima[i - 1].Value);
            if (i + 1 < bandMinima.Count) v = Math.Min(v, bandMinima[i + 1].Value);
            outRows.Add((bandMinima[i].LatDeg, v));
        }
        return outRows;
    }

    private static int Band(double latDeg, double bandDeg) => (int)Math.Floor(latDeg / bandDeg);
    private static double BandLat(int band, double bandDeg) => band * bandDeg + bandDeg / 2.0;
    private static double FloorTenth(double v) => Math.Floor(v * 10.0) / 10.0;

    private static void Floor(Dictionary<int, double> d, int band, double v)
    {
        if (!d.TryGetValue(band, out double c) || v < c) d[band] = v;
    }

    private static Vec3 CellEcef(double latDeg, double lonDeg)
    {
        double lat = latDeg * Math.PI / 180.0, lon = lonDeg * Math.PI / 180.0;
        double r = Radians.Orbits.Core.Utilities.OrbitalConstants.EarthRadiusKm;
        return new Vec3(r * Math.Cos(lat) * Math.Cos(lon),
                        r * Math.Cos(lat) * Math.Sin(lon),
                        r * Math.Sin(lat));
    }

    private static double AngleDeg(Vec3 a, Vec3 b)
        => Math.Acos(Math.Clamp(Vec3.Dot(a.Normalized(), b.Normalized()), -1.0, 1.0))
           * 180.0 / Math.PI;
}
