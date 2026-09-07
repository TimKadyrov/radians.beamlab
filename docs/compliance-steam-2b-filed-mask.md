# The filed STEAM-2B mask, examined

*Produced by `dotnet run --project tests/radians.beamlab.checks -- examine "STEAM-2.opprofile.json" "STEAM-2.orbitdesign.json" "steam-2.operparams.json" "<filed mask>" <days> 60 0 60 10`, examination only -- no derivation probe, no truth sweep.*
*Date: 2026-09-06. Wall clock 0.1 min (0.1 d) and 0.3 min (1.0 d).*

## What was examined

- **The mask:** the operator's filed pfd mask for STEAM-2B (ntc_id 317520389, mask_id 150,
  17700-20200 MHz; az/el, 179 latitude blocks, 88 MB), as supplied to WP 4A. Not in this
  repository.
- **The R set:** this project's derived set for the STEAM-2 case (`dataset/margin/steam-2/
  steam-2.operparams.json`): min_elev 40 deg, alpha 22 deg constant, Nco 4 -- the same
  values the mask dissection found encoded in the filing itself. **Not the operator's filed
  R set.**
- **The victim:** earth station at longitude 0, GSO satellite at +10 deg, 1.00 m dish (the
  limit row's own diameter). One earth-station longitude and one GSO offset, not the
  examination's full grid.
- **The limit:** Article 22, TABLE 22-1B, FSS 17800-18600 MHz, refbw 40 kHz, applied at the
  profile's 18.15 GHz. The mask's band reaches 20.2 GHz, where TABLE 22-1C governs; that
  row was not applied.
- **The algorithm:** this project's implementation of Sec. D5.1.4.1 with the D5.1.5 mask read.
  Validated against the radians oracle for the geometry-and-selection chain; not validated
  for the reading of a real filing.

## Verdicts

Sweep grid: latitudes 0..60 every 10 deg. The worst margin is the worst over THESE
latitudes; a finer sweep can find worse between them.

| latitude | max epfd, 0.1 d | margin, 0.1 d | max epfd, 1.0 d | margin, 1.0 d |
|---|---|---|---|---|
| 0 | -173.6 | -1.2 | -170.7 | -1.0 |
| 10 | -173.3 | -0.9 | -171.7 | -0.9 |
| 20 | -173.2 | -0.8 | -172.8 | -1.0 |
| 30 | -173.6 | -0.5 | -170.6 | -0.5 |
| 40 | -173.7 | -1.2 | -171.3 | -1.3 |
| 50 | -168.9 | -6.2 | -168.7 | -6.2 |
| 60 | -169.7 | -4.9 | -169.7 | -4.9 |

Depth: 144 steps (floor 0.694%) and 1440 steps (floor 0.069%). Ten times the depth moved
no margin by more than 0.2 dB.

**Under these conditions the filed mask exceeds TABLE 22-1B at all seven latitudes: by 0.5
to 1.3 dB at 0..40 deg and by 4.9 to 6.2 dB at 50 and 60.** Under the BR's conditions --
the operator's own R set, the full victim grid, every applicable row -- it may not. This is
a question to put to the filing, not a verdict on it.

## Against this project's own numbers, at equal depth (0.1 d)

| latitude | T, assumed payload | E1, our derived mask | E_filed | E_filed - T |
|---|---|---|---|---|
| 0 | -158.0 | -151.5 | -173.6 | -15.6 |
| 10 | -166.0 | -158.9 | -173.3 | -7.3 |
| 20 | -159.7 | -149.1 | -173.2 | -13.5 |
| 30 | -164.5 | -155.3 | -173.6 | -9.1 |
| 40 | -160.2 | -150.2 | -173.7 | -13.5 |
| 50 | -157.5 | -146.2 | -168.9 | -11.4 |
| 60 | -158.3 | -148.1 | -169.7 | -11.4 |

The filed declaration sits 7 to 16 dB below the truth of the payload this project
ASSUMED for STEAM-2 (scene-default pattern and layout, Gm 35 dBi assumed, per-beam power
set so the boresight pfd meets the filed cap, 40 deg elevation from Doc 4A/653). The
assumed system radiates far more than the operator declared, so the in-band failure in
`compliance-steam-2.md` is a property of the assumption, not of the filed system. The
parity run (`mask-parity-steam-2b.md`) had already placed our side-lobe floor 19.5 dB above
theirs.

## A note on depth

The filed mask is a two-level rule mask and its verdicts settled at 0.1 d. This project's
derived mask did not: its E1 moved by -6.1 dB at latitude 30 and +4.1 dB at 50 between
0.1 d and 1.0 d (`dataset/margin/examine/record-mask-record-rset-1d.md`). Every comparison
in the STEAM-2 records held depth fixed, so their differences stand; their absolute E1
figures at 0.1 d are soft by 4 to 6 dB per latitude and need a truth sweep at 1.0 d to
restate.

## Correction, 7 September 2026 (later the same day)

The verdicts above at latitudes 50 and 60 were inflated by the examination's own resolver. For an
earth-station latitude outside the span of the declared rows -- the derived R set's rows sit at
+-45 deg -- it read no elevation floor and no Nco cap instead of the nearest row (Sec. D5.1.5
step 1), while it clamps MIN_EXCLUDE to its end rows. Re-examined with the same R set's rows
extended to +-65 deg at the same values, which emulates the nearest-row read:

| latitude | margin, 0.1 d | margin, 1.0 d |
|---|---|---|
| 0 | -1.2 | -1.0 |
| 10 | -0.9 | -0.9 |
| 20 | -0.8 | -1.0 |
| 30 | -0.5 | -0.5 |
| 40 | -1.2 | -1.3 |
| 50 | -2.0 | -1.8 |
| 60 | -4.0 | -4.0 |

**Under these conditions the filed mask exceeds TABLE 22-1B by 0.5 to 2.0 dB at 0..50 deg and by
4.0 dB at 60**, firm across ten times the depth. The figures at 0..40 are unchanged. This project's
derived masks are unaffected within 0.1 dB, because they are dark where the missing cap and floor
would have admitted satellites. Records: `dataset/margin/examine/filed-mask-rows65-rset.md` and
`filed-mask-rows65-rset-1d.md`. The resolver fix -- clamping the nearest-read arrays to their end
rows, as the exclusion resolver already does -- awaits the operator's confirmation; until then the
extended rows are the measurement. Every caveat of the first section still applies.

The mask-versus-gates consistency check, added the same day, reads this filing CONSISTENT with the
record's R set: 109 latitude blocks consistent, 70 not exercised where no cell reaches the zone,
elevation consistent at all 179 blocks. The notch sits at 22 deg and the floor at 40 deg, as the
dissection found.
