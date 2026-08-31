using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using radlimits;

namespace radians.beamlab;

/// <summary>
/// Reads the BR limits database (EPFD_limits_*.mdb) through the vendored
/// radlimits interop -- the calling pattern of the radians reader
/// (radaccess NGLimits.ReadDirect) reproduced: the FSS and BSS service
/// codes, rows extracted per examination/direction with the band
/// midpoint as the assigned frequency (GHz), the emission bandwidth in
/// kHz and the operating height in km (the reference date is fixed
/// inside the vendored ExtractLimits), merged by region exactly as the
/// radians reader merges them. Requires the BR native pair
/// EpfdLimitsApi64.dll + EpfdLimitsDbAccessLib64.dll: either next to
/// the process or via <see cref="DllDirectory"/>.
/// </summary>
public static class LimitsDbReader
{
    /// <summary>Directory containing EpfdLimitsApi64.dll; set before the first call.</summary>
    public static string? DllDirectory { get; set; }

    static LimitsDbReader()
    {
        NativeLibrary.SetDllImportResolver(typeof(LimitsDbReader).Assembly, (name, _, _) =>
        {
            if (name == "EpfdLimitsApi64.dll" && DllDirectory is string dir)
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return NativeLibrary.Load(p);
            }
            return IntPtr.Zero;
        });
    }

    /// <summary>
    /// Extracts the limit rows applicable to a frequency band. Band in
    /// MHz (a reference-bandwidth sliver around the carrier is enough --
    /// rows are intersected with the regulatory band and only an empty
    /// intersection is dropped), bandwidth in kHz, operating height in
    /// km. Throws with a precise message when the database cannot be
    /// opened or the extraction fails.
    /// </summary>
    public static List<Limit> Read(string limitsDbPath,
        double freqMinMhz, double freqMaxMhz, double bandwidthKhz, double operatingHeightKm,
        LimitsDirectionType direction = LimitsDirectionType.EPFD_DN,
        LimitsExamType exam = LimitsExamType.EPFD_A22)
    {
        if (!File.Exists(limitsDbPath))
            throw new FileNotFoundException("limits database not found", limitsDbPath);

        var status = EPFDLimits.EPFD_Limits_OpenConnectionToDb(limitsDbPath);
        if (status != EPFDLimitsStatus.OK)
            throw new InvalidOperationException(
                $"cannot open the limits database ({status}): {limitsDbPath}");
        try
        {
            var all = new List<Limit>();
            double freqAssgnGhz = (freqMinMhz + freqMaxMhz) / 2000.0;
            foreach (var code in new[] { ServiceCode("FSS"), ServiceCode("BSS") })
            {
                if (!EPFDLimits.ExtractLimits(0, freqMinMhz, freqMaxMhz, direction, exam,
                        operatingHeightKm, freqAssgnGhz, bandwidthKhz, code, ref all))
                    throw new InvalidOperationException("cannot extract limits from the database");
            }
            return all;
        }
        finally
        {
            EPFDLimits.EPFD_Limits_CloseConnectionToDb();
        }
    }

    private static EpfdServiceCode ServiceCode(string text)
    {
        var code = new EpfdServiceCode();
        unsafe
        {
            for (int i = 0; i < text.Length && i < 5; i++)
                code.service[i] = (sbyte)text[i];
        }
        return code;
    }
}
