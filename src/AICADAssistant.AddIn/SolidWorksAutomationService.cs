using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;

namespace AICADAssistant.AddIn
{
    public class SolidWorksAutomationService
    {
        private readonly ISldWorks _swApp;

        public SolidWorksAutomationService(ISldWorks swApp)
        {
            _swApp = swApp;
        }

        public ModelDoc2 GetActiveDocument()
        {
            if (_swApp == null) return null;
            return _swApp.ActiveDoc as ModelDoc2;
        }

        public ExecutionResult ExecuteSpec(CadSpec spec, string saveDirectory = null)
        {
            if (_swApp == null)
            {
                return ExecutionResult.CreateFailure("SOLIDWORKS tidak terhubung atau instance aplikasi tidak ditemukan.");
            }

            if (spec == null || spec.Operations == null || spec.Operations.Count == 0)
            {
                return ExecutionResult.CreateFailure("Spesifikasi CAD kosong atau tidak memiliki operasi.");
            }

            var stopwatch = Stopwatch.StartNew();
            var execResult = new ExecutionResult();
            ModelDoc2 activeDoc = GetActiveDocument();
            var createdFeatures = new List<string>();

            try
            {
                for (int i = 0; i < spec.Operations.Count; i++)
                {
                    var op = spec.Operations[i];
                    OperationResult opRes = null;

                    switch (op.Type)
                    {
                        case OperationType.NewPart:
                            activeDoc = CreateNewPartDocument();
                            opRes = activeDoc != null
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal membuat dokumen part baru.");
                            break;

                        case OperationType.NewSketch:
                            if (activeDoc == null) activeDoc = CreateNewPartDocument();
                            string plane = "Front";
                            op.TryGetString("plane", out plane);
                            bool skOk = OpenSketchOnPlane(activeDoc, plane);
                            opRes = skOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal membuka sketch pada bidang: " + plane);
                            break;

                        case OperationType.AddRectangle:
                            if (activeDoc == null) activeDoc = CreateNewPartDocument();
                            double wVal, hVal;
                            op.TryGetDouble("width", out wVal);
                            op.TryGetDouble("height", out hVal);
                            double wM = UnitConverter.ConvertToInternal(wVal, spec.Units);
                            double hM = UnitConverter.ConvertToInternal(hVal, spec.Units);
                            bool rectOk = DrawCenterRectangle(activeDoc, wM, hM);
                            opRes = rectOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal menggambar persegi panjang.");
                            break;

                        case OperationType.AddCircle:
                            if (activeDoc == null) activeDoc = CreateNewPartDocument();
                            double dVal;
                            if (!op.TryGetDouble("diameter", out dVal))
                            {
                                double rVal;
                                if (op.TryGetDouble("radius", out rVal))
                                    dVal = rVal * 2.0;
                                else
                                    dVal = 10.0;
                            }
                            double rM = UnitConverter.ConvertToInternal(dVal / 2.0, spec.Units);
                            bool circOk = DrawCircle(activeDoc, 0, 0, rM);
                            opRes = circOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal menggambar lingkaran.");
                            break;

                        case OperationType.ExtrudeBoss:
                            if (activeDoc == null)
                            {
                                opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi extrude.");
                                break;
                            }
                            double depthVal;
                            op.TryGetDouble("depth", out depthVal);
                            double depthM = UnitConverter.ConvertToInternal(depthVal, spec.Units);
                            string extFeatName;
                            bool extOk = ExtrudeBoss(activeDoc, depthM, out extFeatName);
                            if (extOk && !string.IsNullOrEmpty(extFeatName)) createdFeatures.Add(extFeatName);
                            opRes = extOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal melakukan Extrude Boss.");
                            break;

                        case OperationType.ExtrudeCut:
                            if (activeDoc == null)
                            {
                                opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi cut.");
                                break;
                            }
                            double cutVal;
                            op.TryGetDouble("depth", out cutVal);
                            double cutM = UnitConverter.ConvertToInternal(cutVal, spec.Units);
                            string cutFeatName;
                            bool cutOk = ExtrudeCut(activeDoc, cutM, out cutFeatName);
                            if (cutOk && !string.IsNullOrEmpty(cutFeatName)) createdFeatures.Add(cutFeatName);
                            opRes = cutOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal melakukan Extrude Cut.");
                            break;

                        case OperationType.Fillet:
                            if (activeDoc == null)
                            {
                                opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi fillet.");
                                break;
                            }
                            double filVal;
                            op.TryGetDouble("radius", out filVal);
                            double filM = UnitConverter.ConvertToInternal(filVal, spec.Units);
                            string filFeatName;
                            bool filOk = AddFillet(activeDoc, filM, out filFeatName);
                            if (filOk && !string.IsNullOrEmpty(filFeatName)) createdFeatures.Add(filFeatName);
                            opRes = filOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal menambahkan fillet.");
                            break;

                        case OperationType.Chamfer:
                            if (activeDoc == null)
                            {
                                opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi chamfer.");
                                break;
                            }
                            double chamVal;
                            op.TryGetDouble("distance", out chamVal);
                            double chamM = UnitConverter.ConvertToInternal(chamVal, spec.Units);
                            string chamFeatName;
                            bool chamOk = AddChamfer(activeDoc, chamM, out chamFeatName);
                            if (chamOk && !string.IsNullOrEmpty(chamFeatName)) createdFeatures.Add(chamFeatName);
                            opRes = chamOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal menambahkan chamfer.");
                            break;

                        case OperationType.SimpleHole:
                            if (activeDoc == null)
                            {
                                opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi hole.");
                                break;
                            }
                            double hDiamVal, hDepthVal;
                            op.TryGetDouble("diameter", out hDiamVal);
                            op.TryGetDouble("depth", out hDepthVal);
                            double hDiamM = UnitConverter.ConvertToInternal(hDiamVal, spec.Units);
                            double hDepthM = UnitConverter.ConvertToInternal(hDepthVal, spec.Units);
                            string holeFeatName;
                            bool holeOk = AddSimpleHole(activeDoc, hDiamM, hDepthM, out holeFeatName);
                            if (holeOk && !string.IsNullOrEmpty(holeFeatName)) createdFeatures.Add(holeFeatName);
                            opRes = holeOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal menambahkan lubang.");
                            break;

                        case OperationType.RevolvedBoss:
                            if (activeDoc == null)
                            {
                                opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi revolve.");
                                break;
                            }
                            double revAngleDeg = 360.0;
                            if (op.TryGetDouble("angle", out revAngleDeg) && revAngleDeg <= 0)
                                revAngleDeg = 360.0;
                            double revAngleRad = revAngleDeg * Math.PI / 180.0;
                            string revFeatName;
                            bool revOk = RevolvedBoss(activeDoc, revAngleRad, out revFeatName);
                            if (revOk && !string.IsNullOrEmpty(revFeatName)) createdFeatures.Add(revFeatName);
                            opRes = revOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal melakukan Revolved Boss.");
                            break;

                        case OperationType.CreateHemisphere:
                            if (activeDoc == null) activeDoc = CreateNewPartDocument();
                            if (activeDoc == null)
                            {
                                opRes = OperationResult.Failed(op.Id, "Gagal menginisialisasi dokumen untuk pembuatan setengah bola.");
                                break;
                            }
                            double hemiDiam;
                            if (!op.TryGetDouble("diameter", out hemiDiam))
                            {
                                double hemiRad;
                                if (op.TryGetDouble("radius", out hemiRad))
                                    hemiDiam = hemiRad * 2.0;
                                else
                                    hemiDiam = 100.0;
                            }
                            double hemiDiamM = UnitConverter.ConvertToInternal(hemiDiam, spec.Units);
                            string hemiPlane = "Front";
                            op.TryGetString("plane", out hemiPlane);
                            string hemiFeatName;
                            bool hemiOk = CreateHemisphere(activeDoc, hemiDiamM, hemiPlane, out hemiFeatName);
                            if (hemiOk && !string.IsNullOrEmpty(hemiFeatName)) createdFeatures.Add(hemiFeatName);
                            opRes = hemiOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal membuat setengah bola (hemisphere).");
                            break;

                        default:
                            opRes = OperationResult.Succeeded(op.Id);
                            break;
                    }

                    if (opRes != null)
                    {
                        execResult.CompletedOperations.Add(opRes);
                        if (!opRes.Success)
                        {
                            execResult.Success = false;
                            execResult.OverallStatus = ExecutionStatus.Failed;
                            execResult.FailedOperation = opRes;
                            execResult.ErrorMessage = opRes.ErrorMessage;
                            return execResult;
                        }
                    }
                }

                // Post-execution: Rebuild & Geometric Verification
                if (activeDoc != null)
                {
                    bool rebuildSuccess = RebuildModel(activeDoc);
                    execResult.RebuildSuccess = rebuildSuccess;
                    if (!rebuildSuccess)
                    {
                        execResult.FeatureWarnings.Add("Peringatan: Terdapat peringatan atau error saat membangun ulang (rebuild) model.");
                    }

                    execResult.DocumentTitle = activeDoc.GetTitle();
                    execResult.SolidBodyCount = GetSolidBodyCount(activeDoc);
                    execResult.BoundingBoxMm = GetBoundingBoxMm(activeDoc);
                    execResult.VolumeMm3 = GetVolumeMm3(activeDoc);
                    if (createdFeatures.Count > 0)
                    {
                        execResult.FeatureName = string.Join(", ", createdFeatures);
                    }

                    // STRICT CHECK: Reject false success if no solid bodies exist
                    if (execResult.SolidBodyCount <= 0)
                    {
                        stopwatch.Stop();
                        execResult.Success = false;
                        execResult.OverallStatus = ExecutionStatus.Failed;
                        execResult.ErrorMessage = "Pembuatan geometri gagal: Tidak ada Solid Body yang terbentuk (Body Count = 0). Periksa apakah sketch tertutup dan operasi fitur valid.";
                        execResult.DurationMs = stopwatch.ElapsedMilliseconds;
                        return execResult;
                    }

                    // Save part file
                    string targetFolder = !string.IsNullOrWhiteSpace(saveDirectory) && Directory.Exists(saveDirectory)
                        ? saveDirectory
                        : AppSettings.GetDefaultOutputDirectory();

                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                    }

                    string partFileName = string.Format("Part_{0:yyyyMMdd_HHmmss}.SLDPRT", DateTime.Now);
                    string fullPartPath = Path.Combine(targetFolder, partFileName);
                    if (SaveAs(activeDoc, fullPartPath))
                    {
                        execResult.SavedFilePath = fullPartPath;
                    }

                    // Handle export targets (STEP, STL)
                    if (spec.ExportTargets != null)
                    {
                        foreach (var target in spec.ExportTargets)
                        {
                            string tUpper = target.Trim().ToUpperInvariant();
                            if (tUpper == "STEP" || tUpper == "STP")
                            {
                                string stepPath = Path.ChangeExtension(fullPartPath, ".step");
                                if (ExportStep(activeDoc, stepPath))
                                {
                                    execResult.ExportedFilePaths.Add(stepPath);
                                }
                            }
                            else if (tUpper == "STL")
                            {
                                string stlPath = Path.ChangeExtension(fullPartPath, ".stl");
                                if (ExportStl(activeDoc, stlPath))
                                {
                                    execResult.ExportedFilePaths.Add(stlPath);
                                }
                            }
                        }
                    }
                }

                stopwatch.Stop();
                execResult.Success = true;
                execResult.OverallStatus = ExecutionStatus.Success;
                execResult.DurationMs = stopwatch.ElapsedMilliseconds;
                return execResult;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LoggingService.Instance.Error("Kesalahan saat mengeksekusi operasi CAD", ex);
                return ExecutionResult.CreateFailure("Terjadi kesalahan interop CAD: " + ex.Message);
            }
        }

