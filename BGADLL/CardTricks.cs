using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Output = System.Collections.Generic.Dictionary<string, System.Collections.Concurrent.ConcurrentBag<(byte tricks, double weight, int combinationId)>>;
using System.Linq;
using System.Security.Cryptography;


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
                output[card] = new ConcurrentBag<(byte tricks, double weight, int combinationId)>();
            }
        }

        // Property to get the number of cards in the output dictionary
        public int Count
        {
            get { return output.Count; }
        }

        public void AddTricksWithWeight(string card, byte tricks, double weight, int combinationId)
        {
            if (!output.ContainsKey(card))
            {
                output[card] = new ConcurrentBag<(byte tricks, double weight, int combinationId)>();
            }
            //Console.WriteLine("{0} {1} {2}", card, tricks, weight);
            output[card].Add((tricks, weight, combinationId));
        }

        public double CalculateWeightedTricks(string card)
        {
            if (!output.ContainsKey(card))
            {
                return 0;
            }

            double totalWeightedTricks = 0;
            double totalWeight = 0;
            //int i = 0;

            foreach (var entry in output[card])
            {
                totalWeightedTricks += entry.tricks * entry.weight;
                totalWeight += entry.weight;
                //i++;
            }
            //Console.WriteLine("{0} {1} {2} {3}", card, i, totalWeightedTricks, totalWeight);
            return totalWeight > 0 ? totalWeightedTricks / totalWeight : 0;
        }

        public IEnumerable<(byte tricks, double weight, int combinationId)> GetTricksWithWeights(string card)
        {
            //Console.WriteLine("GetTricksWithWeights {0}", card);
            if (output.ContainsKey(card))
            {
                return output[card];
            }

            return Enumerable.Empty<(byte tricks, double weight, int combinationId)>();
        }
        // Method to sort results
        public void SortResults()
        {
            // Take a snapshot of the keys to avoid modifying the collection during iteration
            var keysSnapshot = output.Keys.ToList();

            foreach (var key in keysSnapshot)
            {
                // Extract and sort the items
                var sortedList = output[key]
                    .OrderBy(x => x.combinationId) // Primary sort by combinationId
                    .ThenBy(x => x.tricks)   // Secondary sort by tricks
                    .ThenBy(x => x.weight)   // Tertiary sort by weight
                    .ToList();

                // Replace the ConcurrentBag with a new one containing the sorted data
                output[key] = new ConcurrentBag<(byte tricks, double weight, int combinationId)>(sortedList);
            }
        }
    }
}
