using System;

namespace AICADAssistant.Core.Models.Contracts
{
    public enum DiagnosticSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Fatal = 3
    }

    public enum DiagnosticStage
    {
        InputNormalizer = 0,
        IntentClassifier = 1,
        ArchitectPlanning = 2,
        CadCompiler = 3,
        PreValidation = 4,
        NativeExecution = 5,
        PostVerification = 6,
        AutoRepair = 7
    }

    public class CADDiagnostic
    {
        public DiagnosticSeverity Severity { get; set; }
        public DiagnosticStage Stage { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string Remediation { get; set; }
        public string TargetOperationId { get; set; }
        public DateTime Timestamp { get; set; }

        public CADDiagnostic()
        {
            Timestamp = DateTime.Now;
            Severity = DiagnosticSeverity.Info;
        }

        public static CADDiagnostic CreateError(DiagnosticStage stage, string code, string message, string remediation = null, string opId = null)
        {
            return new CADDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Stage = stage,
                Code = code,
                Message = message,
                Remediation = remediation,
                TargetOperationId = opId
            };
        }

        public static CADDiagnostic CreateWarning(DiagnosticStage stage, string code, string message, string remediation = null, string opId = null)
        {
            return new CADDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Stage = stage,
                Code = code,
                Message = message,
                Remediation = remediation,
                TargetOperationId = opId
            };
        }

        public static CADDiagnostic CreateInfo(DiagnosticStage stage, string code, string message)
        {
            return new CADDiagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Stage = stage,
                Code = code,
                Message = message
            };
        }

        public override string ToString()
        {
            return string.Format("[{0}] [{1}] {2}: {3}{4}",
                Severity,
                Stage,
                Code ?? "NO_CODE",
                Message ?? string.Empty,
                string.IsNullOrEmpty(Remediation) ? string.Empty : string.Format(" (Remediation: {0})", Remediation));
        }
    }
}

