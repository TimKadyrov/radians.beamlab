# radians.beamlab — user guide

This guide walks through both tabs of the tool and every control. For the
theory of the single-beam pattern and the hex layout see the
[README](../README.md) and [hex-layout.md](hex-layout.md).

The app opens with two tabs:

- **Composite gain map** — place a multi-beam set and view its composite
  antenna gain on a world map.
- **PFD Mask Generator** — compute the downlink power-flux-density mask of
  that beam set in the ITU-R S.1503-4 coordinate systems and export it.

The two tabs are **independent**: each keeps its own orbit, antenna and
display state, so editing one never disturbs the other.

---

## Common concepts

- **Spherical Earth**, radius 6371 km. All geometry is line-of-sight on the
  sphere; there is no atmosphere or terrain.
- **Beams.** The satellite carries a set of beams, each an S.1528-1 §1.4
  Taylor pattern pointed at a ground cell. A per-beam **weight** in [0, 1]
  scales its contribution: 1 = on, 0 = off, in between = attenuated.
- **Composite** gain / PFD is the incoherent power sum of the active beams —
  correct when each beam carries an independent signal (the usual non-GSO
  multi-beam payload).

---

## Tab 1 — Composite gain map

Left panel, top to bottom:

- **Orbit** — altitude (km). The read-out shows the horizon off-nadir angle.
- **Sub-satellite point** — latitude, longitude.
- **RF / antenna** — centre frequency (GHz); the read-out gives λ and a
  gain-derived beamwidth. *Fill θb from Gm* stamps that beamwidth in.
- **Beam pattern** — one of six S.1528 models: §1.4 Taylor circular or
  elliptical, §1.2 envelope, §1.3 LEO/MEO/HEO. Peak gain Gm, half-3-dB
  beamwidth θb, side-lobe ratio and n̄ (Taylor), null floor LF, and the
  elliptical cell parameters. *Auto hex tessellation* places beams on a hex
  lattice (3GPP NTN UV-plane for circular patterns, ground tangent plane for
  the elliptical one); otherwise beams are laid out as concentric rings.
- **Beam layout** — the served **min user-elevation** (sets how far out the
  outer ring reaches) and the **adjacent-beam crossover level** (sets the
  centre-to-centre spacing).
- **Heatmap / probe** — power-sum vs single-dominant-beam gain.
- **Region exclusion** — switch off every beam whose footprint centre falls
  inside a selected country or a lat/lon box.
- **PFD adjustment** — reduce adjacent-beam gains (or switch beams off) so the
  aggregate PFD over the selected country stays under a limit mask.
- **Display** — heatmap on/off, footprint rings, heatmap floor.

Map (right): equirectangular world map with coastlines (`countries.json`),
the sub-satellite marker and amber horizon disc, and each beam's footprint as
a green (on) / red (off) marker with an optional 3-dB ring.

- **Click a beam** → toggle it on/off.
- **Click elsewhere** → probe composite gain / PFD at that ground point.
- **Left-drag** pans, **right-drag** moves the satellite, **wheel** zooms.

---

## Tab 2 — PFD Mask Generator

This tab is locked to the S.1528-1 §1.4 elliptical Taylor pattern on a 3GPP
hex lattice, and computes a **PFD mask**: PFD as a function of the S.1503-4
mask coordinates. It never mutates Tab 1.

### Mask type

Radio buttons pick the coordinate system (both are satellite-referenced per
S.1503-4):

- **Azimuth / Elevation (§D6.4.5).** X = azimuth from nadir toward East
  (±90°), Y = elevation out of the East-Down plane toward North (±90°). Each
  heatmap pixel is one look direction from the satellite; its colour is the
  aggregate PFD at that direction's ground intersection.
- **α / ΔLongitude (§D6.4.4).** X = ΔLongitude (NGSO sub-satellite longitude
  minus the longitude of the α-minimising GSO arc point, ±180°), Y = signed
  α, the avoidance angle to the nearest visible GSO arc point (±90°, sign per
  §D6.4.4.1). The visible disc is swept and each ground point's PFD is
  max-binned into its (α, ΔL) cell — the ITU mask is the maximum PFD over all
  ground points sharing (α, ΔL). α is computed with the analytic §D6.4.4.4
  method (quartic + Newton).

The whole visible disc is sampled, **including ground below the served
min-elevation**, because the active beams' side lobes still radiate there.
Only look directions past the horizon are blank.

### Antenna, cell, gating

