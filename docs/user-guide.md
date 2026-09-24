# radians.beamlab — User Guide

radians.beamlab describes a multi-beam non-GSO satellite system and
prepares what its filing under Rec. ITU-R S.1503-4 needs. Four tabs work
on the satellite and its orbit, each answering one question:

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
4. **"How do I describe my orbit in the filing?"** — the **Orbit Design**
   tab: find repeating ground tracks, compare the three ways a filing can
   describe station keeping, and grow the orbit into shells of satellites.

Five windows, opened from the Home page, carry the system from what it
really does to what it declares:

- **Operation profile** — how the real system operates, saved as one file.
- **Simulation runner** — simulates the equivalent power flux-density
  (epfd) of the system — downlink, inter-satellite and uplink — and writes
  its statistics.
- **Compliance loop** — checks the downlink epfd against the Article 22
  limits over a range of latitudes, and derives the declarations from the
  system.
- **Operating parameters (R set)** — writes the declared operating
  constraints.
- **SNS v10 builder** — assembles the filing dataset.

The tabs are independent — each keeps its own settings, so you can
experiment in one without disturbing the others.

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
Mask Viewer. Each card's **Open** button brings up its tab or opens its
window.

Below the cards, **Documentation** has four buttons: **User guide** (this
page); **Parameter cards** (`docs/parameter-cards.html`, the reference for
every parameter and how they relate — its **pipeline diagram** shows the
whole tool at a glance: what a run consumes, what happens inside the
simulation, what comes out, and the producer loop that turns outputs back
into declared inputs); **Orbit case guide** (the three station-keeping
cases); and **Repeat solver guide**. A button is greyed out when its page
is not found. The version sits at the bottom.

Switching tabs never resets a function's state. The sections below follow
the tab strip — Composite gain map, PFD Mask Generator, Mask Viewer, Orbit
Design — and then describe the windows.

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
| Left-click a beam marker | toggle that beam on/off (on release; a drag that starts on a marker pans instead) |
| Left-click elsewhere | probe gain / PFD at that point |
| Left-drag | pan |
| Right-drag | move the satellite (live) |
| Mouse wheel | zoom around the cursor |

### The controls, in brief

In the order the panel shows them:

- **Beam pattern** — six models from ITU-R S.1528. Use the Taylor §1.4
  (circular or elliptical) when you want realistic side lobes; the §1.2 /
  §1.3 envelopes when you want the standard's envelope shapes. The *Show
  pattern…* button plots the current single-beam pattern so you can sanity-
  check it before building a constellation of it. Under the model sit its
  shape inputs:
  - **Far-out / null floor LF** — the level far side lobes never drop
    below, for every model.
  - **Side-lobe ratio SLR** and **n̄ secondary lobes (2..6)** — the side-lobe
    shape of the two §1.4 Taylor models.
  - With the §1.4 elliptical model: **Edge-of-cell roll-off** (3, 5 or 7 dB)
    sets how far the gain falls at the edge of the cell. The cell size comes
    from **Target ground-cell radius** when Auto hex tessellation is on —
    each beam is then sized so its footprint is a circle of that radius on
    the ground — and from the half-axes **α** and **β** (angles seen from the
    satellite) when it is off.
  - **Near-in side-lobe LN** — the near side-lobe level of the §1.2
    envelope.
- **Crossover level** — how far apart neighbouring beams sit: −3 dB is the
  engineering standard (beams meet at their half-power edge); pull it tighter
  for more overlap.
- **Heatmap / probe** — *Single-beam max gain contribution* (the default)
  shows just the strongest beam at each point; *Power-sum from adjacent
  beams (aggregate)* adds all beams. The choice also decides how the probe
  and the PFD adjustment read the PFD.
- **Region exclusion** — switch off every beam whose footprint centre lands
  in a chosen country or lat/lon box, in one click.
- **All beams ON** — switches every beam back on and gives each adjusted
  beam its original peak gain again.
- **PFD adjustment** — after switching off a country's beams you may still
  leak side-lobe power into it. **Adjust adjacent beam gains** lowers the
  peak gain of each beam that leaks into the country chosen under Region
  exclusion until the PFD over that country stays under the limit, read in
  the active Heatmap / probe mode. In the default single-beam mode each beam
  must stay under the limit on its own; in the power-sum mode the limit is
  split equally among the leaking beams so that their sum stays under it. A
  beam whose reduction would take its peak gain below **Minimum Gmax** is
  switched off instead. Adjusted beams turn amber, and the status bar says
  how many beams were reduced or switched off and what worst excess over
  the limit remains. The group's other inputs:
  - **Tx power per beam, in refBW (dBW)** — the power each beam transmits;
    it turns gain into PFD for the adjustment and for the probe.
  - **PFD limit mask** — *US MSS 1.518–1.525 GHz (4 kHz refBW)*, a limit
    that rises with the elevation at which the ground point sees the
    satellite, or *Flat*, which uses **Flat PFD limit** everywhere. The
    probe's status line also shows this limit and the margin at the clicked
    point.
- **Display** — **Render gain heatmap** (off by default), **Show 3-dB
  footprint circles** (on by default) draws each beam's 3 dB outline, and
  **Heatmap floor (dBi)** sets the bottom of the colour ramp, which runs
  from this floor up to the peak gain; lower gains take the floor colour.
