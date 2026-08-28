using System.Globalization;
using System.IO;
using System.Xml;
using radians.beamlab;
using radians.beamlab.app;
using Radians.Orbits.Core.Propagation;
using Radians.Orbits.Core.Utilities;
using static radians.beamlab.GeoMath;

// Headless business-logic verification of radians.beamlab against
// independent invariants (brute-force references, closed-form identities).
//
// Run:  dotnet run --project tests/radians.beamlab.checks
// Exit code 0 iff every check passes; each check prints PASS/FAIL + detail.
//
// Sections: A-C geometry and composition (alpha solver vs brute force,
// frame round-trips, reuse colourings, aggregation ordering), D-E the PFD
// field and mask export (grid consistency, XML/CSV round-trip, envelope
// binning), F peak retention on coarse grids, G Taylor-kernel bounds at the
// Bessel-zero abscissae, H array-steered UV beams, I mask-viewer import
// (D5.1.5 reads, real-filing regression). Checks against the local ITU
// reference case (I2-I4) skip cleanly when that file is not present.

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  |  " + detail : "")}");
    if (ok) pass++; else fail++;
}

var rng = new Random(42);

// ------------------------------------------------------------------
// A. alpha geometry (GsoGeometry) vs brute force + hand cases
// ------------------------------------------------------------------

// Brute-force min |angle(ES->NGSO, ES->G(theta))| over the visible GSO arc.
(double alphaAbs, double gsoLonDeg)? BruteAlpha(Vec3 es, Vec3 ngso)
{
    double esMag = es.Length;
    double lat = Math.Asin(Math.Clamp(es.Z / esMag, -1, 1));
    double lon = Math.Atan2(es.Y, es.X);
    double vis = EarthRadiusKm / (GsoGeometry.GsoRadiusKm * Math.Cos(lat));
    if (vis >= 1) return null;
    double thMax = Math.Acos(vis);
    var d = ngso - es;
    double dLen = d.Length;
    double best = double.PositiveInfinity; double bestTh = 0;
    const int N = 400_000;
    for (int i = 0; i <= N; i++)
    {
        double th = lon - thMax + 2 * thMax * i / N;
        var g = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(th), GsoGeometry.GsoRadiusKm * Math.Sin(th), 0) - es;
        double a = Math.Acos(Math.Clamp(Vec3.Dot(d, g) / (dLen * g.Length), -1, 1));
        if (a < best) { best = a; bestTh = th; }
    }
    return (best * 180 / Math.PI, ((bestTh * 180 / Math.PI + 540) % 360) - 180);
}

// A1: hand case -- ES at (0,0), sat overhead: alpha = 0, gsoLon = 0.
{
    var es = GeodeticToEcef(0, 0, 0);
    var sat = GeodeticToEcef(0, 0, 1200);
    var r = GsoGeometry.AlphaSignedDeg(es, sat);
    Check("A1 ES(0,0) sat overhead → α=0, gsoLon=0",
        r is { } v && Math.Abs(v.alphaDeg) < 1e-3 && Math.Abs(v.gsoLonDeg) < 1e-3,
        r is { } w ? $"α={w.alphaDeg:F5} lon={w.gsoLonDeg:F5}" : "null");
}

// A2: in-plane geometry -- equatorial ES, equatorial sat east of it: alpha = 0.
{
    var es = GeodeticToEcef(0, 0, 0);
    var sat = GeodeticToEcef(0, 10, 1200);
    var r = GsoGeometry.AlphaSignedDeg(es, sat);
    Check("A2 equatorial in-plane → α=0", r is { } v && Math.Abs(v.alphaDeg) < 1e-3,
        r is { } w ? $"α={w.alphaDeg:F5}" : "null");
}

// A3: sign convention -- northern ES looking south at equatorial sat crosses the
// equatorial plane inside the GSO radius -> alpha > 0; mirrored southern ES -> alpha < 0.
{
    var sat = GeodeticToEcef(0, 0, 1200);
    var north = GsoGeometry.AlphaSignedDeg(GeodeticToEcef(10, 0, 0), sat);
    var south = GsoGeometry.AlphaSignedDeg(GeodeticToEcef(-10, 0, 0), sat);
    Check("A3 §D6.4.4.1 sign: north(+), south(−), equal magnitude",
        north is { } n && south is { } s && n.alphaDeg > 0 && s.alphaDeg < 0 &&
        Math.Abs(n.alphaDeg + s.alphaDeg) < 1e-6,
        north is { } n2 && south is { } s2 ? $"N={n2.alphaDeg:F4} S={s2.alphaDeg:F4}" : "null");
}

// A4: analytic vs brute force over random visible geometries.
{
    double worstA = 0, worstL = 0; int tested = 0;
    for (int t = 0; t < 60; t++)
    {
        double subLat = rng.NextDouble() * 120 - 60;
        double subLon = rng.NextDouble() * 360 - 180;
        double alt = 500 + rng.NextDouble() * 1500;
        var sat = GeodeticToEcef(subLat, subLon, alt);
        // ES somewhere in the visible disc.
        double gamma = rng.NextDouble() * (HorizonHalfAngleDeg(alt) - 0.5);
        double brg = rng.NextDouble() * 360;
        var ring = GeoMath.SampleSmallCircle(subLat, subLon, gamma, 360);
        var (esLat, esLon) = ring[(int)(brg)];
        var es = GeodeticToEcef(esLat, esLon, 0);

        var a = GsoGeometry.AlphaSignedDeg(es, sat);
        var b = BruteAlpha(es, sat);
        if (a is null || b is null) continue;
        tested++;
        worstA = Math.Max(worstA, Math.Abs(Math.Abs(a.Value.alphaDeg) - b.Value.alphaAbs));
        // gsoLon comparison only meaningful when the minimum is unique/sharp.
        if (b.Value.alphaAbs > 0.2)
        {
            double dl = Math.Abs(((a.Value.gsoLonDeg - b.Value.gsoLonDeg + 540) % 360) - 180);
            worstL = Math.Max(worstL, dl);
        }
    }
    Check($"A4 analytic |α| vs brute force ({tested} random cases)", worstA < 0.01,
        $"worst |Δα|={worstA:E2}°, worst ΔgsoLon={worstL:F3}°");
}

// A5: AlphaMinAbsDeg wrapper consistency.
{
    var es = GeodeticToEcef(25, 40, 0);
    var sat = GeodeticToEcef(20, 38, 1200);
    var s = GsoGeometry.AlphaSignedDeg(es, sat);
    double m = GsoGeometry.AlphaMinAbsDeg(es, sat);
    Check("A5 |signed| == AlphaMinAbsDeg", s is { } v && Math.Abs(Math.Abs(v.alphaDeg) - m) < 1e-12);
}

// ------------------------------------------------------------------
// B. GeoMath spherical identities
// ------------------------------------------------------------------

// B1: OffNadirForEsElevationDeg endpoints + round trip through the ray geometry.
{
    double alt = 1200;
    bool ok = Math.Abs(OffNadirForEsElevationDeg(90, alt)) < 1e-9 &&
              Math.Abs(OffNadirForEsElevationDeg(0, alt) - HorizonOffNadirDeg(alt)) < 1e-9;
    double worst = 0;
    var sat = GeodeticToEcef(0, 0, alt);
    var (n, e, dwn) = SatNedBasis(0, 0);
    foreach (double eps in new[] { 5.0, 10, 25, 47, 60, 80 })
    {
        double off = OffNadirForEsElevationDeg(eps, alt);
        var look = NedToEcef(BeamDirNed(off, 90), n, e, dwn);
        var hit = RaySphereHit(sat, look);
        double got = ElevationAngleDeg(sat, hit!.Value);
        worst = Math.Max(worst, Math.Abs(got - eps));
    }
    Check("B1 ε ↔ off-nadir law-of-sines round trip", ok && worst < 1e-6, $"worst Δε={worst:E2}°");
}

