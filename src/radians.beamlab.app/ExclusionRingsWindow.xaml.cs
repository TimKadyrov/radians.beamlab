using System.Windows;

namespace radians.beamlab.app;

/// <summary>
/// Modal editor for the advanced GSO exclusion rings. DataContext is the
/// <see cref="PfdMaskViewModel"/>; the grid binds directly to its live
/// <see cref="PfdMaskViewModel.ExclusionRings"/> collection, so edits apply
/// immediately (the VM re-gates and redraws on every ring change).
/// </summary>
public partial class ExclusionRingsWindow : Window
{
    public ExclusionRingsWindow(PfdMaskViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
