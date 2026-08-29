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
    public static readonly string[] CaseNames = { "BL-D1", "BL-D2", "BL-U1", "BL-U2", "BL-I1", "BL-ALL" };
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
        // Standard J2 nodal regression rate, declared by the administration.
        double n = Math.Sqrt(OrbitalConstants.MuEarth / (a * a * a));
        double pSemiLatus = a * (1.0 - e * e);
        double incRad = 63.4 * Math.PI / 180.0;
        double raanRateRadS = -1.5 * n * OrbitalConstants.J2
            * Math.Pow(OrbitalConstants.EarthRadiusKm / pSemiLatus, 2.0) * Math.Cos(incRad);
        return new ConstellationShell
        {
            AltitudeKm = a - OrbitalConstants.EarthRadiusKm, Eccentricity = e,
            InclinationDeg = 63.4, ArgumentOfPerigeeDeg = 270.0,
            PlaneCount = 2, SatsPerPlane = 4, WalkerPhasingF = 1,
            OperatingHeightKm = 1000.0,
            PrecessionSupplied = true, PrecessionRateDegPerSec = raanRateRadS * 180.0 / Math.PI,
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
    private static readonly Band U1 = new("U1", 'R', 27500, 28600, 23);
    private static readonly Band U2 = new("U2", 'R', 29500, 30000, 24);
    private static readonly Band I1 = new("I1", 'E', 17800, 18400, 25);

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

    public static void Generate(DatasetOptions o)
    {
        if (!File.Exists(o.DonorSrsPath))
            throw new InvalidOperationException($"donor SRS not found: {o.DonorSrsPath}");
        if (!File.Exists(o.DonorMasksPath))
            throw new InvalidOperationException($"donor Masks not found: {o.DonorMasksPath}");
        string dllDir = o.EpfdMasksDllDir ?? new[]
        {
            @"C:\Projects\_EPFD\radians\radians\dlls",
            @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
        }.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdMasksApi64.dll")));
        if (dllDir is null || !File.Exists(Path.Combine(dllDir, "EpfdMasksApi64.dll")))
            throw new InvalidOperationException("EpfdMasksApi64.dll not found; pass EpfdMasksDllDir");
        SrsMdbWriter.EpfdMasksDllDirectory = dllDir;

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

    private static void GeneratePfd(string path, IReadOnlyList<(ConstellationShell Shell, double TxDeltaDb)> shells,
        Band band, int maskId, MaskPlotKind kind, double alphaExcl, double minElev, bool quick)
    {
        double latCap = shells.Max(s => MaskXmlExport.MaxLatitudeForInclination(s.Shell.InclinationDeg));
        var opts = new MaskXmlExportOptions
        {
            SatName = SatName, NtcId = 0, MaskId = maskId,
            LowFreqMhz = band.FMin, HighFreqMhz = band.FMax, RefBwKHz = 40,
            LatMinDeg = -Math.Min(70.0, latCap), LatMaxDeg = Math.Min(70.0, latCap),
            LatStepDeg = quick ? 35 : 10,
            BStepDeg = quick ? 30 : 5, CStepDeg = quick ? 60 : 10,
            Kind = kind, Format = MaskExportFormat.Xml, OutputPath = path,
        };
        var samplers = shells
            .Select(s => (IPfdMaskSampler)new ReachableEnvelopeSampler(
                Vm(s.Shell, band.FMin / 1000.0, minElev, alphaExcl, s.TxDeltaDb), opts, s.Shell.InclinationDeg))
            .ToArray();
        IPfdMaskSampler sampler = samplers.Length == 1 ? samplers[0] : new MaxOfSamplers(samplers);
        MaskXmlExport.GenerateAsync(sampler, opts, null, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

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

    private static void AddMinExcludeAllOrbits(OperatingParamsSet s)
    {
        for (int orb = OrbA0; orb < OrbB0; orb++)
            s.MinExclude.Add(new MinExcludeByOrbit { OrbId = orb, ByLat = { (-70.0, 6.0), (0.0, 8.0), (70.0, 6.0) } });
        for (int orb = OrbB0; orb < OrbC0; orb++)
            s.MinExclude.Add(new MinExcludeByOrbit { OrbId = orb, ByLat = { (-70.0, 8.0), (0.0, 10.0), (70.0, 8.0) } });
        for (int orb = OrbC0; orb <= OrbLast; orb++)
            s.MinExclude.Add(new MinExcludeByOrbit { OrbId = orb, ByLat = { (0.0, 8.0) } });
    }

    /// <summary>D1: per-latitude arrays only (no header scalars) -- track-duration algorithm.</summary>
    public static OperatingParamsSet Set21(int ntcId)
    {
        var s = new OperatingParamsSet
        {
            SatName = SatName, NtcId = ntcId, ParamId = D1.ParamId,
            LowFreqMhz = D1.FMin, HighFreqMhz = D1.FMax,
            EsDensityPerKm2 = 0.00012, EsDistanceKm = 300, EsLatMinDeg = -70, EsLatMaxDeg = 70,
        };
        AddMinExcludeAllOrbits(s);
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
    /// D2: header scalars AND arrays with deliberately different values, so
    /// the array-prevails resolution (EPS 6.7.2.2) is load-bearing. Classic
    /// algorithm: MIN_ANGLE_AT_ES set, MIN_DURATION absent (mutually
    /// exclusive).
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
        AddMinExcludeAllOrbits(s);
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
        s.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 10.0) } });
        return s;
    }

    public static OperatingParamsSet SetFor(int paramId, int ntcId) => paramId switch
    {
        21 => Set21(ntcId), 22 => Set22(ntcId), 23 => Set23(ntcId),
        24 => Set24(ntcId), 25 => Set25(ntcId),
        _ => throw new ArgumentOutOfRangeException(nameof(paramId)),
    };

    private static void BuildMaskSources(string srcDir, DatasetOptions o)
    {
        string P(string f) => Path.Combine(srcDir, f);
        // Declared operating constraints feed the reachable envelope: the
        // exclusion ring and minimum elevation below bound the mask above.
        GeneratePfd(P(MaskDefs[0].FileName),
            new[] { (ShellA, 0.0), (ShellB, 0.0), (ShellC, 0.0) }, D1, 1,
            MaskPlotKind.AlphaDeltaLong, alphaExcl: 8.0, minElev: 10.0, o.Quick);
        o.Log("  mask 1 (alpha, all shells) done");
        GeneratePfd(P(MaskDefs[1].FileName), new[] { (ShellA, 0.0) }, D2, 2,
            MaskPlotKind.AzEl, 8.0, 10.0, o.Quick);
        GeneratePfd(P(MaskDefs[2].FileName), new[] { (ShellB, 0.0) }, D2, 3,
            MaskPlotKind.AzEl, 10.0, 10.0, o.Quick);
        GeneratePfd(P(MaskDefs[3].FileName), new[] { (ShellC, 0.0) }, D2, 4,
            MaskPlotKind.AzEl, 8.0, 10.0, o.Quick);
        // Named-satellite override: the first satellite of plane 1 commits
        // to a 3 dB tighter payload (mask_lnk1 granularity sat_orb_id).
        GeneratePfd(P(MaskDefs[4].FileName), new[] { (ShellA, -3.0) }, D2, 5,
            MaskPlotKind.AzEl, 8.0, 10.0, o.Quick);
        o.Log("  masks 2-5 (az/el per shell + named satellite) done");
        GenerateSs(P(MaskDefs[5].FileName), o.Quick);
        GenerateEs2D(P(MaskDefs[6].FileName), o.Quick);
        for (int g = 0; g < Gateways.Length; g++)
            GenerateEs4D(P(MaskDefs[7 + g].FileName), 8 + g, Gateways[g], o.Quick);
        o.Log("  masks 6-10 (S, ES 2-D, ES 4-D x3) done");
        foreach (int pid in new[] { 21, 22, 23, 24, 25 })
            OperParamsXmlWriter.Write(P(ParamFile(pid)), SetFor(pid, 0));
        o.Log("  operating-parameter sets 21-25 done");
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

    private static void AddShellPfdLinks(SrsScenario sc, ref int seq, bool namedOverride)
    {
        if (namedOverride)
            sc.PfdMaskLinks.Add(new SrsMaskLink(seq++, MaskId: 5, OrbId: OrbA0, SatOrbId: 1));
        for (int orb = OrbA0; orb < OrbB0; orb++) sc.PfdMaskLinks.Add(new SrsMaskLink(seq++, 2, orb));
        for (int orb = OrbB0; orb < OrbC0; orb++) sc.PfdMaskLinks.Add(new SrsMaskLink(seq++, 3, orb));
        for (int orb = OrbC0; orb <= OrbLast; orb++) sc.PfdMaskLinks.Add(new SrsMaskLink(seq++, 4, orb));
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
                var b = new[] { D1, D2, U1, U2, I1 }.Single(x => x.ParamId == pid);
                n.MaskInfo.Add(new SrsMaskInfo(pid, b.FMin, b.FMax, 'R', null));
                n.OperatingParamIds.Add(pid);
            }
        }

        switch (caseName)
        {
            case "BL-D1":
            {
                Masks(1); Params(21);
                var sc = new SrsScenario { ScenId = 1, ScenName = "Track duration downlink 19.7-20.2 GHz" };
                sc.Frequencies.Add(new SrsFreqRange(1, D1.EmiRcp, D1.FMin, D1.FMax));
                sc.PfdMaskLinks.Add(new SrsMaskLink(1, 1));
                n.Scenarios.Add(sc);
                break;
            }
            case "BL-D2":
            {
                Masks(2, 3, 4, 5); Params(22);
                var sc = new SrsScenario { ScenId = 1, ScenName = "Classic downlink 17.8-18.6 GHz angular separation" };
                sc.Frequencies.Add(new SrsFreqRange(1, D2.EmiRcp, D2.FMin, D2.FMax));
                int seq = 1;
                AddShellPfdLinks(sc, ref seq, namedOverride: true);
                n.Scenarios.Add(sc);
                break;
            }
            case "BL-U1":
            {
                Masks(7); Params(23);
                var sc = new SrsScenario { ScenId = 1, ScenName = "Typical uplink 27.5-28.6 GHz" };
                sc.Frequencies.Add(new SrsFreqRange(1, U1.EmiRcp, U1.FMin, U1.FMax));
                sc.EsMaskLinks.Add(new SrsMaskLink(1, 7, EAsId: -1));
                n.Scenarios.Add(sc);
                break;
            }
            case "BL-U2":
            {
                Masks(8, 9, 10); Params(24);
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
                Masks(2, 3, 4, 6); Params(25);
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
                Masks(1, 2, 3, 4, 5, 6, 7, 8, 9, 10); Params(21, 22, 23, 24);
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
    };
    private static readonly Dictionary<string, int[]> CaseParams = new()
    {
        ["BL-D1"] = new[] { 21 },
        ["BL-D2"] = new[] { 22 },
        ["BL-U1"] = new[] { 23 },
        ["BL-U2"] = new[] { 24 },
        ["BL-I1"] = new[] { 25 },
        ["BL-ALL"] = new[] { 21, 22, 23, 24 },
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
            var b = new[] { D1, D2, U1, U2, I1 }.Single(x => x.ParamId == pid);
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
        switch (caseName)
        {
            case "BL-D1":
                WriteDownExpectation(Exp("epfd_down_cdf.csv"), null, Set21(ntc), D1, o);
                expected.Add("down");
                break;
            case "BL-D2":
                WriteDownExpectation(Exp("epfd_down_cdf.csv"), null, Set22(ntc), D2, o);
                expected.Add("down");
                break;
            case "BL-U1":
                WriteUpExpectation(Exp("epfd_up_cdf.csv"), Set23(ntc), U1,
                    ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, o.Quick ? 900.0 : 450.0),
                    esPowerDbw: 12.0, antFreqMhz: 28000.0, antDiamM: 0.65,
                    "victim GSO sat lon=10, boresight lat=45 lon=0; typical ES = scheduled cells, ceiling 12 dBW range-controlled + S.1428 0.65 m", o);
                expected.Add("up");
                break;
            case "BL-U2":
                WriteUpExpectation(Exp("epfd_up_cdf.csv"), Set24(ntc), U2, GatewayGeo(),
                    esPowerDbw: 15.0, antFreqMhz: 29750.0, antDiamM: 2.4,
                    "victim GSO sat lon=10, boresight lat=45 lon=0; ES = the three declared gateways, ceiling 15 dBW range-controlled + S.1428 2.4 m", o);
                expected.Add("up");
                break;
            case "BL-I1":
                // The IS statistic is a byproduct of the downlink emission run
                // over the same band: one snapshot stream, two accumulators.
                WriteDownExpectation(Exp("epfd_down_cdf.csv"), Exp("epfd_is_cdf.csv"),
                    Set25(ntc), I1, o);
                expected.Add("down"); expected.Add("is");
                break;
            case "BL-ALL":
                WriteDownExpectation(Exp("epfd_down_cdf.csv"), null, Set21(ntc), D1, o);
                WriteDownExpectation(null, Exp("epfd_is_cdf.csv"), Set22(ntc), D2, o);
                WriteUpExpectation(Exp("epfd_up_cdf.csv"), Set23(ntc), U1,
                    ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, o.Quick ? 900.0 : 450.0),
                    esPowerDbw: 12.0, antFreqMhz: 28000.0, antDiamM: 0.65,
                    "victim GSO sat lon=10, boresight lat=45 lon=0; typical ES = scheduled cells, ceiling 12 dBW range-controlled + S.1428 0.65 m", o);
                expected.Add("down"); expected.Add("is"); expected.Add("up");
                break;
        }
        File.WriteAllText(Path.Combine(caseDir, "README.md"), CaseReadme(caseName, ntc), Utf8NoBom);
        o.Log($"  {caseName}: SRS + Masks + README" +
              (expected.Count > 0 ? $" + expectation CDFs ({string.Join("/", expected)})" : ""));
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
    private static void WriteDownExpectation(string downPath, string isPath,
        OperatingParamsSet declared, Band band, DatasetOptions o)
    {
        var con = new Constellation(Shells);
        double simDur = ExpSimDur(o);
        long steps = (long)(simDur / ExpStepSec);

        var vm = Vm(ShellA, band.FMin / 1000.0, 10.0, 8.0);
        var geo = ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, o.Quick ? 900.0 : 450.0);
        var pointing = new ScheduledPointing(con, geo, declared, vm, simDur);
        var ant = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, band.FMin, 0.6);
        var victim = new EpfdDownVictim { EsLatDeg = 45.0, EsLonDeg = 0.0, GsoLonDeg = 10.0, Antenna = ant };
        var isVictim = isPath is null ? null : GsoSatVictim(band.FMin);

        var res = EpfdDown.Run(con, pointing, victim, ExpStepSec, steps, PermissiveLimits(),
            simDur, isVictim);
        if (downPath is not null)
            WriteCdfCsv(downPath, "epfd(down)", "victim ES lat=45 lon=0, GSO lon=10, S.1428 0.6 m",
                band, steps, res.QuietSteps, res.MaxEpfdDb, res.Accumulator);
        if (isPath is not null)
            WriteCdfCsv(isPath, "epfd(is)",
                "victim GSO sat lon=10, boresight lat=45 lon=0, S.672 40.7 dBi / 1.55 deg / Ls -20",
                band, steps, res.IsQuietSteps, res.MaxEpfdIsDb, res.IsAccumulator);
    }

    /// <summary>
    /// epfd(up) CDF: the transmitting ES are the scheduler's active links
    /// over the given service geography, radiating esPowerDbw through the
    /// declared-mask antenna family toward their serving satellites.
    /// </summary>
    private static void WriteUpExpectation(string path, OperatingParamsSet declared, Band band,
        ServiceGeography geo, double esPowerDbw, double antFreqMhz, double antDiamM,
        string victimDesc, DatasetOptions o)
    {
        var con = new Constellation(Shells);
        double simDur = ExpSimDur(o);
        long steps = (long)(simDur / ExpStepSec);

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
        WriteCdfCsv(path, "epfd(up)", victimDesc, band, steps, res.QuietSteps, res.MaxEpfdDb,
            res.Accumulator);
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
            """;
        string body = caseName switch
        {
            "BL-D1" => """
                Activates: downlink 19.7-20.2 GHz, track-duration algorithm.
                - pfd mask 1, alpha/DeltaLongitude form, one mask for the whole constellation.
                - Operating-parameter set 21: per-latitude ARRAYS ONLY (no header scalars):
                  MIN_EXCLUDE varying by latitude and by orb_id (a value for every plane),
                  MIN_ELEV[lat][az], MAX_CO_FREQ[lat], MIN_DURATION[lat].
                - expected/epfd_down_cdf.csv: simulated epfd(down) CDF under a scheduler that
                  honours the declared MIN_DURATION (dwell) and Nco bounds.
                """,
            "BL-D2" => """
                Activates: downlink 17.8-18.6 GHz, classic algorithm with angular separation.
                - pfd masks 2/3/4, azimuth/elevation form, one per shell (mask_lnk1 per orb_id),
                  plus mask 5 for the named satellite orb_id 1 / sat_orb_id 1 (a 3 dB tighter
                  payload commitment) -- the most-specific-link-prevails granularity case.
                - Operating-parameter set 22: header scalars AND arrays with DIFFERENT values
                  (elev_angle 5 vs MIN_ELEV rows 10; max_co_freq 4 vs rows 2): EPS 6.7.2.2
                  array-prevails resolution is load-bearing. MIN_ANGLE_AT_ES = 2.5 deg set,
                  MIN_DURATION absent (the two are mutually exclusive).
                - expected/epfd_down_cdf.csv as in BL-D1, under set 22.
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
                - Operating-parameter set 25 (minimal, header elev_angle only).
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
                - Operating-parameter sets 21-24 via mask_lnk3.
                - expected/: all three directions -- epfd_down_cdf.csv (19.7-20.2 GHz under
                  set 21), epfd_up_cdf.csv (27.5-28.6 GHz under set 23), and
                  epfd_is_cdf.csv (17.8-18.6 GHz emission composed toward the GSO
                  satellite, byproduct of the downlink run under set 22).

                NOTE (S.1503-4 B5.1 tension): this notice mixes a repeating station-kept
                shell (A) with non-repeating shells (B, C). EPS V42 places f_stn_keep,
                rpt_prd_*, f_precess and keep_rnge per orbital plane (6.4.1.1) and therefore
                allows the mix; S.1503-4 B5.1 still says all sub-constellations must be
                repeating or all non-repeating. The EPS is authoritative for the database;
                the tension is deliberate specification pressure and a consumer should
                state which rule it applies.
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

            Generation profile of this copy: {profile}.

            | Case | ntc_id | Focus |
            |---|---|---|
            | BL-D1 | 900123471 | track-duration downlink, alpha mask, arrays-only R set |
            | BL-D2 | 900123472 | classic downlink, az/el masks per shell + named satellite, header-vs-array R set |
            | BL-U1 | 900123473 | typical-ES uplink, 2-D E mask, header-only R set |
            | BL-U2 | 900123474 | specific gateways, 4-D E masks, e_as_stn |
            | BL-I1 | 900123475 | inter-satellite S mask |
            | BL-ALL | 900123476 | everything in one notice, two mixed-direction scenarios |

            Direction of comparison (design brief section 2): the examination result must
            sit AT OR ABOVE the simulated CDF at every percentile -- the masks are
            envelopes over the reachable configuration set, worst-case geometry bounds the
            victim, and the selection rules bound the interferer count. An examination
            below the expectation CDF is a defect; the gap above it is the measurable
            conservatism margin.

            The expectation CDFs use sampling option 2: body percentiles from a
            {(o.Quick ? "2-hour" : "48-hour")} run at 30 s steps, tail justified by the envelope
            argument. Victims: epfd(down) an S.1428 60 cm earth station at 45N 0E against
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

            Regeneration requires the donor databases (schema source: worked case
            127520101) and the BR native EpfdMasksApi64.dll:

                dotnet run --project tools/radians.beamlab.dataset -- --out dataset

            Options: `--quick` (coarse), `--case BL-D1` (single case), `--donor-srs`,
            `--donor-masks`, `--dll-dir`, `--out`.

            The constellation mixes orbit models across shells (see BL-ALL/README.md for
            the S.1503-4 B5.1 vs EPS 6.4.1.1 tension, which is deliberate). The system is
            deliberately over-featured: it is a coverage vehicle for parser and algorithm
            validation, not a representative filing.
            """;
        File.WriteAllText(Path.Combine(o.OutDir, "README.md"), text, Utf8NoBom);
    }
}
