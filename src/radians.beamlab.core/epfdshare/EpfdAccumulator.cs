using radlimits;
using System;
using System.Collections.Generic;
using System.Linq;

namespace radcompute1503_2
{
    /// <summary>
    /// Accumulates EPFD samples into 0.1 dB bins and produces a CDF.
    /// Per S.1503-4 §D7.1.2: bin size SB = 0.1 dB, CDFi = 100×(1 - SUM(PDFmin:PDFi)).
    /// </summary>
    public class EpfdAccumulator
    {
        private const double BinWidth = 0.1; // §D7.1.2: SB = 0.1 dB
        private const double BinMargin = 100.0; // dB margin below min and above max limit EPFD

        private readonly double _epfdMin;
        private readonly double _epfdMax;
        private readonly int _nbBins;
        private readonly long[] _binCounts;
        private long _noEpfdCount;
        private long _totalSamples;
        private bool _epfdMaxExceeded;

        public long TotalSamples => _totalSamples;

        // Bin parameters, exposed so GPU managers can align their device histogram with this
        // accumulator's bins (replaces reflection in GpuHostHelpers).
        public double EpfdMin => _epfdMin;
        public int NbBins => _nbBins;

        /// <summary>
        /// Creates an accumulator with EPFD range derived from limit points.
        /// </summary>
        public EpfdAccumulator(List<LimitPoint> limitPoints)
        {
            double minEpfd = double.MaxValue;
            double maxEpfd = double.MinValue;
            foreach (var pt in limitPoints)
            {
                if (pt.EPFD < minEpfd) minEpfd = pt.EPFD;
                if (pt.EPFD > maxEpfd) maxEpfd = pt.EPFD;
            }

            _epfdMin = Math.Floor((minEpfd - BinMargin) / BinWidth) * BinWidth;
            _epfdMax = Math.Ceiling((maxEpfd + BinMargin) / BinWidth) * BinWidth;
            _nbBins = (int)Math.Ceiling((_epfdMax - _epfdMin) / BinWidth);
            _binCounts = new long[_nbBins];
        }

        /// <summary>
        /// Creates an accumulator with explicit EPFD bounds.
        /// </summary>
        public EpfdAccumulator(double epfdMin, double epfdMax)
        {
            _epfdMin = epfdMin;
            _epfdMax = epfdMax;
            _nbBins = (int)Math.Ceiling((_epfdMax - _epfdMin) / BinWidth);
            _binCounts = new long[_nbBins];
        }

        /// <summary>
        /// Wraps a pre-binned histogram (e.g. a GPU kernel result) so it can be merged into a
        /// matching-range accumulator via MergeFrom. Only the counts are populated; bins must be
        /// sized to the target accumulator's NbBins.
        /// </summary>
        internal EpfdAccumulator(long[] binCounts, long noEpfd, long total)
        {
            _binCounts = binCounts;
            _nbBins = binCounts.Length;
            _noEpfdCount = noEpfd;
            _totalSamples = total;
        }

        /// <summary>
        /// Bins a single EPFD sample (in dB).
        /// increment = 1 for fine steps, Ncoarse for coarse steps (§D5.2.6 Sub-step 35.1).
        /// </summary>
        public void AccumulateSample(double epfdDb, int increment)
        {
            _totalSamples += increment;

            if (epfdDb < _epfdMin)
            {
                _noEpfdCount += increment;
                return;
            }

            if (epfdDb >= _epfdMax)
            {
                _binCounts[_nbBins - 1] += increment;
                _epfdMaxExceeded = true;
                return;
            }

            int index = (int)Math.Ceiling((epfdDb - _epfdMin) / BinWidth);
            if (index < 0) index = 0;
            if (index >= _nbBins) index = _nbBins - 1;
            _binCounts[index] += increment;
        }

