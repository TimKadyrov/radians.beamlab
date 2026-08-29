using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace radians.beamlab.app;

/// <summary>
/// The SNS v10 builder window: dialogs and database writes over
/// <see cref="SnsBuilderViewModel"/>. Donor databases are probed at the
/// known reference location and picked interactively otherwise.
/// </summary>
public partial class SnsBuilderWindow : Window
{
    private const string DefaultDonorSrs =
        @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 SRS.MDB";
    private const string DefaultDonorMasks =
        @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 Masks.MDB";

    private readonly SnsBuilderViewModel _vm = new();

    public SnsBuilderWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private void OnAddShellClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Orbit design (*.orbitdesign.json)|*.orbitdesign.json|JSON|*.json",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        foreach (string f in dlg.FileNames)
        {
            try { _vm.AddShellFile(f); }
            catch (Exception ex) { _vm.StatusText = $"{Path.GetFileName(f)}: {ex.Message}"; return; }
        }
        _vm.StatusText = _vm.SummaryText();
    }

    private void OnRemoveShellClick(object sender, RoutedEventArgs e)
    {
        if (ShellsGrid.SelectedItem is ShellEntry s) _vm.Shells.Remove(s);
    }

    private void OnAddMaskClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Mask XML (*.xml)|*.xml",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        int nextId = 1;
        foreach (var m in _vm.Masks) nextId = Math.Max(nextId, m.MaskId + 1);
        foreach (string f in dlg.FileNames)
            _vm.Masks.Add(new MaskEntry { MaskId = nextId++, FilePath = f });
        _vm.StatusText = "set f_mask (P/E/S/R), type and the frequency range per row";
    }

    private void OnRemoveMaskClick(object sender, RoutedEventArgs e)
    {
        if (MasksGrid.SelectedItem is MaskEntry m) _vm.Masks.Remove(m);
    }

    private void OnAddFreqClick(object sender, RoutedEventArgs e) => _vm.Frequencies.Add(new FreqEntry());

    private void OnRemoveFreqClick(object sender, RoutedEventArgs e)
    {
        if (FreqGrid.SelectedItem is FreqEntry f) _vm.Frequencies.Remove(f);
    }

    private void OnPreviewClick(object sender, RoutedEventArgs e) => _vm.StatusText = _vm.SummaryText();

    private void OnBuildClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var notice = _vm.BuildNotice();   // validates

            string donorSrs = DefaultDonorSrs;
            if (!File.Exists(donorSrs) && !PickFile("Select a donor SRS database", ref donorSrs)) return;
            var save = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "SRS database (*.mdb)|*.mdb",
                FileName = $"{_vm.NtcId} SRS.MDB",
            };
            if (save.ShowDialog() != true) return;
            SrsMdbWriter.WriteSrs(donorSrs, save.FileName, notice);

            string masksNote = "";
            var contents = _vm.BuildMaskContents();
            if (contents.Count > 0)
            {
                string donorMasks = DefaultDonorMasks;
                if (!File.Exists(donorMasks) && !PickFile("Select a donor Masks database", ref donorMasks)) return;
                string masksPath = Path.Combine(Path.GetDirectoryName(save.FileName)!,
                    $"{_vm.NtcId} Masks.MDB");
                var stored = SrsMdbWriter.WriteMasks(donorMasks, masksPath, _vm.NtcId, _vm.SatName, contents);
                var bad = stored.Where(r => r.Status != 0).ToList();
                masksNote = bad.Count == 0
                    ? $"; Masks: {stored.Count} row(s) -> {masksPath}"
                    : "; mask store FAILED: " + string.Join(",", bad.Select(r => $"{r.MaskId}:{r.Status}"));
            }
            _vm.StatusText = $"SRS written: {save.FileName} ({notice.Orbits.Count} orbit / " +
                             $"{notice.Phases.Count} phase rows){masksNote}";
        }
        catch (Exception ex) { _vm.StatusText = "build failed: " + ex.Message; }
    }

    private static bool PickFile(string title, ref string path)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = "Database (*.mdb)|*.mdb" };
        if (dlg.ShowDialog() != true) return false;
        path = dlg.FileName;
        return true;
    }
}
