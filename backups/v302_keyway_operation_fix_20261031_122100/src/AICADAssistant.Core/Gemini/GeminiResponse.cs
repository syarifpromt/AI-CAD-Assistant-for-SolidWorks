using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Gemini
{
    public class GeminiResponse
    {
        public List<GeminiCandidate> candidates { get; set; }
        public GeminiUsage usageMetadata { get; set; }

        public GeminiResponse()
        {
            candidates = new List<GeminiCandidate>();
        }

        public string GetFirstText()
        {
            if (candidates != null && candidates.Count > 0)
            {
                var candidate = candidates[0];
                if (candidate != null && candidate.content != null && candidate.content.parts != null)
                {
                    foreach (var part in candidate.content.parts)
                    {
                        if (!string.IsNullOrEmpty(part.text))
                        {
                            return part.text;
                        }
                    }
                }
            }
            return null;
        }
    }

    public class GeminiCandidate
    {
        public GeminiContent content { get; set; }
        public string finishReason { get; set; }
        public int index { get; set; }
    }

    public class GeminiUsage
    {
        public int promptTokenCount { get; set; }
        public int candidatesTokenCount { get; set; }
        public int totalTokenCount { get; set; }
        public int thoughtsTokenCount { get; set; }
    }

    public class GeminiCallResult
    {
        public string Text { get; set; }
        public int StatusCode { get; set; }
        public long LatencyMs { get; set; }
        public int PromptTokens { get; set; }
        public int CandidateTokens { get; set; }
        public int ThoughtTokens { get; set; }
        public int TotalTokens { get; set; }
        public string ModelName { get; set; }

        public GeminiCallResult()
        {
            StatusCode = 200;
        }
    }

    public class GeminiErrorResponse
    {
        public GeminiError error { get; set; }
    }

    public class GeminiError
    {
        public int code { get; set; }
        public string message { get; set; }
        public string status { get; set; }
    }
}
