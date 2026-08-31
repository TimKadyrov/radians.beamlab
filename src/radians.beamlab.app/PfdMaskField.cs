using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Data model for the PFD-mask heatmap: samples aggregate PFD on a 2-D mask
/// grid and holds the retained grids so the companion profile plot can slice
/// columns without re-rasterising. No WPF -- the view (<see cref="PfdHeatmapRenderer"/>)
/// turns these grids into a bitmap and the profile plot reads the slices.
///
/// Two coordinate systems (<see cref="MaskPlotKind"/>):
///
/// <b>AzEl</b> (S.1503-4 Sec. D6.4.5): X = sat-frame azimuth (from nadir toward
/// East), Y = sat-frame elevation (out of the East-Down plane toward North),
/// both +/-90 deg. Each pixel is one look direction; its ray-Earth intersection
/// gives the ground point whose aggregate PFD is stored. One sample per pixel.
///
/// <b>AlphaDeltaLong</b> (S.1503-4 Sec. D6.4.4): X = deltaLongitude (NGSO sub-sat
/// longitude - longitude of the alpha-minimising GSO arc point, wrapped +/-180 deg),
/// Y = signed alpha (+/-90 deg). The visible disc is swept in look directions and each
/// ground point's PFD is max-binned into its (dL, alpha) cell -- the ITU mask is
/// the maximum PFD over all ground points sharing (alpha, dL). Cells never hit
/// stay <see cref="double.NegativeInfinity"/> (the mask's -1000 analogue).
///
/// The whole visible disc is sampled -- including ground below the served
/// min-elevation, where side lobes of the active beams still radiate. Only
/// look directions past the horizon are excluded.
/// </summary>
public sealed class PfdMaskField
{
    /// <summary>Coordinate system of the current grids.</summary>
    public MaskPlotKind Kind { get; private set; } = MaskPlotKind.AzEl;

    /// <summary>X axis lower bound (deg): -90 (azimuth) or -180 (deltaLongitude).</summary>
    public double XMin { get; private set; } = -90.0;
    /// <summary>X axis upper bound (deg).</summary>
    public double XMax { get; private set; } = 90.0;
    /// <summary>Y axis lower bound (deg): -90 for both elevation and alpha.</summary>
    public double YMin { get; private set; } = -90.0;
    /// <summary>Y axis upper bound (deg).</summary>
    public double YMax { get; private set; } = 90.0;

    /// <summary>Grid width (X pixels). 0 until the first <see cref="Rebuild"/>.</summary>
    public int PixW { get; private set; }
    /// <summary>Grid height (Y pixels). 0 until the first <see cref="Rebuild"/>.</summary>
    public int PixH { get; private set; }

    /// <summary>Row-major PFD grid; NegativeInfinity where no data. Row 0 = Y max; col 0 = X min.</summary>
    public double[]? PfdGrid { get; private set; }
    /// <summary>
    /// Row-major |alpha| grid (deg) used for the exclusion tint. AzEl: |alpha| to the
    /// nearest visible GSO satellite at the pixel's ground point (180 where no
    /// data). AlphaDeltaLong: the |alpha| of the pixel's own Y coordinate.
    /// </summary>
    public double[]? AlphaGrid { get; private set; }
    /// <summary>
    /// Row-major earth-station elevation grid (deg): the ES elevation of the
    /// ground point whose PFD won each cell's max-binning. AlphaDeltaLong only
    /// (null in AzEl mode, where the ES-elevation guides have a closed form);
    /// NaN where no data. Drives the data-driven eps guides on the profile plot.
    /// </summary>
    public double[]? EsElevGrid { get; private set; }

    /// <summary>Auto-scaled colour-ramp lower bound from the last rebuild.</summary>
    public double PfdFloor { get; private set; } = -180.0;
    /// <summary>Auto-scaled colour-ramp upper bound from the last rebuild.</summary>
    public double PfdCeil { get; private set; } = -100.0;
    /// <summary>True iff the last rebuild produced any valid PFD samples.</summary>
    public bool HasValidRange { get; private set; }

    // --- Imported-mask source (Mask Viewer): the table IS the data model ---

    private MaskLatBlock? _maskBlock;
    private double[] _maskBVals = Array.Empty<double>();

    /// <summary>
    /// True for a viewer field: <see cref="Rebuild"/> never computes from the
    /// scene -- it re-rasterises the attached mask source, or clears the grids
    /// when nothing is loaded yet.
    /// </summary>
    public bool ExternalOnly { get; set; }

