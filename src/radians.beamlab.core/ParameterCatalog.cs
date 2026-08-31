using System;
using System.Collections.Generic;
using System.Linq;

namespace radians.beamlab;

public enum ParameterGroup { Declared, Truth, Orbit }

/// <summary>One parameter's help text -- the app-facing twin of its card.</summary>
public sealed record ParameterInfo(ParameterGroup Group, string Name, string Unit,
    string Where, string Description, IReadOnlyList<string> Relations)
{
    /// <summary>Tooltip form: description plus the relations as bullet lines.</summary>
    public string ToolTipText => Description + (Relations.Count == 0 ? "" :
        "\n\n" + string.Join("\n", Relations.Select(r => "- " + r)));
}

/// <summary>
/// The parameter help catalog: the same per-parameter text as the card deck
/// (docs/parameter-cards.html), ported mechanically and kept in lock-step by
/// the check harness -- edit the page and this file together. The app reads
/// tooltips from here so the UI and the documentation cannot drift apart.
/// </summary>
public static class ParameterCatalog
{
    public static ParameterInfo? Find(string name)
        => All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static readonly IReadOnlyList<ParameterInfo> All = new ParameterInfo[]
    {
        new(ParameterGroup.Declared, "FREQ_MIN / FREQ_MAX", "MHz",
            "OperatingParamsSet.LowFreqMhz/HighFreqMhz · epfd_freq, mask_info",
            "The band identity of the set. A band may not carry two sets, and every examined band must have one — the rule that decides how sets 21–25 partition the BL family.",
            new[]
            {
                "scopes which masks and which scenario frequencies the set governs",
                "one-set-per-band forces BL-ALL to drop the standalone I1 set for its D2 overlap",
            }),
        new(ParameterGroup.Declared, "MIN_EXCLUDE", "deg α · [lat][orb_id]",
            "MinExcludeByOrbit · min_exclude/exclusion_zone_angle",
            "The GSO-arc exclusion half-angle: a satellite whose cell-centre α sits inside it may not serve. Varies by latitude and per orbital plane (a per-orb_id row overrides the all-orbits row, and when it varies every plane needs a value).",
            new[]
            {
                "SelectionPolicy.MaxGsoSeparation maximises this same α metric",
                "a tighter zone tightens the pfd mask and the selection rule — margin twice over",
                "the scene’s advanced exclusion rings are its power-taper generalisation",
            }),
        new(ParameterGroup.Declared, "MIN_ELEV", "deg · [lat][az]",
            "MinElevByLat · min_elev/elev_angle · header elev_angle",
            "Minimum serving elevation, azimuth-dependent (e.g. raised toward the arc). Candidates below it are infeasible; azimuth rows interpolate linearly, latitude picks the nearest block.",
            new[]
            {
                "reference elevation for PowerControlRefElevDeg — the ceiling’s slant range",
                "sets the FOV edge the mask latitude cap and beam lattice must reach",
                "array prevails over the header inside its latitude span",
            }),
        new(ParameterGroup.Declared, "MIN_DURATION", "s · [lat]",
            "MinDurationByLat / header · min_duration",
            "Minimum tracking dwell once a link is made: voluntary handover to a better satellite waits out the dwell; only infeasibility forces one. Non-zero also gates admission — a new link is only made toward a satellite that stays above the elevation floor and outside the exclusion for the whole duration — and selects the track-duration examination algorithm (BL-D1).",
            new[]
            {
                "mutually exclusive with MIN_ANGLE_AT_ES — a band declares one regime",
                "an inactive ActivityFactor window releases the link without a handover; dwell restarts on return",
            }),
        new(ParameterGroup.Declared, "MAX_CO_FREQ", "count · [lat]",
            "MaxCoFreqByLat / header · max_co_freq",
            "Nco: how many co-frequency satellites may serve one location at once. The scheduler grants min(DemandLinks, Nco) slots per cell, each on a distinct satellite.",
            new[]
            {
                "DemandLinks — whichever is smaller is the slot count",
                "Nco ≥ 2 is what lets MIN_ANGLE_AT_ES bite at all",
            }),
        new(ParameterGroup.Declared, "MAX_CO_FREQ_SAT", "count · header",
            "MaxCoFreqSat · max_co_freq_sat",
            "Per-satellite cap on simultaneous co-frequency earth stations. Enforced as an assignment-time budget, so a capped satellite’s contested cell reassigns to its next-best satellite rather than transmitting unseen — drops would under-count the epfd↑ truth.",
            new[]
            {
                "a continuing link squeezed out by an earlier cell’s claim breaks as a forced handover",
                "uplink examination Steps 29–30 apply the same cap link-by-link",
            }),
        new(ParameterGroup.Declared, "MIN_ANGLE_AT_ES", "deg · header",
            "MinAngleAtEsDeg · min_angle_at_es",
            "Minimum separation, seen from one cell, between the satellites co-serving it: slot k only takes a satellite at least this far from every satellite already serving the cell.",
            new[]
            {
                "MIN_DURATION — the classic-algorithm half of the pair (BL-D2)",
                "inert until MAX_CO_FREQ and DemandLinks allow a second slot",
            }),
        new(ParameterGroup.Declared, "MIN_ANGLE_AT_SAT", "deg · header",
            "MinAngleAtSatDeg · min_angle_at_sat",
            "The mirror gate at the satellite: its co-frequency cells must be separated by at least this angle as seen from it. A violating candidate falls to the cell’s next satellite.",
            new[]
            {
                "with MAX_CO_FREQ_SAT, the pair of uplink gates the scheduler enforces by reassignment",
            }),
        new(ParameterGroup.Declared, "ES_DENSITY · ES_DISTANCE", "/km² · km",
            "EsDensityPerKm2 / EsDistanceKm · es_density, es_distance",
            "The typical-ES deployment declaration: the examination plants representative stations every ES_DISTANCE inside the victim beam with NUM_ES = dist²·density aggregation (§D5.2.5). The truth side transmits from actually scheduled cells instead — that asymmetry is measured margin.",
            new[]
            {
                "switched off entirely by specific earth stations (e_as_stn, mask ES_ID) — BL-U2",
                "BL-U1 declares both active; its up-CDF is the scheduled-cells truth",
            }),
        new(ParameterGroup.Declared, "ES_LAT_MIN / ES_LAT_MAX", "deg",
            "EsLatMinDeg / EsLatMaxDeg · es_lat_min, es_lat_max",
            "The declared service latitude range. Cells outside it are never served in the band — their demand counts as unserved rather than being silently trusted to the caller’s geography.",
            new[]
            {
                "bounded below the shell inclination, it trims the geometry the masks must cover",
            }),
        new(ParameterGroup.Declared, "header ↔ arrays", "precedence rule",
            "ElevAngleHeaderDeg, MaxCoFreqHeader, MinDurationSecHeader vs the [lat] arrays",
            "Several quantities exist twice: as an XML header scalar and as a per-latitude array. The array prevails inside the latitudes it covers; the header applies outside. The BL sets carry all three shapes (arrays-only, header-only, both-with-different-values) so a consumer must implement the rule, not infer it.",
            new[]
            {
                "every DeclaredConstraints accessor resolves this precedence in one place",
                "set 21 arrays-only · set 22 both-different · set 23 header-only",
            }),
        new(ParameterGroup.Truth, "DemandLinks", "count · per cell",
            "ServiceCell.DemandLinks (default 1)",
            "Simultaneous co-frequency links a cell requests — the demand side of the slot count. Slots beyond feasible capacity surface as unserved demand, i.e. blocking is an output.",
            new[]
            {
                "MAX_CO_FREQ caps it into the slot count",
                "with ActivityFactor, offered intensity ≈ DemandLinks × activity Erlang",
            }),
        new(ParameterGroup.Truth, "ActivityFactor", "0–1 · + ActivityPeriodSec",
            "ServiceCell.ActivityFactor / ActivityPeriodSec (1.0 / 300 s)",
            "On/off traffic per slot: in each holding window a deterministic hash of (cell, slot, window) decides whether demand exists. Inactive windows release the link with no handover and no unserved count — no traffic, no transmission, in both link directions at once.",
            new[]
            {
                "releases restart MIN_DURATION dwell without counting handovers",
                "hash-deterministic: same inputs, same CDFs — no RNG state anywhere",
            }),
        new(ParameterGroup.Truth, "PowerDbw", "dBW / ref. BW",
            "EpfdUpEsModel.PowerDbw",
            "The ES transmit ceiling into its antenna — the same base the declared E mask envelopes (mask = PowerDbw + G(θ) monotone hull), so simulated eirp ≤ mask by construction.",
            new[]
            {
                "the E mask’s base level; gateway and typical classes differ (15 / 12 dBW)",
                "reduced per link by PowerControlRefElevDeg",
            }),
        new(ParameterGroup.Truth, "PowerControlRefElevDeg", "deg · nullable",
            "EpfdUpEsModel.PowerControlRefElevDeg",
            "Range-based closed-loop power control (S.1325 “power control on range”): the ceiling corresponds to the slant range at this elevation, and each link transmits 20 log₁₀(d ref / d link) below it — constant flux at the serving satellite. Worth ≈2 dB in the BL-U1 truth CDF.",
            new[]
            {
                "referenced to the band’s declared MIN_ELEV in the dataset",
                "null keeps the ceiling — the pre-control behaviour, bit for bit",
            }),
        new(ParameterGroup.Truth, "IlluminationDutyCycle", "(0,1]",
            "ScenePointing(…, illuminationDutyCycle)",
            "Beam-hopping time average for frames much shorter than the 30 s step: every resolved beam power carries 10 log₁₀(duty). The declared masks stay peak-PSD envelopes — only the simulated statistics average (see the duty row in the Activity timeline).",
            new[]
            {
                "emission side only — the scheduler’s footprint layout is duty-independent",
                "never enters the mask samplers: peak vs average is the point",
            }),
        new(ParameterGroup.Truth, "SelectionPolicy", "enum",
            "Scheduler(…, policy) · HighestElevation | MaxGsoSeparation | HoldUntilForced",
            "Which feasible satellite serves: the highest-elevation default, the one farthest from the GSO arc, or hold-until-forced — no voluntary handover while the link stays feasible. All obey every declared bound; the differences between their CDFs price the strategies.",
            new[]
            {
                "MaxGsoSeparation maximises the same α that MIN_EXCLUDE bounds",
                "drives the candidate sort and the voluntary-handover comparison",
            }),
        new(ParameterGroup.Truth, "OperationalFraction", "0–1 · per shell",
            "ConstellationShell.OperationalFraction",
            "The transmitting cohort: spares and orbit-raising satellites fly with real positions but radiate nothing and never serve, interleaved by a Bresenham spread. The SRS always declares the full shell, so truth ≤ declaration by construction.",
            new[]
            {
                "declared N_sat stays the envelope; the fraction is pure measured margin",
            }),
        new(ParameterGroup.Truth, "YawSweepDeg", "deg[] · default {0}",
            "MaskXmlExportOptions.YawSweepDeg → ReachableEnvelopeSampler",
            "Body-yaw offsets swept on top of each pass heading when the pfd envelope is sampled. A yaw-steering payload must sweep its reachable yaw range here or the derived mask is not an envelope — the one parameter that guards mask correctness rather than tightness.",
            new[]
            {
                "the S mask needs no sweep: body yaw is a rigid rotation about nadir and its azimuth envelope is invariant",
                "sweep step no coarser than the output bin, or peaks slip between cells",
            }),
        new(ParameterGroup.Truth, "CellPitchKm / coverageRadiusKm", "km",
            "ServiceGeography.CellPitchKm · Scheduler ctor override",
            "The service-grid pitch, doubling as the default radius within which a resolved beam footprint must land to cover a cell. The default hex layout has no central beam — nearest boresights sit 433 km from the sub-satellite point at 1200 km — a lattice fact that decides feasibility.",
            new[]
            {
                "too tight a radius silently unserves covered-looking cells (three harness checks learned this)",
                "interacts with MIN_ELEV: both must admit the geometry before a candidate exists",
            }),
        new(ParameterGroup.Truth, "GainPeakDbi", "dBi · per beam",
            "PfdMaskViewModel.GmDbi · profile GainPeakDbi",
            "Per-beam peak gain Gm of the S.1528-1 §1.4 pattern — the top of every beam's gain curve and the bridge from transmit power to e.i.r.p.: per beam at its own boresight, e.i.r.p. density = TxEirpDbw + Gm (the composite adds neighbouring side lobes on top). Empty in the profile keeps the scene default.",
            new[]
            {
                "with TxEirpDbw it sets the boresight density the payload envelope study sweeps",
                "beam width comes from the layout, not Gm — cell sizing lives in BeamCellRadiusKm",
            }),
        new(ParameterGroup.Truth, "BeamCellRadiusKm", "km · per beam",
            "PfdMaskViewModel.CellRadiusKm · profile BeamCellRadiusKm",
            "Ground-cell radius one beam serves — sets the beam lattice density and each beam's width in the auto layout: smaller cells mean more, narrower beams over the same service area.",
            new[]
            {
                "the composite the masks envelope is the power sum of exactly this lattice",
                "coverage feasibility is separate — CellPitchKm / coverageRadiusKm decide which cells a footprint reaches",
            }),
        new(ParameterGroup.Truth, "TaylorSlrDb · TaylorNbar", "dB · count",
            "PfdMaskViewModel.TaylorSlrDb / TaylorNbar · profile TaylorSlrDb / TaylorNbar",
            "The S.1528-1 §1.4 Taylor illumination knobs: side-lobe ratio and the number of shaped secondary lobes; Annex 2's reference values, 20 dB and 4, are the scene defaults. Off-axis epfd toward a victim rides on exactly these side lobes.",
            new[]
            {
                "PatternFloorDbi caps the far-out lobes the Taylor shape decays into",
                "the kernel is pinned by checks at the Bessel-zero abscissae (|F| ≤ 1)",
            }),
        new(ParameterGroup.Truth, "PatternFloorDbi", "dBi",
            "PfdMaskViewModel.LfDbi · profile PatternFloorDbi",
            "The far-out side-lobe / null floor LF: the pattern never falls below it, so far off boresight a victim sees the floor, not the Taylor shape. It dominates the mask's far bins and the quiet end of the epfd CDF; the scene default is 0 dBi.",
            new[]
            {
                "raising it lifts every far-off-axis epfd sample dB for dB",
            }),
        new(ParameterGroup.Truth, "TxEirpDbw", "dBW / ref. BW · per beam",
            "PfdMaskViewModel.TxEirpDbw · profile TxEirpDbw",
            "Per-beam transmit power density into the pattern (dBW in the reference bandwidth): constant-e.i.r.p. mode feeds exactly this to every beam and the composite adds the pattern gain on top. Every epfd statistic moves dB for dB with it — the payload envelope study's compliance frontier is linear in this one knob.",
            new[]
            {
                "per-beam boresight e.i.r.p. density = TxEirpDbw + GainPeakDbi",
                "PowerMode adds per-beam slant compensation in constant-boresight-PFD mode",
            }),
        new(ParameterGroup.Truth, "PowerMode", "enum",
            "PfdMaskViewModel.PowerMode · constant e.i.r.p. | constant boresight PFD",
            "Downlink power control: constant e.i.r.p. (the default) drives every beam at TxEirpDbw; constant boresight PFD adds 20 log₁₀(slant/altitude) per beam so every boresight lands the same flux on the ground despite spreading — the compensation a real payload flies.",
            new[]
            {
                "check C4 pins the compensation: boresight PFD flat across the layout",
                "the derived masks bake whichever mode the profile declares",
            }),
        new(ParameterGroup.Truth, "Aggregation · ReuseClusterIndex", "enum · index",
            "PfdMaskViewModel.Aggregation / ReuseClusterIndex · power sum | co-channel worst colour",
            "How beams combine toward one direction: the all-co-frequency power sum, or the frequency-reuse worst colour — only one cluster's co-channel beams sum, the reuse index picking the colouring. The ordering is invariant: max single ≤ co-channel ≤ power sum.",
            new[]
            {
                "checks pin the K-colour adjacency and the ordering (C2, C3)",
                "a smaller co-channel population is the aggregation-side face of frequency reuse",
            }),
        new(ParameterGroup.Truth, "RefBwKHz", "kHz",
            "PfdMaskViewModel.RefBwKHz · mask refbw_khz",
            "Reference bandwidth of every density in the chain — the masks, the limits and the CDFs all quote per-reference-bandwidth quantities, and Article 22 epfd(down) uses 40 kHz. Declared once, carried everywhere.",
            new[]
            {
                "the BR limits rows carry their own refbw — the compliance window shows both",
            }),
        new(ParameterGroup.Truth, "FootprintSource", "enum",
            "DownlinkProfile.FootprintSource · composition | mask (+ MaskXmlPath)",
            "Where the downlink footprint toward a victim comes from in simulations: beam composition computes it live from the shaped beams (the truth); PFD mask reads a declared mask XML the examination's way — §D5.1.4.1 selection over §D5.1.5 reads. Run both on one profile and the CDF difference is the projection margin.",
            new[]
            {
                "the beam fields still shape the scheduler's coverage geometry in both modes",
                "no epfd(is) byproduct under the mask source — that needs the e.i.r.p. masks",
            }),
        new(ParameterGroup.Truth, "EsDishM", "m",
            "UplinkProfile.EsDishM · S.1428 diameter",
            "S.1428 antenna diameter of the transmitting earth stations on the uplink. The downlink victim's dish is not declared here — the examination pairs each Article 22 limit row with its own reference diameter, so the victim antenna comes from the limit table.",
            new[]
            {
                "with PowerDbw and PowerControlRefElevDeg it is the whole uplink transmit chain",
            }),
        new(ParameterGroup.Truth, "Service area", "deg lat × lon",
            "OperationProfile.ServiceLat/LonMin..Max · ServiceGeography.Grid",
            "The served-cell rectangle the geography is gridded over at the cell pitch — the truth-side population of transmitting and receiving cells. Distinct from the declared ES_LAT range: the composer copies the latitude bounds into the declaration; the longitude bounds exist only on the truth side (the examination plants typical ES by density instead).",
            new[]
            {
                "cells outside the declared ES_LAT range are never served — declaration binds truth",
                "the deriver's measured es_lat envelope comes from exactly these cells",
            }),
        new(ParameterGroup.Orbit, "StationKeeping · WDeltaDeg · RepeatPeriod", "Case 2",
            "f_stn_keep='Y', keep_rnge, rpt_prd_* · shell A",
            "Station-kept repeating ground track: the longitude tolerance W_delta sweeps the track across its deadband, and the declared repeat period tells the examination the comb it may fold over.",
            new[]
            {
                "excludes artificial precession — NOrbits is ignored when kept",
                "keep_rnge is a float column: compare at 1e-4, not 1e-6",
            }),
        new(ParameterGroup.Orbit, "NOrbits", "count · Case 1",
            "ConstellationShell.NOrbits → ArtificialPrecession · shell B",
            "Free drift: the examination adds artificial precession so NOrbits nodal passes tile the equator (§D6.3.2 Steps 8–11). Transcribed formula-identically — including the documented sign quirk where the measured spacing lands at 2·S_pass − S_grid.",
            new[]
            {
                "zero disables the mechanism (used when a declared rate exists)",
                "identity with the examination outranks track-repeat elegance — see the upstream note",
            }),
        new(ParameterGroup.Orbit, "PrecessionSupplied · PrecessionRateDegPerSec", "Case 3",
            "f_precess='Y', precession · shell C (J2 rate)",
            "Administration-supplied nodal precession: the declared rate is used directly instead of the derived artificial one. The BL value is the standard J2 regression for the elliptical shell (−1.81×10⁻⁵ deg/s).",
            new[]
            {
                "supersedes NOrbits-derived precession on that shell",
            }),
        new(ParameterGroup.Orbit, "Eccentricity · ArgumentOfPerigee · OperatingHeightKm", "elliptical",
            "apog/perig_km, perig_arg, op_ht_km · shell C",
            "The elliptical declarations. The minimum operating height (H_MIN) is an emission gate: below it the satellite flies dark — and it sets the worst-case range the pfd envelope is sampled at (Vm uses op-height, not mean altitude).",
            new[]
            {
                "activates the examination’s elliptical worst-case-geometry path and per-step height check",
                "phase_ang declares ω + ν; the constellation applies the inverse, so declared and propagated agree",
            }),
    };
}
