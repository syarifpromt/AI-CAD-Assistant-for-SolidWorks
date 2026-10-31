using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Models
{
    public enum ExecutionStatus
    {
        Success,
        PartialSuccess,
        Failed,
        Cancelled
    }

    public class ExecutionResult
    {
        public bool Success { get; set; }
        public ExecutionStatus OverallStatus { get; set; }
        public List<OperationResult> CompletedOperations { get; set; }
        public OperationResult FailedOperation { get; set; }
        public string ErrorMessage { get; set; }
        public int SolidBodyCount { get; set; }
        public double[] BoundingBoxMm { get; set; }
        public double VolumeMm3 { get; set; }
        public string DocumentTitle { get; set; }
        public string FeatureName { get; set; }
        public bool RebuildSuccess { get; set; }
        public string SavedFilePath { get; set; }
        public List<string> ExportedFilePaths { get; set; }
        public string CheckpointPath { get; set; }
        public long DurationMs { get; set; }
        public List<string> FeatureWarnings { get; set; }

        public ExecutionResult()
        {
            CompletedOperations = new List<OperationResult>();
            BoundingBoxMm = new double[3];
            ExportedFilePaths = new List<string>();
            FeatureWarnings = new List<string>();
        }

        public static ExecutionResult CreateSuccess(string savedPath, int bodyCount, double[] bboxMm, long durationMs)
        {
            return new ExecutionResult
            {
                Success = true,
                OverallStatus = ExecutionStatus.Success,
                SavedFilePath = savedPath,
                SolidBodyCount = bodyCount,
                BoundingBoxMm = bboxMm ?? new double[3],
                DurationMs = durationMs
            };
        }

        public static ExecutionResult CreateFailure(string errorMessage, OperationResult failedOp = null)
        {
            return new ExecutionResult
            {
                Success = false,
                OverallStatus = ExecutionStatus.Failed,
                ErrorMessage = errorMessage,
                FailedOperation = failedOp
            };
        }

        public static ExecutionResult CreateCancelled()
        {
            return new ExecutionResult
            {
                Success = false,
                OverallStatus = ExecutionStatus.Cancelled,
                ErrorMessage = "Operasi dibatalkan oleh pengguna."
            };
        }
    }
}
