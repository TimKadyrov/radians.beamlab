using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace radians.beamlab.app;

/// <summary>
/// State for the mask-XML export dialog. Holds the metadata / resolution
/// inputs, derives the inclination-capped latitude range, and drives the
/// asynchronous <see cref="MaskXmlExport.GenerateAsync"/>. The coordinate type
/// follows the live tab's current mask kind.
/// </summary>
public sealed class MaskXmlExportViewModel : ObservableObject
{
    private readonly PfdMaskViewModel _live;
    private CancellationTokenSource? _cts;

    public MaskXmlExportViewModel(PfdMaskViewModel live)
    {
        _live = live;
        _refBwKHz = live.RefBwKHz;
        // Prefill a rough band around the tab's centre frequency.
        double centreMhz = live.FrequencyGHz * 1000.0;
        _lowFreqMhz = Math.Round(centreMhz - 1000.0);
        _highFreqMhz = Math.Round(centreMhz + 1000.0);
        ClampLatitudes();

        GenerateCommand = new AsyncRelayCommand(GenerateAsync);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
    }

    public ICommand GenerateCommand { get; }
    public ICommand CancelCommand { get; }

    // --- Metadata ---
    private string _satName = "NGSO-SAT";
    public string SatName { get => _satName; set => SetField(ref _satName, value); }

    private int _ntcId;
    public int NtcId { get => _ntcId; set => SetField(ref _ntcId, value); }

    private int _maskId = 1;
    public int MaskId { get => _maskId; set => SetField(ref _maskId, value); }

    private double _lowFreqMhz;
    public double LowFreqMhz { get => _lowFreqMhz; set => SetField(ref _lowFreqMhz, value); }

    private double _highFreqMhz;
    public double HighFreqMhz { get => _highFreqMhz; set => SetField(ref _highFreqMhz, value); }

    private double _refBwKHz;
    public double RefBwKHz { get => _refBwKHz; set => SetField(ref _refBwKHz, value); }

    // --- Orbit / latitude range ---
    private double _inclinationDeg = 53.0;
    public double InclinationDeg
    {
        get => _inclinationDeg;
        set
        {
            if (SetField(ref _inclinationDeg, value))
            {
                OnPropertyChanged(nameof(MaxLatDeg));
                OnPropertyChanged(nameof(LatRangeReadout));
                ClampLatitudes();
            }
        }
    }

    /// <summary>Maximum sub-satellite latitude reachable at the current inclination.</summary>
    public double MaxLatDeg => MaskXmlExport.MaxLatitudeForInclination(_inclinationDeg);

    private double _latMinDeg = -53.0;
    public double LatMinDeg
    {
        get => _latMinDeg;
        set { if (SetField(ref _latMinDeg, ClampLat(value))) OnPropertyChanged(nameof(LatRangeReadout)); }
    }

    private double _latMaxDeg = 53.0;
    public double LatMaxDeg
    {
        get => _latMaxDeg;
        set { if (SetField(ref _latMaxDeg, ClampLat(value))) OnPropertyChanged(nameof(LatRangeReadout)); }
    }

    private double _latStepDeg = 5.0;
    public double LatStepDeg { get => _latStepDeg; set { if (value > 0) SetField(ref _latStepDeg, value); } }

    public string LatRangeReadout =>
        $"max |lat| = {MaxLatDeg:F1}°   →   table {_latMinDeg:F1}° … {_latMaxDeg:F1}°";

    // --- Output resolution (labels depend on the tab's mask kind) ---
    private double _bStepDeg = 2.0;
    public double BStepDeg { get => _bStepDeg; set { if (value > 0 && SetField(ref _bStepDeg, value)) OnPropertyChanged(nameof(StepHint)); } }

    private double _cStepDeg = 5.0;
    public double CStepDeg { get => _cStepDeg; set { if (value > 0 && SetField(ref _cStepDeg, value)) OnPropertyChanged(nameof(StepHint)); } }

    /// <summary>
    /// Guidance under the step inputs: the tab's quarter-beamwidth
    /// recommendation, plus a conservatism note when the chosen steps are
    /// coarser. Peaks are never lost either way (envelope binning).
    /// </summary>
    public string StepHint
    {
        get
        {
            double rec = _live.RecommendedStepDeg;
            if (rec <= 0.0) return "";
            string basis = $"Recommended step ≤ {rec:F2}° (¼ of the narrowest 3 dB beamwidth).";
            return Math.Max(_bStepDeg, _cStepDeg) > rec + 1e-9
                ? basis + " Coarser steps keep the exact peaks but make the mask more conservative between nodes."
                : basis;
        }
    }

