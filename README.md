# radians.beamlab — non-GSO multi-beam composer

A small C# / WPF tool for studying the composite antenna pattern and
downlink PFD of a non-GSO satellite as individual beams are switched on or
off (e.g. to avoid transmissions toward a protected region). The single-beam
pattern is one of six ITU-R S.1528 models. The default is the **Taylor
illumination function** of Recommendation **ITU-R S.1528-1 §1.4** (2025
revision), circular or elliptical, which gives a realistic side-lobe shape
rather than an envelope; the S.1528-0 §1.2 envelope and the §1.3 LEO, MEO
and HEO patterns are the alternatives.

Functions (each a tab or tool window, launched from the Home page):

- **Composite gain map** — the original composer: place a beam set, switch
  beams on/off, and view the composite gain heatmap on a world map.
- **PFD Mask Generator** — build a downlink PFD mask in the ITU-R S.1503-4
  coordinate systems (satellite-frame az/el §D6.4.5, or signed α / ΔLongitude
  §D6.4.4), with per-beam power control, frequency-reuse aggregation, a
  GSO-arc exclusion zone, and export to S.1503-4 mask **XML** and tabular
  **CSV**.
- **Mask Viewer** — read any S.1503-4 mask XML back the way EPFD tools do.
- **Orbit Design** — prototype the SNS v10 orbit declaration, shell by
  shell, from repeat solving to a saved multi-shell design document.
- **SNS v10 builder** — assemble complete SRS + Masks databases from
  designs, mask XMLs, operating-parameter sets and earth stations.
- **Operation profile** — the real system's operating characteristics
  (payload, coverage, scheduling, activity) as one saved element feeding
  the runner, the deriver and the compliance loop; the downlink footprint
  source is an explicit choice — live beam composition, or a declared
  PFD mask XML that simulations read the examination's way (§D5.1.4.1).
- **Operating parameters designer** — author the declared operating
  constraints (the R set) directly, or fill them from the compliance loop's
  own derivation, which measures the system once on a saturated probe;
  export as R-set XML.
- **Simulation runner** — run epfd(down)/(is)/(up) from an orbit design
  document and an operation profile (both required) and write the CDFs;
  an R-set file optionally swaps in the declared gates.
- **Compliance loop** — derive the declaration from a saturated probe,
  sweep epfd(down) victims across a latitude grid, verdict against the
  entered limit under the limit-curve rule (every tabulated point, and the
  log-linear curve between them), then examine that
  same truth against the declaration (**Run loop**; **Run sweep** makes a
  single pass, and an optional R set examines a filing); walks the exclusion angle to the
  smallest compliant value, written back into the operation profile. The
  truth runs on a preset 1 s step; a declared-mask examination runs on
  that step or on the fine and coarse steps of S.1503-4 §D4.

See the **[user guide](docs/user-guide.md)** for a full walk-through of
every function and control.

For what the tool is doing and why — the inputs, the three runs, the two
loops and the rules the construction obeys — see
**[how the projection is built](docs/construction.html)**.

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
intersected with the spherical Earth (radius 6 378.145 km, the S.1503-4 value the propagator also uses) to find the ground
footprint centre. The horizon (line-of-sight) cap on Earth has half-angle
`arccos(R / (R + h))` from the sub-point.

## Tabs

### Home

The front door: one card per function with a description and an Open
button, the EPFD pipeline first in flow order and the other tools after
it; links to the local documentation (the user guide, the parameter cards,
the orbit case guide and the repeat solver guide), each enabled when its
file is found; and the version. The functional tabs keep their state while
you switch.

### Composite gain map

The original composer. Inputs (left panel): orbit altitude and sub-satellite
lat/lon; centre frequency; the per-beam pattern (six S.1528 models — §1.4
Taylor circular/elliptical, §1.2 envelope, §1.3 LEO/MEO/HEO); the beam layout
(auto hex tessellation or concentric rings, driven by a served
min-elevation and an adjacent-beam crossover level); heatmap/probe mode; a
country- or bounding-box exclusion; and an optional PFD-adjuster that trims
adjacent-beam gains to hold a PFD limit over a chosen country, the PFD read
in the active heatmap/probe mode (single-beam max by default, the
adjacent-beam power sum when that mode is chosen).

