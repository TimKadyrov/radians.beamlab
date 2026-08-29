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
    // J0: drift guard -- every vendored radians source (orbits/ propagator,
    // epfdshare/ statistics components) must stay byte-identical to the
    // radians working copy when it is present.
    string radiansRoot = @"C:\Projects\_EPFD\radians\radians";
    // Locate the repo's core dir robustly: walk up from the run directory to
    // the solution marker, falling back to the standard path (the harness
    // may run from an out-of-tree output directory).
    string coreDir = null;
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
    {
        if (File.Exists(Path.Combine(d.FullName, "radians.beamlab.slnx")))
        {
            coreDir = Path.Combine(d.FullName, "src", "radians.beamlab.core");
            break;
        }
    }
    coreDir ??= Path.Combine("C:" + Path.DirectorySeparatorChar + "Projects",
        "radians.beamlab", "src", "radians.beamlab.core");
    (string local, string source)[] vendoredPairs =
    {
        (@"orbits\Propagation\OrbitPropagator.cs", @"radians.orbits.core\Propagation\OrbitPropagator.cs"),
        (@"orbits\Propagation\OrbitalElements.cs", @"radians.orbits.core\Propagation\OrbitalElements.cs"),
        (@"orbits\Propagation\StateVector.cs", @"radians.orbits.core\Propagation\StateVector.cs"),
        (@"orbits\Propagation\CoordinateFrame.cs", @"radians.orbits.core\Propagation\CoordinateFrame.cs"),
        (@"orbits\Utilities\AngleUtilities.cs", @"radians.orbits.core\Utilities\AngleUtilities.cs"),
        (@"orbits\Utilities\OrbitalConstants.cs", @"radians.orbits.core\Utilities\OrbitalConstants.cs"),
        (@"orbits\Utilities\VectorOperations.cs", @"radians.orbits.core\Utilities\VectorOperations.cs"),
        (@"orbits\Models\Vector3D.cs", @"radians.orbits.core\Models\Vector3D.cs"),
        (@"orbits\Models\GeocentricCoordinate.cs", @"radians.orbits.core\Models\GeocentricCoordinate.cs"),
        (@"epfdshare\radlimits.cs", @"radlimits\radlimits.cs"),
        (@"epfdshare\EpfdAccumulator.cs", @"radcompute1503-2\EpfdAccumulator.cs"),
        (@"epfdshare\ApLib.cs", @"radantenna\ApLib.cs"),
    };
    if (Directory.Exists(radiansRoot))
    {
        bool okDrift = true; string detDrift = $"files={vendoredPairs.Length}";
        foreach (var (local, source) in vendoredPairs)
        {
            string a = Path.Combine(coreDir, local);
            string b = Path.Combine(radiansRoot, source);
            if (!File.Exists(a) || !File.Exists(b) ||
                !File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b)))
            {
                okDrift = false; detDrift = $"drift: {local}"; break;
            }
        }
        Check("J0 vendored radians sources byte-identical", okDrift, detDrift);
    }
    else
    {
        Check("J0 vendored source drift guard", true, "radians working copy not present, skipped");
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

// ---- L: WP4 mask derivation over the reachable configuration set ----
{
    // L1: the analytic pass-heading formula matches the vendored
    // propagator's inertial velocity direction on both pass branches.
    var shellL = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
    };
    var conL = new Constellation(new[] { shellL });
    double simL = 86400.0;
    bool okL1 = true; string detL1 = "";
    foreach (double t in new[] { 300.0, 900.0, 1500.0, 2500.0, 3200.0 })
    {
        var s0 = conL.StateAt(0, t, simL, Radians.Orbits.Core.Propagation.CoordinateFrame.ECI);
        var s1 = conL.StateAt(0, t + 0.1, simL, Radians.Orbits.Core.Propagation.CoordinateFrame.ECI);
        var v = (s1.PositionEcefKm - s0.PositionEcefKm) * (1.0 / 0.1);
        var (nB, eB, dB3) = SatNedBasis(s0.SubSatLatDeg, s0.SubSatLonDeg);
        double headMeas = Math.Atan2(Vec3.Dot(v, eB), Vec3.Dot(v, nB)) * 180.0 / Math.PI;
        if (GroundTrack.HeadingsAtLatitude(53.0, s0.SubSatLatDeg) is not { } hv)
        { okL1 = false; detL1 = $"t={t}: latitude {s0.SubSatLatDeg:F2} unreachable?"; break; }
        double want = Vec3.Dot(v, nB) > 0 ? hv.AscendingDeg : hv.DescendingDeg;
        double dh = Math.Abs((((headMeas - want) % 360.0) + 540.0) % 360.0 - 180.0);
        if (dh > 0.05) { okL1 = false; detL1 = $"t={t}: meas={headMeas:F3} want={want:F3}"; break; }
    }
    Check("L1 pass headings match propagated inertial velocity", okL1, detL1);

    // L2: BodyYawDeg turns the layout rigidly about nadir -- matching lattice
    // beam rotates in azimuth by exactly the yaw, off-nadir unchanged.
    var scL = new SceneModel
    {
        PatternKind = BeamPatternKind.Taylor_1p4, AutoMode = true,
        FrequencyGHz = 12.0, GmDbi = 35.0, ThetaBDeg = 4.0,
        MinElevDeg = 10.0, AltitudeKm = 1200.0, SubSatLatDeg = 0.0, SubSatLonDeg = 0.0,
    };
    scL.RebuildBeams();
    int countL0 = scL.Beams.Count;
    var (nL, eL, dL) = SatNedBasis(0.0, 0.0);
    double AzOf(Beam b) => Math.Atan2(Vec3.Dot(b.Boresight, eL), Vec3.Dot(b.Boresight, nL)) * 180.0 / Math.PI;
    var b0L = scL.Beams.First(b => b.LatticeI == 2 && b.LatticeJ == 1);
    double az0L = AzOf(b0L), off0L = b0L.OffNadirDeg;
    scL.BodyYawDeg = 25.0;
    scL.RebuildBeams();
    var b1L = scL.Beams.First(b => b.LatticeI == 2 && b.LatticeJ == 1);
    double dAzL = ((AzOf(b1L) - az0L - 25.0) % 360.0 + 540.0) % 360.0 - 180.0;
    bool okL2 = Math.Abs(dAzL) < 1e-9 && Math.Abs(b1L.OffNadirDeg - off0L) < 1e-9
             && scL.Beams.Count == countL0;
    Check("L2 BodyYawDeg rotates layout rigidly (az +25 deg, off-nadir kept)", okL2,
        $"dAz={dAzL:E2} dOff={b1L.OffNadirDeg - off0L:E2} beams {countL0}->{scL.Beams.Count}");

    // L3: the envelope sampler equals the max over the per-heading fields at
    // every probe, and the two headings genuinely differ somewhere.
    var vmL = new PfdMaskViewModel();
    var optsL = new MaskXmlExportOptions { Kind = MaskPlotKind.AzEl, BStepDeg = 30.0, CStepDeg = 30.0 };
    var sampL = new ReachableEnvelopeSampler(vmL, optsL, 53.0);
    sampL.PrepareLatitude(35.0);

    var hhL = GroundTrack.HeadingsAtLatitude(53.0, 35.0)!.Value;
    PfdMaskField FieldAt(double psi)
    {
        var gen = new PfdMaskViewModel();
        vmL.CopySettingsTo(gen);
        gen.MaskKind = MaskPlotKind.AzEl;
        gen.Scene.SubSatLatDeg = 35.0;
        gen.Scene.BodyYawDeg = psi;
        gen.RebuildForCompute();
        var f = new PfdMaskField();
        f.Rebuild(gen);
        return f;
    }
    var fAsc = FieldAt(hhL.AscendingDeg);
    var fDesc = FieldAt(hhL.DescendingDeg);

    bool okL3 = true; string detL3 = ""; int differ = 0, probes = 0;
    for (double az = -85; az <= 85 && okL3; az += 10)
    for (double el = -85; el <= 85 && okL3; el += 10)
    {
        double a1 = fAsc.SampleMaxIn(az, el, 15.0, 15.0);
        double a2 = fDesc.SampleMaxIn(az, el, 15.0, 15.0);
        double want = Math.Max(a1, a2);
        double got = sampL.SampleMaxIn(az, el, 15.0, 15.0);
        probes++;
        if (Math.Abs(a1 - a2) > 0.1 && !double.IsNegativeInfinity(a1) && !double.IsNegativeInfinity(a2)) differ++;
        bool same = double.IsNegativeInfinity(want) ? double.IsNegativeInfinity(got)
                                                    : Math.Abs(got - want) < 1e-9;
        if (!same) { okL3 = false; detL3 = $"az={az} el={el}: got={got} want={want}"; }
    }
    Check("L3 envelope == max over pass-heading fields; headings differ", okL3 && differ > 0,
        okL3 ? $"probes={probes} cells-where-headings-differ={differ}" : detL3);
}

// ---- M: WP7 SNS v10 notice written into real BR databases ----
{
    string donorSrs = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 SRS.MDB";
    string donorMasks = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 Masks.MDB";
    string[] dllDirs =
    {
        @"C:\Projects\_EPFD\radians\radians\dlls",
        @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
    };
    string dllDir = dllDirs.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdMasksApi64.dll")));

    if (File.Exists(donorSrs) && File.Exists(donorMasks) && dllDir is not null)
    {
        // Crash-proof: an escaping exception here leaves a wedged process
        // holding the BR native DLL; fail the checks instead.
        try
        {
        string outDir = Path.Combine(AppContext.BaseDirectory, "exp");
        Directory.CreateDirectory(outDir);
        const int ntc = 900123456;
        const string sat = "BEAMLAB1";

        // The notice describes the same Walker shell the J-section propagates.
        var shellM = new ConstellationShell
        {
            AltitudeKm = 1200.0, InclinationDeg = 53.0,
            PlaneCount = 3, SatsPerPlane = 4, WalkerPhasingF = 1, NOrbits = 288,
        };
        var notice = new SrsNotice { NtcId = ntc, SatName = sat, Adm = "LUX" };
        notice.AddShell(shellM);
        notice.MaskInfo.Add(new SrsMaskInfo(1, 19700, 20200, 'P', 'Z'));
        notice.MaskInfo.Add(new SrsMaskInfo(7, 19700, 20200, 'R', null));
        var scen = new SrsScenario { ScenId = 1, ScenName = "Downlink 19.7-20.2 GHz" };
        scen.Frequencies.Add(new SrsFreqRange(1, 'E', 19700, 20200));
        scen.PfdMaskLinks.Add(new SrsMaskLink(1, MaskId: 1));
        notice.Scenarios.Add(scen);
        notice.OperatingParamIds.Add(7);

        // M1: SRS content round-trips through the cloned donor database.
        string outSrs = Path.Combine(outDir, "BEAMLAB1 SRS.MDB");
        SrsMdbWriter.WriteSrs(donorSrs, outSrs, notice);

        using (var conn = new System.Data.OleDb.OleDbConnection(
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={outSrs}"))
        {
            conn.Open();
            object Scalar(string sql)
            {
                using var cmd = new System.Data.OleDb.OleDbCommand(sql, conn);
                return cmd.ExecuteScalar();
            }
            int orbits = Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM orbit WHERE ntc_id={ntc}"));
            int phases = Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM phase WHERE ntc_id={ntc}"));
            double lan2 = Convert.ToDouble(Scalar($"SELECT right_asc FROM orbit WHERE ntc_id={ntc} AND orb_id=2"));
            double ph21 = Convert.ToDouble(Scalar($"SELECT phase_ang FROM phase WHERE ntc_id={ntc} AND orb_id=2 AND orb_sat_id=1"));
            string satRb = (string)Scalar($"SELECT sat_name FROM com_el WHERE ntc_id={ntc}");
            int lnk3 = Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM mask_lnk3 WHERE ntc_id={ntc} AND param_id=7"));
            int freqs = Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM epfd_freq WHERE ntc_id={ntc} AND scen_id=1"));
            int leftovers = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM orbit WHERE ntc_id=127520101"));
            string fsk = (string)Scalar($"SELECT f_stn_keep FROM orbit WHERE ntc_id={ntc} AND orb_id=1");

            bool okM1 = orbits == 3 && phases == 12 && Math.Abs(lan2 - 120.0) < 1e-9
                     && Math.Abs(ph21 - 30.0) < 1e-9 && satRb == sat && lnk3 == 1
                     && freqs == 1 && leftovers == 0 && fsk == "N";
            Check("M1 SRS v10 notice round-trips through cloned donor", okM1,
                $"orbits={orbits} phases={phases} lan2={lan2} ph21={ph21} sat={satRb} lnk3={lnk3} freqs={freqs} leftovers={leftovers} fsk={fsk}");
        }

        // Generate the two mask contents with matching identity: WP4 pfd XML
        // and WP3 operating-parameter XML.
        string pfdXml = Path.Combine(outDir, "beamlab_pfd_mask1.xml");
        var vmM = new PfdMaskViewModel();
        var optsM = new MaskXmlExportOptions
        {
            SatName = sat, NtcId = ntc, MaskId = 1, RefBwKHz = 40,
            LowFreqMhz = 19700, HighFreqMhz = 20200,
            LatMinDeg = -10, LatMaxDeg = 10, LatStepDeg = 10,
            BStepDeg = 30, CStepDeg = 60,
            Kind = MaskPlotKind.AzEl, Format = MaskExportFormat.Xml, OutputPath = pfdXml,
        };
        MaskXmlExport.GenerateAsync(new ReachableEnvelopeSampler(vmM, optsM, shellM.InclinationDeg),
            optsM, null, CancellationToken.None).GetAwaiter().GetResult();

        string opXml = Path.Combine(outDir, "beamlab_op_param7.xml");
        var opSet = new OperatingParamsSet
        {
            SatName = sat, NtcId = ntc, ParamId = 7,
            LowFreqMhz = 19700, HighFreqMhz = 20200,
            EsDensityPerKm2 = 0.0001, EsDistanceKm = 200,
        };
        opSet.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 10.0) } });
        opSet.MaxCoFreqByLat.Add((0.0, 1));
        opSet.MinElev.Add(new MinElevByLat { LatDeg = 0.0, ByAz = { (0.0, 10.0) } });
        OperParamsXmlWriter.Write(opXml, opSet);

        // M2: masks stored through the BR native API and extracted back.
        SrsMdbWriter.EpfdMasksDllDirectory = dllDir;
        string outMasks = Path.Combine(outDir, "BEAMLAB1 Masks.MDB");
        var stored = SrsMdbWriter.WriteMasks(donorMasks, outMasks, ntc, sat, new[]
        {
            new SrsMdbWriter.MaskContent(1, pfdXml, 'P', 19700, 20200),
            new SrsMdbWriter.MaskContent(7, opXml, 'R', 19700, 20200),
        });

        bool okStore = stored.All(r => r.Status == 0);
        string extP = Path.Combine(outDir, "extract_mask1.xml");
        string extR = Path.Combine(outDir, "extract_param7.xml");
        int exP = SrsMdbWriter.ExtractMask(outMasks, ntc, 1, extP);
        int exR = SrsMdbWriter.ExtractMask(outMasks, ntc, 7, extR);

        bool SameXml(string a, string b)
        {
            var da2 = new XmlDocument(); da2.Load(a);
            var db3 = new XmlDocument(); db3.Load(b);
            return da2.OuterXml == db3.OuterXml;
        }
        bool okM2 = okStore && exP == 0 && exR == 0 && SameXml(pfdXml, extP) && SameXml(opXml, extR);
        Check("M2 masks stored via BR native API, extracted back identical", okM2,
            $"store=[{string.Join(",", stored.Select(r => r.MaskId + ":" + r.Status))}] extract={exP},{exR}");
        }
        catch (Exception ex)
        {
            Check("M1/M2 SNS v10 notice writing", false, "exception: " + ex.Message);
        }
    }
    else
    {
        Check("M1/M2 SNS v10 notice writing", true, "donor MDBs or EpfdMasksApi64.dll not present, skipped");
    }
}