    private bool _envelopeOverHeadings = true;
    /// <summary>
    /// WP4 derivation (default): per latitude the mask envelopes the
    /// ascending- and descending-pass headings of the body-stabilised
    /// layout. Off = the single north-aligned configuration the live
    /// plots show.
    /// </summary>
    public bool EnvelopeOverHeadings
    {
        get => _envelopeOverHeadings;
        set => SetField(ref _envelopeOverHeadings, value);
    }

    private static readonly MaskExportFormat[] Formats = { MaskExportFormat.Xml, MaskExportFormat.Csv, MaskExportFormat.Both };
    private int _formatIndex;
    /// <summary>ComboBox index into {XML, CSV, XML+CSV}.</summary>
    public int FormatIndex { get => _formatIndex; set { if (value >= 0 && value < Formats.Length) SetField(ref _formatIndex, value); } }
    public MaskExportFormat Format => Formats[_formatIndex];

    public bool IsAlphaDelta => _live.MaskKind == MaskPlotKind.AlphaDeltaLong;
    public string BAxisLabel => IsAlphaDelta ? "α step (deg, ±90)" : "azimuth step (deg, ±90)";
    public string CAxisLabel => IsAlphaDelta ? "ΔLongitude step (deg, ±180)" : "elevation step (deg, ±90)";
    public string MaskKindLabel => IsAlphaDelta
        ? "α / ΔLongitude  (type = alpha_deltaLongitude)"
        : "Azimuth / Elevation  (type = azimuth_elevation)";

    // --- Output file + progress ---
    private string _outputPath = "";
    public string OutputPath { get => _outputPath; set => SetField(ref _outputPath, value); }

    private double _progressValue;
    public double ProgressValue { get => _progressValue; set => SetField(ref _progressValue, value); }

    private bool _isGenerating;
    public bool IsGenerating { get => _isGenerating; set => SetField(ref _isGenerating, value); }

    private string _statusText = "ready";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private double ClampLat(double v) => Math.Clamp(v, -MaxLatDeg, MaxLatDeg);

    private void ClampLatitudes()
    {
        LatMinDeg = ClampLat(_latMinDeg);
        LatMaxDeg = ClampLat(_latMaxDeg);
    }

    private async Task GenerateAsync()
    {
        if (string.IsNullOrWhiteSpace(_outputPath)) { StatusText = "choose an output file first"; return; }
        if (_latMaxDeg <= _latMinDeg) { StatusText = "latitude max must exceed min"; return; }

        var opts = new MaskXmlExportOptions
        {
            SatName = _satName,
            NtcId = _ntcId,
            MaskId = _maskId,
            LowFreqMhz = _lowFreqMhz,
            HighFreqMhz = _highFreqMhz,
            RefBwKHz = _refBwKHz,
            LatMinDeg = _latMinDeg,
            LatMaxDeg = _latMaxDeg,
            LatStepDeg = _latStepDeg,
            BStepDeg = _bStepDeg,
            CStepDeg = _cStepDeg,
            Kind = _live.MaskKind,
            Format = Format,
            OutputPath = _outputPath,
        };

        _cts = new CancellationTokenSource();
        IsGenerating = true;
        ProgressValue = 0;
        StatusText = "generating…";
        try
        {
            var progress = new Progress<double>(p =>
            {
                ProgressValue = p * 100.0;
                StatusText = $"generating… {p * 100.0:F0}%";
            });
            IPfdMaskSampler sampler = _envelopeOverHeadings
                ? new ReachableEnvelopeSampler(_live, opts, _inclinationDeg)
                : new MaskExportSampler(_live, opts);
            await MaskXmlExport.GenerateAsync(sampler, opts, progress, _cts.Token);
            ProgressValue = 100.0;
            string exts = Format switch
            {
                MaskExportFormat.Xml => ".xml",
                MaskExportFormat.Csv => ".csv",
                _ => ".xml + .csv",
            };
            StatusText = $"done → {System.IO.Path.GetFileNameWithoutExtension(_outputPath)}{exts}";
        }
        catch (OperationCanceledException) { StatusText = "cancelled"; }
        catch (Exception ex) { StatusText = "error: " + ex.Message; }
        finally { IsGenerating = false; _cts?.Dispose(); _cts = null; }
    }
}
