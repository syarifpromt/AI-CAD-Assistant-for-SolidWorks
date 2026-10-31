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

        public const string CadSpecRevisionJsonSchemaExample = @"
{
  ""RequestId"": ""req-rev-001"",
  ""DocumentType"": 0,
  ""Units"": ""mm"",
  ""Operations"": [
    {
      ""Id"": ""rev_plane_keyway"",
      ""Type"": 30, /* 30=CreatePlane */
      ""Parameters"": { ""referencePlane"": ""Top"", ""offset"": 12.5, ""flip"": false },
      ""DependsOn"": [],
      ""Description"": ""Buat bidang referensi tangensial silinder Ø25 mm (offset 12.5 mm dari Top Plane)""
    },
    {
      ""Id"": ""rev_sk_keyway"",
      ""Type"": 1, /* 1=NewSketch */
      ""Parameters"": { ""plane"": ""rev_plane_keyway"" },
      ""DependsOn"": [""rev_plane_keyway""],
      ""Description"": ""Buka sketsa pada bidang referensi baru""
    },
    {
      ""Id"": ""rev_slot_keyway"",
      ""Type"": 22, /* 22=AddSlot (atau 3=AddRectangle) */
      ""Parameters"": { ""width"": 8.0, ""length"": 50.0, ""x"": 0.0, ""y"": 0.0 },
      ""DependsOn"": [""rev_sk_keyway""],
      ""Description"": ""Buat slot alur pasak 50x8 mm pada segmen tengah poros""
    },
    {
      ""Id"": ""rev_cut_keyway"",
      ""Type"": 7, /* 7=ExtrudeCut */
      ""Parameters"": { ""depth"": 4.0, ""throughAll"": false, ""sketch"": ""rev_sk_keyway"" },
      ""DependsOn"": [""rev_slot_keyway""],
      ""Description"": ""Extrude Cut kedalaman 4 mm ke arah dalam material""
    }
  ]
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
            return ParseCadSpec(jsonResponse, null);
        }

        public CadSpec ParseCadSpec(string jsonResponse, IEnumerable<string> existingOpIds)
        {
            if (string.IsNullOrWhiteSpace(jsonResponse))
                return null;

            try
            {
                string cleanJson = ExtractJsonFromResponse(jsonResponse);
                if (string.IsNullOrWhiteSpace(cleanJson))
                    return null;

                CadSpec spec = null;

                // 1. Try parsing via dictionary first to be completely immune to enum deserialization exceptions
                try
                {
                    var rawDict = _serializer.Deserialize<Dictionary<string, object>>(cleanJson);
                    if (rawDict != null)
                    {
                        spec = ParseCadSpecFromDictionary(rawDict);
                    }
                }
                catch (Exception exDict)
                {
                    LoggingService.Instance.Warn("Parsing CadSpec via dictionary gagal, mencoba fallback typed: " + exDict.Message);
                }

                // 2. Fallback to direct typed deserialization if dictionary parsing did not yield a spec
                if (spec == null)
                {
                    spec = _serializer.Deserialize<CadSpec>(cleanJson);
                }

                if (spec != null)
                {
                    if (string.IsNullOrEmpty(spec.RequestId))
                    {
                        spec.RequestId = "req_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    }

                    // Diagnostic logging for all parsed operations
                    if (spec.Operations != null)
                    {
                        for (int i = 0; i < spec.Operations.Count; i++)
                        {
                            var op = spec.Operations[i];
                            if (op == null) continue;
                            LoggingService.Instance.Info(string.Format(
                                "[PARSER_OP_DIAGNOSTIC] Op[{0}]: Id='{1}', RawType='{2}', ParsedType='{3}', Desc='{4}', ParamsCount={5}, Deps=[{6}]",
                                i,
                                op.Id ?? "?",
                                op.RawTypeString ?? "none",
                                op.Type,
                                op.Description ?? "",
                                op.Parameters != null ? op.Parameters.Count : 0,
                                op.DependsOn != null ? string.Join(", ", op.DependsOn) : ""));
                        }
                    }

                    CadPlanNormalizer.TryNormalizeOperationIds(spec, existingOpIds, out int missingCount, out int genCount, out int dupCount, out string diagMsg);
                    if (missingCount > 0)
                    {
                        LoggingService.Instance.Warn("[CAD_PLAN_NORMALIZER] " + diagMsg);
                    }
                    if (dupCount > 0)
                    {
                        LoggingService.Instance.Warn("[CAD_PLAN_NORMALIZER] Peringatan duplikasi ID: " + diagMsg);
                    }
                }
                return spec;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal mem-parsing CadSpec JSON: " + ex.Message);
                return null;
            }
        }

        private CadSpec ParseCadSpecFromDictionary(Dictionary<string, object> dict)
        {
            if (dict == null) return null;
            var spec = new CadSpec();

            object reqIdObj;
            if (TryGetDictValue(dict, new[] { "RequestId", "requestId", "id", "Id" }, out reqIdObj))
                spec.RequestId = reqIdObj != null ? reqIdObj.ToString() : null;

            object unitsObj;
            if (TryGetDictValue(dict, new[] { "Units", "units", "unit" }, out unitsObj))
                spec.Units = unitsObj != null ? unitsObj.ToString() : "mm";
            else
                spec.Units = "mm";

            object docTypeObj;
            if (TryGetDictValue(dict, new[] { "DocumentType", "documentType" }, out docTypeObj) && docTypeObj != null)
            {
                int dt;
                if (int.TryParse(docTypeObj.ToString(), out dt) && Enum.IsDefined(typeof(DocumentType), dt))
                    spec.DocumentType = (DocumentType)dt;
            }

            object srcObj;
            if (TryGetDictValue(dict, new[] { "Source", "source" }, out srcObj) && srcObj != null)
            {
                int sc;
                if (int.TryParse(srcObj.ToString(), out sc) && Enum.IsDefined(typeof(CadSource), sc))
                    spec.Source = (CadSource)sc;
            }

            object reqConfObj;
            if (TryGetDictValue(dict, new[] { "RequiresConfirmation", "requiresConfirmation" }, out reqConfObj) && reqConfObj != null)
            {
                bool rc;
                if (bool.TryParse(reqConfObj.ToString(), out rc)) spec.RequiresConfirmation = rc;
            }

            object notesObj;
            if (TryGetDictValue(dict, new[] { "Notes", "notes", "note" }, out notesObj))
                spec.Notes = notesObj != null ? notesObj.ToString() : null;

            // ExplicitDimensions
            object expDimObj;
            if (TryGetDictValue(dict, new[] { "ExplicitDimensions", "explicitDimensions" }, out expDimObj))
            {
                var expDimDict = expDimObj as Dictionary<string, object>;
                if (expDimDict != null)
                {
                    foreach (var kvp in expDimDict)
                    {
                        double dVal;
                        if (kvp.Value != null && double.TryParse(kvp.Value.ToString(), out dVal))
                            spec.ExplicitDimensions[kvp.Key] = dVal;
                    }
                }
            }

            // InferredValues
            object infValObj;
            if (TryGetDictValue(dict, new[] { "InferredValues", "inferredValues" }, out infValObj))
            {
                var infValDict = infValObj as Dictionary<string, object>;
                if (infValDict != null)
                {
                    foreach (var kvp in infValDict)
                    {
                        double dVal;
                        if (kvp.Value != null && double.TryParse(kvp.Value.ToString(), out dVal))
                            spec.InferredValues[kvp.Key] = dVal;
                    }
                }
            }

            // Unknowns
            object unkObj;
            if (TryGetDictValue(dict, new[] { "Unknowns", "unknowns" }, out unkObj))
            {
                var unkEnum = unkObj as System.Collections.IEnumerable;
                if (unkEnum != null && !(unkObj is string))
                {
                    foreach (var item in unkEnum)
                    {
                        if (item != null) spec.Unknowns.Add(item.ToString());
                    }
                }
            }

            // ExportTargets
            object expObj;
            if (TryGetDictValue(dict, new[] { "ExportTargets", "exportTargets" }, out expObj))
            {
                var expEnum = expObj as System.Collections.IEnumerable;
                if (expEnum != null && !(expObj is string))
                {
                    foreach (var item in expEnum)
                    {
                        if (item != null) spec.ExportTargets.Add(item.ToString());
                    }
                }
            }

            // Operations
            object opsObj;
            if (TryGetDictValue(dict, new[] { "Operations", "operations", "ops", "Ops" }, out opsObj))
            {
                var opsEnum = opsObj as System.Collections.IEnumerable;
                if (opsEnum != null && !(opsObj is string))
                {
                    spec.Operations = new List<CadOperation>();
                    foreach (var opItem in opsEnum)
                    {
                        var opDict = opItem as Dictionary<string, object>;
                        if (opDict != null)
                        {
                            var op = new CadOperation();

                            // Id
                            object idObj;
                            if (TryGetDictValue(opDict, new[] { "Id", "id", "ID", "opId", "op_id" }, out idObj) && idObj != null)
                            {
                                op.Id = idObj.ToString().Trim();
                            }

                            // Type mapping: check all aliases for operation type
                            object rawTypeObj;
                            if (TryGetDictValue(opDict, new[] { "Type", "type", "operation", "Operation", "operation_type", "operationType", "op_type", "opType", "action", "Action" }, out rawTypeObj) && rawTypeObj != null)
                            {
                                op.RawTypeString = rawTypeObj.ToString();
                                op.Type = OperationTypeMapper.Parse(rawTypeObj);
                            }
                            else
                            {
                                op.RawTypeString = null;
                                op.Type = OperationType.Unknown;
                            }

                            // Description
                            object descObj;
                            if (TryGetDictValue(opDict, new[] { "Description", "description", "desc", "Desc" }, out descObj) && descObj != null)
                            {
                                op.Description = descObj.ToString();
                            }

                            // DependsOn
                            object depObj;
                            if (TryGetDictValue(opDict, new[] { "DependsOn", "dependsOn", "depends_on", "dependencies" }, out depObj))
                            {
                                var depEnum = depObj as System.Collections.IEnumerable;
                                if (depEnum != null && !(depObj is string))
                                {
                                    foreach (var dep in depEnum)
                                    {
                                        if (dep != null) op.DependsOn.Add(dep.ToString().Trim());
                                    }
                                }
                            }

                            // Parameters
                            object paramObj;
                            if (TryGetDictValue(opDict, new[] { "Parameters", "parameters", "params", "Params" }, out paramObj))
                            {
                                var paramDict = paramObj as Dictionary<string, object>;
                                if (paramDict != null)
                                {
                                    foreach (var kvp in paramDict)
                                    {
                                        op.Parameters[kvp.Key] = kvp.Value;
                                    }
                                }
                            }

                            spec.Operations.Add(op);
                        }
                    }
                }
            }

            return spec;
        }

        private static bool TryGetDictValue(Dictionary<string, object> dict, string[] keys, out object value)
        {
            value = null;
            if (dict == null || keys == null) return false;
            foreach (var key in keys)
            {
                if (dict.TryGetValue(key, out value))
                    return true;
            }
            return false;
        }

        public string BuildPromptForTextRequest(string userMessage)
        {
            return "Pengguna meminta pembuatan model 3D di SOLIDWORKS dengan deskripsi berikut:\n\"" +
                   userMessage + "\"\n\n" +
                   "Tugasmu:\n" +
                   "1. Identifikasi bentuk, dimensi eksplisit, dan satuan ukuran (default: mm).\n" +
                   "2. Jika ada dimensi kritis yang belum diketahui atau tidak masuk akal, tambahkan ke dalam array 'Unknowns'.\n" +
                   "3. Petakan urutan operasi pemodelan CAD native yang valid ke dalam array 'Operations'.\n" +
                   "Daftar OperationType v2.0:\n" +
                   "0=NewPart, 1=NewSketch, 2=AddLine, 3=AddRectangle, 4=AddCircle, 5=AddDimension, " +
                   "6=ExtrudeBoss, 7=ExtrudeCut, 8=RevolvedBoss, 9=Fillet, 10=Chamfer, 11=SimpleHole, 12=LinearPattern, 13=CircularPattern, 14=SavePart, 15=ExportStep, 16=ExportStl, 17=CreateHemisphere, " +
                   "18=EndSketch, 19=AddPolyline, 20=AddArc, 21=AddCenterRectangle, 22=AddSlot, 23=AddPolygon, 24=AddPoint, 25=AddConstraint, 26=RevolvedCut, 27=SweepBoss, 28=Shell, 29=Mirror, 30=CreatePlane, 31=ActivateDocument, 32=Rebuild.\n\n" +
                   "Pedoman pemodelan SOLIDWORKS yang wajib dipatuhi:\n" +
                   "- Setiap operasi WAJIB memiliki 'Id' unik yang tidak boleh kosong (contoh: 'sk1', 'rect1', 'ext1', 'rev1', 'cut1').\n" +
                   "- Setiap operasi 3D (ExtrudeBoss / ExtrudeCut / RevolvedBoss) HARUS didahului operasi NewSketch pada bidang ('Front', 'Top', atau 'Right').\n" +
                   "- Untuk kontur mekanik yang kompleks (seperti kunci pas, bracket, profil bodi): Gunakan AddPolyline (Type=19) dengan parameter 'points': [[x1,y1], [x2,y2], ...] dan 'closed': true, diikuti ExtrudeBoss.\n" +
                   "- JANGAN menaruh profil bertabrakan atau tumpang tindih di sketsa yang sama. Buatlah fitur secara bertahap: Fitur 1 (NewSketch -> profil -> ExtrudeBoss), lalu Fitur 2 (NewSketch -> profil -> ExtrudeBoss), lalu Pemotongan (NewSketch -> profil -> ExtrudeCut).\n" +
                   "- Pada AddCircle dan AddRectangle, gunakan parameter 'x' dan 'y' untuk posisi titik pusat jika tidak berada di (0,0).\n" +
                   "- Untuk lubang tembus: Gunakan ExtrudeCut (Type=7) dengan 'throughAll': true (atau SimpleHole Type=11 dengan 'throughAll': true).\n" +
                   "- Untuk bentuk setengah bola (hemisphere/kubah): Gunakan operasi 'CreateHemisphere' (Type=17) dengan parameter 'diameter' (atau 'radius') dan 'plane' (default 'Front').\n" +
                   "- Setiap parameter numerik (width, height, depth, diameter, radius) WAJIB berupa angka positif lebih dari nol.\n" +
                   "- Untuk fitur putar (revolve): Gunakan 'RevolvedBoss' (Type=8) dengan parameter 'angle' (default 360).\n" +
                   "- Untuk poros bertingkat (stepped shaft) atau komponen putar: Buat NewSketch pada 'Front' plane -> AddPolyline (Type=19) separuh penampang di atas sumbu X tertutup -> RevolvedBoss (Type=8) angle 360 -> Jika ada lubang aksial, buat NewSketch pada 'Right' plane -> AddCircle (Type=4) -> ExtrudeCut (Type=7) dengan 'throughAll': true.\n\n" +
                   "PENTING: Jangan membuat script C# atau macro VBA. Kembalikan HANYA format JSON CadSpec valid berikut:\n" +
                   CadSpecJsonSchemaExample;
        }

        public string BuildPromptForRevision(string revisionMessage, CadSpec currentSpec)
        {
            return BuildPromptForRevision(revisionMessage, currentSpec, null);
        }

        public string BuildPromptForRevision(string revisionMessage, CadSpec currentSpec, ModelContextSnapshot snapshot)
        {
            string currentJson = currentSpec != null ? _serializer.Serialize(currentSpec) : "{}";
            string snapshotContext = snapshot != null ? "\n" + snapshot.ToPromptContextString() + "\n" : "";

            return "Model CAD saat ini SUDAH TERBENTUK di SOLIDWORKS dengan spesifikasi berikut:\n" +
                   currentJson + "\n" +
                   snapshotContext + "\n" +
                   "Pengguna meminta revisi atau penambahan fitur berikut:\n\"" +
                   revisionMessage + "\"\n\n" +
                   "Pedoman revisi dan penambahan fitur yang WAJIB dipatuhi:\n" +
                   "1. Geometri dasar pada model di atas SUDAH ADA di SOLIDWORKS (Bodies=1). JANGAN membuat part baru (DILARANG NewPart / Type=0) dan JANGAN meregenerasi fitur dasar (Revolve1, Cut-Extrude1).\n" +
                   "2. Setiap operasi revisi WAJIB memiliki 'Id' unik dan 'Type' yang valid secara eksplisit (contoh: Type=30 untuk CreatePlane, Type=1 untuk NewSketch, Type=22 untuk AddSlot atau Type=3 untuk AddRectangle, Type=7 untuk ExtrudeCut).\n" +
                   "3. DILARANG mengulangi operasi 'CreateHemisphere' jika objek setengah bola sudah dibuat. Hasilkan HANYA operasi NewSketch dan fitur lanjutannya.\n" +
                   "4. Untuk alur pasak (keyway) pada segmen poros bertingkat (contoh: segmen tengah Ø25 mm, panjang 64 mm, dengan alur pasak lebar 8 mm, kedalaman 4 mm, panjang 50 mm):\n" +
                   "   - Langkah 1: Buat bidang referensi tangensial dengan 'CreatePlane' (Type=30) dengan 'referencePlane': 'Top', 'offset': 12.5 (radius silinder Ø25 mm).\n" +
                   "   - Langkah 2: Buka sketsa pada bidang tersebut dengan 'NewSketch' (Type=1) dengan 'plane': 'rev_plane_keyway'.\n" +
                   "   - Langkah 3: Buat profil alur pasak dengan 'AddSlot' (Type=22) atau 'AddRectangle' (Type=3) dengan width 8 mm, length 50 mm (terpusat pada segmen tengah, menyisakan margin 7 mm di kedua ujung).\n" +
                   "   - Langkah 4: Potong material dengan 'ExtrudeCut' (Type=7) dengan 'depth': 4.0 mm ke arah dalam material poros ('throughAll': false).\n" +
                   "   - Pertahankan lubang aksial Ø8 mm yang sudah ada agar tidak tertutup.\n" +
                   "   - Hasil akhir harus tetap berupa 1 solid body yang utuh.\n" +
                   "5. Untuk menambahkan fitur pada planar face: Gunakan NewSketch dengan 'plane': 'PlanarFace'.\n" +
                   "6. Untuk penambahan Fillet atau Chamfer saja: Hasilkan langsung operasi Fillet (Type=9) atau Chamfer (Type=10).\n" +
                   "7. Setiap parameter numerik (depth, diameter, width, height, radius) WAJIB bernilai positif lebih dari nol.\n" +
                   "8. Pertahankan konsistensi satuan (default: mm).\n\n" +
                   "Daftar OperationType v2.0:\n" +
                   "1=NewSketch, 2=AddLine, 3=AddRectangle, 4=AddCircle, 5=AddDimension, " +
                   "6=ExtrudeBoss, 7=ExtrudeCut, 8=RevolvedBoss, 9=Fillet, 10=Chamfer, 11=SimpleHole, 12=LinearPattern, 13=CircularPattern, " +
                   "18=EndSketch, 19=AddPolyline, 20=AddArc, 21=AddCenterRectangle, 22=AddSlot, 23=AddPolygon, 24=AddPoint, 25=AddConstraint, 26=RevolvedCut, 27=SweepBoss, 28=Shell, 29=Mirror, 30=CreatePlane, 31=ActivateDocument, 32=Rebuild.\n\n" +
                   "PENTING: JANGAN menghasilkan NewPart (Type=0). Kembalikan HANYA format JSON CadSpec valid berikut:\n" +
                   CadSpecRevisionJsonSchemaExample;
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