        /// <summary>
        /// Merges another accumulator's counts into this one.
        /// Both must have the same bin range (_epfdMin, _epfdMax).
        /// </summary>
        public void MergeFrom(EpfdAccumulator other)
        {
            _totalSamples += other._totalSamples;
            _noEpfdCount += other._noEpfdCount;
            _epfdMaxExceeded |= other._epfdMaxExceeded;
            for (int i = 0; i < _nbBins; i++)
                _binCounts[i] += other._binCounts[i];
        }

        /// <summary>
        /// Builds the complementary CDF per §D7.1.2:
        /// CDFi = 100 × (1 - SUM(PDFmin:PDFi))
        /// where PDFx is normalized so total sum = 1.
        /// Returns arrays of EPFD values (dB) and percentages (%).
        /// </summary>
        public (double[] epfdValues, double[] percentages) BuildCdf()
        {
            var epfdValues = new double[_nbBins];
            var percentages = new double[_nbBins];

            if (_totalSamples == 0)
                return (epfdValues, percentages);

            // Accumulate from max EPFD (lowest percentage) to preserve
            // precision for the small percentages that matter for pass/fail.
            double cumulative = 0;
            for (int i = _nbBins - 1; i >= 0; i--)
            {
                cumulative += _binCounts[i];
                epfdValues[i] = Math.Round(_epfdMin + i * BinWidth, 1);
                percentages[i] = cumulative / _totalSamples * 100.0;
            }

            return (epfdValues, percentages);
        }

        /// <summary>
        /// Returns the raw bin counts (PDF histogram) and corresponding EPFD values.
        /// Prepends a -3000 entry for timesteps with no interference.
        /// </summary>
        public (double[] epfdValues, long[] binCounts) BuildPdf()
        {
            bool hasNoInterference = _noEpfdCount > 0;
            int extra = hasNoInterference ? 1 : 0;
            var epfdValues = new double[_nbBins + extra];
            var counts = new long[_nbBins + extra];

            if (hasNoInterference)
            {
                epfdValues[0] = -3000.0;
                counts[0] = _noEpfdCount;
            }

            for (int i = 0; i < _nbBins; i++)
            {
                epfdValues[i + extra] = Math.Round(_epfdMin + i * BinWidth, 1);
                counts[i + extra] = _binCounts[i];
            }
            return (epfdValues, counts);
        }

