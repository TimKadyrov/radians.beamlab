using System.Windows;

namespace radians.beamlab.app;

/// <summary>
/// Thin composer: instantiates the ViewModel + map subsystems and wires the
/// renderer's redraw to scene + viewport changes. Buttons bind to VM commands;
/// drawing lives in <see cref="MapRenderer"/>, mouse / wheel input in
/// <see cref="MapInteractionHandler"/>, viewport state in
/// <see cref="MapViewport"/>, and all business logic in
/// <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private MapViewport? _viewport;
    private MapRenderer? _renderer;
    private MapInteractionHandler? _interaction;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewport = new MapViewport();
        _renderer = new MapRenderer(MapCanvas, _viewport, _vm);
        _interaction = new MapInteractionHandler(MapCanvas, _viewport, _vm, _renderer);

        _vm.SceneChanged      += _renderer.Redraw;
        _viewport.Changed     += _renderer.Redraw;
        MapCanvas.SizeChanged += (_, _) => _renderer!.Redraw();

        // VM asks the View to open a plot window when "Show pattern" is invoked.
        _vm.ShowPatternRequested += (pattern, header) =>
        {
            var w = new PatternPlotWindow(pattern, header) { Owner = this };
            w.Show();
        };

        int n = _vm.Coastlines.Polylines.Count;
        int countryCount = _vm.Coastlines.Countries.Count;
        _vm.StatusText = $"loaded {n} coastline rings, {countryCount} countries from {_vm.Coastlines.SourceLabel}";
        _renderer.Redraw();
    }
}
