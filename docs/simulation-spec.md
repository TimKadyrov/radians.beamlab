# Simulation: implementation specification

*What radians.beamlab has to become in order to produce S.1503-4 examination
input from a system description, and to measure how much the S.1503-4
abstraction discards. August 2026.*

Companion document: **S.1503-4 validation dataset: design brief** (radians
repo, `architecture/s1503-4-dataset-design-brief.md`; also on the project
Confluence, EP space). That brief states *what* the dataset must contain and
why. This document states *how beamlab builds it*.

## 1. Role

Two programs, two jobs:

| | beamlab (simulator) | radians (examination) |
|---|---|---|
| Role | **producer** of EPFD input data | **consumer** of EPFD input data |
| Input | full system description — beam layouts, per-beam power, pointing, traffic and scheduling, service geography — deliberately **richer than the S.1503-4 format** | the S.1503-4 input set only |
| Output | pfd / e.i.r.p. masks, operating-parameter sets, SRS content, plus its own interference statistics | examination result and compliance verdict |

The separation is the point. A simulator built inside the examination engine
could only model what the format already expresses, which is precisely the
thing under test. beamlab must be able to describe systems S.1503-4 cannot.

**Generating the masks and operating parameters is a projection** from that
richer description onto the format. The margin between beamlab's own
interference statistics and radians' examination of the projected data
measures what the projection discards. That margin is the scientific output;
the input set is the engineering output.

## 2. What exists today

Already in `radians.beamlab.core`:

- `SinglePatterns.Rec1528_1p4` — ITU-R S.1528-1 §1.4 Taylor circular
  illumination, plus the §1.2 envelope and §1.3 LEO/MEO/HEO forms
- `Beam` — boresight, pattern, weight, lattice indices, `GainDbi(test)`,
  `OffAxisDeg`, `AzimuthAroundBoresightDeg`
- `BeamComposer` — `CompositeGainDbi`, `CompositeEirpDbw(beams, test, powersDbw)`,
  `MaxCoChannelEirpDbw`, `HexReuseColor`, `ApplyExclusion`
- `GeoMath` — ECEF/geodetic, satellite NED basis, beam-to-ground ray-sphere
  intersection, elevation and horizon geometry, small-circle sampling

And in `radians.beamlab.app`: hex/ring layout, beam gating by served minimum
elevation and GSO exclusion, per-beam power modes, N-colour reuse aggregation,
α-ring exclusion, pfd mask XML export in both S.1503-4 coordinate systems, and
a mask viewer that reads back with §D5.1.5 bilinear interpolation.

That is the whole downlink pfd path for **one satellite at one instant**.

## 3. What is missing

1. **Time.** Everything above is evaluated at a fixed sub-satellite point.
   There is no propagation and no notion of a run.
2. **Constellation.** One satellite, not a filing's worth of planes.
3. **A traffic and scheduling model.** Nothing decides which satellite serves
   which location when — so `MAX_CO_FREQ`, `MIN_DURATION` and the uplink link
   assignment have no behaviour behind them.
4. **Uplink and inter-satellite mask generation.** Only the downlink pfd mask
   is produced.
5. **Operating-parameter (`R`) XML authoring.** Not written at all.
6. **SRS content.** No `orbit` / `phase` / `epfd_param` / `epfd_freq` /
   `mask_info` / `mask_lnk*` generation.
7. **Its own interference statistics.** Without these there is nothing to
   compare the examination against.

Two structural obstacles to note before any of that:

- **Mask export lives in the app project** (`radians.beamlab.app/MaskXmlExport.cs`),
  so it is unreachable from a headless run. It has to move to `core` or to a
  new library before anything can be generated in batch.
- **`Beam.Boresight` is get-only.** A steered beam is a beam whose boresight
  varies with time, so pointing has to become something evaluated per step
  rather than fixed at construction. See WP1.

## 4. Data model

### 4.1 The beam abstraction

A steerable beam is not a separate mechanism. It is a beam in the composite
whose boresight varies with time:

```
beam = (pattern, boresight(t), power(t), gate(t))
```

A fixed body-stabilised layout is the constant case. This keeps `BeamComposer`
unchanged — it still sums whatever beams it is handed — and confines the time
dependence to how the beam set for step *t* is produced.

Recommended shape: leave `Beam` immutable and add a `BeamPointing` abstraction
that yields the beam set at time *t*, rather than mutating `Beam.Boresight`.
Mutation would make the composer's inputs order-dependent and would break the
existing static views that hold a beam list.

### 4.2 The system description (the superset)

The input model beamlab owns, and which S.1503-4 cannot express:

