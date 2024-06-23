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
    public class BGADefTests
    {

        private PIMCDef pimcdef;
        // Set up any necessary objects or resources before each test method
        [SetUp]
        public void Setup()
        {
            // You can initialize objects or set up resources here
            pimcdef = new PIMCDef(10);
        }

        // Clean up any objects or resources after each test method
        [TearDown]
        public void TearDown()
        {
            pimcdef = null;
        }

        private void displayResults(int minTricks)
        {
            float bestScore = -1f, bestTricks = -1f;
            string bestMove = "";
            foreach (string card in pimcdef.LegalMoves)
            {
                // calculate win probability
                ConcurrentBag<byte> set = pimcdef.Output[card];
                float count = (float)set.Count;
                int beatable = set.Count(t => t >= minTricks);
                float probability = (float)beatable / count;
                if (float.IsNaN(probability)) probability = 0f;
                double tricks = count > 0 ? set.Average(t => (byte)t) : 0;
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
            Console.WriteLine("Best move {0}, Tricks={1:F2}, Probability={2:F4}", bestMove, bestTricks, bestScore);
        }

        [Test]
        public void TestDefence3()
        {
            Hand dummy = "432..7632.83".Parse();
            Hand myhand = "Q976.9875.T9.".Parse();
            Hand played = new Hand();
            played.Add(new Card("JC"));
            played.Add(new Card("6C"));
            bool overdummy = true; // We are east then
            int minTricks = 1;
            Hand oppos = "AKJT.KQJT6.AKQJ85.T752".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints declarerConsts = new Constraints(0, 0, 0, 7, 0, 6, 4, 6, 0, 30);
            Constraints partnerConsts = new Constraints(4, 7, 0, 6, 0, 6, 0, 1, 0, 5);
            pimcdef.SetupEvaluation(new Hand[2] {
                dummy, myhand }, oppos, played, new Constraints[2] { declarerConsts, partnerConsts }, Macros.Player.East, -1, false, overdummy);
            Trump trump = Trump.Spade;
            pimcdef.BeginEvaluate(trump);
            pimcdef.AwaitEvaluation(1000);
            pimcdef.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimcdef.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimcdef.Combinations);
            Console.WriteLine("Examined {0}", pimcdef.Examined);
            Console.WriteLine("Playouts {0}", pimcdef.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestDefence6()
        {
            Hand dummy = "Q..K985.A974".Parse();
            Hand myhand = ".K98.A74.K65".Parse();
            Hand played = new Hand();
            played.Add(new Card("QD"));
            played.Add(new Card("6D"));
            bool overdummy = false; // We are east then
            int minTricks = 2;
            Hand oppos = "T.QJ7532.JT32.QJT83".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints declarerConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints partnerConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimcdef.SetupEvaluation(new Hand[2] {
                dummy, myhand }, oppos, played, new Constraints[2] { declarerConsts, partnerConsts }, Macros.Player.West, -1, false, overdummy);
            Trump trump = Trump.Heart;
            pimcdef.BeginEvaluate(trump);
            pimcdef.AwaitEvaluation(1000);
            pimcdef.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimcdef.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimcdef.Combinations);
            Console.WriteLine("Examined {0}", pimcdef.Examined);
            Console.WriteLine("Playouts {0}", pimcdef.Playouts);
            displayResults(minTricks);
        }

        [Test]
        public void TestDefence2()
        {
            Hand dummy = "KQ987.KJ3.AT6.8".Parse();
            Hand myhand = ".A98.KQ9432.Q742".Parse();
            Hand played = new Hand();
            played.Add(new Card("AC"));
            played.Add(new Card("6C"));
            bool overdummy = true;
            int minTricks = 1;
            Hand oppos = "AJT65432.QT76542.J875.KJT953".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints declarerConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints partnerConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimcdef.SetupEvaluation(new Hand[2] {
                dummy, myhand }, oppos, played, new Constraints[2] { declarerConsts, partnerConsts }, Macros.Player.East, -1, false, overdummy);
            Trump trump = Trump.Diamond;
            pimcdef.BeginEvaluate(trump);
            pimcdef.AwaitEvaluation(1000);
            pimcdef.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimcdef.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimcdef.Combinations);
            Console.WriteLine("Examined {0}", pimcdef.Examined);
            Console.WriteLine("Playouts {0}", pimcdef.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestDefence4()
        {
            Hand dummy = "KQ987.KJ3.AT6.8".Parse();
            Hand myhand = "AJT543.65.J.T95".Parse();
            Hand played = new Hand();
            bool overdummy = false;
            int minTricks = 1;
            Hand oppos = "62.AQT98742.KQ9875432.KQJ74".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints declarerConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            Constraints partnerConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
            pimcdef.SetupEvaluation(new Hand[2] {
                dummy, myhand }, oppos, played, new Constraints[2] { declarerConsts, partnerConsts }, Macros.Player.West, -1, false, overdummy);
            Trump trump = Trump.Diamond;
            pimcdef.BeginEvaluate(trump);
            pimcdef.AwaitEvaluation(1000);
            pimcdef.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimcdef.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimcdef.Combinations);
            Console.WriteLine("Examined {0}", pimcdef.Examined);
            Console.WriteLine("Playouts {0}", pimcdef.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestDefence5()
        {
            Hand dummy = ".J.6.".Parse();
            Hand myhand = "T...9".Parse();
            Hand played = new Hand();
            played.Add(new Card("9H"));
            bool overdummy = false;
            int minTricks = 1;
            Hand oppos = ".T.K.J".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints declarerConsts = new Constraints(0, 13, 0, 13, 0, 13, 0, 0, 0, 37);
            Constraints partnerConsts = new Constraints(0, 13, 0, 0, 0, 0, 0, 13, 0, 37);
            pimcdef.SetupEvaluation(new Hand[2] {
                dummy, myhand }, oppos, played, new Constraints[2] { declarerConsts, partnerConsts }, Macros.Player.West, -1, false, overdummy);
            Trump trump = Trump.Diamond;
            pimcdef.BeginEvaluate(trump);
            pimcdef.AwaitEvaluation(1000);
            pimcdef.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimcdef.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimcdef.Combinations);
            Console.WriteLine("Examined {0}", pimcdef.Examined);
            Console.WriteLine("Playouts {0}", pimcdef.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestDefence7()
        {
            Hand dummy = "98...A7".Parse();
            Hand myhand = "7.6..K2".Parse();
            Hand played = new Hand();
            bool overdummy = false;
            int minTricks = 3;
            Hand oppos = ".9.T3.QJT94".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints declarerConsts = new Constraints(0, 5, 0, 3, 0, 2, 0, 1, 0, 5);
            Constraints partnerConsts = new Constraints(0, 5, 0, 3, 0, 2, 0, 1, 0, 5);
            pimcdef.SetupEvaluation(new Hand[2] {
                dummy, myhand }, oppos, played, new Constraints[2] { declarerConsts, partnerConsts }, Macros.Player.West, -1, false, overdummy);
            Trump trump = Trump.No;
            pimcdef.BeginEvaluate(trump);
            pimcdef.AwaitEvaluation(1000);
            pimcdef.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimcdef.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimcdef.Combinations);
            Console.WriteLine("Examined {0}", pimcdef.Examined);
            Console.WriteLine("Playouts {0}", pimcdef.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestDefence8()
        {
            Hand dummy = ".A84.AJ.9753".Parse();
            Hand myhand = "42.QJT5.5.T2".Parse();
            Hand played = new Hand();
            bool overdummy = true; // We are east then
            int minTricks = 1;
            Hand oppos = ".K97632.QT9763.AKQJ64".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints declarerConsts = new Constraints(5, 7, 1, 4, 0, 5, 0, 0, 10, 17);
            Constraints partnerConsts = new Constraints(0, 2, 2, 7, 1, 7, 0, 0, 0, 7);
            pimcdef.SetupEvaluation(new Hand[2] {
                dummy, myhand }, oppos, played, new Constraints[2] { declarerConsts, partnerConsts }, Macros.Player.East, -1, false, overdummy);
            Trump trump = Trump.Club;
            pimcdef.BeginEvaluate(trump);
            pimcdef.AwaitEvaluation(10000);
            pimcdef.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimcdef.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimcdef.Combinations);
            Console.WriteLine("Examined {0}", pimcdef.Examined);
            Console.WriteLine("Playouts {0}", pimcdef.Playouts);
            displayResults(minTricks);
        }
        [Test]
        public void TestDefence9()
        {
            Hand dummy = "QJ..QJT8.8".Parse();
            Hand myhand = "96..K64.Q3".Parse();
            Hand played = new Hand();
            bool overdummy = true; // We are east then
            int minTricks = 1;
            Hand oppos = "T73.J65.73.KJT642".Parse();

            // Constrant of minimum number of hearts greater than remaining cards
            Constraints declarerConsts = new Constraints(0, 5, 0, 3, 1, 4, 0, 3, 3, 7);
            Constraints partnerConsts = new Constraints(1, 7, 0, 2, 0, 1, 0, 4, 0, 6);
            pimcdef.SetupEvaluation(new Hand[2] {
                dummy, myhand }, oppos, played, new Constraints[2] { declarerConsts, partnerConsts }, Macros.Player.East, -1, false, overdummy);
            Trump trump = Trump.Heart;
            pimcdef.BeginEvaluate(trump);
            pimcdef.AwaitEvaluation(10000);
            pimcdef.EndEvaluate();
            Console.WriteLine("LegalMoves: {0}", pimcdef.LegalMovesToString);
            Console.WriteLine("Combinations {0}", pimcdef.Combinations);
            Console.WriteLine("Examined {0}", pimcdef.Examined);
            Console.WriteLine("Playouts {0}", pimcdef.Playouts);
            displayResults(minTricks);
        }
    }
}
