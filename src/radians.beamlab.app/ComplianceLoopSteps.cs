using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// The steps of the compliance loop shared by the console loop mode and the
/// compliance window's Run loop: the reachable-envelope pfd mask export and
/// its cache name, the examination against a declaration (E1), the
/// acceptance statement, the one-line description of a set, and the run's
/// artefacts. One implementation, so the two runs cannot drift apart.
/// </summary>
public static class ComplianceLoopSteps
{
    /// <summary>
    /// The cache key for an exported mask. It must cover EVERYTHING that
    /// determines the values, and the producing code is one of those things.
    /// Keying only on grid, service span and profile timestamp let a values
    /// fix hide behind a warm cache -- which happened: the band-envelope fix
    /// was invisible on every cached case until the files were deleted by
    /// hand.
    ///
    /// The producer component is the module version id of the assemblies that
    /// build and write the field, which changes on every rebuild of them. That
    /// is deliberately over-eager: a rebuild that did not touch the samplers
    /// still re-exports. Correctness is worth more than a warm cache, and
    /// because the id is IN THE NAME rather than a validity test, each build
    /// keeps its own cache instead of thrashing one.
    /// </summary>
    public static string MaskCacheTag(double maskLatStepDeg, double azElStepDeg,
        double esLatMinDeg, double esLatMaxDeg, double yawRangeDeg = 0.0)
    {
        var inv = CultureInfo.InvariantCulture;
        // A yaw-swept mask is a different mask; without yaw the tag is unchanged.
        string yaw = yawRangeDeg > 0.0 ? string.Create(inv, $"-yaw{yawRangeDeg:F1}") : "";
        return string.Create(inv,
            $"lat{maskLatStepDeg:F1}-ae{azElStepDeg:F1}-svc{esLatMinDeg:F0}to{esLatMaxDeg:F0}{yaw}-v{ProducerId()}")
            .Replace(".", "p");
    }

    /// <summary>Short id of the code that produces mask values.</summary>
    public static string ProducerId()
    {
        // Both the sampler that builds the field and the generator that bins
        // and writes it decide the values, so both assemblies are keyed.
        var a = typeof(ReachableEnvelopeSampler).Assembly.ManifestModule.ModuleVersionId;
        var b = typeof(IPfdMaskSampler).Assembly.ManifestModule.ModuleVersionId;
        Span<byte> bytes = stackalloc byte[32];
        a.TryWriteBytes(bytes[..16]);
        b.TryWriteBytes(bytes[16..]);
        // FNV-1a over the two ids: short, stable within a build, different
        // across builds. Not a security hash and does not need to be.
        ulong h = 1469598103934665603UL;
        foreach (byte x in bytes) { h ^= x; h *= 1099511628211UL; }
        return h.ToString("x16", CultureInfo.InvariantCulture)[..8];
    }

    /// <summary>
    /// How often a step samples the fastest crossing of the earth station's
    /// 3 dB beam -- the pass time of S.1503-4 Sec. D4.2 at the lowest shell --
    /// with the S.1503-4 fine step beside it. The time step is a control of
    /// every figure, as the depth and the grid are.
    /// </summary>
    public static (double PassTimeSec, double Samples, double FineStepSec) StepSampling(
        IReadOnlyList<ConstellationShell> shells, double freqMhz, double dishM, double stepSec)
    {
        var plan = S1503TimeStep.Downlink(shells, radantenna.AntennaLibrary.Compute3dBDeg(freqMhz, dishM));
        return (plan.PassTimeSec, plan.PassTimeSec / stepSec, plan.FineStepSec);
    }

    /// <summary>
    /// The step's sampling as one sentence for a record. Fewer than three
    /// samples per crossing is named as under-sampling: on STEAM-2 three
    /// samples (a 1 s step) read within 0.1 dB of the S.1503-4 step, half a
    /// sample (6 s) up to 0.5 dB below it, a twentieth (60 s) up to 2.2 dB
    /// where the maximum decides.
    /// </summary>
    public static string StepSentence(IReadOnlyList<ConstellationShell> shells, double freqMhz, double dishM, double stepSec)
    {
        var (pass, n, fine) = StepSampling(shells, freqMhz, dishM, stepSec);
        return string.Create(CultureInfo.InvariantCulture,
            $"Step: the fastest crossing of the earth station's 3 dB beam takes {pass:F2} s, so the {stepSec:0.###} s step samples it {n:0.##} time(s)")
            + (n < 3.0 ? ", fewer than three: maxima are under-sampled" : "")
            + string.Create(CultureInfo.InvariantCulture, $"; the S.1503-4 fine step is {fine:0.000} s.");
    }

