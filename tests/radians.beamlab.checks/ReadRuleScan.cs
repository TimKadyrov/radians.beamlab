using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using radians.beamlab;
using radians.beamlab.app;
using radians.beamlab.dataset;

namespace radians.beamlab.checks;

/// <summary>
/// Measurement scan behind the section 3.9 read-rule probes: how much the
/// examination's verdict at a victim moves when ONE declared quantity is
/// pinned to each candidate value, on the BL constellation against a probe
/// mask. The probes' rows, values and power offsets are chosen from these
/// tables so that the read rule alone decides the verdict; the generator then
/// reproduces the quoted numbers at emission.
///
/// Run:  -- probescan [key=value ...]
///   q=Nco|MinElev|Alpha      the quantity scanned (default Nco)
///   vals=1,2,4,8             its candidate values
///   lats=25,30,35            victim latitudes (ES lon 0, GSO lon 10)
///   nco=3 elev=10 alpha=8    the pinned values of the quantities not scanned
///   gate=8 minel=10 notch=0 bstep=2 tx=0   the probe mask's construction
///   step=30 steps=5760       depth (default 48 h); pair=1 adds the half-depth run
///   gso=10                   longitude of the wanted GSO satellite (ES at lon 0)
/// </summary>
internal static class ReadRuleScan
{
    public static int Run(string[] a)
    {
        var inv = CultureInfo.InvariantCulture;
        var kv = a.Where(x => x.Contains('=')).ToDictionary(x => x.Split('=')[0].ToLowerInvariant(), x => x.Split('=', 2)[1]);
        string S(string k, string d) => kv.TryGetValue(k, out var v) ? v : d;
        double D(string k, double d) => kv.TryGetValue(k, out var v) ? double.Parse(v, inv) : d;
        double[] List(string k, string d) => S(k, d).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, inv)).ToArray();

        string q = S("q", "Nco");
        double[] vals = List("vals", q == "Nco" ? "1,2,4,8" : q == "MinElev" ? "10,20,30,40" : "6,8,10,12,14");
        double[] lats = List("lats", "25,30,35");
        int ncoFix = (int)D("nco", 3); double elevFix = D("elev", 10.0), alphaFix = D("alpha", 8.0);
        var spec = new DatasetGenerator.ProbeMaskSpec(D("gate", 8.0), D("minel", 10.0), D("tx", 0.0), D("notch", 0.0), D("bstep", 2.0));
        double stepSec = D("step", 30.0);
        long steps = (long)D("steps", 5760);
        bool pair = D("pair", 0) > 0;
        double gsoLon = D("gso", 10.0);

        string dllDir = new[]
        {
            @"C:\Projects\_EPFD\radians\radians\dlls",
            @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
        }.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdLimitsApi64.dll")));
        string limitsDb = ProbeExamination.ResolveLimitsDb(null);
        if (dllDir is null || limitsDb is null) { Console.WriteLine("ABORT: BR limits database or EpfdLimitsApi64.dll not present."); return 2; }

        var (fMin, fMax) = DatasetGenerator.ProbeBandMhz;
        var lim = ProbeExamination.LoadLimitRow(limitsDb, dllDir, fMin, fMax, 40.0, 900.0);
        Console.WriteLine("limit row: " + lim.Label);
        Console.WriteLine(string.Create(inv, $"points: {string.Join("  ", lim.Points.Select(p => $"{p.EPFD:F1}@{p.Perc:G4}%"))}"));

        string outDir = Path.Combine(AppContext.BaseDirectory, "exp", "probescan");
        Directory.CreateDirectory(outDir);
        string maskPath = Path.Combine(outDir, string.Create(inv,
            $"probe_gate{spec.GateAlphaDeg:F0}_minel{spec.MinElevDeg:F0}_notch{spec.NotchAlphaDeg:F0}_b{spec.BStepDeg:F0}_tx{spec.TxDeltaDb:F0}.xml"));
        var sw = Stopwatch.StartNew();
        if (!File.Exists(maskPath))
        {
            DatasetGenerator.GenerateProbeMask(maskPath, 11, spec, quick: false);
            Console.WriteLine(string.Create(inv, $"probe mask generated in {sw.Elapsed.TotalSeconds:F0} s: {Path.GetFileName(maskPath)}"));
        }
        else Console.WriteLine("probe mask reused: " + Path.GetFileName(maskPath));
        var mask = MaskFootprint.LoadFile(maskPath);
        var con = new Constellation(DatasetGenerator.Shells);
        double freqMhz = 0.5 * (fMin + fMax);

        OperatingParamsSet Pinned(int nco, double elev, double alpha)
        {
            var s = new OperatingParamsSet { SatName = DatasetGenerator.SatName, NtcId = 0, ParamId = 27, LowFreqMhz = fMin, HighFreqMhz = fMax };
            return ProbeExamination.WithAlpha(ProbeExamination.WithMinElev(ProbeExamination.WithNco(s, nco), elev), alpha);
        }
        Console.WriteLine(string.Create(inv,
            $"scan {q} over {string.Join(",", vals.Select(v => v.ToString(inv)))}; pinned nco={ncoFix} elev={elevFix} alpha={alphaFix}; " +
            $"depth {stepSec:F0} s x {steps} = {stepSec * steps / 3600.0:F1} h{(pair ? " (+ half-depth pair)" : "")}; victims {string.Join(" ", lats.Select(v => v.ToString(inv)))}; GSO lon {gsoLon:F0}"));
        Console.WriteLine();
        Console.WriteLine(pair
            ? "quantity   value   lat   max_epfd   worst_margin  verdict   quiet | half: max_epfd  worst_margin  verdict   move"
            : "quantity   value   lat   max_epfd   worst_margin  verdict   quiet   s");

        foreach (double val in vals)
        {
            var set = q switch
            {
                "Nco" => Pinned((int)val, elevFix, alphaFix),
                "MinElev" => Pinned(ncoFix, val, alphaFix),
                "Alpha" => Pinned(ncoFix, elevFix, val),
                _ => throw new ArgumentException("q must be Nco, MinElev or Alpha"),
            };
            foreach (double lat in lats)
            {
                var t = Stopwatch.StartNew();
                var v = ProbeExamination.Examine(con, mask, set, lim, freqMhz, lat, 0.0, gsoLon, stepSec, steps);
                if (!pair)
                {
                    Console.WriteLine(string.Create(inv,
                        $"{q,-9}  {val,5:G4}  {lat,4:F0}   {v.MaxEpfdDb,8:F2}   {v.WorstMarginDb,12:+0.00;-0.00;0.00}   {(v.Pass ? "PASS" : "FAIL")}   {v.QuietSteps,5}   {t.Elapsed.TotalSeconds,4:F0}"));
                }
                else
                {
                    var h = ProbeExamination.Examine(con, mask, set, lim, freqMhz, lat, 0.0, gsoLon, stepSec, steps / 2);
                    Console.WriteLine(string.Create(inv,
                        $"{q,-9}  {val,5:G4}  {lat,4:F0}   {v.MaxEpfdDb,8:F2}   {v.WorstMarginDb,12:+0.00;-0.00;0.00}   {(v.Pass ? "PASS" : "FAIL")}   {v.QuietSteps,5} | " +
                        $"{h.MaxEpfdDb,8:F2}   {h.WorstMarginDb,12:+0.00;-0.00;0.00}   {(h.Pass ? "PASS" : "FAIL")}   {v.WorstMarginDb - h.WorstMarginDb,6:+0.00;-0.00;0.00}"));
                }
            }
        }
        Console.WriteLine(string.Create(inv, $"\nscan done in {sw.Elapsed.TotalMinutes:F1} min"));
        return 0;
    }
}