Map (right panel): equirectangular world map with coastlines from
`countries.json`, the sub-satellite point and horizon disc, and each beam's
ground footprint as a coloured marker (green = on, amber = on with its peak
gain reduced by the PFD adjustment, red = off) with an optional 3-dB ring.
**Click a beam** to toggle it, **click elsewhere** to probe the
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

Prototypes the SNS v10 orbit declaration, shell by shell — one document
holds every shell of the constellation. Four sub-tabs share the state:
**Start here** (the shells list — named, ordered, add/duplicate/remove —
and the target orbit every case starts from), the **Repeat solver** (mode
first: *adjusting altitude*, the default, files the selected repeat's
exact closing altitude, where the filed orbit repeats by itself; *fixed
altitude* declares a repeat pair at your own altitude, auto-filled with
the nearest; the EPFD calculation flies the filed orbit on its own J2
rates plus the `keep_rnge` sweep (S.1503-4 eq (49)), so at a fixed
altitude it flies that orbit's drift every cycle; then the `keep_rnge`
tolerance, the candidate grid with both cycle readings, and one propagated
declared cycle over the coastline map — closure 0 at the exact altitude, the
free-flight drift at the target, with a whole-constellation overlay), the
**Station-keeping cases** (the three S.1503-4 cases, decided as in its
Figure 52, previewed as ready SNS fields — Case 1 free drift, purely
informational; Case 2 station-kept repeating; Case 3 station keeping with
a supplied precession rate, the Case 2 repeat and `keep_rnge` plus the
rate, by default the one that closes the repeat at the target altitude,
filed as its magnitude in degrees/day with the direction the inclination
implies, west below 90° and east above — a rate turning against its
inclination is refused at save and by the SNS builder, and a typed rate
that misses the repeat by more than `keep_rnge` a cycle at save; for an
elliptical orbit the panel notes that Case 3 holds the perigee fixed)
and the **Constellation** (Walker shell to live SNS orbit/phase tables,
selected shell or all shells combined, with the constellation repeat
period P_repeat and one-click `rpt_prd` harmonization across the Case 2
and Case 3 shells).
Designs save and reload as `*.orbitdesign.json` documents (every shell,
selected candidates included); tooltips share their text with the
parameter cards.

### SNS v10 builder (window)

Assembles complete SNS v10 datasets (SRS + Masks databases) from separate
elements — orbit-design documents as shells, mask XMLs with per-row link
scope (whole constellation, per plane, per satellite, or a specific earth
station), declared earth stations (`e_as_stn`), operating-parameter sets
and the scenario frequency ranges — cloned from donor schemas and written
through the verified writer.

### Operating parameters designer (window)

Authors one non-GSO operating-parameter set (the `f_mask R` element):
the header quantities and the four per-latitude arrays, round-tripped
through `*.opparams.json` and exported as byte-convention R-set XML —
entered directly, or **filled from a compliance-loop run**: the loop flies
the real system on a saturated probe, measures every granted link, and
envelopes the measurements into the declared set the way the pfd/e.i.r.p.
masks envelope the payload. The designer reads that set — it does not
simulate a second opinion of the same system.

### Simulation runner (window)

Runs the epfd(down) / epfd(is) / epfd(up) simulation from an orbit
design document and an operation profile — the space segment and the
operated system, both required; an optional R-set file swaps the
scheduler's gates for the declared constraints. The time step is preset
to 1 s, the truth's step. **▶ play**
and **⏩ accelerated play** share one continuous timeline — ▶ animates
the world map (satellites, candidate and active links, live counts),
⏩ advances the same clock without updating the map —
switchable mid-run; **Write CDFs** executes the statistics run with no
UI updates, writes the three CDF CSVs in S.1503-4 D7.1.2 bins and
opens the CDF viewer over the written curves.

### Operation profile (window)

