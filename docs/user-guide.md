# radians.beamlab — User Guide

radians.beamlab helps you answer two practical questions about a multi-beam
non-GSO satellite:

1. **"What does my beam layout radiate onto the Earth?"** — the
   **Composite gain map** tab: lay out beams, switch them on and off, and see
   the combined antenna gain painted on a world map.
2. **"What PFD mask does that produce, and can I file it?"** — the
   **PFD Mask Generator** tab: compute the downlink power-flux-density mask
   in the ITU coordinate systems, experiment with power control, frequency
   reuse and GSO-arc protection, and export the result as S.1503-4 mask XML
   or a CSV table.
3. **"What's inside this mask file?"** — the **Mask Viewer** tab: open any
   S.1503-4 mask XML (yours or a third party's) and browse its latitude
   blocks with the same heatmap and profile plots.

The tabs are independent — each keeps its own satellite, antenna and
display settings, so you can experiment in one without disturbing the other.

---

## Getting started

```
dotnet build radians.beamlab.slnx
dotnet run --project src/radians.beamlab.app
```

For real coastlines, put a Natural Earth GeoJSON named `countries.json` in
the working directory, next to the executable, or in the project root. The
status bar tells you which source was loaded; without it a coarse built-in
outline is used.

---

## Home

The app opens on a launcher page in two sections. **EPFD pipeline —
truth to declarations** holds the producer loop in flow order: Orbit
Design, Operation profile, Simulation runner, Compliance loop,
Operating parameters (R set), SNS v10 builder — submit the truth,
simulate it, derive the declarations that demonstrate compliance,
assemble the dataset. **Other tools** below holds the composition and
mask utilities: the Composite gain map, the PFD Mask Generator and the
Mask Viewer. Each card's Open button activates its tab or window. Below
them: links to this guide and to the parameter cards
(`docs/parameter-cards.html`, per-parameter reference with relations;
its **pipeline diagram** shows the whole tool at a glance — what a run
consumes, what happens inside the simulation, what comes out, and the
producer loop that turns outputs back into declared inputs), and the
version. Switching tabs never resets a function's state — the
numbered tabs below describe the functions in their card order.

## Tab 1 — Composite gain map

### Your first map in five steps

1. Set **Altitude** and the **sub-satellite point** — the amber circle on the
   map is everything the satellite can see.
2. Pick a **beam pattern**. The default (§1.4 Taylor circular) is a realistic
   single-beam shape; set its peak gain **Gm** and beamwidth **θb** (or click
   *Fill θb from Gm* to derive one from the other).
3. Tick **Auto hex tessellation** to fill the coverage with a honeycomb of
   beams, and set **Min user-elevation served** — beams stop where a ground
   user would see the satellite lower than this (the limit applies to beam
   *centres*; outer footprints spill past it).

   With a circular pattern the honeycomb lives in the 3GPP UV plane
   (uniform in sin θ), so on the ground it is dense at nadir and sparse at
   the edge, and fixed-width cones leave radial coverage gaps in the outer
   rings. Tick **Array-steered UV beams** (§1.4 circular only) to model each
   beam as a planar-array beam instead: its radial width broadens by
   1/cos(off-nadir) — as a real steered array does, because the projected
   aperture shrinks — and the lattice then tiles with a uniform crossover
   all the way out.
4. Tick **Render gain heatmap** — every visible ground pixel is coloured by
   the composite gain.
5. **Click any beam marker** to switch it off (it turns red and drops out of
   the sum). Click empty map to probe the exact gain/PFD at that spot; the
   answer appears in the status bar.

### Map gestures

| Gesture | Action |
|---|---|
| Left-click a beam marker | toggle that beam on/off |
| Left-click elsewhere | probe gain / PFD at that point |
| Left-drag | pan |
| Right-drag | move the satellite (live) |
| Mouse wheel | zoom around the cursor |

### The controls, in brief

- **Beam pattern** — six models from ITU-R S.1528. Use the Taylor §1.4
  (circular or elliptical) when you want realistic side lobes; the §1.2 /
  §1.3 envelopes when you want the standard's envelope shapes. The *Show
  pattern…* button plots the current single-beam pattern so you can sanity-
  check it before building a constellation of it.
- **Crossover level** — how far apart neighbouring beams sit: −3 dB is the
  engineering standard (beams meet at their half-power edge); pull it tighter
  for more overlap.
- **Region exclusion** — switch off every beam whose footprint lands in a
  chosen country or lat/lon box, in one click.
- **PFD adjustment** — after excluding a country you may still leak side-lobe
  power into it. This tool reduces the gain of the neighbouring beams (or
  switches them off if it can't reduce enough) until the aggregate PFD over
  that country stays under the selected limit. Adjusted beams turn amber.
- **Heatmap / probe mode** — *power sum* shows the aggregate of all beams;
  *single-beam max* shows just the strongest beam at each point.

---

## Tab 2 — PFD Mask Generator

This tab answers: *for the beam set I've configured, what PFD arrives at
every point the satellite can see — expressed in the mask coordinates the
ITU regime uses?* It always uses the elliptical Taylor pattern on a hex
lattice (the realistic case for an NTN payload).

### Your first mask in five steps

1. Set **altitude**, **latitude/longitude**, **frequency**, **Gm** and
   **cell radius** — the top map shows the resulting footprints.
2. Set the **beam gating**: *minimum user elevation* (how low on the horizon
   you still serve users) and the *GSO exclusion α_excl* (beams pointing too
   close to the geostationary arc are switched off — that's the red band you
   will see in the plots).
3. Pick a **mask type** (see below), leave power and aggregation at their
   defaults for now.
4. Look at the bottom two plots: the **heatmap** is the whole mask at a
   glance; the **profile** is a single cut through it, chosen with the
   slider.
5. Click **Generate mask XML…** to export the mask over a full latitude
   sweep (see *Exporting*, below).

### Choosing a mask type

- **Azimuth / Elevation** — the mask as seen *from the satellite*: where is
  it pointing energy? X is azimuth (east–west from nadir), Y is elevation
  (north–south). The bright oval is the visible Earth; the red band through
  it is the GSO exclusion.
- **α / ΔLongitude** — the mask organised around *how close to the GSO arc*
  each ground point looks from its own sky. Y is α — 0° means an earth
  station there sees your satellite exactly in line with the GSO arc
  (maximum interference risk); large |α| means far from the arc. X is the
  longitude offset to the nearest GSO point. This is the classic filing
  coordinate system: each horizontal line of the plot is literally one row
  of the mask table.

Both sample the **whole visible disc**, including ground below your minimum
served elevation — beams don't point there, but their side lobes still land
there, and the mask has to show it.

### Power options

- **Constant power per beam** — every beam transmits the same power. Distant
  (low-elevation) cells then receive less PFD because the signal travels
  farther.
- **Constant boresight PFD** — the payload boosts outer beams to compensate
  for the longer path, so every cell's centre receives the same PFD (typical
  downlink power control). The power box then means "power of a nadir beam";
  edge beams get up to a few dB more automatically.

The colour ramp auto-scales to the data, so after switching modes read the
numbers off the legend rather than comparing colours between screenshots.

### Aggregation — do your beams share spectrum?

- **Power sum of all beams** — assumes every beam transmits in the same
  channel. It's the worst case and the conservative choice for a filing.
- **Co-channel sum, N-colour reuse** — assumes a frequency plan: the
  honeycomb is coloured with N = 3, 4 or 7 colours so neighbouring beams
  never share a channel, and only same-colour beams add up. Pick this to see
  the realistic per-channel PFD. In this mode the top map paints each beam
  with its colour so you can see the plan.

  *Where N comes from:* the PFD mask is an operator-declared envelope of the
  system's actual co-frequency operation (Rec. ITU-R S.1503-4, §C2.3.1 /
  §C2.4.1), so the cluster size is **your system design choice, not a
  regulatory value** — set it to match the payload's real channelisation, or
  use the power sum as the N = 1 worst case. N = 3/4/7 are the classical
  hexagonal reuse cluster sizes — the three- and seven-frequency beam
  lattices of Maral, Bousquet & Sun, §9.8.7.3 (Fig. 9.40); reuse-1 vs
  reuse-3 are exactly the two options studied for NTN in 3GPP TR 38.821.

  *A note on names:* N is the **cluster size** (how many colours the plan
  uses). Maral's "frequency reuse factor" is a different quantity — the
  number of times the band is reused across the coverage, roughly beams ÷
  colours (his example: 4.3 for 13 beams on a 3-colour lattice) — while
  3GPP calls the cluster size itself the "frequency reuse factor" (FRF).
  This tool always means the cluster size.