        /// <summary>
        /// Builds the limit visualisation curve across all CDF bins. Smooth
        /// segments are log-linearly interpolated in CCDF (Perc) between
        /// adjacent spec points using raw EPFDs (no flooring). Staircase
        /// discontinuities (duplicate EPFD with different Perc) place the
        /// cap value at the floored bin per §D7.1.3 Step 3.
        ///
        /// §D7.1.3 rounding is applied elsewhere, not here:
        ///   (a) per-timestep simulation EPFDs floored before binning
        ///       (in AddSample),
        ///   (b) limit Ji floored at the compliance lookup
        ///       (in CompareWithLimits).
        /// This function returns the visualisation curve only — compliance
        /// pi is read from limitPoints[i].Perc directly in CompareWithLimits.
        /// </summary>
        public double[] BuildLinearizedLimit(List<LimitPoint> limitPoints)
        {
            var result = new double[_nbBins];
            if (limitPoints == null || limitPoints.Count == 0)
                return result;

            // Group spec points by EPFD. A singleton group has one Perc.
            // A multi-point group is a staircase discontinuity at a single
            // EPFD: ApproachPerc = value held from below (max Perc),
            // CapPerc = post-discontinuity value held from above (min).
            var groups = limitPoints
                .GroupBy(p => p.EPFD)
                .OrderBy(g => g.Key)
                .Select(g => new LimitGroup(
                    epfd: g.Key,
                    approachPerc: g.Max(p => p.Perc),
                    capPerc: g.Min(p => p.Perc),
                    isStaircase: g.Count() > 1))
                .ToList();

            var first = groups[0];
            var last = groups[groups.Count - 1];

            // Below the first spec point: 100% for the "single-point hard cap"
            // case (e.g. lat-dependent limit collapsed to one never-to-be-
            // exceeded value); otherwise the first group's ApproachPerc,
            // extending the first segment leftward.
            double belowFirstPerc = groups.Count == 1 ? 100.0 : first.ApproachPerc;

            // Smooth interpolation pass.
            for (int i = 0; i < _nbBins; i++)
            {
                double epfd = _epfdMin + i * BinWidth;

                if (epfd < first.EPFD)
                {
                    // Strict < (not ≤) so a bin AT the first spec EPFD does not
                    // get belowFirstPerc. This matters for the single-point cap
                    // case (groups.Count == 1, e.g. lat-dependent limit collapsed
                    // to one never-to-be-exceeded value at EPFD = X): the bin AT
                    // X must return the cap value (via the "above last" branch
                    // below, since first == last for a single group), not the
                    // "below first" value of 100. For multi-group cases, the bin
                    // AT first.EPFD falls into the interp loop and returns
                    // first.CapPerc as the segment's left endpoint — same final
                    // value the staircase override would write if applicable.
                    result[i] = belowFirstPerc;
                    continue;
                }
                if (epfd >= last.EPFD)
                {
                    result[i] = last.CapPerc;
                    continue;
                }

                // Find segment containing this bin and interpolate. Endpoints:
                // CapPerc on the left (post-staircase value at group j),
                // ApproachPerc on the right (pre-staircase value at group j+1).
                // Singletons have CapPerc == ApproachPerc so they participate
                // as their single Perc on either side.
                for (int j = 0; j < groups.Count - 1; j++)
                {
                    if (epfd <= groups[j + 1].EPFD)
                    {
                        result[i] = InterpolateLog(epfd,
                            groups[j].EPFD, groups[j].CapPerc,
                            groups[j + 1].EPFD, groups[j + 1].ApproachPerc);
                        break;
                    }
                }
            }

            // Staircase override: place the cap value at the bin NEAREST to the
            // staircase EPFD (Math.Round). The visualisation curve reflects the
            // spec's piecewise limit envelope: bins whose representative EPFD
            // sits in the flat segment between staircase points hold the segment
            // value (via interpolation), and the discontinuity is placed at the
            // bin nearest the spec EPFD where the drop happens. For a staircase
            // at non-bin-aligned EPFD like -158.33, Round → bin -158.3, so the
            // cap drop is placed there while bin -158.4 retains the flat-segment
            // value 0.571.
            //
            // Note: this differs from §D7.1.3 Step 3 (Floor) used in
            // CompareWithLimits for the compliance bin lookup. The visualisation
            // shows the spec envelope at each bin's representative EPFD; the
            // compliance check is independently conservative at the floored bin.
            // The two diverge at bins that straddle a cap discontinuity, which
            // is expected (visualisation is per-EPFD; compliance is per-bin).
            //
            // Singleton groups don't need an override — the interpolated curve
            // already carries their value at the appropriate place.
            foreach (var g in groups)
            {
                if (!g.IsStaircase) continue;
                int binIdx = (int)Math.Round((g.EPFD - _epfdMin) / BinWidth);
                if (binIdx >= 0 && binIdx < _nbBins)
                    result[binIdx] = g.CapPerc;
            }

            return result;
        }

        private readonly struct LimitGroup
        {
            public LimitGroup(double epfd, double approachPerc, double capPerc, bool isStaircase)
            {
                EPFD = epfd;
                ApproachPerc = approachPerc;
                CapPerc = capPerc;
                IsStaircase = isStaircase;
            }
            public double EPFD { get; }
            public double ApproachPerc { get; }
            public double CapPerc { get; }
            public bool IsStaircase { get; }
        }

