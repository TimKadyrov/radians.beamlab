using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Data model for the PFD-mask heatmap: samples aggregate PFD on a 2-D mask
/// grid and holds the retained grids so the companion profile plot can slice
/// columns without re-rasterising. No WPF — the view (<see cref="PfdHeatmapRenderer"/>)
/// turns these grids into a bitmap and the profile plot reads the slices.
///
/// Two coordinate systems (<see cref="MaskPlotKind"/>):
///
/// <b>AzEl</b> (S.1503-4 §D6.4.5): X = sat-frame azimuth (from nadir toward
/// East), Y = sat-frame elevation (out of the East-Down plane toward North),
/// both ±90°. Each pixel is one look direction; its ray-Earth intersection
/// gives the ground point whose aggregate PFD is stored. One sample per pixel.
///
/// <b>AlphaDeltaLong</b> (S.1503-4 §D6.4.4): X = ΔLongitude (NGSO sub-sat
/// longitude − longitude of the α-minimising GSO arc point, wrapped ±180°),
/// Y = signed α (±90°). The visible disc is swept in look directions and each
/// ground point's PFD is max-binned into its (ΔL, α) cell — the ITU mask is
/// the maximum PFD over all ground points sharing (α, ΔL). Cells never hit
/// stay <see cref="double.NegativeInfinity"/> (the mask's −1000 analogue).
///
/// The whole visible disc is sampled — including ground below the served
/// min-elevation, where side lobes of the active beams still radiate. Only
/// look directions past the horizon are excluded.
/// </summary>
public sealed class PfdMaskField
{
    /// <summary>Coordinate system of the current grids.</summary>
    public MaskPlotKind Kind { get; private set; } = MaskPlotKind.AzEl;

    /// <summary>X axis lower bound (deg): −90 (azimuth) or −180 (ΔLongitude).</summary>
    public double XMin { get; private set; } = -90.0;
    /// <summary>X axis upper bound (deg).</summary>
    public double XMax { get; private set; } = 90.0;
    /// <summary>Y axis lower bound (deg): −90 for both elevation and α.</summary>
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
    /// Row-major |α| grid (deg) used for the exclusion tint. AzEl: |α| to the
    /// nearest visible GSO satellite at the pixel's ground point (180 where no
    /// data). AlphaDeltaLong: the |α| of the pixel's own Y coordinate.
    /// </summary>
    public double[]? AlphaGrid { get; private set; }
    /// <summary>
    /// Row-major earth-station elevation grid (deg): the ES elevation of the
    /// ground point whose PFD won each cell's max-binning. AlphaDeltaLong only
    /// (null in AzEl mode, where the ES-elevation guides have a closed form);
    /// NaN where no data. Drives the data-driven ε guides on the profile plot.
    /// </summary>
    public double[]? EsElevGrid { get; private set; }

    /// <summary>Auto-scaled colour-ramp lower bound from the last rebuild.</summary>
    public double PfdFloor { get; private set; } = -180.0;
    /// <summary>Auto-scaled colour-ramp upper bound from the last rebuild.</summary>
    public double PfdCeil { get; private set; } = -100.0;
    /// <summary>True iff the last rebuild produced any valid PFD samples.</summary>
    public bool HasValidRange { get; private set; }