### Advanced exclusion — α rings

The basic GSO protection is a single rule: *beams pointing within α_excl of
the arc are off*. If you need something graded, tick **Advanced exclusion
(α rings)** and click **Edit α rings…**:

- Each row is a ring around the GSO arc, given by its **outer α edge**.
  Rings stack outward from α = 0.
- Tick **Off** to switch beams in that ring off entirely — the attenuation
  field greys out, it isn't needed.
- Untick Off and enter **Atten (dB)** to keep those beams on but quieter.
- *Add ring* appends a ring outside the current outermost; *Remove selected*
  deletes the highlighted row.

Example: `0–5° Off, 5–10° −10 dB, 10–15° −3 dB` gives a hard core with a
graded shoulder. Everything updates live as you edit — the heatmap shows off
rings in red and attenuated rings in orange.

### Reading the plots

**Heatmap** (bottom-left)
- Colour = PFD, red (lowest) → green (highest); the legend bar gives the
  actual dB(W/m²) values. The range auto-fits the data.
- Blank/dark pixels = no line of sight (beyond the horizon) or, in α/ΔL
  mode, combinations no ground point produces.
- The dashed white line is the **profile cursor** — drag the slider above
  the profile plot to move it.
- Red / orange tinting = the exclusion rings (toggle with *Mark α*).

