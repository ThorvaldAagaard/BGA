using NUnit.Framework;
using BGADLL;
using static BGADLL.Macros;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Drawing.Printing; // Replace with the namespace of your DLL

namespace BGA.Tests // Create a separate namespace for your tests
{

    [TestFixture]
    public class ConstraintsTests
    {

        // Set up any necessary objects or resources before each test method
        [SetUp]
        public void Setup()
        {
        }

        // Clean up any objects or resources after each test method
        [TearDown]
        public void TearDown()
        {
        }

        public List<byte[]> LoadCombinations(int n, int k)
        {
            List<byte[]> combinations = new List<byte[]>();
            Utils utils = new Utils();
            int noOfCombinations = utils.Count(n, k);
            int[] array = new int[noOfCombinations];
            // Create all combinations
            foreach (byte[] series in utils.Generate(n, k))
                combinations.Add(series.ToArray());

            for (int i = 0; i < noOfCombinations; i++) array[i] = i;
            return combinations;
        }

        private bool Ignore(Hand hand, Constraints constraints)
        {
            int minHcp = constraints.MinHCP;
            int maxHcp = constraints.MaxHCP;
            int hcp = hand.Sum(c => c.HCP());
            if (hcp < minHcp || hcp > maxHcp)
            {
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



        // Test methods
        [Test]
        public void TestHandConstraint()
        {
            Constraints con = new Constraints(0, 3, 0, 6, 4, 7, 0, 4, 1, 10);
            Hand hand = "J..QJ97652.".Parse();
            Console.WriteLine(Ignore(hand, con));
        }

        [Test]
        public void TestConstraints()
        {
            Hand north = "5..AT8.AJ843".Parse();
            Hand south = "AT.T3.K43.5".Parse();
            Hand played = new Hand();
            played.Add(new Card("AH"));
            played.Add(new Card("5H"));
            Hand remainingCards = "J962.J94.QJ97652.Q97".Parse();

            Constraints east = new Constraints(0, 5, 1, 7, 0, 0, 2, 7, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 4, 7, 0, 4, 1, 10);
            var combinations = LoadCombinations(7, 3);
            for (int i = 0; i < combinations.Count; i++)
            {
                var set = combinations[i];
                Hand westHand = new Hand(set.Select(index => remainingCards[index - 1]));
                Hand eastHand = remainingCards.Except(westHand);
                var westOK = !Ignore(westHand, west);
                var eastOK = !Ignore(eastHand, east);
                // exclude impossible hands
                if (eastOK && westOK)
                {
                    Console.WriteLine("Hand found: {0}", eastHand + " " + westHand);
                    continue;
                }
                else
                {
                    Console.WriteLine("Hand found: {0} {1} {2} {3} ", eastHand, eastOK, westHand, westOK);
                }
            }

            // Constraints are updated after each played card, so is added after check, before DDS
        }

        [Test]
        public void TestConstraintsHCP()
        {
            Hand north = "5..AT8.AJ843".Parse();
            Hand south = "AT.T3.K43.5".Parse();
            Hand played = new Hand();
            played.Add(new Card("AH"));
            played.Add(new Card("5H"));
            Hand remainingCards = "J962.J94.QJ97652.Q97".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 5, 7, 0, 0, 2, 7, 8, 18);
            Constraints west = new Constraints(0, 3, 5, 6, 4, 7, 0, 4, 10, 12);
            var combinations = LoadCombinations(17, 8);
            for (int i = 0; i < combinations.Count; i++)
            {
                var set = combinations[i];
                Hand westHand = new Hand(set.Select(index => remainingCards[index - 1]));
                Hand eastHand = remainingCards.Except(westHand);
                var westOK = !Ignore(westHand, west);
                var eastOK = !Ignore(eastHand, east);
                // exclude impossible hands
                if (eastOK && westOK)
                {
                    Console.WriteLine("Hand found: {0}", eastHand + " " + westHand);
                    continue;
                }
                else
                {
                    Console.WriteLine("Hand found: {0} {1} {2} {3} ", eastHand, eastOK, westHand, westOK);
                }
            }

            // Constraints are updated after each played card, so is added after check, before DDS
        }
    }
}
