using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using radians.beamlab;
using radians.beamlab.app;

namespace radians.beamlab.dataset;

/// <summary>
/// Provenance of an emitted case: who produced it, when, in which profile and
/// at what depth, and the identity of every artefact -- the stamp a frozen
/// triple is checked by. A case is a triple: the notice (the SRS database),
/// the masks (the Masks database with its XML sources) and the expectation
/// records. Each file is listed with its SHA-256; a file whose hash differs
/// is not this emission's artefact, whatever its name says. Artefacts are
/// checked by identity; statistics by pairs (the records carry those).
/// </summary>
public static class Provenance
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Short id of the code that produced the values: FNV-1a over the module
    /// version ids of the three assemblies that decide them (core: the
    /// examination and the export writer; app: the envelope sampler; this
    /// tool: the constructions). Stable within a build, different across
    /// builds -- the same construction as the loop's mask cache key.
    /// </summary>
    public static string ProducerId()
    {
        var ids = new[]
        {
            typeof(IPfdMaskSampler).Assembly.ManifestModule.ModuleVersionId,
            typeof(ReachableEnvelopeSampler).Assembly.ManifestModule.ModuleVersionId,
            typeof(Provenance).Assembly.ManifestModule.ModuleVersionId,
        };
        ulong h = 1469598103934665603UL;
        foreach (var id in ids)
            foreach (byte x in id.ToByteArray()) { h ^= x; h *= 1099511628211UL; }
        return h.ToString("x16", CultureInfo.InvariantCulture)[..8];
    }

    /// <summary>SHA-256 of a file, lower-case hex: the identity a frozen artefact is checked by.</summary>
    public static string Sha256Hex(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    /// <summary>The provenance line stamped into every record.</summary>
    public static string Line(bool quick)
        => string.Create(CultureInfo.InvariantCulture,
            $"producer radians.beamlab.dataset (app {typeof(ReachableEnvelopeSampler).Assembly.GetName().Version?.ToString(3)}), build id {ProducerId()}, generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, profile {(quick ? "quick" : "full")}.");

    /// <summary>
    /// Writes expected/provenance.md for a case: the stamp and the triple's
    /// files by identity. Call it last, after every other file of the case
    /// exists; the stamp does not list itself.
    /// </summary>
    public static string WriteCaseStamp(string caseDir, string caseName, int ntcId, bool quick, string depthText)
    {
        var inv = CultureInfo.InvariantCulture;
        string expDir = Path.Combine(caseDir, "expected");
        Directory.CreateDirectory(expDir);
        string stampPath = Path.Combine(expDir, "provenance.md");
        string xmlDir = Path.Combine(caseDir, "xml");
        IEnumerable<string> Sorted(IEnumerable<string> files) => files.OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal);
        var groups = new (string Title, string What, IEnumerable<string> Files)[]
        {
            ("The notice", "the SNS v10 SRS database", Sorted(Directory.EnumerateFiles(caseDir, "* SRS.MDB"))),
            ("The masks", "the Masks database (BR container format) and the XML sources it was stored from",
                Sorted(Directory.EnumerateFiles(caseDir, "* Masks.MDB")).Concat(
                    Directory.Exists(xmlDir) ? Sorted(Directory.EnumerateFiles(xmlDir, "*.xml")) : Array.Empty<string>())),
            ("The expectation records", "what a consumer must reproduce, and the curves and tables behind it",
                Sorted(Directory.EnumerateFiles(expDir).Where(f => !Path.GetFileName(f).Equals("provenance.md", StringComparison.OrdinalIgnoreCase)))),
            ("The case notes", "the README", new[] { Path.Combine(caseDir, "README.md") }.Where(File.Exists)),
        };

        var sb = new StringBuilder();
        sb.AppendLine(string.Create(inv, $"# Provenance: {caseName} (ntc_id {ntcId})"));
        sb.AppendLine();
        sb.AppendLine("This case is a frozen, version-stamped triple: the notice, the masks and the expectation records, emitted together by one build of one producer. Every file is listed below with its SHA-256; a file whose hash differs is not this emission's artefact, whatever its name says. Artefacts are checked by identity; the statistics in the records carry their own convergence pairs.");
        sb.AppendLine();
        sb.AppendLine("- Emission: " + Line(quick));
        sb.AppendLine("- Depth: " + depthText);
        sb.AppendLine();
        foreach (var (title, what, files) in groups)
        {
            var list = files.ToList();
            if (list.Count == 0) continue;
            sb.AppendLine("## " + title);
            sb.AppendLine();
            sb.AppendLine(what + ".");
            sb.AppendLine();
            sb.AppendLine("| file | bytes | SHA-256 |");
            sb.AppendLine("|---|---|---|");
            foreach (string f in list)
                sb.AppendLine(string.Create(inv, $"| {Path.GetRelativePath(caseDir, f).Replace('\\', '/')} | {new FileInfo(f).Length} | {Sha256Hex(f)} |"));
            sb.AppendLine();
        }
        sb.AppendLine("To check: recompute the SHA-256 of each file (any standard tool) and compare; the README's text and this stamp's time are the only parts of the triple that are not themselves hashed.");
        File.WriteAllText(stampPath, sb.ToString(), Utf8NoBom);
        return stampPath;
    }
}
