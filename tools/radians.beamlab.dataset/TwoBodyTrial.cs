using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using radians.beamlab;
using radians.beamlab.app;

namespace radians.beamlab.dataset;

/// <summary>
/// The benign pair for the two-body interference trial of the 11.32A concept
/// note (case (b), non-GSO against non-GSO): two systems co-frequency in
/// 19.7-20.2 GHz whose in-line events are rare by geometry, in the dataset's
/// own format so that the consumer reads them as it reads the BL family.
///
/// Variant 1, TB-M / TB-L: a 20-satellite MEO at 8 000 km (2 x 10, 45 deg,
/// orbit model Case 1 with artificial precession) against the family's shell A
/// (1 200 km / 55 deg, 4 x 8, station-kept repeating, Case 2) -- a large
/// altitude separation. Variant 2, TB-M2 / TB-L2: the same two systems with
/// disjoint-ish service latitudes (the MEO serving 30 S-30 N, the LEO 40-70 N),
/// declared through ES_LAT_MIN/MAX and written into the masks as dark rows by
/// the service-span certificate.
///
/// Per system: its own notice with orbits, a pfd mask in the azimuth/elevation
/// form (linked in the scenario) and one in the alpha/deltaLongitude form
/// (stored beside it, unlinked, so a consumer's two readers can be compared on
/// one system), an arrays-only operating-parameter set with no exclusion zone,
/// MIN_ELEV 10 deg and MAX_CO_FREQ 2 -- consistent by construction, which the
/// record grades -- plus the provenance stamp. Both payloads are set to the
/// same boresight pfd, about -120 dB(W/(m2 MHz)), a working Ka-band downlink
/// level. The typical earth station and the reference link budget are the
/// trial plan's (radians, architecture/two-body-trial-plan.md, section 4);
/// the READMEs cite the file rather than repeat its numbers.
/// </summary>
public static class TwoBodyTrial
{
    public const double FMin = 19700.0, FMax = 20200.0;
    public const double MinElevDeg = 10.0;
    public const int Nco = 2;
    /// <summary>The LEO payload offset against the dataset's mask 1: boresight pfd about -120 dB(W/(m2 MHz)) from 1 200 km (measured -119.8).</summary>
    public const double LeoTxDeltaDb = -40.0;
    /// <summary>The MEO payload offset: the same boresight pfd from 8 000 km, i.e. 20 log10(8000/1200) = 16.5 dB more e.i.r.p.</summary>
    public static double MeoTxDeltaDb => LeoTxDeltaDb + 20.0 * Math.Log10(8000.0 / 1200.0);

    public static ConstellationShell MeoShell { get; } = new()
    {
        AltitudeKm = 8000.0, InclinationDeg = 45.0, PlaneCount = 2, SatsPerPlane = 10,
        WalkerPhasingF = 1, NOrbits = 100,
    };
    public static ConstellationShell LeoShell => DatasetGenerator.ShellA;

    public sealed record TrialSystem(string Case, string SatName, int NtcId, ConstellationShell Shell, double TxDeltaDb,
        int ParamId, double EsLatMinDeg, double EsLatMaxDeg, int Variant, string Role);

    public static IReadOnlyList<TrialSystem> Systems { get; } = new[]
    {
        new TrialSystem("TB-M", "TB-MEO", 900123491, MeoShell, MeoTxDeltaDb, 41, -70.0, 70.0, 1, "the MEO, 20 satellites at 8 000 km"),
        new TrialSystem("TB-L", "TB-LEO", 900123492, LeoShell, LeoTxDeltaDb, 42, -70.0, 70.0, 1, "the LEO, 32 satellites at 1 200 km (the family's shell A)"),
        new TrialSystem("TB-M2", "TB-MEO2", 900123493, MeoShell, MeoTxDeltaDb, 43, -30.0, 30.0, 2, "the MEO serving 30 S-30 N only"),
        new TrialSystem("TB-L2", "TB-LEO2", 900123494, LeoShell, LeoTxDeltaDb, 44, 40.0, 70.0, 2, "the LEO serving 40-70 N only"),
    };

    public const int AzElMaskId = 1, AlphaMaskId = 2;

