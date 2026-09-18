using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>
/// Reads operating constraints off the declared parameter set itself
/// (<see cref="OperatingParamsSet"/> -- the object the R XML is written
/// from), so the scheduler's bounds and the declaration cannot drift apart.
/// Header and array are mutually exclusive per quantity (design brief
/// Sec. 3.8, EPS V43 Sec. 6.7.2.2, ruling of 2026-09-07): a quantity is read
/// from its array when the array is filed, else from its header, and a set
/// carrying both is an invalid filing (see FormConflicts). The nearest-
/// latitude read is total -- the outermost row governs every latitude beyond
/// the table, so a single row declares a global constant; min_elev then
/// interpolates linearly in azimuth, clamped at the ends. MIN_EXCLUDE alone
/// is read by linear interpolation between latitude rows (its own Part B
/// rule), the end rows governing beyond them.
/// </summary>
public static class DeclaredConstraints
{
    public static double MinElevDeg(OperatingParamsSet p, double latDeg, double azDeg)
    {
        if (p.MinElev.Count > 0)
        {
            // The nearest-latitude read is total: the outermost row governs
            // every latitude beyond the table (no out-of-span fallback).
            var blk = Nearest(p.MinElev, b => b.LatDeg, latDeg);
            var rows = blk.ByAz.OrderBy(r => r.AzDeg).ToList();
            if (rows.Count == 1) return rows[0].ElevDeg;
            if (azDeg <= rows[0].AzDeg) return rows[0].ElevDeg;
            if (azDeg >= rows[^1].AzDeg) return rows[^1].ElevDeg;
            for (int i = 1; i < rows.Count; i++)
            {
                if (azDeg > rows[i].AzDeg) continue;
                double f = (azDeg - rows[i - 1].AzDeg) / (rows[i].AzDeg - rows[i - 1].AzDeg);
                return rows[i - 1].ElevDeg + f * (rows[i].ElevDeg - rows[i - 1].ElevDeg);
            }
            return rows[^1].ElevDeg;
        }
        return p.ElevAngleHeaderDeg ?? 0.0;
    }

    /// <summary>Nco at latitude: the nearest row when the array is filed (total read), else the header; absent = no cap.</summary>
    public static int MaxCoFreq(OperatingParamsSet p, double latDeg)
    {
        if (p.MaxCoFreqByLat.Count > 0)
            return Nearest(p.MaxCoFreqByLat, v => v.LatDeg, latDeg).Value;
        return p.MaxCoFreqHeader ?? int.MaxValue;
    }

    /// <summary>Minimum tracking duration (s) at latitude: the nearest row when the array is filed (total read), else the header; absent = 0 (classic algorithm).</summary>
    public static int MinDurationSec(OperatingParamsSet p, double latDeg)
    {
        if (p.MinDurationByLat.Count > 0)
            return Nearest(p.MinDurationByLat, v => v.LatDeg, latDeg).Seconds;
        return p.MinDurationSecHeader ?? 0;
    }

    /// <summary>
    /// The quantities a set declares in BOTH the header and the array form.
    /// Header and array are mutually exclusive per quantity (design brief
    /// Sec. 3.8, EPS V43 Sec. 6.7.2.2): a set carrying both is an invalid
    /// filing, to be reported, never read under an invented precedence.
    /// Empty for a valid set.
    /// </summary>
    public static IReadOnlyList<string> FormConflicts(OperatingParamsSet p)
    {
        var both = new List<string>();
        if (p.MinElev.Count > 0 && p.ElevAngleHeaderDeg is not null) both.Add("min_elev / elev_angle");
        if (p.MaxCoFreqByLat.Count > 0 && p.MaxCoFreqHeader is not null) both.Add("max_co_freq (array) / max_co_freq (header)");
        if (p.MinDurationByLat.Count > 0 && p.MinDurationSecHeader is not null) both.Add("min_duration (array) / min_duration (header)");
        return both;
    }

