using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace radians.beamlab.app;

/// <summary>
/// Shared WPF drawing helpers for the equirectangular maps (composite-gain map
/// and PFD-tab map). Projection differs between the two (pan/zoom viewport vs
/// fixed full-earth), so callers pass their own <c>project</c> function.
/// </summary>
public static class MapDraw
{
    /// <summary>
    /// Add a polyline of (lat, lon) points to <paramref name="canvas"/>, breaking
    /// it wherever a single projected segment exceeds <paramref name="maxSegPx"/>
    /// (an antimeridian wrap or a clipping artifact) rather than drawing a chord
    /// across the whole map.
    /// </summary>
    public static void AddSplitPolyline(Canvas canvas, IReadOnlyList<(double lat, double lon)> pts,
                                        Brush stroke, double thickness, double maxSegPx,
                                        Func<double, double, (double x, double y)> project)
    {
        Polyline? cur = null;
        Point? prev = null;
        foreach (var (lat, lon) in pts)
        {
            var (x, y) = project(lat, lon);
            var p = new Point(x, y);
            if (prev is Point q)
            {
                double dx = p.X - q.X, dy = p.Y - q.Y;
                if (Math.Abs(dx) > maxSegPx || Math.Abs(dy) > maxSegPx) cur = null;
            }
            if (cur is null)
            {
                cur = new Polyline { Stroke = stroke, StrokeThickness = thickness, IsHitTestVisible = false };
                canvas.Children.Add(cur);
            }
            cur.Points.Add(p);
            prev = p;
        }
    }
}
