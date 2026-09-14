# Case (a) of the 11.32A concept note, measured: protecting the arc against the Table 22-1C values

*Produced by `dotnet run --project tests/radians.beamlab.checks -- arcshield `, 2026-09-13; wall clock 1.7 min.*

## The question

The concept note's case (a) proposes, for bands adjacent to those with Article 22 limits, that the Table 22-1 values of the adjacent band be applied to a non-GSO system with a geostationary victim, so that an operator who protects the geostationary arc has a definite route to a favourable finding. That rests on two measurable claims: a system that protects the arc clears the transferred values with room, and one that does not protect it fails them. This record measures both on one constellation with one payload.

## The construction

- The constellation: the validation dataset's three shells (A: 1 200 km / 55 deg, 4 x 8, repeating; B: 900 km / 87 deg, 6 x 6; C: elliptical 800 x 4 000 km / 63.4 deg, 2 x 4), 76 satellites, cells of 450 km served by the family's payload at -40.0 dB against the dataset's mask 1 -- one power level for all three systems, chosen so that the protecting systems sit near the limit; every margin below moves dB for dB with it, and the differences between systems do not move at all.
- Three systems from that one payload, each a pfd mask in the alpha/deltaLongitude form beside an arrays-only operating-parameter set (minimum elevation 10 deg, co-frequency cap 2):
  - notch 0 deg: does not protect the arc: no zone, the arc lit -- the reachable envelope with no gate; the declaration says no zone, so the pair is consistent too, and the arc is lit. File `arcshield_notch0_tx-40.xml`.
  - notch 8 deg: protects the arc: zone 8 deg declared and written into the mask -- the reachable envelope composed with a boresight gate of that angle and the Sec. C1 -1000 null written into the alpha axis inside it, so mask and declaration describe one system (consistent by this producer's grader). File `arcshield_notch8_tx-40.xml`.
  - notch 22 deg: protects the arc: zone 22 deg declared and written into the mask -- the reachable envelope composed with a boresight gate of that angle and the Sec. C1 -1000 null written into the alpha axis inside it, so mask and declaration describe one system (consistent by this producer's grader). File `arcshield_notch22_tx-40.xml`.
- The examination: S.1503-4 Sec. D5.1.4.1 (the classic downlink algorithm) at victims 0 N, 10 N, 20 N, 30 N, 40 N, 50 N, 60 N, earth station at longitude 0, wanted GSO satellite at 10 E, the row's own reference dish in the S.1428 pattern evaluated at 19.05 GHz (the centre of 18.8-19.3 GHz, the band the row would be transferred to); 5760 steps of 30 s (48 h) with the 24 h prefix as the extension pair. Worst margin = the minimum over the row's points of (limit epfd minus the epfd exceeded for at most the point's percentage), in the examination's 0.1 dB bins; positive is room.
- The rows: every plain FSS row of Table 22-1C at 19.7-20.2 GHz in the BR limits database, per reference dish and per reference bandwidth; the 1 MHz rows read the 40 kHz mask scaled by 10 log(1000/40) = 13.98 dB (a flat spectrum, the reference implementation's convention).

## The rows

- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 0.70 m, regions XR1/XR2/XR3: -187.4 dB(W/m2) for 100%; -182.0 dB(W/m2) for 28.57%; -172.0 dB(W/m2) for 2.857%; -154.0 dB(W/m2) for 0.017%; -154.0 dB(W/m2) for 0%.
- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 1000 kHz, dish 0.70 m, regions XR1/XR2/XR3: -173.4 dB(W/m2) for 100%; -168.0 dB(W/m2) for 28.57%; -158.0 dB(W/m2) for 2.857%; -140.0 dB(W/m2) for 0.017%; -140.0 dB(W/m2) for 0%.
- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 0.90 m, regions XR1/XR2/XR3: -190.4 dB(W/m2) for 100%; -181.4 dB(W/m2) for 9%; -170.4 dB(W/m2) for 0.2%; -168.6 dB(W/m2) for 0.2%; -165.0 dB(W/m2) for 0.057%; -160.0 dB(W/m2) for 0.057%; -154.0 dB(W/m2) for 0.003%; -154.0 dB(W/m2) for 0%.
- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 1000 kHz, dish 0.90 m, regions XR1/XR2/XR3: -176.4 dB(W/m2) for 100%; -167.4 dB(W/m2) for 9%; -156.4 dB(W/m2) for 0.2%; -154.6 dB(W/m2) for 0.2%; -151.0 dB(W/m2) for 0.057%; -146.0 dB(W/m2) for 0.057%; -140.0 dB(W/m2) for 0.003%; -140.0 dB(W/m2) for 0%.
- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 2.50 m, regions XR1/XR2/XR3: -196.4 dB(W/m2) for 100%; -162.0 dB(W/m2) for 0.02%; -154.0 dB(W/m2) for 0.00057%; -154.0 dB(W/m2) for 0%.
- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 1000 kHz, dish 2.50 m, regions XR1/XR2/XR3: -182.4 dB(W/m2) for 100%; -148.0 dB(W/m2) for 0.02%; -140.0 dB(W/m2) for 0.00057%; -140.0 dB(W/m2) for 0%.
- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 5.00 m, regions XR1/XR2/XR3: -200.4 dB(W/m2) for 100%; -189.4 dB(W/m2) for 10%; -187.8 dB(W/m2) for 6%; -184.0 dB(W/m2) for 2.857%; -175.0 dB(W/m2) for 0.114%; -164.2 dB(W/m2) for 0.01%; -154.6 dB(W/m2) for 0.001%; -154.0 dB(W/m2) for 0.0008%; -154.0 dB(W/m2) for 0%.
- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 1000 kHz, dish 5.00 m, regions XR1/XR2/XR3: -186.4 dB(W/m2) for 100%; -175.4 dB(W/m2) for 10%; -173.8 dB(W/m2) for 6%; -170.0 dB(W/m2) for 2.857%; -161.0 dB(W/m2) for 0.114%; -150.2 dB(W/m2) for 0.01%; -140.6 dB(W/m2) for 0.001%; -140.0 dB(W/m2) for 0.0008%; -140.0 dB(W/m2) for 0%.

## The answer, row by row

For each row: the worst margin over the victims of each system at the common payload, the victim and the binding point, the prefix's figure and how far it moved; then the DAYLIGHT -- the protecting system's worst margin minus the non-protecting system's, which is what protecting the arc is worth at the deciding point, independent of the payload level -- and the payload at which the protecting system would just clear the row, with the non-protecting system's margin at that same payload.

| row | system | worst margin (dB) | at | binding point | prefix (24 h) | moved | daylight vs no notch (dB) | boresight pfd at which this system just clears, dB(W/(m2 MHz)) | non-protector at the protector's clearing level (dB) |
|---|---|---|---|---|---|---|---|---|---|
| 0.70 m, 40 kHz | notch 0 | -20.3 | 40 N | 0.017% | -20.3 | +0.0 | - | -140.1 | - |
| 0.70 m, 40 kHz | notch 8 | -2.5 | 60 N | 28.57% | -2.6 | +0.1 | +17.8 | -122.3 | -17.8 (FAIL) |
| 0.70 m, 40 kHz | notch 22 | -0.7 | 60 N | 28.57% | -0.7 | +0.0 | +19.6 | -120.5 | -19.6 (FAIL) |
| 0.70 m, 1000 kHz | notch 0 | -20.3 | 40 N | 0.017% | -20.3 | +0.0 | - | -140.1 | - |
| 0.70 m, 1000 kHz | notch 8 | -2.5 | 60 N | 28.57% | -2.5 | +0.0 | +17.8 | -122.3 | -17.8 (FAIL) |
| 0.70 m, 1000 kHz | notch 22 | -0.7 | 60 N | 28.57% | -0.7 | +0.0 | +19.6 | -120.5 | -19.6 (FAIL) |
| 0.90 m, 40 kHz | notch 0 | -28.2 | 40 N | 0.057% | -30.5 | +2.3 | - | -148.0 | - |
| 0.90 m, 40 kHz | notch 8 | -2.2 | 60 N | 9% | -2.2 | +0.0 | +26.0 | -122.0 | -26.0 (FAIL) |
| 0.90 m, 40 kHz | notch 22 | +0.4 | 60 N | 9% | +0.4 | +0.0 | +28.6 | -119.4 | -28.6 (FAIL) |
| 0.90 m, 1000 kHz | notch 0 | -28.1 | 40 N | 0.057% | -30.5 | +2.4 | - | -147.9 | - |
| 0.90 m, 1000 kHz | notch 8 | -2.2 | 60 N | 9% | -2.2 | +0.0 | +25.9 | -122.0 | -25.9 (FAIL) |
| 0.90 m, 1000 kHz | notch 22 | +0.4 | 60 N | 9% | +0.5 | -0.1 | +28.5 | -119.4 | -28.5 (FAIL) |
| 2.50 m, 40 kHz | notch 0 | -26.9 | 40 N | 0.02% | -27.8 | +0.9 | - | -146.7 | - |
| 2.50 m, 40 kHz | notch 8 | +18.2 | 10 N | 0.02% | +17.8 | +0.4 | +45.1 | -101.6 | -45.1 (FAIL) |
| 2.50 m, 40 kHz | notch 22 | +25.6 | 60 N | 0.02% | +25.3 | +0.3 | +52.5 | -94.2 | -52.5 (FAIL) |
| 2.50 m, 1000 kHz | notch 0 | -26.9 | 40 N | 0.02% | -27.8 | +0.9 | - | -146.7 | - |
| 2.50 m, 1000 kHz | notch 8 | +18.2 | 10 N | 0.02% | +17.8 | +0.4 | +45.1 | -101.6 | -45.1 (FAIL) |
| 2.50 m, 1000 kHz | notch 22 | +25.6 | 60 N | 0.02% | +25.3 | +0.3 | +52.5 | -94.2 | -52.5 (FAIL) |
| 5.00 m, 40 kHz | notch 0 | -28.4 | 40 N | 0.01% | -28.4 | +0.0 | - | -148.2 | - |
| 5.00 m, 40 kHz | notch 8 | +4.9 | 50 N | 10% | +4.9 | +0.0 | +33.3 | -114.9 | -33.3 (FAIL) |
| 5.00 m, 40 kHz | notch 22 | +7.4 | 60 N | 10% | +7.4 | +0.0 | +35.8 | -112.4 | -35.8 (FAIL) |
| 5.00 m, 1000 kHz | notch 0 | -28.4 | 40 N | 0.01% | -28.4 | +0.0 | - | -148.2 | - |
| 5.00 m, 1000 kHz | notch 8 | +4.9 | 60 N | 10% | +4.8 | +0.1 | +33.3 | -114.9 | -33.3 (FAIL) |
| 5.00 m, 1000 kHz | notch 22 | +7.4 | 60 N | 10% | +7.5 | -0.1 | +35.8 | -112.4 | -35.8 (FAIL) |

## Per victim, the 40 kHz rows

### Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 0.70 m, regions XR1/XR2/XR3

| victim | notch 0: worst margin / binding point | notch 8: worst margin / binding point | notch 22: worst margin / binding point |
|---|---|---|---|
| 0 N | -18.5 / 0.017% | +0.6 / 28.57% | +1.3 / 28.57% |
| 10 N | -11.8 / 0.017% | +0.4 / 28.57% | +1.0 / 28.57% |
| 20 N | -17.0 / 0.017% | +0.0 / 28.57% | +0.5 / 28.57% |
| 30 N | -19.4 / 0.017% | -0.7 / 28.57% | -0.1 / 28.57% |
| 40 N | -20.3 / 0.017% | -1.7 / 28.57% | -0.4 / 28.57% |
| 50 N | -14.3 / 0.017% | -2.3 / 28.57% | -0.6 / 28.57% |
| 60 N | -18.2 / 0.017% | -2.5 / 28.57% | -0.7 / 28.57% |

### Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 0.90 m, regions XR1/XR2/XR3

| victim | notch 0: worst margin / binding point | notch 8: worst margin / binding point | notch 22: worst margin / binding point |
|---|---|---|---|
| 0 N | -17.2 / 0.003% | +0.6 / 9% | +2.6 / 9% |
| 10 N | -16.9 / 0.057% | -0.1 / 9% | +2.4 / 9% |
| 20 N | -14.9 / 0.003% | -0.8 / 9% | +2.2 / 9% |
| 30 N | -18.7 / 0.003% | -0.8 / 9% | +1.8 / 9% |
| 40 N | -28.2 / 0.057% | -1.6 / 9% | +1.4 / 9% |
| 50 N | -16.2 / 0.057% | -2.1 / 9% | +1.5 / 9% |
| 60 N | -26.1 / 0.057% | -2.2 / 9% | +0.4 / 9% |

### Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 2.50 m, regions XR1/XR2/XR3

| victim | notch 0: worst margin / binding point | notch 8: worst margin / binding point | notch 22: worst margin / binding point |
|---|---|---|---|
| 0 N | -3.6 / 0.00057% | +19.3 / 0.02% | +26.2 / 0.02% |
| 10 N | -1.5 / 0.02% | +18.2 / 0.02% | +25.8 / 0.02% |
| 20 N | -4.5 / 0.02% | +18.8 / 0.02% | +26.0 / 0.02% |
| 30 N | -7.6 / 0.00057% | +18.6 / 0.02% | +26.2 / 0.02% |
| 40 N | -26.9 / 0.02% | +18.5 / 0.02% | +26.2 / 0.02% |
| 50 N | -3.2 / 0.02% | +19.1 / 0.02% | +26.1 / 0.02% |
| 60 N | -15.9 / 0.00057% | +19.5 / 0.02% | +25.6 / 0.02% |

### Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 5.00 m, regions XR1/XR2/XR3

| victim | notch 0: worst margin / binding point | notch 8: worst margin / binding point | notch 22: worst margin / binding point |
|---|---|---|---|
| 0 N | -7.1 / 0.01% | +7.8 / 10% | +9.7 / 10% |
| 10 N | -2.8 / 0.114% | +7.2 / 10% | +9.4 / 10% |
| 20 N | -4.6 / 0.114% | +6.6 / 10% | +9.3 / 10% |
| 30 N | -12.3 / 0.01% | +6.4 / 10% | +8.8 / 10% |
| 40 N | -28.4 / 0.01% | +5.4 / 10% | +8.5 / 10% |
| 50 N | -4.6 / 0.114% | +4.9 / 10% | +8.6 / 10% |
| 60 N | -18.7 / 0.01% | +4.9 / 10% | +7.4 / 10% |

## The two grades the Rule would refer to

The consistency check of the note's A.3.8, applied to each mask against its own declared zone (the alpha axis read directly off the alpha/deltaLongitude form; the elevation axis is not readable in this form):

- notch 0 deg (declared zone none): **CONSISTENT** -- exclusion (alpha form): consistent 15, lit inside 0, saturated 0; dark blocks 0 of 15; near-peak power reaches alpha 0.0 deg against a declared 0.0 (block -40); elevation axis not readable in this form -> CONSISTENT.
- notch 8 deg (declared zone 8): **CONSISTENT** -- exclusion (alpha form): consistent 15, lit inside 0, saturated 0; dark blocks 0 of 15; near-peak power reaches alpha 8.0 deg against a declared 8.0 (block -40); elevation axis not readable in this form -> CONSISTENT.
- notch 22 deg (declared zone 22): **CONSISTENT** -- exclusion (alpha form): consistent 15, lit inside 0, saturated 0; dark blocks 0 of 15; near-peak power reaches alpha 22.0 deg against a declared 22.0 (block -60); elevation axis not readable in this form -> CONSISTENT.
- the unnotched mask graded against a DECLARED zone of 8 deg -- a system that claims the zone but does not carry it, the pair the Rule would meet on filed material: **SATURATED (no shaping)** -- exclusion (alpha form): consistent 4, lit inside 2, saturated 9; dark blocks 0 of 15; near-peak power reaches alpha 0.0 deg against a declared 8.0 (block -40); elevation axis not readable in this form -> SATURATED (no shaping).

## On the scale of a working downlink

The masks' boresight pfd at the payload used here: notch 0: -133.8 dB(W/m2) in 40 kHz = -119.8 dB(W/(m2 MHz)); notch 8: -133.8 dB(W/m2) in 40 kHz = -119.8 dB(W/(m2 MHz)); notch 22: -133.8 dB(W/m2) in 40 kHz = -119.8 dB(W/(m2 MHz)). The table's ninth column moves each system's peak to the payload at which it would just clear the row, per MHz. For the scale: Article 21's pfd limit in these bands is -105 dB(W/(m2 MHz)) at high elevation, and a typical working Ka-band downlink sits in the range -115 to -125 dB(W/(m2 MHz)) at the earth station. The 1 MHz figures assume the emission fills 1 MHz with a flat density -- exact for a carrier at least 1 MHz wide, an over-statement for a narrower one; this payload declares no carrier bandwidth, so the flat reading is the upper bound.

## Reading it

- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 0.70 m, regions XR1/XR2/XR3: protecting the arc with a 8 deg zone is worth +17.8 dB at the deciding point. At the payload where that system just clears the row, the system that does not protect the arc sits at -17.8 dB: it fails.
- Article 22, TABLE 22-1C -- FSS 19700-20200 MHz, refbw 40 kHz, dish 0.70 m, regions XR1/XR2/XR3: protecting the arc with a 22 deg zone is worth +19.6 dB at the deciding point. At the payload where that system just clears the row, the system that does not protect the arc sits at -19.6 dB: it fails.
- Latitude: the protectors' worst victims on the Article 22, TABLE 22-1C row are notch 8 at 60 N (-2.5 dB), notch 22 at 60 N (-0.7 dB). The mechanism is general high-latitude geometry, not a property of one system: the arc sits low from a high-latitude earth station, so a zone about it removes little of the sky, and an inclined shell's sub-satellite density peaks near its inclination latitude, so more and closer satellites are in view there. The magnitude depends on the shell's inclination and satellite count -- the same reading of a filed 1 600-satellite system at 53 deg inclination gave 12.7 dB at 60 N against 8.5-9.9 dB at 0-50 N -- so a criterion of this kind bites hardest at latitudes near the interferer's inclination, which an operator serving those latitudes may not be able to meet by arc protection alone; Article 22 itself grades its further limits by latitude above 57.5 deg (No. 22.5C.4).
- The daylight is the difference between where the two systems' worst points fall: the non-protecting system's worst point is the short-term end of the row, set by a satellite crossing the earth station's main beam inside the zone with a main-beam-grade mask value (Step 22 counts it whatever the declaration says); the protecting system's worst point is the body of the row, set by the co-frequency cap and the side-lobe levels, which the notch does not touch. Protecting the arc therefore buys nothing at the body and everything at the short-term end.
- Caveats: one constellation and one payload family; this producer's reading of Sec. D5.1.4.1; a rule notch as the protecting mask (the declaration and the mask agree by construction, which is exactly the route case (a) offers an operator); the antenna evaluated at 19.05 GHz rather than at the row's own band, a difference of a fraction of a decibel in the S.1428 pattern; the prefix column says how far each worst margin is from converged (the body converges in hours, the short-term end with the closest pass of the run).
