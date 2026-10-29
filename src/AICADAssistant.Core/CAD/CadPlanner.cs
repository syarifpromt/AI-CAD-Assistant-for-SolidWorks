using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;

namespace AICADAssistant.Core.CAD
{
    /// <summary>
    /// Translates conversational prompts and Gemini responses into typed CadSpec structures.
    /// Ensures JSON extraction and schema compliance.
    /// </summary>
    public class CadPlanner
    {
        private readonly JavaScriptSerializer _serializer;

        public const string CadSpecJsonSchemaExample = @"
{
  ""RequestId"": ""req-001"",
  ""DocumentType"": 0, /* 0=Part, 1=Assembly, 2=Drawing */
  ""Units"": ""mm"",
  ""Source"": 0, /* 0=Text, 1=Image, 2=Document */
  ""ExplicitDimensions"": { ""panjang"": 100.0, ""lebar"": 50.0, ""tinggi"": 20.0 },
  ""InferredValues"": {},
  ""Unknowns"": [],
  ""Operations"": [
    {
      ""Id"": ""sk1"",
      ""Type"": 1, /* 1=NewSketch */
      ""Parameters"": { ""plane"": ""Front"" },
      ""DependsOn"": [],
      ""Description"": ""Buat sketch di Front Plane""
    },
    {
      ""Id"": ""rect1"",
      ""Type"": 3, /* 3=AddRectangle */
      ""Parameters"": { ""width"": 100.0, ""height"": 50.0 },
      ""DependsOn"": [""sk1""],
      ""Description"": ""Gambar persegi panjang 100x50 mm""
    },
    {
      ""Id"": ""ext1"",
      ""Type"": 6, /* 6=ExtrudeBoss */
      ""Parameters"": { ""depth"": 20.0, ""profile"": ""rect1"" },
      ""DependsOn"": [""rect1""],
      ""Description"": ""Extrude boss 20 mm""
    }
  ],
  ""RequiresConfirmation"": false,
  ""ExportTargets"": [""STEP"", ""STL""],
  ""Notes"": ""Model balok dasar""
}";

        public CadPlanner()
        {
            _serializer = new JavaScriptSerializer
            {
                MaxJsonLength = 50 * 1024 * 1024
            };
        }

        public string ExtractJsonFromResponse(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return null;

            string text = rawResponse.Trim();

            // Check if wrapped in markdown code fence ```json ... ``` or ``` ... ```
            var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // Find first '{' and last '}'
            int firstBrace = text.IndexOf('{');
            int lastBrace = text.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return text.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return text;
        }

        public CadSpec ParseCadSpec(string jsonResponse)
        {
            if (string.IsNullOrWhiteSpace(jsonResponse))
                return null;

            try
            {
                string cleanJson = ExtractJsonFromResponse(jsonResponse);
                var spec = _serializer.Deserialize<CadSpec>(cleanJson);
                if (spec != null && string.IsNullOrEmpty(spec.RequestId))
                {
                    spec.RequestId = "req_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                }
                return spec;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal mem-parsing CadSpec JSON: " + ex.Message);
                return null;
            }
        }

        public string BuildPromptForTextRequest(string userMessage)
        {
            return "Pengguna meminta pembuatan model 3D di SOLIDWORKS dengan deskripsi berikut:\n\"" +
                   userMessage + "\"\n\n" +
                   "Tugasmu:\n" +
                   "1. Identifikasi bentuk, dimensi eksplisit, dan satuan ukuran (default: mm).\n" +
                   "2. Jika ada dimensi kritis yang belum diketahui atau tidak masuk akal, tambahkan ke dalam array 'Unknowns'.\n" +
                   "3. Petakan urutan operasi pemodelan CAD native yang valid ke dalam array 'Operations'.\n" +
                   "Daftar OperationType: 0=NewPart, 1=NewSketch, 2=AddLine, 3=AddRectangle, 4=AddCircle, 5=AddDimension, " +
                   "6=ExtrudeBoss, 7=ExtrudeCut, 8=RevolvedBoss, 9=Fillet, 10=Chamfer, 11=SimpleHole, 12=LinearPattern, 13=CircularPattern, 14=SavePart, 15=ExportStep, 16=ExportStl.\n\n" +
                   "PENTING: Jangan membuat script C# atau macro VBA. Kembalikan HANYA format JSON CadSpec valid berikut:\n" +
                   CadSpecJsonSchemaExample;
        }

        public string BuildPromptForRevision(string revisionMessage, CadSpec currentSpec)
        {
            string currentJson = currentSpec != null ? _serializer.Serialize(currentSpec) : "{}";
            return "Model CAD saat ini memiliki spesifikasi berikut:\n" +
                   currentJson + "\n\n" +
                   "Pengguna meminta revisi berikut:\n\"" +
                   revisionMessage + "\"\n\n" +
                   "Tugasmu:\n" +
                   "1. Perbarui spesifikasi di atas dengan merevisi parameter yang relevan atau menambahkan operasi yang diperlukan.\n" +
                   "2. Jangan menghapus fitur yang sudah ada kecuali diminta pengguna.\n" +
                   "3. Pertahankan konsistensi geometris dan satuan.\n" +
                   "4. Kembalikan HANYA JSON CadSpec yang telah direvisi.";
        }

        public string BuildPromptForImageAnalysis(string userInstructions)
        {
            string extra = string.IsNullOrWhiteSpace(userInstructions)
                ? "Analisis gambar teknik ini secara mendetail."
                : userInstructions;

            return extra + "\n\n" +
                   "Tugasmu:\n" +
                   "1. Analisis tampak (Front, Top, Right, Isometrik) yang terlihat pada gambar.\n" +
                   "2. Ekstraksi semua dimensi numerik yang terbaca dengan jelas (panjang, lebar, tebal, diameter lubang, radius fillet).\n" +
                   "3. Jika ada angka dimensi yang buram, ambigu, atau tidak tercantum, masukkan ke array 'Unknowns'.\n" +
                   "4. Susun urutan pemodelan CAD native untuk membuat komponen ini di SOLIDWORKS.\n" +
                   "Kembalikan HANYA format JSON CadSpec valid sesuai skema yang telah ditentukan.";
        }

        public string BuildClarificationMessage(List<string> missingDimensions)
        {
            if (missingDimensions == null || missingDimensions.Count == 0)
                return "Mohon lengkapi spesifikasi dimensi komponen.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Untuk memastikan akurasi geometri, mohon berikan informasi dimensi berikut yang belum tercantum:");
            for (int i = 0; i < missingDimensions.Count; i++)
            {
                sb.AppendLine(string.Format("{0}. {1}", i + 1, missingDimensions[i]));
            }
            return sb.ToString().TrimEnd();
        }
    }
}

