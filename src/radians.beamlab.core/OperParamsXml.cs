using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace radians.beamlab;

/// <summary>One min_exclude array: exclusion angle by latitude for one orbit (0 = all orbits).</summary>
public sealed class MinExcludeByOrbit
{
    /// <summary>orb_id this array applies to; the all-orbits marker is an explicit 0.</summary>
    public int OrbId { get; init; }
    /// <summary>(latitude deg, exclusion zone angle alpha0 deg), any order; written ascending by latitude.</summary>
    public List<(double LatDeg, double AlphaDeg)> ByLat { get; } = new();
}

/// <summary>One min_elev array: minimum elevation by azimuth, for one latitude.</summary>
public sealed class MinElevByLat
{
    public double LatDeg { get; init; }
    /// <summary>(azimuth deg 0..360, minimum elevation deg), written ascending by azimuth.</summary>
    public List<(double AzDeg, double ElevDeg)> ByAz { get; } = new();
}

/// <summary>
/// A non-GSO operating-parameter set (S.1503-4 Part B "non_gso_operating_parameters",
/// EPS Sec. 6.7.2) -- the R mask: the declared operating constraints the
/// examination enforces alongside the pfd/e.i.r.p. masks.
///
/// Quantities that exist both as a header attribute and as a per-latitude
/// array (max_co_freq, min_duration, elev_angle/min_elev) follow EPS
/// Sec. 6.7.2.2: the array prevails inside the latitudes it covers and the
/// header applies outside them. Leave the header null and/or the array empty
/// to produce header-only, array-only or both-with-different-values sets.
/// </summary>
public sealed class OperatingParamsSet
{
    public string SatName { get; set; } = "NGSO-SAT";
    public int NtcId { get; set; }
    public int ParamId { get; set; } = 1;
    public double LowFreqMhz { get; set; }
    public double HighFreqMhz { get; set; }

    // Earth-station population (typical ES). For specific earth stations set
    // both to null: es_density / es_distance are then not used (Part B) and
    // the attributes are omitted.
    public double? EsDensityPerKm2 { get; set; }
    public double? EsDistanceKm { get; set; }
    public double EsLatMinDeg { get; set; } = -90.0;
    public double EsLatMaxDeg { get; set; } = +90.0;

    /// <summary>Minimum angle at the satellite between lines to two active ES (deg); null = not provided (0 assumed).</summary>
    public double? MinAngleAtSatDeg { get; set; }
    /// <summary>
    /// Minimum angle at the ES between lines to two active satellites (deg).
    /// Not applicable wherever min_duration is non-zero -- the writer rejects
    /// the combination.
    /// </summary>
    public double? MinAngleAtEsDeg { get; set; }

    /// <summary>Header max_co_freq (EPS 6.7.2.2 duality with the array below).</summary>
    public int? MaxCoFreqHeader { get; set; }
    /// <summary>Header max_co_freq_sat; absent means no cap (writer omits when null).</summary>
    public int? MaxCoFreqSat { get; set; }
    /// <summary>
    /// Header min_duration in seconds. For the classic algorithm leave header
    /// null and the array empty -- min_duration is then omitted entirely
    /// (never written as 0; the writer rejects explicit zeros).
    /// </summary>
    public int? MinDurationSecHeader { get; set; }
    /// <summary>Header elev_angle (deg) -- the header form of min_elev.</summary>
    public double? ElevAngleHeaderDeg { get; set; }

    public List<MinExcludeByOrbit> MinExclude { get; } = new();
    public List<(double LatDeg, int Value)> MaxCoFreqByLat { get; } = new();
    public List<(double LatDeg, int Seconds)> MinDurationByLat { get; } = new();
    public List<MinElevByLat> MinElev { get; } = new();
}

