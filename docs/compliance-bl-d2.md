# Compliance loop: BL-D2

*Produced by `dotnet run --project tests/radians.beamlab.checks -- loop "BL-D2.opprofile.json" "design.orbitdesign.json" 0.1 60 30 60 10`.*
*Date: 2026-09-07. Wall clock 0.3 min.*

## The system under test

- Shell(s): 1, 32 satellites at 1200 km / inclination 53.0 deg.
- Enforced rules: minimum elevation 10 deg, exclusion alpha 8.0 deg, Nco 4, selection HighestElevation.
- Footprint source: composition (live beam composition -- the truth).
- Victim: earth station at longitude 0, GSO satellite +10 deg, dish 1.00 m (the limit row's own reference diameter).

## The limit

- Article 22, TABLE 22-1B -- FSS 17800-18600 MHz, refbw 40 kHz, dish 1.00 m, regions XR1/XR2/XR3
- Points (epfd dB / % of time): -175.4@100, -175.4@10, -172.5@1, -167@0.286, -164@0.029, -164@0

## Verdicts

Depth: 144 steps of 60 s per latitude (0.100 d) -- resolvable percentile floor 0.694%, so short-term points below that floor are located, not decided, at this depth.

| latitude | max epfd (dB) | worst margin (dB) | verdict | quiet steps |
|---|---|---|---|---|
| 30 | -142.4 | -29.3 | FAIL | 0 |
| 40 | -136.8 | -33.4 | FAIL | 0 |
| 50 | -138.3 | -29.2 | FAIL | 0 |
| 60 | -141.6 | -29.3 | FAIL | 0 |

**EXCEEDED at 4 of 4 latitude(s) (30, 40, 50, 60); worst margin -33.4 dB -- power headroom -33.4 dB on per-beam TxEirpDbw (dB-for-dB)**

## The declaration, derived

Measured on a SATURATED probe with no victim -- demand 1 -> 4, activity 1.00 -> 1.00, duty 1.00 -> 1.00, operating fraction 1.00 -> 1.00. A declaration is an envelope of what the system MAY do, so traffic is taken out before it is measured; and being a different run from the truth sweep, it keeps E1 >= T an adequacy test rather than a tautology.

- Depth: 144 steps / 10712 link samples, latitude band 10 deg.
- Derived set: min_exclude 35:8.0/45:8.0/55:8.0; min_elev 35:10.0/45:10.0/55:10.0; max_co_freq 35:2/45:2/55:2; max_co_freq_sat 49; min_angle es 28.9 / sat 1.9; es_lat 32..57

## E1 -- the examination against that declaration

The truth above does not move; E1 is what the examination sees when it reads the declared mask and the derived R set instead. The gap is the margin the declaration gives away -- and, at the same dB-for-dB rate, the operating power the system could have been licensed for.

| latitude | T margin (dB) | E1 margin (dB) | gap (dB) | E1 >= T |
|---|---|---|---|---|
| 30 | -29.3 | -37.0 | 7.7 | yes |
| 40 | -33.4 | -40.5 | 7.1 | yes |
| 50 | -29.2 | -39.0 | 9.8 | yes |
| 60 | -29.3 | -37.2 | 7.9 | yes |

**ADEQUATE: E1 >= T at every latitude; widest gap 9.8 dB**

## Artefacts

Profile, R set and pfd mask come out of this one run, so they describe the same system:

- Operation profile (the truth as run): `dataset\margin\bl-d2\bl-d2.opprofile.json`
- Derived R set (S.1503-4 Part B): `dataset\margin\bl-d2\bl-d2.operparams.xml`
- Derived R set, designer format (open with the operating-parameters designer): `dataset\margin\bl-d2\bl-d2.operparams.json`
- Declared pfd mask: `dataset\margin\bl-d2\bl-d2.mask.lat10p0-ae1p0-svc32to57.xml`