    /// <summary>Per-satellite co-frequency link cap MAX_CO_FREQ_SAT (header only); absent = no cap.</summary>
    public static int MaxCoFreqSat(OperatingParamsSet p) => p.MaxCoFreqSat ?? int.MaxValue;

    /// <summary>Minimum angle at the satellite between co-frequency ES (deg, header only); absent = 0.</summary>
    public static double MinAngleAtSatDeg(OperatingParamsSet p) => p.MinAngleAtSatDeg ?? 0.0;

    /// <summary>Minimum angle at the ES between co-serving satellites (deg, header only); absent = 0.</summary>
    public static double MinAngleAtEsDeg(OperatingParamsSet p) => p.MinAngleAtEsDeg ?? 0.0;

    /// <summary>
    /// Exclusion-zone angle alpha0 (deg) at latitude for the given orbit
    /// (SRS orb_id, per plane): an orbit-specific min_exclude array overrides
    /// the all-orbits (c = 0) array; absent = 0 (no exclusion). Unlike the
    /// other arrays, MIN_EXCLUDE is read by LINEAR INTERPOLATION between
    /// latitude rows, clamped at the ends -- the Recommendation's own rule
    /// for this array ("derived using linear interpolation between data
    /// points", Part B).
    /// </summary>
    public static double ExclusionAlphaDeg(OperatingParamsSet p, double latDeg, int orbId)
    {
        var specific = p.MinExclude.FirstOrDefault(e => e.OrbId == orbId && e.ByLat.Count > 0);
        var chosen = specific ?? p.MinExclude.FirstOrDefault(e => e.OrbId == 0 && e.ByLat.Count > 0);
        if (chosen is null) return 0.0;
        var rows = chosen.ByLat.OrderBy(v => v.LatDeg).ToList();
        if (rows.Count == 1 || latDeg <= rows[0].LatDeg) return rows[0].AlphaDeg;
        if (latDeg >= rows[^1].LatDeg) return rows[^1].AlphaDeg;
        for (int i = 1; i < rows.Count; i++)
        {
            if (latDeg > rows[i].LatDeg) continue;
            double f = (latDeg - rows[i - 1].LatDeg) / (rows[i].LatDeg - rows[i - 1].LatDeg);
            return rows[i - 1].AlphaDeg + f * (rows[i].AlphaDeg - rows[i - 1].AlphaDeg);
        }
        return rows[^1].AlphaDeg;
    }

    private static T Nearest<T>(IReadOnlyList<T> items, Func<T, double> key, double v)
    {
        T best = items[0];
        double bestD = Math.Abs(key(best) - v);
        foreach (var it in items)
        {
            double d = Math.Abs(key(it) - v);
            if (d < bestD) { best = it; bestD = d; }
        }
        return best;
    }
}

/// <summary>
/// The scheduler's satellite-selection strategy -- the declaration-side
/// policy the operating parameters bound. MaxGsoSeparation picks the
/// feasible satellite farthest from the GSO arc (largest alpha), an
/// arc-avoidance mitigation whose epfd effect is measurable against the
/// default.
/// </summary>
public enum SelectionPolicy
{
    HighestElevation,
    MaxGsoSeparation,
    /// <summary>
    /// Minimum handovers: a made link is held while it stays feasible --
    /// no voluntary handover ever; new links pick the highest elevation.
    /// </summary>
    HoldUntilForced,
    /// <summary>
    /// Uniform random choice among the feasible satellites (a fresh seeded
    /// key per candidate per step; the argmax of iid uniforms is uniform).
    /// With no hold it re-draws every step -- the memoryless rule of WP 4A
    /// Doc 4A/653, which operators described as close to their real
    /// selection; a hold time turns it into random-at-setup.
    /// </summary>
    Random,
}

/// <summary>One granted cell-satellite link at a step.</summary>
public sealed record CellLink(int CellId, int SatelliteNumber, int BeamIndex,
    double StartTimeSec, double ElevationDeg, double AlphaDeg);

