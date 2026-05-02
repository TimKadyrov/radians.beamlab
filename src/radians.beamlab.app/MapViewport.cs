using System;

namespace radians.beamlab.app;

/// <summary>
/// Equirectangular viewport state + projection helpers for the map canvas.
///
/// The viewport is parameterised by (centre lat, centre lon, latHalf): the
/// visible region is always 2:1 lon:lat in degrees, with longitude half-extent
/// = 2·latHalf. The map rectangle (mapX, mapY, mapW, mapH) is the inscribed
/// 2:1 rectangle inside the canvas; <see cref="TryRecomputePlacement"/> sets it
/// from the canvas's current size.
///
/// Mutating pan/zoom helpers raise <see cref="Changed"/> so the renderer can
/// redraw without the caller having to know the exact set of properties.
/// </summary>
public sealed class MapViewport
{
    public double MapX { get; private set; }
    public double MapY { get; private set; }
    public double MapW { get; private set; }
    public double MapH { get; private set; }

    public double ViewCenterLat { get; private set; } = 0.0;
    public double ViewCenterLon { get; private set; } = 0.0;
    public double ViewLatHalf   { get; private set; } = 90.0;

    public const double MinLatHalf = 1.5;
    public const double MaxLatHalf = 90.0;

    public event Action? Changed;

    public double ViewLonHalf => ViewLatHalf * 2.0;
    public double ViewLatMin  => ViewCenterLat - ViewLatHalf;
    public double ViewLatMax  => ViewCenterLat + ViewLatHalf;
    public double ViewLonMin  => ViewCenterLon - ViewLonHalf;
    public double ViewLonMax  => ViewCenterLon + ViewLonHalf;

    /// <summary>Inscribe a 2:1 rectangle into the canvas size; returns false if it's degenerate.</summary>
    public bool TryRecomputePlacement(double canvasW, double canvasH)
    {
        if (canvasW <= 1 || canvasH <= 1) { MapW = MapH = 0; return false; }
        double byHeight = canvasH * 2.0;
        if (byHeight <= canvasW) { MapW = byHeight; MapH = canvasH; }
        else { MapW = canvasW; MapH = canvasW / 2.0; }
        MapX = (canvasW - MapW) * 0.5;
        MapY = (canvasH - MapH) * 0.5;
        return true;
    }

    /// <summary>(lat, lon) → canvas pixel. Longitudes are wrapped to be near the view centre.</summary>
    public (double x, double y) ToCanvas(double latDeg, double lonDeg)
    {
        double dlon = ((lonDeg - ViewCenterLon + 540.0) % 360.0) - 180.0;
        double mappedLon = ViewCenterLon + dlon;
        double x = MapX + (mappedLon - ViewLonMin) / (ViewLonMax - ViewLonMin) * MapW;
        double y = MapY + (ViewLatMax - latDeg) / (ViewLatMax - ViewLatMin) * MapH;
        return (x, y);
    }

    /// <summary>Canvas pixel → (lat, lon), or null if outside the map rect or past a pole.</summary>
    public (double latDeg, double lonDeg)? FromCanvas(double x, double y)
    {
        if (MapW <= 0) return null;
        if (x < MapX || x > MapX + MapW || y < MapY || y > MapY + MapH) return null;
        double lon = ViewLonMin + (x - MapX) / MapW * (ViewLonMax - ViewLonMin);
        double lat = ViewLatMax - (y - MapY) / MapH * (ViewLatMax - ViewLatMin);
        if (lat > 90 || lat < -90) return null;
        return (lat, lon);
    }

    /// <summary>Translate the view by (dx, dy) canvas pixels relative to a saved start centre.</summary>
    public void PanByPixels(double dxPx, double dyPx, double startCenterLat, double startCenterLon)
    {
        if (MapW <= 0 || MapH <= 0) return;
        double lonRange = ViewLonMax - ViewLonMin;
        double latRange = ViewLatMax - ViewLatMin;
        double newCLon = startCenterLon - dxPx / MapW * lonRange;
        double newCLat = startCenterLat + dyPx / MapH * latRange;
        ViewCenterLat = Math.Clamp(newCLat, -90.0 + ViewLatHalf, 90.0 - ViewLatHalf);
        ViewCenterLon = newCLon;
        Changed?.Invoke();
    }

    /// <summary>
    /// Zoom around a canvas pixel. If <paramref name="zoomIn"/> is true, the
    /// view shrinks (lat-half ÷ <paramref name="factor"/>); otherwise it grows.
    /// The world point under the cursor stays fixed.
    /// </summary>
    public bool ZoomAround(double cursorX, double cursorY, double factor)
    {
        var pre = FromCanvas(cursorX, cursorY);
        if (pre is null) return false;

        double newHalf = Math.Clamp(ViewLatHalf * factor, MinLatHalf, MaxLatHalf);
        if (Math.Abs(newHalf - ViewLatHalf) < 1e-6) return false;
        ViewLatHalf = newHalf;

        double fx = (cursorX - MapX) / MapW;
        double fy = (cursorY - MapY) / MapH;
        ViewCenterLon = pre.Value.lonDeg - (2.0 * fx - 1.0) * ViewLonHalf;
        double newLat = pre.Value.latDeg + (1.0 - 2.0 * fy) * ViewLatHalf;
        ViewCenterLat = Math.Clamp(newLat, -90.0 + ViewLatHalf, 90.0 - ViewLatHalf);
        Changed?.Invoke();
        return true;
    }
}
