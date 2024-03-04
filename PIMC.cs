using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BGA
{
    using static BGA.Macros;
    using Comparer = CardComparer;
    using Hand = List<Card>;
    using Output = Dictionary<string, ConcurrentBag<byte>>;
    using Queue = ConcurrentQueue<int>;

    internal class PIMC
    {
        private string commands = "";
        private bool evaluate = false;
        private Player leader = 0;
        private readonly int threads;
        private readonly List<byte[]> combinations = new List<byte[]>();
        private int K = 0, N = 0, playouts = 0, free;
        private IEnumerable<string> legalMoves = null;
        private readonly HashSet<string> check = null;
        private readonly Comparer comparer = new Comparer();
        private readonly Output output = new Output();
        private readonly Queue queue = new Queue();
        private readonly Utils utils = new Utils();
        private readonly Random random = null;

        // player hands
        private Hand played = null;
        private Details eastDetails = null;
        private Details westDetails = null;
        private readonly Hand northHand = new Hand();
        private readonly Hand southHand = new Hand();
        private readonly Hand eastPlayed = new Hand();
        private readonly Hand westPlayed = new Hand();
        private readonly Hand opposCards = new Hand();

        // getters
        internal int Playouts => this.playouts;
        internal bool Evaluating => this.evaluate || this.threads != this.free;
        internal IEnumerable<string> LegalMoves => this.legalMoves;
        internal Output Output => this.output;

        internal PIMC()
        {
            int count = Environment.ProcessorCount;
            int seed = Guid.NewGuid().GetHashCode();
            this.check = new HashSet<string>();
            this.random = new Random(seed);
            this.threads = Math.Max(1, count - 2);
            this.free = this.threads;
        }

        private void Clear(List<byte[]> list)
        {
            int id = GC.GetGeneration(list);
            list.Clear();
            GC.Collect(id, GCCollectionMode.Forced);
        }

        internal void Clear()
        {
            this.commands = "";
            this.check.Clear();
            this.output.Clear();
            this.northHand.Clear();
            this.southHand.Clear();
            this.eastPlayed.Clear();
            this.westPlayed.Clear();
            this.opposCards.Clear();
            this.Clear(this.combinations);
            while (!this.queue.IsEmpty)
                this.queue.TryDequeue(out _);
        }

        internal void LoadCombinations()
        {
            int n = this.N, k = this.K;
            int sum = this.utils.Count(n, k);
            int[] array = new int[sum];
            for (int i = 0; i < sum; i++) array[i] = i;
            this.utils.Shuffle(array, sum, this.random);
            foreach (int i in array) this.queue.Enqueue(i);
            foreach (byte[] series in this.utils.Generate(n, k))
                this.combinations.Add(series.ToArray());
        }

        internal void SetupEvaluation(Hand[] our, Hand oppos, Hand played,
            IEnumerable<string> legal, Details[] details, Player leader)
        {
            this.legalMoves = legal;
            this.played = played;
            this.commands = string.Join(" ",
                played.Select(c => c.ToString()));
            this.eastDetails = details[0];
            this.westDetails = details[1];
            this.northHand.AddRange(our[0]);
            this.southHand.AddRange(our[1]);
            this.opposCards.AddRange(oppos);
            Player player = (Player)((int)leader);

            foreach (string card in legal)
            {
                this.output.Add(card, new ConcurrentBag<byte>());
            }

            var leads = Enumerable.Reverse(played);
            foreach (Card lead in leads)
            {
                player = player.Prev();
                switch (player)
                {
                    case Player.North: this.northHand.Add(lead); break;
                    case Player.South: this.southHand.Add(lead); break;
                    case Player.East: this.eastPlayed.Add(lead); break;
                    case Player.West: this.westPlayed.Add(lead); break;
                    default: break;
                }
            }

            Hand a = our[0], b = our[1];
            int left = Math.Max(a.Count, b.Count);
            this.K = left - this.westPlayed.Count;
            this.N = oppos.Count;
            this.playouts = 0;
            this.leader = player;
            this.LoadCombinations();
        }

        internal void BeginEvaluate(Trump trump)
        {
            if (this.evaluate) return;
            this.evaluate = true;
            this.free = this.threads;
            string N = this.northHand.Parse();
            string S = this.southHand.Parse();
            for (int t = 0; t < this.threads; t++)
            {
                new Thread(start: () =>
                {
                    Interlocked.Decrement(ref this.free);
                    while (this.evaluate && !this.queue.IsEmpty)
                    {
                        Interlocked.Increment(ref this.playouts);

                        // repeat if failed to dequeue item
                        if (!this.queue.TryDequeue(out int pos))
                        {
                            Thread.Sleep(10); continue;
                        }

                        // recover hands before leads
                        var set = this.combinations[pos];
                        IEnumerable<Card> westHand = set.Select(
                            index => this.opposCards[index - 1]).ToList();
                        westHand = westHand.Concat(this.westPlayed);
                        var eastHand = this.opposCards.Except(westHand,
                            this.comparer).Concat(this.eastPlayed);

                        // exclude impossible hands
                        if (this.Ignore(eastHand, this.eastDetails) ||
                            this.Ignore(westHand, this.westDetails)) continue;

                        // DDS analysis
                        string E = eastHand.Parse(), W = westHand.Parse();
                        string format = N + " " + E + " " + S + " " + W;
                        DDS dds = new DDS(format, trump, this.leader);
                        if (this.commands != "") dds.Execute(this.commands);
                        Player opposite = this.leader.Next().Next();
                        foreach (string card in this.legalMoves)
                        {
                            int tricks = dds.Tricks(card), result = -1;
                            this.output[card].Add((byte)tricks);
                            Suit suit = (Suit)"CDHS".IndexOf(card[1]);
                            if (this.N > 2 && this.played.Count == 0 &&
                                eastHand.Any(c => c.Suit == suit) &&
                                westHand.Any(c => c.Suit == suit))
                            {
                                // make sure calculated tricks are correct
                                DDS d1 = new DDS(dds.Clone());
                                string reversed = N + " " + W + " " + S + " " + E;
                                DDS d2 = new DDS(reversed, trump, this.leader);
                                d1.Execute(card + " x"); d2.Execute(card + " x");
                                var nextMoves = this.NextMoves(opposite, card);
                                result = nextMoves.Max(next => Math.Min(
                                    d1.Tricks(next), d2.Tricks(next)));
                                this.output[card].Add((byte)result);
                                d1.Delete(); d2.Delete();
                            }
                            else this.output[card].Add((byte)tricks);
                        }
                        dds.Delete();
                    }
                    Interlocked.Increment(ref this.free);
                })
                { IsBackground = true }.Start();
            }
        }

        internal void EndEvaluate()
        {
            this.evaluate = false;
        }

        private bool Ignore(IEnumerable<Card> hand, Details details)
        {
            int minHcp = details.MinHCP;
            int maxHcp = details.MaxHCP;
            int hcp = hand.Sum(c => c.HCP());
            if (hcp < minHcp || hcp > maxHcp) return true;
            for (int index = 0; index <= 3; index++)
            {
                int min = details[(Suit)index, 0];
                int max = details[(Suit)index, 1];
                int count = hand.Count(c => c.Suit == (Suit)index);
                if (count < min || count > max) return true;
            }
            return false;
        }

        private IEnumerable<string> NextMoves(Player opposite, string lead)
        {
            var hand = opposite == Player.North ?
                this.northHand : this.southHand;
            Suit suit = (Suit)"CDHS".IndexOf(lead[1]);
            var output = hand.Select(c => c.ToString());
            var moves = hand.Where(c => c.Suit.Equals(
                suit)).Select(c => c.ToString());
            return moves.Count() > 0 ? moves : output;
        }
    }
}