**Profile** (bottom-right) — one slice through the heatmap:
- In Az/El mode: PFD vs elevation at the azimuth you chose.
- In α/ΔL mode: PFD vs ΔLongitude at the α you chose — one mask-table row.
  If your α sits inside an exclusion ring, a note tells you whether beams
  there are off or attenuated.
- Guide lines: **amber** = the horizon (a ground user would see the
  satellite at 0° elevation); **cyan** = your minimum served elevation. The
  region between them is pure side-lobe spill — served users stop at cyan.

**Geo map** (top) — footprints as on Tab 1 (green on / red off, or reuse
colours), horizon disc, 3-dB rings (toggle above the map). Left-drag pans,
wheel zooms.

**Mask sampling step** controls resolution: 1° is a good working default;
0.5° looks better but computes ~4× longer; go coarser while iterating on
settings and finer for the final picture.

Two things keep coarse grids honest:

- **Beam peaks are always sampled exactly.** Every active beam's boresight
  is evaluated at its true direction and max-binned into its grid cell, so
  the mask maximum never depends on where the grid happens to fall — even a
  5° draft grid carries the exact peaks.
- **Between peaks, follow the on-screen hint.** The narrowest beams set the
  resolution requirement: a step of about a quarter of the narrowest 3 dB
  beamwidth keeps the sampled field within ~0.4 dB of the true pattern
  (an eighth gets ~0.1 dB). The hint under the step input computes this for
  the current beam set — note it tightens as you raise the edge roll-off,
  because deeper roll-off means narrower beams.

### Exporting a mask (XML / CSV)

**Generate mask XML…** computes the mask not just at the current latitude
but over the whole **latitude table** an orbit sweeps, and writes it to a
file:

1. **Inclination** — enter your orbit's inclination; the dialog caps the
   latitude range at the maximum sub-satellite latitude that inclination can
   reach (e.g. 53° inclination → ±53°). You can narrow the range or change
   the latitude step. The table always contains the exact range endpoints
   and crosses latitude 0 exactly (grid points are multiples of the step),
   even when the range is not a whole number of steps — so the equator,
   where the GSO exclusion bites hardest, is never skipped.

   **Envelope over pass headings** (on by default): a body-stabilised
   layout flies at the orbit's ground heading — ascending and descending
   passes cross each latitude at mirrored headings (sin ψ = cos i / cos φ)
   while the mask's az/el frame stays Earth-referenced. The export computes
   the field at both headings and takes the per-node maximum, which is what
   a filed mask must envelope. Untick to export the single north-aligned
   configuration the live plots show.
2. **Resolution** — the grid steps for the two mask axes in the output file.
   Every output node carries the **maximum** PFD over its surrounding bin
   (±half a step on each axis) rather than a point sample — the mask is an
   envelope, and this guarantees the exact beam peaks appear in the table at
   any step. A coarse step therefore makes the mask more *conservative*
   (peaks spread over wider bins), never under-reported; follow the dialog's
   ¼-beamwidth hint to keep it tight. The compute grid is automatically
   refined to at least half the finest axis step.
3. **Metadata** — satellite name, NTC id, mask id, frequency band and
   reference bandwidth: these go into the XML header.
4. **Format** — *XML* (the S.1503-4 schema, loadable by EPFD tools),
   *CSV* (a spreadsheet-friendly table: one row per latitude × α/azimuth,
   one column per ΔLongitude/elevation value), or both files at once.
5. **Browse…**, then **Generate**. The sweep runs in the background with a
   progress bar; *Cancel* stops it. Your on-screen plots are not disturbed.

The exported type follows the mask-type radio on the tab, and the file
reflects *everything* you configured: power mode, aggregation, exclusion
rings, gating. Points no geometry can reach are written as −1000, the
conventional "unreachable" floor.

