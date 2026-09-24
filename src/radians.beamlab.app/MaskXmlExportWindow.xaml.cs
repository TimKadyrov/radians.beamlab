using System.Windows;
using Microsoft.Win32;

namespace radians.beamlab.app;

/// <summary>
/// Modal dialog driving <see cref="MaskXmlExportViewModel"/> -- collects the
/// mask metadata / latitude range / resolution and runs the export.
/// </summary>
public partial class MaskXmlExportWindow : Window
{
    private readonly MaskXmlExportViewModel _vm;

    public MaskXmlExportWindow(MaskXmlExportViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        // The view model's Browse asks for the file; the dialog is this window's.
        _vm.PickSaveFile = (filter, fileName) =>
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save PFD mask XML", Filter = filter, DefaultExt = ".xml", FileName = fileName,
            };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        };
        DataContext = vm;
        // Close is the dialog's cancel button (IsCancel), so it needs no handler.
    }
}
