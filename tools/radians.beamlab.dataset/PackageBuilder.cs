using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using radians.beamlab;
using radians.beamlab.app;

namespace radians.beamlab.dataset;

/// <summary>
/// One cross-read package: a filed pfd mask delivered verbatim as raw XML,
/// paired with a constellation reconstructed from an orbit design and an R set
/// this project derived. The notice goes into an SRS database the BL way; the
/// mask and the operating-parameter XML travel as files (no Masks database --
/// the consumer reads the XML directly), so a second implementation can
/// examine the same declaration at the same victims.
/// </summary>
public sealed class PackageOptions
{
    public string Name { get; set; }
    public string DesignPath { get; set; }
    public string RsetJsonPath { get; set; }
    public string MaskXmlPath { get; set; }
    public int MaskId { get; set; } = 150;
    public double BandMinMhz { get; set; } = 17700;
    public double BandMaxMhz { get; set; } = 20200;
    // 900123481: the number after BL-C1's. The first package was emitted with
    // BL-C1's own 900123480 (2026-09-07), a collision corrected on 2026-09-22.
    public int NtcId { get; set; } = 900123481;
    public string SatName { get; set; } = "BEAMLAB-FILED";
    /// <summary>Optional record copied into expected/ (this project's verdicts at the victims).</summary>
    public string ExpectedPath { get; set; }
    /// <summary>Optional provenance line for the README (where the mask came from).</summary>
    public string Provenance { get; set; }
}

public static class PackageBuilder
{
    public static void Build(PackageOptions p, DatasetOptions o)
    {
        var inv = CultureInfo.InvariantCulture;
        if (string.IsNullOrWhiteSpace(p.Name)) throw new ArgumentException("package needs a name");
        foreach (var (label, path) in new[] { ("design", p.DesignPath), ("R set", p.RsetJsonPath), ("mask", p.MaskXmlPath) })
            if (path is null || !File.Exists(path))
                throw new InvalidOperationException($"{label} not found: {path}");
        if (!File.Exists(o.DonorSrsPath))
            throw new InvalidOperationException($"donor SRS not found: {o.DonorSrsPath}");

        // The system: shells exactly as the loop propagates them.
        var doc = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(p.DesignPath));
        var shells = doc.Shells.Select(OrbitDesignFileCodec.ToShell).ToArray();

        // The declaration: the derived R set, re-labelled for this notice and
        // declared over the mask's band (its values are geometric; the band it
        // was derived at is recorded in the README).
        var set = OpParamsFileCodec.ToSet(OpParamsFileCodec.Load(File.ReadAllText(p.RsetJsonPath)));
        double derivedLo = set.LowFreqMhz, derivedHi = set.HighFreqMhz;
        set.NtcId = p.NtcId;
        set.SatName = p.SatName;
        set.LowFreqMhz = p.BandMinMhz;
        set.HighFreqMhz = p.BandMaxMhz;

        string caseDir = Path.Combine(o.OutDir, p.Name);
        string xmlDir = Path.Combine(caseDir, "xml");
        string expDir = Path.Combine(caseDir, "expected");
        Directory.CreateDirectory(xmlDir);
        Directory.CreateDirectory(expDir);

        string paramXml = Path.Combine(xmlDir, $"param{set.ParamId}_oper.xml");
        OperParamsXmlWriter.Write(paramXml, set);

        string maskXml = Path.Combine(xmlDir, $"mask{p.MaskId}_pfd_azel_filed.xml");
        CopyWithRootNtcId(p.MaskXmlPath, maskXml, p.NtcId);
        o.Log(FormattableString.Invariant(
            $"mask copied ({new FileInfo(maskXml).Length / 1048576.0:F1} MB), root ntc_id set to {p.NtcId}"));

        var n = new SrsNotice { NtcId = p.NtcId, SatName = p.SatName };
        foreach (var sh in shells) n.AddShell(sh);
        n.MaskInfo.Add(new SrsMaskInfo(p.MaskId, p.BandMinMhz, p.BandMaxMhz, 'P', 'Z'));
        n.MaskInfo.Add(new SrsMaskInfo(set.ParamId, p.BandMinMhz, p.BandMaxMhz, 'R', null));
        var sc = new SrsScenario
        {
            ScenId = 1,
            ScenName = FormattableString.Invariant($"down {p.BandMinMhz:F0}-{p.BandMaxMhz:F0} MHz, filed pfd mask {p.MaskId}"),
        };
        sc.Frequencies.Add(new SrsFreqRange(1, 'E', p.BandMinMhz, p.BandMaxMhz));
        sc.PfdMaskLinks.Add(new SrsMaskLink(1, p.MaskId));
        var bands = NearestReadBands(set);
        foreach (var b in bands) sc.SatOper.Add(b);
        n.Scenarios.Add(sc);
        n.OperatingParamIds.Add(set.ParamId);

