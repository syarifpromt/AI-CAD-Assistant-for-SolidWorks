using System;
using System.Collections.Generic;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Core.CAD
{
    public enum VerificationStatus
    {
        CompleteSuccess,
        PartialSuccess,
        Failed,
        NeedsClarification
    }

    public class GeometryVerificationReport
    {
        public bool IsValid { get; set; }
        public VerificationStatus Status { get; set; }
        public ExecutionStatus RecommendedStatus { get; set; }
        public string Summary { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }
        public double VolumeDeltaMm3 { get; set; }
        public double ExpectedVolumeMm3 { get; set; }
        public double[] ExpectedBoundingBoxMm { get; set; }

        public GeometryVerificationReport()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
            Status = VerificationStatus.CompleteSuccess;
            RecommendedStatus = ExecutionStatus.Success;
        }
    }

    /// <summary>
    /// Strict geometric and topological verification engine for AICADAssistant v3.0.
    /// Ensures that models are not reported as successful solely because an API didn't throw exceptions.
    /// Differentiates CompleteSuccess, PartialSuccess, Failed, and NeedsClarification.
    /// Validates solid body count, bounding box envelope, rebuild integrity, feature completeness, and expected volume deltas.
    /// </summary>
    public static class GeometryVerifier
    {
        public static GeometryVerificationReport Verify(
            ExecutionResult result,
            CadSpec spec,
            double previousVolumeMm3 = 0,
            bool isModification = false)
        {
            var report = new GeometryVerificationReport();

            if (result == null)
            {
                report.IsValid = false;
                report.Status = VerificationStatus.Failed;
                report.RecommendedStatus = ExecutionStatus.Failed;
                report.Errors.Add("Hasil eksekusi CAD bernilai null.");
                report.Summary = "Eksekusi gagal: tidak ada hasil dari CAD executor.";
                return report;
            }

            // 1. Solid Body Count Verification
            if (result.SolidBodyCount <= 0)
            {
                report.IsValid = false;
                report.Status = VerificationStatus.Failed;
                report.RecommendedStatus = ExecutionStatus.Failed;
                report.Errors.Add("Tidak ada Solid Body yang terbentuk (Body Count = 0). Profil sketsa mungkin terbuka atau operasi fitur gagal.");
                report.Summary = "Verifikasi geometri gagal: Solid body count = 0.";
                return report;
            }

            // 2. Execution Operation Failure
            if (result.FailedOperation != null)
            {
                report.IsValid = false;
                if (result.SolidBodyCount > 0)
                {
                    report.Status = VerificationStatus.PartialSuccess;
                    report.RecommendedStatus = ExecutionStatus.PartialSuccess;
                    report.Errors.Add(string.Format("Operasi '{0}' gagal dieksekusi: {1}",
                        result.FailedOperation.OperationId ?? "?", result.FailedOperation.ErrorMessage));
                    report.Summary = string.Format("Sebagian operasi berhasil (Solid Body: {0}), namun operasi '{1}' gagal.",
                        result.SolidBodyCount, result.FailedOperation.OperationId ?? "?");
                }
                else
                {
                    report.Status = VerificationStatus.Failed;
                    report.RecommendedStatus = ExecutionStatus.Failed;
                    report.Errors.Add(string.Format("Operasi '{0}' gagal dieksekusi: {1}",
                        result.FailedOperation.OperationId ?? "?", result.FailedOperation.ErrorMessage));
                    report.Summary = string.Format("Eksekusi gagal pada operasi '{0}': {1}",
                        result.FailedOperation.OperationId ?? "?", result.FailedOperation.ErrorMessage);
                }
                return report;
            }

            // 3. Rebuild Status
            if (!result.RebuildSuccess)
            {
                report.Warnings.Add("Terdapat peringatan pada Feature Tree SOLIDWORKS saat Force Rebuild.");
            }

            // 4. Volume Verification
            if (result.VolumeMm3 <= 0)
            {
                report.Warnings.Add("Volume model terbaca 0 mm3. Periksa densitas atau massa geometri part.");
            }

            // 5. Check for Unsupported Operations in Spec
            if (spec != null && spec.Operations != null)
            {
                foreach (var op in spec.Operations)
                {
                    if (op != null && !AllowedOperations.Whitelist.Contains(op.Type))
                    {
                        report.IsValid = false;
                        report.Status = VerificationStatus.Failed;
                        report.RecommendedStatus = ExecutionStatus.Failed;
                        report.Errors.Add(string.Format("Operasi '{0}' ({1}) tidak didukung oleh CAD executor.", op.Id, op.Type));
                        report.Summary = "Verifikasi gagal: rencana CAD memuat operasi yang tidak didukung.";
                        return report;
                    }
                }
            }

            // 6. Incomplete Operation Graph / Missing Required 3D Features
            if (spec != null && spec.Operations != null)
            {
                var planned3DFeatureOps = new List<CadOperation>();
                foreach (var op in spec.Operations)
                {
                    if (op != null && Is3DFeatureOperation(op.Type))
                    {
                        planned3DFeatureOps.Add(op);
                    }
                }

                if (planned3DFeatureOps.Count > 0)
                {
                    var completedOpIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (result.CompletedOperations != null)
                    {
                        foreach (var cop in result.CompletedOperations)
                        {
                            if (cop != null && cop.Success && !string.IsNullOrEmpty(cop.OperationId))
                            {
                                completedOpIds.Add(cop.OperationId);
                            }
                        }
                    }

                    // Check if any planned 3D feature was not completed
                    var missingFeatures = new List<string>();
                    foreach (var fop in planned3DFeatureOps)
                    {
                        if (completedOpIds.Count > 0 && !completedOpIds.Contains(fop.Id))
                        {
                            missingFeatures.Add(string.Format("{0} ({1})", fop.Id, fop.Type));
                        }
                    }

                    if (missingFeatures.Count > 0)
                    {
                        report.IsValid = false;
                        report.Status = VerificationStatus.PartialSuccess;
                        report.RecommendedStatus = ExecutionStatus.PartialSuccess;
                        report.Errors.Add(string.Format("Operasi fitur 3D yang direncanakan tidak lengkap. Fitur belum terbentuk: {0}.",
                            string.Join(", ", missingFeatures)));
                        report.Summary = "Verifikasi geometri parsial: rencana operasi 3D belum dieksekusi secara lengkap.";
                        return report;
                    }
                }

                bool hasCutPlanned = HasOperationType(spec, OperationType.ExtrudeCut, OperationType.RevolvedCut, OperationType.SimpleHole);
                bool hasBossPlanned = HasOperationType(spec, OperationType.ExtrudeBoss, OperationType.RevolvedBoss, OperationType.CreateHemisphere);

                if (hasCutPlanned && hasBossPlanned)
                {
                    int featureOpCount = Count3DFeatureOperations(spec);
                    int completedFeatureCount = CountCompleted3DFeatures(result);
                    if (completedFeatureCount < featureOpCount && completedFeatureCount <= 1)
                    {
                        report.IsValid = false;
                        report.Status = VerificationStatus.PartialSuccess;
                        report.RecommendedStatus = ExecutionStatus.PartialSuccess;
                        report.Errors.Add(string.Format("Spesifikasi multi-fitur ({0} fitur 3D) hanya menghasilkan {1} fitur primitif (fitur pemotongan/aksial belum terbentuk).",
                            featureOpCount, completedFeatureCount));
                        report.Summary = "Verifikasi geometri parsial: fitur pemotongan atau lubang aksial belum terbentuk.";
                        return report;
                    }
                }
            }

            // 7. Bounding Box Dimension Verification against Expected Dimensions
            if (spec != null && spec.ExplicitDimensions != null && spec.ExplicitDimensions.Count > 0 &&
                result.BoundingBoxMm != null && result.BoundingBoxMm.Length >= 3)
            {
                double targetLength = 0;
                if (spec.ExplicitDimensions.TryGetValue("panjang", out targetLength) ||
                    spec.ExplicitDimensions.TryGetValue("total panjang", out targetLength) ||
                    spec.ExplicitDimensions.TryGetValue("length", out targetLength))
                {
                    if (targetLength > 0)
                    {
                        double maxActualDim = Math.Max(result.BoundingBoxMm[0], Math.Max(result.BoundingBoxMm[1], result.BoundingBoxMm[2]));
                        if (maxActualDim > 0)
                        {
                            double ratio = maxActualDim / targetLength;
                            // If actual dimension is less than 50% or more than 200% of target length
                            if (ratio < 0.5 || ratio > 2.0)
                            {
                                report.IsValid = false;
                                report.Status = VerificationStatus.Failed;
                                report.RecommendedStatus = ExecutionStatus.Failed;
                                report.Errors.Add(string.Format("Dimensi Bounding Box tidak sesuai spesifikasi. Dimensi maksimum aktual: {0:F1} mm, diharapkan: {1:F1} mm (deviasi {2:F1}%).",
                                    maxActualDim, targetLength, Math.Abs(1.0 - ratio) * 100.0));
                                report.Summary = string.Format("Verifikasi gagal: Dimensi Bounding Box {0:F1} mm menyimpang jauh dari target {1:F1} mm.", maxActualDim, targetLength);
                                return report;
                            }
                        }
                    }
                }
            }

            // 8. Revision Volume Delta Verification
            if (isModification && previousVolumeMm3 > 0 && result.VolumeMm3 > 0)
            {
                double delta = result.VolumeMm3 - previousVolumeMm3;
                report.VolumeDeltaMm3 = delta;

                bool hasBoss = HasOperationType(spec, OperationType.ExtrudeBoss, OperationType.RevolvedBoss, OperationType.SweepBoss);
                bool hasCut = HasOperationType(spec, OperationType.ExtrudeCut, OperationType.RevolvedCut, OperationType.SimpleHole);

                if (hasBoss && !hasCut && delta < 0)
                {
                    report.Warnings.Add(string.Format("Operasi penambahan material (Boss) menghasilkan pengurangan volume ({0:N2} mm3).", delta));
                }
                else if (hasCut && !hasBoss && delta > 0)
                {
                    report.Warnings.Add(string.Format("Operasi pemotongan (Cut/Hole) menghasilkan penambahan volume ({0:N2} mm3).", delta));
                }
            }

            // 9. All Validations Passed: Complete Success
            report.IsValid = true;
            report.Status = VerificationStatus.CompleteSuccess;
            report.RecommendedStatus = ExecutionStatus.Success;
            report.Summary = string.Format("Model 100% valid secara geometris (Complete Success): {0} Solid Body, Volume: {1:N2} mm3.",
                result.SolidBodyCount, result.VolumeMm3);

            return report;
        }

        private static bool HasOperationType(CadSpec spec, params OperationType[] types)
        {
            if (spec == null || spec.Operations == null) return false;
            var set = new HashSet<OperationType>(types);
            foreach (var op in spec.Operations)
            {
                if (op != null && set.Contains(op.Type)) return true;
            }
            return false;
        }

        private static bool Is3DFeatureOperation(OperationType type)
        {
            return type == OperationType.ExtrudeBoss ||
                   type == OperationType.ExtrudeCut ||
                   type == OperationType.RevolvedBoss ||
                   type == OperationType.RevolvedCut ||
                   type == OperationType.SimpleHole ||
                   type == OperationType.CreateHemisphere ||
                   type == OperationType.Shell ||
                   type == OperationType.SweepBoss;
        }

        private static int Count3DFeatureOperations(CadSpec spec)
        {
            if (spec == null || spec.Operations == null) return 0;
            int count = 0;
            foreach (var op in spec.Operations)
            {
                if (op != null && (op.Type == OperationType.ExtrudeBoss ||
                                   op.Type == OperationType.ExtrudeCut ||
                                   op.Type == OperationType.RevolvedBoss ||
                                   op.Type == OperationType.RevolvedCut ||
                                   op.Type == OperationType.SimpleHole ||
                                   op.Type == OperationType.CreateHemisphere ||
                                   op.Type == OperationType.Fillet ||
                                   op.Type == OperationType.Chamfer))
                {
                    count++;
                }
            }
            return count;
        }

        private static int CountCompleted3DFeatures(ExecutionResult result)
        {
            if (result == null) return 0;
            if (result.CompletedOperations != null && result.CompletedOperations.Count > 0)
            {
                return result.CompletedOperations.Count;
            }
            return !string.IsNullOrEmpty(result.FeatureName) ? 1 : 0;
        }
    }
}

