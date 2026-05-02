using System;

namespace radians.beamlab;

/// <summary>
/// Spherical-Earth orbital and pointing geometry for the multi-beam
/// non-GSO antenna tool. Earth is a sphere of radius <see cref="EarthRadiusKm"/>.
/// Frames:
///   ECEF       : Earth-centred Earth-fixed (X axis through 0N 0E, Z through North pole).
///   SatNED     : at the satellite's sub-point, axes (North, East, Down). +Down = -nadir.
/// All angles in degrees on the public surface.
/// </summary>
public static class GeoMath
{
    public const double EarthRadiusKm = 6371.0;
    private const double Deg2Rad = Math.PI / 180.0;
    private const double Rad2Deg = 180.0 / Math.PI;

    /// <summary>3D vector in ECEF, units of km.</summary>
    public readonly record struct Vec3(double X, double Y, double Z)
    {
        public double LengthSq => X * X + Y * Y + Z * Z;
        public double Length => Math.Sqrt(LengthSq);
        public Vec3 Normalized() { var l = Length; return l == 0 ? this : new(X / l, Y / l, Z / l); }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vec3 Cross(Vec3 a, Vec3 b) => new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
    }

    /// <summary>(lat,lon) in degrees + altitude in km above the surface => ECEF in km.</summary>
    public static Vec3 GeodeticToEcef(double latDeg, double lonDeg, double altKm)
    {
        double r = EarthRadiusKm + altKm;
        double lat = latDeg * Deg2Rad;
        double lon = lonDeg * Deg2Rad;
        double cl = Math.Cos(lat);
        return new Vec3(r * cl * Math.Cos(lon), r * cl * Math.Sin(lon), r * Math.Sin(lat));
    }

    /// <summary>ECEF point => (lat,lon) of the sub-point and altitude above the sphere.</summary>
    public static (double latDeg, double lonDeg, double altKm) EcefToGeodetic(Vec3 p)
    {
        double r = p.Length;
        double lat = Math.Asin(Math.Clamp(p.Z / r, -1.0, 1.0));
        double lon = Math.Atan2(p.Y, p.X);
        return (lat * Rad2Deg, lon * Rad2Deg, r - EarthRadiusKm);
    }

    /// <summary>
    /// Build the satellite local NED orthonormal basis at the sub-point of the
    /// satellite at (lat, lon). Returns (north, east, down) ECEF unit vectors.
    /// "Down" is along -position (towards Earth centre), i.e. nadir.
    /// </summary>
    public static (Vec3 north, Vec3 east, Vec3 down) SatNedBasis(double satLatDeg, double satLonDeg)
    {
        double lat = satLatDeg * Deg2Rad;
        double lon = satLonDeg * Deg2Rad;
        double sl = Math.Sin(lat), cl = Math.Cos(lat);
        double so = Math.Sin(lon), co = Math.Cos(lon);

        // Up = radial outward; Down = -Up.
        var up = new Vec3(cl * co, cl * so, sl);
        var down = new Vec3(-up.X, -up.Y, -up.Z);
        // North = derivative of position w.r.t. latitude, projected to local tangent plane.
        var north = new Vec3(-sl * co, -sl * so, cl);
        // East = North x Up (right-handed, points East at sub-point).
        var east = Vec3.Cross(north, up);
        return (north, east, down);
    }

    /// <summary>
    /// Beam direction in the satellite NED frame from off-nadir cone angle and
    /// azimuth (measured clockwise from North in the local tangent plane).
    /// </summary>
    public static Vec3 BeamDirNed(double offNadirDeg, double azFromNorthDeg)
    {
        double t = offNadirDeg * Deg2Rad;
        double a = azFromNorthDeg * Deg2Rad;
        double s = Math.Sin(t), c = Math.Cos(t);
        // Down component cos(t); horizontal component sin(t) decomposed as
        // (north = cos(az), east = sin(az)).
        return new Vec3(s * Math.Cos(a), s * Math.Sin(a), c);
    }

