using System;
using System.Collections.Generic;
using System.Linq;
using Radians.Orbits.Core.Utilities;

namespace radians.beamlab;

/// <summary>One SRS orbit row (v10 `orbit` table).</summary>
public sealed class SrsOrbitRow
{
    public int OrbId { get; init; }
    public int NbrSatPl { get; init; }
    /// <summary>LAN (deg). Written to both right_asc and long_asc, as the worked notices do.</summary>
    public double LanDeg { get; init; }
    public double InclinationDeg { get; init; }
    /// <summary>Apogee altitude (km); equals perigee for circular orbits.</summary>
    public double ApogeeKm { get; init; }
    /// <summary>Perigee altitude (km).</summary>
    public double PerigeeKm { get; init; }
    /// <summary>Argument of perigee (deg).</summary>
    public double PerigArgDeg { get; init; }
    /// <summary>Minimum operating height op_ht_km (EPS 6.8.2.3: perig_km &lt;= op_ht &lt;= apog_km).</summary>
    public double OpHtKm { get; init; }
    /// <summary>Keplerian period, split into the SRS ddd/hh/mm fields.</summary>
    public (int Days, int Hours, int Minutes) Period { get; init; }
    /// <summary>false = free drift (orbit case 1: the examination adds artificial precession).</summary>
    public bool StationKeeping { get; init; }
    /// <summary>W_delta tolerance (deg), written only when station keeping.</summary>
    public double? KeepRangeDeg { get; init; }
    /// <summary>Case 3: administration-supplied precession declared.</summary>
    public bool PrecessionSupplied { get; init; }
    /// <summary>Case 3 precession rate (deg/s).</summary>
    public double PrecessionRateDegPerSec { get; init; }
    /// <summary>Declared repeating ground-track period (rpt_prd_dd/hh/mm/ss).</summary>
    public (int Days, int Hours, int Minutes, int Seconds)? RepeatPeriod { get; init; }
}

/// <summary>One SRS phase row: in-plane angle from the ascending node (deg).</summary>
public sealed record SrsPhaseRow(int OrbId, int OrbSatId, double PhaseAngDeg);

/// <summary>An epfd_freq range of a scenario. EmiRcp: 'E' = emission (down), 'R' = reception (up).</summary>
public sealed record SrsFreqRange(int SeqNo, char EmiRcp, double FreqMinMhz, double FreqMaxMhz);

/// <summary>A mask_lnk1/2 entry: which mask serves which orbit/satellite (-1 = all) in a scenario.</summary>
public sealed record SrsMaskLink(int SeqNo, int MaskId, int OrbId = -1, int? SatOrbId = null, int? EAsId = null);

/// <summary>One examination scenario (v10 epfd_param + its epfd_freq / mask links / sat_oper).</summary>
public sealed class SrsScenario
{
    public int ScenId { get; init; }
    public string ScenName { get; init; } = "";
    public List<SrsFreqRange> Frequencies { get; } = new();
    /// <summary>Space-station pfd mask links (mask_lnk1).</summary>
    public List<SrsMaskLink> PfdMaskLinks { get; } = new();
    /// <summary>Earth-station e.i.r.p. mask links (mask_lnk2).</summary>
    public List<SrsMaskLink> EsMaskLinks { get; } = new();
    /// <summary>Nco latitude ranges (sat_oper): lat_fr, lat_to, nbr_op_sat.</summary>
    public List<(double LatFr, double LatTo, int NbrOpSat)> SatOper { get; } = new();
}

/// <summary>A mask_info registry row. FMask: P/S/E/R; FMaskType letter code (A/X/Z/O/D) or null for R.</summary>
public sealed record SrsMaskInfo(int MaskId, double FreqMinMhz, double FreqMaxMhz, char FMask, char? FMaskType);

