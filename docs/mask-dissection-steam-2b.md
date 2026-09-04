# Mask dissection: STEAM-2B (ntc_id 317520389, mask_id 150)

*Produced by `dotnet run --project tests/radians.beamlab.checks -- dissect "mask ntc_id 317520389 mask_id 150 17700-20200 MHz.xml" 1150`.*
*Date: 2026-09-03. Wall clock 0.1 min.*

## What is read, and how

A filed S.1503-4 pfd mask in the satellite-frame (azimuth/elevation) form: 179 latitude blocks, 17700-20200 MHz, reference bandwidth 40 kHz. Each cell is a direction the satellite may radiate toward at a given sub-satellite latitude. The cells are mapped to the ground through the same frame the examination reads the mask with (NED at the sub-satellite point: azimuth = atan2(east, down), elevation = asin(north)), for a satellite at 1150 km, and each ground point is given its satellite elevation angle and its GSO-arc alpha. The mask's main-beam plateau (within 3 dB of the block peak) is the set of allowed targets; the floor (more than 20 dB below the peak) is where no beam points. The plateau's boundaries in ground elevation and in alpha are the operating rules -- read per latitude, so a latitude-dependent exclusion would show as a varying alpha boundary, a constant rule as a constant one seen through geometry.

## Structure

- Cells reaching the Earth: 1984931; on the plateau 729052, intermediate 0 -- a **two-level mask**: main beam at -130.2 dB, side-lobe floor -181.4..-160.2 dB (30 dB below the peak, falling toward the horizon with range). Every block radiates over the whole visible Earth; the operating rules are expressed as levels, not as the -1000 hole.
- Distinct integer levels: 24.
- Byte-identical block runs (the rules stop depending on latitude there): -89..-54; 54..89.

## Per latitude

Plateau = allowed targets. "floor: max alpha (elev ok)" is the largest alpha among excluded targets that clear the elevation floor -- the exclusion boundary seen from below; "max elev (alpha ok)" the largest elevation among excluded targets that clear the alpha floor -- the elevation boundary seen from below.

| lat | peak (dB) | plateau cells | plateau min ground elev | plateau min alpha | plateau max off-nadir | floor max alpha (elev ok) | floor max elev (alpha ok) | plateau N-S pointing extent |
|---|---|---|---|---|---|---|---|---|
| -85 | -130.2 | 5261 | 40.0 | 106.8 | 40.4 | - | 39.9 | -40..40 |
| -80 | -130.2 | 5261 | 40.0 | 81.5 | 40.4 | - | 39.9 | -40..40 |
| -75 | -130.2 | 5261 | 40.0 | 53.4 | 40.4 | - | 39.9 | -40..40 |
| -70 | -130.2 | 5261 | 40.0 | 38.4 | 40.4 | - | 39.9 | -40..40 |
| -65 | -130.2 | 5261 | 40.0 | 33.4 | 40.4 | - | 39.9 | -40..40 |
| -60 | -130.2 | 5261 | 40.0 | 28.3 | 40.4 | - | 39.9 | -40..40 |
| -55 | -130.2 | 5261 | 40.0 | 23.1 | 40.4 | - | 39.9 | -40..40 |
| -50 | -130.2 | 5086 | 40.0 | 22.0 | 40.4 | 21.9 | 39.9 | -35..40 |
| -45 | -130.2 | 4755 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -30..40 |
| -40 | -130.2 | 4359 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -24..40 |
| -35 | -130.2 | 3942 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -19..40 |
| -30 | -130.2 | 3504 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -13..40 |
| -25 | -130.2 | 3056 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -7..40 |
| -20 | -131.2 | 2608 | 40.0 | 22.0 | 40.4 | 21.9 | 39.9 | -1..40 |
| -15 | -131.2 | 2158 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | 5..40 |
| -10 | -131.2 | 1887 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -40..40 |
| -5 | -131.2 | 1792 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -40..40 |
| 0 | -131.2 | 1788 | 40.0 | 22.0 | 40.4 | 21.2 | 39.9 | -40..40 |
| 5 | -131.2 | 1792 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -40..40 |
| 10 | -131.2 | 1887 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -40..40 |
| 15 | -131.2 | 2158 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -40..-5 |
| 20 | -131.2 | 2608 | 40.0 | 22.0 | 40.4 | 21.9 | 39.9 | -40..1 |
| 25 | -130.2 | 3056 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -40..7 |
| 30 | -130.2 | 3504 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -40..13 |
| 35 | -130.2 | 3942 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -40..19 |
| 40 | -130.2 | 4359 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -40..24 |
| 45 | -130.2 | 4755 | 40.0 | 22.0 | 40.4 | 22.0 | 39.9 | -40..30 |
| 50 | -130.2 | 5086 | 40.0 | 22.0 | 40.4 | 21.9 | 39.9 | -40..35 |
| 55 | -130.2 | 5261 | 40.0 | 23.1 | 40.4 | - | 39.9 | -40..40 |
| 60 | -130.2 | 5261 | 40.0 | 28.3 | 40.4 | - | 39.9 | -40..40 |
| 65 | -130.2 | 5261 | 40.0 | 33.4 | 40.4 | - | 39.9 | -40..40 |
| 70 | -130.2 | 5261 | 40.0 | 38.4 | 40.4 | - | 39.9 | -40..40 |
| 75 | -130.2 | 5261 | 40.0 | 53.4 | 40.4 | - | 39.9 | -40..40 |
| 80 | -130.2 | 5261 | 40.0 | 81.5 | 40.4 | - | 39.9 | -40..40 |
| 85 | -130.2 | 5261 | 40.0 | 106.8 | 40.4 | - | 39.9 | -40..40 |

## The operating rules this mask encodes

- **Minimum elevation ~ 39.9-40.0 deg** (bracketed by the grid): the plateau's outer edge sits at the same ground elevation at every latitude.
- **GSO exclusion alpha ~ 22.0-22.0 deg**, alpha-limited at latitudes -53..53 and inert beyond (there every target clearing the elevation floor also clears alpha). The boundary alpha is the SAME at every alpha-limited latitude: one constant rule, whose hole in (az, el) changes shape with latitude purely through geometry -- a per-latitude MIN_EXCLUDE table would be flat.
- **A flat pfd cap of -130.2 dB(W/m2) per 40 kHz, independent of range** (plateau spread 0.00 dB out to the elevation edge): constant-boresight-PFD power control, not a constant e.i.r.p. seen through spreading. The boresight e.i.r.p. density therefore runs from 2.0 dBW/40 kHz at nadir to 5.0 at the edge (slant range 1624 km), with the side-lobe envelope 30 dB down.

## Against the filed R set

The operating-parameter XML of the same filing declares min_exclude 22 deg (all orbits, one latitude row) and no elev_angle at all; the contribution text states a 40 deg minimum elevation as its simulation assumption. The mask above says which of those the payload actually enforces -- and per latitude.
