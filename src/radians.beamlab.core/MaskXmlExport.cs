using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace radians.beamlab;

/// <summary>Output format(s) for a mask export.</summary>
public enum MaskExportFormat
{
    /// <summary>S.1503-4 PFD-mask XML only.</summary>
    Xml,
    /// <summary>Tabular CSV only (rows = latitude x b, columns = c values).</summary>
    Csv,
    /// <summary>Both XML and CSV (companion files, same base name).</summary>
    Both,
}

/// <summary>Inputs for a S.1503-4 PFD-mask export.</summary>
public sealed class MaskXmlExportOptions
{
    public string SatName = "NGSO-SAT";
    public int NtcId;
    public int MaskId = 1;
    public double LowFreqMhz = 10_700.0;
    public double HighFreqMhz = 12_750.0;
    public double RefBwKHz = 40.0;

    /// <summary>Latitude table bounds (deg) -- already clamped to +/-maxLat(inclination) by the caller.</summary>
    public double LatMinDeg = -60.0;
    public double LatMaxDeg = 60.0;
    public double LatStepDeg = 5.0;

    /// <summary>Output axis steps (deg): b = alpha (alpha/deltaLong) or azimuth (Az/El); c = deltaLongitude or elevation.</summary>
    public double BStepDeg = 2.0;
    public double CStepDeg = 5.0;

    public MaskPlotKind Kind = MaskPlotKind.AlphaDeltaLong;
    public MaskExportFormat Format = MaskExportFormat.Xml;
    public string OutputPath = "";
}

/// <summary>
/// What the mask exporter needs from a PFD computation: position the system
/// at a sub-satellite latitude and recompute, then answer envelope reads off
/// the recomputed field. The WPF app implements this over its scene /
/// mask-field pair; a headless run can implement it over any equivalent
/// compute (WP0 seam -- the writer is format logic, the sampler is physics).
/// </summary>
public interface IPfdMaskSampler
{
    /// <summary>Move the system to the given sub-satellite latitude (deg) and recompute the PFD field.</summary>
    void PrepareLatitude(double latDeg);

    /// <summary>
    /// Maximum PFD (dB(W/m^2) in the reference bandwidth) over the bin
    /// [x-halfW, x+halfW] x [y-halfH, y+halfH] of the prepared field;
    /// NegativeInfinity where the bin is unreachable. Coordinates are the
    /// mask kind's field axes (AzEl: x = azimuth, y = elevation;
    /// AlphaDeltaLong: x = deltaLongitude, y = alpha).
    /// </summary>
    double SampleMaxIn(double xDeg, double yDeg, double halfW, double halfH);
}

/// <summary>
/// Generates a S.1503-4 PFD-mask export. The XML matches the reference
/// <c>maskdata.xml_PFD</c> schema (satellite_system -> pfd_mask ->
/// by_a[latitude] -> by_b[alpha|azimuth] -> pfd[deltaLong|elevation]); the CSV is a flat
/// table with one row per (latitude, b) and one column per c value.
///
/// For each latitude the sampler prepares the system there and PFD is read at
/// each (b, c) output node as the max over the node's bin (envelope
/// semantics) -- unreachable nodes get the -1000 dB(W/m^2) floor used by the
/// reference. The whole sweep runs off the caller's thread.
/// </summary>
public static class MaskXmlExport
{
    /// <summary>
    /// Maximum sub-satellite latitude (deg) reachable at the given orbital
    /// inclination: sin(psi_max) = +/- sin(i), i.e. psi_max = i for prograde
    /// and 180 deg - i for retrograde orbits. Reference: Capderou, "Handbook
    /// of Satellite Orbits: From Kepler to GPS" (Springer, 2014), Sec. 8.2.2
    /// "Maximum Attained Latitude".
    /// </summary>
    public static double MaxLatitudeForInclination(double inclinationDeg)
        => 90.0 - Math.Abs(90.0 - inclinationDeg);

