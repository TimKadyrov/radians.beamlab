using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>min_exclude node: exclusion angle at one latitude for one orbit (0 = all orbits).</summary>
public sealed record OpMinExcludeRow(int OrbId, double LatDeg, double AlphaDeg);

/// <summary>One (latitude, integer value) node of max_co_freq or min_duration.</summary>
public sealed record OpLatIntRow(double LatDeg, int Value);

/// <summary>min_elev node: minimum elevation at one (latitude, azimuth).</summary>
public sealed record OpMinElevRow(double LatDeg, double AzDeg, double ElevDeg);

/// <summary>
/// The intermediate operating-parameters file (*.opparams.json): one R set
/// as flat rows, so the designer round-trips exactly what the XML writer
/// consumes. Schema 1.
/// </summary>
public sealed record OpParamsData(
    int SchemaVersion, string SatName, int NtcId, int ParamId,
    double LowFreqMhz, double HighFreqMhz,
    double? EsDensityPerKm2, double? EsDistanceKm,
    double EsLatMinDeg, double EsLatMaxDeg,
    double? MinAngleAtSatDeg, double? MinAngleAtEsDeg,
    int? MaxCoFreqHeader, int? MaxCoFreqSat, int? MinDurationSecHeader, double? ElevAngleHeaderDeg,
    IReadOnlyList<OpMinExcludeRow> MinExclude,
    IReadOnlyList<OpLatIntRow> MaxCoFreqByLat,
    IReadOnlyList<OpLatIntRow> MinDurationByLat,
    IReadOnlyList<OpMinElevRow> MinElev);

public static class OpParamsFileCodec
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Save(OpParamsData d) => JsonSerializer.Serialize(d, Options);

    public static OpParamsData Load(string json)
        => JsonSerializer.Deserialize<OpParamsData>(json)
           ?? throw new InvalidOperationException("empty operating-parameters file");

    /// <summary>Flattens a set into its file form.</summary>
    public static OpParamsData FromSet(OperatingParamsSet p)
        => new(1, p.SatName, p.NtcId, p.ParamId, p.LowFreqMhz, p.HighFreqMhz,
            p.EsDensityPerKm2, p.EsDistanceKm, p.EsLatMinDeg, p.EsLatMaxDeg,
            p.MinAngleAtSatDeg, p.MinAngleAtEsDeg,
            p.MaxCoFreqHeader, p.MaxCoFreqSat, p.MinDurationSecHeader, p.ElevAngleHeaderDeg,
            p.MinExclude.SelectMany(e => e.ByLat.Select(v => new OpMinExcludeRow(e.OrbId, v.LatDeg, v.AlphaDeg))).ToList(),
            p.MaxCoFreqByLat.Select(v => new OpLatIntRow(v.LatDeg, v.Value)).ToList(),
            p.MinDurationByLat.Select(v => new OpLatIntRow(v.LatDeg, v.Seconds)).ToList(),
            p.MinElev.SelectMany(e => e.ByAz.Select(v => new OpMinElevRow(e.LatDeg, v.AzDeg, v.ElevDeg))).ToList());

    /// <summary>Regroups the file form into the writer's set.</summary>
    public static OperatingParamsSet ToSet(OpParamsData d)
    {
        var p = new OperatingParamsSet
        {
            SatName = d.SatName, NtcId = d.NtcId, ParamId = d.ParamId,
            LowFreqMhz = d.LowFreqMhz, HighFreqMhz = d.HighFreqMhz,
            EsDensityPerKm2 = d.EsDensityPerKm2, EsDistanceKm = d.EsDistanceKm,
            EsLatMinDeg = d.EsLatMinDeg, EsLatMaxDeg = d.EsLatMaxDeg,
            MinAngleAtSatDeg = d.MinAngleAtSatDeg, MinAngleAtEsDeg = d.MinAngleAtEsDeg,
            MaxCoFreqHeader = d.MaxCoFreqHeader, MaxCoFreqSat = d.MaxCoFreqSat,
            MinDurationSecHeader = d.MinDurationSecHeader, ElevAngleHeaderDeg = d.ElevAngleHeaderDeg,
        };
        foreach (var g in d.MinExclude.GroupBy(r => r.OrbId).OrderBy(g => g.Key))
        {
            var ex = new MinExcludeByOrbit { OrbId = g.Key };
            foreach (var r in g) ex.ByLat.Add((r.LatDeg, r.AlphaDeg));
            p.MinExclude.Add(ex);
        }
        foreach (var r in d.MaxCoFreqByLat) p.MaxCoFreqByLat.Add((r.LatDeg, r.Value));
        foreach (var r in d.MinDurationByLat) p.MinDurationByLat.Add((r.LatDeg, r.Value));
        foreach (var g in d.MinElev.GroupBy(r => r.LatDeg).OrderBy(g => g.Key))
        {
            var me = new MinElevByLat { LatDeg = g.Key };
            foreach (var r in g) me.ByAz.Add((r.AzDeg, r.ElevDeg));
            p.MinElev.Add(me);
        }
        return p;
    }
}
