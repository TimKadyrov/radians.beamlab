using System;
using System.Collections.Generic;
using radians.beamlab;
using static radians.beamlab.GeoMath;

namespace radians.beamlab.app;

/// <summary>
/// Model object that owns the satellite state and beam set. The MainWindow
/// rebuilds this whenever the user edits a control. Per-beam pattern is the
/// S.1528-1 §1.4 Taylor circular illumination pattern.
/// </summary>
public sealed class SceneModel
{
    public const double SpeedOfLightKmPerSec = 299_792.458;

    /// <summary>Peak gain on boresight, dBi.</summary>
    public double GmDbi { get; set; } = 35.0;

    /// <summary>Half the 3 dB beamwidth in the plane of interest, deg.</summary>
    public double ThetaBDeg { get; set; } = 4.0;

    /// <summary>Centre frequency, GHz. Informational metadata; not used by the §1.4 pattern.</summary>
    public double FrequencyGHz { get; set; } = 1.5;

    /// <summary>Far-out side-lobe floor, dBi (also the §1.4 noise floor).</summary>
    public double LfDbi { get; set; } = 0.0;

    /// <summary>Side-lobe ratio (dB) for the §1.4 Taylor pattern. Positive.</summary>
    public double TaylorSlrDb { get; set; } = 20.0;

    /// <summary>Number of secondary lobes (n̄) for the §1.4 Taylor pattern, 2..6.</summary>
    public int TaylorNbar { get; set; } = 4;

    /// <summary>Which §1.x pattern model to construct for each beam.</summary>
    public BeamPatternKind PatternKind { get; set; } = BeamPatternKind.Taylor_1p4;

    /// <summary>Near-in side-lobe level (dB rel. peak) for the §1.2 envelope; ignored otherwise. APL uses −15 dB.</summary>
    public double LnDb { get; set; } = -15.0;

    /// <summary>§1.4 elliptical: half-radial cell axis subtended at the satellite, deg (Annex 2 α).</summary>
    public double EllAlphaDeg { get; set; } = 13.4;

    /// <summary>§1.4 elliptical: half-transverse cell axis subtended at the satellite, deg (Annex 2 β).</summary>
    public double EllBetaDeg { get; set; } = 13.4;

    /// <summary>§1.4 elliptical: edge-of-cell roll-off in dB (Annex 2 Table 2: 3 / 5 / 7 dB).</summary>
    public double EllRollOffDb { get; set; } = 3.0;

    /// <summary>
    /// Auto layout mode: when true, beam centres are placed on a tangent-plane hex lattice
    /// at the sub-satellite point with constant <see cref="CellRadiusKm"/> spacing. Works
    /// for any pattern kind. When pattern is §1.4 elliptical, each beam's α/β are also
    /// derived per-beam from <see cref="CellRadiusKm"/> + slant geometry so the ground
    /// footprint stays circular at <see cref="CellRadiusKm"/>; for other pattern kinds the
    /// pattern stays fixed and only the layout uses the cell-radius parameter.
    /// </summary>
    public bool AutoMode { get; set; } = false;

    /// <summary>Auto-mode: target ground-cell radius (km). Drives lattice constant s = R·√3 (full-coverage hex).</summary>
    public double CellRadiusKm { get; set; } = 350.0;

    /// <summary>Heatmap / probe aggregation mode.</summary>
    public HeatmapMode Mode { get; set; } = HeatmapMode.MaxSingleBeam;

    /// <summary>Satellite altitude (km above the spherical Earth surface).</summary>
    public double AltitudeKm { get; set; } = 500.0;

    /// <summary>Sub-satellite latitude (deg).</summary>
    public double SubSatLatDeg { get; set; } = 30.0;

    /// <summary>Sub-satellite longitude (deg).</summary>
    public double SubSatLonDeg { get; set; } = -75.0;

    /// <summary>
    /// Minimum user-elevation served by the outermost beam ring (deg, ground
    /// elevation angle from the local horizon). The outer ring's beams point
    /// at off-nadir = arcsin(R·cos(elev)/(R+h)). Default 25° = typical FSS
    /// service-edge elevation.
    /// </summary>
    public double MinElevDeg { get; set; } = 10.0;

    /// <summary>
    /// Crossover level (dB, negative) at which two adjacent beams meet. Drives
    /// the centre-to-centre angular spacing via the parabolic main-beam
    /// approximation G/G_m ≈ −3·(Δ/(2·θ_b))² → Δ = 2·θ_b·√(−L/3).
    /// −3 dB is the engineering-standard cross-over (default); the heatmap
    /// shows the composite power, which fills any vertex gaps.
    /// </summary>
    public double CrossoverDb { get; set; } = -3.0;

