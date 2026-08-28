# Vendored orbit propagator (radians.orbits.core)

These files are **verbatim, byte-for-byte copies** from the radians
examination tool (same owner), directory `radians/radians.orbits.core/`:

- `Propagation/OrbitPropagator.cs` — the ITU-R S.1503-4 Part D6.3 propagator
  (Keplerian + secular J2, the three station-keeping cases)
- `Propagation/OrbitalElements.cs`, `Propagation/StateVector.cs`,
  `Propagation/CoordinateFrame.cs`
- `Utilities/AngleUtilities.cs`, `Utilities/OrbitalConstants.cs`,
  `Utilities/VectorOperations.cs`
- `Models/Vector3D.cs`, `Models/GeocentricCoordinate.cs`

Sharing the propagator makes beamlab's trajectories identical to the
examination's **by construction** (simulation spec, WP1). Do not edit these
files here — fix upstream in radians and re-copy. The verification harness
(`tests/radians.beamlab.checks`, check J0) byte-compares every file against
the radians working copy when that repository is present and fails on any
divergence.

Because byte-identity is the point, this directory keeps the original
namespace (`Radians.Orbits.Core.*`) and is **exempt from the repository's
ASCII-only-comments rule**.

Note on Earth radius: the propagator uses the S.1503 value
(`OrbitalConstants.EarthRadiusKm` = 6378.145) for orbital mechanics, while
beamlab's ground geometry (`GeoMath`) is a 6371 km sphere. Constellation
code derives sub-satellite direction from the position vector (radius-free)
and converts altitude against the beamlab sphere so scene geometry stays
internally consistent.