// ---- N: WP5/WP6 e.i.r.p. mask writers and SS-mask generation ----
{
    string donorMasksN = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 Masks.MDB";
    string[] dllDirsN =
    {
        @"C:\Projects\_EPFD\radians\radians\dlls",
        @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
    };
    string dllDirN = dllDirsN.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdMasksApi64.dll")));
    string outDirN = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(outDirN);

    EirpMaskTable ParseEirp(string path, bool es)
    {
        var doc = new XmlDocument();
        doc.Load(path);
        var sys2 = (XmlElement)doc.SelectSingleNode("/satellite_system")!;
        var head = (XmlElement)doc.SelectSingleNode(es ? "//eirp_mask_es" : "//eirp_mask_ss")!;
        var t = new EirpMaskTable
        {
            NtcId = int.Parse(sys2.GetAttribute("ntc_id")),
            SatName = sys2.GetAttribute("sat_name"),
            MaskId = int.Parse(head.GetAttribute("mask_id")),
            LowFreqMhz = double.Parse(head.GetAttribute("low_freq_mhz"), CultureInfo.InvariantCulture),
            HighFreqMhz = double.Parse(head.GetAttribute("high_freq_mhz"), CultureInfo.InvariantCulture),
            RefBwKHz = head.HasAttribute("refbw_khz")
                ? double.Parse(head.GetAttribute("refbw_khz"), CultureInfo.InvariantCulture) : null,
            MinElevDeg = head.HasAttribute("min_elev")
                ? double.Parse(head.GetAttribute("min_elev"), CultureInfo.InvariantCulture) : null,
            EsId = head.HasAttribute("ES_ID") ? int.Parse(head.GetAttribute("ES_ID")) : -1,
        };
        foreach (XmlElement byA in head.SelectNodes("by_a")!)
        {
            var blk = new EirpLatBlock { LatDeg = double.Parse(byA.GetAttribute("a"), CultureInfo.InvariantCulture) };
            foreach (XmlElement e in byA.SelectNodes("eirp")!)
                blk.ByAngle.Add((double.Parse(e.GetAttribute("d"), CultureInfo.InvariantCulture),
                                 double.Parse(e.InnerText, CultureInfo.InvariantCulture)));
            t.Blocks.Add(blk);
        }
        return t;
    }

    bool SameDoc(string a, string b)
    {
        var da4 = new XmlDocument(); da4.Load(a);
        var db4 = new XmlDocument(); db4.Load(b);
        return da4.OuterXml == db4.OuterXml;
    }

    if (File.Exists(donorMasksN) && dllDirN is not null)
    {
        SrsMdbWriter.EpfdMasksDllDirectory = dllDirN;
        try
        {
        // N1: SS worked mask -> parse -> rewrite -> canonically identical.
        string ssRef = Path.Combine(outDirN, "ref_ss_mask3.xml");
        string esRef = Path.Combine(outDirN, "ref_es_mask6.xml");
        int x3 = SrsMdbWriter.ExtractMask(donorMasksN, 127520101, 3, ssRef);
        int x6 = SrsMdbWriter.ExtractMask(donorMasksN, 127520101, 6, esRef);

        string ssOut = Path.Combine(outDirN, "rt_ss_mask3.xml");
        var ssT = ParseEirp(ssRef, es: false);
        var wSs = EirpMaskXmlWriter.WriteSs(ssOut, ssT);
        Check("N1 SS eirp mask round-trips the worked file canonically",
            x3 == 0 && SameDoc(ssRef, ssOut), $"extract={x3} warnings={wSs.Count}");

        // N2: ES worked mask -- same, and its monotonicity bend is reported.
        string esOut = Path.Combine(outDirN, "rt_es_mask6.xml");
        var esT = ParseEirp(esRef, es: true);
        var wEs = EirpMaskXmlWriter.WriteEs(esOut, esT);
        Check("N2 ES eirp mask round-trips; should-rule violations reported",
            x6 == 0 && SameDoc(esRef, esOut) && wEs.Count > 0, $"extract={x6} warnings={wEs.Count}");

        // N3: WP6 generation physics -- at theta 0 the mask equals the nadir
        // composite; every row envelopes a directly sampled azimuth sweep.
        var vmN = new PfdMaskViewModel();
        double[] latsN = { 0.0, 35.0 };
        double[] angsN = { 0.0, 10.0, 30.0, 60.0, 90.0, 120.0, 180.0 };
        var gen = SatEirpMaskBuilder.Build(vmN, 53.0, latsN, angsN, azimuthSamples: 90);

        var genChk = new PfdMaskViewModel();
        vmN.CopySettingsTo(genChk);
        genChk.Scene.SubSatLatDeg = 0.0;
        genChk.Scene.BodyYawDeg = GroundTrack.HeadingsAtLatitude(53.0, 0.0)!.Value.AscendingDeg;
        genChk.RebuildForCompute();
        var powersN = PfdMaskField.BeamPowersDbw(genChk);
        var (nN, eN, dN) = SatNedBasis(0.0, 0.0);
        double nadirE = BeamComposer.CompositeEirpDbw(genChk.Scene.Beams,
            NedToEcef(BeamDirNed(0.0, 0.0), nN, eN, dN).Normalized(), powersN);
        double mask0 = gen.Blocks[0].ByAngle.First(r => r.AngleDeg == 0.0).EirpDbw;

        bool okN3 = Math.Abs(mask0 - nadirE) < 1e-9;
        string detN3 = $"mask(0)={mask0:F3} nadir={nadirE:F3}";
        foreach (var (ang, eirp) in gen.Blocks[0].ByAngle)
        {
            for (int k = 0; k < 30 && okN3; k++)
            {
                double az = 360.0 * k / 30.0;
                double e = BeamComposer.CompositeEirpDbw(genChk.Scene.Beams,
                    NedToEcef(BeamDirNed(ang, az), nN, eN, dN).Normalized(), powersN);
                if (e > eirp + 1e-9) { okN3 = false; detN3 = $"theta={ang} az={az}: sample {e:F3} > mask {eirp:F3}"; }
            }
        }
        Check("N3 generated SS mask: nadir-exact, envelopes az sweep", okN3, detN3);

        // N4: generated S and E masks store via the BR native API and
        // round-trip through its extractor.
        gen.SatName = "BEAMLAB1"; gen.NtcId = 900123456; gen.MaskId = 3;
        gen.LowFreqMhz = 17800; gen.HighFreqMhz = 18600;
        string genSs = Path.Combine(outDirN, "beamlab_ss_mask3.xml");
        EirpMaskXmlWriter.WriteSs(genSs, gen);

        var esDecl = new EirpMaskTable
        {
            SatName = "BEAMLAB1", NtcId = 900123456, MaskId = 6,
            LowFreqMhz = 27500, HighFreqMhz = 30000, RefBwKHz = 40, MinElevDeg = 10, EsId = -1,
        };
        var esBlk = new EirpLatBlock { LatDeg = 0.0 };
        foreach (var (ang, g) in new[] { (0.0, 34.0), (5.0, 10.0), (20.0, -5.0), (180.0, -10.0) })
            esBlk.ByAngle.Add((ang, g));
        esDecl.Blocks.Add(esBlk);
        string genEs = Path.Combine(outDirN, "beamlab_es_mask6.xml");
        EirpMaskXmlWriter.WriteEs(genEs, esDecl);

        string outMasksN = Path.Combine(outDirN, "BEAMLAB1 EirpMasks.MDB");
        var storedN = SrsMdbWriter.WriteMasks(donorMasksN, outMasksN, 900123456, "BEAMLAB1", new[]
        {
            new SrsMdbWriter.MaskContent(3, genSs, 'S', 17800, 18600),
            new SrsMdbWriter.MaskContent(6, genEs, 'E', 27500, 30000),
        });
        string exS = Path.Combine(outDirN, "extract_ss3.xml");
        string exE = Path.Combine(outDirN, "extract_es6.xml");
        int rS = SrsMdbWriter.ExtractMask(outMasksN, 900123456, 3, exS);
        int rE = SrsMdbWriter.ExtractMask(outMasksN, 900123456, 6, exE);
        bool okN4 = storedN.All(r => r.Status == 0) && rS == 0 && rE == 0
                 && SameDoc(genSs, exS) && SameDoc(genEs, exE);
        Check("N4 generated S+E masks: BR native store + extract identical", okN4,
            $"store=[{string.Join(",", storedN.Select(r => r.MaskId + ":" + r.Status))}] extract={rS},{rE}");
        }
        catch (Exception ex)
        {
            Check("N eirp mask checks", false, "exception: " + ex.Message);
        }
    }
    else
    {
        Check("N eirp mask checks", true, "donor Masks.MDB or EpfdMasksApi64.dll not present, skipped");
    }
}

// ---- O: WP8 epfd(down) statistics over the simulated system ----
{
    // Shared victim: GSO ES at (0, 0) tracking the GSO satellite at lon 0,
    // Rec. S.1428 receive antenna (the epfd(down) reference), 12 GHz, 60 cm.
    var ant = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 12000.0, 0.6);
    var victimO = new EpfdDownVictim { EsLatDeg = 0, EsLonDeg = 0, GsoLonDeg = 0, Antenna = ant };
    var limitsO = new List<radlimits.LimitPoint>
    {
        new radlimits.LimitPoint { EPFD = -300.0, Perc = 0.001 },   // impossible: must fail
        new radlimits.LimitPoint { EPFD = 0.0, Perc = 100.0 },      // generous: must pass
    };

    // O1: analytic single-satellite case -- sat directly over the ES at t=0,
    // so phi = 0, Grx = Gmax, and epfd equals the hand-computed pfd.
    var oneSat = new Constellation(new[] { new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
    } });
    var vmO = new PfdMaskViewModel();
    var res1 = EpfdDown.Run(oneSat, new ScenePointing(vmO), victimO, 1.0, 1, limitsO);

    // Independent hand value: rebuild the scene at the satellite's state and
    // compose toward the ES directly.
    var st0 = oneSat.StateAt(0, 0.0, 1.0);
    var genO = new PfdMaskViewModel();
    vmO.CopySettingsTo(genO);
    genO.Scene.SubSatLatDeg = st0.SubSatLatDeg;
    genO.Scene.SubSatLonDeg = st0.SubSatLonDeg;
    genO.Scene.AltitudeKm = st0.AltitudeKm;
    genO.Scene.BodyYawDeg = st0.HeadingDeg;
    genO.RebuildForCompute();
    var powO = PfdMaskField.BeamPowersDbw(genO);
    var esO = GeodeticToEcef(0, 0, 0);
    var toEsO = (esO - st0.PositionEcefKm).Normalized();
    double eirpO = BeamComposer.CompositeEirpDbw(genO.Scene.Beams, toEsO, powO);
    double dMO = (esO - st0.PositionEcefKm).Length * 1000.0;
    double pfdO = eirpO - 10.0 * Math.Log10(4.0 * Math.PI * dMO * dMO);

    bool okO1 = res1.Steps == 1 && res1.QuietSteps == 0
             && Math.Abs(res1.MaxEpfdDb - pfdO) < 1e-9
             && Math.Abs(st0.SubSatLatDeg) < 1e-6 && Math.Abs(st0.SubSatLonDeg) < 1e-6;
    Check("O1 single-sat overhead: epfd == pfd (phi=0, Grx=Gmax)", okO1,
        $"epfd={res1.MaxEpfdDb:F4} pfd={pfdO:F4} satLat={st0.SubSatLatDeg:F4} satLon={st0.SubSatLonDeg:F4}");

    // O2: constellation run -- totals, CDF shape, and the D7.1.3 comparison.
    var conO = new Constellation(new[] { new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0,
        PlaneCount = 3, SatsPerPlane = 4, WalkerPhasingF = 1, NOrbits = 288,
    } });
    var resN = EpfdDown.Run(conO, new ScenePointing(vmO), victimO, 30.0, 200, limitsO);
    var (epfdVals, percents) = resN.Accumulator.BuildCdf();
    bool cdfMono = true;
    for (int i = 1; i < percents.Length; i++)
        if (percents[i] > percents[i - 1] + 1e-9) { cdfMono = false; break; }
    var (passes, _) = resN.Accumulator.CompareWithLimits(limitsO);
    bool okO2 = resN.Accumulator.TotalSamples == 200 && cdfMono
             && passes.Length == 2 && !passes[0] && passes[1]
             && resN.MaxEpfdDb > -200 && resN.MaxEpfdDb < 0;
    Check("O2 constellation run: totals, monotone CDF, D7.1.3 verdicts", okO2,
        $"samples={resN.Accumulator.TotalSamples} quiet={resN.QuietSteps} max={resN.MaxEpfdDb:F2} pass=[{string.Join(",", passes)}]");

    // O3: the acceptance direction in miniature (spec Sec. 8) -- at t=0 the
    // satellite sits exactly on the lat=0 mask block at an enveloped pass
    // heading, so the derived mask must bound the live pfd toward any ES.
    string outDirO = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(outDirO);
    string maskO = Path.Combine(outDirO, "wp8_mask.xml");
    var optsO = new MaskXmlExportOptions
    {
        SatName = "T", NtcId = 7, MaskId = 1, RefBwKHz = 40,
        LatMinDeg = -10, LatMaxDeg = 10, LatStepDeg = 10,
        BStepDeg = 5, CStepDeg = 5,
        Kind = MaskPlotKind.AzEl, Format = MaskExportFormat.Xml, OutputPath = maskO,
    };
    MaskXmlExport.GenerateAsync(new ReachableEnvelopeSampler(vmO, optsO, 53.0),
        optsO, null, CancellationToken.None).GetAwaiter().GetResult();

    var loadedO = MaskXmlImport.Load(maskO);
    var blk0 = loadedO.Blocks.First(b => Math.Abs(b.LatDeg) < 1e-9);
    var fieldO = new PfdMaskField();
    MaskXmlImport.ApplyBlockToField(loadedO, blk0, fieldO);

    var (nO, eO2, dO2) = SatNedBasis(st0.SubSatLatDeg, st0.SubSatLonDeg);
    bool okO3 = true; string detO3 = "";
    foreach (var (esLat, esLon) in new[] { (0.0, 0.0), (5.0, 3.0), (-8.0, 10.0), (15.0, -6.0) })
    {
        var esP = GeodeticToEcef(esLat, esLon, 0);
        var dirP = (esP - st0.PositionEcefKm).Normalized();
        double eP = BeamComposer.CompositeEirpDbw(genO.Scene.Beams, dirP, powO);
        double dPm = (esP - st0.PositionEcefKm).Length * 1000.0;
        double pfdP = eP - 10.0 * Math.Log10(4.0 * Math.PI * dPm * dPm);
        double azP = Math.Atan2(Vec3.Dot(dirP, eO2), Vec3.Dot(dirP, dO2)) * 180.0 / Math.PI;
        double elP = Math.Asin(Math.Clamp(Vec3.Dot(dirP, nO), -1.0, 1.0)) * 180.0 / Math.PI;
        double maskV = fieldO.MaskReadRaw(azP, elP);
        if (maskV < pfdP - 0.05001)
        {
            okO3 = false;
            detO3 = $"ES({esLat},{esLon}): mask={maskV:F2} < live={pfdP:F2} at az={azP:F1} el={elP:F1}";
            break;
        }
    }
    Check("O3 derived mask bounds the live composition (examination >= simulation)", okO3,
        okO3 ? "4 earth stations bounded" : detO3);
}