**Speed tip:** export time ≈ (number of latitudes) × (one heatmap compute).
Widen the latitude step and/or the mask sampling step for drafts.

---

## Tab 3 — Mask Viewer

Opens an existing S.1503-4 mask XML — the schema the Generate dialog writes
(`satellite_system / pfd_mask / by_a / by_b / pfd`), for either mask type —
and displays it without recomputing anything:

1. Click **Load mask XML…** and pick the file. The header shows the
   satellite name, ntc/mask ids, frequency range, reference bandwidth and
   grid size.
2. Pick a **latitude block** — one `by_a` entry of the table; the viewer
   starts at the block closest to the equator. The panel shows the block's
   **minimum declared PFD**. If that minimum is below −300 dB(W/m²) it
   cannot be operational PFD — it's the mask's "off" floor (some filings
   use −999 instead of the spec's −1000 null) and **Treat min as
   unreachable cut-off** ticks itself, blanking that level and rescaling
   the ramp; untick it to see the raw table. Minima above −300 may be real
   PFD, so they are never treated as a cut-off.
3. Read the plots exactly as on the generator tab: the profile plot slices
   the mask at the cut slider's azimuth (az/el masks) or α (α/ΔLongitude
   masks).

The loaded table is kept **exact** in memory — the plots merely sample it,
at screen resolution, whenever they redraw. Every sampled value is the read
an EPFD tool would make: **bilinear interpolation between the bracketing
nodes, clamped at the table edges** (Rec. ITU-R S.1503-4 §D5.1.5). Real
filings usually compress plateau rows, so each row can carry its own node
list; interpolation follows each row's own grid, exactly as the EPFD
software does. Resizing the window re-samples the exact table — nothing is
ever baked into a fixed-resolution intermediate. Reads at or below the
current cut-off — the S.1503-4 null of −1000 dBW by default, or the block
minimum when the checkbox is ticked — are blank; where real data borders
the cut-off, the interpolation ramp is clipped at the colour scale's floor
so the ramp stays scaled to the declared data. Scene-derived overlays
(footprint map, ES-elevation guides, exclusion tint) don't apply to an
imported table and are omitted.

---

## Tab 4 — Orbit Design

Prototypes the three SNS v10 orbit-parameter groups from a target orbit,
shell by shell — one document holds every shell of the constellation, and
every sub-tab edits the shell selected in the **Shells** list on Start
here (a switcher on each working sub-tab flips shells without going
back).

**Target orbit (Start here).** Altitude (km), inclination and
eccentricity, entered once on the opening sub-tab — every case starts from
this orbit. The solver's own settings (the largest cycle length to search,
in orbits per cycle, and the altitude band it may move within) sit on the
Repeat solver sub-tab. Everything recomputes as you edit.

**Solutions grid (top right).** Repeating-ground-track candidates: a track
repeats after `k` nodal orbits when `k · S_pass = m · 360°`, with `S_pass`
the westward node shift per orbit from the same J2 secular rates the
propagator integrates. Per row: `k` and `m` (coprime), the exact altitude
that closes the cycle, its offset from your target, the cycle duration in
the SNS `rpt_prd_dd/hh/mm/ss` split at both altitudes (`cycle@target`,
the default declaration at your own altitude, and `cycle@exact`), the
free-flight drift per cycle at your altitude — what station keeping
absorbs when declaring at the target — the equator spacing `360/k`, and
the largest `keep_rnge` before adjacent swept deadbands overlap
(`180/k`).

**Declare a period of your own** (fixed-altitude mode). The declaration
itself: auto-filled with the nearest repeat, updated by selecting a
grid row, or typed as any pair (whole orbits per whole nodal days). It
is validated like any solved candidate — a non-coprime entry is reduced
to the true cycle first, the exact closing altitude is solved anywhere
in 100–30 000 km — and rides as the selectable, highlighted top row of
the grid, flagged red when its altitude falls outside the search band.
In adjusting-altitude mode the entry gives way to the search settings.

**Case previews (left).** Cases 1 and 3 read the target orbit; only
Case 2 reads the selected candidate row.

- *Case 1 — free drift* (purely informational — it shows how the EPFD
  calculation will model the orbit; nothing in the panel is filed): for a
  chosen run length `NOrbits`, the artificial precession numbers the
  calculation derives — `S_pass`, the grid value `S_grid`, the rate, the
  run duration, and the spacing the run actually measures
  (`2·S_pass − S_grid`; the Steps 8–11 transcription lands one adjustment
  past the grid, documented upstream). The filing itself carries only
  `f_stn_keep='N'`, `f_precess='N'`.