        /// <summary>
        /// Compares CDF against linearized limit per §D7.1.3.
        /// For each limit point (Ji, Pi): round Ji to bin, compare
        /// computed CDF against interpolated limit at that bin.
        /// Returns pass/fail per limit point and the linearized limit array.
        /// </summary>
        public (bool[] passResults, double[] limitPercentages) CompareWithLimits(List<LimitPoint> limitPoints)
        {
            var (epfdValues, percentages) = BuildCdf();
            var linearizedLimit = BuildLinearizedLimit(limitPoints);
            var results = new bool[limitPoints.Count];

            for (int i = 0; i < limitPoints.Count; i++)
            {
                double ji = limitPoints[i].EPFD;

                // §D7.1.3 Step 3: Round Ji to lower 0.1 dB precision
                double jiRounded = Math.Floor(ji / BinWidth) * BinWidth;

                // Find the bin index for the rounded limit EPFD
                int binIndex = (int)Math.Ceiling((jiRounded - _epfdMin) / BinWidth);

                double pt;
                // Pi is the spec's percentage at this limit point — must come directly
                // from limitPoints[i].Perc, NOT from the interpolated visualisation curve
                // (linearizedLimit). The visualisation curve is built from unrounded
                // limit EPFDs and at the floored bin index it returns an interpolated
                // value, not the spec Pi. Using that for compliance would silently
                // relax the limit by the interpolation delta. §D7.1.3 mandates Pi at
                // the spec's Ji (rounded for the lookup bin), so read it from the
                // input list.
                double pi = limitPoints[i].Perc;
                if (binIndex < 0)
                {
                    pt = 100.0;
                }
                else if (binIndex >= _nbBins)
                {
                    pt = 0.0;
                }
                else
                {
                    pt = percentages[binIndex];
                }

                // §D7.1.3 Step 5: Pass if Pt ≤ Pi
                results[i] = pt <= pi;
            }

            return (results, linearizedLimit);
        }

        // Serialization for save/load
        public class SaveState
        {
            public double EpfdMin { get; set; }
            public double EpfdMax { get; set; }
            public int NbBins { get; set; }
            public long[] BinCounts { get; set; }
            public long NoEpfdCount { get; set; }
            public long TotalSamples { get; set; }
            public bool EpfdMaxExceeded { get; set; }
        }

        public SaveState ToSaveState() => new SaveState
        {
            EpfdMin = _epfdMin,
            EpfdMax = _epfdMax,
            NbBins = _nbBins,
            BinCounts = (long[])_binCounts.Clone(),
            NoEpfdCount = _noEpfdCount,
            TotalSamples = _totalSamples,
            EpfdMaxExceeded = _epfdMaxExceeded
        };

        public static EpfdAccumulator FromSaveState(SaveState s)
        {
            var acc = new EpfdAccumulator(s.EpfdMin, s.EpfdMax);
            if (s.NbBins != acc._nbBins)
                throw new InvalidOperationException(
                    $"Bin count mismatch: saved {s.NbBins}, computed {acc._nbBins}");
            Array.Copy(s.BinCounts, acc._binCounts, s.NbBins);
            acc._noEpfdCount = s.NoEpfdCount;
            acc._totalSamples = s.TotalSamples;
            acc._epfdMaxExceeded = s.EpfdMaxExceeded;
            return acc;
        }

        /// <summary>
        /// Linear interpolation in log10 scale for percentage values.
        /// </summary>
        private static double InterpolateLog(double x, double x1, double y1, double x2, double y2)
        {
            if (x <= x1) return y1;
            if (x >= x2) return y2;
            if (x1 == x2) return y1;

            double log1 = y1 > 0.0 ? Math.Log10(y1) : Math.Log10(1e-05);
            double log2 = y2 > 0.0 ? Math.Log10(y2) : Math.Log10(1e-05);

            double slope = (log1 - log2) / (x1 - x2);
            double intercept = (x1 * log2 - x2 * log1) / (x1 - x2);
            return Math.Pow(10.0, slope * x + intercept);
        }
    }
}