// ---- P: WP2 scheduler -- the declared parameters are its true bounds ----
{
    var shellP = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0,
        PlaneCount = 3, SatsPerPlane = 4, WalkerPhasingF = 1, NOrbits = 288,
    };
    var conP = new Constellation(new[] { shellP });
    var geoP = ServiceGeography.Grid(-20, 20, -20, 20, 800.0);
    double simP = 86400.0;
    var vmP2 = new PfdMaskViewModel();   // scene defaults: eps_min 10, alpha_excl 10 -- matching the declaration

    var declaredP = new OperatingParamsSet
    {
        SatName = "T", NtcId = 1, ParamId = 1, LowFreqMhz = 19700, HighFreqMhz = 20200,
        EsDensityPerKm2 = 0.0001, EsDistanceKm = 200,
        MaxCoFreqHeader = 1, ElevAngleHeaderDeg = 10.0, MinDurationSecHeader = 300,
    };
    declaredP.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 10.0) } });

    // P1: run the schedule and check every granted link against the declared
    // set at every step; verify link bookkeeping and step accounting.
    var schedP = new Scheduler(conP, geoP, declaredP, new ScenePointing(vmP2), simP);
    bool okP1 = true; string detP1 = "";
    long linksTotal = 0; int volTotal = 0, forcedTotal = 0;
    var lastSat = new Dictionary<int, (int sat, double start)>();
    for (int k = 0; k < 40 && okP1; k++)
    {
        double t = k * 60.0;
        var st = schedP.Step(t);
        volTotal += st.VoluntaryHandovers; forcedTotal += st.ForcedHandovers;
        linksTotal += st.Links.Count;
        if (st.Links.Count + st.UnservedCellLinks != geoP.Cells.Count)
        { okP1 = false; detP1 = $"t={t}: {st.Links.Count}+{st.UnservedCellLinks} != {geoP.Cells.Count}"; break; }

        var perCell = new Dictionary<int, int>();
        foreach (var l in st.Links)
        {
            var cell = geoP.Cells.First(c => c.CellId == l.CellId);
            if (l.ElevationDeg < 10.0 - 1e-9) { okP1 = false; detP1 = $"t={t} cell {l.CellId}: elev {l.ElevationDeg:F2} < 10"; break; }
            if (l.AlphaDeg < 10.0 - 1e-9) { okP1 = false; detP1 = $"t={t} cell {l.CellId}: alpha {l.AlphaDeg:F2} < 10"; break; }
            perCell[l.CellId] = perCell.GetValueOrDefault(l.CellId) + 1;
            if (perCell[l.CellId] > 1) { okP1 = false; detP1 = $"t={t} cell {l.CellId}: Nco violated"; break; }
            if (lastSat.TryGetValue(l.CellId, out var prev) && prev.sat == l.SatelliteNumber
                && Math.Abs(prev.start - l.StartTimeSec) > 1e-9)
            { okP1 = false; detP1 = $"t={t} cell {l.CellId}: dwell start drifted"; break; }
            lastSat[l.CellId] = (l.SatelliteNumber, l.StartTimeSec);
        }
        // A dropped link legitimately restarts its dwell on re-acquisition:
        // forget cells that were not served this step.
        var servedNow = st.Links.Select(l => l.CellId).ToHashSet();
        foreach (var cid in lastSat.Keys.Where(c => !servedNow.Contains(c)).ToList())
            lastSat.Remove(cid);
    }
    Check("P1 scheduled links honour the declared bounds at every step", okP1 && linksTotal > 200,
        okP1 ? $"links={linksTotal} voluntary={volTotal} forced={forcedTotal}" : detP1);

    // P2: dwell semantics. One plane of 15 satellites (24 deg spacing) over a
    // single equatorial cell: with a huge declared min_duration no voluntary
    // handover ever happens; with min_duration absent the highest-elevation
    // policy switches voluntarily as satellites pass over.
    var conP2 = new Constellation(new[] { new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 15,
    } });
    var geo1 = new ServiceGeography(new[] { new ServiceCell(1, 0.0, 0.0) }, 800.0);

    OperatingParamsSet DeclP2(int? minDur) => new OperatingParamsSet
    {
        SatName = "T", NtcId = 1, ParamId = 2, LowFreqMhz = 19700, HighFreqMhz = 20200,
        EsDensityPerKm2 = 0.0001, EsDistanceKm = 200,
        ElevAngleHeaderDeg = 10.0, MinDurationSecHeader = minDur,
    };
    int Voluntary(OperatingParamsSet d)
    {
        var sc = new Scheduler(conP2, geo1, d, new ScenePointing(vmP2), simP);
        int v = 0;
        for (int k = 0; k <= 40; k++) v += sc.Step(k * 60.0).VoluntaryHandovers;
        return v;
    }
    int volFree = Voluntary(DeclP2(null));
    int volHeld = Voluntary(DeclP2(100000));
    Check("P2 min_duration: absent switches voluntarily, huge dwell never does",
        volFree > 0 && volHeld == 0, $"voluntary: absent={volFree} held={volHeld}");

    // P3: occurring is a per-step subset of reachable -- gated weights only
    // ever shrink, and the epfd statistics can only fall.
    var declaredP3 = DeclP2(null);
    var occPoint = new ScheduledPointing(conP, geoP, declaredP3, vmP2, simP);
    var reachPoint = new ScenePointing(vmP2);

    var st1 = conP.StateAt(2, 3600.0, simP);
    var occSet = occPoint.Resolve(st1);
    var reachSet = reachPoint.Resolve(st1);
    bool subset = occSet.Beams.Count == reachSet.Beams.Count;
    int gatedOff = 0;
    for (int i = 0; i < occSet.Beams.Count && subset; i++)
    {
        double wo = occSet.Beams[i].Weight, wr = reachSet.Beams[i].Weight;
        if (wo > wr + 1e-12) subset = false;
        if (wo < wr - 1e-12) gatedOff++;
    }

    var antP = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 12000.0, 0.6);
    var victimP = new EpfdDownVictim { EsLatDeg = 0, EsLonDeg = 0, GsoLonDeg = 0, Antenna = antP };
    var limitsP = new List<radlimits.LimitPoint>
    {
        new radlimits.LimitPoint { EPFD = -300.0, Perc = 0.001 },
        new radlimits.LimitPoint { EPFD = 0.0, Perc = 100.0 },
    };
    var occRes = EpfdDown.Run(conP, new ScheduledPointing(conP, geoP, declaredP3, vmP2, simP),
        victimP, 60.0, 50, limitsP, simP);
    var reachRes = EpfdDown.Run(conP, new ScenePointing(vmP2), victimP, 60.0, 50, limitsP, simP);
    bool okP3 = subset && gatedOff > 0
             && occRes.MaxEpfdDb <= reachRes.MaxEpfdDb + 1e-9
             && occRes.Accumulator.TotalSamples == reachRes.Accumulator.TotalSamples;
    Check("P3 occurring subset of reachable; epfd(occurring) <= epfd(reachable)", okP3,
        $"gatedOff={gatedOff} occMax={occRes.MaxEpfdDb:F2} reachMax={reachRes.MaxEpfdDb:F2}");

    // P4: coverage -- with only the elevation bound declared, every cell that
    // any satellite sees clearly above the threshold is served.
    var schedP4 = new Scheduler(conP, geoP, DeclP2(null), new ScenePointing(vmP2), simP);
    var stP4 = schedP4.Step(0.0);
    var servedCells = stP4.Links.Select(l => l.CellId).ToHashSet();
    bool okP4 = true; string detP4 = ""; int mustServe = 0;
    foreach (var cell in geoP.Cells)
    {
        var es = GeodeticToEcef(cell.LatDeg, cell.LonDeg, 0);
        double bestElev = double.NegativeInfinity;
        for (int i = 0; i < conP.SatelliteCount; i++)
        {
            double e = ElevationAngleDeg(conP.StateAt(i, 0.0, simP).PositionEcefKm, es);
            if (e > bestElev) bestElev = e;
        }
        if (bestElev >= 12.0)
        {
            mustServe++;
            if (!servedCells.Contains(cell.CellId))
            { okP4 = false; detP4 = $"cell {cell.CellId} ({cell.LatDeg:F1},{cell.LonDeg:F1}) best elev {bestElev:F1} unserved"; break; }
        }
    }
    Check("P4 every clearly-visible cell is served when only elevation binds", okP4 && mustServe > 5,
        okP4 ? $"mustServe={mustServe} served={servedCells.Count} of {geoP.Cells.Count}" : detP4);
}

// ---- Q: dataset gap 1 -- station-kept, precessing and elliptical shells ----
{
    double simQ = 86400.0;

    // Q1: orbit case 2 -- the W_delta box sweep. At t=0 the LAN sits at the
    // west edge (-W_delta), at t=T_sim at the east edge (+W_delta), relative
    // to a free-drift twin with artificial precession off.
    var kept = new Constellation(new[] { new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
        StationKeeping = true, WDeltaDeg = 0.5,
    } });
    var freeTwin = new Constellation(new[] { new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
    } });
    bool okQ1 = kept.Elements[0].OrbitCase == 2 && freeTwin.Elements[0].OrbitCase == 1;
    string detQ1 = $"cases={kept.Elements[0].OrbitCase},{freeTwin.Elements[0].OrbitCase}";
    if (okQ1)
    {
        // Case 1 vs case 2 also differ by the J2-vs-unperturbed... no: both
        // cases 1 and 2 use the J2-corrected mean motion; with artPrec=0 the
        // only difference is the W_delta term. Compare sub-longitudes.
        double d0 = AngleDiffQ(kept.StateAt(0, 0.0, simQ).SubSatLonDeg,
                               freeTwin.StateAt(0, 0.0, simQ).SubSatLonDeg);
        double d1 = AngleDiffQ(kept.StateAt(0, simQ, simQ).SubSatLonDeg,
                               freeTwin.StateAt(0, simQ, simQ).SubSatLonDeg);
        okQ1 = Math.Abs(d0 - -0.5) < 1e-6 && Math.Abs(d1 - 0.5) < 1e-6;
        detQ1 = $"t=0: {d0:F6} (want -0.5)  t=Tsim: {d1:F6} (want +0.5)";
    }
    Check("Q1 case 2 station keeping sweeps the W_delta box", okQ1, detQ1);

    static double AngleDiffQ(double a, double b)
    {
        double d = (a - b) % 360.0;
        if (d > 180) d -= 360; else if (d < -180) d += 360;
        return d;
    }

    // Q2: case 3 -- supplied precession drives the LAN drift; compare two
    // case-3 twins whose declared rates differ by a known amount.
    double ratePlus = 1e-4;   // deg/s
    Constellation Case3(double rate) => new Constellation(new[] { new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
        StationKeeping = true, WDeltaDeg = 0.0, PrecessionSupplied = true,
        PrecessionRateDegPerSec = rate,
    } });
    var c3a = Case3(0.0);
    var c3b = Case3(ratePlus);
    double t3 = 5000.0;
    double dLon = AngleDiffQ(c3b.StateAt(0, t3, simQ).SubSatLonDeg,
                             c3a.StateAt(0, t3, simQ).SubSatLonDeg);
    bool okQ2 = c3a.Elements[0].OrbitCase == 3
             && Math.Abs(dLon - ratePlus * t3) < 1e-6;
    Check("Q2 case 3 supplied precession shifts LAN by rate*t", okQ2,
        $"dLon={dLon:F6} want={ratePlus * t3:F6}");

    // Q3: elliptical shell -- radius range spans a(1-e)..a(1+e), the phase
    // convention round-trips through the examination's phase - omega
    // transform, and op_ht defaults to the perigee altitude.
    var ell = new ConstellationShell
    {
        AltitudeKm = 8062.0, InclinationDeg = 63.4, PlaneCount = 1, SatsPerPlane = 4,
        Eccentricity = 0.25, ArgumentOfPerigeeDeg = 270.0,
    };
    var conE = new Constellation(new[] { ell });
    double aE = Radians.Orbits.Core.Utilities.OrbitalConstants.EarthRadiusKm + ell.AltitudeKm;
    double rMin = double.MaxValue, rMax = double.MinValue;
    for (double t = 0; t < 20000; t += 100)
    {
        double r = conE.StateAt(0, t, simQ).RadiusKm;
        rMin = Math.Min(rMin, r); rMax = Math.Max(rMax, r);
    }
    var elE = conE.Elements[1];   // second satellite: phase 90
    double phaseBack = ((elE.TrueAnomalyDeg + elE.ArgumentOfPerigeeDeg) % 360.0 + 360.0) % 360.0;
    bool okQ3 = Math.Abs(rMin - aE * (1 - ell.Eccentricity)) < 1.0
             && Math.Abs(rMax - aE * (1 + ell.Eccentricity)) < 1.0
             && Math.Abs(phaseBack - 90.0) < 1e-9
             && Math.Abs(elE.OperatingHeightKm - (aE * (1 - ell.Eccentricity) - Radians.Orbits.Core.Utilities.OrbitalConstants.EarthRadiusKm)) < 1e-9;
    Check("Q3 elliptical shell: radius span, phase-omega round-trip, op_ht default", okQ3,
        $"r=[{rMin:F1},{rMax:F1}] want=[{aE * (1 - ell.Eccentricity):F1},{aE * (1 + ell.Eccentricity):F1}] phase={phaseBack:F3}");

    // Q4: the three-shell A/B/C notice (dataset brief Sec. 4) writes into the
    // cloned donor SRS with per-plane orbit models intact.
    string donorQ = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 SRS.MDB";
    if (File.Exists(donorQ))
    {
        var shellA = new ConstellationShell
        {
            AltitudeKm = 1000.0, InclinationDeg = 53.0, PlaneCount = 2, SatsPerPlane = 4,
            StationKeeping = true, WDeltaDeg = 0.1, RepeatPeriod = (0, 23, 56, 4),
        };
        var shellB = new ConstellationShell
        {
            AltitudeKm = 1200.0, InclinationDeg = 90.0, PlaneCount = 2, SatsPerPlane = 4, NOrbits = 288,
        };
        var shellC = ell;
        var noticeQ = new SrsNotice { NtcId = 900123457, SatName = "BEAMLAB2", Adm = "LUX" };
        noticeQ.AddShell(shellA);
        noticeQ.AddShell(shellB);
        noticeQ.AddShell(shellC);
        noticeQ.MaskInfo.Add(new SrsMaskInfo(1, 19700, 20200, 'P', 'A'));
        var scQ = new SrsScenario { ScenId = 1, ScenName = "coverage" };
        scQ.Frequencies.Add(new SrsFreqRange(1, 'E', 19700, 20200));
        scQ.PfdMaskLinks.Add(new SrsMaskLink(1, MaskId: 1));
        noticeQ.Scenarios.Add(scQ);

        string outQ = Path.Combine(AppContext.BaseDirectory, "exp", "BEAMLAB2 SRS.MDB");
        Directory.CreateDirectory(Path.GetDirectoryName(outQ));
        SrsMdbWriter.WriteSrs(donorQ, outQ, noticeQ);

        using var connQ = new System.Data.OleDb.OleDbConnection(
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={outQ}");
        connQ.Open();
        object Sc(string sql)
        {
            using var cmd = new System.Data.OleDb.OleDbCommand(sql, connQ);
            return cmd.ExecuteScalar();
        }
        string fskA = (string)Sc("SELECT f_stn_keep FROM orbit WHERE ntc_id=900123457 AND orb_id=1");
        string fskB = (string)Sc("SELECT f_stn_keep FROM orbit WHERE ntc_id=900123457 AND orb_id=3");
        double keepA = Convert.ToDouble(Sc("SELECT keep_rnge FROM orbit WHERE ntc_id=900123457 AND orb_id=1"));
        int rptHh = Convert.ToInt32(Sc("SELECT rpt_prd_hh FROM orbit WHERE ntc_id=900123457 AND orb_id=1"));
        double apoC = Convert.ToDouble(Sc("SELECT apog_km FROM orbit WHERE ntc_id=900123457 AND orb_id=5"));
        double perC = Convert.ToDouble(Sc("SELECT perig_km FROM orbit WHERE ntc_id=900123457 AND orb_id=5"));
        double pargC = Convert.ToDouble(Sc("SELECT perig_arg FROM orbit WHERE ntc_id=900123457 AND orb_id=5"));
        double ophtC = Convert.ToDouble(Sc("SELECT op_ht_km FROM orbit WHERE ntc_id=900123457 AND orb_id=5"));
        int orbits3 = Convert.ToInt32(Sc("SELECT COUNT(*) FROM orbit WHERE ntc_id=900123457"));

        double aC = Radians.Orbits.Core.Utilities.OrbitalConstants.EarthRadiusKm + shellC.AltitudeKm;
        bool okQ4 = fskA == "Y" && fskB == "N" && Math.Abs(keepA - 0.1) < 1e-6 && rptHh == 23   // keep_rnge is a float column
                 && Math.Abs(apoC - (aC * 1.25 - Radians.Orbits.Core.Utilities.OrbitalConstants.EarthRadiusKm)) < 1e-6
                 && Math.Abs(perC - (aC * 0.75 - Radians.Orbits.Core.Utilities.OrbitalConstants.EarthRadiusKm)) < 1e-6
                 && Math.Abs(pargC - 270.0) < 1e-9 && Math.Abs(ophtC - perC) < 1e-6
                 && orbits3 == 5;
        Check("Q4 A/B/C mixed-model notice round-trips through the donor SRS", okQ4,
            $"fsk={fskA}/{fskB} keep={keepA} rpt_hh={rptHh} apo/per/parg/opht={apoC:F1}/{perC:F1}/{pargC:F0}/{ophtC:F1} orbits={orbits3}");
    }
    else
    {
        Check("Q4 mixed-model notice", true, "donor SRS not present, skipped");
    }
}

