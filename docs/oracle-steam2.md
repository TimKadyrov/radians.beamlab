# External oracle: WP 4A Doc 4A/653 (STEAM-2 alpha CDF, L5 eligible count)

*Produced by `dotnet run --project tests/radians.beamlab.checks -- oracle 86400 1`.*
*Date: 2026-09-03. Wall clock 3.7 min.*

## What is tested

The half of the chain the vendored radians files do not cover: propagation
of a full Walker shell into sky positions, the alpha geometry at a ground
point, the joint eligibility gate (elevation AND alpha), and the selection
step. The eligible set per step is beamlab's own Scheduler's (declared set:
header elev_angle = min elevation, min_exclude = the alpha floor for all
orbits); the selection is the document's rule -- one eligible satellite drawn
uniformly at random per step (seed 4653). Beams are irrelevant by construction
(one nadir beam per satellite, covering radius 5000 km): the document has no
beam concept. Nothing about power, masks, composition, the accumulator or the
limits is touched.

The source is one team's published simulation, not a reference implementation:
a disagreement means someone is wrong, not necessarily beamlab -- but the setup
is specified tightly enough that a disagreement is diagnosable.

## L5 -- the eligible-count check (runs first)

1200 km, 87.9 deg, 18 planes x 40 satellites, inter-plane phase 4.5 deg, plane
spacing 10.5 deg; minimum elevation 45 deg, GSO avoidance 8.4 deg. Stated result:
between 3 and 8 satellites meet the eligibility criteria at 50 N at any time step.
This separates a constellation-phasing fault from a selection fault, since the
count does not depend on selection at all.

- Measured over 21600 steps of 1 s: eligible per step **min 3 / mean 3.87 / max 6**; inside [3, 8] on 100.00% of steps.
- **AGREES** with 4A/653.

## STEAM-2 -- the alpha CDF of the selected satellite

1150 km, 53 deg, 32 planes x 50 satellites, inter-plane phase 1.9 deg (exact, not
an integer Walker F), plane spacing 11.25 deg (11.3 in the document); eligible =
elevation >= 40 deg and alpha >= 22 deg; selection at random; test latitudes 0-50 N
at longitude 0. Run here: 86400 steps of 1 s (1.00 d); the document's run was 1e6 x 1 s.

Columns per latitude: published CDF | simulated CDF (fraction of steps whose selected
satellite has alpha <= the row threshold).

| alpha (deg) | 0N pub | 0N sim | 10N pub | 10N sim | 20N pub | 20N sim | 30N pub | 30N sim | 40N pub | 40N sim | 50N pub | 50N sim |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 22 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 |
| 25 | 0.127 | 0.127 | 0.121 | 0.120 | 0.094 | 0.095 | 0.055 | 0.054 | 0.042 | 0.041 | 0.036 | 0.035 |
| 30 | 0.345 | 0.345 | 0.303 | 0.298 | 0.211 | 0.213 | 0.149 | 0.149 | 0.114 | 0.113 | 0.102 | 0.102 |
| 35 | 0.544 | 0.544 | 0.470 | 0.466 | 0.320 | 0.323 | 0.244 | 0.243 | 0.188 | 0.186 | 0.176 | 0.175 |
| 40 | 0.739 | 0.738 | 0.626 | 0.624 | 0.428 | 0.432 | 0.338 | 0.338 | 0.265 | 0.264 | 0.257 | 0.256 |
| 45 | 0.906 | 0.907 | 0.760 | 0.761 | 0.538 | 0.541 | 0.433 | 0.432 | 0.345 | 0.344 | 0.348 | 0.347 |
| 50 | 1.000 | 1.000 | 0.871 | 0.872 | 0.649 | 0.651 | 0.526 | 0.526 | 0.424 | 0.421 | 0.459 | 0.458 |
| 55 | 1.000 | 1.000 | 0.945 | 0.946 | 0.754 | 0.754 | 0.614 | 0.615 | 0.502 | 0.499 | 0.602 | 0.602 |
| 60 | 1.000 | 1.000 | 1.000 | 0.993 | 0.840 | 0.840 | 0.699 | 0.699 | 0.580 | 0.578 | 0.722 | 0.722 |
| 65 | 1.000 | 1.000 | 1.000 | 1.000 | 0.917 | 0.919 | 0.783 | 0.783 | 0.657 | 0.654 | 0.821 | 0.822 |
| 70 | 1.000 | 1.000 | 1.000 | 1.000 | 0.979 | 0.980 | 0.860 | 0.859 | 0.732 | 0.730 | 0.906 | 0.910 |
| 75 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 0.925 | 0.926 | 0.805 | 0.804 | 0.981 | 0.983 |
| 80 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 0.975 | 0.975 | 0.872 | 0.871 | 1.000 | 1.000 |
| 85 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 0.932 | 0.932 | 1.000 | 1.000 |
| 90 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 0.977 | 0.977 | 1.000 | 1.000 |
| 100 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 |

| latitude | max abs CDF deviation | mean eligible / step | outage steps |
|---|---|---|---|
| 0 N | 0.001 | 4.4 | 0 |
| 10 N | 0.007 | 4.6 | 0 |
| 20 N | 0.004 | 5.2 | 0 |
| 30 N | 0.001 | 7.4 | 0 |
| 40 N | 0.003 | 12.4 | 0 |
| 50 N | 0.004 | 20.7 | 0 |

**Verdict: worst max deviation 0.007 -- AGREES (within digitisation and sampling noise).** The published table is digitised to 5 deg bins and 3 decimals; sampling noise at these sample counts is well below 0.01, so deviations are dominated by digitisation and by any true geometric difference.

## Reading it

- Agreement at every latitude validates propagation, the alpha metric, the joint gate
  and the candidate enumeration the scheduler feeds every policy from.
- A latitude-dependent offset with the L5 count correct points at the alpha geometry
  (arc sampling or ES frame), not at the constellation.
- An L5 count outside 3-8 points at the constellation build (phase, plane spacing,
  inclination convention) before anything downstream is blamed.
