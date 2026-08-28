using System.Linq;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// Fixed body-stabilised <see cref="IBeamPointing"/> over the PFD tab's scene
/// (WP1 first milestone: constant boresights in the satellite body frame).
/// Snapshots the live tab's payload settings into an independent generation
/// VM; per satellite state it re-tessellates the layout at the sub-satellite
/// point and applies the elevation and GSO-exclusion gating, so the resolved
/// set is exactly what the static tab would compute at that position.
///
/// Note: for circular patterns the UV lattice is fixed in the body frame, so
/// regeneration IS the body-stabilised constant case; the elliptical auto
/// layout re-derives per-cell axes from local geometry, which re-adapts beam
/// widths slightly as the satellite moves.
/// </summary>
public sealed class ScenePointing : IBeamPointing
{
    private readonly PfdMaskViewModel _gen;

    public ScenePointing(PfdMaskViewModel live)
    {
        _gen = new PfdMaskViewModel(live.Coastlines);
        live.CopySettingsTo(_gen);
    }

    public ResolvedBeamSet Resolve(SatelliteState state)
    {
        _gen.Scene.SubSatLatDeg = state.SubSatLatDeg;
        _gen.Scene.SubSatLonDeg = state.SubSatLonDeg;
        _gen.Scene.AltitudeKm = state.AltitudeKm;
        // Fly the fixed body-frame layout at the pass heading (WP4/WP8): the
        // resolved set is then one of the configurations the derived mask
        // envelopes, so mask >= live composition holds by construction.
        _gen.Scene.BodyYawDeg = state.HeadingDeg;
        _gen.RebuildForCompute();
        // Beams are recreated on every rebuild; snapshot the list so the
        // resolved set stays stable when this pointing moves to the next state.
        return new ResolvedBeamSet(_gen.Scene.Beams.ToList(), PfdMaskField.BeamPowersDbw(_gen));
    }
}
