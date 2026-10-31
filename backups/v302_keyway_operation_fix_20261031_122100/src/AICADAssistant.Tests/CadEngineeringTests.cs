using System;
using System.Collections.Generic;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class CadEngineeringTests
    {
        private CadPlanValidator _validator;

        [TestInitialize]
        public void Setup()
        {
            _validator = new CadPlanValidator();
        }

        [TestMethod]
        public void FastPath_Wrench10mm_GeneratesAccurateSpecWithPolylineAndHole()
        {
            string prompt = "Buatkan kunci pas ukuran 10 mm dengan panjang 120 mm, ketebalan 5 mm, lubang gantung diameter 5 mm di ujung gagang, radius pada sudut kepala, dan bentuk gagang yang mengecil secara bertahap.";
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec(prompt, out spec);

            Assert.IsTrue(matched, "Wrench prompt must be matched by Fast Path");
            Assert.IsNotNull(spec);
            Assert.AreEqual("mm", spec.Units);
            Assert.IsTrue(spec.Operations.Count >= 6, "Must contain at least sketch, polyline, extrude, hole sketch, circle, cut, fillet");

            // Verify Polyline operation
            var polyOp = spec.Operations.Find(o => o.Type == OperationType.AddPolyline);
            Assert.IsNotNull(polyOp, "Must have AddPolyline operation for outer contour");
            List<double[]> pts;
            Assert.IsTrue(polyOp.TryGetPoints(out pts));
            Assert.AreEqual(12, pts.Count, "Wrench contour polyline must have 12 perimeter points");
            bool closed;
            Assert.IsTrue(polyOp.TryGetBool("closed", out closed) && closed);

            // Verify Extrude Boss depth
            var extOp = spec.Operations.Find(o => o.Type == OperationType.ExtrudeBoss);
            Assert.IsNotNull(extOp);
            double depth;
            Assert.IsTrue(extOp.TryGetDouble("depth", out depth));
            Assert.AreEqual(5.0, depth, 0.001);

            // Verify Hole
            var circOp = spec.Operations.Find(o => o.Type == OperationType.AddCircle);
            Assert.IsNotNull(circOp);
            double holeDia, holeX;
            Assert.IsTrue(circOp.TryGetDouble("diameter", out holeDia));
            Assert.AreEqual(5.0, holeDia, 0.001);
            Assert.IsTrue(circOp.TryGetDouble("x", out holeX));
            Assert.AreEqual(112.0, holeX, 0.001, "Hole must be placed 8 mm from end (120 - 8 = 112)");

            var cutOp = spec.Operations.Find(o => o.Type == OperationType.ExtrudeCut);
            Assert.IsNotNull(cutOp);
            bool isThrough;
            Assert.IsTrue(cutOp.TryGetBool("throughAll", out isThrough) && isThrough);

            // Verify Fillet
            var filOp = spec.Operations.Find(o => o.Type == OperationType.Fillet);
            Assert.IsNotNull(filOp);
            double rad;
            Assert.IsTrue(filOp.TryGetDouble("radius", out rad));
            Assert.AreEqual(1.0, rad, 0.001);

            // Semantic validation
            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid, "Wrench spec must be 100% semantically valid");
        }

        [TestMethod]
        public void FastPath_LBracket_GeneratesAccurateSpec()
        {
            string prompt = "Buat L-bracket 80x100x40 mm tebal 5 mm";
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec(prompt, out spec);

            Assert.IsTrue(matched, "L-bracket prompt must be matched by Fast Path");
            Assert.IsNotNull(spec);

            var polyOp = spec.Operations.Find(o => o.Type == OperationType.AddPolyline);
            Assert.IsNotNull(polyOp);
            List<double[]> pts;
            Assert.IsTrue(polyOp.TryGetPoints(out pts));
            Assert.AreEqual(6, pts.Count, "L-bracket cross section must have 6 vertices");

            var extOp = spec.Operations.Find(o => o.Type == OperationType.ExtrudeBoss);
            Assert.IsNotNull(extOp);
            double depth;
            Assert.IsTrue(extOp.TryGetDouble("depth", out depth));
            Assert.AreEqual(40.0, depth, 0.001);

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid, "L-bracket spec must be 100% valid");
        }

        [TestMethod]
        public void FastPath_Pulley_GeneratesRevolvedBossSpec()
        {
            string prompt = "Buat puli V-groove diameter 100 mm bore 20 mm lebar 30 mm";
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec(prompt, out spec);

            Assert.IsTrue(matched, "Pulley prompt must be matched by Fast Path");
            Assert.IsNotNull(spec);

            var polyOp = spec.Operations.Find(o => o.Type == OperationType.AddPolyline);
            Assert.IsNotNull(polyOp);

            var revOp = spec.Operations.Find(o => o.Type == OperationType.RevolvedBoss);
            Assert.IsNotNull(revOp);
            double angle;
            Assert.IsTrue(revOp.TryGetDouble("angle", out angle));
            Assert.AreEqual(360.0, angle, 0.001);

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid, "Pulley spec must be 100% valid");
        }

        [TestMethod]
        public void FastPath_SteppedShaft_GeneratesRevolvedBossSpec()
        {
            string prompt = "Buatkan poros bertingkat 3 seksi";
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec(prompt, out spec);

            Assert.IsTrue(matched, "Stepped shaft prompt must be matched by Fast Path");
            Assert.IsNotNull(spec);

            var polyOp = spec.Operations.Find(o => o.Type == OperationType.AddPolyline);
            Assert.IsNotNull(polyOp);

            var revOp = spec.Operations.Find(o => o.Type == OperationType.RevolvedBoss);
            Assert.IsNotNull(revOp);

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid, "Stepped shaft spec must be 100% valid");
        }

        [TestMethod]
        public void IntentClassifier_DifferentiatesAllIntents()
        {
            // Conversational
            Assert.AreEqual(UserIntent.Conversational, IntentClassifier.Classify("Halo, apa kabar?", null, 0));
            Assert.AreEqual(UserIntent.Conversational, IntentClassifier.Classify("Apa fungsi fitur revolve?", null, 0));
            Assert.AreEqual(UserIntent.Conversational, IntentClassifier.Classify("Bagaimana cara membuat ulir di SOLIDWORKS?", null, 0));
            Assert.AreEqual(UserIntent.Conversational, IntentClassifier.Classify("Berapa volume part ini?", null, 1));

            // Export
            Assert.AreEqual(UserIntent.Export, IntentClassifier.Classify("Ekspor model ke STEP", null, 1));
            Assert.AreEqual(UserIntent.Export, IntentClassifier.Classify("Simpan sebagai STL", null, 1));
            Assert.AreEqual(UserIntent.Export, IntentClassifier.Classify("export", null, 1));

            // CreateNewModel
            Assert.AreEqual(UserIntent.CreateNewModel, IntentClassifier.Classify("Buat part baru balok 100x50x20", null, 0));
            Assert.AreEqual(UserIntent.CreateNewModel, IntentClassifier.Classify("Buat kunci pas 10 mm", null, 0));
            Assert.AreEqual(UserIntent.CreateNewModel, IntentClassifier.Classify("Buat setengah bola diameter 100 mm", null, 0));

            // ModifyExistingModel
            Assert.AreEqual(UserIntent.ModifyExistingModel, IntentClassifier.Classify("Tambahkan tangkai diameter 20 mm panjang 100 mm", null, 1));
            Assert.AreEqual(UserIntent.ModifyExistingModel, IntentClassifier.Classify("Buat lubang 5 mm tembus di ujung", null, 1));
            Assert.AreEqual(UserIntent.ModifyExistingModel, IntentClassifier.Classify("Tambahkan fillet radius 2 mm", null, 1));
            Assert.AreEqual(UserIntent.ModifyExistingModel, IntentClassifier.Classify("Tambahkan chamfer 1 mm", null, 1));
        }

        [TestMethod]
        public void FastPathRevision_HoleWithOffset_CalculatesPositionFromEnd()
        {
            var baseSpec = new CadSpec
            {
                Units = "mm",
                ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { "panjang", 120.0 } },
                Operations = new List<CadOperation> { new CadOperation { Id = "base1", Type = OperationType.ExtrudeBoss } }
            };

            CadSpec revSpec;
            bool matched = CadFastPathEngine.TryCreateRevisionSpec("tambahkan lubang diameter 5 mm 8 mm dari ujung", baseSpec, out revSpec);
            Assert.IsTrue(matched);
            Assert.IsNotNull(revSpec);

            var circOp = revSpec.Operations.Find(o => o.Type == OperationType.AddCircle);
            Assert.IsNotNull(circOp);
            double x;
            Assert.IsTrue(circOp.TryGetDouble("x", out x));
            Assert.AreEqual(112.0, x, 0.001, "Position X must be 120 - 8 = 112 mm");

            var cutOp = revSpec.Operations.Find(o => o.Type == OperationType.ExtrudeCut);
            Assert.IsNotNull(cutOp);
            bool isThrough;
            Assert.IsTrue(cutOp.TryGetBool("throughAll", out isThrough) && isThrough);
        }

        [TestMethod]
        public void FastPathRevision_FilletAndChamfer_GeneratesTargetedSpec()
        {
            var baseSpec = new CadSpec
            {
                Units = "mm",
                Operations = new List<CadOperation> { new CadOperation { Id = "ext1", Type = OperationType.ExtrudeBoss } }
            };

            CadSpec filSpec;
            bool filMatched = CadFastPathEngine.TryCreateRevisionSpec("tambahkan fillet radius 2.5 mm", baseSpec, out filSpec);
            Assert.IsTrue(filMatched);
            Assert.AreEqual(1, filSpec.Operations.Count);
            Assert.AreEqual(OperationType.Fillet, filSpec.Operations[0].Type);
            double r;
            Assert.IsTrue(filSpec.Operations[0].TryGetDouble("radius", out r));
            Assert.AreEqual(2.5, r, 0.001);

            CadSpec chamSpec;
            bool chamMatched = CadFastPathEngine.TryCreateRevisionSpec("tambahkan chamfer 1.5 mm", baseSpec, out chamSpec);
            Assert.IsTrue(chamMatched);
            Assert.AreEqual(1, chamSpec.Operations.Count);
            Assert.AreEqual(OperationType.Chamfer, chamSpec.Operations[0].Type);
            double d;
            Assert.IsTrue(chamSpec.Operations[0].TryGetDouble("distance", out d));
            Assert.AreEqual(1.5, d, 0.001);
        }

        [TestMethod]
        public void GeometryVerifier_ZeroBodies_ReturnsFailed()
        {
            var res = new ExecutionResult
            {
                Success = true,
                SolidBodyCount = 0,
                VolumeMm3 = 0
            };
            var spec = new CadSpec();

            var report = GeometryVerifier.Verify(res, spec);
            Assert.IsFalse(report.IsValid, "Zero bodies must fail geometry verification");
            Assert.AreEqual(ExecutionStatus.Failed, report.RecommendedStatus);
        }

        [TestMethod]
        public void GeometryVerifier_ValidModel_ReturnsSuccess()
        {
            var res = new ExecutionResult
            {
                Success = true,
                SolidBodyCount = 1,
                RebuildSuccess = true,
                VolumeMm3 = 50000.0,
                BoundingBoxMm = new double[] { 120.0, 24.0, 5.0 }
            };
            var spec = new CadSpec();

            var report = GeometryVerifier.Verify(res, spec);
            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(ExecutionStatus.Success, report.RecommendedStatus);
            Assert.AreEqual(0, report.Errors.Count);
        }

        [TestMethod]
        public void GeometryVerifier_VolumeDelta_DetectsAnomalies()
        {
            var res = new ExecutionResult
            {
                Success = true,
                SolidBodyCount = 1,
                RebuildSuccess = true,
                VolumeMm3 = 45000.0 // decreased volume after boss
            };
            var spec = new CadSpec
            {
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "boss1", Type = OperationType.ExtrudeBoss, Parameters = new Dictionary<string, object> { { "depth", 10.0 } } }
                }
            };

            var report = GeometryVerifier.Verify(res, spec, 50000.0, true);
            Assert.IsTrue(report.IsValid);
            Assert.IsTrue(report.Warnings.Count > 0, "Decreased volume on boss operation must trigger warning");
        }

        [TestMethod]
        public void OperationCatalog_CoversAll33Operations()
        {
            int count = 0;
            foreach (var op in OperationCatalog.AllOperations) count++;
            Assert.AreEqual(33, count);

            for (int i = 0; i <= 32; i++)
            {
                OperationType opType = (OperationType)i;
                OperationMetadata meta = OperationCatalog.Get(opType);
                Assert.IsNotNull(meta, "Metadata must exist for OperationType " + opType);
                Assert.AreEqual(opType, meta.Type);
                Assert.IsFalse(string.IsNullOrWhiteSpace(meta.Name));
            }
        }
    }
}
