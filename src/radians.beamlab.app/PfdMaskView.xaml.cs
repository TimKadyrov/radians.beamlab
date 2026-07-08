using System.Windows;
using System.Windows.Controls;

namespace radians.beamlab.app;

/// <summary>
/// UserControl hosting the PFD-mask tab (az/el or alpha/deltaLongitude coordinates).
/// Instantiates its own <see cref="PfdMaskViewModel"/>, the shared
/// <see cref="PfdMaskField"/>, and three renderers -- geo map for footprints
/// (top), mask heatmap (bottom left) and profile slice (bottom right).
/// </summary>
public partial class PfdMaskView : UserControl
{
    private readonly PfdMaskViewModel _vm = new();
    private readonly PfdMaskField _field = new();
    private PfdHeatmapRenderer? _plotRenderer;
    private MapViewport? _mapViewport;
    private PfdMapRenderer? _mapRenderer;
    private PfdMapInteractionHandler? _mapInteraction;
    private PfdProfileRenderer? _profileRenderer;

    public PfdMaskView()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _mapViewport     = new MapViewport();
        _mapRenderer     = new PfdMapRenderer(MapCanvas, _mapViewport, _vm, _vm.Coastlines);
        _mapInteraction  = new PfdMapInteractionHandler(MapCanvas, _mapViewport);
        _plotRenderer    = new PfdHeatmapRenderer(PlotCanvas, _vm, _field);
        _profileRenderer = new PfdProfileRenderer(ProfileCanvas, _vm, _field);

        // Pan / zoom moves the viewport -- redraw just the map.
        _mapViewport.Changed += _mapRenderer.Redraw;

        // Open the advanced-exclusion dialog on request.
        _vm.EditExclusionRingsRequested += () =>
        {
            var dlg = new ExclusionRingsWindow(_vm) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        };

        // Open the mask-XML export dialog on request.
        _vm.GenerateXmlRequested += () =>
        {
            var dlg = new MaskXmlExportWindow(new MaskXmlExportViewModel(_vm)) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        };

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