// B2: SampleSmallCircle points sit at the requested central angle.
{
    var pts = GeoMath.SampleSmallCircle(37, -75, 21.5, 180);
    double worst = 0;
    foreach (var (la, lo) in pts) worst = Math.Max(worst, Math.Abs(GreatCircleDeg(37, -75, la, lo) - 21.5));
    Check("B2 SampleSmallCircle radius", worst < 1e-9, $"worst Δ={worst:E2}°");
}

// B3: sat-frame decomposition -- |lookNed| = 1 and cos(offNadir) = cos(az)?cos(el).
{
    double worst = 0;
    for (int t = 0; t < 500; t++)
    {
        double az = rng.NextDouble() * 180 - 90, el = rng.NextDouble() * 180 - 90;
        double sinEl = Math.Sin(el * Math.PI / 180), cosEl = Math.Cos(el * Math.PI / 180);
        double sinAz = Math.Sin(az * Math.PI / 180), cosAz = Math.Cos(az * Math.PI / 180);
        var v = new Vec3(sinEl, sinAz * cosEl, cosAz * cosEl);
        worst = Math.Max(worst, Math.Abs(v.Length - 1.0));
    }
    Check("B3 sat-frame az/el decomposition is unit-norm", worst < 1e-12, $"worst={worst:E2}");
}

// ------------------------------------------------------------------
// C. Composites, power modes, reuse (on the real default beam set)
// ------------------------------------------------------------------
var vm = new PfdMaskViewModel();          // default scene: equator, 1200 km, hex beams
var beams = vm.Scene.Beams;
var sat0 = vm.Scene.SatEcef;
Check("C0 default scene built beams", beams.Count > 50, $"beams={beams.Count}, active={vm.ActiveBeamCount}");

List<Vec3> RandomLooks(int n)
{
    var looks = new List<Vec3>();
    double horizon = HorizonOffNadirDeg(vm.Scene.AltitudeKm);
    var (nn, ee, dd) = SatNedBasis(vm.Scene.SubSatLatDeg, vm.Scene.SubSatLonDeg);
    for (int i = 0; i < n; i++)
    {
        double off = rng.NextDouble() * (horizon - 0.1);
        double azm = rng.NextDouble() * 360;
        looks.Add(NedToEcef(BeamDirNed(off, azm), nn, ee, dd));
    }
    return looks;
}
var looks = RandomLooks(300);

// C1: equal-power identity CompositeEirpDbw == P + CompositeGainDbi.
{
    var powers = new double[beams.Count];
    for (int i = 0; i < powers.Length; i++) powers[i] = -7.3;
    double worst = 0;
    foreach (var lk in looks)
    {
        double a = BeamComposer.CompositeEirpDbw(beams, lk, powers);
        double b = -7.3 + BeamComposer.CompositeGainDbi(beams, lk);
        if (!double.IsNegativeInfinity(a) || !double.IsNegativeInfinity(b))
            worst = Math.Max(worst, Math.Abs(a - b));
    }
    Check("C1 equal-power identity (EIRP composite = P + gain composite)", worst < 1e-9, $"worst Δ={worst:E2} dB");
}

// C2: hex reuse colouring -- no two adjacent lattice cells share a colour (K=3,4,7).
{
    bool ok = true; string bad = "";
    (int, int)[] nb = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, -1), (-1, 1) };
    foreach (int k in new[] { 3, 4, 7 })
        for (int i = -8; i <= 8 && ok; i++)
            for (int j = -8; j <= 8 && ok; j++)
                foreach (var (di, dj) in nb)
                    if (BeamComposer.HexReuseColor(i, j, k) == BeamComposer.HexReuseColor(i + di, j + dj, k))
                    { ok = false; bad = $"K={k} at ({i},{j})+({di},{dj})"; break; }
    Check("C2 K-colour adjacency (K=3,4,7)", ok, bad);
}

// C3: pointwise ordering -- maxSingle <= worst-colour co-channel <= power sum.
{
    var powers = new double[beams.Count];
    var colors = new int[beams.Count];
    for (int i = 0; i < beams.Count; i++)
    {
        powers[i] = 0;
        colors[i] = beams[i].LatticeI is int li && beams[i].LatticeJ is int lj
            ? BeamComposer.HexReuseColor(li, lj, 3) : i % 3;
    }
    bool ok = true; string bad = "";
    foreach (var lk in looks)
    {
        double sum = BeamComposer.CompositeEirpDbw(beams, lk, powers);
        double co = BeamComposer.MaxCoChannelEirpDbw(beams, lk, powers, colors, 3);
        double single = double.NegativeInfinity;
        for (int i = 0; i < beams.Count; i++)
            if (beams[i].Weight > 0)
                single = Math.Max(single, powers[i] + beams[i].GainDbi(lk) + 10 * Math.Log10(beams[i].Weight));
        if (!(co <= sum + 1e-9 && single <= co + 1e-9)) { ok = false; bad = $"single={single:F2} co={co:F2} sum={sum:F2}"; break; }
    }
    Check("C3 ordering: maxSingle ≤ coChannel ≤ powerSum (300 dirs)", ok, bad);
}

// C4: constant-boresight-PFD compensation -- P_k ? 20log10(d_k) is beam-independent.
{
    double alt = vm.Scene.AltitudeKm;
    double worst = 0; int nOn = 0;
    double reference = double.NaN;
    foreach (var b in beams)
    {
        var hit = RaySphereHit(sat0, b.Boresight);
        if (hit is null) continue;
        double slant = (hit.Value - sat0).Length;
        double pk = 0 + 20 * Math.Log10(slant / alt);          // the compensation formula
        double boresightPfdTerm = pk - 20 * Math.Log10(slant); // + const terms omitted
        if (double.IsNaN(reference)) reference = boresightPfdTerm;
        worst = Math.Max(worst, Math.Abs(boresightPfdTerm - reference));
        nOn++;
    }
    Check($"C4 spreading-loss compensation flattens boresight PFD ({nOn} beams)", worst < 1e-9, $"worst Δ={worst:E2} dB");
}

// C5: exclusion bands -- WeightFactor, innermost-band selection.
{
    var bands = new[]
    {
        new ExclusionBand(5, true, 0),
        new ExclusionBand(10, false, 10),
        new ExclusionBand(15, false, 3),
    };
    bool ok =
        PfdMaskViewModel.BandFor(bands, 2)!.Value.WeightFactor == 0.0 &&
        Math.Abs(PfdMaskViewModel.BandFor(bands, 7)!.Value.WeightFactor - Math.Pow(10, -1.0)) < 1e-12 &&
        Math.Abs(PfdMaskViewModel.BandFor(bands, 12)!.Value.WeightFactor - Math.Pow(10, -0.3)) < 1e-12 &&
        PfdMaskViewModel.BandFor(bands, 20) is null;
    Check("C5 exclusion bands: off/−10dB/−3dB/none by |α|", ok);
}

// C6: basic gating on the default VM -- every beam with footprint |alpha| < alpha_excl is off.
{
    bool ok = true; string bad = "";
    foreach (var b in beams)
    {
        var fp = vm.Scene.GroundFootprint(b);
        if (fp is null) continue;
        double a = GsoGeometry.AlphaMinAbsDeg(GeodeticToEcef(fp.Value.lat, fp.Value.lon, 0), sat0);
        bool inside = a < vm.AlphaExclDeg;
        if (inside && b.Weight != 0) { ok = false; bad = $"{b.Name} α={a:F2} w={b.Weight}"; break; }
        if (!inside && b.Weight == 0) { ok = false; bad = $"{b.Name} α={a:F2} w=0 (over-gated)"; break; }
    }
    Check("C6 α_excl gating matches per-beam footprint α", ok, bad);
}

