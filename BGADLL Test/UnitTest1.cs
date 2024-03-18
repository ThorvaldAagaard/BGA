using NUnit.Framework;
using BGADLL;
using static BGADLL.Macros;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq; // Replace with the namespace of your DLL

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
            pimc = new PIMC();
        }

        // Clean up any objects or resources after each test method
        [TearDown]
        public void TearDown()
        {
            pimc = null;
        }

        // Test methods
        [Test]
        public void TestPlay()
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
                north, south }, oppos, played, new Constraints[2] { east, west }, Macros.Player.North, -1);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            Thread.Sleep(1000);
            Console.WriteLine("LegalMoves: ");
            Console.WriteLine(pimc.LegalMovesToString);
            foreach (string card in pimc.LegalMoves)
            {
                // calculate win probability
                ConcurrentBag<byte> set = pimc.Output[card];
                float count = (float)set.Count;
                int makable = set.Count(t => t >= minTricks);
                float probability = (float)makable / count;
                if (float.IsNaN(probability)) probability = 0f;
                double tricks = count > 0 ? set.Average(t => (int)t) : 0;
            }
            pimc.EndEvaluate();
        }
        // Test methods
        [Test]
        public void TestPlay2()
        {
            Hand north = "5..AT8.AJ843".Parse();
            Hand south = "AT.T3.K43.5".Parse();
            Hand played = ".A5..".Parse();
            int minTricks = 9;
            Hand oppos = "J962.J94.QJ97652.Q97".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 1, 7, 0, 0, 2, 7, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 4, 7, 0, 4, 1, 10);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, played, new Constraints[2] { east, west }, Macros.Player.North, -1);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            Thread.Sleep(1000);
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            foreach (string card in pimc.LegalMoves)
            {
                // calculate win probability
                ConcurrentBag<byte> set = pimc.Output[card];
                float count = (float)set.Count;
                int makable = set.Count(t => t >= minTricks);
                float probability = (float)makable / count;
                if (float.IsNaN(probability)) probability = 0f;
                double tricks = count > 0 ? set.Average(t => (int)t) : 0;
            }
            pimc.EndEvaluate();
        }

        // Test methods
        [Test]
        public void TestPlay3()
        {
            Hand north = ".Q..".Parse();
            Hand south = "...".Parse();
            Hand played = ".4.9.".Parse();
            int minTricks = 9;
            Hand oppos = "4...".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints east = new Constraints(0, 5, 1, 7, 0, 0, 2, 7, 0, 8);
            Constraints west = new Constraints(0, 3, 0, 6, 4, 7, 0, 4, 1, 10);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, played, new Constraints[2] { east, west }, Macros.Player.North, -1);
            Trump trump = Trump.No;
            pimc.BeginEvaluate(trump);
            Thread.Sleep(1000);
            Console.WriteLine("LegalMoves: {0}", pimc.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimc.Combinations);
            Console.WriteLine("Examined {0}", pimc.Examined);
            Console.WriteLine("Playouts {0}", pimc.Playouts);
            foreach (string card in pimc.LegalMoves)
            {
                // calculate win probability
                ConcurrentBag<byte> set = pimc.Output[card];
                float count = (float)set.Count;
                int makable = set.Count(t => t >= minTricks);
                float probability = (float)makable / count;
                if (float.IsNaN(probability)) probability = 0f;
                double tricks = count > 0 ? set.Average(t => (int)t) : 0;
            }
            pimc.EndEvaluate();
        }
    }
}
