using System;
using System.Collections.Generic;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>
/// Projects a beam's cone onto the spherical Earth. Pure geometry (no UI), so
/// both the composite-gain map and the PFD-tab map — and any future test — can
/// share one implementation.
/// </summary>
public static class BeamFootprint
{
    /// <summary>
    /// Sample the beam's cone at half-angle θ(φ) around its boresight in 3D and
    /// project each sample to ground via ray-Earth intersection. Returns
    /// connected (lat, lon) segments — when the cone partially overshoots the
    /// horizon, missed samples split the contour into open arcs (no spurious
    /// chord across the gap).
    /// </summary>
    /// <param name="sat">Satellite position, ECEF km.</param>
    /// <param name="boresight">Beam boresight unit vector, ECEF.</param>
    /// <param name="radialAxis">
    /// φ = 0 axis ⊥ boresight (elliptical patterns). Null → pick any ⊥ axis.
    /// </param>
    /// <param name="halfAngleAtPhiDeg">Cone half-angle (deg) as a function of φ (deg).</param>
    /// <param name="samples">Number of azimuthal samples around the cone.</param>
    public static List<List<(double lat, double lon)>> SampleConeOnGround(
        Vec3 sat, Vec3 boresight, Vec3? radialAxis, Func<double, double> halfAngleAtPhiDeg, int samples)
    {
        var segments = new List<List<(double lat, double lon)>>();
        var b = boresight;

        Vec3 e1;
        if (radialAxis is Vec3 rax)
        {
            e1 = rax;
        }
        else
        {
            var ref0 = (Math.Abs(b.Z) < 0.9) ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
            var c = Vec3.Cross(b, ref0);
            double clen = Math.Sqrt(Vec3.Dot(c, c));
            if (clen < 1e-9) return segments;
            e1 = new Vec3(c.X / clen, c.Y / clen, c.Z / clen);
        }
        var e2 = Vec3.Cross(b, e1);

        var hits = new (double lat, double lon)?[samples];
        for (int i = 0; i < samples; i++)
        {
            double phiDeg = 360.0 * i / samples;
            double phi = phiDeg * Math.PI / 180.0;
            double halfAngleDeg = halfAngleAtPhiDeg(phiDeg);
            double cs = Math.Cos(halfAngleDeg * Math.PI / 180.0);
            double sn = Math.Sin(halfAngleDeg * Math.PI / 180.0);
            double cp = Math.Cos(phi), sp = Math.Sin(phi);
            var d = new Vec3(
                b.X * cs + e1.X * sn * cp + e2.X * sn * sp,
                b.Y * cs + e1.Y * sn * cp + e2.Y * sn * sp,
                b.Z * cs + e1.Z * sn * cp + e2.Z * sn * sp);
            var hit = RaySphereHit(sat, d);
            if (hit is null) hits[i] = null;
            else
            {
                var (lat, lon, _) = EcefToGeodetic(hit.Value);
                hits[i] = (lat, lon);
            }
        }

        bool anyMiss = false;
        foreach (var h in hits) if (h is null) { anyMiss = true; break; }
        if (!anyMiss)
        {
            var seg = new List<(double, double)>(samples + 1);
            for (int i = 0; i < samples; i++) seg.Add(hits[i]!.Value);
            seg.Add(hits[0]!.Value);
            segments.Add(seg);
            return segments;
        }

        int firstMiss = -1;
        for (int k = 0; k < samples; k++) if (hits[k] is null) { firstMiss = k; break; }
        int start = (firstMiss + 1) % samples;

        List<(double, double)>? cur = null;
        for (int k = 0; k < samples; k++)
        {
            int idx = (start + k) % samples;
            var h = hits[idx];
            if (h is null) { cur = null; continue; }
            if (cur is null) { cur = new List<(double, double)>(); segments.Add(cur); }
            cur.Add(h.Value);
        }
        return segments;
    }
}
