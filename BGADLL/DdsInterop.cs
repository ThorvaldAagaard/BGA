// .NET P/Invoke binding for the DDS (Double Dummy Solver) 3.0.0 C library.
//
// This is the C-ABI binding: it calls the native dds.dll / libdds.so / libdds.dylib
// produced by the //:dds_shared Bazel target. The C API is unchanged from DDS 2.x,
// so this binding is also a drop-in for code that previously used a 2.x dds.dll.
//
// The native library is resolved by base name "dds": dds.dll on Windows,
// libdds.so on Linux, libdds.dylib on macOS. Place it next to your executable
// (or anywhere on the OS library search path).
//
// Python users should use the dds3 extension instead (see docs/python_interface.md).

// Nullable-agnostic so the file drops cleanly into any consuming project.
#nullable disable

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Dds
{
    /// <summary>DDS array-size constants (from library/src/api/dll.h).</summary>
    public static class DdsConstants
    {
        public const int Hands = 4;
        public const int Suits = 4;
        public const int Strains = 5;
        public const int MaxNoOfBoards = 200;
        public const int MaxNoOfTables = 40;
        public const int ReturnNoFault = 1;
    }

    // ===================== Structs (layout matches dll.h) =====================

    /// <summary>Result of solving one board: candidate cards and their scores.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FutureTricks
    {
        public int Nodes;
        public int Cards;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)] public int[] Suit;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)] public int[] Rank;
        // Bitmask of cards equivalent to Suit[i]/Rank[i] (C struct field "equals").
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)] public int[] EqualCards;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)] public int[] Score;
    }

    /// <summary>A deal in PBN (Portable Bridge Notation) format.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DealPbn
    {
        public int Trump;  // 0=S, 1=H, 2=D, 3=C, 4=NT
        public int First;  // 0=N, 1=E, 2=S, 3=W
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public int[] CurrentTrickSuit;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public int[] CurrentTrickRank;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string RemainCards;

        public static DealPbn Create(string remainCards, int trump = 4, int first = 0)
        {
            return new DealPbn
            {
                Trump = trump,
                First = first,
                CurrentTrickSuit = new int[3],
                CurrentTrickRank = new int[3],
                RemainCards = remainCards,
            };
        }
    }

    /// <summary>Multiple PBN deals for batch solving (SolveAllBoards).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BoardsPbn
    {
        public int NoOfBoards;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.MaxNoOfBoards)] public DealPbn[] Deals;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.MaxNoOfBoards)] public int[] Target;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.MaxNoOfBoards)] public int[] Solutions;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.MaxNoOfBoards)] public int[] Mode;
    }

    /// <summary>Results for a batch of solved boards.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SolvedBoards
    {
        public int NoOfBoards;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.MaxNoOfBoards)] public FutureTricks[] SolvedBoard;
    }

    /// <summary>A deal for DD-table calculation in PBN format.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DdTableDealPbn
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Cards;
    }

    /// <summary>Multiple PBN DD-table deals (CalcAllTablesPBN).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DdTableDealsPbn
    {
        public int NoOfTables;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.MaxNoOfTables * DdsConstants.Strains)]
        public DdTableDealPbn[] Deals;
    }

    /// <summary>A double-dummy table: tricks per strain (0-4) and declarer hand (0-3).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DdTableResults
    {
        // Flattened 5x4: index [strain * 4 + hand].
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.Strains * DdsConstants.Hands)]
        public int[] ResTable;

        public int Tricks(int strain, int hand) => ResTable[strain * DdsConstants.Hands + hand];
    }

    /// <summary>Results for a batch of DD tables.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DdTablesRes
    {
        public int NoOfBoards;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.MaxNoOfTables * DdsConstants.Strains)]
        public DdTableResults[] Results;
    }

    /// <summary>Par result for one deal (NS view = index 0, EW view = index 1).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ParResults
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2 * 16)] public byte[] ParScoreRaw;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2 * 128)] public byte[] ParContractsRaw;

        /// <summary>Par score string, e.g. "NS 420". side: 0 = NS view, 1 = EW view.</summary>
        public string ParScore(int side) => DdsText.Ansi(ParScoreRaw, side * 16, 16);

        /// <summary>Par contracts string. side: 0 = NS view, 1 = EW view.</summary>
        public string ParContracts(int side) => DdsText.Ansi(ParContractsRaw, side * 128, 128);
    }

    /// <summary>Par results for a batch of DD tables.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AllParResults
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.MaxNoOfTables)] public ParResults[] Results;
    }

    /// <summary>Par result from a dealer's perspective (DealerPar).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ParResultsDealer
    {
        public int Number;  // number of contracts giving the par score
        public int Score;   // signed from the NS view
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10 * 10)] public byte[] ContractsRaw;

        /// <summary>Par contract string i (0 .. Number-1).</summary>
        public string Contract(int i) => DdsText.Ansi(ContractsRaw, i * 10, 10);
    }

    /// <summary>A trace of cards played, in PBN format (2 characters per card).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct PlayTracePbn
    {
        public int Number;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 106)] public string Cards;

        public static PlayTracePbn Create(string cards)
        {
            return new PlayTracePbn { Number = (cards?.Length ?? 0) / 2, Cards = cards ?? string.Empty };
        }
    }

    /// <summary>Multiple play traces (AnalyseAllPlaysPBN).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlayTracesPbn
    {
        public int NoOfBoards;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.MaxNoOfBoards)] public PlayTracePbn[] Plays;
    }

    /// <summary>Result of analysing a played hand: DD trick count after each card.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SolvedPlay
    {
        public int Number;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 53)] public int[] Tricks;
    }

    /// <summary>Results for a batch of analysed plays.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SolvedPlays
    {
        public int NoOfBoards;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DdsConstants.MaxNoOfBoards)] public SolvedPlay[] Solved;
    }

    /// <summary>Build, platform and threading information about the loaded DDS library.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DdsInfo
    {
        public int Major;
        public int Minor;
        public int Patch;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)] public string VersionString;
        public int System;
        public int NumBits;
        public int Compiler;
        public int Constructor;
        public int NumCores;
        public int Threading;
        public int NoOfThreads;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ThreadSizes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string SystemString;
    }

    // ===================== Native P/Invoke entry points =====================

    /// <summary>
    /// Raw P/Invoke declarations for the DDS C API. Functions return 1
    /// (<see cref="DdsConstants.ReturnNoFault"/>) on success, or a negative
    /// error code — pass it to <see cref="DdsApi.ErrorMessage"/> for the text.
    /// </summary>
    public static class Native
    {
        private const string Lib = "dds";

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern void SetMaxThreads(int userThreads);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern void FreeMemory();

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern int SolveBoardPBN(
            DealPbn dl, int target, int solutions, int mode,
            ref FutureTricks futp, int thrId);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern int SolveAllBoards(ref BoardsPbn bop, ref SolvedBoards solvedp);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern int CalcDDtablePBN(DdTableDealPbn tableDealPBN, ref DdTableResults tablep);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern int CalcAllTablesPBN(
            ref DdTableDealsPbn dealsp, int mode, int[] trumpFilter,
            ref DdTablesRes resp, ref AllParResults presp);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern int Par(ref DdTableResults tablep, ref ParResults presp, int vulnerable);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern int CalcParPBN(
            DdTableDealPbn tableDealPBN, ref DdTableResults tablep,
            int vulnerable, ref ParResults presp);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern int DealerPar(
            ref DdTableResults tablep, ref ParResultsDealer presp, int dealer, int vulnerable);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern int AnalysePlayPBN(
            DealPbn dlPBN, PlayTracePbn playPBN, ref SolvedPlay solvedp, int thrId);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern int AnalyseAllPlaysPBN(
            ref BoardsPbn bopPBN, ref PlayTracesPbn plpPBN, ref SolvedPlays solvedp, int chunkSize);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern void GetDDSInfo(ref DdsInfo info);

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        public static extern void ErrorMessage(int code, byte[] line);
    }

    // ===================== Convenience helpers =====================

    /// <summary>Helpers for the fixed ASCII buffers used in DDS structs.</summary>
    public static class DdsText
    {
        /// <summary>Read a NUL-terminated ASCII string from a fixed buffer slice.</summary>
        public static string Ansi(byte[] buffer, int offset, int maxLength)
        {
            if (buffer == null) return string.Empty;
            int end = offset;
            int limit = Math.Min(offset + maxLength, buffer.Length);
            while (end < limit && buffer[end] != 0) end++;
            return Encoding.ASCII.GetString(buffer, offset, end - offset);
        }
    }

    /// <summary>Top-level convenience API over <see cref="Native"/>.</summary>
    public static class DdsApi
    {
        /// <summary>Translate a DDS return code into its human-readable message.</summary>
        public static string ErrorMessage(int code)
        {
            var buffer = new byte[80];
            Native.ErrorMessage(code, buffer);
            return DdsText.Ansi(buffer, 0, buffer.Length);
        }

        /// <summary>Throw if <paramref name="code"/> is not <see cref="DdsConstants.ReturnNoFault"/>.</summary>
        public static void ThrowOnError(int code)
        {
            if (code != DdsConstants.ReturnNoFault)
                throw new DdsException(code, ErrorMessage(code));
        }

        /// <summary>Version/build/threading info for the loaded native library.</summary>
        public static DdsInfo GetInfo()
        {
            var info = new DdsInfo();
            Native.GetDDSInfo(ref info);
            return info;
        }
    }

    /// <summary>Exception carrying a DDS native return code.</summary>
    public sealed class DdsException : Exception
    {
        public int Code { get; }

        public DdsException(int code, string message)
            : base($"DDS error {code}: {message}")
        {
            Code = code;
        }
    }
}