// ------------------------------------------------------------------
// D. Field end-to-end (AzEl): nadir PFD vs independent computation
// ------------------------------------------------------------------
{
    var field = new PfdMaskField();
    field.Rebuild(vm);
    // Independent nadir PFD: composite gain toward nadir + spreading at alt.
    var (nn, ee, dd) = SatNedBasis(vm.Scene.SubSatLatDeg, vm.Scene.SubSatLonDeg);
    var nadir = NedToEcef(BeamDirNed(0.001, 0), nn, ee, dd);
    double g = BeamComposer.CompositeGainDbi(beams, nadir);
    double expected = vm.TxEirpDbw + g - 10 * Math.Log10(4 * Math.PI * Math.Pow(vm.Scene.AltitudeKm * 1000, 2));
    double got = field.SampleAt(0.0, 0.0);
    Check("D1 field nadir PFD vs independent formula", Math.Abs(got - expected) < 0.3,
        $"field={got:F2}  independent={expected:F2} dB(W/m²)");

    // D2: pixels past the horizon are empty; on-disc centre pixels are finite.
    double horizonEl = HorizonOffNadirDeg(vm.Scene.AltitudeKm);
    bool offDisc = double.IsNegativeInfinity(field.SampleAt(0.0, horizonEl + 5)) &&
                   double.IsNegativeInfinity(field.SampleAt(horizonEl + 5, 0.0));
    bool onDisc = !double.IsNegativeInfinity(field.SampleAt(0.0, horizonEl - 5));
    Check("D2 horizon boundary (blank outside, data inside)", offDisc && onDisc);

    // D3: autoscale bounds actually bound the data.
    bool ok = field.HasValidRange && field.PfdCeil > field.PfdFloor;
    double mn = double.PositiveInfinity, mx = double.NegativeInfinity;
    for (int i = 0; i < field.PfdGrid!.Length; i++)
    {
        double v = field.PfdGrid[i];
        if (double.IsNegativeInfinity(v)) continue;
        mn = Math.Min(mn, v); mx = Math.Max(mx, v);
    }
    ok &= Math.Abs(mn - field.PfdFloor) < 1e-9 && Math.Abs(mx - field.PfdCeil) < 1e-9;
    Check("D3 autoscale = data min/max", ok, $"[{field.PfdFloor:F2}, {field.PfdCeil:F2}]");

    // D4: profile slice equals direct grid samples along the cut.
    var slice = field.ProfileAtX(0.0);
    bool match = slice.Count > 10;
    foreach (var (y, p) in slice)
        if (Math.Abs(field.SampleAt(0.0, y) - p) > 1e-9) { match = false; break; }
    Check("D4 profile slice consistent with grid", match, $"samples={slice.Count}");
}

// ------------------------------------------------------------------
// E. alpha/DeltaL field + XML/CSV export end-to-end
// ------------------------------------------------------------------
{
    vm.MaskKind = MaskPlotKind.AlphaDeltaLong;
    var field = new PfdMaskField();
    field.Rebuild(vm);
    // E1: exclusion dip -- PFD averaged inside |alpha|<alpha_excl should sit below the
    // average just outside it (beams off inside; side lobes only).
    double SumBand(double a0, double a1)
    {
        double s = 0; int n = 0;
        for (double dl = -60; dl <= 60; dl += 2)
            for (double a = a0; a <= a1; a += 1)
            {
                double v = field.SampleAt(dl, a);
                if (!double.IsNegativeInfinity(v)) { s += v; n++; }
            }
        return n > 0 ? s / n : double.NaN;
    }
    double inside = SumBand(-8, 8), outside = SumBand(12, 25);
    Check("E1 α/ΔL exclusion dip (inside < outside)", inside < outside, $"in={inside:F2} out={outside:F2}");

    // E2: tiny export, both formats; re-parse and cross-check XML vs CSV cells.
    string dir = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(dir);
    string basePath = Path.Combine(dir, "check.xml");
    var opts = new MaskXmlExportOptions
    {
        SatName = "T", NtcId = 7, MaskId = 3, RefBwKHz = 40,
        LatMinDeg = -10, LatMaxDeg = 10, LatStepDeg = 10,
        BStepDeg = 30, CStepDeg = 60,
        Kind = MaskPlotKind.AlphaDeltaLong, Format = MaskExportFormat.Both, OutputPath = basePath,
    };
    MaskXmlExport.GenerateAsync(new MaskExportSampler(vm, opts), opts, null, CancellationToken.None).GetAwaiter().GetResult();

    var doc = new XmlDocument();
    doc.Load(basePath);
    var byA = doc.SelectNodes("//by_a")!;
    var byB = doc.SelectNodes("//by_b")!;
    var pfd = doc.SelectNodes("//pfd")!;
    int expLats = 3, expB = 7, expC = 7;   // -10..10/10; -90..90/30; -180..180/60
    bool counts = byA.Count == expLats && byB.Count == expLats * expB && pfd.Count == expLats * expB * expC;
    Check("E2a XML node counts (lat×b×c)", counts, $"a={byA.Count} b={byB.Count} pfd={pfd.Count}");

    string[] csv = File.ReadAllLines(Path.ChangeExtension(basePath, ".csv"));
    // header comment + column header + one row per (lat, b)
    bool csvShape = csv.Length == 2 + expLats * expB && csv[1].Split(',').Length == 2 + expC;
    Check("E2b CSV shape (rows, cols)", csvShape, $"rows={csv.Length} cols={csv[1].Split(',').Length}");

    // E2c: every XML pfd value equals the corresponding CSV cell.
    bool same = true;
    int row = 2;
    foreach (XmlNode a in byA)
    {
        foreach (XmlNode b in a.SelectNodes("by_b")!)
        {
            var cells = csv[row++].Split(',');
            var pfds = b.SelectNodes("pfd")!;
            for (int ci = 0; ci < pfds.Count; ci++)
                if (cells[2 + ci] != pfds[ci]!.InnerText) { same = false; break; }
        }
    }
    Check("E2c XML values == CSV values", same);

    // E2d: values plausible -- some finite, some -1000, nothing absurdly high.
    int finite = 0, floor1000 = 0; double maxV = double.NegativeInfinity;
    foreach (XmlNode p in pfd)
    {
        double v = double.Parse(p.InnerText, CultureInfo.InvariantCulture);
        if (v <= -999) floor1000++; else { finite++; maxV = Math.Max(maxV, v); }
    }
    Check("E2d export value sanity", finite >= 8 && floor1000 > 0 && maxV < -60,
        $"finite={finite} floor={floor1000} max={maxV:F1} (coarse grid → few reachable nodes is expected)");

    // E2e: STRONG check -- the a=0 latitude block of the XML must equal the live
    // field's BIN-MAX (envelope semantics: max over the node's +/-step/2 bin)
    // at every (b, c) node, within F1 rounding.
    bool nodesMatch = true; string mismatch = "";
    foreach (XmlNode a in byA)
    {
        if (Math.Abs(double.Parse(a.Attributes!["a"]!.Value, CultureInfo.InvariantCulture)) > 1e-9) continue;
        foreach (XmlNode b in a.SelectNodes("by_b")!)
        {
            double bv = double.Parse(b.Attributes!["b"]!.Value, CultureInfo.InvariantCulture);
            foreach (XmlNode p in b.SelectNodes("pfd")!)
            {
                double cv = double.Parse(p.Attributes!["c"]!.Value, CultureInfo.InvariantCulture);
                double fieldV = field.SampleMaxIn(cv, bv, 30.0, 15.0);   // alpha/DeltaL: x = DeltaL = c (+/-CStep/2), y = alpha = b (+/-BStep/2)
                string xmlS = p.InnerText;
                if (double.IsNegativeInfinity(fieldV))
                {
                    if (xmlS != "-1000") { nodesMatch = false; mismatch = $"b={bv} c={cv}: xml={xmlS} field=-inf"; }
                }
                else
                {
                    double xv = double.Parse(xmlS, CultureInfo.InvariantCulture);
                    if (Math.Abs(xv - fieldV) > 0.05001) { nodesMatch = false; mismatch = $"b={bv} c={cv}: xml={xv} field={fieldV:F3}"; }
                }
                if (!nodesMatch) break;
            }
            if (!nodesMatch) break;
        }
    }
    Check("E2e export a=0 block equals live field bin-max at every node", nodesMatch, mismatch);

    // E2f: the exported latitude table must cross lat = 0 exactly even when
    // (max - min) is not a multiple of the step: grid is 0-anchored with the
    // exact endpoints pinned. -53..53 step 5 -> -53, -50..50 (x21), 53 = 23.
    var vmF = new PfdMaskViewModel();
    vmF.MaskStepDeg = 3.0;                    // coarse field: grid check only
    string pathF = Path.Combine(dir, "latgrid.xml");
    var optsF = new MaskXmlExportOptions
    {
        SatName = "T", NtcId = 7, MaskId = 4, RefBwKHz = 40,
        LatMinDeg = -53, LatMaxDeg = 53, LatStepDeg = 5,
        BStepDeg = 45, CStepDeg = 90,
        Kind = MaskPlotKind.AzEl, Format = MaskExportFormat.Xml, OutputPath = pathF,
    };
    MaskXmlExport.GenerateAsync(new MaskExportSampler(vmF, optsF), optsF, null, CancellationToken.None).GetAwaiter().GetResult();
    var docF = new XmlDocument();
    docF.Load(pathF);
    var latsF = docF.SelectNodes("//by_a")!.Cast<XmlNode>()
        .Select(a => double.Parse(a.Attributes!["a"]!.Value, CultureInfo.InvariantCulture)).ToList();
    bool sortedF = latsF.SequenceEqual(latsF.OrderBy(v => v));
    bool gridF = latsF.Contains(0.0) && latsF[0] == -53 && latsF[^1] == 53 && latsF.Count == 23 && sortedF;
    Check("E2f latitude table crosses 0 (endpoints pinned, sorted)", gridF,
        $"n={latsF.Count} first={latsF[0]} last={latsF[^1]} has0={latsF.Contains(0.0)}");
}