    /// <summary>
    /// Exports the pfd mask of the REACHABLE envelope to <paramref name="path"/>:
    /// the ungated configuration space, the mask's saturated counterpart to the
    /// R set's saturated probe, written under the service-span certificate.
    /// <paramref name="log"/> receives the run's status lines.
    /// </summary>
    /// <summary>
    /// The body-yaw offsets the computed mask sweeps for the profile's yaw
    /// steering range: -R..+R in equal steps no coarser than the mask's
    /// az/el grid, so no peak slips between cells; {0} without yaw steering.
    /// </summary>
    public static double[] YawSweep(double? rangeDeg, double azElStepDeg)
    {
        if (rangeDeg is not double r || r <= 0.0) return new[] { 0.0 };
        int n = (int)Math.Ceiling(r / azElStepDeg);
        return Enumerable.Range(-n, 2 * n + 1).Select(j => r * j / n).ToArray();
    }

    /// <summary>
    /// Why a loop cannot use the profile's named pfd mask, or null: a named
    /// file that is missing stops the run rather than being replaced by a
    /// computed mask the operator did not ask for.
    /// </summary>
    public static string? MissingMaskNote(OperationProfile prof)
        => prof.Down.MaskXmlPath.Trim() is { Length: > 0 } named && !File.Exists(named)
            ? $"the profile names the pfd mask {named}, which is missing -- restore the file, or clear the PFD mask XML field of the profile to have the run compute the mask"
            : null;

    public static void ExportReachableMask(OperationProfile prof, ConstellationShell[] shells,
        OperatingParamsSet declaredSet, string path, double maskLatStepDeg, double azElStepDeg,
        Action<string>? log = null, IProgress<double>? progress = null)
    {
        var inv = CultureInfo.InvariantCulture;
        double altKm = shells[0].OperatingHeightKm ?? shells[0].AltitudeKm;
        double freqMhz = prof.Down.FrequencyGhz * 1000.0;
        string stem = prof.Name.Split('(')[0].Trim();
        var compMask = OperationComposer.Compose(prof, altKm);
        double maxLat = MaskXmlExport.MaxLatitudeForInclination(shells[0].InclinationDeg);
        double latMax = Math.Floor(maxLat / maskLatStepDeg) * maskLatStepDeg;
        var opts = new MaskXmlExportOptions
        {
            SatName = stem, NtcId = 0, MaskId = 1,
            LowFreqMhz = freqMhz, HighFreqMhz = freqMhz, RefBwKHz = prof.Down.RefBwKHz,
            LatMinDeg = -latMax, LatMaxDeg = latMax, LatStepDeg = maskLatStepDeg,
            BStepDeg = azElStepDeg, CStepDeg = azElStepDeg,
            Kind = MaskPlotKind.AzEl, Format = MaskExportFormat.Xml,
            OutputPath = path,
            YawSweepDeg = YawSweep(prof.Down.YawSteeringRangeDeg, azElStepDeg),
        };
        log?.Invoke(string.Create(inv,
            $"  exporting the reachable-envelope mask: lat {-latMax:F0}..{latMax:F0} step {maskLatStepDeg:F1}, az/el {azElStepDeg:F1} deg..."));
        // The service-span certificate: rows from which no declared cell is
        // reachable are written dark (Sec. C1 -1000). Closed-form, from
        // declared commitments only -- never from what a finite probe
        // happened to visit, which is the unsafe direction.
        var span = new ServiceSpanSampler(
            new ReachableEnvelopeSampler(compMask.Scene, opts, maxLat), declaredSet,
            altKm, maskLatStepDeg);
        log?.Invoke(string.Create(inv,
            $"  service-span certificate: es_lat {declaredSet.EsLatMinDeg:F0}..{declaredSet.EsLatMaxDeg:F0}, "
            + $"coverage half-angle {span.HalfAngleDeg:F2} deg"));
        MaskXmlExport.GenerateAsync(span, opts, progress, CancellationToken.None)
            .GetAwaiter().GetResult();
        log?.Invoke(string.Create(inv,
            $"  latitude rows: {span.LitLatitudes} lit, {span.DarkLatitudes} dark"));
    }

    /// <summary>
    /// The profile as the examination reads it: the declared pfd mask as the
    /// footprint and no per-latitude alpha rows -- the declaration's own
    /// exclusion governs the examination.
    /// </summary>
    public static OperationProfile MaskExamined(OperationProfile prof, string maskPath)
        => prof with
        {
            AlphaByLat = null,
            Downlink = prof.Down with { FootprintSource = "mask", MaskXmlPath = maskPath },
        };

