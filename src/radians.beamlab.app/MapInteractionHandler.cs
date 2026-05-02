using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace radians.beamlab.app;

/// <summary>
/// Wires mouse + wheel input on the map canvas to <see cref="MapViewport"/>
/// pan / zoom and to <see cref="MainViewModel"/> probe / sat-position. Keeps
/// the small drag-state machine local so MainWindow stays a thin composer.
///
/// Gestures:
///   - Left-click on a beam dot   → handled by <see cref="MapRenderer"/>
///   - Left-click empty map       → probe gain / PFD at point
///   - Left-button drag           → pan the view
///   - Right-button drag          → move sub-satellite point (live recompute)
///   - Mouse wheel                → zoom around the cursor
/// </summary>
public sealed class MapInteractionHandler
{
    private readonly Canvas _canvas;
    private readonly MapViewport _vp;
    private readonly MainViewModel _vm;
    private readonly MapRenderer _renderer;

    private bool _draggingSat;
    private bool _maybePan;
    private bool _panning;
    private Point _dragStart;
    private double _panStartCenterLat;
    private double _panStartCenterLon;

    /// <summary>Cursor must move more than this many pixels for a left-down to escalate to a pan.</summary>
    private const double DragStartThresholdPx = 4.0;

    public MapInteractionHandler(Canvas canvas, MapViewport viewport, MainViewModel vm, MapRenderer renderer)
    {
        _canvas = canvas;
        _vp = viewport;
        _vm = vm;
        _renderer = renderer;

        canvas.MouseLeftButtonDown  += OnLeftDown;
        canvas.MouseLeftButtonUp    += OnLeftUp;
        canvas.MouseRightButtonDown += OnRightDown;
        canvas.MouseRightButtonUp   += OnRightUp;
        canvas.MouseMove            += OnMove;
        canvas.MouseWheel           += OnWheel;
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        // Beam-marker clicks set Handled = true and never reach here.
        if (e.Handled) return;
        var pos = e.GetPosition(_canvas);
        if (_vp.FromCanvas(pos.X, pos.Y) is null) return;

        _maybePan = true;
        _panning = false;
        _dragStart = pos;
        _panStartCenterLat = _vp.ViewCenterLat;
        _panStartCenterLon = _vp.ViewCenterLon;
        _canvas.CaptureMouse();
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_canvas);
        var ll = _vp.FromCanvas(pos.X, pos.Y);
        if (ll is null) return;

        _draggingSat = true;
        _renderer.IsInteracting = true;
        _canvas.CaptureMouse();
        _canvas.Cursor = Cursors.SizeAll;
        _vm.StatusText = "dragging satellite — release to commit";
        _vm.SetSatPosition(ll.Value.latDeg, ll.Value.lonDeg);
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(_canvas);

        if (_draggingSat)
        {
            if (e.RightButton != MouseButtonState.Pressed) return;
            var ll = _vp.FromCanvas(pos.X, pos.Y);
            if (ll is null) return;
            _vm.SetSatPosition(ll.Value.latDeg, ll.Value.lonDeg);
            return;
        }

        if (_maybePan)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            double dx = pos.X - _dragStart.X, dy = pos.Y - _dragStart.Y;
            if (!_panning && (dx * dx + dy * dy < DragStartThresholdPx * DragStartThresholdPx)) return;
            if (!_panning)
            {
                _panning = true;
                _renderer.IsInteracting = true;
                _canvas.Cursor = Cursors.Hand;
            }
            _vp.PanByPixels(dx, dy, _panStartCenterLat, _panStartCenterLon);
        }
    }

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingSat) return;
        _draggingSat = false;
        _renderer.IsInteracting = false;
        _canvas.ReleaseMouseCapture();
        _canvas.Cursor = Cursors.Arrow;
        _vm.ReportSatPosition();
        _renderer.Redraw(); // pick up the heatmap after commit
        e.Handled = true;
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (!_maybePan) return;
        bool wasPanning = _panning;
        _maybePan = false;
        _panning = false;
        _renderer.IsInteracting = false;
        _canvas.ReleaseMouseCapture();
        _canvas.Cursor = Cursors.Arrow;

        if (wasPanning)
        {
            _renderer.Redraw();
            return;
        }

        // No drag → probe at the click point.
        var pos = e.GetPosition(_canvas);
        var ll = _vp.FromCanvas(pos.X, pos.Y);
        if (ll is null) return;
        _vm.Probe(ll.Value.latDeg, ll.Value.lonDeg);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(_canvas);
        double factor = e.Delta > 0 ? 0.8 : 1.25;
        if (_vp.ZoomAround(pos.X, pos.Y, factor)) e.Handled = true;
    }
}
