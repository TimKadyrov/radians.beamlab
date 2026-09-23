# Compliance loop: STEAM-2-d4step (STEAM-2 profile unchanged; 0.5 d, E1 also on the S.1503-4 time step, declaration reused from the record)

*Produced by `dotnet run --project tests/radians.beamlab.checks -- loop "STEAM-2-d4step.opprofile.json" "STEAM-2.orbitdesign.json" 0.5 60 0 60 10 reuse=dataset/margin/steam-2 examstep=d4`.*
*Date: 2026-09-23. Wall clock 4.3 min.*

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

| latitude | max epfd (dB) | point margin (dB) | deciding point (% of time) | curve margin (dB) | curve crossing | verdict | quiet steps |
|---|---|---|---|---|---|---|---|
| 0 | -154.9 | -9.3 | 0.029% | -11.3 | crosses at -164.4 dB: 0.5556% vs 0.04088% allowed (limit 0.03935%), x13.59 | FAIL | 0 |
| 10 | -154.0 | -10.2 | 0.029% | -12.2 | crosses at -164.1 dB: 0.5556% vs 0.03252% allowed (limit 0.0313%), x17.09 | FAIL | 0 |
| 20 | -154.6 | -9.5 | 0.029% | -11.5 | crosses at -164.1 dB: 0.6944% vs 0.03252% allowed (limit 0.0313%), x21.36 | FAIL | 0 |
| 30 | -153.2 | -10.9 | 0.029% | -13.1 | crosses at -164.1 dB: 0.6944% vs 0.03252% allowed (limit 0.0313%), x21.36 | FAIL | 0 |
| 40 | -156.6 | -7.5 | 0.029% | -10.3 | crosses at -164.1 dB: 0.6944% vs 0.03252% allowed (limit 0.0313%), x21.36 | FAIL | 0 |
| 50 | -156.9 | -11.9 | 1% | -11.9 | crosses at -164.1 dB: 1.528% vs 0.03252% allowed (limit 0.0313%), x46.99 | FAIL | 0 |
| 60 | -157.1 | -10.6 | 1% | -11.0 | crosses at -164.1 dB: 1.806% vs 0.03252% allowed (limit 0.0313%), x55.53 | FAIL | 0 |

Verdict rule: compliance against the Article 22 limit curve (design brief) -- PASS only if every tabulated limit point passes AND the distribution nowhere crosses the log-linear curve between the tabulated points, tolerance 0.05 dB read towards lower epfd. Margins quoted as 'point margin' are the limit minus the computed level at each tabulated percentage; the 'curve margin' is the dB shift of the whole distribution that just clears the curve; the rule margin is the smaller of the two.

The deciding point is the limit point at the worst point margin: "max" is the maximum sample against the 0% row, which can only rise as the run lengthens; a percentage names a resolved percentile, which can move either way.

**EXCEEDED at 7 of 7 latitude(s) (0, 10, 20, 30, 40, 50, 60); worst margin -13.1 dB under the limit-curve rule -- sampled every 10 deg over 0..60, so a finer sweep can find worse between them -- power headroom -13.1 dB on per-beam TxEirpDbw (dB-for-dB)**

## The declaration, reused

Reused, not derived: the R set and the pfd mask are read back from `dataset\margin\steam-2` (`steam-2.operparams.json`, `steam-2.mask.lat10p0-ae1p0-svc-50to50.xml`), the artefacts of an earlier run of the same system. The saturated probe measures a configuration space, not a sample of one, and measured exactly depth-stable on STEAM-2 (identical R set and mask at 0.1 d and 1.0 d), so only the truth sweep and the examination run at this depth: this record is a convergence pair for T and E1 against the run it reuses.

- Depth of the derivation: that of the reused run; see its record.
- Reused set: min_exclude -45:22.0/-35:22.0/-25:22.0/-15:22.0/-5:22.0/5:22.0/15:22.0/25:22.0/35:22.0/45:22.0; min_elev -45:40.0/-35:40.0/-25:40.0/-15:40.0/-5:40.0/5:40.0/15:40.0/25:40.0/35:40.0/45:40.0; max_co_freq -45:4/-35:4/-25:4/-15:4/-5:4/5:4/15:4/25:4/35:4/45:4; max_co_freq_sat 59; min_angle es 0.0 / sat 2.2; es_lat -50..50

## E1 -- the examination against that declaration

The truth above does not move; E1 is what the examination sees when it reads the declared mask and the derived R set instead. The gap is the margin the declaration gives away -- and, at the same dB-for-dB rate, the operating power the system could have been licensed for.

