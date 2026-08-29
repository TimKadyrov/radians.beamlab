# radians.beamlab — non-GSO multi-beam composer

A small C# / WPF tool for studying the composite antenna pattern and
downlink PFD of a non-GSO satellite as individual beams are switched on or
off (e.g. to avoid transmissions toward a protected region). The single-beam
pattern is the **Taylor circular illumination function** of Recommendation
**ITU-R S.1528-1 §1.4** (2025 revision), which gives a realistic side-lobe
shape rather than an envelope.

Two tabs:

- **Composite gain map** — the original composer: place a beam set, switch
  beams on/off, and view the composite gain heatmap on a world map.
- **PFD Mask Generator** — build a downlink PFD mask in the ITU-R S.1503-4
  coordinate systems (satellite-frame az/el §D6.4.5, or signed α / ΔLongitude
  §D6.4.4), with per-beam power control, frequency-reuse aggregation, a
  GSO-arc exclusion zone, and export to S.1503-4 mask **XML** and tabular
  **CSV**.

See the **[user guide](docs/user-guide.md)** for a full walk-through of both
tabs and every control.

Layout:

- `src/radians.beamlab.Core/` — class library
  - `SinglePatterns.cs` — `Rec1528_1p4` (S.1528-1 §1.4 Taylor) per-beam pattern
  - `BesselJ1.cs` — Abramowitz & Stegun polynomial approximation of J₁(x)
  - `Beam.cs` / `BeamComposer.cs` — beam abstraction + incoherent power-sum composer
  - `GeoMath.cs` — spherical-Earth geometry, orbit altitude, beam-to-ground projection
- `src/radians.beamlab.app/` — WPF tool
- `countries.json` — Natural Earth 10m admin_0 boundaries (auto-loaded if present)

## Math

### Per-beam pattern: S.1528-1 §1.4 (Taylor circular illumination)

```
F(u) = [2·J₁(π·u) / (π·u)] · ∏_{n=1..n̄−1} (1 − u²/u_n²) / (1 − u²/μ_n²)
G(θ) = G_max + 20·log₁₀|F(u)|
```

with

```
μ_n  = j_{1,n}/π                           (un-modified Bessel zeros)
A    = arccosh(10^(SLR/20)) / π
σ    = μ_{n̄} / sqrt(A² + (n̄ − ½)²)
u_n  = σ · sqrt(A² + (n − ½)²)             (Taylor replacement zeros)
u    = u_edge · sin(θ) / sin(θ_b)          (mapping off-axis angle to u)
```

`u_edge` is solved numerically so that |F|² = ½ at θ = θ_b. SLR (side-lobe
ratio, dB) and n̄ (number of secondary lobes) are user inputs. Annex 2 of
S.1528-1 uses SLR = 20 dB and n̄ = 4, giving A = 0.95277 and σ = 1.1692 —
which the implementation reproduces exactly.

The pattern is floored at LF (far-out side-lobe / null floor, dBi).

### Composite (multi-beam)

```
G_tot(d̂) = 10·log₁₀( Σ_k w_k · 10^(G_k(d̂)/10) )
```

`d̂` is a test unit vector (in ECEF), `w_k ∈ [0, 1]` is the per-beam on/off
weight, and `G_k` is each beam's §1.4 pattern referenced to that beam's
own boresight. Switching off a beam means setting `w_k = 0`; the beam
simply drops out of the sum.

This is incoherent power summation — correct when each beam carries an
independent signal (the usual non-GSO multi-beam payload). For a coherent
phased array driven from a single feed network it is **not** the right model.

### Beam-to-ground

Each beam's boresight starts as a unit vector in the satellite local NED
frame (built from sub-satellite lat/lon). It is rotated into ECEF and
intersected with the spherical Earth (radius 6 371 km) to find the ground
footprint centre. The horizon (line-of-sight) cap on Earth has half-angle
`arccos(R / (R + h))` from the sub-point.

## Tabs

### Home

The front door: one card per function with a description and an Open
button, links to the local user guide and the parameter cards, and the
version. The functional tabs keep their state while you switch.

### Composite gain map