- **Refresh / rebuild** — rebuilds the beam set from the current inputs and
  redraws. A rebuild switches every beam back on at full gain, so it clears
  toggles, exclusions and adjustments. Changing most inputs rebuilds too:
  switch beams off and adjust gains once the layout is final.

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

### Other controls, in brief

- **Edge-of-cell roll-off**, **Side-lobe ratio SLR**, **n̄ secondary lobes
  (2..6)** and **Far-out / null floor LF** — the shape of the elliptical
  Taylor beam, as on Tab 1. The cell radius sizes each beam; the roll-off
  sets how far its gain falls at the cell edge.
- **GSO min elev** — the readout under the frequency: the lowest elevation
  at which a GSO earth station operates at that frequency, from S.1503-4
  Table 8 (10° below 17 GHz, 20° from 17 GHz up). It is shown for
  reference; nothing on the tab uses it.
- Under the gating inputs, a readout counts the beams built and the beams
  still on after the elevation and GSO-exclusion gating.
- **Reference bandwidth (kHz)** — the bandwidth the transmit power is
  quoted in. It labels the PFD legend and pre-fills the export dialog; it
  does not rescale the PFD.
- **Refresh / rebuild** — rebuilds the beams and their gating from the
  current inputs and redraws; the status line reports how many beams are
  active.

### Choosing a mask type

- **Azimuth / Elevation** — the mask as seen *from the satellite*: where is
  it pointing energy? X is azimuth (east–west from nadir), Y is elevation
  (north–south). The coloured oval is the visible Earth; the red band through
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

  *Where N comes from:* the PFD mask is the operator's declared envelope of
  how the system really operates on shared frequencies, so the cluster size
  is **your system design choice, not a regulatory value** — set it to
  match the payload's real channel plan, or use the power sum as the N = 1
  worst case. N = 3, 4 and 7 are the classical cluster sizes of a hexagonal
  reuse plan. In this tool N always means the **cluster size**: how many
  colours the plan uses.

  > **Technical detail.** Rec. ITU-R S.1503-4 §C2.3.1 takes the pfd at a
  > ground point as the sum over all illuminating beams in the co-frequency
  > band, and §C2.4.1 has the mask account for the largest number of
  > co-frequency beams that can be lit at the same time. Reuse-1 and reuse-3
  > are the two options 3GPP TR 38.821 studies for NTN. The name "frequency
  > reuse factor" means different things in different sources: some
  > satellite-communications texts use it for the number of times the band
  > is reused across the coverage, roughly beams ÷ colours (4.3 for 13 beams
  > on a 3-colour lattice), while 3GPP calls the cluster size itself the
  > frequency reuse factor (FRF).

### Advanced exclusion — α rings

The basic GSO protection is a single rule: *beams pointing within α_excl of
the arc are off*.

> **Technical detail.** The basic rule is the mitigation Rec. ITU-R
> S.1503-4 §C2.2 calls cell-centre observance of a non-operating zone: a
> beam is off when the centre of its cell sees the satellite within α_excl
> of the GSO arc. The test uses the magnitude of α, the avoidance angle of
> §D6.4.4, to the nearest visible point of the arc.

If you need something graded, tick **Advanced exclusion (α rings)** and
click **Edit α rings…**:

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
- Blank white pixels = no line of sight (beyond the horizon) or, in α/ΔL
  mode, combinations no ground point produces.
- The dashed black line is the **profile cursor** — drag the slider above
  the profile plot to move it.
- Red / orange tinting = the exclusion rings (toggle with *Mark α*).

**Profile** (bottom-right) — one slice through the heatmap:
- In Az/El mode: PFD vs elevation at the azimuth you chose.
- In α/ΔL mode: PFD vs ΔLongitude at the α you chose — one mask-table row.
  If your α sits inside an exclusion ring, a note tells you whether beams
  there are off or attenuated.
- Guide lines: **amber** = the horizon (a ground user would see the
  satellite at 0° elevation); **teal** = your minimum served elevation. The
  region between them is pure side-lobe spill — served users stop at teal.

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
   layout turns with the satellite's direction of flight, and a satellite
   heading north crosses a latitude at a different heading from one heading
   south, while the mask's directions stay fixed to the Earth. The export
   therefore computes the field at both headings and keeps the larger value
   at every node, which is what a filed mask must cover. Untick to export
   the single north-aligned configuration the live plots show.

   > **Technical detail.** Ascending and descending passes cross latitude
   > φ at mirrored headings ψ, with sin ψ = cos i / cos φ for inclination
   > i, while the mask's az/el frame (§D6.4.5) stays Earth-referenced. The
   > export takes the per-node maximum over the two headings.
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
   use −999 instead of the spec's −1000 null). **Treat min as unreachable
   cut-off** then blanks that level and rescales the ramp; untick it to see
   the raw table. The box ticks itself only when a file loads, and only if
   the starting block has such a floor; picking another block leaves it as
   you set it. It is greyed out for a block whose minimum is −300 or above:
   such minima may be real PFD, so they are never treated as a cut-off.
3. Read the plots exactly as on the generator tab: the profile plot slices
   the mask at the cut slider's azimuth (az/el masks) or α (α/ΔLongitude
   masks).

