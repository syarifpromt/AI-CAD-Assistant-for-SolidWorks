using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Validation;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class CadRevisionTests
    {
        private CadPlanValidator _validator;

        [TestInitialize]
        public void Setup()
        {
            _validator = new CadPlanValidator();
        }

        [TestMethod]
        public void NewHemisphereRequest_CreatesCreateHemisphereSpec()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("Buat setengah bola pejal dengan diameter 100 mm.", out spec);

            Assert.IsTrue(matched, "Perintah membuat setengah bola baru harus dikenali Fast Path");
            Assert.IsNotNull(spec);
            Assert.AreEqual(1, spec.Operations.Count);
            Assert.AreEqual(OperationType.CreateHemisphere, spec.Operations[0].Type);
            Assert.AreEqual(100.0, Convert.ToDouble(spec.Operations[0].Parameters["diameter"]));

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid, "Spesifikasi setengah bola baru harus valid");
        }

        [TestMethod]
        public void AddingRodToHemisphere_DoesNotGenerateCreateHemisphere()
        {
            // Initial hemisphere spec
            CadSpec hemiSpec;
            CadFastPathEngine.TryCreateSpec("Buat setengah bola pejal dengan diameter 100 mm.", out hemiSpec);

            // Revision prompt to add rod
            string revText = "Tambahkan tangkai silinder diameter 20 mm panjang 100 mm di tengah bola.";
            CadSpec revSpec;
            bool matched = CadFastPathEngine.TryCreateRevisionSpec(revText, hemiSpec, out revSpec);

            Assert.IsTrue(matched, "Fast Path revision harus mengenali penambahan tangkai silinder");
            Assert.IsNotNull(revSpec);

            // Ensure NONE of the operations is CreateHemisphere
            foreach (var op in revSpec.Operations)
            {
                Assert.AreNotEqual(OperationType.CreateHemisphere, op.Type,
                    "Perintah menambahkan tangkai TIDAK BOLEH menghasilkan CreateHemisphere ulang");
            }

            // Verify operations are NewSketch, AddCircle, ExtrudeBoss
            Assert.AreEqual(3, revSpec.Operations.Count);
            Assert.AreEqual(OperationType.NewSketch, revSpec.Operations[0].Type);
            Assert.AreEqual("PlanarFace", revSpec.Operations[0].Parameters["plane"]);
            Assert.AreEqual(OperationType.AddCircle, revSpec.Operations[1].Type);
            Assert.AreEqual(20.0, Convert.ToDouble(revSpec.Operations[1].Parameters["diameter"]));
            Assert.AreEqual(OperationType.ExtrudeBoss, revSpec.Operations[2].Type);
            Assert.AreEqual(100.0, Convert.ToDouble(revSpec.Operations[2].Parameters["depth"]));
        }

        [TestMethod]
        public void IsModificationRequest_DetectsRevisionKeywords()
        {
            var currentSpec = new CadSpec { RequestId = "req_1" };

            Assert.IsTrue(CadContextReconciler.IsModificationRequest("Tambahkan tangkai silinder diameter 20 mm", currentSpec, 1));
            Assert.IsTrue(CadContextReconciler.IsModificationRequest("tambah lubang 10 mm di tengah", currentSpec, 1));
            Assert.IsTrue(CadContextReconciler.IsModificationRequest("pasangkan silinder panjang 100 mm", currentSpec, 1));
            Assert.IsTrue(CadContextReconciler.IsModificationRequest("revisi tebal menjadi 15 mm", currentSpec, 1));
            Assert.IsTrue(CadContextReconciler.IsModificationRequest("ubah diameter menjadi 50 mm", currentSpec, 1));
            Assert.IsTrue(CadContextReconciler.IsModificationRequest("buat lubang di tengah pelat", currentSpec, 1));
        }

        [TestMethod]
        public void IsModificationRequest_ExplicitNewModel_ReturnsFalse()
        {
            var currentSpec = new CadSpec { RequestId = "req_1" };

            // User explicitly requests new model -> Must not be treated as modification
            Assert.IsFalse(CadContextReconciler.IsModificationRequest("Buat part baru pelat 100x50 mm", currentSpec, 1));
            Assert.IsFalse(CadContextReconciler.IsModificationRequest("buat model baru", currentSpec, 1));
            Assert.IsFalse(CadContextReconciler.IsModificationRequest("mulai dari awal buat silinder 50 mm", currentSpec, 1));
            Assert.IsFalse(CadContextReconciler.IsModificationRequest("reset model buat balok 100x100x10", currentSpec, 1));
        }

        [TestMethod]
        public void ReconcileRevisionSpec_SkipsDuplicatedBaseOperationAndPreservesDependencies()
        {
            // Case: Gemini echoes base CreateHemisphere (hemi1) followed by rod features
            var currentSpec = new CadSpec
            {
                RequestId = "req_base",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "hemi1", Type = OperationType.CreateHemisphere }
                }
            };

            var incomingSpec = new CadSpec
            {
                RequestId = "req_rev",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "hemi1",
                        Type = OperationType.CreateHemisphere,
                        Parameters = new Dictionary<string, object> { { "diameter", 100.0 } }
                    },
                    new CadOperation
                    {
                        Id = "sk2",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object> { { "plane", "PlanarFace" } },
                        DependsOn = new List<string> { "hemi1" }
                    },
                    new CadOperation
                    {
                        Id = "circ1",
                        Type = OperationType.AddCircle,
                        Parameters = new Dictionary<string, object> { { "diameter", 20.0 }, { "x", 0.0 }, { "y", 0.0 } },
                        DependsOn = new List<string> { "sk2" }
                    },
                    new CadOperation
                    {
                        Id = "ext1",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object> { { "depth", 100.0 } },
                        DependsOn = new List<string> { "circ1" }
                    }
                }
            };

            HashSet<string> existingCompletedOpIds;
            string note;
            bool ok = CadContextReconciler.TryReconcileRevisionSpec(incomingSpec, currentSpec, 1, out existingCompletedOpIds, out note);

            Assert.IsTrue(ok);
            Assert.IsTrue(existingCompletedOpIds.Contains("hemi1"), "Operasi hemi1 harus didaftarkan sebagai existing completed operation");

            // Validate that downstream dependencies pass
            var valResult = _validator.Validate(incomingSpec, existingCompletedOpIds);
            Assert.IsTrue(valResult.IsValid, "Seluruh dependensi downstream harus valid");
            Assert.AreEqual(0, valResult.Errors.Count);
        }

        [TestMethod]
        public void ReconcileRevisionSpec_NewOperationsOnly_PreservesExistingOperationDependencies()
        {
            // Case: Plan contains ONLY new operations that depend on currentSpec base op
            var currentSpec = new CadSpec
            {
                RequestId = "req_base",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "hemi1", Type = OperationType.CreateHemisphere }
                }
            };

            var incomingSpec = new CadSpec
            {
                RequestId = "req_new_only",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "sk_rod",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object> { { "plane", "PlanarFace" } },
                        DependsOn = new List<string> { "hemi1" }
                    },
                    new CadOperation
                    {
                        Id = "circ_rod",
                        Type = OperationType.AddCircle,
                        Parameters = new Dictionary<string, object> { { "diameter", 20.0 } },
                        DependsOn = new List<string> { "sk_rod" }
                    },
                    new CadOperation
                    {
                        Id = "ext_rod",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object> { { "depth", 100.0 } },
                        DependsOn = new List<string> { "circ_rod" }
                    }
                }
            };

            HashSet<string> existingCompletedOpIds;
            string note;
            bool ok = CadContextReconciler.TryReconcileRevisionSpec(incomingSpec, currentSpec, 1, out existingCompletedOpIds, out note);

            Assert.IsTrue(ok);
            Assert.IsTrue(existingCompletedOpIds.Contains("hemi1"));

            // Validator with existingCompletedOpIds resolves 'hemi1' dependency
            var valResult = _validator.Validate(incomingSpec, existingCompletedOpIds);
            Assert.IsTrue(valResult.IsValid, "Dependensi ke hemi1 dari model aktif harus berhasil diselesaikan");
        }

        [TestMethod]
        public void ActiveModel_ZeroBodies_HandledSafely()
        {
            // When active doc has 0 bodies and no currentSpec, modification should not proceed blindly
            bool isMod = CadContextReconciler.IsModificationRequest("Tambahkan tangkai 20 mm", null, 0);
            Assert.IsFalse(isMod, "Jika tidak ada model aktif dan body count = 0, tidak boleh diperlakukan sebagai revisi");
        }

        [TestMethod]
        public void MergeSpecs_CombinesExistingAndNewOperationsWithoutLoss()
        {
            var currentSpec = new CadSpec
            {
                RequestId = "req_1",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "hemi1", Type = OperationType.CreateHemisphere }
                },
                ExplicitDimensions = new Dictionary<string, double> { { "diameter", 100.0 } }
            };

            var revSpec = new CadSpec
            {
                RequestId = "req_2",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "sk_rod", Type = OperationType.NewSketch },
                    new CadOperation { Id = "circ_rod", Type = OperationType.AddCircle },
                    new CadOperation { Id = "ext_rod", Type = OperationType.ExtrudeBoss }
                },
                ExplicitDimensions = new Dictionary<string, double> { { "panjang", 100.0 } }
            };

            var merged = CadContextReconciler.MergeSpecs(currentSpec, revSpec);

            Assert.IsNotNull(merged);
            Assert.AreEqual(4, merged.Operations.Count, "Hasil merge harus memiliki 4 operasi total");
            Assert.AreEqual("hemi1", merged.Operations[0].Id);
            Assert.AreEqual("sk_rod", merged.Operations[1].Id);
            Assert.AreEqual("circ_rod", merged.Operations[2].Id);
            Assert.AreEqual("ext_rod", merged.Operations[3].Id);
            Assert.IsTrue(merged.ExplicitDimensions.ContainsKey("diameter"));
            Assert.IsTrue(merged.ExplicitDimensions.ContainsKey("panjang"));
        }

        [TestMethod]
        public void FastPathRevision_RodAddition_ProducesCorrectSketchCircleExtrudeOperations()
        {
            var currentSpec = new CadSpec
            {
                RequestId = "req_1",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "hemi1", Type = OperationType.CreateHemisphere }
                }
            };

            CadSpec revSpec;
            bool ok = CadFastPathEngine.TryCreateRevisionSpec("Tambahkan batang diameter 20 mm panjang 100 mm di tengah bola", currentSpec, out revSpec);

            Assert.IsTrue(ok);
            Assert.IsNotNull(revSpec);
            Assert.AreEqual(3, revSpec.Operations.Count);

            var valResult = _validator.Validate(revSpec, new string[] { "hemi1" });
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void FastPathRevision_HoleAddition_ProducesCorrectSketchCircleCutOperations()
        {
            var currentSpec = new CadSpec
            {
                RequestId = "req_plate",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "rect1", Type = OperationType.AddRectangle },
                    new CadOperation { Id = "ext1", Type = OperationType.ExtrudeBoss }
                },
                ExplicitDimensions = new Dictionary<string, double> { { "tebal", 10.0 } }
            };

            CadSpec revSpec;
            bool ok = CadFastPathEngine.TryCreateRevisionSpec("Tambahkan lubang diameter 12 mm kedalaman 10 mm di tengah", currentSpec, out revSpec);

            Assert.IsTrue(ok);
            Assert.IsNotNull(revSpec);
            Assert.AreEqual(3, revSpec.Operations.Count);
            Assert.AreEqual(OperationType.NewSketch, revSpec.Operations[0].Type);
            Assert.AreEqual(OperationType.AddCircle, revSpec.Operations[1].Type);
            Assert.AreEqual(12.0, Convert.ToDouble(revSpec.Operations[1].Parameters["diameter"]));
            Assert.AreEqual(OperationType.ExtrudeCut, revSpec.Operations[2].Type);
            Assert.AreEqual(10.0, Convert.ToDouble(revSpec.Operations[2].Parameters["depth"]));

            var valResult = _validator.Validate(revSpec, new string[] { "rect1", "ext1" });
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void InvalidDependency_RejectedByValidator()
        {
            var spec = new CadSpec
            {
                RequestId = "req_invalid",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "ext_orphan",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object> { { "depth", 10.0 } },
                        DependsOn = new List<string> { "non_existent_op_123" }
                    }
                }
            };

            var valResult = _validator.Validate(spec);
            Assert.IsFalse(valResult.IsValid, "Rencana dengan dependensi yang tidak ditemukan harus ditolak");
            Assert.IsTrue(valResult.Errors.Count > 0);
        }
    }
}

