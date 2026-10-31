using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Models
{
    public enum DocumentType
    {
        Part = 0,
        Assembly = 1,
        Drawing = 2
    }

    public enum CadSource
    {
        Text = 0,
        Image = 1,
        Document = 2
    }

    public enum ConfidenceLevel
    {
        Explicit = 0,
        Interpreted = 1,
        Unknown = 2
    }

    public class CadSpec
    {
        public string RequestId { get; set; }
        public DocumentType DocumentType { get; set; }
        public string Units { get; set; }
        public CadSource Source { get; set; }
        public Dictionary<string, double> ExplicitDimensions { get; set; }
        public Dictionary<string, double> InferredValues { get; set; }
        public List<string> Unknowns { get; set; }
        public List<CadOperation> Operations { get; set; }
        public bool RequiresConfirmation { get; set; }
        public List<string> ExportTargets { get; set; }
        public string Notes { get; set; }
        public string TargetDocumentId { get; set; }
        public string TargetDocumentTitle { get; set; }

        public CadSpec()
        {
            Units = "mm";
            ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            InferredValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Unknowns = new List<string>();
            Operations = new List<CadOperation>();
            ExportTargets = new List<string>();
        }

        public bool HasUnknowns()
        {
            return Unknowns != null && Unknowns.Count > 0;
        }

        public ConfidenceLevel GetConfidenceLevel()
        {
            if (HasUnknowns())
                return ConfidenceLevel.Unknown;

            if (InferredValues != null && InferredValues.Count > 0)
                return ConfidenceLevel.Interpreted;

            return ConfidenceLevel.Explicit;
        }

        public override string ToString()
        {
            return string.Format("CadSpec[{0}] Type={1} Ops={2} Confidence={3}",
                RequestId ?? "?",
                DocumentType,
                Operations != null ? Operations.Count : 0,
                GetConfidenceLevel());
        }
    }
}