        public ModelDoc2 CreateNewPartDocument()
        {
            try
            {
                string defaultTemplate = _swApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                if (string.IsNullOrWhiteSpace(defaultTemplate) || !File.Exists(defaultTemplate))
                {
                    // Fallback to empty string for standard template
                    defaultTemplate = "";
                }

                object newDoc = _swApp.NewDocument(defaultTemplate, 0, 0, 0);
                return newDoc as ModelDoc2;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat part baru", ex);
                return null;
            }
        }

        public bool OpenSketchOnPlane(ModelDoc2 doc, string planeName)
        {
            try
            {
                doc.ClearSelection2(true);
                string actualPlane = "Front Plane";
                string pLower = (planeName ?? "").ToLowerInvariant();
                if (pLower.Contains("top")) actualPlane = "Top Plane";
                else if (pLower.Contains("right")) actualPlane = "Right Plane";

                bool selected = doc.Extension.SelectByID2(actualPlane, "PLANE", 0, 0, 0, false, 0, null, 0);
                if (!selected)
                {
                    // Try without "Plane" suffix
                    selected = doc.Extension.SelectByID2(planeName, "PLANE", 0, 0, 0, false, 0, null, 0);
                }

                doc.SketchManager.InsertSketch(true);
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuka sketch di plane: " + planeName, ex);
                return false;
            }
        }