    /// <summary>
    /// One examination sweep, E1: the declared pfd mask read against a
    /// declared R set, on the sweep's own grid and step.
    /// </summary>
    public static List<ComplianceRow> ExamineE1(ComplianceViewModel.Sweep sweep, OperationProfile prof,
        OperatingParamsSet declared, string maskPath,
        IProgress<ComplianceViewModel.SweepProgress>? progress = null)
        => ComplianceViewModel.RunSweepProfile(sweep with { Declared = declared },
            MaskExamined(prof, maskPath), progress);

    /// <summary>
    /// The acceptance statement: E1 must sit at or above T everywhere, or the
    /// derivation did not envelope the system it describes -- which is a
    /// granularity failure of the probe, not a compliance failure of the system.
    /// </summary>
    public static string E1Summary(IReadOnlyList<ComplianceRow> t,
        IReadOnlyList<ComplianceRow> e1, CultureInfo inv)
    {
        var below = new List<double>();
        for (int i = 0; i < e1.Count && i < t.Count; i++)
            if (e1[i].WorstMarginDb > t[i].WorstMarginDb + 1e-9) below.Add(t[i].LatDeg);
        double worstGap = double.NegativeInfinity;
        for (int i = 0; i < e1.Count && i < t.Count; i++)
            worstGap = Math.Max(worstGap, t[i].WorstMarginDb - e1[i].WorstMarginDb);
        string gap = string.Create(inv, $"widest gap {worstGap:F1} dB");
        string lats = string.Join(", ", below.Select(l => l.ToString("F0", inv)));
        return below.Count == 0
            ? "ADEQUATE: E1 >= T at every latitude; " + gap
            : "ADEQUACY FAILURE: E1 sits BELOW T at latitude(s) " + lats
                + " -- the probe did not envelope the system, so the declaration is not conservative; " + gap;
    }

    /// <summary>A declared set in one line: every quantity's rows or its absence.</summary>
    public static string DescribeSet(OperatingParamsSet p, CultureInfo inv)
    {
        var bits = new List<string>();
        var ex = p.MinExclude.FirstOrDefault(m => m.ByLat.Count > 0);
        bits.Add(ex is null ? "min_exclude none"
            : "min_exclude " + string.Join("/", ex.ByLat.Select(r =>
                string.Create(inv, $"{r.LatDeg:F0}:{r.AlphaDeg:F1}"))));
        bits.Add(p.MinElev.Count == 0 ? "min_elev none"
            : "min_elev " + string.Join("/", p.MinElev.Where(m => m.ByAz.Count > 0).Select(m =>
                string.Create(inv, $"{m.LatDeg:F0}:{m.ByAz[0].ElevDeg:F1}"))));
        bits.Add(p.MaxCoFreqByLat.Count == 0 ? "max_co_freq none"
            : "max_co_freq " + string.Join("/", p.MaxCoFreqByLat.Select(r =>
                string.Create(inv, $"{r.LatDeg:F0}:{r.Value}"))));
        bits.Add(string.Create(inv, $"max_co_freq_sat {p.MaxCoFreqSat?.ToString(inv) ?? "-"}"));
        bits.Add(string.Create(inv,
            $"min_angle es {p.MinAngleAtEsDeg?.ToString("F1", inv) ?? "-"} / sat {p.MinAngleAtSatDeg?.ToString("F1", inv) ?? "-"}"));
        bits.Add(string.Create(inv, $"es_lat {p.EsLatMinDeg:F0}..{p.EsLatMaxDeg:F0}"));
        return string.Join("; ", bits);
    }

    /// <summary>
    /// Writes the run's profile and R set into its directory, the set both as
    /// S.1503-4 Part B XML and in the designer's own format, so the
    /// designer's "derive & fill" can LOAD this run rather than simulate a
    /// second opinion of the same system.
    /// </summary>
    public static (string ProfilePath, string SetXmlPath, string SetJsonPath) WriteRunArtefacts(
        string repoDir, OperationProfile prof, OperatingParamsSet declaredSet)
    {
        string runDir = ComplianceViewModel.RunDir(repoDir, prof);
        Directory.CreateDirectory(runDir);
        string safe = ComplianceViewModel.RunName(prof);
        string profOut = Path.Combine(runDir, safe + ".opprofile.json");
        string setOut = Path.Combine(runDir, safe + ".operparams.xml");
        File.WriteAllText(profOut, OperationProfileCodec.Save(prof));
        OperParamsXmlWriter.Write(setOut, declaredSet);
        string setJson = ComplianceViewModel.RunSetJsonPath(repoDir, prof);
        File.WriteAllText(setJson, OpParamsFileCodec.Save(OpParamsFileCodec.FromSet(declaredSet)));
        return (profOut, setOut, setJson);
    }
}
