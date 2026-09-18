# Compliance loop: STEAM-2-4d (STEAM-2 profile unchanged; 4.0 d depth run, declaration reused from the record)

*Produced by `dotnet run --project tests/radians.beamlab.checks -- loop "STEAM-2-4d.opprofile.json" "STEAM-2.orbitdesign.json" 4 60 0 60 10 reuse=dataset/margin/steam-2`.*
*Date: 2026-09-18. Wall clock 74.6 min.*

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

Depth: 5760 steps of 60 s per latitude (4.000 d) -- resolvable percentile floor 0.017%, so short-term points below that floor are located, not decided, at this depth.

Sweep grid: latitudes 0..60 every 10 deg. The worst margin below is the worst over THESE latitudes; the sweep evaluates real victims at discrete points, so a finer grid can find worse between them -- carry the step with the figure.

| latitude | max epfd (dB) | worst margin (dB) | verdict | quiet steps |
|---|---|---|---|---|
| 0 | -153.3 | -10.8 | FAIL | 0 |
| 10 | -152.9 | -11.2 | FAIL | 0 |
| 20 | -151.9 | -12.2 | FAIL | 0 |
| 30 | -152.0 | -12.1 | FAIL | 0 |
| 40 | -153.9 | -10.2 | FAIL | 0 |
| 50 | -155.4 | -9.2 | FAIL | 0 |
| 60 | -157.1 | -9.6 | FAIL | 0 |

**EXCEEDED at 7 of 7 latitude(s) (0, 10, 20, 30, 40, 50, 60); worst margin -12.2 dB -- sampled every 10 deg over 0..60, so a finer sweep can find worse between them -- power headroom -12.2 dB on per-beam TxEirpDbw (dB-for-dB)**

## The declaration, reused

Reused, not derived: the R set and the pfd mask are read back from `dataset\margin\steam-2` (`steam-2.operparams.json`, `steam-2.mask.lat10p0-ae1p0-svc-50to50.xml`), the artefacts of an earlier run of the same system. The saturated probe measures a configuration space, not a sample of one, and measured exactly depth-stable on STEAM-2 (identical R set and mask at 0.1 d and 1.0 d), so only the truth sweep and the examination run at this depth: this record is a convergence pair for T and E1 against the run it reuses.

- Depth of the derivation: that of the reused run; see its record.
- Reused set: min_exclude -45:22.0/-35:22.0/-25:22.0/-15:22.0/-5:22.0/5:22.0/15:22.0/25:22.0/35:22.0/45:22.0; min_elev -45:40.0/-35:40.0/-25:40.0/-15:40.0/-5:40.0/5:40.0/15:40.0/25:40.0/35:40.0/45:40.0; max_co_freq -45:4/-35:4/-25:4/-15:4/-5:4/5:4/15:4/25:4/35:4/45:4; max_co_freq_sat 59; min_angle es 0.0 / sat 2.2; es_lat -50..50

## E1 -- the examination against that declaration

The truth above does not move; E1 is what the examination sees when it reads the declared mask and the derived R set instead. The gap is the margin the declaration gives away -- and, at the same dB-for-dB rate, the operating power the system could have been licensed for.

| latitude | T margin (dB) | E1 margin (dB) | gap (dB) | E1 >= T |
|---|---|---|---|---|
| 0 | -10.8 | -18.1 | 7.3 | yes |
| 10 | -11.2 | -19.4 | 8.2 | yes |
| 20 | -12.2 | -19.7 | 7.5 | yes |
| 30 | -12.1 | -18.5 | 6.4 | yes |
| 40 | -10.2 | -19.0 | 8.8 | yes |
| 50 | -9.2 | -20.3 | 11.1 | yes |
| 60 | -9.6 | -21.0 | 11.4 | yes |

**ADEQUATE: E1 >= T at every latitude; widest gap 11.4 dB**

## Artefacts

The profile is this run's; the R set and the pfd mask are the reused run's, copied here so the directory stands alone:

- Operation profile (the truth as run): `dataset\margin\steam-2-4d\steam-2-4d.opprofile.json`
- Reused R set (S.1503-4 Part B): `dataset\margin\steam-2-4d\steam-2-4d.operparams.xml`
- Reused R set, designer format (open with the operating-parameters designer): `dataset\margin\steam-2-4d\steam-2-4d.operparams.json`
- Declared pfd mask: `dataset\margin\steam-2-4d\steam-2-4d.mask.reused.xml`
