using System;
using System.Collections.Generic;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>
/// App-side <see cref="IPfdMaskSampler"/>: the compute engine behind
/// <see cref="MaskXmlExport.GenerateAsync"/> (which lives in core since WP0
/// made the writer headless). Snapshots the live tab's settings into an
/// independent generation VM so the live view is untouched; per latitude it
/// rebuilds beams + exclusion and the mask field, then answers envelope
/// reads off it.
/// </summary>
public sealed class MaskExportSampler : IPfdMaskSampler
{
    private readonly PfdMaskViewModel _gen;
    private readonly List<PfdMaskField> _fields = new();
    private readonly double _halfRowDeg;

    // A mask row does not describe a point. Sec. D5.1.5 step 1 reads the table
    // with the NEAREST latitude, so a row governs the half-step either side of
    // it, and a field built only at the row centre under-declares wherever the
    // emission varies across that band -- the deflated-mask direction, which a
    // truth run then exceeds. The band is therefore sampled at its edges as
    // well as its centre and the envelope is the max across all of them.
    //
    // Honest about what this is: denser point sampling, not a proof. Three
    // offsets capture monotone variation across the band exactly and
    // non-monotone variation approximately.
    private static readonly double[] BandOffsets = { -1.0, 0.0, 1.0 };

    public MaskExportSampler(PfdMaskViewModel live, MaskXmlExportOptions o)
    {
        _gen = new PfdMaskViewModel(live.Coastlines);
        live.CopySettingsTo(_gen);
        _gen.MaskKind = o.Kind;

        // Envelope binning wants >= ~2 field cells per output bin; if the
        // dialog's axis steps are finer than the tab's compute step allows,
        // tighten the compute grid for the generation VM only.
        double finestHalf = 0.5 * Math.Min(o.BStepDeg, o.CStepDeg);
        if (_gen.MaskStepDeg > finestHalf) _gen.MaskStepDeg = Math.Max(0.1, finestHalf);
        _halfRowDeg = o.LatStepDeg / 2.0;
    }

    public void PrepareLatitude(double latDeg)
    {
        _fields.Clear();
        foreach (double off in BandOffsets)
        {
            _gen.Scene.SubSatLatDeg = latDeg + off * _halfRowDeg;
            _gen.RebuildForCompute();
            var field = new PfdMaskField();
            field.Rebuild(_gen);
            _fields.Add(field);
        }
    }

    public double SampleMaxIn(double xDeg, double yDeg, double halfW, double halfH)
    {
        double max = double.NegativeInfinity;
        foreach (var f in _fields) max = Math.Max(max, f.SampleMaxIn(xDeg, yDeg, halfW, halfH));
        return max;
    }
}