The original composer. Inputs (left panel): orbit altitude and sub-satellite
lat/lon; centre frequency; the per-beam pattern (six S.1528 models — §1.4
Taylor circular/elliptical, §1.2 envelope, §1.3 LEO/MEO/HEO); the beam layout
(auto hex tessellation or concentric rings, driven by a served
min-elevation and an adjacent-beam crossover level); heatmap/probe mode; a
country- or bounding-box exclusion; and an optional PFD-adjuster that trims
adjacent-beam gains to hold an aggregate PFD limit over a chosen country.

Map (right panel): equirectangular world map with coastlines from
`countries.json`, the sub-satellite point and horizon disc, and each beam's
ground footprint as a coloured marker (green = on, red = off) with an optional
3-dB ring. **Click a beam** to toggle it, **click elsewhere** to probe the
composite gain; **left-drag** pans, **right-drag** moves the satellite, wheel
zooms.

### PFD Mask Generator

Builds a downlink PFD mask for the current beam set. Left panel selects the
**mask type** (az/el §D6.4.5 or α/ΔLongitude §D6.4.4), antenna and cell
parameters, **beam gating** (served min-elevation and GSO exclusion), the
per-beam **power mode** (constant power, or constant-boresight-PFD spreading-
loss compensation), the **aggregation** (all-co-frequency power sum, or
N-colour frequency-reuse worst-colour sum), and the **advanced α-ring
exclusion** dialog (concentric α rings, each switching beams off or
attenuating them). The right side shows a small footprint map, the mask
heatmap, and a profile slice with ES-elevation and α guides. **Generate mask
XML…** exports an S.1503-4 mask (XML and/or CSV) over a latitude table capped
by the orbital inclination, enveloping the ascending/descending pass headings
of the body-stabilised layout.

### Mask Viewer

Opens an existing S.1503-4 mask XML (either mask type) and displays its
latitude blocks with the same heatmap and profile plots. Values are read the
way EPFD tools read a mask — §D5.1.5 bilinear interpolation, clamped at the
table edges — with fully unreachable regions blank.

### Orbit Design

Prototypes the SNS v10 orbit parameters. Enter a target altitude,
inclination and eccentricity: the tab lists the repeating-ground-track
candidates near it (k orbits per m nodal days, the exact altitude for each,
the cycle as the ready `rpt_prd_dd/hh/mm/ss` fields, the equator spacing and
the largest `keep_rnge` that keeps swept tracks distinct), previews the SNS
fields for all three station-keeping cases — including the Case-1 artificial
precession numbers and the spacing the examination run actually measures —
and draws one full propagated cycle of the selected candidate over the
coastline map, with start/end markers that coincide when the track closes.
Three sub-tabs share the state: the repeat solver, the station-keeping
cases, and a Walker-shell constellation panel with live SNS orbit/phase
tables; designs save and reload as `*.orbitdesign.json` files (including
the selected candidate). Tooltips share their text with the parameter
cards.

### SNS v10 builder (window)

Assembles complete SNS v10 datasets (SRS + Masks databases) from separate
elements — orbit-design files as shells, mask XMLs, operating-parameter
sets and the scenario frequency ranges — cloned from donor schemas and
written through the verified writer.

Full details and the maths for every control are in the
**[user guide](docs/user-guide.md)**.

## Auto-mode hex layout

When the **Auto hex tessellation** checkbox is on, beam centres are placed on
a hex lattice — UV-plane (3GPP NTN TR 38.821) for circular patterns,
ground tangent plane for the §1.4 elliptical pattern. See
[docs/hex-layout.md](docs/hex-layout.md) for the full math and trade-offs.

## Build / run

```
dotnet build radians.beamlab.slnx
dotnet run --project src/radians.beamlab.app
```

Targets `net8.0` (Core) and `net8.0-windows` (App, WPF). Tested with the
.NET 10 SDK.

### Verification harness

```
dotnet run --project tests/radians.beamlab.checks
```

Headless business-logic checks against independent invariants — the α
solver vs brute force, frame round-trips, reuse colourings, aggregation
ordering, export round-trips and envelope binning, peak retention on coarse
grids, Taylor-kernel bounds, array-steered beams, and the mask-viewer's
§D5.1.5 reads. Prints PASS/FAIL per check; exit code 0 iff all pass. A few
checks use a local ITU reference filing and skip cleanly when it is absent.

`countries.json` is searched in the working directory, the application
binary directory, and the project root — drop a Natural Earth GeoJSON
there and restart.

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) for the
full text, or <http://www.apache.org/licenses/LICENSE-2.0>.
