using System;

namespace radians.beamlab;

/// <summary>
/// Per-beam radiation pattern interface used by the multi-beam composer.
/// All angles in degrees, all gains in dBi.
/// </summary>
public interface ISinglePattern
{
    /// <summary>Peak gain on boresight, dBi.</summary>
    double Gm { get; }

    /// <summary>Half the 3 dB beamwidth in the plane of interest, degrees.</summary>
    double ThetaB { get; }

    /// <summary>Far-out / null floor (dBi) below which Gain is clipped.</summary>
    double LF { get; }

    /// <summary>Gain at off-axis angle theta (degrees, 0..180).</summary>
    double Gain(double thetaDeg);

    /// <summary>
    /// Gain for an off-axis angle θ and an azimuth φ around the boresight (degrees).
    /// Circular patterns ignore φ. Elliptical patterns (Rec1528_1p4_Ell) use it.
    /// φ = 0 is the radial axis (Lr direction); φ = 90° is the transverse axis (Lt direction).
    /// </summary>
    double GainAt(double thetaDeg, double phiDeg) => Gain(thetaDeg);
}

/// <summary>
/// Recommendation 1.4 single-beam pattern (S.1528-1, 2025 revision):
/// circular Taylor illumination function. Each beam produces a realistic
/// (non-envelope) side-lobe pattern; multi-beam composition is the usual
/// incoherent power sum across beams.
///
/// F(u) = [2·J1(π·u) / (π·u)] · Π_{n=1..n̄−1} (1 − u²/u_n²) / (1 − u²/μ_n²)
/// G(θ) = Gm + 20·log10|F(u)|,   u = u_edge · sin(θ) / sin(θ_b)
///
/// where μ_n = j_{1,n}/π are the un-modified Bessel zeros, u_n =
/// σ·sqrt(A² + (n−½)²) are the Taylor replacement zeros, A = arccosh(10^(SLR/20))/π,
/// and σ = μ_{n̄}/sqrt(A² + (n̄−½)²). u_edge is solved numerically so that
/// |F|² = ½ at θ = θ_b.
/// </summary>
public sealed class Rec1528_1p4 : ISinglePattern
{
    public double Gm { get; }
    public double ThetaB { get; }
    public double SlrDb { get; }
    public int Nbar { get; }
    public double LF { get; }

    public double A_ { get; }
    public double Sigma { get; }

    private readonly double[] _mu;     // length nbar
    private readonly double[] _uN;     // length nbar - 1
    private readonly double _uEdge;
    private readonly double _sinThetaB;

    // First six positive zeros of J1, divided by π to give the un-modified Taylor μ_n.
    private static readonly double[] J1Zeros =
    {
        3.83170597020751, 7.01558666981562, 10.17346813506272,
        13.32369193631422, 16.47063005087764, 19.61585851046824,
    };

    public Rec1528_1p4(double gmDbi, double thetaBDeg, double slrDb = 20.0, int nbar = 4, double lfDbi = 0.0)
    {
        if (thetaBDeg <= 0) throw new ArgumentOutOfRangeException(nameof(thetaBDeg));
        if (slrDb <= 0) throw new ArgumentOutOfRangeException(nameof(slrDb));
        if (nbar < 2 || nbar > 6) throw new ArgumentOutOfRangeException(nameof(nbar));

        Gm = gmDbi;
        ThetaB = thetaBDeg;
        SlrDb = slrDb;
        Nbar = nbar;
        LF = lfDbi;

        A_ = Acosh(Math.Pow(10.0, slrDb / 20.0)) / Math.PI;
        _mu = new double[nbar];
        for (int i = 0; i < nbar; i++) _mu[i] = J1Zeros[i] / Math.PI;
        Sigma = _mu[nbar - 1] / Math.Sqrt(A_ * A_ + (nbar - 0.5) * (nbar - 0.5));

        _uN = new double[nbar - 1];
        for (int i = 0; i < nbar - 1; i++)
        {
            int n = i + 1;
            _uN[i] = Sigma * Math.Sqrt(A_ * A_ + (n - 0.5) * (n - 0.5));
        }

        _sinThetaB = Math.Sin(ThetaB * Math.PI / 180.0);
        _uEdge = SolveUat3dB();
    }