// ---- R: dataset gap 2 -- the 4-D A-format ES eirp mask ----
{
    string outDirR = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(outDirR);

    EirpMask4D Make4D()
    {
        var m = new EirpMask4D
        {
            SatName = "BEAMLAB1", NtcId = 900123456, MaskId = 10,
            LowFreqMhz = 27500, HighFreqMhz = 30000, RefBwKHz = 40, MinElevDeg = 10, EsId = -1,
        };
        foreach (double lat in new[] { -30.0, 0.0, 30.0 })
        {
            var blk = new Eirp4DLatBlock { LatDeg = lat };
            foreach (double az in new[] { 0.0, 90.0, 180.0, 270.0 })
                foreach (double el in new[] { 10.0, 45.0, 90.0 })
                {
                    var pt = new Eirp4DPointing { AzDeg = az, ElDeg = el };
                    foreach (var (dl, e) in new[] { (0.0, 30.0), (1.0, 20.0), (5.0, 2.5), (20.0, -12.5), (180.0, -18.9) })
                        pt.ByDeltaLong.Add((dl, e - 0.05 * Math.Abs(lat) + 0.01 * el));
                    blk.Pointings.Add(pt);
                }
            m.Blocks.Add(blk);
        }
        return m;
    }

    // R1: structure per the Rec C4.3 format-"A" example.
    var m4 = Make4D();
    string p4 = Path.Combine(outDirR, "beamlab_es4d_mask10.xml");
    var w4 = EirpMaskXmlWriter.WriteEs4D(p4, m4);

    var d4 = new XmlDocument();
    d4.Load(p4);
    var head4 = (XmlElement)d4.SelectSingleNode("//eirp_mask_es")!;
    bool okR1 = head4.GetAttribute("format") == "A"
             && head4.GetAttribute("a_name") == "latitude"
             && head4.GetAttribute("c_name") == "azimuth angle"
             && head4.GetAttribute("d_name") == "elevation angle"
             && head4.GetAttribute("e_name") == "DeltaLongES"
             && head4.GetAttribute("ES_ID") == "-1"
             && d4.SelectNodes("//by_a")!.Count == 3
             && d4.SelectNodes("//by_a/by_c")!.Count == 12
             && d4.SelectNodes("//by_a/by_c/by_d")!.Count == 36
             && d4.SelectNodes("//by_a/by_c/by_d/eirp")!.Count == 180
             && d4.SelectSingleNode("//by_a[@a='0']/by_c[@c='90.0']/by_d[@d='45.0']/eirp[@e='5.0']")!.InnerText == "2.95"
             && w4.Count == 0;
    Check("R1 4-D A-format structure per Rec C4.3", okR1,
        $"by_a={d4.SelectNodes("//by_a")!.Count} by_c={d4.SelectNodes("//by_a/by_c")!.Count} by_d={d4.SelectNodes("//by_a/by_c/by_d")!.Count} eirp={d4.SelectNodes("//by_a/by_c/by_d/eirp")!.Count} warn={w4.Count}");

    // R2: should-rule warnings along DeltaLongES; empty pointing rejected.
    var bad4 = Make4D();
    bad4.Blocks[0].Pointings[0].ByDeltaLong.Add((2.0, 35.0));   // rises after 1.0 -> warning
    string pBad = Path.Combine(outDirR, "beamlab_es4d_warn.xml");
    var wBad = EirpMaskXmlWriter.WriteEs4D(pBad, bad4);
    bool threwR2 = false;
    try
    {
        var empty4 = new EirpMask4D { SatName = "T", NtcId = 1, LowFreqMhz = 1, HighFreqMhz = 2 };
        empty4.Blocks.Add(new Eirp4DLatBlock { LatDeg = 0 });
        empty4.Blocks[0].Pointings.Add(new Eirp4DPointing { AzDeg = 0, ElDeg = 0 });
        EirpMaskXmlWriter.WriteEs4D(Path.Combine(outDirR, "x.xml"), empty4);
    }
    catch (ArgumentException) { threwR2 = true; }
    Check("R2 4-D warnings reported, empty pointing rejected", wBad.Count == 1 && threwR2,
        $"warnings={wBad.Count} threw={threwR2}");

    // R3: container interop -- the native store does not know format "A", so
    // the writer falls back to the custom container, and the BR native
    // extractor must still read it back identically.
    string donorR = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 Masks.MDB";
    string[] dllDirsR =
    {
        @"C:\Projects\_EPFD\radians\radians\dlls",
        @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
    };
    string dllDirR = dllDirsR.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdMasksApi64.dll")));
    if (File.Exists(donorR) && dllDirR is not null)
    {
        SrsMdbWriter.EpfdMasksDllDirectory = dllDirR;
        string outMasksR = Path.Combine(outDirR, "BEAMLAB1 Es4dMasks.MDB");
        var storedR = SrsMdbWriter.WriteMasks(donorR, outMasksR, 900123456, "BEAMLAB1", new[]
        {
            new SrsMdbWriter.MaskContent(10, p4, 'E', 27500, 30000),
        });
        string exR4 = Path.Combine(outDirR, "extract_es4d.xml");
        int rx = SrsMdbWriter.ExtractMask(outMasksR, 900123456, 10, exR4);
        var da5 = new XmlDocument(); da5.Load(p4);
        var db5 = new XmlDocument(); db5.Load(exR4);
        bool okR3 = storedR[0].Status == 0 && rx == 0 && da5.OuterXml == db5.OuterXml;
        Check("R3 4-D mask stored (custom fallback) and BR-extracted identical", okR3,
            $"store={storedR[0].Status} extract={rx}");
    }
    else
    {
        Check("R3 4-D mask container interop", true, "donor Masks.MDB or DLL not present, skipped");
    }
}

// ---- S: dataset gap 3 -- specific earth stations (e_as_stn) ----
{
    SrsNotice MakeEsNotice()
    {
        var n = new SrsNotice { NtcId = 900123458, SatName = "BEAMLAB3", Adm = "LUX" };
        n.AddShell(new ConstellationShell
        {
            AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 2, SatsPerPlane = 4, NOrbits = 288,
        });
        n.MaskInfo.Add(new SrsMaskInfo(6, 27500, 30000, 'E', 'O'));
        n.EarthStations.Add(new SrsEarthStation
        {
            EAsId = 5001, StnName = "GW-NORTH", StnType = 'S',
            LonDeg = 12.5, LatDeg = 48.2, GainDbi = 53.4, AntDiamM = 2.4, NoiseT = 150,
        });
        n.EarthStations.Add(new SrsEarthStation
        {
            EAsId = 5002, StnName = "GW-SOUTH", StnType = 'S',
            LonDeg = -3.7, LatDeg = -33.9, GainDbi = 53.4, AntDiamM = 2.4, NoiseT = 150,
        });
        n.EarthStations.Add(new SrsEarthStation
        {
            EAsId = 5003, StnName = "TYP-KA", StnType = 'T',
            GainDbi = 40.4, AntDiamM = 0.45, BeamwidthDeg = 2.31, PatternId = 33,
        });
        var sc = new SrsScenario { ScenId = 1, ScenName = "Uplink specific gateways" };
        sc.Frequencies.Add(new SrsFreqRange(1, 'R', 27500, 30000));
        sc.EsMaskLinks.Add(new SrsMaskLink(1, MaskId: 6, EAsId: 5001));
        sc.EsMaskLinks.Add(new SrsMaskLink(2, MaskId: 6, EAsId: 5002));
        n.Scenarios.Add(sc);
        return n;
    }

    // S1: validation -- consistent notice passes; dangling e_as_id and a
    // specific station without coordinates are rejected.
    var okNotice = MakeEsNotice();
    bool okS1 = true; string detS1 = "";
    try { okNotice.Validate(); } catch (Exception ex) { okS1 = false; detS1 = ex.Message; }

    bool threwDangling = false, threwNoCoords = false;
    var dangling = MakeEsNotice();
    dangling.Scenarios[0].EsMaskLinks.Add(new SrsMaskLink(3, MaskId: 6, EAsId: 9999));
    try { dangling.Validate(); } catch (InvalidOperationException) { threwDangling = true; }
    var noCoords = MakeEsNotice();
    noCoords.EarthStations.Add(new SrsEarthStation { EAsId = 5004, StnName = "BAD", StnType = 'S' });
    try { noCoords.Validate(); } catch (InvalidOperationException) { threwNoCoords = true; }
    Check("S1 e_as_stn validation: consistent passes, dangling/uncoordinated rejected",
        okS1 && threwDangling && threwNoCoords,
        okS1 ? $"dangling={threwDangling} noCoords={threwNoCoords}" : detS1);

    // S2: round-trip through the cloned donor SRS -- stations, coordinates
    // and the named mask_lnk2 rows, with the donor's 114 typical rows gone.
    string donorS = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 SRS.MDB";
    if (File.Exists(donorS))
    {
        try
        {
        string outS = Path.Combine(AppContext.BaseDirectory, "exp", "BEAMLAB3 SRS.MDB");
        Directory.CreateDirectory(Path.GetDirectoryName(outS));
        SrsMdbWriter.WriteSrs(donorS, outS, okNotice);

        using var connS = new System.Data.OleDb.OleDbConnection(
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={outS}");
        connS.Open();
        object Sc2(string sql)
        {
            using var cmd = new System.Data.OleDb.OleDbCommand(sql, connS);
            return cmd.ExecuteScalar();
        }
        int esCount = Convert.ToInt32(Sc2("SELECT COUNT(*) FROM e_as_stn"));
        string t5001 = (string)Sc2("SELECT stn_type FROM e_as_stn WHERE e_as_id=5001");
        double lon5001 = Convert.ToDouble(Sc2("SELECT long_dec FROM e_as_stn WHERE e_as_id=5001"));
        double lat5002 = Convert.ToDouble(Sc2("SELECT lat_dec FROM e_as_stn WHERE e_as_id=5002"));
        string t5003 = (string)Sc2("SELECT stn_type FROM e_as_stn WHERE e_as_id=5003");
        object coord5003 = Sc2("SELECT long_dec FROM e_as_stn WHERE e_as_id=5003");
        int lnk2 = Convert.ToInt32(Sc2("SELECT COUNT(*) FROM mask_lnk2 WHERE ntc_id=900123458 AND e_as_id IN (5001,5002)"));

        bool okS2 = esCount == 3 && t5001 == "S" && Math.Abs(lon5001 - 12.5) < 1e-4
                 && Math.Abs(lat5002 - -33.9) < 1e-4 && t5003 == "T"   // long/lat_dec are float columns
                 && coord5003 is DBNull && lnk2 == 2;
        Check("S2 specific earth stations round-trip through the donor SRS", okS2,
            $"rows={esCount} t5001={t5001} lon={lon5001} lat={lat5002} typ={t5003} coordNull={coord5003 is DBNull} lnk2={lnk2}");
        }
        catch (Exception ex)
        {
            Check("S2 specific earth stations", false, "exception: " + ex.Message);
        }
    }
    else
    {
        Check("S2 specific earth stations", true, "donor SRS not present, skipped");
    }
}

// ---- T: dataset gap 5 -- the BL-* case generator ----
{
    // T1: notice content invariants (no database needed): the three shells
    // project into the declared orbit rows -- Case 2 keep_rnge/repeat on
    // shell A, Case 3 negative declared precession and the elliptical
    // geometry on shell C, and the family's scenario/mask/e_as structure.
    try
    {
        var nAll = radians.beamlab.dataset.DatasetGenerator.BuildNotice("BL-ALL");
        var orbA = nAll.Orbits.First(r => r.OrbId == 1);
        var orbC = nAll.Orbits.First(r => r.OrbId == 11);
        bool okT1 = nAll.Orbits.Count == 12 && nAll.Phases.Count == 76
                 && nAll.Scenarios.Count == 2 && nAll.EarthStations.Count == 3
                 && nAll.MaskInfo.Count == 14
                 && Math.Abs(orbC.ApogeeKm - 4000.0) < 0.5 && Math.Abs(orbC.PerigeeKm - 800.0) < 0.5
                 && Math.Abs(orbC.OpHtKm - 1000.0) < 1e-9 && orbC.PrecessionSupplied
                 && orbC.PrecessionRateDegPerSec < 0
                 && orbA.StationKeeping && orbA.KeepRangeDeg == 0.5 && orbA.RepeatPeriod.HasValue;
        Check("T1 dataset notice: shells project into declared orbit rows", okT1,
            $"orbits={nAll.Orbits.Count} phases={nAll.Phases.Count} scen={nAll.Scenarios.Count} " +
            $"es={nAll.EarthStations.Count} mi={nAll.MaskInfo.Count} apog={orbC.ApogeeKm:F1} " +
            $"perig={orbC.PerigeeKm:F1} prec={orbC.PrecessionRateDegPerSec:E2}");
    }
    catch (Exception ex) { Check("T1 dataset notice", false, "exception: " + ex.Message); }

    string donorSrsT = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 SRS.MDB";
    string donorMasksT = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 Masks.MDB";
    string[] dllDirsT =
    {
        @"C:\Projects\_EPFD\radians\radians\dlls",
        @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
    };
    string dllDirT = dllDirsT.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdMasksApi64.dll")));
    if (File.Exists(donorSrsT) && File.Exists(donorMasksT) && dllDirT is not null)
    {
        // Crash-proof: the BR native DLL is involved; an escaping exception
        // would leave a wedged process holding it. Fail the checks instead.
        try
        {
        string outDs = Path.Combine(AppContext.BaseDirectory, "exp", "ds");
        if (Directory.Exists(outDs)) Directory.Delete(outDs, recursive: true);
        radians.beamlab.dataset.DatasetGenerator.Generate(new radians.beamlab.dataset.DatasetOptions
        {
            DonorSrsPath = donorSrsT, DonorMasksPath = donorMasksT,
            EpfdMasksDllDir = dllDirT, OutDir = outDs, Quick = true,
        });

        // T2: every case emitted; the BL-ALL SRS carries the full structure.
        bool filesOk = radians.beamlab.dataset.DatasetGenerator.CaseNames.All(c =>
        {
            int ntcC = radians.beamlab.dataset.DatasetGenerator.NtcIdFor(c);
            string d = Path.Combine(outDs, c);
            return File.Exists(Path.Combine(d, $"{ntcC} SRS.MDB"))
                && File.Exists(Path.Combine(d, $"{ntcC} Masks.MDB"))
                && File.Exists(Path.Combine(d, "README.md"));
        });
        string srsAll = Path.Combine(outDs, "BL-ALL", "900123476 SRS.MDB");
        using (var connT = new System.Data.OleDb.OleDbConnection(
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={srsAll}"))
        {
            connT.Open();
            int CountT(string sql)
            {
                using var cmd = new System.Data.OleDb.OleDbCommand(sql, connT);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            int orbitsT = CountT("SELECT COUNT(*) FROM orbit WHERE ntc_id=900123476");
            int phasesT = CountT("SELECT COUNT(*) FROM phase WHERE ntc_id=900123476");
            int scensT = CountT("SELECT COUNT(*) FROM epfd_param WHERE ntc_id=900123476");
            int freqsT = CountT("SELECT COUNT(*) FROM epfd_freq WHERE ntc_id=900123476");
            // e_as_stn is grp-keyed (no ntc_id column); the writer clears it wholesale.
            int esT = CountT("SELECT COUNT(*) FROM e_as_stn");
            int miT = CountT("SELECT COUNT(*) FROM mask_info WHERE ntc_id=900123476");
            int l1T = CountT("SELECT COUNT(*) FROM mask_lnk1 WHERE ntc_id=900123476");
            int l2T = CountT("SELECT COUNT(*) FROM mask_lnk2 WHERE ntc_id=900123476");
            int l3T = CountT("SELECT COUNT(*) FROM mask_lnk3 WHERE ntc_id=900123476");
            bool okT2 = filesOk && orbitsT == 12 && phasesT == 76 && scensT == 2 && freqsT == 4
                     && esT == 3 && miT == 14 && l1T == 15 && l2T == 4 && l3T == 4;
            Check("T2 all six cases emitted; BL-ALL SRS structure", okT2,
                $"files={filesOk} orbit={orbitsT} phase={phasesT} scen={scensT} freq={freqsT} " +
                $"es={esT} mi={miT} lnk1={l1T} lnk2={l2T} lnk3={l3T}");
        }

        // T3: the BL-ALL Masks database round-trips through the BR native
        // extractor -- mask 1 (native-stored P) and mask 8 (container-stored
        // 4-D format "A") both come back identical to the xml/ sources.
        string masksAll = Path.Combine(outDs, "BL-ALL", "900123476 Masks.MDB");
        int rowsT;
        using (var connM = new System.Data.OleDb.OleDbConnection(
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={masksAll}"))
        {
            connM.Open();
            using var cmd = new System.Data.OleDb.OleDbCommand("SELECT COUNT(*) FROM masks", connM);
            rowsT = Convert.ToInt32(cmd.ExecuteScalar());
        }
        string exT1 = Path.Combine(outDs, "extract_t_mask1.xml");
        string exT8 = Path.Combine(outDs, "extract_t_mask8.xml");
        int rT1 = SrsMdbWriter.ExtractMask(masksAll, 900123476, 1, exT1);
        int rT8 = SrsMdbWriter.ExtractMask(masksAll, 900123476, 8, exT8);
        bool SameXmlT(string a, string b)
        {
            var daT = new XmlDocument(); daT.Load(a);
            var dbT = new XmlDocument(); dbT.Load(b);
            return daT.OuterXml == dbT.OuterXml;
        }
        bool okT3 = rowsT == 14 && rT1 == 0 && rT8 == 0
                 && SameXmlT(exT1, Path.Combine(outDs, "BL-ALL", "xml", "mask1_pfd_alpha.xml"))
                 && SameXmlT(exT8, Path.Combine(outDs, "BL-ALL", "xml", "mask8_es_eirp_4d_gw5001.xml"));
        Check("T3 BL-ALL masks: 14 rows, native + container extract identical", okT3,
            $"rows={rowsT} extract={rT1},{rT8}");

        // T4: expectation CDFs exist for the downlink cases, parse, and are
        // monotone non-increasing in percent-exceeded.
        bool okT4 = true; string detT4 = "";
        var expT4 = new (string Case, string File)[]
        {
            ("BL-D1", "epfd_down_cdf.csv"), ("BL-D2", "epfd_down_cdf.csv"),
            ("BL-U1", "epfd_up_cdf.csv"), ("BL-U2", "epfd_up_cdf.csv"),
            ("BL-I1", "epfd_down_cdf.csv"), ("BL-I1", "epfd_is_cdf.csv"),
            ("BL-ALL", "epfd_down_cdf.csv"), ("BL-ALL", "epfd_up_cdf.csv"),
            ("BL-ALL", "epfd_is_cdf.csv"),
        };
        foreach (var (c, f) in expT4)
        {
            string csv = Path.Combine(outDs, c, "expected", f);
            if (!File.Exists(csv)) { okT4 = false; detT4 = c + "/" + f + " missing"; break; }
            var rows = File.ReadAllLines(csv)
                .Where(l => l.Length > 0 && l[0] != '#' && char.IsDigit(l[0]) || l.StartsWith("-"))
                .Select(l => l.Split(','))
                .Where(pr => pr.Length == 2 && double.TryParse(pr[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                .Select(pr => (E: double.Parse(pr[0], CultureInfo.InvariantCulture),
                               P: double.Parse(pr[1], CultureInfo.InvariantCulture)))
                .ToList();
            if (rows.Count < 3) { okT4 = false; detT4 = c + "/" + f + " too few rows"; break; }
            for (int i = 1; i < rows.Count && okT4; i++)
                if (rows[i].P > rows[i - 1].P + 1e-12) { okT4 = false; detT4 = c + "/" + f + " pct not monotone"; }
            if (okT4 && (rows[0].P > 100.0 || rows[^1].P < 0.0)) { okT4 = false; detT4 = c + "/" + f + " pct range"; }
            if (okT4) detT4 += $"{c}:{rows.Count} ";
        }
        Check("T4 expectation CDFs present for every direction, parse, monotone", okT4, detT4.Trim());
        }
        catch (Exception ex)
        {
            Check("T2-T4 dataset generation", false, "exception: " + ex.Message);
        }
    }
    else
    {
        Check("T2-T4 dataset generation", true, "donor MDBs or EpfdMasksApi64.dll not present, skipped");
    }
}

// ---- U: epfd(up) and the epfd(is) byproduct ----
{
    // One satellite over a known cell: every quantity is hand-computable.
    var shellU = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
    };
    var conU = new Constellation(new[] { shellU });
    double simDurU = 600.0;
    var st0 = conU.StateAt(0, 0.0, simDurU);
    double cLat = st0.SubSatLatDeg, cLon = st0.SubSatLonDeg;

    var vicUp = new EpfdGsoSatVictim
    {
        GsoLonDeg = cLon + 15.0, BoresightLatDeg = cLat, BoresightLonDeg = cLon,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APSREC408V01, 28000.0, null),
        GmaxDbi = 40.7, Phi3DbDeg = 1.55,
    };
    var esModelU = new EpfdUpEsModel
    {
        PowerDbw = 12.0,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 28000.0, 0.65),
    };
    var limitsU = new List<radlimits.LimitPoint>
    {
        new radlimits.LimitPoint { EPFD = -300.0, Perc = 0.001 },
        new radlimits.LimitPoint { EPFD = 0.0, Perc = 100.0 },
    };
    OperatingParamsSet DeclU(int? capSat, double? minAngleSat) => new()
    {
        SatName = "T", NtcId = 1, ParamId = 1, LowFreqMhz = 27500, HighFreqMhz = 28600,
        MaxCoFreqSat = capSat, MinAngleAtSatDeg = minAngleSat,
    };

    double gsoLonRadU = vicUp.GsoLonDeg * Math.PI / 180.0;
    var gsoU = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(gsoLonRadU),
                        GsoGeometry.GsoRadiusKm * Math.Sin(gsoLonRadU), 0.0);
    double LinkLinear(double latDeg, double lonDeg)
    {
        var esP = GeodeticToEcef(latDeg, lonDeg, 0.0);
        var satP = st0.PositionEcefKm;
        double phi = Math.Acos(Math.Clamp(Vec3.Dot((satP - esP).Normalized(),
            (gsoU - esP).Normalized()), -1.0, 1.0)) * 180.0 / Math.PI;
        double eirp = 12.0 + esModelU.Antenna.GetAntGain(phi, 0.0);
        double dM = (gsoU - esP).Length * 1000.0;
        var bsDir = (GeodeticToEcef(cLat, cLon, 0.0) - gsoU).Normalized();
        double psi = Math.Acos(Math.Clamp(Vec3.Dot(bsDir, (esP - gsoU).Normalized()),
            -1.0, 1.0)) * 180.0 / Math.PI;
        return Math.Pow(10.0, (eirp - 10.0 * Math.Log10(4.0 * Math.PI * dM * dM)
            + vicUp.RelativeGainDb(psi)) / 10.0);
    }

    // U1: single link, analytic identity (boresight at the cell: Grel = 0).
    // Pitch 500: the default hex layout has no central beam -- the nearest
    // boresights sit 433 km from the sub-satellite point.
    var geo1 = new ServiceGeography(new List<ServiceCell> { new(1, cLat, cLon) }, 500.0);
    var vmU = new PfdMaskViewModel();
    var res1 = EpfdUp.Run(conU, new Scheduler(conU, geo1, DeclU(null, null),
        new ScenePointing(vmU), simDurU), geo1, vicUp, esModelU,
        1.0, 1, limitsU, simDurU);
    double exp1 = 10.0 * Math.Log10(LinkLinear(cLat, cLon));
    Check("U1 epfd(up) single link matches the hand formula",
        res1.QuietSteps == 0 && Math.Abs(res1.MaxEpfdDb - exp1) < 1e-9,
        $"run={res1.MaxEpfdDb:F6} hand={exp1:F6} quiet={res1.QuietSteps}");

    // U2/U3 geometry: a second cell 3 deg east (about 15.5 deg apart at the
    // satellite -- computed below rather than assumed).
    double lon2 = cLon + 3.0;
    var geo2 = new ServiceGeography(new List<ServiceCell> { new(1, cLat, cLon), new(2, cLat, lon2) }, 500.0);
    double expPair = 10.0 * Math.Log10(LinkLinear(cLat, cLon) + LinkLinear(cLat, lon2));
    var esA = GeodeticToEcef(cLat, cLon, 0.0);
    var esB = GeodeticToEcef(cLat, lon2, 0.0);
    double sepAtSat = Math.Acos(Math.Clamp(Vec3.Dot(
        (esA - st0.PositionEcefKm).Normalized(), (esB - st0.PositionEcefKm).Normalized()),
        -1.0, 1.0)) * 180.0 / Math.PI;

    // U2: no cap -> both links (pair identity); MAX_CO_FREQ_SAT = 1 -> the
    // higher-elevation (sub-satellite) link only.
    var resPair = EpfdUp.Run(conU, new Scheduler(conU, geo2, DeclU(null, null),
        new ScenePointing(vmU), simDurU), geo2, vicUp, esModelU,
        1.0, 1, limitsU, simDurU);
    var resCap = EpfdUp.Run(conU, new Scheduler(conU, geo2, DeclU(1, null),
        new ScenePointing(vmU), simDurU), geo2, vicUp, esModelU,
        1.0, 1, limitsU, simDurU);
    Check("U2 MAX_CO_FREQ_SAT gate: pair without cap, best link with cap 1",
        Math.Abs(resPair.MaxEpfdDb - expPair) < 1e-9 && Math.Abs(resCap.MaxEpfdDb - exp1) < 1e-9
        && resPair.MaxEpfdDb > resCap.MaxEpfdDb,
        $"pair={resPair.MaxEpfdDb:F6}/{expPair:F6} cap={resCap.MaxEpfdDb:F6}/{exp1:F6}");

    // U3: MIN_ANGLE_AT_SAT above the pair separation drops the weaker link;
    // below it keeps both.
    var resWide = EpfdUp.Run(conU, new Scheduler(conU, geo2, DeclU(null, sepAtSat + 5.0),
        new ScenePointing(vmU), simDurU), geo2, vicUp, esModelU,
        1.0, 1, limitsU, simDurU);
    var resNarrow = EpfdUp.Run(conU, new Scheduler(conU, geo2, DeclU(null, Math.Max(0.5, sepAtSat - 5.0)),
        new ScenePointing(vmU), simDurU), geo2, vicUp, esModelU,
        1.0, 1, limitsU, simDurU);
    Check("U3 MIN_ANGLE_AT_SAT gate around the actual pair separation",
        Math.Abs(resWide.MaxEpfdDb - exp1) < 1e-9 && Math.Abs(resNarrow.MaxEpfdDb - expPair) < 1e-9,
        $"sep={sepAtSat:F2} wide={resWide.MaxEpfdDb:F6}/{exp1:F6} narrow={resNarrow.MaxEpfdDb:F6}/{expPair:F6}");

    // U4: the epfd(is) byproduct -- identical down statistics with and
    // without the extra victim, the IS value matching the hand-composed
    // eirp toward the GSO, and Earth blockage silencing the far side.
    var antD = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 19700.0, 0.6);
    var vicDown = new EpfdDownVictim { EsLatDeg = cLat, EsLonDeg = cLon, GsoLonDeg = cLon + 15.0, Antenna = antD };
    var vicIs = new EpfdGsoSatVictim
    {
        GsoLonDeg = cLon + 20.0, BoresightLatDeg = cLat, BoresightLonDeg = cLon,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APSREC408V01, 19700.0, null),
        GmaxDbi = 40.7, Phi3DbDeg = 1.55,
    };
    var resDown0 = EpfdDown.Run(conU, new ScenePointing(vmU), vicDown, 1.0, 1, limitsU, simDurU);
    var resDown1 = EpfdDown.Run(conU, new ScenePointing(vmU), vicDown, 1.0, 1, limitsU, simDurU, vicIs);

    var snapU = conU.SnapshotAt(0.0, simDurU, new ScenePointing(vmU));
    var beamsU = snapU.Satellites[0].Beams;
    double lonIsRad = vicIs.GsoLonDeg * Math.PI / 180.0;
    var gsoIsU = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(lonIsRad),
                          GsoGeometry.GsoRadiusKm * Math.Sin(lonIsRad), 0.0);
    var satPosU = snapU.Satellites[0].State.PositionEcefKm;
    double eirpIsU = BeamComposer.CompositeEirpDbw(beamsU.Beams,
        (gsoIsU - satPosU).Normalized(), beamsU.PowersDbw);
    double dIsM = (gsoIsU - satPosU).Length * 1000.0;
    var bsDirIs = (GeodeticToEcef(cLat, cLon, 0.0) - gsoIsU).Normalized();
    double psiIs = Math.Acos(Math.Clamp(Vec3.Dot(bsDirIs, (satPosU - gsoIsU).Normalized()),
        -1.0, 1.0)) * 180.0 / Math.PI;
    double expIs = eirpIsU - 10.0 * Math.Log10(4.0 * Math.PI * dIsM * dIsM) + vicIs.RelativeGainDb(psiIs);

    var (cdf0, pct0) = resDown0.Accumulator.BuildCdf();
    var (cdf1, pct1) = resDown1.Accumulator.BuildCdf();
    bool downUnchanged = resDown0.MaxEpfdDb == resDown1.MaxEpfdDb
        && pct0.Length == pct1.Length && pct0.SequenceEqual(pct1);

    var vicIsFar = new EpfdGsoSatVictim
    {
        GsoLonDeg = cLon + 180.0, BoresightLatDeg = 0.0, BoresightLonDeg = cLon + 180.0,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APSREC408V01, 19700.0, null),
        GmaxDbi = 40.7, Phi3DbDeg = 1.55,
    };
    var resFar = EpfdDown.Run(conU, new ScenePointing(vmU), vicDown, 1.0, 1, limitsU, simDurU, vicIsFar);

    Check("U4 epfd(is) byproduct: down unchanged, IS matches hand value, far side blocked",
        downUnchanged && resDown1.IsAccumulator is not null
        && resDown1.IsQuietSteps == 0 && Math.Abs(resDown1.MaxEpfdIsDb - expIs) < 1e-9
        && resFar.IsQuietSteps == 1 && double.IsNegativeInfinity(resFar.MaxEpfdIsDb),
        $"is={resDown1.MaxEpfdIsDb:F6} hand={expIs:F6} downSame={downUnchanged} farQuiet={resFar.IsQuietSteps}");
}

// ---- U5-U7: WP2 assignment-time gates (reassign, not drop) ----
{
    // Two satellites 15 deg of longitude apart at t = 0.
    var shellV = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 2, SatsPerPlane = 1,
        LanSpreadDeg = 30.0,
    };
    var conV = new Constellation(new[] { shellV });
    double simDurV = 600.0;
    var stA = conV.StateAt(0, 0.0, simDurV);
    var stB = conV.StateAt(1, 0.0, simDurV);
    var vmV = new PfdMaskViewModel();

    OperatingParamsSet DeclV(int? capSat = null, double? minAngleEs = null,
        double latMin = -90.0, double latMax = 90.0) => new()
    {
        SatName = "T", NtcId = 1, ParamId = 1, LowFreqMhz = 27500, HighFreqMhz = 28600,
        MaxCoFreqSat = capSat, MinAngleAtEsDeg = minAngleEs,
        EsLatMinDeg = latMin, EsLatMaxDeg = latMax,
    };

    // U5: both cells prefer satellite A; MAX_CO_FREQ_SAT = 1 must push the
    // second cell onto satellite B (reassignment), not leave it unserved.
    var cellsV = new List<ServiceCell>
    {
        new(1, stA.SubSatLatDeg, stA.SubSatLonDeg),
        new(2, stA.SubSatLatDeg, stA.SubSatLonDeg + 5.0),
    };
    var geoV = new ServiceGeography(cellsV, 500.0);
    var freeStep = new Scheduler(conV, geoV, DeclV(), new ScenePointing(vmV), simDurV).Step(0.0);
    var capStep = new Scheduler(conV, geoV, DeclV(capSat: 1), new ScenePointing(vmV), simDurV).Step(0.0);
    bool bothPreferA = freeStep.Links.Count == 2
        && freeStep.Links.All(l => l.SatelliteNumber == stA.SatelliteNumber);
    var capBy = capStep.Links.ToDictionary(l => l.CellId, l => l.SatelliteNumber);
    bool okU5 = bothPreferA && capStep.Links.Count == 2 && capStep.UnservedCellLinks == 0
        && capBy[1] == stA.SatelliteNumber && capBy[2] == stB.SatelliteNumber;
    Check("U5 MAX_CO_FREQ_SAT reassigns the contested cell to the next satellite", okU5,
        $"free=[{string.Join(",", freeStep.Links.Select(l => l.CellId + ":" + l.SatelliteNumber))}] " +
        $"cap=[{string.Join(",", capStep.Links.Select(l => l.CellId + ":" + l.SatelliteNumber))}] " +
        $"unserved={capStep.UnservedCellLinks}");

    // U6: one cell midway with two demand links; MIN_ANGLE_AT_ES above the
    // actual satellite separation blocks the second satellite, below keeps it.
    double midLon = (stA.SubSatLonDeg + stB.SubSatLonDeg) / 2.0;
    var cellMid = new ServiceCell(1, stA.SubSatLatDeg, midLon) { DemandLinks = 2 };
    var geoMid = new ServiceGeography(new List<ServiceCell> { cellMid }, 500.0);
    var esMid = GeodeticToEcef(cellMid.LatDeg, cellMid.LonDeg, 0.0);
    double sepAtEs = Math.Acos(Math.Clamp(Vec3.Dot(
        (stA.PositionEcefKm - esMid).Normalized(), (stB.PositionEcefKm - esMid).Normalized()),
        -1.0, 1.0)) * 180.0 / Math.PI;
    var wideStep = new Scheduler(conV, geoMid, DeclV(minAngleEs: sepAtEs + 10.0),
        new ScenePointing(vmV), simDurV).Step(0.0);
    var narrowStep = new Scheduler(conV, geoMid, DeclV(minAngleEs: Math.Max(0.5, sepAtEs - 10.0)),
        new ScenePointing(vmV), simDurV).Step(0.0);
    bool okU6 = wideStep.Links.Count == 1 && wideStep.UnservedCellLinks == 1
        && narrowStep.Links.Count == 2
        && narrowStep.Links.Select(l => l.SatelliteNumber).Distinct().Count() == 2;
    Check("U6 MIN_ANGLE_AT_ES separates the satellites co-serving one cell", okU6,
        $"sep={sepAtEs:F2} wide={wideStep.Links.Count}+{wideStep.UnservedCellLinks}u narrow={narrowStep.Links.Count}");

    // U7: a cell outside the declared ES latitude range is not served.
    var geoOne = new ServiceGeography(new List<ServiceCell> { new(1, stA.SubSatLatDeg, stA.SubSatLonDeg) }, 500.0);
    var inStep = new Scheduler(conV, geoOne, DeclV(latMin: stA.SubSatLatDeg - 5, latMax: stA.SubSatLatDeg + 5),
        new ScenePointing(vmV), simDurV).Step(0.0);
    var outStep = new Scheduler(conV, geoOne, DeclV(latMin: stA.SubSatLatDeg + 30, latMax: stA.SubSatLatDeg + 60),
        new ScenePointing(vmV), simDurV).Step(0.0);
    bool okU7 = inStep.Links.Count == 1 && outStep.Links.Count == 0 && outStep.UnservedCellLinks == 1;
    Check("U7 ES_LAT_MIN/MAX: cells outside the declared range are not served", okU7,
        $"in={inStep.Links.Count} out={outStep.Links.Count}+{outStep.UnservedCellLinks}u");
}

// ---- U8-U10: power control, deployment fraction, yaw sweep ----
{
    var shellW = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
    };
    var conW = new Constellation(new[] { shellW });
    double simDurW = 600.0;
    var stW = conW.StateAt(0, 0.0, simDurW);
    double wLat = stW.SubSatLatDeg, wLon = stW.SubSatLonDeg;
    var vmW = new PfdMaskViewModel();
    var geoW = new ServiceGeography(new List<ServiceCell> { new(1, wLat, wLon) }, 500.0);
    var declW = new OperatingParamsSet
    {
        SatName = "T", NtcId = 1, ParamId = 1, LowFreqMhz = 27500, HighFreqMhz = 28600,
    };
    var vicW = new EpfdGsoSatVictim
    {
        GsoLonDeg = wLon + 15.0, BoresightLatDeg = wLat, BoresightLonDeg = wLon,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APSREC408V01, 28000.0, null),
        GmaxDbi = 40.7, Phi3DbDeg = 1.55,
    };
    var limitsW = new List<radlimits.LimitPoint>
    {
        new radlimits.LimitPoint { EPFD = -300.0, Perc = 0.001 },
        new radlimits.LimitPoint { EPFD = 0.0, Perc = 100.0 },
    };
    var antW = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 28000.0, 0.65);

    // U8: range-based power control -- the single zenith link transmits
    // 20 log10(dRef/dLink) below the ceiling; null keeps the ceiling.
    double HandUp(double powerDbw)
    {
        var esP = GeodeticToEcef(wLat, wLon, 0.0);
        var satP = stW.PositionEcefKm;
        double lonR = vicW.GsoLonDeg * Math.PI / 180.0;
        var gsoP = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(lonR),
                            GsoGeometry.GsoRadiusKm * Math.Sin(lonR), 0.0);
        double phi = Math.Acos(Math.Clamp(Vec3.Dot((satP - esP).Normalized(),
            (gsoP - esP).Normalized()), -1.0, 1.0)) * 180.0 / Math.PI;
        double dM = (gsoP - esP).Length * 1000.0;
        return powerDbw + antW.GetAntGain(phi, 0.0)
             - 10.0 * Math.Log10(4.0 * Math.PI * dM * dM) + vicW.RelativeGainDb(0.0);
    }
    double dRefW = EpfdUp.SlantRangeKm(stW.AltitudeKm, 10.0);
    double dLinkW = (stW.PositionEcefKm - GeodeticToEcef(wLat, wLon, 0.0)).Length;
    double redW = 20.0 * Math.Log10(dRefW / dLinkW);

    EpfdUpResult RunUp(double? refElev) => EpfdUp.Run(conW,
        new Scheduler(conW, geoW, declW, new ScenePointing(vmW), simDurW), geoW, vicW,
        new EpfdUpEsModel { PowerDbw = 12.0, Antenna = antW, PowerControlRefElevDeg = refElev },
        1.0, 1, limitsW, simDurW);
    var resCeil = RunUp(null);
    var resPc = RunUp(10.0);
    bool okU8 = redW > 0.0
        && Math.Abs(resCeil.MaxEpfdDb - HandUp(12.0)) < 1e-9
        && Math.Abs(resPc.MaxEpfdDb - (HandUp(12.0) - redW)) < 1e-9;
    Check("U8 range-based uplink power control: ceiling and controlled link exact", okU8,
        $"ceil={resCeil.MaxEpfdDb:F6}/{HandUp(12.0):F6} pc={resPc.MaxEpfdDb:F6}/{HandUp(12.0) - redW:F6} red={redW:F3}");

    // U9: OperationalFraction -- spares fly dark and are never scheduled.
    var shellY = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 2, SatsPerPlane = 1,
        LanSpreadDeg = 30.0, OperationalFraction = 0.5,
    };
    var conY = new Constellation(new[] { shellY });
    var snapY = conY.SnapshotAt(0.0, simDurW, new ScenePointing(vmW));
    var stY0 = conY.StateAt(0, 0.0, simDurW);
    // Pitch 900: the operational satellite is 15 deg of longitude away and
    // its nearest beam boresight sits beyond a 500 km radius.
    var geoY = new ServiceGeography(new List<ServiceCell> { new(1, stY0.SubSatLatDeg, stY0.SubSatLonDeg) }, 900.0);
    var stepY = new Scheduler(conY, geoY, declW, new ScenePointing(vmW), simDurW).Step(0.0);
    bool threwFrac = false;
    try { _ = new Constellation(new[] { shellY with { OperationalFraction = 1.5 } }); }
    catch (ArgumentOutOfRangeException) { threwFrac = true; }
    bool okU9 = !conY.IsOperational(0) && conY.IsOperational(1)
        && snapY.Satellites[0].Beams.Beams.Count == 0
        && snapY.Satellites[1].Beams.Beams.Count > 0
        && stepY.Links.Count == 1 && stepY.Links[0].SatelliteNumber == 2
        && threwFrac;
    Check("U9 OperationalFraction: spare flies dark, scheduler serves from the operational sat", okU9,
        $"op=[{conY.IsOperational(0)},{conY.IsOperational(1)}] beams=[{snapY.Satellites[0].Beams.Beams.Count},{snapY.Satellites[1].Beams.Beams.Count}] link={stepY.Links.FirstOrDefault()?.SatelliteNumber} threw={threwFrac}");

    // U10: yaw sweep semantics, probed at the sampler (the export binning
    // is too coarse to see a rotation of the near-symmetric hex layout):
    // a yawed field differs from the heading-locked one, and the swept
    // sampler is exactly the max of the two.
    ReachableEnvelopeSampler Samp(double[] sweep)
    {
        var o10 = new MaskXmlExportOptions
        {
            LatMinDeg = 0, LatMaxDeg = 0, LatStepDeg = 10, BStepDeg = 5, CStepDeg = 5,
            Kind = MaskPlotKind.AzEl, YawSweepDeg = sweep,
        };
        var samp = new ReachableEnvelopeSampler(vmW, o10, 53.0);
        samp.PrepareLatitude(0.0);
        return samp;
    }
    // 37 deg breaks both the hex layout symmetry and the pass headings.
    var s0 = Samp(new[] { 0.0 });
    var s37 = Samp(new[] { 37.0 });
    var sBoth = Samp(new[] { 0.0, 37.0 });
    bool unionOk = true, maxOk = true; double diff37 = 0.0; string detU10 = "";
    for (int k = 0; k < 72 && unionOk && maxOk; k++)
    {
        double az = -180.0 + 5.0 * k;
        foreach (double el in new[] { 20.0, 45.0, 70.0 })
        {
            double v0 = s0.SampleMaxIn(az, el, 2.5, 2.5);
            double v37 = s37.SampleMaxIn(az, el, 2.5, 2.5);
            double vb = sBoth.SampleMaxIn(az, el, 2.5, 2.5);
            double expect = Math.Max(v0, v37);
            if (double.IsFinite(v0) && double.IsFinite(v37))
                diff37 = Math.Max(diff37, Math.Abs(v37 - v0));
            if (double.IsFinite(expect) && Math.Abs(vb - expect) > 1e-9)
            { maxOk = false; detU10 = $"az={az} el={el}: both={vb} max={expect}"; break; }
            if (double.IsFinite(v0) && vb < v0 - 1e-9)
            { unionOk = false; detU10 = $"az={az} el={el}: both={vb} < base={v0}"; break; }
        }
    }
    bool okU10 = unionOk && maxOk && diff37 > 1e-6;
    Check("U10 yaw sweep: yawed field differs, swept sampler is the exact union max", okU10,
        okU10 ? $"maxYawDiff={diff37:F3} dB" : detU10 + $" diff37={diff37:F6}");
}