    /// <summary>The arrays-only set of a trial system: no exclusion zone, MIN_ELEV 10, MAX_CO_FREQ 2, the declared service span.</summary>
    public static OperatingParamsSet SetFor(TrialSystem s)
    {
        var p = new OperatingParamsSet
        {
            SatName = s.SatName, NtcId = s.NtcId, ParamId = s.ParamId,
            LowFreqMhz = FMin, HighFreqMhz = FMax,
            EsDensityPerKm2 = 0.00012, EsDistanceKm = 300, EsLatMinDeg = s.EsLatMinDeg, EsLatMaxDeg = s.EsLatMaxDeg,
        };
        p.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 0.0) } });
        p.MinElev.Add(new MinElevByLat { LatDeg = 0.0, ByAz = { (0.0, MinElevDeg), (360.0, MinElevDeg) } });
        p.MaxCoFreqByLat.Add((0.0, Nco));
        return p;
    }

    public static SrsNotice BuildNotice(TrialSystem s)
    {
        var n = new SrsNotice { NtcId = s.NtcId, SatName = s.SatName, Adm = "LUX" };
        n.AddShell(s.Shell);
        n.MaskInfo.Add(new SrsMaskInfo(AzElMaskId, FMin, FMax, 'P', 'Z'));
        n.MaskInfo.Add(new SrsMaskInfo(AlphaMaskId, FMin, FMax, 'P', 'A'));
        n.MaskInfo.Add(new SrsMaskInfo(s.ParamId, FMin, FMax, 'R', null));
        n.OperatingParamIds.Add(s.ParamId);
        var sc = new SrsScenario { ScenId = 1, ScenName = string.Create(CultureInfo.InvariantCulture, $"Two-body trial {s.Case} downlink 19.7-20.2 GHz") };
        sc.Frequencies.Add(new SrsFreqRange(1, 'E', FMin, FMax));
        // The scenario links the az/el mask for every orbit; the alpha-form mask is
        // stored beside it (mask_info row, no link) for the consumer's second reader.
        sc.PfdMaskLinks.Add(new SrsMaskLink(1, AzElMaskId));
        n.Scenarios.Add(sc);
        n.Validate();
        return n;
    }

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Emits the four trial systems under o.OutDir (TB-M, TB-L, TB-M2, TB-L2) and TB-README.md.</summary>
    public static void Generate(DatasetOptions o)
    {
        if (!File.Exists(o.DonorSrsPath)) throw new InvalidOperationException($"donor SRS not found: {o.DonorSrsPath}");
        if (!File.Exists(o.DonorMasksPath)) throw new InvalidOperationException($"donor Masks not found: {o.DonorMasksPath}");
        SrsMdbWriter.EpfdMasksDllDirectory = DatasetGenerator.ResolveMasksDllDir(o);
        Directory.CreateDirectory(o.OutDir);
        var inv = CultureInfo.InvariantCulture;

        foreach (var s in Systems)
        {
            o.Log($"building {s.Case} ({s.Role})...");
            string caseDir = Path.Combine(o.OutDir, s.Case);
            string xmlDir = Path.Combine(caseDir, "xml");
            string expDir = Path.Combine(caseDir, "expected");
            Directory.CreateDirectory(xmlDir);
            Directory.CreateDirectory(expDir);
            var set = SetFor(s);
            // Variant 2 writes the declared span into the masks: rows from which no
            // declared latitude is reachable are dark (the service-span certificate).
            OperatingParamsSet spanOf = s.Variant == 2 ? set : null;
            string azel = Path.Combine(xmlDir, string.Create(inv, $"mask{AzElMaskId}_pfd_azel_{s.Case.ToLowerInvariant()}.xml"));
            string alpha = Path.Combine(xmlDir, string.Create(inv, $"mask{AlphaMaskId}_pfd_alpha_{s.Case.ToLowerInvariant()}.xml"));
            DatasetGenerator.GenerateSystemMask(azel, s.Shell, s.SatName, s.NtcId, FMin, FMax, AzElMaskId, MaskPlotKind.AzEl, MinElevDeg, s.TxDeltaDb, o.Quick, spanOf);
            DatasetGenerator.GenerateSystemMask(alpha, s.Shell, s.SatName, s.NtcId, FMin, FMax, AlphaMaskId, MaskPlotKind.AlphaDeltaLong, MinElevDeg, s.TxDeltaDb, o.Quick, spanOf);
            string param = Path.Combine(xmlDir, string.Create(inv, $"param{s.ParamId}_oper.xml"));
            OperParamsXmlWriter.Write(param, set);
            o.Log("  masks (az/el + alpha) and set written");

            var notice = BuildNotice(s);
            SrsMdbWriter.WriteSrs(o.DonorSrsPath, Path.Combine(caseDir, $"{s.NtcId} SRS.MDB"), notice);
            var stored = SrsMdbWriter.WriteMasks(o.DonorMasksPath, Path.Combine(caseDir, $"{s.NtcId} Masks.MDB"), s.NtcId, s.SatName,
                new List<SrsMdbWriter.MaskContent>
                {
                    new(AzElMaskId, azel, 'P', FMin, FMax),
                    new(AlphaMaskId, alpha, 'P', FMin, FMax),
                    new(s.ParamId, param, 'R', FMin, FMax),
                });
            var bad = stored.Where(r => r.Status != 0).ToList();
            if (bad.Count > 0)
                throw new InvalidOperationException($"{s.Case}: mask store failed: " + string.Join(",", bad.Select(r => $"{r.MaskId}:{r.Status}")));

            // The consistency record: both masks against the set they were derived with.
            double alt = DatasetGenerator.MaskAltitudeKm(s.Shell);
            var gradeAzEl = MaskConsistency.Check(azel, alt, set);
            var gradeAlpha = MaskConsistency.CheckAlphaForm(MaskXmlImport.Load(alpha), set);
            var loadedAzEl = MaskXmlImport.Load(azel);
            double peak40 = loadedAzEl.Blocks.SelectMany(b => b.Rows).SelectMany(r => r.Values).Where(v => v > MaskLatBlock.UnreachableDb + 1).Max();
            var sb = new StringBuilder();
            sb.AppendLine($"# Consistency: {s.Case} -- the masks against the declared set");
            sb.AppendLine();
            sb.AppendLine("The pair is meant to be consistent by construction: the masks are the reachable envelope of the payload composed under the declared minimum elevation with no exclusion gate, and the set declares no exclusion zone. This record grades them with the producer's mask-versus-parameters check (the six grades CONSISTENT, MASK TIGHTER, LIT INSIDE, SATURATED, NOT EXERCISED, DARK; near-peak = within 3 dB of the block peak, dark = 20 dB or more down, tolerance one 1-degree cell), so the consumer's own check (its review section 16) has a stated expectation.");
            sb.AppendLine();
            sb.AppendLine(string.Create(inv, $"- az/el mask {AzElMaskId} (mapped at {alt:F0} km): **{MaskConsistency.Word(gradeAzEl.Overall)}** -- {gradeAzEl.Summary}{(gradeAzEl.Note.Length > 0 ? " (" + gradeAzEl.Note + ")" : "")}."));
            sb.AppendLine(string.Create(inv, $"- alpha/deltaLongitude mask {AlphaMaskId} (exclusion axis only, read off the alpha axis): **{MaskConsistency.Word(gradeAlpha.Overall)}** -- {gradeAlpha.Summary}."));
            sb.AppendLine(string.Create(inv, $"- Boresight pfd of the masks at this payload: {peak40:F1} dB(W/m2) in 40 kHz = {peak40 + 13.98:F1} dB(W/(m2 MHz)) (flat spectrum), a working Ka-band downlink level by design; both systems of the pair sit at the same level."));
            sb.AppendLine("- Expected of a consumer: CONSISTENT on the exclusion axis for both masks (no zone declared, none carried); on the elevation axis the near-peak grade reads CONSISTENT for a range-shaped envelope with or without a floor -- see the BL-C1 record for that limitation -- so it does not verify the 10-degree floor, which the service-span check and the link-feasibility check verify instead.");
            if (s.Variant == 2)
                sb.AppendLine(string.Create(inv, $"- Variant 2: the declared service span {s.EsLatMinDeg:F0}..{s.EsLatMaxDeg:F0} deg is written into the masks as dark rows (Sec. C1 -1000) at sub-satellite latitudes from which no declared latitude is reachable at the declared minimum elevation -- the service-span certificate; the grader reports those rows as DARK."));
            sb.AppendLine();
            sb.AppendLine("Provenance: " + Provenance.Line(o.Quick));
            File.WriteAllText(Path.Combine(expDir, "consistency.md"), sb.ToString(), Utf8NoBom);

            File.WriteAllText(Path.Combine(caseDir, "README.md"), Readme(s, peak40), Utf8NoBom);
            Provenance.WriteCaseStamp(caseDir, s.Case, s.NtcId, o.Quick, "no truth curve in this case: the trial's calculation is the consumer's; masks and set are the artefacts");
            o.Log(string.Create(inv, $"  {s.Case}: SRS + Masks (az/el + alpha) + set + consistency ({MaskConsistency.Word(gradeAzEl.Overall).Split(' ')[0]} / {MaskConsistency.Word(gradeAlpha.Overall).Split(' ')[0]}) + stamp; boresight pfd {peak40 + 13.98:F1} dB(W/(m2 MHz))"));
        }
        File.WriteAllText(Path.Combine(o.OutDir, "TB-README.md"), TopReadme(o), Utf8NoBom);
        o.Log("two-body trial pair done.");
    }

    private static string Readme(TrialSystem s, double peak40)
    {
        var inv = CultureInfo.InvariantCulture;
        var sh = s.Shell;
        string model = sh.StationKeeping ? "station-kept repeating track (orbit model Case 2)" : "free drift with artificial precession (orbit model Case 1)";
        return string.Create(inv, $"""
            # {s.Case} -- two-body trial, {s.Role}

            One of the two systems of the benign co-frequency pair for the two-body interference trial
            of the 11.32A concept note (case (b), non-GSO against non-GSO): in-line events between the
            two are rare by geometry -- variant 1 by a large altitude separation, variant 2 by disjoint
            service latitudes as well. This system: {s.SatName} (ntc_id {s.NtcId}), {sh.PlaneCount} planes x
            {sh.SatsPerPlane} satellites at {sh.AltitudeKm:F0} km, inclination {sh.InclinationDeg:F1} deg, {model}.
            Its partner in variant {s.Variant} is {(s.Case.StartsWith("TB-M") ? (s.Variant == 1 ? "TB-L" : "TB-L2") : (s.Variant == 1 ? "TB-M" : "TB-M2"))}.

            - Band 19 700-20 200 MHz, space-to-Earth (No. 5.484A; Table 22-1C rows: 70 cm, 90 cm, 2.5 m, 5 m,
              40 kHz and 1 MHz), reference bandwidth 40 kHz.
            - pfd mask {AzElMaskId}, azimuth/elevation form, linked in the scenario for every orbit; pfd mask
              {AlphaMaskId}, alpha/deltaLongitude form, stored beside it (mask_info row, not linked) so the
              consumer's two mask readers can be compared on one system. Both are the reachable envelope of
              the same payload composed under a 10-degree minimum elevation with no exclusion gate, at a
              boresight pfd of {peak40 + 13.98:F1} dB(W/(m2 MHz)) -- a working Ka-band downlink level; the
              pair's two systems sit at the same level.
            - Operating-parameter set {s.ParamId}, arrays only: no exclusion zone (one all-orbits MIN_EXCLUDE row of
              0), MIN_ELEV 10 deg, MAX_CO_FREQ {Nco}, ES_LAT {s.EsLatMinDeg:F0}..{s.EsLatMaxDeg:F0}, typical earth
              stations (ES_DENSITY 0.00012 per km2, ES_DISTANCE 300 km). Consistent with the masks by construction;
              expected/consistency.md grades it.
            - Typical earth station and reference link budget: the trial plan's, section 4 of
              C:\Projects\_EPFD\radians\architecture\two-body-trial-plan.md (0.65 m S.1428-1, about 40 dBi at
              19.95 GHz, 200 K, minimum elevation 10 deg; the wanted e.i.r.p. density, additional losses,
              M0inter/M0intra, the threshold C/N set, the short-term allowance and the single-entry apportionment
              are stated there and not repeated here, so a change there does not orphan this README).
            - No truth curve: the trial's two-body calculation is the consumer's. expected/provenance.md stamps
              the artefacts by SHA-256.
            {(s.Variant == 2 ? $"- Variant 2: the declared service span is written into the masks as dark rows at sub-satellite latitudes from which no declared latitude is reachable (the service-span certificate, Sec. C1 -1000).{Environment.NewLine}" : "")}
            Generated by tools/radians.beamlab.dataset (`--trial two-body`). Deliberately controlled rather
            than filed: the pair exists to show whether the trial's criterion discriminates a benign pair from
            a failing one.
            """);
    }

    private static string TopReadme(DatasetOptions o) => string.Create(CultureInfo.InvariantCulture, $"""
        # TB-* two-body trial pair

        The benign co-frequency pair for the two-body interference trial of the 11.32A concept note
        (case (b)): two non-GSO systems in 19.7-20.2 GHz whose in-line events are rare by geometry.

        | Case | ntc_id | System |
        |---|---|---|
        | TB-M | 900123491 | MEO, 2 x 10 at 8 000 km / 45 deg, Case 1 (artificial precession); serves 70 S-70 N |
        | TB-L | 900123492 | LEO, 4 x 8 at 1 200 km / 55 deg, Case 2 (station-kept repeating); serves 70 S-70 N |
        | TB-M2 | 900123493 | the MEO serving 30 S-30 N only (variant 2) |
        | TB-L2 | 900123494 | the LEO serving 40-70 N only (variant 2) |

        Per case: `<ntc> SRS.MDB`, `<ntc> Masks.MDB` (az/el mask 1 linked, alpha mask 2 stored beside it,
        operating-parameter set), `xml/`, `README.md`, `expected/consistency.md` (both masks graded against the
        set), `expected/provenance.md` (SHA-256 of every artefact). Both payloads sit at the same boresight
        pfd, about -120 dB(W/(m2 MHz)). The typical earth station and the reference link budget are the trial
        plan's (radians repository, architecture/two-body-trial-plan.md, section 4).

        Emission: {Provenance.Line(o.Quick)}

        Regeneration: `dotnet run --project tools/radians.beamlab.dataset -- --trial two-body --out dataset`
        (same donors and DLLs as the BL family).
        """);
}
