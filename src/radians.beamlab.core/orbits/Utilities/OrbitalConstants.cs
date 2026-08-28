using System;

namespace Radians.Orbits.Core.Utilities
{
    /// <summary>
    /// Physical and orbital constants used in satellite orbit calculations.
    /// Values are based on ITU-R standards.
    /// </summary>
    public static class OrbitalConstants
    {
        public const int SecondsInDay = 86400;
        public const int SecondsInHour = 3600;
        public const int SecondsInMinute = 60;

        /// <summary>
        /// Earth's equatorial radius in meters (ITU-R standard).
        /// </summary>
        //public const double EarthRadiusM = 6378145.0;

        /// <summary>
        /// Earth's equatorial radius in kilometers (ITU-R standard).
        /// </summary>
        public const double EarthRadiusKm = 6378.145;

        /// <summary>
        /// Geostationary orbit radius in meters.
        /// </summary>
        //public const double GsoRadiusM = 42164200.0;

        /// <summary>
        /// Geostationary orbit radius in kilometers.
        /// </summary>
        public const double GsoRadiusKm = 42164.2;

        /// <summary>
        /// Earth's gravitational parameter (GM) in km³/s².
        /// </summary>
        public const double MuEarth = 398601.2;

        /// <summary>
        /// Earth's J2 oblateness coefficient (second zonal harmonic).
        /// Used for calculating perturbations in satellite orbits.
        /// </summary>
        public const double J2 = 0.001082636;

        /// <summary>
        /// Earth's rotation rate in radians per second.
        /// </summary>
        public const double EarthRotationRate = 0.0000729211578550218;
        /// <summary>
        /// Earth's rotation rate in degrees per second.
        /// </summary>
        public const double EarthRotationRateDeg = 0.0041780745823;

        /// <summary>
        /// Earth's rotation period in seconds (sidereal day).
        /// </summary>
        public const double EarthRotationPeriod = 86164.09054;

        /// <summary>
        /// Greenwich angle at J2000 epoch in degrees.
        /// </summary>
        public const double J2000AngleDeg = -79.8058;

        /// <summary>
        /// Greenwich angle at J2000 epoch in radians.
        /// </summary>
        public const double J2000AngleRad = -1.393748384532;

        /// <summary>
        /// Speed of light in kilometers per second.
        /// </summary>
        public const double LightSpeed = 299792.458;

        /// <summary>
        /// Earth's non-spherical factor (used in perturbation calculations).
        /// </summary>
        public const double EarthNonSphFactor = 26340000000.0;

        public const double EarthOblatenessJ2 = 0.001082636;

        /// <summary>
        /// Ratio of Earth radius to GSO radius (k = Re / Rgso).
        /// </summary>
        public const double K = EarthRadiusKm / GsoRadiusKm;
        public const double InvertedK = GsoRadiusKm / EarthRadiusKm;


        //public const double HGeoM = GsoRadiusM - EarthRadiusM;
        public const double HGeoKm = GsoRadiusKm - EarthRadiusKm;

    }
}