| latitude | T margin (dB) | E1 margin (dB) | gap (dB) | deciding point T / E1 | curve margin T / E1 (dB) | E1 >= T |
|---|---|---|---|---|---|---|
| 0 | -9.3 | -18.1 | 8.8 | 0.029% / 0.029% | -11.3 / -20.1 | yes |
| 10 | -10.2 | -17.7 | 7.5 | 0.029% / 0.029% | -12.2 / -19.7 | yes |
| 20 | -9.5 | -18.1 | 8.6 | 0.029% / 0.286% | -11.5 / -20.0 | yes |
| 30 | -10.9 | -18.6 | 7.7 | 0.029% / 0.286% | -13.1 / -21.2 | yes |
| 40 | -7.5 | -18.1 | 10.6 | 0.029% / 0.029% | -10.3 / -20.1 | yes |
| 50 | -11.9 | -23.5 | 11.6 | 1% / 1% | -11.9 / -23.7 | yes |
| 60 | -10.6 | -21.9 | 11.3 | 1% / 1% | -11.0 / -22.1 | yes |

Margins and the gap in this table are point-wise; the curve margins beside them are the rule's second test.

**ADEQUATE: E1 >= T at every latitude; widest gap 11.6 dB**

## E1 on the S.1503-4 time step

The same examination sampled as S.1503-4 Sec. D4 prescribes: fine step 0.208 s (S.1503-4 Sec. D4.2: a 3.34 s pass across the 1.156 deg beam at 1150 km / 53.0 deg, N_hit 16); coarse step 4.160 s = 20 fine steps (Sec. D4.7.1). It covers the same 0.5 d as the 60 s step above and propagates the same trajectories, so the change between the two is the time step alone. The truth stays on the 60 s step, so the gap and the E1 >= T check above remain the matched-step comparison.

The dual time step follows Sec. D5.1.4.1 Sub-steps 6.1 to 6.3, each sample counted T_step / T_fine times (Step 24). The Recommendation words the fine-step region twice: Sec. D4.7.1 defines it as G_RX(phi) > min[Gmax - 30 dB, G_RX(alpha0[Latitude])], near the main beam or the exclusion-zone edge, while Sub-step 6.3 says a G_RX(phi) within 30 dB of peak. The dual columns use the Sec. D4.7.1 definition, the 30 dB column the Sub-step 6.3 wording; the fine column evaluates every fine step.

| latitude | E1 point margin, 60 s step (dB) | E1 point margin, S.1503-4 step, dual (dB) | change (dB) | deciding point | curve margin, dual (dB) | verdict, dual | E1 fine only (dB) | E1 dual, 30 dB reading (dB) | max epfd, 60 s / S.1503-4 (dB) | samples dual / 30 dB / fine |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | -18.1 | -19.0 | -0.9 | max | -17.4 | FAIL | -19.0 | -19.0 | -146.0 / -145.2 | 136233 / 17768 / 207692 |
| 10 | -17.7 | -19.7 | -2.0 | max | -19.1 | FAIL | -19.7 | -19.7 | -146.5 / -144.5 | 144992 / 18870 / 207692 |
| 20 | -18.1 | -20.3 | -2.2 | max | -19.5 | FAIL | -20.3 | -20.3 | -146.1 / -143.9 | 180427 / 20390 / 207692 |
| 30 | -18.6 | -18.7 | -0.1 | max | -19.1 | FAIL | -18.7 | -18.7 | -145.7 / -145.4 | 207692 / 24722 / 207692 |
| 40 | -18.1 | -18.8 | -0.7 | max | -19.4 | FAIL | -18.8 | -18.8 | -146.0 / -145.3 | 207692 / 31220 / 207692 |
| 50 | -23.5 | -20.5 | +3.0 | 1% | -20.9 | FAIL | -20.5 | -20.5 | -146.2 / -145.3 | 207692 / 56072 / 207692 |
| 60 | -21.9 | -21.4 | +0.5 | 1% | -21.3 | FAIL | -21.4 | -21.4 | -148.1 / -148.1 | 207692 / 102451 / 207692 |

**On the S.1503-4 time step the E1 point margin changes by -2.2 to +3.0 dB across the latitudes; worst -21.4 dB against -23.5 dB on the 60 s step.**

## Artefacts

The profile is this run's; the R set and the pfd mask are the reused run's, copied here so the directory stands alone:

- Operation profile (the truth as run): `dataset\margin\steam-2-d4step\steam-2-d4step.opprofile.json`
- Reused R set (S.1503-4 Part B): `dataset\margin\steam-2-d4step\steam-2-d4step.operparams.xml`
- Reused R set, designer format (open with the operating-parameters designer): `dataset\margin\steam-2-d4step\steam-2-d4step.operparams.json`
- Declared pfd mask: `dataset\margin\steam-2-d4step\steam-2-d4step.mask.reused.xml`
