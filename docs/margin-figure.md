# The first margin figure

> **Read this first (added 2026-09-01):** this baseline runs a 60 s comb
> and a 5 deg mask b/c grid -- its TAIL rows are below the run's
> resolution floor and are NOT trustworthy as levels. The refined runs
> (docs/margin-figure-6s.md, -1s, -6s-bc2, -6s-bc1, -6s-bc1-lat5)
> converge the deep-tail margin at **6.50 dB** (6 s comb, b/c 1 deg,
> lat 5 deg -- every grid knob exhausted); this file's 9.80 dB headline
> is the coarse-comb, coarse-grid reading of the same case
> (decomposition: ~1 dB sampling, ~2.1 dB b/c grid, 0.0 lat step,
> ~6.5 dB envelope). Kept as the historical baseline the variants diff
> against; the attribution record is frozen in simulation-debate.md.

> **Footnote (added 2026-09-06):** the two grid terms in that decomposition
> are not of a kind, and one of them was a category error. The ~2.1 dB
> b/c share is a per-case measurement of a SAFE axis -- the bins take a
> max over their extent, so a coarse b/c grid inflates -- and it stands
> for this lattice only: the same axis is worth 0.1 dB on STEAM-2, and no
> grid figure travels without its case attached. The 0.0 dB latitude
> step was never a lever at all: a pfd row point-sampled at its own
> latitude is read across the band it governs (Sec. D5.1.5, nearest
> row), so a coarse latitude grid DEFLATES the mask -- a correctness
> defect, since fixed by enveloping the band, and measured at up to
> 7.4 dB of under-declaration on BL-D2. It is not a term in any
> decomposition and should not be read as one here. The ~6.5 dB envelope
> term and the 6.50 dB converged figure stand as this case’s envelope
> price. The correction and its controls are in simulation-debate.md,
> 5 and 6 September 2026.

*Produced by `dotnet run --project tests/radians.beamlab.checks -- margin`.*
*Date: 2026-09-01. Wall clock 9.6 min.*

## What is measured

The projection margin of docs/simulation-debate.md (Q6/Q8): the same
fully specified system, the same victim, the same time comb -- computed
once as the truth (the live scheduled beam composition) and once as the
examination would compute the filing (declared PFD mask + declared R set
through S.1503-4 D5.1.4.1). The difference is what the declaration
granularity plus the examination's reading add on top of reality. It is
NOT the margin to the Article 22 limit (that is the compliance loop's
number and is minimised by design).

## The system (truth, in full)

- Shell: 1200 km / 53 deg, Walker 3 planes x 4 satellites, F = 1.
- Payload: S.1528-1 sec. 1.4 Taylor scene defaults (SLR 20 dB, nbar 4),
  19.7 GHz, 40 kHz reference bandwidth.
- Operation: min elevation 10 deg, service 30-60 N / +/-20 E at 450 km
  cells, highest-elevation tracking, demand 1 link/cell, full activity,
  operational fraction 1, illumination duty 1.
- Declared exclusion: global alpha = 20.0 deg -- the advisor CAP (no compliant angle found up to 20 deg) 
  after 21 linear sweep(s) against the limit row below.
  Walk trajectory: worst margin -35.0 -> -31.2 dB, improving with alpha (+3.8 dB over the walk).

## The declarations (derived from the truth, never fitted to the verdict)

- PFD mask: alpha/deltaLongitude, latitude table -53..53 step 10 (pinned), b/c 5 deg, exclusion baked (13 blocks) -- dataset/margin/margin.mask.xml
- R set: envelope of the flown operation, 10 deg latitude bands, 14641 link samples -- dataset/margin/margin.rset.xml

## The limit (from the BR database, not hand-guessed)

- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 0.70 m, regions XR1/XR2/XR3
- Points (epfd dB(W/m2/40kHz) / % of time it may be exceeded): -187.4@100, -182@28.571, -172@2.857, -154@0.017, -154@0

## The victim and the comb

- GSO ES at lat 40 / lon 0 (the sweep's worst-margin latitude), wanted GSO at lon 10, S.1428 0.70 m (the limit row's reference dish).
- One shared comb for all three runs: 2880 steps of 60 s (2.00 d); resolvable percentile floor 0.035%.

## The three runs

| run | projection | gates | max epfd (dB) | quiet steps | verdict vs the row |
|---|---|---|---|---|---|
| T | live composition (occurring) | profile rules, scheduler-enforced | -137.46 | 604 | FAIL |
| E1 | declared mask, D5.1.4.1 | derived R set (the filing) | -127.66 | 1480 | FAIL |
| E2 | declared mask, D5.1.4.1 | profile-composed rules | -127.66 | 1480 | FAIL |

## The margin, point by point

Measured epfd at each limit percentage (dB); margin = limit - measured
(positive = room). The projection margin is E1 - T: the conservatism the
examination's view of the filing adds. E2 - E1 names the R-set derivation
component (measured envelope vs declared rules).

| limit point (dB @ %) | T epfd | E1 epfd | E2 epfd | T margin | E1 margin | E1-T (projection) | E2-E1 |
|---|---|---|---|---|---|---|---|
| -154 @ 0 | -137.30 | -127.50 | -127.50 | -16.70 | -26.50 | 9.80 | 0.00 |
| -154 @ 0.017 | -137.30 | -127.50 | -127.50 | -16.70 | -26.50 | 9.80 | 0.00 |
| -172 @ 2.857 | -147.50 | -146.00 | -145.10 | -24.50 | -26.00 | 1.50 | 0.90 |
| -182 @ 28.571 | -152.30 | -148.50 | -148.50 | -29.70 | -33.50 | 3.80 | 0.00 |
| -187.4 @ 100 | -287.40 | -287.40 | -287.40 | 100.00 | 100.00 | 0.00 | 0.00 |

**Headline: at the deepest RESOLVABLE limit point (2.857% of time; comb floor 0.0347%) the projection margin (E1 - T) is 1.50 dB; across resolvable points 1.50-3.80 dB. The sub-floor point(s) (0%, 0.017%) differ by up to 9.80 dB in per-run maxima -- below the comb's resolution, quoted only as such (max-epfd difference 9.80 dB, same caveat).**

## Named caveats and knobs (the granularity study starts here)

- Single victim geometry: the sweep's worst latitude at one ES longitude
  and one GSO offset. The worst-case geometry handshake with the
  examination side is pending; a GSO-offset sweep is the tracked next
  exploration axis.
- Global exclusion only, by decision: the per-latitude mask-inheritance
  component is absent from this figure by construction.
- Payload power budget: contingent, unmodelled (the truth-side
  measurement is tracked separately); power control is in the model.
- Sampling: percentiles finer than 0.035% are not resolved on this comb;
  the deepest-event wobble study says tail agreement is bin-class.
- Declaration granularity knobs measurable next: mask latitude step and
  b/c grid, R-set latitude banding, per-latitude alpha rows.
- epfd(is)/(up) are out of scope here (down only).
- The 100% limit point reads the accumulator's lowest bin (range floor
  = min limit - 100 dB), so its margin is a range artefact, not a
  measurement. The examination runs are quiet more often than the truth
  (exclusion switches satellites off entirely where the composition
  still radiates sidelobes) yet peak louder (the mask envelope plus its
  bin granularity exceed any instantaneous composite) -- both faithful.

CDFs: dataset/margin/margin.{T,E1,E2}.csv (epfd dB, % time exceeded).
