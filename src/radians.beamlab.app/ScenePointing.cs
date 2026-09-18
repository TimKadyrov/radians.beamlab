using System.Collections.Concurrent;
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
/// Concurrent: a generation VM is mutable scene state, so each Resolve
/// borrows one from a pool -- every member a settings copy of the same
/// frozen template, as the single VM used to be -- and returns it after.
/// Which member resolves a satellite leaves no trace in the result.
///
/// Note: for circular patterns the UV lattice is fixed in the body frame, so
/// regeneration IS the body-stabilised constant case; the elliptical auto
/// layout re-derives per-cell axes from local geometry, which re-adapts beam
/// widths slightly as the satellite moves.
/// </summary>
public sealed class ScenePointing : IConcurrentBeamPointing
{
    private readonly PfdMaskViewModel _template;
    private readonly ConcurrentBag<PfdMaskViewModel> _pool = new();
    private readonly double _dutyDb;

    /// <param name="illuminationDutyCycle">
    /// Time-averaged illumination fraction per beam for hopping frames much
    /// shorter than the simulation step: resolved powers carry
    /// 10 log10(duty). The declared masks stay peak-PSD envelopes; only the
    /// simulated statistics average. 1 = continuous (previous behaviour).
    /// </param>
    public ScenePointing(PfdMaskViewModel live, double illuminationDutyCycle = 1.0)
    {
        if (illuminationDutyCycle is <= 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(illuminationDutyCycle));
        _template = new PfdMaskViewModel(live.Coastlines);
        live.CopySettingsTo(_template);
        _dutyDb = 10.0 * Math.Log10(illuminationDutyCycle);
    }

    /// <summary>Nothing is shared between the satellites of a step here.</summary>
    public void Prepare(double timeSeconds) { }

    public ResolvedBeamSet Resolve(SatelliteState state)
    {
        if (!_pool.TryTake(out var gen))
        {
            gen = new PfdMaskViewModel(_template.Coastlines);
            _template.CopySettingsTo(gen);
        }
        try
        {
            return Resolve(gen, state);
        }
        finally
        {
            _pool.Add(gen);
        }
    }

    private ResolvedBeamSet Resolve(PfdMaskViewModel gen, SatelliteState state)
    {
        gen.Scene.SubSatLatDeg = state.SubSatLatDeg;
        gen.Scene.SubSatLonDeg = state.SubSatLonDeg;
        gen.Scene.AltitudeKm = state.AltitudeKm;
        // Fly the fixed body-frame layout at the pass heading (WP4/WP8): the
        // resolved set is then one of the configurations the derived mask
        // envelopes, so mask >= live composition holds by construction.
        gen.Scene.BodyYawDeg = state.HeadingDeg;
        gen.RebuildForCompute();
        // Beams are recreated on every rebuild; snapshot the list so the
        // resolved set stays stable when this pointing moves to the next state.
        var powers = PfdMaskField.BeamPowersDbw(gen);
        if (_dutyDb != 0.0)
            for (int i = 0; i < powers.Length; i++) powers[i] += _dutyDb;
        var beams = gen.Scene.Beams.ToList();
        // Declared co-channel N-colour reuse rides with the set, so the
        // epfd composite models the aggregation the payload declares.
        int? n = gen.Aggregation == PfdAggregation.CoChannelSum ? gen.ReuseClusterSize : null;
        return new ResolvedBeamSet(beams, powers, n,
            n is int nn ? BeamComposer.ReuseColors(beams, nn) : null,
            n is not null ? gen.CoFrequencyBeamCapacity : null);
    }
}