    public double Gain(double thetaDeg)
    {
        double theta = Math.Abs(thetaDeg);
        if (theta >= 90.0) return LF;
        double u = _uEdge * Math.Sin(theta * Math.PI / 180.0) / _sinThetaB;
        double f = TaylorF(u);
        double mag = Math.Max(1e-12, Math.Abs(f));
        double g = Gm + 20.0 * Math.Log10(mag);
        return Math.Max(g, LF);
    }

    /// <summary>The "u" coordinate corresponding to the half-3-dB beamwidth (where |F|²=½).</summary>
    public double UEdge => _uEdge;

    /// <summary>Taylor F(u). Public for diagnostics.</summary>
    public double TaylorF(double u)
    {
        if (u == 0.0) return 1.0;
        double pu = Math.PI * u;
        double kernel = 2.0 * BesselJ1.J1(pu) / pu;
        double prod = 1.0;
        for (int i = 0; i < Nbar - 1; i++)
        {
            double num = 1.0 - u * u / (_uN[i] * _uN[i]);
            double den = 1.0 - u * u / (_mu[i] * _mu[i]);
            // Near u = μ_n the kernel and the denominator share a simple zero
            // and their ratio is finite. Floor |denominator| so that the limit
            // is approximated rather than NaN — exact-null sampling is rare.
            const double eps = 1e-9;
            if (Math.Abs(den) < eps) den = den >= 0 ? eps : -eps;
            prod *= num / den;
        }
        return kernel * prod;
    }

