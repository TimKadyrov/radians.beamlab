using System;
using System.Linq;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// Darkens the mask where the DECLARED system cannot transmit.
///
/// A pfd mask lit at a sub-satellite latitude from which no served cell is
/// reachable is not a purer declaration -- it is a wrong one, about a
/// different system. Rec. ITU-R S.1503-4 Sec. C1's -1000 null is the
/// format's own way of saying "this space station does not radiate here",
/// and the mask's latitude axis exists precisely because emissions vary
/// with sub-satellite latitude for operational reasons.
///
/// The dark rows are NOT the R set's bound consulted at read time. Their
/// VALUES record the consequence of the commitments, so the examination
/// still reads the mask from geometry alone, and a reader of the mask by
/// itself learns a true, independently enforceable property -- serve
/// outside the declared span and the emission exceeds the filed mask, which
/// is a violation of the mask itself. The coupling lives in the derivation,
/// where the commitments live; the artefacts stay independent where they
/// are read and where they bind.
///
/// UNVISITED IS NOT UNREACHABLE. A row is darkened only under a closed-form
/// certificate computed from declared commitments -- never because a finite
/// probe happened to visit nothing there, which is the unsafe direction
/// (a deflated mask that a truth run then exceeds).
/// </summary>
public sealed class ServiceSpanSampler : IPfdMaskSampler
{
    private readonly IPfdMaskSampler _inner;
    private readonly double _esLatMinDeg, _esLatMaxDeg, _halfAngleDeg, _halfRowDeg;
    private bool _dark;

    /// <summary>Latitude rows darkened by the certificate (diagnostics).</summary>
    public int DarkLatitudes { get; private set; }
    /// <summary>Rows the certificate left lit.</summary>
    public int LitLatitudes { get; private set; }

    public double HalfAngleDeg => _halfAngleDeg;
    /// <summary>Half the mask latitude step: the reach either side of a row.</summary>
    public double HalfRowDeg => _halfRowDeg;

    /// <param name="declared">
    /// The declared set: es_lat_min/es_lat_max give the served span and
    /// min_elev the elevation the coverage circle is taken at. The SMALLEST
    /// declared minimum elevation is used, since a lower floor reaches
    /// further and therefore darkens fewer rows -- the conservative reading.
    /// </param>
    /// <param name="latStepDeg">
    /// The mask's latitude step. A row does not describe a point: Sec. D5.1.5
    /// step 1 reads the table with the NEAREST latitude, so a row governs the
    /// half-step either side of it. Darkening a row because its own latitude
    /// cannot reach the span would under-declare for the reachable latitudes
    /// that row also governs -- the deflated-mask direction, the unsafe one.
    /// </param>
    public ServiceSpanSampler(IPfdMaskSampler inner, OperatingParamsSet declared,
        double altitudeKm, double latStepDeg)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (latStepDeg <= 0.0) throw new ArgumentOutOfRangeException(nameof(latStepDeg));
        _esLatMinDeg = declared.EsLatMinDeg;
        _esLatMaxDeg = declared.EsLatMaxDeg;
        _halfAngleDeg = CoverageHalfAngleDeg(altitudeKm, SmallestDeclaredMinElevDeg(declared));
        _halfRowDeg = latStepDeg / 2.0;
    }

    /// <summary>
    /// Earth-central half-angle (deg) of the coverage circle: the greatest
    /// great-circle distance from the sub-satellite point at which a station
    /// still sees the satellite at or above <paramref name="minElevDeg"/>.
    /// </summary>
    public static double CoverageHalfAngleDeg(double altitudeKm, double minElevDeg)
    {
        double eps = minElevDeg * Math.PI / 180.0;
        double ratio = GeoMath.EarthRadiusKm / (GeoMath.EarthRadiusKm + altitudeKm);
        return (Math.Acos(Math.Clamp(ratio * Math.Cos(eps), -1.0, 1.0)) - eps) * 180.0 / Math.PI;
    }

    /// <summary>
    /// The certificate. A mask row has no longitude axis, so the test must
    /// hold for EVERY longitude at that latitude: the satellite sweeps them
    /// all, and a row is dark only when no longitude could reach the span.
    /// The minimum great-circle distance from a point to a latitude BAND is
    /// therefore the latitude gap alone.
    /// </summary>
    public bool ReachesServiceSpan(double subSatLatDeg)
    {
        // The band this row governs, not the row's own latitude.
        double lo = subSatLatDeg - _halfRowDeg, hi = subSatLatDeg + _halfRowDeg;
        double gap = hi < _esLatMinDeg ? _esLatMinDeg - hi
                   : lo > _esLatMaxDeg ? lo - _esLatMaxDeg
                   : 0.0;
        return gap <= _halfAngleDeg + 1e-9;
    }

    private static double SmallestDeclaredMinElevDeg(OperatingParamsSet p)
    {
        double best = double.PositiveInfinity;
        foreach (var row in p.MinElev)
            foreach (var (_, elev) in row.ByAz)
                best = Math.Min(best, elev);
        if (p.ElevAngleHeaderDeg is double h) best = Math.Min(best, h);
        // Nothing declared means nothing promised: 0 deg reaches furthest,
        // so no row can be certified dark.
        return double.IsPositiveInfinity(best) ? 0.0 : best;
    }

    public void PrepareLatitude(double latDeg)
    {
        _dark = !ReachesServiceSpan(latDeg);
        if (_dark) { DarkLatitudes++; return; }   // no field to build for a dark row
        LitLatitudes++;
        _inner.PrepareLatitude(latDeg);
    }

    public double SampleMaxIn(double xDeg, double yDeg, double halfW, double halfH)
        => _dark ? double.NegativeInfinity : _inner.SampleMaxIn(xDeg, yDeg, halfW, halfH);
}
