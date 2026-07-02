using System.Windows;
using System.Windows.Controls;

namespace radians.beamlab.app;

/// <summary>
/// UserControl hosting the PFD-mask (Az/El) tab. Instantiates its own
/// <see cref="PfdMaskViewModel"/> and both renderers — a small geo map for
/// footprints (top) and the az/el PFD heatmap (bottom).
/// </summary>
public partial class PfdMaskView : UserControl
{
    private readonly PfdMaskViewModel _vm = new();
    private readonly AzElPfdField _field = new();
    private AzElPfdRenderer? _plotRenderer;
    private PfdMapRenderer? _mapRenderer;
    private PfdVsElRenderer? _profileRenderer;

    public PfdMaskView()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _mapRenderer     = new PfdMapRenderer(MapCanvas, _vm, _vm.Coastlines);
        _plotRenderer    = new AzElPfdRenderer(PlotCanvas, _vm, _field);
        _profileRenderer = new PfdVsElRenderer(ProfileCanvas, _vm, _field);

        // Scene changes (rebuild / gating / alpha overlay toggle) invalidate the
        // pre-rasterised PFD bitmap so the next Redraw regenerates the shared
        // field. The profile renderer reads its slices from that same field, so
        // it must redraw *after* the heatmap renderer has rebuilt it.
        _vm.SceneChanged += () =>
        {
            _plotRenderer!.Invalidate();
            _plotRenderer!.Redraw();
            _profileRenderer!.Redraw();
            _mapRenderer!.Redraw();
        };
        // Azimuth-cursor moves are cheap: re-slice the retained grid (profile) and
        // re-blit the cached heatmap bitmap with a new cursor line. No rebuild.
        _vm.ProfileCursorChanged += () =>
        {
            _plotRenderer!.Redraw();
            _profileRenderer!.Redraw();
        };
        MapCanvas.SizeChanged     += (_, _) => _mapRenderer!.Redraw();
        PlotCanvas.SizeChanged    += (_, _) => _plotRenderer!.Redraw();
        ProfileCanvas.SizeChanged += (_, _) => _profileRenderer!.Redraw();

        _mapRenderer.Redraw();
        _plotRenderer.Redraw();
        _profileRenderer.Redraw();
    }
}
