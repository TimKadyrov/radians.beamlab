# Mask parity: beamlab's composition of the STEAM-2 case vs the filed STEAM-2B mask

*Produced by `dotnet run --project tests/radians.beamlab.checks -- parity "mask ntc_id 317520389 mask_id 150 17700-20200 MHz.xml" 5`.*
*Date: 2026-09-03. Wall clock 0.2 min.*

## The question

Is the beam composition written down for this case -- the operation profile plus the design document, composed exactly as a simulation composes them -- an operable system consistent with the operator's filed declaration? Beamlab exports its own satellite-frame mask from that composition; both masks go through the same dissection; the rules read off each are set side by side and the cells differenced on the common grid.

## The case as composed

- Shell: 32 x 50 at 1150 km, inclination 53.0 deg, inter-plane phase 1.9 deg.
- Rules: minimum elevation 40 deg, exclusion alpha 22 deg, Nco 4, selection Random.
- Payload (assumed where the filing is silent): power mode 'pfd', peak gain 35 dBi, Tx density -33 dBW/40 kHz, pattern floor 5 dBi, pattern scene default.
- Our export: latitude -50..50 step 5, az/el 1 deg; theirs: 179 blocks at 1 deg.

## Rules read off each mask

| rule | ours (composition) | theirs (filed) |
|---|---|---|
| minimum elevation (deg) | 30.8-37.3 | 39.9-40.0 |
| exclusion alpha (deg) | NaN-NaN, varying | 22.0-22.0, constant |
| alpha-limited latitudes | NaN..NaN (0) | -53..53 (107) |
| pfd cap (dB(W/m2)/40 kHz) | -129.8, spread 3.00 (range-shaped) | -130.2, spread 0.00 (flat) |
| e.i.r.p. density nadir / edge (dBW/40 kHz) | 2.4 / 2.5 | 2.0 / 5.0 |
| side-lobe floor (dB) | -155.5..-149.9 (20 below peak) | -181.4..-160.2 (30 below peak) |
| intermediate cells | 118345 | 0 |

## Cells on the common grid (classes by the filed mask)

- Plateau cells: 68082; ours minus theirs mean -0.1 dB, range -5.1..+1.2; 88.9% within 1 dB.
- Floor cells: 164955; mean +19.5 dB, range +15.4..+34.2; 0.0% within 3 dB.
- Radiated by us only: 2604; by them only: 0; intermediate (theirs between plateau and floor): 0.

| lat | plateau cells | ours - theirs, plateau mean (dB) | floor cells | ours - theirs, floor mean (dB) | ours only | theirs only |
|---|---|---|---|---|---|---|
| -50 | 5086 | -0.2 | 6011 | +20.8 | 124 | 0 |
| -45 | 4755 | -0.3 | 6342 | +20.9 | 124 | 0 |
| -40 | 4359 | -0.3 | 6738 | +20.5 | 124 | 0 |
| -35 | 3942 | -0.3 | 7155 | +20.0 | 124 | 0 |
| -30 | 3504 | -0.3 | 7593 | +19.5 | 124 | 0 |
| -25 | 3056 | -0.4 | 8041 | +18.7 | 124 | 0 |
| -20 | 2608 | +0.6 | 8489 | +19.1 | 124 | 0 |
| -15 | 2158 | +0.6 | 8939 | +18.6 | 124 | 0 |
| -10 | 1887 | +0.6 | 9210 | +18.9 | 124 | 0 |
| -5 | 1792 | +0.5 | 9305 | +19.1 | 124 | 0 |
| 0 | 1788 | +0.5 | 9309 | +19.2 | 124 | 0 |
| 5 | 1792 | +0.5 | 9305 | +19.1 | 124 | 0 |
| 10 | 1887 | +0.6 | 9210 | +18.9 | 124 | 0 |
| 15 | 2158 | +0.6 | 8939 | +18.6 | 124 | 0 |
| 20 | 2608 | +0.6 | 8489 | +19.1 | 124 | 0 |
| 25 | 3056 | -0.4 | 8041 | +18.7 | 124 | 0 |
| 30 | 3504 | -0.3 | 7593 | +19.5 | 124 | 0 |
| 35 | 3942 | -0.3 | 7155 | +20.0 | 124 | 0 |
| 40 | 4359 | -0.3 | 6738 | +20.5 | 124 | 0 |
| 45 | 4755 | -0.3 | 6342 | +20.9 | 124 | 0 |
| 50 | 5086 | -0.2 | 6011 | +20.8 | 124 | 0 |

## Reading it

- Rules agreeing (elevation, alpha, cap) means the composition obeys the same operating rules the filing encodes -- the declaration is a plausible envelope of THIS operable system.
- Plateau residue is power: a flat offset is the assumed gain/power pair against the filed cap; a range-shaped one is the power-control mode.
- Floor residue is the pattern: our side lobes against their 30 dB-down envelope. It prices how much of a margin figure on this case would be pattern assumption rather than rule.
- Cells radiated by one side only are the disc edge and any coverage disagreement; they should be a thin rim, not a region.
