using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>One (ES/service latitude, value) row of a per-latitude profile array.</summary>
public sealed record ProfileLatRow(double LatDeg, double Value);

/// <summary>Which link direction a composition enforces.</summary>
public enum LinkDirection { Down, Up }

/// <summary>Space-to-Earth side: the space-station payload, beams and link discipline.</summary>
/// <param name="FootprintSource">
/// Where the downlink footprint toward a victim comes from in
/// simulations: "composition" (default) computes it live from the
/// shaped beams above; "mask" reads the declared PFD mask XML at
/// <paramref name="MaskXmlPath"/> the examination's way (S.1503-4
/// Sec. D5.1.4.1). The beam fields still shape the scheduler's
/// coverage geometry in both modes.
/// </param>
public sealed record DownlinkProfile(
    double FrequencyGhz = 19.7,
    double? GainPeakDbi = null, double? BeamCellRadiusKm = null,
    double? TaylorSlrDb = null, int? TaylorNbar = null, double? PatternFloorDbi = null,
    double? TxEirpDbw = null, string PowerMode = "", string Aggregation = "",
    int? ReuseClusterIndex = null, double RefBwKHz = 40.0,
    double? MinAngleAtSatDeg = null, double? MinAngleAtEsDeg = null,
    string FootprintSource = "composition", string MaskXmlPath = "");

/// <summary>Earth-to-space side: the transmitting earth stations and their link discipline.</summary>
public sealed record UplinkProfile(
    double FrequencyGhz = 29.5,
    double? EsPowerDbw = null, double? PowerControlRefElevDeg = null,
    double? EsDishM = null,
    double? MinAngleAtSatDeg = null, double? MinAngleAtEsDeg = null);

/// <summary>
/// The Operation profile (*.opprofile.json, schema 1): the real system's
/// operating characteristics as a first-class input element, the way the
/// design document is for orbits. Payload knobs are nullable -- null
/// keeps the scene's own default -- so the schema can grow without
/// breaking older files. The exclusion starts absent: it is the
/// compliance loop's output, not an input.
/// See docs/compliance-loop-plan.md.
/// </summary>
public sealed record OperationProfile(
    int SchemaVersion = 1, string Name = "profile",
    // the two link directions, cleanly split
    DownlinkProfile? Downlink = null, UplinkProfile? Uplink = null,
    // coverage / service (direction-agnostic: one served population)
    double MinElevDeg = 10.0,
    double ServiceLatMinDeg = 30.0, double ServiceLatMaxDeg = 60.0,
    double ServiceLonMinDeg = -20.0, double ServiceLonMaxDeg = 20.0,
    double CellKm = 450.0, double? CoverageRadiusKm = null,
    // operation / scheduling (the served population is shared; the link
    // discipline can differ per direction and lives in the two sides)
    string TrackingPolicy = "HighestElevation",
    double? MinHoldSec = null,
    int? NcoPerCell = null, int? MaxCoFreqSat = null,
    int DemandLinksPerCell = 1, double ActivityFactor = 1.0, double ActivityPeriodSec = 300.0,
    double OperationalFraction = 1.0, double IlluminationDutyCycle = 1.0,
    // exclusion (the loop's output; 0 / empty = none yet)
    double AlphaExclDeg = 0.0,
    IReadOnlyList<ProfileLatRow>? MinElevByLat = null,
    IReadOnlyList<ProfileLatRow>? NcoByLat = null,
    IReadOnlyList<ProfileLatRow>? AlphaByLat = null)
{
    /// <summary>The downlink side, defaulted when the file carries none.</summary>
    public DownlinkProfile Down => Downlink ?? new DownlinkProfile();

    /// <summary>The uplink side, defaulted when the file carries none.</summary>
    public UplinkProfile Up => Uplink ?? new UplinkProfile();

    public string Summary => FormattableString.Invariant(
        $"{Name}: dl {Down.FrequencyGhz:F1} / ul {Up.FrequencyGhz:F1} GHz, min elev {MinElevDeg:F1}, alpha {AlphaExclDeg:F1}, {TrackingPolicy}, lat {ServiceLatMinDeg:F0}..{ServiceLatMaxDeg:F0}");
}

public static class OperationProfileCodec
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Save(OperationProfile p) => JsonSerializer.Serialize(p, Options);

    public static OperationProfile Load(string json)
        => JsonSerializer.Deserialize<OperationProfile>(json)
           ?? throw new InvalidOperationException("empty operation profile");
}

/// <summary>
/// Turns a profile into the runtime stack. The enforced set IS the real
/// system's behaviour: the scheduler's gates carry the profile's
/// elevation, exclusion, Nco and angle rules; the scene carries the
/// payload; the geography carries demand and activity.
/// </summary>
public static class OperationComposer
{
    public sealed record Composition(OperatingParamsSet Enforced, PfdMaskViewModel Scene,
        ServiceGeography Geography, SelectionPolicy Policy,
        double IlluminationDutyCycle, double? CoverageRadiusKm,
        string DownlinkFootprintSource = "composition", string DownlinkMaskXmlPath = "")
    {
        /// <summary>True when epfd(down) statistics should read the declared mask.</summary>
        public bool UsesMaskFootprint => DownlinkFootprintSource == "mask";
    }

