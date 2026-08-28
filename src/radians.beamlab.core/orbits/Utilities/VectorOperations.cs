using System;
using System.Runtime.CompilerServices;
using Radians.Orbits.Core.Models;

namespace Radians.Orbits.Core.Utilities
{
    /// <summary>
    /// Utility methods for vector operations.
    /// Provides static methods for common vector calculations.
    /// </summary>
    public static class VectorOperations
    {
        /// <summary>
        /// Computes the dot product of two vectors.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <returns>Dot product.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DotProduct(Vector3D v1, Vector3D v2)
        {
            return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
        }

        /// <summary>
        /// Computes the cross product of two vectors.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <returns>Cross product vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D CrossProduct(Vector3D v1, Vector3D v2)
        {
            return new Vector3D(
                v1.Y * v2.Z - v1.Z * v2.Y,
                v1.Z * v2.X - v1.X * v2.Z,
                v1.X * v2.Y - v1.Y * v2.X
            );
        }

        /// <summary>
        /// Adds two vectors.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <returns>Sum vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D Add(Vector3D v1, Vector3D v2)
        {
            return new Vector3D(v1.X + v2.X, v1.Y + v2.Y, v1.Z + v2.Z);
        }

        /// <summary>
        /// Subtracts the second vector from the first.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <returns>Difference vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D Subtract(Vector3D v1, Vector3D v2)
        {
            return new Vector3D(v1.X - v2.X, v1.Y - v2.Y, v1.Z - v2.Z);
        }

        /// <summary>
        /// Multiplies a vector by a scalar.
        /// </summary>
        /// <param name="v">Vector.</param>
        /// <param name="scalar">Scalar value.</param>
        /// <returns>Scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D Multiply(Vector3D v, double scalar)
        {
            return new Vector3D(v.X * scalar, v.Y * scalar, v.Z * scalar);
        }

        /// <summary>
        /// Divides a vector by a scalar.
        /// </summary>
        /// <param name="v">Vector.</param>
        /// <param name="scalar">Scalar value.</param>
        /// <returns>Scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D Divide(Vector3D v, double scalar)
        {
            return new Vector3D(v.X / scalar, v.Y / scalar, v.Z / scalar);
        }

        /// <summary>
        /// Normalizes a vector to unit length.
        /// </summary>
        /// <param name="v">Vector to normalize.</param>
        /// <returns>Unit vector in the same direction.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D Normalize(Vector3D v)
        {
            double magnitude = v.Magnitude;
            if (magnitude < 1e-10)
                return Vector3D.Zero;
            return new Vector3D(v.X / magnitude, v.Y / magnitude, v.Z / magnitude);
        }

        /// <summary>
        /// Computes the angle between two vectors.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <returns>Angle in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double AngleBetween(Vector3D v1, Vector3D v2)
        {
            return AngleUtilities.SafeAcos(DotProduct(v1, v2) / (v1.Magnitude * v2.Magnitude));
        }

        /// <summary>
        /// Computes the signed angle between two vectors around a specified axis.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <param name="axis">Axis to measure rotation around.</param>
        /// <returns>Signed angle in radians.</returns>
        public static double SignedAngleBetween(Vector3D v1, Vector3D v2, Vector3D axis)
        {
            Vector3D n1 = Normalize(v1);
            Vector3D n2 = Normalize(v2);
            
            double dot = DotProduct(n1, n2);
            double angle = Math.Atan2(CrossProduct(n1, n2).Magnitude, dot);
            
            // Determine sign based on axis
            Vector3D cross = CrossProduct(n1, n2);
            if (DotProduct(axis, cross) < 0.0)
                angle = -angle;
            
            return angle;
        }

        /// <summary>
        /// Projects vector v1 onto vector v2.
        /// </summary>
        /// <param name="v1">Vector to project.</param>
        /// <param name="v2">Vector to project onto.</param>
        /// <returns>Projected vector.</returns>
        public static Vector3D Project(Vector3D v1, Vector3D v2)
        {
            double dot = DotProduct(v1, v2);
            double magSquared = v2.MagnitudeSquared;
            if (magSquared < 1e-10)
                return Vector3D.Zero;
            return Multiply(v2, dot / magSquared);
        }

        /// <summary>
        /// Computes the component of v1 perpendicular to v2.
        /// </summary>
        /// <param name="v1">Vector.</param>
        /// <param name="v2">Reference vector.</param>
        /// <returns>Perpendicular component.</returns>
        public static Vector3D Reject(Vector3D v1, Vector3D v2)
        {
            return Subtract(v1, Project(v1, v2));
        }

        /// <summary>
        /// Linearly interpolates between two vectors.
        /// </summary>
        /// <param name="v1">Start vector.</param>
        /// <param name="v2">End vector.</param>
        /// <param name="t">Interpolation parameter (0 to 1).</param>
        /// <returns>Interpolated vector.</returns>
        public static Vector3D Lerp(Vector3D v1, Vector3D v2, double t)
        {
            return new Vector3D(
                v1.X + t * (v2.X - v1.X),
                v1.Y + t * (v2.Y - v1.Y),
                v1.Z + t * (v2.Z - v1.Z)
            );
        }

        /// <summary>
        /// Computes the distance between two position vectors.
        /// </summary>
        /// <param name="v1">First position.</param>
        /// <param name="v2">Second position.</param>
        /// <returns>Distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Distance(Vector3D v1, Vector3D v2)
        {
            return Subtract(v1, v2).Magnitude;
        }

        /// <summary>
        /// Computes the squared distance between two position vectors.
        /// More efficient than Distance when comparing distances.
        /// </summary>
        /// <param name="v1">First position.</param>
        /// <param name="v2">Second position.</param>
        /// <returns>Squared distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DistanceSquared(Vector3D v1, Vector3D v2)
        {
            return Subtract(v1, v2).MagnitudeSquared;
        }

        /// <summary>
        /// Determines if two vectors are approximately equal within a tolerance.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <param name="tolerance">Tolerance for comparison.</param>
        /// <returns>True if vectors are approximately equal.</returns>
        public static bool ApproximatelyEqual(Vector3D v1, Vector3D v2, double tolerance = 1e-6)
        {
            return Math.Abs(v1.X - v2.X) < tolerance &&
                   Math.Abs(v1.Y - v2.Y) < tolerance &&
                   Math.Abs(v1.Z - v2.Z) < tolerance;
        }

        /// <summary>
        /// S.1503-4 §D6.4.3: two stations are visible if D(s1,s2) is less than Dh(s1) + Dh(s2).
        /// </summary>
        /// <param name="pos1">First position vector in kilometers.</param>
        /// <param name="pos2">Second position vector in kilometers.</param>
        /// <returns>True if the two points are mutually visible.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckVisibility(Vector3D pos1, Vector3D pos2)
        {
            double dh1 = DistanceToHorizon(pos1);
            double dh2 = DistanceToHorizon(pos2);
            return (pos1 - pos2).Magnitude < (dh1 + dh2);
        }

        /// <summary>
        /// S.1503-4 §D6.4.2: Dh = sqrt(R² - Re²).
        /// </summary>
        /// <param name="position">Position vector in kilometers.</param>
        /// <returns>Distance to horizon in kilometers.</returns>
        public static double DistanceToHorizon(Vector3D position)
        {
            double magSquared = position.MagnitudeSquared;
            double earthRadiusSquared = OrbitalConstants.EarthRadiusKm * OrbitalConstants.EarthRadiusKm;
            double d = magSquared - earthRadiusSquared;
            if (d < 0.0)
                d = 0.0;
            return Math.Sqrt(d);
        }
    }
}