// ---- U11-U13: activity model, illumination duty, selection policy ----
{
    var shellZ = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
    };
    var conZ = new Constellation(new[] { shellZ });
    double simDurZ = 600.0;
    var stZ = conZ.StateAt(0, 0.0, simDurZ);
    var vmZ = new PfdMaskViewModel();
    var declZ = new OperatingParamsSet
    {
        SatName = "T", NtcId = 1, ParamId = 1, LowFreqMhz = 27500, HighFreqMhz = 28600,
    };

    // U11: on/off traffic -- 200 half-active cells at one instant grant
    // about half the links (deterministic hash across cell ids); factor-0
    // cells contribute nothing; neither counts unserved demand.
    var cellsZ = new List<ServiceCell>();
    for (int c = 1; c <= 200; c++)
        cellsZ.Add(new ServiceCell(c, stZ.SubSatLatDeg, stZ.SubSatLonDeg) { ActivityFactor = 0.5 });
    for (int c = 201; c <= 250; c++)
        cellsZ.Add(new ServiceCell(c, stZ.SubSatLatDeg, stZ.SubSatLonDeg) { ActivityFactor = 0.0 });
    var geoZ = new ServiceGeography(cellsZ, 500.0);
    var stepZ = new Scheduler(conZ, geoZ, declZ, new ScenePointing(vmZ), simDurZ).Step(0.0);
    bool okU11 = stepZ.Links.Count >= 70 && stepZ.Links.Count <= 130
        && stepZ.Links.All(l => l.CellId <= 200)
        && stepZ.UnservedCellLinks == 0;
    Check("U11 activity model: about half the half-active cells link, zero-activity dark", okU11,
        $"links={stepZ.Links.Count}/200 idleCells=50 unserved={stepZ.UnservedCellLinks}");

    // U12: illumination duty cycle -- every beam power carries 10 log10(d),
    // so the composite epfd(down) shifts by exactly that.
    var antZ = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 19700.0, 0.6);
    var vicZ = new EpfdDownVictim
    {
        EsLatDeg = stZ.SubSatLatDeg, EsLonDeg = stZ.SubSatLonDeg,
        GsoLonDeg = stZ.SubSatLonDeg + 15.0, Antenna = antZ,
    };
    var limitsZ = new List<radlimits.LimitPoint>
    {
        new radlimits.LimitPoint { EPFD = -300.0, Perc = 0.001 },
        new radlimits.LimitPoint { EPFD = 0.0, Perc = 100.0 },
    };
    var resFull = EpfdDown.Run(conZ, new ScenePointing(vmZ), vicZ, 1.0, 1, limitsZ, simDurZ);
    var resDuty = EpfdDown.Run(conZ, new ScenePointing(vmZ, 0.25), vicZ, 1.0, 1, limitsZ, simDurZ);
    double dutyShift = resDuty.MaxEpfdDb - resFull.MaxEpfdDb;
    bool threwDuty = false;
    try { _ = new ScenePointing(vmZ, 0.0); } catch (ArgumentOutOfRangeException) { threwDuty = true; }
    bool okU12 = Math.Abs(dutyShift - 10.0 * Math.Log10(0.25)) < 1e-9 && threwDuty;
    Check("U12 illumination duty: composite shifts by exactly 10 log10(duty)", okU12,
        $"shift={dutyShift:F6} expected={10.0 * Math.Log10(0.25):F6} threw={threwDuty}");

    // U13: selection policy -- with two visible satellites whose elevation
    // and alpha rankings disagree, HighestElevation and MaxGsoSeparation
    // pick different satellites (each the argmax of its metric).
    // Two single-sat shells staggered in latitude: A near the equator, B
    // about 24 deg north on the same meridian. A cell between them (8 deg
    // north of A) sees A toward the GSO arc (high elevation, small alpha)
    // and B away from it (low elevation, large alpha) -- the two metrics
    // rank the satellites oppositely.
    var shellPa = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
    };
    var shellPb = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
        InPlaneOffsetDeg = 30.0, Lan0Deg = -18.9,
    };
    var conP2 = new Constellation(new[] { shellPa, shellPb });
    var stP0 = conP2.StateAt(0, 0.0, simDurZ);
    var stP1 = conP2.StateAt(1, 0.0, simDurZ);
    var cellP = new ServiceCell(1, stP0.SubSatLatDeg + 8.0, stP0.SubSatLonDeg);
    var esP2 = GeodeticToEcef(cellP.LatDeg, cellP.LonDeg, 0.0);
    double elevA = ElevationAngleDeg(stP0.PositionEcefKm, esP2);
    double elevB = ElevationAngleDeg(stP1.PositionEcefKm, esP2);
    double alphaA = GsoGeometry.AlphaMinAbsDeg(esP2, stP0.PositionEcefKm);
    double alphaB = GsoGeometry.AlphaMinAbsDeg(esP2, stP1.PositionEcefKm);
    var geoP2 = new ServiceGeography(new List<ServiceCell> { cellP }, 900.0);
    var stepElev = new Scheduler(conP2, geoP2, declZ, new ScenePointing(vmZ), simDurZ).Step(0.0);
    var stepAlpha = new Scheduler(conP2, geoP2, declZ, new ScenePointing(vmZ), simDurZ,
        policy: SelectionPolicy.MaxGsoSeparation).Step(0.0);
    int wantElev = elevA >= elevB ? stP0.SatelliteNumber : stP1.SatelliteNumber;
    int wantAlpha = alphaA >= alphaB ? stP0.SatelliteNumber : stP1.SatelliteNumber;
    bool okU13 = wantElev != wantAlpha
        && stepElev.Links.Count == 1 && stepElev.Links[0].SatelliteNumber == wantElev
        && stepAlpha.Links.Count == 1 && stepAlpha.Links[0].SatelliteNumber == wantAlpha;
    Check("U13 selection policy: elevation and GSO-separation argmax picked respectively", okU13,
        $"elev=[{elevA:F1},{elevB:F1}] alpha=[{alphaA:F1},{alphaB:F1}] " +
        $"gotElev={stepElev.Links.FirstOrDefault()?.SatelliteNumber} gotAlpha={stepAlpha.Links.FirstOrDefault()?.SatelliteNumber}");
}

