using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using radians.beamlab;
using radcompute1503_2;

namespace radians.beamlab.app;

/// <summary>
/// The simulation runner: runs the epfd(down) / epfd(is) / epfd(up)
/// simulation from an orbit design document (the space segment) and an
/// operation profile (the operated system -- payload, transmission
/// basics, gates, geography, activity), interactive. One run writes the
/// three CDF CSVs (S.1503-4 D7.1.2 bins, 0.1 dB) next to the chosen
/// base name. An optional *.opparams.json swaps ONLY the scheduler's
/// gates for the declared R constraints -- the truth payload flown
/// under the declared discipline.
/// </summary>
public sealed class SimulationViewModel : ObservableObject
{
    private string _designPath = "";
    public string DesignPath { get => _designPath; set => SetField(ref _designPath, value); }

    private string _opParamsPath = "";
    /// <summary>
    /// Optional operating-parameter set (*.opparams.json): an alternative
    /// gate source. When set the scheduler obeys the declared
    /// pointing/scheduling constraints instead of the profile's enforced
    /// gates; the transmission side still comes from the profile.
    /// </summary>
    public string OpParamsPath { get => _opParamsPath; set => SetField(ref _opParamsPath, value); }

    private string _profilePath = "";
    /// <summary>
    /// The operation profile (*.opprofile.json) -- required. It supplies
    /// the whole system side: scene, transmission basics, gates,
    /// geography, policy, duty and the downlink frequency; the remaining
    /// inputs describe only the victim and the run.
    /// </summary>
    public string ProfilePath { get => _profilePath; set => SetField(ref _profilePath, value); }

    private string _gsoLonText = "10";
    /// <summary>Victim GSO longitude (deg E) -- the wanted satellite for down, the victim for up/is.</summary>
    public string GsoLonText { get => _gsoLonText; set => SetField(ref _gsoLonText, value); }

    private string _esLatText = "45";
    /// <summary>Victim ES latitude (deg); also the up/is victim's boresight point.</summary>
    public string EsLatText { get => _esLatText; set => SetField(ref _esLatText, value); }

    private string _esLonText = "0";
    public string EsLonText { get => _esLonText; set => SetField(ref _esLonText, value); }

    private string _esDishMText = "0.6";
    /// <summary>
    /// S.1428 dish diameter (m) for the victim ES; also the transmitting
    /// ES when the profile's uplink side declares no dish of its own.
    /// </summary>
    public string EsDishMText { get => _esDishMText; set => SetField(ref _esDishMText, value); }

    private string _durationDaysText = "2";
    public string DurationDaysText { get => _durationDaysText; set => SetField(ref _durationDaysText, value); }

