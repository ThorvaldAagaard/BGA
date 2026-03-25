using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using static BGADLL.Macros;

namespace BGADLL
{
    public class DDS
    {
        // ===== Backend detection =====
        private static readonly bool _useHaglund;

        static DDS()
        {
            // Try bcalcdds first (existing behavior), fall back to Haglund's dds.dll
            try
            {
                bcalcDDS_delete(IntPtr.Zero); // harmless call to probe the library
                _useHaglund = false;
            }
            catch (DllNotFoundException)
            {
                _useHaglund = true;
            }
        }

        // ===== bcalcdds native imports =====
        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr bcalcDDS_new(IntPtr format, IntPtr hands, Int32 strain, Int32 leader);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr bcalcDDS_clone(IntPtr solver);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void bcalcDDS_delete(IntPtr solver);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int bcalcDDS_getTricksToTake(IntPtr solver);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int bcalcDDS_getTricksToTakeEx(IntPtr solver, Int32 tricks_target, IntPtr card);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void bcalcDDS_exec(IntPtr solver, IntPtr cmds);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr bcalcDDS_getCards(IntPtr solver, IntPtr result, Int32 player, Int32 suit);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr bcalcDDS_getLastError(IntPtr solver);

        // ===== Haglund dds.dll native imports =====
        [DllImport("dds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetMaxThreads(int userThreads);

        [DllImport("dds.dll")]
        private static extern int SolveBoardPBN(DealPbn dealPBN, int target, int solutions, int mode, ref FutureTricks futureTricks, int threadIndex);

        [StructLayout(LayoutKind.Sequential)]
        private struct DealPbn
        {
            public int trump;
            public int first;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public int[] currentTrickSuit;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public int[] currentTrickRank;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]
            public char[] remainCards;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FutureTricks
        {
            public int nodes;
            public int cards;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)]
            public int[] suit;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)]
            public int[] rank;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)]
            public int[] equals;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)]
            public int[] score;
        }

        private static FutureTricks NewFutureTricks()
        {
            var ft = new FutureTricks();
            ft.nodes = 0;
            ft.cards = 0;
            ft.suit = new int[13];
            ft.rank = new int[13];
            ft.equals = new int[13];
            ft.score = new int[13];
            return ft;
        }

        private static bool _haglundInitialized = false;
        private static readonly object _haglundInitLock = new object();

        // Thread-index pool for concurrent Haglund DDS calls (indices 0-7)
        private static readonly System.Collections.Concurrent.BlockingCollection<int> _threadIndexPool =
            new System.Collections.Concurrent.BlockingCollection<int>(new System.Collections.Concurrent.ConcurrentQueue<int>(Enumerable.Range(0, 8)));

        private static void EnsureHaglundInitialized()
        {
            if (_haglundInitialized) return;
            lock (_haglundInitLock)
            {
                if (_haglundInitialized) return;
                SetMaxThreads(8);
                _haglundInitialized = true;
            }
        }

        // ===== State =====
        // bcalcdds state
        private IntPtr solver = IntPtr.Zero;

        // Haglund state: we track the original PBN hands and any executed cards
        // so we can rebuild the position for each SolveBoardPBN call
        private string _handsNESW;     // Original hands in "N E S W" format (SHDC per hand)
        private Trump _trump;
        private Player _leader;
        private List<string> _executedCards; // Cards played via Execute(), format "RS" (e.g., "AH")

        // ===== Constructors =====
        public DDS(IntPtr solver) => this.solver = solver;

        public DDS(string hands, Trump trump, Player leader)
        {
            _trump = trump;
            _leader = leader;
            _handsNESW = hands;
            _executedCards = new List<string>();

            if (_useHaglund)
            {
                EnsureHaglundInitialized();
            }
            else
            {
                IntPtr deal = Marshal.StringToHGlobalAnsi(hands);
                IntPtr format = Marshal.StringToHGlobalAnsi("NESW");
                this.solver = bcalcDDS_new(format, deal, (Int32)trump, (Int32)leader);
            }
        }

        // Private constructor for Haglund clone
        private DDS(string hands, Trump trump, Player leader, List<string> executedCards)
        {
            _handsNESW = hands;
            _trump = trump;
            _leader = leader;
            _executedCards = new List<string>(executedCards);
        }

        // ===== Public API =====
        public IntPtr Clone()
        {
            if (!_useHaglund)
                return bcalcDDS_clone(this.solver);
            // For Haglund, Clone() is not used directly - use CloneDDS() instead
            return IntPtr.Zero;
        }

        /// <summary>
        /// Clone this DDS instance. Works for both bcalcdds and Haglund backends.
        /// Use this instead of new DDS(dds.Clone()).
        /// </summary>
        public DDS CloneDDS()
        {
            if (!_useHaglund)
                return new DDS(bcalcDDS_clone(this.solver));
            return new DDS(_handsNESW, _trump, _leader, _executedCards);
        }

        public void Delete()
        {
            if (!_useHaglund)
                bcalcDDS_delete(this.solver);
        }

        public void Execute(string commands)
        {
            if (!_useHaglund)
            {
                IntPtr cmds = Marshal.StringToHGlobalAnsi(commands);
                bcalcDDS_exec(this.solver, cmds);
                return;
            }

            // Parse commands: space-separated cards like "AH" or "AH x" or "AH KH x"
            // "x" means play cheapest available (we skip it - DDS handles optimal play)
            foreach (string token in commands.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "x" || token == "X")
                {
                    // "x" = play cheapest. We need to figure out what card that is.
                    // For now, we call SolveBoardPBN to find the optimal play.
                    // In the fusion strategy context, "x" follows a specific card lead.
                    // DDS will be called on the position after removing executed cards,
                    // so we solve to find what the next player plays.
                    string cheapest = FindCheapestCard();
                    if (cheapest != null)
                        _executedCards.Add(cheapest);
                }
                else
                {
                    _executedCards.Add(token);
                }
            }
        }

        public string LastError()
        {
            if (!_useHaglund) {
                IntPtr ptr = bcalcDDS_getLastError(this.solver);
                return Marshal.PtrToStringAnsi(ptr);
            }
            return "";
        }

        public int Tricks()
        {
            if (!_useHaglund)
                return bcalcDDS_getTricksToTake(this.solver);
            return SolveHaglund(null);
        }

        public int Tricks(string card)
        {
            if (!_useHaglund)
            {
                IntPtr move = Marshal.StringToHGlobalAnsi(card);
                return bcalcDDS_getTricksToTakeEx(this.solver, -1, move);
            }
            return SolveHaglund(card);
        }

        // ===== Haglund solver implementation =====

        /// <summary>
        /// Map BGADLL Trump enum to Haglund DDS trump encoding.
        /// BGADLL: Club=0, Diamond=1, Heart=2, Spade=3, No=4
        /// Haglund: Spade=0, Heart=1, Diamond=2, Club=3, NT=4
        /// </summary>
        private static int TrumpToDds(Trump trump)
        {
            switch (trump)
            {
                case Trump.Spade: return 0;
                case Trump.Heart: return 1;
                case Trump.Diamond: return 2;
                case Trump.Club: return 3;
                case Trump.No: return 4;
                default: return 4;
            }
        }

        /// <summary>
        /// Map BGADLL Player enum to DDS player index.
        /// Both use: North=0, East=1, South=2, West=3
        /// </summary>
        private static int PlayerToDds(Player player) => (int)player;

        /// <summary>
        /// Convert card string "RS" (rank+suit) to DDS suit index.
        /// Card format: first char = rank (A,K,Q,J,T,9..2), second char = suit (S,H,D,C)
        /// DDS suits: S=0, H=1, D=2, C=3
        /// </summary>
        private static int CardToSuit(string card)
        {
            if (card.Length < 2) return 0;
            switch (card[1])
            {
                case 'S': return 0;
                case 'H': return 1;
                case 'D': return 2;
                case 'C': return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// Convert card string "RS" to DDS rank value (2-14).
        /// </summary>
        private static int CardToRank(string card)
        {
            if (card.Length < 1) return 0;
            switch (card[0])
            {
                case '2': return 2; case '3': return 3; case '4': return 4; case '5': return 5;
                case '6': return 6; case '7': return 7; case '8': return 8; case '9': return 9;
                case 'T': return 10; case 'J': return 11; case 'Q': return 12; case 'K': return 13; case 'A': return 14;
                default: return 0;
            }
        }

        /// <summary>
        /// Convert rank value (2-14) to character.
        /// </summary>
        private static char RankToChar(int rank)
        {
            switch (rank)
            {
                case 2: return '2'; case 3: return '3'; case 4: return '4'; case 5: return '5';
                case 6: return '6'; case 7: return '7'; case 8: return '8'; case 9: return '9';
                case 10: return 'T'; case 11: return 'J'; case 12: return 'Q'; case 13: return 'K'; case 14: return 'A';
                default: return '?';
            }
        }

        /// <summary>
        /// Convert DDS suit index (0-3) to suit character.
        /// </summary>
        private static char SuitToChar(int suit)
        {
            switch (suit)
            {
                case 0: return 'S'; case 1: return 'H'; case 2: return 'D'; case 3: return 'C';
                default: return '?';
            }
        }

        /// <summary>
        /// Calculate who leads the current trick after replaying all executed cards.
        /// Returns DDS player index (0-3).
        /// </summary>
        private int CalculateCurrentLeader()
        {
            int currentLeader = PlayerToDds(_leader);
            int ddsTrump = TrumpToDds(_trump);
            int completeTricks = _executedCards.Count / 4;

            for (int trick = 0; trick < completeTricks; trick++)
            {
                int start = trick * 4;
                int leadSuit = CardToSuit(_executedCards[start]);
                int winnerOffset = 0, winnerPriority = -1, winnerRank = -1;

                for (int i = 0; i < 4; i++)
                {
                    string card = _executedCards[start + i];
                    int suit = CardToSuit(card);
                    int rank = CardToRank(card);
                    int priority = (suit == ddsTrump && ddsTrump < 4) ? 2 : (suit == leadSuit) ? 1 : 0;

                    if (priority > winnerPriority || (priority == winnerPriority && rank > winnerRank))
                    {
                        winnerPriority = priority;
                        winnerRank = rank;
                        winnerOffset = i;
                    }
                }
                currentLeader = (currentLeader + winnerOffset) % 4;
            }
            return currentLeader;
        }

        /// <summary>
        /// Remove executed cards from the original PBN hands and build a new PBN string.
        /// Input format: "N E S W" (NESW order, each hand is SHDC with dots)
        /// Output format: "N:N E S W" (PBN format for Haglund's DDS)
        /// </summary>
        private string BuildPbn(string extraCard = null)
        {
            // Build set of cards to remove
            var toRemove = new HashSet<(int suit, char rank)>();
            foreach (string card in _executedCards)
            {
                if (card.Length >= 2)
                    toRemove.Add((CardToSuit(card), card[0]));
            }
            if (extraCard != null && extraCard.Length >= 2)
                toRemove.Add((CardToSuit(extraCard), extraCard[0]));

            // Parse original hands (NESW order, each is "SHDC" with dots)
            string[] handParts = _handsNESW.Split(' ');
            if (handParts.Length != 4)
                return "N:" + _handsNESW;

            // PBN suit order in hand string: S.H.D.C = DDS suits 0.1.2.3
            var sb = new StringBuilder("N:");
            for (int h = 0; h < 4; h++)
            {
                if (h > 0) sb.Append(' ');
                string[] suits = handParts[h].Split('.');
                for (int s = 0; s < 4 && s < suits.Length; s++)
                {
                    if (s > 0) sb.Append('.');
                    foreach (char c in suits[s])
                    {
                        if (!toRemove.Contains((s, c)))
                            sb.Append(c);
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Find the cheapest card the current player can play.
        /// Used for "x" command in Execute.
        /// </summary>
        private string FindCheapestCard()
        {
            int threadIndex = _threadIndexPool.Take();
            try
            {
                var deal = BuildDealPbn(null);
                var ft = NewFutureTricks();
                int result = SolveBoardPBN(deal, -1, 1, 1, ref ft, threadIndex);
                if (result == 1 && ft.cards > 0)
                {
                    // Find the cheapest card among legal moves
                    int minRank = int.MaxValue;
                    int minSuit = 0;
                    for (int i = 0; i < ft.cards; i++)
                    {
                        if (ft.rank[i] < minRank)
                        {
                            minRank = ft.rank[i];
                            minSuit = ft.suit[i];
                        }
                    }
                    return $"{RankToChar(minRank)}{SuitToChar(minSuit)}";
                }
            }
            finally
            {
                _threadIndexPool.Add(threadIndex);
            }
            return null;
        }

        /// <summary>
        /// Build a DealPbn struct from current state.
        /// </summary>
        private DealPbn BuildDealPbn(string extraCard)
        {
            var deal = new DealPbn();
            deal.trump = TrumpToDds(_trump);
            deal.currentTrickSuit = new int[3];
            deal.currentTrickRank = new int[3];

            // All played cards including the extra card for Tricks(card)
            var allPlayed = new List<string>(_executedCards);
            if (extraCard != null)
                allPlayed.Add(extraCard);

            // Calculate current trick leader and set current trick cards
            int currentLeaderBeforeExtra = CalculateCurrentLeader();
            // If we added the extra card, recalculate with it included
            // But we need the leader BEFORE the extra card for the current trick
            deal.first = currentLeaderBeforeExtra;

            // Current trick = cards after last complete trick
            int currentTrickSize = allPlayed.Count % 4;
            int startIndex = allPlayed.Count - currentTrickSize;

            for (int i = 0; i < currentTrickSize && i < 3; i++)
            {
                string card = allPlayed[startIndex + i];
                deal.currentTrickSuit[i] = CardToSuit(card);
                deal.currentTrickRank[i] = CardToRank(card);
            }

            // Remove all played cards from PBN
            string pbn = BuildPbn(extraCard);
            var chars = new char[80];
            for (int i = 0; i < Math.Min(pbn.Length, 80); i++)
                chars[i] = pbn[i];
            deal.remainCards = chars;

            return deal;
        }

        /// <summary>
        /// Solve using Haglund's SolveBoardPBN.
        /// If card is specified, evaluates that specific card lead.
        /// Returns tricks for the leader's side.
        /// </summary>
        private int SolveHaglund(string card)
        {
            int threadIndex = _threadIndexPool.Take();
            try
            {
                var deal = BuildDealPbn(card);
                var ft = NewFutureTricks();
                int result = SolveBoardPBN(deal, -1, 1, 1, ref ft, threadIndex);

                if (result != 1 || ft.cards == 0)
                    return 0;

                return ft.score[0];
            }
            catch (Exception)
            {
                return 0;
            }
            finally
            {
                _threadIndexPool.Add(threadIndex);
            }
        }
    }
}