/// <summary>The schedule at one step: links and the per-satellite active beams.</summary>
public sealed class ScheduleStep
{
    public required double TimeSeconds { get; init; }
    public required IReadOnlyList<CellLink> Links { get; init; }
    /// <summary>Every feasible cell-satellite pair this step (the granted Links are a subset).</summary>
    public required IReadOnlyList<CellLink> CandidateLinks { get; init; }
    /// <summary>satellite number -> gate-ON beam indices.</summary>
    public required IReadOnlyDictionary<int, HashSet<int>> ActiveBeams { get; init; }
    public required int VoluntaryHandovers { get; init; }
    public required int ForcedHandovers { get; init; }
    public required int UnservedCellLinks { get; init; }
}

/// <summary>
/// WP2 selection policy, explicit and singular: per step and per cell-link,
/// the feasible satellite with the highest elevation serves -- where
/// feasible means, against the DECLARED parameter set: elevation at the cell
/// at or above min_elev(lat, azimuth-of-satellite); the cell-centre alpha to
/// the GSO arc outside min_exclude(lat, orb); a resolved (scene-gated) beam
/// whose footprint covers the cell. A made link is kept for at least
/// min_duration(lat) unless it becomes infeasible (forced handover);
/// voluntary handovers to a higher-elevation satellite happen only after the
/// dwell. Distinct satellites per cell never exceed max_co_freq(lat).
///
/// The remaining declared bounds are enforced during assignment, so they
/// reassign rather than drop: max_co_freq_sat caps links per satellite;
/// min_angle_at_sat separates a satellite's co-frequency cells as seen from
/// it; min_angle_at_es separates the satellites co-serving one cell as seen
/// from it; cells outside es_lat_min/max are not served. Contested capacity
/// resolves in cell-list order -- a deterministic, declaration-compliant
/// greedy assignment, not an optimal one.
///
/// The selection metric is a policy: highest elevation (default), maximum
/// GSO separation -- the feasible satellite farthest from the arc, an
/// arc-avoidance strategy whose margin effect is thereby measurable -- or
/// uniform random among the feasible set (seeded; the operator-attested
/// baseline of WP 4A Doc 4A/653). No policy has an R-set field: selection
/// reaches the filing only through the gates every policy obeys.
/// Demand follows each cell's on/off activity model (ServiceCell
/// .ActivityFactor): an inactive slot releases its link without counting a
/// handover or unserved demand.
/// </summary>
public sealed class Scheduler
{
    private readonly Constellation _con;
    private readonly ServiceGeography _geo;
    private readonly OperatingParamsSet _declared;
    private readonly IBeamPointing _layout;
    private readonly double _simDurationSec;
    private readonly double _coverageRadiusKm;

    private sealed class LinkState
    {
        public int SatelliteNumber;
        public int BeamIndex;
        public double StartTimeSec;
    }

    // (cellId, slot) -> current link.
    private readonly Dictionary<(int, int), LinkState> _links = new();
    private readonly Vec3[] _cellEcef;
    private readonly Dictionary<(int shell, int plane), int> _orbIds = new();
    private readonly SelectionPolicy _policy;
    // Seeded so a Random-policy run is reproducible step for step.
    private readonly System.Random _rng = new(4653);

    public Scheduler(Constellation constellation, ServiceGeography geography,
        OperatingParamsSet declared, IBeamPointing layout, double simulationDurationSec,
        double? coverageRadiusKm = null, SelectionPolicy policy = SelectionPolicy.HighestElevation)
    {
        _con = constellation;
        _geo = geography;
        _declared = declared;
        _layout = layout;
        _simDurationSec = simulationDurationSec;
        _coverageRadiusKm = coverageRadiusKm ?? geography.CellPitchKm;
        _policy = policy;

        _cellEcef = new Vec3[geography.Cells.Count];
        for (int i = 0; i < geography.Cells.Count; i++)
            _cellEcef[i] = GeodeticToEcef(geography.Cells[i].LatDeg, geography.Cells[i].LonDeg, 0.0);

        // SRS orb_id numbering: planes in satellite order across shells.
        int orb = 0;
        for (int i = 0; i < constellation.SatelliteCount; i++)
        {
            var st = constellation.StateAt(i, 0.0, simulationDurationSec);
            if (!_orbIds.ContainsKey((st.ShellIndex, st.PlaneIndex)))
                _orbIds[(st.ShellIndex, st.PlaneIndex)] = ++orb;
        }
    }

