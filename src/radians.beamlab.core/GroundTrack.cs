using System;

namespace radians.beamlab;

/// <summary>Analytic ground-track geometry of circular inclined orbits.</summary>
public static class GroundTrack
{
    /// <summary>
    /// Inertial ground-track headings (deg from north toward east) of a
    /// circular orbit of the given inclination at latitude phi, from the
    /// spherical relation sin(psi) = cos(i) / cos(phi). Returns the
    /// ascending- and descending-pass headings (they merge at the latitude
    /// limit |phi| = i for prograde orbits), or null when the latitude is
    /// beyond the orbit's reach. This is the reachable heading set of a
    /// body-stabilised layout at that latitude (WP4): the fixed layout flies
    /// at the pass heading, while the S.1503-4 mask az/el frame stays
    /// Earth-referenced, so a derived mask envelopes both headings.
    /// </summary>
    public static (double AscendingDeg, double DescendingDeg)? HeadingsAtLatitude(
        double inclinationDeg, double latDeg)
    {
        double cosLat = Math.Cos(latDeg * Math.PI / 180.0);
        if (Math.Abs(cosLat) < 1e-9) return null;      // poles: reachable only by i = 90, heading undefined
        double s = Math.Cos(inclinationDeg * Math.PI / 180.0) / cosLat;
        if (Math.Abs(s) > 1.0) return null;            // latitude beyond the orbit's reach
        double psi = Math.Asin(s) * 180.0 / Math.PI;
        return (psi, 180.0 - psi);
    }
}