    public static Composition Compose(OperationProfile p, double sceneAltitudeKm,
        LinkDirection direction = LinkDirection.Down)
    {
        var dl = p.Down;
        var ul = p.Up;
        var enforced = new OperatingParamsSet
        {
            SatName = "TRUTH",
            LowFreqMhz = Math.Min(dl.FrequencyGhz, ul.FrequencyGhz) * 1000.0,
            HighFreqMhz = Math.Max(dl.FrequencyGhz, ul.FrequencyGhz) * 1000.0,
            ElevAngleHeaderDeg = p.MinElevDeg,
            EsLatMinDeg = p.ServiceLatMinDeg, EsLatMaxDeg = p.ServiceLatMaxDeg,
            MaxCoFreqHeader = p.NcoPerCell, MaxCoFreqSat = p.MaxCoFreqSat,
            // The link discipline of the direction being composed.
            MinAngleAtSatDeg = direction == LinkDirection.Down ? dl.MinAngleAtSatDeg : ul.MinAngleAtSatDeg,
            MinAngleAtEsDeg = direction == LinkDirection.Down ? dl.MinAngleAtEsDeg : ul.MinAngleAtEsDeg,
            // The hold time before a voluntary handover (the strategies'
            // dwell parameter; HoldUntilForced ignores it).
            MinDurationSecHeader = p.MinHoldSec is double h && h > 0.0 ? (int)Math.Round(h) : null,
        };
        foreach (var r in p.MinElevByLat ?? Array.Empty<ProfileLatRow>())
        {
            var me = new MinElevByLat { LatDeg = r.LatDeg };
            me.ByAz.Add((0.0, r.Value));
            me.ByAz.Add((360.0, r.Value));
            enforced.MinElev.Add(me);
        }
        foreach (var r in p.NcoByLat ?? Array.Empty<ProfileLatRow>())
            enforced.MaxCoFreqByLat.Add((r.LatDeg, (int)Math.Round(r.Value)));
        var alphaRows = (p.AlphaByLat ?? Array.Empty<ProfileLatRow>()).ToList();
        if (alphaRows.Count == 0 && p.AlphaExclDeg > 0.0)
        {
            alphaRows.Add(new ProfileLatRow(-90.0, p.AlphaExclDeg));
            alphaRows.Add(new ProfileLatRow(90.0, p.AlphaExclDeg));
        }
        if (alphaRows.Count > 0)
        {
            var ring = new MinExcludeByOrbit { OrbId = 0 };
            foreach (var r in alphaRows) ring.ByLat.Add((r.LatDeg, r.Value));
            enforced.MinExclude.Add(ring);
        }

        // The scene is the DOWNLINK payload; the scheduler it feeds serves
        // both directions.
        var scene = new PfdMaskViewModel
        {
            AltitudeKm = sceneAltitudeKm,
            FrequencyGHz = dl.FrequencyGhz,
            MinElevDeg = p.MinElevDeg,
            AlphaExclDeg = p.AlphaExclDeg,
            RefBwKHz = dl.RefBwKHz,
        };
        if (dl.GainPeakDbi is double g) scene.GmDbi = g;
        if (dl.BeamCellRadiusKm is double cr) scene.CellRadiusKm = cr;
        if (dl.TaylorSlrDb is double slr) scene.TaylorSlrDb = slr;
        if (dl.TaylorNbar is int nb) scene.TaylorNbar = nb;
        if (dl.PatternFloorDbi is double lf) scene.LfDbi = lf;
        if (dl.TxEirpDbw is double tx) scene.TxEirpDbw = tx;
        if (dl.PowerMode == "pfd") scene.IsConstantPfdMode = true;
        else if (dl.PowerMode == "eirp") scene.IsConstantEirpMode = true;
        if (dl.Aggregation == "cochannel") scene.IsCoChannelMode = true;
        else if (dl.Aggregation == "powersum") scene.IsPowerSumMode = true;
        if (dl.ReuseClusterIndex is int rc) scene.ReuseClusterIndex = rc;

        var cells = ServiceGeography.Grid(p.ServiceLatMinDeg, p.ServiceLatMaxDeg,
                p.ServiceLonMinDeg, p.ServiceLonMaxDeg, p.CellKm).Cells
            .Select(c => c with
            {
                DemandLinks = Math.Max(1, p.DemandLinksPerCell),
                ActivityFactor = p.ActivityFactor,
                ActivityPeriodSec = p.ActivityPeriodSec,
            })
            .ToList();
        var geo = new ServiceGeography(cells, p.CellKm);

        var policy = p.TrackingPolicy switch
        {
            "MaxGsoSeparation" => SelectionPolicy.MaxGsoSeparation,
            "HoldUntilForced" => SelectionPolicy.HoldUntilForced,
            _ => SelectionPolicy.HighestElevation,
        };
        return new Composition(enforced, scene, geo, policy,
            p.IlluminationDutyCycle, p.CoverageRadiusKm,
            dl.FootprintSource, dl.MaskXmlPath);
    }

    /// <summary>Applies the profile's shell-level operations to the designed shells.</summary>
    public static ConstellationShell[] ApplyToShells(OperationProfile p,
        IEnumerable<ConstellationShell> shells)
        => shells.Select(s => s with { OperationalFraction = p.OperationalFraction }).ToArray();

    /// <summary>
    /// Non-null when the profile carries per-latitude exclusion rows the
    /// composed scene cannot express yet: the scheduler enforces them but
    /// the scene ring (and every mask exported from it) bakes only the
    /// global angle, so a projection margin from such a profile is
    /// silently inflated by the un-inherited rows. The guard exists so
    /// that state cannot be entered unknowingly; it retires when the
    /// scene/export gain per-latitude alpha (tracked in
    /// docs/compliance-loop-plan.md).
    /// </summary>
    public static string? PerLatExclusionSceneGap(OperationProfile p)
        => (p.AlphaByLat?.Count ?? 0) == 0 ? null
           : FormattableString.Invariant(
               $"WARNING: {p.AlphaByLat!.Count} per-latitude exclusion row(s) gate the scheduler but the scene/mask bakes only the global alpha ({p.AlphaExclDeg:F1} deg) -- exported masks and projection margins from this profile are inflated until mask inheritance lands");
}
