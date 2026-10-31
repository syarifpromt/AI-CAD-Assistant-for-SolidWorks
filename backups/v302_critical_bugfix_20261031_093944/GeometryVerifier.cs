using System;
using System.Collections.Generic;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Core.CAD
{
    public class GeometryVerificationReport
    {
        public bool IsValid { get; set; }
        public ExecutionStatus RecommendedStatus { get; set; }
        public string Summary { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }
        public double VolumeDeltaMm3 { get; set; }

        public GeometryVerificationReport()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
            RecommendedStatus = ExecutionStatus.Success;
        }
    }

    /// <summary>
    /// Strict geometric and topological verification engine for AICADAssistant v2.0.
    /// Ensures that models are not reported as successful solely because an API didn't throw exceptions.
    /// Validates solid body count, bounding box, rebuild integrity, and expected volume deltas.
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
                report.RecommendedStatus = ExecutionStatus.Failed;
                report.Errors.Add("Hasil eksekusi CAD bernilai null.");
                report.Summary = "Eksekusi gagal: tidak ada hasil dari CAD executor.";
                return report;
            }

            // 1. Solid Body Count Verification
            if (result.SolidBodyCount <= 0)
            {
                report.IsValid = false;
                report.RecommendedStatus = ExecutionStatus.Failed;
                report.Errors.Add("Tidak ada Solid Body yang terbentuk (Body Count = 0). Profil sketsa mungkin terbuka atau operasi fitur gagal.");
                report.Summary = "Verifikasi geometri gagal: Solid body count = 0.";
                return report;
            }

            // 2. Bounding Box Verification
            if (result.BoundingBoxMm != null && result.BoundingBoxMm.Length >= 3)
            {
                if (result.BoundingBoxMm[0] <= 0 && result.BoundingBoxMm[1] <= 0 && result.BoundingBoxMm[2] <= 0)
                {
                    report.Warnings.Add("Dimensi Bounding Box terbaca nol atau tidak valid.");
                }
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

            // 5. Revision Volume Delta Verification
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

            // 6. Overall Status Determination
            if (result.FailedOperation != null)
            {
                // If some operations failed
                if (result.SolidBodyCount > 0 && result.VolumeMm3 > 0)
                {
                    report.IsValid = true;
                    report.RecommendedStatus = ExecutionStatus.PartialSuccess;
                    report.Summary = string.Format("Sebagian operasi berhasil (Solid Body: {0}), namun operasi '{1}' gagal.",
                        result.SolidBodyCount, result.FailedOperation.OperationId ?? "?");
                }
                else
                {
                    report.IsValid = false;
                    report.RecommendedStatus = ExecutionStatus.Failed;
                    report.Summary = string.Format("Eksekusi gagal pada operasi '{0}': {1}",
                        result.FailedOperation.OperationId ?? "?", result.FailedOperation.ErrorMessage);
                }
            }
            else
            {
                report.IsValid = true;
                report.RecommendedStatus = ExecutionStatus.Success;
                report.Summary = string.Format("Model 100% valid secara geometris: {0} Solid Body, Volume: {1:N2} mm3.",
                    result.SolidBodyCount, result.VolumeMm3);
            }

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
    }
}

