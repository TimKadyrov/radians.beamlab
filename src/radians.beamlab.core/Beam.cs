using System;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>
/// One beam of a multi-beam non-GSO antenna: a per-beam pattern referenced to
/// its own boresight direction (a unit Vec3 in any consistent frame), an
/// optional ID, and an on/off (or duty-cycle) weight used by the composer.
///
/// The boresight and the test direction passed to <see cref="GainDbi"/> must be
/// expressed in the same frame. Convention used by the WPF app: ECEF.
/// </summary>
public sealed class Beam
{
    public string Name { get; }

    /// <summary>Beam boresight as a unit vector (caller must normalise). Set once at construction.</summary>
    public Vec3 Boresight { get; }

    /// <summary>
    /// Single-beam pattern. Mutable so the PFD adjuster can swap in a new
    /// pattern with a reduced peak gain — that automatically shifts the
    /// off-axis pattern (and side-lobe contributions to PFD) accordingly.
    /// </summary>
    public ISinglePattern Pattern { get; set; }

    /// <summary>The peak gain this beam was originally built with — used as
    /// the reference when the PFD adjuster reduces <see cref="Pattern"/>.Gm
    /// or when "All beams ON" restores the unadjusted state.</summary>
    public double OriginalGmDbi { get; }

    /// <summary>True iff the current pattern's G_m is below the original.</summary>
    public bool IsGmAdjusted => Pattern.Gm < OriginalGmDbi - 1e-6;

    /// <summary>
    /// Activity weight in [0, 1]. 1 = always on, 0 = switched off (e.g. inside a
    /// GSO-arc avoidance zone), values in between for time-averaged duty cycle.
    /// </summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>Optional metadata: off-nadir cone angle of the beam boresight in the satellite frame, deg.</summary>
    public double OffNadirDeg { get; init; }

    /// <summary>
    /// Unit vector ⊥ <see cref="Boresight"/> defining φ = 0 (the radial / Lr direction)
    /// for elliptical patterns. For circular patterns this is unused. Conventionally the
    /// projection of the satellite-to-nadir direction onto the plane perpendicular to
    /// boresight, so radial = along the off-nadir tilt and transverse = cross-track.
    /// </summary>
    public Vec3? RadialAxisEcef { get; init; }

    public Beam(string name, Vec3 boresight, ISinglePattern pattern, double weight = 1.0)
    {
        Name = name;
        Boresight = boresight;
        Pattern = pattern;
        OriginalGmDbi = pattern.Gm;
        Weight = weight;
    }

    /// <summary>Off-axis angle (degrees) from this beam's boresight to a test unit vector.</summary>
    public double OffAxisDeg(Vec3 test)
    {
        double d = Math.Clamp(Vec3.Dot(Boresight, test), -1.0, 1.0);
        return Math.Acos(d) * 180.0 / Math.PI;
    }

    /// <summary>Per-beam gain (dBi) at the given test direction.</summary>
    public double GainDbi(Vec3 test)
    {
        double theta = OffAxisDeg(test);
        if (RadialAxisEcef is Vec3 radial)
        {
            double phi = AzimuthAroundBoresightDeg(test, radial);
            return Pattern.GainAt(theta, phi);
        }
        return Pattern.Gain(theta);
    }

    /// <summary>
    /// Azimuth (deg) of <paramref name="test"/> around <see cref="Boresight"/>, measured
    /// from <paramref name="radialRef"/> (φ = 0). Stable for test ≈ boresight (returns 0).
    /// </summary>
    public double AzimuthAroundBoresightDeg(Vec3 test, Vec3 radialRef)
    {
        // Project test onto plane ⊥ boresight, then resolve into (radialRef, transverse) basis.
        double cos = Vec3.Dot(Boresight, test);
        Vec3 perp = test - Boresight * cos;
        double pl = perp.Length;
        if (pl < 1e-12) return 0.0;
        Vec3 transverse = Vec3.Cross(Boresight, radialRef); // already unit length if inputs are unit & ⊥
        double rComp = Vec3.Dot(perp, radialRef);
        double tComp = Vec3.Dot(perp, transverse);
        return Math.Atan2(tComp, rComp) * 180.0 / Math.PI;
    }
}