        public bool DrawCenterRectangle(ModelDoc2 doc, double widthM, double heightM)
        {
            try
            {
                double halfW = widthM / 2.0;
                double halfH = heightM / 2.0;
                object lines = doc.SketchManager.CreateCenterRectangle(0, 0, 0, halfW, halfH, 0);
                return lines != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat center rectangle", ex);
                return false;
            }
        }

        public bool DrawCircle(ModelDoc2 doc, double centerXm, double centerYm, double radiusM)
        {
            try
            {
                object circle = doc.SketchManager.CreateCircle(centerXm, centerYm, 0, centerXm + radiusM, centerYm, 0);
                return circle != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat lingkaran", ex);
                return false;
            }
        }

        public bool ExtrudeBoss(ModelDoc2 doc, double depthM)
        {
            string dummy;
            return ExtrudeBoss(doc, depthM, out dummy);
        }

        public bool ExtrudeBoss(ModelDoc2 doc, double depthM, out string featName)
        {
            featName = null;
            try
            {
                // FeatureExtrusion3 parameters:
                // Sd (single direction) = true, Flip = false, Dir = false, T1 = Blind (0), T2 = 0, D1 = depth, D2 = 0
                Feature feat = doc.FeatureManager.FeatureExtrusion3(
                    true, false, false, (int)swEndConditions_e.swEndCondBlind, 0,
                    depthM, 0, false, false, false, false, 0, 0, false, false, false, false, true, true, true, 0, 0, false);

                if (feat != null)
                {
                    featName = feat.Name;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Extrude Boss", ex);
                return false;
            }
        }

        public bool ExtrudeCut(ModelDoc2 doc, double depthM)
        {
            string dummy;
            return ExtrudeCut(doc, depthM, out dummy);
        }

        public bool ExtrudeCut(ModelDoc2 doc, double depthM, out string featName)
        {
            featName = null;
            try
            {
                Feature feat = doc.FeatureManager.FeatureCut4(
                    true, false, false, (int)swEndConditions_e.swEndCondBlind, 0,
                    depthM, 0, false, false, false, false, 0, 0, false, false, false, false, false, true, true, true, true, false, 0, 0, false, false);

                if (feat != null)
                {
                    featName = feat.Name;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Extrude Cut", ex);
                return false;
            }
        }

        public bool AddFillet(ModelDoc2 doc, double radiusM)
        {
            string dummy;
            return AddFillet(doc, radiusM, out dummy);
        }

        public bool AddFillet(ModelDoc2 doc, double radiusM, out string featName)
        {
            featName = null;
            try
            {
                object rawFeat = doc.FeatureManager.FeatureFillet(
                    (int)swFeatureFilletOptions_e.swFeatureFilletUniformRadius,
                    radiusM,
                    (int)swFeatureFilletType_e.swFeatureFilletType_Simple,
                    0,
                    null, null, null);

                Feature feat = rawFeat as Feature;
                if (feat != null)
                {
                    featName = feat.Name;
                    return true;
                }
                return rawFeat != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Fillet", ex);
                return false;
            }
        }

        public bool AddChamfer(ModelDoc2 doc, double distanceM)
        {
            string dummy;
            return AddChamfer(doc, distanceM, out dummy);
        }

        public bool AddChamfer(ModelDoc2 doc, double distanceM, out string featName)
        {
            featName = null;
            try
            {
                Feature feat = doc.FeatureManager.InsertFeatureChamfer(
                    1, (int)swChamferType_e.swChamferEqualDistance, distanceM, 0, 0, 0, 0, 0);

                if (feat != null)
                {
                    featName = feat.Name;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Chamfer", ex);
                return false;
            }
        }

        public bool AddSimpleHole(ModelDoc2 doc, double diamM, double depthM)
        {
            string dummy;
            return AddSimpleHole(doc, diamM, depthM, out dummy);
        }

        public bool AddSimpleHole(ModelDoc2 doc, double diamM, double depthM, out string featName)
        {
            featName = null;
            try
            {
                // Hole creation via Sketch circle + Cut-Extrude (universal across SW 2023-2025)
                doc.SketchManager.InsertSketch(true);
                DrawCircle(doc, 0, 0, diamM / 2.0);
                return ExtrudeCut(doc, depthM, out featName);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Hole", ex);
                return false;
            }
        }

        public bool RevolvedBoss(ModelDoc2 doc, double angleRad)
        {
            string dummy;
            return RevolvedBoss(doc, angleRad, out dummy);
        }

        public bool RevolvedBoss(ModelDoc2 doc, double angleRad, out string featName)
        {
            featName = null;
            try
            {
                if (doc == null) return false;
                if (angleRad <= 0) angleRad = 2.0 * Math.PI;

                Feature feat = doc.FeatureManager.FeatureRevolve2(
                    true,                           // SingleDir
                    true,                           // IsSolid
                    false,                          // IsThin
                    false,                          // IsCut
                    false,                          // ReverseDir
                    false,                          // BothDirectionUpToSameEntity
                    (int)swEndConditions_e.swEndCondBlind, // Dir1Type (0)
                    0,                              // Dir2Type
                    angleRad,                       // Dir1Angle
                    0,                              // Dir2Angle
                    false,                          // OffsetReverse1
                    false,                          // OffsetReverse2
                    0,                              // OffsetDistance1
                    0,                              // OffsetDistance2
                    0,                              // ThinType
                    0,                              // ThinThickness1
                    0,                              // ThinThickness2
                    true,                           // Merge
                    true,                           // UseFeatScope
                    true                            // UseAutoSelect
                );

                if (feat == null)
                {
                    feat = doc.FeatureManager.FeatureRevolve(
                        angleRad, false, 0, (int)swEndConditions_e.swEndCondBlind, 0, true, true, true);
                }

                if (feat != null)
                {
                    featName = feat.Name;
                    LoggingService.Instance.Info("Berhasil membuat Revolved Boss: " + featName);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal melakukan Revolved Boss", ex);
                return false;
            }
        }

        public bool CreateHemisphere(ModelDoc2 doc, double diamM, string planeName, out string featName)
        {
            featName = null;
            try
            {
                if (doc == null) return false;
                double radiusM = diamM / 2.0;
                if (radiusM <= 0) return false;

                // 1. Open sketch on specified plane (default "Front Plane")
                if (!OpenSketchOnPlane(doc, planeName))
                {
                    LoggingService.Instance.Error("Gagal membuka sketch untuk setengah bola di bidang: " + planeName);
                    return false;
                }

                // 2. Draw quarter-circle arc from (R, 0, 0) to (0, R, 0) with center (0, 0, 0)
                // Direction: +1 = Counter-Clockwise in XY plane
                SketchSegment arc = doc.SketchManager.CreateArc(0, 0, 0, radiusM, 0, 0, 0, radiusM, 0, 1);
                if (arc == null)
                {
                    LoggingService.Instance.Error("Gagal menggambar arc seperempat lingkaran untuk setengah bola");
                    return false;
                }

                // 3. Draw horizontal base line from (0, 0, 0) to (R, 0, 0)
                SketchSegment baseLine = doc.SketchManager.CreateLine(0, 0, 0, radiusM, 0, 0);

                // 4. Draw vertical rotation axis line from (0, R, 0) to (0, 0, 0)
                SketchSegment axisLine = doc.SketchManager.CreateLine(0, radiusM, 0, 0, 0, 0);

                // 5. Select the vertical axis line with Mark = 4 (SolidWorks Revolve axis)
                doc.ClearSelection2(true);
                if (axisLine != null)
                {
                    axisLine.Select2(false, 4);
                }

                // 6. Execute Revolve 360 degrees (2 * PI radians)
                Feature feat = doc.FeatureManager.FeatureRevolve2(
                    true,                           // SingleDir
                    true,                           // IsSolid
                    false,                          // IsThin
                    false,                          // IsCut
                    false,                          // ReverseDir
                    false,                          // BothDirectionUpToSameEntity
                    (int)swEndConditions_e.swEndCondBlind, // Dir1Type (0)
                    0,                              // Dir2Type
                    2.0 * Math.PI,                  // Dir1Angle (360 degrees in radians)
                    0,                              // Dir2Angle
                    false,                          // OffsetReverse1
                    false,                          // OffsetReverse2
                    0,                              // OffsetDistance1
                    0,                              // OffsetDistance2
                    0,                              // ThinType
                    0,                              // ThinThickness1
                    0,                              // ThinThickness2
                    true,                           // Merge
                    true,                           // UseFeatScope
                    true                            // UseAutoSelect
                );

                if (feat == null)
                {
                    // Fallback to FeatureRevolve
                    feat = doc.FeatureManager.FeatureRevolve(
                        2.0 * Math.PI, false, 0, (int)swEndConditions_e.swEndCondBlind, 0, true, true, true);
                }

                if (feat != null)
                {
                    featName = feat.Name;
                    LoggingService.Instance.Info(string.Format("Berhasil membuat Setengah Bola solid (diameter {0:F1} mm, tinggi {1:F1} mm): {2}", diamM * 1000.0, radiusM * 1000.0, featName));
                    return true;
                }

                LoggingService.Instance.Error("FeatureRevolve mengembalikan null saat membuat setengah bola");
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Exception saat membuat setengah bola", ex);
                return false;
            }
        }

        public double GetVolumeMm3(ModelDoc2 doc)
        {
            try
            {
                if (doc == null || doc.Extension == null) return 0;
                int status = 0;
                object rawMass = doc.Extension.GetMassProperties2(1, out status, false);
                double[] massProps = rawMass as double[];
                if (massProps != null && massProps.Length > 3)
                {
                    // massProps[3] is volume in m^3. Convert to mm^3 (* 1e9)
                    return massProps[3] * 1e9;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("Gagal membaca volume model: " + ex.Message);
            }
            return 0;
        }

        public bool RebuildModel(ModelDoc2 doc)
        {
            try
            {
                return doc.ForceRebuild3(true);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal rebuild model", ex);
                return false;
            }
        }

        public int GetSolidBodyCount(ModelDoc2 doc)
        {
            try
            {
                PartDoc part = doc as PartDoc;
                if (part == null) return 0;

                object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                return bodies != null ? bodies.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        public double[] GetBoundingBoxMm(ModelDoc2 doc)
        {
            var res = new double[3];
            try
            {
                PartDoc part = doc as PartDoc;
                if (part == null) return res;

                double[] box = part.GetPartBox(true) as double[];
                if (box != null && box.Length >= 6)
                {
                    // box: Xmin, Ymin, Zmin, Xmax, Ymax, Zmax (in meters)
                    res[0] = Math.Abs(box[3] - box[0]) * 1000.0;
                    res[1] = Math.Abs(box[4] - box[1]) * 1000.0;
                    res[2] = Math.Abs(box[5] - box[2]) * 1000.0;
                }
            }
            catch { }
            return res;
        }

        public bool SaveAs(ModelDoc2 doc, string fullPath)
        {
            try
            {
                int errors = 0;
                int warnings = 0;
                return doc.Extension.SaveAs(fullPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal menyimpan berkas: " + fullPath, ex);
                return false;
            }
        }

        public bool ExportStep(ModelDoc2 doc, string fullPath)
        {
            return SaveAs(doc, fullPath);
        }

        public bool ExportStl(ModelDoc2 doc, string fullPath)
        {
            return SaveAs(doc, fullPath);
        }
    }
}