// ---- V: OrbitDesign -- prototyping the SNS v10 orbit parameters ----
{
    // V1: the promoted Case-3 J2 rate is bit-identical to the value the
    // dataset generator declares for shell C (refactor invariance).
    var shC = radians.beamlab.dataset.DatasetGenerator.ShellC;
    double aC = OrbitalConstants.EarthRadiusKm + shC.AltitudeKm;
    double rateV = OrbitDesign.J2NodalRateDegPerSec(aC, shC.Eccentricity, shC.InclinationDeg);
    Check("V1 Case-3 J2 rate identical to the generator's shell C declaration",
        rateV == shC.PrecessionRateDegPerSec && rateV < 0,
        $"module={rateV:E9} shell={shC.PrecessionRateDegPerSec:E9}");

    // V2: repeat solver end to end -- fly the solved altitude through the
    // vendored propagator for one full cycle and the ascending-node
    // longitude returns to its start.
    var sols = OrbitDesign.RepeatSolutions(1200.0, 0.0, 53.0, maxOrbitsPerCycle: 120);
    var best = sols[0];
    bool shapeOk = sols.Count > 0
        && sols.All(x => x.NodalDays >= 1 && Math.Abs(x.EquatorSpacingDeg - 360.0 / x.Orbits) < 1e-12
                      && Math.Abs(x.MaxKeepRangeDeg - 180.0 / x.Orbits) < 1e-12)
        && Math.Abs(best.RepeatSeconds
            - (((best.RptPrd.Days * 24 + best.RptPrd.Hours) * 60 + best.RptPrd.Minutes) * 60
               + best.RptPrd.Seconds)) <= 0.5;

    var shellR = new ConstellationShell
    {
        AltitudeKm = best.AltitudeKm, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1,
    };
    var conR = new Constellation(new[] { shellR });
    double simDurR = best.RepeatSeconds + 20000.0;
    double CrossLonV(double tStart)
    {
        double t0 = tStart, dt = 20.0;
        double z0 = conR.StateAt(0, t0, simDurR).PositionEcefKm.Z;
        for (int k = 0; k < 400000; k++)
        {
            double t1 = t0 + dt;
            double z1 = conR.StateAt(0, t1, simDurR).PositionEcefKm.Z;
            if (z0 < 0 && z1 >= 0)
            {
                for (int b = 0; b < 60; b++)
                {
                    double tm = 0.5 * (t0 + t1);
                    if (conR.StateAt(0, tm, simDurR).PositionEcefKm.Z < 0) t0 = tm; else t1 = tm;
                }
                return conR.StateAt(0, 0.5 * (t0 + t1), simDurR).SubSatLonDeg;
            }
            t0 = t1; z0 = z1;
        }
        return double.NaN;
    }
    double lonStart = CrossLonV(10.0);
    double lonCycle = CrossLonV(10.0 + best.RepeatSeconds);
    double dLon = ((lonCycle - lonStart) % 360.0 + 540.0) % 360.0 - 180.0;
    Check("V2 repeat solver: one solved cycle returns the ascending node",
        shapeOk && Math.Abs(dLon) < 0.05,
        $"k={best.Orbits} m={best.NodalDays} alt={best.AltitudeKm:F2} " +
        $"(target 1200{best.AltitudeDeltaKm:+0.00;-0.00}) dLon={dLon:F4} shape={shapeOk}");

    // V3: field previews -- the three cases produce the right SNS flags and
    // the keep_rnge overlap rule rejects a deadband at half the spacing.
    var f1 = OrbitDesign.Case1Fields();
    var f2 = OrbitDesign.Case2Fields(best, 0.4 * best.MaxKeepRangeDeg);
    var f3 = OrbitDesign.Case3Fields(rateV);
    bool threwKeep = false;
    try { OrbitDesign.Case2Fields(best, best.MaxKeepRangeDeg); }
    catch (ArgumentOutOfRangeException) { threwKeep = true; }
    bool okV3 = f1 is { FStnKeep: 'N', FPrecess: 'N', KeepRngeDeg: null, RptPrd: null }
        && f2.FStnKeep == 'Y' && f2.RptPrd == best.RptPrd
        && Math.Abs(f2.KeepRngeDeg!.Value - 0.4 * best.MaxKeepRangeDeg) < 1e-12
        && f3 is { FPrecess: 'Y', FStnKeep: 'N' } && f3.PrecessionDegPerSec == rateV
        && threwKeep;
    Check("V3 SNS field previews per case; keep_rnge overlap rule enforced", okV3,
        $"f2=({f2.FStnKeep},{f2.KeepRngeDeg:F3},rpt={f2.RptPrd?.Days}d{f2.RptPrd?.Hours}h) threw={threwKeep}");

    // V4: the precession plan restates the Case-1 formula set exactly.
    var plan = OrbitDesign.PrecessionPlan(aC, 0.0, 53.0, 288);
    var (spV, tnV) = OrbitDesign.NodalPassGeometry(aC, 0.0, 53.0);
    double gridV = 360.0 * Math.Floor(288 * spV / 360.0) / 288;
    bool okV4 = plan.RateRadPerSec == ArtificialPrecession.RadPerSec(aC, 0.0, 53.0, 288)
        && plan.SPassDeg == spV && plan.SGridDeg == gridV
        && plan.MeasuredSpacingDeg == 2.0 * spV - gridV
        && plan.RunDurationSec == 288 * tnV;
    Check("V4 precession plan identical to the Steps 8-11 formula set", okV4,
        $"spass={plan.SPassDeg:F4} grid={plan.SGridDeg:F4} measured={plan.MeasuredSpacingDeg:F4} rate={plan.RateDegPerSec:E3}");
}

