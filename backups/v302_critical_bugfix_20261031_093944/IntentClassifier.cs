using System;
using System.Text.RegularExpressions;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Core.CAD
{
    public enum UserIntent
    {
        CreateNewModel,
        ModifyExistingModel,
        Conversational,
        Export
    }

    /// <summary>
    /// Deterministic Intent Classifier for User Prompts in AICADAssistant v2.0.
    /// Strictly differentiates between new model generation, active model revision,
    /// conversational queries (which must never trigger CAD execution), and export requests.
    /// </summary>
    public static class IntentClassifier
    {
        private static readonly Regex ConversationalPattern = new Regex(
            @"\b(?:apa\s+(?:itu|artinya|maksud|fungsi|perbedaan)|bagaimana\s+(?:cara|alur|proses)|mengapa|kenapa|tolong\s+jelaskan|jelaskan|terangkan|siapa\s+kamu|kamu\s+siapa|halo|hai|selamat\s+(?:pagi|siang|sore|malam)|terima\s+kasih|makasih|thank\s+you|thanks|help|bantuan)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex QuestionPattern = new Regex(
            @"\b(?:berapa\s+(?:volume|luas|ukuran|dimensi|berat|massa|panjang|tebal|diameter)|apakah\s+(?:model|fitur|sketsa|dokumen)|ada\s+berapa\s+body)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ExportPattern = new Regex(
            @"\b(?:ekspor|export|simpan\s+sebagai|save\s+as)\s+(?:ke\s+)?(?:step|stp|stl|iges|igs|dxf|pdf)\b|^(?:ekspor|export)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ExplicitNewModelPattern = new Regex(
            @"\b(?:buat(?:\s+(?:model|part|komponen|dokumen))?\s+baru|model\s+baru|part\s+baru|dokumen\s+baru|mulai\s+dari\s+awal|reset\s+model|bikin\s+baru)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ModificationKeywordsPattern = new Regex(
            @"\b(?:tambah|tambahkan|pasang|pasangkan|buatkan\s+lubang|buat\s+lubang|lubangi|coak|coakan|potong|potongkan|extrude|fillet|chamfer|revisi|ubah|ganti|perpanjang|perpendek|modifikasi|di\s+tengah|pada\s+permukaan|pada\s+sisi|di\s+atas|di\s+bawah|di\s+dasar|di\s+ujung)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static UserIntent Classify(string text, CadSpec currentSpec, int activeBodyCount)
        {
            if (string.IsNullOrWhiteSpace(text))
                return UserIntent.Conversational;

            string clean = text.Trim();

            // 1. Export Intent Check
            if (ExportPattern.IsMatch(clean))
            {
                return UserIntent.Export;
            }

            // 2. Conversational Intent Check (Question / Explanation / Greeting)
            // If it's clearly conversational and doesn't contain geometric creation keywords
            if (ConversationalPattern.IsMatch(clean) || QuestionPattern.IsMatch(clean))
            {
                // Ensure it doesn't also say "buatkan model..." or "buat pelat..."
                bool hasCreateVerb = Regex.IsMatch(clean, @"\b(?:buat|buatkan|bikin|gambar|modelkan|rancang)\b", RegexOptions.IgnoreCase);
                if (!hasCreateVerb)
                {
                    return UserIntent.Conversational;
                }
            }

            // A pure question ending in '?' without dimensions or creation commands is conversational
            if (clean.EndsWith("?") && !Regex.IsMatch(clean, @"\b(?:buat|buatkan|bikin|tambah|tambahkan|mm|cm|m)\b", RegexOptions.IgnoreCase))
            {
                return UserIntent.Conversational;
            }

            // 3. Explicit New Model Takes Precedence
            if (ExplicitNewModelPattern.IsMatch(clean))
            {
                return UserIntent.CreateNewModel;
            }

            // 4. Modify Existing Model Check
            // If active document has solid bodies or active spec exists
            if (activeBodyCount > 0 || currentSpec != null)
            {
                if (ModificationKeywordsPattern.IsMatch(clean))
                {
                    return UserIntent.ModifyExistingModel;
                }

                // If prompt starts with feature words on existing model
                if (Regex.IsMatch(clean, @"^(?:tangkai|batang|lubang|silinder|fillet|chamfer|coakan)\b", RegexOptions.IgnoreCase))
                {
                    return UserIntent.ModifyExistingModel;
                }
            }

            // 5. Default: If not conversational, not export, not revision -> Create New Model
            return UserIntent.CreateNewModel;
        }
    }
}

