# Margin decomposition: steam-2-1d

*Produced by `dotnet run --project tests/radians.beamlab.checks -- decompose "STEAM-2.opprofile.json" "STEAM-2.orbitdesign.json" "steam-2" 1 60 0 60 10 steam-2-1d`, 2026-09-20; wall clock 97.8 min.*

## What is decomposed

The design brief (Sec. 2) asks not for closeness between the examination and the simulation but for a decomposition of their gap: how many decibels come from the mask envelope, how many from the selection rules, how many from the worst-case geometry, each isolated by defeating it in turn. Three runs on one victim do that here:

- **T** -- the truth: every satellite visible from the earth station, the pfd its resolved beams actually put there, power-summed; no selection.
- **E_sel** -- the examination's selection rules (Sec. D5.1.4.1: the elevation gate, the exclusion zone, the MAX_CO_FREQ pick by highest contribution, MIN_ANGLE_AT_ES pruning, the main-beam always-include) applied to the same live values, read from the declared operating-parameter set.
- **E1** -- the examination proper: the same selection over the declared pfd mask.

So **E_sel − T** is what the selection rules do to the count (negative where they remove satellites the truth sums), **E1 − E_sel** is what the mask envelope adds over the live values, and **E1 − T** is the projection margin. The worst-case geometry component is zero by construction: all three runs read the same victim, so a consumer's own geometry search adds to these figures rather than being inside them.

System: STEAM-2 (WP 4A Doc 4A/653 + filed mask; pattern, layout, reuse assumed); 1600 satellites. Declaration: `steam-2.operparams.json` and `steam-2.mask.lat10p0-ae1p0-svc-50to50.xml` (min_exclude -45:22.0/-35:22.0/-25:22.0/-15:22.0/-5:22.0/5:22.0/15:22.0/25:22.0/35:22.0/45:22.0; min_elev -45:40.0/-35:40.0/-25:40.0/-15:40.0/-5:40.0/5:40.0/15:40.0/25:40.0/35:40.0/45:40.0; max_co_freq -45:4/-35:4/-25:4/-15:4/-5:4/5:4/15:4/25:4/35:4/45:4; max_co_freq_sat 59; min_angle es 0.0 / sat 2.2; es_lat -50..50). Victim: earth station at longitude 0, GSO satellite at 10 E, 1.00 m S.1428 dish at 18.15 GHz -- the row's own. Row: Article 22, TABLE 22-1B -- FSS 17800-18600 MHz, refbw 40 kHz, dish 1.00 m, regions XR1/XR2/XR3. Depth: 1440 steps of 60 s (1.000 d), resolvable floor 0.069%.

Margins in this record are point-wise (the limit minus the computed level at each tabulated percentage); no verdict is taken here, so the limit-curve rule's second test does not enter -- see the compliance records for verdicts under the rule.

Quotability: the three runs share one comb, so their DIFFERENCES at matched depth are quotable without a pair (the rule of 7-8 September); the absolute levels at this depth are not converged where the records at 1.0 d say they are not, and are given for orientation only.

## Per latitude: worst margins and the components at the deciding point

| latitude | T worst margin | E_sel worst margin | E1 worst margin | selection E_sel − T | envelope E1 − E_sel | total E1 − T | quiet steps T / E_sel / E1 |
|---|---|---|---|---|---|---|---|
| 0 | -9.3 | -9.3 | -18.1 | +0.0 | +8.8 | +8.8 | 0 / 0 / 0 |
| 10 | -10.2 | -10.2 | -17.7 | +0.0 | +7.5 | +7.5 | 0 / 0 / 0 |
| 20 | -9.5 | -9.5 | -18.0 | +0.0 | +8.5 | +8.5 | 0 / 0 / 0 |
| 30 | -10.9 | -10.9 | -18.6 | +0.0 | +7.7 | +7.7 | 0 / 0 / 0 |
| 40 | -10.3 | -10.3 | -18.8 | +0.0 | +8.5 | +8.5 | 0 / 0 / 0 |
| 50 | -9.3 | -9.3 | -20.0 | +0.0 | +10.7 | +10.7 | 0 / 0 / 0 |
| 60 | -10.4 | -10.4 | -21.4 | +0.0 | +11.0 | +11.0 | 0 / 0 / 0 |

Components are in level terms (dB of epfd), positive when the later stage sits higher; the worst margins are the row's minimum over its points.

## Per percentile