    /// <summary>Centre-to-centre angular spacing (deg) from <see cref="CrossoverDb"/> and <see cref="ThetaBDeg"/>. Circular case.</summary>
    public double SpacingDeg => 2.0 * ThetaBDeg * Math.Sqrt(Math.Max(0.0, -CrossoverDb) / 3.0);

    /// <summary>Per-cut half-3-dB beamwidths used by the layout. Radial = along off-nadir tilt; transverse = cross-track. Equal for circular patterns.</summary>
    public (double radialDeg, double transverseDeg) HalfBeamwidthsDeg => HalfBeamwidthsForBeam(0.0);

    /// <summary>
    /// Per-cut half-3-dB beamwidths for a beam at the given off-nadir angle. In manual elliptical mode this
    /// is the same for every beam; in auto mode α/β (and hence θb) vary per off-nadir.
    /// </summary>
    public (double radialDeg, double transverseDeg) HalfBeamwidthsForBeam(double offNadirDeg)
    {
        if (PatternKind == BeamPatternKind.Taylor_1p4_Ell)
        {
            var p = BuildEllipticalPatternFor(GmDbi, offNadirDeg);
            return (p.ThetaB, p.ThetaBTransverseDeg);
        }
        return (ThetaBDeg, ThetaBDeg);
    }

    /// <summary>
    /// In auto mode: half-axes (α, β, deg subtended at the satellite) of a circular ground cell of radius
    /// <see cref="CellRadiusKm"/> for a beam at off-nadir angle <paramref name="offNadirDeg"/>.
    /// In manual mode: returns (<see cref="EllAlphaDeg"/>, <see cref="EllBetaDeg"/>) regardless of off-nadir.
    /// </summary>
    public (double alphaDeg, double betaDeg) CellHalfAxesForBeam(double offNadirDeg)
    {
        if (!AutoMode) return (EllAlphaDeg, EllBetaDeg);

        double R = EarthRadiusKm;
        double r = R + AltitudeKm;
        double off = offNadirDeg * Math.PI / 180.0;
        double sinOff = Math.Sin(off);
        double cosOff = Math.Cos(off);
        // Slant range D from cosine rule in (Earth-centre, sat, ground-point) triangle.
        double disc = R * R - r * r * sinOff * sinOff;
        if (disc < 0) return (EllAlphaDeg, EllBetaDeg);              // beam misses Earth
        double D = r * cosOff - Math.Sqrt(disc);
        // Elevation ε at ground point: cos(ε) = (r/R)·sin(θ_off).
        double cosE = r * sinOff / R;
        cosE = Math.Min(1.0, Math.Max(0.0, cosE));
        double sinE = Math.Sqrt(1.0 - cosE * cosE);
        // Half-angles subtended at the satellite by a ground disc of radius R_cell:
        //   transverse (cross-track): β = atan(R_cell / D)
        //   radial (in SP-plane):     α = atan(R_cell · sin(ε) / D)   ← foreshortened by sin(ε)
        double beta  = Math.Atan(CellRadiusKm / D) * 180.0 / Math.PI;
        double alpha = Math.Atan(CellRadiusKm * sinE / D) * 180.0 / Math.PI;
        // Clamp α from going to zero near the horizon — keeps Lr finite.
        if (alpha < 0.5) alpha = 0.5;
        return (alpha, beta);
    }

    /// <summary>Inter-ring radial spacing (deg) — radial neighbours sit along the φ=0 cut, so this scales with θb_radial.</summary>
    public double SpacingRadialDeg
    {
        get { var (r, _) = HalfBeamwidthsDeg; return 2.0 * r * Math.Sqrt(Math.Max(0.0, -CrossoverDb) / 3.0); }
    }

    /// <summary>Within-ring azimuthal arc target (deg) — within-ring neighbours sit along the φ=90° cut, so this scales with θb_transverse.</summary>
    public double SpacingTransverseDeg
    {
        get { var (_, t) = HalfBeamwidthsDeg; return 2.0 * t * Math.Sqrt(Math.Max(0.0, -CrossoverDb) / 3.0); }
    }