- *Case 2 — station-kept repeating*: your `keep_rnge` against the row's
  bound (red when the deadbands would overlap), plus the ready
  `f_stn_keep='Y'` / `keep_rnge` / `rpt_prd_*` field set. The declared
  altitude follows the solver's **altitude mode** — *fixed altitude*
  (the default: keep the target orbit; the panel prints the drift the
  station keeping absorbs with a correction cadence for your
  `keep_rnge`) or *adjusting altitude* (adopt the exact closing
  altitude, zero correction); the EPFD calculation flies the declared
  repeat and sweeps the deadband either way.
- *Case 3 — declared precession*: `f_precess='Y'` / `precession`, either
  the plain-J2 nodal regression rate at the target orbit (the default) or
  a rate you supply yourself (deg/s, any sign — prograde orbits drift
  westward, so negative is the normal case), shown against the J2 value.

**Copy SNS fields** puts all three previews on the clipboard.

**Ground track (bottom right).** One full cycle of the selected
candidate, propagated through the real constellation propagator over the
coastline map at the *declared* altitude. The filled dot marks the start,
the ring the end: at the exact closing altitude they coincide and the
printed closure is 0; at the target altitude (the default) the printed
closure equals the free-flight drift per cycle — the correction burden,
not an error. A *whole constellation* toggle overlays one declared
cycle of every satellite of every shell in the document, each shell
flying its own declared repeat: the repeat is per orbit and shared by a
shell's planes, so the phasing only decides whether the satellites ride
the same k track lines or interleave between them — the overlay makes
that visible.

The tab opens on a **Start here** overview: the shells list (add,
duplicate, move up/down, remove — a document always keeps at least one
shell, shells can be named, and the order is what numbers `orb_id`
across shells in the combined notice and the built SRS), the selected
shell's target-orbit inputs, plus the four-step workflow with a jump
button per step and a note on what each step feeds forward (the selected
candidate drives the cases and the constellation; the saved design file
is the hand-off to the builder).

The shells list also shows the **constellation repeat** — `P_repeat`,
the LCM of the shells' declared cycles, which is what the EPFD run
actually cycles over (§D4.6: the time for *every* satellite, across
every sub-constellation, to return to the same position relative to the
Earth) — and warns when shells mix repeating and non-repeating
(§A2.4/§B5.1 want all one or the other). The **Harmonize rpt_prd**
button on the Constellation sub-tab (shown while the case is Case 2)
declares that common period on every Case-2 shell: any multiple of a
shell's own cycle is a valid repeat period for its track. Changing a
shell's pair or altitude mode clears its harmonization.
Only a Case-2 repeating design needs the solver's repeat pair — and by
default even that is declared at the target altitude, the exact closing
altitude staying as the zero-correction reference; free drift and
declared precession skip the solver entirely. The working sub-tabs share
one state:

- **Repeat solver** — mode first: *fixed altitude* shows the
  declared-pair entry (auto-filled with the nearest repeat), *adjusting
  altitude* shows the search settings (max orbits per cycle, altitude
  band); then the keep_rnge tolerance (your promise, not a solved value —
  the solver bounds it and, at a fixed altitude, prices its crossing
  cadence; mirrored with the cases tab), the shared candidate grid and
  the propagated ground track with its closure markers; *How the solver
  works* opens `docs/repeat-solver.html`, the full explanation with a
  worked example.
- **Station-keeping cases** — the three case panels (artificial-precession
  numbers with the NOrbits derivation from a victim beamwidth, the
  keep_rnge rule, the declared J2 rate) and the copy-to-clipboard field
  set.
- **Constellation** — the Walker shell (planes, satellites per plane,
  phasing F, LAN of plane 1 and spread — 360 = delta, 180 = star —
  in-plane offset, argument of perigee, operating height, case choice)
  with the SNS orbit and phase tables shown live: the selected shell
  alone, or with *preview all shells* every shell combined into one
  notice (orb_id continuing across shells) — the exact tables the
  builder emits.

**Save / Load design.** The whole document — every shell, each with its
*selected* repeat candidate — round-trips through one
`*.orbitdesign.json` file (schema 4; older single-shell files load as a
one-shell document). The intermediate file a simulation or the SNS
builder consumes reproduces exactly the constellation you designed, and
the builder takes all shells from it in one load.

Every input carries a tooltip; filing parameters share their help text
with the parameter cards, so the app and the documentation cannot drift
apart.

---

## SNS v10 builder (window)

