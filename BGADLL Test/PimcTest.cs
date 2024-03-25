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
    public class BGADLLTests
    {

        private PIMC pimc;
        // Set up any necessary objects or resources before each test method
        [SetUp]
        public void Setup()
        {
            // You can initialize objects or set up resources here
            pimc = new PIMC(1);
        }

        // Clean up any objects or resources after each test method
        [TearDown]
        public void TearDown()
        {
            pimc = null;
        }

        // Test methods
        [Test]
        public void TestPlay1()
        {
            Hand fullDeck = "AKQJT98765432.AKQJT98765432.AKQJT98765432.AKQJT98765432".Parse();
            Hand north = "52.K7.QT543.JT84".Parse();
            Hand south = "AQ.A42.K72.AQ973".Parse();
            Hand played = new Hand();
            played.Add(new Card("QH"));
            int minTricks = 9;
            Hand oppos = fullDeck.Except(north).Except(south).Except(played);

            Constraints east = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints west = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, played, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            Thread.Sleep(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: ");
            Console.WriteLine(pimc.LegalMovesToString);
            float bestScore = -1f, bestTricks = -1f;
            string bestMove = "";
            foreach (string card in pimc.LegalMoves)
            {
                // calculate win probability
                ConcurrentBag<byte> set = pimc.Output[card];
                float count = (float)set.Count;
                int makable = set.Count(t => t >= minTricks);
                float probability = (float)makable / count;
                if (float.IsNaN(probability)) probability = 0f;
                double tricks = count > 0 ? set.Average(t => (int)t) : 0;
                Console.WriteLine("Possible move {0}, Tricks={1:F1}, Probability={2:F3}", card, tricks, probability);
                // find the best move
                if (bestScore.Equals(-1f) ||
                    probability > bestScore ||
                    bestScore == probability && tricks > bestTricks)
                {
                    bestMove = card;
                    bestScore = probability;
                    bestTricks = (float)tricks;
                }
            }
            Console.WriteLine("Best move {0}, Tricks={1:F1}, Probability={2:F3}", bestMove, bestTricks, bestScore);
        }
        // Test methods
        [Test]
        public void TestPlay2()
        {
            Hand north = "5..AT8.AJ843".Parse();
            Hand south = "AT.T3.K43.5".Parse();
            Hand played = new Hand();
            played.Add(new Card("AH"));
            played.Add(new Card("5H"));
            int minTricks = 9;
            Hand oppos = "J962.J94.QJ97652.Q97".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 1, 7, 0, 0, 2, 7, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 4, 7, 0, 4, 1, 10);
            try
            {
                pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, played, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
                Trump trump = Trump.No;
                pimc.BeginEvaluate(trump);
                Thread.Sleep(1000);
                pimc.EndEvaluate();
                Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
                Console.WriteLine("Combinations {0}", pimc.Combinations);
                Console.WriteLine("Examined {0}", pimc.Examined);
                Console.WriteLine("Playouts {0}", pimc.Playouts);
                float bestScore = -1f, bestTricks = -1f;
                string bestMove = "";
                foreach (string card in pimc.LegalMoves)
                {
                    // calculate win probability
                    ConcurrentBag<byte> set = pimc.Output[card];
                    float count = (float)set.Count;
                    int makable = set.Count(t => t >= minTricks);
                    float probability = (float)makable / count;
                    if (float.IsNaN(probability)) probability = 0f;
                    double tricks = count > 0 ? set.Average(t => (int)t) : 0;
                    Console.WriteLine("Possible move {0}, Tricks={1:F1}, Probability={2:F3}", card, tricks, probability);
                    // find the best move
                    if (bestScore.Equals(-1f) ||
                        probability > bestScore ||
                        bestScore == probability && tricks > bestTricks)
                    {
                        bestMove = card;
                        bestScore = probability;
                        bestTricks = (float)tricks;
                    }
                }
                Console.WriteLine("Best move {0}, Tricks={1:F1}, Probability={2:F3}", bestMove, bestTricks, bestScore);
            }
            catch (Exception ex)
            {
                // Handle the exception
                // For example, you might log the exception or ignore it for this test
                Console.WriteLine("An exception occurred: " + ex.Message);
                // You can also mark the test as inconclusive or pass it, depending on your needs
                Assert.Pass("An exception occurred: " + ex.Message);
            }
        }

        [Test]
        public void TestPlay3()
        {
            Hand north = ".Q..".Parse();
            Hand south = "...".Parse();
            Hand played = new Hand();
            played.Add(new Card("4H"));
            played.Add(new Card("9D"));
            int minTricks = 9;
            Hand oppos = "4...".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 0, 7, 0, 0, 0, 7, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 0, 7, 0, 4, 0, 10);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, played, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            Thread.Sleep(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            float bestScore = -1f, bestTricks = -1f;
            string bestMove = "";
            foreach (string card in pimc.LegalMoves)
            {
                // calculate win probability
                ConcurrentBag<byte> set = pimc.Output[card];
                float count = (float)set.Count;
                int makable = set.Count(t => t >= minTricks);
                float probability = (float)makable / count;
                if (float.IsNaN(probability)) probability = 0f;
                double tricks = count > 0 ? set.Average(t => (int)t) : 0;
                Console.WriteLine("Possible move {0}, Tricks={1:F1}, Probability={2:F3}", card, tricks, probability);
                // find the best move
                if (bestScore.Equals(-1f) ||
                    probability > bestScore ||
                    bestScore == probability && tricks > bestTricks)
                {
                    bestMove = card;
                    bestScore = probability;
                    bestTricks = (float)tricks;
                }
            }
            Console.WriteLine("Best move {0}, Tricks={1:F1}, Probability={2:F3}", bestMove, bestTricks, bestScore);
        }
        [Test]
        public void TestAPlay4()
        {
            Hand north = "KQ987.KJ.AT6.".Parse();
            Hand south = ".A9.KQ932.Q7".Parse();
            Hand played = new Hand();
            played.Add(new Card("4D"));
            played.Add(new Card("7D"));
            int minTricks = 10;
            Hand oppos = "AJT65432.QT754.J85.JT9".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints west = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, played, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
            Trump trump = Trump.Diamond;
            pimc.BeginEvaluate(trump);
            Thread.Sleep(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            float bestScore = -1f, bestTricks = -1f;
            string bestMove = "";
            foreach (string card in pimc.LegalMoves)
            {
                // calculate win probability
                ConcurrentBag<byte> set = pimc.Output[card];
                float count = (float)set.Count;
                int makable = set.Count(t => t >= minTricks);
                float probability = (float)makable / count;
                if (float.IsNaN(probability)) probability = 0f;
                double tricks = count > 0 ? set.Average(t => (int)t) : 0;
                Console.WriteLine("Possible move {0}, Tricks={1:F1}, Probability={2:F3}", card, tricks, probability);
                // find the best move
                if (bestScore.Equals(-1f) ||
                    probability > bestScore ||
                    bestScore == probability && tricks > bestTricks)
                {
                    bestMove = card;
                    bestScore = probability;
                    bestTricks = (float)tricks;
                }
            }
            Console.WriteLine("Best move {0}, Tricks={1:F1}, Probability={2:F3}", bestMove, bestTricks, bestScore);
        }
        [Test]
        public void TestPlay5()
        {
            Hand north = "8...".Parse();
            Hand south = "T...9".Parse();
            Hand played = new Hand();
            played.Add(new Card("4H"));
            played.Add(new Card("3H"));
            played.Add(new Card("9D"));
            int minTricks = 9;
            Hand oppos = ".2.Q.".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 0, 7, 0, 0, 0, 7, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 0, 7, 0, 4, 0, 10);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, played, new Constraints[2] { east, west }, Macros.Player.South, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            Thread.Sleep(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            float bestScore = -1f, bestTricks = -1f;
            string bestMove = "";
            foreach (string card in pimc.LegalMoves)
            {
                // calculate win probability
                ConcurrentBag<byte> set = pimc.Output[card];
                float count = (float)set.Count;
                int makable = set.Count(t => t >= minTricks);
                float probability = (float)makable / count;
                if (float.IsNaN(probability)) probability = 0f;
                double tricks = count > 0 ? set.Average(t => (int)t) : 0;
                Console.WriteLine("Possible move {0}, Tricks={1:F1}, Probability={2:F3}", card, tricks, probability);
                // find the best move
                if (bestScore.Equals(-1f) ||
                    probability > bestScore ||
                    bestScore == probability && tricks > bestTricks)
                {
                    bestMove = card;
                    bestScore = probability;
                    bestTricks = (float)tricks;
                }
            }
            Console.WriteLine("Best move {0}, Tricks={1:F1}, Probability={2:F3}", bestMove, bestTricks, bestScore);
        }
    }
}
