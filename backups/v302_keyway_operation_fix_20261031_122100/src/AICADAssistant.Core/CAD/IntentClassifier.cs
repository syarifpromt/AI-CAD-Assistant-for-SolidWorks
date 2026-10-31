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
        Export,
        ResumePendingTask
    }

    /// <summary>
    /// Deterministic Intent Classifier for User Prompts in AICADAssistant v3.0.
    /// Strictly differentiates between new model generation, active model revision,
    /// conversational queries (which must never trigger CAD execution), and export requests.
    /// Implements document context isolation and primary intent prioritization.
    /// </summary>
    public static class IntentClassifier
    {
        private static readonly Regex ResumePendingTaskPattern = new Regex(
            @"^\s*(?:kerjakan|coba\s*lagi|lanjutkan|perbaiki|ulang|eksekusi|teruskan|lakukan|gas|jalan|jalankan|lanjut)\s*[\.!]?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        // Matches explicit requests to create a whole new root mechanical component / part
        private static readonly Regex PrimaryComponentCreationPattern = new Regex(
            @"\b(?:buat|buatkan|bikin|gambar|rancang|modelkan|desain|design|create|generate)\s+(?:sebuah\s+|suatu\s+|satu\s+|model\s+|part\s+)?(?:poros|shaft|puli|pulley|pelat|plate|bracket|siku|balok|block|kotak|box|kunci|wrench|silinder|cylinder|setengah\s+bola|hemisphere|kubah|kubus|cube)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Matches modification verbs / actions targeting an existing model
        private static readonly Regex ModificationKeywordsPattern = new Regex(
            @"\b(?:tambah|tambahkan|pasang|pasangkan|buatkan\s+lubang|buat\s+lubang|lubangi|coak|coakan|potong|potongkan|fillet|chamfer|revisi|ubah|ganti|perpanjang|perpendek|modifikasi)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ModificationTargetPattern = new Regex(
            @"\b(?:model\s+(?:aktif|ini|tersebut)|part\s+(?:aktif|ini|tersebut)|pelat\s+aktif|poros\s+aktif|pada\s+permukaan|pada\s+sisi|di\s+tengah|di\s+atas|di\s+bawah|di\s+dasar|di\s+ujung)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static UserIntent Classify(string text, CadSpec currentSpec, int activeBodyCount)
        {
            string reason;
            return Classify(text, currentSpec, activeBodyCount, false, out reason);
        }

        public static UserIntent Classify(string text, CadSpec currentSpec, int activeBodyCount, out string reason)
        {
            return Classify(text, currentSpec, activeBodyCount, false, out reason);
        }

        public static UserIntent Classify(string text, CadSpec currentSpec, int activeBodyCount, bool hasPendingTask)
        {
            string reason;
            return Classify(text, currentSpec, activeBodyCount, hasPendingTask, out reason);
        }

        public static UserIntent Classify(string text, CadSpec currentSpec, int activeBodyCount, bool hasPendingTask, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                reason = "Teks prompt kosong.";
                return UserIntent.Conversational;
            }

            string clean = text.Trim();

            // 0. Resume Pending Task Check (if there is an active pending CAD task on current document)
            if (ResumePendingTaskPattern.IsMatch(clean))
            {
                if (hasPendingTask)
                {
                    reason = "Frasa lanjutan pengguna mendeteksi adanya pending CAD task pada dokumen aktif.";
                    return UserIntent.ResumePendingTask;
                }
                else
                {
                    reason = "Frasa lanjutan pengguna diterima tetapi tidak ada tugas CAD tertunda untuk dilanjutkan.";
                    return UserIntent.Conversational;
                }
            }

            // 1. Export Intent Check
            if (ExportPattern.IsMatch(clean))
            {
                reason = "Perintah ekspor berkas CAD terdeteksi.";
                return UserIntent.Export;
            }

            // 2. Conversational Intent Check (Question / Explanation / Greeting)
            bool hasCreateCommand = Regex.IsMatch(clean, @"\b(?:buat|buatkan|bikin|gambar|modelkan|rancang)\s+(?:sebuah\s+|suatu\s+|satu\s+|model\s+|part\s+)?(?:poros|shaft|puli|pelat|bracket|balok|kunci|silinder|lubang|fillet|chamfer)\b", RegexOptions.IgnoreCase);
            bool hasDirectModCommand = Regex.IsMatch(clean, @"\b(?:tambahkan|pasang|pasangkan|lubangi|revisi|modifikasi)\s+(?:pada|di|ke)\b", RegexOptions.IgnoreCase);

            if (ConversationalPattern.IsMatch(clean) || QuestionPattern.IsMatch(clean))
            {
                if (!hasCreateCommand && !hasDirectModCommand)
                {
                    reason = "Pertanyaan percakapan atau informasi model tanpa instruksi pemodelan CAD.";
                    return UserIntent.Conversational;
                }
            }

            // A pure question ending in '?' without explicit modeling dimensions or creation commands is conversational
            if (clean.EndsWith("?") && !Regex.IsMatch(clean, @"\b(?:buat|buatkan|bikin|tambah|tambahkan)\s+\w+.*?\b(?:mm|cm|m)\b", RegexOptions.IgnoreCase))
            {
                reason = "Pertanyaan percakapan bertanda tanya tanpa instruksi dimensi CAD.";
                return UserIntent.Conversational;
            }

            // 3. Explicit New Model Takes Precedence
            if (ExplicitNewModelPattern.IsMatch(clean))
            {
                reason = "Instruksi eksplisit pembuatan model baru terdeteksi.";
                return UserIntent.CreateNewModel;
            }

            // 4. Primary Root Component Creation Overrides Secondary Feature Additions
            // E.g. "Buat poros bertingkat dengan lima bagian dan tambahkan lubang aksial" -> CreateNewModel
            // E.g. "Buat puli dengan bore dan keyway" -> CreateNewModel
            if (PrimaryComponentCreationPattern.IsMatch(clean))
            {
                reason = "Instruksi eksplisit pembuatan komponen utama baru terdeteksi; mengungguli frasa penambahan fitur sekunder.";
                return UserIntent.CreateNewModel;
            }

            // 5. Empty Document Check (Document Context Isolation)
            // A modification request STRICTLY requires an active model with at least one solid body (activeBodyCount > 0).
            // An empty document (activeBodyCount == 0) CANNOT be modified regardless of previous specs.
            if (activeBodyCount <= 0)
            {
                reason = string.Format("Dokumen aktif kosong atau tidak memiliki solid body (Bodies={0}); diklasifikasikan sebagai pembuatan model baru.", activeBodyCount);
                return UserIntent.CreateNewModel;
            }

            // 6. Modify Existing Model Check (Requires activeBodyCount > 0 AND revision intent)
            bool hasModKeywords = ModificationKeywordsPattern.IsMatch(clean);
            bool hasModTarget = ModificationTargetPattern.IsMatch(clean);
            bool startsWithFeature = Regex.IsMatch(clean, @"^(?:tangkai|batang|lubang|silinder|fillet|chamfer|coakan)\b", RegexOptions.IgnoreCase);

            if (hasModKeywords || (hasModTarget && startsWithFeature) || startsWithFeature)
            {
                reason = string.Format("Instruksi revisi/penambahan fitur terdeteksi pada model aktif valid (Bodies={0}).", activeBodyCount);
                return UserIntent.ModifyExistingModel;
            }

            // 7. Default: Create New Model
            reason = string.Format("Tidak ditemukan instruksi revisi eksplisit; membuat model baru (Bodies={0}).", activeBodyCount);
            return UserIntent.CreateNewModel;
        }
    }
}

