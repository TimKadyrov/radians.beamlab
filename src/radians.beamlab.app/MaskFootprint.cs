using System;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// <see cref="IMaskPfdRead"/> over an imported S.1503-4 PFD mask XML: the
/// examination's read of a declaration. Table selection is by the nearest
/// latitude block to the satellite's sub-satellite latitude, then the
/// exact Sec. D5.1.5 bilinear read inside it
/// (<see cref="PfdMaskField.MaskReadRaw"/>). Coordinates per mask type:
///
/// - azimuth_elevation (Sec. D6.4.5): the generator's north-referenced
///   satellite frame (pass headings are enveloped into the mask) --
///   azimuth from nadir toward East, elevation out of the East-Down
///   plane toward North, exactly the frame the exporter samples.
/// - alpha_deltaLongitude (Sec. D6.4.4): signed alpha at the earth
///   station (Sec. D6.4.4.1 sign) and deltaLongitude = sub-satellite
///   longitude minus the longitude of the alpha-minimising GSO arc
///   point, wrapped to +/-180.
///
/// Reads are raw dB in the mask's declared reference bandwidth; the
/// -1000 no-transmission floor stays numeric, as in the reference.
/// </summary>
public sealed class MaskFootprint : IMaskPfdRead
{
    private readonly LoadedPfdMask _mask;
    private readonly PfdMaskField[] _fields;
    private readonly double[] _blockLats;

    public MaskFootprint(LoadedPfdMask mask)
    {
        _mask = mask;
        _fields = new PfdMaskField[mask.Blocks.Count];
        _blockLats = new double[mask.Blocks.Count];
        for (int i = 0; i < mask.Blocks.Count; i++)
        {
            _blockLats[i] = mask.Blocks[i].LatDeg;
            _fields[i] = new PfdMaskField { ExternalOnly = true };
            MaskXmlImport.ApplyBlockToField(mask, mask.Blocks[i], _fields[i]);
        }
    }

    /// <summary>Loads and wraps a mask XML file.</summary>
    public static MaskFootprint LoadFile(string path) => new(MaskXmlImport.Load(path));

    public MaskPlotKind Kind => _mask.Kind;
    public int BlockCount => _mask.Blocks.Count;
    public double RefBwKHz => _mask.RefBwKHz;

    public double PfdDb(SatelliteState state, Vec3 satPosKm, Vec3 esPosKm)
    {
        // Sec. D5.1.5 step 1: the table with the nearest latitude.
        int best = 0;
        double bestD = Math.Abs(_blockLats[0] - state.SubSatLatDeg);
        for (int i = 1; i < _blockLats.Length; i++)
        {
            double d = Math.Abs(_blockLats[i] - state.SubSatLatDeg);
            if (d < bestD) { best = i; bestD = d; }
        }

        var dir = (esPosKm - satPosKm).Normalized();
        if (_mask.Kind == MaskPlotKind.AzEl)
        {
            var (n, e, d) = SatNedBasis(state.SubSatLatDeg, state.SubSatLonDeg);
            double az = Math.Atan2(Vec3.Dot(dir, e), Vec3.Dot(dir, d)) * 180.0 / Math.PI;
            double el = Math.Asin(Math.Clamp(Vec3.Dot(dir, n), -1.0, 1.0)) * 180.0 / Math.PI;
            return _fields[best].MaskReadRaw(az, el);
        }

        var signed = GsoGeometry.AlphaSignedDeg(esPosKm, satPosKm);
        if (signed is not { } r) return MaskLatBlock.UnreachableDb;   // no visible GSO arc
        double dL = state.SubSatLonDeg - r.gsoLonDeg;
        while (dL > 180.0) dL -= 360.0;
        while (dL < -180.0) dL += 360.0;
        return _fields[best].MaskReadRaw(dL, r.alphaDeg);
    }
}