// ---- V5: the Orbit Design tab view model, headless ----
{
    var vmO = new OrbitDesignViewModel();   // defaults: 1200 km, i 53, e 0
    var expO = OrbitDesign.RepeatSolutions(1200.0, 0.0, 53.0, 120, take: 10);
    bool rowsOk = vmO.Solutions.Count == expO.Count && vmO.Solutions.Count > 0
        && vmO.Solutions[0].Solution == expO[0]
        && ReferenceEquals(vmO.SelectedSolution, vmO.Solutions[0]);

    bool textsOk = vmO.Case2Text.Contains("rpt_prd_dd=") && vmO.KeepRangeValid
        && vmO.Case1Text.Contains("2*S_pass - S_grid")
        && vmO.Case3Text.Contains("f_precess='Y'")
        && vmO.BuildCopyText().Contains("[Case 2 station-kept repeating]");

    vmO.KeepRangeDeg = vmO.SelectedSolution!.Solution.MaxKeepRangeDeg + 1.0;
    bool invalidCaught = !vmO.KeepRangeValid;
    vmO.KeepRangeDeg = 0.5;
    bool validAgain = vmO.KeepRangeValid;

    bool trackOk = vmO.TrackSegments.Count > 0
        && vmO.TrackSegments.Sum(seg => seg.Count) > vmO.SelectedSolution.Solution.Orbits * 100
        && vmO.TrackClosureDeg < 0.05;

    Check("V5 Orbit Design view model: rows, previews, keep_rnge validation, track closure",
        rowsOk && textsOk && invalidCaught && validAgain && trackOk,
        $"rows={vmO.Solutions.Count} closure={vmO.TrackClosureDeg:F4} " +
        $"invalidCaught={invalidCaught} texts={textsOk}");
}

// ---- V6: the Home tab view model, headless ----
{
    // Running inside the repo tree, the docs walk-up must find the guide
    // and the parameter cards; the four function cards target tabs 1..4.
    // Alt-dir builds run outside the repo tree; fall back to the repo root
    // (the J0 pattern) so the docs walk-up still resolves.
    string homeStart = radians.beamlab.app.HomeViewModel.FindDocsDir(AppContext.BaseDirectory) is null
        ? @"C:\Projects\radians.beamlab" : AppContext.BaseDirectory;
    var vmH = new HomeViewModel(homeStart);
    bool okV6 = vmH.Functions.Count == 4
        && vmH.Functions.Select(f => f.TabIndex).SequenceEqual(new[] { 1, 2, 3, 4 })
        && vmH.Functions.All(f => f.Title.Length > 0 && f.Description.Length > 40)
        && vmH.UserGuidePath is not null && File.Exists(vmH.UserGuidePath)
        && vmH.ParameterCardsPath is not null && File.Exists(vmH.ParameterCardsPath)
        && vmH.OrbitCasesPath is not null && File.Exists(vmH.OrbitCasesPath)
        && vmH.RepeatSolverPath is not null && File.Exists(vmH.RepeatSolverPath)
        && vmH.VersionText.StartsWith("v1.");
    Check("V6 Home view model: four cards, docs resolved from the repo tree, version", okV6,
        $"funcs={vmH.Functions.Count} guide={vmH.UserGuidePath is not null} " +
        $"cards={vmH.ParameterCardsPath is not null} ver={vmH.VersionText}");
}

// ---- V7: Case-1 run length from the victim beam ----
{
    // Hand transcription of Part D eq (3) and D4.6.2 Steps 5-7 (N_tracks 16).
    double bwV = 4.0, altV = 1200.0;
    double halfRad = bwV * Math.PI / 360.0;
    double kV = OrbitalConstants.EarthRadiusKm / (OrbitalConstants.EarthRadiusKm + altV);
    double phiHand = (halfRad - Math.Asin(kV * Math.Sin(halfRad))) * 180.0 / Math.PI;
    int nHand = (int)Math.Ceiling(180.0 / (2.0 * phiHand / 16.0));
    bool formulaOk = Math.Abs(OrbitDesign.BeamCrossingHalfAngleDeg(bwV, altV) - phiHand) < 1e-12
        && OrbitDesign.SuggestedNOrbits(bwV, altV) == nHand
        && OrbitDesign.SuggestedNOrbits(2.0 * bwV, altV) < nHand      // wider beam -> fewer orbits
        && OrbitDesign.SuggestedNOrbits(bwV, 4000.0) < nHand;         // higher shell -> larger phi

    // VM wiring: the beamwidth drives NOrbits at the selected candidate's
    // altitude; clearing it returns NOrbits to manual control.
    var vmB = new OrbitDesignViewModel();
    vmB.VictimBeamwidthText = "4";
    int expN = OrbitDesign.SuggestedNOrbits(4.0, vmB.TargetAltitudeKm);
    bool wiredOk = vmB.NOrbits == expN && vmB.Case1Text.Contains("orbits");
    vmB.VictimBeamwidthText = "";
    vmB.NOrbits = 288;
    bool manualOk = vmB.NOrbits == 288;

    Check("V7 NOrbits from the victim beam: eq (3) chain exact, VM wiring",
        formulaOk && wiredOk && manualOk,
        $"phi={phiHand:F4} n={nHand} vmN={expN} manual={manualOk}");
}

// ---- V8: parameter catalog locked to the card deck ----
{
    // The catalog is the app-facing twin of docs/parameter-cards.html; both
    // must carry the same text. Normalise the page the way the catalog was
    // ported (strip tags, decode entities, collapse whitespace) and require
    // every entry's name and description verbatim.
    string docsDirV8 = radians.beamlab.app.HomeViewModel.FindDocsDir(AppContext.BaseDirectory)
        ?? @"C:\Projects\radians.beamlab\docs";
    string cardsPath = Path.Combine(docsDirV8, "parameter-cards.html");
    string norm = System.Text.RegularExpressions.Regex.Replace(
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(cardsPath), "<[^>]+>", "")), @"\s+", " ");
    var missing = ParameterCatalog.All
        .Where(e => !norm.Contains(e.Name) || !norm.Contains(e.Description))
        .Select(e => e.Name).ToList();
    bool okV8 = ParameterCatalog.All.Count == 24
        && ParameterCatalog.All.Count(e => e.Group == ParameterGroup.Declared) == 11
        && ParameterCatalog.All.Count(e => e.Group == ParameterGroup.Truth) == 9
        && ParameterCatalog.All.Count(e => e.Group == ParameterGroup.Orbit) == 4
        && missing.Count == 0
        && ParameterCatalog.Find("MIN_EXCLUDE") is { } me && me.ToolTipText.Contains("- ");
    Check("V8 parameter catalog: 24 entries locked verbatim to the card deck", okV8,
        missing.Count > 0 ? "drifted: " + string.Join(", ", missing.Take(3))
                          : $"entries={ParameterCatalog.All.Count}");
}

