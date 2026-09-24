using System.Windows;
using System.Windows.Controls;

namespace radians.beamlab.app;

/// <summary>
/// The Home tab: launcher cards for the other tabs, documentation links and
/// the version. Every button is a command of <see cref="HomeViewModel"/>; the
/// view opens what a command asks for -- a tab through <see cref="OpenTab"/>,
/// which the hosting window supplies, a window, or a document.
/// </summary>
public partial class HomeView : UserControl
{
    private readonly HomeViewModel _vm = new();

    /// <summary>Set by the hosting window: activate the tab at this index.</summary>
    public System.Action<int>? OpenTab { get; set; }

    public HomeView()
    {
        InitializeComponent();
        _vm.OpenDocument = ViewServices.OpenDocument;
        _vm.OpenCardRequested += OpenCard;
        DataContext = _vm;
    }

    private void OpenCard(HomeCard card)
    {
        if (card.TabIndex >= 0) { OpenTab?.Invoke(card.TabIndex); return; }
        Window w = card.Key switch
        {
            "profile" => new OperationProfileWindow(),
            "compliance" => new ComplianceWindow(),
            "opparams" => new OpParamsWindow(),
            "simulation" => new SimulationWindow(),
            _ => new SnsBuilderWindow(),
        };
        w.Owner = Window.GetWindow(this);
        w.Show();
    }
}
