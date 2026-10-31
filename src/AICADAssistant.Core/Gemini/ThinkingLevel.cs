using System;
using System.Text.RegularExpressions;

namespace AICADAssistant.Core.Gemini
{
    public enum ThinkingLevel
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    public static class AdaptiveThinkingManager
    {
        private static readonly Regex AdvancedGeometryPattern = new Regex(
            @"\b(?:loft|sweep|helical|spiral|organic|organik|aerodinamis|kompleks|bebas|freeform|surface|spline|3d\s*sketch|manifold)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MechanicalCadPattern = new Regex(
            @"\b(?:kunci\s*pas|wrench|spanner|bracket|l-bracket|siku|pulley|puli|v-groove|poros|shaft|bertingkat|gear|roda\s*gigi|bearing|flange|kopling|coupling|slot|engsel|hinge|chassis|enclosure|mekanik|assembly)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ThinkingLevel DetermineLevel(string prompt, bool isConversational)
        {
            if (isConversational || string.IsNullOrWhiteSpace(prompt))
            {
                return ThinkingLevel.Low;
            }

            string clean = prompt.Trim();

            // 1. Advanced Geometry -> High thinking
            if (AdvancedGeometryPattern.IsMatch(clean))
            {
                return ThinkingLevel.High;
            }

            // 2. Mechanical CAD with multi-features -> Medium thinking
            if (MechanicalCadPattern.IsMatch(clean))
            {
                return ThinkingLevel.Medium;
            }

            // 3. Basic CAD (primitives: plate, block, cylinder, hemisphere, simple hole) -> Low thinking
            return ThinkingLevel.Low;
        }

        public static string ToApiString(ThinkingLevel level)
        {
            switch (level)
            {
                case ThinkingLevel.High:
                    return "high";
                case ThinkingLevel.Medium:
                    return "medium";
                case ThinkingLevel.Low:
                default:
                    return "low";
            }
        }
    }
}

