using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>One loaded orbit design (a shell of the notice).</summary>
public sealed record ShellEntry(string FilePath, OrbitDesignData Data)
{
    public string FileName => Path.GetFileName(FilePath);
    public string Summary => Data.Summary;
}

/// <summary>One mask registration: the XML source plus its mask_info row.</summary>
public sealed class MaskEntry : ObservableObject
{
    private int _maskId = 1;
    public int MaskId { get => _maskId; set => SetField(ref _maskId, value); }

    private string _filePath = "";
    public string FilePath { get => _filePath; set => SetField(ref _filePath, value); }

    private string _fMask = "P";
    /// <summary>P (pfd), E (ES eirp), S (satellite eirp) or R (operating parameters).</summary>
    public string FMask { get => _fMask; set => SetField(ref _fMask, value); }

    private string _fMaskType = "";
    public string FMaskType { get => _fMaskType; set => SetField(ref _fMaskType, value); }

    private double _freqMinMhz = 10700;
    public double FreqMinMhz { get => _freqMinMhz; set => SetField(ref _freqMinMhz, value); }

    private double _freqMaxMhz = 12750;
    public double FreqMaxMhz { get => _freqMaxMhz; set => SetField(ref _freqMaxMhz, value); }

    private string _linkOrbIdText = "";
    /// <summary>Link scope: orb_id (plane) this mask serves; empty = the whole constellation.</summary>
    public string LinkOrbIdText { get => _linkOrbIdText; set => SetField(ref _linkOrbIdText, value); }

    private string _linkSatIdText = "";
    /// <summary>Link scope: satellite number within the plane; empty = every satellite of the plane.</summary>
    public string LinkSatIdText { get => _linkSatIdText; set => SetField(ref _linkSatIdText, value); }

    private string _linkEsIdText = "";
    /// <summary>E masks: the specific earth station (e_as_id) this mask belongs to; empty = typical/all (-1).</summary>
    public string LinkEsIdText { get => _linkEsIdText; set => SetField(ref _linkEsIdText, value); }
}

/// <summary>One declared earth station row (e_as_stn) for specific-ES mask links.</summary>
public sealed class EsEntry : ObservableObject
{
    private int _eAsId = 1;
    public int EAsId { get => _eAsId; set => SetField(ref _eAsId, value); }

    private string _stnName = "ES-1";
    public string StnName { get => _stnName; set => SetField(ref _stnName, value); }

    private string _stnType = "S";
    /// <summary>'S' = specific (needs coordinates), 'T' = typical.</summary>
    public string StnType { get => _stnType; set => SetField(ref _stnType, value); }

    private string _latText = "45";
    public string LatText { get => _latText; set => SetField(ref _latText, value); }

    private string _lonText = "0";
    public string LonText { get => _lonText; set => SetField(ref _lonText, value); }

    private string _antDiamText = "";
    public string AntDiamText { get => _antDiamText; set => SetField(ref _antDiamText, value); }
}

/// <summary>One examination frequency range of the single built scenario.</summary>
public sealed class FreqEntry : ObservableObject
{
    private string _emiRcp = "E";
    /// <summary>E = emission (down / inter-satellite), R = reception (up).</summary>
    public string EmiRcp { get => _emiRcp; set => SetField(ref _emiRcp, value); }

    private double _freqMinMhz = 10700;
    public double FreqMinMhz { get => _freqMinMhz; set => SetField(ref _freqMinMhz, value); }

    private double _freqMaxMhz = 12750;
    public double FreqMaxMhz { get => _freqMaxMhz; set => SetField(ref _freqMaxMhz, value); }
}

/// <summary>
/// The SNS v10 builder: assembles a whole notice from separate elements --
/// orbit designs (shells), mask XMLs and derived operating-parameter sets
/// -- and writes the complete dataset (SRS + Masks databases). Version 1
/// links every space mask (P/S) and every ES mask (E) into one scenario at
/// whole-constellation granularity; R rows register as operating-parameter
/// sets. Pure state -- the window owns dialogs and the database writes.
/// </summary>
public sealed class SnsBuilderViewModel : ObservableObject
{
    private int _ntcId = 900000001;
    public int NtcId { get => _ntcId; set => SetField(ref _ntcId, value); }

    private string _satName = "DESIGN";
    public string SatName { get => _satName; set => SetField(ref _satName, value); }

    private string _adm = "XXX";
    public string Adm { get => _adm; set => SetField(ref _adm, value); }

    private string _scenarioName = "Scenario 1";
    public string ScenarioName { get => _scenarioName; set => SetField(ref _scenarioName, value); }