    /// <summary>Off-nadir cone angle (deg) of the outermost ring, derived from MinElevDeg + altitude.</summary>
    public double OuterOffNadirDeg
    {
        get
        {
            double R = EarthRadiusKm;
            double r = R + AltitudeKm;
            double elev = MinElevDeg * Math.PI / 180.0;
            double s = R * Math.Cos(elev) / r;
            if (s >= 1.0) s = 1.0; // clamp at horizon
            return Math.Asin(s) * 180.0 / Math.PI;
        }
    }

    /// <summary>Wavelength in m derived from <see cref="FrequencyGHz"/>.</summary>
    public double WavelengthM => SpeedOfLightKmPerSec / (FrequencyGHz * 1e6);

    /// <summary>
    /// Half 3-dB beamwidth (deg) derived from <see cref="GmDbi"/> using the
    /// textbook gain–beamwidth relation G ≈ 10·log10(K/(2θ_b)²) with K ≈ 32 400
    /// (typical taper). Solved: θ_b ≈ 90°·10^(−G/20).
    /// </summary>
    public double DerivedHalfBeamwidthDeg => 90.0 * Math.Pow(10.0, -GmDbi / 20.0);

    private readonly List<Beam> _beams = new();

    /// <summary>Read-only view of the currently built beam set (mutable per-beam state — Weight, Pattern — is fine to change in place).</summary>
    public IReadOnlyList<Beam> Beams => _beams;

    public Vec3 SatEcef =>
        GeodeticToEcef(SubSatLatDeg, SubSatLonDeg, AltitudeKm);

    /// <summary>
    /// Build a fresh single-beam pattern instance for the given peak gain,
    /// using the currently selected <see cref="PatternKind"/> and all the
    /// shared parameters (θ_b, LF, plus pattern-specific LN / Ls / SLR / n̄).
    /// Used by the rebuilder, by the PFD adjuster (to reduce G_m), and by the
    /// "Show pattern" plot.
    /// </summary>
    public ISinglePattern BuildPattern(double gmDbi) => BuildPatternFor(gmDbi, 0.0);

    /// <summary>
    /// Build a fresh single-beam pattern for a beam at the given off-nadir angle. Most pattern kinds
    /// don't depend on off-nadir; the elliptical §1.4 in auto mode uses it to derive per-beam α, β.
    /// </summary>
    public ISinglePattern BuildPatternFor(double gmDbi, double offNadirDeg)
    {
        // Circular patterns: in auto-mode the user's manual θ_b drives both the antenna
        // pattern and the UV-plane hex lattice spacing (single parameter for circular
        // antennas). In manual mode same value also applies. R_cell only affects the
        // elliptical pattern (where each beam adapts per-beam to match it).
        double thetaB = ThetaBDeg;
        return PatternKind switch
        {
            BeamPatternKind.Envelope_1p2  => new Rec1528_1p2(gmDbi, thetaB, LnDb, LfDbi),
            BeamPatternKind.Leo_1p3       => new Rec1528_1p3(gmDbi, thetaB, Rec1528_1p3.Variant.Leo, LfDbi),
            BeamPatternKind.Meo_1p3       => new Rec1528_1p3(gmDbi, thetaB, Rec1528_1p3.Variant.Meo, LfDbi),
            BeamPatternKind.Heo_1p3       => new Rec1528_1p3(gmDbi, thetaB, Rec1528_1p3.Variant.Heo, LfDbi),
            BeamPatternKind.Taylor_1p4_Ell => BuildEllipticalPatternFor(gmDbi, offNadirDeg),
            _                             => new Rec1528_1p4(gmDbi, thetaB, TaylorSlrDb, TaylorNbar, LfDbi),
        };
    }

    /// <summary>Slant range (km) to the ground footprint at the given off-nadir angle. Returns the spherical-Earth solution.</summary>
    public double SlantRangeForOffNadirKm(double offNadirDeg)
    {
        double R = EarthRadiusKm;
        double r = R + AltitudeKm;
        double off = offNadirDeg * Math.PI / 180.0;
        double sinOff = Math.Sin(off);
        double cosOff = Math.Cos(off);
        double disc = R * R - r * r * sinOff * sinOff;
        if (disc < 0) return AltitudeKm;            // beam misses Earth (above horizon)
        return r * cosOff - Math.Sqrt(disc);
    }

    private Rec1528_1p4_Ell BuildEllipticalPatternFor(double gmDbi, double offNadirDeg)
    {
        var (alpha, beta) = CellHalfAxesForBeam(offNadirDeg);
        var (lr, lt) = Rec1528_1p4_Ell.DeriveLrLtFromCell(alpha, beta, EllRollOffDb, WavelengthM);
        return new Rec1528_1p4_Ell(gmDbi, WavelengthM, lr, lt, TaylorSlrDb, TaylorNbar, LfDbi);
    }