### 0 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -180.6 | -181.5 | -174.4 | -0.9 | +7.1 | +6.2 |
| 20 | -178.9 | -179.4 | -173.3 | -0.5 | +6.1 | +5.6 |
| 10 | -177.7 | -178.1 | -172.9 | -0.4 | +5.2 | +4.8 |
| 5 | -176.9 | -177.3 | -171.9 | -0.4 | +5.4 | +5.0 |
| 2 | -175.6 | -175.7 | -169.2 | -0.1 | +6.5 | +6.4 |
| 1 | -174.2 | -174.4 | -166.7 | -0.2 | +7.7 | +7.5 |
| 0.5 | -169.3 | -169.3 | -159.4 | 0.0 | +9.9 | +9.9 |
| 0.286 | -164.7 | -164.7 | -159.3 | 0.0 | +5.4 | +5.4 |
| 0.2 | -158.9 | -158.9 | -151.4 | 0.0 | +7.5 | +7.5 |
| 0.1 | -157.9 | -157.9 | -150.6 | 0.0 | +7.3 | +7.3 |
| max | -154.9 | -154.9 | -146.0 | 0.0 | +8.9 | +8.8 |

### 10 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -180.9 | -181.6 | -174.1 | -0.7 | +7.5 | +6.8 |
| 20 | -179.1 | -179.5 | -172.8 | -0.4 | +6.7 | +6.3 |
| 10 | -178.0 | -178.4 | -172.3 | -0.4 | +6.1 | +5.7 |
| 5 | -177.1 | -177.4 | -171.6 | -0.3 | +5.8 | +5.5 |
| 2 | -175.9 | -176.1 | -168.7 | -0.2 | +7.4 | +7.2 |
| 1 | -173.7 | -173.8 | -165.4 | -0.1 | +8.4 | +8.3 |
| 0.5 | -167.8 | -167.8 | -158.8 | 0.0 | +9.0 | +9.0 |
| 0.286 | -165.8 | -165.9 | -158.2 | -0.1 | +7.7 | +7.6 |
| 0.2 | -161.3 | -161.3 | -150.2 | 0.0 | +11.1 | +11.1 |
| 0.1 | -155.0 | -155.0 | -147.3 | 0.0 | +7.7 | +7.7 |
| max | -154.0 | -154.0 | -146.5 | 0.0 | +7.5 | +7.5 |

### 20 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -181.4 | -182.4 | -173.2 | -1.0 | +9.2 | +8.2 |
| 20 | -179.8 | -180.4 | -171.5 | -0.6 | +8.9 | +8.3 |
| 10 | -178.4 | -178.8 | -170.2 | -0.4 | +8.6 | +8.2 |
| 5 | -177.2 | -177.5 | -168.9 | -0.3 | +8.6 | +8.3 |
| 2 | -175.1 | -175.4 | -166.8 | -0.3 | +8.6 | +8.3 |
| 1 | -169.7 | -169.7 | -163.4 | 0.0 | +6.3 | +6.3 |
| 0.5 | -166.3 | -166.3 | -156.2 | 0.0 | +10.1 | +10.1 |
| 0.286 | -159.9 | -159.9 | -150.9 | 0.0 | +9.0 | +9.0 |
| 0.2 | -158.5 | -158.5 | -148.9 | 0.0 | +9.6 | +9.6 |
| 0.1 | -158.4 | -158.4 | -147.0 | 0.0 | +11.4 | +11.4 |
| max | -154.6 | -154.6 | -146.1 | 0.0 | +8.5 | +8.5 |

### 30 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -181.3 | -182.2 | -174.3 | -0.9 | +7.9 | +7.0 |
| 20 | -179.8 | -180.4 | -171.8 | -0.6 | +8.6 | +8.0 |
| 10 | -178.7 | -179.2 | -170.5 | -0.5 | +8.7 | +8.2 |
| 5 | -177.5 | -177.8 | -169.5 | -0.3 | +8.3 | +8.0 |
| 2 | -174.6 | -174.8 | -167.0 | -0.2 | +7.8 | +7.6 |
| 1 | -170.7 | -170.8 | -165.2 | -0.1 | +5.6 | +5.5 |
| 0.5 | -161.3 | -161.4 | -153.9 | -0.1 | +7.5 | +7.4 |
| 0.286 | -156.6 | -156.6 | -148.4 | 0.0 | +8.2 | +8.2 |
| 0.2 | -153.8 | -153.8 | -147.6 | 0.0 | +6.2 | +6.2 |
| 0.1 | -153.1 | -153.1 | -145.7 | 0.0 | +7.4 | +7.4 |
| max | -153.2 | -153.2 | -145.7 | 0.0 | +7.5 | +7.5 |

