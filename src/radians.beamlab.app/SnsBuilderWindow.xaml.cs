using System.Windows;

namespace radians.beamlab.app;

/// <summary>
/// The SNS v10 builder window over <see cref="SnsBuilderViewModel"/>: every
/// button is a command of the view model, the window supplies the dialogs
/// and opens the R-set designer. Donor databases are probed at the known
/// reference location and picked interactively otherwise.
/// </summary>
public partial class SnsBuilderWindow : Window
{
    private readonly SnsBuilderViewModel _vm = new();

    public SnsBuilderWindow()
    {
        InitializeComponent();
        _vm.PickOpenFiles = ViewServices.PickOpenFiles;
        _vm.PickOpenFileTitled = ViewServices.PickOpenFileTitled;
        _vm.PickSaveFile = ViewServices.PickSaveFile;
        _vm.OpenOpParamsRequested += () => new OpParamsWindow { Owner = this }.Show();
        DataContext = _vm;
    }
}
