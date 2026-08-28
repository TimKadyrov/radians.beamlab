namespace Radians.Orbits.Core.Propagation
{
    /// <summary>
    /// Defines the coordinate reference frame for orbital state vectors.
    /// </summary>
    public enum CoordinateFrame
    {
        /// <summary>
        /// Earth-Centered Inertial (ECI) frame - non-rotating reference frame
        /// aligned with the Earth's equator and the vernal equinox at J2000 epoch.
        /// </summary>
        ECI,

        /// <summary>
        /// Earth-Centered Fixed (ECF) frame - rotating reference frame
        /// fixed to the Earth's surface.
        /// </summary>
        ECF
    }
}