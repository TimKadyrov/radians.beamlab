# The first margin figure -- fine-comb rerun

*Produced by `dotnet run --project tests/radians.beamlab.checks -- margin 6 28800 1 10`.*
*Date: 2026-09-01. Wall clock 16.6 min.*

*Variant of the baseline (docs/margin-figure.md): same system, same
victim -- only the comb and/or the mask grid (b/c or latitude step)
named above differ, isolating sampling and mask-grid contributions.*

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

- PFD mask: alpha/deltaLongitude, latitude table -53..53 step 10, b/c 1 deg, exclusion baked (13 blocks) -- dataset/margin/margin-6s-bc1.mask.xml
- R set: envelope of the flown operation, 10 deg latitude bands, 14641 link samples -- dataset/margin/margin.rset.xml

## The limit (from the BR database, not hand-guessed)

- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 0.70 m, regions XR1/XR2/XR3
- Points (epfd dB(W/m2/40kHz) / % of time it may be exceeded): -187.4@100, -182@28.571, -172@2.857, -154@0.017, -154@0

## The victim and the comb

- GSO ES at lat 40 / lon 0 (the sweep's worst-margin latitude), wanted GSO at lon 10, S.1428 0.70 m (the limit row's reference dish).
- One shared comb for all three runs: 28800 steps of 6 s (2.00 d); resolvable percentile floor 0.003%.

## The three runs

| run | projection | gates | max epfd (dB) | quiet steps | verdict vs the row |
|---|---|---|---|---|---|
| T | live composition (occurring) | profile rules, scheduler-enforced | -117.46 | 6101 | FAIL |
| E1 | declared mask, D5.1.4.1 | derived R set (the filing) | -110.88 | 14886 | FAIL |
| E2 | declared mask, D5.1.4.1 | profile-composed rules | -110.88 | 14886 | FAIL |

## The margin, point by point

Measured epfd at each limit percentage (dB); margin = limit - measured
(positive = room). The projection margin is E1 - T: the conservatism the
examination's view of the filing adds. E2 - E1 names the R-set derivation
component (measured envelope vs declared rules).

| limit point (dB @ %) | T epfd | E1 epfd | E2 epfd | T margin | E1 margin | E1-T (projection) | E2-E1 |
|---|---|---|---|---|---|---|---|
| -154 @ 0 | -117.30 | -110.70 | -110.70 | -36.70 | -43.30 | 6.60 | 0.00 |
| -154 @ 0.017 | -124.10 | -117.60 | -117.60 | -29.90 | -36.40 | 6.50 | 0.00 |
| -172 @ 2.857 | -147.50 | -146.40 | -145.50 | -24.50 | -25.60 | 1.10 | 0.90 |
| -182 @ 28.571 | -152.20 | -148.80 | -148.80 | -29.80 | -33.20 | 3.40 | 0.00 |
| -187.4 @ 100 | -287.40 | -287.40 | -287.40 | 100.00 | 100.00 | 0.00 | 0.00 |

**Headline: at the deepest RESOLVABLE limit point (0.017% of time; comb floor 0.0035%) the projection margin (E1 - T) is 6.50 dB; across resolvable points 1.10-6.50 dB. The sub-floor point(s) (0%) differ by up to 6.60 dB in per-run maxima -- below the comb's resolution, quoted only as such (max-epfd difference 6.58 dB, same caveat).**

## Named caveats and knobs (the granularity study starts here)

- Single victim geometry: the sweep's worst latitude at one ES longitude
  and one GSO offset. The worst-case geometry handshake with the
  examination side is pending; a GSO-offset sweep is the tracked next
  exploration axis.
- Global exclusion only, by decision: the per-latitude mask-inheritance
  component is absent from this figure by construction.
- Payload power budget: contingent, unmodelled (the truth-side
  measurement is tracked separately); power control is in the model.
- Sampling: percentiles finer than 0.003% are not resolved on this comb;
  the deepest-event wobble study says tail agreement is bin-class.
- Comb rule (measured, 60 s vs 6 s vs 1 s combs): per-run maxima moved
  ~20 dB from 60 s to 6 s and only ~0.3 dB from 6 s to 1 s -- the step
  must resolve the beam-footprint crossing at the victim. On this class
  of geometry (450 km cells at 1200 km) tail LEVELS need steps <= 6 s;
  body percentiles are stable from 60 s. Quote tail differences at
  resolvable percentiles only -- coarse-comb tail levels are meaningless
  and even their differences are luck.
- Declaration granularity knobs measurable next: mask latitude step and
  b/c grid, R-set latitude banding, per-latitude alpha rows.
- epfd(is)/(up) are out of scope here (down only).
- The 100% limit point reads the accumulator's lowest bin (range floor
  = min limit - 100 dB), so its margin is a range artefact, not a
  measurement. The examination runs are quiet more often than the truth
  (exclusion switches satellites off entirely where the composition
  still radiates sidelobes) yet peak louder (the mask envelope plus its
  bin granularity exceed any instantaneous composite) -- both faithful.

CDFs: dataset/margin/margin-6s-bc1.{T,E1,E2}.csv (epfd dB, % time exceeded).