    private sealed record Candidate(int SatIndex, int SatelliteNumber, int BeamIndex,
        double ElevationDeg, double AlphaDeg, double RandomKey = 0.0, int Colour = 0);

    public ScheduleStep Step(double tSec)
    {
        int n = _con.SatelliteCount;
        var states = new SatelliteState[n];
        var footprints = new List<(int beamIndex, double lat, double lon)>[n];
        // Per-satellite reuse colours and the payload's per-colour beam capacity
        // come from the resolved set: the same values the mask envelopes over.
        var colours = new IReadOnlyList<int>?[n];
        var capacities = new int?[n];
        void ResolveSatellite(int i)
        {
            states[i] = _con.StateAt(i, tSec, _simDurationSec);
            var resolved = _layout.Resolve(states[i]);
            colours[i] = resolved.ReuseColors;
            capacities[i] = resolved.CoFrequencyBeamCapacity;
            var fps = new List<(int, double, double)>();
            for (int b = 0; b < resolved.Beams.Count; b++)
            {
                var beam = resolved.Beams[b];
                if (beam.Weight <= 0.0) continue;          // scene-gated off
                var hit = RaySphereHit(states[i].PositionEcefKm, beam.Boresight);
                if (hit is null) continue;
                var g = hit.Value;
                double la = Math.Asin(Math.Clamp(g.Z / g.Length, -1.0, 1.0)) * 180.0 / Math.PI;
                double lo = Math.Atan2(g.Y, g.X) * 180.0 / Math.PI;
                fps.Add((b, la, lo));
            }
            footprints[i] = fps;
        }

        // Satellites are independent within a step (one propagator each; a
        // layout that declares itself concurrent), so they resolve in parallel;
        // the first declared capacity in satellite order is kept either way.
        bool parallel = SimulationParallel.Enabled;
        if (parallel && _layout is IConcurrentBeamPointing concurrentLayout)
        {
            concurrentLayout.Prepare(tSec);
            Parallel.For(0, n, SimulationParallel.Options, ResolveSatellite);
        }
        else
        {
            for (int i = 0; i < n; i++) ResolveSatellite(i);
        }
        int? capColour = null;
        for (int i = 0; i < n; i++) capColour ??= capacities[i];

        // Candidates per cell, against the declared bounds. Cells are
        // independent of one another (geometry, gates and covering beam read
        // only the resolved states), so they are built in parallel; the
        // Random policy's keys are then drawn on this thread in the original
        // order -- cell by cell, candidate by candidate -- so the seeded
        // sequence lands on the same candidates as the sequential loop.
        var perCell = new List<Candidate>[_geo.Cells.Count];
        void BuildCandidates(int c)
        {
            var cell = _geo.Cells[c];
            var es = _cellEcef[c];
            var (cn, ce, _) = SatNedBasis(cell.LatDeg, cell.LonDeg);
            var list = new List<Candidate>();
            for (int i = 0; i < n; i++)
            {
                if (!_con.IsOperational(i)) continue;   // spares do not serve
                var pos = states[i].PositionEcefKm;
                double elev = ElevationAngleDeg(pos, es);
                if (elev <= 0.0) continue;

                var toSat = (pos - es).Normalized();
                double az = Math.Atan2(Vec3.Dot(toSat, ce), Vec3.Dot(toSat, cn)) * 180.0 / Math.PI;
                if (az < 0) az += 360.0;
                if (elev < DeclaredConstraints.MinElevDeg(_declared, cell.LatDeg, az)) continue;

                int orbId = _orbIds[(states[i].ShellIndex, states[i].PlaneIndex)];
                double alpha = GsoGeometry.AlphaMinAbsDeg(es, pos);
                if (alpha < DeclaredConstraints.ExclusionAlphaDeg(_declared, cell.LatDeg, orbId)) continue;

                // Covering beam: nearest resolved footprint within the radius.
                int bestBeam = -1; double bestKm = _coverageRadiusKm;
                foreach (var (bi, la, lo) in footprints[i])
                {
                    double km = GreatCircleDeg(cell.LatDeg, cell.LonDeg, la, lo) * Math.PI / 180.0 * EarthRadiusKm;
                    if (km <= bestKm) { bestKm = km; bestBeam = bi; }
                }
                if (bestBeam < 0) continue;

                int colour = colours[i] is { } cc && bestBeam < cc.Count ? cc[bestBeam] : 0;
                list.Add(new Candidate(i, states[i].SatelliteNumber, bestBeam, elev, alpha, 0.0, colour));
            }
            perCell[c] = list;
        }
        void SortCandidates(int c)
        {
            perCell[c].Sort((a, b) =>
            {
                int cmp = Metric(b).CompareTo(Metric(a));
                if (cmp != 0) return cmp;
                cmp = b.ElevationDeg.CompareTo(a.ElevationDeg);
                return cmp != 0 ? cmp : a.SatelliteNumber.CompareTo(b.SatelliteNumber);
            });
        }

        if (parallel) Parallel.For(0, perCell.Length, SimulationParallel.Options, BuildCandidates);
        else for (int c = 0; c < perCell.Length; c++) BuildCandidates(c);

        if (_policy == SelectionPolicy.Random)
        {
            for (int c = 0; c < perCell.Length; c++)
            {
                var list = perCell[c];
                for (int j = 0; j < list.Count; j++)
                    list[j] = list[j] with { RandomKey = _rng.NextDouble() };
            }
        }

        if (parallel) Parallel.For(0, perCell.Length, SimulationParallel.Options, SortCandidates);
        else for (int c = 0; c < perCell.Length; c++) SortCandidates(c);

        var candidates = new Dictionary<int, List<Candidate>>();
        for (int c = 0; c < perCell.Length; c++)
            candidates[_geo.Cells[c].CellId] = perCell[c];

        // Assignment with dwell. The remaining declared bounds gate candidate
        // ELIGIBILITY here, so contested capacity reassigns to the next-best
        // satellite (or goes unserved) rather than being dropped downstream;
        // a continuing link whose satellite a gate now refuses breaks as a
        // forced handover.
        var links = new List<CellLink>();
        var active = new Dictionary<int, HashSet<int>>();
        int voluntary = 0, forced = 0, unserved = 0;

        int capSat = DeclaredConstraints.MaxCoFreqSat(_declared);
        double minAngleSat = DeclaredConstraints.MinAngleAtSatDeg(_declared);
        double minAngleEs = DeclaredConstraints.MinAngleAtEsDeg(_declared);
        var satLinkCount = new Dictionary<int, int>();          // satellite number -> links granted this step
        // satellite number, colour -> beams lit this step. The payload's co-frequency
        // beam capacity is hardware: a satellite with this many same-colour beams
        // already on cannot light another, whatever the cell asks for.
        var satColourCount = new Dictionary<(int Sat, int Colour), int>();
        var satServedCells = new Dictionary<int, List<Vec3>>(); // satellite number -> served cell positions

        for (int ci = 0; ci < _geo.Cells.Count; ci++)
        {
            var cell = _geo.Cells[ci];
            var esPos = _cellEcef[ci];

            if (cell.LatDeg < _declared.EsLatMinDeg || cell.LatDeg > _declared.EsLatMaxDeg)
            {
                unserved += cell.DemandLinks;   // outside the declared ES latitude range
                continue;
            }

            var cand = candidates[cell.CellId];
            int nLinks = Math.Min(cell.DemandLinks, DeclaredConstraints.MaxCoFreq(_declared, cell.LatDeg));
            int minDur = DeclaredConstraints.MinDurationSec(_declared, cell.LatDeg);
            var taken = new HashSet<int>();   // satellites already serving this cell

            bool Eligible(Candidate x)
            {
                if (satLinkCount.GetValueOrDefault(x.SatelliteNumber) >= capSat) return false;
                if (capColour is int kc
                    && satColourCount.GetValueOrDefault((x.SatelliteNumber, x.Colour)) >= kc) return false;
                if (minAngleSat > 0.0 && satServedCells.TryGetValue(x.SatelliteNumber, out var served))
                {
                    var sp = states[x.SatIndex].PositionEcefKm;
                    foreach (var other in served)
                        if (AngleBetweenDeg(other - sp, esPos - sp) < minAngleSat) return false;
                }
                if (minAngleEs > 0.0)
                {
                    var cp = states[x.SatIndex].PositionEcefKm;
                    foreach (int satNo in taken)
                        if (AngleBetweenDeg(states[satNo - 1].PositionEcefKm - esPos, cp - esPos) < minAngleEs)
                            return false;
                }
                return true;
            }

            void Book(Candidate c)
            {
                satLinkCount[c.SatelliteNumber] = satLinkCount.GetValueOrDefault(c.SatelliteNumber) + 1;
                satColourCount[(c.SatelliteNumber, c.Colour)] =
                    satColourCount.GetValueOrDefault((c.SatelliteNumber, c.Colour)) + 1;
                if (!satServedCells.TryGetValue(c.SatelliteNumber, out var served))
                    satServedCells[c.SatelliteNumber] = served = new List<Vec3>();
                served.Add(esPos);
            }

            for (int slot = 0; slot < nLinks; slot++)
            {
                var key = (cell.CellId, slot);

                // On/off traffic: an inactive slot has no demand this window
                // -- release the link, no handover and no unserved counted.
                if (!ActiveAt(cell, slot, tSec))
                {
                    _links.Remove(key);
                    continue;
                }

                _links.TryGetValue(key, out var current);

                Candidate feasible = null;
                if (current is not null)
                    feasible = cand.FirstOrDefault(x => x.SatelliteNumber == current.SatelliteNumber && Eligible(x));

                Candidate best = cand.FirstOrDefault(x => !taken.Contains(x.SatelliteNumber)
                    && Eligible(x) && Sustainable(x, cell, esPos, tSec, minDur));

                if (current is not null && feasible is not null && !taken.Contains(current.SatelliteNumber))
                {
                    double dwell = tSec - current.StartTimeSec;
                    bool wantSwitch = _policy != SelectionPolicy.HoldUntilForced
                                   && best is not null
                                   && best.SatelliteNumber != current.SatelliteNumber
                                   && Metric(best) > Metric(feasible);
                    if (wantSwitch && dwell >= minDur)
                    {
                        _links[key] = new LinkState { SatelliteNumber = best.SatelliteNumber, BeamIndex = best.BeamIndex, StartTimeSec = tSec };
                        voluntary++;
                        Grant(cell, key, best, links, active, taken);
                        Book(best);
                    }
                    else
                    {
                        current.BeamIndex = feasible.BeamIndex;   // beam may drift as the sat moves
                        Grant(cell, key, feasible with { BeamIndex = feasible.BeamIndex }, links, active, taken, current.StartTimeSec);
                        Book(feasible);
                    }
                }
                else
                {
                    if (current is not null) _links.Remove(key);
                    if (best is null)
                    {
                        unserved++;
                        continue;
                    }
                    if (current is not null) forced++;
                    _links[key] = new LinkState { SatelliteNumber = best.SatelliteNumber, BeamIndex = best.BeamIndex, StartTimeSec = tSec };
                    Grant(cell, key, best, links, active, taken);
                    Book(best);
                }
            }
        }

        return new ScheduleStep
        {
            TimeSeconds = tSec,
            Links = links,
            CandidateLinks = candidates.SelectMany(kv => kv.Value.Select(c =>
                new CellLink(kv.Key, c.SatelliteNumber, c.BeamIndex, tSec,
                    c.ElevationDeg, c.AlphaDeg))).ToList(),
            ActiveBeams = active,
            VoluntaryHandovers = voluntary,
            ForcedHandovers = forced,
            UnservedCellLinks = unserved,
        };
    }