The loaded table is kept **exact** in memory — the plots merely sample it,
at screen resolution, whenever they redraw. Every sampled value is read the
way Rec. ITU-R S.1503-4 §D5.1.5 reads a mask: **bilinear interpolation
between the bracketing nodes, clamped at the table edges**. Real filings
usually compress plateau rows, so each row can carry its own node list;
interpolation follows each row's own grid. Resizing the window re-samples
the exact table — nothing is ever baked into a fixed-resolution
intermediate. Reads at or below the
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

**Solutions grid (Repeat solver sub-tab, top right).** Repeating
ground-track candidates near your target orbit: each row is a track that
repeats after a whole number of orbits in a whole number of days, solved to
the exact altitude that closes the cycle. Per row: the two whole numbers,
the exact altitude and its offset from your target, the cycle duration
at your own altitude and at the exact altitude (what a Case 2 shell
declares by default), the drift per cycle at your altitude — what the
EPFD calculation flies every cycle when a shell files that altitude — the
spacing between neighbouring tracks at the equator, and the widest keep
range before neighbouring swept deadbands overlap.

> **Technical detail.** A track repeats after `k` nodal orbits when
> `k · S_pass = m · 360°`, with `S_pass` the westward node shift per orbit
> from the same J2 secular rates the propagator integrates; `k` and `m` are
> coprime. Columns: `orbits k`, `days m`, `exact alt`, `delta`,
> `cycle@target` and `cycle@exact` (the SNS `rpt_prd_dd/hh/mm/ss` split at
> each altitude), `drift@target` (free-flight drift per cycle), `spacing`
> (`360/k`) and `max keep_rnge` (`180/k`).

**Declare a period of your own** (Repeat solver sub-tab, left, in
fixed-altitude mode). The declaration itself: auto-filled with the
nearest repeat, updated by selecting a grid row, or typed as any pair
(whole orbits per whole nodal days). It
is validated like any solved candidate — a non-coprime entry is reduced
to the true cycle first, the exact closing altitude is solved anywhere
in 100–30 000 km — and rides as the selectable, highlighted top row of
the grid, flagged red when its altitude falls outside the search band.
In adjusting-altitude mode the entry gives way to the search settings.

**Case previews (Station-keeping cases sub-tab).** Case 1 reads the
target orbit; Cases 2 and 3 read the selected candidate row. The case is
decided the way Rec. ITU-R S.1503-4 Figure 52 decides it, station keeping
first: a shell without station keeping is Case 1 whatever else it files;
a station-kept shell is Case 2, or Case 3 when the administration
supplies a precession rate.

- *Case 1 — free drift* (purely informational — it shows how the EPFD
  calculation will model the orbit; nothing in the panel is filed): for a
  run length **NOrbits (run)**, typed or derived from the **victim
  beamwidth**, the artificial precession the calculation adds so that the
  run's equator crossings spread evenly — its rate, the run duration, and
  the crossing spacing the run actually measures.