Opened from the Home page or the Constellation sub-tab, the builder
assembles a complete SNS v10 dataset from separate elements:

- **Notice identity** — `ntc_id`, satellite name and administration.
- **Orbits** — the shells of each loaded `*.orbitdesign.json` file: a
  schema-4 document contributes its whole constellation at once, an older
  single-shell file contributes one.
- **Masks and operating-parameter sets** — one row per mask XML with its
  `mask_id`, `f_mask` (P/E/S/R), type, frequency range and link scope:
  `orb` (the plane the mask serves; empty = whole constellation), `sat`
  (the satellite within that plane; empty = every satellite) and, for E
  masks, `e_as` (the specific earth station; empty = typical/all). `R`
  rows register as operating-parameter sets.
- **Earth stations** — declared `e_as_stn` rows (id, name, type S/T,
  coordinates, dish) that E-mask `e_as` links reference; a specific
  station (S) needs coordinates.
- **Scenario frequencies** — the examined ranges (E = emission,
  R = reception) of the single built scenario.

**Build dataset** writes both databases — `<ntc_id> SRS.MDB` and
`<ntc_id> Masks.MDB` — cloned from donor schemas (the reference donors
are used when present, otherwise you pick them). Every mask links into
scenario 1 at the scope its row sets — whole constellation, per plane,
per satellite, or a specific earth station — and the builder checks the
references (an `orb` link must match an orbit row; an `e_as` link needs
its `e_as_stn` entry).

---

## Operating-parameters designer (R set)

*Tools → Operating parameters (R set)*, also reachable from the builder.
Authors one `non_gso_operating_parameters` set: the header quantities as
single fields (empty = the attribute is omitted from the XML), the four
arrays — `min_exclude`, `min_elev`, `max_co_freq`, `min_duration` — as
plain text, one node per line (the group headers show each line format).
The set round-trips through `*.opparams.json`, and **Export R XML**
writes the byte-convention R-set XML; register it in the SNS builder as
an `f_mask R` row. The writer's encoding rules are enforced on export:
`min_duration` is never written as 0 (omit it for the classic
algorithm), `min_angle_at_es` is rejected when `min_duration` is
declared, and `es_density`/`es_distance` must be declared together or
both omitted. Every field carries the parameter-card help text.

**Derive from simulation.** The intended path when the declarations are
not known a priori: point at an orbit design document and the operation
profile — required, because the profile IS the system under measurement
(payload, transmission basics, gates, service geography; there are no
stand-in fields) — and fly it. Every granted link's measured
elevation, GSO offset, per-cell and per-satellite link counts and
inter-link angles are enveloped into the declared set — minima floored
to 0.1°, maxima carried, `es_density`/`es_distance` taken from the
profile's service grid,
an exclusion array derived only where an exclusion actually shaped
operations, and quantities never observed left undeclared. The result
fills the designer for review, then saves or exports like any
hand-entered set — declarations as envelopes of the simulated truth,
exactly the way the pfd/e.i.r.p. masks are made. The **What goes in
the R set?** button opens the accompanying page
(`docs/r-set-designer.html`): what the set declares and deliberately
does not, header-vs-arrays reading, and both authoring paths.

---

## Operation profile (window)

*Tools → Operation profile.* The real system's operating characteristics
as one saved element (`*.opprofile.json`) — the counterpart of the orbit
design document for the operational side, and the input to the
simulation runner, the R-set deriver and the compliance loop
(`docs/compliance-loop-plan.md`). Every group says **where its
parameters flow**, and every field's tooltip is its parameter card (the
same text as `docs/parameter-cards.html`, kept identical by a check):

- **Downlink** — the *footprint source* first (*beam composition*
  computes the victim-facing footprint live from the shaped beams — the
  truth; *PFD mask* points at a declared S.1503-4 mask XML that
  simulations read the examination's way, §D5.1.4.1 over §D5.1.5 reads;
  the beam fields still shape the scheduler's coverage geometry in both
  modes); then the *payload*, whose **Beam pattern** and **Beam layout**
  groups mirror the Composite gain tab input for input — showing and
  hiding by mode exactly as the tab does, driven by the *effective*
  (composed) pattern and layout so "(scene default)" choices reveal the
  right inputs (pattern model,
  peak gain, θ_b, floor, Taylor SLR/n̄, the auto-hex and UV-array
  switches, beam cell radius, elliptical half-axes, edge roll-off, §1.2
  LN; rings crossover level — minimum elevation is not repeated, it
  comes from Coverage), plus the transmit side (per-beam Tx power
  density in the reference bandwidth — per-beam boresight e.i.r.p.
  density = power density + peak gain — power mode, aggregation and the
  reuse cluster size N picked from {3, 4, 7} as on the generator tab —
  and the aggregation is **modelled in the runs**: with co-channel
  reuse declared, the epfd composite takes the worst colour instead of
  the all-beam power sum, matching what the masks envelope —
  reference bandwidth); then the *link discipline* pair (min angles at
  satellite/ES), which the scheduler enforces and the R set declares. A
  fresh profile opens **pre-filled with the PFD-mask generator's own
  defaults** (Taylor elliptical and friends) rather than blanks;
  emptying a field (or an indeterminate checkbox) falls back to the
  scene default.
