using System;
using System.Collections.Generic;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICADAssistant.Tests
{
    /// <summary>
    /// Regression and validation tests for Section F (P0 Bugfix v3.0.2):
    /// CAD Intent Classification, Document Context Isolation, and Prevention of False Success.
    /// </summary>
    [TestClass]
    public class CadContextIsolationTests
    {
        private const string SteppedShaftPrompt =
            "Total panjang 120 mm. Lima segmen poros. Diameter 20, 30, 25, 30, 20 mm. Panjang segmen 20, 8, 64, 8, 20 mm. Revolve Boss/Base 360°. Lubang aksial Ø8 mm menggunakan Cut-Extrude Through All.";

        private CadSpec CreateSamplePlateSpec()
        {
            return new CadSpec
            {
                RequestId = "plate_001",
                DocumentType = DocumentType.Part,
                Units = "mm",
                TargetDocumentId = "Part1.SLDPRT",
                TargetDocumentTitle = "Part1",
                ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "panjang", 100.0 },
                    { "lebar", 60.0 },
                    { "tebal", 5.0 }
                },
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "sk1", Type = OperationType.NewSketch },
                    new CadOperation { Id = "rect1", Type = OperationType.AddRectangle },
                    new CadOperation { Id = "ext1", Type = OperationType.ExtrudeBoss },
                    new CadOperation { Id = "sk2", Type = OperationType.NewSketch },
                    new CadOperation { Id = "circ1", Type = OperationType.AddCircle },
                    new CadOperation { Id = "cut1", Type = OperationType.ExtrudeCut }
                }
            };
        }

        /// <summary>
        /// Test 1: New stepped shaft request with existing CadSpec but activeBodyCount = 0 produces CreateNewModel.
        /// </summary>
        [TestMethod]
        public void Test01_NewSteppedShaftRequest_WithExistingCadSpec_ZeroBodies_YieldsCreateNewModel()
        {
            var priorPlateSpec = CreateSamplePlateSpec();
            int activeBodyCount = 0; // Empty / newly opened document

            string reason;
            UserIntent intent = IntentClassifier.Classify(SteppedShaftPrompt, priorPlateSpec, activeBodyCount, out reason);

            Assert.AreEqual(UserIntent.CreateNewModel, intent,
                "Request on empty document (bodies=0) MUST be classified as CreateNewModel regardless of prior CadSpec.");
            Assert.IsFalse(string.IsNullOrEmpty(reason), "Classification reason must be provided.");
        }

        /// <summary>
        /// Test 2: New stepped shaft request does NOT trigger FastPathRevision.
        /// </summary>
        [TestMethod]
        public void Test02_NewSteppedShaftRequest_DoesNotTriggerFastPathRevision()
        {
            var priorPlateSpec = CreateSamplePlateSpec();

            CadSpec revSpec;
            bool matched = CadFastPathEngine.TryCreateRevisionSpec(SteppedShaftPrompt, priorPlateSpec, out revSpec);

            Assert.IsFalse(matched, "Stepped shaft prompt must NEVER match FastPathRevision.");
            Assert.IsNull(revSpec, "Revision spec must remain null for stepped shaft.");

            // Also test Indonesian variant
            string promptIndo = "Buat sebuah poros bertingkat dengan lima bagian dan tambahkan lubang aksial Ø8 mm.";
            bool matchedIndo = CadFastPathEngine.TryCreateRevisionSpec(promptIndo, priorPlateSpec, out revSpec);
            Assert.IsFalse(matchedIndo, "Poros bertingkat must be inhibited from FastPathRevision.");
        }

        /// <summary>
        /// Test 3: Prompt 'Tambahkan lubang pada pelat aktif' remains ModifyExistingModel when context is valid.
        /// </summary>
        [TestMethod]
        public void Test03_AddHoleToActivePlate_ClassifiesAsModifyExistingModel_WhenContextValid()
        {
            var activePlateSpec = CreateSamplePlateSpec();
            int activeBodyCount = 1; // Solid body exists

            string prompt = "Tambahkan empat lubang pada pelat aktif";
            string reason;
            UserIntent intent = IntentClassifier.Classify(prompt, activePlateSpec, activeBodyCount, out reason);

            Assert.AreEqual(UserIntent.ModifyExistingModel, intent,
                "Prompt adding holes to active plate with bodyCount=1 must classify as ModifyExistingModel.");
            Assert.IsTrue(reason.Contains("revisi") || reason.Contains("penambahan"),
                "Reason should mention revision or addition on active model.");
        }

        /// <summary>
        /// Test 4: Switching to new document does not use prior document's CadSpec.
        /// </summary>
        [TestMethod]
        public void Test04_DocumentSwitch_DoesNotUsePriorDocumentCadSpec()
        {
            // Simulate document spec repository
            var docSpecs = new Dictionary<string, CadSpec>(StringComparer.OrdinalIgnoreCase);
            var doc1Spec = CreateSamplePlateSpec();
            docSpecs["Part1.SLDPRT"] = doc1Spec;

            // User switches to Part2 (which has 0 solid bodies or is a distinct document)
            string currentDocId = "Part2.SLDPRT";
            int activeBodyCount = 0;

            CadSpec activeDocSpec = null;
            if (activeBodyCount > 0 && docSpecs.TryGetValue(currentDocId, out var foundSpec))
            {
                activeDocSpec = foundSpec;
            }

            Assert.IsNull(activeDocSpec, "Active spec for a fresh/different document Part2 MUST be null.");

            string reason;
            UserIntent intent = IntentClassifier.Classify(SteppedShaftPrompt, activeDocSpec, activeBodyCount, out reason);
            Assert.AreEqual(UserIntent.CreateNewModel, intent);
        }

        /// <summary>
        /// Test 5: Complex prompt does not produce a single primitive.
        /// </summary>
        [TestMethod]
        public void Test05_ComplexPrompt_DoesNotProduceSinglePrimitive()
        {
            var priorPlateSpec = CreateSamplePlateSpec();

            // 1. Revision fast path must NOT produce a single 20x20 cylinder
            CadSpec revSpec;
            bool revMatched = CadFastPathEngine.TryCreateRevisionSpec(SteppedShaftPrompt, priorPlateSpec, out revSpec);
            Assert.IsFalse(revMatched, "FastPathRevision must reject stepped shaft.");

            // 2. New model fast path must either produce the full multi-segment spec or yield to Gemini
            CadSpec fastSpec;
            bool matched = CadFastPathEngine.TryCreateSpec(SteppedShaftPrompt, out fastSpec);
            if (matched)
            {
                Assert.IsNotNull(fastSpec);
                Assert.IsTrue(fastSpec.Operations.Count >= 4,
                    "Generated stepped shaft must have at least 4 operations (not a single primitive).");
                Assert.IsTrue(fastSpec.Operations.Exists(o => o.Type == OperationType.RevolvedBoss),
                    "Stepped shaft must contain RevolvedBoss operation.");
                Assert.IsTrue(fastSpec.Operations.Exists(o => o.Type == OperationType.ExtrudeCut),
                    "Stepped shaft with axial hole must contain ExtrudeCut operation.");
                Assert.AreEqual(120.0, fastSpec.ExplicitDimensions["panjang"], 0.001,
                    "Total length must be 120 mm.");
            }
        }

        /// <summary>
        /// Test 6: Incomplete operation graph cannot return CompleteSuccess.
        /// </summary>
        [TestMethod]
        public void Test06_IncompleteOperationGraph_CannotReturnCompleteSuccess()
        {
            // Plan specifies Boss Revolve + Axial Cut
            var spec = new CadSpec
            {
                RequestId = "req_stepped_shaft",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "sk1", Type = OperationType.NewSketch },
                    new CadOperation { Id = "poly1", Type = OperationType.AddPolyline },
                    new CadOperation { Id = "rev1", Type = OperationType.RevolvedBoss },
                    new CadOperation { Id = "sk2", Type = OperationType.NewSketch },
                    new CadOperation { Id = "circ1", Type = OperationType.AddCircle },
                    new CadOperation { Id = "cut1", Type = OperationType.ExtrudeCut }
                }
            };

            // Result: only 1 feature completed (e.g. primitive cylinder created, cut missing)
            var incompleteResult = new ExecutionResult
            {
                Success = true,
                SolidBodyCount = 1,
                RebuildSuccess = true,
                FeatureName = "Boss-Extrude1",
                CompletedOperations = new List<OperationResult>
                {
                    OperationResult.Succeeded("sk1"),
                    OperationResult.Succeeded("circ1"),
                    OperationResult.Succeeded("ext1")
                },
                BoundingBoxMm = new double[] { 20.0, 20.0, 20.0 },
                VolumeMm3 = 6283.19
            };

            var report = GeometryVerifier.Verify(incompleteResult, spec);

            Assert.AreNotEqual(VerificationStatus.CompleteSuccess, report.Status,
                "Incomplete operation graph must not return CompleteSuccess.");
            Assert.IsFalse(report.IsValid, "Report must not be valid when secondary features are missing.");
        }

        /// <summary>
        /// Test 7: Bounding box mismatch cannot return CompleteSuccess.
        /// </summary>
        [TestMethod]
        public void Test07_BoundingBoxMismatch_CannotReturnCompleteSuccess()
        {
            var spec = new CadSpec
            {
                RequestId = "req_shaft_120",
                ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "panjang", 120.0 },
                    { "diameter", 30.0 }
                },
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "sk1", Type = OperationType.NewSketch },
                    new CadOperation { Id = "ext1", Type = OperationType.ExtrudeBoss }
                }
            };

            // Result has bounding box 20x20x20 mm (the bug from the log!)
            var bugResult = new ExecutionResult
            {
                Success = true,
                SolidBodyCount = 1,
                RebuildSuccess = true,
                BoundingBoxMm = new double[] { 20.0, 20.0, 20.0 },
                VolumeMm3 = 6283.19
            };

            var report = GeometryVerifier.Verify(bugResult, spec);

            Assert.AreEqual(VerificationStatus.Failed, report.Status,
                "Bounding box of 20 mm when 120 mm was requested must be marked Failed.");
            Assert.IsFalse(report.IsValid, "Report must be invalid.");
            Assert.IsTrue(report.Errors.Exists(e => e.Contains("Bounding Box") && e.Contains("120")),
                "Error must detail the bounding box deviation from expected 120 mm.");
        }

        /// <summary>
        /// Test 8: Unsupported operation cannot report success.
        /// </summary>
        [TestMethod]
        public void Test08_UnsupportedOperation_CannotReportSuccess()
        {
            var spec = new CadSpec
            {
                RequestId = "req_unsupported",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "sk1", Type = OperationType.NewSketch },
                    new CadOperation { Id = "custom1", Type = (OperationType)999 } // Invalid/unsupported operation
                }
            };

            var result = new ExecutionResult
            {
                Success = true,
                SolidBodyCount = 1,
                RebuildSuccess = true,
                BoundingBoxMm = new double[] { 10.0, 10.0, 10.0 },
                VolumeMm3 = 1000.0
            };

            var report = GeometryVerifier.Verify(result, spec);

            Assert.AreEqual(VerificationStatus.Failed, report.Status,
                "Specification containing unsupported operation must be classified as Failed.");
            Assert.IsFalse(report.IsValid, "Report must be invalid.");
        }
    }
}

