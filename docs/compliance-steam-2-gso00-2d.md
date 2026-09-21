# Compliance loop: STEAM-2-gso00-2d (STEAM-2 profile unchanged; GSO satellite +0 deg east of the earth station, 2.0 d, declaration reused from the record)

*Produced by `dotnet run --project tests/radians.beamlab.checks -- loop "STEAM-2-gso00-2d.opprofile.json" "STEAM-2.orbitdesign.json" 2 60 0 60 10 reuse=dataset/margin/steam-2 gso=0`.*
*Date: 2026-09-21. Wall clock 16.9 min.*

## The system under test

- Shell(s): 1, 1600 satellites at 1150 km / inclination 53.0 deg.
- Enforced rules: minimum elevation 40 deg, exclusion alpha 22.0 deg, Nco 4, selection Random.
- Footprint source: composition (live beam composition -- the truth).
- Victim: earth station at longitude 0, GSO satellite +0 deg, dish 1.00 m (the limit row's own reference diameter).

## The limit

- Article 22, TABLE 22-1B -- FSS 17800-18600 MHz, refbw 40 kHz, dish 1.00 m, regions XR1/XR2/XR3
- Points (epfd dB / % of time): -175.4@100, -175.4@10, -172.5@1, -167@0.286, -164@0.029, -164@0

## Verdicts

Depth: 2880 steps of 60 s per latitude (2.000 d) -- resolvable percentile floor 0.035%, so short-term points below that floor are located, not decided, at this depth.

Sweep grid: latitudes 0..60 every 10 deg. The worst margin below is the worst over THESE latitudes; the sweep evaluates real victims at discrete points, so a finer grid can find worse between them -- carry the step with the figure.

| latitude | max epfd (dB) | point margin (dB) | deciding point (% of time) | curve margin (dB) | curve crossing | verdict | quiet steps |
|---|---|---|---|---|---|---|---|
| 0 | -150.4 | -13.8 | 0.029% | -14.5 | crosses at -164.2 dB: 0.4514% vs 0.03509% allowed (limit 0.03378%), x12.86 | FAIL | 0 |
| 10 | -151.9 | -12.3 | 0.029% | -12.7 | crosses at -164.1 dB: 0.3472% vs 0.03252% allowed (limit 0.0313%), x10.68 | FAIL | 0 |
| 20 | -153.9 | -10.2 | 0.029% | -10.9 | crosses at -164.1 dB: 0.4167% vs 0.03252% allowed (limit 0.0313%), x12.81 | FAIL | 0 |
| 30 | -153.6 | -10.5 | 0.029% | -11.5 | crosses at -164.1 dB: 0.4514% vs 0.03252% allowed (limit 0.0313%), x13.88 | FAIL | 0 |
| 40 | -154.7 | -9.4 | 0.029% | -10.7 | crosses at -164.1 dB: 0.7986% vs 0.03252% allowed (limit 0.0313%), x24.56 | FAIL | 0 |
| 50 | -155.5 | -9.1 | 1% | -9.6 | crosses at -164.1 dB: 1.146% vs 0.03252% allowed (limit 0.0313%), x35.24 | FAIL | 0 |
| 60 | -158.1 | -9.8 | 1% | -9.8 | crosses at -164.1 dB: 1.528% vs 0.03252% allowed (limit 0.0313%), x46.99 | FAIL | 0 |

Verdict rule: compliance against the Article 22 limit curve (design brief) -- PASS only if every tabulated limit point passes AND the distribution nowhere crosses the log-linear curve between the tabulated points, tolerance 0.05 dB read towards lower epfd. Margins quoted as 'point margin' are the limit minus the computed level at each tabulated percentage; the 'curve margin' is the dB shift of the whole distribution that just clears the curve; the rule margin is the smaller of the two.

The deciding point is the limit point at the worst point margin: "max" is the maximum sample against the 0% row, which can only rise as the run lengthens; a percentage names a resolved percentile, which can move either way.

**EXCEEDED at 7 of 7 latitude(s) (0, 10, 20, 30, 40, 50, 60); worst margin -14.5 dB under the limit-curve rule -- sampled every 10 deg over 0..60, so a finer sweep can find worse between them -- power headroom -14.5 dB on per-beam TxEirpDbw (dB-for-dB)**

## The declaration, reused

Reused, not derived: the R set and the pfd mask are read back from `dataset\margin\steam-2` (`steam-2.operparams.json`, `steam-2.mask.lat10p0-ae1p0-svc-50to50.xml`), the artefacts of an earlier run of the same system. The saturated probe measures a configuration space, not a sample of one, and measured exactly depth-stable on STEAM-2 (identical R set and mask at 0.1 d and 1.0 d), so only the truth sweep and the examination run at this depth: this record is a convergence pair for T and E1 against the run it reuses.

- Depth of the derivation: that of the reused run; see its record.
- Reused set: min_exclude -45:22.0/-35:22.0/-25:22.0/-15:22.0/-5:22.0/5:22.0/15:22.0/25:22.0/35:22.0/45:22.0; min_elev -45:40.0/-35:40.0/-25:40.0/-15:40.0/-5:40.0/5:40.0/15:40.0/25:40.0/35:40.0/45:40.0; max_co_freq -45:4/-35:4/-25:4/-15:4/-5:4/5:4/15:4/25:4/35:4/45:4; max_co_freq_sat 59; min_angle es 0.0 / sat 2.2; es_lat -50..50

## E1 -- the examination against that declaration

The truth above does not move; E1 is what the examination sees when it reads the declared mask and the derived R set instead. The gap is the margin the declaration gives away -- and, at the same dB-for-dB rate, the operating power the system could have been licensed for.

| latitude | T margin (dB) | E1 margin (dB) | gap (dB) | deciding point T / E1 | curve margin T / E1 (dB) | E1 >= T |
|---|---|---|---|---|---|---|
| 0 | -13.8 | -20.2 | 6.4 | 0.029% / 0.029% | -14.5 / -20.3 | yes |
| 10 | -12.3 | -19.7 | 7.4 | 0.029% / 0.029% | -12.7 / -19.8 | yes |
| 20 | -10.2 | -18.6 | 8.4 | 0.029% / 0.029% | -10.9 / -19.5 | yes |
| 30 | -10.5 | -16.5 | 6.0 | 0.029% / 0.029% | -11.5 / -17.5 | yes |
| 40 | -9.4 | -18.5 | 9.1 | 0.029% / 0.029% | -10.7 / -19.9 | yes |
| 50 | -9.1 | -20.1 | 11.0 | 1% / 1% | -9.6 / -21.0 | yes |
| 60 | -9.8 | -20.9 | 11.1 | 1% / 1% | -9.8 / -21.2 | yes |

Margins and the gap in this table are point-wise; the curve margins beside them are the rule's second test.

**ADEQUATE: E1 >= T at every latitude; widest gap 11.1 dB**

## Artefacts

The profile is this run's; the R set and the pfd mask are the reused run's, copied here so the directory stands alone:

- Operation profile (the truth as run): `dataset\margin\steam-2-gso00-2d\steam-2-gso00-2d.opprofile.json`
- Reused R set (S.1503-4 Part B): `dataset\margin\steam-2-gso00-2d\steam-2-gso00-2d.operparams.xml`
- Reused R set, designer format (open with the operating-parameters designer): `dataset\margin\steam-2-gso00-2d\steam-2-gso00-2d.operparams.json`
- Declared pfd mask: `dataset\margin\steam-2-gso00-2d\steam-2-gso00-2d.mask.reused.xml`