- **Uplink** — the transmit chain feeding epfd(up) (frequency, ES power
  ceiling, range-based power-control reference elevation, S.1428 dish),
  then its own link-discipline pair.
- **Coverage / service** — the served-cell geography (minimum elevation
  with per-latitude rows, service area, cell pitch, coverage radius).
- **Operation / scheduling** — scheduler gates and the derived R set
  (tracking strategy, Nco per cell and per satellite, hold time, demand,
  activity factor/period, operational fraction, illumination duty).
- **Exclusion** — the one number that reaches *both* sides: the scene
  ring baked into every exported mask, and the scheduler gate declared
  in the R set (left at 0 until the compliance loop finds it).
  Per-latitude rows gate the scheduler only until mask inheritance
  lands — setting them raises a warning where figures are made.
- **Composition preview** — the payload these fields produce, live: the
  built spot-beam count (cell radius + min elevation + altitude decide
  it; the roll-off shapes each beam within its cell), the active count
  after the exclusion-ring and serving gates, at a chosen preview
  altitude (a run uses each shell's own operating height). Which beams
  actually transmit at an instant is the scheduler's decision. An
  **in-place sketch** draws the beams right there — 3-dB outlines and
  boresight dots in local kilometres around the sub-satellite point,
  the dashed rim marking the served field of view, gated-off beams in
  grey; no geographic map.

The parameter list is open and grows with the model.

---

## Compliance loop (window)

*Tools → Compliance loop.* Steps 4–7 of the producer workflow
(`docs/compliance-loop-plan.md`): epfd(down) victims are swept across a
latitude grid (ES latitude from/to/step at a chosen ES longitude, the
wanted GSO at ES longitude + offset, S.1428 dish), one run per grid
point against the entered limit — the applicable Article 22 table rows,
one `epfd_db percent` per line. The rows can be **loaded from the BR
limits database** (`EPFD_limits_*.mdb`, needs the BR native library
pair): Load extracts the epfd(down) rows applicable to the profile's
downlink carrier and the design's operating height through the same
vendored reader radians uses, and Use fills the limit text with the
chosen row's points — what the sweep verdicts against stays visible and
editable, so a hand-entered table and a loaded one are the same thing
checked the same way. Per-latitude short-term rows are shown for hand
transcription (the flat text cannot express them). Each point is verdicted with the
examination's own §D7.1.3 comparison (pass iff the measured exceedance
at every limit epfd stays within the allowed percentage) and reported
with the worst dB margin read off the CDF (positive = room to spare);
failing rows show red, and the table exports to CSV. The summary also
prints the **power headroom**: epfd moves exactly dB-for-dB with the
per-beam Tx power density, so the worst margin doubles as the TxEirpDbw
headroom at the swept exclusion (live-composition footprint only — a
declared mask is fixed). **Suggest
exclusion** walks the global exclusion angle upward from the profile's
value — one full sweep per step, up to a cap — to the smallest compliant
α, noting when only some latitudes failed (per-latitude α rows are then
the finer declaration); **Apply to profile** writes the found angle back
into the operation profile, from where the R-set deriver and the mask
export turn the compliant system into its declarations. When the
profile's downlink footprint source is *PFD mask*, the sweep runs the
examination's own down algorithm against the declared mask and R-set
gates instead of the live composition — the direct check of what the
examination will compute from the filing; the advisor's α walk then
tightens the declared exclusion zone while the mask file stays fixed.
The **How is compliance judged?** button opens the accompanying page
(`docs/compliance-loop.html`): the sweep and its verdicts, the limits,
the advisor's walk and write-back, and how deep a screening run reads.

---

## Simulation runner (window)

