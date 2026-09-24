using System.Windows;

namespace radians.beamlab.app;

/// <summary>
/// The compliance loop window over <see cref="ComplianceViewModel"/>: every
/// button is a command of the view model; the window supplies only the
/// dialogs and the shell the commands ask for. Sweeps run on a worker thread.
/// </summary>
public partial class ComplianceWindow : Window
{
    private readonly ComplianceViewModel _vm = new();

    public ComplianceWindow()
    {
        InitializeComponent();
        _vm.PickOpenFile = ViewServices.PickOpenFile;
        _vm.PickSaveFile = ViewServices.PickSaveFile;
        _vm.OpenDocument = ViewServices.OpenDocument;
        DataContext = _vm;
    }
}
