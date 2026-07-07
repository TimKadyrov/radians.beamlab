using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace radians.beamlab.app;

/// <summary>
/// Pan / zoom mouse input for the PFD-tab geo map: left-drag pans, mouse
/// wheel zooms around the cursor. A trimmed-down sibling of
/// <see cref="MapInteractionHandler"/> — the PFD map is read-only, so there
/// is no probe, beam toggle or satellite drag. Same gesture feel as tab 1
/// (4 px drag threshold, 0.8 / 1.25 wheel factors).
/// </summary>
public sealed class PfdMapInteractionHandler
{
    private readonly Canvas _canvas;
    private readonly MapViewport _vp;

    private bool _maybePan;
    private bool _panning;
    private Point _dragStart;
    private double _panStartCenterLat;
    private double _panStartCenterLon;

    /// <summary>Cursor must move more than this many pixels for a left-down to escalate to a pan.</summary>
    private const double DragStartThresholdPx = 4.0;

    public PfdMapInteractionHandler(Canvas canvas, MapViewport viewport)
    {
        _canvas = canvas;
        _vp = viewport;

        canvas.MouseLeftButtonDown += OnLeftDown;
        canvas.MouseLeftButtonUp   += OnLeftUp;
        canvas.MouseMove           += OnMove;
        canvas.MouseWheel          += OnWheel;
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_canvas);
        if (_vp.FromCanvas(pos.X, pos.Y) is null) return;

        _maybePan = true;
        _panning = false;
        _dragStart = pos;
        _panStartCenterLat = _vp.ViewCenterLat;
        _panStartCenterLon = _vp.ViewCenterLon;
        _canvas.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_maybePan) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(_canvas);
        double dx = pos.X - _dragStart.X, dy = pos.Y - _dragStart.Y;
        if (!_panning && (dx * dx + dy * dy < DragStartThresholdPx * DragStartThresholdPx)) return;
        if (!_panning)
        {
            _panning = true;
            _canvas.Cursor = Cursors.Hand;
        }
        _vp.PanByPixels(dx, dy, _panStartCenterLat, _panStartCenterLon);
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (!_maybePan) return;
        _maybePan = false;
        _panning = false;
        _canvas.ReleaseMouseCapture();
        _canvas.Cursor = Cursors.Arrow;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(_canvas);
        double factor = e.Delta > 0 ? 0.8 : 1.25;
        if (_vp.ZoomAround(pos.X, pos.Y, factor)) e.Handled = true;
    }
}
