using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using radians.beamlab;
using radians.beamlab.app;
using Radians.Orbits.Core.Utilities;

namespace radians.beamlab.dataset;

public sealed class DatasetOptions
{
    public string DonorSrsPath { get; set; } = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 SRS.MDB";
    public string DonorMasksPath { get; set; } = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 Masks.MDB";
    /// <summary>Directory holding EpfdMasksApi64.dll; null probes the known locations.</summary>
    public string EpfdMasksDllDir { get; set; }
    public string OutDir { get; set; } = "dataset";
    /// <summary>Coarse grids and short expectation runs (structure verification, not delivery).</summary>
    public bool Quick { get; set; }
    /// <summary>Generate a single case (e.g. "BL-D1"); null generates the family.</summary>
    public string OnlyCase { get; set; }
    /// <summary>The BR limits database the section 3.9 probes verdict against; null probes the known locations.</summary>
    public string LimitsDbPath { get; set; }
    public Action<string> Log { get; set; } = _ => { };
}

/// <summary>
/// Emits the BL-* S.1503-4 validation case family over one constellation:
/// per case an SRS database, a Masks database, the mask XML sources, a
/// README, and (downlink cases) an EpfdDown CDF as expectation data. The
/// case catalog and the system it exercises follow the dataset design brief
/// (s1503-4-dataset-design-brief.md): three shells covering the three orbit
/// models, five bands covering the P/E/S/R mask forms and both downlink
/// algorithms. Deliberately over-featured relative to a real filing -- it is
/// a coverage vehicle, and the READMEs say so.
/// </summary>
public static class DatasetGenerator
{
    public const string SatName = "BEAMLAB";
    public static readonly string[] CaseNames = { "BL-D1", "BL-D2", "BL-U1", "BL-U2", "BL-I1", "BL-ALL", "BL-R1", "BL-R2", "BL-R3", "BL-C1" };
    public static int NtcIdFor(string caseName) => 900123471 + Array.IndexOf(CaseNames, caseName);

    // ---- the one constellation (brief section 4) ----------------------

    /// <summary>Shell A: circular 1200 km, station-kept repeating track (orbit model Case 2).</summary>
    public static ConstellationShell ShellA { get; } = new()
    {
        AltitudeKm = 1200.0, InclinationDeg = 55.0, PlaneCount = 4, SatsPerPlane = 8,
        WalkerPhasingF = 1, StationKeeping = true, WDeltaDeg = 0.5,
        RepeatPeriod = RepeatOf(1200.0, 13),
    };

    /// <summary>Shell B: circular 900 km polar, free drift (Case 1, artificial precession).</summary>
    public static ConstellationShell ShellB { get; } = new()
    {
        AltitudeKm = 900.0, InclinationDeg = 87.0, PlaneCount = 6, SatsPerPlane = 6,
        WalkerPhasingF = 1, NOrbits = 288,
    };

    /// <summary>
    /// Shell C: elliptical (perigee 800 km, apogee 4000 km), administration
    /// -supplied J2 nodal precession (Case 3), operating height 1000 km.
    /// </summary>
    public static ConstellationShell ShellC { get; } = BuildShellC();

    private static ConstellationShell BuildShellC()
    {
        const double perigAltKm = 800.0, apogAltKm = 4000.0;
        double rp = OrbitalConstants.EarthRadiusKm + perigAltKm;
        double ra = OrbitalConstants.EarthRadiusKm + apogAltKm;
        double a = (rp + ra) / 2.0, e = (ra - rp) / (ra + rp);
        return new ConstellationShell
        {
            AltitudeKm = a - OrbitalConstants.EarthRadiusKm, Eccentricity = e,
            InclinationDeg = 63.4, ArgumentOfPerigeeDeg = 270.0,
            PlaneCount = 2, SatsPerPlane = 4, WalkerPhasingF = 1,
            OperatingHeightKm = 1000.0,
            // Standard J2 nodal regression rate, declared by the administration.
            PrecessionSupplied = true,
            PrecessionRateDegPerSec = OrbitDesign.J2NodalRateDegPerSec(a, e, 63.4),
        };
    }

    private static (int, int, int, int) RepeatOf(double altKm, int orbits)
    {
        double a = OrbitalConstants.EarthRadiusKm + altKm;
        double periodSec = 2.0 * Math.PI * Math.Sqrt(a * a * a / OrbitalConstants.MuEarth);
        int total = (int)Math.Round(orbits * periodSec);
        return (total / 86400, total % 86400 / 3600, total % 3600 / 60, total % 60);
    }

    public static ConstellationShell[] Shells => new[] { ShellA, ShellB, ShellC };

    // Plane numbering follows satellite order across shells (the scheduler's
    // SRS orb_id map): A = 1..4, B = 5..10, C = 11..12.
    private const int OrbA0 = 1, OrbB0 = 5, OrbC0 = 11, OrbLast = 12;

    // ---- bands and mask identities ------------------------------------

    private sealed record Band(string Key, char EmiRcp, double FMin, double FMax, int ParamId);
    private static readonly Band D1 = new("D1", 'E', 19700, 20200, 21);
    private static readonly Band D2 = new("D2", 'E', 17800, 18600, 22);
    // The same band under a VALID (arrays-only) set: set 22 files two quantities in
    // both forms and is the invalid-filing probe (design brief Sec. 3.8), so the
    // everything case reads the D2 band through set 26 instead.
    private static readonly Band D2v = new("D2", 'E', 17800, 18600, 26);
    private static readonly Band U1 = new("U1", 'R', 27500, 28600, 23);
    private static readonly Band U2 = new("U2", 'R', 29500, 30000, 24);
    private static readonly Band I1 = new("I1", 'E', 17800, 18400, 25);
    // The section 3.9 read-rule probes each file their own set in the D1 band (one set per band per notice).
    private static readonly Band R1b = new("D1", 'E', 19700, 20200, 27);
    private static readonly Band R2b = new("D1", 'E', 19700, 20200, 28);
    private static readonly Band R3b = new("D1", 'E', 19700, 20200, 29);
    // The section 3.10 consistency probe files its set in the D2 band, whose masks are per shell.
    private static readonly Band C1b = new("D2", 'E', 17800, 18600, 30);
    private static readonly Band[] AllBands = { D1, D2, D2v, U1, U2, I1, R1b, R2b, R3b, C1b };

    private sealed record MaskDef(int MaskId, char FMask, char? FMaskType, Band Band, string FileName);
    private static readonly MaskDef[] MaskDefs =
    {
        new(1, 'P', 'A', D1, "mask1_pfd_alpha.xml"),
        new(2, 'P', 'Z', D2, "mask2_pfd_azel_shellA.xml"),
        new(3, 'P', 'Z', D2, "mask3_pfd_azel_shellB.xml"),
        new(4, 'P', 'Z', D2, "mask4_pfd_azel_shellC.xml"),
        new(5, 'P', 'Z', D2, "mask5_pfd_azel_sat1.xml"),
        new(6, 'S', 'O', I1, "mask6_ss_eirp.xml"),
        new(7, 'E', 'O', U1, "mask7_es_eirp_2d.xml"),
        new(8, 'E', 'D', U2, "mask8_es_eirp_4d_gw5001.xml"),
        new(9, 'E', 'D', U2, "mask9_es_eirp_4d_gw5002.xml"),
        new(10, 'E', 'D', U2, "mask10_es_eirp_4d_gw5003.xml"),
        // Section 3.9 probe masks: mask 1's construction with a rule notch and a power offset (ReadRuleProbes).
        new(11, 'P', 'A', R1b, "mask11_pfd_alpha_probe_nearest.xml"),
        new(12, 'P', 'A', R2b, "mask12_pfd_alpha_probe_interp.xml"),
        new(13, 'P', 'A', R3b, "mask13_pfd_alpha_probe_sweep.xml"),
        // Section 3.10 consistency probe: the saturated per-shell masks (no gate, no floor; ConsistencyProbe).
        new(14, 'P', 'Z', C1b, "mask14_pfd_azel_saturated_shellA.xml"),
        new(15, 'P', 'Z', C1b, "mask15_pfd_azel_saturated_shellB.xml"),
        new(16, 'P', 'Z', C1b, "mask16_pfd_azel_saturated_shellC.xml"),
    };
    private static string ParamFile(int paramId) => $"param{paramId}_oper.xml";

    private sealed record Gateway(int EAsId, string Name, double LonDeg, double LatDeg);
    private static readonly Gateway[] Gateways =
    {
        new(5001, "GW-NORTH", 11.6, 48.1),
        new(5002, "GW-SOUTH", 18.4, -33.9),
        new(5003, "GW-EAST", 139.7, 35.7),
    };

    // ---- entry point ---------------------------------------------------

    /// <summary>The BR mask API directory: the option when given, else the known locations.</summary>
    internal static string ResolveMasksDllDir(DatasetOptions o)
    {
        string dllDir = o.EpfdMasksDllDir ?? new[]
        {
            @"C:\Projects\_EPFD\radians\radians\dlls",
            @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
        }.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdMasksApi64.dll")));
        if (dllDir is null || !File.Exists(Path.Combine(dllDir, "EpfdMasksApi64.dll")))
            throw new InvalidOperationException("EpfdMasksApi64.dll not found; pass EpfdMasksDllDir");
        return dllDir;
    }

    public static void Generate(DatasetOptions o)
    {
        if (!File.Exists(o.DonorSrsPath))
            throw new InvalidOperationException($"donor SRS not found: {o.DonorSrsPath}");
        if (!File.Exists(o.DonorMasksPath))
            throw new InvalidOperationException($"donor Masks not found: {o.DonorMasksPath}");
        SrsMdbWriter.EpfdMasksDllDirectory = ResolveMasksDllDir(o);

        Directory.CreateDirectory(o.OutDir);
        string srcDir = Path.Combine(o.OutDir, "_src");
        Directory.CreateDirectory(srcDir);

        o.Log("generating mask XML sources (shared across cases)...");
        BuildMaskSources(srcDir, o);

        var cases = o.OnlyCase is null ? CaseNames : new[] { o.OnlyCase };
        foreach (string c in cases)
        {
            if (!CaseNames.Contains(c)) throw new ArgumentException($"unknown case {c}");
            o.Log($"building {c}...");
            BuildCase(c, srcDir, o);
        }
        WriteTopReadme(o);
        o.Log("done.");
    }

    // ---- mask XML sources (identity-neutral; patched per case) ---------

    private static PfdMaskViewModel Vm(ConstellationShell sh, double freqGhz, double minElevDeg,
        double alphaExclDeg, double txDeltaDb = 0.0)
    {
        var vm = new PfdMaskViewModel
        {
            // Elliptical shells transmit from the operating height up; the
            // envelope's worst range is the minimum operating height.
            AltitudeKm = sh.OperatingHeightKm ?? sh.AltitudeKm,
            FrequencyGHz = freqGhz,
            MinElevDeg = minElevDeg,
            AlphaExclDeg = alphaExclDeg,
            RefBwKHz = 40.0,
        };
        vm.TxEirpDbw += txDeltaDb;
        return vm;
    }

