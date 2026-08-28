using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// Writes a single-notice SNS v10 content set into real BR databases
/// (open decision 3 of the simulation spec, resolved: write directly).
///
/// SRS: clone a user-supplied V10 donor/template MDB, clear the
/// notice-scoped tables, INSERT the <see cref="SrsNotice"/> rows via ACE
/// OleDB -- populating exactly the columns the worked NEXT101/NEXT102
/// notices populate. Masks: clone a donor Masks.MDB, clear the masks table,
/// and store each mask XML through the BR's own native
/// EPFD_Masks_Store (EpfdMasksApi64.dll) -- the only sanctioned writer of
/// the zipped memo encoding, the same library radians P/Invokes to read it.
///
/// Windows-only (ACE OLEDB provider + the BR DLL); nothing here ships BR
/// binaries or templates -- both are supplied by path.
/// </summary>
public static class SrsMdbWriter
{
    private const string Provider = "Microsoft.ACE.OLEDB.12.0";

    /// <summary>Notice-scoped tables cleared before inserting (v10 read surface of the examination).</summary>
    private static readonly string[] NoticeTables =
    {
        "notice", "com_el", "non_geo", "orbit", "phase", "orbit_set",
        "epfd_param", "epfd_freq", "sat_oper",
        "mask_info", "mask_lnk1", "mask_lnk2", "mask_lnk3",
    };

    public static void WriteSrs(string donorSrsPath, string outSrsPath, SrsNotice n)
    {
        n.Validate();
        File.Copy(donorSrsPath, outSrsPath, overwrite: true);

        using var conn = new OleDbConnection($"Provider={Provider};Data Source={outSrsPath}");
        conn.Open();

        foreach (var t in NoticeTables)
            Exec(conn, $"DELETE FROM [{t}]");

        // notice + com_el: the minimal identity set the worked notices carry.
        Exec(conn,
            "INSERT INTO notice (ntc_id, prov, adm, ntf_rsn, st_cur, act_code, d_rcv, ntc_type) VALUES (?,?,?,?,?,?,?,?)",
            n.NtcId, n.Prov, n.Adm, n.NtfRsn.ToString(), n.StCur, n.ActCode.ToString(), n.DRcv, n.NtcType.ToString());
        Exec(conn,
            "INSERT INTO com_el (ntc_id, prov, adm, sat_name, act_code, ntf_rsn, st_cur, d_rcv, ntc_type) VALUES (?,?,?,?,?,?,?,?,?)",
            n.NtcId, n.Prov, n.Adm, n.SatName, n.ActCode.ToString(), n.NtfRsn.ToString(), n.StCur, n.DRcv, n.NtcType.ToString());

        Exec(conn,
            "INSERT INTO non_geo (ntc_id, sat_name, ref_body, nbr_plane, f_constell, multi_config_type, examset_type) VALUES (?,?,?,?,?,?,?)",
            n.NtcId, n.SatName, n.RefBody.ToString(), n.PlaneCount, n.FConstell.ToString(),
            n.MultiConfigType.ToString(), n.ExamSetType.ToString());

        foreach (var o in n.Orbits)
        {
            Exec(conn,
                @"INSERT INTO orbit (ntc_id, orb_id, nbr_sat_pl, right_asc, inclin_ang,
                    prd_ddd, prd_hh, prd_mm, apog_km, perig_km, perig_arg, op_ht_km,
                    f_stn_keep, keep_rnge, f_precess, precession, long_asc, f_sunsynch,
                    rpt_prd_dd, rpt_prd_hh, rpt_prd_mm, rpt_prd_ss)
                  VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                n.NtcId, o.OrbId, o.NbrSatPl, o.LanDeg, o.InclinationDeg,
                o.Period.Days, o.Period.Hours, o.Period.Minutes,
                o.ApogeeKm, o.PerigeeKm, o.PerigArgDeg, o.OpHtKm,
                o.StationKeeping ? "Y" : "N",
                o.StationKeeping ? (object?)o.KeepRangeDeg : null,
                o.PrecessionSupplied ? "Y" : "N",
                o.PrecessionSupplied ? (object?)o.PrecessionRateDegPerSec : null,
                o.LanDeg, "N",
                o.RepeatPeriod is { } rp ? (object)rp.Days : DBNull.Value,
                o.RepeatPeriod is { } rp2 ? (object)rp2.Hours : DBNull.Value,
                o.RepeatPeriod is { } rp3 ? (object)rp3.Minutes : DBNull.Value,
                o.RepeatPeriod is { } rp4 ? (object)rp4.Seconds : DBNull.Value);
        }

        foreach (var ph in n.Phases)
            Exec(conn, "INSERT INTO phase (ntc_id, orb_id, orb_sat_id, phase_ang) VALUES (?,?,?,?)",
                n.NtcId, ph.OrbId, ph.OrbSatId, ph.PhaseAngDeg);

