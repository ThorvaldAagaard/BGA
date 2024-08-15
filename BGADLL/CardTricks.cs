using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Output = System.Collections.Generic.Dictionary<string, System.Collections.Concurrent.ConcurrentBag<(byte tricks, double weight)>>;
using System.Linq;


namespace BGADLL
{
    public class CardTricks
    {
        private Output output;

        public CardTricks()
        {
            output = new Output();
        }

        public void Clear()
        {
            output.Clear();
        }

        public void Add(string card)
        {
            if (!output.ContainsKey(card))
            {
                output[card] = new ConcurrentBag<(byte tricks, double weight)>();
            }
        }

        // Property to get the number of cards in the output dictionary
        public int Count
        {
            get { return output.Count; }
        }

        public void AddTricksWithWeight(string card, byte tricks, double weight)
        {
            if (!output.ContainsKey(card))
            {
                output[card] = new ConcurrentBag<(byte tricks, double weight)>();
            }
            output[card].Add((tricks, weight));
        }

        public double CalculateWeightedTricks(string card)
        {
            if (!output.ContainsKey(card))
            {
                return 0;
            }

            double totalWeightedTricks = 0;
            double totalWeight = 0;

            foreach (var entry in output[card])
            {
                totalWeightedTricks += entry.tricks * entry.weight;
                totalWeight += entry.weight;
            }

            return totalWeight > 0 ? totalWeightedTricks / totalWeight : 0;
        }

        public IEnumerable<(byte tricks, double weight)> GetTricksWithWeights(string card)
        {
            //Console.WriteLine("GetTricksWithWeights {0}", card);
            if (output.ContainsKey(card))
            {
                return output[card];
            }

            return Enumerable.Empty<(byte tricks, double weight)>();
        }
    }
}
