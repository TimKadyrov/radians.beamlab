using System;
using System.Windows;

namespace radians.beamlab.app;

/// <summary>
/// The operating-parameters designer window: dialogs over
/// <see cref="OpParamsViewModel"/>. Filing-parameter help comes from the
/// shared ParameterCatalog (the card deck's twin).
/// </summary>
public partial class OpParamsWindow : Window
{
    private readonly OpParamsViewModel _vm = new();

    public OpParamsWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        WireToolTips();
    }

    private void WireToolTips()
    {
        static string? Cat(string name) => radians.beamlab.ParameterCatalog.Find(name)?.ToolTipText;
        FreqMinBox.ToolTip = Cat("FREQ_MIN / FREQ_MAX");
        EsDensityBox.ToolTip = Cat("ES_DENSITY · ES_DISTANCE");
        EsLatBox.ToolTip = Cat("ES_LAT_MIN / ES_LAT_MAX");
        AngleSatBox.ToolTip = Cat("MIN_ANGLE_AT_SAT");
        AngleEsBox.ToolTip = Cat("MIN_ANGLE_AT_ES");
        CoFreqBox.ToolTip = Cat("MAX_CO_FREQ");
        CoFreqSatBox.ToolTip = Cat("MAX_CO_FREQ_SAT");
        DurationBox.ToolTip = Cat("MIN_DURATION");
        ElevHeaderBox.ToolTip = Cat("MIN_ELEV");
        MinExcludeBox.ToolTip = Cat("MIN_EXCLUDE");
        MinElevBox.ToolTip = Cat("MIN_ELEV");
        MaxCoFreqArrBox.ToolTip = Cat("MAX_CO_FREQ");
        MinDurationArrBox.ToolTip = Cat("MIN_DURATION");
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = _vm.BuildJson();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Operating parameters (*.opparams.json)|*.opparams.json",
                FileName = "set.opparams.json",
            };
            if (dlg.ShowDialog() != true) return;
            System.IO.File.WriteAllText(dlg.FileName, json);
            _vm.StatusText = "saved: " + dlg.FileName;
        }
        catch (Exception ex) { _vm.StatusText = "save failed: " + ex.Message; }
    }

    private void OnLoadClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Operating parameters (*.opparams.json)|*.opparams.json|JSON|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _vm.LoadJson(System.IO.File.ReadAllText(dlg.FileName));
            _vm.StatusText = "loaded: " + dlg.FileName;
        }
        catch (Exception ex) { _vm.StatusText = "load failed: " + ex.Message; }
    }

    private void OnDeriveBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Orbit design (*.orbitdesign.json)|*.orbitdesign.json|JSON|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        _vm.DeriveDesignPath = dlg.FileName;
    }

    private void OnDeriveProfileBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Operation profile (*.opprofile.json)|*.opprofile.json|JSON|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        _vm.DeriveProfilePath = dlg.FileName;
    }

    private async void OnDeriveClick(object sender, RoutedEventArgs e) => await _vm.DeriveAsync();

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "R-set XML (*.xml)|*.xml",
            FileName = "opparams.xml",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _vm.ExportXml(dlg.FileName);
            _vm.StatusText = "R XML written: " + dlg.FileName
                + " — register it in the SNS builder as an f_mask R row";
        }
        catch (Exception ex) { _vm.StatusText = "export failed: " + ex.Message; }
    }
}
