using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace radians.beamlab;

/// <summary>One latitude block of an e.i.r.p. mask: (angle deg, eirp dB(W/BWref)) rows.</summary>
public sealed class EirpLatBlock
{
    public double LatDeg { get; init; }
    /// <summary>(separation/off-axis angle deg, eirp); written in given order after an ascending sort.</summary>
    public List<(double AngleDeg, double EirpDbw)> ByAngle { get; } = new();
}

/// <summary>
/// An earth-station (up) or space-station (inter-satellite) e.i.r.p. mask
/// table in the T form -- eirp[latitude][angle] -- which is the form the
/// examination parses (maskdata EIRP_ES / EIRP_SS: nearest latitude, linear
/// interpolation in angle clamped at the edges, Sec. D5.2.7).
/// </summary>
public sealed class EirpMaskTable
{
    public string SatName { get; set; } = "NGSO-SAT";
    public int NtcId { get; set; }
    public int MaskId { get; set; } = 1;
    public double LowFreqMhz { get; set; }
    public double HighFreqMhz { get; set; }
    /// <summary>Reference bandwidth (kHz); omitted when null (the reader defaults to 40).</summary>
    public double? RefBwKHz { get; set; }

    // ES-only header fields (ignored by the SS writer):
    /// <summary>-1 = typical (density-distributed) earth stations.</summary>
    public int EsId { get; set; } = -1;
    /// <summary>Minimum operating elevation (deg) -- the worked masks carry it in the header.</summary>
    public double? MinElevDeg { get; set; }

    public List<EirpLatBlock> Blocks { get; } = new();
}

/// <summary>
/// Writer for eirp_mask_es / eirp_mask_ss XML, matching the worked masks
/// extracted from the reference Masks databases byte-conventions: UTF-8
/// declaration (upper-case, with BOM), attribute order, d_name
/// "separation angle", one-decimal angle attributes. The Rec's illustrative
/// examples differ (b_name "offaxis angle"); the shipped files and the
/// examination's parser agree on this form, so the files win.
/// </summary>
public static class EirpMaskXmlWriter
{
    /// <summary>Write an earth-station (up) mask. Returns Rec "should"-rule warnings (monotonicity).</summary>
    public static IReadOnlyList<string> WriteEs(string path, EirpMaskTable t) => Write(path, t, es: true);

    /// <summary>Write a space-station (inter-satellite) mask. Returns monotonicity warnings.</summary>
    public static IReadOnlyList<string> WriteSs(string path, EirpMaskTable t) => Write(path, t, es: false);

    private static IReadOnlyList<string> Write(string path, EirpMaskTable t, bool es)
    {
        if (t.Blocks.Count == 0) throw new ArgumentException("eirp mask has no latitude blocks");
        foreach (var b in t.Blocks)
            if (b.ByAngle.Count == 0)
                throw new ArgumentException($"latitude block {b.LatDeg} has no angle rows");

        // Rec C4.3/C4.4: "The e.i.r.p. mask should be monotonically
        // decreasing" -- a should-rule the worked masks themselves bend, so
        // violations are reported, not fatal.
        var warnings = new List<string>();
        foreach (var b in t.Blocks)
        {
            var rows = b.ByAngle.OrderBy(r => r.AngleDeg).ToList();
            for (int i = 1; i < rows.Count; i++)
                if (rows[i].EirpDbw > rows[i - 1].EirpDbw + 1e-12)
                    warnings.Add($"lat {Num(b.LatDeg)}: eirp rises {Num(rows[i - 1].EirpDbw)} -> {Num(rows[i].EirpDbw)} at angle {Num(rows[i].AngleDeg)}");
        }

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = true,
            NewLineChars = "\n",
        };
        using var stream = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        stream.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        using var xw = XmlWriter.Create(stream, settings);

        xw.WriteStartElement("satellite_system");
        xw.WriteAttributeString("ntc_id", t.NtcId.ToString(CultureInfo.InvariantCulture));
        xw.WriteAttributeString("sat_name", t.SatName);

        xw.WriteStartElement(es ? "eirp_mask_es" : "eirp_mask_ss");
        xw.WriteAttributeString("mask_id", t.MaskId.ToString(CultureInfo.InvariantCulture));
        xw.WriteAttributeString("low_freq_mhz", Num(t.LowFreqMhz));
        xw.WriteAttributeString("high_freq_mhz", Num(t.HighFreqMhz));
        if (t.RefBwKHz is double bw) xw.WriteAttributeString("refbw_khz", Num(bw));
        if (es && t.MinElevDeg is double me) xw.WriteAttributeString("min_elev", Num(me));
        xw.WriteAttributeString("a_name", "latitude");
        xw.WriteAttributeString("d_name", "separation angle");
        if (es) xw.WriteAttributeString("ES_ID", t.EsId.ToString(CultureInfo.InvariantCulture));

        foreach (var b in t.Blocks.OrderBy(x => x.LatDeg))
        {
            xw.WriteStartElement("by_a");
            xw.WriteAttributeString("a", Num(b.LatDeg));
            foreach (var (angle, eirp) in b.ByAngle.OrderBy(r => r.AngleDeg))
            {
                xw.WriteStartElement("eirp");
                xw.WriteAttributeString("d", Angle(angle));
                xw.WriteString(Num(eirp));
                xw.WriteEndElement();
            }
            xw.WriteEndElement();   // by_a
        }

        xw.WriteEndElement();   // eirp_mask_es / _ss
        xw.WriteEndElement();   // satellite_system
        return warnings;
    }

    /// <summary>Angles carry at least one decimal in the worked masks ("55.0").</summary>
    private static string Angle(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