    private double SolveUat3dB()
    {
        // |F| is positive and monotone-decreasing on [0, μ_1) starting at F(0)=1.
        // Bisect for F(u) = 1/√2.
        double target = 1.0 / Math.Sqrt(2.0);
        double lo = 0.0;
        double hi = _mu[0];
        for (int i = 0; i < 80; i++)
        {
            double mid = 0.5 * (lo + hi);
            double f = TaylorF(mid);
            if (f >= target) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    private static double Acosh(double x) => Math.Log(x + Math.Sqrt(x * x - 1.0));
}

/// <summary>
/// Recommendation 1.4 single-beam Taylor pattern (S.1528-1) — elliptical form.
/// Per Recommendation eq. 8 / 9, the pattern depends on a 2D u coordinate:
///
/// <code>
///   u = sqrt( (Lr·sinθ·cosφ)² + (Lt·sinθ·sinφ)² ) / λ
/// </code>
///
/// φ = 0 is the radial axis (Lr direction, along the off-nadir tilt for a typical
/// LEO non-GSO beam); φ = 90° is the transverse axis (Lt direction). For Lr = Lt
/// this reduces to the circular form <see cref="Rec1528_1p4"/>. The Taylor F(u)
/// kernel is identical; only the u-mapping differs. <see cref="ThetaB"/> reports
/// the half-3-dB beamwidth on the radial cut (φ = 0); for the transverse cut it is
/// θ_b · Lr/Lt.
/// </summary>
public sealed class Rec1528_1p4_Ell : ISinglePattern
{
    public double Gm { get; }
    public double LF { get; }
    public double SlrDb { get; }
    public int Nbar { get; }
    public double LrMeters { get; }
    public double LtMeters { get; }
    public double WavelengthM { get; }
    public double ThetaB { get; }   // Half-3-dB beamwidth on radial cut, deg
    public double ThetaBTransverseDeg { get; }   // Half-3-dB beamwidth on transverse cut, deg

    public double A_ { get; }
    public double Sigma { get; }

    private readonly double[] _mu;
    private readonly double[] _uN;
    private readonly double _uHalf;   // u value at which |F|² = ½

    private static readonly double[] J1Zeros =
    {
        3.83170597020751, 7.01558666981562, 10.17346813506272,
        13.32369193631422, 16.47063005087764, 19.61585851046824,
    };

    public Rec1528_1p4_Ell(double gmDbi, double wavelengthM, double lrMeters, double ltMeters,
                           double slrDb = 20.0, int nbar = 4, double lfDbi = 0.0)
    {
        if (wavelengthM <= 0) throw new ArgumentOutOfRangeException(nameof(wavelengthM));
        if (lrMeters <= 0) throw new ArgumentOutOfRangeException(nameof(lrMeters));
        if (ltMeters <= 0) throw new ArgumentOutOfRangeException(nameof(ltMeters));
        if (slrDb <= 0) throw new ArgumentOutOfRangeException(nameof(slrDb));
        if (nbar < 2 || nbar > 6) throw new ArgumentOutOfRangeException(nameof(nbar));

        Gm = gmDbi;
        LF = lfDbi;
        SlrDb = slrDb;
        Nbar = nbar;
        LrMeters = lrMeters;
        LtMeters = ltMeters;
        WavelengthM = wavelengthM;

        A_ = Acosh(Math.Pow(10.0, slrDb / 20.0)) / Math.PI;
        _mu = new double[nbar];
        for (int i = 0; i < nbar; i++) _mu[i] = J1Zeros[i] / Math.PI;
        Sigma = _mu[nbar - 1] / Math.Sqrt(A_ * A_ + (nbar - 0.5) * (nbar - 0.5));

        _uN = new double[nbar - 1];
        for (int i = 0; i < nbar - 1; i++)
        {
            int n = i + 1;
            _uN[i] = Sigma * Math.Sqrt(A_ * A_ + (n - 0.5) * (n - 0.5));
        }
        _uHalf = SolveUat3dB();

        // Half-3-dB beamwidth per principal cut: |F(u_half)|² = ½ ⇒
        //   radial: u = (Lr/λ)·sinθ ⇒ θ_b_rad = asin(u_half · λ / Lr)
        //   transverse: θ_b_tr = asin(u_half · λ / Lt)
        ThetaB = Math.Asin(Math.Clamp(_uHalf * WavelengthM / LrMeters, 0.0, 1.0)) * 180.0 / Math.PI;
        ThetaBTransverseDeg = Math.Asin(Math.Clamp(_uHalf * WavelengthM / LtMeters, 0.0, 1.0)) * 180.0 / Math.PI;
    }

    public double Gain(double thetaDeg) => GainAt(thetaDeg, 0.0); // radial cut by default

    public double GainAt(double thetaDeg, double phiDeg)
    {
        double theta = Math.Abs(thetaDeg);
        if (theta >= 90.0) return LF;
        double sinT = Math.Sin(theta * Math.PI / 180.0);
        double cosP = Math.Cos(phiDeg * Math.PI / 180.0);
        double sinP = Math.Sin(phiDeg * Math.PI / 180.0);
        double a = LrMeters * sinT * cosP;
        double b = LtMeters * sinT * sinP;
        double u = Math.Sqrt(a * a + b * b) / WavelengthM;
        double f = TaylorF(u);
        double mag = Math.Max(1e-12, Math.Abs(f));
        double g = Gm + 20.0 * Math.Log10(mag);
        return Math.Max(g, LF);
    }

    /// <summary>Taylor F(u). Identical to the circular pattern's kernel.</summary>
    public double TaylorF(double u)
    {
        if (u == 0.0) return 1.0;
        double pu = Math.PI * u;
        double kernel = 2.0 * BesselJ1.J1(pu) / pu;
        double prod = 1.0;
        for (int i = 0; i < Nbar - 1; i++)
        {
            double num = 1.0 - u * u / (_uN[i] * _uN[i]);
            double den = 1.0 - u * u / (_mu[i] * _mu[i]);
            const double eps = 1e-9;
            if (Math.Abs(den) < eps) den = den >= 0 ? eps : -eps;
            prod *= num / den;
        }
        return kernel * prod;
    }

    private double SolveUat3dB()
    {
        double target = 1.0 / Math.Sqrt(2.0);
        double lo = 0.0;
        double hi = _mu[0];
        for (int i = 0; i < 80; i++)
        {
            double mid = 0.5 * (lo + hi);
            double f = TaylorF(mid);
            if (f >= target) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    private static double Acosh(double x) => Math.Log(x + Math.Sqrt(x * x - 1.0));

    /// <summary>
    /// Convert an Annex-2-style cell description (half-axis angles α, β subtended at the
    /// satellite, edge-of-cell roll-off in dB) into Lr, Lt in metres, given λ.
    /// Roll-off table from Annex 2: 3 dB → K=0.51, 5 dB → K=0.64, 7 dB → K=0.74.
    /// Linearly interpolated outside the tabulated range.
    /// </summary>
    public static (double LrMeters, double LtMeters) DeriveLrLtFromCell(
        double alphaDeg, double betaDeg, double rollOffDb, double wavelengthM)
    {
        double k = AnnexTwoKFactor(rollOffDb);
        double lr = wavelengthM * k / Math.Sin(Math.Max(alphaDeg, 1e-3) * Math.PI / 180.0);
        double lt = wavelengthM * k / Math.Sin(Math.Max(betaDeg, 1e-3) * Math.PI / 180.0);
        return (lr, lt);
    }

    private static double AnnexTwoKFactor(double rollOffDb)
    {
        // Table 2 of Annex 2: (3, 0.51), (5, 0.64), (7, 0.74). Piecewise linear / clamped.
        if (rollOffDb <= 3.0) return 0.51;
        if (rollOffDb >= 7.0) return 0.74;
        if (rollOffDb <= 5.0) return 0.51 + (0.64 - 0.51) * (rollOffDb - 3.0) / 2.0;
        return 0.64 + (0.74 - 0.64) * (rollOffDb - 5.0) / 2.0;
    }
}

/// <summary>
/// Recommendation 1.2 single-beam envelope (S.1528-0), as implemented by
/// ITU APL APSREC409V01 — circular case (z = 1). Five branches taken as a
/// MAX, including a far-back-lobe floor at <c>0.25·Gmax</c>:
///
/// <code>
///   G1 = Gmax − 3·(ψ/ψb)^1.5         0 ≤ ψ &lt; a·ψb
///   G2 = Gmax + LN                   a·ψb ≤ ψ &lt; b·ψb
///   G3 = Gmax + LN − 25·log10(ψ/(b·ψb))  b·ψb ≤ ψ &lt; Y
///   G4 = LF                          Y ≤ ψ &lt; 90°
///   G5 = max(15 + LN + 0.25·Gmax, 0)  90° ≤ ψ ≤ 180°    (Table 1 z=1; for LN=−15 this simplifies to 0.25·Gmax)
///   G  = MAX(G1, G2, G3, G4, G5)
/// </code>
///
/// Constants from Table 1 (z=1): a = 2.58, b = 6.32. <c>Y = b·ψb·10^(0.04·(Gmax + LN − LF))</c>.
/// </summary>
public sealed class Rec1528_1p2 : ISinglePattern
{
    public double Gm { get; }
    public double ThetaB { get; }
    public double LN { get; }
    public double LF { get; }
    public double LB { get; }   // Back-lobe floor (90°..180°)
    public double Y  { get; }   // End of the −25·log10 region

    private const double A = 2.58;
    private const double B = 6.32;
    private const double Alpha = 1.5;

    public Rec1528_1p2(double gmDbi, double thetaBDeg, double lnDb = -15.0, double lfDbi = 0.0)
    {
        if (thetaBDeg <= 0) throw new ArgumentOutOfRangeException(nameof(thetaBDeg));
        Gm = gmDbi;
        ThetaB = thetaBDeg;
        LN = lnDb;
        LF = lfDbi;
        LB = Math.Max(15.0 + LN + 0.25 * Gm, 0.0);
        Y  = B * ThetaB * Math.Pow(10.0, 0.04 * (Gm + LN - LF));
    }

    public double Gain(double thetaDeg)
    {
        double t = Math.Abs(thetaDeg);
        if (t > 180.0) t = 180.0;

        // APL APSREC409V01 evaluates G = MAX(G1, G2, G3, G4, G5). G3 is
        // clamped so log10 ≥ 0: below b·ψb the formula extrapolates to +∞,
        // so we plateau it at the boundary value Gm + LN. G5 is gated to
        // ψ ≥ 90° so the back-lobe doesn't apply on the forward hemisphere.
        double bThetaB = B * ThetaB;
        double g1 = Gm - 3.0 * Math.Pow(t / ThetaB, Alpha);
        double g2 = Gm + LN;
        double g3 = (bThetaB > 0.0)
            ? Gm + LN - 25.0 * Math.Max(Math.Log10(Math.Max(t, 1e-9) / bThetaB), 0.0)
            : Gm + LN;
        double g4 = LF;
        double g5 = (t >= 90.0) ? LB : double.NegativeInfinity;
        return Math.Max(Math.Max(Math.Max(Math.Max(g1, g2), g3), g4), g5);
    }
}

/// <summary>
/// Recommendation 1.3 single-beam pattern (S.1528-0), per ITU APL
/// APSREC410V01 (MEO), APSREC411V01 (LEO), APSREC4XXV01 (HEO). Four branches
/// taken as a MAX:
///
/// <code>
///   G1 = Gmax − 3·(ψ/ψb)^1.5     0 ≤ ψ &lt; ψb
///   G2 = Gmax − 3·(ψ/ψb)^2       ψb ≤ ψ &lt; Y
///   G3 = Gmax + Ls − 25·log10(ψ/Y)  Y ≤ ψ &lt; Z
///   G4 = LF                       Z ≤ ψ &lt; 180°
///   G  = MAX(G1, G2, G3, G4)
/// </code>
///
/// Variant constants (orbit-specific):
/// <list type="bullet">
///   <item>LEO: Ls = −6.75 dB, Y = 1.5·ψb</item>
///   <item>MEO: Ls = −12 dB,    Y = 2·ψb</item>
///   <item>HEO: Ls = −20 dB,    Y = ψb·√(−Ls/3) = ψb·√(20/3) ≈ 2.582·ψb</item>
/// </list>
/// <c>Z = Y · 10^(0.04·(Gmax + Ls − LF))</c>.
/// </summary>
public sealed class Rec1528_1p3 : ISinglePattern
{
    public enum Variant { Leo, Meo, Heo }

    public double Gm { get; }
    public double ThetaB { get; }
    public double LF { get; }
    public Variant Kind { get; }
    public double Ls { get; }
    public double Y { get; }
    public double Z { get; }

    public Rec1528_1p3(double gmDbi, double thetaBDeg, Variant variant, double lfDbi = 0.0)
    {
        if (thetaBDeg <= 0) throw new ArgumentOutOfRangeException(nameof(thetaBDeg));
        Gm = gmDbi;
        ThetaB = thetaBDeg;
        LF = lfDbi;
        Kind = variant;
        (Ls, double yFactor) = variant switch
        {
            Variant.Leo => (-6.75, 1.5),
            Variant.Meo => (-12.0, 2.0),
            Variant.Heo => (-20.0, Math.Sqrt(20.0 / 3.0)),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        Y = yFactor * ThetaB;
        Z = Y * Math.Pow(10.0, 0.04 * (Gm + Ls - LF));
    }

    public double Gain(double thetaDeg)
    {
        double t = Math.Abs(thetaDeg);
        if (t > 180.0) t = 180.0;

        // APL APSREC410/411/4XX: G = MAX(G1, G2, G3, G4). G3 is clamped at
        // log10 ≥ 0 so it plateaus at Gm + Ls below ψ = Y instead of
        // extrapolating to +∞ near the boresight.
        double g1 = Gm - 3.0 * Math.Pow(t / ThetaB, 1.5);
        double g2 = Gm - 3.0 * Math.Pow(t / ThetaB, 2.0);
        double g3 = (Y > 0.0)
            ? Gm + Ls - 25.0 * Math.Max(Math.Log10(Math.Max(t, 1e-9) / Y), 0.0)
            : Gm + Ls;
        double g4 = LF;
        return Math.Max(Math.Max(Math.Max(g1, g2), g3), g4);
    }
}

/// <summary>Selectable beam-pattern model used to build a per-beam pattern from scene parameters.</summary>
public enum BeamPatternKind
{
    /// <summary>S.1528-1 §1.4 Taylor circular illumination.</summary>
    Taylor_1p4,
    /// <summary>S.1528-1 §1.4 Taylor elliptical illumination (Lr ≠ Lt).</summary>
    Taylor_1p4_Ell,
    /// <summary>S.1528-0 §1.2 envelope (APSREC409V01-style).</summary>
    Envelope_1p2,
    /// <summary>S.1528-0 §1.3 LEO (APSREC411V01).</summary>
    Leo_1p3,
    /// <summary>S.1528-0 §1.3 MEO (APSREC410V01).</summary>
    Meo_1p3,
    /// <summary>S.1528-0 §1.3 HEO (APSREC4XXV01).</summary>
    Heo_1p3,
}