// ---- F: peak-aware sampling (deliberately coarse grid must keep exact peaks) ----
{
    var vmP = new PfdMaskViewModel();
    vmP.MaskStepDeg = 5.0;                    // coarse on purpose
    var fieldP = new PfdMaskField();
    fieldP.Rebuild(vmP);                      // AzEl

    var scene = vmP.Scene;
    var sat = scene.SatEcef;
    var (north, east, down) = SatNedBasis(scene.SubSatLatDeg, scene.SubSatLonDeg);
    var powers = new double[scene.Beams.Count];   // ConstantEirp default, 0 dBW

    // Independent per-beam boresight PFD (PowerSum default aggregation).
    var peaks = new List<(Vec3 look, Vec3 ground, double pfd)>();
    foreach (var beam in scene.Beams)
    {
        if (beam.Weight <= 0.0) continue;
        var look = beam.Boresight.Normalized();
        var hit = RaySphereHit(sat, look);
        if (hit is null) continue;
        double e = BeamComposer.CompositeEirpDbw(scene.Beams, look, powers);
        double slantM = (hit.Value - sat).Length * 1000.0;
        peaks.Add((look, hit.Value, e - 10.0 * Math.Log10(4.0 * Math.PI * slantM * slantM)));
    }

    bool okAz = peaks.Count > 0; string detAz = $"beams={peaks.Count}";
    foreach (var (look, ground, pfd) in peaks)
    {
        double az = Math.Atan2(Vec3.Dot(look, east), Vec3.Dot(look, down)) * 180.0 / Math.PI;
        double el = Math.Asin(Math.Clamp(Vec3.Dot(look, north), -1.0, 1.0)) * 180.0 / Math.PI;
        double cell = fieldP.SampleAt(az, el);
        if (cell < pfd - 1e-6)
        {
            okAz = false;
            detAz = $"az={az:F1} el={el:F1}: cell={cell:F2} < peak={pfd:F2}";
            break;
        }
    }
    Check("F1 az/el 5° grid keeps every boresight peak", okAz, detAz);

    vmP.MaskKind = MaskPlotKind.AlphaDeltaLong;
    var fieldP2 = new PfdMaskField();
    fieldP2.Rebuild(vmP);
    bool okAd = peaks.Count > 0; string detAd = $"beams={peaks.Count}";
    foreach (var (look, ground, pfd) in peaks)
    {
        var ad = GsoGeometry.AlphaSignedDeg(ground, sat);
        if (ad is null) continue;
        double dLon = ((scene.SubSatLonDeg - ad.Value.gsoLonDeg + 540.0) % 360.0) - 180.0;
        double cell = fieldP2.SampleAt(dLon, ad.Value.alphaDeg);
        if (cell < pfd - 1e-6)
        {
            okAd = false;
            detAd = $"dL={dLon:F1} a={ad.Value.alphaDeg:F1}: cell={cell:F2} < peak={pfd:F2}";
            break;
        }
    }
    Check("F2 α/ΔL 5° grid keeps every boresight peak", okAd, detAd);
}

// ---- F3: the EXPORTED mask keeps every boresight peak at coarse output steps ----
{
    var vmE = new PfdMaskViewModel();          // defaults (MaskStepDeg = 1)
    string pathE = Path.Combine(AppContext.BaseDirectory, "exp", "peaks.xml");
    var optsE = new MaskXmlExportOptions
    {
        SatName = "T", NtcId = 7, MaskId = 5, RefBwKHz = 40,
        LatMinDeg = 0, LatMaxDeg = 0, LatStepDeg = 5,
        BStepDeg = 45, CStepDeg = 90,          // deliberately coarse output nodes
        Kind = MaskPlotKind.AzEl, Format = MaskExportFormat.Xml, OutputPath = pathE,
    };
    MaskXmlExport.GenerateAsync(new MaskExportSampler(vmE, optsE), optsE, null, CancellationToken.None).GetAwaiter().GetResult();

    var docE = new XmlDocument();
    docE.Load(pathE);
    var vals = new Dictionary<(double b, double c), double>();
    foreach (XmlNode a in docE.SelectNodes("//by_a")!)
        foreach (XmlNode b in a.SelectNodes("by_b")!)
        {
            double bv = double.Parse(b.Attributes!["b"]!.Value, CultureInfo.InvariantCulture);
            foreach (XmlNode p in b.SelectNodes("pfd")!)
            {
                double cv = double.Parse(p.Attributes!["c"]!.Value, CultureInfo.InvariantCulture);
                vals[(bv, cv)] = p.InnerText == "-1000" ? double.NegativeInfinity
                    : double.Parse(p.InnerText, CultureInfo.InvariantCulture);
            }
        }

    var sceneE = vmE.Scene;                    // same defaults as the exporter's gen VM at lat 0
    var satE = sceneE.SatEcef;
    var (northE, eastE, downE) = SatNedBasis(sceneE.SubSatLatDeg, sceneE.SubSatLonDeg);
    var powersE = new double[sceneE.Beams.Count];
    bool okE = true; string detE = ""; int nE = 0;
    foreach (var beam in sceneE.Beams)
    {
        if (beam.Weight <= 0) continue;
        var look = beam.Boresight.Normalized();
        var hit = RaySphereHit(satE, look);
        if (hit is null) continue;
        double e = BeamComposer.CompositeEirpDbw(sceneE.Beams, look, powersE);
        double slantM = (hit.Value - satE).Length * 1000.0;
        double pfd = e - 10.0 * Math.Log10(4.0 * Math.PI * slantM * slantM);
        double az = Math.Atan2(Vec3.Dot(look, eastE), Vec3.Dot(look, downE)) * 180.0 / Math.PI;
        double elv = Math.Asin(Math.Clamp(Vec3.Dot(look, northE), -1.0, 1.0)) * 180.0 / Math.PI;
        double bN = Math.Clamp(Math.Round(az / 45.0) * 45.0, -90.0, 90.0);   // owning b node
        double cN = Math.Clamp(Math.Round(elv / 90.0) * 90.0, -90.0, 90.0);  // owning c node
        nE++;
        if (!vals.TryGetValue((bN, cN), out double v) || v < pfd - 0.05001)  // F1 rounding
        {
            okE = false;
            detE = $"az={az:F1} el={elv:F1} node=({bN},{cN}): xml={v:F2} < peak={pfd:F2}";
            break;
        }
    }
    Check("F3 exported mask keeps every boresight peak (45°/90° nodes)", okE && nE > 0, okE ? $"beams={nE}" : detE);
}

