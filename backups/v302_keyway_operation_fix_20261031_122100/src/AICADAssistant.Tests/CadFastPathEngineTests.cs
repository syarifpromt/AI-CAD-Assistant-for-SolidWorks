using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Validation;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class CadFastPathEngineTests
    {
        private CadPlanValidator _validator;

        [TestInitialize]
        public void Setup()
        {
            _validator = new CadPlanValidator();
        }

        [TestMethod]
        public void FastPath_Hemisphere_Diameter100_CreatesValidCreateHemisphereSpec()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("Buatkan setengah bola pejal dengan diameter 100 mm", out spec);

            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);
            Assert.AreEqual("mm", spec.Units);
            Assert.AreEqual(1, spec.Operations.Count);
            Assert.AreEqual(OperationType.CreateHemisphere, spec.Operations[0].Type);
            Assert.AreEqual(100.0, Convert.ToDouble(spec.Operations[0].Parameters["diameter"]));

            // Ensure validator passes 100%
            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void FastPath_Hemisphere_ExactFailingPromptWithPeriod_CreatesValidSpec()
        {
            // Exact user prompt from v1.1.3 testing: "Buat setengah bola pejal dengan diameter 100 mm."
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("Buat setengah bola pejal dengan diameter 100 mm.", out spec);

            Assert.IsTrue(matched, "FastPath harus mengenali kalimat persis dengan titik di akhir");
            Assert.IsNotNull(spec);
            Assert.AreEqual("mm", spec.Units);
            Assert.AreEqual(1, spec.Operations.Count);
            Assert.AreEqual(OperationType.CreateHemisphere, spec.Operations[0].Type);
            Assert.AreEqual(100.0, Convert.ToDouble(spec.Operations[0].Parameters["diameter"]));

            // Verify no hallucinated operations (AddRectangle, ExtrudeCut) exist
            foreach (var op in spec.Operations)
            {
                Assert.AreNotEqual(OperationType.AddRectangle, op.Type, "FastPath tidak boleh menghasilkan AddRectangle untuk setengah bola");
                Assert.AreNotEqual(OperationType.ExtrudeCut, op.Type, "FastPath tidak boleh menghasilkan ExtrudeCut untuk setengah bola");
            }

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid, "Spesifikasi setengah bola pejal harus 100% valid");
            Assert.AreEqual(0, valResult.Errors.Count);
            Assert.AreEqual(0, valResult.MissingDimensions.Count);
        }

        [TestMethod]
        public void FastPath_Hemisphere_Variations_CreateValidSpecs()
        {
            string[] testPhrases = new string[]
            {
                "Buat setengah bola diameter 100 mm.",
                "Buat hemisphere diameter 100 mm.",
                "BUAT SETENGAH BOLA PEJAL DENGAN DIAMETER 100 MM.",
                "   Buat   setengah   bola   diameter   100   mm .  ",
                "buat kubah diameter 100 mm",
                "buat setengah bola pejal d=100 mm",
                "buat setengah bola pejal Ø100 mm",
                "buat setengah bola pejal radius 50 mm",
                "buat hemisphere r=50 mm",
                "setengah bola pejal 100 mm"
            };

            foreach (var phrase in testPhrases)
            {
                CadSpec spec;
                bool matched = CadFastPathEngine.TryCreateSpec(phrase, out spec);
                Assert.IsTrue(matched, "Gagal mengenali variasi: " + phrase);
                Assert.IsNotNull(spec, "Spec null untuk: " + phrase);
                Assert.AreEqual(1, spec.Operations.Count, "Operasi harus berjumlah 1 untuk: " + phrase);
                Assert.AreEqual(OperationType.CreateHemisphere, spec.Operations[0].Type, "Tipe operasi harus CreateHemisphere untuk: " + phrase);
                Assert.AreEqual(100.0, Convert.ToDouble(spec.Operations[0].Parameters["diameter"]), 0.001, "Diameter harus 100 mm untuk: " + phrase);

                var valResult = _validator.Validate(spec);
                Assert.IsTrue(valResult.IsValid, "Validasi gagal untuk: " + phrase);
            }
        }

        [TestMethod]
        public void FastPath_Hemisphere_InvalidDiameter_ReturnsFalse()
        {
            CadSpec spec;
            // Diameter 0 mm
            Assert.IsFalse(CadFastPathEngine.TryCreateSpec("buat setengah bola diameter 0 mm", out spec));
            Assert.IsNull(spec);

            // Diameter negative
            Assert.IsFalse(CadFastPathEngine.TryCreateSpec("buat setengah bola diameter -50 mm", out spec));
            Assert.IsNull(spec);
        }

        [TestMethod]
        public void FastPath_Hemisphere_UnitConversion_NormalizesToMillimeters()
        {
            CadSpec spec;
            // 10 cm = 100 mm
            bool matched = CadFastPathEngine.TryCreateSpec("buat setengah bola diameter 10 cm", out spec);
            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);
            Assert.AreEqual("mm", spec.Units);
            Assert.AreEqual(100.0, Convert.ToDouble(spec.Operations[0].Parameters["diameter"]), 0.001);

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void FastPath_Hemisphere_Radius50_CreatesValidCreateHemisphereSpec()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("setengah bola pejal radius 50 mm", out spec);

            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);
            Assert.AreEqual(OperationType.CreateHemisphere, spec.Operations[0].Type);
            Assert.AreEqual(100.0, Convert.ToDouble(spec.Operations[0].Parameters["diameter"]));

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void FastPath_Plate_100x60x5_CreatesValidPlateSpec()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("Buatkan pelat 100 x 60 x 5 mm", out spec);

            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);
            Assert.AreEqual(3, spec.Operations.Count); // NewSketch, AddRectangle, ExtrudeBoss
            Assert.AreEqual(OperationType.NewSketch, spec.Operations[0].Type);
            Assert.AreEqual(OperationType.AddRectangle, spec.Operations[1].Type);
            Assert.AreEqual(OperationType.ExtrudeBoss, spec.Operations[2].Type);

            Assert.AreEqual(100.0, Convert.ToDouble(spec.Operations[1].Parameters["width"]));
            Assert.AreEqual(60.0, Convert.ToDouble(spec.Operations[1].Parameters["height"]));
            Assert.AreEqual(5.0, Convert.ToDouble(spec.Operations[2].Parameters["depth"]));

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void FastPath_Block_100x50x20_CreatesValidBlockSpec()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("balok 100x50x20 mm", out spec);

            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);
            Assert.AreEqual(3, spec.Operations.Count);

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void FastPath_PlateWithHole_CreatesValidHoleSpec()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("pelat 100x60x5 mm dengan lubang diameter 10 mm", out spec);

            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);
            Assert.AreEqual(4, spec.Operations.Count); // Sketch, Rect, Extrude, SimpleHole
            Assert.AreEqual(OperationType.SimpleHole, spec.Operations[3].Type);
            Assert.AreEqual(10.0, Convert.ToDouble(spec.Operations[3].Parameters["diameter"]));

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void FastPath_Cylinder_CreatesValidCylinderSpec()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("silinder pejal diameter 50 mm tinggi 100 mm", out spec);

            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);
            Assert.AreEqual(3, spec.Operations.Count); // Sketch, Circle, ExtrudeBoss
            Assert.AreEqual(OperationType.AddCircle, spec.Operations[1].Type);
            Assert.AreEqual(OperationType.ExtrudeBoss, spec.Operations[2].Type);
            Assert.AreEqual(50.0, Convert.ToDouble(spec.Operations[1].Parameters["diameter"]));
            Assert.AreEqual(100.0, Convert.ToDouble(spec.Operations[2].Parameters["depth"]));

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void FastPath_AmbiguousRequest_ReturnsFalse()
        {
            CadSpec spec;
            // Ambiguous / tentative requests must be routed to Gemini
            bool matched1 = CadFastPathEngine.TryCreateSpec("mungkin buatkan pelat sekitar 100 mm", out spec);
            Assert.IsFalse(matched1);
            Assert.IsNull(spec);

            bool matched2 = CadFastPathEngine.TryCreateSpec("buatkan bracket motor NEMA 17 dengan 4 lubang baut M3", out spec);
            Assert.IsFalse(matched2);
            Assert.IsNull(spec);

            bool matched3 = CadFastPathEngine.TryCreateSpec("revisi tebal pelat menjadi 8 mm", out spec);
            Assert.IsFalse(matched3);
            Assert.IsNull(spec);
        }

        [TestMethod]
        public void FastPath_EmptyOrNullText_ReturnsFalse()
        {
            CadSpec spec;
            Assert.IsFalse(CadFastPathEngine.TryCreateSpec("", out spec));
            Assert.IsFalse(CadFastPathEngine.TryCreateSpec("   ", out spec));
            Assert.IsFalse(CadFastPathEngine.TryCreateSpec(null, out spec));
        }
    }
}

