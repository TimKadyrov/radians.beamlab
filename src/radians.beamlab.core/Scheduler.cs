using System;
using System.Collections.Generic;
using System.Linq;
using static radians.beamlab.GeoMath;

namespace radians.beamlab;

/// <summary>
/// Reads operating constraints off the declared parameter set itself
/// (<see cref="OperatingParamsSet"/> -- the object the R XML is written
/// from), so the scheduler's bounds and the declaration cannot drift apart.
/// Header/array duality follows EPS Sec. 6.7.2.2: the array prevails inside
/// the latitude span it covers, the header applies outside it. Within an
/// array the nearest-latitude block is used (the Sec. D5 convention);
/// min_elev interpolates linearly in azimuth, clamped at the ends.
/// </summary>
public static class DeclaredConstraints
{
    public static double MinElevDeg(OperatingParamsSet p, double latDeg, double azDeg)
    {
        if (p.MinElev.Count > 0)
        {
            double lo = p.MinElev.Min(b => b.LatDeg), hi = p.MinElev.Max(b => b.LatDeg);
            if (latDeg >= lo && latDeg <= hi)
            {
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
            }
        }
        return p.ElevAngleHeaderDeg ?? 0.0;
    }

    /// <summary>Nco at latitude; absent everywhere = no cap.</summary>
    public static int MaxCoFreq(OperatingParamsSet p, double latDeg)
    {
        if (p.MaxCoFreqByLat.Count > 0)
        {
            double lo = p.MaxCoFreqByLat.Min(v => v.LatDeg), hi = p.MaxCoFreqByLat.Max(v => v.LatDeg);
            if (latDeg >= lo && latDeg <= hi)
                return Nearest(p.MaxCoFreqByLat, v => v.LatDeg, latDeg).Value;
        }
        return p.MaxCoFreqHeader ?? int.MaxValue;
    }

    /// <summary>Minimum tracking duration (s) at latitude; absent = 0 (classic algorithm).</summary>
    public static int MinDurationSec(OperatingParamsSet p, double latDeg)
    {
        if (p.MinDurationByLat.Count > 0)
        {
            double lo = p.MinDurationByLat.Min(v => v.LatDeg), hi = p.MinDurationByLat.Max(v => v.LatDeg);
            if (latDeg >= lo && latDeg <= hi)
                return Nearest(p.MinDurationByLat, v => v.LatDeg, latDeg).Seconds;
        }
        return p.MinDurationSecHeader ?? 0;
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
    /// the all-orbits (c = 0) array; absent = 0 (no exclusion).
    /// </summary>
    public static double ExclusionAlphaDeg(OperatingParamsSet p, double latDeg, int orbId)
    {
        var specific = p.MinExclude.FirstOrDefault(e => e.OrbId == orbId && e.ByLat.Count > 0);
        var chosen = specific ?? p.MinExclude.FirstOrDefault(e => e.OrbId == 0 && e.ByLat.Count > 0);
        if (chosen is null) return 0.0;
        return Nearest(chosen.ByLat, v => v.LatDeg, latDeg).AlphaDeg;
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
/// The selection metric is a policy: highest elevation (default), or
/// maximum GSO separation -- the feasible satellite farthest from the arc,
/// an arc-avoidance strategy whose margin effect is thereby measurable.
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
        double ElevationDeg, double AlphaDeg);

    public ScheduleStep Step(double tSec)
    {
        int n = _con.SatelliteCount;
        var states = new SatelliteState[n];
        var footprints = new List<(int beamIndex, double lat, double lon)>[n];
        for (int i = 0; i < n; i++)
        {
            states[i] = _con.StateAt(i, tSec, _simDurationSec);
            var resolved = _layout.Resolve(states[i]);
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

        // Candidates per cell, against the declared bounds.
        var candidates = new Dictionary<int, List<Candidate>>();
        for (int c = 0; c < _geo.Cells.Count; c++)
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

                list.Add(new Candidate(i, states[i].SatelliteNumber, bestBeam, elev, alpha));
            }
            list.Sort((a, b) =>
            {
                int cmp = Metric(b).CompareTo(Metric(a));
                if (cmp != 0) return cmp;
                cmp = b.ElevationDeg.CompareTo(a.ElevationDeg);
                return cmp != 0 ? cmp : a.SatelliteNumber.CompareTo(b.SatelliteNumber);
            });
            candidates[cell.CellId] = list;
        }

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

    private double Metric(Candidate c)
        => _policy == SelectionPolicy.MaxGsoSeparation ? c.AlphaDeg : c.ElevationDeg;

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
