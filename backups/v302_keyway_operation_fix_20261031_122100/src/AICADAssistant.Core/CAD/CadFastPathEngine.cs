using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;

namespace AICADAssistant.Core.CAD
{
    /// <summary>
    /// Deterministic fast-path geometry generator for unambiguous standard primitives.
    /// Bypasses Gemini API latency (< 10 ms vs ~15-30s) for explicit plates, blocks,
    /// cylinders, solid hemispheres, and simple centered holes.
    /// Falls back to Gemini for all complex, revision, or ambiguous queries.
    /// </summary>
    public static class CadFastPathEngine
    {
        public static bool TryCreateSpec(string text, out CadSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Normalize multiple whitespaces and convert to lowercase
            string clean = Regex.Replace(text.Trim(), @"\s+", " ").ToLowerInvariant();

            // Safety filter: Do not use fast path for ambiguous, tentative, or complex requests
            // Uses word boundaries so words like 'kubah' do not accidentally trigger 'ubah'
            if (Regex.IsMatch(clean, @"\b(?:kira-kira|mungkin|sekitar|atau|revisi|ubah|tambahkan|hapus|ganti)\b", RegexOptions.IgnoreCase))
            {
                return false;
            }

            // 1. Try match: Hemisphere / Setengah Bola Pejal
            if (TryMatchHemisphere(clean, out spec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH] Terdeteksi primitive setengah bola pejal: '{0}'", text));
                return true;
            }

            // 2. Try match: Plate with Centered Hole
            if (TryMatchPlateWithHole(clean, out spec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH] Terdeteksi primitive pelat berlubang: '{0}'", text));
                return true;
            }

            // 3. Try match: Wrench / Kunci Pas
            if (TryMatchWrench(clean, out spec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH] Terdeteksi model mekanik kunci pas: '{0}'", text));
                return true;
            }

            // 4. Try match: L-Bracket / Siku
            if (TryMatchBracket(clean, out spec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH] Terdeteksi model mekanik L-bracket: '{0}'", text));
                return true;
            }

            // 5. Try match: Pulley / Puli V-Groove
            if (TryMatchPulley(clean, out spec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH] Terdeteksi model mekanik puli: '{0}'", text));
                return true;
            }

            // 6. Try match: Stepped Shaft / Poros Bertingkat
            if (TryMatchShaft(clean, out spec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH] Terdeteksi model mekanik poros bertingkat: '{0}'", text));
                return true;
            }

            // 7. Try match: Plate / Box / Balok / Kubus
            if (TryMatchPlateOrBlock(clean, out spec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH] Terdeteksi primitive pelat/balok: '{0}'", text));
                return true;
            }

            // 8. Try match: Solid Cylinder / Silinder Pejal
            if (TryMatchCylinder(clean, out spec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH] Terdeteksi primitive silinder pejal: '{0}'", text));
                return true;
            }

            return false;
        }

        private static readonly Regex ComplexGeometryInhibitorPattern = new Regex(
            @"\b(?:poros\s*bertingkat|stepped\s*shaft|as\s*bertingkat|lima\s*(?:bagian|segmen)|(?:multi|multiple)\s*(?:diameter|segmen|bagian|section)|collar|shoulder|revolve|bertingkat|segmen|alur|keyway|v-groove)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Deterministic fast-path generator for standard model revisions and additions
        /// such as adding a cylindrical rod/shaft or hole to an existing active model.
        /// </summary>
        public static bool TryCreateRevisionSpec(string text, CadSpec currentSpec, out CadSpec revSpec)
        {
            revSpec = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Revision requires an existing valid specification with at least one operation
            if (currentSpec == null || currentSpec.Operations == null || currentSpec.Operations.Count == 0)
                return false;

            string clean = Regex.Replace(text.Trim(), @"\s+", " ").ToLowerInvariant();

            // Safety filter: Do not use fast path for ambiguous or tentative revision requests
            if (Regex.IsMatch(clean, @"\b(?:kira-kira|mungkin|sekitar|atau)\b", RegexOptions.IgnoreCase))
            {
                return false;
            }

            // P0 GUARD: Inhibit fast path revision for complex, multi-segment, or stepped shaft specifications.
            // Complex requests must be routed to the Gemini CAD Planner to prevent generating trivial primitives.
            if (ComplexGeometryInhibitorPattern.IsMatch(clean))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH_REVISION] Permintaan geometri kompleks/poros bertingkat terdeteksi ('{0}'). Fast Path Revision diabaikan, dialihkan ke CAD Planner.", text));
                return false;
            }