        string srsPath = Path.Combine(caseDir, $"{p.NtcId} SRS.MDB");
        SrsMdbWriter.WriteSrs(o.DonorSrsPath, srsPath, n);
        // The SRS layer carries no copy of the gates: grp.elev_min is
        // deprecated and epfd_param.elev_min / x_zone stay empty when an
        // operating-parameter set overrides them (design brief Sec. 3.8). The
        // R set is the one source; sat_oper is the notice's own table.
        o.Log(string.Create(inv, $"SRS written: {n.Orbits.Count} orbit rows, {n.Phases.Count} phase rows, {bands.Count} sat_oper rows; the gates live in the R set only"));

        // The mask travels as raw XML; no Masks database is built (operator
        // direction, 2026-09-07: the consumer reads the XML directly). A
        // database left by an earlier build of the same package is removed so
        // the directory holds one form of the mask only.
        string staleMasks = Path.Combine(caseDir, $"{p.NtcId} Masks.MDB");
        if (File.Exists(staleMasks))
        {
            File.Delete(staleMasks);
            o.Log("removed " + Path.GetFileName(staleMasks) + " from an earlier build");
        }
        o.Log("mask delivered as raw XML: xml/" + Path.GetFileName(maskXml));

        string expectedName = null;
        if (p.ExpectedPath is string ep && File.Exists(ep))
        {
            expectedName = Path.GetFileName(ep);
            File.Copy(ep, Path.Combine(expDir, expectedName), overwrite: true);
        }

