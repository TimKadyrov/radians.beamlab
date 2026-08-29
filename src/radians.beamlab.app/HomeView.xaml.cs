using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace radians.beamlab.app;

/// <summary>
/// The Home tab: launcher cards for the other tabs, documentation links and
/// the version. The hosting window supplies <see cref="OpenTab"/> so a card
/// can activate its function's tab.
/// </summary>
public partial class HomeView : UserControl
{
    private readonly HomeViewModel _vm = new();

    /// <summary>Set by the hosting window: activate the tab at this index.</summary>
    public System.Action<int>? OpenTab { get; set; }

    public HomeView()
    {
        InitializeComponent();
        DataContext = _vm;
        GuideButton.IsEnabled = _vm.UserGuidePath is not null;
        CardsButton.IsEnabled = _vm.ParameterCardsPath is not null;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index }) OpenTab?.Invoke(index);
    }

    private void OnGuideClick(object sender, RoutedEventArgs e) => Shell(_vm.UserGuidePath);
    private void OnCardsClick(object sender, RoutedEventArgs e) => Shell(_vm.ParameterCardsPath);

    private static void Shell(string? path)
    {
        if (path is null) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
