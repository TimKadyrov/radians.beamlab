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

// Opt-in measurement modes: the first margin figure (docs/margin-figure.md)
// and the payload envelope study (docs/margin-study.md).
if (args.Length > 0 && args[0] == "margin")
    return MarginFigure.Run(
        args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 60.0,
        args.Length > 2 ? long.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0,
        args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 5.0,
        args.Length > 4 ? double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 10.0);
if (args.Length > 0 && args[0] == "loop")
{
    string[] a = args;
    string srcDir = System.IO.Path.Combine(@"C:\Projects\radians.beamlab", "dataset", "_src");
    double D(int i, double dflt) => a.Length > i && double.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : dflt;
    // reuse=<run dir>: skip the probe, read that run's R set and mask back, and
    // run only the truth sweep and the examination at this depth.
    string? reuse = a.FirstOrDefault(x => x.StartsWith("reuse=", StringComparison.OrdinalIgnoreCase))?.Substring("reuse=".Length);
    // gso=<deg> and eslon=<deg>: the victim geometry -- the wanted GSO satellite's longitude
    // offset east of the earth station, and the earth station's longitude. Defaults +10
    // and 0, the geometry every record before 2026-09-21 used.
    double Opt(string key, double dflt)
    {
        string? v = a.FirstOrDefault(x => x.StartsWith(key, StringComparison.OrdinalIgnoreCase))?.Substring(key.Length);
        return v is not null && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : dflt;
    }
    double gso = Opt("gso=", 10.0), eslon = Opt("eslon=", 0.0);
    // examstep=d4: also run E1 on the S.1503-4 time step (Sec. D4, the dual time step
    // of Sec. D5.1.4.1), beside the E1 that shares the truth's step.
    bool examD4 = a.Any(x => x.Equals("examstep=d4", StringComparison.OrdinalIgnoreCase));
    return ComplianceLoop.Run(
        a.Length > 1 ? a[1] : System.IO.Path.Combine(srcDir, "STEAM-2.opprofile.json"),
        a.Length > 2 ? a[2] : System.IO.Path.Combine(srcDir, "STEAM-2.orbitdesign.json"),
        D(3, 0.1), D(4, 60.0), D(5, 0.0), D(6, 60.0), D(7, 10.0),
        a.Any(x => x.Equals("walk", StringComparison.OrdinalIgnoreCase)),
        a.Any(x => x.Equals("minimise", StringComparison.OrdinalIgnoreCase)),
        reuse, gso, eslon, examD4);
}
if (args.Length > 0 && args[0] == "beamcount")
{
    string[] b = args;
    string srcB = System.IO.Path.Combine(@"C:\Projects\radians.beamlab", "dataset", "_src");
    double DB(int i, double dflt) => b.Length > i && double.TryParse(b[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : dflt;
    return BeamCount.Run(
        b.Length > 1 ? b[1] : System.IO.Path.Combine(srcB, "STEAM-2.opprofile.json"),
        b.Length > 2 ? b[2] : System.IO.Path.Combine(srcB, "STEAM-2.orbitdesign.json"),
        DB(3, 0.02), DB(4, 60.0),
        b.Any(x => x.Equals("truth", StringComparison.OrdinalIgnoreCase)));
}
if (args.Length > 0 && args[0] == "examine")
{
    // examstep=d4 anywhere after the mode: also examine on the S.1503-4 time step.
    bool examD4E = args.Any(x => x.Equals("examstep=d4", StringComparison.OrdinalIgnoreCase));
    string[] e = args.Where(x => !x.StartsWith("examstep=", StringComparison.OrdinalIgnoreCase)).ToArray();
    if (e.Length < 5) { Console.WriteLine("usage: examine profile design rset.json mask.xml [days] [stepSec] [latFrom] [latTo] [latStep] [tag] [examstep=d4]"); return 2; }
    double DE(int i, double dflt) => e.Length > i && double.TryParse(e[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : dflt;
    return ComplianceLoop.Examine(e[1], e[2], e[3], e[4],
        DE(5, 0.1), DE(6, 60.0), DE(7, 0.0), DE(8, 60.0), DE(9, 10.0),
        e.Length > 10 ? e[10] : "examine", examD4E);
}
if (args.Length > 0 && args[0] == "parity")
    return MaskParity.Run(
        args.Length > 1 ? args[1] : @"c:\_3\mask ntc_id 317520389 mask_id 150 17700-20200 MHz.xml",
        args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 5.0);
if (args.Length > 0 && args[0] == "dissect")
    return MaskDissectCli.Run(
        args.Length > 1 ? args[1] : @"c:\_3\mask ntc_id 317520389 mask_id 150 17700-20200 MHz.xml",
        args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1150.0);
if (args.Length > 0 && args[0] == "oracle")
    return Oracle.Run(
        args.Length > 1 ? long.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 86400,
        args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1.0);
if (args.Length > 0 && args[0] == "study")
    return MarginFigure.Study(
        args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 60.0,
        args.Length > 2 ? long.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0);
// Measurement scan behind the section 3.9 read-rule probes (ReadRuleScan).
if (args.Length > 0 && args[0] == "probescan")
    return radians.beamlab.checks.ReadRuleScan.Run(args.Skip(1).ToArray());
// The margin decomposition of the design brief's section 2 (MarginDecomposition): T, E_sel, E1 on one victim grid.
if (args.Length > 0 && args[0] == "decompose")
{
    string[] m = args;
    string srcM = System.IO.Path.Combine(@"C:\Projects\radians.beamlab", "dataset", "_src");
    double DM(int i, double dflt) => m.Length > i && double.TryParse(m[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : dflt;
    return radians.beamlab.checks.MarginDecomposition.Run(
        m.Length > 1 ? m[1] : System.IO.Path.Combine(srcM, "STEAM-2.opprofile.json"),
        m.Length > 2 ? m[2] : System.IO.Path.Combine(srcM, "STEAM-2.orbitdesign.json"),
        m.Length > 3 ? m[3] : System.IO.Path.Combine(@"C:\Projects\radians.beamlab", "dataset", "margin", "steam-2"),
        DM(4, 0.1), DM(5, 60.0), DM(6, 0.0), DM(7, 60.0), DM(8, 10.0),
        m.Length > 9 ? m[9] : "steam-2");
}
// The curve verdict rule over the dataset's expected examination CDFs (CurveScan): does any expected
// verdict flip when the CDF is tested against the log-linear limit curve, not only its tabulated points?
//   curvescan [toleranceDb]
if (args.Length > 0 && args[0] == "curvescan")
    return radians.beamlab.checks.CurveScan.Run(args.Skip(1).ToArray());
// Seconds per simulated step at the current thread count (Bench): the STEAM-2 truth step and
// the mask-examination step. Run once with BEAMLAB_THREADS=1 and once without for the speed-up.
//   bench [profile] [design] [rsetDir] [steps] [stepSec]
if (args.Length > 0 && args[0] == "bench")
{
    string[] b = args;
    string srcB = System.IO.Path.Combine(@"C:\Projects\radians.beamlab", "dataset", "_src");
    double DB(int i, double dflt) => b.Length > i && double.TryParse(b[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : dflt;
    return radians.beamlab.checks.Bench.Run(
        b.Length > 1 ? b[1] : System.IO.Path.Combine(srcB, "STEAM-2.opprofile.json"),
        b.Length > 2 ? b[2] : System.IO.Path.Combine(srcB, "STEAM-2.orbitdesign.json"),
        b.Length > 3 ? b[3] : System.IO.Path.Combine(@"C:\Projects\radians.beamlab", "dataset", "margin", "steam-2"),
        (long)DB(4, 10), DB(5, 60.0));
}
// The case (a) measurement for the 11.32A concept note (ArcShield): arc-protecting vs not, against every 22-1C row.
if (args.Length > 0 && args[0] == "arcshield")
    return radians.beamlab.checks.ArcShield.Run(args.Skip(1).ToArray());
// Grade a pfd mask against a dataset operating-parameter set (MaskConsistency):
//   grade <mask.xml> <altitudeKm> <paramId>
if (args.Length > 3 && args[0] == "grade")
{
    var repG = MaskConsistency.Check(args[1], double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture),
        radians.beamlab.dataset.DatasetGenerator.SetFor(int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), 0));
    Console.WriteLine(repG.Summary);
    if (repG.Note.Length > 0) Console.WriteLine(repG.Note);
    foreach (var r in repG.Rows.Where(x => x.Alpha != MaskConsistency.Verdict.Dark))
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  block {r.LatDeg,5:0.#}: reach alpha {r.ReachAlpha,5:F1} dark {r.DarkAlpha,5:F1} declared {r.DeclaredAlpha,4:F1} {MaskConsistency.Word(r.Alpha),-30} | reach elev {r.ReachElev,5:F1} lit {MaskConsistency.LitText(r.LitReachElev, System.Globalization.CultureInfo.InvariantCulture),5} dark {r.DarkElev,5:F1} declared {r.DeclaredElev,4:F1} {MaskConsistency.Word(r.Elev)}"));
    return 0;
}

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
    double halfRowE2 = opts.LatStepDeg / 2.0;
    var bandFieldsE2 = new List<PfdMaskField>();
    foreach (double bl in new[] { -halfRowE2, 0.0, halfRowE2 })
    {
        var genE2 = new PfdMaskViewModel();
        vm.CopySettingsTo(genE2);
        genE2.Scene.SubSatLatDeg = bl;
        genE2.RebuildForCompute();
        var fE2 = new PfdMaskField();
        fE2.Rebuild(genE2);
        bandFieldsE2.Add(fE2);
    }

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
                // The a=0 row governs +/- LatStepDeg/2, so the exported value is
                // the max over that band -- not the field at latitude 0 alone.
                double fieldV = double.NegativeInfinity;
                foreach (var bf in bandFieldsE2)
                    fieldV = Math.Max(fieldV, bf.SampleMaxIn(cv, bv, 30.0, 15.0));   // x = DeltaL = c, y = alpha = b
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
    Check("E2e export a=0 block equals the live field bin-max across the row band", nodesMatch, mismatch);

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

    // K2: header-only / array-only / both-with-different-values variants.
    // Header and array are mutually exclusive per quantity (EPS V43 6.7.2.2,
    // design brief 3.8): the both-forms set is an invalid filing, refused by
    // the writer unless emitted deliberately as the dataset's invalid probe.
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
    bool refusedB = false;
    try { OperParamsXmlWriter.Write(bPath, both); }
    catch (ArgumentException ex) { refusedB = ex.Message.Contains("both header and array form") && ex.Message.Contains("max_co_freq"); }
    OperParamsXmlWriter.Write(bPath, both, allowBothForms: true);   // the invalid-filing probe, emitted on purpose

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
            && db2.SelectSingleNode("//max_co_freq")!.InnerText == "4"
            && DeclaredConstraints.FormConflicts(both).Count == 1;
    Check("K2 header-only / array-only encode correctly; a both-forms set is refused unless emitted as the invalid-filing probe",
        okH && okA && okB && refusedB, $"header={okH} array={okA} both={okB} refused={refusedB}");

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
    // A row governs the half-step either side of it (Sec. D5.1.5 step 1 reads
    // the NEAREST latitude), so the envelope maxes over the band as well as
    // over the two pass headings. Pinning the centre alone would pin an
    // under-declaring mask -- measured at 7.4 dB on the BL-D2 case.
    PfdMaskField FieldAt(double psi, double latDeg)
    {
        var gen = new PfdMaskViewModel();
        vmL.CopySettingsTo(gen);
        gen.MaskKind = MaskPlotKind.AzEl;
        gen.Scene.SubSatLatDeg = latDeg;
        gen.Scene.BodyYawDeg = psi;
        gen.RebuildForCompute();
        var f = new PfdMaskField();
        f.Rebuild(gen);
        return f;
    }
    double halfRowL = optsL.LatStepDeg / 2.0;
    var BandLatsL = new[] { 35.0 - halfRowL, 35.0, 35.0 + halfRowL };
    var fieldsL = new List<PfdMaskField>();
    foreach (double psi in new[] { hhL.AscendingDeg, hhL.DescendingDeg })
        foreach (double bl in BandLatsL)
            fieldsL.Add(FieldAt(psi, bl));
    var fAsc = FieldAt(hhL.AscendingDeg, 35.0);
    var fDesc = FieldAt(hhL.DescendingDeg, 35.0);

    bool okL3 = true; string detL3 = ""; int differ = 0, probes = 0;
    for (double az = -85; az <= 85 && okL3; az += 10)
    for (double el = -85; el <= 85 && okL3; el += 10)
    {
        double a1 = fAsc.SampleMaxIn(az, el, 15.0, 15.0);
        double a2 = fDesc.SampleMaxIn(az, el, 15.0, 15.0);
        double want = double.NegativeInfinity;
        foreach (var f in fieldsL) want = Math.Max(want, f.SampleMaxIn(az, el, 15.0, 15.0));
        double got = sampL.SampleMaxIn(az, el, 15.0, 15.0);
        probes++;
        if (Math.Abs(a1 - a2) > 0.1 && !double.IsNegativeInfinity(a1) && !double.IsNegativeInfinity(a2)) differ++;
        bool same = double.IsNegativeInfinity(want) ? double.IsNegativeInfinity(got)
                                                    : Math.Abs(got - want) < 1e-9;
        if (!same) { okL3 = false; detL3 = $"az={az} el={el}: got={got} want={want}"; }
    }
    Check("L3 envelope == max over pass-heading fields across the row band; headings differ", okL3 && differ > 0,
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

        // T4: expectation CDFs exist for every valid direction, parse, and are
        // monotone non-increasing in percent-exceeded. BL-D2 is the invalid-
        // filing probe (design brief Sec. 3.8): its expectation is the rejection
        // record and no CDF, asserted below the loop.
        bool okT4 = true; string detT4 = "";
        var expT4 = new (string Case, string File)[]
        {
            ("BL-D1", "epfd_down_cdf.csv"),
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
        if (okT4)
        {
            string expD2 = Path.Combine(outDs, "BL-D2", "expected");
            string rejPath = Path.Combine(expD2, "rejection.md");
            string rej = File.Exists(rejPath) ? File.ReadAllText(rejPath) : "";
            bool rejOk = rej.Contains("min_elev / elev_angle") && rej.Contains("max_co_freq (array)")
                && rej.Contains("INVALID operating-parameter set: filed in both header and array form")
                && !Directory.EnumerateFiles(expD2, "*.csv").Any();
            if (!rejOk) { okT4 = false; detT4 = "BL-D2 rejection record " + (File.Exists(rejPath) ? "incomplete or a CDF is present" : "missing"); }
            else detT4 += "BL-D2:rejection";
        }
        Check("T4 expectation records: CDFs present for every valid direction, parse, monotone; BL-D2 the rejection and no CDF", okT4, detT4.Trim());

        // T5: the section 3.9 read-rule probes emit their records (with the limit
        // row, the artefact identities and a provenance stamp) and their
        // examination tables. Quick profile: structure, not delivery numbers.
        bool okT5 = true; string detT5 = "";
        int CsvRows(string path) => File.Exists(path)
            ? File.ReadAllLines(path).Count(l => l.Length > 0 && l[0] != '#' && (char.IsDigit(l[0]) || l[0] == '-'))
            : -1;
        foreach (var (c, f, phrase) in new[]
        {
            ("BL-R1", "read-rule-probe.md", "NEAREST-ROW read of MIN_ELEV"),
            ("BL-R2", "read-rule-probe.md", "LINEAR INTERPOLATION"),
            ("BL-R3", "sweep-grid-probe.md", "worst margin depends on the sweep grid"),
        })
        {
            string p5 = Path.Combine(outDs, c, "expected", f);
            string txt5 = File.Exists(p5) ? File.ReadAllText(p5) : "";
            bool ok5 = txt5.Contains(phrase) && txt5.Contains("SHA-256") && txt5.Contains("Provenance:")
                && txt5.Contains("TABLE 22-1C") && txt5.Contains("QUICK profile");
            if (!ok5) { okT5 = false; detT5 = c + "/" + f + (File.Exists(p5) ? " incomplete" : " missing"); break; }
            detT5 += c + " ";
        }
        if (okT5)
        {
            string ex5 = Path.Combine(outDs, "BL-R1", "expected");
            bool r1csv = CsvRows(Path.Combine(ex5, "examination_lat25_cdf.csv")) >= 3 && CsvRows(Path.Combine(ex5, "examination_lat35_cdf.csv")) >= 3;
            string ex25 = Path.Combine(outDs, "BL-R2", "expected");
            bool r2csv = new[] { 25, 30, 35 }.All(l => CsvRows(Path.Combine(ex25, $"examination_lat{l}_cdf.csv")) >= 3);
            int sweepRows = CsvRows(Path.Combine(outDs, "BL-R3", "expected", "sweep_margins.csv"));
            bool r3csv = sweepRows == 141;
            if (!(r1csv && r2csv && r3csv)) { okT5 = false; detT5 = $"csv r1={r1csv} r2={r2csv} sweepRows={sweepRows}"; }
            else detT5 += "csv ok, sweep rows 141";
        }
        Check("T5 section 3.9 probe cases: records carry the limit row, the artefact identities and provenance; examination CDFs at the victims; the 141-latitude sweep table", okT5, detT5.Trim());

        // T6: the section 3.10 consistency probe emits its record (grade vocabulary,
        // the expected grade, the control's limit, the limit row, identities,
        // provenance) and its tables. Quick profile: structure, not delivery numbers.
        {
            string p6 = Path.Combine(outDs, "BL-C1", "expected", "consistency-probe.md");
            string txt6 = File.Exists(p6) ? File.ReadAllText(p6) : "";
            bool rec6 = txt6.Contains("Expected grade") && txt6.Contains("Grade vocabulary") && txt6.Contains("SATURATED (no shaping)") && txt6.Contains("lit reach")
                && txt6.Contains("LIT INSIDE THE DECLARED GATE") && txt6.Contains("MASK TIGHTER THAN DECLARED") && txt6.Contains("NOT EXERCISED")
                && txt6.Contains("The conservative verdict") && txt6.Contains("What the control can and cannot show")
                && txt6.Contains("4A/937") && txt6.Contains("TABLE 22-1B") && txt6.Contains("SHA-256") && txt6.Contains("Provenance:") && txt6.Contains("QUICK profile");
            int sweep6 = CsvRows(Path.Combine(outDs, "BL-C1", "expected", "sweep_margins.csv"));
            bool cdf6 = CsvRows(Path.Combine(outDs, "BL-C1", "expected", "examination_lat40_cdf.csv")) >= 3;
            Check("T6 section 3.10 consistency probe: the record names the grade vocabulary and the expected grade, the control's limit, the limit row, identities and provenance; seven-victim table; the CDF at 40 N",
                rec6 && sweep6 == 7 && cdf6, $"record={rec6} sweepRows={sweep6} cdf={cdf6}" + (File.Exists(p6) ? "" : " (record missing)"));
        }

        // T7: every case is stamped (expected/provenance.md lists its artefacts by
        // SHA-256, and the hashes are the files'); the truth cases carry
        // expected/curves.md with the extension pair; BL-I1 carries the
        // examination-read curve with the direction check; the track-duration
        // cases say why they carry none.
        {
            bool stamps7 = true; string det7 = "";
            foreach (string c in radians.beamlab.dataset.DatasetGenerator.CaseNames)
            {
                string stamp = Path.Combine(outDs, c, "expected", "provenance.md");
                string txt = File.Exists(stamp) ? File.ReadAllText(stamp) : "";
                int ntc7 = radians.beamlab.dataset.DatasetGenerator.NtcIdFor(c);
                if (!(txt.Contains("SHA-256") && txt.Contains($"ntc_id {ntc7}") && txt.Contains("## The notice") && txt.Contains("## The masks") && txt.Contains("## The expectation records") && txt.Contains("Emission:")))
                { stamps7 = false; det7 = c + " stamp " + (File.Exists(stamp) ? "incomplete" : "missing"); break; }
            }
            bool hashes7 = false;
            if (stamps7)
            {
                string mask2 = Path.Combine(outDs, "BL-I1", "xml", "mask2_pfd_azel_shellA.xml");
                string srs7 = Path.Combine(outDs, "BL-I1", "900123475 SRS.MDB");
                string txt = File.ReadAllText(Path.Combine(outDs, "BL-I1", "expected", "provenance.md"));
                hashes7 = txt.Contains(radians.beamlab.dataset.Provenance.Sha256Hex(mask2)) && txt.Contains(radians.beamlab.dataset.Provenance.Sha256Hex(srs7))
                    && txt.Contains("epfd_down_examination_cdf.csv") && txt.Contains("curves.md");
            }
            bool curves7 = new[] { "BL-D1", "BL-U1", "BL-U2", "BL-I1", "BL-ALL" }.All(c =>
            {
                string p = Path.Combine(outDs, c, "expected", "curves.md");
                return File.Exists(p) && File.ReadAllText(p).Contains("EXTENSION pair");
            });
            string i1 = Path.Combine(outDs, "BL-I1", "expected", "curves.md");
            string i1txt = File.Exists(i1) ? File.ReadAllText(i1) : "";
            bool exam7 = i1txt.Contains("Direction check:") && i1txt.Contains("gap E - T")
                && CsvRows(Path.Combine(outDs, "BL-I1", "expected", "epfd_down_examination_cdf.csv")) >= 3;
            string d1 = Path.Combine(outDs, "BL-D1", "expected", "curves.md");
            bool track7 = File.Exists(d1) && File.ReadAllText(d1).Contains("track-duration algorithm")
                && !File.Exists(Path.Combine(outDs, "BL-D1", "expected", "epfd_down_examination_cdf.csv"));
            Check("T7 stamps and curves: every case stamped with its artefacts' SHA-256 (hashes verified on BL-I1); truth cases carry the extension pair; BL-I1 carries the examination-read curve with the direction check; track-duration cases carry none and say why",
                stamps7 && hashes7 && curves7 && exam7 && track7,
                $"stamps={stamps7} hashes={hashes7} curves={curves7} exam={exam7} track={track7} {det7}".Trim());
        }
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
    var vmO = new OrbitDesignViewModel();   // defaults: 1200 km, i 53, e 0; fixed mode
    var expO = OrbitDesign.RepeatSolutions(1200.0, 0.0, 53.0, 120, take: 10);
    // Fixed mode auto-declares the nearest pair as the checked top row.
    bool autoOk = vmO.CheckOrbitsText == expO[0].Orbits.ToString()
        && vmO.CheckDaysText == expO[0].NodalDays.ToString()
        && vmO.Solutions.Count == expO.Count
        && vmO.Solutions[0].IsUserEntry
        && Math.Abs(vmO.Solutions[0].Solution.AltitudeKm - expO[0].AltitudeKm) < 1e-6
        && ReferenceEquals(vmO.SelectedSolution, vmO.Solutions[0]);
    // Adjusting mode with a cleared pair is the pure scan.
    vmO.DeclareAtTargetAltitude = false;
    vmO.CheckOrbitsText = ""; vmO.CheckDaysText = "";
    bool rowsOk = vmO.Solutions.Count == expO.Count && vmO.Solutions.Count > 0
        && vmO.Solutions[0].Solution == expO[0]
        && vmO.SelectedSolution == vmO.Solutions[0];

    bool textsOk = vmO.Case2Text.Contains("rpt_prd_dd=") && vmO.KeepRangeValid
        && vmO.Case1Text.Contains("2*S_pass - S_grid")
        && vmO.Case3Text.Contains("f_precess='Y'")
        && vmO.KeepRangeHintText.Contains("max ")
        && vmO.BuildCopyText().Contains("[Case 2 station-kept repeating]");

    vmO.KeepRangeDeg = vmO.SelectedSolution!.Solution.MaxKeepRangeDeg + 1.0;
    bool invalidCaught = !vmO.KeepRangeValid
        && vmO.KeepRangeHintText.Contains("out of bounds");
    vmO.KeepRangeDeg = 0.5;
    bool validAgain = vmO.KeepRangeValid;

    // Proof by flight (adjusting mode): the exact altitude closes the track.
    bool trackOk = vmO.TrackSegments.Count > 0
        && vmO.TrackSegments.Sum(seg => seg.Count) > vmO.SelectedSolution.Solution.Orbits * 100
        && vmO.TrackClosureDeg < 0.05;
    // Back in fixed mode (the pair re-fills from the nearest) the closure
    // gap IS the free-flight drift the station keeping absorbs.
    vmO.DeclareAtTargetAltitude = true;
    bool trackDriftOk = Math.Abs(vmO.TrackClosureDeg
        - Math.Abs(vmO.SelectedSolution.Solution.DriftDegPerCycleAtTarget)) < 0.02;

    // Whole-shell overlay building block: every satellite contributes.
    int soloSegs = vmO.TrackSegments.Count;
    vmO.PlaneCount = 2; vmO.SatsPerPlane = 2;
    bool shellTrackOk = vmO.BuildShellTrackSegments(150000).Count >= 2 * soloSegs;

    Check("V5 Orbit Design view model: rows, previews, keep_rnge validation, track closure",
        autoOk && rowsOk && textsOk && invalidCaught && validAgain && trackOk && trackDriftOk
        && shellTrackOk,
        $"rows={vmO.Solutions.Count} closure={vmO.TrackClosureDeg:F4} " +
        $"invalidCaught={invalidCaught} texts={textsOk} auto={autoOk} driftGap={trackDriftOk} " +
        $"shell={shellTrackOk}");
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
    bool okV6 = vmH.PipelineCards.Count == 6
        && vmH.PipelineCards[0].TabIndex == 4
        && vmH.PipelineCards.Skip(1).Select(c => c.Key)
            .SequenceEqual(new[] { "profile", "simulation", "compliance", "opparams", "builder" })
        && vmH.OtherCards.Select(c => c.TabIndex).SequenceEqual(new[] { 1, 2, 3 })
        && vmH.PipelineCards.Concat(vmH.OtherCards)
            .All(c => c.Title.Length > 0 && c.Description.Length > 40)
        && vmH.PipelineTitle.Contains("pipeline")
        && vmH.UserGuidePath is not null && File.Exists(vmH.UserGuidePath)
        && vmH.ParameterCardsPath is not null && File.Exists(vmH.ParameterCardsPath)
        && vmH.OrbitCasesPath is not null && File.Exists(vmH.OrbitCasesPath)
        && vmH.RepeatSolverPath is not null && File.Exists(vmH.RepeatSolverPath)
        && vmH.VersionText.StartsWith("v1.");
    Check("V6 Home view model: pipeline + other cards in flow order, docs resolved, version", okV6,
        $"pipeline={vmH.PipelineCards.Count} other={vmH.OtherCards.Count} " +
        $"guide={vmH.UserGuidePath is not null} cards={vmH.ParameterCardsPath is not null} ver={vmH.VersionText}");
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
    bool okV8 = ParameterCatalog.All.Count == 43
        && ParameterCatalog.All.Count(e => e.Group == ParameterGroup.Declared) == 11
        && ParameterCatalog.All.Count(e => e.Group == ParameterGroup.Truth) == 28
        && ParameterCatalog.All.Count(e => e.Group == ParameterGroup.Orbit) == 4
        && missing.Count == 0
        && ParameterCatalog.Find("MIN_EXCLUDE") is { } me && me.ToolTipText.Contains("- ");
    Check("V8 parameter catalog: 43 entries locked verbatim to the card deck", okV8,
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
        && vmC.OrbitRows[0].RepeatPeriod == sol.RptPrdAtTarget
        && vmC.OrbitRows[0].RptPrdText.Contains("d ")
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
        && shellB.RepeatPeriod == vmS.SelectedSolution!.Solution.RptPrdAtTarget
        && Math.Abs(shellB.AltitudeKm - vmS.TargetAltitudeKm) < 1e-9;

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
        && vmV.CheckStatusText.Contains("declared at")
        && vmV.CheckStatusText.Contains("closes by itself at")
        && vmV.Case2Text.Contains("rpt_prd_dd=");
    vmV.CheckOrbitsText = (refW.Orbits * 2).ToString();
    vmV.CheckDaysText = (refW.NodalDays * 2).ToString();
    bool vmReduceOk = vmV.CheckStatusText.Contains("reduces to")
        && vmV.Solutions[0].Orbits == refW.Orbits;
    vmV.DeclareAtTargetAltitude = false;   // fixed mode would re-fill an empty pair
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
    bool fileOk = d12.SchemaVersion == 6 && d12.PrecessionDegPerSec == -2.5e-5
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

    // The constellation-track overlay spans every shell of the document.
    doc.ShowConstellationTrack = true;
    int ov1 = doc.OverlaySegments.Count;
    doc.AddShell();
    bool overlayOk = ov1 > 0 && doc.OverlaySegments.Count > ov1;
    doc.ShowConstellationTrack = false;
    bool overlayOffOk = doc.OverlaySegments.Count == 0;

    Check("V13 multi-shell document: independence, combined preview, round-trip, v3 load",
        startOk && independentOk && combinedOk && selectedOnlyOk && roundOk && v3Ok && dupOk && removeOk
        && overlayOk && overlayOffOk,
        $"start={startOk} indep={independentOk} comb={combinedOk} sel={selectedOnlyOk} " +
        $"round={roundOk} v3={v3Ok} dup={dupOk} rm={removeOk} overlay={overlayOk}");
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

// ---- V15: the operating-parameters designer ----
{
    var vmR = new OpParamsViewModel
    {
        SatName = "V15SAT", NtcIdText = "900555003", ParamIdText = "21",
        LowFreqText = "19700", HighFreqText = "20200",
        EsDensityText = "0.01", EsDistanceText = "10",
        EsLatMinText = "-70", EsLatMaxText = "70",
        MaxCoFreqSatText = "8", MinAngleAtSatText = "5",
        MinExcludeText = "0 -70 3\n0 0 5\n0 70 3",
        MinElevText = "0 0 10\n0 180 12\n45 0 15",
    };
    var pSet = vmR.BuildSet();
    bool parseOk = pSet.MinExclude.Count == 1 && pSet.MinExclude[0].ByLat.Count == 3
        && pSet.MinElev.Count == 2 && pSet.MinElev[0].ByAz.Count == 2
        && pSet.MaxCoFreqSat == 8 && pSet.EsDensityPerKm2 == 0.01
        && vmR.SummaryText.Contains("param_id 21");

    // File round-trip and XML identity: set -> json -> set writes the
    // same bytes the writer produces from the original.
    var pBack = OpParamsFileCodec.ToSet(OpParamsFileCodec.Load(
        OpParamsFileCodec.Save(OpParamsFileCodec.FromSet(pSet))));
    string expDir = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(expDir);
    string x1 = Path.Combine(expDir, "v15a.xml"), x2 = Path.Combine(expDir, "v15b.xml");
    string x3 = Path.Combine(expDir, "v15c.xml");
    OperParamsXmlWriter.Write(x1, pSet);
    OperParamsXmlWriter.Write(x2, pBack);
    vmR.ExportXml(x3);
    string t1 = File.ReadAllText(x1);
    bool xmlOk = t1 == File.ReadAllText(x2) && t1 == File.ReadAllText(x3)
        && t1.Contains("min_exclude") && t1.Contains("max_co_freq_sat=\"8\"");

    // VM load repopulates the texts to the same set.
    var vmR2 = new OpParamsViewModel();
    vmR2.LoadJson(vmR.BuildJson());
    bool loadOk = vmR2.BuildJson() == vmR.BuildJson();

    // Writer encoding rules surface through export: min_duration 0 rejected.
    bool guardOk = false;
    vmR.MinDurationText = "0 0";
    try { vmR.ExportXml(Path.Combine(expDir, "v15d.xml")); }
    catch (ArgumentException) { guardOk = true; }

    // Parse errors are line-precise and land in StatusText.
    vmR.MinDurationText = "";
    vmR.MinElevText = "0 nonsense 10";
    bool errOk = vmR.StatusText.Contains("min_elev line 1");

    Check("V15 operating-parameters designer: parse, json/xml identity, guards",
        parseOk && xmlOk && loadOk && guardOk && errOk,
        $"parse={parseOk} xml={xmlOk} load={loadOk} guard={guardOk} err={errOk}");
}

// ---- V16: the simulation runner -- validation plus a real tiny run ----
{
    var doc16 = new OrbitDesignDocumentViewModel();
    doc16.AddShell();
    string p16 = Path.Combine(AppContext.BaseDirectory, "exp", "v16.orbitdesign.json");
    Directory.CreateDirectory(Path.GetDirectoryName(p16)!);
    File.WriteAllText(p16, doc16.BuildDocumentJson());

    // The operation profile is mandatory: the system side comes from it
    // (a coarse 900 km grid keeps the run tiny).
    string prof16 = Path.Combine(AppContext.BaseDirectory, "exp", "v16.opprofile.json");
    File.WriteAllText(prof16, OperationProfileCodec.Save(
        new OperationProfile(Name: "v16", CellKm: 900.0)));

    var sim = new SimulationViewModel { DesignPath = p16 };
    sim.ValidateInputs();
    bool noProfOk = sim.StatusText.StartsWith("invalid:")
        && sim.StatusText.Contains("operation profile");
    sim.ProfilePath = prof16;
    sim.ValidateInputs();
    bool goodOk = sim.StatusText.StartsWith("ready:") && sim.StatusText.Contains("2 shell(s)")
        && sim.RunEnabled;
    sim.DurationDaysText = "junk";
    sim.ValidateInputs();
    bool badNumOk = sim.StatusText.StartsWith("invalid:");
    sim.DurationDaysText = "2";
    sim.DesignPath = Path.Combine(AppContext.BaseDirectory, "exp", "missing.json");
    sim.ValidateInputs();
    bool badPathOk = sim.StatusText.StartsWith("invalid:");

    // A real (tiny) run: a 1x2 shell, 20 minutes at 60 s on the coarse
    // grid; the three CDFs land on disk with monotone percent columns.
    var docT = new OrbitDesignDocumentViewModel();
    docT.Shells[0].PlaneCount = 1; docT.Shells[0].SatsPerPlane = 2;
    string pT = Path.Combine(AppContext.BaseDirectory, "exp", "v16tiny.orbitdesign.json");
    File.WriteAllText(pT, docT.BuildDocumentJson());
    sim.DesignPath = pT;
    sim.DurationDaysText = (20.0 / 1440.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
    sim.StepSecText = "60";
    string base16 = Path.Combine(AppContext.BaseDirectory, "exp", "v16sim");
    string sum16 = sim.RunCore(sim.BuildSetup(), base16);
    bool ranOk = sum16.StartsWith("done:");
    bool filesOk = File.Exists(base16 + ".down.csv") && File.Exists(base16 + ".is.csv")
        && File.Exists(base16 + ".up.csv");
    var body16 = File.ReadAllLines(base16 + ".down.csv")
        .SkipWhile(l => l.StartsWith("#")).Skip(1)
        .Select(l => double.Parse(l.Split(',')[1], System.Globalization.CultureInfo.InvariantCulture))
        .ToArray();
    bool cdfOk = body16.Length > 0
        && body16.Zip(body16.Skip(1), (a, b) => a >= b).All(x => x);

    Check("V16 simulation runner: mandatory profile, validation, tiny run, three monotone CDFs",
        noProfOk && goodOk && badNumOk && badPathOk && ranOk && filesOk && cdfOk,
        $"noProf={noProfOk} good={goodOk} badNum={badNumOk} badPath={badPathOk} ran={ranOk} files={filesOk} cdf={cdfOk}");
}

// ---- V17: Case-2 declaration at the operator's own altitude ----
{
    var vmT = new OrbitDesignViewModel();     // DeclareAtTargetAltitude defaults true
    var st = vmT.SelectedSolution!.Solution;  // 13/1 near 1205 km
    var (_, tnT) = OrbitDesign.NodalPassGeometry(
        OrbitalConstants.EarthRadiusKm + vmT.TargetAltitudeKm, 0.0, 53.0);
    bool secOk = Math.Abs(st.RepeatSecondsAtTarget - st.Orbits * tnT) < 1e-6
        && st.RptPrdAtTarget == OrbitDesign.DecomposePeriod(st.Orbits * tnT)
        && st.RptPrdAtTarget != st.RptPrd;

    vmT.CaseChoice = 1;
    var shT = vmT.BuildShell();
    bool defOk = Math.Abs(shT.AltitudeKm - vmT.TargetAltitudeKm) < 1e-12
        && shT.RepeatPeriod == st.RptPrdAtTarget
        && vmT.Case2Text.Contains("absorbs")
        && !vmT.AdjustAltitudeChoice;

    vmT.DeclareAtTargetAltitude = false;
    var shE = vmT.BuildShell();
    bool exactOk = Math.Abs(shE.AltitudeKm - st.AltitudeKm) < 1e-12
        && shE.RepeatPeriod == st.RptPrd
        && vmT.Case2Text.Contains("zero correction")
        && vmT.AdjustAltitudeChoice;

    vmT.DeclareAtTargetAltitude = true;
    string jT = vmT.BuildDesignJson();
    var dT = OrbitDesignFileCodec.Load(jT);
    var shF = OrbitDesignFileCodec.ToShell(dT);
    bool fileOk17 = dT.SchemaVersion == 6 && dT.DeclareAtTargetAltitude
        && dT.RptDays == st.RptPrdAtTarget.Days && dT.RptSeconds == st.RptPrdAtTarget.Seconds
        && Math.Abs(shF.AltitudeKm - vmT.TargetAltitudeKm) < 1e-12
        && shF.RepeatPeriod == st.RptPrdAtTarget;

    // A legacy file (flag absent = false, exact rpt stored) reproduces the
    // exact-altitude declaration unchanged.
    var shOld = OrbitDesignFileCodec.ToShell(dT with
    {
        DeclareAtTargetAltitude = false,
        RptDays = st.RptPrd.Days, RptHours = st.RptPrd.Hours,
        RptMinutes = st.RptPrd.Minutes, RptSeconds = st.RptPrd.Seconds,
    });
    bool oldOk = Math.Abs(shOld.AltitudeKm - st.AltitudeKm) < 1e-12
        && shOld.RepeatPeriod == st.RptPrd;

    // Fixed mode auto-fills the nearest pair at start; picking a scan row
    // loads its pair, which rides to the top as the checked row.
    var vmU = new OrbitDesignViewModel();
    bool autoFillOk = vmU.CheckOrbitsText.Length > 0 && vmU.Solutions[0].IsUserEntry
        && ReferenceEquals(vmU.SelectedSolution, vmU.Solutions[0]);
    var pick = vmU.Solutions[1];
    vmU.SelectedSolution = pick;
    bool syncOk = vmU.CheckOrbitsText == pick.Orbits.ToString()
        && vmU.CheckDaysText == pick.NodalDays.ToString()
        && vmU.Solutions[0].IsUserEntry && vmU.Solutions[0].Orbits == pick.Orbits
        && ReferenceEquals(vmU.SelectedSolution, vmU.Solutions[0]);
    // Adjusting mode selects rows without promoting them.
    vmU.DeclareAtTargetAltitude = false;
    var pick2 = vmU.Solutions.First(r => !r.IsUserEntry);
    vmU.SelectedSolution = pick2;
    bool noSyncOk = ReferenceEquals(vmU.SelectedSolution, pick2)
        && vmU.CheckOrbitsText == pick.Orbits.ToString();

    Check("V17 Case-2 at-target declaration: default, rpt_prd@target, file, legacy, auto-fill",
        secOk && defOk && exactOk && fileOk17 && oldOk && autoFillOk && syncOk && noSyncOk,
        $"sec={secOk} def={defOk} exact={exactOk} file={fileOk17} old={oldOk} " +
        $"auto={autoFillOk} sync={syncOk} noSync={noSyncOk}");
}

// ---- V18: constellation repeat period (A2.4) and harmonization ----
{
    var docR = new OrbitDesignDocumentViewModel();
    bool singleOk = docR.ConstellationRepeatText.Contains("P_repeat")
        && docR.ConstellationRepeatText.Contains("1x shell 1");

    docR.AddShell();
    docR.SelectedShell.CaseChoice = 0;
    bool mixedOk = docR.ConstellationRepeatText.Contains("mix");
    docR.SelectedShell.CaseChoice = 1;
    docR.SelectedShell.TargetAltitudeKm = 800.0;
    long t1 = docR.Shells[0].DeclaredRptSeconds!.Value;
    long t2 = docR.Shells[1].DeclaredRptSeconds!.Value;
    bool distinctOk = t1 != t2;

    docR.HarmonizeRptPrd();
    long p18 = docR.Shells[0].HarmonizedRptSeconds!.Value;
    bool harmOk = p18 % t1 == 0 && p18 % t2 == 0
        && docR.Shells[0].BuildShell().RepeatPeriod == OrbitDesign.DecomposePeriod(p18)
        && docR.Shells[1].BuildShell().RepeatPeriod == OrbitDesign.DecomposePeriod(p18)
        && docR.ConstellationRepeatText.Contains("1x shell 2");

    // The harmonization persists through the document file.
    var docR2 = new OrbitDesignDocumentViewModel();
    docR2.LoadDocumentJson(docR.BuildDocumentJson());
    bool persistOk = docR2.Shells[1].HarmonizedRptSeconds == p18
        && docR2.Shells[1].BuildShell().RepeatPeriod == OrbitDesign.DecomposePeriod(p18)
        && docR2.BuildDocumentJson() == docR.BuildDocumentJson();

    // A new pair invalidates the override on that shell.
    docR.Shells[0].CheckDaysText = "2";
    bool clearOk = docR.Shells[0].HarmonizedRptSeconds is null;

    Check("V18 constellation repeat: P_repeat readout, mixed warning, harmonize, persistence",
        singleOk && mixedOk && distinctOk && harmOk && persistOk && clearOk,
        $"single={singleOk} mixed={mixedOk} distinct={distinctOk} harm={harmOk} " +
        $"persist={persistOk} clear={clearOk}");
}

// ---- V19: builder v2 link scopes and earth stations ----
{
    var vb = new OrbitDesignDocumentViewModel();
    vb.Shells[0].PlaneCount = 2; vb.Shells[0].SatsPerPlane = 3;
    string pb = Path.Combine(AppContext.BaseDirectory, "exp", "v19.orbitdesign.json");
    Directory.CreateDirectory(Path.GetDirectoryName(pb)!);
    File.WriteAllText(pb, vb.BuildDocumentJson());

    var b19 = new SnsBuilderViewModel { NtcId = 900555003, SatName = "V19SAT" };
    b19.AddShellFile(pb);
    b19.Masks.Add(new MaskEntry { MaskId = 1, FilePath = "a.xml", FMask = "P", FMaskType = "A",
        FreqMinMhz = 19700, FreqMaxMhz = 20200, LinkOrbIdText = "1" });
    b19.Masks.Add(new MaskEntry { MaskId = 2, FilePath = "b.xml", FMask = "S", FMaskType = "A",
        FreqMinMhz = 19700, FreqMaxMhz = 20200, LinkOrbIdText = "2", LinkSatIdText = "3" });
    b19.Masks.Add(new MaskEntry { MaskId = 6, FilePath = "c.xml", FMask = "E", FMaskType = "O",
        FreqMinMhz = 27500, FreqMaxMhz = 28600, LinkEsIdText = "5" });
    b19.EarthStations.Add(new EsEntry { EAsId = 5, StnName = "GATE-1", LatText = "45", LonText = "7", AntDiamText = "2.4" });
    b19.Frequencies.Add(new FreqEntry { EmiRcp = "E", FreqMinMhz = 19700, FreqMaxMhz = 20200 });

    var n19 = b19.BuildNotice();
    bool linkOk = n19.Scenarios[0].PfdMaskLinks.Count == 2
        && n19.Scenarios[0].PfdMaskLinks[0].OrbId == 1 && n19.Scenarios[0].PfdMaskLinks[0].SatOrbId is null
        && n19.Scenarios[0].PfdMaskLinks[1].OrbId == 2 && n19.Scenarios[0].PfdMaskLinks[1].SatOrbId == 3
        && n19.Scenarios[0].EsMaskLinks[0].EAsId == 5;
    bool esOk = n19.EarthStations.Count == 1 && n19.EarthStations[0].StnType == 'S'
        && n19.EarthStations[0].LatDeg == 45.0 && n19.EarthStations[0].AntDiamM == 2.4
        && b19.SummaryText().Contains("1 earth station(s)");

    // Guards: an orb link to a missing plane; an e_as link with no station.
    bool orbGuardOk = false;
    b19.Masks[0].LinkOrbIdText = "9";
    try { b19.BuildNotice(); } catch (InvalidOperationException) { orbGuardOk = true; }
    b19.Masks[0].LinkOrbIdText = "1";
    bool esGuardOk = false;
    b19.EarthStations.Clear();
    try { b19.BuildNotice(); } catch (InvalidOperationException) { esGuardOk = true; }

    Check("V19 builder link scopes: per-plane, per-satellite, specific ES, guards",
        linkOk && esOk && orbGuardOk && esGuardOk,
        $"link={linkOk} es={esOk} orbGuard={orbGuardOk} esGuard={esGuardOk}");
}

// ---- V20: shell names and document reordering ----
{
    var d20 = new OrbitDesignDocumentViewModel();
    d20.SelectedShell.ShellName = "ALPHA";
    d20.AddShell();
    d20.SelectedShell.ShellName = "BETA";
    d20.SelectedShell.TargetAltitudeKm = 800.0;
    bool nameOk = d20.Shells[0].ShellSummary.StartsWith("ALPHA")
        && d20.Shells[1].ShellSummary.StartsWith("BETA");

    // Reorder: BETA first; the combined notice renumbers with the order.
    double apogBefore = d20.BuildCombinedNotice().Orbits[0].ApogeeKm;
    d20.MoveSelectedUp();
    bool moveOk = ReferenceEquals(d20.Shells[0], d20.SelectedShell)
        && d20.ShellHeaderText == "editing shell 1 of 2"
        && Math.Abs(d20.BuildCombinedNotice().Orbits[0].ApogeeKm - apogBefore) > 1.0;

    // Names and order survive the document file.
    var d20b = new OrbitDesignDocumentViewModel();
    d20b.LoadDocumentJson(d20.BuildDocumentJson());
    var s20 = OrbitDesignFileCodec.Load(d20b.Shells[0].BuildDesignJson());
    bool persistOk = d20b.Shells[0].ShellName == "BETA" && d20b.Shells[1].ShellName == "ALPHA"
        && s20.SchemaVersion == 6 && s20.Summary.StartsWith("BETA");

    d20.MoveSelectedDown();
    bool backOk = d20.Shells[1].ShellName == "BETA";

    Check("V20 shell names and reordering: summary, orb order, persistence",
        nameOk && moveOk && persistOk && backOk,
        $"name={nameOk} move={moveOk} persist={persistOk} back={backOk}");
}

// ---- V21: operating parameters derived from the simulated system ----
{
    var docD = new OrbitDesignDocumentViewModel();
    docD.Shells[0].PlaneCount = 1; docD.Shells[0].SatsPerPlane = 2;
    string pD = Path.Combine(AppContext.BaseDirectory, "exp", "v21.orbitdesign.json");
    Directory.CreateDirectory(Path.GetDirectoryName(pD)!);
    File.WriteAllText(pD, docD.BuildDocumentJson());

    // The system under measurement comes from a profile (mandatory):
    // min elev 10, exclusion 8, coarse 900 km grid.
    string prof21 = Path.Combine(AppContext.BaseDirectory, "exp", "v21.opprofile.json");
    File.WriteAllText(prof21, OperationProfileCodec.Save(new OperationProfile(
        Name: "v21", MinElevDeg: 10.0, AlphaExclDeg: 8.0, CellKm: 900.0)));

    var vmD21 = new OpParamsViewModel
    {
        SatName = "V21SAT", NtcIdText = "900555004", ParamIdText = "31",
        LowFreqText = "19700", HighFreqText = "20200",
        DeriveDesignPath = pD, DeriveProfilePath = prof21,    };
    var r21 = vmD21.DeriveCore(90.0 * 60.0, 60.0, 15.0);
    var set21 = r21.Set;
    bool measuredOk = r21.LinkSamples > 0
        && set21.MinElev.Count > 0
        && set21.MinElev.All(me => me.ByAz.All(v => v.ElevDeg >= 10.0 - 1e-9))
        && set21.MaxCoFreqByLat.All(v => v.Value >= 1)
        && set21.MaxCoFreqSat >= 1
        && set21.EsLatMinDeg >= 29.0 && set21.EsLatMaxDeg <= 61.0;
    // The enforced exclusion floors any derived min_exclude ring.
    bool alphaOk = set21.MinExclude.Count == 0
        || set21.MinExclude[0].ByLat.All(v => v.AlphaDeg >= 8.0 - 1e-9);

    // The measured envelope fills the designer and exports as valid XML,
    // identical to writing the set directly.
    vmD21.ApplySet(set21);
    string x21 = Path.Combine(AppContext.BaseDirectory, "exp", "v21.xml");
    vmD21.ExportXml(x21);
    string x21b = Path.Combine(AppContext.BaseDirectory, "exp", "v21b.xml");
    OperParamsXmlWriter.Write(x21b, set21);
    string t21 = File.ReadAllText(x21);
    bool exportOk = t21 == File.ReadAllText(x21b) && t21.Contains("min_elev");

    Check("V21 derived operating parameters: measured envelope, designer fill, XML",
        measuredOk && alphaOk && exportOk,
        $"samples={r21.LinkSamples} measured={measuredOk} alpha={alphaOk} export={exportOk}");
}

// ---- V22: the operation profile -- codec, composition, profile-driven derive ----
{
    var prof = new OperationProfile(
        Name: "V22",
        Downlink: new DownlinkProfile(FrequencyGhz: 19.7, GainPeakDbi: 34.0, TxEirpDbw: 12.0,
            MinAngleAtSatDeg: 4.0),
        Uplink: new UplinkProfile(FrequencyGhz: 29.5, EsPowerDbw: 3.0, EsDishM: 1.2,
            MinAngleAtEsDeg: 9.0),
        MinElevDeg: 12.0,
        ServiceLatMinDeg: 30.0, ServiceLatMaxDeg: 60.0, CellKm: 900.0,
        TrackingPolicy: "MaxGsoSeparation",
        NcoPerCell: 2, MaxCoFreqSat: 6,
        DemandLinksPerCell: 2, ActivityFactor: 0.8, ActivityPeriodSec: 120.0,
        IlluminationDutyCycle: 0.5,
        AlphaExclDeg: 7.0,
        MinElevByLat: new[] { new ProfileLatRow(45.0, 15.0) });
    var prof2 = OperationProfileCodec.Load(OperationProfileCodec.Save(prof));
    bool codecOk = prof2.Down.FrequencyGhz == 19.7 && prof2.Down.GainPeakDbi == 34.0
        && prof2.Up.FrequencyGhz == 29.5 && prof2.Up.EsPowerDbw == 3.0 && prof2.Up.EsDishM == 1.2
        && prof2.TrackingPolicy == "MaxGsoSeparation" && prof2.NcoPerCell == 2
        && prof2.IlluminationDutyCycle == 0.5 && prof2.AlphaExclDeg == 7.0
        && prof2.MinElevByLat!.Count == 1 && prof2.MinElevByLat[0].Value == 15.0;

    var comp = OperationComposer.Compose(prof2, 1200.0);
    bool compOk = comp.Enforced.ElevAngleHeaderDeg == 12.0
        && comp.Enforced.MinElev.Count == 1
        && comp.Enforced.MinExclude.Count == 1
        && comp.Enforced.MinExclude[0].ByLat.All(v => v.AlphaDeg == 7.0)
        && comp.Enforced.MaxCoFreqHeader == 2 && comp.Enforced.MaxCoFreqSat == 6
        && comp.Scene.FrequencyGHz == 19.7 && comp.Scene.GmDbi == 34.0
        && comp.Scene.TxEirpDbw == 12.0 && comp.Scene.AlphaExclDeg == 7.0
        && comp.Policy == SelectionPolicy.MaxGsoSeparation
        && comp.IlluminationDutyCycle == 0.5
        && comp.Geography.Cells.All(c => c.DemandLinks == 2 && c.ActivityFactor == 0.8)
        // Per-direction link discipline: each composition carries its side's angles.
        && comp.Enforced.MinAngleAtSatDeg == 4.0 && comp.Enforced.MinAngleAtEsDeg is null
        && OperationComposer.Compose(prof2, 1200.0, LinkDirection.Up)
            .Enforced.MinAngleAtEsDeg == 9.0;

    bool shellsOk = OperationComposer.ApplyToShells(prof2 with { OperationalFraction = 0.5 },
        new[] { new ConstellationShell
            { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 2 } })
        [0].OperationalFraction == 0.5;

    // Profile-driven derivation end to end (the profile IS the system).
    var docE = new OrbitDesignDocumentViewModel();
    docE.Shells[0].PlaneCount = 1; docE.Shells[0].SatsPerPlane = 2;
    string pE = Path.Combine(AppContext.BaseDirectory, "exp", "v22.orbitdesign.json");
    Directory.CreateDirectory(Path.GetDirectoryName(pE)!);
    File.WriteAllText(pE, docE.BuildDocumentJson());
    var dprof = new OperationProfile(Name: "V22D", MinElevDeg: 10.0, AlphaExclDeg: 8.0, CellKm: 900.0);
    string profPath = Path.Combine(AppContext.BaseDirectory, "exp", "v22.opprofile.json");
    File.WriteAllText(profPath, OperationProfileCodec.Save(dprof));

    var vm22 = new OpParamsViewModel
    {
        SatName = "V22SAT", NtcIdText = "900555005", ParamIdText = "41",
        DeriveDesignPath = pE, DeriveProfilePath = profPath,    };
    var r22 = vm22.DeriveCore(90.0 * 60.0, 60.0, 15.0);
    bool deriveOk = r22.LinkSamples > 0
        && r22.Set.MinElev.All(me => me.ByAz.All(v => v.ElevDeg >= 10.0 - 1e-9))
        && (r22.Set.MinExclude.Count == 0
            || r22.Set.MinExclude[0].ByLat.All(v => v.AlphaDeg >= 8.0 - 1e-9));

    Check("V22 operation profile: codec, composition, shells, profile-driven derive",
        codecOk && compOk && shellsOk && deriveOk,
        $"codec={codecOk} comp={compOk} shells={shellsOk} derive={deriveOk} samples={r22.LinkSamples}");
}

// ---- V23: the compliance sweep -- per-latitude verdicts and margins ----
{
    var docC = new OrbitDesignDocumentViewModel();
    docC.Shells[0].PlaneCount = 1; docC.Shells[0].SatsPerPlane = 2;
    string pC = Path.Combine(AppContext.BaseDirectory, "exp", "v23.orbitdesign.json");
    Directory.CreateDirectory(Path.GetDirectoryName(pC)!);
    File.WriteAllText(pC, docC.BuildDocumentJson());
    var profC = new OperationProfile(Name: "V23", MinElevDeg: 10.0, CellKm: 900.0);
    string ppC = Path.Combine(AppContext.BaseDirectory, "exp", "v23.opprofile.json");
    File.WriteAllText(ppC, OperationProfileCodec.Save(profC));

    var cvm = new ComplianceViewModel
    {
        DesignPath = pC, ProfilePath = ppC,
        LatFromText = "40", LatToText = "50", LatStepText = "10",
        DurationDaysText = (60.0 / 1440.0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        StepSecText = "60",
    };
    // Verdict-permissive limit: everything passes, margins non-negative.
    var sweepP = cvm.BuildSweep();
    var rowsP = ComplianceViewModel.RunSweep(sweepP, 0.0);
    bool passOk = rowsP.Count == 2 && rowsP.All(r => r.Pass)
        && rowsP.All(r => r.WorstMarginDb >= 0.0);

    // Impossible limit: any epfd at all fails it, with a negative margin.
    cvm.LimitsText = "-300 0.001\n-250 0.002";
    var sweepF = cvm.BuildSweep();
    var rowsF = ComplianceViewModel.RunSweep(sweepF, 0.0);
    bool failOk = rowsF.Count == 2 && rowsF.All(r => !r.Pass)
        && rowsF.All(r => r.WorstMarginDb < 0.0);
    bool summaryOk = ComplianceViewModel.SummarizeRows(rowsP).StartsWith("COMPLIANT")
        && ComplianceViewModel.SummarizeRows(rowsF).StartsWith("EXCEEDED");

    Check("V23 compliance sweep: verdicts and margins across the latitude grid",
        passOk && failOk && summaryOk,
        $"pass={passOk} fail={failOk} summary={summaryOk} " +
        $"marginP={rowsP[0].WorstMarginDb:F1} marginF={rowsF[0].WorstMarginDb:F1}");

    // ---- V24: the exclusion advisor terminates both ways ----
    var adviceP = ComplianceViewModel.Advise(sweepP, 1.0, 5.0);
    bool foundOk = adviceP.FoundAlpha == 0.0 && adviceP.Iterations == 1
        && adviceP.FailingAtStart.Count == 0;
    var adviceF = ComplianceViewModel.Advise(sweepF, 2.0, 4.0);
    bool cappedOk = adviceF.FoundAlpha is null && adviceF.Iterations == 3
        && adviceF.FailingAtStart.Count == 2;
    Check("V24 exclusion advisor: immediate pass, capped walk",
        foundOk && cappedOk,
        $"found={foundOk} capped={cappedOk} itP={adviceP.Iterations} itF={adviceF.Iterations}");
}

// ---- V25: MIN_DURATION admission -- only sustainable links are made ----
{
    var shells25 = new[] { new ConstellationShell
        { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 2 } };
    var con25 = new Constellation(shells25);
    var geo25 = ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, 900.0);
    var scene25 = new PfdMaskViewModel
        { AltitudeKm = 1200.0, FrequencyGHz = 19.7, MinElevDeg = 10.0, RefBwKHz = 40.0 };
    double dur25 = 5400.0;
    const int hold25 = 900;
    var op0 = new OperatingParamsSet
        { SatName = "T", LowFreqMhz = 19700, HighFreqMhz = 19700, ElevAngleHeaderDeg = 10.0 };
    var opD = new OperatingParamsSet
        { SatName = "T", LowFreqMhz = 19700, HighFreqMhz = 19700, ElevAngleHeaderDeg = 10.0,
          MinDurationSecHeader = hold25 };
    var s0 = new Scheduler(con25, geo25, op0, new ScenePointing(scene25), dur25);
    var sD = new Scheduler(con25, geo25, opD, new ScenePointing(scene25), dur25);

    var cellPos25 = geo25.Cells.ToDictionary(c => c.CellId, c =>
    {
        double la = c.LatDeg * Math.PI / 180.0, lo = c.LonDeg * Math.PI / 180.0;
        double r = OrbitalConstants.EarthRadiusKm;
        return new Vec3(r * Math.Cos(la) * Math.Cos(lo), r * Math.Cos(la) * Math.Sin(lo), r * Math.Sin(la));
    });
    var idxByNum25 = new Dictionary<int, int>();
    for (int i = 0; i < con25.SatelliteCount; i++)
        idxByNum25[con25.StateAt(i, 0.0, dur25).SatelliteNumber] = i;

    long links0 = 0, linksD = 0;
    bool sustainOk = true;
    for (double t = 0.0; t < dur25; t += 60.0)
    {
        links0 += s0.Step(t).Links.Count;
        var b = sD.Step(t);
        linksD += b.Links.Count;
        // Every FRESH link must still clear the elevation floor after the
        // hold (the admission's own look-ahead, recomputed independently).
        foreach (var l in b.Links.Where(l => l.StartTimeSec == t))
        {
            double tEnd = Math.Min(t + hold25, dur25);
            var st = con25.StateAt(idxByNum25[l.SatelliteNumber], tEnd, dur25);
            if (GeoMath.ElevationAngleDeg(st.PositionEcefKm, cellPos25[l.CellId]) < 10.0 - 1e-6)
                sustainOk = false;
        }
    }
    bool filterOk = linksD < links0;   // near pass ends the admission must bite

    Check("V25 MIN_DURATION admission: fresh links sustainable, filter bites",
        sustainOk && filterOk && links0 > 0,
        $"links0={links0} linksD={linksD} sustain={sustainOk}");
}

// ---- V26: per-cell Nco cap and the handover policies' effects ----
{
    var shells26 = new[] { new ConstellationShell
        { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 2, SatsPerPlane = 3 } };
    var con26 = new Constellation(shells26);
    var scene26 = new PfdMaskViewModel
        { AltitudeKm = 1200.0, FrequencyGHz = 19.7, MinElevDeg = 10.0, RefBwKHz = 40.0 };
    double dur26 = 7200.0;

    // (a) MAX_CO_FREQ per cell caps the served links below the demand.
    var cells26 = ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, 900.0).Cells
        .Select(c => c with { DemandLinks = 2 }).ToList();
    var geoD2 = new ServiceGeography(cells26, 900.0);
    int MaxPerCell(OperatingParamsSet ops)
    {
        var s = new Scheduler(con26, geoD2, ops, new ScenePointing(scene26), dur26);
        int worst = 0;
        for (double t = 0.0; t < dur26; t += 60.0)
            foreach (var g in s.Step(t).Links.GroupBy(l => l.CellId))
                worst = Math.Max(worst, g.Count());
        return worst;
    }
    static OperatingParamsSet Ops26(int? nco = null, int? holdSec = null) => new()
    {
        SatName = "T", LowFreqMhz = 19700, HighFreqMhz = 19700, ElevAngleHeaderDeg = 10.0,
        MaxCoFreqHeader = nco, MinDurationSecHeader = holdSec,
    };
    int cap1 = MaxPerCell(Ops26(nco: 1));
    int cap2 = MaxPerCell(Ops26(nco: 2));
    bool ncoOk = cap1 == 1 && cap2 == 2;

    // (b, c) Policies. Sparse constellations never see a better satellite
    // while one serves (probed: zero opportunities), so build the flip
    // deliberately: two satellites chase each other 8 deg apart in ONE
    // plane -- mid-pass the trailer overtakes the leader in elevation
    // while both stay feasible. Free election must switch; hold-until-
    // forced must not; a long dwell suppresses the switch too.
    var conPol = new Constellation(new[]
    {
        new ConstellationShell { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1 },
        new ConstellationShell { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1, InPlaneOffsetDeg = 8.0 },
    });
    double durPol = 3600.0;
    var geoD1 = ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, 900.0);
    (long Vol, long Links) Voluntary(OperatingParamsSet ops, SelectionPolicy pol)
    {
        var s = new Scheduler(conPol, geoD1, ops, new ScenePointing(scene26), durPol, null, pol);
        long v = 0, k = 0;
        for (double t = 0.0; t < durPol; t += 60.0)
        {
            var st = s.Step(t);
            v += st.VoluntaryHandovers;
            k += st.Links.Count;
        }
        return (v, k);
    }
    var free = Voluntary(Ops26(), SelectionPolicy.HighestElevation);
    var huf = Voluntary(Ops26(), SelectionPolicy.HoldUntilForced);
    var dwell = Voluntary(Ops26(holdSec: 900), SelectionPolicy.HighestElevation);
    bool policyOk = free.Vol > 0 && huf.Vol == 0 && huf.Links > 0 && dwell.Vol <= free.Vol;

    Check("V26 Nco per cell and handover policies: cap, hold-until-forced, dwell",
        ncoOk && policyOk,
        $"cap1={cap1} cap2={cap2} volFree={free.Vol} volHuf={huf.Vol} " +
        $"volDwell={dwell.Vol} linksHuf={huf.Links}");
}

// ---- V27: play session -- candidates exposed, active links a subset ----
{
    var doc27 = new OrbitDesignDocumentViewModel();
    doc27.Shells[0].PlaneCount = 1; doc27.Shells[0].SatsPerPlane = 2;
    string p27 = Path.Combine(AppContext.BaseDirectory, "exp", "v27.orbitdesign.json");
    Directory.CreateDirectory(Path.GetDirectoryName(p27)!);
    File.WriteAllText(p27, doc27.BuildDocumentJson());

    string prof27 = Path.Combine(AppContext.BaseDirectory, "exp", "v27.opprofile.json");
    File.WriteAllText(prof27, OperationProfileCodec.Save(
        new OperationProfile(Name: "v27", CellKm: 900.0)));
    var sim27 = new SimulationViewModel
    {
        DesignPath = p27, ProfilePath = prof27,
        DurationDaysText = (30.0 / 1440.0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        StepSecText = "60",
    };
    var ps27 = sim27.BuildPlaySession();
    bool sessionOk = ps27.SatCount == 2 && ps27.StepSec == 60.0
        && ps27.Geo.Cells.Count > 0;

    long cand27 = 0, act27 = 0;
    bool subsetOk = true;
    for (double t = 0.0; t < ps27.DurationSec; t += ps27.StepSec)
    {
        var st = ps27.Scheduler.Step(t);
        cand27 += st.CandidateLinks.Count;
        act27 += st.Links.Count;
        foreach (var l in st.Links)
            if (!st.CandidateLinks.Any(c => c.CellId == l.CellId
                && c.SatelliteNumber == l.SatelliteNumber))
                subsetOk = false;
    }
    bool linkOk = act27 > 0 && cand27 >= act27;

    Check("V27 play session: candidate links exposed, granted links a subset",
        sessionOk && subsetOk && linkOk,
        $"session={sessionOk} subset={subsetOk} cand={cand27} act={act27}");
}

// ---- V28: declared-mask downlink footprint (S.1503-4 D5.1.4.1) ----
{
    string expDir = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(expDir);

    // A constant az/el mask: every read returns exactly P.
    static string ConstMaskXml(double pDb) => FormattableString.Invariant($"""
        <?xml version="1.0"?>
        <srs>
          <satellite_system sat_name="V28" ntc_id="1">
            <pfd_mask mask_id="1" low_freq_mhz="11700" high_freq_mhz="12700" refbw_khz="40" type="azimuth_elevation">
              <by_a a="0">
                <by_b b="-90"><pfd c="-90">{pDb}</pfd><pfd c="90">{pDb}</pfd></by_b>
                <by_b b="90"><pfd c="-90">{pDb}</pfd><pfd c="90">{pDb}</pfd></by_b>
              </by_a>
            </pfd_mask>
          </satellite_system>
        </srs>
        """);
    string mask110 = Path.Combine(expDir, "v28const110.xml");
    string mask120 = Path.Combine(expDir, "v28const120.xml");
    File.WriteAllText(mask110, ConstMaskXml(-110.0));
    File.WriteAllText(mask120, ConstMaskXml(-120.0));

    var ant28 = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 12000.0, 0.6);
    double gmax28 = ant28.MaxGain;
    var limits28 = new List<radlimits.LimitPoint>
    {
        new radlimits.LimitPoint { EPFD = -300.0, Perc = 0.001 },
        new radlimits.LimitPoint { EPFD = 0.0, Perc = 100.0 },
    };
    static OperatingParamsSet Free28(double minElev = 0.0) => new()
    { SatName = "M", LowFreqMhz = 11700, HighFreqMhz = 12700, ElevAngleHeaderDeg = minElev };

    // (a) exactness: single sat overhead ES(0,0), GSO far at 60 E -- the
    // run must equal P + Grx(phi) - Gmax, and shifting the mask by -10 dB
    // must shift the run by exactly -10 dB.
    var one28 = new Constellation(new[] { new ConstellationShell
    { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1 } });
    var victim28 = new EpfdDownVictim { EsLatDeg = 0, EsLonDeg = 0, GsoLonDeg = 60, Antenna = ant28 };
    var es28 = GeodeticToEcef(0, 0, 0);
    double gso60 = 60.0 * Math.PI / 180.0;
    var gsoP = new Vec3(GsoGeometry.GsoRadiusKm * Math.Cos(gso60), GsoGeometry.GsoRadiusKm * Math.Sin(gso60), 0.0);
    var dirEsGso28 = (gsoP - es28).Normalized();
    double HandTerm(Constellation c, int i, double p)
    {
        var pos = c.StateAt(i, 0.0, 1.0).PositionEcefKm;
        var toSat = (pos - es28).Normalized();
        double phi = Math.Acos(Math.Clamp(Vec3.Dot(dirEsGso28, toSat), -1.0, 1.0)) * 180.0 / Math.PI;
        return p + ant28.GetAntGain(phi, 0.0) - gmax28;
    }
    var r110 = EpfdDownMask.Run(one28, MaskFootprint.LoadFile(mask110), Free28(), victim28, 1.0, 1, limits28);
    var r120 = EpfdDownMask.Run(one28, MaskFootprint.LoadFile(mask120), Free28(), victim28, 1.0, 1, limits28);
    bool exactOk = Math.Abs(r110.MaxEpfdDb - HandTerm(one28, 0, -110.0)) < 1e-9
                && Math.Abs(r110.MaxEpfdDb - r120.MaxEpfdDb - 10.0) < 1e-9;

    // (b) MAX_CO_FREQ cap at the victim: two neighbouring satellites, the
    // capped run keeps exactly the highest single entry.
    var two28 = new Constellation(new[]
    {
        new ConstellationShell { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1 },
        new ConstellationShell { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1, InPlaneOffsetDeg = 8.0 },
    });
    double h0 = HandTerm(two28, 0, -110.0), h1 = HandTerm(two28, 1, -110.0);
    var freeCap = Free28(); var cap1 = Free28(); cap1.MaxCoFreqHeader = 1;
    var rFree = EpfdDownMask.Run(two28, MaskFootprint.LoadFile(mask110), freeCap, victim28, 1.0, 1, limits28);
    var rCap = EpfdDownMask.Run(two28, MaskFootprint.LoadFile(mask110), cap1, victim28, 1.0, 1, limits28);
    double handSum = 10.0 * Math.Log10(Math.Pow(10.0, h0 / 10.0) + Math.Pow(10.0, h1 / 10.0));
    bool notMainBeam = Math.Max(h0, h1) + gmax28 + 110.0 <= gmax28 - 30.0;   // Grx <= Gmax - 30 for both
    bool capOk = notMainBeam
              && Math.Abs(rFree.MaxEpfdDb - handSum) < 1e-9
              && Math.Abs(rCap.MaxEpfdDb - Math.Max(h0, h1)) < 1e-9
              && rFree.MaxEpfdDb > rCap.MaxEpfdDb;

    // (c) exclusion zone: alpha0 = 90 puts the overhead satellite (alpha 0)
    // inside the zone; with its receive gain below the main-beam threshold
    // the step is quiet -- the D5.2 switch-off.
    var excl28 = Free28();
    var ring28 = new MinExcludeByOrbit { OrbId = 0 };
    ring28.ByLat.Add((-90.0, 90.0)); ring28.ByLat.Add((90.0, 90.0));
    excl28.MinExclude.Add(ring28);
    double grxOverDb = HandTerm(one28, 0, 0.0) + gmax28;   // Grx(phi) alone
    bool gateBelow = grxOverDb <= Math.Min(gmax28 - 30.0, ant28.GetAntGain(90.0, 0.0));
    var rExcl = EpfdDownMask.Run(one28, MaskFootprint.LoadFile(mask110), excl28, victim28, 1.0, 1, limits28);
    bool exclOk = gateBelow && rExcl.QuietSteps == 1 && double.IsNegativeInfinity(rExcl.MaxEpfdDb);

    // (d) the acceptance direction through the full runner, alpha/dLong
    // kind: a mask exported from the scene must bound the live composition
    // run at t = 0 (satellite exactly on the lat-0 block) for several ES.
    var vm28 = new PfdMaskViewModel();
    string maskAD = Path.Combine(expDir, "v28ad.xml");
    var optsAD = new MaskXmlExportOptions
    {
        SatName = "V28", NtcId = 2, MaskId = 1, RefBwKHz = 40,
        LatMinDeg = -10, LatMaxDeg = 10, LatStepDeg = 10,
        BStepDeg = 5, CStepDeg = 5,
        Kind = MaskPlotKind.AlphaDeltaLong, Format = MaskExportFormat.Xml, OutputPath = maskAD,
    };
    MaskXmlExport.GenerateAsync(new ReachableEnvelopeSampler(vm28, optsAD, 53.0),
        optsAD, null, CancellationToken.None).GetAwaiter().GetResult();
    var fpAD = MaskFootprint.LoadFile(maskAD);
    bool envOk = fpAD.Kind == MaskPlotKind.AlphaDeltaLong;
    foreach (var (esLat, esLon) in new[] { (0.0, 0.0), (5.0, 3.0), (-8.0, 10.0) })
    {
        var v = new EpfdDownVictim { EsLatDeg = esLat, EsLonDeg = esLon, GsoLonDeg = 0, Antenna = ant28 };
        var live = EpfdDown.Run(one28, new ScenePointing(vm28), v, 1.0, 1, limits28);
        var masked = EpfdDownMask.Run(one28, fpAD, Free28(), v, 1.0, 1, limits28);
        if (masked.MaxEpfdDb < live.MaxEpfdDb - 0.05001) { envOk = false; break; }
    }

    // (e) plumbing: profile field round-trips, composes through, and the
    // runner writes .down/.up but no .is under the mask footprint.
    var profM = new OperationProfile(Name: "m28", CellKm: 900.0,
        Downlink: new DownlinkProfile(FootprintSource: "mask", MaskXmlPath: mask110));
    var profM2 = OperationProfileCodec.Load(OperationProfileCodec.Save(profM));
    var compM = OperationComposer.Compose(profM2, 1200.0);
    string profPath = Path.Combine(expDir, "v28.opprofile.json");
    File.WriteAllText(profPath, OperationProfileCodec.Save(profM));

    var doc28 = new OrbitDesignDocumentViewModel();
    doc28.Shells[0].PlaneCount = 1; doc28.Shells[0].SatsPerPlane = 2;
    string p28 = Path.Combine(expDir, "v28.orbitdesign.json");
    File.WriteAllText(p28, doc28.BuildDocumentJson());
    var sim28 = new SimulationViewModel
    {
        DesignPath = p28, ProfilePath = profPath,
        DurationDaysText = (20.0 / 1440.0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        StepSecText = "60",
    };
    sim28.ValidateInputs();
    bool validOk = sim28.StatusText.Contains("declared mask");
    string base28 = Path.Combine(expDir, "v28sim");
    string summary28 = sim28.RunCore(sim28.BuildSetup(), base28);
    bool runOk = File.Exists(base28 + ".down.csv") && File.Exists(base28 + ".up.csv")
              && !File.Exists(base28 + ".is.csv") && summary28.Contains("is n/a (mask footprint)");
    bool plumbOk = profM2 == profM && profM2.Down.FootprintSource == "mask"
                && compM.UsesMaskFootprint && compM.DownlinkMaskXmlPath == mask110
                && validOk && runOk;

    // The per-latitude/scene-ring guard (debate follow-up): fires exactly
    // when the profile carries rows the scene cannot express.
    var profLat = new OperationProfile(Name: "g",
        AlphaByLat: new[] { new ProfileLatRow(30.0, 6.0), new ProfileLatRow(60.0, 8.0) });
    bool guardOk = OperationComposer.PerLatExclusionSceneGap(profLat) is string gw
                && gw.Contains("per-latitude")
                && OperationComposer.PerLatExclusionSceneGap(profM) is null
                && OperationComposer.PerLatExclusionSceneGap(new OperationProfile(AlphaExclDeg: 7.0)) is null;

    Check("V28 declared-mask footprint: exact read, cap, exclusion, envelope, plumbing, ring guard",
        exactOk && capOk && exclOk && envOk && plumbOk && guardOk,
        $"exact={exactOk} cap={capOk} excl={exclOk} env={envOk} plumb={plumbOk} guard={guardOk} " +
        $"h0={h0:F2} h1={h1:F2} free={rFree.MaxEpfdDb:F2} cap1={rCap.MaxEpfdDb:F2}");
}

// ---- V30: MIN_EXCLUDE reads by linear interpolation (Rec Part B) ----
{
    var p30 = new OperatingParamsSet { SatName = "X", LowFreqMhz = 1, HighFreqMhz = 2 };
    var ring30 = new MinExcludeByOrbit { OrbId = 0 };
    ring30.ByLat.Add((0.0, 4.0)); ring30.ByLat.Add((10.0, 8.0));
    p30.MinExclude.Add(ring30);
    bool interpOk =
        Math.Abs(DeclaredConstraints.ExclusionAlphaDeg(p30, 5.0, 1) - 6.0) < 1e-12
        && Math.Abs(DeclaredConstraints.ExclusionAlphaDeg(p30, 7.5, 1) - 7.0) < 1e-12
        && Math.Abs(DeclaredConstraints.ExclusionAlphaDeg(p30, -5.0, 1) - 4.0) < 1e-12
        && Math.Abs(DeclaredConstraints.ExclusionAlphaDeg(p30, 15.0, 1) - 8.0) < 1e-12;
    Check("V30 MIN_EXCLUDE read: linear interpolation between latitude rows (Rec Part B)", interpOk,
        FormattableString.Invariant(
            $"mid={DeclaredConstraints.ExclusionAlphaDeg(p30, 5.0, 1):F3} q={DeclaredConstraints.ExclusionAlphaDeg(p30, 7.5, 1):F3} clamp={DeclaredConstraints.ExclusionAlphaDeg(p30, -5.0, 1):F1}/{DeclaredConstraints.ExclusionAlphaDeg(p30, 15.0, 1):F1}"));
}

// ---- V31: layout knobs in the profile + the composition preview ----
{
    var prof31 = new OperationProfile(Name: "v31",
        Downlink: new DownlinkProfile(EllRollOffDb: 4.5, PatternKind: "Taylor_1p4",
            ThetaBDeg: 2.5, AutoHex: false, UvArrayBeams: true,
            EllAlphaDeg: 10.0, EllBetaDeg: 11.0, LnDb: -18.0, CrossoverDb: -4.0));
    var prof31b = OperationProfileCodec.Load(OperationProfileCodec.Save(prof31));
    var comp31 = OperationComposer.Compose(prof31b, 1200.0);
    var sc31 = comp31.Scene.Scene;
    bool wireOk = prof31b == prof31
        && Math.Abs(comp31.Scene.EllRollOffDb - 4.5) < 1e-12
        && sc31.PatternKind == BeamPatternKind.Taylor_1p4
        && Math.Abs(sc31.ThetaBDeg - 2.5) < 1e-12
        && !sc31.AutoMode && sc31.UvArrayBeams
        && Math.Abs(sc31.EllAlphaDeg - 10.0) < 1e-12
        && Math.Abs(sc31.EllBetaDeg - 11.0) < 1e-12
        && Math.Abs(sc31.LnDb - -18.0) < 1e-12
        && Math.Abs(sc31.CrossoverDb - -4.0) < 1e-12;

    var vm31 = new OperationProfileViewModel();
    bool previewOk = vm31.CompositionText.Contains("spot beams built")
        && vm31.CompositionText.Contains("active")
        && vm31.PreviewBeams.Count > 50
        && vm31.PreviewBeams.All(b => b.OutlineEKm.Count >= 8)
        && vm31.PreviewFovKm > 1000.0;
    vm31.EllRollOffText = "6";
    string with6 = vm31.CompositionText;
    bool previewLive = with6.Contains("roll-off 6.0 dB");
    // Visibility mirrors the composite tab: scene default is elliptical
    // auto; a circular pattern hides the elliptical inputs.
    bool visOk = vm31.IsEllipticalPattern && vm31.IsEllipticalAutoMode
        && !vm31.IsEllipticalManualMode;
    vm31.PatternKind = "Taylor_1p4";
    visOk = visOk && !vm31.IsEllipticalPattern && !vm31.IsEllipticalAutoMode;
    vm31.PatternKind = "Taylor_1p4_Ell";
    vm31.AutoHex = false;
    visOk = visOk && vm31.IsEllipticalManualMode && !vm31.IsEllipticalAutoMode;

    Check("V31 layout knobs: pattern/layout wired through, preview and visibility live",
        wireOk && previewOk && previewLive && visOk,
        $"wire={wireOk} preview={previewOk} live={previewLive} vis={visOk}");
}

// ---- V32: declared co-channel reuse is modelled in the truth run ----
{
    var vm32 = new PfdMaskViewModel { IsCoChannelMode = true };   // N = 3 (default index)
    var one32 = new Constellation(new[] { new ConstellationShell
    { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1 } });
    var ant32 = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 12000.0, 0.6);
    var victim32 = new EpfdDownVictim { EsLatDeg = 0, EsLonDeg = 0, GsoLonDeg = 0, Antenna = ant32 };
    var limits32 = new List<radlimits.LimitPoint>
    {
        new radlimits.LimitPoint { EPFD = -300.0, Perc = 0.001 },
        new radlimits.LimitPoint { EPFD = 0.0, Perc = 100.0 },
    };

    var st32 = one32.StateAt(0, 0.0, 1.0);
    var set32 = new ScenePointing(vm32).Resolve(st32);
    bool carryOk = set32.CoChannelN == 3 && set32.ReuseColors is { } cc32
        && cc32.Count == set32.Beams.Count && cc32.Distinct().Count() == 3;

    var resCo = EpfdDown.Run(one32, new ScenePointing(vm32), victim32, 1.0, 1, limits32);
    var resPs = EpfdDown.Run(one32, new ScenePointing(new PfdMaskViewModel()), victim32, 1.0, 1, limits32);

    // Hand value at t=0 (sat overhead, phi = 0 so Grx = Gmax): worst-colour
    // eirp minus spreading equals the run's epfd exactly.
    var gen32 = new PfdMaskViewModel();
    vm32.CopySettingsTo(gen32);
    gen32.Scene.SubSatLatDeg = st32.SubSatLatDeg;
    gen32.Scene.SubSatLonDeg = st32.SubSatLonDeg;
    gen32.Scene.AltitudeKm = st32.AltitudeKm;
    gen32.Scene.BodyYawDeg = st32.HeadingDeg;
    gen32.RebuildForCompute();
    var pow32 = PfdMaskField.BeamPowersDbw(gen32);
    var es32 = GeodeticToEcef(0, 0, 0);
    var toEs32 = (es32 - st32.PositionEcefKm).Normalized();
    int n32 = gen32.ReuseClusterSize;
    double eirpCo = BeamComposer.MaxCoChannelEirpDbw(gen32.Scene.Beams, toEs32, pow32,
        BeamComposer.ReuseColors(gen32.Scene.Beams, n32), n32);
    double dM32 = (es32 - st32.PositionEcefKm).Length * 1000.0;
    double pfdCo = eirpCo - 10.0 * Math.Log10(4.0 * Math.PI * dM32 * dM32);

    bool exactOk32 = Math.Abs(resCo.MaxEpfdDb - pfdCo) < 1e-9;
    bool orderOk32 = resCo.MaxEpfdDb < resPs.MaxEpfdDb - 0.5;
    Check("V32 co-channel reuse modelled in the truth run: worst-colour exact, below power sum",
        carryOk && exactOk32 && orderOk32,
        $"carry={carryOk} co={resCo.MaxEpfdDb:F3} hand={pfdCo:F3} powersum={resPs.MaxEpfdDb:F3}");
}

// ---- V29: BR limits database read + hand-entry cross-check ----
{
    string[] limitsDbs =
    {
        @"C:\Projects\_EPFD\epfd-reference\Cases\EPFD_limits_RES85_WRC23.mdb",
        @"C:\Projects\_EPFD\radians\radians\Resources\EPFD_limits_RES85_WRC23.mdb",
    };
    string[] dllDirs29 =
    {
        @"C:\Projects\_EPFD\radians\radians\dlls",
        @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
    };
    string limitsDb = limitsDbs.FirstOrDefault(File.Exists);
    string dllDir29 = dllDirs29.FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdLimitsApi64.dll")));

    if (limitsDb is not null && dllDir29 is not null)
    {
        // Crash-proof like the M checks: a wedged native DLL must fail
        // the check, not the process.
        try
        {
            LimitsDbReader.DllDirectory = dllDir29;
            // The compliance window's own query shape: 19.7 GHz downlink,
            // a 40 kHz sliver band, 1200 km operating height, A22.
            var lims = LimitsDbReader.Read(limitsDb, 19700.0 - 0.02, 19700.0 + 0.02,
                40.0, 1200.0);
            bool anyOk = lims.Count > 0;
            bool pointsOk = lims.All(l => l.Points.Count > 0 && l.Points.All(p =>
                double.IsFinite(p.EPFD) && p.Perc >= 0.0 && p.Perc <= 100.0));

            // The cross-check radians proposed: a hand-entered table is
            // exactly the rendered text of the loaded one -- render the
            // first plain row through the window's own text form and
            // parse it back with the sweep's own parser.
            var plain = lims.FirstOrDefault(l => !l.ShortTermLatDependent && l.Points.Count > 0);
            bool roundOk = false;
            if (plain is not null)
            {
                var parsed = ComplianceViewModel.ParseLimits(ComplianceViewModel.LimitPointsText(plain));
                roundOk = parsed.Count == plain.Points.Count
                    && parsed.Zip(plain.Points).All(z => z.First.EPFD == z.Second.EPFD
                                                      && z.First.Perc == z.Second.Perc);
            }
            bool descOk = lims.All(l => ComplianceViewModel.DescribeLimit(l).Length > 0);

            Check("V29 BR limits database: rows extracted, hand-entry text round-trips",
                anyOk && pointsOk && roundOk && descOk,
                $"rows={lims.Count} first={(lims.Count > 0 ? lims[0].RrRef : "-")} " +
                $"plainPts={plain?.Points.Count ?? 0} round={roundOk}");
        }
        catch (Exception ex)
        {
            Check("V29 BR limits database read", false, "exception: " + ex.Message);
        }
    }
    else
    {
        Check("V29 BR limits database read", true,
            "limits database or EpfdLimitsApi64.dll not present, skipped");
    }
}

// ---- V33: CDF viewer loader -- the runner's files read back exactly ----
{
    // V16's tiny run left its CDFs in exp/; the viewer's loader must
    // return exactly the file's data rows, comments and header skipped.
    string p33 = Path.Combine(AppContext.BaseDirectory, "exp", "v16sim.down.csv");
    var s33 = CdfSeries.LoadCsv(p33, "down");
    var raw33 = File.ReadAllLines(p33)
        .Select(l => l.Trim())
        .Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith("epfd"))
        .Select(l => l.Split(','))
        .ToArray();
    bool countOk = s33.EpfdDb.Length == raw33.Length && s33.Pct.Length == raw33.Length;
    bool valuesOk = countOk && raw33.Select((p, i) =>
            s33.EpfdDb[i] == double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture)
            && s33.Pct[i] == double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture))
        .All(x => x);
    // The runner writes levels ascending with percent non-increasing;
    // the loader must preserve that shape for the log-axis plot.
    bool shapeOk = s33.EpfdDb.Zip(s33.EpfdDb.Skip(1), (a, b) => a < b).All(x => x)
        && s33.Pct.Zip(s33.Pct.Skip(1), (a, b) => a >= b).All(x => x)
        && s33.Pct[0] > 0.0;
    Check("V33 CDF viewer loader: exact rows, ascending levels, non-increasing percent",
        countOk && valuesOk && shapeOk,
        $"rows={s33.EpfdDb.Length} count={countOk} values={valuesOk} shape={shapeOk}");
}

// ---- V34: loop v2 (Nco leg) -- synthesis, stay-passing, fixed point ----
{
    var lats34 = new List<double> { 30, 40, 50 };

    // (a) monotone case: pass iff cap <= limit, limits (1,2,2) from
    // baseline (3,3,2), floor 1 -> synthesized caps (1,2,2), verified.
    int sweeps34 = 0;
    var advA = ComplianceViewModel.NcoAdviseCore(lats34, new[] { 3, 3, 2 }, 1,
        caps =>
        {
            sweeps34++;
            var lim = new[] { 1, 2, 2 };
            return lats34.Select((l, i) => new ComplianceRow(l, -140.0 - caps[i],
                lim[i] - caps[i], caps[i] <= lim[i], 0)).ToList();
        });
    bool aOk = advA.Converged && advA.LeverMoves
        && advA.Rows.Select(r => (int)r.Value).SequenceEqual(new[] { 1, 2, 2 })
        && advA.GlobalCap == 1 && advA.Sweeps == sweeps34;

    // (b) a non-monotone row: lat 40 passes ONLY at cap exactly 2, and
    // lat 30 needs the floor -- the stay-passing check must reject the
    // dip and the fixed point must end honestly unconverged.
    var advB = ComplianceViewModel.NcoAdviseCore(lats34, new[] { 3, 3, 2 }, 1,
        caps => lats34.Select((l, i) => i switch
        {
            0 => new ComplianceRow(l, -140, 1 - caps[0], caps[0] <= 1, 0),
            1 => new ComplianceRow(l, -140, caps[1] == 2 ? 1 : -1, caps[1] == 2, 0),
            _ => new ComplianceRow(l, -140, 2 - caps[2], caps[2] <= 2, 0),
        }).ToList());
    bool bOk = !advB.Converged && advB.LeverMoves;

    // (c) inert lever: margins never move -> reported as not the lever.
    var advC = ComplianceViewModel.NcoAdviseCore(lats34, new[] { 3, 3, 3 }, 1,
        caps => lats34.Select(l => new ComplianceRow(l, -140, -5, false, 0)).ToList());
    bool cOk = !advC.LeverMoves;

    // (d) the merge keeps operator rows outside the grid span and
    // replaces the one inside it.
    var p34 = new OperationProfile(NcoByLat: new[] { new ProfileLatRow(70, 4), new ProfileLatRow(40, 3) });
    var m34 = ComplianceViewModel.WithNcoRows(p34, lats34, new[] { 1, 2, 2 });
    bool dOk = m34.NcoByLat!.Count == 4
        && m34.NcoByLat.Any(r => r.LatDeg == 70 && r.Value == 4)
        && !m34.NcoByLat.Any(r => r.LatDeg == 40 && r.Value == 3)
        && m34.NcoByLat.Any(r => r.LatDeg == 40 && r.Value == 2);

    // (e) effective baseline: nearest row inside the span, header outside,
    // both clamped by demand (inert caps above demand start at demand).
    var pE = new OperationProfile(DemandLinksPerCell: 2, NcoPerCell: 5,
        NcoByLat: new[] { new ProfileLatRow(0, 1), new ProfileLatRow(20, 3) });
    bool eOk = ComplianceViewModel.EffectiveNcoBaseline(pE, 5) == 1
        && ComplianceViewModel.EffectiveNcoBaseline(pE, 19) == 2
        && ComplianceViewModel.EffectiveNcoBaseline(pE, 40) == 2;

    Check("V34 loop v2 (Nco): synthesis, stay-passing, fixed point, merge, baseline",
        aOk && bOk && cOk && dOk && eOk,
        $"a={aOk} b={bOk} c={cOk} d={dOk} e={eOk} rowsA={string.Join("/", advA.Rows.Select(r => r.Value))} sweepsA={advA.Sweeps}");
}

// ---- V35: the external oracle (WP 4A Doc 4A/653) on a short comb ----
{
    // The one external check of the geometry-and-selection chain: L5's
    // eligible count at 50 N must sit in the published 3-8 band, and the
    // STEAM-2 alpha CDF of a randomly drawn eligible satellite must track
    // the published table. One day at 10 s steps converges the visibility
    // statistic (the full 1e6 x 1 s record is docs/oracle-steam2.md); the
    // 0.03 tolerance is ten times the digitisation of the published table
    // and four times the deviation the one-day 1 s run measured (0.007).
    var orc = Oracle.Measure(8640, 10.0, 600, progress: false);
    bool orcL5 = orc.L5Agrees;
    bool orcCdf = orc.WorstDev <= 0.03 && orc.Outage.All(o => o == 0);
    // The STEAM-2 case files must describe the same system the oracle
    // validated: the design document reproduces the shell satellite for
    // satellite at the epoch (planes, exact 1.9 deg phase, spacing,
    // altitude, inclination -- through the codec), and the profile carries
    // the document's gates and its random selection. Compared at t = 0
    // only: a Case-1 design document always carries the examination's
    // artificial precession (ToShell forces NOrbits >= 1), while the
    // oracle's shell drifts naturally as the published simulation did.
    string srcDir = Path.Combine(Path.GetDirectoryName(cardsPathV35Anchor()) ?? ".", "..", "dataset", "_src");
    string designPath = Path.Combine(srcDir, "STEAM-2.orbitdesign.json");
    string profilePath = Path.Combine(srcDir, "STEAM-2.opprofile.json");
    bool caseOk = false; string caseDetail = "case files absent";
    if (File.Exists(designPath) && File.Exists(profilePath))
    {
        var shellDoc = OrbitDesignFileCodec.ToShell(OrbitDesignFileCodec.LoadDocument(File.ReadAllText(designPath)).Shells[0]);
        var conDoc = new Constellation(new[] { shellDoc });
        var conRef = new Constellation(new[] { Oracle.Steam2Shell() });
        double worstKm = 0.0;
        if (conDoc.SatelliteCount == conRef.SatelliteCount)
            for (int i = 0; i < conRef.SatelliteCount; i += 97)
                worstKm = Math.Max(worstKm,
                    (conDoc.StateAt(i, 0.0, 7200.0).PositionEcefKm - conRef.StateAt(i, 0.0, 7200.0).PositionEcefKm).Length);
        var profS2 = OperationProfileCodec.Load(File.ReadAllText(profilePath));
        bool profOk = profS2.TrackingPolicy == "Random" && Math.Abs(profS2.AlphaExclDeg - 22.0) < 1e-9
            && Math.Abs(profS2.MinElevDeg - 40.0) < 1e-9 && profS2.NcoPerCell == 4 && Math.Abs(profS2.CellKm - 183.0) < 1e-9;
        caseOk = conDoc.SatelliteCount == conRef.SatelliteCount && worstKm < 1e-6 && profOk;
        caseDetail = $"sats={conDoc.SatelliteCount} worstKm={worstKm:0.0e0} profile={profOk}";
    }
    Check("V35 external oracle (4A/653): L5 eligible count 3-8, STEAM-2 alpha CDF within 0.03, case files reproduce the system",
        orcL5 && orcCdf && caseOk,
        $"l5={orc.L5Min}..{orc.L5Max} worstDev={orc.WorstDev:0.000} outage={orc.Outage.Sum()} {caseDetail}");

    static string cardsPathV35Anchor()
        => Path.Combine(radians.beamlab.app.HomeViewModel.FindDocsDir(AppContext.BaseDirectory)
            ?? @"C:\Projects\radians.beamlab\docs", "parameter-cards.html");
}


// ---- V36: compliance progress reporting -- ordered, complete, inert ----
{
    // The window's only feedback during a long sweep. Pin what it promises:
    // reports arrive per latitude (start and finish, the finish carrying the
    // verdict), fractions rise monotonically through [0, 1] and reach 1, the
    // advisor labels each walk step -- and, above all, listening changes no
    // number (progress is side-effect-free).
    var doc36 = new OrbitDesignDocumentViewModel();
    doc36.Shells[0].PlaneCount = 1; doc36.Shells[0].SatsPerPlane = 2;
    string p36 = Path.Combine(AppContext.BaseDirectory, "exp", "v36.orbitdesign.json");
    Directory.CreateDirectory(Path.GetDirectoryName(p36)!);
    File.WriteAllText(p36, doc36.BuildDocumentJson());
    var prof36 = new OperationProfile(Name: "V36", MinElevDeg: 10.0, CellKm: 900.0);
    string pp36 = Path.Combine(AppContext.BaseDirectory, "exp", "v36.opprofile.json");
    File.WriteAllText(pp36, OperationProfileCodec.Save(prof36));
    var cvm36 = new ComplianceViewModel
    {
        DesignPath = p36, ProfilePath = pp36,
        LatFromText = "40", LatToText = "50", LatStepText = "10",
        DurationDaysText = (30.0 / 1440.0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        StepSecText = "60",
        LimitsText = "-100 5",          // permissive: both latitudes pass
    };
    var sweep36 = cvm36.BuildSweep();

    var col36 = new ProgressCollector();
    var withProgress = ComplianceViewModel.RunSweep(sweep36, 0.0, col36);
    var without = ComplianceViewModel.RunSweep(sweep36, 0.0);

    var fr = col36.Reports.Select(r => r.Fraction).ToList();
    bool anyOk = col36.Reports.Count >= 4;                       // >= start + finish per latitude
    bool rangeOk = fr.All(f => f >= -1e-9 && f <= 1.0 + 1e-9);
    bool monotoneOk = fr.Zip(fr.Skip(1), (a, b) => b >= a - 1e-9).All(x => x);
    bool endsOk = fr.Count > 0 && Math.Abs(fr[^1] - 1.0) < 1e-9;
    bool perLatOk = col36.Reports.Any(r => r.Text.Contains("lat 40") && r.Text.Contains("1/2"))
                 && col36.Reports.Any(r => r.Text.Contains("lat 50") && r.Text.Contains("2/2"));
    bool verdictOk = col36.Reports.Count(r => r.Text.Contains("PASS") || r.Text.Contains("FAIL")) >= 2;
    // Inert: identical rows with and without a listener.
    bool inertOk = withProgress.Count == without.Count
        && withProgress.Zip(without, (a, b) => a.LatDeg == b.LatDeg
            && a.MaxEpfdDb == b.MaxEpfdDb && a.WorstMarginDb == b.WorstMarginDb
            && a.Pass == b.Pass && a.QuietSteps == b.QuietSteps).All(x => x);

    // The advisor labels its walk steps and stays inside the bar.
    var colW36 = new ProgressCollector();
    var adv36 = ComplianceViewModel.Advise(sweep36, 1.0, 2.0, colW36);
    bool walkOk = colW36.Reports.Any(r => r.Text.StartsWith("walk 1"))
        && colW36.Reports.All(r => r.Fraction >= -1e-9 && r.Fraction <= 1.0 + 1e-9)
        && adv36.FoundAlpha is not null;

    Check("V36 compliance progress: per-latitude lines, monotone fractions, inert on results",
        anyOk && rangeOk && monotoneOk && endsOk && perLatOk && verdictOk && inertOk && walkOk,
        $"n={col36.Reports.Count} range={rangeOk} monotone={monotoneOk} ends={endsOk} " +
        $"perLat={perLatOk} verdict={verdictOk} inert={inertOk} walk={walkOk}");
}


// ---- V37: multi-victim run identical to running the victims separately ----
{
    // A victim is only an accumulator: the system behaves the same whoever is
    // listening. One pass carrying N accumulators must therefore reproduce N
    // separate passes exactly -- not approximately -- or the sweep speed-up
    // would change the numbers it is meant to leave alone.
    var shells37 = new[] { new ConstellationShell
        { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 2 } };
    var con37 = new Constellation(shells37);
    var scene37 = new PfdMaskViewModel
        { AltitudeKm = 1200.0, FrequencyGHz = 19.7, MinElevDeg = 10.0, RefBwKHz = 40.0 };
    var geo37 = ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, 900.0);
    var ops37 = new OperatingParamsSet
        { SatName = "V37", LowFreqMhz = 19700, HighFreqMhz = 19700, ElevAngleHeaderDeg = 10.0 };
    double dur37 = 3600.0;
    var limits37 = new List<radlimits.LimitPoint>
        { new() { EPFD = -160, Perc = 5.0 }, new() { EPFD = -150, Perc = 1.0 } };
    EpfdDownVictim Victim37(double lat) => new()
    {
        EsLatDeg = lat, EsLonDeg = 0.0, GsoLonDeg = 10.0,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 19700.0, 1.0),
    };
    var victims37 = new[] { Victim37(30.0), Victim37(45.0), Victim37(60.0) };

    // Separate passes: one pointing each, exactly as the sweep used to do.
    var apart = victims37.Select(v => EpfdDown.Run(con37,
        new ScheduledPointing(con37, geo37, ops37, scene37, dur37),
        v, 60.0, 60, limits37, dur37)).ToList();
    // One pass, three accumulators.
    var together = EpfdDown.RunMany(con37,
        new ScheduledPointing(con37, geo37, ops37, scene37, dur37),
        victims37, 60.0, 60, limits37, dur37);

    bool countOk37 = together.Count == apart.Count;
    bool sameOk37 = countOk37;
    for (int v = 0; v < apart.Count && sameOk37; v++)
    {
        var (eA, pA) = apart[v].Accumulator.BuildCdf();
        var (eB, pB) = together[v].Accumulator.BuildCdf();
        sameOk37 = apart[v].MaxEpfdDb == together[v].MaxEpfdDb
            && apart[v].QuietSteps == together[v].QuietSteps
            && apart[v].Steps == together[v].Steps
            && eA.Length == eB.Length && pA.Length == pB.Length
            && eA.Zip(eB, (x, y) => x == y).All(x => x)
            && pA.Zip(pB, (x, y) => x == y).All(x => x);
    }
    // The victims must actually differ, or the identity is vacuous.
    bool distinctOk37 = together.Count == 3
        && together[0].MaxEpfdDb != together[2].MaxEpfdDb;

    Check("V37 multi-victim run: one pass reproduces separate passes bit for bit",
        countOk37 && sameOk37 && distinctOk37,
        $"count={countOk37} same={sameOk37} distinct={distinctOk37} " +
        $"max30={together[0].MaxEpfdDb:F2} max60={together[^1].MaxEpfdDb:F2}");
}


// ---- V38: the saturated probe, and the declaration the examination reads ----
{
    // Two things a declaration must not be: measured under a traffic sample,
    // or silently taken from the gates the truth run enforces. This pins both.
    string expDir38 = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(expDir38);

    // (a) Saturation lifts every traffic-shaped input, and lifts demand to
    // the declared cap so the CAP binds rather than the traffic model.
    var throttled = new OperationProfile(Name: "V38", CellKm: 900.0,
        NcoPerCell: 3, DemandLinksPerCell: 1, ActivityFactor: 0.25,
        OperationalFraction: 0.5, IlluminationDutyCycle: 0.5);
    var enf38 = OperationComposer.Compose(throttled, 1200.0).Enforced;
    var sat38 = ComplianceViewModel.Saturate(throttled, enf38);
    bool satOk38 = sat38.DemandLinksPerCell == 3        // lifted to the declared Nco
        && sat38.ActivityFactor == 1.0
        && sat38.OperationalFraction == 1.0
        && sat38.IlluminationDutyCycle == 1.0
        // everything NOT traffic-shaped is left exactly alone
        && sat38.NcoPerCell == throttled.NcoPerCell
        && sat38.CellKm == throttled.CellKm
        && sat38.Name == throttled.Name;
    // With no cap declared there is nothing to saturate demand to, so the
    // profile's own demand stands rather than becoming unbounded.
    var noCap = new OperationProfile(Name: "V38n", DemandLinksPerCell: 2);
    var satNoCap = ComplianceViewModel.Saturate(noCap,
        OperationComposer.Compose(noCap, 1200.0).Enforced);
    bool capOk38 = satNoCap.DemandLinksPerCell == 2;

    // (b) The leak itself: derive from the throttled profile and from its
    // saturated probe. The throttled run cannot see more than one satellite
    // per cell -- it never asked for more -- so it would declare MAX_CO_FREQ
    // = 1, a promise the system breaks at peak.
    var doc38 = new OrbitDesignDocumentViewModel();
    doc38.Shells[0].PlaneCount = 3; doc38.Shells[0].SatsPerPlane = 6;
    string p38 = Path.Combine(expDir38, "v38.orbitdesign.json");
    File.WriteAllText(p38, doc38.BuildDocumentJson());
    string pp38 = Path.Combine(expDir38, "v38.opprofile.json");
    File.WriteAllText(pp38, OperationProfileCodec.Save(throttled));
    var cvm38 = new ComplianceViewModel
    {
        DesignPath = p38, ProfilePath = pp38,
        LatFromText = "40", LatToText = "40", LatStepText = "10",
        DurationDaysText = (30.0 / 1440.0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        StepSecText = "60", LimitsText = "-100 5",
    };
    var sweep38 = cvm38.BuildSweep();
    var derSat = ComplianceViewModel.DeriveDeclared(sweep38.Shells, sweep38.Profile,
        sweep38.Steps * sweep38.StepSec, sweep38.StepSec);
    int ncoSat = derSat.Set.MaxCoFreqByLat.Count == 0 ? 0
        : derSat.Set.MaxCoFreqByLat.Max(r => r.Value);
    bool leakOk38 = derSat.LinkSamples > 0 && ncoSat > 1;

    // (c) The declaration the examination reads is an input, not the gates.
    // Null keeps the previous behaviour exactly; a tighter declared set
    // changes the verdict, which is what makes the sweep able to compute E1.
    string mask38 = Path.Combine(expDir38, "v38mask.xml");
    File.WriteAllText(mask38, FormattableString.Invariant($"""
        <?xml version="1.0"?>
        <srs>
          <satellite_system sat_name="V38" ntc_id="1">
            <pfd_mask mask_id="1" low_freq_mhz="11700" high_freq_mhz="12700" refbw_khz="40" type="azimuth_elevation">
              <by_a a="0">
                <by_b b="-90"><pfd c="-90">-110</pfd><pfd c="90">-110</pfd></by_b>
                <by_b b="90"><pfd c="-90">-110</pfd><pfd c="90">-110</pfd></by_b>
              </by_a>
            </pfd_mask>
          </satellite_system>
        </srs>
        """));
    var profM38 = new OperationProfile(Name: "V38m", CellKm: 900.0,
        Downlink: new DownlinkProfile(FootprintSource: "mask", MaskXmlPath: mask38));
    string ppm38 = Path.Combine(expDir38, "v38m.opprofile.json");
    File.WriteAllText(ppm38, OperationProfileCodec.Save(profM38));
    var cvmM38 = new ComplianceViewModel
    {
        DesignPath = p38, ProfilePath = ppm38,
        LatFromText = "40", LatToText = "40", LatStepText = "10",
        DurationDaysText = (10.0 / 1440.0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        StepSecText = "60", LimitsText = "-160 5",
    };
    var sweepM38 = cvmM38.BuildSweep();
    var rowsBase = ComplianceViewModel.RunSweepProfile(sweepM38, sweepM38.Profile);
    var rowsNull = ComplianceViewModel.RunSweepProfile(
        sweepM38 with { Declared = null }, sweepM38.Profile);
    // A declaration that admits no satellite at all: 90 deg minimum elevation.
    var shut = new OperatingParamsSet
    {
        SatName = "V38", NtcId = 1, ParamId = 1,
        LowFreqMhz = 11700, HighFreqMhz = 12700, ElevAngleHeaderDeg = 90.0,
    };
    var rowsShut = ComplianceViewModel.RunSweepProfile(
        sweepM38 with { Declared = shut }, sweepM38.Profile);
    bool inertOk38 = rowsNull.Count == rowsBase.Count
        && rowsNull.Zip(rowsBase, (a, b) => a.MaxEpfdDb == b.MaxEpfdDb
            && a.WorstMarginDb == b.WorstMarginDb && a.QuietSteps == b.QuietSteps).All(x => x);
    bool liveOk38 = rowsShut.Count == rowsBase.Count
        && rowsShut[0].QuietSteps > rowsBase[0].QuietSteps;
    // (d) The derived set is array-only for min_elev and max_co_freq --
    // header and array are mutually exclusive per quantity -- and never
    // declares MIN_DURATION in either form.
    bool headerOk38 = derSat.Set.ElevAngleHeaderDeg is null && derSat.Set.MaxCoFreqHeader is null
        && derSat.Set.MinDurationSecHeader is null && derSat.Set.MinDurationByLat.Count == 0
        && derSat.Set.MinElev.Count > 0 && derSat.Set.MaxCoFreqByLat.Count > 0
        && DeclaredConstraints.FormConflicts(derSat.Set).Count == 0;

    Check("V38 saturated probe: traffic cannot reach a declaration; the examination reads a declared set; array-only, one form per quantity",
        satOk38 && capOk38 && leakOk38 && inertOk38 && liveOk38 && headerOk38,
        $"sat={satOk38} cap={capOk38} leak={leakOk38} inert={inertOk38} live={liveOk38} headers={headerOk38} " +
        $"ncoSat={ncoSat} samples={derSat.LinkSamples} " +
        $"quietBase={rowsBase[0].QuietSteps} quietShut={rowsShut[0].QuietSteps}");
}


// ---- V39: derive & fill prefers the compliance loop's run ----
{
    // The loop derives once, saturated, for the whole projection. The designer
    // must READ that rather than simulate a second opinion of the same system --
    // and the two must agree on where it lives, or they silently never meet.
    string root39 = Path.Combine(AppContext.BaseDirectory, "exp", "v39repo");
    var prof39 = new OperationProfile(Name: "STEAM-2 (WP 4A Doc 4A/653; assumed)", CellKm: 900.0);

    // One spelling of the run name, shared by the loop and the designer.
    bool nameOk39 = ComplianceViewModel.RunName(prof39) == "steam-2"
        && ComplianceViewModel.RunName(new OperationProfile(Name: "v21")) == "v21"
        && ComplianceViewModel.RunDir(root39, prof39)
            == Path.Combine(root39, "dataset", "margin", "steam-2")
        && ComplianceViewModel.RunSetJsonPath(root39, prof39)
            == Path.Combine(root39, "dataset", "margin", "steam-2", "steam-2.operparams.json");

    string setPath39 = ComplianceViewModel.RunSetJsonPath(root39, prof39);
    Directory.CreateDirectory(Path.GetDirectoryName(setPath39)!);
    string profPath39 = Path.Combine(root39, "v39.opprofile.json");
    File.WriteAllText(profPath39, OperationProfileCodec.Save(prof39));

    // (a) no run on disk -> nothing to prefer, the designer must measure.
    if (File.Exists(setPath39)) File.Delete(setPath39);
    bool noneOk39 = OpParamsViewModel.LoopRunSetFor(root39, prof39, profPath39) is null;

    // (b) a run newer than the profile -> that is the set the projection used.
    var runSet39 = new OperatingParamsSet
    {
        SatName = "FROMLOOP", NtcId = 7, ParamId = 3,
        LowFreqMhz = 18150, HighFreqMhz = 18150, MaxCoFreqSat = 45,
    };
    runSet39.MaxCoFreqByLat.Add((25.0, 4));
    File.WriteAllText(setPath39, OpParamsFileCodec.Save(OpParamsFileCodec.FromSet(runSet39)));
    File.SetLastWriteTimeUtc(profPath39, DateTime.UtcNow.AddMinutes(-10));
    File.SetLastWriteTimeUtc(setPath39, DateTime.UtcNow);
    bool freshOk39 = OpParamsViewModel.LoopRunSetFor(root39, prof39, profPath39) == setPath39;

    // (c) a run OLDER than the profile describes a system since edited.
    File.SetLastWriteTimeUtc(profPath39, DateTime.UtcNow);
    File.SetLastWriteTimeUtc(setPath39, DateTime.UtcNow.AddMinutes(-10));
    bool staleOk39 = OpParamsViewModel.LoopRunSetFor(root39, prof39, profPath39) is null;

    // (d) what is loaded IS the run's set -- no re-measurement, no drift.
    var vm39 = new OpParamsViewModel();
    vm39.LoadJson(File.ReadAllText(setPath39));
    string a39 = Path.Combine(AppContext.BaseDirectory, "exp", "v39a.xml");
    string b39 = Path.Combine(AppContext.BaseDirectory, "exp", "v39b.xml");
    vm39.ExportXml(a39);
    OperParamsXmlWriter.Write(b39, runSet39);
    bool loadOk39 = File.ReadAllText(a39) == File.ReadAllText(b39);

    Check("V39 derive & fill prefers the compliance loop's run: one name, freshness, exact set",
        nameOk39 && noneOk39 && freshOk39 && staleOk39 && loadOk39,
        $"name={nameOk39} none={noneOk39} fresh={freshOk39} stale={staleOk39} load={loadOk39}");
}


// ---- V40: the service-span certificate darkens what cannot be transmitted ----
{
    // A mask lit where the declared system cannot transmit is not a purer
    // declaration but a wrong one. The rows go dark under a CLOSED-FORM
    // certificate from declared commitments -- never because a probe happened
    // to visit nothing, which is the unsafe direction.
    double half40 = ServiceSpanSampler.CoverageHalfAngleDeg(1200.0, 40.0);
    double half10 = ServiceSpanSampler.CoverageHalfAngleDeg(1200.0, 10.0);
    double half0 = ServiceSpanSampler.CoverageHalfAngleDeg(1200.0, 0.0);
    // acos(Re/(Re+h) * cos eps) - eps, in degrees.
    bool geomOk40 = Math.Abs(half40 - 9.86) < 0.02
        && half10 > half40 && half0 > half10;   // a lower floor reaches further

    var decl40 = new OperatingParamsSet
    {
        SatName = "V40", NtcId = 1, ParamId = 1, LowFreqMhz = 18150, HighFreqMhz = 18150,
        EsLatMinDeg = 20.0, EsLatMaxDeg = 49.0,
    };
    var me40 = new MinElevByLat { LatDeg = 25.0 };
    me40.ByAz.Add((0.0, 40.0)); me40.ByAz.Add((360.0, 40.0));
    decl40.MinElev.Add(me40);

    var probe40 = new ConstantSampler(-120.0);
    var span40 = new ServiceSpanSampler(probe40, decl40, 1200.0, 0.001);
    bool reachOk40 =
        span40.ReachesServiceSpan(30.0)          // inside the span
        && span40.ReachesServiceSpan(20.0)
        && span40.ReachesServiceSpan(49.0)
        && span40.ReachesServiceSpan(20.0 - half40 + 0.01)   // just inside the circle
        && !span40.ReachesServiceSpan(20.0 - half40 - 0.01)  // just outside it
        && !span40.ReachesServiceSpan(0.0)       // 20 deg away, circle is 9.86
        && !span40.ReachesServiceSpan(-30.0);    // the other hemisphere

    // The decorator: dark rows sample as unreachable (the exporter writes
    // Sec. C1 -1000), lit rows pass straight through to the inner sampler.
    span40.PrepareLatitude(0.0);
    double dark40 = span40.SampleMaxIn(0.0, 45.0, 0.5, 0.5);
    span40.PrepareLatitude(30.0);
    double lit40 = span40.SampleMaxIn(0.0, 45.0, 0.5, 0.5);
    bool gateOk40 = double.IsNegativeInfinity(dark40) && lit40 == -120.0
        && span40.DarkLatitudes == 1 && span40.LitLatitudes == 1
        && probe40.Prepared == 1;   // no field is built for a dark row

    // Nothing declared promises nothing, so no row may be certified dark.
    var bare40 = new OperatingParamsSet
    {
        SatName = "V40b", NtcId = 1, ParamId = 1, LowFreqMhz = 18150, HighFreqMhz = 18150,
        EsLatMinDeg = 20.0, EsLatMaxDeg = 49.0,
    };
    var spanBare = new ServiceSpanSampler(new ConstantSampler(-120.0), bare40, 1200.0, 0.001);
    bool bareOk40 = Math.Abs(spanBare.HalfAngleDeg
        - ServiceSpanSampler.CoverageHalfAngleDeg(1200.0, 0.0)) < 1e-9;

    // The smallest declared elevation wins: it reaches furthest, so it
    // darkens the fewest rows -- the conservative reading of the promise.
    var mixed40 = new OperatingParamsSet
    {
        SatName = "V40c", NtcId = 1, ParamId = 1, LowFreqMhz = 18150, HighFreqMhz = 18150,
        EsLatMinDeg = 20.0, EsLatMaxDeg = 49.0, ElevAngleHeaderDeg = 25.0,
    };
    var meHi = new MinElevByLat { LatDeg = 25.0 };
    meHi.ByAz.Add((0.0, 40.0)); meHi.ByAz.Add((360.0, 40.0));
    mixed40.MinElev.Add(meHi);
    var spanMixed = new ServiceSpanSampler(new ConstantSampler(-120.0), mixed40, 1200.0, 0.001);
    bool smallestOk40 = Math.Abs(spanMixed.HalfAngleDeg
        - ServiceSpanSampler.CoverageHalfAngleDeg(1200.0, 25.0)) < 1e-9;

    // A row governs a BAND (Sec. D5.1.5 step 1 reads the nearest latitude),
    // so it may go dark only when NO latitude it governs can reach the span.
    // Darkening on the row centre alone under-declares for the reachable half
    // of the band -- the deflated-mask direction, which is the unsafe one.
    var band40 = new ServiceSpanSampler(new ConstantSampler(-120.0), decl40, 1200.0, 10.0);
    bool bandOk40 =
        // row 10 governs 5..15; 15 is 5 deg from the span, well inside 9.86
        band40.ReachesServiceSpan(10.0)
        // the point test would have darkened it: 10 deg away, circle is 9.86
        && !span40.ReachesServiceSpan(10.0)
        // row 0 governs -5..5; 15 deg from the span, still dark
        && !band40.ReachesServiceSpan(0.0)
        && Math.Abs(band40.HalfRowDeg - 5.0) < 1e-12;

    // End to end: a real export must actually WRITE the Sec. C1 null. The
    // sampler returning NegativeInfinity is only half the claim -- what the
    // filing carries is the text in the file, so read it back.
    string maskDir40 = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(maskDir40);
    string maskPath40 = Path.Combine(maskDir40, "v40.mask.xml");
    var opts40 = new MaskXmlExportOptions
    {
        SatName = "V40", NtcId = 1, MaskId = 1,
        LowFreqMhz = 18150, HighFreqMhz = 18150, RefBwKHz = 40.0,
        LatMinDeg = -30.0, LatMaxDeg = 60.0, LatStepDeg = 30.0,
        BStepDeg = 45.0, CStepDeg = 45.0,
        Kind = MaskPlotKind.AzEl, Format = MaskExportFormat.Xml,
        OutputPath = maskPath40,
    };
    // Rows at -30, 0, 30, 60 on a 30 deg grid, so each governs +/-15 deg,
    // against a 20..49 span and a 9.86 deg circle. Row 0 governs up to 15,
    // which is 5 deg from the span, so it is LIT; row 60 governs down to 45,
    // inside the span; only -30 (nearest reach -15, a 35 deg gap) is dark.
    var spanX = new ServiceSpanSampler(new ConstantSampler(-120.0), decl40, 1200.0, 30.0);
    MaskXmlExport.GenerateAsync(spanX, opts40, null, CancellationToken.None)
        .GetAwaiter().GetResult();
    string maskText40 = File.ReadAllText(maskPath40);
    bool nullOk40 = File.Exists(maskPath40)
        && maskText40.Contains("-1000")            // the dark rows carry the null
        && maskText40.Contains("-120")             // the lit row carries its value
        && spanX.DarkLatitudes == 1 && spanX.LitLatitudes == 3;

    Check("V40 service-span certificate: band-wide reach, -1000 written, smallest declared floor",
        geomOk40 && reachOk40 && gateOk40 && bareOk40 && smallestOk40 && nullOk40 && bandOk40,
        $"geom={geomOk40} reach={reachOk40} gate={gateOk40} bare={bareOk40} smallest={smallestOk40} " +
        $"null={nullOk40} band={bandOk40} darkRows={spanX.DarkLatitudes} " +
        $"half40={half40:F2} dark={span40.DarkLatitudes} lit={span40.LitLatitudes}");
}


// ---- V41: a measured floor stays a floor after interpolation ----
{
    // MIN_EXCLUDE is read by LINEAR INTERPOLATION (Part B), unlike every
    // other per-latitude array. A band minimum labelled at its band centre is
    // exact for a nearest read and too HIGH between rows for an interpolated
    // one -- declaring an exclusion the operation does not honour.
    var raw41 = new List<(double LatDeg, double Value)>
        { (25.0, 22.0), (35.0, 30.0), (45.0, 28.0) };
    var safe41 = OpParamsDeriver.InterpolationSafeFloor(raw41);
    bool slideOk41 = safe41.Count == 3
        && safe41[0] == (25.0, 22.0)      // min(22, 30)
        && safe41[1] == (35.0, 22.0)      // min(22, 30, 28)
        && safe41[2] == (45.0, 28.0);     // min(30, 28)

    // The property itself: at every latitude the interpolated declaration must
    // sit at or below what the band containing that latitude actually did.
    static double TrueMin41(double lat) => lat < 30.0 ? 22.0 : lat < 40.0 ? 30.0 : 28.0;
    static OperatingParamsSet SetOf41(IEnumerable<(double LatDeg, double Value)> rows)
    {
        var p = new OperatingParamsSet
            { SatName = "V41", NtcId = 1, ParamId = 1, LowFreqMhz = 1, HighFreqMhz = 2 };
        var ring = new MinExcludeByOrbit { OrbId = 0 };
        foreach (var (lat, v) in rows) ring.ByLat.Add((lat, v));
        p.MinExclude.Add(ring);
        return p;
    }
    var pSafe41 = SetOf41(safe41);
    var pRaw41 = SetOf41(raw41);
    bool safeOk41 = true, rawViolates41 = false;
    double worstRaw41 = 0.0;
    for (double lat = 25.0; lat <= 45.0 + 1e-9; lat += 0.5)
    {
        double declaredSafe = DeclaredConstraints.ExclusionAlphaDeg(pSafe41, lat, 1);
        double declaredRaw = DeclaredConstraints.ExclusionAlphaDeg(pRaw41, lat, 1);
        if (declaredSafe > TrueMin41(lat) + 1e-9) safeOk41 = false;
        if (declaredRaw > TrueMin41(lat) + 1e-9)
        {
            rawViolates41 = true;
            worstRaw41 = Math.Max(worstRaw41, declaredRaw - TrueMin41(lat));
        }
    }

    // A band where no exclusion shaped operations must pull its neighbours
    // down, not be dropped for interpolation to span.
    var withZero41 = OpParamsDeriver.InterpolationSafeFloor(new List<(double, double)>
        { (25.0, 22.0), (35.0, 0.0), (45.0, 28.0) });
    bool zeroOk41 = withZero41[0].Value == 0.0 && withZero41[1].Value == 0.0
        && withZero41[2].Value == 0.0;

    Check("V41 interpolation-safe MIN_EXCLUDE: sliding floor, property holds, raw violates",
        slideOk41 && safeOk41 && rawViolates41 && zeroOk41,
        $"slide={slideOk41} safe={safeOk41} rawViolates={rawViolates41} zero={zeroOk41} " +
        $"worstRawExcess={worstRaw41:F1} deg of alpha");
}


// ---- V42: the worst margin travels with the grid it is worst over ----
{
    // A sweep evaluates real victims at discrete latitudes, so its extremum is
    // the worst of what it SAMPLED. On BL-D2 a 5 deg sweep found latitude 35
    // to be 10 dB worse than anything the 10 deg sweep visited, while the
    // summary line read "worst margin -33.4 dB" with nothing to qualify it.
    var wide42 = new List<ComplianceRow>
    {
        new(30.0, -142.4, -29.3, false, 0),
        new(40.0, -136.8, -33.4, false, 0),
        new(50.0, -138.3, -29.2, false, 0),
        new(60.0, -141.6, -29.3, false, 0),
    };
    string sumWide = ComplianceViewModel.SummarizeRows(wide42);
    bool gridOk42 = sumWide.StartsWith("EXCEEDED")            // V23's contract intact
        && sumWide.Contains("sampled every 10 deg over 30..60")
        && sumWide.Contains("finer sweep can find worse");

    // The finer grid states its own step, so two records cannot be compared
    // without the difference being visible in the text itself.
    var fine42 = new List<ComplianceRow>
    {
        new(30.0, -142.4, -29.3, false, 0),
        new(35.0, -123.4, -43.7, false, 0),
        new(40.0, -136.8, -33.4, false, 0),
    };
    bool fineOk42 = ComplianceViewModel.SummarizeRows(fine42)
        .Contains("sampled every 5 deg over 30..40");

    // A compliant sweep carries the same qualifier -- passing at the sampled
    // latitudes is not passing everywhere.
    var pass42 = new List<ComplianceRow>
    {
        new(0.0, -206.0, 31.4, true, 0),
        new(10.0, -199.7, 25.0, true, 0),
    };
    string sumPass = ComplianceViewModel.SummarizeRows(pass42);
    bool passOk42 = sumPass.StartsWith("COMPLIANT")
        && sumPass.Contains("sampled every 10 deg over 0..10")
        && sumPass.Contains("finer sweep can find worse");

    // One latitude is not a grid, and says so rather than implying a step.
    var one42 = new List<ComplianceRow> { new(40.0, -136.8, -33.4, false, 0) };
    bool oneOk42 = ComplianceViewModel.SummarizeRows(one42).Contains("at latitude 40 only")
        && !ComplianceViewModel.SummarizeRows(one42).Contains("sampled every")
        && ComplianceViewModel.SamplingNote(new List<ComplianceRow>()) == "";

    Check("V42 the worst margin travels with the grid it is worst over",
        gridOk42 && fineOk42 && passOk42 && oneOk42,
        $"grid={gridOk42} fine={fineOk42} pass={passOk42} one={oneOk42}");
}


// ---- V43: the mask cache key covers the code that produced the values ----
{
    // A cache keyed only on grid, service span and profile timestamp let the
    // band-envelope fix hide behind warm files on every cached case. Anything
    // that decides the values belongs in the key, the producing code included.
    string t43 = ComplianceLoop.MaskCacheTag(10.0, 1.0, -50.0, 50.0);
    string id43 = ComplianceLoop.ProducerId();
    bool shapeOk43 = id43.Length == 8
        && id43.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))
        && t43.Contains("-v" + id43)
        && !t43.Contains(".");                       // safe as a file name
    // Stable within a build: a second call must not invalidate the first.
    bool stableOk43 = ComplianceLoop.ProducerId() == id43
        && ComplianceLoop.MaskCacheTag(10.0, 1.0, -50.0, 50.0) == t43;
    // Every value-determining input still separates caches.
    bool gridOk43 = ComplianceLoop.MaskCacheTag(5.0, 1.0, -50.0, 50.0) != t43
        && ComplianceLoop.MaskCacheTag(10.0, 2.0, -50.0, 50.0) != t43
        && ComplianceLoop.MaskCacheTag(10.0, 1.0, 20.0, 50.0) != t43
        && ComplianceLoop.MaskCacheTag(10.0, 1.0, -50.0, 49.0) != t43;

    Check("V43 mask cache key covers grid, service span and the producing code",
        shapeOk43 && stableOk43 && gridOk43,
        $"shape={shapeOk43} stable={stableOk43} grid={gridOk43} tag={t43}");
}


// ---- V44: the co-frequency beam capacity -- enforced, enveloped, plumbed ----
{
    // E1-1 with its warrant attached. The payload declares how many same-colour
    // beams a satellite may light at once (S.1325-rev Sec. 2.5.2, a required
    // antenna input); the scheduler enforces it; the mask envelopes over it.
    // Without the declaration the observed count is a sample maximum and the
    // mask may only sum the whole colour.

    // (a) The composite: top-K per colour is bounded, monotone, and exact at
    // both ends -- cap 1 is the largest single same-colour contribution, cap >=
    // the colour size is the uncapped sum.
    var scene44 = new PfdMaskViewModel
        { AltitudeKm = 1200.0, FrequencyGHz = 19.7, MinElevDeg = 10.0, RefBwKHz = 40.0 };
    scene44.IsCoChannelMode = true;
    scene44.ReuseClusterIndex = 1;                    // 4 colours
    var shells44 = new[] { new ConstellationShell
        { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 1, SatsPerPlane = 1 } };
    var con44 = new Constellation(shells44);
    double dur44 = 3600.0;
    var st44 = con44.StateAt(0, 0.0, dur44);
    var set44 = new ScenePointing(scene44).Resolve(st44);
    int n44 = set44.CoChannelN!.Value;
    var colors44 = set44.ReuseColors!;
    var look44 = (GeodeticToEcef(st44.SubSatLatDeg + 2.0, st44.SubSatLonDeg + 1.0, 0.0)
        - st44.PositionEcefKm).Normalized();
    double uncapped44 = BeamComposer.MaxCoChannelEirpDbw(set44.Beams, look44, set44.PowersDbw, colors44, n44);
    double cap1_44 = BeamComposer.MaxCoChannelEirpDbw(set44.Beams, look44, set44.PowersDbw, colors44, n44, 1);
    double cap3_44 = BeamComposer.MaxCoChannelEirpDbw(set44.Beams, look44, set44.PowersDbw, colors44, n44, 3);
    double capBig44 = BeamComposer.MaxCoChannelEirpDbw(set44.Beams, look44, set44.PowersDbw, colors44, n44, 100000);
    // Independent hand value for cap 1: the largest single weighted contribution
    // in any colour.
    double best1 = 0.0;
    for (int i = 0; i < set44.Beams.Count; i++)
    {
        if (set44.Beams[i].Weight <= 0.0) continue;
        double lin = set44.Beams[i].Weight * Math.Pow(10.0, (set44.PowersDbw[i] + set44.Beams[i].GainDbi(look44)) / 10.0);
        if (lin > best1) best1 = lin;
    }
    bool compOk44 = cap1_44 <= cap3_44 + 1e-9 && cap3_44 <= uncapped44 + 1e-9
        && Math.Abs(capBig44 - uncapped44) < 1e-9
        && Math.Abs(cap1_44 - 10.0 * Math.Log10(best1)) < 1e-9
        && cap3_44 < uncapped44 - 0.01;            // the cap genuinely bites here

    // (b) Enforcement: one satellite over a dense saturated grid would light
    // many beams per colour; with a capacity of 3 no colour exceeds it, and
    // without one some colour does -- so the check discriminates.
    var geo44 = ServiceGeography.Grid(st44.SubSatLatDeg - 8.0, st44.SubSatLatDeg + 8.0,
        st44.SubSatLonDeg - 10.0, st44.SubSatLonDeg + 10.0, 250.0);
    var ops44 = new OperatingParamsSet
        { SatName = "V44", LowFreqMhz = 19700, HighFreqMhz = 19700, ElevAngleHeaderDeg = 10.0 };
    int WorstColour44(PfdMaskViewModel scene)
    {
        var pointing = new ScenePointing(scene);
        var sched = new Scheduler(con44, geo44, ops44, pointing, dur44);
        var step = sched.Step(0.0);
        int worst = 0;
        foreach (var kv in step.ActiveBeams)
        {
            var rs = pointing.Resolve(con44.StateAt(0, 0.0, dur44));
            var count = new int[rs.CoChannelN ?? 1];
            foreach (int b in kv.Value) count[rs.ReuseColors![b]]++;
            worst = Math.Max(worst, count.Max());
        }
        return worst;
    }
    int freeWorst44 = WorstColour44(scene44);
    scene44.CoFrequencyBeamCapacity = 3;
    int cappedWorst44 = WorstColour44(scene44);
    bool enforceOk44 = freeWorst44 > 3 && cappedWorst44 <= 3 && cappedWorst44 > 0;

    // (c) Plumbing: profile -> codec -> composition -> scene -> resolved set,
    // and the scene clone the pointing works from carries it too.
    var prof44 = new OperationProfile(Name: "V44", CellKm: 900.0,
        Downlink: new DownlinkProfile(Aggregation: "cochannel", ReuseClusterIndex: 1,
            CoFrequencyBeamCapacity: 6));
    var back44 = OperationProfileCodec.Load(OperationProfileCodec.Save(prof44));
    var comp44 = OperationComposer.Compose(prof44, 1200.0);
    var clone44 = new PfdMaskViewModel(comp44.Scene.Coastlines);
    comp44.Scene.CopySettingsTo(clone44);
    var resolved44 = new ScenePointing(comp44.Scene).Resolve(st44);
    bool plumbOk44 = back44 == prof44
        && back44.Down.CoFrequencyBeamCapacity == 6
        && comp44.Scene.CoFrequencyBeamCapacity == 6
        && clone44.CoFrequencyBeamCapacity == 6
        && resolved44.CoFrequencyBeamCapacity == 6
        // and absent stays absent: no limit is declared by default
        && new OperationProfile(Name: "V44n").Down.CoFrequencyBeamCapacity is null
        && OperationComposer.Compose(new OperationProfile(Name: "V44n"), 1200.0)
            .Scene.CoFrequencyBeamCapacity is null;

    // (d) The exported mask: with the capacity declared, no cell is higher and
    // some cell is lower than without it.
    string dir44 = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(dir44);
    string Export44(int? cap)
    {
        var g = new PfdMaskViewModel(scene44.Coastlines);
        scene44.CopySettingsTo(g);
        g.CoFrequencyBeamCapacity = cap;
        string path = Path.Combine(dir44, cap is null ? "v44-free.xml" : "v44-cap.xml");
        var opts = new MaskXmlExportOptions
        {
            SatName = "V44", NtcId = 1, MaskId = 1,
            LowFreqMhz = 19700, HighFreqMhz = 19700, RefBwKHz = 40.0,
            LatMinDeg = 0.0, LatMaxDeg = 0.0, LatStepDeg = 10.0,
            BStepDeg = 15.0, CStepDeg = 15.0,
            Kind = MaskPlotKind.AzEl, Format = MaskExportFormat.Xml, OutputPath = path,
        };
        MaskXmlExport.GenerateAsync(new MaskExportSampler(g, opts), opts, null, CancellationToken.None)
            .GetAwaiter().GetResult();
        return File.ReadAllText(path);
    }
    static List<double> Cells44(string xml)
        => System.Text.RegularExpressions.Regex.Matches(xml, @"<pfd c=""[-0-9.]+"">(-?[0-9.]+)</pfd>")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
    var free44 = Cells44(Export44(null));
    var capd44 = Cells44(Export44(2));
    bool maskOk44 = free44.Count > 0 && free44.Count == capd44.Count
        && free44.Zip(capd44, (f, c) => c <= f + 1e-9).All(x => x)
        && free44.Zip(capd44, (f, c) => f > -999 && c < f - 0.01).Any(x => x);

    Check("V44 co-frequency beam capacity: top-K composite exact, scheduler enforces, plumbed, mask tighter",
        compOk44 && enforceOk44 && plumbOk44 && maskOk44,
        $"comp={compOk44} enforce={enforceOk44} plumb={plumbOk44} mask={maskOk44} " +
        $"free={uncapped44:F2} cap3={cap3_44:F2} cap1={cap1_44:F2} worstFree={freeWorst44} worstCapped={cappedWorst44} cells={free44.Count}");
}


// ---- V45: the declared notice flies the constellation's exact inter-plane phase ----
{
    // AddShell mirrored the Walker phasing only. A shell declared with an
    // exact inter-plane phase (STEAM-2: 1.9 deg) propagated one system and
    // filed another. The declared phase rows must be the propagated system's
    // own initial phases, for both phasing forms.
    bool PhasesAgree45(ConstellationShell sh)
    {
        var n45 = new SrsNotice { NtcId = 1, SatName = "V45" };
        n45.AddShell(sh);
        var c45 = new Constellation(new[] { sh });
        if (n45.Phases.Count != c45.Elements.Count) return false;
        for (int i = 0; i < n45.Phases.Count; i++)
        {
            var el = c45.Elements[i];
            double flown = ((el.TrueAnomalyDeg + el.ArgumentOfPerigeeDeg) % 360.0 + 360.0) % 360.0;
            double d = Math.Abs(n45.Phases[i].PhaseAngDeg - flown);
            if (Math.Min(d, 360.0 - d) > 1e-9) return false;
        }
        return true;
    }
    var exact45 = new ConstellationShell
    {
        AltitudeKm = 1150.0, InclinationDeg = 53.0, PlaneCount = 4, SatsPerPlane = 5,
        WalkerPhasingF = 0, InterPlanePhaseDeg = 1.9, NOrbits = 288,
    };
    var walker45 = new ConstellationShell
    {
        AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 3, SatsPerPlane = 4,
        WalkerPhasingF = 1, NOrbits = 288,
    };
    bool exactOk45 = PhasesAgree45(exact45);
    bool walkerOk45 = PhasesAgree45(walker45);
    // The exact phase shows in the declaration itself: plane 1 leads plane 0 by it.
    var nx45 = new SrsNotice { NtcId = 1, SatName = "V45" };
    nx45.AddShell(exact45);
    double lead45 = nx45.Phases[exact45.SatsPerPlane].PhaseAngDeg - nx45.Phases[0].PhaseAngDeg;
    bool leadOk45 = Math.Abs(lead45 - 1.9) < 1e-9;
    // The sat_oper reconstruction of a flat MAX_CO_FREQ array: midpoints between rows, poles at the ends.
    var set45 = new OperatingParamsSet();
    foreach (double lat in new[] { -45.0, -35.0, -25.0, -15.0, -5.0, 5.0, 15.0, 25.0, 35.0, 45.0 }) set45.MaxCoFreqByLat.Add((lat, 4));
    var bands45 = radians.beamlab.dataset.PackageBuilder.NearestReadBands(set45);
    bool bandsOk45 = bands45.Count == 10 && bands45[0].LatFr == -90.0 && bands45[0].LatTo == -40.0
        && bands45[4].LatFr == -10.0 && bands45[4].LatTo == 0.0 && bands45[9].LatFr == 40.0 && bands45[9].LatTo == 90.0
        && bands45.All(b => b.NbrOpSat == 4);
    Check("V45 declared notice flies the exact inter-plane phase; sat_oper is the nearest-read reconstruction",
        exactOk45 && walkerOk45 && leadOk45 && bandsOk45,
        $"exact={exactOk45} walker={walkerOk45} lead={lead45:F3} bands={bandsOk45}");
}


// ---- V46: mask consistency -- a given mask against the declared gates ----
{
    // Mask given, not derived: does it already carry the shaping the R set
    // declares? Lit inside the declared zone or below the declared floor is
    // the saturation-shaped mask beside operational gates (inconsistent, the
    // examination over-charges); dark beyond the gate makes the gate inert
    // (the STEAM-2B case). The filed STEAM-2B mask is the fixture: its notch
    // sits at 22 deg and its floor at 40 deg, so the declared pairs below have
    // known verdicts.
    string filed46 = @"c:\_3\mask ntc_id 317520389 mask_id 150 17700-20200 MHz.xml";
    if (File.Exists(filed46))
    {
        var dis46 = MaskDissect.Analyze(MaskXmlImport.Load(filed46), 1150.0);
        OperatingParamsSet Set46(double alphaDeg, double elevDeg)
        {
            var s = new OperatingParamsSet();
            var ex = new MinExcludeByOrbit { OrbId = 0 };
            foreach (double lat in new[] { -45.0, -35.0, -25.0, -15.0, -5.0, 5.0, 15.0, 25.0, 35.0, 45.0 })
            {
                ex.ByLat.Add((lat, alphaDeg));
                var el = new MinElevByLat { LatDeg = lat };
                el.ByAz.Add((0.0, elevDeg));
                s.MinElev.Add(el);
            }
            s.MinExclude.Add(ex);
            return s;
        }
        var asFiled46 = MaskConsistency.Check(dis46, Set46(22.0, 40.0));
        var wider46 = MaskConsistency.Check(dis46, Set46(30.0, 40.0));    // zone declared wider than the notch: lit inside it, hard edge
        var narrower46 = MaskConsistency.Check(dis46, Set46(10.0, 40.0)); // zone declared narrower: the mask is the tighter one
        var higher46 = MaskConsistency.Check(dis46, Set46(22.0, 50.0));   // floor declared above the plateau's edge: lit below it
        var lower46 = MaskConsistency.Check(dis46, Set46(22.0, 30.0));    // floor declared below: the mask is the tighter one
        int consistentAlpha46 = asFiled46.Rows.Count(r => r.Alpha == MaskConsistency.Verdict.Consistent);
        bool ok46 = asFiled46.Overall == MaskConsistency.Verdict.Consistent
            && wider46.Overall == MaskConsistency.Verdict.LitInside
            && narrower46.Overall == MaskConsistency.Verdict.MaskTighter
            && higher46.Overall == MaskConsistency.Verdict.LitInside
            && lower46.Overall == MaskConsistency.Verdict.MaskTighter
            && consistentAlpha46 >= 50;
        // A derived mask beside its own derived gates: the main-lobe edge of
        // beams gated at their boresight reaches inside the zone and below the
        // floor, and stops well short of the arc -- LIT INSIDE, never saturated.
        string derived46 = @"C:\Projects\radians.beamlab\dataset\margin\steam-2\steam-2.mask.lat10p0-ae1p0-svc-50to50.xml";
        string derivedOk46 = "derived mask not present, not tested";
        bool derivedFine46 = true;
        if (File.Exists(derived46))
        {
            var rec46 = MaskConsistency.Check(derived46, 1150.0, Set46(22.0, 40.0));
            var litA46 = rec46.Rows.Where(r => r.Alpha == MaskConsistency.Verdict.LitInside).ToList();
            derivedFine46 = rec46.Overall == MaskConsistency.Verdict.LitInside
                && litA46.Count > 0 && litA46.All(r => r.ReachAlpha > MaskConsistency.CellTolDeg && r.ReachAlpha < 22.0 - MaskConsistency.CellTolDeg)
                && !rec46.Rows.Any(r => r.Alpha == MaskConsistency.Verdict.Saturated || r.Elev == MaskConsistency.Verdict.Saturated);
            derivedOk46 = $"derived={rec46.Overall} litBlocks={litA46.Count} reachAlpha={(litA46.Count > 0 ? litA46.Min(r => r.ReachAlpha).ToString("F1") : "-")}";
        }
        Check("V46 mask consistency: filed notch consistent at its own gates, lit inside when a wider gate is declared, tighter when dark beyond it; a derived mask reads lit inside, not saturated",
            ok46 && derivedFine46,
            $"filed={asFiled46.Overall} wider={wider46.Overall} narrower={narrower46.Overall} higherFloor={higher46.Overall} lowerFloor={lower46.Overall} consistentAlphaBlocks={consistentAlpha46}; {derivedOk46}");
    }
    else Check("V46 mask consistency against declared gates", true, "filing not present, skipped");
}


// ---- V47: header and array are mutually exclusive; the nearest-row read is total ----
{
    // Design brief Sec. 3.8 / EPS V43 Sec. 6.7.2.2 (2026-09-07): a quantity is
    // filed as its array or as its header, never both; beyond an array's
    // outermost rows the nearest row is the outermost row, so a set with rows
    // and no header is complete everywhere. The withdrawn reading (array
    // inside its span, header outside) left the derived sets declaring
    // nothing at latitudes 50 and 60 and inflated the filed mask's margins
    // there by 4.2 and 0.9 dB.
    var arr47 = new OperatingParamsSet();
    foreach (double lat in new[] { -45.0, -35.0, -25.0, -15.0, -5.0, 5.0, 15.0, 25.0, 35.0, 45.0 })
    {
        var el = new MinElevByLat { LatDeg = lat };
        el.ByAz.Add((0.0, lat < 0 ? 42.0 : 44.0));
        arr47.MinElev.Add(el);
        arr47.MaxCoFreqByLat.Add((lat, lat < 0 ? 2 : 3));
        arr47.MinDurationByLat.Add((lat, lat < 0 ? 100 : 200));
    }
    bool inside47 = DeclaredConstraints.MinElevDeg(arr47, -12.0, 0.0) == 42.0 && DeclaredConstraints.MinElevDeg(arr47, 12.0, 0.0) == 44.0
        && DeclaredConstraints.MaxCoFreq(arr47, -12.0) == 2 && DeclaredConstraints.MaxCoFreq(arr47, 44.0) == 3
        && DeclaredConstraints.MinDurationSec(arr47, -44.0) == 100 && DeclaredConstraints.MinDurationSec(arr47, 45.0) == 200;
    // Outward: the outermost row governs every latitude beyond the table.
    bool outward47 = DeclaredConstraints.MinElevDeg(arr47, 60.0, 0.0) == 44.0 && DeclaredConstraints.MinElevDeg(arr47, -60.0, 0.0) == 42.0
        && DeclaredConstraints.MaxCoFreq(arr47, 60.0) == 3 && DeclaredConstraints.MaxCoFreq(arr47, -89.0) == 2
        && DeclaredConstraints.MinDurationSec(arr47, 90.0) == 200 && DeclaredConstraints.MinDurationSec(arr47, -90.0) == 100;
    // Header-only sets read the header everywhere; a single row is a global constant.
    var hdr47 = new OperatingParamsSet { ElevAngleHeaderDeg = 40.0, MaxCoFreqHeader = 4, MinDurationSecHeader = 300 };
    bool header47 = DeclaredConstraints.MinElevDeg(hdr47, 60.0, 0.0) == 40.0 && DeclaredConstraints.MaxCoFreq(hdr47, 12.0) == 4
        && DeclaredConstraints.MinDurationSec(hdr47, -70.0) == 300;
    var one47 = new OperatingParamsSet();
    one47.MaxCoFreqByLat.Add((0.0, 5));
    bool one47Ok = DeclaredConstraints.MaxCoFreq(one47, 89.0) == 5 && DeclaredConstraints.MaxCoFreq(one47, -89.0) == 5;
    // Both forms of a quantity: an invalid filing, reported by name; valid sets report nothing.
    var both47 = new OperatingParamsSet { ElevAngleHeaderDeg = 40.0, MaxCoFreqHeader = 4 };
    foreach (var el in arr47.MinElev) both47.MinElev.Add(el);
    foreach (var r in arr47.MaxCoFreqByLat) both47.MaxCoFreqByLat.Add(r);
    var conflicts47 = DeclaredConstraints.FormConflicts(both47);
    bool invalid47 = conflicts47.Count == 2
        && conflicts47.Any(c => c.StartsWith("min_elev")) && conflicts47.Any(c => c.StartsWith("max_co_freq"))
        && DeclaredConstraints.FormConflicts(arr47).Count == 0 && DeclaredConstraints.FormConflicts(hdr47).Count == 0;
    // MIN_EXCLUDE keeps its own rule: interpolated between rows, the end rows beyond them.
    var ex47 = new MinExcludeByOrbit { OrbId = 0 };
    ex47.ByLat.Add((-45.0, 20.0)); ex47.ByLat.Add((45.0, 24.0));
    arr47.MinExclude.Add(ex47);
    bool excl47 = Math.Abs(DeclaredConstraints.ExclusionAlphaDeg(arr47, 0.0, 0) - 22.0) < 1e-9
        && DeclaredConstraints.ExclusionAlphaDeg(arr47, 60.0, 0) == 24.0
        && DeclaredConstraints.ExclusionAlphaDeg(arr47, -60.0, 0) == 20.0;
    Check("V47 header and array: one form per quantity (both reported), the nearest-row read total (outermost row governs outward), a single row a global constant; MIN_EXCLUDE interpolates and clamps",
        inside47 && outward47 && header47 && one47Ok && invalid47 && excl47,
        $"inside={inside47} outward={outward47} header={header47} oneRow={one47Ok} invalid={invalid47} ({conflicts47.Count} named) exclusion={excl47}");
}


// ---- V48: a truth-only rerun reads an earlier run's declaration back exactly ----
{
    // The derivation measured exactly depth-stable, so a convergence pair for T
    // and E1 needs only the truth sweep and the examination. The loop's reuse
    // path reads the earlier run's R set and mask; it must read the set back
    // exactly, take the newest mask when several exist, and refuse a directory
    // that cannot name one set.
    string dir48 = Path.Combine(AppContext.BaseDirectory, "exp", "v48run");
    Directory.CreateDirectory(dir48);
    foreach (var f in Directory.GetFiles(dir48)) File.Delete(f);
    var set48 = new OperatingParamsSet
    {
        SatName = "V48", NtcId = 7, ParamId = 1, LowFreqMhz = 18150, HighFreqMhz = 18150,
        EsDensityPerKm2 = 0.0001, EsDistanceKm = 183, EsLatMinDeg = -50, EsLatMaxDeg = 50, MaxCoFreqSat = 59,
    };
    foreach (double lat in new[] { -45.0, -35.0, 35.0, 45.0 })
    {
        set48.MaxCoFreqByLat.Add((lat, 4));
        var el = new MinElevByLat { LatDeg = lat };
        el.ByAz.Add((0.0, 40.0)); el.ByAz.Add((360.0, 40.0));
        set48.MinElev.Add(el);
    }
    var ex48 = new MinExcludeByOrbit { OrbId = 0 };
    ex48.ByLat.Add((-45.0, 22.0)); ex48.ByLat.Add((45.0, 22.0));
    set48.MinExclude.Add(ex48);
    File.WriteAllText(Path.Combine(dir48, "v48.operparams.json"), OpParamsFileCodec.Save(OpParamsFileCodec.FromSet(set48)));
    File.WriteAllText(Path.Combine(dir48, "v48.mask.lat10p0-ae1p0.xml"), "<satellite_system/>");
    System.Threading.Thread.Sleep(30);
    File.WriteAllText(Path.Combine(dir48, "v48.mask.lat10p0-ae1p0-vnewer.xml"), "<satellite_system/>");
    var got48 = ComplianceLoop.LoadReusedDeclaration(dir48);
    bool setOk48 = got48.Set.MaxCoFreqByLat.Count == 4 && got48.Set.MinElev.Count == 4
        && DeclaredConstraints.MaxCoFreq(got48.Set, 60.0) == 4 && DeclaredConstraints.MinElevDeg(got48.Set, 60.0, 0.0) == 40.0
        && Math.Abs(DeclaredConstraints.ExclusionAlphaDeg(got48.Set, 0.0, 0) - 22.0) < 1e-9
        && got48.Set.MaxCoFreqSat == 59 && got48.Set.EsLatMinDeg == -50.0 && DeclaredConstraints.FormConflicts(got48.Set).Count == 0;
    bool maskOk48 = Path.GetFileName(got48.MaskPath) == "v48.mask.lat10p0-ae1p0-vnewer.xml";
    bool refuse48 = false;
    string empty48 = Path.Combine(AppContext.BaseDirectory, "exp", "v48empty");
    Directory.CreateDirectory(empty48);
    foreach (var f in Directory.GetFiles(empty48)) File.Delete(f);
    try { ComplianceLoop.LoadReusedDeclaration(empty48); }
    catch (InvalidOperationException ex) { refuse48 = ex.Message.Contains("exactly one"); }
    Check("V48 truth-only rerun: the reused declaration reads back exactly, the newest mask is taken, a directory without a set is refused",
        setOk48 && maskOk48 && refuse48, $"set={setOk48} mask={maskOk48} refuse={refuse48}");
}


// ---- V49: the section 3.8 sets -- header-only, arrays-only, and the invalid both-forms probe ----
{
    // Design brief Sec. 3.8: the dataset carries one header-only set and one
    // arrays-only set, both valid, and one set filing two quantities in both
    // forms with different values -- an invalid filing whose expectation is the
    // rejection. Set 22 is that probe and belongs to BL-D2 alone; BL-ALL reads
    // the D2 band through the valid arrays-only set 26, which carries set 22's
    // array values exactly.
    var s21 = radians.beamlab.dataset.DatasetGenerator.Set21(1);
    var s22 = radians.beamlab.dataset.DatasetGenerator.Set22(1);
    var s23 = radians.beamlab.dataset.DatasetGenerator.Set23(1);
    var s26 = radians.beamlab.dataset.DatasetGenerator.Set26(1);
    bool arraysOnly49 = DeclaredConstraints.FormConflicts(s21).Count == 0
        && s21.ElevAngleHeaderDeg is null && s21.MaxCoFreqHeader is null && s21.MinDurationSecHeader is null
        && s21.MinElev.Count > 0 && s21.MaxCoFreqByLat.Count > 0 && s21.MinDurationByLat.Count > 0;
    bool headerOnly49 = DeclaredConstraints.FormConflicts(s23).Count == 0
        && s23.ElevAngleHeaderDeg is not null && s23.MaxCoFreqHeader is not null
        && s23.MinElev.Count == 0 && s23.MaxCoFreqByLat.Count == 0 && s23.MinDurationByLat.Count == 0;
    var both49 = DeclaredConstraints.FormConflicts(s22);
    bool probe49 = both49.Count == 2 && both49.Any(c => c.StartsWith("min_elev")) && both49.Any(c => c.StartsWith("max_co_freq"))
        && s22.ElevAngleHeaderDeg == 5.0 && s22.MaxCoFreqHeader == 4
        && s22.MinElev.All(b => b.ByAz.All(r => r.ElevDeg == 10.0)) && s22.MaxCoFreqByLat.All(r => r.Value == 2);
    bool valid26 = DeclaredConstraints.FormConflicts(s26).Count == 0 && s26.ParamId == 26
        && s26.ElevAngleHeaderDeg is null && s26.MaxCoFreqHeader is null
        && s26.LowFreqMhz == s22.LowFreqMhz && s26.HighFreqMhz == s22.HighFreqMhz
        && s26.MinAngleAtEsDeg == s22.MinAngleAtEsDeg
        && s26.MaxCoFreqByLat.SequenceEqual(s22.MaxCoFreqByLat)
        && s26.MinElev.Count == s22.MinElev.Count
        && s26.MinElev.Zip(s22.MinElev, (a, b) => a.LatDeg == b.LatDeg && a.ByAz.SequenceEqual(b.ByAz)).All(x => x)
        && s26.MinExclude.Count == s22.MinExclude.Count;
    var all49 = radians.beamlab.dataset.DatasetGenerator.ParamsOf("BL-ALL");
    var d2_49 = radians.beamlab.dataset.DatasetGenerator.ParamsOf("BL-D2");
    bool cases49 = all49.Contains(26) && !all49.Contains(22) && d2_49.SequenceEqual(new[] { 22 });
    string rej49 = radians.beamlab.dataset.DatasetGenerator.RejectionText(s22, 17800, 18600);
    string diag49 = radians.beamlab.dataset.DatasetGenerator.Diagnostic(s22);
    bool rejection49 = rej49.Contains("REJECTION") && rej49.Contains("min_elev / elev_angle") && rej49.Contains("max_co_freq (array)")
        && rej49.Contains("6.7.2.2") && rej49.Contains(diag49) && rej49.Contains("No epfd CDF is expected")
        && diag49.StartsWith("INVALID operating-parameter set: filed in both header and array form");
    Check("V49 section 3.8 sets: 21 arrays-only valid, 23 header-only valid, 22 both forms (the probe, BL-D2 alone), 26 arrays-only twin of 22 for BL-ALL; the rejection record carries the diagnostic",
        arraysOnly49 && headerOnly49 && probe49 && valid26 && cases49 && rejection49,
        $"arraysOnly={arraysOnly49} headerOnly={headerOnly49} probe={probe49} set26={valid26} cases={cases49} rejection={rejection49}");
}

// ---- V50: the section 3.9 read-rule probes -- sets, reads, case assignment, notice/params agreement ----
{
    // Design brief Sec. 3.9: the nearest-read probe (set 27, MIN_ELEV rows at 20 N
    // and 40 N, victims at 25 N and 35 N), the interpolation probe (set 28,
    // all-orbits MIN_EXCLUDE rows whose interpolated values at 25/30/35 N differ
    // from both rows) and the sweep-grid disclosure probe (set 29, MAX_CO_FREQ 8
    // in a 2.5-degree band around 65 N). The sets are one form per quantity; the
    // reads below are what the Recommendation's rules resolve; every case's
    // notice lists exactly the sets its Masks database carries.
    var s27 = radians.beamlab.dataset.ReadRuleProbes.Set27(1);
    var s28 = radians.beamlab.dataset.ReadRuleProbes.Set28(1);
    var s29 = radians.beamlab.dataset.ReadRuleProbes.Set29(1);
    bool valid50 = new[] { s27, s28, s29 }.All(s => DeclaredConstraints.FormConflicts(s).Count == 0
        && s.ElevAngleHeaderDeg is null && s.MaxCoFreqHeader is null && s.MinDurationSecHeader is null
        && s.MinDurationByLat.Count == 0 && s.ParamId is >= 27 and <= 29);
    bool r1_50 = DeclaredConstraints.MinElevDeg(s27, 25.0, 0.0) == 10.0 && DeclaredConstraints.MinElevDeg(s27, 35.0, 90.0) == 55.0
        && DeclaredConstraints.MinElevDeg(s27, 0.0, 0.0) == 10.0 && DeclaredConstraints.MinElevDeg(s27, 70.0, 180.0) == 55.0
        && DeclaredConstraints.MaxCoFreq(s27, 35.0) == 3 && Math.Abs(DeclaredConstraints.ExclusionAlphaDeg(s27, 35.0, 7) - 8.0) < 1e-9
        && Math.Abs(radians.beamlab.dataset.ReadRuleProbes.Interpolated(35.0, 20.0, 10.0, 40.0, 55.0) - 43.75) < 1e-9
        && Math.Abs(radians.beamlab.dataset.ReadRuleProbes.Interpolated(25.0, 20.0, 10.0, 40.0, 55.0) - 21.25) < 1e-9;
    double A50(double lat) => DeclaredConstraints.ExclusionAlphaDeg(s28, lat, 3);
    bool r2_50 = Math.Abs(A50(25.0) - 8.0) < 1e-9 && Math.Abs(A50(30.0) - 10.0) < 1e-9 && Math.Abs(A50(35.0) - 12.0) < 1e-9
        && Math.Abs(A50(10.0) - 6.0) < 1e-9 && Math.Abs(A50(60.0) - 14.0) < 1e-9
        && DeclaredConstraints.MaxCoFreq(s28, 30.0) == 1 && DeclaredConstraints.MinElevDeg(s28, 30.0, 0.0) == 10.0;
    int C50(double lat) => DeclaredConstraints.MaxCoFreq(s29, lat);
    bool r3_50 = C50(64.0) == 8 && C50(65.0) == 8 && C50(66.0) == 8 && C50(63.0) == 1 && C50(67.0) == 1
        && C50(30.0) == 1 && C50(-70.0) == 1 && C50(70.0) == 1
        && radians.beamlab.dataset.ReadRuleProbes.R3SweepLats.Count() == 141;
    var names50 = radians.beamlab.dataset.DatasetGenerator.CaseNames;
    bool cases50 = names50.Contains("BL-R1") && names50.Contains("BL-R2") && names50.Contains("BL-R3")
        && radians.beamlab.dataset.DatasetGenerator.ParamsOf("BL-R1").SequenceEqual(new[] { 27 })
        && radians.beamlab.dataset.DatasetGenerator.ParamsOf("BL-R2").SequenceEqual(new[] { 28 })
        && radians.beamlab.dataset.DatasetGenerator.ParamsOf("BL-R3").SequenceEqual(new[] { 29 })
        && radians.beamlab.dataset.DatasetGenerator.NtcIdFor("BL-R3") == 900123479;
    // The notice's operating-parameter list must be the case's list for EVERY case
    // (BL-ALL's notice had kept set 22 after the case moved to 26).
    bool notice50 = names50.All(c => radians.beamlab.dataset.DatasetGenerator.BuildNotice(c).OperatingParamIds
        .SequenceEqual(radians.beamlab.dataset.DatasetGenerator.ParamsOf(c)));
    var m1 = radians.beamlab.dataset.ReadRuleProbes.MaskSpecR1; var m2 = radians.beamlab.dataset.ReadRuleProbes.MaskSpecR2; var m3 = radians.beamlab.dataset.ReadRuleProbes.MaskSpecR3;
    bool masks50 = m1.NotchAlphaDeg == 8.0 && m2.NotchAlphaDeg == 6.0 && m3.NotchAlphaDeg == 8.0
        && m1.TxDeltaDb < 0 && m2.TxDeltaDb < 0 && m3.TxDeltaDb < 0 && m1.BStepDeg == 2.0;
    Check("V50 section 3.9 probes: sets 27-29 one form per quantity; nearest read 10/55 at 25/35 N; interpolation 8/10/12 at 25/30/35 N; Nco 8 only in 63.75-66.25 N; cases and ntc ids; every notice lists its case's sets; probe masks notched and lowered",
        valid50 && r1_50 && r2_50 && r3_50 && cases50 && notice50 && masks50,
        $"valid={valid50} r1={r1_50} r2={r2_50} r3={r3_50} cases={cases50} notice={notice50} masks={masks50}");
}

// ---- V51: the section 3.10 consistency probe -- set 30, the case, the notice, and the family finding ----
{
    // Design brief Sec. 3.10: a set that declares shaping (exclusion zone, elevation
    // floor) beside masks whose values ignore it. Set 30 is one form per quantity
    // and reads as global constants; the case links the three saturated masks per
    // shell; and the family's own D2 masks, when present on disk at full grid,
    // grade SATURATED on exclusion against their declared zone (450 km cells make
    // an 8-degree boresight gate invisible) and CONSISTENT on elevation -- the
    // control's limit the probe record states.
    var s30 = radians.beamlab.dataset.ConsistencyProbe.Set30(1);
    bool set51 = DeclaredConstraints.FormConflicts(s30).Count == 0 && s30.ParamId == 30
        && s30.ElevAngleHeaderDeg is null && s30.MaxCoFreqHeader is null && s30.MinDurationByLat.Count == 0
        && Math.Abs(DeclaredConstraints.ExclusionAlphaDeg(s30, 45.0, 7) - 8.0) < 1e-9
        && DeclaredConstraints.MinElevDeg(s30, -60.0, 270.0) == 10.0 && DeclaredConstraints.MaxCoFreq(s30, 0.0) == 2
        && s30.MinAngleAtEsDeg == 2.5 && s30.LowFreqMhz == 17800 && s30.HighFreqMhz == 18600
        && MaskConsistency.DeclaredAlphaDeg(s30, 20.0) == 8.0 && MaskConsistency.DeclaredElevDeg(s30, 20.0) == 10.0;
    bool case51 = radians.beamlab.dataset.DatasetGenerator.CaseNames.Contains("BL-C1")
        && radians.beamlab.dataset.DatasetGenerator.ParamsOf("BL-C1").SequenceEqual(new[] { 30 })
        && radians.beamlab.dataset.DatasetGenerator.NtcIdFor("BL-C1") == 900123480
        && radians.beamlab.dataset.ConsistencyProbe.TxDeltaDb < 0 && radians.beamlab.dataset.ConsistencyProbe.SweepLatsDeg.Length == 7;
    var n51 = radians.beamlab.dataset.DatasetGenerator.BuildNotice("BL-C1");
    var links51 = n51.Scenarios.Single().PfdMaskLinks;
    bool notice51 = n51.OperatingParamIds.SequenceEqual(new[] { 30 })
        && n51.MaskInfo.Count(m => m.FMask == 'P') == 3 && links51.Count == 12
        && links51.Where(l => l.OrbId >= 1 && l.OrbId <= 4).All(l => l.MaskId == 14)
        && links51.Where(l => l.OrbId >= 5 && l.OrbId <= 10).All(l => l.MaskId == 15)
        && links51.Where(l => l.OrbId >= 11).All(l => l.MaskId == 16);
    // The family declares no exclusion zone (operator decision of 2026-09-13,
    // after the finding that its 450 km cells leave a boresight gate no trace
    // in the envelope): sets 21, 22, 25 and 26 read alpha0 = 0 everywhere, and a
    // family mask graded against its own set is CONSISTENT on both axes.
    var fam21 = radians.beamlab.dataset.DatasetGenerator.Set21(1);
    var fam25 = radians.beamlab.dataset.DatasetGenerator.Set25(1);
    var fam26 = radians.beamlab.dataset.DatasetGenerator.Set26(1);
    bool noZone51 = new[] { fam21, radians.beamlab.dataset.DatasetGenerator.Set22(1), fam25, fam26 }.All(s =>
        s.MinExclude.Count == 1 && s.MinExclude[0].OrbId == 0
        && new[] { -70.0, -30.0, 0.0, 45.0, 70.0 }.All(lat => DeclaredConstraints.ExclusionAlphaDeg(s, lat, 1) == 0.0));
    string outDsPath51 = Path.Combine(AppContext.BaseDirectory, "exp", "ds");   // the T-block's quick generation
    string fam51 = Path.Combine(outDsPath51, "BL-D2", "xml", "mask2_pfd_azel_shellA.xml");
    bool family51 = true; string famText51 = "family mask not generated, not graded";
    if (File.Exists(fam51))
    {
        var rep51 = MaskConsistency.Check(fam51, 1200.0, fam26);
        var lit51 = rep51.Rows.Where(r => r.Alpha != MaskConsistency.Verdict.Dark).ToList();
        family51 = rep51.Overall == MaskConsistency.Verdict.Consistent
            && lit51.All(r => r.Alpha == MaskConsistency.Verdict.Consistent && r.Elev == MaskConsistency.Verdict.Consistent);
        famText51 = $"mask2 vs set26: {rep51.Overall} over {lit51.Count} lit blocks";
    }
    // The delivered family mask (full profile, on disk) graded the same way: the
    // quick-profile mask composes fewer beams (its lit reach is 53.5 deg where the
    // delivered mask's is 2.7), so the family's own grade is pinned on the mask
    // that is actually delivered whenever it is present (operator, 2026-09-22).
    string delivered51 = @"C:\Projects\radians.beamlab\dataset\BL-D2\xml\mask2_pfd_azel_shellA.xml";
    bool deliveredOk51 = true; string delText51 = "delivered mask not present, not graded";
    if (File.Exists(delivered51))
    {
        var repD51 = MaskConsistency.Check(delivered51, 1200.0, fam26);
        var litD51 = repD51.Rows.Where(r => r.Alpha != MaskConsistency.Verdict.Dark).ToList();
        deliveredOk51 = repD51.Overall == MaskConsistency.Verdict.Consistent
            && litD51.All(r => r.Alpha == MaskConsistency.Verdict.Consistent && r.Elev == MaskConsistency.Verdict.Consistent);
        delText51 = $"delivered mask2 vs set26: {repD51.Overall} over {litD51.Count} lit blocks";
    }
    Check("V51 section 3.10 consistency probe: set 30 one form per quantity with global reads; BL-C1 links the saturated masks per shell; the family declares no exclusion zone and its own D2 mask, quick-profile and delivered, grades CONSISTENT on both axes against its set",
        set51 && case51 && notice51 && noZone51 && family51 && deliveredOk51,
        $"set={set51} case={case51} notice={notice51} noZone={noZone51} {famText51}; {delText51}");
}

// ---- V52: provenance primitives and the curves' direction check ----
{
    // The stamp's identity is SHA-256 (a known vector), the producer id is a
    // stable 8-hex-digit build key, and the direction check reads the two
    // curves at the same percentiles.
    string f52 = Path.Combine(AppContext.BaseDirectory, "exp", "v52.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(f52));
    File.WriteAllText(f52, "abc");
    bool sha52 = radians.beamlab.dataset.Provenance.Sha256Hex(f52) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    string id52 = radians.beamlab.dataset.Provenance.ProducerId();
    bool id52ok = id52.Length == 8 && id52.All(ch => Uri.IsHexDigit(ch)) && id52 == radians.beamlab.dataset.Provenance.ProducerId();
    bool line52 = radians.beamlab.dataset.Provenance.Line(true).Contains("profile quick") && radians.beamlab.dataset.Provenance.Line(false).Contains(id52);
    // Direction check on synthetic curves: E one bin above T everywhere holds; E below T at one percentile is a violation.
    var pct52 = new[] { 100.0, 50.0, 10.0, 1.0, 0.1, 0.0 };
    var t52 = new radians.beamlab.dataset.FamilyCurves.Curve("T", 1000, 0, -150.0, new[] { -170.0, -165.0, -160.0, -155.0, -150.0, -150.0 }, pct52);
    var eUp52 = new radians.beamlab.dataset.FamilyCurves.Curve("E", 1000, 0, -148.0, new[] { -168.0, -163.0, -158.0, -153.0, -148.0, -148.0 }, pct52);
    var eDown52 = new radians.beamlab.dataset.FamilyCurves.Curve("E", 1000, 0, -150.0, new[] { -170.0, -165.0, -161.0, -155.0, -150.0, -150.0 }, pct52);
    var okDir52 = radians.beamlab.dataset.FamilyCurves.DirectionCheck(t52, eUp52);
    var badDir52 = radians.beamlab.dataset.FamilyCurves.DirectionCheck(t52, eDown52);
    bool dir52 = okDir52.Violations == 0 && Math.Abs(okDir52.MinGapDb - 2.0) < 1e-9
        && badDir52.Violations >= 1 && badDir52.MinGapDb < 0;
    bool floor52 = radians.beamlab.dataset.FamilyCurves.Percentiles(5760).Min() >= 100.0 / 5760 && radians.beamlab.dataset.FamilyCurves.Percentiles(240).Min() >= 100.0 / 240;
    Check("V52 provenance and curves: SHA-256 known vector, stable 8-hex producer id, provenance line; the direction check holds on a curve above and reports a violation on a curve below; percentiles stop at the resolvable floor",
        sha52 && id52ok && line52 && dir52 && floor52, $"sha={sha52} id={id52ok} line={line52} dir={dir52} floor={floor52}");
}

// ---- V53: the two-body trial pair -- sets, notices, the pair's construction ----
{
    // The benign pair of the 11.32A trial: four systems (two variants), each an
    // arrays-only set with no zone, MIN_ELEV 10 and cap 2 over its declared span,
    // a notice with one shell, two pfd masks registered (az/el linked, alpha
    // stored), and the set linked; the two payloads at one boresight pfd.
    var tb = radians.beamlab.dataset.TwoBodyTrial.Systems;
    bool ids53 = tb.Select(s => s.NtcId).Distinct().Count() == 4 && tb.All(s => s.NtcId > 900123480)
        && tb.Select(s => s.ParamId).Distinct().Count() == 4 && tb.Select(s => s.Case).SequenceEqual(new[] { "TB-M", "TB-L", "TB-M2", "TB-L2" });
    bool sets53 = tb.All(s =>
    {
        var p = radians.beamlab.dataset.TwoBodyTrial.SetFor(s);
        return DeclaredConstraints.FormConflicts(p).Count == 0 && p.ElevAngleHeaderDeg is null && p.MaxCoFreqHeader is null
            && DeclaredConstraints.ExclusionAlphaDeg(p, 45.0, 1) == 0.0 && DeclaredConstraints.MinElevDeg(p, 20.0, 90.0) == 10.0
            && DeclaredConstraints.MaxCoFreq(p, -60.0) == 2 && p.EsLatMinDeg == s.EsLatMinDeg && p.EsLatMaxDeg == s.EsLatMaxDeg
            && p.LowFreqMhz == 19700 && p.HighFreqMhz == 20200;
    });
    bool span53 = tb[2].EsLatMinDeg == -30 && tb[2].EsLatMaxDeg == 30 && tb[3].EsLatMinDeg == 40 && tb[3].EsLatMaxDeg == 70
        && tb[0].EsLatMinDeg == -70 && tb[1].EsLatMaxDeg == 70;
    bool shells53 = tb[0].Shell.AltitudeKm == 8000 && tb[0].Shell.PlaneCount * tb[0].Shell.SatsPerPlane == 20 && tb[0].Shell.InclinationDeg == 45
        && ReferenceEquals(tb[1].Shell, radians.beamlab.dataset.DatasetGenerator.ShellA)
        && Math.Abs(tb[0].TxDeltaDb - tb[1].TxDeltaDb - 20.0 * Math.Log10(8000.0 / 1200.0)) < 1e-9;
    bool notices53 = tb.All(s =>
    {
        var n = radians.beamlab.dataset.TwoBodyTrial.BuildNotice(s);
        var sc = n.Scenarios.Single();
        return n.NtcId == s.NtcId && n.Orbits.Count == s.Shell.PlaneCount
            && n.MaskInfo.Count(m => m.FMask == 'P') == 2 && n.MaskInfo.Count(m => m.FMask == 'R') == 1
            && n.OperatingParamIds.SequenceEqual(new[] { s.ParamId })
            && sc.PfdMaskLinks.Count == 1 && sc.PfdMaskLinks[0].MaskId == radians.beamlab.dataset.TwoBodyTrial.AzElMaskId
            && sc.Frequencies.Single().FreqMinMhz == 19700;
    });
    Check("V53 two-body trial pair: four systems with distinct ids in 19.7-20.2 GHz; arrays-only sets with no zone, MIN_ELEV 10, cap 2 over the declared spans (variant 2 disjoint); MEO 20 sats at 8 000 km at the LEO's boresight pfd; notices register az/el + alpha masks and link the az/el one",
        ids53 && sets53 && span53 && shells53 && notices53,
        $"ids={ids53} sets={sets53} span={span53} shells={shells53} notices={notices53}");
}

// ---- V54: the alpha-form exclusion grading (MaskConsistency.CheckAlphaForm) ----
{
    // The alpha axis of an alpha/deltaLongitude mask IS the angle to the arc, so
    // its exclusion grade needs no geometry: a mask with a rule notch reads
    // CONSISTENT at its declared angle, the same payload without a notch reads
    // SATURATED against a claimed zone and CONSISTENT against none, and an
    // az/el mask is reported not applicable. Quick-grid probe masks (alpha
    // nodes every 30 deg) are enough to pin the thresholds.
    string dir54 = Path.Combine(AppContext.BaseDirectory, "exp", "v54");
    Directory.CreateDirectory(dir54);
    string notched54 = Path.Combine(dir54, "notch8.xml"), lit54 = Path.Combine(dir54, "notch0.xml");
    if (!File.Exists(notched54))
        radians.beamlab.dataset.DatasetGenerator.GenerateProbeMask(notched54, 11, new radians.beamlab.dataset.DatasetGenerator.ProbeMaskSpec(8.0, 10.0, -30.0, 8.0, 2.0), quick: true);
    if (!File.Exists(lit54))
        radians.beamlab.dataset.DatasetGenerator.GenerateProbeMask(lit54, 11, new radians.beamlab.dataset.DatasetGenerator.ProbeMaskSpec(0.0, 10.0, -30.0, 0.0, 2.0), quick: true);
    OperatingParamsSet Zone54(double alpha)
    {
        var s = new OperatingParamsSet { SatName = "V54", NtcId = 1, ParamId = 1, LowFreqMhz = 19700, HighFreqMhz = 20200 };
        radians.beamlab.dataset.ProbeExamination.WithMinElev(radians.beamlab.dataset.ProbeExamination.WithNco(s, 2), 10.0);
        return alpha > 0 ? radians.beamlab.dataset.ProbeExamination.WithAlpha(s, alpha) : s;
    }
    var mNotched54 = MaskXmlImport.Load(notched54);
    var mLit54 = MaskXmlImport.Load(lit54);
    var gNotched = MaskConsistency.CheckAlphaForm(mNotched54, Zone54(8.0));
    var gLitClaimed = MaskConsistency.CheckAlphaForm(mLit54, Zone54(8.0));
    var gLitNone = MaskConsistency.CheckAlphaForm(mLit54, Zone54(0.0));
    var litRows54 = gNotched.Rows.Where(r => r.Alpha != MaskConsistency.Verdict.Dark).ToList();
    bool notchedOk54 = gNotched.Overall == MaskConsistency.Verdict.Consistent && litRows54.Count > 0
        && litRows54.All(r => r.ReachAlpha >= 8.0 - MaskConsistency.CellTolDeg && r.Elev == MaskConsistency.Verdict.NotExercised);
    bool claimedOk54 = gLitClaimed.Overall == MaskConsistency.Verdict.Saturated
        && gLitClaimed.Rows.Where(r => r.Alpha != MaskConsistency.Verdict.Dark).All(r => r.ReachAlpha <= MaskConsistency.CellTolDeg);
    bool noneOk54 = gLitNone.Overall == MaskConsistency.Verdict.Consistent;
    string azel54 = Path.Combine(AppContext.BaseDirectory, "exp", "ds", "BL-D2", "xml", "mask2_pfd_azel_shellA.xml");
    bool naOk54 = !File.Exists(azel54) || MaskConsistency.CheckAlphaForm(MaskXmlImport.Load(azel54), Zone54(8.0)).Overall == MaskConsistency.Verdict.NotExercised;
    Check("V54 alpha-form exclusion grading: a rule-notched mask reads CONSISTENT at its declared angle (elevation not exercised); the unnotched payload reads SATURATED against a claimed 8 deg zone and CONSISTENT against none; an az/el mask is not applicable",
        notchedOk54 && claimedOk54 && noneOk54 && naOk54,
        $"notched={gNotched.Overall} claimed={gLitClaimed.Overall} none={gLitNone.Overall} azel={naOk54}");
}

// ---- T8: the two-body trial pair emits (quick profile) with its grades, span certificate and stamps ----
{
    string donorSrs8 = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 SRS.MDB";
    string donorMasks8 = @"C:\Projects\_EPFD\epfd-reference\Cases\S.1503-4\127520101 Masks.MDB";
    string dllDir8 = new[] { @"C:\Projects\_EPFD\radians\radians\dlls", @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0" }
        .FirstOrDefault(d => File.Exists(Path.Combine(d, "EpfdMasksApi64.dll")));
    if (File.Exists(donorSrs8) && File.Exists(donorMasks8) && dllDir8 is not null)
    {
        try
        {
            string out8 = Path.Combine(AppContext.BaseDirectory, "exp", "tb");
            if (Directory.Exists(out8)) Directory.Delete(out8, recursive: true);
            radians.beamlab.dataset.TwoBodyTrial.Generate(new radians.beamlab.dataset.DatasetOptions
            {
                DonorSrsPath = donorSrs8, DonorMasksPath = donorMasks8, EpfdMasksDllDir = dllDir8, OutDir = out8, Quick = true,
            });
            bool files8 = radians.beamlab.dataset.TwoBodyTrial.Systems.All(s =>
            {
                string d = Path.Combine(out8, s.Case);
                return File.Exists(Path.Combine(d, $"{s.NtcId} SRS.MDB")) && File.Exists(Path.Combine(d, $"{s.NtcId} Masks.MDB"))
                    && File.Exists(Path.Combine(d, "README.md")) && File.Exists(Path.Combine(d, "expected", "consistency.md"))
                    && File.Exists(Path.Combine(d, "expected", "provenance.md"))
                    && Directory.GetFiles(Path.Combine(d, "xml"), "*.xml").Length == 3;
            }) && File.Exists(Path.Combine(out8, "TB-README.md"));
            int Dark8(string c)
            {
                string txt = File.ReadAllText(Path.Combine(out8, c, "expected", "consistency.md"));
                var m = System.Text.RegularExpressions.Regex.Match(txt, @"az/el mask 1 \(mapped at \d+ km\): \*\*[^*]+\*\* -- [^\n]*?dark blocks (\d+) of (\d+)");
                return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
            }
            bool grades8 = radians.beamlab.dataset.TwoBodyTrial.Systems.All(s =>
                File.ReadAllText(Path.Combine(out8, s.Case, "expected", "consistency.md")).Contains("**CONSISTENT**"));
            int darkL2 = Dark8("TB-L2"), darkM2 = Dark8("TB-M2"), darkL = Dark8("TB-L");
            bool span8 = darkL2 > 0 && darkM2 == 0 && darkL == 0;
            bool pfd8 = File.ReadAllText(Path.Combine(out8, "TB-M", "expected", "consistency.md")).Contains("dB(W/(m2 MHz))")
                && File.ReadAllText(Path.Combine(out8, "TB-L", "README.md")).Contains("two-body-trial-plan.md");
            Check("T8 two-body trial pair (quick): four stamped cases with az/el + alpha masks and a set; every mask CONSISTENT against its set; the span certificate darkens the LEO's variant-2 rows and none of the MEO's; the READMEs cite the trial plan",
                files8 && grades8 && span8 && pfd8, $"files={files8} grades={grades8} darkL2={darkL2} darkM2={darkM2} darkL={darkL} pfd={pfd8}");
        }
        catch (Exception ex) { Check("T8 two-body trial pair generation", false, "exception: " + ex.Message); }
    }
    else Check("T8 two-body trial pair generation", true, "donor MDBs or EpfdMasksApi64.dll not present, skipped");
}

// ---- V55: the live-composition read reproduces the truth when nothing is declared ----
{
    // The decomposition's zero: the examination's selection over LIVE values
    // (E_sel) with no gate declared -- no zone, elevation floor 0, no cap, no
    // angular separation -- counts every visible satellite once at its live
    // pfd, which is exactly the truth's sum. Same comb, same scheduler
    // sequence, so the CDFs must agree bin for bin; the maxima to double
    // precision (the -1000 null of a dark satellite adds 1e-100 to a sum).
    var shells55 = new[] { new ConstellationShell { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 2, SatsPerPlane = 3 } };
    var con55 = new Constellation(shells55);
    var scene55 = new PfdMaskViewModel { AltitudeKm = 1200.0, FrequencyGHz = 19.7, MinElevDeg = 10.0, RefBwKHz = 40.0 };
    var geo55 = ServiceGeography.Grid(30.0, 60.0, -20.0, 20.0, 900.0);
    var enforced55 = new OperatingParamsSet { SatName = "V55", LowFreqMhz = 19700, HighFreqMhz = 19700, ElevAngleHeaderDeg = 10.0 };
    var nothing55 = new OperatingParamsSet { SatName = "V55", LowFreqMhz = 19700, HighFreqMhz = 19700, ElevAngleHeaderDeg = 0.0 };
    double dur55 = 60.0 * 90;
    var limits55 = new List<radlimits.LimitPoint> { new() { EPFD = -160, Perc = 5.0 }, new() { EPFD = -150, Perc = 1.0 } };
    bool ok55 = true; string det55 = "";
    foreach (double lat in new[] { 30.0, 50.0 })
    {
        var victim55 = new EpfdDownVictim { EsLatDeg = lat, EsLonDeg = 0.0, GsoLonDeg = 10.0, Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 19700.0, 1.0) };
        var truth55 = EpfdDown.Run(con55, new ScheduledPointing(con55, geo55, enforced55, scene55, dur55), victim55, 60.0, 90, limits55, dur55);
        var live55 = new radians.beamlab.checks.LiveCompositionRead(new ScheduledPointing(con55, geo55, enforced55, scene55, dur55));
        var esel55 = EpfdDownMask.Run(con55, live55, nothing55, victim55, 60.0, 90, limits55, dur55);
        var (eT, pT) = truth55.Accumulator.BuildCdf();
        var (eS, pS) = esel55.Accumulator.BuildCdf();
        bool binsEqual = eT.Length == eS.Length && pT.Length == pS.Length
            && eT.Zip(eS, (x, y) => x == y).All(x => x) && pT.Zip(pS, (x, y) => x == y).All(x => x);
        bool same = binsEqual && Math.Abs(truth55.MaxEpfdDb - esel55.MaxEpfdDb) < 1e-6;
        // And with a cap of 1 declared, E_sel must sit at or below T (selection removes, never adds, against live values).
        var capped55 = new OperatingParamsSet { SatName = "V55", LowFreqMhz = 19700, HighFreqMhz = 19700, ElevAngleHeaderDeg = 0.0, MaxCoFreqHeader = 1 };
        var eselCap55 = EpfdDownMask.Run(con55, new radians.beamlab.checks.LiveCompositionRead(new ScheduledPointing(con55, geo55, enforced55, scene55, dur55)), capped55, victim55, 60.0, 90, limits55, dur55);
        bool capBelow = eselCap55.MaxEpfdDb <= truth55.MaxEpfdDb + 1e-9;
        ok55 &= same && capBelow && truth55.MaxEpfdDb > -300;
        det55 += string.Create(CultureInfo.InvariantCulture, $"lat{lat:F0}: same={same} maxT={truth55.MaxEpfdDb:F3} maxSel={esel55.MaxEpfdDb:F3} cap1={eselCap55.MaxEpfdDb:F3} ");
    }
    Check("V55 live-composition read: with nothing declared the examination's selection over live values reproduces the truth bin for bin; with a cap of 1 it sits at or below it", ok55, det55.Trim());
}

// ---- V56: the parallel simulation is the sequential simulation bit for bit ----
{
    // Every parallel loop partitions work that is independent by construction
    // -- the satellites of a step, the cells of a schedule step, the steps of
    // a scheduler-free examination -- and reduces in the sequential order, so
    // the thread count must leave no trace. Compared as raw bits: the CDF
    // bins, maxima and quiet counts of epfd(down) over three victims with the
    // epfd(is) byproduct; the schedule's links, candidates and handover
    // counts under the Random policy (its seeded keys are drawn on one thread
    // in the original order); epfd(up); the mask examination (time chunks,
    // one constellation clone per worker) and the live-composition
    // examination (step by step, the scheduler inside it parallel).
    string expDir56 = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(expDir56);
    string mask56 = Path.Combine(expDir56, "v56mask.xml");
    File.WriteAllText(mask56, """
        <?xml version="1.0"?>
        <srs>
          <satellite_system sat_name="V56" ntc_id="1">
            <pfd_mask mask_id="1" low_freq_mhz="19700" high_freq_mhz="19700" refbw_khz="40" type="azimuth_elevation">
              <by_a a="0">
                <by_b b="-90"><pfd c="-90">-120</pfd><pfd c="0">-117</pfd><pfd c="90">-116</pfd></by_b>
                <by_b b="90"><pfd c="-90">-118</pfd><pfd c="0">-121</pfd><pfd c="90">-122</pfd></by_b>
              </by_a>
              <by_a a="50">
                <by_b b="-90"><pfd c="-90">-123</pfd><pfd c="0">-119</pfd><pfd c="90">-118</pfd></by_b>
                <by_b b="90"><pfd c="-90">-117</pfd><pfd c="0">-124</pfd><pfd c="90">-120</pfd></by_b>
              </by_a>
            </pfd_mask>
          </satellite_system>
        </srs>
        """);

    var shells56 = new[] { new ConstellationShell { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 3, SatsPerPlane = 4, OperationalFraction = 0.75 } };
    var scene56 = new PfdMaskViewModel { AltitudeKm = 1200.0, FrequencyGHz = 19.7, MinElevDeg = 10.0, RefBwKHz = 40.0 };
    var geo56 = ServiceGeography.Grid(20.0, 60.0, -30.0, 30.0, 700.0, demandLinks: 2);
    var decl56 = new OperatingParamsSet
    {
        SatName = "V56", LowFreqMhz = 19700, HighFreqMhz = 19700, ElevAngleHeaderDeg = 10.0,
        MaxCoFreqHeader = 2, MinAngleAtEsDeg = 5.0, MaxCoFreqSat = 6, MinAngleAtSatDeg = 2.0,
    };
    decl56.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 6.0), (60.0, 4.0) } });
    double step56 = 60.0; long steps56 = 45; double dur56 = step56 * steps56;
    var limits56 = new List<radlimits.LimitPoint> { new() { EPFD = -160, Perc = 5.0 }, new() { EPFD = -150, Perc = 1.0 } };
    EpfdDownVictim Victim56(double lat) => new()
    {
        EsLatDeg = lat, EsLonDeg = 0.0, GsoLonDeg = 10.0,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 19700.0, 1.0),
    };
    var isVictim56 = new EpfdGsoSatVictim
    {
        GsoLonDeg = 10.0, BoresightLatDeg = 40.0, BoresightLonDeg = 5.0,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APSREC408V01, 19700.0, null),
        GmaxDbi = 40.7, Phi3DbDeg = 1.55,
    };
    var esUp56 = new EpfdUpEsModel
    {
        PowerDbw = 12.0,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 19700.0, 0.65),
    };

    // One full pass at a given thread count, reduced to the bits of everything it produced.
    (List<long> Sig, bool Live, string Detail) Pass56(int degree)
    {
        SimulationParallel.MaxDegreeOfParallelism = degree;
        var sig = new List<long>();
        void Add(double v) => sig.Add(BitConverter.DoubleToInt64Bits(v));
        void AddResult(radcompute1503_2.EpfdAccumulator acc, double max, long quiet)
        {
            var (e, p) = acc.BuildCdf();
            sig.Add(e.Length);
            foreach (double v in e) Add(v);
            foreach (double v in p) Add(v);
            Add(max);
            sig.Add(quiet);
        }
        var con = new Constellation(shells56);

        var down = EpfdDown.RunMany(con,
            new ScheduledPointing(con, geo56, decl56, scene56, dur56, null, SelectionPolicy.Random),
            new[] { Victim56(30.0), Victim56(45.0), Victim56(55.0) }, step56, steps56, limits56, dur56, isVictim56);
        foreach (var r in down) AddResult(r.Accumulator, r.MaxEpfdDb, r.QuietSteps);
        AddResult(down[0].IsAccumulator!, down[0].MaxEpfdIsDb, down[0].IsQuietSteps);

        var sched = new Scheduler(con, geo56, decl56, new ScenePointing(scene56), dur56, null, SelectionPolicy.Random);
        long links = 0;
        for (int k = 0; k < 12; k++)
        {
            var st = sched.Step(k * step56);
            foreach (var l in st.Links)
            {
                sig.Add(l.CellId); sig.Add(l.SatelliteNumber); sig.Add(l.BeamIndex);
                Add(l.StartTimeSec); Add(l.ElevationDeg); Add(l.AlphaDeg);
            }
            foreach (var l in st.CandidateLinks)
            {
                sig.Add(l.CellId); sig.Add(l.SatelliteNumber); sig.Add(l.BeamIndex);
                Add(l.ElevationDeg); Add(l.AlphaDeg);
            }
            sig.Add(st.VoluntaryHandovers); sig.Add(st.ForcedHandovers); sig.Add(st.UnservedCellLinks);
            links += st.Links.Count;
        }

        var up = EpfdUp.Run(con, new Scheduler(con, geo56, decl56, new ScenePointing(scene56), dur56),
            geo56, isVictim56, esUp56, step56, steps56, limits56, dur56);
        AddResult(up.Accumulator, up.MaxEpfdDb, up.QuietSteps);

        var exam = EpfdDownMask.Run(con, MaskFootprint.LoadFile(mask56), decl56, Victim56(45.0),
            step56, steps56 * 4, limits56, dur56 * 4);
        AddResult(exam.Accumulator, exam.MaxEpfdDb, exam.QuietSteps);

        var live = new radians.beamlab.checks.LiveCompositionRead(
            new ScheduledPointing(con, geo56, decl56, scene56, dur56, null, SelectionPolicy.Random));
        var esel = EpfdDownMask.Run(con, live, decl56, Victim56(45.0), step56, steps56, limits56, dur56);
        AddResult(esel.Accumulator, esel.MaxEpfdDb, esel.QuietSteps);

        bool alive = down.Any(r => r.MaxEpfdDb > -300) && down[0].MaxEpfdIsDb > -300 && links > 0
            && up.MaxEpfdDb > -300 && exam.MaxEpfdDb > -300 && esel.MaxEpfdDb > -300;
        string detail = string.Create(CultureInfo.InvariantCulture,
            $"down={down[0].MaxEpfdDb:F3}/{down[1].MaxEpfdDb:F3}/{down[2].MaxEpfdDb:F3} is={down[0].MaxEpfdIsDb:F3} links={links} up={up.MaxEpfdDb:F3} exam={exam.MaxEpfdDb:F3} esel={esel.MaxEpfdDb:F3}");
        return (sig, alive, detail);
    }

    int degree56 = SimulationParallel.MaxDegreeOfParallelism;
    int wide56 = Math.Max(2, Environment.ProcessorCount);
    (List<long> Sig, bool Live, string Detail) seq56, par56;
    try
    {
        seq56 = Pass56(1);
        par56 = Pass56(wide56);
    }
    finally { SimulationParallel.MaxDegreeOfParallelism = degree56; }
    int firstDiff56 = -1;
    if (seq56.Sig.Count != par56.Sig.Count) firstDiff56 = Math.Min(seq56.Sig.Count, par56.Sig.Count);
    else for (int i = 0; i < seq56.Sig.Count; i++) if (seq56.Sig[i] != par56.Sig[i]) { firstDiff56 = i; break; }
    Check("V56 parallel simulation: every thread count reproduces the sequential run bit for bit -- epfd(down) with epfd(is), the Random-policy schedule, epfd(up), the mask examination over time chunks and the live-composition examination",
        firstDiff56 < 0 && seq56.Live && par56.Live,
        $"threads 1 vs {wide56}: {seq56.Sig.Count} values, first difference at {firstDiff56}; live={seq56.Live}; {par56.Detail}");
}