    private sealed class MaxOfSamplers : IPfdMaskSampler
    {
        private readonly IPfdMaskSampler[] _inner;
        public MaxOfSamplers(params IPfdMaskSampler[] inner) => _inner = inner;
        public void PrepareLatitude(double latDeg) { foreach (var s in _inner) s.PrepareLatitude(latDeg); }
        public double SampleMaxIn(double xDeg, double yDeg, double halfW, double halfH)
            => _inner.Max(s => s.SampleMaxIn(xDeg, yDeg, halfW, halfH));
    }

    /// <summary>
    /// The declared exclusion zone written into an alpha/deltaLongitude mask:
    /// alpha nodes strictly inside |alpha| &lt; notch read the Sec. C1 -1000
    /// null -- no emission toward earth stations that close to the GSO arc --
    /// while the node at the edge keeps its envelope value, so the bilinear
    /// read (Sec. D5.1.5) ramps back to the lit level over one node step. A
    /// rule notch, as real filings write it; the reachable envelope alone does
    /// not produce one on this family, whose beams are far wider than the
    /// zone. Used by the section 3.9 probe masks.
    /// </summary>
    private sealed class ExclusionNotchSampler : IPfdMaskSampler
    {
        private readonly IPfdMaskSampler _inner;
        private readonly double _notchDeg;
        public ExclusionNotchSampler(IPfdMaskSampler inner, double notchDeg) { _inner = inner; _notchDeg = notchDeg; }
        public void PrepareLatitude(double latDeg) => _inner.PrepareLatitude(latDeg);
        // AlphaDeltaLong axes: x = deltaLongitude, y = alpha.
        public double SampleMaxIn(double xDeg, double yDeg, double halfW, double halfH)
            => Math.Abs(yDeg) < _notchDeg - 1e-9 ? double.NegativeInfinity : _inner.SampleMaxIn(xDeg, yDeg, halfW, halfH);
    }

    /// <summary>
    /// A pfd mask for one system outside the BL family (the two-body trial):
    /// the reachable envelope of one shell in the given form, under the given
    /// minimum elevation with no exclusion gate, at a payload offset against
    /// mask 1's; with spanOf, the declared service span is written in as dark
    /// rows (the service-span certificate). Header identities as given.
    /// </summary>
    public static void GenerateSystemMask(string path, ConstellationShell shell, string satName, int ntcId,
        double fMinMhz, double fMaxMhz, int maskId, MaskPlotKind kind, double minElevDeg, double txDeltaDb,
        bool quick, OperatingParamsSet spanOf = null)
        => GeneratePfd(path, new[] { (shell, txDeltaDb) }, new Band("TB", 'E', fMinMhz, fMaxMhz, 0), maskId, kind,
            alphaExcl: 0.0, minElev: minElevDeg, quick, spanOf: spanOf, satName: satName, ntcId: ntcId);

    private static void GeneratePfd(string path, IReadOnlyList<(ConstellationShell Shell, double TxDeltaDb)> shells,
        Band band, int maskId, MaskPlotKind kind, double alphaExcl, double minElev, bool quick,
        double? bStepDeg = null, double notchAlphaDeg = 0.0, OperatingParamsSet spanOf = null,
        string satName = null, int ntcId = 0)
    {
        double latCap = shells.Max(s => MaskXmlExport.MaxLatitudeForInclination(s.Shell.InclinationDeg));
        var opts = new MaskXmlExportOptions
        {
            SatName = satName ?? SatName, NtcId = ntcId, MaskId = maskId,
            LowFreqMhz = band.FMin, HighFreqMhz = band.FMax, RefBwKHz = 40,
            LatMinDeg = -Math.Min(70.0, latCap), LatMaxDeg = Math.Min(70.0, latCap),
            LatStepDeg = quick ? 35 : 10,
            BStepDeg = quick ? 30 : bStepDeg ?? 5, CStepDeg = quick ? 60 : 10,
            Kind = kind, Format = MaskExportFormat.Xml, OutputPath = path,
        };
        var samplers = shells
            .Select(s => (IPfdMaskSampler)new ReachableEnvelopeSampler(
                Vm(s.Shell, band.FMin / 1000.0, minElev, alphaExcl, s.TxDeltaDb), opts, s.Shell.InclinationDeg))
            .ToArray();
        IPfdMaskSampler sampler = samplers.Length == 1 ? samplers[0] : new MaxOfSamplers(samplers);
        if (notchAlphaDeg > 0.0)
        {
            if (kind != MaskPlotKind.AlphaDeltaLong) throw new ArgumentException("the exclusion notch is defined on the alpha axis only");
            sampler = new ExclusionNotchSampler(sampler, notchAlphaDeg);
        }
        if (spanOf is not null)
        {
            // The declared service span written into the mask: rows from which no
            // declared latitude is reachable at the declared floor are dark.
            double alt = shells.Min(s => s.Shell.OperatingHeightKm ?? s.Shell.AltitudeKm);
            sampler = new ServiceSpanSampler(sampler, spanOf, alt, opts.LatStepDeg);
        }
        MaskXmlExport.GenerateAsync(sampler, opts, null, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// A probe pfd mask: mask 1's construction (alpha/deltaLongitude form over
    /// all three shells, D1 band) with its own gating and a power offset -- the
    /// masks the section 3.9 read-rule probes are examined against, and the
    /// mask a measurement scan generates before the probe values are chosen.
    /// </summary>
    public static void GenerateProbeMask(string path, int maskId, ProbeMaskSpec spec, bool quick)
        => GeneratePfd(path, new[] { (ShellA, spec.TxDeltaDb), (ShellB, spec.TxDeltaDb), (ShellC, spec.TxDeltaDb) }, D1, maskId,
            MaskPlotKind.AlphaDeltaLong, spec.GateAlphaDeg, spec.MinElevDeg, quick,
            bStepDeg: spec.BStepDeg, notchAlphaDeg: spec.NotchAlphaDeg);

    /// <summary>
    /// A probe mask's construction: the boresight gate and minimum elevation
    /// the envelope is composed under, the power offset against mask 1's
    /// payload, the rule notch written into the alpha axis (0 = none) and the
    /// alpha node step (finer than mask 1's 5 deg so the notch edge is sharp).
    /// </summary>
    public sealed record ProbeMaskSpec(double GateAlphaDeg, double MinElevDeg, double TxDeltaDb,
        double NotchAlphaDeg, double BStepDeg);

    /// <summary>The D1 band edges (MHz): the band the read-rule probe masks and sets are filed in.</summary>
    public static (double FMin, double FMax) ProbeBandMhz => (D1.FMin, D1.FMax);

    /// <summary>The D2 band edges (MHz): the band the consistency probe is filed in.</summary>
    public static (double FMin, double FMax) ConsistencyBandMhz => (D2.FMin, D2.FMax);

    /// <summary>The altitude a shell's az/el mask is dissected at: the operating height for an elliptical shell.</summary>
    public static double MaskAltitudeKm(ConstellationShell sh) => sh.OperatingHeightKm ?? sh.AltitudeKm;

    /// <summary>Monotone non-increasing hull from the far end: a valid upper envelope.</summary>
    private static double[] Hull(double[] raw)
    {
        var h = (double[])raw.Clone();
        for (int i = h.Length - 2; i >= 0; i--) h[i] = Math.Max(h[i], h[i + 1]);
        return h;
    }

    private static void GenerateEs2D(string path, bool quick)
    {
        var ant = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 28000.0, 0.65);
        double[] theta = { 0, 1, 2, 3, 5, 8, 12, 20, 35, 60, 90, 140, 180 };
        double[] env = Hull(theta.Select(t => ant.GetAntGain(t, 0.0)).ToArray());
        var t7 = new EirpMaskTable
        {
            SatName = SatName, NtcId = 0, MaskId = 7,
            LowFreqMhz = U1.FMin, HighFreqMhz = U1.FMax, RefBwKHz = 40, MinElevDeg = 10, EsId = -1,
        };
        foreach (var (lat, derate) in new[] { (-45.0, -1.0), (0.0, 0.0), (45.0, -1.0) })
        {
            var blk = new EirpLatBlock { LatDeg = lat };
            for (int i = 0; i < theta.Length; i++)
                blk.ByAngle.Add((theta[i], 12.0 + env[i] + derate));
            t7.Blocks.Add(blk);
        }
        var warn = EirpMaskXmlWriter.WriteEs(path, t7);
        if (warn.Count > 0) throw new InvalidOperationException("ES 2-D mask not monotone: " + warn[0]);
    }

    /// <summary>
    /// Off-axis angle between an earth station's boresight (azimuth/elevation
    /// at latitude lat, spherical Earth) and the GSO arc point displaced
    /// dLong in longitude -- the geometry the 4-D e.i.r.p. mask tabulates.
    /// </summary>
    private static double OffAxisDeg(double latDeg, double azDeg, double elDeg, double dLongDeg)
    {
        double lat = latDeg * Math.PI / 180.0;
        double re = OrbitalConstants.EarthRadiusKm;
        double ex = re * Math.Cos(lat), ez = re * Math.Sin(lat);
        double az = azDeg * Math.PI / 180.0, el = elDeg * Math.PI / 180.0;
        double e = Math.Cos(el) * Math.Sin(az), n = Math.Cos(el) * Math.Cos(az), u = Math.Sin(el);
        double bx = -n * Math.Sin(lat) + u * Math.Cos(lat);
        double by = e;
        double bz = n * Math.Cos(lat) + u * Math.Sin(lat);
        double dl = dLongDeg * Math.PI / 180.0;
        double gx = GsoGeometry.GsoRadiusKm * Math.Cos(dl) - ex;
        double gy = GsoGeometry.GsoRadiusKm * Math.Sin(dl);
        double gz = -ez;
        double gn = Math.Sqrt(gx * gx + gy * gy + gz * gz);
        double dot = (bx * gx + by * gy + bz * gz) / gn;
        return Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * 180.0 / Math.PI;
    }

    private static void GenerateEs4D(string path, int maskId, Gateway gw, bool quick)
    {
        var ant = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 29750.0, 2.4);
        double[] azs = quick ? new double[] { 0, 120, 240 }
                             : Enumerable.Range(0, 12).Select(k => 30.0 * k).ToArray();
        double[] els = quick ? new double[] { 10, 90 } : new double[] { 10, 30, 50, 70, 90 };
        double[] dls = quick ? new double[] { 0, 10, 180 } : new double[] { 0, 2, 5, 10, 20, 40, 90, 180 };

        var m = new EirpMask4D
        {
            SatName = SatName, NtcId = 0, MaskId = maskId,
            LowFreqMhz = U2.FMin, HighFreqMhz = U2.FMax, RefBwKHz = 40, MinElevDeg = 10, EsId = gw.EAsId,
        };
        var blk = new Eirp4DLatBlock { LatDeg = gw.LatDeg };
        foreach (double az in azs)
            foreach (double el in els)
            {
                var pt = new Eirp4DPointing { AzDeg = az, ElDeg = el };
                double[] raw = dls.Select(dl =>
                    15.0 + ant.GetAntGain(OffAxisDeg(gw.LatDeg, az, el, dl), 0.0)).ToArray();
                double[] env = Hull(raw);
                for (int i = 0; i < dls.Length; i++) pt.ByDeltaLong.Add((dls[i], env[i]));
                blk.Pointings.Add(pt);
            }
        m.Blocks.Add(blk);
        var warn = EirpMaskXmlWriter.WriteEs4D(path, m);
        if (warn.Count > 0) throw new InvalidOperationException("ES 4-D mask not monotone: " + warn[0]);
    }

