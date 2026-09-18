# Compliance loop: STEAM-2-05d (STEAM-2 profile unchanged; 0.5 d depth run, declaration reused from the record)

*Produced by `dotnet run --project tests/radians.beamlab.checks -- loop "STEAM-2-05d.opprofile.json" "STEAM-2.orbitdesign.json" 0.5 60 0 60 10 reuse=dataset/margin/steam-2`.*
*Date: 2026-09-07. Wall clock 85.4 min.*

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

Depth: 720 steps of 60 s per latitude (0.500 d) -- resolvable percentile floor 0.139%, so short-term points below that floor are located, not decided, at this depth.

Sweep grid: latitudes 0..60 every 10 deg. The worst margin below is the worst over THESE latitudes; the sweep evaluates real victims at discrete points, so a finer grid can find worse between them -- carry the step with the figure.

| latitude | max epfd (dB) | worst margin (dB) | verdict | quiet steps |
|---|---|---|---|---|
| 0 | -154.9 | -9.3 | FAIL | 0 |
| 10 | -154.0 | -10.2 | FAIL | 0 |
| 20 | -154.6 | -9.5 | FAIL | 0 |
| 30 | -153.2 | -10.9 | FAIL | 0 |
| 40 | -156.6 | -7.5 | FAIL | 0 |
| 50 | -156.9 | -11.9 | FAIL | 0 |
| 60 | -157.1 | -10.6 | FAIL | 0 |

**EXCEEDED at 7 of 7 latitude(s) (0, 10, 20, 30, 40, 50, 60); worst margin -11.9 dB -- sampled every 10 deg over 0..60, so a finer sweep can find worse between them -- power headroom -11.9 dB on per-beam TxEirpDbw (dB-for-dB)**

## The declaration, reused

Reused, not derived: the R set and the pfd mask are read back from `dataset\margin\steam-2` (`steam-2.operparams.json`, `steam-2.mask.lat10p0-ae1p0-svc-50to50.xml`), the artefacts of an earlier run of the same system. The saturated probe measures a configuration space, not a sample of one, and measured exactly depth-stable on STEAM-2 (identical R set and mask at 0.1 d and 1.0 d), so only the truth sweep and the examination run at this depth: this record is a convergence pair for T and E1 against the run it reuses.

- Depth of the derivation: that of the reused run; see its record.
- Reused set: min_exclude -45:22.0/-35:22.0/-25:22.0/-15:22.0/-5:22.0/5:22.0/15:22.0/25:22.0/35:22.0/45:22.0; min_elev -45:40.0/-35:40.0/-25:40.0/-15:40.0/-5:40.0/5:40.0/15:40.0/25:40.0/35:40.0/45:40.0; max_co_freq -45:4/-35:4/-25:4/-15:4/-5:4/5:4/15:4/25:4/35:4/45:4; max_co_freq_sat 59; min_angle es 0.0 / sat 2.2; es_lat -50..50

## E1 -- the examination against that declaration

The truth above does not move; E1 is what the examination sees when it reads the declared mask and the derived R set instead. The gap is the margin the declaration gives away -- and, at the same dB-for-dB rate, the operating power the system could have been licensed for.

| latitude | T margin (dB) | E1 margin (dB) | gap (dB) | E1 >= T |
|---|---|---|---|---|
| 0 | -9.3 | -18.1 | 8.8 | yes |
| 10 | -10.2 | -17.7 | 7.5 | yes |
| 20 | -9.5 | -18.1 | 8.6 | yes |
| 30 | -10.9 | -18.6 | 7.7 | yes |
| 40 | -7.5 | -18.1 | 10.6 | yes |
| 50 | -11.9 | -23.5 | 11.6 | yes |
| 60 | -10.6 | -21.9 | 11.3 | yes |

**ADEQUATE: E1 >= T at every latitude; widest gap 11.6 dB**

## Artefacts

The profile is this run's; the R set and the pfd mask are the reused run's, copied here so the directory stands alone:

- Operation profile (the truth as run): `dataset\margin\steam-2-05d\steam-2-05d.opprofile.json`
- Reused R set (S.1503-4 Part B): `dataset\margin\steam-2-05d\steam-2-05d.operparams.xml`
- Reused R set, designer format (open with the operating-parameters designer): `dataset\margin\steam-2-05d\steam-2-05d.operparams.json`
- Declared pfd mask: `dataset\margin\steam-2-05d\steam-2-05d.mask.reused.xml`

## Against the 1.0 d record (`compliance-steam-2-1d.md`)

This truth-only run is the convergence pair of the 1.0 d record under the rule's tolerance of
0.5 dB: the same declaration (the record's R set and mask, read back), the same profile in all but
name, design, limit row, victims, grid and step; depth is the only variable. The runs are
deterministic and this one is the first half of the 1.0 d run, so the pair asks whether the second
half of the day changes a figure.

| latitude | T 0.5 d -> 1.0 d | E1 0.5 d -> 1.0 d | gap 0.5 d -> 1.0 d | status |
|---|---|---|---|---|
| 0 | -9.3 -> -9.3 | -18.1 -> -18.1 | 8.8 -> 8.8 | firm |
| 10 | -10.2 -> -10.2 | -17.7 -> -17.7 | 7.5 -> 7.5 | firm |
| 20 | -9.5 -> -9.5 | -18.1 -> -18.0 | 8.6 -> 8.5 | firm |
| 30 | -10.9 -> -10.9 | -18.6 -> -18.6 | 7.7 -> 7.7 | firm |
| 40 | -7.5 -> -10.3 | -18.1 -> -18.8 | 10.6 -> 8.5 | provisional |
| 50 | -11.9 -> -9.3 | -23.5 -> -20.0 | 11.6 -> 10.7 | provisional |
| 60 | -10.6 -> -10.4 | -21.9 -> -21.4 | 11.3 -> 11.0 | firm |

**Firm at 1.0 d:** T from -9.3 to -10.9 and the in-band gap 7.5 to 8.8 dB at latitudes 0..30, and
latitude 60 at 11.0; the power headroom -10.9 dB at latitude 30. **Provisional:** latitudes 40 and
50, where the worst event arrived in the second half of the day; a 2.0 d truth-only run would pair
them directly.
