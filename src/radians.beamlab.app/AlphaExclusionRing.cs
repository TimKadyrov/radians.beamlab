namespace radians.beamlab.app;

/// <summary>
/// One concentric GSO-exclusion ring, defined by its outer alpha edge (deg). Rings
/// stack from alpha = 0 outward; ring i covers [previous outer, this outer). A
/// beam whose footprint |alpha| falls in the ring is either switched off or
/// attenuated by <see cref="AttenDb"/> dB. Bindable -- edited live in the
/// exclusion-rings dialog.
/// </summary>
public sealed class AlphaExclusionRing : ObservableObject
{
    private double _outerDeg;
    /// <summary>Outer alpha boundary of the ring (deg from the nearest visible GSO arc).</summary>
    public double OuterDeg
    {
        get => _outerDeg;
        set => SetField(ref _outerDeg, value);
    }

    private bool _isOff = true;
    /// <summary>True -> beams in this ring are switched off; false -> attenuated by <see cref="AttenDb"/>.</summary>
    public bool IsOff
    {
        get => _isOff;
        set { if (SetField(ref _isOff, value)) OnPropertyChanged(nameof(IsAttenuate)); }
    }

    /// <summary>Inverse of <see cref="IsOff"/>, for a two-state radio/checkbox binding.</summary>
    public bool IsAttenuate
    {
        get => !_isOff;
        set => IsOff = !value;
    }

    private double _attenDb = 10.0;
    /// <summary>Attenuation applied to beams in this ring when <see cref="IsOff"/> is false (dB, >= 0).</summary>
    public double AttenDb
    {
        get => _attenDb;
        set => SetField(ref _attenDb, value);
    }
}

/// <summary>
/// Immutable snapshot of one exclusion band for the compute / render passes:
/// the outer alpha edge, whether it switches beams off, and the attenuation (dB)
/// otherwise. Basic single-threshold mode reduces to one off band at alpha_excl.
/// </summary>
public readonly record struct ExclusionBand(double OuterDeg, bool IsOff, double AttenDb)
{
    /// <summary>Linear weight multiplier a beam in this band receives (0 when off).</summary>
    public double WeightFactor => IsOff ? 0.0 : System.Math.Pow(10.0, -System.Math.Max(0.0, AttenDb) / 10.0);
}
