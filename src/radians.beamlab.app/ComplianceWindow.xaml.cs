using System;
using System.Windows;

namespace radians.beamlab.app;

/// <summary>Dialogs over <see cref="ComplianceViewModel"/>; sweeps run on a worker thread.</summary>
public partial class ComplianceWindow : Window
{
    private readonly ComplianceViewModel _vm = new();

    public ComplianceWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private void OnBrowseDesignClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Orbit design (*.orbitdesign.json)|*.orbitdesign.json|JSON|*.json",
        };
        if (dlg.ShowDialog() == true) _vm.DesignPath = dlg.FileName;
    }

    private void OnBrowseProfileClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Operation profile (*.opprofile.json)|*.opprofile.json|JSON|*.json",
        };
        if (dlg.ShowDialog() == true) _vm.ProfilePath = dlg.FileName;
    }

    private async void OnRunClick(object sender, RoutedEventArgs e) => await _vm.RunAsync();

    private async void OnAdviseClick(object sender, RoutedEventArgs e) => await _vm.AdviseAsync();

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        try { _vm.ApplyFoundAlpha(); }
        catch (Exception ex) { _vm.StatusText = "apply failed: " + ex.Message; }
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "compliance.csv",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            System.IO.File.WriteAllText(dlg.FileName, _vm.BuildCsv());
            _vm.StatusText = "table written: " + dlg.FileName;
        }
        catch (Exception ex) { _vm.StatusText = "export failed: " + ex.Message; }
    }
}
