using System;
using System.Diagnostics;

namespace radians.beamlab.app;

/// <summary>
/// The view side of what the view models need from the desktop: file
/// dialogs and opening a document. A view model asks through delegates it
/// exposes (PickOpenFile, PickSaveFile, OpenDocument); its window assigns
/// these. Button logic stays in the view model's commands, and dialogs stay
/// in the view.
/// </summary>
public static class ViewServices
{
    /// <summary>An open-file dialog; the chosen path, or null when cancelled.</summary>
    public static string? PickOpenFile(string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    /// <summary>A titled open-file dialog; the chosen path, or null when cancelled.</summary>
    public static string? PickOpenFileTitled(string title, string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    /// <summary>An open-file dialog over several files; the chosen paths, or null when cancelled.</summary>
    public static string[]? PickOpenFiles(string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter, Multiselect = true };
        return dlg.ShowDialog() == true && dlg.FileNames.Length > 0 ? dlg.FileNames : null;
    }

    /// <summary>A save-file dialog; the chosen path, or null when cancelled.</summary>
    public static string? PickSaveFile(string filter, string fileName)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = fileName };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    /// <summary>Opens a document (a guide page) with the shell's default application.</summary>
    public static void OpenDocument(string path)
        => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
