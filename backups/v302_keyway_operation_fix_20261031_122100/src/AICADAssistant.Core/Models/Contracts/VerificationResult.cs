using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Models.Contracts
{
    public class VerificationResult
    {
        public bool IsValid { get; set; }
        public int SolidBodyCount { get; set; }
        public double CalculatedVolumeMm3 { get; set; }
        public double TargetVolumeMm3 { get; set; }
        public double VolumeDeviationPercent { get; set; }
        public double[] BoundingBoxMm { get; set; }
        public int RebuildErrorCode { get; set; }
        public string RebuildErrorMessage { get; set; }
        public List<string> FailedFeatureNames { get; set; }
        public List<CADDiagnostic> Diagnostics { get; set; }
        public bool IsPhysicallySound { get; set; }

        public VerificationResult()
        {
            BoundingBoxMm = new double[3];
            FailedFeatureNames = new List<string>();
            Diagnostics = new List<CADDiagnostic>();
            IsValid = true;
            IsPhysicallySound = true;
        }

        public static VerificationResult CreateSuccess(int bodyCount, double volumeMm3, double[] bboxMm)
        {
            return new VerificationResult
            {
                IsValid = true,
                IsPhysicallySound = true,
                SolidBodyCount = bodyCount,
                CalculatedVolumeMm3 = volumeMm3,
                BoundingBoxMm = bboxMm ?? new double[3]
            };
        }

        public static VerificationResult CreateFailure(string reason, int rebuildError = 0)
        {
            var res = new VerificationResult
            {
                IsValid = false,
                IsPhysicallySound = false,
                RebuildErrorCode = rebuildError,
                RebuildErrorMessage = reason
            };
            res.Diagnostics.Add(CADDiagnostic.CreateError(
                DiagnosticStage.PostVerification,
                "ERR_VERIFICATION_FAILED",
                reason));
            return res;
        }

        public override string ToString()
        {
            return string.Format("VerificationResult[Valid={0}, Bodies={1}, Vol={2:F2} mm3, Sound={3}]",
                IsValid,
                SolidBodyCount,
                CalculatedVolumeMm3,
                IsPhysicallySound);
        }
    }
}