    /// <summary>Re-sample the whole grid from the current view-model state.</summary>
    public void Rebuild(PfdMaskViewModel vm)
    {
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
        int kColors = vm.ReuseColorsK;
        var colors = agg == PfdAggregation.CoChannelSum ? BeamReuseColors(scene.Beams, kColors) : Array.Empty<int>();

        // Visibility cap: look rays past the horizon miss the Earth. Off-nadir at
        // the horizon = asin(R/(R+h)); we skip pixels with cos(az)·cos(el) < that.
        double cosOffNadirHorizon = Math.Cos(Math.Asin(R / rSat));

        // Pixel grid scales with the user's MaskStepDeg over the full ±90° square.
        double step = Math.Clamp(vm.MaskStepDeg, 0.1, 5.0);
        int pixW = Math.Max(32, (int)Math.Round(180.0 / step));
        int pixH = Math.Max(32, (int)Math.Round(180.0 / step));

        var pfdBuf = new double[pixW * pixH];
        var alphaBuf = new double[pixW * pixH];
        for (int i = 0; i < pfdBuf.Length; i++) { pfdBuf[i] = double.NegativeInfinity; alphaBuf[i] = 180.0; }

        double dPix = 180.0 / pixW;    // deg / pixel — square pixels

        // Parallelise by el row. Each pixel is independent; no shared writes.
        Parallel.For(0, pixH, py =>
        {
            // Row 0 = top of plot = el = +90° (North); row (pixH-1) = el = −90°.
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

                // Look direction in sat NED: North = sin(el), East = sin(az)·cos(el),
                // Down = cos(az)·cos(el). Matches CalculateIntersectionWithEarth §D6.4.5.
                var lookNed = new Vec3(sinEl, sinAz * cosEl, cosOffNadir);
                var look = NedToEcef(lookNed, north, east, down).Normalized();

                var hit = RaySphereHit(sat, look);
                if (hit is null) continue;
                var ground = hit.Value;

                // No ε_min gate here: side lobes of the active beams radiate onto
                // ground below the served min-elevation too, and the PFD mask must
                // show that. ε_min only gates which beams are ON (SceneModel /
                // PfdMaskViewModel), not where PFD is evaluated.
                double e = agg == PfdAggregation.CoChannelSum
                    ? BeamComposer.MaxCoChannelEirpDbw(scene.Beams, look, powers, colors, kColors)
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

        AutoScale(pfdBuf);
        PixW = pixW;
        PixH = pixH;
        PfdGrid = pfdBuf;
        AlphaGrid = alphaBuf;
        EsElevGrid = null;      // AzEl guides use the closed-form geometry instead
    }

    private void RebuildAlphaDelta(PfdMaskViewModel vm)
    {
        XMin = -180.0; XMax = 180.0;   // ΔLongitude
        YMin = -90.0;  YMax = 90.0;    // signed α

        var scene = vm.Scene;
        double R = EarthRadiusKm;
        double rSat = R + scene.AltitudeKm;
        var sat = scene.SatEcef;
        var (north, east, down) = SatNedBasis(scene.SubSatLatDeg, scene.SubSatLonDeg);

        var powers = BeamPowersDbw(vm);
        var agg = vm.Aggregation;
        int kColors = vm.ReuseColorsK;
        var colors = agg == PfdAggregation.CoChannelSum ? BeamReuseColors(scene.Beams, kColors) : Array.Empty<int>();
        double subLonDeg = scene.SubSatLonDeg;
        double cosOffNadirHorizon = Math.Cos(Math.Asin(R / rSat));

        double step = Math.Clamp(vm.MaskStepDeg, 0.1, 5.0);
        double xRange = XMax - XMin, yRange = YMax - YMin;
        int pixW = Math.Max(64, (int)Math.Round(xRange / step));
        int pixH = Math.Max(32, (int)Math.Round(yRange / step));

        var pfdBuf = new double[pixW * pixH];
        var esElevBuf = new double[pixW * pixH];
        for (int i = 0; i < pfdBuf.Length; i++) { pfdBuf[i] = double.NegativeInfinity; esElevBuf[i] = double.NaN; }

        // Source sweep over the sat-frame look grid at half the mask step —
        // the forward map (az, el) → (ΔL, α) is smooth but non-uniform, so
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
                    ? BeamComposer.MaxCoChannelEirpDbw(scene.Beams, look, powers, colors, kColors)
                    : BeamComposer.CompositeEirpDbw(scene.Beams, look, powers);
                if (double.IsNegativeInfinity(e)) continue;

                double slantM = (ground - sat).Length * 1000.0;
                double pathLossDb = 10.0 * Math.Log10(4.0 * Math.PI * slantM * slantM);
                double pfd = e - pathLossDb;

                // Signed α + minimising GSO arc longitude at this ground point;
                // ΔL = NGSO sub-longitude − GSO longitude, wrapped to ±180°
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

                // Mask semantics: max PFD over all ground points sharing (α, ΔL).
                // The winning sample also stamps its ES elevation for the ε guides.
                lock (binLock)
                {
                    if (pfd > pfdBuf[idx]) { pfdBuf[idx] = pfd; esElevBuf[idx] = esElev; }
                }
            }
        });