// ---- V9: constellation construction and the design file ----
{
    var vmC = new OrbitDesignViewModel();
    vmC.PlaneCount = 3; vmC.SatsPerPlane = 5; vmC.LanSpreadDeg = 360.0;
    vmC.CaseChoice = 1;   // Case 2 station-kept, selected candidate present
    var sol = vmC.SelectedSolution!.Solution;
    bool tablesOk = vmC.OrbitRows.Count == 3 && vmC.PhaseRows.Count == 15
        && vmC.OrbitRows[0].StationKeeping
        && vmC.OrbitRows[0].KeepRangeDeg == vmC.KeepRangeDeg
        && vmC.OrbitRows[0].RepeatPeriod == sol.RptPrd
        && Math.Abs(vmC.OrbitRows[1].LanDeg - vmC.OrbitRows[0].LanDeg - 120.0) < 1e-9
        && Math.Abs(vmC.PhaseRows[1].PhaseAngDeg - 72.0) < 1e-9;

    vmC.CaseChoice = 2;
    bool case3Ok = vmC.OrbitRows[0].PrecessionSupplied
        && vmC.OrbitRows[0].PrecessionRateDegPerSec < 0
        && !vmC.OrbitRows[0].StationKeeping;
    vmC.CaseChoice = 0;
    bool case1Ok = !vmC.OrbitRows[0].StationKeeping && !vmC.OrbitRows[0].PrecessionSupplied;

    var notice = vmC.BuildNotice();
    bool noticeOk = notice.NtcId == 0 && notice.SatName == "DESIGN"
        && notice.Orbits.Count == 3 && notice.Phases.Count == 15;

    string j1 = vmC.BuildDesignJson();
    var vmD = new OrbitDesignViewModel();
    vmD.LoadDesignJson(j1);
    bool jsonOk = vmD.BuildDesignJson() == j1 && vmD.OrbitRows.Count == 3
        && vmD.PhaseRows.Count == 15;

    Check("V9 constellation tables, case fields, notice and design-file round-trip",
        tablesOk && case3Ok && case1Ok && noticeOk && jsonOk,
        $"orb={vmC.OrbitRows.Count} ph={vmC.PhaseRows.Count} json={jsonOk} notice={noticeOk}");
}

// ---- V10: the SNS v10 builder assembles a notice from elements ----
{
    // A Case-2 design saved by the tab (with its selected candidate), then
    // consumed by the builder alongside mask registrations and R sets.
    var vmS = new OrbitDesignViewModel();
    vmS.CaseChoice = 1; vmS.PlaneCount = 2; vmS.SatsPerPlane = 3;
    string dj = vmS.BuildDesignJson();
    string tmpD = Path.Combine(AppContext.BaseDirectory, "exp", "v10.orbitdesign.json");
    Directory.CreateDirectory(Path.GetDirectoryName(tmpD)!);
    File.WriteAllText(tmpD, dj);

    var shellB = OrbitDesignFileCodec.ToShell(OrbitDesignFileCodec.Load(dj));
    bool shellOk = shellB.StationKeeping
        && shellB.RepeatPeriod == vmS.SelectedSolution!.Solution.RptPrd
        && Math.Abs(shellB.AltitudeKm - vmS.SelectedSolution.Solution.AltitudeKm) < 1e-9;

    var b = new SnsBuilderViewModel { NtcId = 900555001, SatName = "V10SAT" };
    b.AddShellFile(tmpD);
    b.Masks.Add(new MaskEntry { MaskId = 1, FilePath = "a.xml", FMask = "P", FMaskType = "A", FreqMinMhz = 19700, FreqMaxMhz = 20200 });
    b.Masks.Add(new MaskEntry { MaskId = 6, FilePath = "b.xml", FMask = "E", FMaskType = "O", FreqMinMhz = 27500, FreqMaxMhz = 28600 });
    b.Masks.Add(new MaskEntry { MaskId = 21, FilePath = "c.xml", FMask = "R", FreqMinMhz = 19700, FreqMaxMhz = 20200 });
    b.Frequencies.Add(new FreqEntry { EmiRcp = "E", FreqMinMhz = 19700, FreqMaxMhz = 20200 });
    b.Frequencies.Add(new FreqEntry { EmiRcp = "R", FreqMinMhz = 27500, FreqMaxMhz = 28600 });

    var nB = b.BuildNotice();   // validates internally
    bool okV10 = shellOk
        && nB.NtcId == 900555001 && nB.SatName == "V10SAT"
        && nB.Orbits.Count == 2 && nB.Phases.Count == 6
        && nB.MaskInfo.Count == 3 && nB.OperatingParamIds.SequenceEqual(new[] { 21 })
        && nB.Scenarios.Count == 1 && nB.Scenarios[0].Frequencies.Count == 2
        && nB.Scenarios[0].PfdMaskLinks.Count == 1 && nB.Scenarios[0].EsMaskLinks.Count == 1
        && b.BuildMaskContents().Count == 3
        && b.SummaryText().Contains("1 R set");
    Check("V10 SNS builder: design-file shell, mask registry, R set, auto-linked scenario", okV10,
        $"orb={nB.Orbits.Count} ph={nB.Phases.Count} mi={nB.MaskInfo.Count} " +
        $"lnk1={nB.Scenarios[0].PfdMaskLinks.Count} lnk2={nB.Scenarios[0].EsMaskLinks.Count} shell={shellOk}");
}

// ---- V11: the own-period validator ----
{
    // A pair the scan itself finds must validate to identical numbers.
    var scanW = OrbitDesign.RepeatSolutions(1200.0, 0.0, 53.0, 120, take: 10);
    var refW = scanW[0];   // 13 orbits / 1 nodal day near 1205 km
    var chkA = OrbitDesign.CheckRepeat(1200.0, 0.0, 53.0, refW.Orbits, refW.NodalDays, 400.0);
    bool agreeOk = chkA.Solution is { } sa && chkA.WithinBand && !chkA.Reduced
        && Math.Abs(sa.AltitudeKm - refW.AltitudeKm) < 1e-6
        && sa.RptPrd == refW.RptPrd && sa.MaxKeepRangeDeg == refW.MaxKeepRangeDeg;

    // A non-coprime pair reduces to the true cycle at the same altitude.
    var chkB = OrbitDesign.CheckRepeat(1200.0, 0.0, 53.0, refW.Orbits * 2, refW.NodalDays * 2, 400.0);
    bool reduceOk = chkB.Reduced && chkB.Orbits == refW.Orbits && chkB.NodalDays == refW.NodalDays
        && chkB.Solution is { } sb && Math.Abs(sb.AltitudeKm - refW.AltitudeKm) < 1e-6;

    // 15/1 closes far below a 1200 km target: still solved, flagged out of band.
    var chkC = OrbitDesign.CheckRepeat(1200.0, 0.0, 53.0, 15, 1, 400.0);
    bool bandOk = chkC.Solution is { } sc && !chkC.WithinBand
        && sc.AltitudeDeltaKm < -400.0 && sc.AltitudeKm > 100.0;

    // VM: the entered pair becomes the selected, highlighted top row and
    // replaces the identical scan row; clearing restores the plain scan.
    var vmV = new OrbitDesignViewModel();
    int baseCount = vmV.Solutions.Count;
    vmV.CheckOrbitsText = refW.Orbits.ToString();
    vmV.CheckDaysText = refW.NodalDays.ToString();
    bool vmOk = vmV.Solutions.Count == baseCount
        && vmV.Solutions[0].IsUserEntry && vmV.Solutions[0].WithinBand
        && ReferenceEquals(vmV.SelectedSolution, vmV.Solutions[0])
        && vmV.Solutions.Count(r => r.Orbits == refW.Orbits && r.NodalDays == refW.NodalDays) == 1
        && vmV.CheckStatusText.Contains("closes at")
        && vmV.Case2Text.Contains("rpt_prd_dd=");
    vmV.CheckOrbitsText = (refW.Orbits * 2).ToString();
    vmV.CheckDaysText = (refW.NodalDays * 2).ToString();
    bool vmReduceOk = vmV.CheckStatusText.Contains("reduces to")
        && vmV.Solutions[0].Orbits == refW.Orbits;
    vmV.CheckOrbitsText = ""; vmV.CheckDaysText = "";
    bool clearOk = vmV.CheckStatusText.Length == 0
        && vmV.Solutions.Count == baseCount && !vmV.Solutions[0].IsUserEntry;

    Check("V11 own-period validator: scan agreement, gcd reduction, band flag, VM row",
        agreeOk && reduceOk && bandOk && vmOk && vmReduceOk && clearOk,
        $"agree={agreeOk} reduce={reduceOk} band={bandOk} vm={vmOk} clear={clearOk}");
}

// ---- V12: Case-3 admin-supplied precession override ----
{
    var vmP = new OrbitDesignViewModel();
    vmP.CaseChoice = 2;   // Case 3 declared precession
    double aT12 = OrbitalConstants.EarthRadiusKm + vmP.TargetAltitudeKm;
    double j2Def = OrbitDesign.J2NodalRateDegPerSec(aT12, 0.0, 53.0);
    bool defOk = vmP.OrbitRows[0].PrecessionRateDegPerSec == j2Def
        && vmP.Case3Text.Contains("plain-J2");

    vmP.PrecessionText = "-2.5e-5";
    bool ovrOk = vmP.OrbitRows[0].PrecessionRateDegPerSec == -2.5e-5
        && vmP.BuildShell().PrecessionRateDegPerSec == -2.5e-5
        && vmP.Case3Text.Contains("admin-supplied") && vmP.Case3Text.Contains("would be");

    vmP.PrecessionText = "1.14e-5";   // retrograde-style positive passes signed
    bool signOk = vmP.OrbitRows[0].PrecessionRateDegPerSec == 1.14e-5;

    vmP.PrecessionText = "-2.5e-5";
    string j12 = vmP.BuildDesignJson();
    var d12 = OrbitDesignFileCodec.Load(j12);
    var sh12 = OrbitDesignFileCodec.ToShell(d12);
    bool fileOk = d12.SchemaVersion == 3 && d12.PrecessionDegPerSec == -2.5e-5
        && sh12.PrecessionSupplied && sh12.PrecessionRateDegPerSec == -2.5e-5;

    var vmQ = new OrbitDesignViewModel();
    vmQ.LoadDesignJson(j12);
    bool loadOk = vmQ.BuildDesignJson() == j12
        && vmQ.OrbitRows[0].PrecessionRateDegPerSec == -2.5e-5;

    // A version-2 file (field absent) declares the plain-J2 default.
    var shv2 = OrbitDesignFileCodec.ToShell(
        OrbitDesignFileCodec.Load(OrbitDesignFileCodec.Save(
            d12 with { SchemaVersion = 2, PrecessionDegPerSec = null })));
    bool v2Ok = shv2.PrecessionRateDegPerSec == j2Def;

    Check("V12 Case-3 precession override: default, signed pass-through, schema v3, v2 fallback",
        defOk && ovrOk && signOk && fileOk && loadOk && v2Ok,
        $"def={defOk} ovr={ovrOk} sign={signOk} file={fileOk} load={loadOk} v2={v2Ok}");
}

// ---- V13: the multi-shell design document ----
{
    var doc = new OrbitDesignDocumentViewModel();
    bool startOk = doc.Shells.Count == 1 && ReferenceEquals(doc.SelectedShell, doc.Shells[0])
        && doc.ShellHeaderText == "editing shell 1 of 1";

    doc.AddShell();
    doc.SelectedShell.TargetAltitudeKm = 800.0;
    doc.SelectedShell.CaseChoice = 0;
    bool independentOk = doc.Shells.Count == 2
        && ReferenceEquals(doc.SelectedShell, doc.Shells[1])
        && doc.Shells[0].TargetAltitudeKm == 1200.0
        && doc.Shells[1].TargetAltitudeKm == 800.0
        && doc.ShellHeaderText == "editing shell 2 of 2";

    // Combined preview: both shells' rows, orb ids unique across shells.
    doc.PreviewAllShells = true;
    var comb = doc.BuildCombinedNotice();
    int expOrb = doc.Shells[0].OrbitRows.Count + doc.Shells[1].OrbitRows.Count;
    int expPh = doc.Shells[0].PhaseRows.Count + doc.Shells[1].PhaseRows.Count;
    bool combinedOk = doc.PreviewOrbitRows.Count == expOrb && doc.PreviewPhaseRows.Count == expPh
        && comb.Orbits.Select(o => o.OrbId).Distinct().Count() == expOrb;
    doc.PreviewAllShells = false;
    bool selectedOnlyOk = doc.PreviewOrbitRows.Count == doc.SelectedShell.OrbitRows.Count;

    // Document JSON round-trip; a bare v3 single-shell file loads as one shell.
    string dj13 = doc.BuildDocumentJson();
    var doc2 = new OrbitDesignDocumentViewModel();
    doc2.LoadDocumentJson(dj13);
    bool roundOk = doc2.Shells.Count == 2 && doc2.Shells[1].TargetAltitudeKm == 800.0
        && doc2.Shells[1].CaseChoice == 0 && doc2.BuildDocumentJson() == dj13;
    var doc3 = new OrbitDesignDocumentViewModel();
    doc3.LoadDocumentJson(doc.Shells[0].BuildDesignJson());
    bool v3Ok = doc3.Shells.Count == 1 && doc3.Shells[0].TargetAltitudeKm == 1200.0;

    // Duplicate deep-copies the selection; remove always keeps one shell.
    doc.DuplicateSelected();
    bool dupOk = doc.Shells.Count == 3
        && doc.SelectedShell.TargetAltitudeKm == 800.0
        && !ReferenceEquals(doc.SelectedShell, doc.Shells[1]);
    doc.RemoveSelected(); doc.RemoveSelected(); doc.RemoveSelected();
    bool removeOk = doc.Shells.Count == 1;

    Check("V13 multi-shell document: independence, combined preview, round-trip, v3 load",
        startOk && independentOk && combinedOk && selectedOnlyOk && roundOk && v3Ok && dupOk && removeOk,
        $"start={startOk} indep={independentOk} comb={combinedOk} sel={selectedOnlyOk} " +
        $"round={roundOk} v3={v3Ok} dup={dupOk} rm={removeOk}");
}

// ---- V14: the builder consumes a whole design document ----
{
    var docB = new OrbitDesignDocumentViewModel();
    docB.Shells[0].PlaneCount = 2; docB.Shells[0].SatsPerPlane = 3;
    docB.AddShell();
    docB.SelectedShell.TargetAltitudeKm = 800.0;
    docB.SelectedShell.CaseChoice = 0;
    docB.SelectedShell.PlaneCount = 3; docB.SelectedShell.SatsPerPlane = 2;
    string tmpDoc = Path.Combine(AppContext.BaseDirectory, "exp", "v14.orbitdesign.json");
    Directory.CreateDirectory(Path.GetDirectoryName(tmpDoc)!);
    File.WriteAllText(tmpDoc, docB.BuildDocumentJson());

    var bb = new SnsBuilderViewModel { NtcId = 900555002, SatName = "V14SAT" };
    bb.AddShellFile(tmpDoc);
    bb.Masks.Add(new MaskEntry { MaskId = 1, FilePath = "a.xml", FMask = "P", FMaskType = "A", FreqMinMhz = 19700, FreqMaxMhz = 20200 });
    bb.Frequencies.Add(new FreqEntry { EmiRcp = "E", FreqMinMhz = 19700, FreqMaxMhz = 20200 });
    var nb = bb.BuildNotice();
    bool okV14 = bb.Shells.Count == 2
        && nb.Orbits.Count == 5 && nb.Phases.Count == 12
        && nb.Orbits.Select(o => o.OrbId).Distinct().Count() == 5;
    Check("V14 builder loads a schema-4 document: one file, all shells", okV14,
        $"entries={bb.Shells.Count} orb={nb.Orbits.Count} ph={nb.Phases.Count}");
}

Console.WriteLine($"\n===== {pass} passed, {fail} failed =====");
return fail == 0 ? 0 : 1;
