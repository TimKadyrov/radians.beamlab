# Payload envelope study

*Produced by `dotnet run --project tests/radians.beamlab.checks -- study`.*
*Date: 2026-08-31. Wall clock 11.4 min.*

No real system parameters exist yet, so this study answers the inverse
question: what payload envelope does TABLE 22-1C compliance REQUIRE of
the baseline system? The knob is the per-beam transmit power density
(dBW in the 40 kHz reference bandwidth; the S.1528-1 pattern gain rides
on top), which moves every epfd dB for dB. The system is otherwise the
first margin figure's baseline (docs/margin-figure.md): 1200 km / 53 deg
Walker 3x4, 19.7 GHz, min elev 10, service 30-60 N / +/-20 E, 450 km
cells, full activity. When the real numbers arrive they replace this
envelope; nothing here is fitted to the verdict -- the frontier IS the
deliverable.

Anchor: scene per-beam peak gain 35.0 dBi, so boresight e.i.r.p. density = power density + 35.0 dB.

## The compliance frontier (alpha = 0, sweep lat 0-70)

| power density (dBW/40kHz) | boresight e.i.r.p. (dBW/40kHz) | worst margin (dB) | at lat | verdict |
|---|---|---|---|---|
| 0 | 35.0 | -35.0 | 50 | FAIL |
| -10 | 25.0 | -25.0 | 50 | FAIL |
| -20 | 15.0 | -15.0 | 50 | FAIL |
| -30 | 5.0 | -5.0 | 50 | FAIL |
| -40 | -5.0 | +5.0 | 50 | PASS |

Advisor at the louder neighbour (-30 dBW/40kHz): no compliant alpha up to 20 deg after 21 sweep(s); worst margin -5.0 -> -1.2 dB, improving with alpha (+3.8 dB over the walk).

**Figure point: power density -40 dBW/40kHz (boresight e.i.r.p. -5.0), declared alpha 0.0 deg.**

## The margin figure at the point

Victim: GSO ES lat 50 / lon 0 (worst sweep latitude at the point), GSO lon 10, S.1428 0.70 m; comb 2880 x 60 s (floor 0.035%). Declarations: alpha/deltaLong mask lat -53..53 step 10, R set 10-deg bands (21015 samples).

| run | what | max epfd (dB) | quiet | verdict |
|---|---|---|---|---|
| T | truth (live composition) | -149.28 | 756 | FAIL |
| E1 | mask b/c 5 deg + derived R | -144.23 | 1328 | FAIL |
| E2 | mask b/c 5 deg + profile rules | -144.23 | 1328 | FAIL |
| E1f | mask b/c 2 deg + derived R | -144.71 | 1328 | FAIL |

| limit point (dB @ %) | T | E1 | E1f | E1-T (projection, 5 deg) | E1f-T (2 deg) | grid component E1-E1f | E2-E1 |
|---|---|---|---|---|---|---|---|
| -154 @ 0 | -149.10 | -144.10 | -144.60 | 5.00 | 4.50 | 0.50 | 0.00 |
| -154 @ 0.017 | -149.10 | -144.10 | -144.60 | 5.00 | 4.50 | 0.50 | 0.00 |
| -172 @ 2.857 | -182.50 | -180.50 | -180.70 | 2.00 | 1.80 | 0.20 | 0.40 |
| -182 @ 28.571 | -190.40 | -188.30 | -188.80 | 2.10 | 1.60 | 0.50 | 0.40 |
| -187.4 @ 100 | -287.40 | -287.40 | -287.40 | 0.00 | 0.00 | 0.00 | 0.00 |

**Headline: at the compliant point the projection margin is 5.00 dB with the 5-deg mask grid and 4.50 dB with the 2-deg grid (max-epfd E1-T 5.05 / E1f-T 4.57 dB) -- the grid step accounts for 0.48 dB of it at max-epfd.**

Caveats as in docs/margin-figure.md (single victim, global alpha by
decision, power budget contingent, tail floor as stated); additionally
the payload here is a REQUIRED-envelope stand-in, not a real system --
the 100% limit point's margin is a range artefact. CDFs:
dataset/margin/study.{T,E1,E2,E1fine}.csv.