- **Frequency, Gm, cell radius, edge roll-off, SLR, n̄, LF** — the elliptical
  Taylor cell parameters; the read-out shows the S.1503-4 Table 8 GSO minimum
  elevation for the frequency.
- **Minimum user elevation ε_min** — gates which beams are *on* (the outer
  hex extent) and is drawn as a guide on the profile. PFD is still evaluated
  over the whole disc.
- **GSO exclusion α_excl** — basic mode: beams whose footprint |α| is below
  this are switched off. (Superseded by the advanced dialog when enabled.)

### Power mode

- **Constant power per beam** — every beam transmits the same power; the input
  is that power (dBW in the reference bandwidth).
- **Constant boresight PFD** — each beam's power is raised by
  20·log₁₀(boresight slant / altitude) so its *boresight* PFD is the same as a
  nadir beam's, cancelling spreading loss. The input is then the
  nadir-reference power. This flattens the per-beam served PFD; the aggregate
  still ripples with beam overlap.

### Aggregation

How per-beam PFD contributions combine into the mask:

- **Power sum of all beams** — every beam co-frequency (reuse factor 1). The
  conservative upper bound and the safest regulatory posture.
- **Co-channel sum, K-colour reuse** — beams are coloured on the hex lattice
  (K = 3, 4 or 7 — no two adjacent cells share a colour), each colour is
  power-summed separately, and the worst colour is taken per pixel. The
  realistic view for a payload with a frequency plan; in this mode the geo
  map paints on beams by their reuse colour.

Pointwise, co-channel sits between power-sum (higher) and a single beam
(lower).

### Advanced exclusion (α rings)

Tick **Advanced exclusion** and open **Edit α rings…** for concentric α
rings from 0° outward. Each ring has an outer α edge and an action:

- **Off** — beams whose footprint |α| falls in the ring are switched off.
- **Attenuate N dB** — those beams' power is reduced by N dB.

A beam takes the innermost ring it is under; beyond the outermost ring it is
unaffected. On the heatmap, off rings tint red and attenuate rings tint
orange; the profile marks each ring's boundary. (Basic single-threshold mode
is the special case of one off ring at α_excl.)

### Plots (right side)

- **Geo map** (top) — coastlines, horizon, sub-satellite, and beam footprints
  coloured green/red (or by reuse colour in co-channel mode). Pan with
  left-drag, zoom with the wheel.
- **Mask heatmap** (bottom-left) — PFD over the (X, Y) mask grid on an
  auto-scaled red→green ramp (red = lowest, green = highest). A dashed cursor
  marks the profile cut; the α exclusion is tinted when *Mark α* is on.
- **Profile** (bottom-right) — a single slice through the heatmap at the
  slider-selected cut: PFD vs elevation at an azimuth (Az/El), or PFD vs
  ΔLongitude at a fixed α, i.e. one mask-table row (α/ΔL). Guides show ES
  elevation = 0° (horizon, amber) and = ε_min (cyan), and the α-exclusion
  boundaries (red/orange).
- **Mask sampling step** sets the grid/compute resolution.

### Generate mask XML / CSV

**Generate mask XML…** opens the export dialog. It exports the currently
selected mask type over a **latitude table**:

- **Inclination** sets the reachable latitude cap, max |lat| = 90° − |90° − i|
  (= i for prograde i ≤ 90°). Latitude min/max are editable within that cap,
  plus a latitude step.
- **Output resolution** — the b (α or azimuth) and c (ΔLongitude or
  elevation) axis steps. The compute grid uses the tab's mask step; output
  nodes are sampled from it, and unreachable nodes are written as −1000.
- **Metadata** — satellite name, NTC id, mask id, frequency band, refBW.
- **Format** — XML (S.1503-4 schema), CSV (a flat table: one row per
  latitude × b, one column per c value), or both.

For each latitude the beam set + exclusion are recomputed and the mask field
re-sampled, so the table reflects the changing GSO/ES geometry with latitude.
The run is off the UI thread with a progress bar and Cancel; the live view is
untouched. The XML matches the reference `maskdata` reader
(`satellite_system → pfd_mask → by_a[latitude] → by_b[α|azimuth] →
pfd[ΔLong|elevation]`) and has been round-trip-verified against it.

---

## Build / run

```
dotnet build radians.beamlab.slnx
dotnet run --project src/radians.beamlab.app
```

Targets `net8.0` (Core) and `net8.0-windows` (App, WPF).