// ---- V57: the verdict rule -- the limit curve between its tabulated points ----
{
    // The design brief's rule (LimitCurveRule): PASS only if every tabulated point
    // passes AND the distribution nowhere crosses the log-linear curve between
    // the points. A distribution built to clear every tabulated point of the
    // 22-1B row and to bulge above the curve between the 1% and 0.286% points
    // must FAIL, with the crossing reported in the segment; the array scan must
    // name the same bin as the vendored accumulator's own scan; the curve margin
    // must be negative there and the rule margin never above the point margin.
    // A second distribution that sits under the curve must PASS with the rule
    // margin equal to the point margin.
    var pts57 = new List<radlimits.LimitPoint>
    {
        new() { EPFD = -175.4, Perc = 100.0 }, new() { EPFD = -175.4, Perc = 10.0 },
        new() { EPFD = -172.5, Perc = 1.0 }, new() { EPFD = -167.0, Perc = 0.286 },
        new() { EPFD = -164.0, Perc = 0.029 }, new() { EPFD = -164.0, Perc = 0.0 },
    };
    var curve57 = LimitCurveRule.Curve(pts57);
    radcompute1503_2.EpfdAccumulator Build57(double highDb)
    {
        var a = new radcompute1503_2.EpfdAccumulator(pts57);
        a.AccumulateSample(highDb, 6);      // 0.6% of the time at the chosen level
        a.AccumulateSample(-200.0, 994);    // the rest far below every point
        return a;
    }
    // (a) bulge between the 1% and 0.286% points: -169 dB, where the curve allows about 0.45%.
    var accCross = Build57(-169.0);
    var (eC, pC) = accCross.BuildCdf();
    var (pointsC, _) = accCross.CompareWithLimits(pts57);
    bool pointsPassC = pointsC.All(x => x);
    bool ruleC = LimitCurveRule.Pass(accCross, pts57);
    var scanC = LimitCurveRule.Scan(eC, pC, curve57, pts57);
    var ownC = accCross.FindWorstMaskViolation(pts57);
    bool sameBin = scanC is not null && ownC.HasValue && Math.Abs(scanC.EpfdDb - ownC.Value.Epfd) < 0.051
        && Math.Abs(scanC.ComputedPerc - ownC.Value.CalcPerc) < 1e-9 && Math.Abs(scanC.LimitPerc - ownC.Value.LimitPerc) < 1e-9;
    double pointMarginC = pts57.Min(l => ComplianceViewModel.MarginDb(eC, pC, l.EPFD, l.Perc));
    double curveMarginC = LimitCurveRule.CurveMarginDb(eC, pC, curve57, pts57);
    bool crossOk = pointsPassC && !ruleC && scanC is not null && scanC.EpfdDb > -172.5 && scanC.EpfdDb < -167.0
        && curveMarginC < 0.0 && Math.Min(pointMarginC, curveMarginC) <= pointMarginC + 1e-9;
    // (b) under the curve: -174 dB, where the curve allows about 3.3%.
    var accUnder = Build57(-174.0);
    var (eU, pU) = accUnder.BuildCdf();
    bool ruleU = LimitCurveRule.Pass(accUnder, pts57);
    var scanU = LimitCurveRule.Scan(eU, pU, curve57, pts57);
    double pointMarginU = pts57.Min(l => ComplianceViewModel.MarginDb(eU, pU, l.EPFD, l.Perc));
    double curveMarginU = LimitCurveRule.CurveMarginDb(eU, pU, curve57, pts57);
    // The curve margin is a whole-bin shift: shifting the distribution by exactly that much must
    // leave the curve clear, one more bin must cross it (mid-bin samples so binning cannot waver).
    var accMid = Build57(-173.95);
    var (eM, pM) = accMid.BuildCdf();
    double curveMarginMid = LimitCurveRule.CurveMarginDb(eM, pM, curve57, pts57);
    var (eAt, pAt) = Build57(-173.95 + curveMarginMid).BuildCdf();
    var (eOver, pOver) = Build57(-173.95 + curveMarginMid + 0.1).BuildCdf();
    bool shiftOk = curveMarginMid > 0.0 && LimitCurveRule.Scan(eAt, pAt, curve57, pts57) is null && LimitCurveRule.Scan(eOver, pOver, curve57, pts57) is not null;
    bool underOk = ruleU && scanU is null && curveMarginU >= 0.0 && shiftOk;
    // (c) the rule is named, with its tolerance.
    bool named = LimitCurveRule.Name().Contains("0.05 dB") && LimitCurveRule.Name().Contains("limit curve");
    Check("V57 verdict rule: a distribution clearing every tabulated point but crossing the log-linear curve between them FAILS, the array scan names the accumulator's own worst bin, the curve margin is negative and the rule margin never exceeds the point margin; one under the curve passes, and its curve margin is exactly the shift at which the curve is reached; the rule names its tolerance",
        crossOk && sameBin && underOk && named,
        string.Create(CultureInfo.InvariantCulture, $"cross: points={pointsPassC} rule={ruleC} scan={(scanC is null ? "none" : scanC.Text)} own={(ownC.HasValue ? ownC.Value.Epfd.ToString("F1", CultureInfo.InvariantCulture) : "none")} pointMargin={pointMarginC:+0.0;-0.0} curveMargin={curveMarginC:+0.0;-0.0}; under: rule={ruleU} scan={(scanU is null ? "none" : "crossing")} pointMargin={pointMarginU:+0.0;-0.0} curveMargin={curveMarginU:+0.0;-0.0} shiftTest={shiftOk} (curve margin mid-bin {curveMarginMid:+0.0;-0.0})"));
}