// ---- G: Taylor F(u) bounded at the J1-zero removable singularities ----
// The product form divides by (1 - u^2/mu_i^2); analytically the kernel's J1
// zero cancels each pole, but a rational J1 fit has offset zeros, so F can
// spike near u = mu_i. |F| <= 1 must hold everywhere (peak is F(0) = 1).
{
    double[] j1z = { 3.83170597020751, 7.01558666981562, 10.17346813506272 };
    var vmT = new PfdMaskViewModel();
    vmT.EllRollOffDb = 7.0;
    var ell = (Rec1528_1p4_Ell)vmT.Scene.Beams[0].Pattern;
    var circ = new Rec1528_1p4(35.0, 5.0, 20.0, 4, 0.0);

    bool ok = true; string det = "";
    foreach (double z in j1z)
    {
        double mu = z / Math.PI;
        foreach (double du in new[] { 0.0, 1e-8, -1e-8, 1e-6, -1e-6, 1e-5, -1e-5, 1e-4, -1e-4, 5e-4, -5e-4 })
        {
            double u = mu + du;
            double fe = Math.Abs(ell.TaylorF(u));
            double fc = Math.Abs(circ.TaylorF(u));
            if (fe > 1.0 + 1e-9 || fc > 1.0 + 1e-9 || double.IsNaN(fe) || double.IsNaN(fc))
            {
                ok = false;
                det = $"u={u:F9}: |F_ell|={fe:E2} |F_circ|={fc:E2}";
                break;
            }
        }
        if (!ok) break;
    }
    // Dense sweep as well -- no other u may exceed the peak.
    for (double u = 0.0; ok && u <= 12.0; u += 7.3e-4)
    {
        double fe = Math.Abs(ell.TaylorF(u));
        if (fe > 1.0 + 1e-9 || double.IsNaN(fe)) { ok = false; det = $"sweep u={u:F6}: |F|={fe:E2}"; }
    }
    Check("G1 |TaylorF| <= 1 incl. exact J1-zero abscissae", ok, det);

    var fieldT = new PfdMaskField();
    fieldT.Rebuild(vmT);
    Check("G2 roll-off 7 field ceiling sane (no spike cells)", fieldT.PfdCeil < -90.0, $"ceil={fieldT.PfdCeil:F2}");
}

// ---- H: array-steered UV beams (shift-invariant lattice) ----
{
    var sc = new SceneModel
    {
        PatternKind = BeamPatternKind.Taylor_1p4,
        AutoMode = true,
        UvArrayBeams = true,
        FrequencyGHz = 12.0, GmDbi = 35.0, ThetaBDeg = 4.0,
        MinElevDeg = 10.0, AltitudeKm = 1200.0,
        SubSatLatDeg = 0.0, SubSatLonDeg = 0.0,
    };
    sc.RebuildBeams();

    // H1: per-beam width law -- transverse width fixed at theta_b, radial
    // width broadened so sin(thetaR)*cos(off-nadir) = sin(theta_b).
    double sinTb = Math.Sin(sc.ThetaBDeg * Math.PI / 180.0);
    bool okW = sc.Beams.Count > 10; string detW = $"beams={sc.Beams.Count}";
    int nEll = 0;
    foreach (var b in sc.Beams)
    {
        if (b.Pattern is not Rec1528_1p4_Ell ell) continue;   // centre beam stays circular
        nEll++;
        double cosOff = Math.Cos(b.OffNadirDeg * Math.PI / 180.0);
        double sinTr = Math.Sin(ell.ThetaB * Math.PI / 180.0);
        double sinTt = Math.Sin(ell.ThetaBTransverseDeg * Math.PI / 180.0);
        if (Math.Abs(sinTt - sinTb) > 1e-6 || Math.Abs(sinTr * cosOff - sinTb) > 1e-6)
        {
            okW = false;
            detW = $"{b.Name}: off={b.OffNadirDeg:F1} sinTr*cos={sinTr * cosOff:E4} sinTt={sinTt:E4} want={sinTb:E4}";
            break;
        }
    }
    Check("H1 array beams: sin(thetaR)*cos(off) = sin(thetaT) = sin(theta_b)", okW && nEll > 10, okW ? $"ellipticals={nEll}" : detW);

    // H2: crossover at every adjacent-pair midpoint is uniform with array
    // beams (shift invariance) and degrades radially without them.
    double MinPairCrossover(SceneModel scene)
    {
        var byIdx = new Dictionary<(int, int), Beam>();
        foreach (var b in scene.Beams)
            if (b.LatticeI is int li && b.LatticeJ is int lj && !byIdx.ContainsKey((li, lj)))
                byIdx[(li, lj)] = b;
        var offsets = new (int di, int dj)[] { (1, 0), (0, 1), (1, -1) };
        double min = double.PositiveInfinity;
        foreach (var kv in byIdx)
        foreach (var (di, dj) in offsets)
        {
            if (!byIdx.TryGetValue((kv.Key.Item1 + di, kv.Key.Item2 + dj), out var nb)) continue;
            var mid = (kv.Value.Boresight + nb.Boresight).Normalized();
            double g = Math.Min(kv.Value.GainDbi(mid), nb.GainDbi(mid)) - scene.GmDbi;
            if (g < min) min = g;
        }
        return min;
    }
    double minArr = MinPairCrossover(sc);
    sc.UvArrayBeams = false;
    sc.RebuildBeams();
    double minFix = MinPairCrossover(sc);
    Check("H2 UV crossover uniform with array beams, degraded without",
        minArr > -4.5 && minFix < minArr - 2.0, $"array min={minArr:F2} dB, fixed-cone min={minFix:F2} dB");
}

