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
    /// Every field's help comes from the shared ParameterCatalog (the
    /// card deck's twin) -- one card per parameter, UI and documentation
    /// unable to drift.
    /// </summary>
    private void WireToolTips()
    {
        static string? Cat(string name) => radians.beamlab.ParameterCatalog.Find(name)?.ToolTipText;
        FreqBox.ToolTip = Cat("FREQ_MIN / FREQ_MAX");
        UlFreqBox.ToolTip = Cat("FREQ_MIN / FREQ_MAX");
        FootprintCombo.ToolTip = Cat("FootprintSource");
        MaskPathBox.ToolTip = Cat("FootprintSource");
        GainBox.ToolTip = Cat("GainPeakDbi");
        CellRadBox.ToolTip = Cat("BeamCellRadiusKm");
        SlrBox.ToolTip = Cat("TaylorSlrDb · TaylorNbar");
        NbarBox.ToolTip = Cat("TaylorSlrDb · TaylorNbar");
        FloorBox.ToolTip = Cat("PatternFloorDbi");
        EirpBox.ToolTip = Cat("TxEirpDbw");
        PowerModeCombo.ToolTip = Cat("PowerMode");
        AggCombo.ToolTip = Cat("Aggregation · ReuseClusterIndex");
        ReuseBox.ToolTip = Cat("Aggregation · ReuseClusterIndex");
        RefBwBox.ToolTip = Cat("RefBwKHz");
        EsDishBox.ToolTip = Cat("EsDishM");
        MinElevBox.ToolTip = Cat("MIN_ELEV");
        MinElevByLatBox.ToolTip = Cat("MIN_ELEV");
        LatBox.ToolTip = Cat("Service area");
        LonBox.ToolTip = Cat("Service area");
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
