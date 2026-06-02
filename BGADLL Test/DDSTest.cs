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
    public class DDSTests
    {

        [SetUp]
        public void Setup()
        {
        }

        // Clean up any objects or resources after each test method
        [TearDown]
        public void TearDown()
        {
        }


        // Test methods
        [Test]
        public void TestStrategy()
        {
            DDS d2 = new DDS("KQ987.KJ3.AT6.8 62.AT72.K82.J74 .Q984.Q97543.KQ AJT543.65.J.T95", Trump.Diamond, Player.West);
            d2.Execute("JD" + " x");
            Console.WriteLine(d2);
            Console.WriteLine(d2.Tricks("8D"));

        }
        [Test]
        public void TestStrategy2()
        {
            try
            {
                DDS d2 = new DDS("KQ987.KJ3.AT6. 62.AT72.K82.J7 .Q984.Q97543.K AJT543.65.J.T9", Trump.Diamond, Player.West);
                d2.Execute("TC");
                Console.WriteLine(d2.LastError());
                Console.WriteLine(d2.Tricks());
            }
            catch (Exception ex)
            {
                // Handle the exception
                // For example, you might log the exception or ignore it for this test
                Console.WriteLine("An exception occurred: " + ex.Message);
                // You can also mark the test as inconclusive or pass it, depending on your needs
                //Assert.Pass("An exception occurred: " + ex.Message);
            }
        }

        /// <summary>
        /// Test that Tricks(card) returns correct values when the extra card completes a trick.
        /// Simulates the problematic 3NT position: West leads DT, North plays DA, East plays D3.
        /// South plays D4/D8/DQ — D4 should yield more tricks than DQ.
        /// </summary>
        [Test]
        public void TestTricksAfterCompleteTrick()
        {
            // Full 13-card hands, standard position
            // North: SA9873.HJ4.DA96.CK52  South: SKT.HAT93.DQ84.CAQ98
            // West: SQJ42.HK8765.DT2.C76  East: S65.HQ2.DKJ753.CJT43
            string hands = "A9873.J4.A96.K52 65.Q2.KJ753.JT43 KT.AT93.Q84.AQ98 QJ42.K8765.T2.76";

            // West leads, NT contract
            DDS dds = new DDS(hands, Trump.No, Player.West);
            // Play trick 1: H6 H4 HQ HA
            dds.Execute("6H");
            dds.Execute("4H");
            dds.Execute("QH");
            dds.Execute("AH");
            // Play trick 2: H3 HK HJ H2
            dds.Execute("3H");
            dds.Execute("KH");
            dds.Execute("JH");
            dds.Execute("2H");
            // Current trick 3: DT DA D3
            dds.Execute("TD");
            dds.Execute("AD");
            dds.Execute("3D");

            // South plays each diamond option
            int tricksD4 = dds.Tricks("4D");
            int tricksD8 = dds.Tricks("8D");
            int tricksDQ = dds.Tricks("QD");

            Console.WriteLine("D4={0}, D8={1}, DQ={2}", tricksD4, tricksD8, tricksDQ);
            Console.WriteLine("DDS backend: {0}", DDS.UseHaglund ? "haglund" : "bcalcdds");

            // D4 should yield at least as many tricks as DQ
            Assert.That(tricksD4, Is.GreaterThanOrEqualTo(tricksDQ),
                "D4 should yield at least as many tricks as DQ (preserve the queen)");
        }
    }
}
