using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Data model for the PFD az/el heatmap: samples aggregate PFD (and the GSO
/// avoidance angle α) on a satellite-frame az/el grid and holds the retained
/// grids so the companion elevation-profile plot can slice azimuth columns
/// without re-rasterising. No WPF — the view (<see cref="AzElPfdRenderer"/>)
/// turns these grids into a bitmap and the profile plot reads the slices.
///
/// Sampling (S.1503-4 §D6.4.5): each (az, el) pixel is a satellite-frame look
/// direction (az from nadir toward East, el out of the East-Down plane toward
/// North); the look ray is intersected with the Earth and the ground point's
/// aggregate PFD is stored. Pixels past the horizon or with earth-station
/// elevation below ε_min stay <see cref="double.NegativeInfinity"/>.
/// </summary>
public sealed class AzElPfdField
{
    /// <summary>Grid width (az pixels). 0 until the first <see cref="Rebuild"/>.</summary>
    public int PixW { get; private set; }
    /// <summary>Grid height (el pixels). 0 until the first <see cref="Rebuild"/>.</summary>
    public int PixH { get; private set; }

    /// <summary>Row-major PFD grid; NegativeInfinity where no data. Row 0 = el +90°; col 0 = az −90°.</summary>
    public double[]? PfdGrid { get; private set; }
    /// <summary>Row-major |α| grid (deg) to nearest visible GSO satellite; 180 where no data.</summary>
    public double[]? AlphaGrid { get; private set; }

    /// <summary>Auto-scaled colour-ramp lower bound from the last rebuild.</summary>
    public double PfdFloor { get; private set; } = -180.0;
    /// <summary>Auto-scaled colour-ramp upper bound from the last rebuild.</summary>
    public double PfdCeil { get; private set; } = -100.0;
    /// <summary>True iff the last rebuild produced any valid PFD samples.</summary>
    public bool HasValidRange { get; private set; }

    /// <summary>Re-sample the whole grid from the current view-model state.</summary>
    public void Rebuild(PfdMaskViewModel vm)
    {
        var scene = vm.Scene;
        double R = EarthRadiusKm;
        double rSat = R + scene.AltitudeKm;
        var sat = scene.SatEcef;
        var (north, east, down) = SatNedBasis(scene.SubSatLatDeg, scene.SubSatLonDeg);

        double eirp = vm.TxEirpDbw;
        double esMinElevDeg = vm.MinElevDeg;

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

                // User-elevation gate at the ES (still the user's ε_min knob).
                double esElev = ElevationAngleDeg(sat, ground);
                if (esElev < esMinElevDeg) continue;

                double g = BeamComposer.CompositeGainDbi(scene.Beams, look);
                if (double.IsNegativeInfinity(g)) continue;

                double slantM = (ground - sat).Length * 1000.0;
                double pathLossDb = 10.0 * Math.Log10(4.0 * Math.PI * slantM * slantM);
                double pfd = eirp + g - pathLossDb;

                double alphaDeg = GsoGeometry.AlphaMinAbsDeg(ground, sat);

                int idx = rowBase + px;
                pfdBuf[idx] = pfd;
                alphaBuf[idx] = alphaDeg;
            }
        });

        // Auto-scale colour ramp to the actual PFD range. Skip transparent pixels.
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
            // No valid pixels — plausible if the sat is fully below the ES min-elev
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

        PixW = pixW;
        PixH = pixH;
        PfdGrid = pfdBuf;
        AlphaGrid = alphaBuf;
    }

    /// <summary>
    /// PFD-vs-elevation slice at the given mask azimuth (nearest column). Returns
    /// finite (elDeg, pfd) samples ordered by ascending elevation. Empty until a
    /// grid has been built.
    /// </summary>
    public List<(double elDeg, double pfd)> ElevationProfileAtAzimuth(double azDeg)
    {
        var result = new List<(double, double)>();
        if (PfdGrid is null || PixW == 0 || PixH == 0) return result;

        int col = ColumnForAzimuth(azDeg);
        double dPix = 180.0 / PixW;
        // Walk rows bottom-up so elevation ascends from −90° to +90°.
        for (int py = PixH - 1; py >= 0; py--)
        {
            double pfd = PfdGrid[py * PixW + col];
            if (double.IsNegativeInfinity(pfd)) continue;
            double elDeg = 90.0 - (py + 0.5) * dPix;
            result.Add((elDeg, pfd));
        }
        return result;
    }

    /// <summary>
    /// |α|-vs-elevation slice at the given mask azimuth, over the same pixels that
    /// carry PFD data. Returns (elDeg, alphaDeg) ordered by ascending elevation.
    /// </summary>
    public List<(double elDeg, double alphaDeg)> AlphaProfileAtAzimuth(double azDeg)
    {
        var result = new List<(double, double)>();
        if (AlphaGrid is null || PfdGrid is null || PixW == 0 || PixH == 0) return result;

        int col = ColumnForAzimuth(azDeg);
        double dPix = 180.0 / PixW;
        for (int py = PixH - 1; py >= 0; py--)
        {
            int idx = py * PixW + col;
            if (double.IsNegativeInfinity(PfdGrid[idx])) continue;   // only where the slice has data
            double elDeg = 90.0 - (py + 0.5) * dPix;
            result.Add((elDeg, AlphaGrid[idx]));
        }
        return result;
    }

    private int ColumnForAzimuth(double azDeg)
    {
        int col = (int)((azDeg + 90.0) / 180.0 * PixW);
        if (col < 0) col = 0; else if (col >= PixW) col = PixW - 1;
        return col;
    }
}