// ---- V58: the examination's time step, S.1503-4 Sec. D4 ----
{
    // Sec. D4.2 fine step and Sec. D4.7.1 coarse ratio against published
    // numbers: the reference tool's run report attached to WP 4A Doc 4A/509
    // (UK, October 2021) examines Skybridge -- the Res. 770 reference
    // constellation, 1469.155 km at 53 deg -- in four downlink runs at
    // 37.995 GHz with dishes of 0.45, 0.6, 2 and 9 m, and prints each run's
    // fine step and coarse multiplier: 289, 217, 65 and 14 ms; x19, x26, x86
    // and x391. The 3 dB beamwidth is 70 lambda/D, as in the vendored
    // antenna library. Then STEAM-2 by hand, several orbit types, and the
    // Sec. D4.1 reduction for long non-repeating runs.
    var inv58 = CultureInfo.InvariantCulture;
    var sky58 = new[] { new ConstellationShell { AltitudeKm = 1469.155, InclinationDeg = 53.0, PlaneCount = 20, SatsPerPlane = 4, StationKeeping = true } };
    var published58 = new (double Dish, double FineSec, int NCoarse)[] { (0.45, 0.289, 19), (0.6, 0.217, 26), (2.0, 0.065, 86), (9.0, 0.014, 391) };
    var got58 = published58.Select(p => S1503TimeStep.Downlink(sky58, radantenna.AntennaLibrary.Compute3dBDeg(37995.0, p.Dish))).ToList();
    bool skyOk58 = published58.Zip(got58).All(z => Math.Abs(z.Second.FineStepSec - z.First.FineSec) < 1e-9 && z.Second.NCoarse == z.First.NCoarse);
    // STEAM-2 with the 1 m dish of TABLE 22-1B at 18.2 GHz: 0.208 s and x20 by hand.
    double th58 = radantenna.AntennaLibrary.Compute3dBDeg(18200.0, 1.0);
    var steam58 = new[] { new ConstellationShell { AltitudeKm = 1150.0, InclinationDeg = 53.0, PlaneCount = 32, SatsPerPlane = 50 } };
    var planSteam58 = S1503TimeStep.Downlink(steam58, th58, 0.5 * 86400.0);
    bool steamOk58 = Math.Abs(planSteam58.FineStepSec - 0.208) < 1e-9 && planSteam58.NCoarse == 20 && !planSteam58.LongRunReduced;
    // Several orbit types: the smallest step governs; an elliptical shell steps at its operating height.
    var multi58 = new[]
    {
        new ConstellationShell { AltitudeKm = 1200.0, InclinationDeg = 55.0, PlaneCount = 4, SatsPerPlane = 8 },
        new ConstellationShell { AltitudeKm = 900.0, InclinationDeg = 90.0, PlaneCount = 4, SatsPerPlane = 8 },
        new ConstellationShell { AltitudeKm = 2400.0, InclinationDeg = 63.4, PlaneCount = 2, SatsPerPlane = 4, Eccentricity = 0.2, OperatingHeightKm = 1000.0 },
    };
    var planMulti58 = S1503TimeStep.Downlink(multi58, th58);
    double minPass58 = multi58.Min(s => S1503TimeStep.PassTimeSec(S1503TimeStep.StepAltitudeKm(s), s.InclinationDeg, th58));
    bool multiOk58 = Math.Abs(planMulti58.PassTimeSec - minPass58) < 1e-12 && planMulti58.GoverningAltitudeKm == 900.0
        && S1503TimeStep.StepAltitudeKm(multi58[2]) == 1000.0;
    // Sec. D4.1: a non-repeating run beyond 1e8 fine steps reduces N_hit to
    // 16 / min(N_coarse, sqrt(N_sat)); a station-kept one does not.
    double longRun58 = 1.2e8 * planSteam58.FineStepSec;
    var planLong58 = S1503TimeStep.Downlink(steam58, th58, longRun58);
    double nhitExp58 = 16.0 / Math.Min(20.0, Math.Sqrt(1600.0));
    bool longOk58 = planLong58.LongRunReduced && Math.Abs(planLong58.NhitUsed - nhitExp58) < 1e-12
        && Math.Abs(planLong58.FineStepSec - S1503TimeStep.RoundToMillisecond(planSteam58.PassTimeSec / nhitExp58)) < 1e-9
        && planLong58.NCoarse == Math.Max(1, (int)Math.Floor(nhitExp58 / 16.0 * 20));
    var planKept58 = S1503TimeStep.Downlink(steam58.Select(s => s with { StationKeeping = true }).ToArray(), th58, longRun58);
    longOk58 &= !planKept58.LongRunReduced;
    Check("V58 S.1503-4 Sec. D4 time step: the four published Skybridge downlink runs of Doc 4A/509 reproduced to the millisecond with their coarse multipliers; STEAM-2 0.208 s x20; the smallest step governs across orbit types, an elliptical shell at its operating height; the Sec. D4.1 long-run reduction for non-repeating orbits only",
        skyOk58 && steamOk58 && multiOk58 && longOk58,
        string.Create(inv58, $"skybridge {string.Join(", ", got58.Select(g => g.FineStepSec.ToString("0.000", inv58) + " s x" + g.NCoarse))}; steam-2 {planSteam58.FineStepSec:0.000} s x{planSteam58.NCoarse}; ")
        + string.Create(inv58, $"multi {planMulti58.FineStepSec:0.000} s at {planMulti58.GoverningAltitudeKm:F0} km; long run N_hit {planLong58.NhitUsed:0.###}, {planLong58.FineStepSec:0.000} s x{planLong58.NCoarse}, kept shell reduced={planKept58.LongRunReduced}"));
}

