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
            ModelDoc2 activeDoc = null;

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
                            bool extOk = ExtrudeBoss(activeDoc, depthM);
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
                            bool cutOk = ExtrudeCut(activeDoc, cutM);
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
                            bool filOk = AddFillet(activeDoc, filM);
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
                            bool chamOk = AddChamfer(activeDoc, chamM);
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
                            bool holeOk = AddSimpleHole(activeDoc, hDiamM, hDepthM);
                            opRes = holeOk
                                ? OperationResult.Succeeded(op.Id)
                                : OperationResult.Failed(op.Id, "Gagal menambahkan lubang.");
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
                    if (!rebuildSuccess)
                    {
                        execResult.FeatureWarnings.Add("Peringatan: Terdapat peringatan atau error saat membangun ulang (rebuild) model.");
                    }

                    execResult.SolidBodyCount = GetSolidBodyCount(activeDoc);
                    execResult.BoundingBoxMm = GetBoundingBoxMm(activeDoc);

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
            try
            {
                // FeatureExtrusion3 parameters:
                // Sd (single direction) = true, Flip = false, Dir = false, T1 = Blind (0), T2 = 0, D1 = depth, D2 = 0
                Feature feat = doc.FeatureManager.FeatureExtrusion3(
                    true, false, false, (int)swEndConditions_e.swEndCondBlind, 0,
                    depthM, 0, false, false, false, false, 0, 0, false, false, false, false, true, true, true, 0, 0, false);

                return feat != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Extrude Boss", ex);
                return false;
            }
        }

        public bool ExtrudeCut(ModelDoc2 doc, double depthM)
        {
            try
            {
                Feature feat = doc.FeatureManager.FeatureCut4(
                    true, false, false, (int)swEndConditions_e.swEndCondBlind, 0,
                    depthM, 0, false, false, false, false, 0, 0, false, false, false, false, false, true, true, true, true, false, 0, 0, false, false);

                return feat != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Extrude Cut", ex);
                return false;
            }
        }

        public bool AddFillet(ModelDoc2 doc, double radiusM)
        {
            try
            {
                object feat = doc.FeatureManager.FeatureFillet(
                    (int)swFeatureFilletOptions_e.swFeatureFilletUniformRadius,
                    radiusM,
                    (int)swFeatureFilletType_e.swFeatureFilletType_Simple,
                    0,
                    null, null, null);

                return feat != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Fillet", ex);
                return false;
            }
        }

        public bool AddChamfer(ModelDoc2 doc, double distanceM)
        {
            try
            {
                Feature feat = doc.FeatureManager.InsertFeatureChamfer(
                    1, (int)swChamferType_e.swChamferEqualDistance, distanceM, 0, 0, 0, 0, 0);

                return feat != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Chamfer", ex);
                return false;
            }
        }

        public bool AddSimpleHole(ModelDoc2 doc, double diamM, double depthM)
        {
            try
            {
                // Hole creation via Sketch circle + Cut-Extrude (universal across SW 2023-2025)
                doc.SketchManager.InsertSketch(true);
                DrawCircle(doc, 0, 0, diamM / 2.0);
                return ExtrudeCut(doc, depthM);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Hole", ex);
                return false;
            }
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