    public ObservableCollection<ShellEntry> Shells { get; } = new();
    public ObservableCollection<MaskEntry> Masks { get; } = new();
    public ObservableCollection<FreqEntry> Frequencies { get; } = new();
    public ObservableCollection<EsEntry> EarthStations { get; } = new();

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    /// <summary>Loads a design file; a schema-4 document contributes all its shells.</summary>
    public void AddShellFile(string path)
    {
        var doc = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(path));
        foreach (var d in doc.Shells) Shells.Add(new ShellEntry(path, d));
    }

    /// <summary>The assembled notice: shells + mask registry + one auto-linked scenario.</summary>
    public SrsNotice BuildNotice()
    {
        if (Shells.Count == 0) throw new InvalidOperationException("add at least one orbit design");
        var n = new SrsNotice { NtcId = _ntcId, SatName = _satName, Adm = _adm };
        foreach (var sh in Shells) n.AddShell(OrbitDesignFileCodec.ToShell(sh.Data));

        foreach (var m in Masks)
        {
            char fm = (m.FMask.Trim().ToUpperInvariant() + "P")[0];
            char? ft = string.IsNullOrWhiteSpace(m.FMaskType)
                ? null : char.ToUpperInvariant(m.FMaskType.Trim()[0]);
            n.MaskInfo.Add(new SrsMaskInfo(m.MaskId, m.FreqMinMhz, m.FreqMaxMhz, fm, ft));
            if (fm == 'R') n.OperatingParamIds.Add(m.MaskId);
        }

        foreach (var es in EarthStations)
            n.EarthStations.Add(new SrsEarthStation
            {
                EAsId = es.EAsId,
                StnName = es.StnName,
                StnType = (es.StnType.Trim().ToUpperInvariant() + "S")[0],
                LatDeg = OptNum(es.LatText, $"earth station {es.EAsId} latitude"),
                LonDeg = OptNum(es.LonText, $"earth station {es.EAsId} longitude"),
                AntDiamM = OptNum(es.AntDiamText, $"earth station {es.EAsId} dish"),
            });

        if (Frequencies.Count > 0)
        {
            var sc = new SrsScenario { ScenId = 1, ScenName = _scenarioName };
            int seq = 1;
            foreach (var f in Frequencies)
                sc.Frequencies.Add(new SrsFreqRange(seq++,
                    (f.EmiRcp.Trim().ToUpperInvariant() + "E")[0], f.FreqMinMhz, f.FreqMaxMhz));
            int s1 = 1, s2 = 1;
            foreach (var m in Masks)
            {
                char fm = (m.FMask.Trim().ToUpperInvariant() + "P")[0];
                // Per-row link scope: empty orb = the whole constellation
                // (-1), empty sat = every satellite of the plane, empty
                // e_as = typical/all (-1).
                int orb = OptInt(m.LinkOrbIdText, $"mask {m.MaskId} orb link") ?? -1;
                int? sat = OptInt(m.LinkSatIdText, $"mask {m.MaskId} sat link");
                if (orb != -1 && n.Orbits.All(o => o.OrbId != orb))
                    throw new InvalidOperationException($"mask {m.MaskId}: orb link {orb} matches no orbit row");
                if (sat is not null && orb == -1)
                    throw new InvalidOperationException($"mask {m.MaskId}: a sat link needs an orb link");
                if (fm is 'P' or 'S')
                    sc.PfdMaskLinks.Add(new SrsMaskLink(s1++, m.MaskId, orb, sat));
                else if (fm == 'E')
                    sc.EsMaskLinks.Add(new SrsMaskLink(s2++, m.MaskId, orb, sat,
                        OptInt(m.LinkEsIdText, $"mask {m.MaskId} e_as link") ?? -1));
            }
            n.Scenarios.Add(sc);
        }

        n.Validate();
        return n;
    }

    private static double? OptNum(string text, string what)
        => text.Trim().Length == 0 ? null
            : double.TryParse(text, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : throw new FormatException($"{what}: '{text.Trim()}' is not a number");

    private static int? OptInt(string text, string what)
        => text.Trim().Length == 0 ? null
            : int.TryParse(text, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : throw new FormatException($"{what}: '{text.Trim()}' is not a whole number");

    /// <summary>The Masks-database content rows for the registered masks.</summary>
    public IReadOnlyList<SrsMdbWriter.MaskContent> BuildMaskContents()
        => Masks.Select(m => new SrsMdbWriter.MaskContent(m.MaskId, m.FilePath,
                (m.FMask.Trim().ToUpperInvariant() + "P")[0], m.FreqMinMhz, m.FreqMaxMhz))
            .ToList();

    public string SummaryText()
    {
        try
        {
            var n = BuildNotice();
            return string.Create(CultureInfo.InvariantCulture,
                $"{Shells.Count} shell(s) -> {n.Orbits.Count} orbit / {n.Phases.Count} phase rows; " +
                $"{n.MaskInfo.Count} mask_info row(s), {n.OperatingParamIds.Count} R set(s); " +
                $"{(n.Scenarios.Count > 0 ? n.Scenarios[0].Frequencies.Count : 0)} frequency range(s); " +
                $"{n.EarthStations.Count} earth station(s)");
        }
        catch (Exception ex) { return ex.Message; }
    }
}
