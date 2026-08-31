using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace radians.beamlab.app;

/// <summary>One launcher entry on the Home tab.</summary>
public sealed record HomeFunction(string Title, string Description, int TabIndex);

/// <summary>One tool card on the Home tab; Key selects the window to open.</summary>
public sealed record HomeTool(string Key, string Title, string Description);

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

    public IReadOnlyList<HomeTool> Tools { get; } = new[]
    {
        new HomeTool("profile", "Operation profile",
            "The real system's operating characteristics as one saved element -- " +
            "payload per direction, coverage, scheduling, activity -- feeding the " +
            "simulation runner, the R-set deriver and the compliance loop."),
        new HomeTool("compliance", "Compliance loop",
            "Sweep epfd(down) victims across a latitude grid, verdict each point " +
            "against the entered Article 22 limit with the examination's own " +
            "comparison, and walk the exclusion angle to the smallest compliant value."),
        new HomeTool("opparams", "Operating parameters (R set)",
            "Author the declared operating constraints directly, or derive them " +
            "by simulating the system and enveloping what it actually does; " +
            "round-trip the set and export the R-set XML the builder registers."),
        new HomeTool("simulation", "Simulation runner",
            "Run the epfd(down)/(is)/(up) simulation directly from a design " +
            "document over the scheduler-driven operation model and write the " +
            "three CDF CSVs in the examination's bins."),
        new HomeTool("builder", "SNS v10 builder",
            "Assemble complete SNS v10 datasets (SRS + Masks databases) from " +
            "orbit-design documents, mask XMLs with per-row link scope, declared " +
            "earth stations and operating-parameter sets."),
    };

    /// <summary>Local docs, when running inside the repo tree; null hides the link.</summary>
    public string? UserGuidePath { get; }
    public string? ParameterCardsPath { get; }
    public string? OrbitCasesPath { get; }
    public string? RepeatSolverPath { get; }

    public string VersionText { get; }

    public HomeViewModel() : this(AppContext.BaseDirectory) { }

    public HomeViewModel(string startDir)
    {
        string? docs = FindDocsDir(startDir);
        UserGuidePath = docs is null ? null : Path.Combine(docs, "user-guide.md");
        string? cards = docs is null ? null : Path.Combine(docs, "parameter-cards.html");
        ParameterCardsPath = cards is not null && File.Exists(cards) ? cards : null;
        string? cases = docs is null ? null : Path.Combine(docs, "orbit-design-cases.html");
        OrbitCasesPath = cases is not null && File.Exists(cases) ? cases : null;
        string? solver = docs is null ? null : Path.Combine(docs, "repeat-solver.html");
        RepeatSolverPath = solver is not null && File.Exists(solver) ? solver : null;

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
