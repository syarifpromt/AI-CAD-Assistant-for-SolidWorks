using System;

namespace AICADAssistant.Core.Models
{
    /// <summary>
    /// Tracks a pending CAD revision or creation task that encountered planning or validation issues,
    /// enabling the user to resume or retry seamlessly with conversational continuation phrases.
    /// </summary>
    public class PendingCadTask
    {
        public string OriginalPrompt { get; set; }
        public string TargetDocumentId { get; set; }
        public string TargetDocumentTitle { get; set; }
        public int BodyCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string FailureReason { get; set; }
        public CadSpec UnexecutedSpec { get; set; }

        public PendingCadTask()
        {
            CreatedAt = DateTime.UtcNow;
        }

        public PendingCadTask(string prompt, string docId, string docTitle, int bodyCount, string reason = null, CadSpec spec = null)
        {
            OriginalPrompt = prompt;
            TargetDocumentId = docId;
            TargetDocumentTitle = docTitle;
            BodyCount = bodyCount;
            FailureReason = reason;
            UnexecutedSpec = spec;
            CreatedAt = DateTime.UtcNow;
        }

        public bool MatchesDocument(string currentDocId, string currentDocTitle)
        {
            if (string.IsNullOrWhiteSpace(TargetDocumentId) && string.IsNullOrWhiteSpace(TargetDocumentTitle))
                return true;

            if (!string.IsNullOrWhiteSpace(currentDocId) &&
                string.Equals(TargetDocumentId, currentDocId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(currentDocTitle) &&
                string.Equals(TargetDocumentTitle, currentDocTitle, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}

