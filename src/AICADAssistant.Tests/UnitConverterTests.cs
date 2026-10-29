using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AICADAssistant.Core.CAD;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class UnitConverterTests
    {
        [TestMethod]
        public void FromMillimeters_ConvertsCorrectly()
        {
            double mm = 100.0;
            double meters = UnitConverter.FromMillimeters(mm);
            Assert.AreEqual(0.1, meters, 1e-10);
        }

        [TestMethod]
        public void ToMillimeters_ConvertsCorrectly()
        {
            double meters = 0.05;
            double mm = UnitConverter.ToMillimeters(meters);
            Assert.AreEqual(50.0, mm, 1e-10);
        }

        [TestMethod]
        public void ConvertToInternal_MM_ReturnsMeters()
        {
            double result = UnitConverter.ConvertToInternal(1000.0, "mm");
            Assert.AreEqual(1.0, result, 1e-10);
        }

        [TestMethod]
        public void ConvertToInternal_Inch_ReturnsMeters()
        {
            double result = UnitConverter.ConvertToInternal(1.0, "inch");
            Assert.AreEqual(0.0254, result, 1e-6);
        }

        [TestMethod]
        public void ConvertToInternal_CM_ReturnsMeters()
        {
            double result = UnitConverter.ConvertToInternal(100.0, "cm");
            Assert.AreEqual(1.0, result, 1e-10);
        }

        [TestMethod]
        public void ConvertToInternal_M_ReturnsMeters()
        {
            double result = UnitConverter.ConvertToInternal(2.5, "m");
            Assert.AreEqual(2.5, result, 1e-10);
        }

        [TestMethod]
        public void ConvertToInternal_In_ReturnsMeters()
        {
            double result = UnitConverter.ConvertToInternal(2.0, "in");
            Assert.AreEqual(0.0508, result, 1e-6);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ConvertToInternal_InvalidUnit_ThrowsArgumentException()
        {
            UnitConverter.ConvertToInternal(100.0, "furlongs");
        }

        [TestMethod]
        public void IsValidUnit_ValidUnits_ReturnsTrue()
        {
            Assert.IsTrue(UnitConverter.IsValidUnit("mm"));
            Assert.IsTrue(UnitConverter.IsValidUnit("cm"));
            Assert.IsTrue(UnitConverter.IsValidUnit("m"));
            Assert.IsTrue(UnitConverter.IsValidUnit("in"));
            Assert.IsTrue(UnitConverter.IsValidUnit("inch"));
            Assert.IsTrue(UnitConverter.IsValidUnit("ft"));
            Assert.IsTrue(UnitConverter.IsValidUnit("MM")); // case insensitive
        }

        [TestMethod]
        public void IsValidUnit_InvalidUnit_ReturnsFalse()
        {
            Assert.IsFalse(UnitConverter.IsValidUnit("furlongs"));
            Assert.IsFalse(UnitConverter.IsValidUnit(""));
            Assert.IsFalse(UnitConverter.IsValidUnit(null));
        }

        [TestMethod]
        public void ParseUnit_NormalizesCorrectly()
        {
            Assert.AreEqual("mm", UnitConverter.ParseUnit("MM"));
            Assert.AreEqual("mm", UnitConverter.ParseUnit("millimeter"));
            Assert.AreEqual("in", UnitConverter.ParseUnit("INCH"));
            Assert.AreEqual("in", UnitConverter.ParseUnit("inches"));
        }

        [TestMethod]
        public void RoundTrip_MMToMetersAndBack()
        {
            double originalMm = 123.456;
            double meters = UnitConverter.FromMillimeters(originalMm);
            double backToMm = UnitConverter.ToMillimeters(meters);
            Assert.AreEqual(originalMm, backToMm, 1e-9);
        }

        [TestMethod]
        public void ConvertToInternal_ZeroValue_ReturnsZero()
        {
            double result = UnitConverter.ConvertToInternal(0.0, "mm");
            Assert.AreEqual(0.0, result, 1e-15);
        }

        [TestMethod]
        public void ConvertToInternal_NegativeValue_ReturnsNegative()
        {
            // Negative values are allowed at the converter level; validation happens in CadPlanValidator
            double result = UnitConverter.ConvertToInternal(-50.0, "mm");
            Assert.AreEqual(-0.05, result, 1e-10);
        }
    }
}

