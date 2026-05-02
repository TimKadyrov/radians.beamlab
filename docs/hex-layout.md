# Hex tessellation auto-mode

The auto-mode in `radians.beamlab` places beam centres on a hex lattice in either
the **satellite UV-plane** (for circular antenna patterns) or the **ground tangent
plane at the sub-satellite point** (for the §1.4 elliptical pattern). Both
algorithms target full hex coverage; they differ in which space is uniform.

This note documents the math, the parameter trade-offs, and the small "boundary
snap" that makes the outermost ring sit on the user's min-elevation circle.

## Pattern → layout space

| Pattern | Layout space | Lattice constant | What's uniform |
|---|---|---|---|
| §1.4 circular Taylor | UV-plane | `ABS_uv = √3·sin(θ_b·√(−L/3))` | beam shape in satellite frame |
| §1.2 envelope | UV-plane | `ABS_uv = √3·sin(θ_b·√(−L/3))` | beam shape in satellite frame |
| §1.3 LEO/MEO/HEO | UV-plane | `ABS_uv = √3·sin(θ_b·√(−L/3))` | beam shape in satellite frame |
| §1.4 elliptical | Ground tangent plane | `ABS_g = √3·R_cell` km | ground cell radius |

`θ_b` is the half-3 dB beamwidth (deg), `L` is `CrossoverDb` (negative dB), and
`R_cell` is the user-specified ground-cell radius in km.

The crossover factor `√(−L/3)` lets the user shift between conventions:

| L | `θ_b·√(−L/3)` | UV spacing in θ-units | comment |
|---|---|---|---|
| −3 dB | `θ_b` | `√3·sin(θ_b)` | canonical 3GPP NTN (cells touching at −3 dB) |
| −2 dB | `θ_b·0.816` | tighter | more overlap, deeper ground coverage |
| −1 dB | `θ_b·0.577` | tightest | heavy overlap, no centroid gaps |

## UV-plane lattice (circular patterns)

3GPP NTN TR 38.821 places beam boresight directions on a hex lattice in the
**UV-plane** — the plane perpendicular to nadir at the satellite. UV
coordinates are direction cosines:

```
u = sin(θ_off) · cos(φ_az)
v = sin(θ_off) · sin(φ_az)
```

where `(θ_off, φ_az)` is off-nadir + azimuth in the satellite frame.

### Hex basis

Triangular lattice with basis vectors

```
e₁ = (s, 0)
e₂ = (s/2, s·√3/2)
```

Lattice points: `(u, v) = i·e₁ + j·e₂` for integer `(i, j)`.

### Adjacent-beam spacing (ABS)

3GPP TR 38.821 specifies `ABS = √3·sin(θ_3dB/2)`, where `θ_3dB` is the *full*
3 dB beamwidth. Our `θ_b` is the half-3 dB, so the equivalent is

```
ABS = √3·sin(θ_b)        (cells touching at −3 dB; centroids just covered)
```

Generalising to crossover level `L`:

```
ABS = √3·sin(θ_b · √(−L/3))
```

### Visibility filter

Each lattice point is kept iff `u² + v² ≤ sin²(θ_outer)`, where
`θ_outer = arcsin(R · cos(MinElev) / (R + h))` is the off-nadir angle at the
user's min-elevation boundary.

### Boundary snap

A naive lattice with `ABS = √3·sin(θ_b·√(−L/3))` rarely places its outermost
ring exactly on `sin(θ_outer)` — the discrete grid usually leaves a half-step
gap to the visible-disc edge. We rescale:

```
N      = round( sin(θ_outer) / ABS )
ABS'   = sin(θ_outer) / N
```

so the lattice's outermost hex-axis ring sits *exactly* on the min-elevation
boundary. `N` is the number of hex rings.

### Hex-vs-circle boundary mismatch

The hex lattice has six-fold symmetry; the visible-disc boundary is circular.
After the snap:

- Along the 6 **hex-axis** directions the outermost lattice point lies at
  `r = N·ABS' = sin(θ_outer)` — exactly on the boundary.
- Along the 6 **mid-edge** directions the closest outermost lattice point lies
  at `r = √(N²−N+1)·ABS' ≈ 0.944·sin(θ_outer)` for `N=8` — about a half-step
  short.