    /// <summary>
    /// Project a beam direction (unit vector in sat NED) to its boresight ECEF
    /// unit vector, given the satellite NED basis.
    /// </summary>
    public static Vec3 NedToEcef(Vec3 dirNed, Vec3 north, Vec3 east, Vec3 down)
        => new(
            dirNed.X * north.X + dirNed.Y * east.X + dirNed.Z * down.X,
            dirNed.X * north.Y + dirNed.Y * east.Y + dirNed.Z * down.Y,
            dirNed.X * north.Z + dirNed.Y * east.Z + dirNed.Z * down.Z);

    /// <summary>
    /// Intersect a ray (origin + t*dir, t&gt;=0) with the Earth sphere. Returns
    /// the nearest forward hit, or null if the ray misses the Earth.
    /// </summary>
    public static Vec3? RaySphereHit(Vec3 origin, Vec3 dir)
    {
        double b = Vec3.Dot(origin, dir);
        double c = origin.LengthSq - EarthRadiusKm * EarthRadiusKm;
        double disc = b * b - c;
        if (disc < 0) return null;
        double s = Math.Sqrt(disc);
        double t1 = -b - s;
        double t2 = -b + s;
        double t = t1 > 0 ? t1 : (t2 > 0 ? t2 : -1);
        if (t < 0) return null;
        return new Vec3(origin.X + dir.X * t, origin.Y + dir.Y * t, origin.Z + dir.Z * t);
    }

    /// <summary>
    /// Earth-central angle from sub-satellite point to the visible horizon.
    /// alpha = arccos(R / (R + h)). Anything inside this cap on the surface is
    /// in the line-of-sight footprint of a satellite at altitude h.
    /// </summary>
    public static double HorizonHalfAngleDeg(double altKm)
        => Math.Acos(EarthRadiusKm / (EarthRadiusKm + altKm)) * Rad2Deg;

    /// <summary>
    /// Off-nadir cone angle (deg) at the satellite from nadir to the geometric
    /// horizon: arcsin(R / (R + h)).
    /// </summary>
    public static double HorizonOffNadirDeg(double altKm)
        => Math.Asin(EarthRadiusKm / (EarthRadiusKm + altKm)) * Rad2Deg;

    /// <summary>
    /// User elevation (deg above the horizontal plane at the ground point) of
    /// the satellite as seen from <paramref name="groundEcef"/>. Returns 90°
    /// when the points coincide.
    /// </summary>
    public static double ElevationAngleDeg(Vec3 satEcef, Vec3 groundEcef)
    {
        var slant = satEcef - groundEcef;
        double slantLen = slant.Length;
        if (slantLen < 1e-9) return 90.0;
        var up = groundEcef.Normalized();
        double sinTheta = Vec3.Dot(slant, up) / slantLen;
        return Math.Asin(Math.Clamp(sinTheta, -1.0, 1.0)) * Rad2Deg;
    }

    /// <summary>
    /// Off-nadir cone angle (deg) at the satellite for a given Earth-central
    /// angle (deg) between sub-point and target. Useful to size beam fans.
    /// </summary>
    public static double OffNadirForCentralAngle(double centralDeg, double altKm)
    {
        double r = EarthRadiusKm + altKm;
        double c = centralDeg * Deg2Rad;
        // Law of sines on the Earth-centre / sat / target triangle.
        double y = EarthRadiusKm * Math.Sin(c);
        double x = r - EarthRadiusKm * Math.Cos(c);
        return Math.Atan2(y, x) * Rad2Deg;
    }

    /// <summary>
    /// Distance along the spherical Earth surface between two (lat,lon) points
    /// in great-circle (Earth-central) angle, in degrees.
    /// </summary>
    public static double GreatCircleDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double a1 = lat1 * Deg2Rad, a2 = lat2 * Deg2Rad;
        double dl = (lon2 - lon1) * Deg2Rad;
        double s = Math.Sin(a1) * Math.Sin(a2) + Math.Cos(a1) * Math.Cos(a2) * Math.Cos(dl);
        return Math.Acos(Math.Clamp(s, -1.0, 1.0)) * Rad2Deg;
    }

}
