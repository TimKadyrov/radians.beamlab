using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using radians.beamlab;

namespace radians.beamlab.app;

/// <summary>One latitude grid point's verdict.</summary>
public sealed record ComplianceRow(double LatDeg, double MaxEpfdDb, double WorstMarginDb,
    bool Pass, long QuietSteps)
{
    public string LatText => LatDeg.ToString("F0", CultureInfo.InvariantCulture);
    public string MaxText => double.IsFinite(MaxEpfdDb)
        ? MaxEpfdDb.ToString("F1", CultureInfo.InvariantCulture) : "quiet";
    public string MarginText => double.IsFinite(WorstMarginDb)
        ? WorstMarginDb.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) : "";
    public string PassText => Pass ? "PASS" : "FAIL";
}

/// <summary>
/// Stage B of the compliance loop (docs/compliance-loop-plan.md): sweep
/// epfd(down) victims across a latitude grid, verdict each point with the
/// examination's own limit comparison, and report the worst dB margin.
/// The system side comes from the operation profile; the limits are
/// hand-entered points of the applicable Article 22 table.
/// </summary>
public sealed class ComplianceViewModel : ObservableObject
{
    private string _designPath = "";
    public string DesignPath { get => _designPath; set => SetField(ref _designPath, value); }

    private string _profilePath = "";
    public string ProfilePath { get => _profilePath; set => SetField(ref _profilePath, value); }

    private string _esLonText = "0";
    /// <summary>Victim ES longitude (deg) for every grid point.</summary>
    public string EsLonText { get => _esLonText; set => SetField(ref _esLonText, value); }

    private string _gsoOffsetText = "10";
    /// <summary>Wanted GSO longitude = ES longitude + this offset (deg).</summary>
    public string GsoOffsetText { get => _gsoOffsetText; set => SetField(ref _gsoOffsetText, value); }

    private string _dishMText = "0.6";
    public string DishMText { get => _dishMText; set => SetField(ref _dishMText, value); }

    private string _latFromText = "0";
    public string LatFromText { get => _latFromText; set => SetField(ref _latFromText, value); }

    private string _latToText = "70";
    public string LatToText { get => _latToText; set => SetField(ref _latToText, value); }

    private string _latStepText = "10";
    public string LatStepText { get => _latStepText; set => SetField(ref _latStepText, value); }

    private string _durationDaysText = "0.1";
    public string DurationDaysText { get => _durationDaysText; set => SetField(ref _durationDaysText, value); }

    private string _stepSecText = "60";
    public string StepSecText { get => _stepSecText; set => SetField(ref _stepSecText, value); }

    // The template pair is wide (it sets the accumulator's bin range) and
    // verdict-permissive under the D7.1.3 rule Pt <= Pi; replace it with
    // the real Article 22 rows for the band and dish.
    private string _limitsText = "-300 100\n0 0.0001";
    /// <summary>Limit points, one "epfd_db percent_time" per line (the applicable Article 22 table rows).</summary>
    public string LimitsText { get => _limitsText; set => SetField(ref _limitsText, value); }

