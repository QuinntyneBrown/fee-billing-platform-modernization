using System;
using FeeBilling.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Core
{
    [TestClass]
    public class QuarterHelperTests
    {
        [TestMethod]
        public void GetQuarterStart_Q3_ReturnsJuly1()
        {
            Assert.AreEqual(new DateTime(2016, 7, 1), QuarterHelper.GetQuarterStart(new DateTime(2016, 9, 30)));
        }

        [TestMethod]
        public void GetQuarterEnd_Q3_ReturnsSept30()
        {
            Assert.AreEqual(new DateTime(2016, 9, 30), QuarterHelper.GetQuarterEnd(new DateTime(2016, 8, 15)));
        }

        [TestMethod]
        public void IsQuarterEnd_Sept30_True()
        {
            Assert.IsTrue(QuarterHelper.IsQuarterEnd(new DateTime(2016, 9, 30)));
        }

        [TestMethod]
        public void IsQuarterEnd_Sept29_False()
        {
            Assert.IsFalse(QuarterHelper.IsQuarterEnd(new DateTime(2016, 9, 29)));
        }

        [TestMethod]
        public void DaysInQuarter_Q3_Is92()
        {
            Assert.AreEqual(92, QuarterHelper.DaysInQuarter(new DateTime(2016, 9, 30)));
        }

        [TestMethod]
        public void DaysInQuarter_Q1LeapYear_Is91()
        {
            Assert.AreEqual(91, QuarterHelper.DaysInQuarter(new DateTime(2016, 3, 31)));
        }

        [TestMethod, Ignore("Depends on today's date")]
        public void LastQuarterEnd_ReturnsJune30()
        {
            Assert.AreEqual(new DateTime(2016, 6, 30), QuarterHelper.LastQuarterEnd());
        }
    }
}
