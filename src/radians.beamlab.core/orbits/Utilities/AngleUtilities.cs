using System;
using System.Runtime.CompilerServices;

namespace Radians.Orbits.Core.Utilities
{
    /// <summary>
    /// Utility methods for angle conversions and normalization.
    /// </summary>
    public static class AngleUtilities
    {
        /// <summary>
        /// Two times PI (2π).
        /// </summary>
        public const double TwoPi = 2.0 * Math.PI;

        /// <summary>
        /// Half of PI (π/2).
        /// </summary>
        public const double HalfPi = Math.PI / 2.0;

        /// <summary>
        /// Converts degrees to radians.
        /// </summary>
        /// <param name="degrees">Angle in degrees.</param>
        /// <returns>Angle in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DegToRad(double degrees)
        {
            return Math.PI * degrees / 180.0;
        }

        /// <summary>
        /// Converts radians to degrees.
        /// </summary>
        /// <param name="radians">Angle in radians.</param>
        /// <returns>Angle in degrees.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double RadToDeg(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        /// <summary>
        /// Normalizes an angle in degrees to the range [0, 360).
        /// </summary>
        /// <param name="degrees">Angle in degrees.</param>
        /// <returns>Normalized angle in degrees [0, 360).</returns>
        public static double NormalizeAngle0To360(double degrees)
        {
            if (degrees == double.MaxValue)
                throw new ArgumentException($"Incorrect angle value: {degrees}", nameof(degrees));
            
            degrees += 360.0;
            while (degrees >= 360.0)
                degrees -= 360.0;
            return degrees;
        }

        /// <summary>
        /// Normalizes an angle in degrees to the range [-180, 180).
        /// </summary>
        /// <param name="degrees">Angle in degrees.</param>
        /// <returns>Normalized angle in degrees [-180, 180).</returns>
        public static double NormalizeAngle180(double degrees)
        {
            double normalized = (degrees % 360.0 + 360.0) % 360.0;
            if (normalized > 180.0)
                normalized -= 360.0;
            return normalized;
        }

        /// <summary>
        /// Normalizes an angle in radians to the range [0, 2π).
        /// </summary>
        /// <param name="radians">Angle in radians.</param>
        /// <returns>Normalized angle in radians [0, 2π).</returns>
        public static double NormalizeAngle0To2Pi(double radians)
        {
            return (radians % TwoPi + TwoPi) % TwoPi;
        }

        /// <summary>
        /// Normalizes an angle in radians to the range [-π, π).
        /// </summary>
        /// <param name="radians">Angle in radians.</param>
        /// <returns>Normalized angle in radians [-π, π).</returns>
        public static double NormalizeAnglePi(double radians)
        {
            double normalized = (radians % TwoPi + TwoPi) % TwoPi;
            if (normalized > Math.PI)
                normalized -= TwoPi;
            return normalized;
        }

        /// <summary>
        /// Safe arccosine that clamps the input to [-1, 1] to avoid domain errors.
        /// </summary>
        /// <param name="value">Value to compute arccosine of.</param>
        /// <returns>Arccosine in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SafeAcos(double value)
        {
            if (value > 1.0)
                value = 1.0;
            else if (value < -1.0)
                value = -1.0;
            return Math.Acos(value);
        }

        /// <summary>
        /// Safe arcsine that clamps the input to [-1, 1] to avoid domain errors.
        /// </summary>
        /// <param name="value">Value to compute arcsine of.</param>
        /// <returns>Arcsine in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SafeAsin(double value)
        {
            if (value > 1.0)
                value = 1.0;
            else if (value < -1.0)
                value = -1.0;
            return Math.Asin(value);
        }

        /// <summary>
        /// Computes sine of an angle in degrees.
        /// </summary>
        /// <param name="degrees">Angle in degrees.</param>
        /// <returns>Sine of the angle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SinDeg(double degrees)
        {
            return Math.Sin(DegToRad(degrees));
        }

        /// <summary>
        /// Computes cosine of an angle in degrees.
        /// </summary>
        /// <param name="degrees">Angle in degrees.</param>
        /// <returns>Cosine of the angle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double CosDeg(double degrees)
        {
            return Math.Cos(DegToRad(degrees));
        }

        /// <summary>
        /// Computes tangent of an angle in degrees.
        /// </summary>
        /// <param name="degrees">Angle in degrees.</param>
        /// <returns>Tangent of the angle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double TanDeg(double degrees)
        {
            return Math.Tan(DegToRad(degrees));
        }

        /// <summary>
        /// Computes arcsine and returns result in degrees.
        /// </summary>
        /// <param name="value">Value to compute arcsine of.</param>
        /// <returns>Arcsine in degrees.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double AsinDeg(double value)
        {
            return RadToDeg(SafeAsin(value));
        }

        /// <summary>
        /// Computes arccosine and returns result in degrees.
        /// </summary>
        /// <param name="value">Value to compute arccosine of.</param>
        /// <returns>Arccosine in degrees.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double AcosDeg(double value)
        {
            return RadToDeg(SafeAcos(value));
        }

        /// <summary>
        /// Computes arctangent and returns result in degrees.
        /// </summary>
        /// <param name="value">Value to compute arctangent of.</param>
        /// <returns>Arctangent in degrees.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double AtanDeg(double value)
        {
            return RadToDeg(Math.Atan(value));
        }
    }
}