    private string _stepSecText = "30";
    public string StepSecText { get => _stepSecText; set => SetField(ref _stepSecText, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set { if (SetField(ref _isRunning, value)) OnPropertyChanged(nameof(RunEnabled)); }
    }

    public bool RunEnabled => !_isRunning;

    /// <summary>Parses every input and reports what a run would cover.</summary>
    public void ValidateInputs()
    {
        try
        {
            var s = BuildSetup();
            var stack = BuildStack(s);   // loads the profile's mask footprint, if declared
            string fp = stack.DownMask is { } m
                ? string.Create(CultureInfo.InvariantCulture,
                    $"; downlink footprint: declared mask ({(m.Kind == MaskPlotKind.AlphaDeltaLong ? "alpha/dLong" : "az/el")}, {m.BlockCount} lat block(s), refbw {m.RefBwKHz:F0} kHz -- no .is.csv)")
                : "";
            string gap = OperationComposer.PerLatExclusionSceneGap(s.Profile) is string g
                ? " -- " + g : "";
            string gates = s.DeclaredOverride is not null
                ? "; gates: declared R set (override)" : "";
            StatusText = string.Create(CultureInfo.InvariantCulture,
                $"ready: {s.Shells.Length} shell(s), {s.SatCount} satellites; victim ES {s.EsLat}/{s.EsLon}, GSO {s.GsoLon} degE; {s.Steps} steps of {s.StepSec} s -- Write CDFs writes .down/.is/.up.csv")
                + gates + fp + gap;
        }
        catch (Exception ex) { StatusText = "invalid: " + ex.Message; }
    }

    /// <summary>
    /// Runs the three simulations on a worker thread and writes the CDFs
    /// as outputBase + ".down.csv" / ".is.csv" / ".up.csv".
    /// </summary>
    public async Task RunAsync(string outputBase)
    {
        Setup setup;
        try { setup = BuildSetup(); }
        catch (Exception ex) { StatusText = "invalid: " + ex.Message; return; }

        IsRunning = true;
        StatusText = string.Create(CultureInfo.InvariantCulture,
            $"running: {setup.Steps} steps x {setup.SatCount} satellites (down+is, then up)...");
        try
        {
            string summary = await Task.Run(() => RunCore(setup, outputBase));
            StatusText = summary;
        }
        catch (Exception ex) { StatusText = "run failed: " + ex.Message; }
        finally { IsRunning = false; }
    }

    /// <summary>Synchronous run (the check harness calls this directly).</summary>
    public string RunCore(Setup setup, string outputBase)
    {
        var inv = CultureInfo.InvariantCulture;
        var con = new Constellation(setup.Shells);
        double simDur = setup.Steps * setup.StepSec;
        double freqMhz = setup.FreqGhz * 1000.0;

        var stack = BuildStack(setup);
        var declared = stack.Declared;
        var scene = stack.Scene;
        var geo = stack.Geo;
        var policy = stack.Policy;
        double duty = stack.Duty;
        double? coverageKm = stack.CoverageKm;
        double sceneAlt = scene.AltitudeKm;

        var isVictim = new EpfdGsoSatVictim
        {
            GsoLonDeg = setup.GsoLon, BoresightLatDeg = setup.EsLat, BoresightLonDeg = setup.EsLon,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APSREC408V01, freqMhz, null),
            GmaxDbi = 40.7, Phi3DbDeg = 1.55,
        };

        var downVictim = new EpfdDownVictim
        {
            EsLatDeg = setup.EsLat, EsLonDeg = setup.EsLon, GsoLonDeg = setup.GsoLon,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, freqMhz, setup.DishM),
        };
        // Downlink footprint per the profile's declared source: the live
        // scheduled composition (with the is byproduct), or the declared
        // PFD mask read the examination's way (no is byproduct -- that
        // needs the e.i.r.p. masks, not the pfd mask).
        EpfdDownResult down;
        if (stack.DownMask is { } downMask)
        {
            down = EpfdDownMask.Run(con, downMask, declared, downVictim,
                setup.StepSec, setup.Steps, PermissiveLimits(), simDur);
        }
        else
        {
            var pointing = new ScheduledPointing(con, geo, declared, scene, simDur,
                coverageKm, policy, duty);
            down = EpfdDown.Run(con, pointing, downVictim, setup.StepSec, setup.Steps,
                PermissiveLimits(), simDur, isVictim);
        }
        string desc = string.Create(inv,
            $"victim ES lat={setup.EsLat} lon={setup.EsLon}, GSO lon={setup.GsoLon}, S.1428 {setup.DishM} m, {freqMhz:F0} MHz");
        if (stack.DownMask is not null) desc += " -- footprint: declared PFD mask (D5.1.4.1)";
        WriteCdf(outputBase + ".down.csv", "epfd(down)", desc,
            down.Accumulator, down.Steps, down.QuietSteps, down.MaxEpfdDb);
        if (down.IsAccumulator is not null)
            WriteCdf(outputBase + ".is.csv", "epfd(is)",
                string.Create(inv, $"victim GSO sat lon={setup.GsoLon}, boresight {setup.EsLat}/{setup.EsLon}, S.672 40.7 dBi / 1.55 deg / Ls -20"),
                down.IsAccumulator, down.Steps, down.IsQuietSteps, down.MaxEpfdIsDb);