    private static double AngleBetweenDeg(Vec3 a, Vec3 b)
        => Math.Acos(Math.Clamp(Vec3.Dot(a.Normalized(), b.Normalized()), -1.0, 1.0)) * 180.0 / Math.PI;

    private double Metric(Candidate c) => _policy switch
    {
        SelectionPolicy.MaxGsoSeparation => c.AlphaDeg,
        SelectionPolicy.Random => c.RandomKey,
        _ => c.ElevationDeg,
    };

    // MIN_DURATION as an admission rule: a NEW link is only made toward a
    // satellite that can sustain it -- still above the declared elevation
    // and outside the exclusion at the cell after the declared duration.
    // Beam coverage is not re-resolved at the look-ahead instant: the
    // layout tiles the whole min-elevation footprint, so elevation and
    // exclusion are the binding gates.
    private bool Sustainable(Candidate c, ServiceCell cell, Vec3 es, double tSec, int minDurSec)
    {
        if (minDurSec <= 0) return true;
        double tEnd = Math.Min(tSec + minDurSec, _simDurationSec);
        if (tEnd <= tSec) return true;
        var st = _con.StateAt(c.SatIndex, tEnd, _simDurationSec);
        var pos = st.PositionEcefKm;
        double elev = ElevationAngleDeg(pos, es);
        if (elev <= 0.0) return false;
        var (cn, ce, _) = SatNedBasis(cell.LatDeg, cell.LonDeg);
        var toSat = (pos - es).Normalized();
        double az = Math.Atan2(Vec3.Dot(toSat, ce), Vec3.Dot(toSat, cn)) * 180.0 / Math.PI;
        if (az < 0) az += 360.0;
        if (elev < DeclaredConstraints.MinElevDeg(_declared, cell.LatDeg, az)) return false;
        double alpha = GsoGeometry.AlphaMinAbsDeg(es, pos);
        int orbId = _orbIds[(st.ShellIndex, st.PlaneIndex)];
        return alpha >= DeclaredConstraints.ExclusionAlphaDeg(_declared, cell.LatDeg, orbId);
    }