            // If prompt contains 5 or more numerical values, it represents a complex multi-segment specification
            if (Regex.Matches(clean, @"\b\d+(?:[.,]\d+)?\s*(?:mm|cm|m)?\b").Count >= 5)
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH_REVISION] Banyak parameter dimensi terdeteksi (>=5 angka). Fast Path Revision diabaikan, dialihkan ke CAD Planner."));
                return false;
            }

            // 1. Try match: Adding a cylindrical rod/shaft/tangkai to the model (e.g. hemisphere or plate)
            if (TryMatchRodRevision(clean, currentSpec, out revSpec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH_REVISION] Terdeteksi penambahan tangkai/batang silinder: '{0}'", text));
                return true;
            }

            // 2. Try match: Adding a hole / lubang to the model
            if (TryMatchHoleRevision(clean, currentSpec, out revSpec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH_REVISION] Terdeteksi penambahan lubang: '{0}'", text));
                return true;
            }

            // 3. Try match: Adding a Fillet to the active model
            if (TryMatchFilletRevision(clean, currentSpec, out revSpec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH_REVISION] Terdeteksi penambahan fillet: '{0}'", text));
                return true;
            }

            // 4. Try match: Adding a Chamfer to the active model
            if (TryMatchChamferRevision(clean, currentSpec, out revSpec))
            {
                LoggingService.Instance.Info(string.Format("[FAST_PATH_REVISION] Terdeteksi penambahan chamfer: '{0}'", text));
                return true;
            }

            return false;
        }

        private static bool TryMatchRodRevision(string text, CadSpec currentSpec, out CadSpec spec)
        {
            spec = null;

            // Inhibit rod revision if text contains complex stepped geometry
            if (ComplexGeometryInhibitorPattern.IsMatch(text))
                return false;

            // Must contain explicit addition action phrases for rod/tangkai/batang/pegangan
            // Bare word 'shaft' alone MUST NOT trigger rod addition
            bool hasExplicitAddRod = Regex.IsMatch(text,
                @"\b(?:tambah(?:kan)?|pasang(?:kan)?|buat(?:kan)?)\s+(?:sebuah\s+)?(?:tangkai|batang|rod|silinder|pegangan|pin)\b",
                RegexOptions.IgnoreCase);

            if (!hasExplicitAddRod)
                return false;

            double diameter = 0;
            string unitStr = currentSpec != null && !string.IsNullOrWhiteSpace(currentSpec.Units) ? currentSpec.Units : "mm";

            // Extract diameter
            var mDia = Regex.Match(text, @"(?:(?:\b(?:diameter|diam|dia)\b|[øØ]|\bd\b)\s*[:=]?\s*|\bd(?=\d))(\d+(?:[.,]\d+)?)\s*(mm|cm|m|in(?:ch)?)?", RegexOptions.IgnoreCase);
            if (mDia.Success)
            {
                diameter = ParseDouble(mDia.Groups[1].Value);
                if (mDia.Groups[2].Success && !string.IsNullOrWhiteSpace(mDia.Groups[2].Value))
                {
                    unitStr = mDia.Groups[2].Value.ToLowerInvariant();
                }
            }
            else
            {
                var mRad = Regex.Match(text, @"(?:(?:\b(?:radius|rad|jari-jari|\br\b)\s*[:=]?\s*|\br(?=\d)))(\d+(?:[.,]\d+)?)\s*(mm|cm|m|in(?:ch)?)?", RegexOptions.IgnoreCase);
                if (mRad.Success)
                {
                    diameter = ParseDouble(mRad.Groups[1].Value) * 2.0;
                    if (mRad.Groups[2].Success && !string.IsNullOrWhiteSpace(mRad.Groups[2].Value))
                    {
                        unitStr = mRad.Groups[2].Value.ToLowerInvariant();
                    }
                }
            }

            // Extract length / height
            double length = 0;
            var mLen = Regex.Match(text, @"(?:(?:\b(?:panjang|tinggi|kedalaman|depth|tebal|length|\bp\b|\bt\b)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)\s*(mm|cm|m|in(?:ch)?)?", RegexOptions.IgnoreCase);
            if (mLen.Success)
            {
                length = ParseDouble(mLen.Groups[1].Value);
                if (mLen.Groups[2].Success && !string.IsNullOrWhiteSpace(mLen.Groups[2].Value) && string.IsNullOrEmpty(mDia.Groups[2].Value))
                {
                    unitStr = mLen.Groups[2].Value.ToLowerInvariant();
                }
            }

            // Fallback for expressions like "20 mm x 100 mm" or "diameter 20 panjang 100"
            if (diameter > 0 && length <= 0)
            {
                var allNums = Regex.Matches(text, @"\b(\d+(?:[.,]\d+)?)\s*(mm|cm|m)?\b", RegexOptions.IgnoreCase);
                foreach (Match numMatch in allNums)
                {
                    double val = ParseDouble(numMatch.Groups[1].Value);
                    if (Math.Abs(val - diameter) > 0.001 && val > 0)
                    {
                        length = val;
                        break;
                    }
                }
            }

            if (diameter <= 0 || length <= 0)
                return false;

            // Generate revision specification
            string baseOpId = (currentSpec != null && currentSpec.Operations != null && currentSpec.Operations.Count > 0)
                ? currentSpec.Operations[0].Id
                : null;

            var dependsOnList = new List<string>();
            if (!string.IsNullOrEmpty(baseOpId))
            {
                dependsOnList.Add(baseOpId);
            }

            spec = new CadSpec
            {
                RequestId = "fp_rod_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                DocumentType = DocumentType.Part,
                Units = unitStr,
                Source = CadSource.Text,
                ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "diameter", diameter },
                    { "panjang", length }
                },
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "sk_rod",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object>
                        {
                            { "plane", "PlanarFace" }
                        },
                        DependsOn = dependsOnList,
                        Description = "Buka sketsa pada permukaan datar model aktif"
                    },
                    new CadOperation
                    {
                        Id = "circ_rod",
                        Type = OperationType.AddCircle,
                        Parameters = new Dictionary<string, object>
                        {
                            { "diameter", diameter },
                            { "x", 0.0 },
                            { "y", 0.0 }
                        },
                        DependsOn = new List<string> { "sk_rod" },
                        Description = string.Format("Gambar lingkaran tangkai diameter {0:G} {1} di tengah", diameter, unitStr)
                    },
                    new CadOperation
                    {
                        Id = "ext_rod",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object>
                        {
                            { "depth", length }
                        },
                        DependsOn = new List<string> { "circ_rod" },
                        Description = string.Format("Extrude boss tangkai panjang {0:G} {1} menyatu dengan model", length, unitStr)
                    }
                },
                Notes = string.Format("Penambahan tangkai silinder diameter {0:G} {1} panjang {2:G} {1} via Fast Path", diameter, unitStr, length)
            };

            return true;
        }

        private static bool TryMatchHoleRevision(string text, CadSpec currentSpec, out CadSpec spec)
        {
            spec = null;

            bool hasHoleKeyword = Regex.IsMatch(text, @"\b(?:lubang|hole|coak|coakan)\b", RegexOptions.IgnoreCase);
            if (!hasHoleKeyword)
                return false;

            double diameter = 0;
            string unitStr = currentSpec != null && !string.IsNullOrWhiteSpace(currentSpec.Units) ? currentSpec.Units : "mm";

            var mDia = Regex.Match(text, @"(?:(?:\b(?:diameter|diam|dia)\b|[øØ]|\bd\b)\s*[:=]?\s*|\bd(?=\d))(\d+(?:[.,]\d+)?)\s*(mm|cm|m|in(?:ch)?)?", RegexOptions.IgnoreCase);
            if (mDia.Success)
            {
                diameter = ParseDouble(mDia.Groups[1].Value);
                if (mDia.Groups[2].Success && !string.IsNullOrWhiteSpace(mDia.Groups[2].Value))
                {
                    unitStr = mDia.Groups[2].Value.ToLowerInvariant();
                }
            }

            double depth = 0;
            var mDep = Regex.Match(text, @"(?:(?:\b(?:kedalaman|depth|panjang|dalam|tebal)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)\s*(mm|cm|m|in(?:ch)?)?", RegexOptions.IgnoreCase);
            if (mDep.Success)
            {
                depth = ParseDouble(mDep.Groups[1].Value);
            }
            else
            {
                if (currentSpec != null && currentSpec.ExplicitDimensions != null)
                {
                    if (currentSpec.ExplicitDimensions.ContainsKey("tebal")) depth = currentSpec.ExplicitDimensions["tebal"];
                    else if (currentSpec.ExplicitDimensions.ContainsKey("depth")) depth = currentSpec.ExplicitDimensions["depth"];
                    else if (currentSpec.ExplicitDimensions.ContainsKey("tinggi")) depth = currentSpec.ExplicitDimensions["tinggi"];
                }
                if (depth <= 0) depth = 50.0;
            }

            if (diameter <= 0 || depth <= 0)
                return false;

            double posX = 0.0;
            double posY = 0.0;
            var mCoordX = Regex.Match(text, @"(?:posisi\s+)?x\s*[:=]?\s*(-?\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (mCoordX.Success) posX = ParseDouble(mCoordX.Groups[1].Value);

            var mCoordY = Regex.Match(text, @"(?:posisi\s+)?y\s*[:=]?\s*(-?\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (mCoordY.Success) posY = ParseDouble(mCoordY.Groups[1].Value);

            var mOffset = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(?:mm|cm)?\s*(?:dari\s+(?:ujung|tepi|pinggir)|from\s+end)", RegexOptions.IgnoreCase);
            if (mOffset.Success)
            {
                double off = ParseDouble(mOffset.Groups[1].Value);
                double baseLen = 0;
                if (currentSpec != null && currentSpec.ExplicitDimensions != null)
                {
                    if (currentSpec.ExplicitDimensions.ContainsKey("panjang")) baseLen = currentSpec.ExplicitDimensions["panjang"];
                    else if (currentSpec.ExplicitDimensions.ContainsKey("length")) baseLen = currentSpec.ExplicitDimensions["length"];
                }
                if (baseLen > 0) posX = baseLen - off;
                else posX = off;
            }

            bool isThrough = Regex.IsMatch(text, @"\b(?:tembus|menembus|through|through[\s-]*all)\b", RegexOptions.IgnoreCase) || !mDep.Success;

            string baseOpId = (currentSpec != null && currentSpec.Operations != null && currentSpec.Operations.Count > 0)
                ? currentSpec.Operations[0].Id
                : null;

            var dependsOnList = new List<string>();
            if (!string.IsNullOrEmpty(baseOpId))
            {
                dependsOnList.Add(baseOpId);
            }

            var cutParams = new Dictionary<string, object>
            {
                { "depth", depth },
                { "throughAll", isThrough }
            };

            spec = new CadSpec
            {
                RequestId = "fp_hole_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                DocumentType = DocumentType.Part,
                Units = unitStr,
                Source = CadSource.Text,
                ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "diameter", diameter },
                    { "depth", depth }
                },
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "sk_hole",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object>
                        {
                            { "plane", "PlanarFace" }
                        },
                        DependsOn = dependsOnList,
                        Description = "Buka sketsa pada permukaan model aktif"
                    },
                    new CadOperation
                    {
                        Id = "circ_hole",
                        Type = OperationType.AddCircle,
                        Parameters = new Dictionary<string, object>
                        {
                            { "diameter", diameter },
                            { "x", posX },
                            { "y", posY }
                        },
                        DependsOn = new List<string> { "sk_hole" },
                        Description = string.Format("Gambar lingkaran lubang diameter {0:G} {1} pada ({2:G}, {3:G})", diameter, unitStr, posX, posY)
                    },
                    new CadOperation
                    {
                        Id = "cut_hole",
                        Type = OperationType.ExtrudeCut,
                        Parameters = cutParams,
                        DependsOn = new List<string> { "circ_hole" },
                        Description = string.Format("Extrude cut lubang kedalaman {0:G} {1} (throughAll: {2})", depth, unitStr, isThrough)
                    }
                },
                Notes = string.Format("Penambahan lubang diameter {0:G} {1} kedalaman {2:G} {1} via Fast Path", diameter, unitStr, depth)
            };

            return true;
        }

        private static bool TryMatchFilletRevision(string text, CadSpec currentSpec, out CadSpec spec)
        {
            spec = null;
            if (!Regex.IsMatch(text, @"\b(?:fillet|radius|round)\b", RegexOptions.IgnoreCase))
                return false;

            double radius = 0;
            string unitStr = currentSpec != null && !string.IsNullOrWhiteSpace(currentSpec.Units) ? currentSpec.Units : "mm";

            var mRad = Regex.Match(text, @"(?:(?:\b(?:radius|rad|r|fillet)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)\s*(mm|cm|m)?", RegexOptions.IgnoreCase);
            if (mRad.Success)
            {
                radius = ParseDouble(mRad.Groups[1].Value);
                if (mRad.Groups[2].Success && !string.IsNullOrWhiteSpace(mRad.Groups[2].Value))
                    unitStr = mRad.Groups[2].Value.ToLowerInvariant();
            }
            if (radius <= 0) return false;

            string baseOpId = (currentSpec != null && currentSpec.Operations != null && currentSpec.Operations.Count > 0)
                ? currentSpec.Operations[currentSpec.Operations.Count - 1].Id
                : null;
            var deps = new List<string>();
            if (!string.IsNullOrEmpty(baseOpId)) deps.Add(baseOpId);

            spec = new CadSpec
            {
                RequestId = "fp_fillet_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                DocumentType = DocumentType.Part,
                Units = unitStr,
                Source = CadSource.Text,
                ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { "radius", radius } },
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "fil1",
                        Type = OperationType.Fillet,
                        Parameters = new Dictionary<string, object> { { "radius", radius } },
                        DependsOn = deps,
                        Description = string.Format("Tambahkan fillet radius {0:G} {1}", radius, unitStr)
                    }
                },
                Notes = string.Format("Penambahan fillet radius {0:G} {1} via Fast Path", radius, unitStr)
            };
            return true;
        }

        private static bool TryMatchChamferRevision(string text, CadSpec currentSpec, out CadSpec spec)
        {
            spec = null;
            if (!Regex.IsMatch(text, @"\b(?:chamfer|chamfering)\b", RegexOptions.IgnoreCase))
                return false;

            double distance = 0;
            string unitStr = currentSpec != null && !string.IsNullOrWhiteSpace(currentSpec.Units) ? currentSpec.Units : "mm";

            var mDist = Regex.Match(text, @"(?:(?:\b(?:distance|jarak|d|chamfer)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)\s*(mm|cm|m)?", RegexOptions.IgnoreCase);
            if (mDist.Success)
            {
                distance = ParseDouble(mDist.Groups[1].Value);
                if (mDist.Groups[2].Success && !string.IsNullOrWhiteSpace(mDist.Groups[2].Value))
                    unitStr = mDist.Groups[2].Value.ToLowerInvariant();
            }
            if (distance <= 0) return false;

            string baseOpId = (currentSpec != null && currentSpec.Operations != null && currentSpec.Operations.Count > 0)
                ? currentSpec.Operations[currentSpec.Operations.Count - 1].Id
                : null;
            var deps = new List<string>();
            if (!string.IsNullOrEmpty(baseOpId)) deps.Add(baseOpId);

            spec = new CadSpec
            {
                RequestId = "fp_chamfer_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                DocumentType = DocumentType.Part,
                Units = unitStr,
                Source = CadSource.Text,
                ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { "distance", distance } },
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "cham1",
                        Type = OperationType.Chamfer,
                        Parameters = new Dictionary<string, object> { { "distance", distance } },
                        DependsOn = deps,
                        Description = string.Format("Tambahkan chamfer {0:G} {1}", distance, unitStr)
                    }
                },
                Notes = string.Format("Penambahan chamfer {0:G} {1} via Fast Path", distance, unitStr)
            };
            return true;
        }

        private static bool TryMatchWrench(string text, out CadSpec spec)
        {
            spec = null;
            if (!Regex.IsMatch(text, @"\b(?:kunci\s*pas|wrench|spanner)\b", RegexOptions.IgnoreCase))
                return false;

            double size = 10.0;
            var mSize = Regex.Match(text, @"(?:(?:\b(?:ukuran|size|nomor|no)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (mSize.Success) size = ParseDouble(mSize.Groups[1].Value);
            else
            {
                var mStandaloneNum = Regex.Match(text, @"\b(\d+(?:[.,]\d+)?)\s*mm\b", RegexOptions.IgnoreCase);
                if (mStandaloneNum.Success) size = ParseDouble(mStandaloneNum.Groups[1].Value);
            }
            if (size <= 0) size = 10.0;

            double length = 120.0;
            var mLen = Regex.Match(text, @"(?:(?:\b(?:panjang|length)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (mLen.Success) length = ParseDouble(mLen.Groups[1].Value);

            double thickness = 5.0;
            var mThick = Regex.Match(text, @"(?:(?:\b(?:tebal|ketebalan|thick(?:ness)?)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (mThick.Success) thickness = ParseDouble(mThick.Groups[1].Value);

            double holeDia = 5.0;
            var mHole = Regex.Match(text, @"(?:lubang(?:\s+gantung)?(?:\s+diameter)?)\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (mHole.Success) holeDia = ParseDouble(mHole.Groups[1].Value);

            double filletRad = 1.0;
            var mFil = Regex.Match(text, @"(?:(?:\b(?:radius|fillet)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (mFil.Success) filletRad = ParseDouble(mFil.Groups[1].Value);

            double jawDepth = 1.5 * size;
            double jawHalfOpen = size / 2.0;
            double headHalfWidth = 1.2 * size;
            double handleStartHalf = 0.8 * size;
            double handleEndHalf = 0.6 * size;
            double handleStartX = 2.5 * size;

            var polylinePts = new List<double[]>
            {
                new double[] { jawDepth, jawHalfOpen, 0.0 },
                new double[] { 0.0, jawHalfOpen, 0.0 },
                new double[] { 0.0, headHalfWidth, 0.0 },
                new double[] { jawDepth, headHalfWidth, 0.0 },
                new double[] { handleStartX, handleStartHalf, 0.0 },
                new double[] { length, handleEndHalf, 0.0 },
                new double[] { length, -handleEndHalf, 0.0 },
                new double[] { handleStartX, -handleStartHalf, 0.0 },
                new double[] { jawDepth, -headHalfWidth, 0.0 },
                new double[] { 0.0, -headHalfWidth, 0.0 },
                new double[] { 0.0, -jawHalfOpen, 0.0 },
                new double[] { jawDepth, -jawHalfOpen, 0.0 }
            };

            spec = new CadSpec
            {
                RequestId = "fp_wrench_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                DocumentType = DocumentType.Part,
                Units = "mm",
                Source = CadSource.Text,
                ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ukuran", size },
                    { "panjang", length },
                    { "tebal", thickness },
                    { "lubang", holeDia }
                },
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "sk_wrench",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object> { { "plane", "Front" } },
                        Description = "Buka sketsa profil kunci pas pada Front Plane"
                    },
                    new CadOperation
                    {
                        Id = "poly_wrench",
                        Type = OperationType.AddPolyline,
                        Parameters = new Dictionary<string, object>
                        {
                            { "points", polylinePts },
                            { "closed", true }
                        },
                        DependsOn = new List<string> { "sk_wrench" },
                        Description = string.Format("Gambar kontur luar kunci pas {0:G} mm panjang {1:G} mm", size, length)
                    },
                    new CadOperation
                    {
                        Id = "ext_wrench",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object>
                        {
                            { "depth", thickness },
                            { "profile", "poly_wrench" }
                        },
                        DependsOn = new List<string> { "poly_wrench" },
                        Description = string.Format("Extrude profil bodi kunci pas ketebalan {0:G} mm", thickness)
                    },
                    new CadOperation
                    {
                        Id = "sk_hole",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object> { { "plane", "Front" } },
                        DependsOn = new List<string> { "ext_wrench" },
                        Description = "Buka sketsa untuk lubang gantung pada ujung gagang"
                    },
                    new CadOperation
                    {
                        Id = "circ_hole",
                        Type = OperationType.AddCircle,
                        Parameters = new Dictionary<string, object>
                        {
                            { "diameter", holeDia },
                            { "x", length - 8.0 },
                            { "y", 0.0 }
                        },
                        DependsOn = new List<string> { "sk_hole" },
                        Description = string.Format("Gambar lingkaran lubang gantung diameter {0:G} mm di 8 mm dari ujung", holeDia)
                    },
                    new CadOperation
                    {
                        Id = "cut_hole",
                        Type = OperationType.ExtrudeCut,
                        Parameters = new Dictionary<string, object>
                        {
                            { "depth", thickness + 5.0 },
                            { "throughAll", true },
                            { "profile", "circ_hole" }
                        },
                        DependsOn = new List<string> { "circ_hole" },
                        Description = "Extrude cut tembus lubang gantung"
                    },
                    new CadOperation
                    {
                        Id = "fillet_wrench",
                        Type = OperationType.Fillet,
                        Parameters = new Dictionary<string, object>
                        {
                            { "radius", filletRad }
                        },
                        DependsOn = new List<string> { "ext_wrench" },
                        Description = string.Format("Terapkan fillet {0:G} mm pada tepi kepala dan gagang", filletRad)
                    }
                },
                Notes = string.Format("Kunci pas ukuran {0:G} mm, panjang {1:G} mm, tebal {2:G} mm via Fast Path", size, length, thickness)
            };
            return true;
        }

        private static bool TryMatchBracket(string text, out CadSpec spec)
        {
            spec = null;
            if (!Regex.IsMatch(text, @"\b(?:l-bracket|bracket\s*l|siku\s*l|braket\s*l)\b", RegexOptions.IgnoreCase))
                return false;

            double baseLen = 80.0, height = 100.0, width = 40.0, thick = 5.0;
            var mDims = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*[xX*]\s*(\d+(?:[.,]\d+)?)\s*[xX*]\s*(\d+(?:[.,]\d+)?)");
            if (mDims.Success)
            {
                baseLen = ParseDouble(mDims.Groups[1].Value);
                height = ParseDouble(mDims.Groups[2].Value);
                width = ParseDouble(mDims.Groups[3].Value);
            }
            var mThick = Regex.Match(text, @"(?:(?:\b(?:tebal|ketebalan)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)");
            if (mThick.Success) thick = ParseDouble(mThick.Groups[1].Value);

            var bracketPts = new List<double[]>
            {
                new double[] { 0.0, 0.0, 0.0 },
                new double[] { baseLen, 0.0, 0.0 },
                new double[] { baseLen, thick, 0.0 },
                new double[] { thick, thick, 0.0 },
                new double[] { thick, height, 0.0 },
                new double[] { 0.0, height, 0.0 }
            };

            spec = new CadSpec
            {
                RequestId = "fp_bracket_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                DocumentType = DocumentType.Part,
                Units = "mm",
                Source = CadSource.Text,
                ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "alas", baseLen },
                    { "tinggi", height },
                    { "lebar", width },
                    { "tebal", thick }
                },
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "sk_brk",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object> { { "plane", "Front" } },
                        Description = "Buka sketsa L-bracket pada Front Plane"
                    },
                    new CadOperation
                    {
                        Id = "poly_brk",
                        Type = OperationType.AddPolyline,
                        Parameters = new Dictionary<string, object>
                        {
                            { "points", bracketPts },
                            { "closed", true }
                        },
                        DependsOn = new List<string> { "sk_brk" },
                        Description = string.Format("Gambar profil L-bracket {0:G}x{1:G} mm tebal {2:G} mm", baseLen, height, thick)
                    },
                    new CadOperation
                    {
                        Id = "ext_brk",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object>
                        {
                            { "depth", width },
                            { "profile", "poly_brk" }
                        },
                        DependsOn = new List<string> { "poly_brk" },
                        Description = string.Format("Extrude boss bodi siku lebar {0:G} mm", width)
                    }
                },
                Notes = string.Format("L-bracket {0:G}x{1:G}x{2:G} mm tebal {3:G} mm via Fast Path", baseLen, height, width, thick)
            };
            return true;
        }

        private static bool TryMatchPulley(string text, out CadSpec spec)
        {
            spec = null;
            if (!Regex.IsMatch(text, @"\b(?:pulley|puli|v-groove)\b", RegexOptions.IgnoreCase))
                return false;

            double od = 100.0, width = 30.0, bore = 20.0;
            var mOd = Regex.Match(text, @"(?:(?:\b(?:od|diameter|dia)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)");
            if (mOd.Success) od = ParseDouble(mOd.Groups[1].Value);
            var mBore = Regex.Match(text, @"(?:(?:\b(?:bore|poros|as)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)");
            if (mBore.Success) bore = ParseDouble(mBore.Groups[1].Value);
            var mWidth = Regex.Match(text, @"(?:(?:\b(?:lebar|tebal|width)\b)\s*[:=]?\s*)(\d+(?:[.,]\d+)?)");
            if (mWidth.Success) width = ParseDouble(mWidth.Groups[1].Value);

            double rOut = od / 2.0;
            double rIn = bore / 2.0;
            double halfW = width / 2.0;
            double grooveDepth = Math.Min(12.0, (rOut - rIn) * 0.4);

            var pulleyPts = new List<double[]>
            {
                new double[] { rIn, -halfW, 0.0 },
                new double[] { rOut, -halfW, 0.0 },
                new double[] { rOut, -halfW * 0.4, 0.0 },
                new double[] { rOut - grooveDepth, 0.0, 0.0 },
                new double[] { rOut, halfW * 0.4, 0.0 },
                new double[] { rOut, halfW, 0.0 },
                new double[] { rIn, halfW, 0.0 }
            };

            spec = new CadSpec
            {
                RequestId = "fp_pulley_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                DocumentType = DocumentType.Part,
                Units = "mm",
                Source = CadSource.Text,
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "sk_pulley",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object> { { "plane", "Front" } },
                        Description = "Buka sketsa profil puli V-groove pada Front Plane"
                    },
                    new CadOperation
                    {
                        Id = "poly_pulley",
                        Type = OperationType.AddPolyline,
                        Parameters = new Dictionary<string, object>
                        {
                            { "points", pulleyPts },
                            { "closed", true }
                        },
                        DependsOn = new List<string> { "sk_pulley" },
                        Description = string.Format("Gambar penampang puli V-groove OD {0:G} mm", od)
                    },
                    new CadOperation
                    {
                        Id = "rev_pulley",
                        Type = OperationType.RevolvedBoss,
                        Parameters = new Dictionary<string, object>
                        {
                            { "angle", 360.0 }
                        },
                        DependsOn = new List<string> { "poly_pulley" },
                        Description = "Revolve 360 derajat penampang puli mengelilingi sumbu poros"
                    }
                },
                Notes = string.Format("Puli V-groove OD {0:G} mm bore {1:G} mm lebar {2:G} mm via Fast Path", od, bore, width)
            };
            return true;
        }

        private static bool TryMatchShaft(string text, out CadSpec spec)
        {
            spec = null;

            // Must contain shaft/poros keywords
            if (!Regex.IsMatch(text, @"\b(?:poros\s*bertingkat|stepped\s*shaft|as\s*bertingkat|segmen\s*poros|poros|shaft)\b", RegexOptions.IgnoreCase))
                return false;

            // 1. Try to extract dynamic multi-segment stepped shaft
            // E.g.: "Total panjang 120 mm. Lima segmen poros. Diameter 20, 30, 25, 30, 20 mm. Panjang segmen 20, 8, 64, 8, 20 mm. Revolve Boss/Base 360°. Lubang aksial Ø8 mm menggunakan Cut-Extrude Through All."
            var mDiaList = Regex.Match(text, @"(?:diameter|diam|dia)\s*[:=]?\s*(\d+(?:[.,]\d+)?(?:\s*,\s*\d+(?:[.,]\d+)?)+)", RegexOptions.IgnoreCase);
            var mLenList = Regex.Match(text, @"(?:panjang\s*segmen|panjang)\s*[:=]?\s*(\d+(?:[.,]\d+)?(?:\s*,\s*\d+(?:[.,]\d+)?)+)", RegexOptions.IgnoreCase);

            if (mDiaList.Success && mLenList.Success)
            {
                string[] diaParts = mDiaList.Groups[1].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string[] lenParts = mLenList.Groups[1].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                if (diaParts.Length >= 2 && diaParts.Length == lenParts.Length)
                {
                    var diameters = new List<double>();
                    var lengths = new List<double>();
                    for (int i = 0; i < diaParts.Length; i++)
                    {
                        diameters.Add(ParseDouble(diaParts[i].Trim()));
                        lengths.Add(ParseDouble(lenParts[i].Trim()));
                    }

                    // Check for axial hole
                    double holeDiam = 0.0;
                    var mHole = Regex.Match(text, @"(?:lubang\s*aksial|lubang|hole|bore)\s*[:=]?\s*[øØ]?\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
                    if (mHole.Success)
                    {
                        holeDiam = ParseDouble(mHole.Groups[1].Value);
                    }

                    double totalLength = 0;
                    foreach (var l in lengths) totalLength += l;

                    // Build stepped shaft polyline points
                    var pts = new List<double[]>();
                    pts.Add(new double[] { 0.0, 0.0, 0.0 });
                    double curX = 0.0;
                    double maxDia = 0.0;

                    for (int i = 0; i < diameters.Count; i++)
                    {
                        double r = diameters[i] / 2.0;
                        if (diameters[i] > maxDia) maxDia = diameters[i];

                        if (i == 0)
                        {
                            pts.Add(new double[] { 0.0, r, 0.0 });
                        }
                        else if (Math.Abs(r - (diameters[i - 1] / 2.0)) > 1e-4)
                        {
                            pts.Add(new double[] { curX, r, 0.0 });
                        }

                        curX += lengths[i];
                        pts.Add(new double[] { curX, r, 0.0 });
                    }

                    // Bottom close back to axis
                    pts.Add(new double[] { curX, 0.0, 0.0 });

                    var ops = new List<CadOperation>
                    {
                        new CadOperation
                        {
                            Id = "sk_shaft",
                            Type = OperationType.NewSketch,
                            Parameters = new Dictionary<string, object> { { "plane", "Front" } },
                            Description = "Buka sketsa profil poros bertingkat pada Front Plane"
                        },
                        new CadOperation
                        {
                            Id = "poly_shaft",
                            Type = OperationType.AddPolyline,
                            Parameters = new Dictionary<string, object>
                            {
                                { "points", pts },
                                { "closed", true }
                            },
                            DependsOn = new List<string> { "sk_shaft" },
                            Description = string.Format("Gambar kontur poros bertingkat {0} segmen L={1:G} mm", diameters.Count, totalLength)
                        },
                        new CadOperation
                        {
                            Id = "rev_shaft",
                            Type = OperationType.RevolvedBoss,
                            Parameters = new Dictionary<string, object>
                            {
                                { "angle", 360.0 }
                            },
                            DependsOn = new List<string> { "poly_shaft" },
                            Description = "Revolve 360 derajat profil poros bertingkat"
                        }
                    };

                    if (holeDiam > 0)
                    {
                        ops.Add(new CadOperation
                        {
                            Id = "sk_hole",
                            Type = OperationType.NewSketch,
                            Parameters = new Dictionary<string, object> { { "plane", "Right" } },
                            DependsOn = new List<string> { "rev_shaft" },
                            Description = "Buka sketsa pada Right Plane untuk lubang aksial"
                        });
                        ops.Add(new CadOperation
                        {
                            Id = "circ_hole",
                            Type = OperationType.AddCircle,
                            Parameters = new Dictionary<string, object>
                            {
                                { "diameter", holeDiam },
                                { "x", 0.0 },
                                { "y", 0.0 }
                            },
                            DependsOn = new List<string> { "sk_hole" },
                            Description = string.Format("Gambar lingkaran lubang aksial Ø{0:G} mm di titik pusat", holeDiam)
                        });
                        ops.Add(new CadOperation
                        {
                            Id = "cut_hole",
                            Type = OperationType.ExtrudeCut,
                            Parameters = new Dictionary<string, object>
                            {
                                { "depth", totalLength },
                                { "throughAll", true }
                            },
                            DependsOn = new List<string> { "circ_hole" },
                            Description = string.Format("Cut-Extrude Through All lubang aksial Ø{0:G} mm sepanjang {1:G} mm", holeDiam, totalLength)
                        });
                    }

                    var expDims = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "panjang", totalLength },
                        { "total panjang", totalLength },
                        { "length", totalLength },
                        { "diameter", maxDia },
                        { "segmen_count", diameters.Count }
                    };
                    if (holeDiam > 0)
                    {
                        expDims["lubang"] = holeDiam;
                        expDims["lubang_aksial"] = holeDiam;
                    }

                    spec = new CadSpec
                    {
                        RequestId = "fp_shaft_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                        DocumentType = DocumentType.Part,
                        Units = "mm",
                        Source = CadSource.Text,
                        ExplicitDimensions = expDims,
                        Operations = ops,
                        Notes = string.Format("Poros bertingkat {0} segmen total panjang {1:G} mm{2} via Fast Path",
                            diameters.Count, totalLength, holeDiam > 0 ? string.Format(" dengan lubang aksial Ø{0:G} mm", holeDiam) : "")
                    };
                    return true;
                }
            }

            // 2. Fallback to standard 3-segment shaft if explicitly named poros bertingkat / stepped shaft
            if (Regex.IsMatch(text, @"\b(?:poros\s*bertingkat|stepped\s*shaft|as\s*bertingkat)\b", RegexOptions.IgnoreCase))
            {
                double r1 = 10.0, l1 = 30.0;
                double r2 = 15.0, l2 = 50.0;
                double r3 = 12.5, l3 = 40.0;

                var shaftPts = new List<double[]>
                {
                    new double[] { 0.0, 0.0, 0.0 },
                    new double[] { 0.0, r1, 0.0 },
                    new double[] { l1, r1, 0.0 },
                    new double[] { l1, r2, 0.0 },
                    new double[] { l1 + l2, r2, 0.0 },
                    new double[] { l1 + l2, r3, 0.0 },
                    new double[] { l1 + l2 + l3, r3, 0.0 },
                    new double[] { l1 + l2 + l3, 0.0, 0.0 }
                };

                spec = new CadSpec
                {
                    RequestId = "fp_shaft_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                    DocumentType = DocumentType.Part,
                    Units = "mm",
                    Source = CadSource.Text,
                    ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "panjang", l1 + l2 + l3 },
                        { "diameter", r2 * 2.0 },
                        { "segmen_count", 3.0 }
                    },
                    Operations = new List<CadOperation>
                    {
                        new CadOperation
                        {
                            Id = "sk_shaft",
                            Type = OperationType.NewSketch,
                            Parameters = new Dictionary<string, object> { { "plane", "Front" } },
                            Description = "Buka sketsa profil poros bertingkat pada Front Plane"
                        },
                        new CadOperation
                        {
                            Id = "poly_shaft",
                            Type = OperationType.AddPolyline,
                            Parameters = new Dictionary<string, object>
                            {
                                { "points", shaftPts },
                                { "closed", true }
                            },
                            DependsOn = new List<string> { "sk_shaft" },
                            Description = "Gambar kontur poros bertingkat 3 seksi"
                        },
                        new CadOperation
                        {
                            Id = "rev_shaft",
                            Type = OperationType.RevolvedBoss,
                            Parameters = new Dictionary<string, object>
                            {
                                { "angle", 360.0 }
                            },
                            DependsOn = new List<string> { "poly_shaft" },
                            Description = "Revolve 360 derajat profil poros bertingkat"
                        }
                    },
                    Notes = "Poros bertingkat 3 seksi via Fast Path"
                };
                return true;
            }

            return false;
        }

        private static bool TryMatchHemisphere(string text, out CadSpec spec)
        {
            spec = null;
            // Must contain hemisphere keywords: setengah bola, hemisphere, semi sphere, kubah, half sphere
            bool hasHemiKeyword = Regex.IsMatch(text, @"(?:setengah[\s-]*bola|hemi[\s-]*sphere|semi[\s-]*sphere|half[\s-]*sphere|kubah)", RegexOptions.IgnoreCase);
            if (!hasHemiKeyword)
                return false;

            double diameter = 0;
            string unitStr = "mm";

            // Pattern A: "diameter [nilai]", "diam [nilai]", "dia [nilai]", "ø [nilai]", or standalone "d [nilai]" / "d=100" / "d:100" / "d100"
            // Note: Uses word boundaries \b to avoid false positive on words like "dengan" or "dan"
            var mDia = Regex.Match(text, @"(?:(?:\b(?:diameter|diam|dia)\b|[øØ]|\bd\b)\s*[:=]?\s*|\bd(?=\d))(\d+(?:[.,]\d+)?)\s*(mm|cm|m|in(?:ch)?)?", RegexOptions.IgnoreCase);
            if (mDia.Success)
            {
                diameter = ParseDouble(mDia.Groups[1].Value);
                if (mDia.Groups[2].Success && !string.IsNullOrWhiteSpace(mDia.Groups[2].Value))
                {
                    unitStr = mDia.Groups[2].Value.ToLowerInvariant();
                }
            }
            else
            {
                // Pattern B: "radius [nilai]", "jari-jari [nilai]", "rad [nilai]", or standalone "r [nilai]" / "r=50" / "r:50" / "r50"
                var mRad = Regex.Match(text, @"(?:(?:\b(?:radius|jari-jari|jari|rad)\b|\br\b)\s*[:=]?\s*|\br(?=\d))(\d+(?:[.,]\d+)?)\s*(mm|cm|m|in(?:ch)?)?", RegexOptions.IgnoreCase);
                if (mRad.Success)
                {
                    diameter = ParseDouble(mRad.Groups[1].Value) * 2.0;
                    if (mRad.Groups[2].Success && !string.IsNullOrWhiteSpace(mRad.Groups[2].Value))
                    {
                        unitStr = mRad.Groups[2].Value.ToLowerInvariant();
                    }
                }
                else
                {
                    // Pattern C: "setengah bola [pejal] [ukuran] [nilai] [satuan]"
                    var mSimple = Regex.Match(text, @"(?:setengah[\s-]*bola|hemi[\s-]*sphere|semi[\s-]*sphere|kubah)(?:\s+(?:pejal|padat|solid))?\s+(?:(?:dengan\s+)?ukuran\s+)?(\d+(?:[.,]\d+)?)\s*(mm|cm|m|in(?:ch)?)?", RegexOptions.IgnoreCase);
                    if (mSimple.Success)
                    {
                        diameter = ParseDouble(mSimple.Groups[1].Value);
                        if (mSimple.Groups[2].Success && !string.IsNullOrWhiteSpace(mSimple.Groups[2].Value))
                        {
                            unitStr = mSimple.Groups[2].Value.ToLowerInvariant();
                        }
                    }
                }
            }

            if (diameter <= 0)
                return false;

            // Unit normalization to standard millimeters
            if (unitStr == "cm")
            {
                diameter *= 10.0;
            }
            else if (unitStr == "m")
            {
                diameter *= 1000.0;
            }
            else if (unitStr.StartsWith("in"))
            {
                diameter *= 25.4;
            }

            if (diameter > 50000)
                return false;

            spec = new CadSpec
            {
                RequestId = "fp_hemi_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                DocumentType = DocumentType.Part,
                Units = "mm",
                Source = CadSource.Text,
                ExplicitDimensions = new Dictionary<string, double>
                {
                    { "diameter", diameter }
                },
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "hemi1",
                        Type = OperationType.CreateHemisphere,
                        Parameters = new Dictionary<string, object>
                        {
                            { "diameter", diameter },
                            { "plane", "Front" }
                        },
                        Description = string.Format("Buat setengah bola pejal diameter {0:G} mm", diameter)
                    }
                },
                Notes = "Dihasilkan secara deterministik via Fast Path (Setengah Bola Pejal)"
            };

            return true;
        }

        private static bool TryMatchPlateOrBlock(string text, out CadSpec spec)
        {
            spec = null;

            bool isPlateOrBlock = text.Contains("pelat") || text.Contains("plat") || text.Contains("balok") ||
                                  text.Contains("kubus") || text.Contains("box") || text.Contains("persegi") ||
                                  text.Contains("prism");

            // Pattern 1: e.g. "100x60x5" or "100 x 60 x 5" or "100 * 60 * 5" or "100 × 60 × 5"
            var mDims = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(?:x|×|\*)\s*(\d+(?:[.,]\d+)?)\s*(?:x|×|\*)\s*(\d+(?:[.,]\d+)?)\s*(?:mm)?", RegexOptions.IgnoreCase);
            if (mDims.Success)
            {
                double d1 = ParseDouble(mDims.Groups[1].Value);
                double d2 = ParseDouble(mDims.Groups[2].Value);
                double d3 = ParseDouble(mDims.Groups[3].Value);
                if (d1 > 0 && d2 > 0 && d3 > 0)
                {
                    spec = BuildBlockSpec(d1, d2, d3, "Pelat/Balok " + d1 + "x" + d2 + "x" + d3 + " mm");
                    return true;
                }
            }

            // Pattern 2: e.g. "pelat 100x60 mm tebal 5 mm"
            var mPlate = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(?:x|×|\*)\s*(\d+(?:[.,]\d+)?)\s*(?:mm)?.*?(?:tebal|tinggi|ketebalan)\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(?:mm)?", RegexOptions.IgnoreCase);
            if (mPlate.Success)
            {
                double length = ParseDouble(mPlate.Groups[1].Value);
                double width = ParseDouble(mPlate.Groups[2].Value);
                double thickness = ParseDouble(mPlate.Groups[3].Value);
                if (length > 0 && width > 0 && thickness > 0)
                {
                    spec = BuildBlockSpec(length, width, thickness, "Pelat " + length + "x" + width + "x" + thickness + " mm");
                    return true;
                }
            }

            // Pattern 3: e.g. "panjang 100 lebar 50 tebal 20"
            if (isPlateOrBlock)
            {
                var mPanjang = Regex.Match(text, @"panjang\s*[:=]?\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
                var mLebar = Regex.Match(text, @"lebar\s*[:=]?\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
                var mTebal = Regex.Match(text, @"(?:tebal|tinggi|ketebalan)\s*[:=]?\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);

                if (mPanjang.Success && mLebar.Success && mTebal.Success)
                {
                    double p = ParseDouble(mPanjang.Groups[1].Value);
                    double l = ParseDouble(mLebar.Groups[1].Value);
                    double t = ParseDouble(mTebal.Groups[1].Value);
                    if (p > 0 && l > 0 && t > 0)
                    {
                        spec = BuildBlockSpec(p, l, t, "Balok " + p + "x" + l + "x" + t + " mm");
                        return true;
                    }
                }

                // Pattern 4: Kubus e.g. "kubus 50 mm" or "kubus sisi 50"
                var mKubus = Regex.Match(text, @"kubus(?:\s+sisi)?\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(?:mm)?", RegexOptions.IgnoreCase);
                if (mKubus.Success)
                {
                    double s = ParseDouble(mKubus.Groups[1].Value);
                    if (s > 0)
                    {
                        spec = BuildBlockSpec(s, s, s, "Kubus sisi " + s + " mm");
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryMatchPlateWithHole(string text, out CadSpec spec)
        {
            spec = null;
            if (!text.Contains("lubang") && !text.Contains("hole"))
                return false;

            // Plate dimensions 100x60x5 with hole dia 10
            var mDims = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(?:x|×|\*)\s*(\d+(?:[.,]\d+)?)\s*(?:x|×|\*)\s*(\d+(?:[.,]\d+)?)\s*(?:mm)?", RegexOptions.IgnoreCase);
            var mHole = Regex.Match(text, @"(?:lubang|hole).*?(?:diameter|dia|d)\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(?:mm)?", RegexOptions.IgnoreCase);

            if (mDims.Success && mHole.Success)
            {
                double length = ParseDouble(mDims.Groups[1].Value);
                double width = ParseDouble(mDims.Groups[2].Value);
                double thickness = ParseDouble(mDims.Groups[3].Value);
                double holeDia = ParseDouble(mHole.Groups[1].Value);

                if (length > 0 && width > 0 && thickness > 0 && holeDia > 0 && holeDia < Math.Min(length, width))
                {
                    double cx = 0.0;
                    double cy = 0.0;

                    spec = new CadSpec
                    {
                        RequestId = "fp_plate_hole_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                        DocumentType = DocumentType.Part,
                        Units = "mm",
                        Source = CadSource.Text,
                        ExplicitDimensions = new Dictionary<string, double>
                        {
                            { "panjang", length },
                            { "lebar", width },
                            { "tebal", thickness },
                            { "diameter_lubang", holeDia }
                        },
                        Operations = new List<CadOperation>
                        {
                            new CadOperation
                            {
                                Id = "sk1",
                                Type = OperationType.NewSketch,
                                Parameters = new Dictionary<string, object> { { "plane", "Front" } },
                                Description = "Buat sketch di Front Plane"
                            },
                            new CadOperation
                            {
                                Id = "rect1",
                                Type = OperationType.AddRectangle,
                                Parameters = new Dictionary<string, object> { { "width", length }, { "height", width } },
                                DependsOn = new List<string> { "sk1" },
                                Description = string.Format("Gambar persegi {0:G}x{1:G} mm", length, width)
                            },
                            new CadOperation
                            {
                                Id = "ext1",
                                Type = OperationType.ExtrudeBoss,
                                Parameters = new Dictionary<string, object> { { "depth", thickness }, { "profile", "rect1" } },
                                DependsOn = new List<string> { "rect1" },
                                Description = string.Format("Extrude pelat tebal {0:G} mm", thickness)
                            },
                            new CadOperation
                            {
                                Id = "hole1",
                                Type = OperationType.SimpleHole,
                                Parameters = new Dictionary<string, object>
                                {
                                    { "diameter", holeDia },
                                    { "depth", thickness },
                                    { "x", cx },
                                    { "y", cy }
                                },
                                DependsOn = new List<string> { "ext1" },
                                Description = string.Format("Buat lubang tembus diameter {0:G} mm di tengah ({1:G}, {2:G})", holeDia, cx, cy)
                            }
                        },
                        Notes = "Dihasilkan secara deterministik via Fast Path (Pelat dengan Lubang)"
                    };
                    return true;
                }
            }

            return false;
        }

        private static bool TryMatchCylinder(string text, out CadSpec spec)
        {
            spec = null;
            if (!text.Contains("silinder") && !text.Contains("tabung") && !text.Contains("cylinder"))
                return false;

            var mDia = Regex.Match(text, @"(?:diameter|dia|d)\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(?:mm)?", RegexOptions.IgnoreCase);
            var mHeight = Regex.Match(text, @"(?:tinggi|panjang|tebal|t|h)\s*[:=]?\s*(\d+(?:[.,]\d+)?)\s*(?:mm)?", RegexOptions.IgnoreCase);

            if (mDia.Success && mHeight.Success)
            {
                double dia = ParseDouble(mDia.Groups[1].Value);
                double height = ParseDouble(mHeight.Groups[1].Value);

                if (dia > 0 && height > 0)
                {
                    spec = new CadSpec
                    {
                        RequestId = "fp_cyl_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                        DocumentType = DocumentType.Part,
                        Units = "mm",
                        Source = CadSource.Text,
                        ExplicitDimensions = new Dictionary<string, double>
                        {
                            { "diameter", dia },
                            { "tinggi", height }
                        },
                        Operations = new List<CadOperation>
                        {
                            new CadOperation
                            {
                                Id = "sk1",
                                Type = OperationType.NewSketch,
                                Parameters = new Dictionary<string, object> { { "plane", "Top" } },
                                Description = "Buat sketch di Top Plane"
                            },
                            new CadOperation
                            {
                                Id = "circ1",
                                Type = OperationType.AddCircle,
                                Parameters = new Dictionary<string, object> { { "diameter", dia }, { "x", 0.0 }, { "y", 0.0 } },
                                DependsOn = new List<string> { "sk1" },
                                Description = string.Format("Gambar lingkaran diameter {0:G} mm", dia)
                            },
                            new CadOperation
                            {
                                Id = "ext1",
                                Type = OperationType.ExtrudeBoss,
                                Parameters = new Dictionary<string, object> { { "depth", height }, { "profile", "circ1" } },
                                DependsOn = new List<string> { "circ1" },
                                Description = string.Format("Extrude silinder tinggi {0:G} mm", height)
                            }
                        },
                        Notes = "Dihasilkan secara deterministik via Fast Path (Silinder Pejal)"
                    };
                    return true;
                }
            }

            return false;
        }

        private static CadSpec BuildBlockSpec(double length, double width, double height, string desc)
        {
            return new CadSpec
            {
                RequestId = "fp_block_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                DocumentType = DocumentType.Part,
                Units = "mm",
                Source = CadSource.Text,
                ExplicitDimensions = new Dictionary<string, double>
                {
                    { "panjang", length },
                    { "lebar", width },
                    { "tebal", height }
                },
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "sk1",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object> { { "plane", "Front" } },
                        Description = "Buat sketch di Front Plane"
                    },
                    new CadOperation
                    {
                        Id = "rect1",
                        Type = OperationType.AddRectangle,
                        Parameters = new Dictionary<string, object> { { "width", length }, { "height", width } },
                        DependsOn = new List<string> { "sk1" },
                        Description = string.Format("Gambar persegi {0:G}x{1:G} mm", length, width)
                    },
                    new CadOperation
                    {
                        Id = "ext1",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object> { { "depth", height }, { "profile", "rect1" } },
                        DependsOn = new List<string> { "rect1" },
                        Description = string.Format("Extrude {0:G} mm", height)
                    }
                },
                Notes = "Dihasilkan secara deterministik via Fast Path (" + desc + ")"
            };
        }

        private static double ParseDouble(string str)
        {
            if (string.IsNullOrWhiteSpace(str)) return 0;
            str = str.Replace(',', '.');
            double val;
            if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
            {
                return val;
            }
            return 0;
        }
    }
}
