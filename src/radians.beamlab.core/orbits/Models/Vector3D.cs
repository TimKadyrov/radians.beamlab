using Radians.Orbits.Core.Utilities;
using System;
using System.Runtime.CompilerServices;

namespace Radians.Orbits.Core.Models
{
    /// <summary>
    /// Represents a three-dimensional vector in Cartesian coordinates.
    /// Immutable structure for representing positions or velocities in 3D space.
    /// </summary>
    public readonly struct Vector3D : IEquatable<Vector3D>
    {
        /// <summary>
        /// Gets the X component of the vector.
        /// </summary>
        public double X { get; }

        /// <summary>
        /// Gets the Y component of the vector.
        /// </summary>
        public double Y { get; }

        /// <summary>
        /// Gets the Z component of the vector.
        /// </summary>
        public double Z { get; }

        /// <summary>
        /// Gets the magnitude (length) of the vector.
        /// </summary>
        public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>
        /// Gets the squared magnitude of the vector (more efficient than Magnitude when comparing lengths).
        /// </summary>
        public double MagnitudeSquared => X * X + Y * Y + Z * Z;

        /// <summary>
        /// Gets a zero vector (0, 0, 0).
        /// </summary>
        public static Vector3D Zero => new Vector3D(0, 0, 0);

        /// <summary>
        /// Gets a unit vector in the X direction (1, 0, 0).
        /// </summary>
        public static Vector3D UnitX => new Vector3D(1, 0, 0);

        /// <summary>
        /// Gets a unit vector in the Y direction (0, 1, 0).
        /// </summary>
        public static Vector3D UnitY => new Vector3D(0, 1, 0);

        /// <summary>
        /// Gets a unit vector in the Z direction (0, 0, 1).
        /// </summary>
        public static Vector3D UnitZ => new Vector3D(0, 0, 1);

        /// <summary>
        /// Initializes a new instance of the Vector3D struct.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <param name="z">The Z component.</param>
        public Vector3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Converts an ECF/ECI position (km) to (lat, lon, alt). Latitude here is
        /// geocentric, asin(Z/|R|). Earth is treated as a sphere of radius
        /// OrbitalConstants.EarthRadiusKm, so geocentric == geodetic.
        /// </summary>
        /// <returns>Spherical-Earth coordinates with geocentric latitude.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GeocentricCoordinate ToGeocentric()
        {
            double latRad = 0.0;
            if (Z != 0.0)
            {
                latRad = AngleUtilities.SafeAsin(Z / Magnitude);
            }

            double lonRad = Math.Atan2(Y, X);
            double altKm = Magnitude - OrbitalConstants.EarthRadiusKm;

            return new GeocentricCoordinate(latRad, lonRad, altKm);
        }

        /// <summary>
        /// Gets the longitude in radians from this position vector.
        /// </summary>
        /// <returns>Longitude in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetLongitude()
        {
            return Math.Atan2(Y, X);
        }

        /// <summary>
        /// Returns a normalized (unit) vector in the same direction.
        /// </summary>
        /// <returns>Normalized vector.</returns>
        public Vector3D Normalize()
        {
            double mag = Magnitude;
            if (mag < 1e-10)
                return Zero;
            return new Vector3D(X / mag, Y / mag, Z / mag);
        }

        /// <summary>
        /// Computes the dot product with another vector.
        /// </summary>
        /// <param name="other">The other vector.</param>
        /// <returns>Dot product.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Dot(Vector3D other)
        {
            return X * other.X + Y * other.Y + Z * other.Z;
        }

        /// <summary>
        /// Computes the cross product with another vector.
        /// </summary>
        /// <param name="other">The other vector.</param>
        /// <returns>Cross product vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3D Cross(Vector3D other)
        {
            return new Vector3D(
                Y * other.Z - Z * other.Y,
                Z * other.X - X * other.Z,
                X * other.Y - Y * other.X
            );
        }

        /// <summary>
        /// Computes the angle between this vector and another vector.
        /// </summary>
        /// <param name="other">The other vector.</param>
        /// <returns>Angle in radians.</returns>
        public double AngleTo(Vector3D other)
        {
            return AngleUtilities.SafeAcos(Dot(other) / (Magnitude * other.Magnitude));
        }

        /// <summary>
        /// Adds two vectors.
        /// </summary>
        public static Vector3D operator +(Vector3D a, Vector3D b)
        {
            return new Vector3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        /// <summary>
        /// Subtracts two vectors.
        /// </summary>
        public static Vector3D operator -(Vector3D a, Vector3D b)
        {
            return new Vector3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        /// <summary>
        /// Negates a vector.
        /// </summary>
        public static Vector3D operator -(Vector3D a)
        {
            return new Vector3D(-a.X, -a.Y, -a.Z);
        }

        /// <summary>
        /// Multiplies a vector by a scalar.
        /// </summary>
        public static Vector3D operator *(Vector3D v, double scalar)
        {
            return new Vector3D(v.X * scalar, v.Y * scalar, v.Z * scalar);
        }

        /// <summary>
        /// Multiplies a vector by a scalar.
        /// </summary>
        public static Vector3D operator *(double scalar, Vector3D v)
        {
            return new Vector3D(v.X * scalar, v.Y * scalar, v.Z * scalar);
        }

        /// <summary>
        /// Divides a vector by a scalar.
        /// </summary>
        public static Vector3D operator /(Vector3D v, double scalar)
        {
            return new Vector3D(v.X / scalar, v.Y / scalar, v.Z / scalar);
        }

        /// <summary>
        /// Tests for equality between two vectors.
        /// </summary>
        public static bool operator ==(Vector3D a, Vector3D b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Tests for inequality between two vectors.
        /// </summary>
        public static bool operator !=(Vector3D a, Vector3D b)
        {
            return !a.Equals(b);
        }

        /// <summary>
        /// Determines whether the specified vector is equal to this vector.
        /// </summary>
        public bool Equals(Vector3D other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        /// <summary>
        /// Determines whether the specified object is equal to this vector.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is Vector3D other && Equals(other);
        }

        /// <summary>
        /// Returns a hash code for this vector.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                hash = hash * 31 + Z.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Returns a string representation of this vector.
        /// </summary>
        public override string ToString()
        {
            return $"({X:F3}, {Y:F3}, {Z:F3})";
        }

        /// <summary>
        /// Returns a detailed string representation with magnitude.
        /// </summary>
        public string ToStringDetailed()
        {
            return $"Vector3D: X={X:F3}, Y={Y:F3}, Z={Z:F3}, Magnitude={Magnitude:F3}";
        }
    }
}