/// <summary>
/// A declared earth station (e_as_stn, EPS Sec. 6.4.3). Specific stations
/// (StnType 'S') carry coordinates -- the columns the examination reads when
/// mask_lnk2 names an e_as_id; typical stations ('T') describe a class and
/// leave the coordinates null, as the worked notices do.
/// </summary>
public sealed record SrsEarthStation
{
    public required int EAsId { get; init; }
    public required string StnName { get; init; }
    /// <summary>'S' = specific (named, located), 'T' = typical.</summary>
    public char StnType { get; init; } = 'S';
    public double? LonDeg { get; init; }
    public double? LatDeg { get; init; }
    public double? NoiseT { get; init; }
    public double? GainDbi { get; init; }
    public double? AntDiamM { get; init; }
    public double? BeamwidthDeg { get; init; }
    public int? PatternId { get; init; }
    public int SeqNo { get; init; } = 1;
    /// <summary>BR group linkage; null for generated notices (no grp rows exist).</summary>
    public int? GrpId { get; init; }
}

/// <summary>
/// A single-notice SNS v10 content set: exactly the tables the S.1503-4
/// examination reads, populated the way the worked NEXT101/NEXT102 notices
/// populate them. Built either directly or from <see cref="ConstellationShell"/>s
/// via <see cref="AddShell"/> so the declared orbit/phase rows describe the
/// same system the simulation propagates.
/// </summary>
public sealed class SrsNotice
{
    public int NtcId { get; init; }
    public string SatName { get; init; } = "NGSO-SAT";
    /// <summary>Notifying administration symbol (com_el/notice adm).</summary>
    public string Adm { get; init; } = "XXX";
    public string Prov { get; init; } = "9.6";
    public char NtfRsn { get; init; } = 'C';
    public string StCur { get; init; } = "50";
    public char NtcType { get; init; } = 'N';
    public char ActCode { get; init; } = 'M';
    public DateTime DRcv { get; init; } = new(2026, 1, 1);

    // non_geo constellation flags, mirroring the worked notices.
    public char RefBody { get; init; } = 'T';
    public char FConstell { get; init; } = 'Y';
    public char MultiConfigType { get; init; } = 'S';
    public char ExamSetType { get; init; } = 'L';

    public List<SrsOrbitRow> Orbits { get; } = new();
    public List<SrsPhaseRow> Phases { get; } = new();
    public List<SrsScenario> Scenarios { get; } = new();
    public List<SrsMaskInfo> MaskInfo { get; } = new();
    /// <summary>Operating-parameter set ids (mask_lnk3) -- WP3's param_id values.</summary>
    public List<int> OperatingParamIds { get; } = new();
    /// <summary>Declared earth stations (e_as_stn); required by mask_lnk2 rows that name an e_as_id.</summary>
    public List<SrsEarthStation> EarthStations { get; } = new();

    /// <summary>Total number of planes across shells (non_geo nbr_plane).</summary>
    public int PlaneCount => Orbits.Count;

