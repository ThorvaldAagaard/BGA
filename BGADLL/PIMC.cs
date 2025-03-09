using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BGADLL
{
    using static BGADLL.Macros;
    using Queue = ConcurrentQueue<int>;

    public class PIMC
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
        private int examined = 0;
        private int seed = 0;
        private int activeThreads = 0;  // Counter to track active threads
        private int[] combinationIndex;

        // player hands
        private Play current_trick = null;
        private Play previous_tricks = null;
        private Constraints eastConsts = null;
        private Constraints westConsts = null;
        private int maxPlayout;
        private bool useFusuionStrategy = true;

        public bool verbose { get; private set; }

        private readonly Hand northHand = new Hand();
        private readonly Hand southHand = new Hand();
        private readonly Hand eastHand = new Hand();
        private readonly Hand westHand = new Hand();
        private readonly Hand eastHandShown = new Hand();
        private readonly Hand westHandShown = new Hand();
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
        public PIMC(int MaxThreads, bool verbose)
        {
            this.verbose = verbose;
            int count = Environment.ProcessorCount;
            this.threads = Math.Max(1, count - 2);
            if (MaxThreads > 0)
                this.threads = Math.Min(MaxThreads, this.threads);
            this.free = this.threads;
            if (verbose)
            {
                Console.WriteLine($"PIMC Loaded - Threads: {this.threads}");
            }
        }

        public PIMC(int MaxThreads) : this(MaxThreads, false)
        {
        }

        // Parameterless constructor calling the existing constructor with -1 as the parameter
        public PIMC() : this(-1, false)
        {
        }

        private void Clear(List<byte[]> list)
        {
            int id = GC.GetGeneration(list);
            list.Clear();
            GC.Collect(id, GCCollectionMode.Forced);
        }

        public string version()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Version assemblyversion = assembly.GetName().Version;
            return $"{assemblyversion}";
        }
        public void Clear()

        {
            this.commands = "";
            this.output.Clear();
            this.northHand.Clear();
            this.southHand.Clear();
            this.eastHand.Clear();
            this.westHand.Clear();
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

        public int GetShuffledIndex(int n)
        {
            const int Multiplier = 1664525;
            const int Increment = 1013904223;
            const long Modulus = 1L << 31;

            long x = this.seed;
            x = (Multiplier * x + Increment * n) % Modulus; // Skip intermediate iterations
            return (int)((x % noOfCombinations + noOfCombinations) % noOfCombinations);
        }

        // Access combinations in pseudo-random order using LCG
        // Access combinations via LCG
        public int GetShuffledCombinationIndex(int i)
        {
            // We have shuffled the combinations indexes, so here we just return the number
            //return i;
            int shuffledIndex = GetShuffledIndex(i);
            //Console.WriteLine("{0} {1} {2}", i, shuffledIndex, noOfCombinations);
            return shuffledIndex;
        }

        public void LoadCombinations(int n, int k)
        {
            noOfCombinations = this.utils.Count(n, k);
            this.combinationIndex = new int[noOfCombinations];
            // Create all combinations
            foreach (byte[] series in this.utils.Generate(n, k))
                this.combinations.Add(series.ToArray());

            for (int i = 0; i < noOfCombinations; i++) combinationIndex[i] = i;
            this.utils.Shuffle(combinationIndex, noOfCombinations, this.random);
        }

        public IEnumerable<string> LegitMoves(Player player)
        {
            Hand cards = new List<Hand>() {
                this.northHand, this.remainingCards,
                this.southHand, this.remainingCards }[(int)player];
            var output = cards.Select(c => c.ToString());
            if (this.current_trick.Count == 0) return output;
            var moves = cards.Where(c => this.current_trick[0].Suit
                .Equals(c.Suit)).Select(c => c.ToString());
            moves = moves.Count() > 0 ? moves : output;
            return moves;
        }

        public void validateInput()
        {
            Deck deck = new Deck();
            deck.AddRange(northHand);
            deck.AddRange(southHand);
            deck.AddRange(eastHand);
            deck.AddRange(westHand);
            deck.AddRange(remainingCards);
            deck.AddRange(current_trick.Cards);
            if (deck.Count != northHand.Count + southHand.Count + eastHand.Count + westHand.Count + remainingCards.Count + current_trick.Count)
            {
                Console.WriteLine("Deck {0}", deck);
                throw new Exception($"Duplicate cards in deck: {deck}");
            }
            if (deck.Count % 4 != 0)
            {
                Console.WriteLine("Deck {0}", deck);
                throw new Exception($"Wrong number of cards: {deck}");
            }
            var cards_left = deck.Count / 4;
            if (northHand.Count > cards_left || northHand.Count < cards_left - 1)
            {
                Console.WriteLine("Wrong number of cards in North {0}", northHand);
                throw new Exception($"Wrong number of cards in North {northHand}");
            }
            if (southHand.Count > cards_left || southHand.Count < cards_left - 1)
            {
                Console.WriteLine("Wrong number of cards in South {0}", southHand);
                throw new Exception($"Wrong number of cards in South {southHand}");
            }
            if (eastConsts.MinHCP + westConsts.MinHCP > remainingCards.Sum(c => c.HCP()))
            {
                Console.WriteLine(string.Format("Constraints not possible - Min HCP {0} {1}", eastConsts.MinHCP + westConsts.MinHCP, remainingCards.Sum(c => c.HCP())));
                eastConsts.MinHCP = 0;
                westConsts.MinHCP = 0;
            }
            if (eastConsts.MaxHCP + westConsts.MaxHCP < remainingCards.Sum(c => c.HCP()))
            {
                Console.WriteLine(string.Format("Constraints not possible - Max HCP {0} {1}", eastConsts.MaxHCP + westConsts.MaxHCP, remainingCards.Sum(c => c.HCP())));
                eastConsts.MaxHCP = 37;
                westConsts.MaxHCP = 37;
            }
            int min = 0;
            int max = 0;
            for (int index = 0; index <= 3; index++)
            {
                min += eastConsts[(Suit)index, 0] + westConsts[(Suit)index, 0];
                max += eastConsts[(Suit)index, 1] + westConsts[(Suit)index, 1];
            }
            if (min > remainingCards.Count || remainingCards.Count > max)
            {
                // Remove constraints
                eastConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
                westConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            }
        }

        public void updateConstraints() {
            int min = 0;
            int max = 0;
            for (int index = 0; index <= 3; index++)
                {
                    min = eastConsts[(Suit)index, 0] + westConsts[(Suit)index, 0];
                    max = eastConsts[(Suit)index, 1] + westConsts[(Suit)index, 1];
                    int count = remainingCards.CardsInSuit(c => c.Suit == (Suit)index);
                    if (count == 0)
                    {
                        // No more cards in the suit, so we force contraints to zero.
                        eastConsts[(Suit)index, 0] = 0;
                        westConsts[(Suit)index, 0] = 0;
                        eastConsts[(Suit)index, 1] = 0;
                        westConsts[(Suit)index, 1] = 0;
                    }
                    else
                    {
                        if (count < min || count > max)
                        {
                            Console.WriteLine("Constraints not possible - Suit lengths {0} count={1} min={2} max={3}", (Suit)index, count, min, max);
                            eastConsts[(Suit)index, 0] = 0;
                            westConsts[(Suit)index, 0] = 0;
                            eastConsts[(Suit)index, 1] = 37;
                            westConsts[(Suit)index, 1] = 37;
                        }
                    }
                
            }
        }

    public void SetupEvaluation(Hand[] our, Hand oppos, Play current_trick, Play previous_tricks, Constraints[] consts, Player nextToLead, int maxPlayout, bool autoplaysingleton)
        {
            SetupEvaluation(our, oppos, current_trick, previous_tricks, consts, nextToLead, maxPlayout, autoplaysingleton, true);
        }
    public void SetupEvaluation(Hand[] our, Hand oppos, Play current_trick, Play previous_tricks, Constraints[] consts, Player nextToLead, int maxPlayout, bool autoplaysingleton, bool useStratefy)
        {
            //Console.WriteLine("SetupEvaluation");
            this.current_trick = current_trick;
            this.previous_tricks = previous_tricks;
            this.commands = string.Join(" ",
                current_trick.Select(c => c.ToString()));
            // Constraints are for remaining cards and does not include current_trick cards
            // Our modification should not be on the passed object
            // Create a new array for the copied Constraints
            Constraints[] copiedConstraints = new Constraints[consts.Length];

            // Deep copy each Constraints object using the Clone method
            for (int i = 0; i < consts.Length; i++)
            {
                copiedConstraints[i] = (Constraints)consts[i].Clone();
            }

            this.eastConsts = copiedConstraints[0];
            this.westConsts = copiedConstraints[1];
            this.northHand.AddRange(our[0]);
            this.southHand.AddRange(our[1]);
            if (our.Length > 2)
            {
                this.eastHandShown.AddRange(our[2]);
                this.westHandShown.AddRange(our[3]);
            }
            this.remainingCards.AddRange(oppos);
            validateInput();
            this.maxPlayout = maxPlayout;
            this.useFusuionStrategy = useStratefy;
            Player player = (Player)((int)nextToLead);
            this.legalMoves = LegitMoves(nextToLead);

            if (autoplaysingleton && legalMoves.Count() == 1)
            {
                this.evaluate = false;
            }

            foreach (string card in this.legalMoves)
            {
                this.output.Add(card);
            }

            var leads = Enumerable.Reverse(current_trick.Cards);
            foreach (Card lead in leads)
            {
                player = player.Prev();
                switch (player)
                {
                    case Player.North: 
                        this.northHand.Add(lead); 
                        break;
                    case Player.South: 
                        this.southHand.Add(lead); 
                        break;
                    case Player.East: 
                        this.eastHand.Add(lead);
                        remainingCards.Add(lead);
                        eastConsts[lead.Suit, 1] += 1;
                        eastConsts.MaxHCP += lead.HCP();
                        eastConsts.MinHCP += lead.HCP();
                        break;
                    case Player.West: 
                        this.westHand.Add(lead);
                        remainingCards.Add(lead);
                        westConsts[lead.Suit, 1] += 1;
                        westConsts.MaxHCP += lead.HCP();
                        westConsts.MinHCP += lead.HCP();
                        break;
                    default: break;
                }
            }
            updateConstraints();
            this.playouts = 0;
            this.leader = player;

            this.seed = this.utils.CalculateSeed(this.northHand.ToString() + this.southHand.ToString());
            this.random = new Random(this.seed);
            this.LoadCombinations(remainingCards.Count, remainingCards.Count / 2);
        }

        public List<int> findSamples(Trump trump)
        {
            List<int> samples = new List<int>();
            string N = this.northHand.ToString();
            string S = this.southHand.ToString();
            for (int i = 0; i < noOfCombinations; i++)
            {
                if (playouts >= this.maxPlayout)
                {
                    break;
                }
                var set = this.combinations[combinationIndex[i]];
                this.examined += 1;
                Hand westHand = new Hand(set.Select(index => this.remainingCards[index - 1]));
                Hand eastHand = this.remainingCards.Except(westHand);

                // Constraints are updated after each current_trick card, so is added after check, before DDS
                westHand = westHand.Concat(this.westHand);
                eastHand = eastHand.Concat(this.eastHand);

                string E = eastHand.ToString(), W = westHand.ToString();
                string format = N + " " + E + " " + S + " " + W;
                if (!(N.Length == E.Length && E.Length == S.Length && S.Length == W.Length))
                {
                    //Console.WriteLine("Hand ignored N:{0}", N + " " + eastHand + " " + S + " " + westHand);
                    //Console.WriteLine((N.Length, E.Length, S.Length, W.Length));
                    continue;
                }

                var sampleEast = new Hand();
                sampleEast.AddRange(this.eastHandShown);
                sampleEast.AddRange(eastHand);
                var sampleWest = new Hand();
                sampleWest.AddRange(this.westHandShown);
                sampleWest.AddRange(westHand);
                // exclude impossible hands
                if (this.Ignore(eastHand, this.eastConsts) ||
                    this.Ignore(westHand, this.westConsts))
                {
                    //Console.WriteLine("Hand ignored constraints N:{0}", N + " " + eastHand + " " + S + " " + westHand);
                    continue;
                }

                string hand = N + " " + eastHand + " " + S + " " + westHand;
                if (this.verbose && playouts <= 20)
                {
                    Console.WriteLine("Hand N:{0}", hand);
                }
                samples.Add(combinationIndex[i]);
                playouts += 1;

            }
            if (this.verbose) 
                Console.WriteLine("Examined {0} Playout {1}", examined, playouts);
            return samples;
        }

        public void Evaluate(Trump trump)
        {
            List<int> samples = findSamples(trump);
            foreach (int i in samples) this.queue.Enqueue(i);

            //Console.WriteLine("BeginEvaluation");
            this.evaluate = true;
            this.free = this.threads;
            string N = this.northHand.ToString();
            string S = this.southHand.ToString();
            Semaphore semaphore = new Semaphore(0, this.threads);
            // Always evaluate trump first
            if (trump != Trump.No)
            {
                List<string> movesList = legalMoves.ToList();

                for (int i = 0; i < movesList.Count; i++)
                {
                    var suit = "CDHS".IndexOf(movesList[i][1]);
                    if (trump == ((Trump)(suit)))
                    {
                        string importantMove = movesList[i];
                        movesList.RemoveAt(i);
                        movesList.Insert(0, importantMove);
                    }
                }
                legalMoves = movesList.ToArray();
            }
            for (int t = 0; t < this.threads; t++)
            {
                Interlocked.Increment(ref activeThreads);  // Increment when starting a new thread
                new Thread(start: () =>
                {
                    Interlocked.Decrement(ref this.free);
                    try
                    {
                        while (!this.queue.IsEmpty)
                        {
                            // repeat if failed to dequeue item
                            if (!this.queue.TryDequeue(out int pos))
                            {
                                if (this.queue.IsEmpty)
                                    break; // Exit if the queue is actually empty
                                Thread.Sleep(10); continue;
                            }

                            // We could use default weight of 1, but as we include fusion strategy we have 2 calculations for each combination
                            double weight = 0.5f;
                            // recover hands before leads
                            var set = this.combinations[pos];
                            Hand westHand = new Hand(set.Select(index => this.remainingCards[index - 1]));
                            Hand eastHand = this.remainingCards.Except(westHand);

                            // Constraints are updated after each current_trick card, so is added after check, before DDS
                            westHand = westHand.Concat(this.westHand);
                            eastHand = eastHand.Concat(this.eastHand);

                            // DDS analysis
                            string E = eastHand.ToString(), W = westHand.ToString();
                            // All hands must have the same number of cards, or it will crash
                            string format = N + " " + E + " " + S + " " + W;

                            var sampleEast = new Hand();
                            sampleEast.AddRange(this.eastHandShown);
                            sampleEast.AddRange(eastHand);
                            var sampleWest = new Hand();
                            sampleWest.AddRange(this.westHandShown);
                            sampleWest.AddRange(westHand);
                            double weight1 = sampleEast.getOdds();
                            double weight2 = sampleWest.getOdds();
                            weight = weight1 * weight2;
                            //Console.WriteLine("Hand N:{0} {1}", format, weight);

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
                                    output.AddTricksWithWeight(card, (byte)tricks, weight, pos);
                                    //Console.WriteLine("Card: {0} Tricks: {1}", card, tricks);
                                }
                                catch (KeyNotFoundException ex)
                                {
                                    throw new KeyNotFoundException($"Key '{card}' not found in output dictionary.", ex);
                                }
                                if (this.useFusuionStrategy)
                                {
                                    Suit suit = (Suit)"CDHS".IndexOf(card[1]);
                                    // Now we switch the EW hands and calculate the result again
                                    // But only if both hands has a card in the suit current_trick,
                                    // and constraints not are vialoted
                                    // This is the mixed strategy
                                    if (this.remainingCards.Count > 2 && this.current_trick.Count == 0 &&
                                        eastHand.Any(c => c.Suit == suit) &&
                                        westHand.Any(c => c.Suit == suit) &&
                                        !this.Ignore(eastHand, this.westConsts) &&
                                        !this.Ignore(westHand, this.eastConsts))
                                    {
                                        // make sure calculated tricks are correct
                                        DDS d1 = new DDS(dds.Clone());
                                        string reversed = N + " " + W + " " + S + " " + E;
                                        if (this.verbose)
                                        {
                                            Console.WriteLine("reversed: {0} Command: {1}", reversed, commands);
                                        }
                                        DDS d2 = new DDS(reversed, trump, this.leader);
                                        try
                                        {
                                            d1.Execute(card + " x");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine("Input: {0} Command: {1} x   Message: {2}", format, card, ex.Message);
                                            throw ex;
                                        }
                                        try
                                        {
                                            d2.Execute(card + " x");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine("Input: {0} Command: {1} x  Message: {2}", reversed, card, ex.Message);
                                            throw ex;
                                        }
                                        var nextMoves = this.NextMoves(opposite, card);
                                        result = nextMoves.Max(next => Math.Min(d1.Tricks(next), d2.Tricks(next)));
                                        if (result > 13 || result < 0)
                                        {
                                            Console.WriteLine("Input: {0} Command: {1} x   Result: {2}", format, card, result);
                                            throw new Exception();
                                        }
                                        output.AddTricksWithWeight(card, (byte)result, weight, pos);
                                        //Console.WriteLine("Card: {0} Tricks: {1}", card, result);
                                        d1.Delete();
                                        d2.Delete();
                                    }
                                    else
                                    {
                                        output.AddTricksWithWeight(card, (byte)tricks, weight, pos);
                                        //Console.WriteLine("Card: {0} Tricks: {1}", card, tricks);
                                    }
                                }

                            }
                            dds.Delete();
                            //Console.WriteLine("---------------------------------");
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
        public void BeginEvaluate(Trump trump)
        {
            //this.combinationIndex = new int[noOfCombinations];
            //for (int i = 0; i < noOfCombinations; i++) combinationIndex[i] = i;
            //this.utils.Shuffle(combinationIndex, noOfCombinations, this.random);
            //Console.WriteLine("BeginEvaluation");
            foreach (int i in combinationIndex) this.queue.Enqueue(i);
            this.evaluate = true;
            this.free = this.threads;
            string N = this.northHand.ToString();
            string S = this.southHand.ToString();
            Semaphore semaphore = new Semaphore(0, this.threads);
            // Always evaluate trump first
            if (trump != Trump.No)
            {
                List<string> movesList = legalMoves.ToList();

                for (int i = 0; i < movesList.Count; i++)
                {
                    var suit = "CDHS".IndexOf(movesList[i][1]);
                    if (trump == ((Trump)(suit)))
                    {
                        string importantMove = movesList[i];
                        movesList.RemoveAt(i);
                        movesList.Insert(0, importantMove);
                    }
                }
                legalMoves = movesList.ToArray();
            }
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
                            if (this.maxPlayout > 0 && this.playouts >= this.maxPlayout)
                            {
                                if (this.verbose)
                                {
                                    Console.WriteLine("maxPlayout {0} {1} Use strategy {2}", this.playouts, this.maxPlayout, this.useFusuionStrategy);
                                }
                                // End processing if max playouts is reached
                                this.evaluate = false; continue;
                            }

                            // repeat if failed to dequeue item
                            if (!this.queue.TryDequeue(out int pos))
                            {
                                Console.WriteLine("Dequeue failed");
                                Thread.Sleep(10); continue;
                            }

                            // We could use default weight of 1, but as we include fusion strategy we have 2 calculations for each combination
                            double weight = 0.5f;
                            // recover hands before leads
                            var set = this.combinations[pos];
                            Interlocked.Increment(ref this.examined);
                            Hand westHand = new Hand(set.Select(index => this.remainingCards[index - 1]));
                            Hand eastHand = this.remainingCards.Except(westHand);

                            // Constraints are updated after each current_trick card, so is added after check, before DDS
                            westHand = westHand.Concat(this.westHand);
                            eastHand = eastHand.Concat(this.eastHand);

                            // DDS analysis
                            string E = eastHand.ToString(), W = westHand.ToString();
                            //Console.WriteLine("{0}", E + " " + W, commands);
                            // All hands must have the same number of cards, or it will crash
                            string format = N + " " + E + " " + S + " " + W;
                            if (!(N.Length == E.Length && E.Length == S.Length && S.Length == W.Length))
                            {
                                //Console.WriteLine("Hand ignored N:{0}", N + " " + eastHand + " " + S + " " + westHand);
                                continue;
                            }

                            var sampleEast = new Hand();
                            sampleEast.AddRange(this.eastHandShown);
                            sampleEast.AddRange(eastHand);
                            var sampleWest = new Hand();
                            sampleWest.AddRange(this.westHandShown);
                            sampleWest.AddRange(westHand);
                            double weight1 = sampleEast.getOdds();
                            double weight2 = sampleWest.getOdds();
                            weight = weight1 * weight2;

                            // Based on the remaining cards we should add a weight to this sample
                            //if (westHand.Cards.Contains(new Card("5C")))
                            //{
                            //    weight = 0.25f;
                            //}
                            // exclude impossible hands
                            if (this.Ignore(eastHand, this.eastConsts) ||
                                this.Ignore(westHand, this.westConsts))
                            {
                                //Console.WriteLine("Hand ignored constraints N:{0}", N + " " + eastHand + " " + S + " " + westHand);
                                continue;
                            }

                            Interlocked.Increment(ref this.playouts);
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
                                //Console.WriteLine("{0} Tricks: {1}", card, tricks);
                                try
                                {
                                    output.AddTricksWithWeight(card, (byte)tricks, weight, pos);
                                    //Console.WriteLine("Card: {0} Tricks: {1}", card, tricks);
                                }
                                catch (KeyNotFoundException ex)
                                {
                                    throw new KeyNotFoundException($"Key '{card}' not found in output dictionary.", ex);
                                }
                                if (this.useFusuionStrategy)
                                {
                                    Suit suit = (Suit)"CDHS".IndexOf(card[1]);
                                    // Now we switch the EW hands and calculate the result again
                                    // But only if both hands has a card in the suit current_trick,
                                    // and constraints not are vialoted
                                    // This is the mixed strategy
                                    if (this.remainingCards.Count > 2 && this.current_trick.Count == 0 &&
                                        eastHand.Any(c => c.Suit == suit) &&
                                        westHand.Any(c => c.Suit == suit) &&
                                        !this.Ignore(eastHand, this.westConsts) &&
                                        !this.Ignore(westHand, this.eastConsts))
                                    {
                                        // make sure calculated tricks are correct
                                        DDS d1 = new DDS(dds.Clone());
                                        string reversed = N + " " + W + " " + S + " " + E;
                                        if (this.verbose)
                                        {
                                            Console.WriteLine("reversed: {0} Command: {1}", reversed, commands);
                                        }
                                        DDS d2 = new DDS(reversed, trump, this.leader);
                                        try
                                        {
                                            d1.Execute(card + " x");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine("Input: {0} Command: {1} x   Message: {2}", format, card, ex.Message);
                                            throw ex;
                                        }
                                        try
                                        {
                                            d2.Execute(card + " x");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine("Input: {0} Command: {1} x  Message: {2}", reversed, card, ex.Message);
                                            throw ex;
                                        }
                                        var nextMoves = this.NextMoves(opposite, card);
                                        result = nextMoves.Max(next => Math.Min(d1.Tricks(next), d2.Tricks(next)));
                                        if (result > 13 || result < 0)
                                        {
                                            Console.WriteLine("Input: {0} Command: {1} x   Result: {2}", format, card, result);
                                            throw new Exception();
                                        }
                                        output.AddTricksWithWeight(card, (byte)result, weight, pos);
                                        //Console.WriteLine("Card: {0} Tricks: {1}", card, result);
                                        d1.Delete();
                                        d2.Delete();
                                    }
                                    else
                                    {
                                        output.AddTricksWithWeight(card, (byte)tricks, weight, pos);
                                        //Console.WriteLine("Card: {0} Tricks: {1}", card, tricks);
                                    }
                                }

                            }
                            dds.Delete();
                            //Console.WriteLine("---------------------------------");
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
                    //Console.WriteLine("Discarding {0} {1} {2} {3} {4}", hand, index, count, min, max);
                    return true;
                }
            }
            return false;
        }

        private IEnumerable<string> NextMoves(Player opposite, string lead)
        {
            var hand = opposite == Player.North ? this.northHand : this.southHand;
            Suit suit = (Suit)"CDHS".IndexOf(lead[1]);
            var output = hand.Select(c => c.ToString());
            var moves = hand.Where(c => c.Suit.Equals(suit)).Select(c => c.ToString());
            return moves.Count() > 0 ? moves : output;
        }
    }
}
