# Margin decomposition: steam-2

*Produced by `dotnet run --project tests/radians.beamlab.checks -- decompose "STEAM-2.opprofile.json" "STEAM-2.orbitdesign.json" "steam-2" 0.1 60 0 60 10 steam-2`, 2026-09-14; wall clock 10.0 min.*

## What is decomposed

The design brief (Sec. 2) asks not for closeness between the examination and the simulation but for a decomposition of their gap: how many decibels come from the mask envelope, how many from the selection rules, how many from the worst-case geometry, each isolated by defeating it in turn. Three runs on one victim do that here:

- **T** -- the truth: every satellite visible from the earth station, the pfd its resolved beams actually put there, power-summed; no selection.
- **E_sel** -- the examination's selection rules (Sec. D5.1.4.1: the elevation gate, the exclusion zone, the MAX_CO_FREQ pick by highest contribution, MIN_ANGLE_AT_ES pruning, the main-beam always-include) applied to the same live values, read from the declared operating-parameter set.
- **E1** -- the examination proper: the same selection over the declared pfd mask.

So **E_sel − T** is what the selection rules do to the count (negative where they remove satellites the truth sums), **E1 − E_sel** is what the mask envelope adds over the live values, and **E1 − T** is the projection margin. The worst-case geometry component is zero by construction: all three runs read the same victim, so a consumer's own geometry search adds to these figures rather than being inside them.

System: STEAM-2 (WP 4A Doc 4A/653 + filed mask; pattern, layout, reuse assumed); 1600 satellites. Declaration: `steam-2.operparams.json` and `steam-2.mask.lat10p0-ae1p0-svc-50to50.xml` (min_exclude -45:22.0/-35:22.0/-25:22.0/-15:22.0/-5:22.0/5:22.0/15:22.0/25:22.0/35:22.0/45:22.0; min_elev -45:40.0/-35:40.0/-25:40.0/-15:40.0/-5:40.0/5:40.0/15:40.0/25:40.0/35:40.0/45:40.0; max_co_freq -45:4/-35:4/-25:4/-15:4/-5:4/5:4/15:4/25:4/35:4/45:4; max_co_freq_sat 59; min_angle es 0.0 / sat 2.2; es_lat -50..50). Victim: earth station at longitude 0, GSO satellite at 10 E, 1.00 m S.1428 dish at 18.15 GHz -- the row's own. Row: Article 22, TABLE 22-1B -- FSS 17800-18600 MHz, refbw 40 kHz, dish 1.00 m, regions XR1/XR2/XR3. Depth: 144 steps of 60 s (0.100 d), resolvable floor 0.694%.

Quotability: the three runs share one comb, so their DIFFERENCES at matched depth are quotable without a pair (the rule of 7-8 September); the absolute levels at this depth are not converged where the records at 1.0 d say they are not, and are given for orientation only.

## Per latitude: worst margins and the components at the deciding point

| latitude | T worst margin | E_sel worst margin | E1 worst margin | selection E_sel − T | envelope E1 − E_sel | total E1 − T | quiet steps T / E_sel / E1 |
|---|---|---|---|---|---|---|---|
| 0 | -9.1 | -9.1 | -15.6 | +0.0 | +6.5 | +6.5 | 0 / 0 / 0 |
| 10 | -4.8 | -4.7 | -13.7 | -0.1 | +9.0 | +8.9 | 0 / 0 / 0 |
| 20 | -7.4 | -7.4 | -18.1 | +0.0 | +10.7 | +10.7 | 0 / 0 / 0 |
| 30 | -5.7 | -5.6 | -12.5 | -0.1 | +6.9 | +6.8 | 0 / 0 / 0 |
| 40 | -6.9 | -6.9 | -16.9 | +0.0 | +10.0 | +10.0 | 0 / 0 / 0 |
| 50 | -11.9 | -11.9 | -24.0 | +0.0 | +12.1 | +12.1 | 0 / 0 / 0 |
| 60 | -11.7 | -11.7 | -21.9 | +0.0 | +10.2 | +10.2 | 0 / 0 / 0 |

Components are in level terms (dB of epfd), positive when the later stage sits higher; the worst margins are the row's minimum over its points.

## Per percentile

### 0 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -180.5 | -181.3 | -174.4 | -0.8 | +6.9 | +6.1 |
| 20 | -179.2 | -179.8 | -173.2 | -0.6 | +6.6 | +6.0 |
| 10 | -178.2 | -178.6 | -173.0 | -0.4 | +5.6 | +5.2 |
| 5 | -176.8 | -177.1 | -172.6 | -0.3 | +4.5 | +4.2 |
| 2 | -174.9 | -175.6 | -168.7 | -0.7 | +6.9 | +6.2 |
| 1 | -174.1 | -174.2 | -167.3 | -0.1 | +6.9 | +6.8 |
| max | -158.0 | -158.1 | -151.5 | 0.0 | +6.5 | +6.5 |