*Tools → Simulation runner.* Runs the epfd(down) / epfd(is) / epfd(up)
simulation from an orbit design document and an operation profile — no
databases needed — over the scheduler-driven operation model, the same
composition the validation dataset used. The **What feeds the run?** button opens the
accompanying page (`docs/simulation-runner.html`) explaining which
input supplies what and what fills in when an input is left empty. The animated map runs one continuous
timeline with two paces side by side: **▶ Play** draws every step —
satellites moving, the service cells, thin lines for every *candidate*
(feasible) link and thick lines for the *active* (granted) ones, with
a live count — and **⏩ accelerated play** advances the same timeline
many steps per tick with sparse redraws; switch between the icons at
any moment, **⏹** stops. Neither collects statistics — **Write CDFs**
is the full statistics run with no UI updates, producing the CDFs.
Inputs: the orbit design document (the space segment — where the
satellites are) and the operation profile (`*.opprofile.json` — what
the system does), **both required**: the profile supplies the whole
system side including the transmission basics (payload, power,
frequencies) and there is deliberately no stand-in payload behind it.
An optional operating-parameter set (`*.opparams.json` from the R-set
designer) is an **alternative gate source**: when set, the scheduler
obeys the declared pointing/scheduling constraints instead of the
profile's enforced gates, in both directions — the truth payload flown
under the declared discipline. The remaining fields describe the victim
and the run: GSO longitude; ES latitude/longitude, which also serve as
the up/is victim's boresight; the S.1428 dish diameter (victim dish;
also the transmitting ES when the profile's uplink side declares no
dish); duration and time step. **Write CDFs…** executes on a
worker thread and writes three CDF CSVs in S.1503-4 D7.1.2 bins (0.1 dB):
`base.down.csv`, `base.is.csv` (the byproduct at the GSO satellite
victim — S.672, 40.7 dBi / 1.55°) and `base.up.csv`. Keep the victim
ES inside the profile's service area. When the operation profile
declares the *PFD mask* footprint source, epfd(down) is computed the
examination's way from the declared mask (§D5.1.4.1) instead of the
live composition; there is then no `.is.csv` — the intersatellite
byproduct needs the e.i.r.p. masks, not the pfd mask.

---

## Tips & troubleshooting

- **The heatmap looks almost one colour.** The ramp auto-scales; when the
  data is genuinely flat (e.g. constant-PFD power mode) the whole served area
  is one shade by design. Check the legend numbers.
- **The profile curve ends before the plot edge.** It ends at the horizon —
  the amber guide marks it. Nothing is missing.
- **A dip in the middle of the profile.** That's the GSO exclusion: beams
  there are off or attenuated, so only side lobes remain.
- **Everything recomputes slowly.** Increase the mask sampling step (the α
  solver runs per pixel), and prefer the α/ΔL mode only when you need it —
  it samples more densely than Az/El.
- **Exports:** generate a small draft first (10° latitude step, coarse axis
  steps) to sanity-check the settings before a fine run.

## Where the maths lives

The beam pattern equations, hex-layout derivations and coordinate
conventions are documented in the [README](../README.md) and
[hex-layout.md](hex-layout.md). References:

- **Rec. ITU-R S.1528** — single-beam antenna patterns (§1.2, §1.3, §1.4).
- **Rec. ITU-R S.1503-4** —
  mask coordinates, the α avoidance angle and its sign (§D6.4.4), the
  satellite az/el frame (§D6.4.5), the mask XML schema; §C2.3.1: the PFD at
  any ground point is *"the sum of the pfd produced by all illuminating
  beams in the co-frequency band"*; §C2.4.1: the mask accounts for *"the
  maximum number of co-frequency beams which can be illuminated
  simultaneously"*; §C2.2: the exclusion implemented here is the
  *"cell-centre observance of a non-operating zone"* mitigation (beam off
  when the cell centre sees the satellite within α₀ of the GSO arc).
- **Frequency reuse / multiple access** — Maral, Bousquet & Sun,
  *Satellite Communications Systems*, 6th ed. (Wiley): §5.11.1.2 "Frequency
  reuse" (reuse via multibeam antenna isolation), §9.8.7.3 "Beam lattice"
  (Fig. 9.40: the three-frequency and seven-frequency patterns = this tool's
  N = 3 / N = 7), Ch. 6 "Multiple Access".
  3GPP TR 38.821 (NTN, FRF = 1 and FRF = 3).
- **Orbital geometry** — M. Capderou, *Handbook of Satellite Orbits: From
  Kepler to GPS* (Springer, 2014): maximum sub-satellite latitude vs
  inclination (§8.2.2 — the export dialog's latitude cap), satellite viewing
  geometry (Ch. 12), spherical trigonometry (§6.13). Note: it also
  quantifies the spherical-Earth simplification used throughout this tool
  (geodetic vs geocentric latitude differ by ≲0.15°).
