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
    /// Worst-colour co-channel EIRP density under an N-colour frequency-reuse
    /// plan: beams are partitioned by <paramref name="reuseColors"/> (only
    /// same-colour beams share a channel), each colour is power-summed
    /// separately, and the strongest colour is returned (dBW). The realistic
    /// middle ground between the all-co-frequency power sum and the
    /// perfect-isolation single-beam view.
    ///
    /// Regulatory grounding (Rec. ITU-R S.1503-4):
    /// Sec. C2.3.1 -- "the pfd radiated by a non-GSO space station at any point
    /// on the Earth's surface is the sum of the pfd produced by all
    /// illuminating beams in the co-frequency band"; Sec. C2.4.1 -- the mask
    /// depends on "the maximum number of co-frequency beams which can be
    /// illuminated simultaneously". The cluster size N itself is a system
    /// design choice declared by the operator, not a value prescribed by
    /// S.1503.
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
    /// Per-beam reuse colours for an N-colour plan: hex-lattice colouring
    /// where the beams carry axial indices (<see cref="Beam.LatticeI"/>/J),
    /// index % N otherwise (manual ring layouts).
    /// </summary>
    public static int[] ReuseColors(IReadOnlyList<Beam> beams, int n)
    {
        var colors = new int[beams.Count];
        for (int i = 0; i < beams.Count; i++)
        {
            colors[i] = beams[i].LatticeI is int li && beams[i].LatticeJ is int lj
                ? HexReuseColor(li, lj, n)
                : i % n;
        }
        return colors;
    }

    /// <summary>
    /// The composite a resolved beam set declares: the worst-colour
    /// co-channel sum when the set carries an N-colour plan, the all-beam
    /// power sum otherwise.
    /// </summary>
    public static double ResolvedEirpDbw(ResolvedBeamSet set, Vec3 test)
        => set.CoChannelN is int n && set.ReuseColors is { } colors
            ? MaxCoChannelEirpDbw(set.Beams, test, set.PowersDbw, colors, n)
            : CompositeEirpDbw(set.Beams, test, set.PowersDbw);

    /// <summary>
    /// Frequency-reuse colour of a hex-lattice cell at axial indices (i, j),
    /// for a reuse cluster of size <paramref name="n"/>. For N = 3, 4 and 7
    /// (the standard hex cluster sizes) no two adjacent cells share a colour;
    /// other N fall back to a diagonal-stripe colouring without that
    /// guarantee.
    ///
    /// References: Maral, Bousquet &amp; Sun, "Satellite Communications
    /// Systems", 6th ed. (Wiley): Sec. 5.11.1.2 "Frequency reuse" (reuse via
    /// beam isolation, p. 261) and Sec. 9.8.7.3 "Beam lattice" (cluster of
    /// beams repeated over the service zone; Fig. 9.40 shows the
    /// three-frequency and seven-frequency patterns implemented here as
    /// N = 3 / N = 7, p. 556). N = 3, 4, 7 are the classical hexagonal
    /// cluster sizes (N = i^2 + ij + j^2). Terminology: N is the CLUSTER SIZE
    /// (number of colours); Maral's "frequency reuse factor" is a different
    /// quantity -- the number of times the band is used across the coverage,
    /// roughly M beams / N colours (his worked example: 4.3 for 13 beams in a
    /// 3-colour lattice, p. 263). 3GPP TR 38.821 calls the cluster size
    /// "frequency reuse factor" and studies FRF = 1 (all beams co-frequency)
    /// and FRF = 3 -- the same two options exposed as PowerSum and
    /// CoChannelSum with N = 3.
    /// </summary>
    public static int HexReuseColor(int i, int j, int n) => n switch
    {
        3 => (((i - j) % 3) + 3) % 3,
        4 => (((i % 2) + 2) % 2) + 2 * (((j % 2) + 2) % 2),
        7 => (((i + 3 * j) % 7) + 7) % 7,
        _ => (((i - j) % n) + n) % n,
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
