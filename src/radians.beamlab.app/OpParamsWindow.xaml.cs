using System;
using System.Windows;

namespace radians.beamlab.app;

/// <summary>
/// The operating-parameters designer window over <see cref="OpParamsViewModel"/>:
/// every button is a command of the view model, the window supplies the
/// dialogs. Filing-parameter help comes from the
/// shared ParameterCatalog (the card deck's twin).
/// </summary>
public partial class OpParamsWindow : Window
{
    private readonly OpParamsViewModel _vm = new();

    public OpParamsWindow()
    {
        InitializeComponent();
        _vm.PickOpenFile = ViewServices.PickOpenFile;
        _vm.PickSaveFile = ViewServices.PickSaveFile;
        _vm.OpenDocument = ViewServices.OpenDocument;
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
}