        // The up scheduler enforces the UPLINK side's link discipline;
        // a declared R override governs both directions.
        var prof = setup.Profile;
        var upDeclared = setup.DeclaredOverride
            ?? OperationComposer.Compose(prof, sceneAlt, LinkDirection.Up).Enforced;
        var scheduler = new Scheduler(con, geo, upDeclared, new ScenePointing(scene, duty),
            simDur, coverageKm, policy);
        // The uplink transmission basics come from the profile's own side.
        double ulFreqMhz = prof.Up.FrequencyGhz * 1000.0;
        double ulDishM = prof.Up.EsDishM ?? setup.DishM;
        double esPowerDbw = prof.Up.EsPowerDbw ?? 0.0;
        double refElevDeg = prof.Up.PowerControlRefElevDeg
            ?? declared.ElevAngleHeaderDeg ?? prof.MinElevDeg;
        var esModel = new EpfdUpEsModel
        {
            PowerDbw = esPowerDbw,
            Antenna = new radantenna.AntennaLibrary(radantenna.ApType.APERR_019V01, ulFreqMhz, ulDishM),
            PowerControlRefElevDeg = refElevDeg,
        };
        var up = EpfdUp.Run(con, scheduler, geo, isVictim, esModel,
            setup.StepSec, setup.Steps, PermissiveLimits(), simDur);
        WriteCdf(outputBase + ".up.csv", "epfd(up)",
            string.Create(inv, $"ES power {esPowerDbw} dBW, power control ref elev {refElevDeg} deg"),
            up.Accumulator, up.Steps, up.QuietSteps, up.MaxEpfdDb);

