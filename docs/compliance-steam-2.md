# Compliance loop: STEAM-2 (WP 4A Doc 4A/653 + filed mask; pattern, layout, reuse assumed)

*Produced by `dotnet run --project tests/radians.beamlab.checks -- loop "STEAM-2.opprofile.json" "STEAM-2.orbitdesign.json" 0.1 60 0 60 10`.*
*Date: 2026-09-04. Wall clock 63.8 min.*

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

| latitude | max epfd (dB) | worst margin (dB) | verdict | quiet steps |
|---|---|---|---|---|
| 0 | -206.0 | +31.4 | PASS | 0 |
| 10 | -199.7 | +25.0 | PASS | 0 |
| 20 | -178.5 | +6.2 | PASS | 0 |
| 30 | -167.8 | -2.4 | FAIL | 0 |
| 40 | -159.6 | -7.5 | FAIL | 0 |
| 50 | -157.2 | -11.7 | FAIL | 0 |
| 60 | -162.8 | -8.8 | FAIL | 0 |

**EXCEEDED at 4 of 7 latitude(s) (30, 40, 50, 60); worst margin -11.7 dB -- power headroom -11.7 dB on per-beam TxEirpDbw (dB-for-dB)**