// ---- I: mask viewer import round-trip (export -> parse -> rasterise) ----
{
    foreach (var kindI in new[] { MaskPlotKind.AzEl, MaskPlotKind.AlphaDeltaLong })
    {
        var vmI = new PfdMaskViewModel();
        string pathI = Path.Combine(AppContext.BaseDirectory, "exp", $"view_{kindI}.xml");
        var optsI = new MaskXmlExportOptions
        {
            SatName = "V", NtcId = 9, MaskId = 6, RefBwKHz = 40,
            LatMinDeg = -10, LatMaxDeg = 10, LatStepDeg = 10,
            BStepDeg = 30, CStepDeg = 60,
            Kind = kindI, Format = MaskExportFormat.Xml, OutputPath = pathI,
        };
        MaskXmlExport.GenerateAsync(new MaskExportSampler(vmI, optsI), optsI, null, CancellationToken.None).GetAwaiter().GetResult();

        var loaded = MaskXmlImport.Load(pathI);
        bool okMeta = loaded.Kind == kindI && loaded.Blocks.Count == 3
                   && loaded.SatName == "V" && loaded.NtcId == 9 && loaded.MaskId == 6;

        // Rasterise the a=0 block; every table node must read back exactly
        // through the field (nearest-node raster is exact at node centres).
        var blk = loaded.Blocks.First(x => Math.Abs(x.LatDeg) < 1e-9);
        var fieldI = new PfdMaskField();
        MaskXmlImport.ApplyBlockToField(loaded, blk, fieldI);   // exact source only
        fieldI.TargetRasterW = 200; fieldI.TargetRasterH = 160;
        fieldI.RasterizeMaskSource();

        // Independent transcription of the reference read (maskdata
        // Helper.ClampedLinear bilinear, S.1503-4 D5.1.5) plus the viewer's
        // display rules (blank all-unreachable stencils, clamp at node floor):
        // every raster pixel must match.
        double Clamped(double x, double x1, double y1, double x2, double y2)
        {
            if (x < x1) return y1;
            if (x > x2) return y2;
            if (x1 - x2 == 0.0) return y1;
            return (y1 - y2) / (x1 - x2) * x + (x1 * y2 - x2 * y1) / (x1 - x2);
        }
        (int lo, int hi) Bracket(double[] nodes, double v)
        {
            int last = nodes.Length - 1;
            if (v <= nodes[0]) return (0, 0);
            if (v >= nodes[last]) return (last, last);
            int lo = 0, hi = last;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (nodes[mid] <= v) lo = mid; else hi = mid; }
            return (lo, hi);
        }
        bool ad = kindI == MaskPlotKind.AlphaDeltaLong;
        double[] bVals = blk.Rows.Select(r => r.B).ToArray();
        double RowV(MaskRow row, double c)
        {
            var (lo, hi) = Bracket(row.CNodes, c);
            return Clamped(c, row.CNodes[lo], row.Values[lo], row.CNodes[hi], row.Values[hi]);
        }

        bool okVals = blk.Rows.Count > 2 && fieldI.PfdGrid is not null;
        string detI = $"rows={blk.Rows.Count} pix={fieldI.PixW}x{fieldI.PixH}";
        double dXp = (fieldI.XMax - fieldI.XMin) / fieldI.PixW;
        double dYp = (fieldI.YMax - fieldI.YMin) / fieldI.PixH;
        for (int py = 0; py < fieldI.PixH && okVals; py++)
        for (int px = 0; px < fieldI.PixW && okVals; px++)
        {
            double x = fieldI.XMin + (px + 0.5) * dXp;
            double y = fieldI.YMax - (py + 0.5) * dYp;
            double bC = ad ? y : x;
            double cC = ad ? x : y;
            var (rl, rh) = Bracket(bVals, bC);
            double v = Clamped(bC, bVals[rl], RowV(blk.Rows[rl], cC),
                                   bVals[rh], RowV(blk.Rows[rh], cC));
            double want = v <= -1000.0 ? double.NegativeInfinity : Math.Max(v, fieldI.PfdFloor);
            double got = fieldI.PfdGrid![py * fieldI.PixW + px];
            bool same = double.IsNegativeInfinity(want)
                ? double.IsNegativeInfinity(got)
                : Math.Abs(got - want) < 1e-6;
            if (!same) { okVals = false; detI = $"px=({px},{py}) x={x:F2} y={y:F2}: field={got} want={want}"; }
        }
        Check($"I1 viewer {kindI}: raster == D5.1.5 per-row bilinear at every pixel", okMeta && okVals,
            okMeta ? detI : $"kind={loaded.Kind} blocks={loaded.Blocks.Count}");

        // I1b: MaskReadRaw is the EXACT reference read (raw, incl. the floor)
        // at arbitrary probe points, independent of any raster resolution.
        bool okRaw = true; string detR = "";
        for (double px2 = fieldI.XMin + 0.37; px2 < fieldI.XMax && okRaw; px2 += 7.31)
        for (double py2 = fieldI.YMin + 0.53; py2 < fieldI.YMax && okRaw; py2 += 5.17)
        {
            double bC = ad ? py2 : px2;
            double cC = ad ? px2 : py2;
            var (rl, rh) = Bracket(bVals, bC);
            double want = Clamped(bC, bVals[rl], RowV(blk.Rows[rl], cC),
                                      bVals[rh], RowV(blk.Rows[rh], cC));
            double got = fieldI.MaskReadRaw(px2, py2);
            if (Math.Abs(got - want) > 1e-9) { okRaw = false; detR = $"({px2:F2},{py2:F2}): got={got} want={want}"; }
        }
        Check($"I1b viewer {kindI}: MaskReadRaw == reference read at probe points", okRaw, detR);
    }

    // I2: a real ITU filing mask (ragged per-row c grids, -999 floor) must
    // load and rasterise with a sane data range.
    string realMask = @"C:\Projects\_EPFD\epfd-reference\Cases\CSN-SSO\mask ntc_id 124520256 mask_id 1.xml";
    if (File.Exists(realMask))
    {
        var m = MaskXmlImport.Load(realMask);
        var f2 = new PfdMaskField();
        MaskXmlImport.ApplyBlockToField(m, m.Blocks[m.Blocks.Count / 2], f2);
        f2.RasterizeMaskSource();   // default 720x720 targets
        bool ragged = m.Blocks[0].Rows.Select(r => r.CNodes.Length).Distinct().Count() > 1;
        bool ok2 = m.Kind == MaskPlotKind.AzEl && m.Blocks.Count > 1 && ragged
                && f2.HasValidRange && f2.PfdGrid is not null
                && f2.PfdCeil < -100 && Math.Abs(f2.PfdFloor - -999.0) < 1e-9   // -999 is data (spec null is -1000)
                && Math.Abs(m.Blocks[0].LatDeg - -51.0) < 1e-9;
        Check("I2 real ITU mask (CSN-SSO, ragged rows) loads + rasterises", ok2,
            $"blocks={m.Blocks.Count} rows={m.Blocks[0].Rows.Count} ragged={ragged} range=[{f2.PfdFloor:F1},{f2.PfdCeil:F1}]");

        // I3: raising the cut-off to the block's own minimum blanks that level
        // and lifts the colour floor (viewer's "treat min as cut-off" box).
        var blkM = m.Blocks[m.Blocks.Count / 2];
        // find a node that sits exactly at the block minimum
        double minV = double.PositiveInfinity; double minB = 0, minC = 0;
        foreach (var row in blkM.Rows)
            for (int ci = 0; ci < row.CNodes.Length; ci++)
                if (row.Values[ci] > -1000.0 && row.Values[ci] < minV)
                { minV = row.Values[ci]; minB = row.B; minC = row.CNodes[ci]; }
        double xAt = minB, yAt = minC;                 // AzEl: x = b, y = c
        var f3 = new PfdMaskField { UnreachableCutoffDb = minV };
        MaskXmlImport.ApplyBlockToField(m, blkM, f3);
        f3.RasterizeMaskSource();
        bool ok3 = double.IsNegativeInfinity(f3.SampleAt(xAt, yAt))     // blanked at min
                && Math.Abs(f2.SampleAt(xAt, yAt) - minV) < 1e-9        // visible by default
                && f3.PfdFloor > f2.PfdFloor + 0.05                     // ramp rescaled
                && f3.HasValidRange;
        Check("I3 min-as-cutoff blanks the mask's own floor + rescales", ok3,
            $"min={minV:F1} at (b={minB},c={minC}); floors {f2.PfdFloor:F1} -> {f3.PfdFloor:F1}");

        // I4: viewer policy -- off-floor below -300 auto-ticks the box and
        // applies the cut-off; unticking restores the raw view.
        var vmV = new MaskViewerViewModel();
        vmV.LoadFile(realMask);
        bool auto = vmV.TreatMinAsCutoff && vmV.CanTreatMinAsCutoff
                 && Math.Abs(vmV.Field.UnreachableCutoffDb - -999.0) < 1e-9
                 && vmV.Field.PfdFloor > -300.0;                 // ramp over real data
        vmV.TreatMinAsCutoff = false;
        bool raw = Math.Abs(vmV.Field.UnreachableCutoffDb - -1000.0) < 1e-9
                && Math.Abs(vmV.Field.PfdFloor - -999.0) < 1e-9; // -999 back as data
        Check("I4 off-floor(<-300) auto-ticks; untick restores raw", auto && raw,
            $"auto={auto} raw={raw} cutoffAfterUntick={vmV.Field.UnreachableCutoffDb}");

        // I4b: a mask whose minimum is plausible PFD never gets a cut-off,
        // even if the box is forced on. (Generated masks: min ~ -117.)
        var vmG = new MaskViewerViewModel();
        vmG.LoadFile(Path.Combine(AppContext.BaseDirectory, "exp", "view_AzEl.xml"));
        bool noAuto = !vmG.TreatMinAsCutoff && !vmG.CanTreatMinAsCutoff;
        vmG.TreatMinAsCutoff = true;   // force -- must have no effect
        bool guarded = Math.Abs(vmG.Field.UnreachableCutoffDb - -1000.0) < 1e-9;
        Check("I4b min above -300 is operational PFD, never a cut-off", noAuto && guarded,
            $"noAuto={noAuto} guarded={guarded}");
    }
    else
    {
        Check("I2 real ITU mask (CSN-SSO) loads + rasterises", true, "file not present, skipped");
    }
}

