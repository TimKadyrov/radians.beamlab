# radians.beamlab — non-GSO multi-beam composer

A small C# tool for studying the composite antenna pattern of a non-GSO
satellite as individual beams are switched on or off (e.g. to avoid
transmissions toward a GSO-protection region). The single-beam pattern is
the **Taylor circular illumination function** of Recommendation
**ITU-R S.1528-1 §1.4** (2025 revision), which gives a realistic
side-lobe shape rather than an envelope.

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

## The WPF tool

Inputs (left panel):

- **Orbit**: a single altitude (km) above the spherical Earth.
- **Sub-satellite point**: latitude, longitude.
- **RF / antenna**: frequency (GHz), antenna diameter D (m), aperture efficiency η. The panel computes λ, D/λ, a recommended half-3-dB beamwidth (≈ 35.2°·λ/D) and a recommended peak gain (η·(πD/λ)²). "Fill θ_b" / "Fill G_m" stamp those values into the pattern inputs.
- **Beam pattern (S.1528-1 §1.4 Taylor)**: peak gain G_m, half-3-dB beamwidth θ_b, side-lobe ratio SLR, secondary-lobe count n̄ (2..6), null floor LF.
- **Beam layout**: number of rings (0..8) and the **outer ring off-nadir / FOV edge** (deg). The per-ring step is FOV edge / rings, so ring `r` sits at off-nadir `r · step` and contains `6r` beams equally spaced in azimuth. A "Fit FOV to horizon" button sets the FOV edge to ~0.97 × the geometric horizon-tilt at the current altitude, so the beam set spans the visible footprint.
- **GSO-region exclusion**: a (lat, lon) bounding box. Every beam whose footprint centre falls inside the box gets `w_k = 0`.
- **Display**: heatmap on/off, restrict heatmap to the visible disc, draw 3-dB footprint rings, heatmap floor (dBi).

Map (right panel):

- Equirectangular projection.
- Coastlines / country boundaries are loaded from `countries.json` (Natural Earth GeoJSON FeatureCollection of `Polygon` / `MultiPolygon` features) at startup. Falls back to a built-in coarse outline if absent. The status bar reports which source was used.
- Sub-satellite point marked, horizon (visible disc) outlined in amber.
- Each beam's ground footprint drawn as a coloured marker — green=on, red=off — with optional 3-dB ring.
- **Click a beam marker** to toggle its on/off state.
- **Click anywhere else** on the map to probe the composite gain at that ground point.
- The heatmap colours every visible ground pixel by composite gain on a fixed ramp from `floor` (dark) to `G_m` (white).

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

`countries.json` is searched in the working directory, the application
binary directory, and the project root — drop a Natural Earth GeoJSON
there and restart.

## Caveats / limitations

- **Spherical Earth.** No WGS-84, no oblateness; sufficient for first-order
  pointing studies but do not lift these numbers into a coordination filing.
- **Stationary "snapshot".** The model uses one (apogee, perigee, true
  anomaly) sample to set altitude, plus an independently chosen sub-satellite
  lat/lon. It does not propagate the orbit; the ground track is whatever you
  set by hand.
- **Circular beams only.** §1.4 has L_r ≠ L_t (elliptical) provisions; this
  implementation uses L_r = L_t and parameterises directly via θ_b.
- **Power-sum composition.** Correct for incoherent multi-beam payloads;
  incorrect for coherent phased arrays.
- **Near nulls.** F(u) has nulls at u = μ_n where the kernel and one
  denominator factor share a simple zero; the implementation floors |denom|
  with a small ε so the value is approximated rather than NaN, which is
  fine in conjunction with the LF floor.

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) for the
full text, or <http://www.apache.org/licenses/LICENSE-2.0>.
