using System;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// WP2 "occurring" pointing: the fixed body-stabilised layout of
/// <see cref="ScenePointing"/>, gated by the <see cref="Scheduler"/> -- a
/// beam transmits only while it serves an assigned cell. Feeding this into
/// <see cref="EpfdDown"/> gives the "occurring" statistics of spec Sec. 5;
/// the ungated <see cref="ScenePointing"/> gives "reachable". Per-beam power
/// is left unchanged (no budget redistribution), so occurring is a per-step
/// subset of reachable by construction.
/// </summary>
public sealed class ScheduledPointing : IBeamPointing
{
    private readonly ScenePointing _inner;
    private readonly Scheduler _scheduler;
    private double _cachedT = double.NaN;
    private ScheduleStep? _step;

    public ScheduledPointing(Constellation constellation, ServiceGeography geography,
        OperatingParamsSet declared, PfdMaskViewModel live, double simulationDurationSec,
        double? coverageRadiusKm = null, SelectionPolicy policy = SelectionPolicy.HighestElevation,
        double illuminationDutyCycle = 1.0)
    {
        _inner = new ScenePointing(live, illuminationDutyCycle);
        _scheduler = new Scheduler(constellation, geography, declared,
            new ScenePointing(live), simulationDurationSec, coverageRadiusKm, policy);
    }

    /// <summary>The schedule used for the most recent step (diagnostics / tests).</summary>
    public ScheduleStep? LastStep => _step;

    public ResolvedBeamSet Resolve(SatelliteState state)
    {
        if (state.TimeSeconds != _cachedT)
        {
            _step = _scheduler.Step(state.TimeSeconds);
            _cachedT = state.TimeSeconds;
        }

        var set = _inner.Resolve(state);
        _step!.ActiveBeams.TryGetValue(state.SatelliteNumber, out var on);
        for (int i = 0; i < set.Beams.Count; i++)
        {
            if (on is null || !on.Contains(i))
                set.Beams[i].Weight = 0.0;   // fresh per-resolve beam objects; gating is local
        }
        return set;
    }
}
