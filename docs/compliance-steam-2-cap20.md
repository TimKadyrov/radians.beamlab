# Compliance loop: STEAM-2 cap20 (co-frequency beam capacity 20; K readout calibration)

*Produced by `dotnet run --project tests/radians.beamlab.checks -- loop "STEAM-2-cap20.opprofile.json" "STEAM-2.orbitdesign.json" 0.1 60 0 60 10`.*
*Date: 2026-09-07. Wall clock 29.2 min.*

> Verdict rule of this record: point-wise -- every tabulated Article 22 point, the rule in force when it was produced. On 2026-09-18 the design brief adopted the limit-curve rule (a distribution must also clear the log-linear curve between the tabulated points, tolerance 0.05 dB read towards lower epfd); records produced since name it and quote a curve margin beside the point margin. The verdicts here cannot change under it: every latitude fails at the tabulated points by the margins shown, and the curve test can only add failures. Every margin in this record is point-wise.

## The system under test

- Shell(s): 1, 1600 satellites at 1150 km / inclination 53.0 deg.
- Enforced rules: minimum elevation 40 deg, exclusion alpha 22.0 deg, Nco 4, selection Random.
- Footprint source: composition (live beam composition -- the truth).
- Victim: earth station at longitude 0, GSO satellite +10 deg, dish 1.00 m (the limit row's own reference diameter).

## The limit

- Article 22, TABLE 22-1B -- FSS 17800-18600 MHz, refbw 40 kHz, dish 1.00 m, regions XR1/XR2/XR3
- Points (epfd dB / % of time): -175.4@100, -175.4@10, -172.5@1, -167@0.286, -164@0.029, -164@0

## Verdicts

Depth: 144 steps of 60 s per latitude (0.100 d) -- resolvable percentile floor 0.694%, so short-term points below that floor are located, not decided, at this depth.

Sweep grid: latitudes 0..60 every 10 deg. The worst margin below is the worst over THESE latitudes; the sweep evaluates real victims at discrete points, so a finer grid can find worse between them -- carry the step with the figure.

| latitude | max epfd (dB) | worst margin (dB) | verdict | quiet steps |
|---|---|---|---|---|
| 0 | -158.0 | -9.1 | FAIL | 0 |
| 10 | -166.0 | -4.8 | FAIL | 0 |
| 20 | -159.7 | -7.4 | FAIL | 0 |
| 30 | -164.5 | -5.7 | FAIL | 0 |
| 40 | -160.2 | -6.9 | FAIL | 0 |
| 50 | -157.5 | -11.9 | FAIL | 0 |
| 60 | -158.3 | -11.7 | FAIL | 0 |

**EXCEEDED at 7 of 7 latitude(s) (0, 10, 20, 30, 40, 50, 60); worst margin -11.9 dB -- sampled every 10 deg over 0..60, so a finer sweep can find worse between them -- power headroom -11.9 dB on per-beam TxEirpDbw (dB-for-dB)**

## The declaration, derived

Measured on a SATURATED probe with no victim -- demand 1 -> 4, activity 1.00 -> 1.00, duty 1.00 -> 1.00, operating fraction 1.00 -> 1.00. A declaration is an envelope of what the system MAY do, so traffic is taken out before it is measured; and being a different run from the truth sweep, it keeps E1 >= T an adequacy test rather than a tautology.

- Depth: 144 steps / 6575111 link samples, latitude band 10 deg.
- Derived set: min_exclude -45:22.0/-35:22.0/-25:22.0/-15:22.0/-5:22.0/5:22.0/15:22.0/25:22.0/35:22.0/45:22.0; min_elev -45:40.0/-35:40.0/-25:40.0/-15:40.0/-5:40.0/5:40.0/15:40.0/25:40.0/35:40.0/45:40.0; max_co_freq -45:4/-35:4/-25:4/-15:4/-5:4/5:4/15:4/25:4/35:4/45:4; max_co_freq_sat 59; min_angle es 0.0 / sat 2.2; es_lat -50..50

## E1 -- the examination against that declaration

The truth above does not move; E1 is what the examination sees when it reads the declared mask and the derived R set instead. The gap is the margin the declaration gives away -- and, at the same dB-for-dB rate, the operating power the system could have been licensed for.

| latitude | T margin (dB) | E1 margin (dB) | gap (dB) | E1 >= T |
|---|---|---|---|---|
| 0 | -9.1 | -15.6 | 6.5 | yes |
| 10 | -4.8 | -13.7 | 8.9 | yes |
| 20 | -7.4 | -17.8 | 10.4 | yes |
| 30 | -5.7 | -12.1 | 6.4 | yes |
| 40 | -6.9 | -15.9 | 9.0 | yes |
| 50 | -11.9 | -22.8 | 10.9 | yes |
| 60 | -11.7 | -20.1 | 8.4 | yes |

**ADEQUATE: E1 >= T at every latitude; widest gap 10.9 dB**

## Artefacts

Profile, R set and pfd mask come out of this one run, so they describe the same system:

- Operation profile (the truth as run): `dataset\margin\steam-2-cap20\steam-2-cap20.opprofile.json`
- Derived R set (S.1503-4 Part B): `dataset\margin\steam-2-cap20\steam-2-cap20.operparams.xml`
- Derived R set, designer format (open with the operating-parameters designer): `dataset\margin\steam-2-cap20\steam-2-cap20.operparams.json`
- Declared pfd mask: `dataset\margin\steam-2-cap20\steam-2-cap20.mask.lat10p0-ae1p0-svc-50to50-vc3f46b06.xml`
