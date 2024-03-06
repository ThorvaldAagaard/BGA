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

            Details east = new Details(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Details west = new Details(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimc.SetupEvaluation(new Hand[2] {
                north, south }, oppos, played, new Details[2] { east, west }, Macros.Player.North);
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
    }
}
