using System.Windows;
using Microsoft.Win32;

namespace radians.beamlab.app;

/// <summary>
/// Modal dialog driving <see cref="MaskXmlExportViewModel"/> — collects the
/// mask metadata / latitude range / resolution and runs the export.
/// </summary>
public partial class MaskXmlExportWindow : Window
{
    private readonly MaskXmlExportViewModel _vm;

    public MaskXmlExportWindow(MaskXmlExportViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save PFD mask XML",
            Filter = "XML mask (*.xml)|*.xml|All files (*.*)|*.*",
            DefaultExt = ".xml",
            FileName = $"mask ntc_id {_vm.NtcId} mask_id {_vm.MaskId}.xml",
        };
        if (dlg.ShowDialog(this) == true) _vm.OutputPath = dlg.FileName;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
