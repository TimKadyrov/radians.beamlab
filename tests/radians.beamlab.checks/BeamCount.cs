using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using radians.beamlab;
using radians.beamlab.app;

/// <summary>
/// E1-1 readout: the per-satellite simultaneous CO-FREQUENCY beam count.
///
/// The reachable-envelope mask sums every same-colour beam of the lattice at
/// each cell, because the ungated scene lights them all. The system does not:
/// a satellite serves the cells it is assigned, so only some of its beams are
/// on at once, and only the same-colour subset of those shares a channel.
/// Enveloping over all M when the system can only light K over-declares by
/// the tail of that sum.
///
/// K is MEASURED here before it is declared anywhere -- on the SATURATED
/// probe, so the count is what the system may light rather than what one
/// traffic sample asked for. The envelope change is only admissible up to
/// the K this reports, and the distribution says how firm that ceiling is.
///
/// Run: dotnet run --project tests/radians.beamlab.checks -- beamcount
///          [profile.json] [design.json] [days] [stepSec]
/// </summary>
internal static class BeamCount
{
    public static int Run(string profilePath, string designPath, double days, double stepSec)
    {
        var inv = CultureInfo.InvariantCulture;
        if (!File.Exists(profilePath) || !File.Exists(designPath))
        {
            Console.WriteLine("ABORT: profile or design not found: " + profilePath + " / " + designPath);
            return 2;
        }

        var prof0 = OperationProfileCodec.Load(File.ReadAllText(profilePath));
        var doc = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(designPath));
        var shells0 = doc.Shells.Select(OrbitDesignFileCodec.ToShell).ToArray();
        double altKm = shells0[0].OperatingHeightKm ?? shells0[0].AltitudeKm;

        // The same saturated probe the declaration is derived from.
        var enforced0 = OperationComposer.Compose(prof0, altKm).Enforced;
        var prof = ComplianceViewModel.Saturate(prof0, enforced0);
        var comp = OperationComposer.Compose(prof, altKm);
        var con = new Constellation(OperationComposer.ApplyToShells(prof, shells0));
        long steps = Math.Max(1, (long)Math.Round(days * 86400.0 / stepSec));
        double simDur = steps * stepSec;

        // Colours come from the body-fixed lattice, so they are computed once.
        // The count is asserted stable rather than assumed: a lattice that
        // changed size with latitude would invalidate the mapping.
        var gen = new PfdMaskViewModel(comp.Scene.Coastlines);
        comp.Scene.CopySettingsTo(gen);
        int beamCount = -1;
        foreach (double lat in new[] { 0.0, 25.0, 50.0 })
        {
            gen.Scene.SubSatLatDeg = lat;
            gen.Scene.SubSatLonDeg = 0.0;
            gen.Scene.AltitudeKm = altKm;
            gen.RebuildForCompute();
            int c = gen.Scene.Beams.Count;
            if (beamCount < 0) beamCount = c;
            else if (c != beamCount)
            {
                Console.WriteLine(string.Create(inv,
                    $"ABORT: the lattice is not latitude-invariant ({beamCount} beams vs {c}); "
                    + $"the colour mapping this readout relies on does not hold."));
                return 2;
            }
        }
        int n = gen.Aggregation == PfdAggregation.CoChannelSum ? gen.ReuseClusterSize : 1;
        var colors = n > 1
            ? BeamComposer.ReuseColors(gen.Scene.Beams, n)
            : new int[beamCount];               // one colour: every beam co-frequency
        var perColour = new int[n];
        for (int i = 0; i < beamCount; i++) perColour[colors[i]]++;

        Console.WriteLine(string.Create(inv, $"profile: {prof0.Name}"));
        Console.WriteLine(string.Create(inv,
            $"system: {con.SatelliteCount} satellites at {altKm:F0} km; lattice {beamCount} beams, "
            + $"{n} colour(s), {perColour.Max()} beams in the largest colour"));
        Console.WriteLine(string.Create(inv,
            $"probe: saturated, {steps} steps of {stepSec:F0} s ({days:F3} d); demand "
            + $"{prof0.DemandLinksPerCell} -> {prof.DemandLinksPerCell}"));

        var sched = new Scheduler(con, comp.Geography, comp.Enforced,
            new ScenePointing(comp.Scene), simDur, comp.CoverageRadiusKm, comp.Policy);

        // Histogram of the per-satellite worst-colour active beam count.
        var hist = new SortedDictionary<int, long>();
        long satSteps = 0, litSatSteps = 0;
        int worst = 0;
        var colourCount = new int[n];
        for (long k = 0; k < steps; k++)
        {
            var step = sched.Step(k * stepSec);
            foreach (var kv in step.ActiveBeams)
            {
                satSteps++;
                if (kv.Value.Count == 0) { Bump(hist, 0); continue; }
                litSatSteps++;
                Array.Clear(colourCount, 0, n);
                foreach (int b in kv.Value)
                    if (b >= 0 && b < beamCount) colourCount[colors[b]]++;
                int kMax = colourCount.Max();
                Bump(hist, kMax);
                if (kMax > worst) worst = kMax;
            }
            if (steps >= 10 && k % (steps / 10) == 0)
                Console.WriteLine(string.Create(inv, $"  {100.0 * k / steps:F0}%"));
        }

        Console.WriteLine();
        Console.WriteLine("per-satellite simultaneous co-frequency beams (worst colour):");
        Console.WriteLine("  K | satellite-steps | share | cumulative from the top");
        long total = hist.Values.Sum();
        long cum = 0;
        foreach (var kv in hist.Reverse())
        {
            cum += kv.Value;
            Console.WriteLine(string.Create(inv,
                $"{kv.Key,3} | {kv.Value,15} | {100.0 * kv.Value / Math.Max(1, total),5:F2}% | {100.0 * cum / Math.Max(1, total),6:F2}%"));
        }

        double overDeclareDb = 10.0 * Math.Log10(
            Math.Max(1, perColour.Max()) / (double)Math.Max(1, worst));
        Console.WriteLine();
        Console.WriteLine(string.Create(inv,
            $"K (measured ceiling) = {worst}; the largest colour holds {perColour.Max()} beams."));
        Console.WriteLine(string.Create(inv,
            $"A flat sum of the whole colour over-declares by {overDeclareDb:F1} dB against a flat sum of K --"));
        Console.WriteLine(
            "  an UPPER bound on what E1-1 can recover, and a loose one: the beams differ in");
        Console.WriteLine(
            "  contribution at any test point, so a top-K envelope keeps the dominant terms and");
        Console.WriteLine(
            "  drops only the tail. Expect materially less than this.");
        Console.WriteLine(string.Create(inv,
            $"satellite-steps: {satSteps} observed, {litSatSteps} with any beam lit."));
        return 0;
    }

    private static void Bump(SortedDictionary<int, long> h, int key)
        => h[key] = h.TryGetValue(key, out long v) ? v + 1 : 1;
}