/// <summary>
/// Writer for the operating-parameter XML, matching the S.1503-4 Part B
/// example and the reference worked examples (epfd-reference Cases/S.1503-4
/// Mask_param_id_*_OP_*.xml) byte-conventions: attribute order, sign-explicit
/// ES latitudes, plain-decimal numbers, two-space indent, declaration without
/// an encoding attribute.
/// </summary>
public static class OperParamsXmlWriter
{
    public static void Write(string path, OperatingParamsSet p)
    {
        Validate(p);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = true,
            NewLineChars = "\n",
        };
        using var stream = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(false));
        // The examples carry a declaration without an encoding attribute;
        // XmlWriter cannot produce that form, so write it directly.
        stream.Write("<?xml version=\"1.0\"?>\n");
        using var xw = XmlWriter.Create(stream, settings);

        xw.WriteStartElement("satellite_system");
        xw.WriteAttributeString("sat_name", p.SatName);
        xw.WriteAttributeString("ntc_id", p.NtcId.ToString(CultureInfo.InvariantCulture));

        xw.WriteStartElement("non_gso_operating_parameters");
        // Header attribute order mirrors the worked examples.
        xw.WriteAttributeString("es_lat_max", Signed(p.EsLatMaxDeg));
        xw.WriteAttributeString("es_lat_min", Signed(p.EsLatMinDeg));
        if (p.EsDistanceKm is double dist) xw.WriteAttributeString("es_distance", Num(dist));
        if (p.EsDensityPerKm2 is double dens) xw.WriteAttributeString("es_density", Num(dens));
        xw.WriteAttributeString("c_name", "orb_id");
        xw.WriteAttributeString("b_name", "azimuth");
        xw.WriteAttributeString("a_name", "latitude");
        xw.WriteAttributeString("high_freq_mhz", Num(p.HighFreqMhz));
        xw.WriteAttributeString("low_freq_mhz", Num(p.LowFreqMhz));
        xw.WriteAttributeString("param_id", p.ParamId.ToString(CultureInfo.InvariantCulture));
        if (p.MinAngleAtSatDeg is double mas) xw.WriteAttributeString("min_angle_at_sat", Num(mas));
        if (p.MinAngleAtEsDeg is double mae) xw.WriteAttributeString("min_angle_at_es", Num(mae));
        if (p.MaxCoFreqHeader is int mcf) xw.WriteAttributeString("max_co_freq", mcf.ToString(CultureInfo.InvariantCulture));
        if (p.MaxCoFreqSat is int mcfs) xw.WriteAttributeString("max_co_freq_sat", mcfs.ToString(CultureInfo.InvariantCulture));
        if (p.MinDurationSecHeader is int mdh) xw.WriteAttributeString("min_duration", mdh.ToString(CultureInfo.InvariantCulture));
        if (p.ElevAngleHeaderDeg is double eah) xw.WriteAttributeString("elev_angle", Num(eah));

        foreach (var ex in p.MinExclude.OrderBy(e => e.OrbId))
        {
            xw.WriteStartElement("min_exclude");
            xw.WriteAttributeString("c", ex.OrbId.ToString(CultureInfo.InvariantCulture));
            foreach (var (lat, alpha) in ex.ByLat.OrderBy(v => v.LatDeg))
            {
                xw.WriteStartElement("exclusion_zone_angle");
                xw.WriteAttributeString("a", Num(lat));
                xw.WriteString(Num(alpha));
                xw.WriteEndElement();
            }
            xw.WriteEndElement();
        }

        foreach (var (lat, value) in p.MaxCoFreqByLat.OrderBy(v => v.LatDeg))
        {
            xw.WriteStartElement("max_co_freq");
            xw.WriteAttributeString("a", Num(lat));
            xw.WriteString(value.ToString(CultureInfo.InvariantCulture));
            xw.WriteEndElement();
        }

        foreach (var (lat, seconds) in p.MinDurationByLat.OrderBy(v => v.LatDeg))
        {
            xw.WriteStartElement("min_duration");
            xw.WriteAttributeString("a", Num(lat));
            xw.WriteString(seconds.ToString(CultureInfo.InvariantCulture));
            xw.WriteEndElement();
        }

        foreach (var me in p.MinElev.OrderBy(e => e.LatDeg))
        {
            xw.WriteStartElement("min_elev");
            xw.WriteAttributeString("a", Num(me.LatDeg));
            foreach (var (az, elev) in me.ByAz.OrderBy(v => v.AzDeg))
            {
                xw.WriteStartElement("elev_angle");
                xw.WriteAttributeString("b", Num(az));
                xw.WriteString(Num(elev));
                xw.WriteEndElement();
            }
            xw.WriteEndElement();
        }

        xw.WriteEndElement();   // non_gso_operating_parameters
        xw.WriteEndElement();   // satellite_system
    }

    /// <summary>The encoding rules that are easy to get wrong, enforced.</summary>
    private static void Validate(OperatingParamsSet p)
    {
        bool anyDuration = p.MinDurationSecHeader is not null || p.MinDurationByLat.Count > 0;

        // Classic algorithm: omit min_duration entirely -- never write 0.
        if (p.MinDurationSecHeader is 0 || p.MinDurationByLat.Any(v => v.Seconds == 0))
            throw new ArgumentException(
                "min_duration 0 must not be written: omit min_duration entirely for the classic algorithm.");

        // min_angle_at_es is not applicable wherever min_duration is non-zero.
        if (anyDuration && p.MinAngleAtEsDeg is not null)
            throw new ArgumentException(
                "min_angle_at_es is not applicable when min_duration is declared (non-zero).");

        // Typical-ES population needs both density and distance; specific ES neither.
        if ((p.EsDensityPerKm2 is null) != (p.EsDistanceKm is null))
            throw new ArgumentException(
                "es_density and es_distance must be declared together (typical ES) or both omitted (specific ES).");

        foreach (var me in p.MinElev)
            foreach (var (az, _) in me.ByAz)
                if (az < 0.0 || az > 360.0)
                    throw new ArgumentException($"min_elev azimuth {az} outside 0..360.");
        foreach (var ex in p.MinExclude)
            if (ex.OrbId < 0)
                throw new ArgumentException("min_exclude orb_id must be >= 0 (0 = all orbits).");
    }

    /// <summary>Sign-explicit integer-degree latitude bound, as the examples write ("+90" / "-90").</summary>
    private static string Signed(double v)
    {
        string s = Num(Math.Abs(v));
        return v >= 0 ? "+" + s : "-" + s;
    }

    /// <summary>Plain decimal, no exponent, as the examples (es_density="0.00000028182").</summary>
    private static string Num(double v) => v.ToString("0.############", CultureInfo.InvariantCulture);
}