    /// <summary>Deterministic on/off activity for one cell slot in the window containing tSec.</summary>
    private static bool ActiveAt(ServiceCell cell, int slot, double tSec)
    {
        if (cell.ActivityFactor >= 1.0) return true;
        if (cell.ActivityFactor <= 0.0) return false;
        long window = (long)Math.Floor(tSec / Math.Max(1.0, cell.ActivityPeriodSec));
        return Hash01(cell.CellId, slot, window) < cell.ActivityFactor;
    }

    /// <summary>SplitMix64-style hash of (cell, slot, window) to [0, 1) -- reproducible traffic.</summary>
    private static double Hash01(int cellId, int slot, long window)
    {
        ulong z = (ulong)(uint)cellId * 0x9E3779B97F4A7C15UL
                ^ ((ulong)(uint)slot + 1UL) * 0xBF58476D1CE4E5B9UL
                ^ (ulong)window * 0x94D049BB133111EBUL;
        z ^= z >> 30; z *= 0xBF58476D1CE4E5B9UL;
        z ^= z >> 27; z *= 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / 9007199254740992.0);
    }

    private void Grant(ServiceCell cell, (int, int) key, Candidate c,
        List<CellLink> links, Dictionary<int, HashSet<int>> active, HashSet<int> taken,
        double? keptStart = null)
    {
        links.Add(new CellLink(cell.CellId, c.SatelliteNumber, c.BeamIndex,
            keptStart ?? _links[key].StartTimeSec, c.ElevationDeg, c.AlphaDeg));
        taken.Add(c.SatelliteNumber);
        if (!active.TryGetValue(c.SatelliteNumber, out var set))
            active[c.SatelliteNumber] = set = new HashSet<int>();
        set.Add(c.BeamIndex);
    }
}