    /// <summary>
    /// Append one Walker shell as orbit + phase rows (orb_id continues after
    /// existing rows). The mapping mirrors <see cref="Constellation"/>: LAN =
    /// Lan0 + spread * p / P, in-plane anomaly = 360 s / S + 360 F p / (P S)
    /// + offset -- so the declared notice and the simulated system are the
    /// same by construction. Free drift (station keeping N): the examination
    /// derives the artificial precession itself, exactly as the simulation
    /// does.
    /// </summary>
    public void AddShell(ConstellationShell shell)
    {
        double a = OrbitalConstants.EarthRadiusKm + shell.AltitudeKm;
        double periodSec = 2.0 * Math.PI * Math.Sqrt(Math.Pow(a, 3.0) / OrbitalConstants.MuEarth);
        int totalMin = (int)Math.Round(periodSec / 60.0);
        var period = (totalMin / 1440, totalMin % 1440 / 60, totalMin % 60);
        double apogKm = a * (1.0 + shell.Eccentricity) - OrbitalConstants.EarthRadiusKm;
        double perigKm = a * (1.0 - shell.Eccentricity) - OrbitalConstants.EarthRadiusKm;

        int orb0 = Orbits.Count;
        for (int p = 0; p < shell.PlaneCount; p++)
        {
            int orbId = orb0 + p + 1;
            Orbits.Add(new SrsOrbitRow
            {
                OrbId = orbId,
                NbrSatPl = shell.SatsPerPlane,
                LanDeg = Norm360(shell.Lan0Deg + shell.LanSpreadDeg * p / shell.PlaneCount),
                InclinationDeg = shell.InclinationDeg,
                ApogeeKm = apogKm,
                PerigeeKm = perigKm,
                PerigArgDeg = Norm360(shell.ArgumentOfPerigeeDeg),
                OpHtKm = shell.OperatingHeightKm ?? perigKm,
                Period = period,
                StationKeeping = shell.StationKeeping,
                KeepRangeDeg = shell.StationKeeping ? shell.WDeltaDeg : null,
                PrecessionSupplied = shell.PrecessionSupplied,
                PrecessionRateDegPerSec = shell.PrecessionRateDegPerSec,
                RepeatPeriod = shell.RepeatPeriod,
            });
            for (int s = 0; s < shell.SatsPerPlane; s++)
            {
                // phase_ang: angle from the ascending node -- omega + true
                // anomaly; Constellation applies the inverse, so the declared
                // rows and the propagated system agree through the
                // examination's own transform.
                double phase = 360.0 * s / shell.SatsPerPlane
                             + 360.0 * shell.WalkerPhasingF * p / (shell.PlaneCount * shell.SatsPerPlane)
                             + shell.InPlaneOffsetDeg;
                Phases.Add(new SrsPhaseRow(orbId, s + 1, Norm360(phase)));
            }
        }
    }

    private static double Norm360(double v)
    {
        double r = v % 360.0;
        return r < 0 ? r + 360.0 : r;
    }

    /// <summary>Sanity used by the writer: links must reference registered masks / param ids.</summary>
    public void Validate()
    {
        var ids = MaskInfo.Select(m => m.MaskId).ToHashSet();
        foreach (var sc in Scenarios)
        {
            foreach (var l in sc.PfdMaskLinks)
                if (!ids.Contains(l.MaskId))
                    throw new InvalidOperationException($"scenario {sc.ScenId}: mask_lnk1 references unregistered mask_id {l.MaskId}");
            foreach (var l in sc.EsMaskLinks)
                if (!ids.Contains(l.MaskId))
                    throw new InvalidOperationException($"scenario {sc.ScenId}: mask_lnk2 references unregistered mask_id {l.MaskId}");
        }
        foreach (int pid in OperatingParamIds)
            if (!ids.Contains(pid))
                throw new InvalidOperationException($"mask_lnk3 references param_id {pid} not present in mask_info (f_mask=R row expected)");

        var esIds = EarthStations.Select(e => e.EAsId).ToHashSet();
        foreach (var sc in Scenarios)
            foreach (var l in sc.EsMaskLinks)
                if (l.EAsId is int ea && ea != -1 && !esIds.Contains(ea))
                    throw new InvalidOperationException($"scenario {sc.ScenId}: mask_lnk2 names e_as_id {ea} with no e_as_stn row");
        foreach (var es in EarthStations)
            if (es.StnType == 'S' && (es.LonDeg is null || es.LatDeg is null))
                throw new InvalidOperationException($"specific earth station {es.StnName} (e_as_id {es.EAsId}) needs coordinates");

        if (Orbits.Count == 0) throw new InvalidOperationException("notice has no orbit rows");
        foreach (var ph in Phases)
            if (Orbits.All(o => o.OrbId != ph.OrbId))
                throw new InvalidOperationException($"phase row references unknown orb_id {ph.OrbId}");
    }
}
