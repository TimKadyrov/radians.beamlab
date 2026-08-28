# Vendored epfd statistics components (radians)

Verbatim, byte-for-byte copies from the radians examination tool (same
owner), vendored so beamlab's own interference statistics (WP8) are
commensurable with the examination bin for bin:

- `radlimits.cs` (from `radians/radlimits/`) -- `Limit` / `LimitPoint`
- `EpfdAccumulator.cs` (from `radians/radcompute1503-2/`) -- the S.1503-4
  Sec. D7.1.2 accumulator (0.1 dB bins, CDF) and the Sec. D7.1.3 linearised
  limits comparison
- `ApLib.cs` (from `radians/radantenna/`) -- the antenna library, including
  the Rec. ITU-R S.1428 GSO earth-station receive patterns the epfd(down)
  examination uses

Sharing these makes the two CDFs identical in binning, flooring and
comparison conventions by construction. Do not edit here -- fix upstream in
radians and re-copy; the verification harness (check J0) byte-compares every
vendored file against the radians working copy when present and fails on
drift.

Same rules as `orbits/`: original namespaces kept (`radlimits`,
`radcompute1503_2`, `radantenna`), directory exempt from the repository's
ASCII-only-comments rule, and excluded from EOL normalization.
