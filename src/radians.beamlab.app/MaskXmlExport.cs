using System;
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
    private readonly PfdMaskField _field = new();

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
    }

    public void PrepareLatitude(double latDeg)
    {
        _gen.Scene.SubSatLatDeg = latDeg;
        _gen.RebuildForCompute();
        _field.Rebuild(_gen);
    }

    public double SampleMaxIn(double xDeg, double yDeg, double halfW, double halfH)
        => _field.SampleMaxIn(xDeg, yDeg, halfW, halfH);
}
