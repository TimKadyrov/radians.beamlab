using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace radians.beamlab.app;

/// <summary>One launcher entry on the Home tab.</summary>
public sealed record HomeFunction(string Title, string Description, int TabIndex);

/// <summary>
/// The Home tab: the app's front door -- one card per function (opening its
/// tab), links to the local documentation, and the version. Pure state; the
/// view owns tab switching and shell-opening.
/// </summary>
public sealed class HomeViewModel
{
    public IReadOnlyList<HomeFunction> Functions { get; } = new[]
    {
        new HomeFunction("Composite gain map",
            "Compose a multibeam payload over the world map: per-beam S.1528 " +
            "patterns, hex or ring layouts, exclusion rings and country gating, " +
            "aggregate PFD adjustment. Click beams to toggle, probe the composite gain.", 1),
        new HomeFunction("PFD Mask Generator",
            "Derive the S.1503-4 downlink pfd mask from the composed payload -- " +
            "alpha/DeltaLongitude or azimuth/elevation form, reachable-envelope " +
            "sampling over pass headings (and a declared yaw sweep), XML/CSV export.", 2),
        new HomeFunction("Mask Viewer",
            "Open any S.1503-4 mask XML and read it the way an EPFD tool does: " +
            "latitude blocks, D5.1.5 interpolation, heatmap and profile cuts.", 3),
        new HomeFunction("Orbit Design",
            "Prototype the SNS v10 orbit parameters: repeating ground-track " +
            "candidates with exact altitudes and rpt_prd fields, the keep_rnge " +
            "bound, artificial-precession numbers, and the propagated one-cycle " +
            "track drawn to visible closure.", 4),
    };

    /// <summary>Local docs, when running inside the repo tree; null hides the link.</summary>
    public string? UserGuidePath { get; }
    public string? ParameterCardsPath { get; }

    public string VersionText { get; }

    public HomeViewModel() : this(AppContext.BaseDirectory) { }

    public HomeViewModel(string startDir)
    {
        string? docs = FindDocsDir(startDir);
        UserGuidePath = docs is null ? null : Path.Combine(docs, "user-guide.md");
        string? cards = docs is null ? null : Path.Combine(docs, "parameter-cards.html");
        ParameterCardsPath = cards is not null && File.Exists(cards) ? cards : null;

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText = v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>Walk up from the start directory to the repo docs folder.</summary>
    public static string? FindDocsDir(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "docs", "user-guide.md");
            if (File.Exists(candidate)) return Path.Combine(dir.FullName, "docs");
            dir = dir.Parent;
        }
        return null;
    }
}