        WriteReadme(Path.Combine(caseDir, "README.md"), p, o, shells, set, derivedLo, derivedHi, bands,
            expectedName, n);
        o.Log("package: " + caseDir);
    }

    /// <summary>
    /// sat_oper rows as the nearest-row read of MAX_CO_FREQ[lat] reconstructs
    /// them: each row governs to the midpoints with its neighbours, the end rows
    /// to the poles (Sec. D5.1.5 step 1 reads the nearest row everywhere).
    /// </summary>
    public static List<(double LatFr, double LatTo, int NbrOpSat)> NearestReadBands(OperatingParamsSet set)
    {
        var rows = set.MaxCoFreqByLat.OrderBy(r => r.LatDeg).ToList();
        var bands = new List<(double, double, int)>();
        if (rows.Count == 0)
        {
            if (set.MaxCoFreqHeader is int h) bands.Add((-90.0, 90.0, h));
            return bands;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            double lo = i == 0 ? -90.0 : 0.5 * (rows[i - 1].LatDeg + rows[i].LatDeg);
            double hi = i == rows.Count - 1 ? 90.0 : 0.5 * (rows[i].LatDeg + rows[i + 1].LatDeg);
            bands.Add((lo, hi, rows[i].Value));
        }
        return bands;
    }

    /// <summary>
    /// Copy a mask XML, rewriting only the root element's ntc_id attribute --
    /// streamed, so an 88 MB filing is never held as a DOM. The root start tag
    /// is the first tag whose name begins with a letter (after any declaration).
    /// </summary>
    internal static void CopyWithRootNtcId(string srcPath, string dstPath, int ntcId)
    {
        using var fin = File.OpenRead(srcPath);
        using var fout = File.Create(dstPath);
        var head = new byte[8192];
        int n = fin.Read(head, 0, head.Length);
        int lt = -1;
        for (int i = 0; i + 1 < n; i++)
            if (head[i] == (byte)'<' && char.IsLetter((char)head[i + 1])) { lt = i; break; }
        if (lt < 0) throw new InvalidOperationException("no root element within the first 8 KB of " + srcPath);
        int gt = Array.IndexOf(head, (byte)'>', lt, n - lt);
        if (gt < 0) throw new InvalidOperationException("root start tag does not close within the first 8 KB of " + srcPath);
        string tag = Encoding.ASCII.GetString(head, lt, gt - lt + 1);
        string id = ntcId.ToString(CultureInfo.InvariantCulture);
        string patched = Regex.IsMatch(tag, "ntc_id=\"[^\"]*\"")
            ? Regex.Replace(tag, "ntc_id=\"[^\"]*\"", "ntc_id=\"" + id + "\"")
            : tag.Insert(tag.Length - 1, " ntc_id=\"" + id + "\"");
        fout.Write(head, 0, lt);
        var tb = Encoding.ASCII.GetBytes(patched);
        fout.Write(tb, 0, tb.Length);
        fout.Write(head, gt + 1, n - gt - 1);
        fin.CopyTo(fout);
    }

    private static string Fmt(double? v) => v is double d ? d.ToString("0.###", CultureInfo.InvariantCulture) : "-";

    private static void WriteReadme(string path, PackageOptions p, DatasetOptions o, ConstellationShell[] shells,
        OperatingParamsSet set, double derivedLo, double derivedHi, List<(double LatFr, double LatTo, int NbrOpSat)> bands,
        string expectedName, SrsNotice n)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"# {p.Name}: a filed pfd mask, examined by two implementations");
        sb.AppendLine();
        sb.AppendLine("A cross-read package. The pfd mask is an operator's filed declaration stored verbatim;");
        sb.AppendLine("the constellation is this project's reconstruction from public parameters; the R set is");
        sb.AppendLine("this project's derivation. Nothing here is the operator's filed R set or the operator's");
        sb.AppendLine("notice. The package exists so that a second S.1503 implementation can examine the same");
        sb.AppendLine("declaration at the same victims and the two verdicts can be compared.");
        if (!string.IsNullOrWhiteSpace(p.Provenance)) { sb.AppendLine(); sb.AppendLine(p.Provenance); }
        sb.AppendLine();
        sb.AppendLine("## Contents");
        sb.AppendLine();
        sb.AppendLine($"- `{p.NtcId} SRS.MDB` -- the notice: orbit and phase rows, one down scenario linking mask {p.MaskId}, sat_oper rows, mask_info registering mask {p.MaskId} (P) and param {set.ParamId} (R), mask_lnk3 to the operating parameters; plus the S.1503-2 group parameters (below).");
        sb.AppendLine($"- `xml/mask{p.MaskId}_pfd_azel_filed.xml` -- the filed mask as raw XML, byte-identical to the source except the root `ntc_id`, rewritten to {p.NtcId}. No Masks database is built: read the XML directly.");
        sb.AppendLine($"- `xml/param{set.ParamId}_oper.xml` -- the R set in the S.1503-4 operating-parameter form, raw XML likewise.");
        if (expectedName is not null) sb.AppendLine($"- `expected/{expectedName}` -- this project's verdicts at the victims, both depths, with their caveats.");
        sb.AppendLine();
        sb.AppendLine("## The system, as declared");
        sb.AppendLine();
        foreach (var sh in shells)
        {
            string phasing = sh.InterPlanePhaseDeg is double ipp
                ? string.Create(inv, $"exact inter-plane phase {ipp:0.###} deg")
                : string.Create(inv, $"Walker F = {sh.WalkerPhasingF}");
            sb.AppendLine(string.Create(inv,
                $"- {sh.PlaneCount} planes x {sh.SatsPerPlane} satellites at {sh.AltitudeKm:0.#} km, inclination {sh.InclinationDeg:0.#} deg, {phasing}, LAN spread {sh.LanSpreadDeg:0.#} deg, station keeping {(sh.StationKeeping ? "Y" : "N")}."));
        }
        sb.AppendLine(string.Create(inv, $"- {n.Orbits.Count} orbit rows, {n.Phases.Count} phase rows. The declared phase rows are the propagated system's own initial phases (harness check V45)."));
        sb.AppendLine();
        sb.AppendLine("## The R set (S.1503-4 form, `xml/param" + set.ParamId.ToString(inv) + "_oper.xml`)");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"- Derived by measurement on a saturated probe at {derivedLo:0.#}-{derivedHi:0.#} MHz; declared here over the mask's band {p.BandMinMhz:0.#}-{p.BandMaxMhz:0.#} MHz. The values are geometric (elevation floor, exclusion angle, satellite count) and do not depend on frequency; the band label is the only thing widened."));
        sb.AppendLine("- One form per quantity: min_elev and max_co_freq are filed as per-latitude arrays and carry no header attribute; min_duration is not filed (the classic algorithm). Beyond the outermost rows the nearest row is the outermost row, so the arrays are complete at every latitude.");
        sb.AppendLine(string.Create(inv, $"- es_lat {set.EsLatMinDeg:0.#}..{set.EsLatMaxDeg:0.#}; es_density {(set.EsDensityPerKm2 is double dd ? dd.ToString("0.############", inv) : "-")} per km2; es_distance {Fmt(set.EsDistanceKm)} km; min_angle_at_sat {Fmt(set.MinAngleAtSatDeg)}; min_angle_at_es {Fmt(set.MinAngleAtEsDeg)}; max_co_freq_sat {(set.MaxCoFreqSat is int mcs ? mcs.ToString(inv) : "-")}."));
        sb.AppendLine("- min_elev rows: " + string.Join(", ", set.MinElev.OrderBy(b => b.LatDeg).Select(b =>
            string.Create(inv, $"{b.LatDeg:0.#}:{b.ByAz.Min(r => r.ElevDeg):0.#}{(b.ByAz.Count > 1 ? "(by az)" : "")}"))) + ".");
        sb.AppendLine("- min_exclude rows: " + string.Join("; ", set.MinExclude.Select(e =>
            string.Create(inv, $"orb {e.OrbId}: ") + string.Join(", ", e.ByLat.OrderBy(r => r.LatDeg).Select(r => string.Create(inv, $"{r.LatDeg:0.#}:{r.AlphaDeg:0.#}"))))) + ".");
        sb.AppendLine("- max_co_freq rows: " + string.Join(", ", set.MaxCoFreqByLat.OrderBy(r => r.LatDeg).Select(r => string.Create(inv, $"{r.LatDeg:0.#}:{r.Value}"))) + ".");
        sb.AppendLine();
        sb.AppendLine("## The SRS layer");
        sb.AppendLine();
        sb.AppendLine("- The notice carries no copy of the gates. `grp.elev_min` is deprecated, and `epfd_param.elev_min` / `epfd_param.x_zone` are left empty because the operating-parameter set overrides them (design brief Sec. 3.8; EPS V43). A reader takes MIN_ELEV, MIN_EXCLUDE and MAX_CO_FREQ from `xml/param" + set.ParamId.ToString(inv) + "_oper.xml`, where each quantity is filed in one form -- here the per-latitude arrays, whose outermost rows govern every latitude beyond them.");
        sb.AppendLine("- `sat_oper` rows (scenario 1) remain the notice's own table, written as the nearest-row read of max_co_freq[lat] with the end rows carried to the poles: " +
            string.Join(", ", bands.Select(b => string.Create(inv, $"[{b.LatFr:0.#},{b.LatTo:0.#}]={b.NbrOpSat}"))) + ".");
        sb.AppendLine("- The exclusion angle the R set declares is the alpha angle at the earth station between the direction to the non-GSO satellite and the direction to the GSO arc -- the angle this project's derivation uses and the angle in which the mask dissection found the filed notch constant across latitudes (the dissection did not test whether the notch is equally constant in the satellite-based X angle).");
        sb.AppendLine();
        sb.AppendLine("## The mask");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"- Source: `{Path.GetFileName(p.MaskXmlPath)}`, {new FileInfo(p.MaskXmlPath).Length / 1048576.0:F1} MB, delivered as `xml/mask{p.MaskId}_pfd_azel_filed.xml`. Only the root `ntc_id` was rewritten (to match the notice); the XML keeps the filing's own `sat_name`, while the notice carries `{p.SatName}`."));
        sb.AppendLine($"- The mask is registered in `mask_info` as mask_id {p.MaskId} (f_mask P, az/el) and linked to every orbit in scenario 1; the file is the mask. No Masks database is built for this package.");
        sb.AppendLine();
        sb.AppendLine("## The victims this project examined");
        sb.AppendLine();
        sb.AppendLine("- Earth station at longitude 0, latitudes 0, 10, 20, 30, 40, 50, 60 deg; GSO satellite at +10 deg longitude offset; 1.00 m dish; Article 22 TABLE 22-1B, FSS 17800-18600 MHz, 40 kHz; 0.1 d and 1.0 d at 60 s steps.");
        sb.AppendLine("- Entered geometry, not a search: run them as Additional Tests with the same earth station, GSO offset and dish.");
        sb.AppendLine();
        sb.AppendLine("## Caveats that travel with any verdict on this package");
        sb.AppendLine();
        sb.AppendLine("- The R set is this project's derivation, not the operator's filed set; a larger exclusion or a smaller cap on the operator's side would lower the epfd.");
        sb.AppendLine("- The constellation is a reconstruction from public parameters (planes, satellites per plane, altitude, inclination, inter-plane phase); the operator's filed orbit rows may differ.");
        sb.AppendLine("- min_elev 40 deg is generous to the filing: fewer eligible satellites, lower epfd.");
        sb.AppendLine("- One limit row was applied by this project (17800-18600 MHz); the mask's band reaches 20200 MHz where TABLE 22-1C governs.");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv, $"Generated {DateTime.Now:yyyy-MM-dd} by `radians.beamlab.dataset --package {p.Name} --design ... --rset ... --mask ... --mask-id {p.MaskId} --band {p.BandMinMhz:0.#} {p.BandMaxMhz:0.#} --ntc {p.NtcId}`."));
        File.WriteAllText(path, sb.ToString());
    }
}
