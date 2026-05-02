using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace radians.beamlab.app;

/// <summary>
/// Coastline / political-boundary polylines for the equirectangular map.
///
/// Loads Natural Earth (or any GeoJSON FeatureCollection of Polygon /
/// MultiPolygon features) from <c>countries.json</c> at startup, falling back
/// to a tiny hand-coded outline if no file is found. Each polyline is a
/// list of (lat, lon) pairs in degrees. Rings that cross the antimeridian
/// are split downstream by the renderer.
/// </summary>
/// <summary>One country's geometry: name + a list of disjoint outer rings (one per landmass).</summary>
public sealed class CountryGeometry
{
    public string Name { get; }
    /// <summary>Outer rings only (holes excluded), one per disjoint sub-polygon (landmass).</summary>
    public IReadOnlyList<IReadOnlyList<(double lat, double lon)>> OuterRings { get; }

    public CountryGeometry(string name, IReadOnlyList<IReadOnlyList<(double lat, double lon)>> outerRings)
    {
        Name = name;
        OuterRings = outerRings;
    }

    /// <summary>Standard ray-casting point-in-polygon over the outer rings.</summary>
    public bool Contains(double lat, double lon)
    {
        foreach (var ring in OuterRings)
            if (PointInRing(lat, lon, ring)) return true;
        return false;
    }

    /// <summary>
    /// Sample roughly <paramref name="targetPoints"/> points inside the country
    /// on a regular lat/lon grid sized from the bounding box of all outer rings.
    /// Each candidate is filtered through <see cref="Contains"/>.
    /// </summary>
    public List<(double lat, double lon)> SampleInterior(int targetPoints)
    {
        double latMin = double.MaxValue, latMax = double.MinValue;
        double lonMin = double.MaxValue, lonMax = double.MinValue;
        foreach (var ring in OuterRings)
            foreach (var (lat, lon) in ring)
            {
                if (lat < latMin) latMin = lat;
                if (lat > latMax) latMax = lat;
                if (lon < lonMin) lonMin = lon;
                if (lon > lonMax) lonMax = lon;
            }
        if (latMin == double.MaxValue) return new();
        double areaDeg2 = Math.Max(0.01, (latMax - latMin) * (lonMax - lonMin));
        double step = Math.Max(0.1, Math.Sqrt(areaDeg2 / Math.Max(1, targetPoints)));
        var pts = new List<(double, double)>();
        for (double la = latMin; la <= latMax; la += step)
            for (double lo = lonMin; lo <= lonMax; lo += step)
                if (Contains(la, lo)) pts.Add((la, lo));
        return pts;
    }

    private static bool PointInRing(double lat, double lon, IReadOnlyList<(double lat, double lon)> ring)
    {
        bool inside = false;
        int n = ring.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var (yi, xi) = ring[i];
            var (yj, xj) = ring[j];
            if ((yi > lat) != (yj > lat))
            {
                double xIntercept = xi + (lat - yi) * (xj - xi) / (yj - yi);
                if (lon < xIntercept) inside = !inside;
            }
        }
        return inside;
    }
}

/// <summary>
/// Instance-based coastline + country loader. One instance is created at app
/// startup (held by the ViewModel) and shared with the renderer. Construction
/// runs the file load — failures fall back to <see cref="HandCoded"/>.
/// </summary>
public sealed class CoastlineDataProvider
{
    public IReadOnlyList<IReadOnlyList<(double lat, double lon)>> Polylines { get; }
    public IReadOnlyList<CountryGeometry> Countries { get; }
    public string SourceLabel { get; }

    public CoastlineDataProvider()
    {
        var loaded = TryLoadGeoJson(out string? path);
        if (loaded != null)
        {
            Polylines = loaded.Polylines;
            Countries = loaded.Countries;
            SourceLabel = path ?? "";
        }
        else
        {
            Polylines = HandCoded;
            Countries = Array.Empty<CountryGeometry>();
            SourceLabel = "(built-in coarse outlines)";
        }
    }

    private record LoadResult(IReadOnlyList<IReadOnlyList<(double lat, double lon)>> Polylines, IReadOnlyList<CountryGeometry> Countries);