| Group | Contents |
|---|---|
| Constellation | shells; per shell: altitude, inclination, plane count, satellites per plane, phasing, station-keeping or free drift |
| Payload, per satellite class | beam layout (hex/ring/explicit), per-beam pattern parameters, **total payload power budget**, per-beam maximum power, scan limits |
| Service geography | the regions or cells served; latitude bounds; any territory scoping |
| Operating constraints | exclusion zone, minimum elevation (by latitude and azimuth), maximum co-frequency satellites, minimum tracking duration |
| Traffic and scheduling | demand per cell over time, the selection policy, handover rules |
| Bands | frequency ranges, direction, reference bandwidth, which payload serves them |

The last two rows are what the format has no place for, and they are what make
the declared operating parameters *true* rather than asserted.

### 4.3 The power budget is not optional

The mask is a maximum over a set of configurations. Taken over "every beam at
full power in every direction" that maximum is valid and useless — no real
payload can illuminate every cell at once. **The total payload power budget is
what makes the envelope meaningful**, and it is the main reason a derived mask
is tighter than a naive worst case. It must be a first-class input, not a
per-beam afterthought.

## 5. What the mask envelopes over

A decision to take before WP4, because it changes what the derivation computes:

- **Reachable** — every configuration the payload can produce, subject to scan
  limits, the power budget and the declared operating constraints. This is what
  an operator must file, because a mask is a commitment rather than a
  description of typical behaviour. **Recommended.**
- **Occurring** — the configurations the scheduler actually produces under a
  given traffic model. Tighter, but only defensible if the traffic model is
  itself part of the commitment.

Either way the operating parameters and the mask are derived **together**, not
in sequence: the operating parameters define the feasible set (exclusion zone,
minimum elevation and Nco each remove configurations from it) and the mask is
the maximum over what remains. A tighter declared exclusion zone therefore
produces both a tighter mask and a stricter selection rule.

That coupling is itself measurable — deriving two input sets from one simulated
system, with different declaration strategies, quantifies in dB what a
declaration strategy is worth. Worth building the derivation so this is a
parameter rather than a rewrite.

## 6. Work packages

Ordered by dependency. WP0–WP4 are the first milestone; the rest follow.

### WP0 — Make the library headless *(prerequisite, small)*

Move `MaskXmlExport` and the mask-writing types from `radians.beamlab.app` to
`radians.beamlab.core` (or a new `radians.beamlab.masks`). No behaviour change;
the WPF app keeps calling the same code. Nothing else can be batch-generated
until this is done.

### WP1 — Time and constellation *(medium)*

- Reference radians' orbit propagator assembly rather than reimplementing it.
  It is the EPS Appendix 4 specified propagator, and sharing it makes beamlab's
  trajectories identical to the examination's **by construction** — otherwise
  every downstream difference is confounded with a propagation difference.
- Add the constellation model of §4.2 and a `SystemState(t)`: satellite
  positions, and for each satellite the beam set with pointing, power and gate
  resolved at *t*.
- Keep `BeamComposer` untouched; it consumes the resolved beam set.

**Do not reimplement propagation.** If the shared assembly is impractical,
raise it rather than writing a second propagator — two propagators is the one
outcome that makes the margin measurement meaningless.

### WP2 — Service geography and scheduler *(medium–large)*

- Cell/region definition over the served geography, with latitude bounds.
- A selection policy that, for each cell and time, chooses serving satellites
  subject to: minimum elevation by latitude and azimuth, GSO exclusion zone by
  latitude and orbital plane, maximum co-frequency satellites by latitude, and
  minimum tracking duration where declared.
- Handover with a declared minimum dwell, so `MIN_DURATION` has real behaviour
  behind it rather than being asserted.

The scheduling policy must be explicit enough that the declared operating
parameters are its true bounds. Otherwise the eventual comparison measures the
scheduler rather than the method.

### WP3 — Operating-parameter set derivation *(small–medium)*

Emit the `R` mask XML per EPS §6.7.2 from the constraints in §4.2: header
attributes plus the `min_exclude` (by latitude and `orb_id`), `max_co_freq`,
`min_duration` and `min_elev` (by latitude and azimuth) arrays.

Encoding rules that are easy to get wrong, from the design brief:

- omit `min_duration` entirely for the classic algorithm — do not write 0
- `min_angle_at_es` is not applicable wherever `min_duration` is non-zero
- the all-orbits marker for `min_exclude` is an explicit `c="0"`
- absent `max_co_freq_sat` means no cap
- EPS §6.7.2.2: where a quantity appears both as a header attribute and as a
  per-latitude array, **the array prevails** and the header applies outside the
  latitudes the array covers

