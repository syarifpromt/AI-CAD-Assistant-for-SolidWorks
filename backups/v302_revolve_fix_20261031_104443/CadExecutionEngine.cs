using System;
using System.Collections.Generic;
using AICADAssistant.Core.Gemini;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Models.Contracts;
using AICADAssistant.Core.Services;
using AICADAssistant.Core.Validation;

namespace AICADAssistant.Core.CAD
{
    public class EngineExecutionResponse
    {
        public bool Success { get; set; }
        public ExecutionResult ExecutionResult { get; set; }
        public CadSpec FinalSpec { get; set; }
        public VerificationResult Verification { get; set; }
        public List<CADDiagnostic> Diagnostics { get; set; }
        public bool UsedFastPath { get; set; }
        public int RepairAttempts { get; set; }
        public string UserMessage { get; set; }

        public EngineExecutionResponse()
        {
            Diagnostics = new List<CADDiagnostic>();
        }
    }

    public class CadExecutionEngine
    {
        private readonly GeminiClient _geminiClient;
        private readonly CadPlanner _planner;
        private readonly CadPlanValidator _validator;

        public CadExecutionEngine(GeminiClient geminiClient = null, CadPlanner planner = null, CadPlanValidator validator = null)
        {
            _geminiClient = geminiClient;
            _planner = planner ?? new CadPlanner();
            _validator = validator ?? new CadPlanValidator();
        }

        public EngineExecutionResponse ProcessAndExecute(
            string userPrompt,
            CadSpec activeSpec,
            int activeBodyCount,
            double activeVolumeMm3,
            Func<CadSpec, ExecutionResult> nativeExecutor,
            Action<int, string> progressReporter = null)
        {
            var response = new EngineExecutionResponse();

            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                response.Success = false;
                response.UserMessage = "Pesan permintaan kosong.";
                response.Diagnostics.Add(CADDiagnostic.CreateError(DiagnosticStage.InputNormalizer, "ERR_EMPTY_INPUT", "Input prompt was empty."));
                return response;
            }

            // STAGE 1: Input Normalization
            ReportProgress(progressReporter, 1, "Menormalisasi teks perintah...");
            string normalizedPrompt = userPrompt.Trim();

            // STAGE 2: Intent & Scope Classification
            ReportProgress(progressReporter, 2, "Mengklasifikasikan maksud perintah...");
            UserIntent classifiedIntent = IntentClassifier.Classify(normalizedPrompt, activeSpec, activeBodyCount);
            if (classifiedIntent == UserIntent.Conversational)
            {
                response.Success = true;
                response.UserMessage = "Permintaan bersifat percakapan umum.";
                return response;
            }

            bool isModification = (classifiedIntent == UserIntent.ModifyExistingModel);

            // STAGE 3: Deterministic Fast Path or Gemini AI Compilation
            ReportProgress(progressReporter, 3, "Menyusun rencana CAD...");
            CadSpec plannedSpec = null;
            bool fastPathMatched = false;

            if (isModification)
            {
                fastPathMatched = CadFastPathEngine.TryCreateRevisionSpec(normalizedPrompt, activeSpec, out plannedSpec);
            }
            else
            {
                fastPathMatched = CadFastPathEngine.TryCreateSpec(normalizedPrompt, out plannedSpec);
            }

            if (fastPathMatched && plannedSpec != null)
            {
                response.UsedFastPath = true;
                LoggingService.Instance.Info(string.Format("[EXEC_ENGINE] Fast path berhasil untuk '{0}'", normalizedPrompt));
            }
            else
            {
                // Fallback to Gemini AI
                if (_geminiClient == null)
                {
                    response.Success = false;
                    response.UserMessage = "Klien AI (Gemini) belum diinisialisasi dan tidak ada Fast Path yang cocok.";
                    response.Diagnostics.Add(CADDiagnostic.CreateError(DiagnosticStage.CadCompiler, "ERR_NO_AI_CLIENT", response.UserMessage));
                    return response;
                }

                ReportProgress(progressReporter, 3, "Mengirim permintaan ke Gemini AI...");
                string aiPrompt = isModification
                    ? _planner.BuildPromptForRevision(normalizedPrompt, activeSpec)
                    : _planner.BuildPromptForTextRequest(normalizedPrompt);

                string aiResponseText = _geminiClient.SendTextAsync(aiPrompt, null).GetAwaiter().GetResult();
                plannedSpec = _planner.ParseCadSpec(aiResponseText);

                if (plannedSpec == null)
                {
                    response.Success = false;
                    response.UserMessage = "Gagal memproses respons dari Gemini AI menjadi format rencana CAD yang valid.";
                    response.Diagnostics.Add(CADDiagnostic.CreateError(DiagnosticStage.CadCompiler, "ERR_INVALID_AI_JSON", "Failed to parse JSON into CadSpec."));
                    return response;
                }
            }

            response.FinalSpec = plannedSpec;

