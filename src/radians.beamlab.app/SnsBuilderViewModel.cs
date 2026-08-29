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
                if (fm is 'P' or 'S') sc.PfdMaskLinks.Add(new SrsMaskLink(s1++, m.MaskId));
                else if (fm == 'E') sc.EsMaskLinks.Add(new SrsMaskLink(s2++, m.MaskId, EAsId: -1));
            }
            n.Scenarios.Add(sc);
        }

        n.Validate();
        return n;
    }

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
                $"{(n.Scenarios.Count > 0 ? n.Scenarios[0].Frequencies.Count : 0)} frequency range(s)");
        }
        catch (Exception ex) { return ex.Message; }
    }
}
