# Compliance loop: STEAM-2-6s (STEAM-2 profile unchanged; 0.5 d on a 6 s step, E1 also on the S.1503-4 time step, declaration reused from the record)

*Produced by `dotnet run --project tests/radians.beamlab.checks -- loop "STEAM-2-6s.opprofile.json" "STEAM-2.orbitdesign.json" 0.5 6 0 60 10 reuse=dataset/margin/steam-2 examstep=d4`.*
*Date: 2026-09-23. Wall clock 41.1 min.*

## The system under test

- Shell(s): 1, 1600 satellites at 1150 km / inclination 53.0 deg.
- Enforced rules: minimum elevation 40 deg, exclusion alpha 22.0 deg, Nco 4, selection Random.
- Footprint source: composition (live beam composition -- the truth).
- Victim: earth station at longitude 0, GSO satellite +10 deg, dish 1.00 m (the limit row's own reference diameter).

## The limit

- Article 22, TABLE 22-1B -- FSS 17800-18600 MHz, refbw 40 kHz, dish 1.00 m, regions XR1/XR2/XR3
- Points (epfd dB / % of time): -175.4@100, -175.4@10, -172.5@1, -167@0.286, -164@0.029, -164@0

## Verdicts

Depth: 7200 steps of 6 s per latitude (0.500 d) -- resolvable percentile floor 0.014%, so short-term points below that floor are located, not decided, at this depth.

Sweep grid: latitudes 0..60 every 10 deg. The worst margin below is the worst over THESE latitudes; the sweep evaluates real victims at discrete points, so a finer grid can find worse between them -- carry the step with the figure.

| latitude | max epfd (dB) | point margin (dB) | deciding point (% of time) | curve margin (dB) | curve crossing | verdict | quiet steps |
|---|---|---|---|---|---|---|---|
| 0 | -152.6 | -11.5 | max | -10.6 | crosses at -164.1 dB: 0.2639% vs 0.03252% allowed (limit 0.0313%), x8.12 | FAIL | 0 |
| 10 | -151.9 | -12.2 | max | -11.8 | crosses at -164.1 dB: 0.375% vs 0.03252% allowed (limit 0.0313%), x11.53 | FAIL | 0 |
| 20 | -151.3 | -12.8 | max | -9.9 | crosses at -164.1 dB: 0.3472% vs 0.03252% allowed (limit 0.0313%), x10.68 | FAIL | 0 |
| 30 | -150.3 | -13.9 | max | -11.7 | crosses at -164.1 dB: 0.5833% vs 0.03252% allowed (limit 0.0313%), x17.94 | FAIL | 0 |
| 40 | -154.1 | -10.1 | max | -10.0 | crosses at -164.1 dB: 0.7361% vs 0.03252% allowed (limit 0.0313%), x22.64 | FAIL | 0 |
| 50 | -154.9 | -9.2 | max | -9.0 | crosses at -164.1 dB: 1.097% vs 0.03252% allowed (limit 0.0313%), x33.74 | FAIL | 0 |
| 60 | -156.8 | -9.5 | 1% | -9.5 | crosses at -164.1 dB: 1.444% vs 0.03252% allowed (limit 0.0313%), x44.42 | FAIL | 0 |

Verdict rule: compliance against the Article 22 limit curve (design brief) -- PASS only if every tabulated limit point passes AND the distribution nowhere crosses the log-linear curve between the tabulated points, tolerance 0.05 dB read towards lower epfd. Margins quoted as 'point margin' are the limit minus the computed level at each tabulated percentage; the 'curve margin' is the dB shift of the whole distribution that just clears the curve; the rule margin is the smaller of the two.

The deciding point is the limit point at the worst point margin: "max" is the maximum sample against the 0% row, which can only rise as the run lengthens; a percentage names a resolved percentile, which can move either way.

**EXCEEDED at 7 of 7 latitude(s) (0, 10, 20, 30, 40, 50, 60); worst margin -13.9 dB under the limit-curve rule -- sampled every 10 deg over 0..60, so a finer sweep can find worse between them -- power headroom -13.9 dB on per-beam TxEirpDbw (dB-for-dB)**

## The declaration, reused

Reused, not derived: the R set and the pfd mask are read back from `dataset\margin\steam-2` (`steam-2.operparams.json`, `steam-2.mask.lat10p0-ae1p0-svc-50to50.xml`), the artefacts of an earlier run of the same system. The saturated probe measures a configuration space, not a sample of one, and measured exactly depth-stable on STEAM-2 (identical R set and mask at 0.1 d and 1.0 d), so only the truth sweep and the examination run at this depth: this record is a convergence pair for T and E1 against the run it reuses.

- Depth of the derivation: that of the reused run; see its record.
- Reused set: min_exclude -45:22.0/-35:22.0/-25:22.0/-15:22.0/-5:22.0/5:22.0/15:22.0/25:22.0/35:22.0/45:22.0; min_elev -45:40.0/-35:40.0/-25:40.0/-15:40.0/-5:40.0/5:40.0/15:40.0/25:40.0/35:40.0/45:40.0; max_co_freq -45:4/-35:4/-25:4/-15:4/-5:4/5:4/15:4/25:4/35:4/45:4; max_co_freq_sat 59; min_angle es 0.0 / sat 2.2; es_lat -50..50

## E1 -- the examination against that declaration

The truth above does not move; E1 is what the examination sees when it reads the declared mask and the derived R set instead. The gap is the margin the declaration gives away -- and, at the same dB-for-dB rate, the operating power the system could have been licensed for.

| latitude | T margin (dB) | E1 margin (dB) | gap (dB) | deciding point T / E1 | curve margin T / E1 (dB) | E1 >= T |
|---|---|---|---|---|---|---|
| 0 | -11.5 | -18.5 | 7.0 | max / max | -10.6 / -17.7 | yes |
| 10 | -12.2 | -19.2 | 7.0 | max / max | -11.8 / -19.3 | yes |
| 20 | -12.8 | -20.1 | 7.3 | max / max | -9.9 / -19.6 | yes |
| 30 | -13.9 | -18.7 | 4.8 | max / max | -11.7 / -19.3 | yes |
| 40 | -10.1 | -18.9 | 8.8 | max / 0.286% | -10.0 / -19.3 | yes |
| 50 | -9.2 | -20.5 | 11.3 | max / 1% | -9.0 / -21.1 | yes |
| 60 | -9.5 | -21.5 | 12.0 | 1% / 1% | -9.5 / -21.5 | yes |

Margins and the gap in this table are point-wise; the curve margins beside them are the rule's second test.

**ADEQUATE: E1 >= T at every latitude; widest gap 12.0 dB**

## E1 on the S.1503-4 time step

The same examination sampled as S.1503-4 Sec. D4 prescribes: fine step 0.208 s (S.1503-4 Sec. D4.2: a 3.34 s pass across the 1.156 deg beam at 1150 km / 53.0 deg, N_hit 16); coarse step 4.160 s = 20 fine steps (Sec. D4.7.1). It covers the same 0.5 d as the 6 s step above and propagates the same trajectories, so the change between the two is the time step alone. The truth stays on the 6 s step, so the gap and the E1 >= T check above remain the matched-step comparison.

The dual time step follows Sec. D5.1.4.1 Sub-steps 6.1 to 6.3, each sample counted T_step / T_fine times (Step 24). The Recommendation words the fine-step region twice: Sec. D4.7.1 defines it as G_RX(phi) > min[Gmax - 30 dB, G_RX(alpha0[Latitude])], near the main beam or the exclusion-zone edge, while Sub-step 6.3 says a G_RX(phi) within 30 dB of peak. The dual columns use the Sec. D4.7.1 definition, the 30 dB column the Sub-step 6.3 wording; the fine column evaluates every fine step.

| latitude | E1 point margin, 6 s step (dB) | E1 point margin, S.1503-4 step, dual (dB) | change (dB) | deciding point | curve margin, dual (dB) | verdict, dual | E1 fine only (dB) | E1 dual, 30 dB reading (dB) | max epfd, 6 s / S.1503-4 (dB) | samples dual / 30 dB / fine |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | -18.5 | -19.0 | -0.5 | max | -17.4 | FAIL | -19.0 | -19.0 | -145.7 / -145.2 | 136233 / 17768 / 207692 |
| 10 | -19.2 | -19.7 | -0.5 | max | -19.1 | FAIL | -19.7 | -19.7 | -145.0 / -144.5 | 144992 / 18870 / 207692 |
| 20 | -20.1 | -20.3 | -0.2 | max | -19.5 | FAIL | -20.3 | -20.3 | -144.1 / -143.9 | 180427 / 20390 / 207692 |
| 30 | -18.7 | -18.7 | +0.0 | max | -19.1 | FAIL | -18.7 | -18.7 | -145.4 / -145.4 | 207692 / 24722 / 207692 |
| 40 | -18.9 | -18.8 | +0.1 | max | -19.4 | FAIL | -18.8 | -18.8 | -145.4 / -145.3 | 207692 / 31220 / 207692 |
| 50 | -20.5 | -20.5 | +0.0 | 1% | -20.9 | FAIL | -20.5 | -20.5 | -145.3 / -145.3 | 207692 / 56072 / 207692 |
| 60 | -21.5 | -21.4 | +0.1 | 1% | -21.3 | FAIL | -21.4 | -21.4 | -148.1 / -148.1 | 207692 / 102451 / 207692 |

**On the S.1503-4 time step the E1 point margin changes by -0.5 to +0.1 dB across the latitudes; worst -21.4 dB against -21.5 dB on the 6 s step.**

## Artefacts

The profile is this run's; the R set and the pfd mask are the reused run's, copied here so the directory stands alone:

- Operation profile (the truth as run): `dataset\margin\steam-2-6s\steam-2-6s.opprofile.json`
- Reused R set (S.1503-4 Part B): `dataset\margin\steam-2-6s\steam-2-6s.operparams.xml`
- Reused R set, designer format (open with the operating-parameters designer): `dataset\margin\steam-2-6s\steam-2-6s.operparams.json`
- Declared pfd mask: `dataset\margin\steam-2-6s\steam-2-6s.mask.reused.xml`