            // STAGE 4: Pre-Execution Semantic Validation
            ReportProgress(progressReporter, 4, "Memvalidasi parameter geometri dan topologi...");
            ValidationResult valResult = _validator.Validate(plannedSpec);
            if (!valResult.IsValid)
            {
                response.Success = false;
                response.UserMessage = "Rencana CAD tidak valid: " + string.Join("; ", valResult.Errors);
                foreach (var err in valResult.Errors)
                {
                    response.Diagnostics.Add(CADDiagnostic.CreateError(DiagnosticStage.PreValidation, "ERR_PARAM_INVALID", err));
                }
                return response;
            }

            // STAGE 5: Revision Context Reconciliation
            if (isModification && activeSpec != null)
            {
                ReportProgress(progressReporter, 5, "Merekonsiliasi dependensi fitur aktif...");
                HashSet<string> existingOps;
                string reconNote;
                CadContextReconciler.TryReconcileRevisionSpec(plannedSpec, activeSpec, activeBodyCount, out existingOps, out reconNote);
            }

            // STAGE 6: Native SOLIDWORKS Execution
            ReportProgress(progressReporter, 6, "Mengeksekusi operasi pada SOLIDWORKS...");
            if (nativeExecutor == null)
            {
                response.Success = false;
                response.UserMessage = "Native CAD executor tidak tersedia.";
                response.Diagnostics.Add(CADDiagnostic.CreateError(DiagnosticStage.NativeExecution, "ERR_NO_EXECUTOR", response.UserMessage));
                return response;
            }

            ExecutionResult execResult = nativeExecutor(plannedSpec);
            response.ExecutionResult = execResult;

            // STAGE 7: Post-Execution Physical & Topological Verification
            ReportProgress(progressReporter, 7, "Memverifikasi integritas fisik dan geometri...");
            GeometryVerificationReport geoReport = GeometryVerifier.Verify(execResult, plannedSpec, activeVolumeMm3, isModification);

            var verif = new VerificationResult
            {
                IsValid = geoReport.IsValid,
                SolidBodyCount = execResult != null ? execResult.SolidBodyCount : 0,
                CalculatedVolumeMm3 = execResult != null ? execResult.VolumeMm3 : 0.0,
                BoundingBoxMm = execResult != null ? execResult.BoundingBoxMm : new double[3],
                IsPhysicallySound = geoReport.IsValid && execResult != null && execResult.SolidBodyCount > 0
            };
            response.Verification = verif;

            // STAGE 8: Diagnostic Feedback & Auto-Repair Loop
            if (!geoReport.IsValid || (execResult != null && !execResult.Success))
            {
                ReportProgress(progressReporter, 8, "Menganalisis kegagalan eksekusi...");
                string failureReason = execResult != null ? execResult.ErrorMessage : geoReport.Summary;

                // If fast-path failed, attempt single AI-based repair with feedback
                if (response.UsedFastPath && _geminiClient != null && response.RepairAttempts < 2)
                {
                    response.RepairAttempts++;
                    ReportProgress(progressReporter, 8, "Mencoba perbaikan otomatis melalui AI...");
                    string repairPrompt = string.Format(
                        "Eksekusi operasi CAD sebelumnya gagal dengan pesan error:\n\"{0}\"\n\n" +
                        "Instruksi pengguna awal:\n\"{1}\"\n\n" +
                        "Perbaiki urutan dan parameter operasi sehingga berhasil dibentuk di SOLIDWORKS. Kembalikan HANYA JSON CadSpec valid.",
                        failureReason, normalizedPrompt);

                    try
                    {
                        string repairResp = _geminiClient.SendTextAsync(repairPrompt, null).GetAwaiter().GetResult();
                        CadSpec repairedSpec = _planner.ParseCadSpec(repairResp);
                        if (repairedSpec != null && _validator.Validate(repairedSpec).IsValid)
                        {
                            response.FinalSpec = repairedSpec;
                            execResult = nativeExecutor(repairedSpec);
                            response.ExecutionResult = execResult;
                            geoReport = GeometryVerifier.Verify(execResult, repairedSpec, activeVolumeMm3, isModification);
                            verif.IsValid = geoReport.IsValid;
                            verif.SolidBodyCount = execResult != null ? execResult.SolidBodyCount : 0;
                            verif.CalculatedVolumeMm3 = execResult != null ? execResult.VolumeMm3 : 0.0;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Warn("Auto-repair loop gagal: " + ex.Message);
                    }
                }
            }

            response.Success = verif.IsValid && (execResult != null && execResult.Success);
            response.UserMessage = response.Success
                ? string.Format("Model CAD berhasil dibuat! Solid Bodies: {0}, Volume: {1:N2} mm3", verif.SolidBodyCount, verif.CalculatedVolumeMm3)
                : string.Format("Eksekusi belum sempurna: {0}", execResult != null ? execResult.ErrorMessage : "Verifikasi geometri gagal.");

            ReportProgress(progressReporter, 8, response.Success ? "Selesai!" : "Eksekusi selesai dengan catatan.");
            return response;
        }

        private static void ReportProgress(Action<int, string> reporter, int stage, string message)
        {
            if (reporter != null)
            {
                try { reporter(stage, message); } catch { }
            }
        }
    }
}
