using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Mask dissection: read a S.1503-4 pfd mask (satellite-frame az/el form) back
/// into the operating rules that produced it. Every (latitude, az, el) cell is
/// a direction the satellite may or may not radiate toward; mapped to the
/// ground through the same frame MaskFootprint reads with (NED at the
/// sub-satellite point: az = atan2(east, down), el = asin(north)), each cell
/// becomes a ground point with a satellite elevation angle and a GSO-arc alpha.
/// The main-beam plateau is then the set of allowed targets, and its boundaries
/// in elevation and alpha are the operator's minimum elevation and exclusion
/// angle -- per latitude, so a latitude-dependent alpha rule would show as a
/// varying boundary while a constant rule shows as a constant one seen through
/// geometry. Also read off: whether the plateau is a flat pfd cap (constant
/// boresight PFD power control) or range-shaped, the implied boresight
/// e.i.r.p. density, and the side-lobe floor.
///
/// The analysis lives here so that the harness (dissect mode, MaskParity, the
/// compliance loop's consistency section) and the dataset generator (the
/// consistency probe's record) read one implementation. The console/document
/// front end is the harness's MaskDissectCli.
/// </summary>
public static class MaskDissect
{
    public sealed class LatResult
    {
        public double Lat;
        public double Peak = double.NegativeInfinity;
        public int Reaching, Plateau, Floor, Mid;
        public double PlateauMinElev = 999, PlateauMinAlpha = 999, PlateauMaxOffNadir = 0;
        public double PlateauElMin = 999, PlateauElMax = -999;      // north-south pointing extent
        public double FloorMaxAlphaAboveElev = -1;                    // excluded despite elevation ok
        public double FloorMaxElevAboveAlpha = -1;                    // excluded despite alpha ok
        public double FloorMin = 0, FloorMax = double.NegativeInfinity;
        public double PeakSlantKm;
        public double PlateauMinPfd = double.PositiveInfinity;
        // Lit reach: the lowest ground elevation and the smallest alpha at which
        // the block radiates within 20 dB of its peak (i.e. is not dark). Where
        // the near-peak reach thins with range before the gate, this is the
        // number that still sees a missing floor or zone.
        public double LitMinElev = 999, LitMinAlpha = 999;
        public string Signature = "";
    }

    public sealed class Result
    {
        public LoadedPfdMask Mask = null!;
        public double AltitudeKm;
        public List<LatResult> Lats = new();
        public SortedDictionary<int, long> Levels = new();
        public List<(double from, double to)> IdenticalSpans = new();
        public long Reaching, PlateauAll, MidAll;
        public double PeakAll, FloorMinAll, FloorMaxAll;
        public double ElevMinLo, ElevMinHi, Alpha0Lo, Alpha0Hi;
        public int AlphaLimitedCount; public double AlphaLimitedFrom, AlphaLimitedTo;
        public double PlateauSpread; public bool FlatCap;
        public double EirpAtNadir, EirpAtEdge, EdgeSlantKm;
        public bool AlphaConstant;
    }

