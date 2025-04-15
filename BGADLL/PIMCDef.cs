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
    using Queue = ConcurrentQueue<int>;

    public class PIMCDef
    {
        private string commands = "";
        private bool evaluate = false;
        private Player leader = 0;
        private readonly int threads;
        private readonly List<byte[]> combinations = new List<byte[]>();
        private int playouts = 0, free;
        private IEnumerable<string> legalMoves = null;
        private readonly CardTricks output = new CardTricks();
        private readonly Queue queue = new Queue();
        private readonly Utils utils = new Utils();
        private Random random = null;
        private int noOfCombinations = 0;
        private int combination_n;
        private int combination_k;
        private int examined = 0;
        private int seed = 0;
        private int activeThreads = 0;  // Counter to track active threads
        private int[] combinationIndex;

        // player hands
        private Play current_trick = null;
        private Play previous_tricks = null;
        private Constraints declarerConsts = null;
        private Constraints partnerConsts = null;
        private bool overdummy;
        private int maxPlayout;
        private bool useFusuionStrategy = false;

        public bool verbose { get; private set; }

        private readonly Hand dummyHand = new Hand();
        private readonly Hand ourHand = new Hand();
        private readonly Hand partnerHand = new Hand();
        private readonly Hand declarerHand = new Hand();
        private readonly Hand partnerHandShown = new Hand();
        private readonly Hand declarerHandShown = new Hand();
        private readonly Hand remainingCards = new Hand();

        // getters
        public int Combinations => this.noOfCombinations;
        public int Examined => this.examined;
        public int Playouts => this.playouts;
        public bool UseFusionStrategy => this.useFusuionStrategy;
        public bool Evaluating => this.evaluate || this.threads != this.free;
        public string[] LegalMoves => this.legalMoves.ToArray();
        public string LegalMovesToString => string.Join(", ", LegalMoves);
        public CardTricks Output => this.output;

        public PIMCDef(int MaxThreads, bool verbose)
        {
            this.verbose = verbose;
            int count = Environment.ProcessorCount;
            this.threads = Math.Max(1, count - 2);
            if (MaxThreads > 0)
                this.threads = Math.Min(MaxThreads, this.threads);
            this.free = this.threads;
            if (verbose)
            {
                Console.WriteLine($"PIMCDef Loaded - Threads: {this.threads}");
            }
        }

        public PIMCDef(int MaxThreads) : this(MaxThreads, false)
        {
        }

        // Parameterless constructor calling the existing constructor with -1 as the parameter
        public PIMCDef() : this(-1)
        {
        }

        public string version()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Version assemblyversion = assembly.GetName().Version;
            return $"{assemblyversion}";
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
            this.partnerHand.Clear();
            this.declarerHand.Clear();
            this.remainingCards.Clear();
            this.Clear(this.combinations);
            this.noOfCombinations = 0;
            this.examined = 0;
            while (!this.queue.IsEmpty)
                this.queue.TryDequeue(out _);
        }
        
        public long ComputeChecksum(int[] array)
        {
            long checksum = 0;
            for (int i = 0; i < array.Length; i++)
            {
                checksum += (long)array[i] * (i + 1); // Multiply by index to account for order
            }
            return checksum;
        }

        public void LoadCombinations(int n, int k)
        {
            noOfCombinations = (int)this.utils.Binomial(n, k);  // optional if you still use it elsewhere
            this.combination_n = n;
            this.combination_k = k;
            this.combinationIndex = Enumerable.Range(0, noOfCombinations).ToArray();
            this.utils.Shuffle(combinationIndex, noOfCombinations, this.random);

        }
        public void LoadCombinationsOld(int n, int k)
        {
            noOfCombinations = this.utils.Count(n, k);
            this.combinationIndex = new int[noOfCombinations];
            // Create all combinations
            foreach (byte[] series in this.utils.Generate(n, k))
                this.combinations.Add(series.ToArray());

            for (int i = 0; i < noOfCombinations; i++) combinationIndex[i] = i;
            this.utils.Shuffle(combinationIndex, noOfCombinations, this.random);
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
            if (this.current_trick.Count == 0) return output;
            var moves = cards.Where(c => this.current_trick[0].Suit
                .Equals(c.Suit)).Select(c => c.ToString());
            // If no legal moves, then all cards are allowed
            return moves.Count() > 0 ? moves : output;
        }

        public void validateInput()
        {
            Deck deck = new Deck();
            deck.AddRange(dummyHand);
            deck.AddRange(ourHand);
            deck.AddRange(partnerHand);
            deck.AddRange(declarerHand);
            deck.AddRange(remainingCards);
            deck.AddRange(current_trick.Cards);
            if (deck.Count != dummyHand.Count + ourHand.Count + remainingCards.Count + current_trick.Count)
            {
                Console.WriteLine("Deck {0}", deck);
                throw new Exception("Duplicate cards");
            }
            if (deck.Count % 4 != 0)
            {
                Console.WriteLine("Deck {0}", deck);
                throw new Exception($"Wrong number of cards: {deck}");
            }
            var cards_left = deck.Count / 4;
            if (dummyHand.Count > cards_left || dummyHand.Count < cards_left - 1)
            {
                Console.WriteLine("Wrong number of cards in dummy {0}", dummyHand);
                throw new Exception($"Wrong number of cards in dummy {dummyHand}");
            }
            if (ourHand.Count > cards_left || ourHand.Count < cards_left - 1)
            {
                Console.WriteLine("Wrong number of cards in our hand {0}", ourHand);
                throw new Exception($"Wrong number of cards in our hand {ourHand}");
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

        public void updateConstraints()
        {
            int min = 0;
            int max = 0;
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


        public string SetupEvaluation(Hand[] our, Hand oppos, Play current_trick, Play previous_tricks, Constraints[] consts, Player leader, int maxPlayout, bool autoplaysingleton, bool overdummy)
        {
            //Console.WriteLine("SetupEvaluation");
            this.current_trick = current_trick;
            this.previous_tricks = previous_tricks;
            this.commands = string.Join(" ", current_trick.Select(c => c.ToString()));
            // Our modification should not be on the passed object
            // Create a new array for the copied Constraints
            Constraints[] copiedConstraints = new Constraints[consts.Length];

            // Deep copy each Constraints object using the Clone method
            for (int i = 0; i < consts.Length; i++)
            {
                copiedConstraints[i] = (Constraints)consts[i].Clone();
            }

            this.declarerConsts = copiedConstraints[0];
            this.partnerConsts = copiedConstraints[1];
            this.dummyHand.AddRange(our[0]);
            this.ourHand.AddRange(our[1]);
            if (our.Length > 2)
            {
                this.partnerHandShown.AddRange(our[2]);
                this.declarerHandShown.AddRange(our[3]);
            }
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
                this.output.Add(card);
            }

            var leads = Enumerable.Reverse(current_trick.Cards);
            foreach (Card lead in leads)
            {
                player = player.Prev();
                if (overdummy)
                {
                    switch (player)
                    {
                        case Player.North: this.dummyHand.Add(lead); break;
                        case Player.South:
                            this.declarerHand.Add(lead);
                            remainingCards.Add(lead);
                            declarerConsts[lead.Suit, 1] += 1;
                            declarerConsts.MaxHCP += lead.HCP();
                            declarerConsts.MinHCP += lead.HCP();
                            break;
                        case Player.East: this.ourHand.Add(lead); break;
                        case Player.West:
                            this.partnerHand.Add(lead);
                            remainingCards.Add(lead);
                            partnerConsts[lead.Suit, 1] += 1;
                            partnerConsts.MaxHCP += lead.HCP();
                            partnerConsts.MinHCP += lead.HCP();
                            break;
                        default: break;
                    }
                }
                else
                {
                    switch (player)
                    {
                        case Player.North: this.dummyHand.Add(lead); break;
                        case Player.South:
                            this.declarerHand.Add(lead);
                            remainingCards.Add(lead);
                            declarerConsts[lead.Suit, 1] += 1;
                            declarerConsts.MaxHCP += lead.HCP();
                            declarerConsts.MinHCP += lead.HCP();
                            break;
                        case Player.East:
                            this.partnerHand.Add(lead);
                            remainingCards.Add(lead);
                            partnerConsts[lead.Suit, 1] += 1;
                            partnerConsts.MaxHCP += lead.HCP();
                            partnerConsts.MinHCP += lead.HCP();
                            break;
                        case Player.West: this.ourHand.Add(lead); break;
                        default: break;
                    }
                }
            }

            updateConstraints();
            
            // They should not be different
            int left = Math.Max(dummyHand.Count, ourHand.Count);
            this.playouts = 0;
            this.leader = player;

            seed = this.utils.CalculateSeed(this.dummyHand.ToString() + this.ourHand.ToString());
            this.random = new Random(seed);

            this.LoadCombinations(remainingCards.Count, remainingCards.Count / 2);
            return null;
        }

        public List<int> findSamples(Trump trump)
        {
            List<int> samples = new List<int>();
            // recover hands before leads
            string N = this.dummyHand.ToString();
            string dummy = this.dummyHand.ToString();
            string we = this.ourHand.ToString();
            for (int i = 0; i < noOfCombinations; i++)
            {
                if (playouts >= this.maxPlayout)
                {
                    break;
                }

                int idx = combinationIndex[i];
                var set = this.utils.GetCombinationAtIndex(this.combination_n, this.combination_k, idx);
                //var set = this.combinations[combinationIndex[i]];
                Interlocked.Increment(ref this.examined);
                // First find partners hand
                Hand partnerHand = new Hand(set.Select(index => this.remainingCards[index - 1]));
                // Then give the rest to declarer
                Hand declarerHand = this.remainingCards.Except(partnerHand);

                // Constraints are updated after each current_trick card, so is added after check, before DDS
                partnerHand = partnerHand.Concat(this.partnerHand);
                declarerHand = declarerHand.Concat(this.declarerHand);
                string E = "";
                string W = "";
                if (this.overdummy)
                {
                    W = partnerHand.ToString();
                    E = this.ourHand.ToString();
                }
                else
                {
                    E = partnerHand.ToString();
                    W = this.ourHand.ToString();
                }

                string S = declarerHand.ToString();

                // All hands must have the same number of cards, or it will crash
                string format = N + " " + E + " " + S + " " + W;
                if (!(N.Length == E.Length && E.Length == S.Length && S.Length == W.Length))
                {
                    //Console.WriteLine("Hand ignored N:{0}", N + " " + E + " " + S + " " + W);
                    continue;
                }


                // exclude impossible hands
                if (this.Ignore(declarerHand, this.declarerConsts) ||
                    this.Ignore(partnerHand, this.partnerConsts))
                {
                    //Console.WriteLine("Hand ignored N:{0}", N + " " + eastHand + " " + S + " " + westHand);
                    continue;
                }
                if (this.verbose && playouts < 10)
                {
                    Console.WriteLine("Hand N:{0}", format);
                }
                samples.Add(combinationIndex[i]);
                playouts += 1;

            }
            if (this.verbose) 
                Console.WriteLine("Examined {0} Playout {1}", examined, playouts);
            return samples;
        }


        public void BeginEvaluate(Trump trump)
        {
            //Console.WriteLine("BeginEvaluation");
            // And queue all combinations
            foreach (int i in combinationIndex) this.queue.Enqueue(i);

            this.evaluate = true;
            this.free = this.threads;
            string N = this.dummyHand.ToString();
            string dummy = this.dummyHand.ToString();
            string we = this.ourHand.ToString();
            Semaphore semaphore = new Semaphore(0, this.threads);
            for (int t = 0; t < this.threads; t++)
            {
                Interlocked.Increment(ref activeThreads);  // Increment when starting a new thread
                new Thread(start: () =>
                {
                    Interlocked.Decrement(ref this.free);
                    try
                    {
                        while (this.evaluate && !this.queue.IsEmpty)
                        {
                            if (this.maxPlayout > 0 && this.playouts > this.maxPlayout)
                            {
                                if (this.verbose)
                                {
                                    Console.WriteLine("maxPlayout {0} {1}", this.playouts, this.maxPlayout);
                                }
                                // End processing if max playouts is reached
                                this.evaluate = false; continue;
                            }
                            // repeat if failed to dequeue item
                            if (!this.queue.TryDequeue(out int pos))
                            {
                                if (this.queue.IsEmpty)
                                    break; // Exit if the queue is actually empty
                                Thread.Sleep(10); continue;
                            }

                            // We could use default weight of 1, but as we include fusion strategy we have 2 calculations for each combination
                            double weight = 1.0f;
                            // recover hands before leads
                            var set = this.utils.GetCombinationAtIndex(combination_n, combination_k, pos);
                            //var set = this.combinations[pos];
                            Interlocked.Increment(ref this.examined);
                            // First find partners hand
                            Hand partnerHand = new Hand(set.Select(index => this.remainingCards[index - 1]));
                            // Then give the rest to declarer
                            Hand declarerHand = this.remainingCards.Except(partnerHand);

                            // Constraints are updated after each current_trick card, so is added after check, before DDS
                            partnerHand = partnerHand.Concat(this.partnerHand);
                            declarerHand = declarerHand.Concat(this.declarerHand);
                            string E = "";
                            string W = "";
                            if (this.overdummy)
                            {
                                W = partnerHand.ToString();
                                E = this.ourHand.ToString();
                            }
                            else
                            {
                                E = partnerHand.ToString();
                                W = this.ourHand.ToString();
                            }

                            string S = declarerHand.ToString();

                            // All hands must have the same number of cards, or it will crash
                            string format = N + " " + E + " " + S + " " + W;
                            if (!(N.Length == E.Length && E.Length == S.Length && S.Length == W.Length))
                            {
                                //Console.WriteLine("Hand ignored N:{0}", N + " " + E + " " + S + " " + W);
                                continue;
                            }


                            // exclude impossible hands
                            if (this.Ignore(declarerHand, this.declarerConsts) ||
                                this.Ignore(partnerHand, this.partnerConsts))
                            {
                                //Console.WriteLine("Hand ignored N:{0}", N + " " + eastHand + " " + S + " " + westHand);
                                continue;
                            }

                            Interlocked.Increment(ref this.playouts);
                            // DDS analysis
                            string declarer = declarerHand.ToString(), partner = partnerHand.ToString();
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
                            foreach (string card in this.legalMoves)
                            {
                                int tricks = dds.Tricks(card);
                                try
                                {
                                    output.AddTricksWithWeight(card, (byte)tricks, weight, pos);
                                    if (tricks > 1)
                                    {
                                        //Console.WriteLine("Input: {0} Card: {1} Tricks: {2}", format, card, tricks);
                                    }
                                }
                                catch (KeyNotFoundException ex)
                                {
                                    throw new KeyNotFoundException($"Key '{card}' not found in output dictionary.", ex);
                                }
                                Suit suit = (Suit)"CDHS".IndexOf(card[1]);
                            }
                            dds.Delete();
                        }
                    }
                    finally
                    {
                        Interlocked.Increment(ref this.free);
                        Interlocked.Decrement(ref activeThreads);  // Decrement when the thread finishes
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
        public void Evaluate(Trump trump)
        {
            //Console.WriteLine("BeginEvaluation");
            List<int> samples = findSamples(trump);
            foreach (int i in samples) this.queue.Enqueue(i);

            this.evaluate = true;
            this.free = this.threads;
            string N = this.dummyHand.ToString();
            string dummy = this.dummyHand.ToString();
            string we = this.ourHand.ToString();
            Semaphore semaphore = new Semaphore(0, this.threads);
            for (int t = 0; t < this.threads; t++)
            {
                Interlocked.Increment(ref activeThreads);  // Increment when starting a new thread
                new Thread(start: () =>
                {
                    Interlocked.Decrement(ref this.free);
                    try
                    {
                        while (this.evaluate && !this.queue.IsEmpty)
                        {
                            if (this.maxPlayout > 0 && this.playouts > this.maxPlayout)
                            {
                                if (this.verbose)
                                {
                                    Console.WriteLine("maxPlayout {0} {1}", this.playouts, this.maxPlayout);
                                }
                                // End processing if max playouts is reached
                                this.evaluate = false; continue;
                            }
                            // repeat if failed to dequeue item
                            if (!this.queue.TryDequeue(out int pos))
                            {
                                Thread.Sleep(10); continue;
                            }

                            // recover hands before leads
                            var set = this.utils.GetCombinationAtIndex(combination_n, combination_k, pos);
                            //var set = this.combinations[pos];
                            Interlocked.Increment(ref this.examined);
                            // First find partners hand
                            Hand partnerHand = new Hand(set.Select(index => this.remainingCards[index - 1]));
                            // Then give the rest to declarer
                            Hand declarerHand = this.remainingCards.Except(partnerHand);

                            // Constraints are updated after each current_trick card, so is added after check, before DDS
                            partnerHand = partnerHand.Concat(this.partnerHand);
                            declarerHand = declarerHand.Concat(this.declarerHand);
                            string E = "";
                            string W = "";
                            if (this.overdummy)
                            {
                                W = partnerHand.ToString();
                                E = this.ourHand.ToString();
                            }
                            else
                            {
                                E = partnerHand.ToString();
                                W = this.ourHand.ToString();
                            }

                            string S = declarerHand.ToString();

                            // All hands must have the same number of cards, or it will crash
                            string format = N + " " + E + " " + S + " " + W;
                            // DDS analysis
                            string declarer = declarerHand.ToString(), partner = partnerHand.ToString();
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
                            //Console.WriteLine("Input: {0} Command: {1}", format, commands);
                            if (this.verbose)
                            {
                                //Console.WriteLine("Input: {0} Command: {1}", format, commands);
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
                            foreach (string card in this.legalMoves)
                            {
                                int tricks = dds.Tricks(card);
                                try
                                {
                                    output.AddTricksWithWeight(card, (byte)tricks, tricks, pos);

                                    if (tricks > 1)
                                    {
                                        //Console.WriteLine("Input: {0} Card: {1} Tricks: {2}", format, card, tricks);
                                    }
                                }
                                catch (KeyNotFoundException ex)
                                {
                                    throw new KeyNotFoundException($"Key '{card}' not found in output dictionary.", ex);
                                }
                                Suit suit = (Suit)"CDHS".IndexOf(card[1]);
                            }
                            dds.Delete();
                        }
                    }
                    finally
                    {
                        Interlocked.Increment(ref this.free);
                        Interlocked.Decrement(ref activeThreads);  // Decrement when the thread finishes
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


        public void AwaitEvaluation(int MaxWait)
        {
            var startTime = DateTime.Now;
            while (this.evaluate && (DateTime.Now - startTime).TotalMilliseconds < MaxWait)
            {
                Thread.Sleep(10); // Sleep for 50 milliseconds
                if (!this.evaluate)
                {
                    if (this.verbose)
                        Console.WriteLine("Playouts {0} Execution time {1:F3} Examined {2}", this.playouts, (DateTime.Now - startTime).TotalSeconds, this.examined);
                    return;
                }
            }
            // Max execution time exeeded
            this.evaluate = false;
            // Now wait for all active threads to finish
            while (Interlocked.CompareExchange(ref activeThreads, 0, 0) > 0)
            {
                //Console.WriteLine("Active threads{0}", activeThreads);
                Thread.Sleep(10);  // Sleep and wait for active threads to complete
            }

            if (this.verbose)
                Console.WriteLine("Playouts {0} Execution time {1:F3} Examined {2}", this.playouts, (DateTime.Now - startTime).TotalSeconds, this.examined);
            return;
        }

        public void EndEvaluate()
        {
            if (this.verbose)
            {
                Console.WriteLine("EndEvaluation");
            }
            this.evaluate = false;
        }

        private bool Ignore(Hand hand, Constraints constraints)
        {
            int minHcp = constraints.MinHCP;
            int maxHcp = constraints.MaxHCP;
            int hcp = hand.Sum(c => c.HCP());
            if (hcp < minHcp || hcp > maxHcp) {
                //Console.WriteLine("HCP={0} {1} {2} {3}", hcp, minHcp, maxHcp, hand);
                return true;
            }
            for (int index = 0; index <= 3; index++)
            {
                int min = constraints[(Suit)index, 0];
                int max = constraints[(Suit)index, 1];
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
