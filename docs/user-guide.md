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

The two tabs are independent — each keeps its own satellite, antenna and
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

## Tab 1 — Composite gain map

### Your first map in five steps

1. Set **Altitude** and the **sub-satellite point** — the amber circle on the
   map is everything the satellite can see.
2. Pick a **beam pattern**. The default (§1.4 Taylor circular) is a realistic
   single-beam shape; set its peak gain **Gm** and beamwidth **θb** (or click
   *Fill θb from Gm* to derive one from the other).
3. Tick **Auto hex tessellation** to fill the coverage with a honeycomb of
   beams, and set **Min user-elevation served** — beams stop where a ground
   user would see the satellite lower than this.
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
- **Co-channel sum, K-colour reuse** — assumes a frequency plan: the
  honeycomb is coloured with K = 3, 4 or 7 colours so neighbouring beams
  never share a channel, and only same-colour beams add up. Pick this to see
  the realistic per-channel PFD. In this mode the top map paints each beam
  with its colour so you can see the plan.

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

### Exporting a mask (XML / CSV)

**Generate mask XML…** computes the mask not just at the current latitude
but over the whole **latitude table** an orbit sweeps, and writes it to a
file:

1. **Inclination** — enter your orbit's inclination; the dialog caps the
   latitude range at the maximum sub-satellite latitude that inclination can
   reach (e.g. 53° inclination → ±53°). You can narrow the range or change
   the latitude step.
2. **Resolution** — the grid steps for the two mask axes in the output file.
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
[hex-layout.md](hex-layout.md). ITU references: single-beam patterns from
Rec. ITU-R S.1528; mask coordinates, α angle and the XML schema from
Rec. ITU-R S.1503-4 (§D6.4.4, §D6.4.5).
