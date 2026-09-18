using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using radians.beamlab;
using radians.beamlab.app;

namespace radians.beamlab.checks;

/// <summary>
/// Seconds per simulated step at the current thread count: the truth step
/// (the scheduler, the live composition of every satellite, seven victims,
/// the epfd(is) byproduct off) and the mask-examination step of one victim.
/// A timing instrument, not a measurement of the system -- the CDFs are
/// discarded. Run it once with BEAMLAB_THREADS=1 and once without to read
/// the speed-up on the machine at hand; V56 pins that both runs agree bit
/// for bit.
///
/// Run:  -- bench [profile] [design] [rsetDir] [steps] [stepSec]
///       defaults: the STEAM-2 case and its recorded declaration
///       (dataset/margin/steam-2), 10 steps of 60 s.
/// </summary>
internal static class Bench
{
    public static int Run(string profilePath, string designPath, string rsetDir, long steps, double stepSec)
    {
        var inv = CultureInfo.InvariantCulture;
        if (!File.Exists(profilePath) || !File.Exists(designPath) || !Directory.Exists(rsetDir))
        { Console.WriteLine("ABORT: profile, design or declaration directory not found."); return 2; }
        steps = Math.Max(1, steps);

        var prof = OperationProfileCodec.Load(File.ReadAllText(profilePath));
        var doc = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(designPath));
        var shells = doc.Shells.Select(OrbitDesignFileCodec.ToShell).ToArray();
        double altKm = shells[0].OperatingHeightKm ?? shells[0].AltitudeKm;
        double freqMhz = prof.Down.FrequencyGhz * 1000.0;
        var con = new Constellation(shells);
        var comp = OperationComposer.Compose(prof, altKm);
        var (declared, _, maskPath) = ComplianceLoop.LoadReusedDeclaration(rsetDir);
        double simDur = steps * stepSec;
        var limits = new List<radlimits.LimitPoint>
        {
            new() { EPFD = -300.0, Perc = 0.001 },
            new() { EPFD = 0.0, Perc = 100.0 },
        };
        EpfdDownVictim Victim(double lat) => new()
        {
            EsLatDeg = lat, EsLonDeg = 0.0, GsoLonDeg = 10.0,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, freqMhz, 1.0),
        };
        var victims = new[] { 0.0, 10.0, 20.0, 30.0, 40.0, 50.0, 60.0 }.Select(Victim).ToList();

        Console.WriteLine(string.Create(inv,
            $"bench: {prof.Name}; {con.SatelliteCount} satellites, {comp.Geography.Cells.Count} cells; "
            + $"{steps} steps of {stepSec:F0} s; threads {SimulationParallel.MaxDegreeOfParallelism} of {Environment.ProcessorCount}"));

        // Warm-up on a fresh pointing (JIT, thread pool, scene copies), then the measured pass.
        EpfdDown.RunMany(con, Pointing(), victims, stepSec, 1, limits, simDur);
        var sw = Stopwatch.StartNew();
        EpfdDown.RunMany(con, Pointing(), victims, stepSec, steps, limits, simDur);
        double truthPerStep = sw.Elapsed.TotalSeconds / steps;
        Console.WriteLine(string.Create(inv,
            $"  truth step (scheduler + live composition, {victims.Count} victims): {truthPerStep:F3} s/step  ({sw.Elapsed.TotalSeconds:F1} s)"));

        var mask = MaskFootprint.LoadFile(maskPath);
        long examSteps = steps * 20;
        EpfdDownMask.Run(con, mask, declared, victims[4], stepSec, 1, limits, examSteps * stepSec);
        sw.Restart();
        EpfdDownMask.Run(con, mask, declared, victims[4], stepSec, examSteps, limits, examSteps * stepSec);
        double examPerStep = sw.Elapsed.TotalSeconds / examSteps;
        Console.WriteLine(string.Create(inv,
            $"  examination step (mask file, one victim, {examSteps} steps): {examPerStep * 1000.0:F1} ms/step  ({sw.Elapsed.TotalSeconds:F1} s)"));
        return 0;

        ScheduledPointing Pointing() => new(con, comp.Geography, comp.Enforced, comp.Scene, simDur,
            comp.CoverageRadiusKm, comp.Policy, comp.IlluminationDutyCycle);
    }
}
