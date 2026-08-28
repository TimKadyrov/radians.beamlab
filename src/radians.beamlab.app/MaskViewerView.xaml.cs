using System;
using System.Windows;
using System.Windows.Controls;

namespace radians.beamlab.app;

/// <summary>
/// UserControl hosting the Mask Viewer tab: loads an S.1503-4 mask XML and
/// renders one latitude block with the shared <see cref="PfdHeatmapRenderer"/>
/// and <see cref="PfdProfileRenderer"/>. No geo map -- an imported mask table
/// carries no beam geometry to draw.
/// </summary>
public partial class MaskViewerView : UserControl
{
    private readonly MaskViewerViewModel _vm = new();
    private PfdHeatmapRenderer? _plotRenderer;
    private PfdProfileRenderer? _profileRenderer;

    public MaskViewerView()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_plotRenderer != null) return;   // wire once

        _plotRenderer    = new PfdHeatmapRenderer(PlotCanvas, _vm.Plot, _vm.Field);
        _profileRenderer = new PfdProfileRenderer(ProfileCanvas, _vm.Plot, _vm.Field);

        // Plot-time rasterisation: the exact table is re-sampled at the
        // canvas resolution whenever the source or the canvas changes.
        void SyncRasterTargets()
        {
            _vm.Field.TargetRasterW = Math.Max(64, (int)PlotCanvas.ActualWidth);
            _vm.Field.TargetRasterH = Math.Max(64, (int)PlotCanvas.ActualHeight);
        }
        void FullRedraw()
        {
            SyncRasterTargets();
            _plotRenderer!.Invalidate();
            _plotRenderer!.Redraw();
            _profileRenderer!.Redraw();
        }

        _vm.MaskChanged += FullRedraw;               // new file / latitude block
        _vm.Plot.SceneChanged += FullRedraw;         // mask-kind change re-labels axes
        _vm.Plot.ProfileCursorChanged += () =>       // cut moves are cheap re-slices
        {
            _plotRenderer!.Redraw();
            _profileRenderer!.Redraw();
        };
        PlotCanvas.SizeChanged    += (_, _) => FullRedraw();
        ProfileCanvas.SizeChanged += (_, _) => _profileRenderer!.Redraw();

        FullRedraw();
    }
}
