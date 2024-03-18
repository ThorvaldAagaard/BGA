using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BGADLL
{
    using static BGADLL.Macros;
    using Output = Dictionary<string, ConcurrentBag<byte>>;
    using Queue = ConcurrentQueue<int>;

    public class PIMC
    {
        private string commands = "";
        private bool evaluate = false;
        private Player leader = 0;
        private readonly int threads;
        private readonly List<byte[]> combinations = new List<byte[]>();
        private int K = 0, N = 0, playouts = 0, free;
        private IEnumerable<string> legalMoves = null;
        private readonly HashSet<string> check = null;
        private readonly Output output = new Output();
        private readonly Queue queue = new Queue();
        private readonly Utils utils = new Utils();
        private Random random = null;
        private int noOfCombinations = 0;
        private int examined = 0;
        private int seed = 0;

        // player hands
        private Hand played = null;
        private Constraints eastConsts = null;
        private Constraints westConsts = null;
        private int maxPlayout;
        private readonly Hand northHand = new Hand();
        private readonly Hand southHand = new Hand();
        private readonly Hand eastPlayed = new Hand();
        private readonly Hand westPlayed = new Hand();
        private readonly Hand opposCards = new Hand();

        // getters
        public int Combinations => this.noOfCombinations;
        public int Examined => this.examined;
        public int Playouts => this.playouts;
        public bool Evaluating => this.evaluate || this.threads != this.free;
        public IEnumerable<string> LegalMoves => this.legalMoves;
        public string LegalMovesToString => string.Join(", ", LegalMoves);
        public Output Output => this.output;

        public PIMC()
        {
            // MaxThreads and logging can be added as parameters
            int count = Environment.ProcessorCount;
            this.threads = Math.Max(1, count - 2);
            this.free = this.threads;
            Console.WriteLine("PIMC Loaded");
            Console.WriteLine($"Threads: {this.threads}");
        }

        private void Clear(List<byte[]> list)
        {
            int id = GC.GetGeneration(list);
            list.Clear();
            GC.Collect(id, GCCollectionMode.Forced);
        }

        public void Clear()
        {
            this.commands = "";
            this.output.Clear();
            this.northHand.Clear();
            this.southHand.Clear();
            this.eastPlayed.Clear();
            this.westPlayed.Clear();
            this.opposCards.Clear();
            this.Clear(this.combinations);
            this.noOfCombinations = 0;
            this.examined = 0;
            while (!this.queue.IsEmpty)
                this.queue.TryDequeue(out _);
        }

        public void LoadCombinations()
        {
            int n = this.N, k = this.K;
            noOfCombinations = this.utils.Count(n, k);
            int[] array = new int[noOfCombinations];
            // Create all combinations
            foreach (byte[] series in this.utils.Generate(n, k))
                this.combinations.Add(series.ToArray());

            for (int i = 0; i < noOfCombinations; i++) array[i] = i;
            // Shuffle the order combination is processed
            this.utils.Shuffle(array, noOfCombinations, this.random);
            // And queue all combinations
            foreach (int i in array) this.queue.Enqueue(i);
        }

        public IEnumerable<string> LegitMoves(Player player)
        {
            Hand cards = new List<Hand>() {
                this.northHand, this.opposCards,
                this.southHand, this.opposCards }[(int)player];
            var output = cards.Select(c => c.ToString());
            if (this.played.Count == 0) return output;
            var moves = cards.Where(c => this.played[0].Suit
                .Equals(c.Suit)).Select(c => c.ToString());
            return moves.Count() > 0 ? moves : output;
        }


        public void SetupEvaluation(Hand[] our, Hand oppos, Hand played, Constraints[] consts, Player leader, int maxPlayout)
        {
            //Console.WriteLine("SetupEvaluation");
            this.played = played;
            this.commands = string.Join(" ",
                played.Select(c => c.ToString()));
            this.eastConsts = consts[0];
            this.westConsts = consts[1];
            this.northHand.AddRange(our[0]);
            this.southHand.AddRange(our[1]);
            this.opposCards.AddRange(oppos);
            this.maxPlayout = maxPlayout;
            Player player = (Player)((int)leader);
            this.legalMoves = LegitMoves(leader);

            foreach (string card in this.legalMoves)
            {
                this.output.Add(card, new ConcurrentBag<byte>());
            }

            var leads = Enumerable.Reverse(played.Cards);
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

            if (seed == 0)
            {
                seed = CalculateSeed(this.northHand.ToString() + this.southHand.ToString());
                this.random = new Random(seed);
            }

            this.LoadCombinations();
        }

        static int CalculateSeed(string input)
        {
            // Calculate the SHA-256 hash
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = sha256.ComputeHash(inputBytes);

                // Convert the first 4 bytes of the hash to an integer and take modulus
                int hashInteger = BitConverter.ToInt32(hashBytes, 0);
                return hashInteger;
            }
        }

        public void BeginEvaluate(Trump trump)
        {
            if (this.evaluate) return;
            //Console.WriteLine("BeginEvaluation");
            this.evaluate = true;
            this.free = this.threads;
            string N = this.northHand.ToString();
            string S = this.southHand.ToString();
            Semaphore semaphore = new Semaphore(0, this.threads);
            for (int t = 0; t < this.threads; t++)
            {
                new Thread(start: () =>
                {
                    Interlocked.Decrement(ref this.free);
                    try
                    {
                        while (this.evaluate && !this.queue.IsEmpty)
                        {
                            Interlocked.Increment(ref this.playouts);
                            if (this.maxPlayout > 0 && this.playouts > this.maxPlayout)
                            {
                                // End processing if max playouts is reached
                                this.evaluate = false;
                            }

                            // repeat if failed to dequeue item
                            if (!this.queue.TryDequeue(out int pos))
                            {
                                Thread.Sleep(10); continue;
                            }

                            // recover hands before leads
                            var set = this.combinations[pos];
                            Interlocked.Increment(ref this.examined);
                            Hand westHand = new Hand(set.Select(index => this.opposCards[index - 1]));
                            var eastHand = this.opposCards.Except(westHand);

                            // exclude impossible hands
                            if (this.Ignore(eastHand, this.eastConsts) ||
                                this.Ignore(westHand, this.westConsts))
                            {
                                Interlocked.Decrement(ref this.playouts);
                                //Console.WriteLine("Hand ignored N:{0}", N + " " + eastHand + " " + S + " " + westHand);
                                continue;
                            }

                            // Constraints are updated after each played card, so is added after check, before DDS
                            westHand = westHand.Concat(this.westPlayed);
                            eastHand = eastHand.Concat(this.eastPlayed);
                            // DDS analysis
                            string E = eastHand.ToString(), W = westHand.ToString();
                            string format = N + " " + E + " " + S + " " + W;
                
                            DDS dds = new DDS(format, trump, this.leader);
                            try
                            {
                                if (this.commands != "") dds.Execute(this.commands);
                            } catch (Exception ex)
                            {
                                Console.WriteLine("Input: {0} Command: {1} Message: {2}", format, commands, ex.Message);
                                throw ex;
                            }
                            Player opposite = this.leader.Next().Next();
                            foreach (string card in this.legalMoves)
                            {
                                int tricks = dds.Tricks(card), result = -1;
                                this.output[card].Add((byte)tricks);
                                Suit suit = (Suit)"CDHS".IndexOf(card[1]);
                                // Now we switch the EW hands ad calculate the result again
                                // But only if both hands has a card in the suit played,
                                // and constraints not are vialoted
                                if (this.N > 2 && this.played.Count == 0 && 
                                    eastHand.Any(c => c.Suit == suit) && 
                                    westHand.Any(c => c.Suit == suit) &&
                                    !this.Ignore(eastHand, this.westConsts) &&
                                    !this.Ignore(westHand, this.eastConsts)                                    )
                                {
                                    // make sure calculated tricks are correct
                                    DDS d1 = new DDS(dds.Clone());
                                    string reversed = N + " " + W + " " + S + " " + E;
                                    DDS d2 = new DDS(reversed, trump, this.leader);
                                    d1.Execute(card + " x"); 
                                    d2.Execute(card + " x");
                                    var nextMoves = this.NextMoves(opposite, card);
                                    result = nextMoves.Max(next => Math.Min(d1.Tricks(next), d2.Tricks(next)));
                                    this.output[card].Add((byte)result);
                                    d1.Delete(); 
                                    d2.Delete();
                                }
                                else 
                                    this.output[card].Add((byte)tricks);
                            }
                            dds.Delete();
                        }
                    }
                    finally
                    {
                        Interlocked.Increment(ref this.free);
                        semaphore.Release(); // Release the semaphore when the thread finishes
                    }
                    if (this.queue.IsEmpty)
                    {
                        this.evaluate = false;
                    }
                })
                { IsBackground = true }.Start();
            }
        }

        public void EndEvaluate()
        {
            //Console.WriteLine("EndEvaluation");
            this.evaluate = false;
        }

        private bool Ignore(Hand hand, Constraints consts)
        {
            int minHcp = consts.MinHCP;
            int maxHcp = consts.MaxHCP;
            int hcp = hand.Sum(c => c.HCP());
            if (hcp < minHcp || hcp > maxHcp) {
                //Console.WriteLine("HCP={0} {1} {2} {3}", hcp, minHcp, maxHcp, hand);
                return true; 
            }
            for (int index = 0; index <= 3; index++)
            {
                int min = consts[(Suit)index, 0];
                int max = consts[(Suit)index, 1];
                int count = hand.CardsInSuit(c => c.Suit == (Suit)index);

                if (count < min || count > max)
                {
                    //Console.WriteLine("Discarding {0}", hand);
                    return true;
                }
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