Edits the operation profile (`*.opprofile.json`): the real system's
operating characteristics, read by the simulation runner, the R-set
deriver and the compliance loop. A profile name and a **Direction**
selector (Downlink / Uplink) head the window; the selector shows that
direction's groups and keeps the shared ones, and the file always carries
both sides. Downlink: the projection switch (footprint from the live beam
composition or from a declared PFD mask XML), radiated power and spectrum
(frequency, per-beam power density, power mode, aggregation and reuse
cluster, reference bandwidth, illumination duty cycle, co-frequency beam
capacity), beam shape and layout (the six S.1528 models and the layout
inputs) and a composition preview. Uplink: the earth-station transmit
chain (frequency, power ceiling, power-control reference elevation, dish).
Shared: service and coverage; traffic and scheduling (the tracking
strategy — highest elevation, max GSO separation, random among feasible,
hold until forced — the Nco caps, hold, demand, activity, operational
fraction, exclusion angle and the selected direction's separation angles);
and per-latitude rows for minimum elevation, Nco and exclusion. Empty optional fields keep the scene defaults. **Save
profile…** and **Load profile…** write and read the file.

### Compliance loop (window)

Verdicts epfd(down) for an orbit design document and an operation profile
over a victim sweep: ES latitude from / to / step at a chosen ES longitude,
the wanted GSO satellite at an offset, the ES dish, and the duration and
step (the truth's preset 1 s). **Run sweep** verdicts each latitude against
the limit under the limit-curve rule and tabulates the maximum, the point
and curve margins, the verdict, the deciding point and any curve crossing;
an optional **Declared R set** is the declaration a declared-mask sweep
examines, the truth keeping the profile's own gates. **Run loop** derives
the declaration on a saturated probe (or takes the given R set), sweeps the
truth, examines the declaration (E1) and shows T, E1, the gap and E1 ≥ T
per latitude in a second table. The **Examination step** choice runs a
declared-mask examination on the preset step or on the S.1503-4 §D4 fine
and coarse steps with the dual time step. Limits are typed one point per
line, or loaded from the BR limits database (**Load**, then **Use** a row).
Two advisors write their result back into the profile: the **exclusion
advisor** walks the global exclusion angle up to the smallest compliant
value, the **cap advisor** derives per-latitude Nco rows. **Export
table…** writes the results as CSV headed by the run's inputs.

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

Headless checks of the business logic against independent invariants
(brute-force references, closed-form identities, round-trips) and reference
cases, each defined in `tests/radians.beamlab.checks/Program.cs`. Each check
prints PASS or FAIL with its detail, a closing line counts passed and
failed, and the exit code is 0 iff all pass. Checks that need files from
outside the repository (a local ITU reference filing, donor SNS databases,
the BR native DLLs, the radians working copy) skip cleanly when those are
absent and print as PASS marked skipped.

The same executable carries the producer's headless measurement modes,
each opt-in by its first argument and each writing the record it is named
for: `margin` (the projection-margin figure), `loop` (the compliance loop;
its step defaults to 1 s, the truth's step, as the examine mode's does;
`reuse=<run dir>` reads a run's declaration back instead of deriving it, and
`gso=<deg>` / `eslon=<deg>` move the victim: the wanted GSO satellite's
longitude offset east of the earth station and the earth station's longitude,
+10 and 0 by default; `examstep=d4` also runs E1 on the time step of
S.1503-4 Sec. D4, the fine step and the dual time step of Sec. D5.1.4.1, beside
the E1 that shares the truth's step), `examine` (a given mask and R set
examined alone, with the same `examstep=d4` option), `dissect` and `parity`
(a filed az/el mask read back into its operating rules, and compared with
the producer's own export), `oracle` (the propagator against a published
case), `study` (the payload envelope study), `probescan` (the read-rule
probe measurement scan), `arcshield` (the arc-protection measurement of the
11.32A concept note), `decompose` (the margin decomposition), `grade` (a
mask against a dataset set), `bench` (seconds per simulated step at the
current thread count) and `curvescan` (the dataset's expected examination
CDFs under the curve verdict rule). Each is documented at its dispatch in
`tests/radians.beamlab.checks/Program.cs`.

The simulation runs its independent work in parallel -- the satellites of
a time step, the cells of a schedule step, the steps of a mask examination
-- and reduces in the sequential order, so the output does not depend on
the thread count (check V56 pins this bit for bit). It uses every
processor unless the `BEAMLAB_THREADS` environment variable names a
smaller count; `BEAMLAB_THREADS=1` runs the sequential code path.

`countries.json` is searched in the working directory, the application
binary directory, and the project root — drop a Natural Earth GeoJSON
there and restart.

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) for the
full text, or <http://www.apache.org/licenses/LICENSE-2.0>.