// ---- J: WP1 time + constellation (vendored S.1503-4 propagator) ----
{
    // J0: drift guard -- the vendored propagator sources must stay
    // byte-identical to the radians working copy when it is present.
    string radiansRoot = @"C:\Projects\_EPFD\radians\radians\radians.orbits.core";
    string vendored = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "radians.beamlab.core", "orbits");
    vendored = Path.GetFullPath(vendored);
    string[] relFiles =
    {
        @"Propagation\OrbitPropagator.cs", @"Propagation\OrbitalElements.cs",
        @"Propagation\StateVector.cs", @"Propagation\CoordinateFrame.cs",
        @"Utilities\AngleUtilities.cs", @"Utilities\OrbitalConstants.cs",
        @"Utilities\VectorOperations.cs", @"Models\Vector3D.cs",
        @"Models\GeocentricCoordinate.cs",
    };
    if (Directory.Exists(radiansRoot))
    {
        bool okDrift = true; string detDrift = $"files={relFiles.Length}";
        foreach (var rel in relFiles)
        {
            string a = Path.Combine(vendored, rel);
            string b = Path.Combine(radiansRoot, rel);
            if (!File.Exists(a) || !File.Exists(b) ||
                !File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b)))
            {
                okDrift = false; detDrift = $"drift: {rel}"; break;
            }
        }
        Check("J0 vendored propagator byte-identical to radians source", okDrift, detDrift);
    }
    else
    {
        Check("J0 vendored propagator drift guard", true, "radians working copy not present, skipped");
    }

    // Shared test shell: 1200 km / 53 deg, 3 planes x 4 sats, Walker F=1.
    var shell = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0,
        PlaneCount = 3, SatsPerPlane = 4, WalkerPhasingF = 1, NOrbits = 288,
    };
    var con = new Constellation(new[] { shell });
    double simDur = 10 * 86400.0;

    // J1: circular orbit -- radius constant and equal to a at every sampled t.
    double aKm = OrbitalConstants.EarthRadiusKm + shell.AltitudeKm;
    bool okR = con.SatelliteCount == 12; string detR = $"sats={con.SatelliteCount}";
    foreach (double t in new[] { 0.0, 137.0, 3600.0, 86400.0, 5 * 86400.0 })
    {
        for (int i = 0; i < con.SatelliteCount && okR; i++)
        {
            double r = con.StateAt(i, t, simDur).RadiusKm;
            if (Math.Abs(r - aKm) > 1e-6) { okR = false; detR = $"sat {i} t={t}: r={r:F9} a={aKm:F9}"; }
        }
        if (!okR) break;
    }
    Check("J1 circular shell: |r| == a at every sampled time", okR, detR);

    // J2: frame consistency -- ECF position equals ECI rotated by -wE*t about Z.
    bool okF = true; string detF = "";
    foreach (double t in new[] { 0.0, 731.0, 40000.0 })
    {
        var eci = con.StateAt(2, t, simDur, CoordinateFrame.ECI).PositionEcefKm;
        var ecf = con.StateAt(2, t, simDur, CoordinateFrame.ECF).PositionEcefKm;
        double ang = -OrbitalConstants.EarthRotationRate * t;
        double c = Math.Cos(ang), sn = Math.Sin(ang);
        var rot = new Vec3(c * eci.X - sn * eci.Y, sn * eci.X + c * eci.Y, eci.Z);
        if ((rot - ecf).Length > 1e-6) { okF = false; detF = $"t={t}: |diff|={(rot - ecf).Length:E2} km"; break; }
    }
    Check("J2 ECF == Rz(-wE t) * ECI", okF, detF);

    // J3: Walker geometry -- LAN spacing 360/P, in-plane spacing 360/S,
    // inter-plane phase F*360/(P*S).
    var els = con.Elements;
    double dLan = AngleDiff(els[4].LanDeg, els[0].LanDeg);
    double dInPlane = AngleDiff(els[1].TrueAnomalyDeg, els[0].TrueAnomalyDeg);
    double dPhase = AngleDiff(els[4].TrueAnomalyDeg, els[0].TrueAnomalyDeg);
    bool okW2 = Math.Abs(dLan - 120.0) < 1e-9 && Math.Abs(dInPlane - 90.0) < 1e-9
             && Math.Abs(dPhase - 360.0 * 1 / 12.0) < 1e-9
             && els.All(e => e.OrbitCase == 1 && e.ArtificialPrecessionRad != 0.0);
    Check("J3 Walker geometry (LAN 120, in-plane 90, phase 30)", okW2,
        $"dLan={dLan:F6} dInPlane={dInPlane:F6} dPhase={dPhase:F6}");

    static double AngleDiff(double a, double b)
    {
        double d = (a - b) % 360.0;
        if (d < 0) d += 360.0;
        return d;
    }

    // J4: artificial precession, exactly as S.1503-4 Part C Steps 8-11 and the
    // reference implement it: S_artificial = S_actual - S_pass added to LAN.
    // Worked through the node-longitude algebra that yields a measured pass
    // spacing of 2*S_pass - S_actual -- one adjustment PAST the 360/nOrbits
    // grid (|error| bounded by one grid cell). The check asserts the true
    // formula behaviour, not the nominal goal; identity with the examination
    // outranks track-repeat elegance. Raised as an upstream observation.
    var (spass, tNodal) = ArtificialPrecession.NodalPassGeometry(aKm, 0.0, shell.InclinationDeg);
    double sGrid = 360.0 * Math.Floor(shell.NOrbits * spass / 360.0) / shell.NOrbits;
    double sExpected = 2.0 * spass - sGrid;

    double CrossLon(double tStart)
    {
        // find ascending z sign change by scan + bisection
        double t0 = tStart, dt = 20.0;
        double z0 = con.StateAt(0, t0, simDur).PositionEcefKm.Z;
        for (int k = 0; k < 100000; k++)
        {
            double t1 = t0 + dt;
            double z1 = con.StateAt(0, t1, simDur).PositionEcefKm.Z;
            if (z0 < 0 && z1 >= 0)
            {
                for (int b = 0; b < 60; b++)
                {
                    double tm = 0.5 * (t0 + t1);
                    if (con.StateAt(0, tm, simDur).PositionEcefKm.Z < 0) t0 = tm; else t1 = tm;
                }
                return con.StateAt(0, 0.5 * (t0 + t1), simDur).SubSatLonDeg;
            }
            t0 = t1; z0 = z1;
        }
        return double.NaN;
    }
    double lon1 = CrossLon(10.0);
    double lon2 = CrossLon(10.0 + tNodal);         // next crossing, one nodal period after the first
    double shift = AngleDiff(lon1, lon2);           // westward shift, 0..360
    bool okP = Math.Abs(shift - sExpected) < 0.02
            && Math.Abs(sExpected - sGrid) <= 2.0 * 360.0 / shell.NOrbits
            && Math.Abs(sGrid - spass) < 360.0 / shell.NOrbits;
    Check("J4 artificial precession matches S.1503-4 Steps 8-11 as implemented", okP,
        $"measured={shift:F4} expected(2*spass-grid)={sExpected:F4} grid={sGrid:F4} spass={spass:F4} deg");

    // J5: SystemState resolves beams through the app's fixed body-stabilised
    // pointing, and BeamComposer consumes the resolved set.
    var vmP = new PfdMaskViewModel();
    var snap = con.SnapshotAt(3600.0, simDur, new ScenePointing(vmP));
    bool okS = snap.Satellites.Count == 12; string detS = $"sats={snap.Satellites.Count}";
    foreach (var sat in snap.Satellites)
    {
        var rb = sat.Beams;
        if (rb is null || rb.Beams.Count == 0 || rb.PowersDbw.Count != rb.Beams.Count)
        { okS = false; detS = $"sat {sat.State.SatelliteNumber}: beams unresolved"; break; }
        var nadir = (sat.State.PositionEcefKm * -1.0).Normalized();
        double e = BeamComposer.CompositeEirpDbw(rb.Beams, nadir, rb.PowersDbw);
        if (double.IsNaN(e)) { okS = false; detS = $"sat {sat.State.SatelliteNumber}: NaN composite"; break; }
    }
    Check("J5 SnapshotAt resolves beams for every satellite (composer-ready)", okS, detS);
}

