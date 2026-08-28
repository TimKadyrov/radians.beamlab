using System;
using System.Collections.Generic;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>One service cell: the demand point the scheduler serves.</summary>
public sealed record ServiceCell(int CellId, double LatDeg, double LonDeg)
{
    /// <summary>Simultaneous co-frequency links this cell requests (traffic model; milestone default 1).</summary>
    public int DemandLinks { get; init; } = 1;
}

/// <summary>
/// The served geography (simulation spec Sec. 4.2): cells over a
/// latitude/longitude-bounded region. Cell centres sit on latitude rings at
/// the cell pitch with longitude spacing scaled by 1/cos(lat), giving a
/// near-equal-area grid. Territory scoping is deferred.
/// </summary>
public sealed class ServiceGeography
{
    public IReadOnlyList<ServiceCell> Cells { get; }
    /// <summary>Grid pitch (km) -- also the default serving-coverage radius.</summary>
    public double CellPitchKm { get; }

    public ServiceGeography(IReadOnlyList<ServiceCell> cells, double cellPitchKm)
    {
        Cells = cells;
        CellPitchKm = cellPitchKm;
    }

    public static ServiceGeography Grid(double latMinDeg, double latMaxDeg,
        double lonMinDeg, double lonMaxDeg, double cellPitchKm, int demandLinks = 1)
    {
        if (latMaxDeg <= latMinDeg || lonMaxDeg <= lonMinDeg || cellPitchKm <= 0)
            throw new ArgumentException("degenerate service region");

        double dLatDeg = cellPitchKm / (EarthRadiusKm * Math.PI / 180.0);
        var cells = new List<ServiceCell>();
        int id = 1;
        for (double lat = latMinDeg + dLatDeg / 2.0; lat < latMaxDeg; lat += dLatDeg)
        {
            double cosLat = Math.Max(0.05, Math.Cos(lat * Math.PI / 180.0));
            double dLonDeg = dLatDeg / cosLat;
            for (double lon = lonMinDeg + dLonDeg / 2.0; lon < lonMaxDeg; lon += dLonDeg)
                cells.Add(new ServiceCell(id++, lat, lon) { DemandLinks = demandLinks });
        }
        if (cells.Count == 0) throw new ArgumentException("service region produced no cells");
        return new ServiceGeography(cells, cellPitchKm);
    }
}