The dataset needs sets exercising header-only, array-only, and both-with-
different-values, so make the writer capable of all three.

### WP4 — Downlink pfd mask derivation over time *(medium)*

Generalise the existing static composition to a maximum over the configuration
set of §5, at each mask cell, for each latitude block. Both coordinate systems
(`f_mask_type` `A` = α/Δlongitude, `Z` = azimuth/elevation) are already
supported by the exporter and both are needed by the coverage matrix.

### WP5 — Uplink ES e.i.r.p. masks *(medium)*

Both forms: `f_mask_type` `O` with XML `format="T"` (2-D `eirp[lat][θ]`, which
must be monotonically decreasing) and `f_mask_type` `D` with XML `format="A"`
(4-D `eirp[lat][az][el][ΔLongES]`). Plus the earth-station population — typical
(`ES_ID = -1`, density-distributed) and specific (named stations, which
switches `es_density`/`es_distance` off and requires `e_as_stn` content).

### WP6 — Inter-satellite satellite e.i.r.p. mask *(small)*

`SAT_eirp[lat][θ]`, `f_mask` = `S`.

### WP7 — SRS content generation *(medium)*

`orbit`, `phase`, `orbit_set`, `epfd_param`, `epfd_freq`, `sat_oper`,
`mask_info`, `mask_lnk1/2/3`, and `e_as_stn` where specific earth stations are
used. Structure per EPS §6; the NEXT101 and NEXT102 cases in
`epfd-reference/Cases/S.1503-4/` are worked examples of the target.

### WP8 — Interference statistics *(medium)*

beamlab's own epfd computation over the simulated system, for the margin
comparison. **Share radians' accumulator binning and limits comparison** so the
two CDFs are commensurable bin for bin rather than differing by rounding
conventions at the 0.1 dB boundaries.

## 7. Sampling depth

Compliance turns on the 10⁻³–10⁻⁴ % percentiles, and resolving those needs of
order 10⁷–10⁸ samples above the level of interest — which is why an examination
run is 10⁸–10¹⁰ steps. A simulation over a short representative period will
validate the body of the distribution and then run out of samples exactly where
the verdict is decided.

Three options, to be chosen deliberately rather than by default:

1. run the simulation on the same time comb and duration as the examination,
   accepting the cost;
2. compare only at the body percentiles, and justify the tail by the envelope
   argument — the mask bounds the composition by construction;
3. run long only at the worst-case geometry the examination identifies, rather
   than at every victim location.

## 8. Acceptance

The relation between the two tools is directional:

> **examination result ≥ simulated result, at every percentile.**

An examination result *below* the simulation is a defect — a non-conservative
mask, a mis-declared parameter, or a bug — not a good fit. A result far above
is valid but of little practical use.

The deliverable is a **margin decomposition**: how many dB come from the mask
envelope, from the worst-case geometry, and from the selection rules. Each is
isolated by defeating it in turn — mask replaced by live composition,
worst-case geometry replaced by the simulated victim location, selection rules
replaced by the simulated scheduler.

## 9. First milestone

Deliberately narrow, to get the pipeline end to end before it gets wide:

- WP0, WP1, WP3, WP4, with **fixed body-stabilised beams** (constant boresight)
- one downlink band, one shell
- output: a pfd mask, an operating-parameter set, and the SRS content for a
  single-scenario notice that radians can examine

With fixed beams the composition is exact and the derived mask is tight, so the
first margin measurement isolates worst-case geometry and selection rules from
the steering question. Steering comes second, as WP2 lands.

## 10. Decisions taken

All four questions this section used to hold are resolved; recorded here so
the spec reads as history rather than as pending work.

1. **Reachable** is the envelope basis (§5), bounded by the payload power
   budget; the *occurring* set remains available through the scheduler-gated
   pointing and is what the expectation CDFs use for the selection-rule side.
2. The propagator is **vendored byte-identical** into `core/orbits/`
   (namespaces preserved, provenance in its README); a drift-guard check
   compares every vendored file against the source tree, so "shared
   component" holds by verification rather than by reference.
3. beamlab writes the SNS v10 `.mdb` content **directly**: cloned worked
   donors via ACE OLE DB for the SRS, and the BR native mask store for the
   Masks database with a container-format fallback for the forms the native
   validator predates (`R` sets, 4-D format "A"); the fallback rows verify
   through the BR native extractor.
4. Sampling depth is **option 2** (body percentiles from a fixed 30 s comb,
   the tail justified by the envelope argument); no dual time step. The
   BL-* dataset ships its expectation CDFs on that basis.