### 10 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -180.9 | -181.9 | -174.1 | -1.0 | +7.8 | +6.8 |
| 20 | -178.9 | -179.3 | -172.6 | -0.4 | +6.7 | +6.3 |
| 10 | -178.2 | -178.5 | -172.2 | -0.3 | +6.3 | +6.0 |
| 5 | -177.8 | -178.0 | -171.8 | -0.2 | +6.2 | +6.0 |
| 2 | -173.9 | -174.0 | -164.3 | -0.1 | +9.7 | +9.6 |
| 1 | -167.7 | -167.8 | -158.8 | -0.1 | +9.0 | +8.9 |
| max | -166.0 | -166.0 | -158.9 | 0.0 | +7.1 | +7.1 |

### 20 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -181.4 | -182.2 | -173.6 | -0.8 | +8.6 | +7.8 |
| 20 | -179.7 | -180.3 | -171.7 | -0.6 | +8.6 | +8.0 |
| 10 | -178.2 | -178.6 | -171.0 | -0.4 | +7.6 | +7.2 |
| 5 | -177.1 | -177.5 | -168.7 | -0.4 | +8.8 | +8.4 |
| 2 | -175.1 | -175.3 | -164.7 | -0.2 | +10.6 | +10.4 |
| 1 | -172.1 | -172.1 | -164.6 | 0.0 | +7.5 | +7.5 |
| max | -159.7 | -159.7 | -149.1 | 0.0 | +10.7 | +10.7 |

### 30 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -181.6 | -182.4 | -174.2 | -0.8 | +8.2 | +7.4 |
| 20 | -179.7 | -180.2 | -171.9 | -0.5 | +8.3 | +7.8 |
| 10 | -178.8 | -179.2 | -170.6 | -0.4 | +8.6 | +8.2 |
| 5 | -177.4 | -177.7 | -169.5 | -0.3 | +8.2 | +7.9 |
| 2 | -174.0 | -174.3 | -167.7 | -0.3 | +6.6 | +6.3 |
| 1 | -166.8 | -166.9 | -160.0 | -0.1 | +6.9 | +6.8 |
| max | -164.5 | -164.5 | -155.3 | 0.0 | +9.3 | +9.2 |

### 40 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -181.0 | -181.9 | -173.9 | -0.9 | +8.0 | +7.1 |
| 20 | -179.7 | -180.4 | -172.7 | -0.7 | +7.7 | +7.0 |
| 10 | -178.6 | -179.3 | -171.1 | -0.7 | +8.2 | +7.5 |
| 5 | -176.8 | -177.2 | -167.8 | -0.4 | +9.4 | +9.0 |
| 2 | -167.4 | -167.4 | -159.7 | 0.0 | +7.7 | +7.7 |
| 1 | -165.8 | -165.8 | -156.8 | 0.0 | +9.0 | +9.0 |
| max | -160.2 | -160.3 | -150.2 | 0.0 | +10.0 | +10.0 |

### 50 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -181.5 | -182.1 | -172.5 | -0.6 | +9.6 | +9.0 |
| 20 | -179.9 | -180.4 | -171.1 | -0.5 | +9.3 | +8.8 |
| 10 | -178.5 | -178.8 | -168.8 | -0.3 | +10.0 | +9.7 |
| 5 | -177.4 | -177.6 | -167.0 | -0.2 | +10.6 | +10.4 |
| 2 | -168.3 | -168.4 | -155.8 | -0.1 | +12.6 | +12.5 |
| 1 | -160.6 | -160.6 | -148.5 | 0.0 | +12.1 | +12.1 |
| max | -157.5 | -157.5 | -146.2 | 0.0 | +11.4 | +11.4 |

### 60 N

| % time exceeded | T | E_sel | E1 | selection | envelope | total |
|---|---|---|---|---|---|---|
| 100 | -275.4 | -275.4 | -275.4 | 0.0 | 0.0 | 0.0 |
| 50 | -185.8 | -186.7 | -169.7 | -0.9 | +17.0 | +16.1 |
| 20 | -183.5 | -184.0 | -168.5 | -0.5 | +15.5 | +15.0 |
| 10 | -180.2 | -180.4 | -166.7 | -0.2 | +13.7 | +13.5 |
| 5 | -175.1 | -175.1 | -161.9 | 0.0 | +13.2 | +13.2 |
| 2 | -164.6 | -164.6 | -152.4 | 0.0 | +12.2 | +12.2 |
| 1 | -160.8 | -160.8 | -150.6 | 0.0 | +10.2 | +10.2 |
| max | -158.3 | -158.3 | -148.1 | 0.0 | +10.2 | +10.2 |

## Reading it

- At the 10% point the selection component ranges -0.7 to -0.2 dB and the envelope component +5.6 to +13.7 dB across the latitudes. Where the selection component is negative the rules remove satellites the truth sums (the cap and the gates); where it is near zero they leave the count alone and the whole gap is the envelope's.
- The envelope component is the price of describing every configuration the system can reach by one per-direction maximum; the selection component is the price, or the credit, of describing the operator's scheduler by a count. Both are what the format discards, in the brief's words, and the first is the one the derivation's granularity levers act on.
- With no gate declared, E_sel equals T bin for bin (V55): the decomposition's zero is a checked invariant, not an assumption.
