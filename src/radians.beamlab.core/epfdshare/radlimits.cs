using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace radlimits
{
    public class LimitPoint : IEquatable<LimitPoint>
    {
        public double EPFD;
        public double Perc;
        public double LatMin;
        public double LatMax;
        public double AdjFact;
        public override bool Equals(object obj)
        {
            return Equals(obj as LimitPoint);
        }

        public bool Equals(LimitPoint other)
        {
            return other != null &&
                   EPFD == other.EPFD &&
                   Perc == other.Perc &&
                   LatMin == other.LatMin &&
                   LatMax == other.LatMax &&
                   AdjFact == other.AdjFact;
        }

        public override int GetHashCode()
        {
            int hashCode = -645204633;
            hashCode = hashCode * -1521134295 + EPFD.GetHashCode();
            hashCode = hashCode * -1521134295 + Perc.GetHashCode();
            hashCode = hashCode * -1521134295 + LatMin.GetHashCode();
            hashCode = hashCode * -1521134295 + LatMax.GetHashCode();
            hashCode = hashCode * -1521134295 + AdjFact.GetHashCode();
            return hashCode;
        }

        public static bool operator ==(LimitPoint left, LimitPoint right)
        {
            return EqualityComparer<LimitPoint>.Default.Equals(left, right);
        }

        public static bool operator !=(LimitPoint left, LimitPoint right)
        {
            return !(left == right);
        }
    }
    public class Limit : IEquatable<Limit>
    {
        public List<string> regions;

        public LimitsExamType Exam;
        public int ScenOrGroupID { get; set; }
        public string Service { get;  set; }
        public double Freq_min { get;  set; }
        public double Freq_max { get;  set; }
        public string Pattern_rr { get;  set; }
        public double? Rf_diam { get; internal set; }
        public double RefBW { get; internal set; }
        public string RrRef { get; internal set; }
        public uint Mask_id { get; internal set; }
        public string Pattern { get; internal set; }
        public bool ShortTermLatDependent { get; internal set; }
        public List<LimitPoint> Points { get; internal set; }
        public LimitsDirectionType Direction { get; internal set; }
        public double RegFreq_min { get; internal set; }
        public double RegFreq_max { get; internal set; }
        public double? RrBeamWidth { get; set; }

        public Limit()
        {
            regions = new List<string>();
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Limit);
        }

        public bool Equals(Limit other)
        {
            return other != null &&
                   new HashSet<string>(regions).SetEquals(other.regions) &&
                   Exam == other.Exam &&
                   ScenOrGroupID == other.ScenOrGroupID &&
                   Service == other.Service &&
                   Freq_min == other.Freq_min &&
                   Freq_max == other.Freq_max &&
                   RegFreq_min == other.RegFreq_min &&
                   RegFreq_max == other.RegFreq_max &&
                   Pattern_rr == other.Pattern_rr &&
                   Rf_diam == other.Rf_diam &&
                   RefBW == other.RefBW &&
                   RrRef == other.RrRef &&
                   Direction == other.Direction &&
                   Mask_id == other.Mask_id &&
                   Pattern == other.Pattern &&
                   ShortTermLatDependent == other.ShortTermLatDependent &&
                   Points.SequenceEqual(other.Points);
        }

        public bool EqualsExceptRegion(Limit other)
        {
            return other != null &&
                   Exam == other.Exam &&
                   ScenOrGroupID == other.ScenOrGroupID &&
                   Service == other.Service &&
                   Freq_min == other.Freq_min &&
                   Freq_max == other.Freq_max &&
                   Freq_min == other.Freq_min &&
                   Freq_max == other.Freq_max &&
                   Pattern_rr == other.Pattern_rr &&
                   Rf_diam == other.Rf_diam &&
                   RefBW == other.RefBW &&
                   RrRef == other.RrRef &&
                   Direction == other.Direction &&
                   Mask_id == other.Mask_id &&
                   Pattern == other.Pattern &&
                   ShortTermLatDependent == other.ShortTermLatDependent &&
                   Points.SequenceEqual(other.Points);
        }

        public bool EqualsExceptGrpFreq(Limit other)
        {
            return other != null &&
                   new HashSet<string>(regions).SetEquals(other.regions) &&
                   Exam == other.Exam &&
                   Service == other.Service &&
                   Pattern_rr == other.Pattern_rr &&
                   Rf_diam == other.Rf_diam &&
                   RefBW == other.RefBW &&
                   RrRef == other.RrRef &&
                   Direction == other.Direction &&
                   Mask_id == other.Mask_id &&
                   Pattern == other.Pattern &&
                   ShortTermLatDependent == other.ShortTermLatDependent &&
                   Points.SequenceEqual(other.Points);
        }

        public override int GetHashCode()
        {
            int hashCode = 1073681020;
            hashCode = hashCode * -1521134295 + EqualityComparer<List<string>>.Default.GetHashCode(regions);
            hashCode = hashCode * -1521134295 + Exam.GetHashCode();
            hashCode = hashCode * -1521134295 + ScenOrGroupID.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Service);
            hashCode = hashCode * -1521134295 + Freq_min.GetHashCode();
            hashCode = hashCode * -1521134295 + Freq_max.GetHashCode();
            hashCode = hashCode * -1521134295 + RegFreq_min.GetHashCode();
            hashCode = hashCode * -1521134295 + RegFreq_max.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Pattern_rr);
            hashCode = hashCode * -1521134295 + Rf_diam.GetHashCode();
            hashCode = hashCode * -1521134295 + RefBW.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(RrRef);
            hashCode = hashCode * -1521134295 + Direction.GetHashCode();
            hashCode = hashCode * -1521134295 + Mask_id.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Pattern);
            hashCode = hashCode * -1521134295 + ShortTermLatDependent.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<List<LimitPoint>>.Default.GetHashCode(Points);
            return hashCode;
        }

        public static bool operator ==(Limit left, Limit right)
        {
            return EqualityComparer<Limit>.Default.Equals(left, right);
        }

        public static bool operator !=(Limit left, Limit right)
        {
            return !(left == right);
        }
    }
    public enum EPFDLimitsStatus
    {
        EPFD_EC_STNCLS = -4,
        EPFD_EC_XML = -3,
        EPFD_EC_MASK_XML = -2,
        EPFD_EC_UNKNOWN = -1,
        OK = 0,
        EPFD_WC_UNKNOWN = 1,
    }

    public enum LimitsDirectionType
    {
        EPFD_DN = 1,
        EPFD_UP = 2,
        EPFD_IS = 3,
    }

    public enum LimitsExamType
    {
        EPFD_A22 = 1,
        EPFD_97A = 2,
        EPFD_97B = 3,
    }

    public struct EpfdStnCls
    {
        public unsafe fixed sbyte stn_cls[3];
    }
    public struct EpfdServiceCode
    {
        public unsafe fixed sbyte service[6];
    }

    public struct StEpfdLimits
    {
        public double epfd;
        public double time_percent;
        public double adjust_fact;
        public double rf_beam_wdth;
        public double rf_diam;
        public double es_long_min;
        public double es_long_max;
        public double es_lat_min;
        public double es_lat_max;
        public double es_min_gain;
        public double es_min_elev;
        public double gso_bss_long;
        public double gso_max_inclin;
        public double ngso_min_alt;
        public double ngso_max_alt;
        public double es_G2T_min;
        public double es_emi_bdwidth_min;
    }

    public struct StEpfdLimitsArray
    {
        public uint array_size;
        public unsafe StEpfdLimits* epfdLimits;
    }

    public struct StEpfdExamsInfo
    {
        public unsafe fixed sbyte ref_rr[51];
        public unsafe fixed sbyte rule_type[51];
        public uint valid_from;
        public uint valid_to;
        public uint mask_id;
        public uint mask_argmt;
        public double freq_min;
        public double freq_max;
        public unsafe fixed sbyte rr_region[4];
        public unsafe fixed sbyte rr_service[6];
        public unsafe fixed sbyte link_direction[5];
        public double rf_diam;
        public double rf_band_wdth;
        public unsafe fixed sbyte rf_pattern[13];
        public unsafe fixed sbyte rf_pattern_rr[51];
    }

    public struct StEpfdExams
    {
        public StEpfdExamsInfo examsInfo;
        public StEpfdLimitsArray limits;
    }

    public static class EPFDLimits
    {
#if WIN64
        private const string EPFD_LIMITS_DLL_NAME = "EpfdLimitsApi64.dll";
#else
            private const string EPFD_LIMITS_DLL_NAME = "EpfdLimitsApi.dll";
#endif

        private const int EPFD_ITURGN_LEN = 4;
        private const int EPFD_SERVICE_LEN = 6;
        private const int EPFD_STNCLS_LEN = 3;
        private const int EPFD_LINKDIR_LEN = 5;
        private const int EPFD_REFRR_LEN = 51;
        private const int EPFD_METHOD_LEN = 51;
        private const int EPFD_ANT_PATTERN_CODE_LEN = 13;
        private const int EPFD_ANT_PATTERN_TEXT_LEN = 51;


        [DllImport(EPFD_LIMITS_DLL_NAME)]
        public static extern EPFDLimitsStatus EPFD_Limits_OpenConnectionToDb(string LimitsDbPath);

        [DllImport(EPFD_LIMITS_DLL_NAME)]
        public static extern EPFDLimitsStatus EPFD_Limits_CloseConnectionToDb();

        [DllImport(EPFD_LIMITS_DLL_NAME)]
        public static extern unsafe EPFDLimitsStatus EPFD_Limits_Extract(LimitsDirectionType DirectionType, EpfdServiceCode ServiceCode, double FreqAssgn, double FreqBandwdth, uint DateOfReceipt, StEpfdExams** outExamsArray,
          int* outExamsCount);

#if WIN64
        [DllImport(EPFD_LIMITS_DLL_NAME)]
        public static extern unsafe EPFDLimitsStatus EPFD_Limits_Extract_By_Examination(
            LimitsExamType ExaminationCode, LimitsDirectionType LinkDirectType,
            EpfdServiceCode ServCode, double FreqAssgn, double FreqBandwdth,
            double OperatingHeight, uint DateOfReceipt, StEpfdExams** outArray, int* outCount);
#else
        [DllImport(EPFD_LIMITS_DLL_NAME, EntryPoint = "_EPFD_Limits_Extract_By_Examination@52")]
        private static extern unsafe EPFDLimitsStatus EPFD_Limits_Extract_By_Examination(
            LimitsExamType ExaminationCode, LimitsDirectionType LinkDirectType,
            EpfdServiceCode ServCode, double FreqAssgn,   double FreqBandwdth, 
            double OperatingHeight, uint DateOfReceipt, StEpfdExams ** outArray, int* outCount);
#endif

        [DllImport(EPFD_LIMITS_DLL_NAME)]
        public static extern unsafe void EPFD_Limits_EmptyStEpfdExams(StEpfdExams* inExamsArray, int inExamsCount);

        private static unsafe string GetString(sbyte* ptr)
        {
            string str = "";
            int index = 0;
            while (ptr[index] != 0) str += ((char)ptr[index++]).ToString();
            return str;
        }
        public static unsafe bool ExtractLimits(int grpId, double fMin, double fMax, LimitsDirectionType link, LimitsExamType exam, double OperationHeight, double freqAssgn, double freqBdwdth, EpfdServiceCode protService, ref List<Limit> all)
        {
            EPFDLimitsStatus status = EPFDLimitsStatus.OK;
            StEpfdExams* limitsArray;
            int limitsCount = 0;
            status = EPFD_Limits_Extract_By_Examination(exam, link, protService, freqAssgn, freqBdwdth, OperationHeight, 20051231U, &limitsArray, &limitsCount);
            StEpfdExamsInfo examsInfo;
            if (status != EPFDLimitsStatus.OK)
                return (status != EPFDLimitsStatus.EPFD_EC_UNKNOWN);

            for (int i = 0; i < limitsCount; ++i)
            {
                examsInfo = limitsArray[i].examsInfo;
                var region = GetString(examsInfo.rr_region);
                var l = new Limit()
                {
                    Exam = exam,
                    ScenOrGroupID=grpId,
                    Direction = link,
                    Service = GetString(examsInfo.rr_service),
                    Freq_min = Math.Max(examsInfo.freq_min * 1000.0, fMin),
                    Freq_max = Math.Min(examsInfo.freq_max * 1000.0, fMax),
                    RegFreq_min = examsInfo.freq_min * 1000.0,
                    RegFreq_max = examsInfo.freq_max * 1000.0,
                    Pattern = GetString(examsInfo.rf_pattern),
                    Pattern_rr = GetString(examsInfo.rf_pattern_rr),
                    Rf_diam = examsInfo.rf_diam,
                    Mask_id = examsInfo.mask_id,
                    RefBW = examsInfo.rf_band_wdth,
                    RrBeamWidth = limitsArray[0].limits.epfdLimits->rf_beam_wdth,
                    RrRef = GetString(examsInfo.ref_rr)
                };

                // Null out inapplicable field - raw value from C lib is uninitialized
                if (link == LimitsDirectionType.EPFD_DN)
                    l.RrBeamWidth = null;
                else
                    l.Rf_diam = null;

                if (l.Freq_min == l.Freq_max)
                    continue;

                l.ShortTermLatDependent =
                        exam == LimitsExamType.EPFD_A22 && link == LimitsDirectionType.EPFD_DN &&
                        (l.RrRef == "Article 22, RR 22.5C4" || l.RrRef == "Article 22, RR 22.5C8");

                l.Points = new List<LimitPoint>();
                if (l.ShortTermLatDependent)
                    for (var ind = 0; ind < (int)limitsArray[i].limits.array_size; ++ind)
                    {
                        var epfdLimit = limitsArray[i].limits.epfdLimits[ind];
                        var perc = 100.0 - epfdLimit.time_percent;
                        if (epfdLimit.time_percent == -99999.0) perc = 0.0;
                        l.Points.Add(new LimitPoint { Perc = perc, EPFD = epfdLimit.epfd, LatMin=epfdLimit.es_lat_min, LatMax=epfdLimit.es_lat_max, AdjFact=epfdLimit.adjust_fact });
                    }
                else
                    for (var ind = 0; ind < (int)limitsArray[i].limits.array_size; ++ind)
                    {
                        var epfdLimit = limitsArray[i].limits.epfdLimits[ind];
                        var perc = 100.0 - epfdLimit.time_percent;
                        if (epfdLimit.time_percent == -99999.0) perc = 0.0;
                        l.Points.Add(new LimitPoint { Perc = perc, EPFD = epfdLimit.epfd });
                    }

                //Combine limits by region
                bool found = false;
                foreach (var lim in all)
                {
                    if (lim.EqualsExceptRegion(l) && lim.ScenOrGroupID==l.ScenOrGroupID)
                    {
                        lim.regions.Add(region);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    l.regions = new List<string>() { region };
                    all.Add(l);
                }
            }

            return true;
        }
    }
}
