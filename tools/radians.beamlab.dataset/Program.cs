using System;
using radians.beamlab.dataset;

// CLI for the BL-* dataset generator; see DatasetGenerator for the content.
// With --package it builds one cross-read package instead (PackageBuilder).
var inv = System.Globalization.CultureInfo.InvariantCulture;
var o = new DatasetOptions { Log = Console.WriteLine };
PackageOptions pkg = null;
for (int i = 0; i < args.Length; i++)
{
    string Next() => ++i < args.Length ? args[i]
        : throw new ArgumentException($"missing value after {args[i - 1]}");
    PackageOptions Pkg() => pkg ??= new PackageOptions();
    switch (args[i])
    {
        case "--out": o.OutDir = Next(); break;
        case "--donor-srs": o.DonorSrsPath = Next(); break;
        case "--donor-masks": o.DonorMasksPath = Next(); break;
        case "--dll-dir": o.EpfdMasksDllDir = Next(); break;
        case "--limits-db": o.LimitsDbPath = Next(); break;
        case "--case": o.OnlyCase = Next(); break;
        case "--quick": o.Quick = true; break;
        case "--package": Pkg().Name = Next(); break;
        case "--design": Pkg().DesignPath = Next(); break;
        case "--rset": Pkg().RsetJsonPath = Next(); break;
        case "--mask": Pkg().MaskXmlPath = Next(); break;
        case "--mask-id": Pkg().MaskId = int.Parse(Next(), inv); break;
        case "--band": Pkg().BandMinMhz = double.Parse(Next(), inv); Pkg().BandMaxMhz = double.Parse(Next(), inv); break;
        case "--ntc": Pkg().NtcId = int.Parse(Next(), inv); break;
        case "--sat-name": Pkg().SatName = Next(); break;
        case "--expected": Pkg().ExpectedPath = Next(); break;
        case "--provenance": Pkg().Provenance = Next(); break;
        default:
            Console.Error.WriteLine($"unknown option {args[i]}");
            Console.Error.WriteLine("usage: radians.beamlab.dataset [--out DIR] [--donor-srs MDB] " +
                "[--donor-masks MDB] [--dll-dir DIR] [--limits-db MDB] [--case BL-*] [--quick]");
            Console.Error.WriteLine("       radians.beamlab.dataset --package NAME --design JSON --rset JSON --mask XML " +
                "[--mask-id N] [--band MIN MAX] [--ntc N] [--sat-name S] [--expected FILE] [--provenance TEXT] [--out DIR] ...");
            return 2;
    }
}

try
{
    if (pkg is not null) PackageBuilder.Build(pkg, o);
    else DatasetGenerator.Generate(o);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAILED: " + ex.Message);
    return 1;
}
