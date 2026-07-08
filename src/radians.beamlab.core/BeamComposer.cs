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
    /// EIRP-density composite with per-beam transmit powers:
    /// 10 * log10( sum_k w_k * 10^((P_k + G_k(test))/10) ) in dBW.
    /// <paramref name="powersDbw"/> is index-aligned with <paramref name="beams"/>.
    /// With equal powers this equals P + <see cref="CompositeGainDbi"/> exactly;
    /// unequal powers model per-beam power control (e.g. spreading-loss
    /// compensation for constant boresight PFD).
    /// </summary>
    public static double CompositeEirpDbw(IReadOnlyList<Beam> beams, Vec3 test, IReadOnlyList<double> powersDbw)
    {
        double linearSum = 0.0;
        for (int i = 0; i < beams.Count; i++)
        {
            double w = beams[i].Weight;
            if (w <= 0.0) continue;
            double eirpDbw = powersDbw[i] + beams[i].GainDbi(test);
            linearSum += w * Math.Pow(10.0, eirpDbw / 10.0);
        }
        if (linearSum <= 0.0) return double.NegativeInfinity;
        return 10.0 * Math.Log10(linearSum);
    }

    /// <summary>
    /// Worst-colour co-channel EIRP density under a K-colour frequency-reuse
    /// plan: beams are partitioned by <paramref name="reuseColors"/> (only
    /// same-colour beams share a channel), each colour is power-summed
    /// separately, and the strongest colour is returned (dBW). The realistic
    /// middle ground between the all-co-frequency power sum and the
    /// perfect-isolation single-beam view.
    ///
    /// Regulatory grounding: Rec. ITU-R S.1503-4 (Annex 1 Part C) makes the
    /// PFD mask an operator-declared envelope of the system's actual
    /// co-frequency operation -- aggregating only co-channel beams mirrors the
    /// notified "number of co-frequency operations" (Nco) concept used by the
    /// EPFD algorithms. The reuse factor K itself is a system design choice,
    /// not a value prescribed by S.1503.
    /// <paramref name="reuseColors"/> is index-aligned with <paramref name="beams"/>;
    /// values are clamped into [0, numColors).
    /// </summary>
    public static double MaxCoChannelEirpDbw(IReadOnlyList<Beam> beams, Vec3 test,
        IReadOnlyList<double> powersDbw, IReadOnlyList<int> reuseColors, int numColors)
    {
        Span<double> sums = numColors <= 16 ? stackalloc double[16] : new double[numColors];
        sums = sums[..Math.Max(1, numColors)];
        sums.Clear();

        for (int i = 0; i < beams.Count; i++)
        {
            double w = beams[i].Weight;
            if (w <= 0.0) continue;
            double linear = w * Math.Pow(10.0, (powersDbw[i] + beams[i].GainDbi(test)) / 10.0);
            int c = reuseColors[i];
            if (c < 0) c = 0; else if (c >= sums.Length) c = sums.Length - 1;
            sums[c] += linear;
        }

        double max = 0.0;
        foreach (double s in sums) if (s > max) max = s;
        if (max <= 0.0) return double.NegativeInfinity;
        return 10.0 * Math.Log10(max);
    }

    /// <summary>
    /// Frequency-reuse colour of a hex-lattice cell at axial indices (i, j).
    /// For K = 3, 4 and 7 (the standard hex cluster sizes) no two adjacent
    /// cells share a colour; other K fall back to a diagonal-stripe colouring
    /// without that guarantee.
    ///
    /// References: the valid hexagonal cluster sizes N = i^2 + ij + j^2
    /// (N = 1, 3, 4, 7, 9, 12, ...) come from classical cellular reuse theory --
    /// V. H. MacDonald, "Advanced Mobile Phone Service: The Cellular Concept",
    /// Bell Syst. Tech. J. 58(1), 1979; multibeam-satellite treatment in
    /// Maral &amp; Bousquet, "Satellite Communications Systems" (frequency-reuse
    /// chapters). For NTN payloads specifically, 3GPP TR 38.821 studies
    /// FRF = 1 (all beams co-frequency) and FRF = 3 -- the same two options
    /// exposed as PowerSum and CoChannelSum with K = 3.
    /// </summary>
    public static int HexReuseColor(int i, int j, int k) => k switch
    {
        3 => (((i - j) % 3) + 3) % 3,
        4 => (((i % 2) + 2) % 2) + 2 * (((j % 2) + 2) % 2),
        7 => (((i + 3 * j) % 7) + 7) % 7,
        _ => (((i - j) % k) + k) % k,
    };

    /// <summary>
    /// Maximum single-beam contribution at the test direction:
    /// max_k ( G_k(test) + 10*log10(w_k) ) in dBi. The dominant beam's
    /// (weight-effective) gain -- useful when adjacent-beam aggregation is
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