    public static async Task GenerateAsync(IPfdMaskSampler sampler, MaskXmlExportOptions o,
                                           IProgress<double>? progress, CancellationToken ct)
    {
        var lats = Nodes(o.LatMinDeg, o.LatMaxDeg, o.LatStepDeg);

        bool alphaDelta = o.Kind == MaskPlotKind.AlphaDeltaLong;
        // b axis: alpha (+/-90) or azimuth (+/-90). c axis: deltaLongitude (+/-180) or elevation (+/-90).
        var bNodes = Nodes(-90.0, 90.0, o.BStepDeg);
        var cNodes = alphaDelta ? Nodes(-180.0, 180.0, o.CStepDeg) : Nodes(-90.0, 90.0, o.CStepDeg);

        bool wantXml = o.Format is MaskExportFormat.Xml or MaskExportFormat.Both;
        bool wantCsv = o.Format is MaskExportFormat.Csv or MaskExportFormat.Both;
        string xmlPath = Path.ChangeExtension(o.OutputPath, ".xml");
        string csvPath = Path.ChangeExtension(o.OutputPath, ".csv");

        await Task.Run(() =>
        {
            XmlWriter? xw = null;
            StreamWriter? cw = null;
            try
            {
                if (wantXml)
                {
                    xw = XmlWriter.Create(xmlPath, new XmlWriterSettings { Indent = true, IndentChars = "  " });
                    xw.WriteStartDocument();
                    xw.WriteStartElement("satellite_system");
                    xw.WriteAttributeString("sat_name", o.SatName);
                    xw.WriteAttributeString("ntc_id", o.NtcId.ToString(CultureInfo.InvariantCulture));
                    xw.WriteStartElement("pfd_mask");
                    xw.WriteAttributeString("mask_id", o.MaskId.ToString(CultureInfo.InvariantCulture));
                    xw.WriteAttributeString("low_freq_mhz", Num(o.LowFreqMhz));
                    xw.WriteAttributeString("high_freq_mhz", Num(o.HighFreqMhz));
                    xw.WriteAttributeString("refbw_khz", Num(o.RefBwKHz));
                    xw.WriteAttributeString("type", alphaDelta ? "alpha_deltaLongitude" : "azimuth_elevation");
                    xw.WriteAttributeString("a_name", "latitude");
                    xw.WriteAttributeString("b_name", alphaDelta ? "alpha" : "azimuth");
                    xw.WriteAttributeString("c_name", alphaDelta ? "deltaLongitude" : "elevation");
                }

                if (wantCsv)
                {
                    cw = new StreamWriter(csvPath, append: false, new UTF8Encoding(false));
                    string bName = alphaDelta ? "alpha" : "azimuth";
                    string cName = alphaDelta ? "deltaLongitude" : "elevation";
                    cw.WriteLine($"# PFD mask (dB(W/m^2) in {Num(o.RefBwKHz)} kHz)  type={(alphaDelta ? "alpha_deltaLongitude" : "azimuth_elevation")}  " +
                                 $"sat={o.SatName}  ntc_id={o.NtcId}  mask_id={o.MaskId}  unreachable=-1000");
                    // Header: latitude, b, then one column per c node.
                    var header = new StringBuilder($"latitude,{bName}");
                    foreach (double c in cNodes) header.Append(',').Append(cName).Append('=').Append(Num(c));
                    cw.WriteLine(header.ToString());
                }

                for (int li = 0; li < lats.Count; li++)
                {
                    ct.ThrowIfCancellationRequested();

                    sampler.PrepareLatitude(lats[li]);

                    if (xw != null) { xw.WriteStartElement("by_a"); xw.WriteAttributeString("a", Num(lats[li])); }

                    foreach (double b in bNodes)
                    {
                        if (xw != null) { xw.WriteStartElement("by_b"); xw.WriteAttributeString("b", Num(b)); }
                        var csvRow = cw != null ? new StringBuilder(Num(lats[li])).Append(',').Append(Num(b)) : null;

                        foreach (double c in cNodes)
                        {
                            // Map (b, c) -> field (X, Y). alpha/deltaLong: X=deltaLong=c, Y=alpha=b.
                            // Az/El: X=azimuth=b, Y=elevation=c.
                            double x = alphaDelta ? c : b;
                            double y = alphaDelta ? b : c;
                            // Each node takes the max over its bin (envelope
                            // semantics) so beam peaks survive any output step.
                            double halfX = 0.5 * (alphaDelta ? o.CStepDeg : o.BStepDeg);
                            double halfY = 0.5 * (alphaDelta ? o.BStepDeg : o.CStepDeg);
                            double pfd = sampler.SampleMaxIn(x, y, halfX, halfY);
                            string cell = double.IsNegativeInfinity(pfd)
                                ? "-1000"
                                : pfd.ToString("F1", CultureInfo.InvariantCulture);

                            if (xw != null)
                            {
                                xw.WriteStartElement("pfd");
                                xw.WriteAttributeString("c", Num(c));
                                xw.WriteString(cell);
                                xw.WriteEndElement();
                            }
                            csvRow?.Append(',').Append(cell);
                        }
                        if (xw != null) xw.WriteEndElement();   // by_b
                        if (csvRow != null) cw!.WriteLine(csvRow.ToString());
                    }
                    if (xw != null) xw.WriteEndElement();        // by_a
                    progress?.Report((li + 1.0) / lats.Count);
                }

                if (xw != null)
                {
                    xw.WriteEndElement();   // pfd_mask
                    xw.WriteEndElement();   // satellite_system
                    xw.WriteEndDocument();
                }
            }
            finally
            {
                xw?.Dispose();
                cw?.Dispose();
            }
        }, ct);
    }

    private static List<double> Nodes(double min, double max, double step)
    {
        // Grid anchored at 0 (nodes are multiples of step) so a range that
        // spans the origin always samples it exactly -- e.g. the latitude
        // table crosses lat = 0, where the GSO exclusion geometry peaks.
        // The exact range endpoints are pinned as extra nodes when the grid
        // does not land on them.
        var list = new List<double>();
        step = Math.Max(1e-6, Math.Abs(step));
        int i0 = (int)Math.Ceiling(min / step - 1e-9);
        int i1 = (int)Math.Floor(max / step + 1e-9);
        if (i0 > i1 || min < i0 * step - 1e-6) list.Add(min);
        for (int i = i0; i <= i1; i++) list.Add(i * step);
        if (list.Count == 0 || Math.Abs(list[^1] - max) > 1e-6) list.Add(max);
        return list;
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
