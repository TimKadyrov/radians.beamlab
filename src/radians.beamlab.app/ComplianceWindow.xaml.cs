using System;
using System.Windows;

namespace radians.beamlab.app;

/// <summary>Dialogs over <see cref="ComplianceViewModel"/>; sweeps run on a worker thread.</summary>
public partial class ComplianceWindow : Window
{
    private readonly ComplianceViewModel _vm = new();

    private readonly string? _guidePath;

    public ComplianceWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        string? docs = HomeViewModel.FindDocsDir(AppContext.BaseDirectory);
        string? guide = docs is null ? null : System.IO.Path.Combine(docs, "compliance-loop.html");
        _guidePath = guide is not null && System.IO.File.Exists(guide) ? guide : null;
        GuideBtn.IsEnabled = _guidePath is not null;
    }

    private void OnGuideClick(object sender, RoutedEventArgs e)
    {
        if (_guidePath is null) return;
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(_guidePath) { UseShellExecute = true });
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

    private void OnBrowseLimitsDbClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "BR limits database (*.mdb)|*.mdb",
        };
        if (dlg.ShowDialog() == true) _vm.LimitsDbPathText = dlg.FileName;
    }

    private void OnLoadLimitsClick(object sender, RoutedEventArgs e) => _vm.LoadLimitsFromDb();

    private void OnUseLimitClick(object sender, RoutedEventArgs e) => _vm.UseSelectedLimit();

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