    private static void GenerateSs(string path, bool quick)
    {
        var vm = Vm(ShellA, I1.FMin / 1000.0, 10.0, 10.0);
        double[] lats = quick ? new double[] { 0, 50 }
                              : Enumerable.Range(0, 9).Select(k => -60.0 + 15.0 * k).ToArray();
        double[] angles = { 0, 2, 5, 10, 20, 40, 90, 180 };
        var t6 = SatEirpMaskBuilder.Build(vm, ShellA.InclinationDeg, lats, angles,
            azimuthSamples: quick ? 36 : 120);
        t6.SatName = SatName; t6.NtcId = 0; t6.MaskId = 6;
        t6.LowFreqMhz = I1.FMin; t6.HighFreqMhz = I1.FMax; t6.RefBwKHz = 40;
        EirpMaskXmlWriter.WriteSs(path, t6);
    }

    // ---- operating-parameter sets (brief section 3.8 triplet) ----------

    // The family declares NO exclusion zone: one all-orbits row of 0 (operator
    // decision of 2026-09-13). Its payload has 450 km cells, some 20 degrees
    // wide from 1200 km, so a boresight gate of a few degrees leaves no trace
    // in the reachable envelope -- the masks light the GSO arc whatever the
    // gate -- and a declared zone the masks do not carry made every downlink
    // pair inconsistent (the finding of the section 3.10 probe). Declaration
    // and masks now agree; the zone as a declared, varying element is
    // exercised by the probe cases (BL-R2 interpolation, BL-R1/R3 rule
    // notches, BL-C1 beside saturated masks). The masks are derived with the
    // same gate (none), so mask and declaration stay two products of one
    // construction.
    private static void AddNoExclusionZone(OperatingParamsSet s)
        => s.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 0.0) } });

    /// <summary>D1: per-latitude arrays only (no header scalars) -- track-duration algorithm.</summary>
    public static OperatingParamsSet Set21(int ntcId)
    {
        var s = new OperatingParamsSet
        {
            SatName = SatName, NtcId = ntcId, ParamId = D1.ParamId,
            LowFreqMhz = D1.FMin, HighFreqMhz = D1.FMax,
            EsDensityPerKm2 = 0.00012, EsDistanceKm = 300, EsLatMinDeg = -70, EsLatMaxDeg = 70,
        };
        AddNoExclusionZone(s);
        s.MaxCoFreqByLat.AddRange(new[] { (-70.0, 2), (-50.0, 3), (50.0, 3), (70.0, 2) });
        s.MinDurationByLat.AddRange(new[] { (-70.0, 60), (-40.0, 120), (40.0, 120), (70.0, 60) });
        foreach (double lat in new[] { -60.0, -30.0, 0.0, 30.0, 60.0 })
            s.MinElev.Add(new MinElevByLat
            {
                LatDeg = lat,
                ByAz = { (0.0, lat < 0 ? 12.0 : 10.0), (90.0, 10.0), (180.0, lat > 0 ? 12.0 : 10.0), (270.0, 10.0) },
            });
        return s;
    }

    /// <summary>
    /// D2, the INVALID-FILING PROBE: header scalars AND arrays with different
    /// values for max_co_freq and min_elev. Header and array are mutually
    /// exclusive per quantity (design brief Sec. 3.8, EPS V43 Sec. 6.7.2.2), so
    /// this set is invalid by construction; its expectation record is the
    /// rejection, and the writer emits it only through the deliberate opt-in.
    /// Classic algorithm otherwise: MIN_ANGLE_AT_ES set, MIN_DURATION absent.
    /// </summary>
    public static OperatingParamsSet Set22(int ntcId)
    {
        var s = new OperatingParamsSet
        {
            SatName = SatName, NtcId = ntcId, ParamId = D2.ParamId,
            LowFreqMhz = D2.FMin, HighFreqMhz = D2.FMax,
            EsDensityPerKm2 = 0.00012, EsDistanceKm = 300, EsLatMinDeg = -70, EsLatMaxDeg = 70,
            ElevAngleHeaderDeg = 5.0, MaxCoFreqHeader = 4, MinAngleAtEsDeg = 2.5,
        };
        AddNoExclusionZone(s);
        s.MaxCoFreqByLat.AddRange(new[] { (-60.0, 2), (60.0, 2) });
        foreach (double lat in new[] { -60.0, 0.0, 60.0 })
            s.MinElev.Add(new MinElevByLat { LatDeg = lat, ByAz = { (0.0, 10.0), (180.0, 10.0) } });
        return s;
    }

    /// <summary>
    /// D2, arrays only: set 22's per-latitude arrays without its header scalars
    /// -- the VALID set through which BL-ALL reads the D2 band, so the family
    /// keeps a valid examination of masks 2-5 while set 22 carries the probe.
    /// </summary>
    public static OperatingParamsSet Set26(int ntcId)
    {
        var s = new OperatingParamsSet
        {
            SatName = SatName, NtcId = ntcId, ParamId = D2v.ParamId,
            LowFreqMhz = D2v.FMin, HighFreqMhz = D2v.FMax,
            EsDensityPerKm2 = 0.00012, EsDistanceKm = 300, EsLatMinDeg = -70, EsLatMaxDeg = 70,
            MinAngleAtEsDeg = 2.5,
        };
        AddNoExclusionZone(s);
        s.MaxCoFreqByLat.AddRange(new[] { (-60.0, 2), (60.0, 2) });
        foreach (double lat in new[] { -60.0, 0.0, 60.0 })
            s.MinElev.Add(new MinElevByLat { LatDeg = lat, ByAz = { (0.0, 10.0), (180.0, 10.0) } });
        return s;
    }

    /// <summary>U1: header scalars only -- typical earth stations.</summary>
    public static OperatingParamsSet Set23(int ntcId)
    {
        var s = new OperatingParamsSet
        {
            SatName = SatName, NtcId = ntcId, ParamId = U1.ParamId,
            LowFreqMhz = U1.FMin, HighFreqMhz = U1.FMax,
            EsDensityPerKm2 = 0.0002, EsDistanceKm = 250, EsLatMinDeg = -60, EsLatMaxDeg = 60,
            ElevAngleHeaderDeg = 10.0, MaxCoFreqHeader = 3, MaxCoFreqSat = 2, MinAngleAtSatDeg = 1.5,
        };
        s.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 10.0) } });
        return s;
    }

    /// <summary>U2: specific earth stations -- density and distance switched off.</summary>
    public static OperatingParamsSet Set24(int ntcId)
    {
        var s = new OperatingParamsSet
        {
            SatName = SatName, NtcId = ntcId, ParamId = U2.ParamId,
            LowFreqMhz = U2.FMin, HighFreqMhz = U2.FMax,
            EsLatMinDeg = -60, EsLatMaxDeg = 60,
            ElevAngleHeaderDeg = 15.0, MaxCoFreqSat = 1, MinAngleAtSatDeg = 2.0,
        };
        s.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 10.0) } });
        return s;
    }

    /// <summary>I1: minimal set for the inter-satellite band.</summary>
    public static OperatingParamsSet Set25(int ntcId)
    {
        var s = new OperatingParamsSet
        {
            SatName = SatName, NtcId = ntcId, ParamId = I1.ParamId,
            LowFreqMhz = I1.FMin, HighFreqMhz = I1.FMax,
            ElevAngleHeaderDeg = 5.0,
        };
        AddNoExclusionZone(s);
        return s;
    }

    public static OperatingParamsSet SetFor(int paramId, int ntcId) => paramId switch
    {
        21 => Set21(ntcId), 22 => Set22(ntcId), 23 => Set23(ntcId),
        24 => Set24(ntcId), 25 => Set25(ntcId), 26 => Set26(ntcId),
        27 => ReadRuleProbes.Set27(ntcId), 28 => ReadRuleProbes.Set28(ntcId), 29 => ReadRuleProbes.Set29(ntcId),
        30 => ConsistencyProbe.Set30(ntcId),
        _ => throw new ArgumentOutOfRangeException(nameof(paramId)),
    };

    /// <summary>The operating-parameter set ids a case carries (mask_lnk3).</summary>
    public static IReadOnlyList<int> ParamsOf(string caseName) => CaseParams[caseName];

    /// <summary>
    /// The expected outcome for a set that files a quantity in both the header
    /// and the array form: the rejection, with the diagnostic a consumer must
    /// produce. No CDF is expected for such a case.
    /// </summary>
    public static string RejectionText(OperatingParamsSet set, double fMinMhz, double fMaxMhz)
    {
        var both = DeclaredConstraints.FormConflicts(set);
        var sb = new StringBuilder();
        sb.AppendLine("# Expected outcome: REJECTION -- an invalid operating-parameter set");
        sb.AppendLine();
        sb.AppendLine(FormattableString.Invariant(
            $"Operating-parameter set param_id {set.ParamId} ({fMinMhz}-{fMaxMhz} MHz) files these quantities in both the header-attribute form and the per-latitude-array form, with different values:"));
        sb.AppendLine();
        foreach (var q in both) sb.AppendLine("- " + q);
        sb.AppendLine();
        sb.AppendLine("Header and array are mutually exclusive per quantity (design brief Sec. 3.8; EPS V43 Sec. 6.7.2.2): a valid set files each of min_elev, max_co_freq and min_duration in exactly one form. A set carrying both is an invalid filing. The expected consumer behaviour is a rejection with a diagnostic naming the quantities -- not an examination under any precedence rule, and not a silent choice of one form. No epfd CDF is expected for this case. The pfd masks and the notice are otherwise well-formed, so the rejection must come from the operating-parameter set and from nothing else.");
        sb.AppendLine();
        sb.AppendLine("Reference diagnostic (this toolchain):");
        sb.AppendLine();
        sb.AppendLine("    " + Diagnostic(set));
        return sb.ToString();
    }

    /// <summary>The one-line diagnostic this toolchain prints for a both-forms set (the same line the examination mode prints).</summary>
    public static string Diagnostic(OperatingParamsSet set)
        => "INVALID operating-parameter set: filed in both header and array form -- "
           + string.Join("; ", DeclaredConstraints.FormConflicts(set))
           + " (design brief Sec. 3.8, EPS V43 Sec. 6.7.2.2). Not examined.";

    private static void WriteRejectionExpectation(string path, OperatingParamsSet set, Band band)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, RejectionText(set, band.FMin, band.FMax), Utf8NoBom);
    }

    private static void BuildMaskSources(string srcDir, DatasetOptions o)
    {
        string P(string f) => Path.Combine(srcDir, f);
        // Declared operating constraints feed the reachable envelope: the
        // minimum elevation below bounds the mask above; the family declares
        // no exclusion zone, so no boresight gate is applied (see
        // AddNoExclusionZone) -- declaration and masks are derived together.
        GeneratePfd(P(MaskDefs[0].FileName),
            new[] { (ShellA, 0.0), (ShellB, 0.0), (ShellC, 0.0) }, D1, 1,
            MaskPlotKind.AlphaDeltaLong, alphaExcl: 0.0, minElev: 10.0, o.Quick);
        o.Log("  mask 1 (alpha, all shells) done");
        GeneratePfd(P(MaskDefs[1].FileName), new[] { (ShellA, 0.0) }, D2, 2,
            MaskPlotKind.AzEl, 0.0, 10.0, o.Quick);
        GeneratePfd(P(MaskDefs[2].FileName), new[] { (ShellB, 0.0) }, D2, 3,
            MaskPlotKind.AzEl, 0.0, 10.0, o.Quick);
        GeneratePfd(P(MaskDefs[3].FileName), new[] { (ShellC, 0.0) }, D2, 4,
            MaskPlotKind.AzEl, 0.0, 10.0, o.Quick);
        // Named-satellite override: the first satellite of plane 1 commits
        // to a 3 dB tighter payload (mask_lnk1 granularity sat_orb_id).
        GeneratePfd(P(MaskDefs[4].FileName), new[] { (ShellA, -3.0) }, D2, 5,
            MaskPlotKind.AzEl, 0.0, 10.0, o.Quick);
        o.Log("  masks 2-5 (az/el per shell + named satellite) done");
        GenerateSs(P(MaskDefs[5].FileName), o.Quick);
        GenerateEs2D(P(MaskDefs[6].FileName), o.Quick);
        for (int g = 0; g < Gateways.Length; g++)
            GenerateEs4D(P(MaskDefs[7 + g].FileName), 8 + g, Gateways[g], o.Quick);
        o.Log("  masks 6-10 (S, ES 2-D, ES 4-D x3) done");
        GenerateProbeMask(P(MaskDefs[10].FileName), 11, ReadRuleProbes.MaskSpecR1, o.Quick);
        GenerateProbeMask(P(MaskDefs[11].FileName), 12, ReadRuleProbes.MaskSpecR2, o.Quick);
        GenerateProbeMask(P(MaskDefs[12].FileName), 13, ReadRuleProbes.MaskSpecR3, o.Quick);
        o.Log("  masks 11-13 (section 3.9 probe masks: rule notch + power offset) done");
        // The saturated masks: the same payload composed with NO boresight gate
        // and NO elevation floor -- full load, no victim avoidance -- beside a
        // set that declares both (design brief Sec. 3.10).
        GeneratePfd(P(MaskDefs[13].FileName), new[] { (ShellA, ConsistencyProbe.TxDeltaDb) }, C1b, 14, MaskPlotKind.AzEl, 0.0, 0.0, o.Quick);
        GeneratePfd(P(MaskDefs[14].FileName), new[] { (ShellB, ConsistencyProbe.TxDeltaDb) }, C1b, 15, MaskPlotKind.AzEl, 0.0, 0.0, o.Quick);
        GeneratePfd(P(MaskDefs[15].FileName), new[] { (ShellC, ConsistencyProbe.TxDeltaDb) }, C1b, 16, MaskPlotKind.AzEl, 0.0, 0.0, o.Quick);
        o.Log("  masks 14-16 (section 3.10 saturated masks: no gate, no floor) done");
        foreach (int pid in new[] { 21, 22, 23, 24, 25, 26, 27, 28, 29, 30 })
            // Set 22 files max_co_freq and min_elev in both forms with different
            // values: by the ruling of 2026-09-07 (design brief Sec. 3.8) that is
            // the invalid-filing probe, emitted deliberately; its expectation
            // record is the rejection. Every other set is one form per quantity.
            OperParamsXmlWriter.Write(P(ParamFile(pid)), SetFor(pid, 0), allowBothForms: pid == D2.ParamId);
        o.Log("  operating-parameter sets 21-30 done (22 = the invalid-filing probe, written on purpose; 27-29 = the read-rule probes; 30 = the consistency probe)");
    }

    // ---- per-case notice content ---------------------------------------

    private static void AddEarthStations(SrsNotice n)
    {
        foreach (var gw in Gateways)
            n.EarthStations.Add(new SrsEarthStation
            {
                EAsId = gw.EAsId, StnName = gw.Name, StnType = 'S',
                LonDeg = gw.LonDeg, LatDeg = gw.LatDeg,
                NoiseT = 150, GainDbi = 55.0, AntDiamM = 2.4,
            });
    }

    private static void AddShellPfdLinks(SrsScenario sc, ref int seq, bool namedOverride,
        int maskA = 2, int maskB = 3, int maskC = 4)
    {
        if (namedOverride)
            sc.PfdMaskLinks.Add(new SrsMaskLink(seq++, MaskId: 5, OrbId: OrbA0, SatOrbId: 1));
        for (int orb = OrbA0; orb < OrbB0; orb++) sc.PfdMaskLinks.Add(new SrsMaskLink(seq++, maskA, orb));
        for (int orb = OrbB0; orb < OrbC0; orb++) sc.PfdMaskLinks.Add(new SrsMaskLink(seq++, maskB, orb));
        for (int orb = OrbC0; orb <= OrbLast; orb++) sc.PfdMaskLinks.Add(new SrsMaskLink(seq++, maskC, orb));
    }

    public static SrsNotice BuildNotice(string caseName)
    {
        var n = new SrsNotice { NtcId = NtcIdFor(caseName), SatName = SatName, Adm = "LUX" };
        foreach (var sh in Shells) n.AddShell(sh);

        void Masks(params int[] ids)
        {
            foreach (var d in MaskDefs.Where(d => ids.Contains(d.MaskId)))
                n.MaskInfo.Add(new SrsMaskInfo(d.MaskId, d.Band.FMin, d.Band.FMax, d.FMask, d.FMaskType));
        }
        void Params(params int[] pids)
        {
            foreach (int pid in pids)
            {
                var b = AllBands.Single(x => x.ParamId == pid);
                n.MaskInfo.Add(new SrsMaskInfo(pid, b.FMin, b.FMax, 'R', null));
                n.OperatingParamIds.Add(pid);
            }
        }

        switch (caseName)
        {
            case "BL-D1":
            {
                Masks(1); Params(CaseParams[caseName]);
                var sc = new SrsScenario { ScenId = 1, ScenName = "Track duration downlink 19.7-20.2 GHz" };
                sc.Frequencies.Add(new SrsFreqRange(1, D1.EmiRcp, D1.FMin, D1.FMax));
                sc.PfdMaskLinks.Add(new SrsMaskLink(1, 1));
                n.Scenarios.Add(sc);
                break;
            }
            case "BL-D2":
            {
                Masks(2, 3, 4, 5); Params(CaseParams[caseName]);
                var sc = new SrsScenario { ScenId = 1, ScenName = "Classic downlink 17.8-18.6 GHz angular separation" };
                sc.Frequencies.Add(new SrsFreqRange(1, D2.EmiRcp, D2.FMin, D2.FMax));
                int seq = 1;
                AddShellPfdLinks(sc, ref seq, namedOverride: true);
                n.Scenarios.Add(sc);
                break;
            }
            case "BL-U1":
            {
                Masks(7); Params(CaseParams[caseName]);
                var sc = new SrsScenario { ScenId = 1, ScenName = "Typical uplink 27.5-28.6 GHz" };
                sc.Frequencies.Add(new SrsFreqRange(1, U1.EmiRcp, U1.FMin, U1.FMax));
                sc.EsMaskLinks.Add(new SrsMaskLink(1, 7, EAsId: -1));
                n.Scenarios.Add(sc);
                break;
            }
            case "BL-U2":
            {
                Masks(8, 9, 10); Params(CaseParams[caseName]);
                AddEarthStations(n);
                var sc = new SrsScenario { ScenId = 1, ScenName = "Specific gateway uplink 29.5-30.0 GHz" };
                sc.Frequencies.Add(new SrsFreqRange(1, U2.EmiRcp, U2.FMin, U2.FMax));
                for (int g = 0; g < Gateways.Length; g++)
                    sc.EsMaskLinks.Add(new SrsMaskLink(g + 1, 8 + g, EAsId: Gateways[g].EAsId));
                n.Scenarios.Add(sc);
                break;
            }
            case "BL-I1":
            {
                Masks(2, 3, 4, 6); Params(CaseParams[caseName]);
                var sc = new SrsScenario { ScenId = 1, ScenName = "Inter-satellite 17.8-18.4 GHz" };
                sc.Frequencies.Add(new SrsFreqRange(1, I1.EmiRcp, I1.FMin, I1.FMax));
                int seq = 1;
                sc.PfdMaskLinks.Add(new SrsMaskLink(seq++, 6));
                AddShellPfdLinks(sc, ref seq, namedOverride: false);
                n.Scenarios.Add(sc);
                break;
            }
            case "BL-ALL":
            {
                // The D2 band's set is 26 here (the valid arrays-only twin); set 22, the
                // both-forms probe, belongs to BL-D2 alone -- CaseParams is the one list.
                Masks(1, 2, 3, 4, 5, 6, 7, 8, 9, 10); Params(CaseParams[caseName]);
                AddEarthStations(n);
                var sc1 = new SrsScenario { ScenId = 1, ScenName = "Classic + inter-satellite + gateway uplink" };
                sc1.Frequencies.Add(new SrsFreqRange(1, 'E', D2.FMin, D2.FMax));
                sc1.Frequencies.Add(new SrsFreqRange(2, 'R', U2.FMin, U2.FMax));
                int seq = 1;
                AddShellPfdLinks(sc1, ref seq, namedOverride: true);
                sc1.PfdMaskLinks.Add(new SrsMaskLink(seq++, 6));
                for (int g = 0; g < Gateways.Length; g++)
                    sc1.EsMaskLinks.Add(new SrsMaskLink(g + 1, 8 + g, EAsId: Gateways[g].EAsId));
                n.Scenarios.Add(sc1);

                var sc2 = new SrsScenario { ScenId = 2, ScenName = "Track duration + typical uplink" };
                sc2.Frequencies.Add(new SrsFreqRange(1, 'E', D1.FMin, D1.FMax));
                sc2.Frequencies.Add(new SrsFreqRange(2, 'R', U1.FMin, U1.FMax));
                sc2.PfdMaskLinks.Add(new SrsMaskLink(1, 1));
                sc2.EsMaskLinks.Add(new SrsMaskLink(1, 7, EAsId: -1));
                n.Scenarios.Add(sc2);
                break;
            }
            case "BL-R1":
            case "BL-R2":
            case "BL-R3":
            {
                // One probe mask, one set, the D1 band (design brief Sec. 3.9; ReadRuleProbes).
                int maskId = CaseMasks[caseName][0];
                Masks(maskId); Params(CaseParams[caseName]);
                var sc = new SrsScenario
                {
                    ScenId = 1,
                    ScenName = caseName switch
                    {
                        "BL-R1" => "Probe 3.9 nearest-read MIN_ELEV 19.7-20.2 GHz",
                        "BL-R2" => "Probe 3.9 interpolation MIN_EXCLUDE 19.7-20.2 GHz",
                        _ => "Probe 3.9 sweep grid MAX_CO_FREQ 19.7-20.2 GHz",
                    },
                };
                sc.Frequencies.Add(new SrsFreqRange(1, D1.EmiRcp, D1.FMin, D1.FMax));
                sc.PfdMaskLinks.Add(new SrsMaskLink(1, maskId));
                n.Scenarios.Add(sc);
                break;
            }
            case "BL-C1":
            {
                // Saturated masks per shell beside a set that declares shaping (design brief Sec. 3.10; ConsistencyProbe).
                Masks(14, 15, 16); Params(CaseParams[caseName]);
                var sc = new SrsScenario { ScenId = 1, ScenName = "Probe 3.10 consistency saturated masks 17.8-18.6" };
                sc.Frequencies.Add(new SrsFreqRange(1, D2.EmiRcp, D2.FMin, D2.FMax));
                int seq = 1;
                AddShellPfdLinks(sc, ref seq, namedOverride: false, maskA: 14, maskB: 15, maskC: 16);
                n.Scenarios.Add(sc);
                break;
            }
        }
        n.Validate();
        return n;
    }

    private static readonly Dictionary<string, int[]> CaseMasks = new()
    {
        ["BL-D1"] = new[] { 1 },
        ["BL-D2"] = new[] { 2, 3, 4, 5 },
        ["BL-U1"] = new[] { 7 },
        ["BL-U2"] = new[] { 8, 9, 10 },
        ["BL-I1"] = new[] { 2, 3, 4, 6 },
        ["BL-ALL"] = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
        ["BL-R1"] = new[] { 11 },
        ["BL-R2"] = new[] { 12 },
        ["BL-R3"] = new[] { 13 },
        ["BL-C1"] = new[] { 14, 15, 16 },
    };
    private static readonly Dictionary<string, int[]> CaseParams = new()
    {
        ["BL-D1"] = new[] { 21 },
        ["BL-D2"] = new[] { 22 },
        ["BL-U1"] = new[] { 23 },
        ["BL-U2"] = new[] { 24 },
        ["BL-I1"] = new[] { 25 },
        // BL-ALL reads the D2 band through the valid arrays-only set 26; set 22,
        // the both-forms probe, belongs to BL-D2 alone.
        ["BL-ALL"] = new[] { 21, 26, 23, 24 },
        ["BL-R1"] = new[] { 27 },
        ["BL-R2"] = new[] { 28 },
        ["BL-R3"] = new[] { 29 },
        ["BL-C1"] = new[] { 30 },
    };

    private static void PatchNtcId(string srcPath, string dstPath, int ntcId)
    {
        var d = new XmlDocument();
        d.Load(srcPath);
        d.DocumentElement.SetAttribute("ntc_id", ntcId.ToString(CultureInfo.InvariantCulture));
        d.Save(dstPath);
    }

    private static void BuildCase(string caseName, string srcDir, DatasetOptions o)
    {
        int ntc = NtcIdFor(caseName);
        string caseDir = Path.Combine(o.OutDir, caseName);
        string xmlDir = Path.Combine(caseDir, "xml");
        Directory.CreateDirectory(xmlDir);

        var contents = new List<SrsMdbWriter.MaskContent>();
        foreach (var d in MaskDefs.Where(d => CaseMasks[caseName].Contains(d.MaskId)))
        {
            string dst = Path.Combine(xmlDir, d.FileName);
            PatchNtcId(Path.Combine(srcDir, d.FileName), dst, ntc);
            contents.Add(new SrsMdbWriter.MaskContent(d.MaskId, dst, d.FMask, d.Band.FMin, d.Band.FMax));
        }
        foreach (int pid in CaseParams[caseName])
        {
            var b = AllBands.Single(x => x.ParamId == pid);
            string dst = Path.Combine(xmlDir, ParamFile(pid));
            PatchNtcId(Path.Combine(srcDir, ParamFile(pid)), dst, ntc);
            contents.Add(new SrsMdbWriter.MaskContent(pid, dst, 'R', b.FMin, b.FMax));
        }

        var notice = BuildNotice(caseName);
        SrsMdbWriter.WriteSrs(o.DonorSrsPath, Path.Combine(caseDir, $"{ntc} SRS.MDB"), notice);
        var stored = SrsMdbWriter.WriteMasks(o.DonorMasksPath, Path.Combine(caseDir, $"{ntc} Masks.MDB"),
            ntc, SatName, contents);
        var bad = stored.Where(r => r.Status != 0).ToList();
        if (bad.Count > 0)
            throw new InvalidOperationException($"{caseName}: mask store failed: " +
                string.Join(",", bad.Select(r => $"{r.MaskId}:{r.Status}")));

        string Exp(string file) => Path.Combine(caseDir, "expected", file);
        ServiceGeography GatewayGeo() => new(
            Gateways.Select((g, i) => new ServiceCell(i + 1, g.LatDeg, g.LonDeg)).ToList(), 500.0);
        var expected = new List<string>();
        var curves = new List<FamilyCurves.Entry>();
        long fullSteps = FullSteps(o), halfSteps = HalfSteps(o);
        string RowLabel(Band b) => LimitRowFor(o, b).Label;
        switch (caseName)
        {
            case "BL-D1":
            {
                var full = WriteDownExpectation(Exp("epfd_down_cdf.csv"), null, Set21(ntc), D1, o);
                var half = WriteDownExpectation(null, null, Set21(ntc), D1, o, halfSteps);
                curves.Add(new FamilyCurves.Entry("epfd(down), 19.7-20.2 GHz under set 21",
                    FamilyCurves.FromDown("truth", full, fullSteps), FamilyCurves.FromDown("truth 24 h", half, halfSteps),
                    null, null, FamilyCurves.TrackDurationNote, RowLabel(D1)));
                expected.Add("down");
                break;
            }
            case "BL-D2":
                // The invalid-filing probe: the expectation is the rejection, not a CDF.
                WriteRejectionExpectation(Exp("rejection.md"), Set22(ntc), D2);
                expected.Add("rejection");
                break;
            case "BL-U1":
            {
                var geo = ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, o.Quick ? 900.0 : 450.0);
                const string desc = "victim GSO sat lon=10, boresight lat=45 lon=0; typical ES = scheduled cells, ceiling 12 dBW range-controlled + S.1428 0.65 m";
                var full = WriteUpExpectation(Exp("epfd_up_cdf.csv"), Set23(ntc), U1, geo, 12.0, 28000.0, 0.65, desc, o);
                var half = WriteUpExpectation(null, Set23(ntc), U1, geo, 12.0, 28000.0, 0.65, desc, o, halfSteps);
                curves.Add(new FamilyCurves.Entry("epfd(up), 27.5-28.6 GHz under set 23",
                    FamilyCurves.FromUp("truth", full, fullSteps), FamilyCurves.FromUp("truth 24 h", half, halfSteps),
                    null, null, FamilyCurves.UpNote, null));
                expected.Add("up");
                break;
            }
            case "BL-U2":
            {
                const string desc = "victim GSO sat lon=10, boresight lat=45 lon=0; ES = the three declared gateways, ceiling 15 dBW range-controlled + S.1428 2.4 m";
                var full = WriteUpExpectation(Exp("epfd_up_cdf.csv"), Set24(ntc), U2, GatewayGeo(), 15.0, 29750.0, 2.4, desc, o);
                var half = WriteUpExpectation(null, Set24(ntc), U2, GatewayGeo(), 15.0, 29750.0, 2.4, desc, o, halfSteps);
                curves.Add(new FamilyCurves.Entry("epfd(up), 29.5-30.0 GHz under set 24",
                    FamilyCurves.FromUp("truth", full, fullSteps), FamilyCurves.FromUp("truth 24 h", half, halfSteps),
                    null, null, FamilyCurves.UpNote, null));
                expected.Add("up");
                break;
            }
            case "BL-I1":
            {
                // The IS statistic is a byproduct of the downlink emission run
                // over the same band: one snapshot stream, two accumulators.
                var full = WriteDownExpectation(Exp("epfd_down_cdf.csv"), Exp("epfd_is_cdf.csv"), Set25(ntc), I1, o);
                var half = WriteDownExpectation(null, null, Set25(ntc), I1, o, halfSteps, withIs: true);
                // Set 25 selects the classic algorithm, so the examination-read
                // curve exists: masks 2/3/4 per shell, the family's victim.
                var maskIds = new[] { 2, 3, 4 };
                var examFull = ExaminationCurve("examination", xmlDir, maskIds, Set25(ntc), I1, o, fullSteps);
                var examHalf = ExaminationCurve("examination 24 h", xmlDir, maskIds, Set25(ntc), I1, o, halfSteps);
                FamilyCurves.WriteExaminationCsv(Exp("epfd_down_examination_cdf.csv"), examFull, DownVictimDesc,
                    I1.FMin, I1.FMax, ExpStepSec, "masks 2/3/4 per shell under set 25 (classic algorithm)");
                curves.Add(new FamilyCurves.Entry("epfd(down), 17.8-18.4 GHz under set 25",
                    FamilyCurves.FromDown("truth", full, fullSteps), FamilyCurves.FromDown("truth 24 h", half, halfSteps),
                    examFull, examHalf, "", RowLabel(I1)));
                curves.Add(new FamilyCurves.Entry("epfd(is), 17.8-18.4 GHz under set 25 (byproduct of the same emission run)",
                    FamilyCurves.FromIs("truth", full, fullSteps), FamilyCurves.FromIs("truth 24 h", half, halfSteps),
                    null, null, FamilyCurves.IsNote, null));
                expected.Add("down"); expected.Add("is"); expected.Add("examination");
                break;
            }
            case "BL-ALL":
            {
                var fullD = WriteDownExpectation(Exp("epfd_down_cdf.csv"), null, Set21(ntc), D1, o);
                var halfD = WriteDownExpectation(null, null, Set21(ntc), D1, o, halfSteps);
                curves.Add(new FamilyCurves.Entry("epfd(down), 19.7-20.2 GHz under set 21",
                    FamilyCurves.FromDown("truth", fullD, fullSteps), FamilyCurves.FromDown("truth 24 h", halfD, halfSteps),
                    null, null, FamilyCurves.TrackDurationNote, RowLabel(D1)));
                var fullI = WriteDownExpectation(null, Exp("epfd_is_cdf.csv"), Set26(ntc), D2v, o);
                var halfI = WriteDownExpectation(null, null, Set26(ntc), D2v, o, halfSteps, withIs: true);
                curves.Add(new FamilyCurves.Entry("epfd(is), 17.8-18.6 GHz under set 26 (byproduct of the D2 emission run)",
                    FamilyCurves.FromIs("truth", fullI, fullSteps), FamilyCurves.FromIs("truth 24 h", halfI, halfSteps),
                    null, null, FamilyCurves.IsNote, null));
                var geo = ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, o.Quick ? 900.0 : 450.0);
                const string desc = "victim GSO sat lon=10, boresight lat=45 lon=0; typical ES = scheduled cells, ceiling 12 dBW range-controlled + S.1428 0.65 m";
                var fullU = WriteUpExpectation(Exp("epfd_up_cdf.csv"), Set23(ntc), U1, geo, 12.0, 28000.0, 0.65, desc, o);
                var halfU = WriteUpExpectation(null, Set23(ntc), U1, geo, 12.0, 28000.0, 0.65, desc, o, halfSteps);
                curves.Add(new FamilyCurves.Entry("epfd(up), 27.5-28.6 GHz under set 23",
                    FamilyCurves.FromUp("truth", fullU, fullSteps), FamilyCurves.FromUp("truth 24 h", halfU, halfSteps),
                    null, null, FamilyCurves.UpNote, null));
                expected.Add("down"); expected.Add("is"); expected.Add("up");
                break;
            }
            case "BL-R1":
            case "BL-R2":
            case "BL-R3":
            {
                // The read-rule probes: the expectation is the EXAMINATION's verdict at
                // named victims (or the resolved value), measured here against the
                // band's Article 22 row; see ReadRuleProbes.
                var lim = LimitRowFor(o, D1);
                string maskFile = Path.Combine(xmlDir, MaskDefs.Single(d => d.MaskId == CaseMasks[caseName][0]).FileName);
                string paramFile = Path.Combine(xmlDir, ParamFile(CaseParams[caseName][0]));
                string prov = Provenance.Line(o.Quick);
                var em = caseName switch
                {
                    "BL-R1" => ReadRuleProbes.EmitR1(caseDir, maskFile, paramFile, ReadRuleProbes.Set27(ntc), lim, o.Quick, prov),
                    "BL-R2" => ReadRuleProbes.EmitR2(caseDir, maskFile, paramFile, ReadRuleProbes.Set28(ntc), lim, o.Quick, prov),
                    _ => ReadRuleProbes.EmitR3(caseDir, maskFile, paramFile, ReadRuleProbes.Set29(ntc), lim, o.Quick, prov),
                };
                o.Log("    " + em.Headline);
                expected.Add("probe");
                break;
            }
            case "BL-C1":
            {
                // The consistency probe: the grades of the saturated masks against the
                // declared set, and the conservative verdict of the examination that
                // proceeds anyway, with the family's gated masks as the control.
                var lim = LimitRowFor(o, C1b);
                var shells = new[] { (Shell: ShellA, Name: "A"), (Shell: ShellB, Name: "B"), (Shell: ShellC, Name: "C") };
                var probeMasks = CaseMasks[caseName].Select((id, i) => new ConsistencyProbe.ProbeMask(
                    Path.Combine(xmlDir, MaskDefs.Single(d => d.MaskId == id).FileName), shells[i].Name, MaskAltitudeKm(shells[i].Shell), id)).ToList();
                var controlMasks = new[] { 2, 3, 4 }.Select((id, i) => new ConsistencyProbe.ProbeMask(
                    Path.Combine(srcDir, MaskDefs.Single(d => d.MaskId == id).FileName), shells[i].Name, MaskAltitudeKm(shells[i].Shell), id)).ToList();
                var em = ConsistencyProbe.Emit(caseDir, probeMasks, controlMasks, Path.Combine(xmlDir, ParamFile(30)),
                    ConsistencyProbe.Set30(ntc), lim, o.Quick, Provenance.Line(o.Quick));
                o.Log("    " + em.Headline);
                expected.Add("probe");
                break;
            }
        }
        if (curves.Count > 0)
        {
            FamilyCurves.WriteRecord(Exp("curves.md"), caseName, curves, ExpStepSec, o.Quick, Provenance.Line(o.Quick));
            expected.Add("curves");
        }
        File.WriteAllText(Path.Combine(caseDir, "README.md"), CaseReadme(caseName, ntc), Utf8NoBom);
        // The stamp goes last: it lists every other file of the triple by identity.
        Provenance.WriteCaseStamp(caseDir, caseName, ntc, o.Quick, string.Create(CultureInfo.InvariantCulture,
            $"truth curves {fullSteps} steps of {ExpStepSec:F0} s ({ExpStepSec * fullSteps / 3600.0:F0} h) with the {ExpStepSec * halfSteps / 3600.0:F0} h prefix as the extension pair; probe records state their own depth."));
        o.Log($"  {caseName}: SRS + Masks + README + stamp" +
              (expected.Count > 0 ? $" + expectation records ({string.Join("/", expected)})" : ""));
    }

    private static readonly Dictionary<int, ProbeExamination.LimitRow> _limitRows = new();

    /// <summary>The Article 22 row a probe band verdicts against, loaded once per band per run.</summary>
    private static ProbeExamination.LimitRow LimitRowFor(DatasetOptions o, Band band)
    {
        if (_limitRows.TryGetValue(band.ParamId, out var cached)) return cached;
        string db = ProbeExamination.ResolveLimitsDb(o.LimitsDbPath)
            ?? throw new InvalidOperationException("BR limits database (EPFD_limits_RES85_WRC23.mdb) not found; pass LimitsDbPath -- the probe cases verdict against real Article 22 rows");
        var row = ProbeExamination.LoadLimitRow(db, ResolveMasksDllDir(o), band.FMin, band.FMax, 40.0,
            Shells.Min(s => s.OperatingHeightKm ?? s.AltitudeKm));
        _limitRows[band.ParamId] = row;
        return row;
    }

    // ---- expectation data (simulated CDFs, sampling option 2) ----------

    private const double ExpStepSec = 30.0;
    private static double ExpSimDur(DatasetOptions o) => o.Quick ? 7200.0 : 172800.0;

    // Permissive limits: the deliverable is the CDF, not a verdict.
    private static List<radlimits.LimitPoint> PermissiveLimits() => new()
    {
        new() { EPFD = -300.0, Perc = 0.001 },
        new() { EPFD = 0.0, Perc = 100.0 },
    };

    /// <summary>
    /// The GSO satellite victim for epfd(up)/epfd(is): S.672-4 reference of
    /// Sec. D6.5.2 -- every dataset band is at or above 17 GHz, so beamwidth
    /// 1.55 deg with peak gain 40.7 dBi (Tables 8 and 16), Ls = -20.
    /// </summary>
    private static EpfdGsoSatVictim GsoSatVictim(double freqMhz) => new()
    {
        GsoLonDeg = 10.0, BoresightLatDeg = 45.0, BoresightLonDeg = 0.0,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APSREC408V01, freqMhz, null),
        GmaxDbi = 40.7, Phi3DbDeg = 1.55,
    };

    private static void WriteCdfCsv(string path, string label, string victimDesc, Band band,
        long steps, long quietSteps, double maxDb,
        radcompute1503_2.EpfdAccumulator acc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var (epfd, pct) = acc.BuildCdf();
        var sb = new StringBuilder();
        sb.AppendLine($"# {label} CDF -- simulated at the victim, S.1503-4 D7.1.2 bins (0.1 dB).");
        sb.AppendLine("# Sampling option 2 (design brief section 6): body percentiles only; the tail");
        sb.AppendLine("# is bounded by the mask envelope by construction.");
        sb.AppendLine(FormattableString.Invariant($"# band={band.FMin}-{band.FMax} MHz  {victimDesc}"));
        sb.AppendLine(FormattableString.Invariant(
            $"# step_s={ExpStepSec}  steps={steps}  quiet_steps={quietSteps}  max_epfd_db={maxDb:F3}"));
        sb.AppendLine("epfd_dbw_m2_40khz,percent_time_exceeded");
        // Trim to the informative support (one flanking bin each side); the
        // omitted bins are exactly 100 below and exactly 0 above.
        int first = Array.FindIndex(pct, p => p < 100.0);
        int last = Array.FindLastIndex(pct, p => p > 0.0);
        if (first < 0) { first = 0; last = pct.Length - 1; }
        first = Math.Max(0, first - 1);
        last = Math.Min(pct.Length - 1, last + 1);
        for (int i = first; i <= last; i++)
            sb.AppendLine(FormattableString.Invariant($"{epfd[i]:F1},{pct[i]:G9}"));
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    /// <summary>
    /// epfd(down) CDF under the band's declared set; when isPath is given the
    /// same run also yields the epfd(is) CDF at the GSO satellite victim --
    /// the byproduct coupling: one snapshot stream, two accumulators.
    /// </summary>
    private static EpfdDownResult WriteDownExpectation(string downPath, string isPath,
        OperatingParamsSet declared, Band band, DatasetOptions o, long? stepsOverride = null, bool withIs = false)
    {
        var con = new Constellation(Shells);
        // The horizon stays the full duration whatever the step count, so a
        // half-depth run is exactly the first half of the full one (the
        // extension pair): satellite states must not depend on the count.
        double simDur = ExpSimDur(o);
        long steps = stepsOverride ?? FullSteps(o);

        var vm = Vm(ShellA, band.FMin / 1000.0, 10.0, 8.0);
        var geo = ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, o.Quick ? 900.0 : 450.0);
        var pointing = new ScheduledPointing(con, geo, declared, vm, simDur);
        var ant = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, band.FMin, 0.6);
        var victim = new EpfdDownVictim { EsLatDeg = 45.0, EsLonDeg = 0.0, GsoLonDeg = 10.0, Antenna = ant };
        var isVictim = isPath is null && !withIs ? null : GsoSatVictim(band.FMin);

        var res = EpfdDown.Run(con, pointing, victim, ExpStepSec, steps, PermissiveLimits(),
            simDur, isVictim);
        if (downPath is not null)
            WriteCdfCsv(downPath, "epfd(down)", "victim ES lat=45 lon=0, GSO lon=10, S.1428 0.6 m",
                band, steps, res.QuietSteps, res.MaxEpfdDb, res.Accumulator);
        if (isPath is not null)
            WriteCdfCsv(isPath, "epfd(is)",
                "victim GSO sat lon=10, boresight lat=45 lon=0, S.672 40.7 dBi / 1.55 deg / Ls -20",
                band, steps, res.IsQuietSteps, res.MaxEpfdIsDb, res.IsAccumulator);
        return res;
    }

    /// <summary>The truth depth: 48 h at 30 s in the full profile, 2 h in quick.</summary>
    private static long FullSteps(DatasetOptions o) => (long)(ExpSimDur(o) / ExpStepSec);
    /// <summary>The extension pair's depth: the first half of the run.</summary>
    private static long HalfSteps(DatasetOptions o) => FullSteps(o) / 2;

    /// <summary>The family's epfd(down) victim description, as the CSV headers carry it.</summary>
    private const string DownVictimDesc = "victim ES lat=45 lon=0, GSO lon=10, S.1428 0.6 m";

    /// <summary>
    /// epfd(up) CDF: the transmitting ES are the scheduler's active links
    /// over the given service geography, radiating esPowerDbw through the
    /// declared-mask antenna family toward their serving satellites.
    /// </summary>
    private static EpfdUpResult WriteUpExpectation(string path, OperatingParamsSet declared, Band band,
        ServiceGeography geo, double esPowerDbw, double antFreqMhz, double antDiamM,
        string victimDesc, DatasetOptions o, long? stepsOverride = null)
    {
        var con = new Constellation(Shells);
        double simDur = ExpSimDur(o);
        long steps = stepsOverride ?? FullSteps(o);

        var vm = Vm(ShellA, band.FMin / 1000.0, 10.0, 8.0);
        var scheduler = new Scheduler(con, geo, declared, new ScenePointing(vm), simDur);
        var esModel = new EpfdUpEsModel
        {
            PowerDbw = esPowerDbw,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, antFreqMhz, antDiamM),
            // Range-based closed-loop power control: the declared ceiling
            // corresponds to the slant range at the band's declared minimum
            // elevation; each link transmits below it (constant flux at the
            // serving satellite). The masks still bound the ceiling.
            PowerControlRefElevDeg = declared.ElevAngleHeaderDeg ?? 10.0,
        };
        var res = EpfdUp.Run(con, scheduler, geo, GsoSatVictim(band.FMin), esModel,
            ExpStepSec, steps, PermissiveLimits(), simDur);
        if (path is not null)
            WriteCdfCsv(path, "epfd(up)", victimDesc, band, steps, res.QuietSteps, res.MaxEpfdDb,
                res.Accumulator);
        return res;
    }

    /// <summary>
    /// The examination-read epfd(down) curve of a case: Sec. D5.1.4.1 over the
    /// case's own pfd masks (the patched copies under xml/, one per shell when
    /// the notice links them per orbital-plane range) and its set, at the
    /// family's victim, the truth's step and horizon. Classic algorithm only.
    /// </summary>
    private static FamilyCurves.Curve ExaminationCurve(string label, string xmlDir, IReadOnlyList<int> maskIds,
        OperatingParamsSet declared, Band band, DatasetOptions o, long steps)
    {
        var con = new Constellation(Shells);
        var reads = maskIds.Select(id => (IPureMaskPfdRead)MaskFootprint.LoadFile(
            Path.Combine(xmlDir, MaskDefs.Single(d => d.MaskId == id).FileName))).ToList();
        IMaskPfdRead masks = reads.Count == 1 ? reads[0] : new ProbeExamination.ShellMaskRead(reads);
        return FamilyCurves.Examination(label, con, masks, declared, band.FMin, 0.6, 45.0, 0.0, 10.0,
            ExpStepSec, steps, ExpSimDur(o), PermissiveLimits());
    }

    // ---- documentation -------------------------------------------------

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static string CaseReadme(string caseName, int ntc)
    {
        string common = $"""
            System: {SatName} (ntc_id {ntc}). Three shells -- A: circular 1200 km / 55 deg,
            4x8, station-kept repeating track (Case 2, W_delta 0.5 deg); B: circular 900 km /
            87 deg, 6x6, free drift (Case 1, artificial precession); C: elliptical 800x4000 km /
            63.4 deg, 2x4, administration-supplied J2 precession (Case 3), operating height
            1000 km. Orbit rows 1-4 = shell A planes, 5-10 = B, 11-12 = C.

            Generated by tools/radians.beamlab.dataset. The masks are derived from the
            simulated payload (reachable-envelope projection); the e.i.r.p. values are
            synthetic commitments enveloped monotone. This case family is deliberately
            over-featured relative to a real filing -- it is a coverage vehicle, not a
            representative system.

            Every case is a frozen, version-stamped triple -- the notice, the masks and the
            expectation records: expected/provenance.md lists every artefact with its SHA-256
            and names the producer build, the time, the profile and the depth. Cases with a
            truth curve also carry expected/curves.md: each direction's 24 h / 48 h extension
            pair per percentile, and -- where this producer has the examination side, i.e.
            the classic downlink algorithm -- the examination-read curve beside the truth with
            the direction check (examination >= truth at every resolvable percentile).
            """;
        string body = caseName switch
        {
            "BL-D1" => """
                Activates: downlink 19.7-20.2 GHz, track-duration algorithm.
                - pfd mask 1, alpha/DeltaLongitude form, one mask for the whole constellation.
                - Operating-parameter set 21: per-latitude ARRAYS ONLY (no header scalars):
                  MIN_ELEV[lat][az], MAX_CO_FREQ[lat], MIN_DURATION[lat]; MIN_EXCLUDE declared as
                  no zone (one all-orbits row of 0). The family's 450 km cells leave a boresight
                  exclusion gate no trace in the envelope, so the declaration says what the masks
                  carry; a declared, varying zone is exercised by the probe cases instead.
                - expected/epfd_down_cdf.csv: simulated epfd(down) CDF under a scheduler that
                  honours the declared MIN_DURATION (dwell) and Nco bounds.
                """,
            "BL-D2" => """
                THE INVALID-FILING PROBE (design brief section 3.8). Downlink 17.8-18.6 GHz,
                classic algorithm with angular separation -- and an operating-parameter set
                that must be rejected.
                - pfd masks 2/3/4, azimuth/elevation form, one per shell (mask_lnk1 per orb_id),
                  plus mask 5 for the named satellite orb_id 1 / sat_orb_id 1 (a 3 dB tighter
                  payload commitment). The masks and the notice are well-formed; the valid
                  examination of these masks lives in BL-ALL under set 26.
                - Operating-parameter set 22: header scalars AND arrays with DIFFERENT values
                  (elev_angle 5 vs MIN_ELEV rows 10; max_co_freq 4 vs rows 2). Header and
                  array are mutually exclusive per quantity (EPS V43 6.7.2.2): a set carrying
                  both is an invalid filing, reported and not resolved. MIN_ANGLE_AT_ES =
                  2.5 deg set, MIN_DURATION absent, no exclusion zone declared.
                - expected/rejection.md: the expected outcome is a REJECTION naming the two
                  quantities. No epfd CDF is expected; a consumer that examines this set under
                  any precedence has failed the case.
                """,
            "BL-U1" => """
                Activates: uplink 27.5-28.6 GHz, typical earth stations.
                - ES e.i.r.p. mask 7, 2-D format "T" (eirp[lat][theta]), ES_ID = -1,
                  monotone by construction (S.1428 0.65 m pattern enveloped).
                - Operating-parameter set 23: HEADER SCALARS ONLY -- max_co_freq, elev_angle,
                  MAX_CO_FREQ_SAT = 2, MIN_ANGLE_AT_SAT = 1.5 deg, ES_DENSITY / ES_DISTANCE
                  active, ES latitude range -60..60.
                - expected/epfd_up_cdf.csv: simulated epfd(up) at the GSO satellite victim;
                  the transmitting ES are the scheduler's active links (each served cell
                  radiating toward its serving satellite with range-based power control
                  below the 12 dBW ceiling, S.1428 0.65 m), with the declared
                  MAX_CO_FREQ_SAT and MIN_ANGLE_AT_SAT gates applied.
                """,
            "BL-U2" => """
                Activates: uplink 29.5-30.0 GHz, specific declared earth stations.
                - e_as_stn: three specific gateways (5001 GW-NORTH, 5002 GW-SOUTH, 5003 GW-EAST)
                  with coordinates; mask_lnk2.e_as_id names them.
                - ES e.i.r.p. masks 8/9/10, 4-D format "A" (eirp[lat][az][el][DeltaLongES]),
                  ES_ID = the gateway's e_as_id. Stored via the container fallback (the BR
                  native store predates format "A"); the BR extractor reads them back.
                - Operating-parameter set 24: ES_DENSITY / ES_DISTANCE switched OFF (specific
                  stations), MAX_CO_FREQ_SAT = 1, MIN_ANGLE_AT_SAT = 2 deg.
                - expected/epfd_up_cdf.csv: simulated epfd(up) with the three gateways as the
                  transmitting population (15 dBW ceiling, range-controlled, S.1428 2.4 m). GW-EAST never sees the
                  GSO victim at 10E (below its horizon) and so contributes nothing -- that is
                  the truth the geometry implies, not an omission.
                """,
            "BL-I1" => """
                Activates: inter-satellite 17.8-18.4 GHz.
                - Satellite e.i.r.p. mask 6 (SAT_eirp[lat][theta], f_mask 'S', f_mask_type 'O'),
                  generated from the shell-A payload composite over the reachable headings;
                  linked through mask_lnk1 alongside the shell pfd masks, following the worked
                  127520101 pairing of P and S masks in one emission band.
                - Exercises the eq (3) / eq (4) phi split between artificial precession and
                  the time-step computation on shells B (derived) and C (declared).
                - Operating-parameter set 25 (minimal: header elev_angle 5 deg, no cap, no zone).
                - expected/epfd_down_cdf.csv and expected/epfd_is_cdf.csv from ONE emission
                  run: the epfd(is) statistic is a byproduct of the downlink simulation --
                  the same resolved beam sets composed toward the GSO satellite victim
                  (S.672 40.7 dBi / 1.55 deg / Ls -20, boresight 45N 0E), every
                  non-Earth-blocked space station contributing (D5.3.5 has no exclusion
                  gating).
                """,
            "BL-ALL" => """
                The full NEXT-style notice: all five bands, two scenarios.
                - Scenario 1 "Classic + inter-satellite + gateway uplink": E 17.8-18.6 GHz
                  (shell pfd masks 2/3/4, named-satellite mask 5, S mask 6) + R 29.5-30.0 GHz
                  (4-D gateway masks 8/9/10, e_as_stn) -- mixed direction.
                - Scenario 2 "Track duration + typical uplink": E 19.7-20.2 GHz (mask 1) +
                  R 27.5-28.6 GHz (mask 7) -- mixed direction, both downlink algorithms
                  across the notice.
                - Operating-parameter sets 21, 26, 23 and 24 via mask_lnk3 -- one form per
                  quantity in each (21 arrays-only, 23 header-only, 26 the D2 arrays-only set
                  with set 22's array values; set 22 itself, the both-forms probe, belongs to
                  BL-D2 alone).
                - expected/: all three directions -- epfd_down_cdf.csv (19.7-20.2 GHz under
                  set 21), epfd_up_cdf.csv (27.5-28.6 GHz under set 23), and
                  epfd_is_cdf.csv (17.8-18.6 GHz emission composed toward the GSO
                  satellite, byproduct of the downlink run under set 26).

                NOTE (S.1503-4 B5.1 tension): this notice mixes a repeating station-kept
                shell (A) with non-repeating shells (B, C). EPS V42 places f_stn_keep,
                rpt_prd_*, f_precess and keep_rnge per orbital plane (6.4.1.1) and therefore
                allows the mix; S.1503-4 B5.1 still says all sub-constellations must be
                repeating or all non-repeating. The EPS is authoritative for the database;
                the tension is deliberate specification pressure and a consumer should
                state which rule it applies.
                """,
            "BL-R1" => """
                READ-RULE PROBE, NEAREST ROW (design brief section 3.9). Downlink 19.7-20.2 GHz.
                - pfd mask 11, alpha/DeltaLongitude form: mask 1's construction with the declared
                  exclusion zone written into the alpha axis as a -1000 notch (8 deg) and the
                  payload 37.0 dB below mask 1's, so the BODY of the CDF sits at the limit and the
                  main-beam pass (which no read rule touches) does not decide the verdict.
                - Operating-parameter set 27: MIN_ELEV in two rows with different values (10 deg
                  at 20 N, 55 deg at 40 N); MAX_CO_FREQ 3 and MIN_EXCLUDE 8 deg as single rows.
                - expected/read-rule-probe.md: the verdicts at the victims 25 N and 35 N -- half a
                  10-degree sweep step either side of the midpoint between the rows -- under the
                  nearest-row read and the limit-curve verdict rule (they differ: FAIL at 25 N, PASS at 35 N), beside what
                  interpolation, a point read and the other row would give; the 24 h / 48 h
                  extension pair; the limit row; the artefacts' SHA-256. expected/
                  examination_lat25_cdf.csv and _lat35_: the examination CDFs under the correct read.
                """,
            "BL-R2" => """
                READ-RULE PROBE, LINEAR INTERPOLATION (design brief section 3.9). Downlink 19.7-20.2 GHz.
                - pfd mask 12: mask 1's construction with a 6 deg -1000 notch on the alpha axis (the
                  smaller row's value) and the payload 42.1 dB below mask 1's.
                - Operating-parameter set 28: all-orbits MIN_EXCLUDE in two rows (6 deg at 20 N,
                  14 deg at 40 N), so the interpolated values at 25/30/35 N are 8/10/12 deg and
                  differ from both rows; MIN_ELEV 10 deg and MAX_CO_FREQ 1 as single rows.
                - expected/read-rule-probe.md: the resolved exclusion angle a consumer must report at
                  each victim, and the MEASURED FINDING that the epfd(down) examination's verdict does
                  not discriminate this read on this family (every read within about 1 dB, all PASS)
                  -- the discriminator is the resolved value, and the record says which direction
                  would make it a verdict. expected/examination_lat25/30/35_cdf.csv under the
                  correct read.
                """,
            "BL-R3" => """
                SWEEP-GRID DISCLOSURE PROBE (design brief section 3.9). Downlink 19.7-20.2 GHz.
                - pfd mask 13: mask 1's construction with the 8 deg notch and the payload 43.2 dB
                  below mask 1's.
                - Operating-parameter set 29: MAX_CO_FREQ 8 at 65 N between rows of 1 at 62.5 N and
                  67.5 N -- under the nearest-row read the worst victim lies in 63.75-66.25 N,
                  between the 10-degree sweep points; MIN_ELEV 10 deg and MIN_EXCLUDE 8 deg as
                  single rows.
                - expected/sweep-grid-probe.md: the worst margin per sweep step (10, 5, 2, 1 deg)
                  with its latitude and the sweep verdict (compliant at 10 deg, exceeded finer);
                  expected/sweep_margins.csv: the examination at every tenth of a degree 70 S-70 N (every whole degree in quick generation), so
                  any grid that is a subset of the 1-degree grid can be looked up.
                """,
            "BL-C1" => """
                DECLARATION-CONSISTENCY PROBE (design brief section 3.10). Downlink 17.8-18.6 GHz.
                - pfd masks 14/15/16, azimuth/elevation form, one per shell (mask_lnk1 per orb_id):
                  the reachable envelope of the same payload composed with NO elevation floor (and,
                  like the family's own masks, no exclusion gate) -- full load, no victim avoidance
                  -- at a payload 45 dB below mask 1's.
                - Operating-parameter set 30 declares the shaping the masks ignore: all-orbits
                  MIN_EXCLUDE 8 deg, MIN_ELEV 10 deg, MAX_CO_FREQ 2, MIN_ANGLE_AT_ES 2.5 deg, one
                  row each. The pair is self-inconsistent by construction; nothing in it is
                  malformed.
                - expected/consistency-probe.md: the grade vocabulary of the mask-versus-parameters
                  consistency check and the expected grade SATURATED on the exclusion axis for
                  every mask (with the measured near-peak reach against the declared zone); the
                  elevation side of the inconsistency stated as real by construction but not
                  detectable by mask inspection on this family (near-peak grade and lit reach
                  read the same for the saturated masks and the gated control); the conservative
                  verdict of the examination that proceeds anyway at seven victims, per limit
                  point, with the family's own D2 masks (no exclusion gate, 10-degree floor) as
                  the control at the same payload; the 24 h / 48 h pair; artefact identities;
                  provenance. expected/sweep_margins.csv (all victims, points, control) and
                  expected/examination_lat40_cdf.csv.
                - The control's limit, stated in the record: the family declares no exclusion
                  zone (its 450 km cells leave a boresight gate no trace in the envelope), so the
                  control has no exclusion gate to isolate, and its 10-degree floor is not carried
                  as an edge either (both mask sets light the ground to the same lowest elevation).
                """,
            _ => "",
        };
        return $"# {caseName}\n\n{body}\n\n{common}\n";
    }

    private static void WriteTopReadme(DatasetOptions o)
    {
        string profile = o.Quick ? "QUICK (coarse grids, short runs -- structure verification only)" : "full";
        string text = $"""
            # BL-* S.1503-4 validation dataset

            Machine-generated examination input for S.1503-4 implementations, produced by
            `tools/radians.beamlab.dataset` from one simulated constellation. Each case
            directory holds an SNS v10 SRS database, a Masks database (BR container
            format), the mask XML sources under `xml/`, and under `expected/` the
            simulated CDFs for the case's directions -- epfd(down), epfd(up) and
            epfd(is) -- in the examination's own 0.1 dB bins.

            Generation profile of this copy: {profile}. Emission: {Provenance.Line(o.Quick)}{(o.OnlyCase is null ? "" : " -- this run emitted case " + o.OnlyCase + " only; every other case keeps the stamp in its own expected/provenance.md")}

            Each case is a frozen, version-stamped triple: the notice (SRS.MDB), the masks
            (Masks.MDB with the XML sources) and the expectation records. Its
            `expected/provenance.md` lists every artefact with its SHA-256 and the depth the
            curves were run at; a file whose hash differs is not this emission's. Cases with a
            truth curve carry `expected/curves.md` with the 24 h / 48 h extension pair per
            percentile and, for BL-I1 (the one downlink case whose set selects the classic
            algorithm), the examination-read curve beside the truth with the direction check.

            | Case | ntc_id | Focus |
            |---|---|---|
            | BL-D1 | 900123471 | track-duration downlink, alpha mask, arrays-only R set |
            | BL-D2 | 900123472 | the INVALID-FILING probe: az/el masks per shell + named satellite, R set filing two quantities in both forms -- expected outcome a rejection |
            | BL-U1 | 900123473 | typical-ES uplink, 2-D E mask, header-only R set |
            | BL-U2 | 900123474 | specific gateways, 4-D E masks, e_as_stn |
            | BL-I1 | 900123475 | inter-satellite S mask |
            | BL-ALL | 900123476 | everything in one notice, two mixed-direction scenarios; the D2 band under the valid arrays-only set 26 |
            | BL-R1 | 900123477 | read-rule probe: MIN_ELEV nearest row -- victims half a step either side of the midpoint between two rows, the two verdicts differ |
            | BL-R2 | 900123478 | read-rule probe: MIN_EXCLUDE linear interpolation -- the resolved value at 25/30/35 N; the verdict is measured not to discriminate |
            | BL-R3 | 900123479 | sweep-grid disclosure probe: the worst victim between the 10-degree sweep points; the worst margin stated per sweep step |
            | BL-C1 | 900123480 | declaration-consistency probe: saturated per-shell masks beside a set declaring an exclusion zone and an elevation floor -- expected grade SATURATED, and the conservative verdict of an examination that proceeds |

            Direction of comparison (design brief section 2): the examination result must
            sit AT OR ABOVE the simulated CDF at every percentile -- the masks are
            envelopes over the reachable configuration set, worst-case geometry bounds the
            victim, and the selection rules bound the interferer count. An examination
            below the expectation CDF is a defect; the gap above it is the measurable
            conservatism margin.

            The expectation CDFs use sampling option 2: body percentiles from a
            {(o.Quick ? "2-hour" : "48-hour")} run at 30 s steps, tail justified by the envelope
            argument. Measured caveat (margin-figure comb study, 2026-09-01): steps of
            30-60 s starve main-beam transients on this class of geometry, so expectation
            TAILS below each case's resolvable floor are not comparison material -- the
            acceptance rule applies at resolvable percentiles, with the tail direction
            still guaranteed by the envelope argument. If tail-level comparison is ever
            needed, regenerate the expectations at 6 s steps (measured sufficient; the
            per-run maxima moved ~20 dB from 60 s to 6 s and ~0.3 dB from 6 s to 1 s).
            Victims: epfd(down) an S.1428 60 cm earth station at 45N 0E against
            the GSO satellite at 10E; epfd(up) and epfd(is) the GSO satellite at 10E with
            its S.672-4 receive beam (40.7 dBi / 1.55 deg / Ls -20, Sec. D6.5.2 Table 16)
            pointed at 45N 0E. The scheduler honours the declared operating-parameter set
            of the case's band (dwell, Nco, exclusion, minimum elevation); the uplink run
            additionally applies the declared MAX_CO_FREQ_SAT and MIN_ANGLE_AT_SAT gates,
            and the epfd(is) statistic is the byproduct of the same emission run that
            produces epfd(down) in that band. Note the asymmetry the margin measures: the
            examination deploys representative uplink ES around the GSO boresight with
            NUM_ES aggregation (Sec. D5.2.5), while these expectations transmit from the
            actually scheduled cells.

            The probe cases are different in kind: their expectation is not a simulated CDF but
            the EXAMINATION's own verdict at named victims -- S.1503-4 D5.1.4.1 over the case's
            masks and set, against the Article 22 row of the band read from the BR limits
            database. The three read-rule probes (BL-R1/R2/R3, design brief section 3.9) are
            constructed so that how a per-latitude array is read (nearest row; linear
            interpolation for MIN_EXCLUDE) is the only thing that decides it; their masks are
            rule masks -- mask 1's construction with the exclusion zone written in as a -1000
            notch and the power lowered to the limit. The consistency probe (BL-C1, section
            3.10) pairs saturated per-shell masks with a set that declares shaping, and expects
            the inconsistency detected (grade SATURATED) before any verdict. Every probe record
            carries a 24 h / 48 h extension pair, the limit row, the artefacts' SHA-256
            identities and a provenance stamp.

            Regeneration requires the donor databases (schema source: worked case
            127520101), the BR native EpfdMasksApi64.dll, and for the probe cases the BR
            limits database (EPFD_limits_RES85_WRC23.mdb) with EpfdLimitsApi64.dll beside the
            masks DLL:

                dotnet run --project tools/radians.beamlab.dataset -- --out dataset

            Options: `--quick` (coarse), `--case BL-D1` (single case), `--donor-srs`,
            `--donor-masks`, `--dll-dir`, `--limits-db`, `--out`.

            The same tool builds a cross-read package -- a filed pfd mask delivered verbatim
            as raw XML, paired with a constellation from an orbit design and an R set this
            project derived (one form per quantity), the notice in the same SRS form and no
            copy of the gates in the SRS layer:

                dotnet run --project tools/radians.beamlab.dataset -- --package NAME --design D.orbitdesign.json --rset R.operparams.json --mask MASK.xml [--mask-id N] [--band MIN MAX] [--ntc N] [--sat-name S] [--expected FILE] [--provenance TEXT]

            The constellation mixes orbit models across shells (see BL-ALL/README.md for
            the S.1503-4 B5.1 vs EPS 6.4.1.1 tension, which is deliberate). The system is
            deliberately over-featured: it is a coverage vehicle for parser and algorithm
            validation, not a representative filing.
            """;
        File.WriteAllText(Path.Combine(o.OutDir, "README.md"), text, Utf8NoBom);
    }
}