        foreach (var sc in n.Scenarios)
        {
            Exec(conn, "INSERT INTO epfd_param (ntc_id, scen_id, scen_name) VALUES (?,?,?)",
                n.NtcId, sc.ScenId, sc.ScenName);
            foreach (var f in sc.Frequencies)
                Exec(conn, "INSERT INTO epfd_freq (ntc_id, scen_id, seq_no, emi_rcp, freq_min, freq_max) VALUES (?,?,?,?,?,?)",
                    n.NtcId, sc.ScenId, f.SeqNo, f.EmiRcp.ToString(), f.FreqMinMhz, f.FreqMaxMhz);
            foreach (var l in sc.PfdMaskLinks)
                Exec(conn, "INSERT INTO mask_lnk1 (ntc_id, scen_id, seq_no, orb_id, sat_orb_id, mask_id) VALUES (?,?,?,?,?,?)",
                    n.NtcId, sc.ScenId, l.SeqNo, l.OrbId, (object?)l.SatOrbId ?? DBNull.Value, l.MaskId);
            foreach (var l in sc.EsMaskLinks)
                Exec(conn, "INSERT INTO mask_lnk2 (ntc_id, scen_id, seq_no, e_as_id, orb_id, sat_orb_id, mask_id) VALUES (?,?,?,?,?,?,?)",
                    n.NtcId, sc.ScenId, l.SeqNo, (object?)l.EAsId ?? -1, l.OrbId, (object?)l.SatOrbId ?? DBNull.Value, l.MaskId);
            foreach (var (latFr, latTo, nco) in sc.SatOper)
                Exec(conn, "INSERT INTO sat_oper (ntc_id, scen_id, lat_fr, lat_to, nbr_op_sat) VALUES (?,?,?,?,?)",
                    n.NtcId, sc.ScenId, latFr, latTo, nco);
        }

        foreach (var m in n.MaskInfo)
            Exec(conn, "INSERT INTO mask_info (ntc_id, mask_id, freq_min, freq_max, f_mask, f_mask_type) VALUES (?,?,?,?,?,?)",
                n.NtcId, m.MaskId, m.FreqMinMhz, m.FreqMaxMhz, m.FMask.ToString(),
                m.FMaskType is char c ? c.ToString() : (object)DBNull.Value);

        foreach (int pid in n.OperatingParamIds)
            Exec(conn, "INSERT INTO mask_lnk3 (ntc_id, param_id) VALUES (?,?)", n.NtcId, pid);
    }

    private static void Exec(OleDbConnection conn, string sql, params object?[] args)
    {
        using var cmd = new OleDbCommand(sql, conn);
        foreach (var a in args)
            cmd.Parameters.AddWithValue("?", a ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // --- Masks.MDB via the BR native API ---

    /// <summary>Directory containing EpfdMasksApi64.dll; set before the first masks call.</summary>
    public static string? EpfdMasksDllDirectory { get; set; }

    static SrsMdbWriter()
    {
        NativeLibrary.SetDllImportResolver(typeof(SrsMdbWriter).Assembly, (name, _, _) =>
        {
            if (name == "EpfdMasksApi64.dll" && EpfdMasksDllDirectory is string dir)
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return NativeLibrary.Load(p);
            }
            return IntPtr.Zero;
        });
    }

    [DllImport("EpfdMasksApi64.dll")]
    private static extern int EPFD_Masks_Store(uint inNoticeID, uint inMaskID, string inMasksDBPath, string inMasksXMLFilePath);

    [DllImport("EpfdMasksApi64.dll")]
    private static extern int EPFD_Masks_Extract(uint inNoticeID, uint inMaskID, string inMasksDBPath, string inMasksXMLFilePath);

    /// <summary>One mask to place into Masks.MDB. FMask: P/S/E (native store) or R (custom store).</summary>
    public sealed record MaskContent(int MaskId, string XmlPath, char FMask, double FreqMinMhz, double FreqMaxMhz);

    /// <summary>
    /// Clone a donor Masks.MDB, clear it, and store each mask XML. P/S/E
    /// masks go through the BR native EPFD_Masks_Store; R (operating
    /// parameter) masks are refused by that API's validation, so they are
    /// stored directly in the observed format -- a zip archive with the entry
    /// named "mask ntc_id N mask_id M fmin-fmax MHz.xml" in the binary mask
    /// column -- which the BR native extractor accepts (verified: extract
    /// status 0 on both the worked donors and our own rows).
    /// Returns the store status per mask (0 = OK).
    /// </summary>
    public static IReadOnlyList<(int MaskId, int Status)> WriteMasks(
        string donorMasksPath, string outMasksPath, int ntcId, string satName,
        IEnumerable<MaskContent> masks)
    {
        File.Copy(donorMasksPath, outMasksPath, overwrite: true);
        using (var conn = new OleDbConnection($"Provider={Provider};Data Source={outMasksPath}"))
        {
            conn.Open();
            Exec(conn, "DELETE FROM masks");
        }

        var results = new List<(int, int)>();
        foreach (var m in masks)
        {
            int status;
            if (m.FMask == 'R')
            {
                StoreRMask(outMasksPath, ntcId, satName, m);
                status = 0;
            }
            else
            {
                status = EPFD_Masks_Store((uint)ntcId, (uint)m.MaskId, outMasksPath, m.XmlPath);
            }
            results.Add((m.MaskId, status));
        }
        return results;
    }

    private static void StoreRMask(string masksPath, int ntcId, string satName, MaskContent m)
    {
        string entryName =
            $"mask ntc_id {ntcId} mask_id {m.MaskId} " +
            $"{m.FreqMinMhz.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}-" +
            $"{m.FreqMaxMhz.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} MHz.xml";

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
            using var es = entry.Open();
            var bytes = File.ReadAllBytes(m.XmlPath);
            es.Write(bytes, 0, bytes.Length);
        }

        using var conn = new OleDbConnection($"Provider={Provider};Data Source={masksPath}");
        conn.Open();
        // f_mask_type carries no meaning for R masks; the column refuses
        // zero-length strings, and the worked donors hold a single space.
        Exec(conn,
            "INSERT INTO masks (ntc_id, mask_id, sat_name, f_mask, f_mask_type, mask) VALUES (?,?,?,?,?,?)",
            ntcId, m.MaskId, satName, "R", " ", ms.ToArray());
    }

    /// <summary>Round-trip helper for verification: extract one mask back out to an XML file.</summary>
    public static int ExtractMask(string masksPath, int ntcId, int maskId, string outXmlPath)
        => EPFD_Masks_Extract((uint)ntcId, (uint)maskId, masksPath, outXmlPath);
}