    private static LoadResult? TryLoadGeoJson(out string? sourcePath)
    {
        sourcePath = null;
        // Some Natural Earth GeoJSON downloads contain stray non-UTF-8 bytes
        // in country names (CP1252 "ç" / "é" instead of UTF-8 multi-byte). The
        // strict UTF-8 decoder in JsonDocument throws on those, killing the
        // load. Read with a replacement-fallback decoder so one bad name turns
        // into U+FFFD and the rest of the data loads cleanly.
        var lenientUtf8 = Encoding.GetEncoding(
            "utf-8", EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);

        foreach (var path in CandidatePaths())
        {
            try
            {
                if (!File.Exists(path)) continue;
                var bytes = File.ReadAllBytes(path);
                var text = lenientUtf8.GetString(bytes);
                using var doc = JsonDocument.Parse(text);
                var result = ParseGeoJson(doc.RootElement);
                if (result.Polylines.Count > 0)
                {
                    sourcePath = path;
                    return result;
                }
            }
            catch
            {
                // try the next candidate
            }
        }
        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        // 1. Application binary directory — the project copies countries.json
        //    here at build time, so this is the portable, primary location.
        yield return Path.Combine(AppContext.BaseDirectory, "countries.json");
        // 2. Working directory of the app process (in case the user launched
        //    the exe from elsewhere with a sibling countries.json).
        yield return Path.Combine(Environment.CurrentDirectory, "countries.json");
        // 3. Project-root sibling, for `dotnet run` from the solution root
        //    when the binary copy hasn't been refreshed.
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "countries.json");
    }

    private static LoadResult ParseGeoJson(JsonElement root)
    {
        var allRings = new List<IReadOnlyList<(double, double)>>();
        var countries = new List<CountryGeometry>();
        if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            ProcessGeometry(root, allRings, null);
            return new LoadResult(allRings, countries);
        }
        foreach (var feat in features.EnumerateArray())
        {
            try
            {
                string name = TryReadName(feat);
                var outerRings = new List<IReadOnlyList<(double, double)>>();
                if (feat.TryGetProperty("geometry", out var geom) && geom.ValueKind == JsonValueKind.Object)
                    ProcessGeometry(geom, allRings, outerRings);
                if (!string.IsNullOrEmpty(name) && outerRings.Count > 0)
                    countries.Add(new CountryGeometry(name, outerRings));
            }
            catch
            {
                // skip this feature, keep going
            }
        }
        countries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return new LoadResult(allRings, countries);
    }

    private static string TryReadName(JsonElement feat)
    {
        if (feat.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            // Prefer common Natural Earth name keys.
            foreach (var key in new[] { "NAME_LONG", "NAME", "name_long", "name", "ADMIN", "admin" })
            {
                if (props.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!;
                }
            }
        }
        return "";
    }

    private static void ProcessGeometry(
        JsonElement geom,
        List<IReadOnlyList<(double, double)>> allRings,
        List<IReadOnlyList<(double, double)>>? outerRings)
    {
        if (!geom.TryGetProperty("type", out var typeProp)) return;
        var type = typeProp.GetString();
        if (!geom.TryGetProperty("coordinates", out var coords)) return;

        switch (type)
        {
            case "Polygon":
                {
                    int idx = 0;
                    foreach (var ring in coords.EnumerateArray())
                    {
                        AddRing(allRings, ring);
                        if (idx == 0) AddRing(outerRings, ring);
                        idx++;
                    }
                    break;
                }
            case "MultiPolygon":
                foreach (var poly in coords.EnumerateArray())
                {
                    int idx = 0;
                    foreach (var ring in poly.EnumerateArray())
                    {
                        AddRing(allRings, ring);
                        if (idx == 0) AddRing(outerRings, ring);
                        idx++;
                    }
                }
                break;
            case "LineString":
                AddRing(allRings, coords);
                break;
            case "MultiLineString":
                foreach (var line in coords.EnumerateArray()) AddRing(allRings, line);
                break;
        }
    }

    private static void AddRing(List<IReadOnlyList<(double, double)>>? result, JsonElement ring)
    {
        if (result is null) return;
        if (ring.ValueKind != JsonValueKind.Array) return;
        var pts = new List<(double, double)>(ring.GetArrayLength());
        foreach (var pt in ring.EnumerateArray())
        {
            if (pt.ValueKind != JsonValueKind.Array || pt.GetArrayLength() < 2) continue;
            double lon = pt[0].GetDouble();
            double lat = pt[1].GetDouble();
            pts.Add((lat, lon));
        }
        if (pts.Count > 1) result.Add(pts);
    }

    // -------- Hand-coded fallback (kept as backup if countries.json is absent). --------

    private static readonly List<IReadOnlyList<(double lat, double lon)>> HandCoded = new()
    {
        new[]
        {
            (35.0, -6.0), (37.0, 10.0), (32.0, 22.0), (32.0, 32.0),
            (24.0, 35.0), (12.0, 43.0), (11.0, 51.0), (-2.0, 41.0),
            (-15.0, 40.0), (-26.0, 33.0), (-30.0, 31.0), (-35.0, 20.0),
            (-30.0, 17.0), (-22.0, 14.0), (-12.0, 13.0), (-5.0, 12.0),
            (0.0, 9.0), (5.0, 4.0), (5.0, -5.0), (10.0, -15.0),
            (14.0, -17.0), (20.0, -17.0), (28.0, -12.0), (32.0, -9.0),
            (35.0, -6.0),
        },
        new[]
        {
            (36.0, -10.0), (43.0, -9.0), (48.0, -5.0), (51.0, -4.0),
            (60.0, 5.0), (62.0, 22.0), (70.0, 30.0), (74.0, 60.0),
            (76.0, 95.0), (74.0, 130.0), (70.0, 160.0), (62.0, 170.0),
            (55.0, 158.0), (45.0, 142.0), (35.0, 140.0), (30.0, 122.0),
            (22.0, 115.0), (10.0, 108.0), (5.0, 103.0), (10.0, 95.0),
            (16.0, 82.0), (8.0, 78.0), (24.0, 67.0), (25.0, 57.0),
            (15.0, 51.0), (12.0, 43.0), (30.0, 35.0), (36.0, 36.0),
            (41.0, 29.0), (38.0, 24.0), (40.0, 18.0), (44.0, 9.0),
            (43.0, 3.0), (36.0, -5.0), (36.0, -10.0),
        },
        new[]
        {
            (71.0, -156.0), (70.0, -141.0), (60.0, -141.0), (54.0, -130.0),
            (48.0, -123.0), (42.0, -124.0), (34.0, -120.0), (27.0, -114.0),
            (23.0, -110.0), (18.0, -103.0), (16.0, -97.0), (18.0, -93.0),
            (21.0, -87.0), (25.0, -82.0), (30.0, -82.0), (37.0, -76.0),
            (41.0, -74.0), (44.0, -67.0), (47.0, -60.0), (52.0, -55.0),
            (60.0, -64.0), (66.0, -60.0), (74.0, -78.0), (75.0, -120.0),
            (71.0, -156.0),
        },
        new[]
        {
            (12.0, -72.0), (10.0, -62.0), (5.0, -52.0), (-1.0, -45.0),
            (-8.0, -35.0), (-23.0, -42.0), (-34.0, -49.0), (-39.0, -58.0),
            (-50.0, -65.0), (-55.0, -68.0), (-53.0, -73.0), (-47.0, -75.0),
            (-37.0, -73.0), (-23.0, -71.0), (-18.0, -71.0), (-5.0, -81.0),
            (3.0, -80.0), (8.0, -77.0), (12.0, -72.0),
        },
        new[]
        {
            (-10.0, 142.0), (-15.0, 145.0), (-22.0, 150.0), (-29.0, 153.0),
            (-37.0, 150.0), (-39.0, 146.0), (-35.0, 138.0), (-32.0, 116.0),
            (-21.0, 114.0), (-15.0, 122.0), (-12.0, 130.0), (-12.0, 137.0),
            (-10.0, 142.0),
        },
        new[]
        {
            (-65.0, -180.0), (-72.0, -150.0), (-78.0, -110.0), (-72.0, -70.0),
            (-65.0, -60.0), (-72.0, -25.0), (-70.0, 10.0), (-68.0, 50.0),
            (-66.0, 90.0), (-66.0, 130.0), (-72.0, 170.0), (-78.0, 180.0),
        },
    };
}