    public ObservableCollection<ComplianceRow> Rows { get; } = new();

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set { if (SetField(ref _isRunning, value)) OnPropertyChanged(nameof(RunEnabled)); }
    }

    public bool RunEnabled => !_isRunning;

    // ---- the sweep ------------------------------------------------------

    public sealed record Sweep(ConstellationShell[] Shells, OperationProfile Profile,
        double EsLon, double GsoOffset, double DishM,
        double LatFrom, double LatTo, double LatStep,
        long Steps, double StepSec, List<radlimits.LimitPoint> Limits);

    public Sweep BuildSweep()
    {
        if (_designPath.Trim().Length == 0)
            throw new InvalidOperationException("pick a design document first");
        if (_profilePath.Trim().Length == 0)
            throw new InvalidOperationException("pick an operation profile - it is the system under test");
        var doc = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(_designPath));
        var prof = OperationProfileCodec.Load(File.ReadAllText(_profilePath));
        var shells = OperationComposer.ApplyToShells(prof,
            doc.Shells.Select(OrbitDesignFileCodec.ToShell));

        double days = Num(_durationDaysText, "duration");
        double step = Num(_stepSecText, "time step");
        double latFrom = Num(_latFromText, "lat from");
        double latTo = Num(_latToText, "lat to");
        double latStep = Num(_latStepText, "lat step");
        if (days <= 0.0 || step <= 0.0 || latStep <= 0.0 || latTo < latFrom)
            throw new InvalidOperationException("duration, step and lat step must be positive; lat to >= lat from");

        return new Sweep(shells, prof,
            Num(_esLonText, "ES longitude"), Num(_gsoOffsetText, "GSO offset"),
            Num(_dishMText, "dish diameter"),
            latFrom, latTo, latStep,
            Math.Max(1, (long)(days * 86400.0 / step)), step, ParseLimits(_limitsText));
    }

    public async Task RunAsync()
    {
        Sweep sweep;
        try { sweep = BuildSweep(); }
        catch (Exception ex) { StatusText = "invalid: " + ex.Message; return; }

        IsRunning = true;
        StatusText = "sweeping latitudes...";
        try
        {
            var rows = await Task.Run(() => RunSweep(sweep, sweep.Profile.AlphaExclDeg));
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
            string gap = OperationComposer.PerLatExclusionSceneGap(sweep.Profile) is string g
                ? g + " -- " : "";
            StatusText = gap + (sweep.Profile.Down.FootprintSource == "mask"
                ? "declared-mask footprint -- " : "") + SummarizeRows(rows);
        }
        catch (Exception ex) { StatusText = "sweep failed: " + ex.Message; }
        finally { IsRunning = false; }
    }

    /// <summary>
    /// One full latitude sweep at the given exclusion angle (the profile's
    /// other characteristics unchanged). Synchronous; the advisor and the
    /// check harness call it directly.
    /// </summary>
    public static List<ComplianceRow> RunSweep(Sweep sweep, double alphaExclDeg)
    {
        var prof = sweep.Profile with { AlphaExclDeg = alphaExclDeg, AlphaByLat = null };
        var con = new Constellation(sweep.Shells);
        var sh0 = sweep.Shells[0];
        var comp = OperationComposer.Compose(prof, sh0.OperatingHeightKm ?? sh0.AltitudeKm);
        double simDur = sweep.Steps * sweep.StepSec;
        double freqMhz = prof.Down.FrequencyGhz * 1000.0;
        // Declared-mask footprint: the sweep then runs the examination's
        // own down algorithm (D5.1.4.1) against the mask + walked alpha;
        // the mask file itself is fixed while the advisor walks the zone.
        MaskFootprint? downMask = comp.UsesMaskFootprint
            ? MaskFootprint.LoadFile(comp.DownlinkMaskXmlPath)
            : null;

        var rows = new List<ComplianceRow>();
        for (double lat = sweep.LatFrom; lat <= sweep.LatTo + 1e-9; lat += sweep.LatStep)
        {
            var victim = new EpfdDownVictim
            {
                EsLatDeg = lat, EsLonDeg = sweep.EsLon, GsoLonDeg = sweep.EsLon + sweep.GsoOffset,
                Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, freqMhz, sweep.DishM),
            };
            EpfdDownResult res;
            if (downMask is not null)
            {
                res = EpfdDownMask.Run(con, downMask, comp.Enforced, victim,
                    sweep.StepSec, sweep.Steps, sweep.Limits, simDur);
            }
            else
            {
                var pointing = new ScheduledPointing(con, comp.Geography, comp.Enforced, comp.Scene,
                    simDur, comp.CoverageRadiusKm, comp.Policy, comp.IlluminationDutyCycle);
                res = EpfdDown.Run(con, pointing, victim, sweep.StepSec, sweep.Steps,
                    sweep.Limits, simDur);
            }
            var (passResults, _) = res.Accumulator.CompareWithLimits(sweep.Limits);
            var (epfd, pct) = res.Accumulator.BuildCdf();
            double worst = sweep.Limits.Count == 0 ? double.PositiveInfinity
                : sweep.Limits.Min(l => MarginDb(epfd, pct, l.EPFD, l.Perc));
            rows.Add(new ComplianceRow(lat, res.MaxEpfdDb, worst,
                passResults.All(p => p), res.QuietSteps));
        }
        return rows;
    }

    public static string SummarizeRows(IReadOnlyList<ComplianceRow> rows)
    {
        var failing = rows.Where(r => !r.Pass).ToList();
        return failing.Count == 0
            ? string.Create(CultureInfo.InvariantCulture,
                $"COMPLIANT at all {rows.Count} latitude(s); worst margin {rows.Min(r => r.WorstMarginDb):+0.0;-0.0} dB")
            : string.Create(CultureInfo.InvariantCulture,
                $"EXCEEDED at {failing.Count} of {rows.Count} latitude(s) ({string.Join(", ", failing.Select(f => f.LatText))}); worst margin {failing.Min(r => r.WorstMarginDb):+0.0;-0.0} dB");
    }

    /// <summary>
    /// dB margin at one limit point: the limit epfd minus the measured
    /// epfd whose exceedance is at most the allowed percentage. Positive
    /// means room to spare.
    /// </summary>
    public static double MarginDb(double[] epfd, double[] pct, double limitEpfd, double limitPerc)
    {
        int i = Array.FindIndex(pct, v => v <= limitPerc);
        double measured = i < 0 ? epfd[^1] : epfd[i];
        return limitEpfd - measured;
    }

    public static List<radlimits.LimitPoint> ParseLimits(string text)
    {
        var pts = new List<radlimits.LimitPoint>();
        var raw = text.Split('\n');
        for (int i = 0; i < raw.Length; i++)
        {
            string line = raw[i].Trim();
            if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new FormatException($"limits line {i + 1}: expected 'epfd_db percent'");
            pts.Add(new radlimits.LimitPoint
            {
                EPFD = Num(parts[0], $"limits line {i + 1} epfd"),
                Perc = Num(parts[1], $"limits line {i + 1} percent"),
            });
        }
        if (pts.Count == 0) throw new InvalidOperationException("enter at least one limit point");
        return pts;
    }

    // ---- limits from the BR database ------------------------------------

    private string _limitsDbPathText = "";
    /// <summary>Path of the BR limits database (EPFD_limits_*.mdb).</summary>
    public string LimitsDbPathText { get => _limitsDbPathText; set => SetField(ref _limitsDbPathText, value); }

    /// <summary>Display rows of the loaded limits, index-aligned with the internal list.</summary>
    public ObservableCollection<string> LimitChoices { get; } = new();

    private List<radlimits.Limit> _loadedLimits = new();

    private int _selectedLimitIndex = -1;
    public int SelectedLimitIndex { get => _selectedLimitIndex; set => SetField(ref _selectedLimitIndex, value); }

    // The BR native pair's known homes on this machine (same convention
    // as the check harness); used only when the DLL is not already
    // resolvable next to the process.
    private static readonly string[] KnownDllDirs =
    {
        @"C:\Projects\_EPFD\radians\radians\dlls",
        @"C:\Projects\_EPFD\radians\radians\bin\Debug\net10.0-windows7.0",
    };

    /// <summary>
    /// Reads the applicable epfd(down) Article 22 rows from the BR
    /// limits database for the profile's downlink carrier (a
    /// reference-bandwidth sliver as the band, the shells' lowest
    /// operating height) via the vendored radlimits interop -- the
    /// radians reader's calling pattern. Fills the choice list; Use
    /// puts the chosen row's points into the limit text.
    /// </summary>
    public void LoadLimitsFromDb()
    {
        try
        {
            var sweep = BuildSweep();   // design + profile supply carrier and height
            if (_limitsDbPathText.Trim().Length == 0)
                throw new InvalidOperationException("pick the limits database (*.mdb) first");
            LimitsDbReader.DllDirectory ??= KnownDllDirs.FirstOrDefault(
                d => File.Exists(Path.Combine(d, "EpfdLimitsApi64.dll")));

            double fMhz = sweep.Profile.Down.FrequencyGhz * 1000.0;
            double bwKhz = sweep.Profile.Down.RefBwKHz;
            double halfMhz = bwKhz / 2000.0;
            double opHt = sweep.Shells.Min(s => s.OperatingHeightKm ?? s.AltitudeKm);
            _loadedLimits = LimitsDbReader.Read(_limitsDbPathText.Trim(),
                fMhz - halfMhz, fMhz + halfMhz, bwKhz, opHt);

            LimitChoices.Clear();
            foreach (var l in _loadedLimits) LimitChoices.Add(DescribeLimit(l));
            SelectedLimitIndex = _loadedLimits.Count > 0 ? 0 : -1;
            StatusText = string.Create(CultureInfo.InvariantCulture,
                $"{_loadedLimits.Count} limit row(s) at {fMhz:F0} MHz, op height {opHt:F0} km -- pick one and Use to fill the points");
        }
        catch (Exception ex) { StatusText = "limits load failed: " + ex.Message; }
    }

    /// <summary>Puts the selected loaded row's points into the limit text.</summary>
    public void UseSelectedLimit()
    {
        if (_selectedLimitIndex < 0 || _selectedLimitIndex >= _loadedLimits.Count)
        { StatusText = "load and pick a limit row first"; return; }
        var l = _loadedLimits[_selectedLimitIndex];
        if (l.ShortTermLatDependent)
        {
            // The flat text cannot express per-latitude short-term rows;
            // show them for hand transcription of the applicable band.
            StatusText = "lat-dependent short-term limit -- transcribe the applicable latitude band by hand: "
                + string.Join("; ", l.Points.Select(p => FormattableString.Invariant(
                    $"lat {p.LatMin}..{p.LatMax}: {p.EPFD} dB at {p.Perc}%")));
            return;
        }
        LimitsText = LimitPointsText(l);
        StatusText = string.Create(CultureInfo.InvariantCulture,
            $"limit points filled from {l.RrRef} ({l.Points.Count} point(s)) -- the sweep verdicts against exactly this text");
    }

    /// <summary>The limit text a loaded row fills in -- one "epfd perc" line per point.</summary>
    public static string LimitPointsText(radlimits.Limit l)
        => string.Join("\n", l.Points.Select(p =>
            FormattableString.Invariant($"{p.EPFD} {p.Perc}")));

    /// <summary>One line describing a loaded limit row.</summary>
    public static string DescribeLimit(radlimits.Limit l)
        => string.Create(CultureInfo.InvariantCulture,
            $"{l.RrRef} -- {l.Service} {l.RegFreq_min:F0}-{l.RegFreq_max:F0} MHz, refbw {l.RefBW:F0} kHz")
           + (l.Rf_diam is double d ? string.Create(CultureInfo.InvariantCulture, $", dish {d:F2} m") : "")
           + $", regions {string.Join("/", l.regions)}"
           + (l.ShortTermLatDependent ? ", lat-dependent short-term" : "");

    // ---- stage C: the exclusion advisor ---------------------------------

    private string _alphaStepText = "1";
    public string AlphaStepText { get => _alphaStepText; set => SetField(ref _alphaStepText, value); }

    private string _alphaMaxText = "15";
    public string AlphaMaxText { get => _alphaMaxText; set => SetField(ref _alphaMaxText, value); }

    private double? _foundAlphaDeg;
    public double? FoundAlphaDeg
    {
        get => _foundAlphaDeg;
        private set { if (SetField(ref _foundAlphaDeg, value)) OnPropertyChanged(nameof(ApplyEnabled)); }
    }

    public bool ApplyEnabled => _foundAlphaDeg is not null && !_isRunning;

    public async Task AdviseAsync()
    {
        Sweep sweep;
        double stepA, maxA;
        try
        {
            sweep = BuildSweep();
            stepA = Num(_alphaStepText, "alpha step");
            maxA = Num(_alphaMaxText, "alpha cap");
            if (stepA <= 0.0) throw new InvalidOperationException("alpha step must be positive");
        }
        catch (Exception ex) { StatusText = "invalid: " + ex.Message; return; }

        IsRunning = true;
        FoundAlphaDeg = null;
        StatusText = "advising: walking the exclusion angle...";
        try
        {
            var advice = await Task.Run(() => Advise(sweep, stepA, maxA));
            Rows.Clear();
            foreach (var r in advice.FinalRows) Rows.Add(r);
            FoundAlphaDeg = advice.FoundAlpha;
            StatusText = advice.FoundAlpha is double a
                ? string.Create(CultureInfo.InvariantCulture,
                    $"compliant at alpha = {a:F1} deg after {advice.Iterations} sweep(s)")
                  + (advice.FailingAtStart.Count > 0
                     && advice.FailingAtStart.Count < advice.FinalRows.Count
                        ? string.Create(CultureInfo.InvariantCulture,
                            $"; at the starting alpha only lat(s) {string.Join(", ", advice.FailingAtStart.Select(l => l.ToString("F0", CultureInfo.InvariantCulture)))} failed -- per-latitude alpha rows are the finer declaration")
                        : "")
                  + " -- Apply writes it into the profile"
                : string.Create(CultureInfo.InvariantCulture,
                    $"no compliant alpha up to {maxA:F1} deg ({advice.Iterations} sweep(s)): worst margin {advice.WorstMarginEndDb:+0.0;-0.0} dB at lat {advice.WorstLatEndDeg:F0}, ")
                  + TrendText(advice.WorstMarginStartDb, advice.WorstMarginEndDb)
                  + (advice.WorstMarginEndDb - advice.WorstMarginStartDb > 0.5
                        ? " -- raise the cap to continue the walk"
                     : advice.WorstMarginStartDb - advice.WorstMarginEndDb > 0.5
                        ? " -- a larger exclusion worsens this geometry"
                        : " -- the exclusion angle is not the binding lever; adjust the system");
        }
        catch (Exception ex) { StatusText = "advise failed: " + ex.Message; }
        finally { IsRunning = false; }
    }

    /// <summary>
    /// The walk's outcome plus its trajectory: the worst margin at the
    /// first and last swept alpha (NaN when no sweep ran) and the
    /// latitude holding the final worst margin -- so a failed walk can
    /// say where the margin ended and which way it was moving, not just
    /// "not compliant".
    /// </summary>
    public sealed record Advice(double? FoundAlpha, List<ComplianceRow> FinalRows,
        int Iterations, List<double> FailingAtStart,
        double WorstMarginStartDb = double.NaN, double WorstMarginEndDb = double.NaN,
        double WorstLatEndDeg = double.NaN);

    /// <summary>How the worst margin moved over the walk, as a phrase.</summary>
    public static string TrendText(double startDb, double endDb)
        => !double.IsFinite(startDb) || !double.IsFinite(endDb) ? "trend unknown"
           : Math.Abs(endDb - startDb) < 0.5 ? "not changing with alpha"
           : endDb > startDb
               ? string.Create(CultureInfo.InvariantCulture, $"improving with alpha (+{endDb - startDb:F1} dB over the walk)")
               : string.Create(CultureInfo.InvariantCulture, $"worsening with alpha ({endDb - startDb:F1} dB over the walk)");

    /// <summary>
    /// Walks the global exclusion angle from the profile's value upward
    /// until the sweep is compliant or the cap is reached. Linear walk by
    /// design: a predictable run count under the user's duration/step.
    /// </summary>
    public static Advice Advise(Sweep sweep, double alphaStep, double alphaMax)
    {
        double a0 = sweep.Profile.AlphaExclDeg;
        var failingAtStart = new List<double>();
        var last = new List<ComplianceRow>();
        int iter = 0;
        double worstStart = double.NaN;
        for (double a = a0; a <= alphaMax + 1e-9; a += alphaStep)
        {
            iter++;
            last = RunSweep(sweep, a);
            if (iter == 1)
            {
                failingAtStart = last.Where(r => !r.Pass).Select(r => r.LatDeg).ToList();
                worstStart = last.Min(r => r.WorstMarginDb);
            }
            if (last.All(r => r.Pass)) return new Advice(a, last, iter, failingAtStart,
                worstStart, last.Min(r => r.WorstMarginDb),
                last.OrderBy(r => r.WorstMarginDb).First().LatDeg);
        }
        var worstEnd = last.Count > 0 ? last.OrderBy(r => r.WorstMarginDb).First() : null;
        return new Advice(null, last, iter, failingAtStart,
            worstStart, worstEnd?.WorstMarginDb ?? double.NaN, worstEnd?.LatDeg ?? double.NaN);
    }

    /// <summary>Writes the found global exclusion back into the profile file (step 8's hand-off).</summary>
    public void ApplyFoundAlpha()
    {
        if (_foundAlphaDeg is not double a) return;
        var prof = OperationProfileCodec.Load(File.ReadAllText(_profilePath));
        File.WriteAllText(_profilePath,
            OperationProfileCodec.Save(prof with { AlphaExclDeg = a, AlphaByLat = null }));
        StatusText = string.Create(CultureInfo.InvariantCulture,
            $"alpha {a:F1} deg written into the profile -- derive the R set and export the masks next");
    }

    public string BuildCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("es_lat_deg,max_epfd_db,worst_margin_db,pass,quiet_steps");
        foreach (var r in Rows)
            sb.AppendLine(FormattableString.Invariant(
                $"{r.LatDeg},{r.MaxEpfdDb:F2},{r.WorstMarginDb:F2},{(r.Pass ? 1 : 0)},{r.QuietSteps}"));
        return sb.ToString();
    }

    private static double Num(string text, string what)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : throw new FormatException($"{what}: '{text.Trim()}' is not a number");
}