### 40 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -181.1 | -182.0 | -173.9 | -0.9 | +8.1 | +7.2 |
| 20 | -179.6 | -180.2 | -172.7 | -0.6 | +7.5 | +6.9 |
| 10 | -178.5 | -179.0 | -171.5 | -0.5 | +7.5 | +7.0 |
| 5 | -177.0 | -177.3 | -168.8 | -0.3 | +8.5 | +8.2 |
| 2 | -170.6 | -170.6 | -159.8 | 0.0 | +10.8 | +10.8 |
| 1 | -166.7 | -166.8 | -157.9 | -0.1 | +8.9 | +8.8 |
| 0.5 | -161.8 | -161.8 | -152.6 | 0.0 | +9.2 | +9.2 |
| 0.286 | -156.7 | -156.7 | -148.5 | 0.0 | +8.2 | +8.2 |
| 0.2 | -156.5 | -156.5 | -146.1 | 0.0 | +10.4 | +10.4 |
| 0.1 | -153.9 | -153.9 | -145.9 | 0.0 | +8.0 | +8.0 |
| max | -153.9 | -154.0 | -145.4 | 0.0 | +8.6 | +8.6 |

### 50 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -181.6 | -182.3 | -172.6 | -0.7 | +9.7 | +9.0 |
| 20 | -179.8 | -180.3 | -170.8 | -0.5 | +9.5 | +9.0 |
| 10 | -178.4 | -178.7 | -168.7 | -0.3 | +10.0 | +9.7 |
| 5 | -176.4 | -176.6 | -165.6 | -0.2 | +11.0 | +10.8 |
| 2 | -169.5 | -169.5 | -159.4 | 0.0 | +10.1 | +10.1 |
| 1 | -163.2 | -163.2 | -152.5 | 0.0 | +10.7 | +10.7 |
| 0.5 | -160.0 | -160.0 | -148.9 | 0.0 | +11.1 | +11.1 |
| 0.286 | -159.1 | -159.1 | -148.5 | 0.0 | +10.6 | +10.6 |
| 0.2 | -158.4 | -158.4 | -148.1 | 0.0 | +10.3 | +10.3 |
| 0.1 | -157.4 | -157.4 | -146.9 | 0.0 | +10.5 | +10.5 |
| max | -156.9 | -156.9 | -146.2 | 0.0 | +10.8 | +10.8 |

### 60 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -185.9 | -186.9 | -169.6 | -1.0 | +17.3 | +16.3 |
| 20 | -182.5 | -183.0 | -168.2 | -0.5 | +14.8 | +14.3 |
| 10 | -179.7 | -179.9 | -166.6 | -0.2 | +13.3 | +13.1 |
| 5 | -172.9 | -172.9 | -161.8 | 0.0 | +11.1 | +11.1 |
| 2 | -164.9 | -164.9 | -153.1 | 0.0 | +11.8 | +11.8 |
| 1 | -162.1 | -162.1 | -151.1 | 0.0 | +11.0 | +11.0 |
| 0.5 | -160.8 | -160.8 | -149.5 | 0.0 | +11.3 | +11.3 |
| 0.286 | -159.1 | -159.1 | -148.5 | 0.0 | +10.6 | +10.6 |
| 0.2 | -158.1 | -158.1 | -148.1 | 0.0 | +10.0 | +10.0 |
| 0.1 | -158.0 | -158.0 | -148.1 | 0.0 | +9.9 | +9.9 |
| max | -157.1 | -157.1 | -148.1 | 0.0 | +9.0 | +9.0 |

## Reading it

- At the 10% point the selection component ranges -0.5 to -0.2 dB and the envelope component +5.2 to +13.3 dB across the latitudes. Where the selection component is negative the rules remove satellites the truth sums (the cap and the gates); where it is near zero they leave the count alone and the whole gap is the envelope's.
- The envelope component is the price of describing every configuration the system can reach by one per-direction maximum; the selection component is the price, or the credit, of describing the operator's scheduler by a count. Both are what the format discards, in the brief's words, and the first is the one the derivation's granularity levers act on.
- With no gate declared, E_sel equals T bin for bin (V55): the decomposition's zero is a checked invariant, not an assumption.
