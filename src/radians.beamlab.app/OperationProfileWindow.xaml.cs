using System;
using System.Windows;

namespace radians.beamlab.app;

/// <summary>Dialogs over <see cref="OperationProfileViewModel"/>.</summary>
public partial class OperationProfileWindow : Window
{
    private readonly OperationProfileViewModel _vm = new();

    public OperationProfileWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        WireToolTips();
    }

    /// <summary>
    /// Parameter help reuses the shared ParameterCatalog (the card deck's
    /// twin) wherever a card exists; payload knobs carry authored inline
    /// tooltips in the XAML.
    /// </summary>
    private void WireToolTips()
    {
        static string? Cat(string name) => radians.beamlab.ParameterCatalog.Find(name)?.ToolTipText;
        FreqBox.ToolTip = Cat("FREQ_MIN / FREQ_MAX");
        UlFreqBox.ToolTip = Cat("FREQ_MIN / FREQ_MAX");
        MinElevBox.ToolTip = Cat("MIN_ELEV");
        MinElevByLatBox.ToolTip = Cat("MIN_ELEV");
        LatBox.ToolTip = Cat("ES_LAT_MIN / ES_LAT_MAX");
        CellBox.ToolTip = Cat("CellPitchKm / coverageRadiusKm");
        PolicyCombo.ToolTip = Cat("SelectionPolicy");
        NcoBox.ToolTip = Cat("MAX_CO_FREQ");
        NcoByLatBox.ToolTip = Cat("MAX_CO_FREQ");
        NcoSatBox.ToolTip = Cat("MAX_CO_FREQ_SAT");
        DlAngleSatBox.ToolTip = Cat("MIN_ANGLE_AT_SAT");
        DlAngleEsBox.ToolTip = Cat("MIN_ANGLE_AT_ES");
        UlAngleSatBox.ToolTip = Cat("MIN_ANGLE_AT_SAT");
        UlAngleEsBox.ToolTip = Cat("MIN_ANGLE_AT_ES");
        HoldBox.ToolTip = Cat("MIN_DURATION");
        DemandBox.ToolTip = Cat("DemandLinks");
        ActivityBox.ToolTip = Cat("ActivityFactor");
        FractionBox.ToolTip = Cat("OperationalFraction");
        DutyBox.ToolTip = Cat("IlluminationDutyCycle");
        AlphaBox.ToolTip = Cat("MIN_EXCLUDE");
        AlphaByLatBox.ToolTip = Cat("MIN_EXCLUDE");
        EsPowerBox.ToolTip = Cat("PowerDbw");
        PowerRefBox.ToolTip = Cat("PowerControlRefElevDeg");
    }

    private void OnBrowseMaskClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "S.1503-4 PFD mask (*.xml)|*.xml",
        };
        if (dlg.ShowDialog() != true) return;
        _vm.MaskXmlPathText = dlg.FileName;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = _vm.BuildJson();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Operation profile (*.opprofile.json)|*.opprofile.json",
                FileName = "system.opprofile.json",
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
            Filter = "Operation profile (*.opprofile.json)|*.opprofile.json|JSON|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _vm.LoadJson(System.IO.File.ReadAllText(dlg.FileName));
            _vm.StatusText = "loaded: " + dlg.FileName;
        }
        catch (Exception ex) { _vm.StatusText = "load failed: " + ex.Message; }
    }
}
