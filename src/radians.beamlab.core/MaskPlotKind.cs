namespace radians.beamlab;

/// <summary>
/// PFD-mask coordinate system, mirroring the S.1503-4 mask types the tool can
/// produce and visualise (radians MaskPFDType AzEl / AlphaDelta; the
/// X/deltaLong variant is not implemented).
/// </summary>
public enum MaskPlotKind
{
    /// <summary>Satellite-frame azimuth / elevation (Sec. D6.4.5).</summary>
    AzEl,
    /// <summary>Signed alpha / deltaLongitude (Sec. D6.4.4).</summary>
    AlphaDeltaLong,
}
