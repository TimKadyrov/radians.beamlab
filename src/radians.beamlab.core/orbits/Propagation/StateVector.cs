using System;
using Radians.Orbits.Core.Models;
using Radians.Orbits.Core.Utilities;

namespace Radians.Orbits.Core.Propagation
{
    /// <summary>
    /// Represents an orbital state vector containing position, velocity, and metadata.
    /// Value type to avoid heap allocations in hot propagation loops.
    /// </summary>
    public readonly struct StateVector
    {
        /// <summary>
        /// Gets the position vector in kilometers.
        /// </summary>
        public Vector3D Position { get; }

        /// <summary>
        /// Gets the velocity vector in km/s.
        /// </summary>
        public Vector3D Velocity { get; }

        /// <summary>
        /// Gets the time in seconds from epoch.
        /// </summary>
        public double TimeSeconds { get; }

        /// <summary>
        /// Gets the coordinate frame of this state vector.
        /// </summary>
        public CoordinateFrame Frame { get; }

        /// <summary>
        /// Gets the radial distance from Earth center in kilometers.
        /// </summary>
        public double RadiusKm { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StateVector"/> struct with position only.
        /// </summary>
        public StateVector(Vector3D position, double timeSeconds, CoordinateFrame frame)
        {
            Position = position;
            Velocity = default;
            TimeSeconds = timeSeconds;
            Frame = frame;
            RadiusKm = position.Magnitude;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StateVector"/> struct with position and explicit radius.
        /// </summary>
        public StateVector(Vector3D position, double timeSeconds, CoordinateFrame frame, double radiusKm)
        {
            Position = position;
            Velocity = default;
            TimeSeconds = timeSeconds;
            Frame = frame;
            RadiusKm = radiusKm;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StateVector"/> struct with position and velocity.
        /// </summary>
        public StateVector(Vector3D position, Vector3D velocity, double timeSeconds, CoordinateFrame frame)
        {
            Position = position;
            Velocity = velocity;
            TimeSeconds = timeSeconds;
            Frame = frame;
            RadiusKm = position.Magnitude;
        }

        /// <summary>
        /// Gets the orbital altitude in kilometers (radius minus Earth radius).
        /// </summary>
        public double GetAltitudeKm()
        {
            return RadiusKm - OrbitalConstants.EarthRadiusKm;
        }

        /// <summary>
        /// Converts position to spherical-Earth coordinates with geocentric latitude.
        /// See <see cref="Vector3D.ToGeocentric"/> for the spherical Earth assumption.
        /// </summary>
        public GeocentricCoordinate ToGeocentric()
        {
            return Position.ToGeocentric();
        }

        /// <summary>
        /// Returns a string representation of the state vector.
        /// </summary>
        public override string ToString()
        {
            return $"[{Frame}] t={TimeSeconds:F2}s, " +
                   $"Pos=({Position.X:F3}, {Position.Y:F3}, {Position.Z:F3})km, " +
                   $"R={RadiusKm:F3}km";
        }
    }
}