    public static Result Analyze(LoadedPfdMask mask, double altitudeKm)
    {
        var res = new Result { Mask = mask, AltitudeKm = altitudeKm };
        foreach (var blk in mask.Blocks)
        {
            var r = new LatResult { Lat = blk.LatDeg };
            var sat = GeodeticToEcef(blk.LatDeg, 0.0, altitudeKm);
            var (n, e, d) = SatNedBasis(blk.LatDeg, 0.0);
            double satMag2 = Vec3.Dot(sat, sat);
            var nadir = (new Vec3(0, 0, 0) - sat).Normalized();

            double peak = double.NegativeInfinity;
            foreach (var row in blk.Rows)
                foreach (var v in row.Values)
                    if (v > MaskLatBlock.UnreachableDb + 1) peak = Math.Max(peak, v);
            r.Peak = peak;
            var sig = new StringBuilder();

            // Per cell: direction -> ground point -> elevation, alpha, off-nadir; classify.
            var cells = new List<(double v, double elevGround, double alpha)>();
            foreach (var row in blk.Rows)
            {
                double az = row.B * Math.PI / 180.0;
                for (int k = 0; k < row.CNodes.Length; k++)
                {
                    double v = row.Values[k];
                    if (v <= MaskLatBlock.UnreachableDb + 1) continue;
                    double el = row.CNodes[k] * Math.PI / 180.0;
                    var dir = n * Math.Sin(el) + e * (Math.Cos(el) * Math.Sin(az)) + d * (Math.Cos(el) * Math.Cos(az));
                    double pd = Vec3.Dot(sat, dir);
                    double disc = pd * pd - (satMag2 - EarthRadiusKm * EarthRadiusKm);
                    if (disc < 0) continue;                       // direction misses the Earth
                    double t = -pd - Math.Sqrt(disc);
                    if (t <= 0) continue;
                    var g = sat + dir * t;
                    r.Reaching++;
                    double elevGround = ElevationAngleDeg(sat, g);
                    double alpha = GsoGeometry.AlphaMinAbsDeg(g, sat);
                    double offNadir = Math.Acos(Math.Clamp(Vec3.Dot(dir, nadir), -1.0, 1.0)) * 180.0 / Math.PI;
                    int lv = (int)Math.Round(v);
                    res.Levels[lv] = res.Levels.GetValueOrDefault(lv) + 1;
                    cells.Add((v, elevGround, alpha));
                    if (v >= peak - 20.0)
                    {
                        r.LitMinElev = Math.Min(r.LitMinElev, elevGround);
                        r.LitMinAlpha = Math.Min(r.LitMinAlpha, alpha);
                    }

                    if (v >= peak - 3.0)
                    {
                        r.Plateau++;
                        r.PlateauMinElev = Math.Min(r.PlateauMinElev, elevGround);
                        r.PlateauMinAlpha = Math.Min(r.PlateauMinAlpha, alpha);
                        r.PlateauMaxOffNadir = Math.Max(r.PlateauMaxOffNadir, offNadir);
                        r.PlateauElMin = Math.Min(r.PlateauElMin, row.CNodes[k]);
                        r.PlateauElMax = Math.Max(r.PlateauElMax, row.CNodes[k]);
                        if (v >= peak - 1e-9) r.PeakSlantKm = t;
                        r.PlateauMinPfd = Math.Min(r.PlateauMinPfd, v);
                        sig.Append('#');
                    }
                    else if (v < peak - 20.0)
                    {
                        r.Floor++;
                        r.FloorMin = r.Floor == 1 ? v : Math.Min(r.FloorMin, v);
                        r.FloorMax = Math.Max(r.FloorMax, v);
                        sig.Append('.');
                    }
                    else { r.Mid++; sig.Append('+'); }
                }
            }
            // The boundaries seen from the excluded side.
            if (r.Plateau > 0)
                foreach (var c in cells)
                {
                    if (c.v >= peak - 20.0) continue;
                    if (c.elevGround >= r.PlateauMinElev + 0.5) r.FloorMaxAlphaAboveElev = Math.Max(r.FloorMaxAlphaAboveElev, c.alpha);
                    if (c.alpha >= r.PlateauMinAlpha + 0.5) r.FloorMaxElevAboveAlpha = Math.Max(r.FloorMaxElevAboveAlpha, c.elevGround);
                }
            r.Signature = sig.ToString();
            res.Lats.Add(r);
        }

        var results = res.Lats;
        for (int i = 0; i < results.Count;)
        {
            int j = i;
            while (j + 1 < results.Count && results[j + 1].Signature == results[i].Signature
                   && Math.Abs(results[j + 1].Peak - results[i].Peak) < 1e-9) j++;
            if (j > i) res.IdenticalSpans.Add((results[i].Lat, results[j].Lat));
            i = j + 1;
        }
        res.Reaching = results.Sum(x => (long)x.Reaching);
        res.PlateauAll = results.Sum(x => (long)x.Plateau);
        res.MidAll = results.Sum(x => (long)x.Mid);
        var withPlateau = results.Where(x => x.Plateau > 0).ToList();
        var withFloor = results.Where(x => x.Floor > 0).ToList();
        res.PeakAll = results.Max(x => x.Peak);
        res.FloorMinAll = withFloor.Count > 0 ? withFloor.Min(x => x.FloorMin) : double.NaN;
        res.FloorMaxAll = withFloor.Count > 0 ? withFloor.Max(x => x.FloorMax) : double.NaN;
        var alphaLimited = results.Where(x => x.FloorMaxAlphaAboveElev > 0).ToList();
        res.AlphaLimitedCount = alphaLimited.Count;
        res.Alpha0Lo = alphaLimited.Count > 0 ? alphaLimited.Max(x => x.FloorMaxAlphaAboveElev) : double.NaN;
        res.Alpha0Hi = alphaLimited.Count > 0 ? alphaLimited.Min(x => x.PlateauMinAlpha) : double.NaN;
        res.AlphaLimitedFrom = alphaLimited.Count > 0 ? alphaLimited.Min(x => x.Lat) : double.NaN;
        res.AlphaLimitedTo = alphaLimited.Count > 0 ? alphaLimited.Max(x => x.Lat) : double.NaN;
        res.AlphaConstant = alphaLimited.Count > 0 && alphaLimited.Max(x => x.PlateauMinAlpha) - alphaLimited.Min(x => x.PlateauMinAlpha) < 2.0;
        res.ElevMinLo = withPlateau.Count > 0 ? withPlateau.Max(x => x.FloorMaxElevAboveAlpha) : double.NaN;
        res.ElevMinHi = withPlateau.Count > 0 ? withPlateau.Min(x => x.PlateauMinElev) : double.NaN;
        var peakRow = results.OrderByDescending(x => x.Peak).First();
        res.EdgeSlantKm = peakRow.PeakSlantKm;
        res.EirpAtEdge = res.PeakAll + 10.0 * Math.Log10(4.0 * Math.PI * Math.Pow(peakRow.PeakSlantKm * 1000.0, 2));
        res.EirpAtNadir = res.PeakAll + 10.0 * Math.Log10(4.0 * Math.PI * Math.Pow(altitudeKm * 1000.0, 2));
        res.PlateauSpread = withPlateau.Count > 0 ? withPlateau.Max(x => x.Peak - x.PlateauMinPfd) : double.NaN;
        res.FlatCap = res.PlateauSpread < 0.5;
        return res;
    }

    public static string RulesSummary(Result r, CultureInfo inv) => string.Create(inv,
        $"min elevation [{r.ElevMinLo:F1}, {r.ElevMinHi:F1}] deg; exclusion alpha [{r.Alpha0Lo:F1}, {r.Alpha0Hi:F1}] deg over {r.AlphaLimitedCount} alpha-limited latitudes ({r.AlphaLimitedFrom:F0}..{r.AlphaLimitedTo:F0}), {(r.AlphaConstant ? "constant" : "VARYING")}; cap {r.PeakAll:F1} dB, plateau spread {r.PlateauSpread:F2} dB ({(r.FlatCap ? "flat pfd cap = constant boresight PFD" : "range-shaped")}); e.i.r.p. density {r.EirpAtNadir:F1} dBW/{r.Mask.RefBwKHz:F0} kHz at nadir, {r.EirpAtEdge:F1} at the edge; side-lobe floor {r.FloorMinAll:F1}..{r.FloorMaxAll:F1} ({r.PeakAll - r.FloorMaxAll:F0} dB below peak)");
}