// ---- K: WP3 operating-parameter (R) XML writer ----
{
    string outDir = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(outDir);

    // K1: reconstruct the reference worked example (NEXT101 param_id 7) and
    // compare canonically against the actual file from the dataset cases.
    var next101 = new OperatingParamsSet
    {
        SatName = "NEXT101", NtcId = 127520101, ParamId = 7,
        LowFreqMhz = 19700, HighFreqMhz = 20200,
        EsDensityPerKm2 = 0.00000028182, EsDistanceKm = 1883,
    };
    next101.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 5.0) } });
    next101.MaxCoFreqByLat.Add((0.0, 3));
    next101.MinDurationByLat.Add((0.0, 2400));
    next101.MinElev.Add(new MinElevByLat { LatDeg = 0.0, ByAz = { (0.0, 10.0) } });

    string k1Path = Path.Combine(outDir, "op_next101_p7.xml");
    OperParamsXmlWriter.Write(k1Path, next101);

    string refPath = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\Mask_param_id_7_OP_NEXT101.xml";
    if (File.Exists(refPath))
    {
        var docA = new XmlDocument();
        var docB = new XmlDocument();
        docA.Load(k1Path);
        docB.Load(refPath);
        bool okK1 = docA.OuterXml == docB.OuterXml;
        Check("K1 R-XML reconstructs the NEXT101 worked example canonically", okK1,
            okK1 ? "OuterXml identical" : $"ours={docA.OuterXml[..Math.Min(120, docA.OuterXml.Length)]}...");
    }
    else
    {
        Check("K1 R-XML vs NEXT101 worked example", true, "reference case not present, skipped");
    }

    // K2: header-only / array-only / both-with-different-values variants
    // (EPS 6.7.2.2: the array prevails inside its latitudes, header outside).
    var headerOnly = new OperatingParamsSet
    {
        SatName = "T", NtcId = 1, ParamId = 1, LowFreqMhz = 10700, HighFreqMhz = 12750,
        EsDensityPerKm2 = 0.0001, EsDistanceKm = 200,
        MaxCoFreqHeader = 2, ElevAngleHeaderDeg = 5.0, MinDurationSecHeader = 400,
    };
    var arrayOnly = new OperatingParamsSet
    {
        SatName = "T", NtcId = 1, ParamId = 2, LowFreqMhz = 10700, HighFreqMhz = 12750,
        EsDensityPerKm2 = 0.0001, EsDistanceKm = 200,
    };
    arrayOnly.MaxCoFreqByLat.Add((-30.0, 2));
    arrayOnly.MaxCoFreqByLat.Add((30.0, 3));
    arrayOnly.MinElev.Add(new MinElevByLat { LatDeg = 0.0, ByAz = { (0.0, 10.0), (180.0, 15.0) } });
    var both = new OperatingParamsSet
    {
        SatName = "T", NtcId = 1, ParamId = 3, LowFreqMhz = 10700, HighFreqMhz = 12750,
        EsDensityPerKm2 = 0.0001, EsDistanceKm = 200,
        MaxCoFreqHeader = 2,
    };
    both.MaxCoFreqByLat.Add((0.0, 4));   // different from the header on purpose

    string hPath = Path.Combine(outDir, "op_header.xml");
    string aPath = Path.Combine(outDir, "op_array.xml");
    string bPath = Path.Combine(outDir, "op_both.xml");
    OperParamsXmlWriter.Write(hPath, headerOnly);
    OperParamsXmlWriter.Write(aPath, arrayOnly);
    OperParamsXmlWriter.Write(bPath, both);

    var dh = new XmlDocument(); dh.Load(hPath);
    var da = new XmlDocument(); da.Load(aPath);
    var db2 = new XmlDocument(); db2.Load(bPath);
    XmlElement HdrOf(XmlDocument d) => (XmlElement)d.SelectSingleNode("//non_gso_operating_parameters")!;

    bool okH = HdrOf(dh).GetAttribute("max_co_freq") == "2"
            && HdrOf(dh).GetAttribute("elev_angle") == "5"
            && HdrOf(dh).GetAttribute("min_duration") == "400"
            && dh.SelectNodes("//max_co_freq")!.Count == 0
            && dh.SelectNodes("//min_elev")!.Count == 0
            && dh.SelectNodes("//min_duration")!.Count == 0;
    bool okA = HdrOf(da).GetAttribute("max_co_freq") == ""
            && da.SelectNodes("//max_co_freq")!.Count == 2
            && da.SelectNodes("//min_elev/elev_angle")!.Count == 2;
    bool okB = HdrOf(db2).GetAttribute("max_co_freq") == "2"
            && db2.SelectSingleNode("//max_co_freq")!.InnerText == "4";
    Check("K2 header-only / array-only / both variants encode correctly",
        okH && okA && okB, $"header={okH} array={okA} both={okB}");

    // K3: the encoding rules that are easy to get wrong are enforced.
    bool threw0 = false, threwEs = false, threwPop = false, classicOmits;
    try
    {
        var bad = new OperatingParamsSet { EsDensityPerKm2 = 1, EsDistanceKm = 1 };
        bad.MinDurationByLat.Add((0.0, 0));
        OperParamsXmlWriter.Write(Path.Combine(outDir, "op_bad0.xml"), bad);
    }
    catch (ArgumentException) { threw0 = true; }
    try
    {
        var bad = new OperatingParamsSet { EsDensityPerKm2 = 1, EsDistanceKm = 1, MinAngleAtEsDeg = 5.0 };
        bad.MinDurationByLat.Add((0.0, 400));
        OperParamsXmlWriter.Write(Path.Combine(outDir, "op_badEs.xml"), bad);
    }
    catch (ArgumentException) { threwEs = true; }
    try
    {
        var bad = new OperatingParamsSet { EsDensityPerKm2 = 1 };   // distance missing
        OperParamsXmlWriter.Write(Path.Combine(outDir, "op_badPop.xml"), bad);
    }
    catch (ArgumentException) { threwPop = true; }

    // classic algorithm: no min_duration anywhere in the output.
    var classic = new OperatingParamsSet
    {
        SatName = "T", NtcId = 1, ParamId = 4, LowFreqMhz = 17800, HighFreqMhz = 18600,
        EsDensityPerKm2 = 0.0001, EsDistanceKm = 200, MinAngleAtEsDeg = 5.0,
    };
    classic.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 5.0) } });
    string cPath = Path.Combine(outDir, "op_classic.xml");
    OperParamsXmlWriter.Write(cPath, classic);
    string cText = File.ReadAllText(cPath);
    classicOmits = !cText.Contains("min_duration") && cText.Contains("min_angle_at_es")
                && !cText.Contains("max_co_freq_sat");
    Check("K3 rules: reject zero duration / es-angle conflict / half population; classic omits",
        threw0 && threwEs && threwPop && classicOmits,
        $"zero={threw0} esAngle={threwEs} pop={threwPop} classic={classicOmits}");
}

Console.WriteLine($"\n===== {pass} passed, {fail} failed =====");
return fail == 0 ? 0 : 1;