    /// <summary>
    /// Build the beam set as concentric rings, working from the outer ring
    /// inward. The outer ring sits at off-nadir = <see cref="OuterOffNadirDeg"/>
    /// (derived from <see cref="MinElevDeg"/> + altitude) and contains as many
    /// beams as fit at angular spacing <see cref="SpacingDeg"/> around its
    /// circle. Each successive inner ring steps the off-nadir down by Δ and
    /// uses fewer beams (so within-ring spacing stays ≈ Δ). Adjacent rings are
    /// rotated by half their azimuth step so radial neighbours interlock like
    /// a hex.
    /// </summary>
    public void RebuildBeams()
    {
        _beams.Clear();
        var (north, east, down) = SatNedBasis(SubSatLatDeg, SubSatLonDeg);

        bool elliptical = PatternKind == BeamPatternKind.Taylor_1p4_Ell;
        // Nadir direction in ECEF (used to define the radial axis for elliptical beams).
        Vec3 nadir = down;

        // Radial reference axis ⊥ boresight, in the plane containing boresight & nadir.
        // Convention: radial points "away from nadir, projected onto ⊥boresight" — i.e.
        // along the off-nadir tilt direction, so radial = α-direction at the cell edge.
        Vec3? RadialAxisFor(Vec3 boresight)
        {
            if (!elliptical) return null;
            double cos = Vec3.Dot(boresight, nadir);
            if (Math.Abs(1.0 - Math.Abs(cos)) < 1e-9)
            {
                // Centre beam: no preferred radial direction. Use sat-North projected
                // onto ⊥boresight as a stable, consistent fallback.
                Vec3 nb = (north - boresight * Vec3.Dot(boresight, north));
                double l = nb.Length;
                return l < 1e-12 ? boresight /* will be ignored */ : nb * (1.0 / l);
            }
            // radial = -(nadir − cos·boresight) / |…|  → points outward (away from nadir).
            Vec3 r = (nadir - boresight * cos).Normalized();
            return r * -1.0;
        }

        double offOuter = OuterOffNadirDeg;
        // Auto-mode = hex tessellation. For elliptical: ground-plane hex with per-beam
        // α/β adaptation. For circular patterns (§1.4 circular, §1.2, §1.3 LEO/MEO/HEO):
        // 3GPP-NTN UV-plane hex with uniform θ_b across all beams.
        bool autoTessellate = AutoMode;

        // Centre beam (always added first; auto-mode rings then expand outward, manual
        // mode rings step inward and the centre is appended at the end as before).
        void AddCentre()
        {
            var cNed = BeamDirNed(0.0, 0.0);
            var cEcef = NedToEcef(cNed, north, east, down).Normalized();
            _beams.Add(new Beam("c", cEcef, BuildPatternFor(GmDbi, 0.0))
            {
                OffNadirDeg = 0.0,
                RadialAxisEcef = RadialAxisFor(cEcef),
            });
        }

        if (autoTessellate && !elliptical)
        {
            // 3GPP NTN (TR 38.821) UV-plane hex — natural for circular patterns. Beam
            // centres form a regular hex lattice on the unit-disc UV-plane ⊥ nadir;
            // every beam shares the user's θ_b.
            //
            // Lattice constant ABS = √3·sin(θ_b·√(−L/3)). The base 3GPP convention
            // "ABS = √3·sin(θ_b)" corresponds to L = −3 dB (parabolic main beam at θ_b
            // is at −3 dB). CrossoverDb pulls the lattice tighter (more overlap, deeper
            // visual coverage) when |L| < 3 dB, looser when |L| > 3 dB.
            double crossFac = Math.Sqrt(Math.Max(0.0, -CrossoverDb) / 3.0);
            double sinTb = Math.Sin(ThetaBDeg * crossFac * Math.PI / 180.0);
            double sUvBase = Math.Sqrt(3.0) * sinTb;
            double sinOffOuter = Math.Sin(offOuter * Math.PI / 180.0);
            // Snap the lattice so the outermost ring sits *exactly* at sinOffOuter
            // (= the user's min-elevation boundary). Without this, the discrete UV
            // lattice typically leaves a half-step gap between its outer ring and
            // the visible-disc edge.
            int Nrings = Math.Max(1, (int)Math.Round(sinOffOuter / Math.Max(1e-9, sUvBase)));
            double sUv = sinOffOuter / Nrings;
            if (sUv > 1e-6)
            {
                int n = Nrings + 1;
                double e2u = 0.5 * sUv;
                double e2v = 0.5 * sUv * Math.Sqrt(3.0);
                for (int j = -n; j <= n; j++)
                {
                    for (int i = -n; i <= n; i++)
                    {
                        double u = i * sUv + j * e2u;
                        double v = j * e2v;
                        double r2 = u * u + v * v;
                        if (r2 < 1e-12) continue;
                        if (r2 > sinOffOuter * sinOffOuter) continue;
                        double sinT = Math.Sqrt(r2);
                        if (sinT > 1.0) continue;
                        double off = Math.Asin(sinT) * 180.0 / Math.PI;
                        double azFromNorth = Math.Atan2(v, u) * 180.0 / Math.PI;
                        var ned = BeamDirNed(off, azFromNorth);
                        var ecef = NedToEcef(ned, north, east, down).Normalized();
                        _beams.Add(new Beam($"uv{i}_{j}", ecef, BuildPatternFor(GmDbi, off))
                        {
                            OffNadirDeg = off,
                            RadialAxisEcef = RadialAxisFor(ecef),
                        });
                    }
                }
            }
            AddCentre();
            return;
        }

        if (autoTessellate)
        {
            // Tangent-plane hex lattice for elliptical pattern. Each beam's α/β are
            // derived from R_cell + slant geometry so its ground footprint stays
            // circular at R_cell. Lattice constant s = √3·R_cell on the ground.
            double s = Math.Sqrt(3.0) * CellRadiusKm;
            double horizonRad = HorizonHalfAngleDeg(AltitudeKm) * Math.PI / 180.0;
            double horizonGroundKm = EarthRadiusKm * horizonRad;
            int gridHalfExtent = (int)Math.Ceiling(horizonGroundKm / s) + 1;
            double lat0 = SubSatLatDeg * Math.PI / 180.0;
            double lon0 = SubSatLonDeg * Math.PI / 180.0;
            var sat = SatEcef;
            // Hex basis (x = east km, y = north km): e1 = (s, 0), e2 = (s/2, s·√3/2).
            double e2x = 0.5 * s;
            double e2y = 0.5 * s * Math.Sqrt(3.0);
            for (int j = -gridHalfExtent; j <= gridHalfExtent; j++)
            {
                for (int i = -gridHalfExtent; i <= gridHalfExtent; i++)
                {
                    double x = i * s + j * e2x;
                    double y = j * e2y;
                    double d = Math.Sqrt(x * x + y * y);
                    if (d < 1e-6) continue;                    // centre handled separately
                    if (d > horizonGroundKm * 1.1) continue;   // outside the visible disc

                    // Great-circle destination on a sphere of radius R_earth, distance d, bearing brg from north.
                    double centralAngle = d / EarthRadiusKm;
                    double brg = Math.Atan2(x, y);
                    double sinLat = Math.Sin(lat0) * Math.Cos(centralAngle)
                                  + Math.Cos(lat0) * Math.Sin(centralAngle) * Math.Cos(brg);
                    sinLat = Math.Clamp(sinLat, -1.0, 1.0);
                    double lat = Math.Asin(sinLat);
                    double lon = lon0 + Math.Atan2(
                        Math.Sin(brg) * Math.Sin(centralAngle) * Math.Cos(lat0),
                        Math.Cos(centralAngle) - Math.Sin(lat0) * sinLat);

                    var groundEcef = GeodeticToEcef(lat * 180.0 / Math.PI, lon * 180.0 / Math.PI, 0.0);
                    double elevDeg = ElevationAngleDeg(sat, groundEcef);
                    if (elevDeg < MinElevDeg) continue;        // below user's min-elevation boundary

                    var look = (groundEcef - sat).Normalized();
                    // Off-nadir = angle between look and nadir at the satellite.
                    var nadirDir = (sat * -1.0).Normalized();
                    double cosOff = Math.Clamp(Vec3.Dot(look, nadirDir), -1.0, 1.0);
                    double offNadirDeg = Math.Acos(cosOff) * 180.0 / Math.PI;

                    _beams.Add(new Beam($"h{i}_{j}", look, BuildPatternFor(GmDbi, offNadirDeg))
                    {
                        OffNadirDeg = offNadirDeg,
                        RadialAxisEcef = RadialAxisFor(look),
                    });
                }
            }
            AddCentre();
            return;
        }

        // -------- Manual / circular modes (concentric rings, walk inward) --------
        double crossoverFactor = Math.Sqrt(Math.Max(0.0, -CrossoverDb) / 3.0);
        int ringIdxIn = 0;
        double offIn = offOuter;
        while (offIn > 1e-3)
        {
            var (thetaBRad, thetaBTrans) = HalfBeamwidthsForBeam(offIn);
            double deltaRadial    = Math.Max(1e-3, 2.0 * thetaBRad   * crossoverFactor);
            double deltaTransverse = Math.Max(1e-3, 2.0 * thetaBTrans * crossoverFactor);
            if (offIn <= 0.5 * deltaRadial) break;

            // Number of beams that fit at this ring with within-ring 3D angular
            // separation ≈ Δ_t (transverse spacing target):
            //   cos(Δ_t) = cos²(off) + sin²(off)·cos(δaz)
            //   => δaz = arccos((cos Δ_t − cos²off) / sin²off)
            double offRad = offIn * Math.PI / 180.0;
            double dTransRad = deltaTransverse * Math.PI / 180.0;
            double sinOff = Math.Sin(offRad);
            double cosOff = Math.Cos(offRad);
            int n;
            if (sinOff < 1e-6)
            {
                n = 1;
            }
            else
            {
                double cosAz = (Math.Cos(dTransRad) - cosOff * cosOff) / (sinOff * sinOff);
                if (cosAz >= 1.0) { n = 1; }
                else
                {
                    double dAz = Math.Acos(Math.Clamp(cosAz, -1.0, 1.0));
                    n = Math.Max(1, (int)Math.Round(2.0 * Math.PI / dAz));
                }
            }

            double dAzActualDeg = n > 0 ? 360.0 / n : 0.0;
            double azOffsetDeg = (ringIdxIn % 2 == 0) ? 0.0 : 0.5 * dAzActualDeg;

            for (int i = 0; i < n; i++)
            {
                double az = i * dAzActualDeg + azOffsetDeg;
                var ned = BeamDirNed(offIn, az);
                var ecef = NedToEcef(ned, north, east, down).Normalized();
                string name = $"r{ringIdxIn}_{i}";
                _beams.Add(new Beam(name, ecef, BuildPatternFor(GmDbi, offIn))
                {
                    OffNadirDeg = offIn,
                    RadialAxisEcef = RadialAxisFor(ecef),
                });
            }

            offIn -= deltaRadial;
            ringIdxIn++;
        }
        AddCentre();
    }