// ---- V59: the examination on the S.1503-4 time step ----
{
    // (a) With a plan whose fine step is the loop's own step and N_coarse = 1,
    // RunD4 is the loop's examination bin for bin in all three readings: the
    // shared per-step body and the Step 24 weights leave no trace.
    // (b) The dual chain on a hand pattern: the first sample fine, a coarse
    // step only after a non-critical sample and only with N_coarse fine steps
    // still ahead, weights summing to the fine-step count.
    // (c) On a real fine-step grid: every reading covers the same time, the
    // dual readings keep a subset of the samples, and the 30 dB reading,
    // whose region is the narrower, keeps no more samples than the Sec. D4.7.1
    // one.
    var inv59 = CultureInfo.InvariantCulture;
    string expDir59 = Path.Combine(AppContext.BaseDirectory, "exp");
    Directory.CreateDirectory(expDir59);
    string mask59 = Path.Combine(expDir59, "v59mask.xml");
    File.WriteAllText(mask59, """
        <?xml version="1.0"?>
        <srs>
          <satellite_system sat_name="V59" ntc_id="1">
            <pfd_mask mask_id="1" low_freq_mhz="19700" high_freq_mhz="19700" refbw_khz="40" type="azimuth_elevation">
              <by_a a="0">
                <by_b b="-90"><pfd c="-90">-120</pfd><pfd c="0">-117</pfd><pfd c="90">-116</pfd></by_b>
                <by_b b="90"><pfd c="-90">-118</pfd><pfd c="0">-121</pfd><pfd c="90">-122</pfd></by_b>
              </by_a>
              <by_a a="50">
                <by_b b="-90"><pfd c="-90">-123</pfd><pfd c="0">-119</pfd><pfd c="90">-118</pfd></by_b>
                <by_b b="90"><pfd c="-90">-117</pfd><pfd c="0">-124</pfd><pfd c="90">-120</pfd></by_b>
              </by_a>
            </pfd_mask>
          </satellite_system>
        </srs>
        """);
    var fp59 = MaskFootprint.LoadFile(mask59);
    var shells59 = new[] { new ConstellationShell { AltitudeKm = 1200.0, InclinationDeg = 53.0, PlaneCount = 3, SatsPerPlane = 4 } };
    var con59 = new Constellation(shells59);
    var decl59 = new OperatingParamsSet
    {
        SatName = "V59", LowFreqMhz = 19700, HighFreqMhz = 19700, ElevAngleHeaderDeg = 10.0,
        MaxCoFreqHeader = 2, MinAngleAtEsDeg = 5.0,
    };
    decl59.MinExclude.Add(new MinExcludeByOrbit { OrbId = 0, ByLat = { (0.0, 6.0), (60.0, 4.0) } });
    var victim59 = new EpfdDownVictim
    {
        EsLatDeg = 30.0, EsLonDeg = 0.0, GsoLonDeg = 10.0,
        Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, 19700.0, 1.0),
    };
    var limits59 = new List<radlimits.LimitPoint> { new() { EPFD = -160, Perc = 5.0 }, new() { EPFD = -150, Perc = 1.0 } };
    double step59 = 60.0; long steps59 = 240; double dur59 = step59 * steps59;
    bool Same59(EpfdDownResult x, EpfdDownResult y)
    {
        var (ex, px) = x.Accumulator.BuildCdf(); var (ey, py) = y.Accumulator.BuildCdf();
        return ex.SequenceEqual(ey) && px.SequenceEqual(py) && x.MaxEpfdDb.Equals(y.MaxEpfdDb) && x.QuietSteps == y.QuietSteps && x.Steps == y.Steps;
    }
    var loop59 = EpfdDownMask.Run(con59, fp59, decl59, victim59, step59, steps59, limits59, dur59);
    var asLoop59 = new S1503TimeStep.Plan(step59, 1, 0.0, 1.0, 16.0, 1200.0, 53.0, false);
    var d4a59 = EpfdDownMask.RunD4(con59, fp59, decl59, victim59, asLoop59, dur59, limits59);
    bool identOk59 = Same59(loop59, d4a59.FineOnly) && Same59(loop59, d4a59.Dual) && Same59(loop59, d4a59.DualMainBeamOnly)
        && d4a59.FineSteps == steps59 && d4a59.DualSamples == steps59;
    // (b) hand pattern, N_coarse 3, twelve fine steps
    var flags59 = new[] { false, false, false, true, false, false, false, false, false, false, false, false };
    var chain59 = EpfdDownMask.DualChain(flags59, 3);
    var expect59 = new List<(long, int)> { (0, 1), (3, 3), (4, 1), (7, 3), (10, 3), (11, 1) };
    bool chainOk59 = chain59.SequenceEqual(expect59) && chain59.Sum(c => c.Weight) == flags59.Length;
    // (c) the real S.1503-4 plan over a short run
    var plan59 = S1503TimeStep.Downlink(shells59, radantenna.AntennaLibrary.Compute3dBDeg(19700.0, 1.0));
    var d4c59 = EpfdDownMask.RunD4(con59, fp59, decl59, victim59, plan59, dur59, limits59);
    bool gridOk59 = d4c59.FineSteps == (long)Math.Round(dur59 / plan59.FineStepSec)
        && d4c59.Dual.Steps == d4c59.FineSteps && d4c59.DualMainBeamOnly.Steps == d4c59.FineSteps
        && d4c59.DualMainBeamOnlySamples <= d4c59.DualSamples && d4c59.DualSamples <= d4c59.FineSteps
        && d4c59.Dual.MaxEpfdDb <= d4c59.FineOnly.MaxEpfdDb && d4c59.DualMainBeamOnly.MaxEpfdDb <= d4c59.FineOnly.MaxEpfdDb;
    Check("V59 examination on the S.1503-4 time step: with the loop's own step and N_coarse 1 it is the loop's examination bin for bin in all three readings; the dual chain follows Sub-steps 6.1-6.3 with weights summing to the fine steps; on the real plan every reading spans the same time and the dual readings keep subsets of the fine samples",
        identOk59 && chainOk59 && gridOk59,
        string.Create(inv59, $"identical={identOk59} chain=[{string.Join(" ", chain59.Select(c => c.Index + ":" + c.Weight))}] plan {plan59.FineStepSec:0.000} s x{plan59.NCoarse}; ")
        + string.Create(inv59, $"samples dual {d4c59.DualSamples} / 30 dB {d4c59.DualMainBeamOnlySamples} / fine {d4c59.FineSteps}; max fine {d4c59.FineOnly.MaxEpfdDb:F1} vs loop {loop59.MaxEpfdDb:F1} dB"));
}