This produces a faint **6-arm star** in the ground projection: the outer
beams reach min-elevation only in 6 directions, leaving slight crescent gaps
between arms. This is intrinsic to a hex lattice meeting a circular boundary
and cannot be removed without breaking the regular tessellation. Pulling
`CrossoverDb` toward 0 dB densifies the lattice and reduces the relative gap;
the absolute gap stays at ~half-a-step in mid-edge directions.

### Ground projection of UV lattice

The mapping `(u, v) → ground` is non-linear:

```
sin(θ_off) = √(u² + v²)
ground distance from sub-sat = R · (arcsin((R+h)/R · sin(θ_off)) − θ_off)
```

A uniform UV lattice therefore **clusters near sub-sat** and **thins toward
the horizon**: most lattice cells project to the inner part of the visible
disc. This is the geometric trade-off — circular antennas give uniform
satellite-frame coverage at the cost of non-uniform ground spacing.

## Ground-plane lattice (elliptical pattern)

For the §1.4 elliptical pattern each beam's `(α, β)` are derived per-beam from
`R_cell` and slant geometry, so every ground footprint is approximately a
circle of radius `R_cell`. With per-beam adaptation available, the natural
lattice is in **ground tangent-plane Cartesian km**:

```
e₁ = (s, 0)            s = √3·R_cell       (km, full hex coverage)
e₂ = (s/2, s·√3/2)
```

For each lattice point `(x, y)`:

1. Spherical-Earth great-circle destination from the sub-satellite point:
   ```
   c = √(x²+y²) / R                     (central angle, rad)
   brg = atan2(x, y)                    (bearing from north, rad)
   sin(lat) = sin(lat₀)·cos(c) + cos(lat₀)·sin(c)·cos(brg)
   lon = lon₀ + atan2(sin(brg)·sin(c)·cos(lat₀), cos(c) − sin(lat₀)·sin(lat))
   ```
2. Compute look direction from satellite to ground point.
3. Filter by `elevation ≥ MinElev`.
4. Build the per-beam pattern with `BuildPatternFor(Gm, off_nadir)`.

Per-beam `α(off)`, `β(off)` for circular ground cells:

```
ε     = arccos((R+h)/R · sin(off))            (elevation at ground point)
D     = (R+h)·cos(off) − √(R² − (R+h)²·sin²(off))    (slant range, km)
β     = atan(R_cell / D)                       (transverse half-angle, deg)
α     = atan(R_cell · sin(ε) / D)              (radial half-angle, deg)
```

`α` is foreshortened by `sin(ε)` because the radial axis on ground tilts
relative to the slant range. `α/β = sin(ε)`, so cells at low elevation are
narrow-radial / wide-transverse in the satellite frame to project a circular
ground cell.

`L_r`, `L_t` (effective aperture sizes, m) follow Annex 2 Table 2:

```
K = 0.51   (3 dB roll-off)
  | 0.64   (5 dB roll-off, linearly interpolated)
  | 0.74   (7 dB roll-off)
L_r = K · λ / sin(α)         L_t = K · λ / sin(β)
```

## When to use each

- **Circular pattern + auto** = UV-plane. Use when you care about a
  3GPP-NTN-style snapshot and accept that ground spacing will be non-uniform.
- **Elliptical §1.4 + auto** = Ground-plane. Use when you want uniform
  R_cell-radius ground cells and the antenna can be specified per-beam.
- **Manual mode (concentric rings)** = always available, hand-tuned via
  `θ_b` and `CrossoverDb`. Closer to how real LEO operators specify beam
  layouts in practice.

## References

1. ITU-R Recommendation S.1528-1 (2025), Annex 2 — elliptical Taylor pattern
   in `(L_r, L_t)` form.
2. 3GPP TR 38.821 — Solutions for NR to support non-terrestrial networks
   (NTN). Annex on UV-plane beam-grid layout.
3. Pachler de la Osa et al., "Static beam placement and frequency plan
   algorithms for LEO constellations", IJSCN 2021. K-means / heuristic beam
   placement variants.
4. arXiv:2408.08090 — "Pragmatic Earth-Fixed Beam Management for 3GPP NTN
   Common Signaling in LEO Satellites". Detailed UV-plane mapping math.
