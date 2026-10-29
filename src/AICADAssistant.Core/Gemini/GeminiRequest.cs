using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Gemini
{
    public class GeminiInlineData
    {
        public string mime_type { get; set; }
        public string data { get; set; } // Base64 encoded
    }

    public class GeminiPart
    {
        public string text { get; set; }
        public GeminiInlineData inline_data { get; set; }
    }

    public class GeminiContent
    {
        public string role { get; set; }
        public List<GeminiPart> parts { get; set; }

        public GeminiContent()
        {
            parts = new List<GeminiPart>();
        }
    }

    public class GeminiGenerationConfig
    {
        public double temperature { get; set; }
        public int maxOutputTokens { get; set; }
        public string responseMimeType { get; set; }

        public GeminiGenerationConfig()
        {
            temperature = 0.2;
            maxOutputTokens = 4096;
            responseMimeType = "application/json";
        }
    }

    public class GeminiTextRequest
    {
        public List<GeminiContent> contents { get; set; }
        public GeminiGenerationConfig generationConfig { get; set; }
        public GeminiContent systemInstruction { get; set; }

        public GeminiTextRequest()
        {
            contents = new List<GeminiContent>();
            generationConfig = new GeminiGenerationConfig();
        }
    }
}
