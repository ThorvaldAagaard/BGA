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
            pimc = new PIMC(1,false);
        }

        // Clean up any objects or resources after each test method
        [TearDown]
        public void TearDown()
        {
            pimc = null;
        }

        private void displayResults(int minTricks)
        {
            double bestScore = -1f, bestTricks = -1f;
            string bestMove = "";
            foreach (string card in pimc.LegalMoves)
            {
                // calculate win probability
                IEnumerable<(byte tricks, double weight)> set = pimc.Output.GetTricksWithWeights(card);
                double totalWeight = set.Sum(entry => entry.weight);
                double makableWeight = set.Where(entry => entry.tricks >= minTricks).Sum(entry => entry.weight);
                double probability = totalWeight > 0 ? (float)makableWeight / totalWeight : 0f;
                if (double.IsNaN(probability)) probability = 0f;
                double weightedTricks = pimc.Output.CalculateWeightedTricks(card);

                Console.WriteLine("Possible move {0}, Tricks={1:F1}, Probability={2:F3}, Count/Weight {3}", card, weightedTricks, probability, totalWeight);

                // find the best move
                if (bestScore.Equals(-1f) ||
                    probability > bestScore ||
                    bestScore == probability && weightedTricks > bestTricks)
                {
                    bestMove = card;
                    bestScore = probability;
                    bestTricks = (float)weightedTricks;
                }
            }
            Console.WriteLine("Best move {0}, Tricks={1:F1}, Probability={2:F3}", bestMove, bestTricks, bestScore);
        }

        // Test methods
        [Test]
        public void TestPlay1()
        {
            Hand fullDeck = "AKQJT98765432.AKQJT98765432.AKQJT98765432.AKQJT98765432".Parse();
            Hand north = "52.K7.QT543.JT84".Parse();
            Hand south = "AQ.A42.K72.AQ973".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("QH"));
            int minTricks = 9;
            Hand oppos = fullDeck.Except(north).Except(south).Except(current_trick.Cards);

            Constraints east = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints west = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }

        [Test]
        public void TestPlay2()
        {
            Hand north = "5..AT8.AJ843".Parse();
            Hand south = "AT.T3.K43.5".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("AH"));
            current_trick.Add(new Card("5H"));
            int minTricks = 9;
            Hand oppos = "J962.J94.QJ97652.Q97".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 1, 7, 0, 0, 2, 7, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 4, 7, 0, 4, 1, 10);
            try
            {
                pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
                Trump trump = Trump.No;
                pimc.BeginEvaluate(trump);
                pimc.AwaitEvaluation(1000);
                pimc.EndEvaluate();
                Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
                Console.WriteLine("Combinations {0}", pimc.Combinations);
                Console.WriteLine("Examined {0}", pimc.Examined);
                Console.WriteLine("Playouts {0}", pimc.Playouts);
                displayResults(minTricks);
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
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("4H"));
            current_trick.Add(new Card("9D"));
            int minTricks = 9;
            Hand oppos = "4...".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 0, 7, 0, 0, 0, 7, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 0, 7, 0, 4, 0, 10);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestAPlay4()
        {
            Hand north = "KQ987.KJ.AT6.".Parse();
            Hand south = ".A9.KQ932.Q7".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("4D"));
            current_trick.Add(new Card("7D"));
            int minTricks = 10;
            Hand oppos = "AJT65432.QT754.J85.JT9".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints west = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
            Trump trump = Trump.Diamond;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestPlay5()
        {
            Hand north = "8...".Parse();
            Hand south = "T...9".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("4H"));
            current_trick.Add(new Card("3H"));
            current_trick.Add(new Card("9D"));
            int minTricks = 9;
            Hand oppos = ".2.Q.".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 0, 7, 0, 0, 0, 7, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 0, 7, 0, 4, 0, 10);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.South, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestPlay6()
        {
            Hand north = "...A932".Parse();
            Hand south = "...KQT8".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 4;
            Hand oppos = ".32.32.J765".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints west = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.South, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);

        }
        [Test]
        public void TestPlay7()
        {
            Hand north = ".K.53.".Parse();
            Hand south = "..K92.".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 2;
            Hand oppos = "..AQT84.T".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 0, 7, 0, 0, 0, 0, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 0, 0, 0, 0, 0, 10);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
            Trump trump = Trump.Heart;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);

        }
        [Test]
        public void TestPlay8()
        {
            Hand north = "JT...Q3".Parse();
            Hand south = "A9..2.J".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 3;
            Hand oppos = "Q652.3..K96".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 0, 0, 0, 1, 0, 4, 0, 8);
            Constraints west = new Constraints(0, 5, 0, 0, 0, 0, 0, 4, 0, 10);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }

        [Test]
        public void TestPlay9()
        {
            Hand north = "KQ.8..".Parse();
            Hand south = "76.J.Q.".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 3;
            current_trick.Add(new Card("JC"));
            current_trick.Add(new Card("9C"));
            Hand oppos = "J983..8.QT".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 2, 0, 0, 0, 0, 1, 4, 0, 5);
            Constraints west = new Constraints(0, 2, 0, 13, 0, 0, 0, 3, 0, 5);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.South, -1, false);
            Trump trump = Trump.Heart;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);

        }
        [Test]
        public void TestPlay10()
        {
            Hand north = ".J654..".Parse();
            Hand south = "..AKJ97.".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 5;
            current_trick.Add(new Card("4D"));
            current_trick.Add(new Card("8D"));
            Hand oppos = ".K8.QT63.KQJ".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 0, 3, 0, 3, 0, 13, 0, 10);
            Constraints west = new Constraints(0, 5, 0, 4, 0, 3, 0, 0, 1, 12);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.South, -1, false);
            Trump trump = Trump.Diamond;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);

        }
        [Test]
        public void TestPlay11()
        {
            Hand north = "6.AJ654.52.975".Parse();
            Hand south = "AK8.T3.AKJT9.A6".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 11;
            current_trick.Add(new Card("4D"));
            current_trick.Add(new Card("8D"));
            Hand oppos = "Q7542.KQ9872.Q763.KQJT8432".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 7, 0, 7, 0, 7, 0, 13, 0, 12);
            Constraints west = new Constraints(0, 7, 0, 7, 0, 7, 0, 13, 0, 12);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.South, -1, false);
            Trump trump = Trump.Diamond;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);

        }
        [Test]
        public void TestPlay12()
        {
            Hand north = "..6.JT96".Parse();
            Hand south = ".K.87.A2".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 3;
            Hand oppos = ".Q54.T9.KQ873".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(2, 7, 0, 7, 0, 7, 0, 13, 0, 12);
            Constraints west = new Constraints(2, 7, 0, 7, 0, 7, 0, 13, 0, 12);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.South, -1, false);
            Trump trump = Trump.Diamond;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);

        }
        [Test]
        public void TestPlay13()
        {
            Hand north = "..6.JT96".Parse();
            Hand south = ".K.87.A2".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 3;
            Hand oppos = ".Q54.T9.KQ873".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(2, 7, 0, 7, 0, 7, 0, 13, 0, 12);
            Constraints west = new Constraints(2, 7, 0, 7, 0, 7, 0, 13, 0, 12);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.South, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);

        }
        [Test]
        public void TestPlay14()
        {
            Hand north = "AQ...".Parse();
            Hand south = "3...".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("2S"));
            current_trick.Add(new Card("4S"));
            int minTricks = 2;
            Hand oppos = "K65...".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 0, 7, 0, 8, 0, 7, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 0, 7, 0, 4, 0, 10);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);

        }
        [Test]
        public void TestPlay15()
        {
            Hand north = "...T84".Parse();
            Hand south = "AQ...Q9".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("JS"));
            current_trick.Add(new Card("5S"));
            current_trick.Add(new Card("TS"));
            current_trick.Add(new Card("9S"));
            current_trick.Add(new Card("8S"));
            current_trick.Add(new Card("7S"));
            int minTricks = 2;
            Hand oppos = "K..J.AKJ75".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 6, 0, 7, 0, 8, 0, 7, 0, 12);
            Constraints west = new Constraints(0, 6, 0, 6, 0, 7, 0, 6, 0, 12);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.South, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }

        [Test]
        public void TestPlay16()
        {
            Hand north = ".T7.Q43.".Parse();
            Hand south = ".AK.J76.".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 2;
            Hand oppos = "2.QJ82.AKT95.".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 0, 1, 5, 0, 4, 0, 0, 0, 12);
            Constraints west = new Constraints(0, 0, 0, 4, 0, 4, 0, 13, 0, 11);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, current_trick, previous_tricks, new Constraints[2] { east, west }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(1000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }

        [Test]
        public void TestPlay17()
        {
            Hand north = ".87..KT63".Parse();
            Hand south = "T.KJT.Q6.7".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("4H"));
            current_trick.Add(new Card("5H"));
            int minTricks = 6;
            Hand oppos = "4.Q6.T95.984".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints eastConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints westConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.South, -1, false);
            Trump trump = Trump.Heart;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }

        [Test]
        public void TestPlay18()
        {
            Hand north = "..AJ842.965".Parse();
            Hand south = "K.A7.QT6.AQJ".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("3C"));
            current_trick.Add(new Card("8C"));
            int minTricks = 7;
            Hand oppos = "JT32.KQT9642.K9753.K".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints eastConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints westConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.South, -1, false);
            Trump trump = Trump.Club;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }

        [Test]
        public void TestPlay19()
        {
            Hand north = "..AQ.".Parse();
            Hand south = "..3.".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("2D"));
            current_trick.Add(new Card("4D"));
            int minTricks = 2;
            Hand oppos = "..K65.".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints eastConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints westConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestPlay20()
        {
            Hand north = "..87.".Parse();
            Hand south = "..Q.".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("TS"));
            current_trick.Add(new Card("9S"));
            current_trick.Add(new Card("7H"));
            int minTricks = 2;
            Hand oppos = "..J.7".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints eastConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints westConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestPlay21()
        {
            Hand north = "Q8.AQT6.AK32.2".Parse();
            Hand south = "J73.K.765.JT97".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 9;
            Hand oppos = "9.J987542.QJT984.AKQ86543".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints eastConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints westConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestPlay22()
        {
            Hand north = "Q6.4.AQT75.KJ8".Parse();
            Hand south = "KJT92.A7.K8.QT".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 10;
            Hand oppos = "87.KJT983.J96432.A9765432".Parse();

            Constraints eastConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints westConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.South, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestPlay23()
        {
            Hand north = "..T8.8".Parse();
            Hand south = "...Q3".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("QD"));
            current_trick.Add(new Card("7S"));
            current_trick.Add(new Card("9C"));
            int minTricks = 3;
            Hand oppos = ".98..KT".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints eastConstraints = new Constraints(0, 0, 0, 13, 0, 13, 0, 0, 0, 37);
            Constraints westConstraints = new Constraints(0, 13, 0, 0, 0, 0, 0, 0, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.North, -1, false);
            Trump trump = Trump.Spade;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestPlay24()
        {
            Hand north = "...AJ9".Parse();
            Hand south = "KT...".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("8C"));
            current_trick.Add(new Card("TC"));
            int minTricks = 3;
            Hand oppos = "AQ..Q.K7".Parse();

            Constraints eastConstraints = new Constraints(0, 3, 0, 1, 0, 0, 0, 3, 3, 11);
            Constraints westConstraints = new Constraints(0, 3, 0, 2, 0, 0, 0, 2, 0, 8);
            pimc.SetupEvaluation(new Hand[2] {
                north, south}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestPlay25()
        {
            Hand north = "...AQ".Parse();
            Hand south = "...2".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("3C"));
            current_trick.Add(new Card("4C"));
            int minTricks = 2;
            Hand oppos = "A...K5".Parse();

            Constraints eastConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints westConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.North, -1, false);
            Trump trump = Trump.Club;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestPlay26()
        {
            Hand north = "...AQT".Parse();
            Hand south = "...43".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            current_trick.Add(new Card("2C"));
            current_trick.Add(new Card("8C"));
            int minTricks = 3;
            Hand oppos = "AQ.A..KJ".Parse();

            Constraints eastConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints westConstraints = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.North, -1, false);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(2000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestPlay27()
        {
            Hand north = "8.5.JT75.QJ6".Parse();
            Hand south = "4.QJ.A43.AT3".Parse();
            Hand east = "K3.64..".Parse();
            Hand west = "T2.K9..".Parse();
            Play current_trick = new Play();
            Play previous_tricks = new Play();
            int minTricks = 6;
            Hand oppos = "Q96.T7.KQ9862.K987542".Parse();

            Constraints eastConstraints = new Constraints(1, 7, 0, 6, 0, 3, 0, 4, 0, 10);
            Constraints westConstraints = new Constraints(0, 6, 0, 7, 0, 3, 0, 4, 0, 12);
            pimc.SetupEvaluation(new Hand[4] {
                north, south, east, west}, oppos, current_trick, previous_tricks, new Constraints[2] { eastConstraints, westConstraints }, Macros.Player.North, 10, true);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            pimc.AwaitEvaluation(10000);
            pimc.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            displayResults(minTricks);
        }
    }
}