    /// <summary>
    /// Display threshold for the mask source: reads at or below this are
    /// treated as the unreachable floor (blank, excluded from the colour
    /// range). Defaults to the spec's -1000 null (S.1503-4 Sec. C1) -- no
    /// inference; the viewer can raise it to the block's own minimum when the
    /// user asks for that explicitly. Set BEFORE
    /// <see cref="SetMaskSource"/> so the colour range follows.
    /// </summary>
    public double UnreachableCutoffDb { get; set; } = MaskLatBlock.UnreachableDb;

    /// <summary>Raster width for mask-source display, set from the canvas by the view (plot-time rasterisation).</summary>
    public int TargetRasterW { get; set; } = 720;
    /// <summary>Raster height for mask-source display, set from the canvas by the view.</summary>
    public int TargetRasterH { get; set; } = 720;

    /// <summary>
    /// Attach an imported S.1503-4 mask latitude block as this field's data
    /// source. The table is kept EXACT -- the model of record, as in the
    /// reference implementation; grids are only a display raster, regenerated
    /// at <see cref="TargetRasterW"/> x <see cref="TargetRasterH"/> on the
    /// next <see cref="Rebuild"/> / <see cref="RasterizeMaskSource"/>.
    /// The colour range comes from the reachable node values only.
    /// </summary>
    public void SetMaskSource(MaskPlotKind kind, MaskLatBlock blk)
    {
        _maskBlock = blk;
        _maskBVals = new double[blk.Rows.Count];
        for (int i = 0; i < blk.Rows.Count; i++) _maskBVals[i] = blk.Rows[i].B;

        Kind = kind;
        bool alphaDelta = kind == MaskPlotKind.AlphaDeltaLong;
        XMin = alphaDelta ? -180.0 : -90.0;
        XMax = -XMin;
        YMin = -90.0; YMax = 90.0;

        var nodeVals = new List<double>();
        foreach (var row in blk.Rows)
            foreach (double v in row.Values)
                nodeVals.Add(v <= UnreachableCutoffDb ? double.NegativeInfinity : v);
        AutoScale(nodeVals.ToArray());

        PixW = 0; PixH = 0;
        PfdGrid = null; AlphaGrid = null; EsElevGrid = null;
    }

    /// <summary>
    /// Exact S.1503-4 Sec. D5.1.5 mask read at field coordinates (x, y) --
    /// transcribed from the reference maskdata GetPFD: bracket the b rows,
    /// interpolate along each bracketing row's OWN c grid (real filings are
    /// ragged), then linearly across b; outside the table range the read
    /// clamps to the edge node. Raw dB -- the -1000 unreachable floor
    /// participates as a plain number, exactly as in the reference.
    /// </summary>
    public double MaskReadRaw(double xDeg, double yDeg)
    {
        var blk = _maskBlock ?? throw new InvalidOperationException("No mask source attached.");
        bool alphaDelta = Kind == MaskPlotKind.AlphaDeltaLong;
        double bC = alphaDelta ? yDeg : xDeg;
        double cC = alphaDelta ? xDeg : yDeg;
        var (rLo, rHi) = Bracket(_maskBVals, bC);
        return ClampedLinear(bC,
            _maskBVals[rLo], MaskRowValue(blk.Rows[rLo], cC),
            _maskBVals[rHi], MaskRowValue(blk.Rows[rHi], cC));
    }

    private static double MaskRowValue(MaskRow row, double c)
    {
        var (lo, hi) = Bracket(row.CNodes, c);
        return ClampedLinear(c, row.CNodes[lo], row.Values[lo], row.CNodes[hi], row.Values[hi]);
    }

    /// <summary>
    /// Regenerate the display raster from the exact mask source at the target
    /// resolution: one Sec. D5.1.5 read per pixel. For display, reads at the
    /// unreachable floor are blanked, and the ramp between real data and the
    /// floor is clipped at the colour-scale floor so the plots stay scaled to
    /// the declared data.
    /// </summary>
    public void RasterizeMaskSource()
    {
        if (_maskBlock is null) return;
        bool alphaDelta = Kind == MaskPlotKind.AlphaDeltaLong;
        int pixW = Math.Clamp(TargetRasterW, 64, 2048);
        int pixH = Math.Clamp(TargetRasterH, 64, 2048);

        var pfdBuf = new double[pixW * pixH];
        var alphaBuf = new double[pixW * pixH];
        double dX = (XMax - XMin) / pixW;
        double dY = (YMax - YMin) / pixH;

        for (int py = 0; py < pixH; py++)
        {
            double y = YMax - (py + 0.5) * dY;
            double alphaRow = alphaDelta ? Math.Abs(y) : 180.0;
            int rowBase = py * pixW;
            for (int px = 0; px < pixW; px++)
            {
                double v = MaskReadRaw(XMin + (px + 0.5) * dX, y);
                pfdBuf[rowBase + px] = v <= UnreachableCutoffDb
                    ? double.NegativeInfinity
                    : Math.Max(v, PfdFloor);
                alphaBuf[rowBase + px] = alphaRow;
            }
        }

        PixW = pixW;
        PixH = pixH;
        PfdGrid = pfdBuf;
        AlphaGrid = alphaBuf;
        EsElevGrid = null;
    }

