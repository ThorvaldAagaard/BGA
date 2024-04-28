using Microsoft.SqlServer.Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static BGADLL.Macros;

namespace BGADLL
{
    using static BGADLL.Macros;
    using Output = Dictionary<string, ConcurrentBag<byte>>;
    using Queue = ConcurrentQueue<int>;

    public class PIMCDef
    {
        private string commands = "";
        private bool evaluate = false;
        private Player leader = 0;
        private readonly int threads;
        private readonly List<byte[]> combinations = new List<byte[]>();
        private int K = 0, N = 0, playouts = 0, free;
        private IEnumerable<string> legalMoves = null;
        private readonly Output output = new Output();
        private readonly Queue queue = new Queue();
        private readonly Utils utils = new Utils();
        private Random random = null;
        private int noOfCombinations = 0;
        private int examined = 0;
        private int seed = 0;

        // player hands
        private Hand played = null;
        private Constraints declarerConsts = null;
        private Constraints partnerConsts = null;
        private bool overdummy;
        private int maxPlayout;
        private readonly Hand dummyHand = new Hand();
        private readonly Hand ourHand = new Hand();
        private readonly Hand partnerPlayed = new Hand();
        private readonly Hand declarerPlayed = new Hand();
        private readonly Hand remainingCards = new Hand();

        // getters
        public int Combinations => this.noOfCombinations;
        public int Examined => this.examined;
        public int Playouts => this.playouts;
        public bool Evaluating => this.evaluate || this.threads != this.free;
        public string[] LegalMoves => this.legalMoves.ToArray();
        public string LegalMovesToString => string.Join(", ", LegalMoves);
        public Output Output => this.output;

        public PIMCDef(int MaxThreads)
        {
            // MaxThreads and logging can be added as parameters
            int count = Environment.ProcessorCount;
            this.threads = Math.Max(1, count - 2);
            if (MaxThreads > 0)
                this.threads = Math.Min(MaxThreads, this.threads);
            this.free = this.threads;
            Assembly assembly = Assembly.GetExecutingAssembly();
            Version version = assembly.GetName().Version;
            Console.WriteLine($"PIMCDef Loaded - version: {version} Threads: {this.threads}");
        }

