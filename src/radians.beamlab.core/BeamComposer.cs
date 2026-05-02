using System;
using System.Collections.Generic;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>
/// Composes the multi-beam antenna pattern by incoherent power summation of
/// the active beams. Appropriate when each beam carries an independent signal
/// (the usual non-GSO multi-beam payload). For phased-array beams driven from
/// a single coherent feed network this is NOT the right model.
/// </summary>
public static class BeamComposer
{
    /// <summary>
    /// G_tot(test) = 10 * log10( sum_k w_k * 10^(G_k(test)/10) ) in dBi.
    /// Beams with weight &lt;= 0 contribute nothing.
    /// </summary>
    public static double CompositeGainDbi(IEnumerable<Beam> beams, Vec3 test)
    {
        double linearSum = 0.0;
        foreach (var beam in beams)
        {
            double w = beam.Weight;
            if (w <= 0.0) continue;
            double gDbi = beam.GainDbi(test);
            linearSum += w * Math.Pow(10.0, gDbi / 10.0);
        }
        if (linearSum <= 0.0) return double.NegativeInfinity;
        return 10.0 * Math.Log10(linearSum);
    }

    /// <summary>
    /// Maximum single-beam contribution at the test direction:
    /// max_k ( G_k(test) + 10*log10(w_k) ) in dBi. The dominant beam's
    /// (weight-effective) gain — useful when adjacent-beam aggregation is
    /// not the right metric (e.g. single-carrier link budget, dominant-beam
    /// interference analyses).
    /// </summary>
    public static double MaxSingleBeamGainDbi(IEnumerable<Beam> beams, Vec3 test)
    {
        double maxLinear = 0.0;
        foreach (var beam in beams)
        {
            double w = beam.Weight;
            if (w <= 0.0) continue;
            double linearG = w * Math.Pow(10.0, beam.GainDbi(test) / 10.0);
            if (linearG > maxLinear) maxLinear = linearG;
        }
        if (maxLinear <= 0.0) return double.NegativeInfinity;
        return 10.0 * Math.Log10(maxLinear);
    }

    /// <summary>
    /// Apply an exclusion predicate over each beam's boresight direction.
    /// Beams whose boresight satisfies the predicate are switched off (w = 0);
    /// the rest keep their existing weight.
    /// </summary>
    public static void ApplyExclusion(IEnumerable<Beam> beams, Func<Vec3, bool> isExcluded)
    {
        foreach (var beam in beams)
        {
            if (isExcluded(beam.Boresight)) beam.Weight = 0.0;
        }
    }
}
