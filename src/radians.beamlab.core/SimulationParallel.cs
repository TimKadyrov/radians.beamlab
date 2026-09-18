using System;
using System.Globalization;
using System.Threading.Tasks;

namespace radians.beamlab;

/// <summary>
/// The simulation's degree of parallelism. Every parallel loop in the
/// simulation partitions work that is independent by construction -- the
/// satellites of one time step (propagation, beam resolution, per-victim
/// contributions), the cells of one schedule step, or the time steps of a
/// scheduler-free examination -- and reduces in the sequential order, so
/// the output is bit-identical at every degree; 1 runs the sequential code
/// path. The scheduler's memory (dwell, handovers, the seeded Random
/// policy) is never split across threads: it advances step by step on the
/// calling thread. Default: every processor, or the BEAMLAB_THREADS
/// environment variable when it names a positive count.
/// </summary>
public static class SimulationParallel
{
    private static int _maxDegree = DefaultDegree();

    /// <summary>Worker threads a parallel loop may use; 1 = sequential.</summary>
    public static int MaxDegreeOfParallelism
    {
        get => _maxDegree;
        set => _maxDegree = Math.Max(1, value);
    }

    /// <summary>True when a loop should fan out at all.</summary>
    public static bool Enabled => _maxDegree > 1;

    /// <summary>Options for a parallel loop at the current degree.</summary>
    public static ParallelOptions Options => new() { MaxDegreeOfParallelism = _maxDegree };

    private static int DefaultDegree()
    {
        string? env = Environment.GetEnvironmentVariable("BEAMLAB_THREADS");
        if (env is not null
            && int.TryParse(env.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            && n >= 1)
            return n;
        return Math.Max(1, Environment.ProcessorCount);
    }
}
