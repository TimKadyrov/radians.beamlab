using System;

namespace radians.beamlab.app;

/// <summary>
/// Piecewise-linear RGB colour ramp over t ∈ [0, 1] defined by anchor stops
/// (t, r, g, b) with channel values in [0, 1]. Below the first stop clamps to
/// the first colour; at/above the last stop clamps to the last. Shared by the
/// map gain heatmap and the PFD az/el heatmap.
/// </summary>
public sealed class ColorRamp
{
    private readonly (double t, double r, double g, double b)[] _stops;

    public ColorRamp((double t, double r, double g, double b)[] stops) => _stops = stops;

    /// <summary>Sample the ramp at <paramref name="t"/>, returning 8-bit RGB.</summary>
    public void Sample(double t, out byte r, out byte g, out byte b)
    {
        var stops = _stops;
        for (int i = 0; i < stops.Length - 1; i++)
        {
            if (t <= stops[i + 1].t)
            {
                double f = (t - stops[i].t) / (stops[i + 1].t - stops[i].t);
                double rr = stops[i].r + f * (stops[i + 1].r - stops[i].r);
                double gg = stops[i].g + f * (stops[i + 1].g - stops[i].g);
                double bb = stops[i].b + f * (stops[i + 1].b - stops[i].b);
                r = (byte)Math.Clamp(rr * 255, 0, 255);
                g = (byte)Math.Clamp(gg * 255, 0, 255);
                b = (byte)Math.Clamp(bb * 255, 0, 255);
                return;
            }
        }
        r = g = b = 255;
    }

    /// <summary>Viridis-ish: dark blue → teal → green → yellow → white (t: 0 → 1). Map gain heatmap.</summary>
    public static readonly ColorRamp Gain = new(new (double, double, double, double)[]
    {
        (0.00, 0.10, 0.05, 0.30),
        (0.25, 0.10, 0.45, 0.65),
        (0.50, 0.20, 0.75, 0.45),
        (0.75, 0.95, 0.85, 0.20),
        (1.00, 1.00, 1.00, 0.95),
    });

    /// <summary>Red (lowest PFD) → orange → yellow → chartreuse → green (highest PFD).</summary>
    public static readonly ColorRamp Pfd = new(new (double, double, double, double)[]
    {
        (0.00, 0.78, 0.16, 0.16),   // deep red
        (0.25, 0.94, 0.51, 0.12),   // orange
        (0.50, 0.94, 0.86, 0.20),   // yellow
        (0.75, 0.55, 0.78, 0.24),   // chartreuse
        (1.00, 0.16, 0.71, 0.31),   // green
    });
}