    /// <summary>Project a beam boresight to its ground intersection (lat,lon), if any.</summary>
    public (double lat, double lon)? GroundFootprint(Beam beam)
    {
        var hit = RaySphereHit(SatEcef, beam.Boresight);
        if (hit is null) return null;
        var (lat, lon, _) = EcefToGeodetic(hit.Value);
        return (lat, lon);
    }

    /// <summary>
    /// Aggregate gain (dBi) towards a ground point at (lat, lon), using the
    /// currently selected <see cref="Mode"/>. Returns
    /// <see cref="double.NegativeInfinity"/> when the point lies below the
    /// satellite's horizon — there is no line of sight, so the antenna gain
    /// in that direction is not defined.
    /// </summary>
    public double GainTowardsGround(double lat, double lon)
    {
        if (!IsVisible(lat, lon)) return double.NegativeInfinity;
        var sat = SatEcef;
        var groundEcef = GeodeticToEcef(lat, lon, 0.0);
        var look = (groundEcef - sat).Normalized();
        return Mode switch
        {
            HeatmapMode.MaxSingleBeam => BeamComposer.MaxSingleBeamGainDbi(Beams, look),
            _ => BeamComposer.CompositeGainDbi(Beams, look),
        };
    }

    /// <summary>True iff the ground point is in line-of-sight of the satellite.</summary>
    public bool IsVisible(double lat, double lon)
    {
        var central = GreatCircleDeg(SubSatLatDeg, SubSatLonDeg, lat, lon);
        return central <= HorizonHalfAngleDeg(AltitudeKm);
    }
}

public enum HeatmapMode
{
    /// <summary>Incoherent power sum across all active beams (composite EIRP-density style).</summary>
    PowerSum,
    /// <summary>Maximum single-beam contribution (dominant beam at the test direction).</summary>
    MaxSingleBeam,
}
