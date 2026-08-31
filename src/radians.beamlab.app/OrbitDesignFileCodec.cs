using System;
using System.Globalization;
using System.Text.Json;
using radians.beamlab;
using Radians.Orbits.Core.Utilities;

namespace radians.beamlab.app;

/// <summary>
/// The intermediate orbit-design file (*.orbitdesign.json): every Orbit
/// Design input plus the SELECTED repeat candidate, so a consumer (the SNS
/// builder, a simulation) reproduces exactly the chosen design without
/// re-running the solver. SchemaVersion 2 added the selected-candidate
/// fields; version 3 added the Case-3 admin-supplied precession rate;
/// version 4 added DeclareAtTargetAltitude (the stored rpt_* fields hold
/// the DECLARED decomposition either way); version 5 added
/// HarmonizedRptSeconds (the constellation repeat period declared as
/// this shell's rpt_prd); version 6 added ShellName. Older files load
/// with the newer fields empty/false, reproducing their original
/// behaviour.
/// </summary>
public sealed record OrbitDesignData(
    int SchemaVersion, double TargetAltitudeKm, double InclinationDeg, double Eccentricity,
    int MaxOrbitsPerCycle, double SearchBandKm,
    int PlaneCount, int SatsPerPlane, int WalkerPhasingF, double Lan0Deg, double LanSpreadDeg,
    double InPlaneOffsetDeg, double ArgPerigeeDeg, string OpHeightText, int CaseChoice,
    double KeepRangeDeg, int NOrbits, string VictimBeamwidthText,
    double? SelectedAltitudeKm = null, int? SelectedOrbits = null, int? SelectedNodalDays = null,
    int? RptDays = null, int? RptHours = null, int? RptMinutes = null, int? RptSeconds = null,
    double? PrecessionDegPerSec = null, bool DeclareAtTargetAltitude = false,
    long? HarmonizedRptSeconds = null, string ShellName = "")
{
    /// <summary>One-line summary for list displays, at the declared altitude.</summary>
    public string Summary
    {
        get
        {
            double alt = CaseChoice == 1 && !DeclareAtTargetAltitude
                ? SelectedAltitudeKm ?? TargetAltitudeKm
                : TargetAltitudeKm;
            string s = FormattableString.Invariant(
                $"{PlaneCount}x{SatsPerPlane} @ {alt:F1} km / i {InclinationDeg:F1}, case {CaseChoice + 1}");
            if (SelectedOrbits is int k)
                s += FormattableString.Invariant($", repeat {k}/{SelectedNodalDays}");
            return ShellName.Trim().Length > 0 ? ShellName.Trim() + " — " + s : s;
        }
    }
}

/// <summary>
/// Schema-4 design document: the whole constellation as an ordered list of
/// single-shell designs. Version-2/3 files (one bare shell record) load as
/// a one-shell document.
/// </summary>
public sealed record OrbitDesignDocument(int SchemaVersion, IReadOnlyList<OrbitDesignData> Shells);

public static class OrbitDesignFileCodec
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Save(OrbitDesignData d) => JsonSerializer.Serialize(d, Options);

    public static OrbitDesignData Load(string json)
        => JsonSerializer.Deserialize<OrbitDesignData>(json)
           ?? throw new InvalidOperationException("empty design file");

    public static string SaveDocument(OrbitDesignDocument doc)
        => JsonSerializer.Serialize(doc, Options);

    /// <summary>
    /// Loads any design-file version: a schema-4 document as-is, an older
    /// single-shell file wrapped as a one-shell document.
    /// </summary>
    public static OrbitDesignDocument LoadDocument(string json)
    {
        var doc = JsonSerializer.Deserialize<OrbitDesignDocument>(json);
        if (doc?.Shells is { Count: > 0 }) return doc;
        return new OrbitDesignDocument(4, new[] { Load(json) });
    }

    /// <summary>
    /// The shell the design describes. Case 2 uses the stored selected
    /// candidate (exact altitude + repeat period); without one the target
    /// altitude is used and no repeat is declared.
    /// </summary>
    public static ConstellationShell ToShell(OrbitDesignData d)
    {
        // Cases 1 and 3 fly the target orbit as-is; a Case-2 design also
        // declares at the target altitude by default, adopting the solved
        // candidate's exact altitude only when opted out (the stored rpt_*
        // fields already hold the matching declared decomposition).
        double alt = d.CaseChoice == 1 && !d.DeclareAtTargetAltitude
            ? d.SelectedAltitudeKm ?? d.TargetAltitudeKm
            : d.TargetAltitudeKm;
        double? opHt = double.TryParse(d.OpHeightText, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double oh) ? oh : null;
        var shell = new ConstellationShell
        {
            AltitudeKm = alt, InclinationDeg = d.InclinationDeg, Eccentricity = d.Eccentricity,
            PlaneCount = Math.Max(1, d.PlaneCount), SatsPerPlane = Math.Max(1, d.SatsPerPlane),
            WalkerPhasingF = d.WalkerPhasingF, Lan0Deg = d.Lan0Deg, LanSpreadDeg = d.LanSpreadDeg,
            InPlaneOffsetDeg = d.InPlaneOffsetDeg, ArgumentOfPerigeeDeg = d.ArgPerigeeDeg,
            OperatingHeightKm = opHt,
        };
        return d.CaseChoice switch
        {
            1 => shell with
            {
                StationKeeping = true, WDeltaDeg = d.KeepRangeDeg,
                RepeatPeriod = d.RptDays is int dd
                    ? (dd, d.RptHours ?? 0, d.RptMinutes ?? 0, d.RptSeconds ?? 0)
                    : null,
            },
            2 => shell with
            {
                PrecessionSupplied = true,
                PrecessionRateDegPerSec = d.PrecessionDegPerSec
                    ?? OrbitDesign.J2NodalRateDegPerSec(
                        OrbitalConstants.EarthRadiusKm + alt, d.Eccentricity, d.InclinationDeg),
            },
            _ => shell with { NOrbits = Math.Max(1, d.NOrbits) },
        };
    }
}