    /// <summary>
    /// Exact profile through the mask source: Sec. D5.1.5 reads at dense
    /// positions along one axis with the other fixed. Floor reads become
    /// gaps; values are clipped at the colour-scale floor like the raster.
    /// </summary>
    private List<(double pos, double pfd)> MaskProfile(double fixedCoord, bool alongY)
    {
        const int N = 1441;
        var result = new List<(double, double)>(N);
        double lo = alongY ? YMin : XMin;
        double hi = alongY ? YMax : XMax;
        for (int i = 0; i < N; i++)
        {
            double p = lo + (hi - lo) * i / (N - 1);
            double v = alongY ? MaskReadRaw(fixedCoord, p) : MaskReadRaw(p, fixedCoord);
            if (v <= UnreachableCutoffDb) continue;
            result.Add((p, Math.Max(v, PfdFloor)));
        }
        return result;
    }

    /// <summary>
    /// Bracketing node indices for v (nodes ascending); lo == hi at and
    /// beyond the table edges, which makes the interpolation clamp there.
    /// </summary>
    private static (int lo, int hi) Bracket(IReadOnlyList<double> nodes, double v)
    {
        int last = nodes.Count - 1;
        if (v <= nodes[0]) return (0, 0);
        if (v >= nodes[last]) return (last, last);
        int lo = 0, hi = last;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (nodes[mid] <= v) lo = mid; else hi = mid;
        }
        return (lo, hi);
    }

    /// <summary>
    /// Linear interpolation clamped outside [x1, x2] -- transcribed from the
    /// reference maskdata Helper.ClampedLinear (S.1503-4 Sec. D5.1.5 /
    /// Sec. D5.2.7).
    /// </summary>
    private static double ClampedLinear(double x, double x1, double y1, double x2, double y2)
    {
        if (x < x1) return y1;
        if (x > x2) return y2;
        if (x1 - x2 == 0.0) return y1;
        double f1 = (y1 - y2) / (x1 - x2);
        double f2 = (x1 * y2 - x2 * y1) / (x1 - x2);
        return f1 * x + f2;
    }

    /// <summary>
    /// Re-generate the grids. A field with a mask source re-rasterises the
    /// exact table (plot-time rasterisation); an external-only field with no
    /// source yet clears; otherwise the grids are computed from the scene.
    /// </summary>
    public void Rebuild(PfdMaskViewModel vm)
    {
        if (_maskBlock != null) { RasterizeMaskSource(); return; }
        if (ExternalOnly)
        {
            PixW = 0; PixH = 0;
            PfdGrid = null; AlphaGrid = null; EsElevGrid = null;
            HasValidRange = false;
            return;
        }
        Kind = vm.MaskKind;
        if (Kind == MaskPlotKind.AlphaDeltaLong) RebuildAlphaDelta(vm);
        else RebuildAzEl(vm);
    }

    private void RebuildAzEl(PfdMaskViewModel vm)
    {
        XMin = -90.0; XMax = 90.0;
        YMin = -90.0; YMax = 90.0;

        var scene = vm.Scene;
        double R = EarthRadiusKm;
        double rSat = R + scene.AltitudeKm;
        var sat = scene.SatEcef;
        var (north, east, down) = SatNedBasis(scene.SubSatLatDeg, scene.SubSatLonDeg);

        var powers = BeamPowersDbw(vm);
        var agg = vm.Aggregation;
        int clusterN = vm.ReuseClusterSize;
        var colors = agg == PfdAggregation.CoChannelSum ? BeamReuseColors(scene.Beams, clusterN) : Array.Empty<int>();

        // Visibility cap: look rays past the horizon miss the Earth. Off-nadir at
        // the horizon = asin(R/(R+h)); we skip pixels with cos(az)*cos(el) < that.
        double cosOffNadirHorizon = Math.Cos(Math.Asin(R / rSat));

        // Pixel grid scales with the user's MaskStepDeg over the full +/-90 deg square.
        double step = Math.Clamp(vm.MaskStepDeg, 0.1, 5.0);
        int pixW = Math.Max(32, (int)Math.Round(180.0 / step));
        int pixH = Math.Max(32, (int)Math.Round(180.0 / step));

        var pfdBuf = new double[pixW * pixH];
        var alphaBuf = new double[pixW * pixH];
        for (int i = 0; i < pfdBuf.Length; i++) { pfdBuf[i] = double.NegativeInfinity; alphaBuf[i] = 180.0; }

        double dPix = 180.0 / pixW;    // deg / pixel -- square pixels

        // Parallelise by el row. Each pixel is independent; no shared writes.
        Parallel.For(0, pixH, py =>
        {
            // Row 0 = top of plot = el = +90 deg (North); row (pixH-1) = el = -90 deg.
            double elDeg = 90.0 - (py + 0.5) * dPix;
            double elRad = elDeg * Math.PI / 180.0;
            double sinEl = Math.Sin(elRad);
            double cosEl = Math.Cos(elRad);
            int rowBase = py * pixW;

            for (int px = 0; px < pixW; px++)
            {
                double azDeg = -90.0 + (px + 0.5) * dPix;
                double azRad = azDeg * Math.PI / 180.0;
                double sinAz = Math.Sin(azRad);
                double cosAz = Math.Cos(azRad);

                double cosOffNadir = cosAz * cosEl;
                if (cosOffNadir < cosOffNadirHorizon) continue;    // past the horizon

                // Look direction in sat NED: North = sin(el), East = sin(az)*cos(el),
                // Down = cos(az)*cos(el). Matches CalculateIntersectionWithEarth Sec. D6.4.5.
                var lookNed = new Vec3(sinEl, sinAz * cosEl, cosOffNadir);
                var look = NedToEcef(lookNed, north, east, down).Normalized();

                var hit = RaySphereHit(sat, look);
                if (hit is null) continue;
                var ground = hit.Value;

                // No eps_min gate here: side lobes of the active beams radiate onto
                // ground below the served min-elevation too, and the PFD mask must
                // show that. eps_min only gates which beams are ON (SceneModel /
                // PfdMaskViewModel), not where PFD is evaluated.
                double e = agg == PfdAggregation.CoChannelSum
                    ? BeamComposer.MaxCoChannelEirpDbw(scene.Beams, look, powers, colors, clusterN)
                    : BeamComposer.CompositeEirpDbw(scene.Beams, look, powers);
                if (double.IsNegativeInfinity(e)) continue;

                double slantM = (ground - sat).Length * 1000.0;
                double pathLossDb = 10.0 * Math.Log10(4.0 * Math.PI * slantM * slantM);
                double pfd = e - pathLossDb;

                double alphaDeg = GsoGeometry.AlphaMinAbsDeg(ground, sat);

                int idx = rowBase + px;
                pfdBuf[idx] = pfd;
                alphaBuf[idx] = alphaDeg;
            }
        });

        // Peak injection: a pixel grid can straddle a narrow main lobe and clip
        // the mask maximum. Each active beam's exact boresight is evaluated and
        // max-binned into its containing pixel -- the mask is an envelope, so
        // taking the in-cell max is the conservative (correct) reading.
        foreach (var (look, ground, pfd) in BoresightPeakSamples(vm, powers, colors, clusterN))
        {
            double azDeg = Math.Atan2(Vec3.Dot(look, east), Vec3.Dot(look, down)) * 180.0 / Math.PI;
            double elDeg = Math.Asin(Math.Clamp(Vec3.Dot(look, north), -1.0, 1.0)) * 180.0 / Math.PI;
            int px = (int)((azDeg - XMin) / (XMax - XMin) * pixW);
            if (px < 0) px = 0; else if (px >= pixW) px = pixW - 1;
            int py = (int)((YMax - elDeg) / (YMax - YMin) * pixH);
            if (py < 0) py = 0; else if (py >= pixH) py = pixH - 1;
            int idx = py * pixW + px;
            if (pfd > pfdBuf[idx])
            {
                pfdBuf[idx] = pfd;
                alphaBuf[idx] = GsoGeometry.AlphaMinAbsDeg(ground, sat);
            }
        }

        AutoScale(pfdBuf);
        PixW = pixW;
        PixH = pixH;
        PfdGrid = pfdBuf;
        AlphaGrid = alphaBuf;
        EsElevGrid = null;      // AzEl guides use the closed-form geometry instead
    }

    private void RebuildAlphaDelta(PfdMaskViewModel vm)
    {
        XMin = -180.0; XMax = 180.0;   // deltaLongitude
        YMin = -90.0;  YMax = 90.0;    // signed alpha

        var scene = vm.Scene;
        double R = EarthRadiusKm;
        double rSat = R + scene.AltitudeKm;
        var sat = scene.SatEcef;
        var (north, east, down) = SatNedBasis(scene.SubSatLatDeg, scene.SubSatLonDeg);

        var powers = BeamPowersDbw(vm);
        var agg = vm.Aggregation;
        int clusterN = vm.ReuseClusterSize;
        var colors = agg == PfdAggregation.CoChannelSum ? BeamReuseColors(scene.Beams, clusterN) : Array.Empty<int>();
        double subLonDeg = scene.SubSatLonDeg;
        double cosOffNadirHorizon = Math.Cos(Math.Asin(R / rSat));

        double step = Math.Clamp(vm.MaskStepDeg, 0.1, 5.0);
        double xRange = XMax - XMin, yRange = YMax - YMin;
        int pixW = Math.Max(64, (int)Math.Round(xRange / step));
        int pixH = Math.Max(32, (int)Math.Round(yRange / step));

        var pfdBuf = new double[pixW * pixH];
        var esElevBuf = new double[pixW * pixH];
        for (int i = 0; i < pfdBuf.Length; i++) { pfdBuf[i] = double.NegativeInfinity; esElevBuf[i] = double.NaN; }

        // Source sweep over the sat-frame look grid at half the mask step --
        // the forward map (az, el) -> (dL, alpha) is smooth but non-uniform, so
        // oversampling the source reduces holes in the target bins.
        double srcStep = 0.5 * step;
        int nSrc = Math.Max(64, (int)Math.Round(180.0 / srcStep));
        double dSrc = 180.0 / nSrc;

        object binLock = new();

        Parallel.For(0, nSrc, iEl =>
        {
            double elDeg = 90.0 - (iEl + 0.5) * dSrc;
            double elRad = elDeg * Math.PI / 180.0;
            double sinEl = Math.Sin(elRad);
            double cosEl = Math.Cos(elRad);

            for (int iAz = 0; iAz < nSrc; iAz++)
            {
                double azDeg = -90.0 + (iAz + 0.5) * dSrc;
                double azRad = azDeg * Math.PI / 180.0;
                double sinAz = Math.Sin(azRad);
                double cosAz = Math.Cos(azRad);

                double cosOffNadir = cosAz * cosEl;
                if (cosOffNadir < cosOffNadirHorizon) continue;    // past the horizon

                var lookNed = new Vec3(sinEl, sinAz * cosEl, cosOffNadir);
                var look = NedToEcef(lookNed, north, east, down).Normalized();

                var hit = RaySphereHit(sat, look);
                if (hit is null) continue;
                var ground = hit.Value;

                double e = agg == PfdAggregation.CoChannelSum
                    ? BeamComposer.MaxCoChannelEirpDbw(scene.Beams, look, powers, colors, clusterN)
                    : BeamComposer.CompositeEirpDbw(scene.Beams, look, powers);
                if (double.IsNegativeInfinity(e)) continue;

                double slantM = (ground - sat).Length * 1000.0;
                double pathLossDb = 10.0 * Math.Log10(4.0 * Math.PI * slantM * slantM);
                double pfd = e - pathLossDb;

                // Signed alpha + minimising GSO arc longitude at this ground point;
                // dL = NGSO sub-longitude - GSO longitude, wrapped to +/-180 deg
                // (order per the reference GetDeltaLongitudeDeg).
                var ad = GsoGeometry.AlphaSignedDeg(ground, sat);
                if (ad is null) continue;              // GSO arc not visible from this ES
                double alpha = ad.Value.alphaDeg;
                double dLon = ((subLonDeg - ad.Value.gsoLonDeg + 540.0) % 360.0) - 180.0;
                double esElev = ElevationAngleDeg(sat, ground);

                int px = (int)((dLon - XMin) / xRange * pixW);
                if (px < 0) px = 0; else if (px >= pixW) px = pixW - 1;
                int py = (int)((YMax - alpha) / yRange * pixH);
                if (py < 0) py = 0; else if (py >= pixH) py = pixH - 1;
                int idx = py * pixW + px;

                // Mask semantics: max PFD over all ground points sharing (alpha, dL).
                // The winning sample also stamps its ES elevation for the eps guides.
                lock (binLock)
                {
                    if (pfd > pfdBuf[idx]) { pfdBuf[idx] = pfd; esElevBuf[idx] = esElev; }
                }
            }
        });

        // Peak injection -- same rationale as in RebuildAzEl: the source sweep
        // can straddle a narrow main lobe, so each active beam's exact boresight
        // is max-binned into its (dL, alpha) cell.
        foreach (var (look, ground, pfd) in BoresightPeakSamples(vm, powers, colors, clusterN))
        {
            var ad = GsoGeometry.AlphaSignedDeg(ground, sat);
            if (ad is null) continue;
            double dLon = ((subLonDeg - ad.Value.gsoLonDeg + 540.0) % 360.0) - 180.0;
            int px = (int)((dLon - XMin) / xRange * pixW);
            if (px < 0) px = 0; else if (px >= pixW) px = pixW - 1;
            int py = (int)((YMax - ad.Value.alphaDeg) / yRange * pixH);
            if (py < 0) py = 0; else if (py >= pixH) py = pixH - 1;
            int idx = py * pixW + px;
            if (pfd > pfdBuf[idx])
            {
                pfdBuf[idx] = pfd;
                esElevBuf[idx] = ElevationAngleDeg(sat, ground);
            }
        }

        // In this coordinate system alpha is the pixel's own Y coordinate -- fill the
        // alpha grid row-wise so the heatmap's |alpha| < alpha_excl tint works unchanged.
        var alphaBuf = new double[pixW * pixH];
        double dPixY = yRange / pixH;
        for (int py = 0; py < pixH; py++)
        {
            double alphaRow = Math.Abs(YMax - (py + 0.5) * dPixY);
            int rowBase = py * pixW;
            for (int px = 0; px < pixW; px++) alphaBuf[rowBase + px] = alphaRow;
        }

        AutoScale(pfdBuf);
        PixW = pixW;
        PixH = pixH;
        PfdGrid = pfdBuf;
        AlphaGrid = alphaBuf;
        EsElevGrid = esElevBuf;
    }

    /// <summary>
    /// Exact-boresight PFD samples, one per active beam (Weight &gt; 0): the
    /// aggregate PFD evaluated at the beam's own boresight look direction --
    /// the local peak a finite grid can otherwise miss. Callers max-bin each
    /// sample into the containing cell of their coordinate system.
    /// </summary>
    private static List<(Vec3 look, Vec3 ground, double pfd)> BoresightPeakSamples(
        PfdMaskViewModel vm, double[] powers, int[] colors, int clusterN)
    {
        var scene = vm.Scene;
        var sat = scene.SatEcef;
        var agg = vm.Aggregation;
        var result = new List<(Vec3, Vec3, double)>();
        foreach (var beam in scene.Beams)
        {
            if (beam.Weight <= 0.0) continue;
            var look = beam.Boresight.Normalized();
            var hit = RaySphereHit(sat, look);
            if (hit is null) continue;
            double e = agg == PfdAggregation.CoChannelSum
                ? BeamComposer.MaxCoChannelEirpDbw(scene.Beams, look, powers, colors, clusterN)
                : BeamComposer.CompositeEirpDbw(scene.Beams, look, powers);
            if (double.IsNegativeInfinity(e)) continue;
            double slantM = (hit.Value - sat).Length * 1000.0;
            double pfd = e - 10.0 * Math.Log10(4.0 * Math.PI * slantM * slantM);
            result.Add((look, hit.Value, pfd));
        }
        return result;
    }

    /// <summary>
    /// Per-beam transmit powers (dBW in refBW), index-aligned with the scene's
    /// beam list. ConstantEirp: TxEirpDbw for every beam. ConstantBoresightPfd:
    /// TxEirpDbw + 20*log10(boresight slant / altitude) -- spreading-loss
    /// compensation so every beam's boresight PFD matches a nadir beam's.
    /// </summary>
    public static double[] BeamPowersDbw(PfdMaskViewModel vm)
    {
        var scene = vm.Scene;
        var beams = scene.Beams;
        var powers = new double[beams.Count];

        if (vm.PowerMode == BeamPowerMode.ConstantEirp)
        {
            for (int i = 0; i < powers.Length; i++) powers[i] = vm.TxEirpDbw;
            return powers;
        }

        var sat = scene.SatEcef;
        double refKm = Math.Max(1e-6, scene.AltitudeKm);   // nadir slant = altitude
        for (int i = 0; i < powers.Length; i++)
        {
            var hit = RaySphereHit(sat, beams[i].Boresight);
            double slantKm = hit is null ? refKm : (hit.Value - sat).Length;
            powers[i] = vm.TxEirpDbw + 20.0 * Math.Log10(Math.Max(1e-6, slantKm) / refKm);
        }
        return powers;
    }

    /// <summary>
    /// Frequency-reuse colour per beam, index-aligned with the beam list.
    /// Hex-lattice beams are coloured from their axial indices via
    /// <see cref="BeamComposer.HexReuseColor"/>; beams without lattice
    /// coordinates (manual ring layouts -- not reachable from this tab) fall
    /// back to index % N.
    /// </summary>
    private static int[] BeamReuseColors(IReadOnlyList<Beam> beams, int n)
        => BeamComposer.ReuseColors(beams, n);

    /// <summary>Auto-scale the colour ramp to the actual PFD range, skipping empty pixels.</summary>
    private void AutoScale(double[] pfdBuf)
    {
        double dataMin = double.PositiveInfinity;
        double dataMax = double.NegativeInfinity;
        for (int i = 0; i < pfdBuf.Length; i++)
        {
            double v = pfdBuf[i];
            if (double.IsNegativeInfinity(v)) continue;
            if (v < dataMin) dataMin = v;
            if (v > dataMax) dataMax = v;
        }
        if (double.IsPositiveInfinity(dataMin))
        {
            // No valid pixels -- plausible if the sat is fully below the horizon
            // for every look direction. Leave a sensible fallback so the legend
            // still shows something.
            PfdFloor = -180.0; PfdCeil = -100.0; HasValidRange = false;
        }
        else if (dataMax - dataMin < 1.0)
        {
            // Flat data -- pad +/-10 dB either side so the colour ramp isn't degenerate.
            PfdFloor = dataMin - 10.0; PfdCeil = dataMax + 10.0; HasValidRange = true;
        }
        else
        {
            PfdFloor = dataMin; PfdCeil = dataMax; HasValidRange = true;
        }
    }

    /// <summary>
    /// PFD-vs-Y slice at the given X coordinate (nearest column): PFD vs mask
    /// elevation at an azimuth (AzEl) or PFD vs alpha at a deltaLongitude
    /// (AlphaDeltaLong). Returns finite (yDeg, pfd) samples ordered by
    /// ascending Y. Empty until a grid has been built.
    /// </summary>
    public List<(double yDeg, double pfd)> ProfileAtX(double xDeg)
    {
        if (_maskBlock != null && Kind == MaskPlotKind.AzEl)
            return MaskProfile(xDeg, alongY: true);      // exact table read

        var result = new List<(double, double)>();
        if (PfdGrid is null || PixW == 0 || PixH == 0) return result;

        int col = ColumnForX(xDeg);
        double dPixY = (YMax - YMin) / PixH;
        // Walk rows bottom-up so Y ascends.
        for (int py = PixH - 1; py >= 0; py--)
        {
            double pfd = PfdGrid[py * PixW + col];
            if (double.IsNegativeInfinity(pfd)) continue;
            double yDeg = YMax - (py + 0.5) * dPixY;
            result.Add((yDeg, pfd));
        }
        return result;
    }

    /// <summary>
    /// |alpha|-vs-Y slice at the given X coordinate, over the same pixels that
    /// carry PFD data. Returns (yDeg, alphaDeg) ordered by ascending Y. Used
    /// by the AzEl profile's alpha-exclusion guides (in AlphaDeltaLong the alpha is
    /// the Y axis itself, so the guides are just Y = +/-alpha_excl).
    /// </summary>
    public List<(double yDeg, double alphaDeg)> AlphaProfileAtX(double xDeg)
    {
        var result = new List<(double, double)>();
        if (AlphaGrid is null || PfdGrid is null || PixW == 0 || PixH == 0) return result;

        int col = ColumnForX(xDeg);
        double dPixY = (YMax - YMin) / PixH;
        for (int py = PixH - 1; py >= 0; py--)
        {
            int idx = py * PixW + col;
            if (double.IsNegativeInfinity(PfdGrid[idx])) continue;   // only where the slice has data
            double yDeg = YMax - (py + 0.5) * dPixY;
            result.Add((yDeg, AlphaGrid[idx]));
        }
        return result;
    }

    /// <summary>
    /// PFD-vs-X slice at the given Y coordinate (nearest row): PFD vs
    /// deltaLongitude at a fixed signed alpha -- one row of the ITU mask table.
    /// Returns finite (xDeg, pfd) samples ordered by ascending X. Used by the
    /// alpha/deltaLongitude profile (horizontal cut).
    /// </summary>
    public List<(double xDeg, double pfd)> ProfileAtY(double yDeg)
    {
        if (_maskBlock != null && Kind == MaskPlotKind.AlphaDeltaLong)
            return MaskProfile(yDeg, alongY: false);     // exact table read

        var result = new List<(double, double)>();
        if (PfdGrid is null || PixW == 0 || PixH == 0) return result;

        int row = RowForY(yDeg);
        double dPixX = (XMax - XMin) / PixW;
        int rowBase = row * PixW;
        for (int px = 0; px < PixW; px++)
        {
            double pfd = PfdGrid[rowBase + px];
            if (double.IsNegativeInfinity(pfd)) continue;
            double xDeg = XMin + (px + 0.5) * dPixX;
            result.Add((xDeg, pfd));
        }
        return result;
    }

    /// <summary>
    /// ES-elevation-vs-X slice at the given Y coordinate, over the same pixels
    /// that carry PFD data. Returns (xDeg, esElevDeg) ordered by ascending X.
    /// Empty in AzEl mode (no <see cref="EsElevGrid"/>) or before a rebuild.
    /// Drives the data-driven eps guides on the alpha/deltaLongitude profile.
    /// </summary>
    public List<(double xDeg, double esElevDeg)> EsElevProfileAtY(double yDeg)
    {
        var result = new List<(double, double)>();
        if (EsElevGrid is null || PfdGrid is null || PixW == 0 || PixH == 0) return result;

        int row = RowForY(yDeg);
        double dPixX = (XMax - XMin) / PixW;
        int rowBase = row * PixW;
        for (int px = 0; px < PixW; px++)
        {
            int idx = rowBase + px;
            if (double.IsNegativeInfinity(PfdGrid[idx])) continue;   // only where the slice has data
            if (double.IsNaN(EsElevGrid[idx])) continue;
            double xDeg = XMin + (px + 0.5) * dPixX;
            result.Add((xDeg, EsElevGrid[idx]));
        }
        return result;
    }

    /// <summary>
    /// PFD (dB(W/m^2)) at the nearest grid cell to (xDeg, yDeg) in the field's
    /// own coordinates (X, Y). Returns <see cref="double.NegativeInfinity"/>
    /// where the cell has no data (out-of-disc / unreachable). Used by the XML
    /// mask exporter to read arbitrary (b, c) nodes off the computed grid.
    /// </summary>
    public double SampleAt(double xDeg, double yDeg)
    {
        if (_maskBlock != null)
        {
            double v = MaskReadRaw(xDeg, yDeg);          // exact table read
            return v <= UnreachableCutoffDb ? double.NegativeInfinity : v;
        }
        if (PfdGrid is null || PixW == 0 || PixH == 0) return double.NegativeInfinity;
        int col = ColumnForX(xDeg);
        int row = (int)((YMax - yDeg) / (YMax - YMin) * PixH);
        if (row < 0) row = 0; else if (row >= PixH) row = PixH - 1;
        return PfdGrid[row * PixW + col];
    }

    /// <summary>
    /// Envelope read for the mask exporter: maximum PFD over all grid cells
    /// whose centres fall inside [x-halfW, x+halfW] x [y-halfH, y+halfH] (the
    /// output node's bin). With bins tiling the axes, every field cell --
    /// including the injected boresight peaks -- is read by at least one
    /// output node regardless of the output step; a coarse step makes the
    /// mask conservative, never under-reporting. Falls back to the nearest
    /// cell when the bin is narrower than the grid pitch.
    /// </summary>
    public double SampleMaxIn(double xDeg, double yDeg, double halfW, double halfH)
    {
        if (PfdGrid is null || PixW == 0 || PixH == 0) return double.NegativeInfinity;
        double dx = (XMax - XMin) / PixW;
        double dy = (YMax - YMin) / PixH;
        int px0 = (int)Math.Ceiling((xDeg - halfW - XMin) / dx - 0.5);
        int px1 = (int)Math.Floor((xDeg + halfW - XMin) / dx - 0.5);
        int py0 = (int)Math.Ceiling((YMax - (yDeg + halfH)) / dy - 0.5);
        int py1 = (int)Math.Floor((YMax - (yDeg - halfH)) / dy - 0.5);
        if (px0 < 0) px0 = 0; if (px1 >= PixW) px1 = PixW - 1;
        if (py0 < 0) py0 = 0; if (py1 >= PixH) py1 = PixH - 1;
        if (px0 > px1 || py0 > py1) return SampleAt(xDeg, yDeg);

        double max = double.NegativeInfinity;
        for (int py = py0; py <= py1; py++)
        {
            int rowBase = py * PixW;
            for (int px = px0; px <= px1; px++)
                if (PfdGrid[rowBase + px] > max) max = PfdGrid[rowBase + px];
        }
        return max;
    }

    private int ColumnForX(double xDeg)
    {
        int col = (int)((xDeg - XMin) / (XMax - XMin) * PixW);
        if (col < 0) col = 0; else if (col >= PixW) col = PixW - 1;
        return col;
    }

    private int RowForY(double yDeg)
    {
        int row = (int)((YMax - yDeg) / (YMax - YMin) * PixH);
        if (row < 0) row = 0; else if (row >= PixH) row = PixH - 1;
        return row;
    }
}
