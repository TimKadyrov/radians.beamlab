using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>One b-row of a mask latitude block, with its own c grid.</summary>
public sealed class MaskRow
{
    /// <summary>b value of this row (alpha or azimuth, deg).</summary>
    public double B { get; init; }
    /// <summary>c nodes, ascending (deltaLongitude or elevation, deg).</summary>
    public double[] CNodes { get; init; } = Array.Empty<double>();
    /// <summary>
    /// Raw dB values aligned with <see cref="CNodes"/> -- the unreachable
    /// floor stays numeric so S.1503-4 Sec. D5.1.5
    /// interpolation treats it exactly like the reference implementation.
    /// </summary>
    public double[] Values { get; init; } = Array.Empty<double>();
}

/// <summary>One latitude block of an imported S.1503-4 PFD mask.</summary>
public sealed class MaskLatBlock
{
    public double LatDeg { get; init; }
    /// <summary>
    /// Rows ascending by b. Each row carries its own c grid -- real ITU
    /// filings compress plateau rows to few nodes, so blocks are usually
    /// ragged, exactly as the reference maskdata parser models them
    /// (a DeltaElevTable per by_b).
    /// </summary>
    public IReadOnlyList<MaskRow> Rows { get; init; } = Array.Empty<MaskRow>();

    /// <summary>
    /// The spec's null convention: S.1503-4 Sec. C1 -- "it is feasible for a
    /// system not to transmit at certain latitudes: in this case a null value
    /// of -1000 dBW should be used". Only this value is treated as
    /// unreachable by default; anything above it (e.g. a -999 floor some
    /// filings use) is DATA until the user promotes the block minimum to a
    /// cut-off in the viewer.
    /// </summary>
    public const double UnreachableDb = -1000.0;
}

/// <summary>An imported S.1503-4 pfd_mask document (first mask in the file).</summary>
public sealed class LoadedPfdMask
{
    public string SatName { get; init; } = "";
    public int NtcId { get; init; }
    public int MaskId { get; init; }
    public double LowFreqMhz { get; init; }
    public double HighFreqMhz { get; init; }
    public double RefBwKHz { get; init; }
    public MaskPlotKind Kind { get; init; }
    /// <summary>Latitude blocks, ascending by latitude.</summary>
    public IReadOnlyList<MaskLatBlock> Blocks { get; init; } = Array.Empty<MaskLatBlock>();
}

/// <summary>
/// Reader for the S.1503-4 mask XML schema written by <see cref="MaskXmlExport"/>
/// (satellite_system / pfd_mask / by_a / by_b / pfd) -- the same structure the
/// reference maskdata parser consumes. Blocks may be ragged: every by_b row
/// carries its own c node list, as real filings do.
/// </summary>
public static class MaskXmlImport
{
    public static LoadedPfdMask Load(string path)
    {
        var doc = new XmlDocument();
        doc.Load(path);

        var mask = doc.SelectSingleNode("//pfd_mask")
            ?? throw new FormatException("No <pfd_mask> element found in the file.");
        var sys = mask.ParentNode;

        string typeAttr = Attr(mask, "type") ?? "azimuth_elevation";
        var kind = typeAttr.Equals("alpha_deltaLongitude", StringComparison.OrdinalIgnoreCase)
            ? MaskPlotKind.AlphaDeltaLong
            : MaskPlotKind.AzEl;

        var blocks = new List<MaskLatBlock>();
        foreach (XmlNode byA in mask.SelectNodes("by_a")!)
        {
            double a = Num(Attr(byA, "a") ?? "0");
            var rows = new List<MaskRow>();

            foreach (XmlNode byB in byA.SelectNodes("by_b")!)
            {
                var cs = new List<double>();
                var vs = new List<double>();
                foreach (XmlNode p in byB.SelectNodes("pfd")!)
                {
                    cs.Add(Num(Attr(p, "c") ?? "0"));
                    vs.Add(Num(p.InnerText));   // raw, incl. the floor
                }
                if (cs.Count == 0) continue;
                int[] order = Enumerable.Range(0, cs.Count).OrderBy(i => cs[i]).ToArray();
                rows.Add(new MaskRow
                {
                    B = Num(Attr(byB, "b") ?? "0"),
                    CNodes = order.Select(i => cs[i]).ToArray(),
                    Values = order.Select(i => vs[i]).ToArray(),
                });
            }
            if (rows.Count == 0)
                throw new FormatException($"by_a a={a}: empty block.");

            blocks.Add(new MaskLatBlock
            {
                LatDeg = a,
                Rows = rows.OrderBy(r => r.B).ToList(),
            });
        }
        if (blocks.Count == 0) throw new FormatException("The pfd_mask contains no <by_a> latitude blocks.");

        return new LoadedPfdMask
        {
            SatName = (sys is null ? null : Attr(sys, "sat_name")) ?? "",
            NtcId = (int)Num(sys is null ? "0" : Attr(sys, "ntc_id") ?? "0"),
            MaskId = (int)Num(Attr(mask, "mask_id") ?? "0"),
            LowFreqMhz = Num(Attr(mask, "low_freq_mhz") ?? "0"),
            HighFreqMhz = Num(Attr(mask, "high_freq_mhz") ?? "0"),
            RefBwKHz = Num(Attr(mask, "refbw_khz") ?? "0"),
            Kind = kind,
            Blocks = blocks.OrderBy(b => b.LatDeg).ToList(),
        };
    }

    /// <summary>
    /// Attach one latitude block as a <see cref="PfdMaskField"/>'s exact data
    /// source; the display raster is regenerated at plot time.
    /// </summary>
    public static void ApplyBlockToField(LoadedPfdMask mask, MaskLatBlock blk, PfdMaskField field)
        => field.SetMaskSource(mask.Kind, blk);

    private static string? Attr(XmlNode n, string name) => n.Attributes?[name]?.Value;
    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