        string isPart = down.IsAccumulator is null
            ? "is n/a (mask footprint), "
            : string.Create(inv, $"is max {down.MaxEpfdIsDb:F1} (quiet {down.IsQuietSteps}), ");
        return string.Create(inv,
            $"done: {setup.Steps} steps; down max {down.MaxEpfdDb:F1} dB (quiet {down.QuietSteps}), ")
            + isPart
            + string.Create(inv,
            $"up max {up.MaxEpfdDb:F1} (quiet {up.QuietSteps}); CDFs at {outputBase}.*.csv");
    }

    // ---- composition ----------------------------------------------------

    private sealed record Stack(OperatingParamsSet Declared, PfdMaskViewModel Scene,
        ServiceGeography Geo, SelectionPolicy Policy, double Duty, double? CoverageKm,
        MaskFootprint? DownMask = null);

    // The truth side always comes from the operation profile; a supplied
    // R set swaps ONLY the scheduler's gates for the declared constraints.
    private static Stack BuildStack(Setup setup)
    {
        var sh0 = setup.Shells[0];
        double sceneAlt = sh0.OperatingHeightKm ?? sh0.AltitudeKm;
        var comp = OperationComposer.Compose(setup.Profile, sceneAlt);
        MaskFootprint? downMask = null;
        if (comp.UsesMaskFootprint)
        {
            if (comp.DownlinkMaskXmlPath.Trim().Length == 0)
                throw new InvalidOperationException(
                    "the profile declares a PFD-mask footprint but names no mask XML");
            downMask = MaskFootprint.LoadFile(comp.DownlinkMaskXmlPath);
        }
        return new Stack(setup.DeclaredOverride ?? comp.Enforced, comp.Scene, comp.Geography,
            comp.Policy, comp.IlluminationDutyCycle, comp.CoverageRadiusKm, downMask);
    }

    /// <summary>Everything the animated map needs to march the operation step by step.</summary>
    public sealed record PlaySession(Constellation Con, ServiceGeography Geo,
        Scheduler Scheduler, double StepSec, double DurationSec, int SatCount);

    /// <summary>Composes a fresh scheduler-driven session for the normal (visible) play.</summary>
    public PlaySession BuildPlaySession()
    {
        var setup = BuildSetup();
        var stack = BuildStack(setup);
        var con = new Constellation(setup.Shells);
        double simDur = setup.Steps * setup.StepSec;
        var scheduler = new Scheduler(con, stack.Geo, stack.Declared,
            new ScenePointing(stack.Scene, stack.Duty), simDur, stack.CoverageKm, stack.Policy);
        return new PlaySession(con, stack.Geo, scheduler, setup.StepSec, simDur, setup.SatCount);
    }

    public sealed record Setup(ConstellationShell[] Shells, int SatCount,
        OperatingParamsSet? DeclaredOverride, double FreqGhz, double GsoLon, double EsLat,
        double EsLon, double DishM, long Steps, double StepSec, OperationProfile Profile);

    public Setup BuildSetup()
    {
        if (_designPath.Trim().Length == 0)
            throw new InvalidOperationException("pick an orbit design document first");
        if (_profilePath.Trim().Length == 0)
            throw new InvalidOperationException(
                "pick an operation profile -- the system side (payload, gates, geography) comes from it");
        var doc = OrbitDesignFileCodec.LoadDocument(File.ReadAllText(_designPath));
        var shells = doc.Shells.Select(OrbitDesignFileCodec.ToShell).ToArray();
        int sats = doc.Shells.Sum(d => Math.Max(1, d.PlaneCount) * Math.Max(1, d.SatsPerPlane));

        var prof = OperationProfileCodec.Load(File.ReadAllText(_profilePath));
        shells = OperationComposer.ApplyToShells(prof, shells);

        var declaredOverride = _opParamsPath.Trim().Length > 0
            ? OpParamsFileCodec.ToSet(OpParamsFileCodec.Load(File.ReadAllText(_opParamsPath)))
            : null;

        double days = Num(_durationDaysText, "duration");
        double step = Num(_stepSecText, "time step");
        double dish = Num(_esDishMText, "dish diameter");
        if (days <= 0.0 || step <= 0.0 || dish <= 0.0)
            throw new InvalidOperationException("duration, step and dish must be positive");
        long steps = Math.Max(1, (long)(days * 86400.0 / step));

        return new Setup(shells, sats, declaredOverride, prof.Down.FrequencyGhz,
            Num(_gsoLonText, "GSO longitude"), Num(_esLatText, "ES latitude"),
            Num(_esLonText, "ES longitude"), dish, steps, step, prof);
    }

    // Permissive limits: the deliverable is the CDF, not a verdict.
    private static System.Collections.Generic.List<radlimits.LimitPoint> PermissiveLimits() => new()
    {
        new() { EPFD = -300.0, Perc = 0.001 },
        new() { EPFD = 0.0, Perc = 100.0 },
    };

    private static void WriteCdf(string path, string label, string desc,
        EpfdAccumulator acc, long steps, long quietSteps, double maxDb)
    {
        var (epfd, pct) = acc.BuildCdf();
        var sb = new StringBuilder();
        sb.AppendLine($"# {label} CDF -- simulated at the victim, S.1503-4 D7.1.2 bins (0.1 dB).");
        sb.AppendLine(FormattableString.Invariant($"# {desc}"));
        sb.AppendLine(FormattableString.Invariant(
            $"# steps={steps}  quiet_steps={quietSteps}  max_epfd_db={maxDb:F3}"));
        sb.AppendLine("epfd_dbw_m2_40khz,percent_time_exceeded");
        int first = Array.FindIndex(pct, p => p < 100.0);
        int last = Array.FindLastIndex(pct, p => p > 0.0);
        if (first < 0) { first = 0; last = pct.Length - 1; }
        first = Math.Max(0, first - 1);
        last = Math.Min(pct.Length - 1, last + 1);
        for (int i = first; i <= last; i++)
            sb.AppendLine(FormattableString.Invariant($"{epfd[i]:F1},{pct[i]:G9}"));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static double Num(string text, string what)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : throw new FormatException($"{what}: '{text.Trim()}' is not a number");
}
