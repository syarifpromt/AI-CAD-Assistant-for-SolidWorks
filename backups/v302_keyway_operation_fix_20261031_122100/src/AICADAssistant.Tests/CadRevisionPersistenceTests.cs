using System;
using System.Collections.Generic;
using System.Linq;
using AICADAssistant.AddIn.UI.ViewModels;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICADAssistant.Tests
{
    /// <summary>
    /// Regression and validation test suite for Level 3A Revision Pipeline (v3.0.2 Critical Fix):
    /// Tests Document Context Persistence, Operation ID Normalization, Reconciler Integrity,
    /// Model Context Snapshot, and Pending CAD Task Continuity.
    /// </summary>
    [TestClass]
    public class CadRevisionPersistenceTests
    {
        /// <summary>
        /// Test 1: Verifies that a newly created part document receives an ID and title
        /// that are consistently persisted in the document CadSpec dictionary.
        /// </summary>
        [TestMethod]
        public void Test01_DocumentIdentifier_StaysConsistentAcrossNewPartAndSave()
        {
            var vm = new MainViewModel(null);
            var spec = new CadSpec
            {
                RequestId = "req_shaft_01",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "sk_shaft", Type = OperationType.NewSketch },
                    new CadOperation { Id = "poly_shaft", Type = OperationType.AddPolyline },
                    new CadOperation { Id = "rev_shaft", Type = OperationType.RevolvedBoss }
                }
            };

            string docId = "Part_20261031_111838";
            string docTitle = "Part_20261031_111838";

            vm.RegisterCadSpecForDocument(docId, docTitle, spec);

            bool found = vm.TryGetCadSpecForDocument(docId, docTitle, out var retrieved);

            Assert.IsTrue(found, "CadSpec harus dapat ditemukan kembali berdasarkan document ID dan title.");
            Assert.IsNotNull(retrieved, "CadSpec yang ditemukan tidak boleh null.");
            Assert.AreEqual("req_shaft_01", retrieved.RequestId);
            Assert.AreEqual(docId, retrieved.TargetDocumentId);
            Assert.AreEqual(3, retrieved.Operations.Count);
        }

        /// <summary>
        /// Test 2: Simulates temporary part (Part_20261031_111838) being saved as a definitive .SLDPRT file.
        /// Verifies alias mapping ensures CadSpec is retrievable using either the old or new document ID.
        /// </summary>
        [TestMethod]
        public void Test02_DocumentAliasMapping_PreservesCadSpecAfterSaveAs()
        {
            var vm = new MainViewModel(null);
            var spec = new CadSpec
            {
                RequestId = "req_shaft_01",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "rev1", Type = OperationType.RevolvedBoss }
                }
            };

            string initialTitle = "Part_20261031_111838";
            string savedPath = @"C:\Engineering\Models\SteppedShaft_rev1.SLDPRT";
            string savedTitle = "SteppedShaft_rev1";

            // Register spec under initial unsaved title
            vm.RegisterCadSpecForDocument(initialTitle, initialTitle, spec);

            // Document is saved (Save As)
            vm.RegisterDocumentAlias(savedPath, initialTitle);
            vm.RegisterDocumentAlias(savedTitle, initialTitle);

            // Verify retrieval using new file path and new title
            bool foundByPath = vm.TryGetCadSpecForDocument(savedPath, savedTitle, out var specFromPath);
            Assert.IsTrue(foundByPath, "CadSpec harus dapat diakses melalui path dokumen yang baru disimpan via alias.");
            Assert.IsNotNull(specFromPath);
            Assert.AreEqual("req_shaft_01", specFromPath.RequestId);

            // Verify retrieval using initial title still works
            bool foundByOldTitle = vm.TryGetCadSpecForDocument(initialTitle, initialTitle, out var specFromOld);
            Assert.IsTrue(foundByOldTitle, "CadSpec harus tetap dapat diakses menggunakan identifier lama.");
            Assert.IsNotNull(specFromOld);
        }

        /// <summary>
        /// Test 3: Verifies that CadPlanNormalizer assigns deterministic unique IDs
        /// and marks IsIdGenerated = true when operations have null or whitespace IDs.
        /// </summary>
        [TestMethod]
        public void Test03_OperationIdNormalizer_AssignsDeterministicIdsToEmptyOps()
        {
            var spec = new CadSpec
            {
                RequestId = "req_keyway_01",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = null, Type = OperationType.NewSketch },
                    new CadOperation { Id = "", Type = OperationType.AddRectangle },
                    new CadOperation { Id = "   ", Type = OperationType.ExtrudeCut }
                }
            };

            bool success = CadPlanNormalizer.TryNormalizeOperationIds(
                spec,
                null,
                out int missingCount,
                out int generatedCount,
                out int duplicateCount,
                out string diagMsg);

            Assert.IsTrue(success, "Normalisasi harus berhasil untuk operasi ber-ID kosong: " + diagMsg);
            Assert.AreEqual(3, missingCount, "Harus mendeteksi 3 operasi tanpa Id.");
            Assert.AreEqual(3, generatedCount, "Harus men-generate 3 Id baru.");
            Assert.AreEqual(0, duplicateCount, "Tidak boleh ada duplikasi Id.");

            foreach (var op in spec.Operations)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(op.Id), "Id operasi tidak boleh kosong setelah normalisasi.");
                Assert.IsTrue(op.IsIdGenerated, "IsIdGenerated harus true untuk operasi yang Id-nya digenerate.");
            }

            // Verify all IDs are mutually distinct
            var idSet = new HashSet<string>(spec.Operations.Select(o => o.Id), StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual(3, idSet.Count, "Setiap Id yang digenerate harus unik satu sama lain.");
        }

        /// <summary>
        /// Test 4: Verifies that CadPlanNormalizer detects and strictly rejects duplicate operation IDs
        /// within the incoming CadSpec.
        /// </summary>
        [TestMethod]
        public void Test04_OperationIdNormalizer_RejectsDuplicateIdsWithinSpec()
        {
            var spec = new CadSpec
            {
                RequestId = "req_test_dup",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "op_cut", Type = OperationType.ExtrudeCut },
                    new CadOperation { Id = "op_cut", Type = OperationType.ExtrudeCut }
                }
            };

            bool success = CadPlanNormalizer.TryNormalizeOperationIds(
                spec,
                null,
                out int missingCount,
                out int generatedCount,
                out int duplicateCount,
                out string diagMsg);

            Assert.IsFalse(success, "Normalizer harus menolak CadSpec yang memiliki ID duplikat.");
            Assert.AreEqual(1, duplicateCount, "Harus melaporkan 1 ID duplikat.");
            Assert.IsTrue(diagMsg.Contains("duplikat") && diagMsg.Contains("op_cut"),
                "Pesan diagnostik harus menyebutkan ID yang duplikat.");
        }

        /// <summary>
        /// Test 5: Verifies that CadPlanValidator maintains its strict safety contract
        /// and rejects operations that lack an ID.
        /// </summary>
        [TestMethod]
        public void Test05_CadPlanValidator_RejectsEmptyIdsEvenAfterNormalizationFailure()
        {
            var validator = new CadPlanValidator();
            var spec = new CadSpec
            {
                RequestId = "req_test",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "", Type = OperationType.NewSketch }
                }
            };

            var result = validator.Validate(spec);

            Assert.IsFalse(result.IsValid, "CadPlanValidator WAJIB menolak operasi dengan Id kosong.");
            Assert.IsTrue(result.Errors.Any(e => e.Contains("Terdapat operasi tanpa Id.")),
                "Error harus menyatakan terdapat operasi tanpa Id.");
        }

        /// <summary>
        /// Test 6: Verifies that CadContextReconciler never adds null or whitespace IDs
        /// to existingCompletedOpIds, preventing false positive collisions.
        /// </summary>
        [TestMethod]
        public void Test06_ContextReconciler_NeverAddsEmptyIdToCompletedSet()
        {
            var incomingSpec = new CadSpec
            {
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "", Type = OperationType.NewSketch },
                    new CadOperation { Id = "rev_cut", Type = OperationType.ExtrudeCut }
                }
            };

            var currentSpec = new CadSpec
            {
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "base_rev", Type = OperationType.RevolvedBoss }
                }
            };

            CadContextReconciler.TryReconcileRevisionSpec(
                incomingSpec,
                currentSpec,
                1,
                out var completedOpIds,
                out string note);

            Assert.IsFalse(completedOpIds.Contains(""), "Completed set TIDAK BOLEH berisi string kosong.");
            Assert.IsFalse(completedOpIds.Contains(null), "Completed set TIDAK BOLEH berisi null.");
            Assert.IsTrue(completedOpIds.Contains("base_rev"), "Completed set harus berisi ID yang valid dari currentSpec.");
        }

        /// <summary>
        /// Test 7: Verifies that CadContextReconciler eliminates NewPart operations
        /// from revision plans when active model already has solid bodies.
        /// </summary>
        [TestMethod]
        public void Test07_ContextReconciler_RejectsNewPartWhenSolidBodiesExist()
        {
            var incomingSpec = new CadSpec
            {
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "np1", Type = OperationType.NewPart },
                    new CadOperation { Id = "sk_key", Type = OperationType.NewSketch },
                    new CadOperation { Id = "cut_key", Type = OperationType.ExtrudeCut }
                }
            };

            var currentSpec = new CadSpec
            {
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "shaft_base", Type = OperationType.RevolvedBoss }
                }
            };

            CadContextReconciler.TryReconcileRevisionSpec(
                incomingSpec,
                currentSpec,
                1, // Active model has 1 solid body
                out var completedOpIds,
                out string note);

            Assert.IsFalse(incomingSpec.Operations.Any(o => o.Type == OperationType.NewPart),
                "Operasi NewPart WAJIB dihapus dari rencana revisi jika model aktif sudah memiliki solid body.");
            Assert.AreEqual(2, incomingSpec.Operations.Count, "Hanya operasi revisi yang tersisa.");
            Assert.IsTrue(note.Contains("Menghapus operasi NewPart"),
                "Catatan rekonsiliasi harus mendokumentasikan penghapusan NewPart.");
        }

        /// <summary>
        /// Test 8: Verifies that CadContextReconciler does NOT claim 'all operations already exist'
        /// when ExistingOps is 0, even if active body count is positive.
        /// </summary>
        [TestMethod]
        public void Test08_ContextReconciler_DoesNotClaimAllExistWhenExistingOpsZero()
        {
            var incomingSpec = new CadSpec
            {
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "sk_rev", Type = OperationType.NewSketch },
                    new CadOperation { Id = "cut_rev", Type = OperationType.ExtrudeCut }
                }
            };

            // Existing part has solid body but no prior recorded spec in memory
            CadContextReconciler.TryReconcileRevisionSpec(
                incomingSpec,
                null,
                1,
                out var completedOpIds,
                out string note);

            Assert.IsFalse(note.Contains("Semua operasi dalam rencana sudah ada pada model aktif."),
                "Reconciler TIDAK BOLEH menyimpulkan semua operasi sudah ada jika ExistingOps == 0.");
            Assert.IsTrue(note.Contains("Terdapat 2 operasi baru yang akan dieksekusi."),
                "Kedua operasi baru harus diidentifikasi untuk dieksekusi.");
        }

        /// <summary>
        /// Test 9: Verifies ModelContextSnapshot produces a structured summary
        /// containing existing features (Revolve1, Cut-Extrude1) and revision rules for Gemini.
        /// </summary>
        [TestMethod]
        public void Test09_ModelContextSnapshot_FormatsFeatureSummaryForGeminiPrompt()
        {
            var snapshot = new ModelContextSnapshot
            {
                DocumentTitle = "Part_20261031_111838",
                SolidBodyCount = 1,
                VolumeMm3 = 49260.17,
                IsRevolvedBaseShaft = true,
                HasAxialHole = true
            };
            snapshot.FeatureList.Add(new ModelFeatureInfo
            {
                Name = "Revolve1",
                TypeName = "Revolve",
                Description = "Revolve Boss 360°"
            });
            snapshot.FeatureList.Add(new ModelFeatureInfo
            {
                Name = "Cut-Extrude1",
                TypeName = "Cut",
                Description = "Through All axial hole Ø8 mm"
            });

            string contextString = snapshot.ToPromptContextString();

            Assert.IsTrue(contextString.Contains("Part_20261031_111838"), "Harus mencantumkan nama dokumen aktif.");
            Assert.IsTrue(contextString.Contains("Revolve1"), "Harus mencantumkan fitur Revolve1.");
            Assert.IsTrue(contextString.Contains("Cut-Extrude1"), "Harus mencantumkan fitur Cut-Extrude1.");
            Assert.IsTrue(contextString.Contains("JANGAN membuat ulang fitur-fitur di atas!"),
                "Harus menyertakan instruksi agar tidak membuat ulang geometri dasar.");
            Assert.IsTrue(contextString.Contains("JANGAN memasukkan operasi NewPart"),
                "Harus menyertakan instruksi larangan NewPart.");
        }

        /// <summary>
        /// Test 10: Verifies IntentClassifier classifies conversational continuation phrases
        /// ("kerjakan", "coba lagi", "lanjutkan", "ulang", "perbaiki") as ResumePendingTask
        /// when an active pending task is present on the document.
        /// </summary>
        [TestMethod]
        public void Test10_IntentClassifier_ResumePendingTaskOnFollowupPhrases()
        {
            string[] continuationPhrases = new[]
            {
                "kerjakan",
                "coba lagi",
                "lanjutkan",
                "ulang",
                "perbaiki",
                "eksekusi",
                "gas",
                "lanjut!"
            };

            foreach (var phrase in continuationPhrases)
            {
                string reason;
                UserIntent intent = IntentClassifier.Classify(phrase, null, 1, true, out reason);

                Assert.AreEqual(UserIntent.ResumePendingTask, intent,
                    string.Format("Frasa '{0}' harus diklasifikasikan sebagai ResumePendingTask saat ada pending task.", phrase));
                Assert.IsFalse(string.IsNullOrEmpty(reason));
            }
        }

        /// <summary>
        /// Test 11: Verifies IntentClassifier does NOT resume or create a blank new model
        /// when "kerjakan" is sent without an active pending task.
        /// </summary>
        [TestMethod]
        public void Test11_IntentClassifier_DoesNotResumeWhenNoPendingTask()
        {
            string reason;
            UserIntent intent = IntentClassifier.Classify("kerjakan", null, 1, false, out reason);

            Assert.AreNotEqual(UserIntent.ResumePendingTask, intent,
                "Tidak boleh ResumePendingTask jika tidak ada pending task.");
            Assert.AreNotEqual(UserIntent.CreateNewModel, intent,
                "Frasa lanjutan tanpa instruksi tidak boleh memicu CreateNewModel kosong.");
            Assert.AreEqual(UserIntent.Conversational, intent,
                "Frasa lanjutan tanpa pending task harus diperlakukan secara percakapan/klarifikasi.");
        }

        /// <summary>
        /// Test 12: Verifies PendingCadTask preserves the user's original revision prompt
        /// and target document metadata across failure and retry.
        /// </summary>
        [TestMethod]
        public void Test12_PendingCadTask_MaintainsOriginalPromptAcrossRetry()
        {
            string originalPrompt = "buatkan alur pasak keyway ukuran lebar 6 mm, kedalaman 3.5 mm, panjang 25 mm di poros tengah";
            string docId = "Part_20261031_111838";
            string docTitle = "Part_20261031_111838";

            var task = new PendingCadTask(originalPrompt, docId, docTitle, 1, "Validation failed: Missing IDs");

            Assert.AreEqual(originalPrompt, task.OriginalPrompt, "Prompt asli harus tersimpan secara utuh.");
            Assert.AreEqual(docId, task.TargetDocumentId);
            Assert.AreEqual(docTitle, task.TargetDocumentTitle);
            Assert.AreEqual(1, task.BodyCount);

            Assert.IsTrue(task.MatchesDocument(docId, docTitle), "Task harus cocok dengan dokumen target.");
            Assert.IsFalse(task.MatchesDocument("OtherPart_999", "OtherPart_999"), "Task tidak boleh cocok dengan dokumen berbeda.");

            // Simulating resume:
            string resumedPrompt = task.OriginalPrompt;
            Assert.AreEqual(originalPrompt, resumedPrompt, "Prompt yang dilanjutkan harus identik dengan prompt asli pengguna.");
        }
    }
}

