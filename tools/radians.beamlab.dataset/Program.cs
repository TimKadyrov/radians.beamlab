using System;
using radians.beamlab.dataset;

// CLI for the BL-* dataset generator; see DatasetGenerator for the content.
var o = new DatasetOptions { Log = Console.WriteLine };
for (int i = 0; i < args.Length; i++)
{
    string Next() => ++i < args.Length ? args[i]
        : throw new ArgumentException($"missing value after {args[i - 1]}");
    switch (args[i])
    {
        case "--out": o.OutDir = Next(); break;
        case "--donor-srs": o.DonorSrsPath = Next(); break;
        case "--donor-masks": o.DonorMasksPath = Next(); break;
        case "--dll-dir": o.EpfdMasksDllDir = Next(); break;
        case "--case": o.OnlyCase = Next(); break;
        case "--quick": o.Quick = true; break;
        default:
            Console.Error.WriteLine($"unknown option {args[i]}");
            Console.Error.WriteLine("usage: radians.beamlab.dataset [--out DIR] [--donor-srs MDB] " +
                "[--donor-masks MDB] [--dll-dir DIR] [--case BL-*] [--quick]");
            return 2;
    }
}

try
{
    DatasetGenerator.Generate(o);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAILED: " + ex.Message);
    return 1;
}