// ---- V60: the notice flags the parameter set it carries (AP4 A.4.b.6bis) ----
{
    // examset_type says which set the station is examined with: E for the
    // A.14.d operating-parameter sets (the S.1503-4 XML through mask_lnk3), L for
    // the single network-level set of A.4.b.6.a and A.4.b.7 in the SRS tables
    // (Doc 4A/663, Annex 1). A notice with sets derives E, one without L, an
    // explicit declaration wins; and the quick-generated BL notices, which all
    // carry sets, are written with E.
    var withSets60 = new SrsNotice { NtcId = 1, SatName = "V60" };
    withSets60.OperatingParamIds.Add(7);
    var noSets60 = new SrsNotice { NtcId = 2, SatName = "V60" };
    var forced60 = new SrsNotice { NtcId = 3, SatName = "V60", ExamSetTypeDeclared = 'L' };
    forced60.OperatingParamIds.Add(7);
    bool derive60 = withSets60.ExamSetType == 'E' && noSets60.ExamSetType == 'L' && forced60.ExamSetType == 'L';
    string srs60 = Path.Combine(AppContext.BaseDirectory, "exp", "ds", "BL-D1", "900123471 SRS.MDB");
    string written60 = "quick dataset not generated, not read back";
    bool written60Ok = true;
    if (File.Exists(srs60))
    {
        using var conn60 = new System.Data.OleDb.OleDbConnection($"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={srs60};Mode=Read");
        conn60.Open();
        using var cmd60 = new System.Data.OleDb.OleDbCommand("SELECT examset_type FROM non_geo WHERE ntc_id=900123471", conn60);
        string flag60 = Convert.ToString(cmd60.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
        using var cmd60b = new System.Data.OleDb.OleDbCommand("SELECT COUNT(*) FROM mask_lnk3 WHERE ntc_id=900123471", conn60);
        int sets60 = Convert.ToInt32(cmd60b.ExecuteScalar(), CultureInfo.InvariantCulture);
        written60Ok = flag60 == "E" && sets60 > 0;
        written60 = $"BL-D1 written with examset_type={flag60} beside {sets60} mask_lnk3 row(s)";
    }
    Check("V60 AP4 A.4.b.6bis: a notice carrying operating-parameter sets is flagged E (examined with its A.14.d sets), one without is flagged L, an explicit flag wins; the generated BL-D1 notice is written with E",
        derive60 && written60Ok,
        $"derived with={withSets60.ExamSetType} without={noSets60.ExamSetType} forced={forced60.ExamSetType}; {written60}");
}

// ---- V61: the truth's preset step, and the compliance window's examination step ----
{
    // Both windows preset the step to 1 s, the truth's step. The compliance
    // window's examination-step choice runs a declared-mask sweep on the
    // S.1503-4 time step -- exactly the dual rows RunD4ExamSweep gives for
    // the profile variant RunSweep examines -- keeps the sweep as before on
    // the predefined choice, and leaves a live-composition sweep (the
    // truth) on the predefined step whichever is chosen.
    var inv61 = CultureInfo.InvariantCulture;
    var cvm61 = new ComplianceViewModel();
    var svm61 = new SimulationViewModel();
    bool presetOk61 = cvm61.StepSecText == "1" && svm61.StepSecText == "1"
        && cvm61.ExamStepIndex == 0 && cvm61.ExamStepChoices.Count == 2;
    string exp61 = Path.Combine(AppContext.BaseDirectory, "exp");
    var doc61 = new OrbitDesignDocumentViewModel();
    doc61.Shells[0].PlaneCount = 1; doc61.Shells[0].SatsPerPlane = 2;
    string design61 = Path.Combine(exp61, "v61.orbitdesign.json");
    File.WriteAllText(design61, doc61.BuildDocumentJson());
    string mask61 = Path.Combine(exp61, "v61mask.xml");
    File.Copy(Path.Combine(exp61, "v59mask.xml"), mask61, overwrite: true);   // V59's 19.7 GHz az/el mask
    string profMask61 = Path.Combine(exp61, "v61mask.opprofile.json");
    File.WriteAllText(profMask61, OperationProfileCodec.Save(new OperationProfile(Name: "V61m", MinElevDeg: 10.0, CellKm: 900.0,
        Downlink: new DownlinkProfile(FootprintSource: "mask", MaskXmlPath: mask61))));
    string profComp61 = Path.Combine(exp61, "v61comp.opprofile.json");
    File.WriteAllText(profComp61, OperationProfileCodec.Save(new OperationProfile(Name: "V61c", MinElevDeg: 10.0, CellKm: 900.0)));
    ComplianceViewModel.Sweep Sweep61(string profPath) => new ComplianceViewModel
    {
        DesignPath = design61, ProfilePath = profPath,
        LatFromText = "30", LatToText = "40", LatStepText = "10",
        DurationDaysText = (60.0 / 1440.0).ToString(inv61), StepSecText = "60",
    }.BuildSweep();
    bool SameRows61(IReadOnlyList<ComplianceRow> x, IReadOnlyList<ComplianceRow> y) => x.Count == y.Count
        && x.Zip(y).All(z => z.First.LatDeg == z.Second.LatDeg && z.First.MaxEpfdDb.Equals(z.Second.MaxEpfdDb)
            && z.First.WorstMarginDb.Equals(z.Second.WorstMarginDb) && z.First.Pass == z.Second.Pass
            && z.First.QuietSteps == z.Second.QuietSteps && z.First.CurveMarginDb.Equals(z.Second.CurveMarginDb));
    // (a) a declared mask on the S.1503-4 step: the S.1503-4 examination's dual rows, with its plan
    var sweepM61 = Sweep61(profMask61);
    var (rowsD4n61, planD4n61) = ComplianceViewModel.RunOnExamStep(sweepM61, true);
    var profM61 = sweepM61.Profile with { AlphaByLat = null };
    var planRef61 = ComplianceViewModel.D4PlanFor(sweepM61, profM61);
    var refD4n61 = ComplianceViewModel.RunD4ExamSweep(sweepM61, profM61, planRef61).Select(r => r.Dual).ToList();
    bool d4Ok61 = planD4n61 is not null && planD4n61.FineStepSec == planRef61.FineStepSec
        && planD4n61.NCoarse == planRef61.NCoarse && SameRows61(rowsD4n61, refD4n61);
    // (b) a declared mask on the predefined step: the sweep as before
    var (rowsP61, planP61) = ComplianceViewModel.RunOnExamStep(sweepM61, false);
    bool predefOk61 = planP61 is null && SameRows61(rowsP61, ComplianceViewModel.RunSweep(sweepM61, sweepM61.Profile.AlphaExclDeg));
    // (c) the truth with the S.1503-4 choice made: still the predefined step
    var sweepC61 = Sweep61(profComp61);
    var (rowsC61, planC61) = ComplianceViewModel.RunOnExamStep(sweepC61, true);
    bool truthOk61 = planC61 is null && SameRows61(rowsC61, ComplianceViewModel.RunSweep(sweepC61, sweepC61.Profile.AlphaExclDeg));
    Check("V61 the truth's preset step is 1 s in both windows; the compliance window's S.1503-4 examination step gives the S.1503-4 examination's dual rows for a declared-mask profile, the predefined choice gives the sweep as before, and a live-composition sweep (the truth) stays on the predefined step",
        presetOk61 && d4Ok61 && predefOk61 && truthOk61,
        string.Create(inv61, $"preset {cvm61.StepSecText} s / {svm61.StepSecText} s, choice {cvm61.ExamStepIndex}; d4={d4Ok61} plan {(planD4n61 is null ? "none" : planD4n61.FineStepSec.ToString("0.000", inv61) + " s x" + planD4n61.NCoarse)}; ")
        + string.Create(inv61, $"predefined={predefOk61} truth={truthOk61}; rows {rowsD4n61.Count}/{rowsP61.Count}/{rowsC61.Count}"));
}

// ---- V62: the compliance sweep as the profile stands; margins under the rule; min_duration named ----
{
    // (a) Run sweep (and the console loop) sweep the profile as it stands,
    // its per-latitude exclusion rows included; RunSweep drops them for the
    // exclusion advisor's global walk. A 179 deg row at every service
    // latitude shuts every link out, so the two readings must differ.
    // (b) The advisors quote the margin under the limit-curve rule: the
    // walk's end margin is the rule margin of its final rows, and a lever
    // that moves only the curve margin still counts as moving.
    // (c) A declared min_duration is named, not examined silently.
    var inv62 = CultureInfo.InvariantCulture;
    string exp62 = Path.Combine(AppContext.BaseDirectory, "exp");
    var doc62 = new OrbitDesignDocumentViewModel();
    doc62.Shells[0].PlaneCount = 4; doc62.Shells[0].SatsPerPlane = 6;
    string design62 = Path.Combine(exp62, "v62.orbitdesign.json");
    File.WriteAllText(design62, doc62.BuildDocumentJson());
    var rows62 = new[] { 30.0, 40.0, 50.0, 60.0 }.Select(l => new ProfileLatRow(l, 179.0)).ToList();
    string prof62 = Path.Combine(exp62, "v62.opprofile.json");
    File.WriteAllText(prof62, OperationProfileCodec.Save(new OperationProfile(Name: "V62", MinElevDeg: 10.0,
        CellKm: 900.0, AlphaByLat: rows62)));
    var sweep62 = new ComplianceViewModel
    {
        DesignPath = design62, ProfilePath = prof62,
        LatFromText = "40", LatToText = "50", LatStepText = "10",
        DurationDaysText = "0.05", StepSecText = "60",
    }.BuildSweep();
    var (kept62, _) = ComplianceViewModel.RunOnExamStep(sweep62, false);
    var asStands62 = ComplianceViewModel.RunSweepProfile(sweep62, sweep62.Profile);
    var dropped62 = ComplianceViewModel.RunSweep(sweep62, sweep62.Profile.AlphaExclDeg);
    bool sameKept62 = kept62.Count == asStands62.Count && kept62.Zip(asStands62).All(z =>
        z.First.MaxEpfdDb.Equals(z.Second.MaxEpfdDb) && z.First.QuietSteps == z.Second.QuietSteps);
    bool rowsGate62 = kept62.All(r => r.QuietSteps == sweep62.Steps)
        && dropped62.Any(r => r.QuietSteps < sweep62.Steps);
    // (b) the walk's end margin, and a curve-only lever
    var cvmF62 = new ComplianceViewModel
    {
        DesignPath = design62, ProfilePath = Path.Combine(exp62, "v62plain.opprofile.json"),
        LatFromText = "40", LatToText = "50", LatStepText = "10",
        DurationDaysText = "0.05", StepSecText = "60", LimitsText = "-300 0.001\n-250 0.002",
    };
    File.WriteAllText(cvmF62.ProfilePath, OperationProfileCodec.Save(new OperationProfile(Name: "V62p", MinElevDeg: 10.0, CellKm: 900.0)));
    var adv62 = ComplianceViewModel.Advise(cvmF62.BuildSweep(), 5.0, 5.0);
    bool endRule62 = adv62.WorstMarginEndDb.Equals(adv62.FinalRows.Min(r => r.RuleMarginDb));
    var lats62 = new List<double> { 40.0, 50.0 };
    var curveLever62 = ComplianceViewModel.NcoAdviseCore(lats62, new[] { 3, 3 }, 1,
        caps => lats62.Select((l, i) => new ComplianceRow(l, -140.0, 1.0, caps[i] <= 1, 0) { CurveMarginDb = -caps[i] }).ToList());
    // (c) the track-duration note
    var withDur62 = new OperatingParamsSet { MinDurationSecHeader = 30 };
    bool noteOk62 = ComplianceViewModel.TrackDurationNote(withDur62).Contains("track-duration")
        && ComplianceViewModel.TrackDurationNote(new OperatingParamsSet()) == "";
    Check("V62 the compliance sweep runs the profile as it stands (its per-latitude exclusion rows gate the truth, the advisor's global walk drops them); the advisors quote the rule margin, a curve-only lever counts as moving; a declared min_duration is named",
        sameKept62 && rowsGate62 && endRule62 && curveLever62.LeverMoves && noteOk62,
        string.Create(inv62, $"kept=asStands {sameKept62}, rows gate {rowsGate62} (quiet kept {string.Join("/", kept62.Select(r => r.QuietSteps))} vs dropped {string.Join("/", dropped62.Select(r => r.QuietSteps))} of {sweep62.Steps}); ")
        + string.Create(inv62, $"walk end {adv62.WorstMarginEndDb:F1} = rule {endRule62}; curve-only lever moves {curveLever62.LeverMoves}; note {noteOk62}"));
}

// ---- V63: the R-set designer -- form conflicts shown, runs matched to their profile, identity kept ----
{
    var inv63 = CultureInfo.InvariantCulture;
    string exp63 = Path.Combine(AppContext.BaseDirectory, "exp");
    // (a) a quantity in both forms: the designer says so live, the runner refuses to fly it
    var vm63 = new OpParamsViewModel();
    vm63.MaxCoFreqHeaderText = "4";
    vm63.MaxCoFreqText = "25 4";
    bool liveOk63 = vm63.StatusText.Contains("INVALID filing") && vm63.FormConflictNote().Length > 0;
    var both63 = new OperatingParamsSet { SatName = "V63", MaxCoFreqHeader = 4 };
    both63.MaxCoFreqByLat.Add((25.0, 4));
    string both63Path = Path.Combine(exp63, "v63both.opparams.json");
    File.WriteAllText(both63Path, OpParamsFileCodec.Save(OpParamsFileCodec.FromSet(both63)));
    var doc63 = new OrbitDesignDocumentViewModel();
    doc63.Shells[0].PlaneCount = 1; doc63.Shells[0].SatsPerPlane = 2;
    string design63 = Path.Combine(exp63, "v63.orbitdesign.json");
    File.WriteAllText(design63, doc63.BuildDocumentJson());
    string prof63 = Path.Combine(exp63, "v63.opprofile.json");
    File.WriteAllText(prof63, OperationProfileCodec.Save(new OperationProfile(Name: "V63", CellKm: 900.0)));
    var sim63 = new SimulationViewModel { DesignPath = design63, ProfilePath = prof63, OpParamsPath = both63Path };
    bool runnerOk63;
    try { sim63.BuildSetup(); runnerOk63 = false; }
    catch (InvalidOperationException ex) { runnerOk63 = ex.Message.Contains("invalid filing"); }
    // (b) a run found by name counts only when its profile copy is this profile
    string root63 = Path.Combine(exp63, "v63repo");
    var p63 = new OperationProfile(Name: "shared name", CellKm: 900.0);
    string setPath63 = ComplianceViewModel.RunSetJsonPath(root63, p63);
    Directory.CreateDirectory(Path.GetDirectoryName(setPath63)!);
    string profPath63 = Path.Combine(root63, "mine.opprofile.json");
    File.WriteAllText(profPath63, OperationProfileCodec.Save(p63));
    File.WriteAllText(ComplianceViewModel.RunProfilePath(root63, p63),
        OperationProfileCodec.Save(p63 with { CellKm = 450.0 }));   // another system of the same name
    var runSet63 = new OperatingParamsSet { SatName = "DERIVED", NtcId = 0, ParamId = 1, LowFreqMhz = 18150, HighFreqMhz = 18150, MaxCoFreqSat = 45 };
    string runJson63 = OpParamsFileCodec.Save(OpParamsFileCodec.FromSet(runSet63));
    File.WriteAllText(setPath63, runJson63);
    File.SetLastWriteTimeUtc(profPath63, DateTime.UtcNow.AddMinutes(-10));
    File.SetLastWriteTimeUtc(setPath63, DateTime.UtcNow);
    bool otherOk63 = OpParamsViewModel.RunIsOfOtherProfile(root63, p63, profPath63)
        && OpParamsViewModel.LoopRunSetFor(root63, p63, profPath63) is null;
    File.WriteAllText(ComplianceViewModel.RunProfilePath(root63, p63), OperationProfileCodec.Save(p63));
    bool sameOk63 = !OpParamsViewModel.RunIsOfOtherProfile(root63, p63, profPath63)
        && OpParamsViewModel.LoopRunSetFor(root63, p63, profPath63) == setPath63;
    // (c) filling from a run keeps the filing's identity and band
    var fill63 = new OpParamsViewModel
    {
        SatName = "MINE", NtcIdText = "123", ParamIdText = "7", LowFreqText = "17800", HighFreqText = "18600",
    };
    fill63.FillFromRun(runJson63);
    bool keptOk63 = fill63.SatName == "MINE" && fill63.NtcIdText == "123" && fill63.ParamIdText == "7"
        && fill63.LowFreqText == "17800" && fill63.HighFreqText == "18600" && fill63.MaxCoFreqSatText == "45";
    Check("V63 R-set designer: a quantity in both forms is flagged live and refused by the runner; a loop run found by name counts only when its profile copy is this profile; filling from a run keeps the filing's identity and band",
        liveOk63 && runnerOk63 && otherOk63 && sameOk63 && keptOk63,
        $"live={liveOk63} runner={runnerOk63} other-profile={otherOk63} same-profile={sameOk63} identity kept={keptOk63}");
}

// ---- V64: an array-steered beam keeps its shape through a new peak gain ----
{
    // The scene builds array-steered UV beams as radially broadened
    // ellipticals; the pattern kind alone rebuilds the circular Taylor. Each
    // array beam carries its own factory: at its original gain it gives the
    // original pattern exactly, at a reduced gain the same shape.
    var sc64 = new SceneModel
    {
        PatternKind = BeamPatternKind.Taylor_1p4, AutoMode = true, UvArrayBeams = true,
        FrequencyGHz = 12.0, GmDbi = 35.0, ThetaBDeg = 4.0,
        MinElevDeg = 10.0, AltitudeKm = 1200.0, SubSatLatDeg = 0.0, SubSatLonDeg = 0.0,
    };
    sc64.RebuildBeams();
    var b64 = sc64.Beams.Where(b => b.Pattern is Rec1528_1p4_Ell).OrderByDescending(b => b.OffNadirDeg).First();
    var orig64 = (Rec1528_1p4_Ell)b64.Pattern;
    var restored64 = b64.PatternForGm?.Invoke(b64.OriginalGmDbi) as Rec1528_1p4_Ell;
    var reduced64 = b64.PatternForGm?.Invoke(b64.OriginalGmDbi - 3.0) as Rec1528_1p4_Ell;
    var probes64 = new[] { (0.5, 0.0), (1.5, 0.0), (1.5, 90.0), (3.0, 45.0), (8.0, 0.0) };
    bool restoredOk64 = restored64 is not null
        && probes64.All(p => restored64.GainAt(p.Item1, p.Item2).Equals(orig64.GainAt(p.Item1, p.Item2)));
    bool shapeOk64 = reduced64 is not null && Math.Abs(reduced64.Gm - (orig64.Gm - 3.0)) < 1e-12
        && reduced64.ThetaB.Equals(orig64.ThetaB) && reduced64.ThetaBTransverseDeg.Equals(orig64.ThetaBTransverseDeg);
    bool oldPathCircular64 = sc64.BuildPatternFor(b64.OriginalGmDbi - 3.0, b64.OffNadirDeg) is not Rec1528_1p4_Ell;
    bool othersNull64 = sc64.Beams.Where(b => b.Pattern is not Rec1528_1p4_Ell).All(b => b.PatternForGm is null);
    Check("V64 array-steered beams keep their radial broadening through a new peak gain: the beam's own factory restores the original pattern exactly and keeps the shape at a reduced gain, where the pattern kind alone would rebuild a circular Taylor",
        restoredOk64 && shapeOk64 && oldPathCircular64 && othersNull64,
        string.Create(CultureInfo.InvariantCulture, $"beam {b64.Name} off-nadir {b64.OffNadirDeg:F1} deg; restored={restoredOk64} shape={shapeOk64} (radial {orig64.ThetaB:F3} / transverse {orig64.ThetaBTransverseDeg:F3} deg); kind-only rebuild circular={oldPathCircular64}; other beams no factory={othersNull64}"));
}

// ---- V65: the orbit tab refuses to save an unfileable Case 2 shell; the builder refuses dropped mask links ----
{
    var doc65 = new OrbitDesignDocumentViewModel();
    var sh65 = doc65.Shells[0];
    bool case1Ok65 = doc65.SaveBlocker() is null;
    sh65.CaseChoice = 1;                       // Case 2 with the default candidate selected
    bool validOk65 = sh65.SelectedSolution is not null && doc65.SaveBlocker() is null;
    double keep65 = sh65.KeepRangeDeg;
    sh65.KeepRangeDeg = 1000.0;                // outside (0, max)
    string? badKeep65 = doc65.SaveBlocker();
    sh65.KeepRangeDeg = keep65;
    sh65.SelectedSolution = null;              // no candidate
    string? noCand65 = doc65.SaveBlocker();
    bool blockOk65 = badKeep65 is not null && badKeep65.Contains("keep_rnge")
        && noCand65 is not null && noCand65.Contains("no repeating candidate");
    // The builder: pfd/e.i.r.p. masks need a scenario frequency; an R set alone does not.
    string design65 = Path.Combine(AppContext.BaseDirectory, "exp", "v65.orbitdesign.json");
    File.WriteAllText(design65, new OrbitDesignDocumentViewModel().BuildDocumentJson());
    var b65 = new SnsBuilderViewModel { NtcId = 900555065, SatName = "V65SAT" };
    b65.AddShellFile(design65);
    b65.Masks.Add(new MaskEntry { MaskId = 1, FilePath = "a.xml", FMask = "P", FMaskType = "A", FreqMinMhz = 19700, FreqMaxMhz = 20200 });
    bool refused65;
    try { b65.BuildNotice(); refused65 = false; }
    catch (InvalidOperationException ex) { refused65 = ex.Message.Contains("no scenario frequency"); }
    var r65 = new SnsBuilderViewModel { NtcId = 900555066, SatName = "V65R" };
    r65.AddShellFile(design65);
    r65.Masks.Add(new MaskEntry { MaskId = 21, FilePath = "c.xml", FMask = "R", FreqMinMhz = 19700, FreqMaxMhz = 20200 });
    bool rOnlyOk65;
    try { rOnlyOk65 = r65.BuildNotice().OperatingParamIds.Count == 1; }
    catch { rOnlyOk65 = false; }
    Check("V65 the orbit tab refuses to save a Case 2 shell with no candidate or a keep range outside its bounds; the SNS builder refuses pfd masks with no scenario frequency, and an R set alone needs none",
        case1Ok65 && validOk65 && blockOk65 && refused65 && rOnlyOk65,
        $"case1={case1Ok65} valid case2={validOk65} blocked={blockOk65} ({badKeep65?.Split(" -- ")[0]} | {noCand65?.Split(" -- ")[0]}); builder refused={refused65} R only={rOnlyOk65}");
}

// ---- V66: the compliance loop in the window ----
{
    // RunLoop, which the window's Run loop button calls, runs the console
    // loop's steps through the shared ComplianceLoopSteps: it derives the
    // declaration on a saturated probe (or takes a given R set), sweeps the
    // truth of the profile as it stands, exports the reachable-envelope mask
    // into the run directory (reused on the next run from its cache name),
    // examines E1 against that declaration, adds E1 on the S.1503-4 step when
    // asked, and writes the run's profile and R set where the designer's
    // Derive & fill finds them. The console's helpers forward to the same code.
    var inv66 = CultureInfo.InvariantCulture;
    string exp66 = Path.Combine(AppContext.BaseDirectory, "exp");
    string root66 = Path.Combine(exp66, "v66repo");
    if (Directory.Exists(root66)) Directory.Delete(root66, true);
    Directory.CreateDirectory(root66);
    var doc66 = new OrbitDesignDocumentViewModel();
    doc66.Shells[0].PlaneCount = 4; doc66.Shells[0].SatsPerPlane = 6;
    string design66 = Path.Combine(root66, "v66.orbitdesign.json");
    File.WriteAllText(design66, doc66.BuildDocumentJson());
    var prof66 = new OperationProfile(Name: "V66 (window loop)", MinElevDeg: 10.0, CellKm: 900.0);
    string profPath66 = Path.Combine(root66, "v66.opprofile.json");
    File.WriteAllText(profPath66, OperationProfileCodec.Save(prof66));
    File.SetLastWriteTimeUtc(profPath66, DateTime.UtcNow.AddMinutes(-10));
    var sweep66 = new ComplianceViewModel
    {
        DesignPath = design66, ProfilePath = profPath66,
        LatFromText = "40", LatToText = "50", LatStepText = "10",
        DurationDaysText = "0.05", StepSecText = "60",
    }.BuildSweep();
    bool Same66(IReadOnlyList<ComplianceRow> x, IReadOnlyList<ComplianceRow> y) => x.Count == y.Count
        && x.Zip(y).All(z => z.First.MaxEpfdDb.Equals(z.Second.MaxEpfdDb) && z.First.WorstMarginDb.Equals(z.Second.WorstMarginDb)
            && z.First.QuietSteps == z.Second.QuietSteps);
    // (a) derived, first run: the mask is exported
    var r66 = ComplianceViewModel.RunLoop(sweep66, profPath66, null, root66, false);
    bool shapeOk66 = r66.Derived && r66.Truth.Count == 2 && r66.E1.Count == 2 && r66.E1OnS1503Step is null
        && r66.MaskNote.Contains("exported") && File.Exists(r66.MaskPath)
        && Path.GetDirectoryName(r66.MaskPath) == ComplianceViewModel.RunDir(root66, sweep66.Profile);
    bool stepsOk66 = Same66(r66.Truth, ComplianceViewModel.RunSweepProfile(sweep66, sweep66.Profile))
        && Same66(r66.E1, ComplianceLoopSteps.ExamineE1(sweep66, sweep66.Profile, r66.Declared, r66.MaskPath));
    string setPath66 = ComplianceViewModel.RunSetJsonPath(root66, sweep66.Profile);
    bool filesOk66 = File.Exists(setPath66) && File.Exists(ComplianceViewModel.RunProfilePath(root66, sweep66.Profile))
        && OpParamsViewModel.LoopRunSetFor(root66, sweep66.Profile, profPath66) == setPath66;
    // (b) a second run reuses the exported mask; a given R set is examined, not re-derived
    var given66 = OpParamsFileCodec.ToSet(OpParamsFileCodec.Load(File.ReadAllText(setPath66)));
    var g66 = ComplianceViewModel.RunLoop(sweep66, profPath66, given66, root66, false);
    bool givenOk66 = !g66.Derived && ReferenceEquals(g66.Declared, given66) && g66.MaskNote.Contains("reused")
        && g66.MaskPath == r66.MaskPath && Same66(g66.E1, r66.E1);
    // (c) E1 on the S.1503-4 step beside it
    var d66 = ComplianceViewModel.RunLoop(sweep66, profPath66, given66, root66, true);
    bool d4Ok66 = d66.Plan is not null && d66.E1OnS1503Step is { Count: 2 };
    // (d) the console's helpers are the shared ones
    bool fwdOk66 = ComplianceLoop.MaskCacheTag(10.0, 1.0, -50.0, 50.0) == ComplianceLoopSteps.MaskCacheTag(10.0, 1.0, -50.0, 50.0)
        && ComplianceLoop.ProducerId() == ComplianceLoopSteps.ProducerId();
    Check("V66 the compliance loop in the window: derives the declaration (or takes a given R set), sweeps the truth, exports and then reuses the reachable-envelope mask, examines E1 through the shared steps, adds E1 on the S.1503-4 step when asked, and writes the run's files where the designer finds them",
        shapeOk66 && stepsOk66 && filesOk66 && givenOk66 && d4Ok66 && fwdOk66,
        string.Create(inv66, $"shape={shapeOk66} steps={stepsOk66} files={filesOk66} given={givenOk66} d4={d4Ok66} forwarders={fwdOk66}; ")
        + string.Create(inv66, $"{ComplianceLoopSteps.E1Summary(r66.Truth, r66.E1, inv66)}; mask {Path.GetFileName(r66.MaskPath)}"));
}

Console.WriteLine($"\n===== {pass} passed, {fail} failed =====");
return fail == 0 ? 0 : 1;

/// <summary>Stand-in sampler for V40: a constant field, counting preparations.</summary>
internal sealed class ConstantSampler : IPfdMaskSampler
{
    private readonly double _pfd;
    public int Prepared { get; private set; }
    public ConstantSampler(double pfdDb) => _pfd = pfdDb;
    public void PrepareLatitude(double latDeg) => Prepared++;
    public double SampleMaxIn(double x, double y, double halfW, double halfH) => _pfd;
}
