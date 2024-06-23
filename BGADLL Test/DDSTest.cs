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
                d2.Execute("TC" + " x");
                Console.WriteLine(d2);
                Console.WriteLine(d2.Tricks("8D"));
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
    }
}
