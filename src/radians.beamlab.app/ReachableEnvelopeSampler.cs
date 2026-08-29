using System;
using System.Collections.Generic;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// WP4 mask derivation: <see cref="IPfdMaskSampler"/> that envelopes the
/// reachable configuration set at each latitude. With fixed body-stabilised
/// beams that set is exact and analytic -- the layout appears at the
/// ascending- and the descending-pass heading (<see cref="GroundTrack"/>),
/// while the mask's az/el frame stays Earth-referenced -- so per latitude the
/// field is computed once per heading (Scene.BodyYawDeg) and every envelope
/// read is the max across headings. Reduces to the single north-aligned
/// configuration of <see cref="MaskExportSampler"/> when the headings merge.
/// A yaw-steering payload widens the set beyond the pass headings: supply
/// MaskXmlExportOptions.YawSweepDeg and each heading is swept over those
/// body-yaw offsets too -- without it the derived mask is only an envelope
/// for heading-locked layouts.
/// </summary>
public sealed class ReachableEnvelopeSampler : IPfdMaskSampler
{
    private readonly PfdMaskViewModel _gen;
    private readonly double _inclinationDeg;
    private readonly double[] _yawSweep;
    private readonly List<PfdMaskField> _fields = new();

    public ReachableEnvelopeSampler(PfdMaskViewModel live, MaskXmlExportOptions o, double inclinationDeg)
    {
        _gen = new PfdMaskViewModel(live.Coastlines);
        live.CopySettingsTo(_gen);
        _gen.MaskKind = o.Kind;
        _inclinationDeg = inclinationDeg;
        _yawSweep = o.YawSweepDeg is { Length: > 0 } ? o.YawSweepDeg : new[] { 0.0 };

        // Same compute-grid clamp as MaskExportSampler: envelope binning
        // wants >= ~2 field cells per output bin.
        double finestHalf = 0.5 * Math.Min(o.BStepDeg, o.CStepDeg);
        if (_gen.MaskStepDeg > finestHalf) _gen.MaskStepDeg = Math.Max(0.1, finestHalf);
    }

    public void PrepareLatitude(double latDeg)
    {
        var headings = new List<double>(2);
        if (GroundTrack.HeadingsAtLatitude(_inclinationDeg, latDeg) is { } h)
        {
            headings.Add(h.AscendingDeg);
            double sep = Math.Abs((((h.DescendingDeg - h.AscendingDeg) % 360.0) + 360.0) % 360.0);
            if (sep > 1e-9 && Math.Abs(sep - 360.0) > 1e-9) headings.Add(h.DescendingDeg);
        }
        else
        {
            // At/beyond the reach limit the passes merge into the east-west
            // limiting heading; the latitude table is capped there anyway.
            headings.Add(90.0);
        }

        _fields.Clear();
        foreach (double psi in headings)
            foreach (double yaw in _yawSweep)
            {
                _gen.Scene.SubSatLatDeg = latDeg;
                _gen.Scene.BodyYawDeg = psi + yaw;
                _gen.RebuildForCompute();
                var field = new PfdMaskField();
                field.Rebuild(_gen);
                _fields.Add(field);
            }
    }

    public double SampleMaxIn(double xDeg, double yDeg, double halfW, double halfH)
    {
        double max = double.NegativeInfinity;
        foreach (var f in _fields)
            max = Math.Max(max, f.SampleMaxIn(xDeg, yDeg, halfW, halfH));
        return max;
    }
}