        // Parameterless constructor calling the existing constructor with -1 as the parameter
        public PIMCDef() : this(-1)
        {
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
            this.dummyHand.Clear();
            this.ourHand.Clear();
            this.partnerPlayed.Clear();
            this.declarerPlayed.Clear();
            this.remainingCards.Clear();
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

        public IEnumerable<string> LegitMoves(Player player, bool overdummy)
        {
            Hand cards;
            if (overdummy)
            {
                cards = new List<Hand>() {
                this.dummyHand,
                this.ourHand, this.remainingCards, this.remainingCards }[(int)player];
            }
            else
            {
                cards = new List<Hand>() {
                this.dummyHand, this.remainingCards,
                this.remainingCards, this.ourHand}[(int)player];
            }
            var output = cards.Select(c => c.ToString());
            if (this.played.Count == 0) return output;
            var moves = cards.Where(c => this.played[0].Suit
                .Equals(c.Suit)).Select(c => c.ToString());
            // If no legal moves, then all cards are allowed
            return moves.Count() > 0 ? moves : output;
        }

        public void validateInput()
        {
            if ((dummyHand.Cards.Count() + ourHand.Cards.Count() + remainingCards.Cards.Count() + played.Count) % 4 != 0)
            {
                Console.WriteLine("Number of cards {0}", dummyHand.Cards.Count() + ourHand.Cards.Count() + remainingCards.Cards.Count() + played.Count);
                throw new Exception("Wrong number of cards");
            }
            if (declarerConsts.MinHCP + partnerConsts.MinHCP > remainingCards.Sum(c => c.HCP()))
            {
                Console.WriteLine(string.Format("Constraints not possible - Min HCP {0} {1}", declarerConsts.MinHCP + partnerConsts.MinHCP, remainingCards.Sum(c => c.HCP())));
                declarerConsts.MinHCP = 0;
                partnerConsts.MinHCP = 0;
            }
            if (declarerConsts.MaxHCP + partnerConsts.MaxHCP < remainingCards.Sum(c => c.HCP()))
            {
                Console.WriteLine(string.Format("Constraints not possible - Max HCP {0} {1}", declarerConsts.MaxHCP + partnerConsts.MaxHCP, remainingCards.Sum(c => c.HCP())));
                declarerConsts.MaxHCP = 37;
                partnerConsts.MaxHCP = 37;
            }
            int min = 0;
            int max = 0;
            for (int index = 0; index <= 3; index++)
            {
                min += declarerConsts[(Suit)index, 0] + partnerConsts[(Suit)index, 0];
                max += declarerConsts[(Suit)index, 1] + partnerConsts[(Suit)index, 1];
            }
            if (min > remainingCards.Count || remainingCards.Count > max)
            {
                // Remove constraints
                declarerConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
                partnerConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            }
            else
            {

                for (int index = 0; index <= 3; index++)
                {
                    min = declarerConsts[(Suit)index, 0] + partnerConsts[(Suit)index, 0];
                    max = declarerConsts[(Suit)index, 1] + partnerConsts[(Suit)index, 1];
                    int count = remainingCards.CardsInSuit(c => c.Suit == (Suit)index);
                    if (count == 0)
                    {
                        // No more cards in the suit, so we force contraints to zero.
                        declarerConsts[(Suit)index, 0] = 0;
                        partnerConsts[(Suit)index, 0] = 0;
                        declarerConsts[(Suit)index, 1] = 0;
                        partnerConsts[(Suit)index, 1] = 0;
                    }
                    else
                    {
                        if (count < min || count > max)
                        {
                            Console.WriteLine("Constraints not possible - Suit lengths {0} count={1} min={2} max={3}", (Suit)index, count, min, max);
                            declarerConsts[(Suit)index, 0] = 0;
                            partnerConsts[(Suit)index, 0] = 0;
                            declarerConsts[(Suit)index, 1] = 37;
                            partnerConsts[(Suit)index, 1] = 37;
                        }
                    }
                }
            }
        }



        public string SetupEvaluation(Hand[] our, Hand oppos, Hand played, Constraints[] consts, Player leader, int maxPlayout, bool autoplaysingleton, bool overdummy)
        {
            //Console.WriteLine("SetupEvaluation");
            this.played = played;
            this.commands = string.Join(" ", played.Select(c => c.ToString()));
            this.declarerConsts = consts[0];
            this.partnerConsts = consts[1];
            this.dummyHand.AddRange(our[0]);
            this.ourHand.AddRange(our[1]);
            this.remainingCards.AddRange(oppos);
            validateInput();

            this.overdummy = overdummy;
            this.maxPlayout = maxPlayout;
            Player player = (Player)((int)leader);
            this.legalMoves = LegitMoves(leader, overdummy);

            if (autoplaysingleton && legalMoves.Count() == 1)
            {
                return legalMoves.First();
            }

            foreach (string card in this.legalMoves)
            {
                this.output.Add(card, new ConcurrentBag<byte>());
            }

            var leads = Enumerable.Reverse(played.Cards);
            foreach (Card lead in leads)
            {
                player = player.Prev();
                if (overdummy)
                {
                    switch (player)
                    {
                        case Player.North: this.dummyHand.Add(lead); break;
                        case Player.South: this.declarerPlayed.Add(lead); break;
                        case Player.East: this.ourHand.Add(lead); break;
                        case Player.West: this.partnerPlayed.Add(lead); break;
                        default: break;
                    }
                }
                else
                {
                    switch (player)
                    {
                        case Player.North: this.dummyHand.Add(lead); break;
                        case Player.South: this.declarerPlayed.Add(lead); break;
                        case Player.East: this.partnerPlayed.Add(lead); break;
                        case Player.West: this.ourHand.Add(lead); break;
                        default: break;
                    }
                }
            }

            // They should not be different
            int left = Math.Max(dummyHand.Count, ourHand.Count);
            this.K = left - this.partnerPlayed.Count;
            this.N = remainingCards.Count;
            this.playouts = 0;
            this.leader = player;

            if (seed == 0)
            {
                seed = CalculateSeed(this.dummyHand.ToString() + this.ourHand.ToString());
                this.random = new Random(seed);
            }

            this.LoadCombinations();
            return null;
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
            //Console.WriteLine("BeginEvaluation");
            this.evaluate = true;
            this.free = this.threads;
            string dummy = this.dummyHand.ToString();
            string we = this.ourHand.ToString();
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
                            if (this.maxPlayout > 0 && this.playouts > this.maxPlayout)
                            {
                                // End processing if max playouts is reached
                                this.evaluate = false; continue;
                            }
                            // repeat if failed to dequeue item
                            if (!this.queue.TryDequeue(out int pos))
                            {
                                Thread.Sleep(10); continue;
                            }

                            Interlocked.Increment(ref this.playouts);
                            // recover hands before leads
                            var set = this.combinations[pos];
                            Interlocked.Increment(ref this.examined);
                            // First find partners hand
                            Hand partnerHand = new Hand(set.Select(index => this.remainingCards[index - 1]));
                            // Then give the rest to declarer
                            Hand declarerHand = this.remainingCards.Except(partnerHand);

                            // exclude impossible hands
                            if (this.Ignore(declarerHand, this.declarerConsts) ||
                                this.Ignore(partnerHand, this.partnerConsts))
                            {
                                Interlocked.Decrement(ref this.playouts);
                                //Console.WriteLine("Hand ignored N:{0}", N + " " + eastHand + " " + S + " " + westHand);
                                continue;
                            }

                            // Constraints are updated after each played card, so is added after check, before DDS
                            partnerHand = partnerHand.Concat(this.partnerPlayed);
                            declarerHand = declarerHand.Concat(this.declarerPlayed);
                            // DDS analysis
                            string declarer = declarerHand.ToString(), partner = partnerHand.ToString();
                            string format = "";
                            if (this.overdummy)
                            {
                                format = dummy + " " + we + " " + declarer + " " + partner;
                            }
                            else
                            {
                                format = dummy + " " + partner + " " + declarer + " " + we;
                            }
                            if (!(dummy.Length == we.Length && partner.Length == declarer.Length && declarer.Length == we.Length))
                            {
                                Console.WriteLine("Input: {0} Command: {1} Message: {2}", format, commands, "Wrong number of cards");
                                throw new Exception("Wrong number of cards");
                            }
                            DDS dds = new DDS(format, trump, this.leader);
                            try
                            {
                                if (this.commands != "") dds.Execute(this.commands);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Input: {0} Command: {1} Message: {2}", format, commands, ex.Message);
                                throw ex;
                            }
                            Player opposite = this.leader.Next().Next();
                            foreach (string card in this.legalMoves)
                            {
                                int tricks = dds.Tricks(card), result = -1;
                                try
                                {
                                    this.output[card].Add((byte)tricks);
                                }
                                catch (KeyNotFoundException ex)
                                {
                                    throw new KeyNotFoundException($"Key '{card}' not found in output dictionary.", ex);
                                }
                                Suit suit = (Suit)"CDHS".IndexOf(card[1]);
                                // Now we switch the partner and declarers hands ad calculate the result again
                                // But only if dummy and partner has a card in the suit played,
                                // and constraints not are violated
                                if (this.N > 6 && this.played.Count == 0 &&
                                    dummyHand.Any(c => c.Suit == suit) &&
                                    partnerHand.Any(c => c.Suit == suit) &&
                                    !this.Ignore(declarerHand, this.partnerConsts) &&
                                    !this.Ignore(partnerHand, this.declarerConsts))
                                {
                                    string reversed = "";
                                    if (this.overdummy)
                                    {
                                        reversed = dummy + " " + we + " " + partner + " " + declarer;
                                    }
                                    else
                                    {
                                        reversed = dummy + " " + declarer + " " + partner + " " + we;
                                    }

                                    DDS d2 = new DDS(reversed, trump, this.leader);
                                    try
                                    {
                                        d2.Execute(card + " x");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("Input: {0} Command: {1} x   Message: {2}", format, card, ex.Message);
                                        throw ex;
                                    }
                                    var nextMoves = this.NextMoves(declarer.Parse(), card);
                                    result = nextMoves.Max(next => d2.Tricks(next));
                                    if (result > 13 || result < 0)
                                    {
                                        Console.WriteLine("Input: {0} Command: {1} x   Result: {2} nextMove {3}", format, card, result, nextMoves);
                                        throw new Exception("-1 from DDS");
                                    }
                                    else
                                    {
                                        this.output[card].Add((byte)result);
                                    }
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
            if (hcp < minHcp || hcp > maxHcp)
            {
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

        private IEnumerable<string> NextMoves(Hand hand, string lead)
        {
            Suit suit = (Suit)"CDHS".IndexOf(lead[1]);
            var output = hand.Select(c => c.ToString());
            var moves = hand.Where(c => c.Suit.Equals(suit)).Select(c => c.ToString());
            return moves.Count() > 0 ? moves : output;
        }
    }
}
