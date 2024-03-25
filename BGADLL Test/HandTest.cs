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
    public class HandTests
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
        public void TestHandConstraint()
        {
            Constraints con = new Constraints(0, 3, 0, 6, 4, 7, 0, 4, 1, 10);
            Hand hand = "J..QJ97652.".Parse();
            Assert.IsNotNull(hand);
            var diamonds = hand.CardsInSuit(c => c.Suit == Suit.Diamond);
            Assert.AreEqual(7, diamonds);
        }
        [Test]
        public void TestCardList()
        {
            Hand played = new Hand();
            played.Add(new Card("4H"));
            played.Add(new Card("4C"));
            played.Add(new Card("9D"));

            Assert.AreEqual("4H4C9D", played.ListAsString());
        }
    }
}
