using System;
using System.Windows;
using System.Windows.Controls;

namespace radians.beamlab.app;

/// <summary>
/// Decorator that lays its child out as the largest centred square that fits
/// the available space. Used by the Mask Viewer so the plot canvases stay
/// square (equal degrees-per-pixel on both axes) regardless of window shape.
/// </summary>
public sealed class SquarePanel : Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        double w = double.IsInfinity(constraint.Width) ? 300 : constraint.Width;
        double h = double.IsInfinity(constraint.Height) ? 300 : constraint.Height;
        double s = Math.Min(w, h);
        Child?.Measure(new Size(s, s));
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        double s = Math.Min(arrangeSize.Width, arrangeSize.Height);
        double x = (arrangeSize.Width - s) / 2.0;
        double y = (arrangeSize.Height - s) / 2.0;
        Child?.Arrange(new Rect(x, y, s, s));
        return arrangeSize;
    }
}