        // In this coordinate system α is the pixel's own Y coordinate — fill the
        // α grid row-wise so the heatmap's |α| < α_excl tint works unchanged.
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
    /// Per-beam transmit powers (dBW in refBW), index-aligned with the scene's
    /// beam list. ConstantEirp: TxEirpDbw for every beam. ConstantBoresightPfd:
    /// TxEirpDbw + 20·log10(boresight slant / altitude) — spreading-loss
    /// compensation so every beam's boresight PFD matches a nadir beam's.
    /// </summary>
    private static double[] BeamPowersDbw(PfdMaskViewModel vm)
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
    /// coordinates (manual ring layouts — not reachable from this tab) fall
    /// back to index % K.
    /// </summary>
    private static int[] BeamReuseColors(IReadOnlyList<Beam> beams, int k)
    {
        var colors = new int[beams.Count];
        for (int i = 0; i < beams.Count; i++)
        {
            colors[i] = beams[i].LatticeI is int li && beams[i].LatticeJ is int lj
                ? BeamComposer.HexReuseColor(li, lj, k)
                : i % k;
        }
        return colors;
    }

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
            // No valid pixels — plausible if the sat is fully below the horizon
            // for every look direction. Leave a sensible fallback so the legend
            // still shows something.
            PfdFloor = -180.0; PfdCeil = -100.0; HasValidRange = false;
        }
        else if (dataMax - dataMin < 1.0)
        {
            // Flat data — pad ±10 dB either side so the colour ramp isn't degenerate.
            PfdFloor = dataMin - 10.0; PfdCeil = dataMax + 10.0; HasValidRange = true;
        }
        else
        {
            PfdFloor = dataMin; PfdCeil = dataMax; HasValidRange = true;
        }
    }

    /// <summary>
    /// PFD-vs-Y slice at the given X coordinate (nearest column): PFD vs mask
    /// elevation at an azimuth (AzEl) or PFD vs α at a ΔLongitude
    /// (AlphaDeltaLong). Returns finite (yDeg, pfd) samples ordered by
    /// ascending Y. Empty until a grid has been built.
    /// </summary>
    public List<(double yDeg, double pfd)> ProfileAtX(double xDeg)
    {
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
    /// |α|-vs-Y slice at the given X coordinate, over the same pixels that
    /// carry PFD data. Returns (yDeg, alphaDeg) ordered by ascending Y. Used
    /// by the AzEl profile's α-exclusion guides (in AlphaDeltaLong the α is
    /// the Y axis itself, so the guides are just Y = ±α_excl).
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
    /// ΔLongitude at a fixed signed α — one row of the ITU mask table.
    /// Returns finite (xDeg, pfd) samples ordered by ascending X. Used by the
    /// α/ΔLongitude profile (horizontal cut).
    /// </summary>
    public List<(double xDeg, double pfd)> ProfileAtY(double yDeg)
    {
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
    /// Drives the data-driven ε guides on the α/ΔLongitude profile.
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
    /// PFD (dB(W/m²)) at the nearest grid cell to (xDeg, yDeg) in the field's
    /// own coordinates (X, Y). Returns <see cref="double.NegativeInfinity"/>
    /// where the cell has no data (out-of-disc / unreachable). Used by the XML
    /// mask exporter to read arbitrary (b, c) nodes off the computed grid.
    /// </summary>
    public double SampleAt(double xDeg, double yDeg)
    {
        if (PfdGrid is null || PixW == 0 || PixH == 0) return double.NegativeInfinity;
        int col = ColumnForX(xDeg);
        int row = (int)((YMax - yDeg) / (YMax - YMin) * PixH);
        if (row < 0) row = 0; else if (row >= PixH) row = PixH - 1;
        return PfdGrid[row * PixW + col];
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