- *Case 2 — station-kept repeating*: your **keep_rnge** against the
  selected row's bound (red when neighbouring deadbands would overlap),
  plus the ready field set. The filed altitude follows the solver's
  **altitude mode** — *adjusting altitude* (the default: the exact closing
  altitude, where the filed orbit repeats by itself) or *fixed altitude*
  (the target altitude; the panel prints the drift the EPFD calculation
  then flies every cycle beyond the keep_rnge sweep, and how long a real
  satellite's drift takes to cross your keep_rnge). The calculation flies
  the filed orbit on its own J2 rates and sweeps the deadband; the
  declared repeat only sets the run length, so nothing pulls a
  fixed-altitude orbit back onto the repeat.
- *Case 3 — station keeping with supplied precession*: station-kept like
  Case 2 — the same repeat, keep_rnge and bound — plus a nodal precession
  rate the administration supplies, which the calculation uses instead of
  the J2 node term. An empty **precession (deg/s)** declares the rate
  that closes the selected repeat at the target altitude; a typed rate
  (signed: negative turns the plane west) is used as typed. Case 3 always
  files the target altitude, whatever the altitude mode. The filing
  carries the rate's magnitude in degrees/day; its direction is the one
  the inclination implies — west for a prograde orbit (below 90°), east
  for a retrograde one (above 90°), none for a polar orbit at exactly
  90°. The panel shows the degrees/day on file and which way the rate
  turns. A rate turning against its inclination, or any nonzero rate at
  exactly 90°, cannot be filed; the panel says so — use Case 2. The
  default rate can turn either way, depending on the selected repeat. A
  typed rate must also close the repeat: the panel shows how far it
  leaves the track from closing the selected repeat each cycle, and more
  than keep_rnge cannot be filed. For an elliptical orbit the panel notes
  that Case 3 holds the perigee fixed while the real perigee turns at the
  J2 apsidal rate it prints (still only at the critical inclinations,
  63.43° and 116.57°). Without a selected candidate the panel asks for
  one.

> **Technical detail.** Case 1 prints `S_pass`, the grid value `S_grid`,
> the rate, the run duration and the measured spacing `2·S_pass − S_grid`:
> Rec. ITU-R S.1503-4 §D6.3.2 Steps 8–11, followed literally, land one
> correction step past the even grid. A Case 1 filing carries only
> `f_stn_keep='N'`, `f_precess='N'`. Case 2 files `f_stn_keep='Y'`,
> `keep_rnge` and `rpt_prd_*`, and is flown per §D6.3.6 eqs (48)–(50):
> its own J2 rates plus the linear `±keep_rnge` sweep of the node over the
> run; the declared period sets only the run length and keeps the time
> step from dividing it exactly (§D4.6.1). Case 3 files the Case 2 fields
> plus `f_precess='Y'` and `precession`, and is flown per eqs (51)–(53):
> the supplied rate `D` replaces the J2 nodal term, the argument of
> perigee is held and the satellite moves at the point-mass mean motion
> `n0`. The default rate is `D = ω_e − m·n0/k` at the target altitude,
> rounded to 0.01 deg/day; the Case 3 repeat period is `k` point-mass
> orbits, `2πk/n0`, in whole seconds. The field holds `|D|·86400`
> deg/day; the direction is `−sign(cos i)`, as in §D6.3.2 eq (21), and a
> rate is fileable when it is 0 or turns that way. The explicit step is
> needed because the rate enters only as a turn about the Z axis,
> `Rz(Ω + D·t) Rx(i) Rz(ω) = Rz(D·t) [Rz(Ω) Rx(i) Rz(ω)]` (§D6.3.3
> eqs (34)–(43) with eq (52)), so the inclination in the matrix does not
> change which way the node turns: the examination software applies the
> direction when it reads the field. The defaults, 1200 km and 53° with
> 13/1, give −3.45 deg/day, filed as 3.45 deg/day turning west; a
> Sun-synchronous orbit at 561 km, 97.64°, circular, with the 15-orbit,
> 1-day repeat gives +0.53 deg/day, filed as 0.53 turning east. The
> default rate also takes up the selected pair's drift at the target
> altitude — roughly 0.08 deg/day for each km between the target and the
> pair's exact altitude in low orbit — so 105/8 at the defaults gives
> +0.02 deg/day and the same Sun-synchronous orbit at 550 km gives
> −0.33 deg/day, both turning against their inclination and refused. The
> typed-rate check is `(D_typed − D_exact)·2πk/n0` per cycle against
> keep_rnge, with `D_exact` the closing rate before rounding; S.1503-2
> §D.6.3.4 Note 1 asked for such a self-consistency check of a supplied
> rate, S.1503-4 no longer does, so it is the app's own. The elliptical
> note compares the perigee eq (51) holds with the J2 apsidal rate of
> eq (22).

**Copy case summary (text)**, below the three panels, puts all three
previews on the clipboard; with no repeating candidate selected it copies
Case 1, which stands without one, and the Case 3 panel, which then asks
for a candidate.

**Ground track (Repeat solver sub-tab, bottom right).** One full cycle of
the selected candidate, propagated through the real constellation
propagator over the
coastline map at the altitude a Case 2 shell would file. The filled dot
marks the start, the ring the end: at the exact closing altitude (the
default) they coincide and the printed closure is 0; at the target
altitude (fixed-altitude mode) the printed closure equals the free-flight
drift per cycle — the drift the EPFD calculation flies every cycle, not
an error of the map. The map does not draw the Case 3 model. A *whole
constellation* toggle overlays one declared
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
(§A2.4/§B5.1 want all one or the other); Case 2 and Case 3 shells both
count as repeating. The **Harmonize rpt_prd** button on the Constellation
sub-tab (shown while the case is Case 2 or Case 3) declares that common
period on every shell: any multiple of a shell's own cycle is a valid
repeat period for its track. It needs every shell to be Case 2 or Case 3
with a candidate, and says which shell is not when one is not. Changing a
shell's pair, case or altitude mode clears its harmonization. Both
station-kept cases need the solver's repeat pair — Case 2 files its exact
closing altitude by default, Case 3 always the target altitude; free
drift skips the solver entirely. The working sub-tabs share one state:

- **Repeat solver** — mode first: *adjusting altitude* (the default)
  shows the search settings (max orbits per cycle, altitude band), *fixed
  altitude* shows the declared-pair entry (auto-filled with the nearest
  repeat); then the keep_rnge tolerance (your promise, not a solved value
  — the solver bounds it and, at a fixed altitude, prices how often a real
  satellite's drift crosses it; mirrored with the cases tab), the shared
  candidate grid and the propagated ground track with its closure
  markers; *How the solver works* opens `docs/repeat-solver.html`, the
  full explanation with a worked example.
- **Station-keeping cases** — the three case panels (artificial-precession
  numbers with the NOrbits derivation from a victim beamwidth, the
  keep_rnge rule, the Case 3 rate in deg/s and deg/day) and the **Copy
  case summary (text)** button.
- **Constellation** — the Walker shell (planes, satellites per plane,
  phasing F, LAN of plane 1 and spread — 360 = delta, 180 = star —
  in-plane offset, argument of perigee, operating height, case choice)
  with the SNS orbit and phase tables shown live: the selected shell
  alone, or with *preview all shells* every shell combined into one
  notice (orb_id continuing across shells) — the exact tables the
  builder emits. **Save design**, **Load design** and **Open SNS v10
  builder…** sit below the shell inputs.

**Save / Load design.** The whole document — every shell, each with its
*selected* repeat candidate — round-trips through one
`*.orbitdesign.json` file (schema 4; older single-shell files load as a
one-shell document). The intermediate file a simulation or the SNS
builder consumes reproduces exactly the constellation you designed, and
the builder takes all shells from it in one load. Save design refuses a
document in which a Case-2 or Case-3 shell has no repeating candidate
selected or a keep_rnge outside its candidate's bound, or a Case-3 shell
has a precession rate turning against its inclination (or any nonzero
rate at exactly 90°) or a typed rate that leaves the track more than
keep_rnge from closing its repeat each cycle, and says which shell.

Most inputs carry a tooltip, and the filing parameters share their help
text with the parameter cards, so the app and the documentation cannot
drift apart. Planes, Sats / plane, LAN of plane 1, In-plane offset, Arg. of
perigee and the Station-keeping case choice have no tooltip.

---

## SNS v10 builder (window)

*Home → SNS v10 builder card → Open*, or **Open SNS v10 builder** on
Orbit Design's Start here or Constellation sub-tab. The builder assembles
a complete SNS v10 dataset from separate elements:

- **Notice identity** — `ntc_id`, satellite name and administration.
- **Orbits** — the shells of each loaded `*.orbitdesign.json` file: a
  schema-4 document contributes its whole constellation at once, an older
  single-shell file contributes one.
- **Masks and operating-parameter sets** — one row per mask XML with its
  `mask_id`, `f_mask` (P/E/S/R), type, frequency range and link scope:
  `orb` (the plane the mask serves; empty = whole constellation), `sat`
  (the satellite within that plane; empty = every satellite) and, for E
  masks, `e_as` (the specific earth station; empty = typical/all). `R`
  rows register as operating-parameter sets. **Design operating
  parameters…** opens the R-set designer; register the XML it exports here
  as an `R` row. Registering an R set also changes how the notice asks to
  be examined: with at least one `R` row it is flagged for examination with
  its operating-parameter sets, and with none, with the single
  network-level set of values in its SRS tables.

  > **Technical detail.** The flag is AP4 item A.4.b.6bis,
  > `non_geo.examset_type`: `E` when the notice carries operating-parameter
  > sets (linked through `mask_lnk3`), `L` when it carries none. The builder
  > derives it; there is no field for it.
- **Earth stations** — declared `e_as_stn` rows (id, name, type S/T,
  coordinates, dish) that E-mask `e_as` links reference; a specific
  station (S) needs coordinates.
- **Scenario frequencies** — the **Scenario name** (default *Scenario 1*)
  and the examined ranges (E = emission, R = reception) of the single built
  scenario.

**Preview notice** assembles the notice without writing anything and
prints what it holds — shells, orbit and phase rows, mask_info rows, R
sets, frequency ranges and earth stations — or the first problem it finds.
**Build dataset (SRS + Masks)…** asks where to save `<ntc_id> SRS.MDB`
and, when the masks grid holds any row (R sets included), writes
`<ntc_id> Masks.MDB` beside it in the same folder. Both are cloned from
donor schemas (the reference donors are used when present, otherwise you
pick them). Every pfd and e.i.r.p. mask links into scenario 1 at the scope
its row sets — whole constellation, per plane, per satellite, or a specific
earth station — and the builder checks the references (an `orb` link must
match an orbit row, a `sat` link needs an `orb` link, and an `e_as` link
needs its `e_as_stn` entry). It refuses to build when such masks are
registered but no scenario frequency range is entered; R sets need none.
Preview notice and Build dataset also refuse a design whose Case 3
precession rate turns against its inclination (or is nonzero at exactly
90°), and name its file: the SRS carries only the rate's magnitude, and
its direction is the one the inclination implies.

> **Technical detail.** The SRS `orbit.precession` field is the magnitude
> of the design's deg/s rate × 86 400, rounded to 0.01 deg/day — format
> 999.99, at least 0 (Rec. ITU-R S.1503-4, SRS orbit table in the
> Attachment to Part B). Its direction, west below 90° and east above, is
> applied by the examination software when it reads the field. The Orbit
> Design tab and the design file keep the signed rate in deg/s.

---

## Operating-parameters designer (R set)

*Home → Operating parameters (R set) card → Open*, or **Design operating
parameters…** in the SNS builder. Authors one
`non_gso_operating_parameters` set: the header quantities as single
fields (empty = the attribute is omitted from the XML), the four arrays —
`min_exclude`, `min_elev`, `max_co_freq`, `min_duration` — as plain text,
one node per line (the group headers show each line format). The set
round-trips through `*.opparams.json` (**Save set (JSON)…**, **Load
set…**, which also opens a loop run's `*.operparams.json`), and **Export R
XML…** writes the byte-convention R-set XML; register it in the SNS
builder as an `f_mask R` row, which also flags the notice for examination
with its operating-parameter sets (see the builder). The writer's encoding
rules are enforced on export: `min_duration` is never written as 0 (omit
it for the classic algorithm), `min_angle_at_es` is rejected when
`min_duration` is declared, and `es_density`/`es_distance` must be
declared together or both omitted. Most fields show their parameter card
as a tooltip; the identity fields sat_name, ntc_id and param_id, and the
second field of each pair — high_freq_mhz, es_distance and es_lat_max —
have none.

**Derive & fill.** The intended path when the declarations are not known
a priori: point at the operation profile and the panel fills every field
from the compliance loop's own derivation for that profile, except the
identity (sat_name, ntc_id, param_id) and the band, which belong to the
filing and stay as entered. It does not
simulate — the loop derives once, on a *saturated* probe (no victim;
demand raised to the declared co-frequency cap when a cap is declared,
otherwise the profile's own demand stands; activity, duty and operating
fraction at 1), because a declaration is an envelope of what the system
*may* do rather than a record of what one traffic sample happened to ask
for. Every granted link's measured elevation, GSO offset, per-cell and
per-satellite link counts and inter-link angles are enveloped into the
declared set — minima floored to 0.1°, maxima carried,
`es_density`/`es_distance` taken from the profile's service grid, an
exclusion array derived only where an exclusion actually shaped
operations, and quantities never observed left undeclared. The result
fills the designer for review, then saves or exports like any
hand-entered set — declarations as envelopes of the simulated truth. (The
pfd mask is not measured on this probe; see the compliance loop.) A run
counts only when it is newer than the profile it describes and was made
from that profile: runs are found by profile name, and a run of the same
name made from a different profile is refused. When there is none, press
**Run loop** in the compliance loop window, which derives the declaration
as its first step. `docs/where-declarations-come-from.html` sets out the
whole relation. The **What goes in the R set?** button opens the
accompanying page
(`docs/r-set-designer.html`): what the set declares and deliberately
does not, header-vs-arrays reading, and both authoring paths.

---

## Operation profile (window)

*Home → Operation profile card → Open.* The real system's operating
characteristics as one saved element (`*.opprofile.json`) — the
counterpart of the orbit design document for the operational side, and
the input to the simulation runner, the R-set deriver and the compliance
loop.

At the top, **Profile name** names the profile: the compliance loop files
its runs under this name, and Derive & fill finds them by it.
**Direction** (*Downlink* or *Uplink*) picks which direction's groups the
left column shows; the shared groups on the right stay, and the saved file
always carries both sides. Each group's header says what its parameters
describe, in the order the window shows them:

- **The projection switch — how the examination sees the downlink**
  (Downlink) — the **Footprint source**: *beam composition (shaped)*
  computes the victim-facing footprint live from the shaped beams — the
  truth; *PFD mask (declared XML)* points at a declared S.1503-4 mask XML
  (**PFD mask XML**) that simulations read the examination's way,
  §D5.1.4.1 over §D5.1.5 reads. The beam fields still shape the
  scheduler's coverage geometry in both modes.
- **Radiated power & spectrum** (Downlink) — the downlink frequency; the
  per-beam Tx power density in the reference bandwidth (per-beam boresight
  e.i.r.p. density = power density + peak gain); the power mode; the
  aggregation and the reuse cluster size N picked from {3, 4, 7} as on the
  generator tab; the reference bandwidth; the illumination duty cycle; and
  the **co-frequency beam capacity**. The aggregation is **modelled in the
  runs**: with co-channel reuse declared, the epfd composite takes the
  worst colour instead of the all-beam power sum, matching what the masks
  envelope. The co-frequency beam capacity is the most same-colour beams
  one satellite radiates at once (S.1325 rev §2.5.2; payload hardware, not
  traffic): the scheduler refuses a satellite a further link in a colour
  that already has that many lit, and the reachable-envelope mask sums
  only that many same-colour beams at each cell — which is what gives a
  top-K mask its warrant; left blank, nothing is limited and the mask sums
  the whole colour.
- **Beam shape & layout** (Downlink) — mirrors the Composite gain tab
  input for input, showing and hiding by mode exactly as the tab does,
  driven by the *effective* (composed) pattern and layout so "(scene
  default)" choices reveal the right inputs (pattern model, peak gain,
  θ_b, floor, Taylor SLR/n̄, the auto-hex and UV-array switches, beam cell
  radius, elliptical half-axes, edge roll-off, §1.2 LN; rings crossover
  level). Minimum elevation is not repeated here; it comes from Service &
  coverage. **Yaw steering range (± deg)**, the last field, is for a
  payload that steers in yaw: the compliance loop's computed mask then
  sweeps every body yaw within it, in steps no coarser than the mask's
  1° grid; empty means no yaw steering. The truth simulation does not
  model yaw steering.
- **Uplink fleet** (Uplink) — the transmit chain feeding epfd(up):
  frequency, ES power ceiling, range-based power-control reference
  elevation, S.1428 dish.
- **Composition preview** (Downlink) — the payload these fields produce,
  live: the built spot-beam count (cell radius + min elevation + altitude
  decide it; the roll-off shapes each beam within its cell), the active
  count after the exclusion-ring and serving gates, at a chosen preview
  altitude (a run uses each shell's own operating height). Which beams
  actually transmit at an instant is the scheduler's decision. An
  **in-place sketch** draws the beams right there — 3-dB outlines and
  boresight dots in local kilometres around the sub-satellite point, the
  dashed rim marking the served field of view and the faint outer ring the
  horizon, gated-off beams in grey, and with co-channel reuse the beams
  filled in their reuse colours; no geographic map.
- **Service & coverage** (shared) — the served-cell geography: minimum
  elevation, service area (latitude and longitude, min / max), service
  cell pitch and coverage radius.
- **Traffic & scheduling** (shared) — the scheduler's gates: tracking
  strategy, Nco per cell and per satellite, hold before handover, demand
  links per cell, activity factor and period, operational fraction, the
  exclusion angle alpha, and the selected direction's minimum angles at the
  satellite and at the ES, which the scheduler enforces and the R set
  declares. As the header says, the Nco caps, the exclusion and the minimum angles
  reach the R set (so does the minimum elevation from Service & coverage); the
  strategy, hold, demand and activity do not. The exclusion is the one
  number that reaches *both* sides: the scene ring baked into every
  exported mask, and the scheduler gate declared in the R set; leave it at
  0 until the compliance loop finds it. Demand, the hold time and the
  per-latitude Nco rows take whole numbers; a fraction or a zero is
  refused rather than rounded.
- **Per-latitude overrides** (shared) — rows by earth-station latitude for
  the minimum elevation, Nco and the exclusion, one node per line: the
  latitude, a space, the value. Minimum elevation and Nco are read at the
  nearest row, the exclusion by linear interpolation between rows; beyond
  the rows the outermost row governs. Once rows are entered they replace
  the scalar field for that quantity (for the exclusion the scalar still
  sets the scene ring the masks bake). Per-latitude exclusion rows gate
  the scheduler only: the beams and any exported mask carry the global
  exclusion angle, and setting rows raises a warning in the compliance
  and simulation windows.
- **File** — **Save profile…** and **Load profile…**, with a summary of
  the profile below and any problem in red.

A fresh profile opens **pre-filled with the PFD-mask generator's own
defaults** (Taylor elliptical and friends) rather than blanks; emptying a
field (or an indeterminate checkbox) falls back to the scene default.

Hover a field for its help. Most fields show their parameter card (the
same text as `docs/parameter-cards.html`). The two carrier-frequency boxes
and the hold field carry their own help instead, because the only cards
for them describe declarations these fields never become. The direction
choice, the coverage radius and the preview altitude have short tooltips
of their own; the profile name and the second box of the service-area and
activity pairs have none.

The parameter list is open and grows with the model.

---

## Compliance loop (window)

*Home → Compliance loop card → Open.* **Run loop** makes three passes.
First the **derivation probe** — saturated, no victim, no limit — measures
the R set. Then the **truth sweep** measures T, and T does not move again.
Then an **examination sweep** reads the declared pfd mask and that R set
and gives E1, shown beside T in a second table with the per-latitude gap
and the acceptance statement: E1 must sit at or above T everywhere, or the
derivation did not envelope the system it describes. The probe does not
measure the pfd mask. The mask is the profile's declared one when the
profile names one; a named file that is missing stops the run, which says
which file. Otherwise the loop exports it into the run directory, over
the profile's **Yaw steering range** when it gives one,
computed directly from the reachable envelope — what the payload can
radiate with no scheduler gate applied — under the service-span
certificate: latitude rows from which the satellite can reach no declared
service cell are written dark. A later run reuses that exported mask while
it is newer than the profile. A set named in the optional **Declared R
set** field replaces the derivation. The run's profile and R set are
written into one directory, an exported mask beside them, so the
designer's Derive & fill reads the set the projection actually used; the
window writes no dated record of the run. **Run sweep** makes one pass:
the truth, or, for a declared-mask profile, the examination against the
named R set or, without one, the profile's own gates.

**The sweep.** epfd(down) victims are swept across a latitude grid (ES
latitude from/to/step at a chosen ES longitude, the wanted GSO at ES
longitude + offset, S.1428 dish), one run per grid point against the
entered limit — the applicable Article 22 table rows, one
`epfd_db percent` per line. The rows can be **loaded from the BR limits
database** (`EPFD_limits_*.mdb`, needs the BR native library pair): Load
extracts the epfd(down) rows applicable to the profile's downlink carrier
and the design's operating height, and Use fills the limit text with the
chosen row's points and sets the ES dish to the row's reference diameter
(a run warns when the dish no longer matches the row) — what the sweep
verdicts against stays visible and editable, so a hand-entered table and
a loaded one are the same thing checked the same way. Per-latitude
short-term rows are shown for hand transcription (the flat text cannot
express them). The verdict follows the rule adopted for this dataset, compliance
against the Article 22 limit curve: pass iff
the measured exceedance at every tabulated limit epfd stays within the
allowed percentage (the examination's own §D7.1.3 comparison) and the CDF
nowhere crosses the log-linear curve between the tabulated points
(tolerance 0.05 dB towards lower epfd). Each row reports the worst point
margin read off the CDF (positive = room to spare) and the curve margin,
the dB shift that just clears the curve; the summary quotes the smaller of
the two and names the rule; the table also shows the deciding point and
the worst crossing of the curve. Failing rows show red. The status line
adds the steps per latitude and the resolvable percentile floor, estimates
the time left while a run goes, warns when the step samples the fastest
crossing of the earth station's 3 dB beam fewer than three times, and says
so when the limit is still the permissive template. **Export table…**
writes the rows as CSV, headed by `#` lines naming the run, its inputs,
grid, depth, step and limit; after a loop run the E1 columns follow. The summary also
prints the **power headroom**: epfd moves exactly dB-for-dB with the
per-beam Tx power density, so the worst margin doubles as the TxEirpDbw
headroom at the swept exclusion (live-composition footprint only — a
declared mask is fixed). **Suggest
exclusion** walks the global exclusion angle upward from the profile's
value — one full sweep per step, up to a cap — to the smallest compliant
α, noting when only some latitudes failed (per-latitude α rows are then
the finer declaration); **Apply to profile** writes the found angle back
into the operation profile, from where the R-set deriver and the mask
export turn the compliant system into its declarations. The **Cap
advisor** group does the same for the per-cell co-frequency
cap, per latitude: it walks the cap down from the profile's effective
baseline, synthesizes the largest cap each latitude's outcome allows,
verifies the composed rows in one joint sweep, and **Apply Nco rows**
writes them into the profile's per-latitude array (operator rows
outside the swept span are kept); when no margin moves it reports that
the cap is not the lever. When the
profile's downlink footprint source is *PFD mask*, the sweep runs the
examination's own down algorithm against the declared mask and R-set
gates instead of the live composition — the direct check of what the
examination will compute from the filing; the advisor's α walk then
tightens the declared exclusion zone while the mask file stays fixed.
The duration and step fields set the run; the step is preset to 1 s, the
truth's step, because sampled every second the examination of STEAM-2
reads within 0.1 dB of the S.1503-4 time step at every latitude. The
**Examination step** choice sets how a declared-mask sweep is sampled: on
that predefined step, or on the fine and coarse steps of S.1503-4 §D4 with
the dual time step of §D5.1.4.1 (the fine-step region of §D4.7.1) over the
same duration, the status line then naming the plan. A live-composition
sweep, the truth, and the two advisors always run on the predefined step.
The **How is compliance judged?** button opens the accompanying page
(`docs/compliance-loop.html`): the sweep and its verdicts, the limits,
the advisor's walk and write-back, and how deep a screening run reads.

---

## Simulation runner (window)

*Home → Simulation runner card → Open.* Runs the epfd(down) / epfd(is) / epfd(up)
simulation from an orbit design document and an operation profile — no
databases needed — over the scheduler-driven operation model, the same
composition the validation dataset used. The **What feeds the run?** button opens the
accompanying page (`docs/simulation-runner.html`) explaining which
input supplies what and what fills in when an input is left empty. The animated map runs one continuous
timeline with two paces side by side: **▶ Play** draws every step —
satellites moving, the service cells, thin lines for every *candidate*
(feasible) link and thick lines for the *active* (granted) ones, with
a live count — and **⏩ accelerated play** advances the same timeline
many steps per tick without updating the map — the last frame stays
and only the status clock (and live counts) move; switch between the
icons at any moment, **⏹** stops. Neither collects statistics — **Write CDFs**
is the full statistics run with no UI updates, producing the CDFs.
Inputs: the orbit design document (the space segment — where the
satellites are) and the operation profile (`*.opprofile.json` — what
the system does), **both required**: the profile supplies the whole
system side including the transmission basics (payload, power,
frequencies) and there is deliberately no stand-in payload behind it.
An optional operating-parameter set (`*.opparams.json` from the R-set
designer) is an **alternative gate source**: when set, the scheduler
obeys the declared pointing/scheduling constraints instead of the
profile's enforced gates, in both directions — the truth payload run
under the declared discipline. The remaining fields describe the victim
and the run: GSO longitude; ES latitude/longitude, which also serve as
the up/is victim's boresight; the S.1428 dish diameter (victim dish;
also the transmitting ES when the profile's uplink side declares no
dish); duration and time step, preset to half a day at 1 s, the truth's
step in the compliance loop as well — the status line warns when the step
samples the fastest crossing of the earth station's 3 dB beam fewer than
three times, and with the Random strategy and no hold the step is also the
reselection period. The optional **Limits** group takes the applicable
Article 22 rows for epfd(down), epfd(is) and epfd(up); a direction with a
limit gets a verdict under the limit-curve rule in the run's summary and in
its CDF file. **Write CDFs…** executes on a
worker thread, using every processor for the satellites of each step
(set the `BEAMLAB_THREADS` environment variable to a smaller count to
leave the machine responsive; the result does not depend on it), and
writes three CDF CSVs in S.1503-4 D7.1.2 bins (0.1 dB), each stating its
step, duration and reference bandwidth in its header, with the time left
estimated while it runs:
`base.down.csv`, `base.is.csv` (the byproduct at the GSO satellite
victim — S.672, 40.7 dBi / 1.55°) and `base.up.csv` — then opens the
**CDF viewer** over the written curves (epfd on a linear dB axis
against % time exceeded on a log axis, one colour per direction; the
viewer takes any (label, epfd, percent) series, so other tools can
call it too). **View CDFs…** opens the same viewer over existing CDF
CSVs — pick one or more files from any earlier run. Keep the victim
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

## Earth model and sources

The tool works on a spherical Earth throughout; geodetic and geocentric
latitude differ by less than 0.2°.

Sources: Rec. ITU-R S.1528 §1.2, §1.3 and §1.4 (single-beam antenna
patterns); Rec. ITU-R S.1503-4 Parts A to D — §C2.2, §C2.3.1 and §C2.4.1
(the pfd mask and the exclusion), §D4 (the time step), §D5.1.4.1 and
§D5.1.5 (reading a mask), §D6.4.4 (the α avoidance angle and its sign),
§D6.4.5 (the satellite az/el frame) — and its mask XML schema; Rec. ITU-R
S.1428 (earth-station antenna); Rec. ITU-R S.672 (GSO satellite antenna);
Rec. ITU-R S.1325 and its draft revision (the co-frequency beam capacity).
