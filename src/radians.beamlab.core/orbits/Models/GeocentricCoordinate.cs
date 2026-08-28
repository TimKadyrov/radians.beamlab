using System;
using System.Runtime.CompilerServices;
using Radians.Orbits.Core.Utilities;

namespace Radians.Orbits.Core.Models
{
    /// <summary>
    /// Spherical-Earth coordinates: latitude (geocentric, asin(z/|R|)),
    /// longitude, altitude. Earth is treated as a sphere of radius
    /// OrbitalConstants.EarthRadiusKm, so geocentric == geodetic.
    /// Immutable.
    /// </summary>
    public readonly struct GeocentricCoordinate : IEquatable<GeocentricCoordinate>
    {
        /// <summary>
        /// Gets the latitude in radians.
        /// </summary>
        public double Latitude { get; }

        /// <summary>
        /// Gets the longitude in radians.
        /// </summary>
        public double Longitude { get; }

        /// <summary>
        /// Gets the altitude above Earth's surface in kilometers.
        /// </summary>
        public double Altitude { get; }

        /// <summary>
        /// Gets the latitude in degrees.
        /// </summary>
        public double LatitudeDeg => AngleUtilities.RadToDeg(Latitude);

        /// <summary>
        /// Gets the longitude in degrees.
        /// </summary>
        public double LongitudeDeg => AngleUtilities.RadToDeg(Longitude);

        /// <summary>
        /// Gets the altitude in meters.
        /// </summary>
        public double AltitudeM => Altitude * 1000.0;

        /// <summary>
        /// Initializes a new instance of the GeocentricCoordinate struct from radians and meters.
        /// </summary>
        /// <param name="latitudeRad">Latitude in radians.</param>
        /// <param name="longitudeRad">Longitude in radians.</param>
        /// <param name="altitudeM">Altitude in kilometers.</param>
        public GeocentricCoordinate(double latitudeRad, double longitudeRad, double altitudeKm)
        {
            Latitude = latitudeRad;
            Longitude = longitudeRad;
            Altitude = altitudeKm;
        }

        /// <summary>
        /// Creates a GeocentricCoordinate from degrees and meters.
        /// </summary>
        /// <param name="latitudeDeg">Latitude in degrees.</param>
        /// <param name="longitudeDeg">Longitude in degrees.</param>
        /// <param name="altitudeM">Altitude in kilometers.</param>
        /// <returns>GeocentricCoordinate instance.</returns>
        public static GeocentricCoordinate FromDegrees(double latitudeDeg, double longitudeDeg, double altitudeKm)
        {
            return new GeocentricCoordinate(
                AngleUtilities.DegToRad(latitudeDeg),
                AngleUtilities.DegToRad(longitudeDeg),
                altitudeKm
            );
        }

        /// <summary>
        /// Creates a GeocentricCoordinate from degrees and kilometers.
        /// </summary>
        /// <param name="latitudeDeg">Latitude in degrees.</param>
        /// <param name="longitudeDeg">Longitude in degrees.</param>
        /// <param name="altitudeKm">Altitude in kilometers.</param>
        /// <returns>GeocentricCoordinate instance.</returns>
        public static GeocentricCoordinate FromDegreesAndKm(double latitudeDeg, double longitudeDeg, double altitudeKm)
        {
            return new GeocentricCoordinate(
                AngleUtilities.DegToRad(latitudeDeg),
                AngleUtilities.DegToRad(longitudeDeg),
                altitudeKm * 1000.0
            );
        }

        /// <summary>
        /// Converts geodetic coordinates to Cartesian (ECF) coordinates.
        /// Assumes a spherical Earth model with radius from OrbitalConstants.
        /// </summary>
        /// <returns>Position vector in Earth-Centered Fixed frame (meters).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3D ToCartesian()
        {
            double x, y, z;

            if (Latitude == 0.0)
            {
                // On equator
                double radius = OrbitalConstants.EarthRadiusKm + Altitude;
                x = radius * Math.Cos(Longitude);
                y = radius * Math.Sin(Longitude);
                z = 0.0;
            }
            else
            {
                // General case
                double cosLat = Math.Cos(Latitude);
                double radius = OrbitalConstants.EarthRadiusKm + Altitude;
                x = radius * cosLat * Math.Cos(Longitude);
                y = radius * cosLat * Math.Sin(Longitude);
                z = radius * Math.Sin(Latitude);
            }

            return new Vector3D(x, y, z);
        }

        /// <summary>
        /// Computes the great circle distance to another geodetic coordinate.
        /// Uses the haversine formula for spherical Earth approximation.
        /// </summary>
        /// <param name="other">The other coordinate.</param>
        /// <returns>Distance in meters.</returns>
        public double DistanceTo(GeocentricCoordinate other)
        {
            double dLat = other.Latitude - Latitude;
            double dLon = other.Longitude - Longitude;

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                      Math.Cos(Latitude) * Math.Cos(other.Latitude) *
                      Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            // Use average altitude for distance calculation
            double avgAlt = (Altitude + other.Altitude) / 2.0;
            return (OrbitalConstants.EarthRadiusKm + avgAlt) * c;
        }

        /// <summary>
        /// Normalizes the longitude to [-π, π) range.
        /// </summary>
        /// <returns>New coordinate with normalized longitude.</returns>
        public GeocentricCoordinate NormalizeLongitude()
        {
            double normalizedLon = AngleUtilities.NormalizeAnglePi(Longitude);
            return new GeocentricCoordinate(Latitude, normalizedLon, Altitude);
        }

        /// <summary>
        /// Tests for equality between two coordinates.
        /// </summary>
        public static bool operator ==(GeocentricCoordinate a, GeocentricCoordinate b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Tests for inequality between two coordinates.
        /// </summary>
        public static bool operator !=(GeocentricCoordinate a, GeocentricCoordinate b)
        {
            return !a.Equals(b);
        }

        /// <summary>
        /// Determines whether the specified coordinate is equal to this coordinate.
        /// </summary>
        public bool Equals(GeocentricCoordinate other)
        {
            return Latitude == other.Latitude && 
                   Longitude == other.Longitude && 
                   Altitude == other.Altitude;
        }

        /// <summary>
        /// Determines whether the specified object is equal to this coordinate.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is GeocentricCoordinate other && Equals(other);
        }

        /// <summary>
        /// Returns a hash code for this coordinate.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Latitude.GetHashCode();
                hash = hash * 31 + Longitude.GetHashCode();
                hash = hash * 31 + Altitude.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Returns a string representation of this coordinate.
        /// </summary>
        public override string ToString()
        {
            return $"Lat: {LatitudeDeg:F4}°, Lon: {LongitudeDeg:F4}°, Alt: {Altitude / 1000.0:F3} km";
        }

        /// <summary>
        /// Returns a detailed string representation with both degrees and radians.
        /// </summary>
        public string ToStringDetailed()
        {
            return $"GeocentricCoordinate: " +
                   $"Lat={LatitudeDeg:F6}° ({Latitude:F6} rad), " +
                   $"Lon={LongitudeDeg:F6}° ({Longitude:F6} rad), " +
                   $"Alt={Altitude:F3} km";
        }
    }
}