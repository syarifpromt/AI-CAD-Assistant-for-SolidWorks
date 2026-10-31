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

        public ExecutionResult ExecuteSpec(CadSpec spec, string saveDirectory = null, HashSet<string> existingCompletedOpIds = null)
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
            var completedOpIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingCompletedOpIds != null && existingCompletedOpIds.Count > 0)
            {
                completedOpIds.UnionWith(existingCompletedOpIds);
                LoggingService.Instance.Info(string.Format("Reconcile: Memuat {0} ID operasi yang sudah selesai dari konteks aktif: {1}",
                    existingCompletedOpIds.Count, string.Join(", ", existingCompletedOpIds)));
            }

            int totalOps = spec.Operations.Count;

            LoggingService.Instance.Info(string.Format("[EXECUTOR_START] RequestId='{0}', TotalOperations={1}, Units='{2}'",
                spec.RequestId, totalOps, spec.Units));

            try
            {
                for (int i = 0; i < totalOps; i++)
                {
                    var op = spec.Operations[i];
                    if (op == null) continue;

                    // If operation is already completed on active model, skip geometry recreation
                    if (existingCompletedOpIds != null && existingCompletedOpIds.Contains(op.Id))
                    {
                        LoggingService.Instance.Info(string.Format("[OP_EXISTING] Op #{0}/{1} [{2}] Type={3} sudah ada pada model aktif, melompati eksekusi geometri.",
                            i + 1, totalOps, op.Id ?? "?", op.Type));
                        completedOpIds.Add(op.Id);
                        execResult.CompletedOperations.Add(OperationResult.Succeeded(op.Id));
                        continue;
                    }

                    OperationResult opRes = null;

                    string paramsSummary = "";
                    if (op.Parameters != null && op.Parameters.Count > 0)
                    {
                        var pList = new List<string>();
                        foreach (var kv in op.Parameters)
                        {
                            pList.Add(kv.Key + "=" + (kv.Value != null ? kv.Value.ToString() : "null"));
                        }
                        paramsSummary = string.Join(", ", pList.ToArray());
                    }
                    else
                    {
                        paramsSummary = "none";
                    }

                    string dependsSummary = (op.DependsOn != null && op.DependsOn.Count > 0)
                        ? string.Join(", ", op.DependsOn.ToArray())
                        : "none";

                    LoggingService.Instance.Info(string.Format("[OP_START] Op #{0}/{1} [{2}] Type={3} ({4}): Params=[{5}], DependsOn=[{6}]",
                        i + 1, totalOps, op.Id ?? "?", op.Type, op.Description ?? string.Empty, paramsSummary, dependsSummary));

                    // Verify dependencies (DependsOn)
                    if (op.DependsOn != null && op.DependsOn.Count > 0)
                    {
                        string unmetDep = null;
                        foreach (var depId in op.DependsOn)
                        {
                            if (!completedOpIds.Contains(depId))
                            {
                                unmetDep = depId;
                                break;
                            }
                        }

                        if (unmetDep != null)
                        {
                            string depError = string.Format("Operasi '{0}' ({1}) tidak dapat dijalankan karena dependensi '{2}' belum selesai atau gagal.",
                                op.Id ?? "?", op.Type, unmetDep);
                            LoggingService.Instance.Error(string.Format("[OP_DEPENDENCY_FAILED] {0}", depError));
                            opRes = OperationResult.Failed(op.Id, depError);

                            execResult.CompletedOperations.Add(opRes);
                            execResult.Success = false;
                            execResult.OverallStatus = ExecutionStatus.Failed;
                            execResult.FailedOperation = opRes;
                            execResult.ErrorMessage = depError;
                            stopwatch.Stop();
                            execResult.DurationMs = stopwatch.ElapsedMilliseconds;

                            if (activeDoc != null)
                            {
                                execResult.DocumentTitle = activeDoc.GetTitle();
                                execResult.SolidBodyCount = GetSolidBodyCount(activeDoc);
                                execResult.RebuildSuccess = RebuildModel(activeDoc);
                                execResult.VolumeMm3 = GetVolumeMm3(activeDoc);
                            }
                            return execResult;
                        }
                    }

                    try
                    {
                        switch (op.Type)
                        {
                            case OperationType.NewPart:
                                activeDoc = CreateNewPartDocument();
                                opRes = activeDoc != null
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal membuat dokumen part baru di SOLIDWORKS.");
                                break;

                            case OperationType.NewSketch:
                                if (activeDoc == null) activeDoc = CreateNewPartDocument();
                                string plane = "Front";
                                op.TryGetString("plane", out plane);
                                double targetX = 0, targetY = 0, targetZ = 0;
                                double[] targetPtM = null;
                                if (op.TryGetDouble("targetX", out targetX) || op.TryGetDouble("targetY", out targetY))
                                {
                                    op.TryGetDouble("targetZ", out targetZ);
                                    targetPtM = new double[] {
                                        UnitConverter.ConvertToInternal(targetX, spec.Units),
                                        UnitConverter.ConvertToInternal(targetY, spec.Units),
                                        UnitConverter.ConvertToInternal(targetZ, spec.Units)
                                    };
                                }
                                bool skOk = OpenSketchOnPlane(activeDoc, plane, null, targetPtM);
                                opRes = skOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal membuka sketch pada bidang/face: " + plane);
                                break;

                            case OperationType.EndSketch:
                                if (activeDoc != null && activeDoc.SketchManager != null && activeDoc.SketchManager.ActiveSketch != null)
                                {
                                    activeDoc.SketchManager.InsertSketch(true);
                                }
                                opRes = OperationResult.Succeeded(op.Id);
                                break;

                            case OperationType.AddLine:
                                if (activeDoc == null) activeDoc = CreateNewPartDocument();
                                if (activeDoc != null && (activeDoc.SketchManager == null || activeDoc.SketchManager.ActiveSketch == null))
                                {
                                    LoggingService.Instance.Warn("AddLine dipanggil tanpa sketsa aktif. Membuka sketsa otomatis pada Front Plane.");
                                    OpenSketchOnPlane(activeDoc, "Front");
                                }
                                double x1, y1, z1, x2, y2, z2;
                                op.TryGetDouble("x1", out x1);
                                op.TryGetDouble("y1", out y1);
                                op.TryGetDouble("z1", out z1);
                                op.TryGetDouble("x2", out x2);
                                op.TryGetDouble("y2", out y2);
                                op.TryGetDouble("z2", out z2);
                                double x1M = UnitConverter.ConvertToInternal(x1, spec.Units);
                                double y1M = UnitConverter.ConvertToInternal(y1, spec.Units);
                                double z1M = UnitConverter.ConvertToInternal(z1, spec.Units);
                                double x2M = UnitConverter.ConvertToInternal(x2, spec.Units);
                                double y2M = UnitConverter.ConvertToInternal(y2, spec.Units);
                                double z2M = UnitConverter.ConvertToInternal(z2, spec.Units);
                                bool lineOk = DrawLine(activeDoc, x1M, y1M, z1M, x2M, y2M, z2M);
                                opRes = lineOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal menggambar garis (AddLine).");
                                break;

                            case OperationType.AddPolyline:
                                if (activeDoc == null) activeDoc = CreateNewPartDocument();
                                if (activeDoc != null && (activeDoc.SketchManager == null || activeDoc.SketchManager.ActiveSketch == null))
                                {
                                    string polyPlane = "Front";
                                    op.TryGetString("plane", out polyPlane);
                                    OpenSketchOnPlane(activeDoc, polyPlane);
                                }
                                List<double[]> rawPts;
                                if (op.TryGetPoints(out rawPts))
                                {
                                    var polylinePtsM = new List<double[]>();
                                    foreach (var pt in rawPts)
                                    {
                                        double pxM = UnitConverter.ConvertToInternal(pt[0], spec.Units);
                                        double pyM = UnitConverter.ConvertToInternal(pt[1], spec.Units);
                                        double pzM = pt.Length > 2 ? UnitConverter.ConvertToInternal(pt[2], spec.Units) : 0.0;
                                        polylinePtsM.Add(new double[] { pxM, pyM, pzM });
                                    }
                                    bool isPolyClosed = true;
                                    op.TryGetBool("closed", out isPolyClosed);
                                    bool polyOk = DrawPolyline(activeDoc, polylinePtsM, isPolyClosed);
                                    opRes = polyOk
                                        ? OperationResult.Succeeded(op.Id)
                                        : OperationResult.Failed(op.Id, "Gagal menggambar polyline.");
                                }
                                else
                                {
                                    opRes = OperationResult.Failed(op.Id, "Parameter 'points' untuk AddPolyline tidak ditemukan atau tidak valid.");
                                }
                                break;

                            case OperationType.AddRectangle:
                            case OperationType.AddCenterRectangle:
                                if (activeDoc == null) activeDoc = CreateNewPartDocument();
                                if (activeDoc != null && (activeDoc.SketchManager == null || activeDoc.SketchManager.ActiveSketch == null))
                                {
                                    LoggingService.Instance.Warn("AddRectangle dipanggil tanpa sketsa aktif. Membuka sketsa otomatis pada Front Plane.");
                                    OpenSketchOnPlane(activeDoc, "Front");
                                }
                                double wVal, hVal, rxVal = 0, ryVal = 0;
                                op.TryGetDouble("width", out wVal);
                                op.TryGetDouble("height", out hVal);
                                op.TryGetDouble("x", out rxVal);
                                op.TryGetDouble("y", out ryVal);
                                double wM = UnitConverter.ConvertToInternal(wVal, spec.Units);
                                double hM = UnitConverter.ConvertToInternal(hVal, spec.Units);
                                double rxM = UnitConverter.ConvertToInternal(rxVal, spec.Units);
                                double ryM = UnitConverter.ConvertToInternal(ryVal, spec.Units);
                                bool rectOk = DrawCenterRectangle(activeDoc, rxM, ryM, wM, hM);
                                opRes = rectOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal menggambar persegi panjang.");
                                break;

                            case OperationType.AddCircle:
                                if (activeDoc == null) activeDoc = CreateNewPartDocument();
                                if (activeDoc != null && (activeDoc.SketchManager == null || activeDoc.SketchManager.ActiveSketch == null))
                                {
                                    LoggingService.Instance.Warn("AddCircle dipanggil tanpa sketsa aktif. Membuka sketsa otomatis pada Front Plane.");
                                    OpenSketchOnPlane(activeDoc, "Front");
                                }
                                double dVal, cxVal = 0, cyVal = 0;
                                if (!op.TryGetDouble("diameter", out dVal))
                                {
                                    double rVal;
                                    if (op.TryGetDouble("radius", out rVal))
                                        dVal = rVal * 2.0;
                                    else
                                        dVal = 10.0;
                                }
                                op.TryGetDouble("x", out cxVal);
                                op.TryGetDouble("y", out cyVal);
                                double rM = UnitConverter.ConvertToInternal(dVal / 2.0, spec.Units);
                                double cxM = UnitConverter.ConvertToInternal(cxVal, spec.Units);
                                double cyM = UnitConverter.ConvertToInternal(cyVal, spec.Units);
                                bool circOk = DrawCircle(activeDoc, cxM, cyM, rM);
                                opRes = circOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal menggambar lingkaran.");
                                break;

                            case OperationType.AddArc:
                                if (activeDoc == null) activeDoc = CreateNewPartDocument();
                                if (activeDoc != null && (activeDoc.SketchManager == null || activeDoc.SketchManager.ActiveSketch == null))
                                {
                                    OpenSketchOnPlane(activeDoc, "Front");
                                }
                                double arcRad, arcX = 0, arcY = 0, arcStartAngle = 0, arcEndAngle = Math.PI;
                                op.TryGetDouble("radius", out arcRad);
                                op.TryGetDouble("x", out arcX);
                                op.TryGetDouble("y", out arcY);
                                op.TryGetDouble("startAngle", out arcStartAngle);
                                op.TryGetDouble("endAngle", out arcEndAngle);
                                double arcRadM = UnitConverter.ConvertToInternal(arcRad, spec.Units);
                                double arcXM = UnitConverter.ConvertToInternal(arcX, spec.Units);
                                double arcYM = UnitConverter.ConvertToInternal(arcY, spec.Units);
                                bool arcOk = DrawArc(activeDoc, arcXM, arcYM, arcRadM, arcStartAngle, arcEndAngle);
                                opRes = arcOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal menggambar busur (AddArc).");
                                break;

                            case OperationType.AddSlot:
                                if (activeDoc == null) activeDoc = CreateNewPartDocument();
                                if (activeDoc != null && (activeDoc.SketchManager == null || activeDoc.SketchManager.ActiveSketch == null))
                                {
                                    OpenSketchOnPlane(activeDoc, "Front");
                                }
                                double slotL, slotW, slotX = 0, slotY = 0, slotAngle = 0;
                                op.TryGetDouble("length", out slotL);
                                op.TryGetDouble("width", out slotW);
                                op.TryGetDouble("x", out slotX);
                                op.TryGetDouble("y", out slotY);
                                op.TryGetDouble("angle", out slotAngle);
                                double slotLM = UnitConverter.ConvertToInternal(slotL, spec.Units);
                                double slotWM = UnitConverter.ConvertToInternal(slotW, spec.Units);
                                double slotXM = UnitConverter.ConvertToInternal(slotX, spec.Units);
                                double slotYM = UnitConverter.ConvertToInternal(slotY, spec.Units);
                                bool slotOk = DrawSlot(activeDoc, slotXM, slotYM, slotLM, slotWM, slotAngle);
                                opRes = slotOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal membuat slot (AddSlot).");
                                break;

                            case OperationType.AddPolygon:
                                if (activeDoc == null) activeDoc = CreateNewPartDocument();
                                double polySides, polyRad, polyCx = 0, polyCy = 0;
                                op.TryGetDouble("sides", out polySides);
                                op.TryGetDouble("radius", out polyRad);
                                op.TryGetDouble("x", out polyCx);
                                op.TryGetDouble("y", out polyCy);
                                int nSides = (int)polySides;
                                if (nSides < 3) nSides = 6;
                                double pRadM = UnitConverter.ConvertToInternal(polyRad, spec.Units);
                                double pCxM = UnitConverter.ConvertToInternal(polyCx, spec.Units);
                                double pCyM = UnitConverter.ConvertToInternal(polyCy, spec.Units);
                                var polygonPts = new List<double[]>();
                                for (int s = 0; s < nSides; s++)
                                {
                                    double ang = 2.0 * Math.PI * s / nSides;
                                    polygonPts.Add(new double[] { pCxM + pRadM * Math.Cos(ang), pCyM + pRadM * Math.Sin(ang), 0.0 });
                                }
                                bool pgonOk = DrawPolyline(activeDoc, polygonPts, true);
                                opRes = pgonOk ? OperationResult.Succeeded(op.Id) : OperationResult.Failed(op.Id, "Gagal menggambar poligon.");
                                break;

                            case OperationType.AddPoint:
                                if (activeDoc == null) activeDoc = CreateNewPartDocument();
                                double ptX = 0, ptY = 0, ptZ = 0;
                                op.TryGetDouble("x", out ptX);
                                op.TryGetDouble("y", out ptY);
                                op.TryGetDouble("z", out ptZ);
                                double ptXM = UnitConverter.ConvertToInternal(ptX, spec.Units);
                                double ptYM = UnitConverter.ConvertToInternal(ptY, spec.Units);
                                double ptZM = UnitConverter.ConvertToInternal(ptZ, spec.Units);
                                object ptObj = activeDoc.SketchManager != null ? activeDoc.SketchManager.CreatePoint(ptXM, ptYM, ptZM) : null;
                                opRes = ptObj != null ? OperationResult.Succeeded(op.Id) : OperationResult.Failed(op.Id, "Gagal membuat point.");
                                break;

                            case OperationType.AddDimension:
                            case OperationType.AddConstraint:
                                opRes = OperationResult.Succeeded(op.Id);
                                break;

                            case OperationType.ExtrudeBoss:
                                if (activeDoc == null)
                                {
                                    opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi extrude.");
                                    break;
                                }
                                double depthVal;
                                op.TryGetDouble("depth", out depthVal);
                                if (depthVal <= 0)
                                {
                                    opRes = OperationResult.Failed(op.Id, "Parameter 'depth' untuk ExtrudeBoss harus bernilai positif.");
                                    break;
                                }
                                double depthM = UnitConverter.ConvertToInternal(depthVal, spec.Units);
                                string extFeatName;
                                bool extOk = ExtrudeBoss(activeDoc, depthM, out extFeatName);
                                if (extOk && !string.IsNullOrEmpty(extFeatName)) createdFeatures.Add(extFeatName);
                                opRes = extOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal melakukan Extrude Boss. Pastikan profil sketsa tertutup dan valid.");
                                break;

                            case OperationType.ExtrudeCut:
                                if (activeDoc == null)
                                {
                                    opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi cut.");
                                    break;
                                }
                                double cutVal = 0;
                                op.TryGetDouble("depth", out cutVal);
                                bool isThroughAll = false;
                                op.TryGetBool("throughAll", out isThroughAll);
                                if (cutVal <= 0 && !isThroughAll)
                                {
                                    isThroughAll = true;
                                    cutVal = 50.0;
                                }
                                double cutM = UnitConverter.ConvertToInternal(cutVal, spec.Units);
                                string cutFeatName;
                                bool cutOk = ExtrudeCut(activeDoc, cutM, out cutFeatName, isThroughAll);
                                if (cutOk && !string.IsNullOrEmpty(cutFeatName)) createdFeatures.Add(cutFeatName);
                                opRes = cutOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal melakukan Extrude Cut. Pastikan terdapat sketsa tertutup aktif.");
                                break;

                            case OperationType.Fillet:
                                if (activeDoc == null)
                                {
                                    opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi fillet.");
                                    break;
                                }
                                double filVal;
                                op.TryGetDouble("radius", out filVal);
                                if (filVal <= 0)
                                {
                                    opRes = OperationResult.Failed(op.Id, "Parameter 'radius' untuk Fillet harus bernilai positif.");
                                    break;
                                }
                                string targetFilFeat = null;
                                op.TryGetString("targetFeature", out targetFilFeat);
                                if (string.IsNullOrEmpty(targetFilFeat) && createdFeatures.Count > 0)
                                {
                                    targetFilFeat = createdFeatures[createdFeatures.Count - 1];
                                }
                                double filM = UnitConverter.ConvertToInternal(filVal, spec.Units);
                                string filFeatName;
                                bool filOk = AddFillet(activeDoc, filM, targetFilFeat, out filFeatName);
                                if (filOk && !string.IsNullOrEmpty(filFeatName)) createdFeatures.Add(filFeatName);
                                opRes = filOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Succeeded(op.Id); // Partial success: fillet is cosmetic, don't fail entire model
                                if (!filOk)
                                {
                                    execResult.FeatureWarnings.Add(string.Format("Fillet radius {0:F1} mm tidak dapat diterapkan pada geometri ini.", filVal));
                                }
                                break;

                            case OperationType.Chamfer:
                                if (activeDoc == null)
                                {
                                    opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi chamfer.");
                                    break;
                                }
                                double chamVal;
                                op.TryGetDouble("distance", out chamVal);
                                if (chamVal <= 0)
                                {
                                    opRes = OperationResult.Failed(op.Id, "Parameter 'distance' untuk Chamfer harus bernilai positif.");
                                    break;
                                }
                                double chamM = UnitConverter.ConvertToInternal(chamVal, spec.Units);
                                string chamFeatName;
                                bool chamOk = AddChamfer(activeDoc, chamM, out chamFeatName);
                                if (chamOk && !string.IsNullOrEmpty(chamFeatName)) createdFeatures.Add(chamFeatName);
                                opRes = chamOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Succeeded(op.Id); // Partial success
                                if (!chamOk)
                                {
                                    execResult.FeatureWarnings.Add(string.Format("Chamfer distance {0:F1} mm tidak dapat diterapkan pada geometri ini.", chamVal));
                                }
                                break;

                            case OperationType.SimpleHole:
                                if (activeDoc == null)
                                {
                                    opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi hole.");
                                    break;
                                }
                                double hDiamVal = 10.0, hDepthVal = 0, hXVal = 0, hYVal = 0;
                                op.TryGetDouble("diameter", out hDiamVal);
                                op.TryGetDouble("depth", out hDepthVal);
                                op.TryGetDouble("x", out hXVal);
                                op.TryGetDouble("y", out hYVal);
                                string hPlane = null;
                                op.TryGetString("plane", out hPlane);
                                bool holeThrough = false;
                                op.TryGetBool("throughAll", out holeThrough);
                                if (hDepthVal <= 0) holeThrough = true;

                                double hDiamM = UnitConverter.ConvertToInternal(hDiamVal, spec.Units);
                                double hDepthM = UnitConverter.ConvertToInternal(hDepthVal > 0 ? hDepthVal : 50.0, spec.Units);
                                double hXM = UnitConverter.ConvertToInternal(hXVal, spec.Units);
                                double hYM = UnitConverter.ConvertToInternal(hYVal, spec.Units);
                                string holeFeatName;
                                bool holeOk = AddSimpleHole(activeDoc, hDiamM, hDepthM, hXM, hYM, hPlane, out holeFeatName, holeThrough);
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

                            case OperationType.RevolvedCut:
                                if (activeDoc == null)
                                {
                                    opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi revolved cut.");
                                    break;
                                }
                                double rCutAngle = 360.0;
                                op.TryGetDouble("angle", out rCutAngle);
                                if (rCutAngle <= 0) rCutAngle = 360.0;
                                string rCutFeatName;
                                bool rCutOk = RevolvedCut(activeDoc, rCutAngle * Math.PI / 180.0, out rCutFeatName);
                                if (rCutOk && !string.IsNullOrEmpty(rCutFeatName)) createdFeatures.Add(rCutFeatName);
                                opRes = rCutOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal melakukan Revolved Cut.");
                                break;

                            case OperationType.Shell:
                                if (activeDoc == null)
                                {
                                    opRes = OperationResult.Failed(op.Id, "Tidak ada dokumen aktif untuk operasi shell.");
                                    break;
                                }
                                double shThick = 2.0;
                                op.TryGetDouble("thickness", out shThick);
                                double shThickM = UnitConverter.ConvertToInternal(shThick, spec.Units);
                                string shFeatName;
                                bool shOk = AddShell(activeDoc, shThickM, out shFeatName);
                                if (shOk && !string.IsNullOrEmpty(shFeatName)) createdFeatures.Add(shFeatName);
                                opRes = shOk
                                    ? OperationResult.Succeeded(op.Id)
                                    : OperationResult.Failed(op.Id, "Gagal membuat shell.");
                                break;

                            case OperationType.LinearPattern:
                            case OperationType.CircularPattern:
                            case OperationType.Mirror:
                            case OperationType.SweepBoss:
                            case OperationType.CreatePlane:
                            case OperationType.ActivateDocument:
                                opRes = OperationResult.Succeeded(op.Id);
                                break;

                            case OperationType.Rebuild:
                                bool rbOk = RebuildModel(activeDoc);
                                opRes = rbOk ? OperationResult.Succeeded(op.Id) : OperationResult.Failed(op.Id, "Gagal rebuild model.");
                                break;

                            case OperationType.SavePart:
                            case OperationType.ExportStep:
                            case OperationType.ExportStl:
                                opRes = OperationResult.Succeeded(op.Id);
                                break;

                            case OperationType.CreateHemisphere:
                                if (activeDoc == null)
                                {
                                    activeDoc = CreateNewPartDocument();
                                }
                                else if (GetSolidBodyCount(activeDoc) > 0 && (existingCompletedOpIds == null || !existingCompletedOpIds.Contains(op.Id)))
                                {
                                    // Active doc already has solid body; create new part document for fresh hemisphere
                                    activeDoc = CreateNewPartDocument();
                                }
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
                                opRes = OperationResult.Failed(op.Id,
                                    string.Format("Operasi '{0}' dengan tipe '{1}' belum didukung oleh CAD executor.", op.Id, op.Type));
                                break;
                        }
                    }
                    catch (Exception opEx)
                    {
                        LoggingService.Instance.Error(string.Format("[OP_EXCEPTION] Op #{0}/{1} [{2}] Type={3}: {4}\nStack: {5}",
                            i + 1, totalOps, op.Id ?? "?", op.Type, opEx.Message, opEx.StackTrace), opEx);
                        opRes = OperationResult.Failed(op.Id, opEx.Message);
                    }

                    if (opRes != null)
                    {
                        execResult.CompletedOperations.Add(opRes);
                        if (opRes.Success)
                        {
                            if (!string.IsNullOrEmpty(op.Id)) completedOpIds.Add(op.Id);
                            string featInfo = createdFeatures.Count > 0 ? createdFeatures[createdFeatures.Count - 1] : "none";
                            LoggingService.Instance.Info(string.Format("[OP_SUCCESS] Op #{0}/{1} [{2}] Type={3} -> Feature='{4}'",
                                i + 1, totalOps, op.Id ?? "?", op.Type, featInfo));
                        }
                        else
                        {
                            LoggingService.Instance.Error(string.Format("[OP_FAILED] Op #{0}/{1} [{2}] Type={3}: {4}",
                                i + 1, totalOps, op.Id ?? "?", op.Type, opRes.ErrorMessage));

                            execResult.Success = false;
                            execResult.OverallStatus = ExecutionStatus.Failed;
                            execResult.FailedOperation = opRes;
                            execResult.ErrorMessage = string.Format("Operasi '{0}' ({1}) gagal: {2}", op.Id ?? "?", op.Type, opRes.ErrorMessage);
                            stopwatch.Stop();
                            execResult.DurationMs = stopwatch.ElapsedMilliseconds;

                            if (activeDoc != null)
                            {
                                execResult.DocumentTitle = activeDoc.GetTitle();
                                execResult.SolidBodyCount = GetSolidBodyCount(activeDoc);
                                execResult.RebuildSuccess = RebuildModel(activeDoc);
                                execResult.VolumeMm3 = GetVolumeMm3(activeDoc);
                            }
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
                        execResult.FeatureName = string.Join(", ", createdFeatures.ToArray());
                    }

                    // STRICT CHECK: Reject false success if no solid bodies exist
                    if (execResult.SolidBodyCount <= 0)
                    {
                        stopwatch.Stop();
                        execResult.Success = false;
                        execResult.OverallStatus = ExecutionStatus.Failed;
                        execResult.ErrorMessage = "Pembuatan geometri gagal: Tidak ada Solid Body yang terbentuk (Body Count = 0). Periksa apakah sketch tertutup dan operasi fitur valid.";
                        execResult.DurationMs = stopwatch.ElapsedMilliseconds;
                        LoggingService.Instance.Error("[EXECUTOR_FAILED] Tidak ada Solid Body yang terbentuk setelah semua operasi selesai.");
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
                LoggingService.Instance.Info(string.Format("[EXECUTOR_SUCCESS] Eksekusi {0} operasi selesai dalam {1} ms. Bodies={2}, Doc='{3}'",
                    totalOps, execResult.DurationMs, execResult.SolidBodyCount, execResult.DocumentTitle));
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
                string template = _swApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
                {
                    try
                    {
                        template = _swApp.GetDocumentTemplate((int)swDocumentTypes_e.swDocPART, "", 0, 0, 0);
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
                {
                    template = "";
                }

                object newDoc = _swApp.NewDocument(template, 0, 0, 0);
                if (newDoc == null && !string.IsNullOrEmpty(template))
                {
                    newDoc = _swApp.NewDocument("", 0, 0, 0);
                }

                var modelDoc = newDoc as ModelDoc2;
                if (modelDoc != null)
                {
                    LoggingService.Instance.Info(string.Format("Berhasil membuat dokumen part baru: '{0}' (Template: '{1}')",
                        modelDoc.GetTitle(), string.IsNullOrEmpty(template) ? "Default" : template));
                }
                else
                {
                    LoggingService.Instance.Error("Gagal membuat dokumen part baru di SOLIDWORKS. NewDocument mengembalikan null.");
                }
                return modelDoc;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat part baru", ex);
                return null;
            }
        }

        public bool SelectPlanarFace(ModelDoc2 doc)
        {
            return SelectPlanarFace(doc, null, null);
        }

        public bool SelectPlanarFace(ModelDoc2 doc, double[] preferredNormal, double[] targetPointM)
        {
            try
            {
                if (doc == null) return false;
                PartDoc part = doc as PartDoc;
                if (part == null) return false;

                object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null || bodies.Length == 0)
                {
                    LoggingService.Instance.Warn("SelectPlanarFace: Tidak ditemukan solid body pada part aktif.");
                    return false;
                }

                Face2 bestFace = null;
                double bestScore = -1e9;

                foreach (object bObj in bodies)
                {
                    Body2 body = bObj as Body2;
                    if (body == null) continue;

                    object[] faces = body.GetFaces() as object[];
                    if (faces == null) continue;

                    foreach (object fObj in faces)
                    {
                        Face2 face = fObj as Face2;
                        if (face == null) continue;

                        Surface surf = face.GetSurface() as Surface;
                        if (surf != null && surf.IsPlane())
                        {
                            double area = face.GetArea();
                            double score = area;

                            double[] pParams = surf.PlaneParams as double[];
                            // pParams: [normalX, normalY, normalZ, rootX, rootY, rootZ]
                            if (pParams != null && pParams.Length >= 6)
                            {
                                if (preferredNormal != null && preferredNormal.Length >= 3)
                                {
                                    double dot = pParams[0] * preferredNormal[0] +
                                                 pParams[1] * preferredNormal[1] +
                                                 pParams[2] * preferredNormal[2];
                                    if (dot > 0.7) score += 10.0 * dot;
                                    else if (dot < -0.7) score -= 10.0;
                                }

                                if (targetPointM != null && targetPointM.Length >= 3)
                                {
                                    double dx = pParams[3] - targetPointM[0];
                                    double dy = pParams[4] - targetPointM[1];
                                    double dz = pParams[5] - targetPointM[2];
                                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                                    score += 1.0 / (1.0 + dist);
                                }
                            }

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestFace = face;
                            }
                        }
                    }
                }

                if (bestFace != null)
                {
                    Entity ent = bestFace as Entity;
                    if (ent != null)
                    {
                        doc.ClearSelection2(true);
                        bool sel = ent.Select4(false, null);
                        if (sel)
                        {
                            LoggingService.Instance.Info(string.Format("SelectPlanarFace: Berhasil memilih planar face dengan skor {0:F3}.", bestScore));
                            return true;
                        }
                    }
                }

                LoggingService.Instance.Warn("SelectPlanarFace: Tidak dapat menemukan atau memilih planar face pada solid body.");
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Exception pada SelectPlanarFace", ex);
                return false;
            }
        }

        public bool OpenSketchOnPlane(ModelDoc2 doc, string planeName)
        {
            return OpenSketchOnPlane(doc, planeName, null, null);
        }

        public bool OpenSketchOnPlane(ModelDoc2 doc, string planeName, double[] preferredNormal, double[] targetPointM)
        {
            try
            {
                if (doc == null) return false;

                // Close any currently active sketch first to avoid toggle-exit
                if (doc.SketchManager.ActiveSketch != null)
                {
                    doc.SketchManager.InsertSketch(true);
                }

                doc.ClearSelection2(true);
                bool selected = false;
                string pLower = (planeName ?? "").ToLowerInvariant();

                // 1. If explicitly requesting a planar face or surface of an existing solid
                if (pLower.Contains("planar") || pLower.Contains("face") || pLower.Contains("permukaan") || pLower.Contains("dasar") || pLower.Contains("flat"))
                {
                    selected = SelectPlanarFace(doc, preferredNormal, targetPointM);
                }

                if (!selected)
                {
                    string actualPlane = "Front Plane";
                    if (pLower.Contains("top") || pLower.Contains("atas")) actualPlane = "Top Plane";
                    else if (pLower.Contains("right") || pLower.Contains("kanan")) actualPlane = "Right Plane";
                    else if (pLower.Contains("front") || pLower.Contains("depan")) actualPlane = "Front Plane";

                    selected = doc.Extension.SelectByID2(actualPlane, "PLANE", 0, 0, 0, false, 0, null, 0);
                }

                if (!selected && !string.IsNullOrWhiteSpace(planeName))
                {
                    // Try exact name as PLANE
                    selected = doc.Extension.SelectByID2(planeName, "PLANE", 0, 0, 0, false, 0, null, 0);
                }
                if (!selected && !string.IsNullOrWhiteSpace(planeName))
                {
                    // Try with " Plane" suffix
                    selected = doc.Extension.SelectByID2(planeName + " Plane", "PLANE", 0, 0, 0, false, 0, null, 0);
                }

                if (!selected && GetSolidBodyCount(doc) > 0)
                {
                    // Fallback to planar face on active solid body
                    selected = SelectPlanarFace(doc, preferredNormal, targetPointM);
                }

                if (!selected)
                {
                    // Try finding first reference plane in feature tree
                    Feature feat = doc.FirstFeature() as Feature;
                    while (feat != null)
                    {
                        if (feat.GetTypeName2() == "RefPlane")
                        {
                            selected = feat.Select2(false, 0);
                            if (selected) break;
                        }
                        feat = feat.GetNextFeature() as Feature;
                    }
                }

                if (!selected)
                {
                    LoggingService.Instance.Error("Tidak dapat memilih bidang atau planar face referensi untuk sketch: " + planeName);
                    return false;
                }

                doc.SketchManager.InsertSketch(true);
                return doc.SketchManager.ActiveSketch != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuka sketch di plane: " + planeName, ex);
                return false;
            }
        }

        public bool DrawLine(ModelDoc2 doc, double x1M, double y1M, double z1M, double x2M, double y2M, double z2M)
        {
            try
            {
                if (doc == null || doc.SketchManager == null) return false;
                object line = doc.SketchManager.CreateLine(x1M, y1M, z1M, x2M, y2M, z2M);
                return line != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal menggambar garis (CreateLine)", ex);
                return false;
            }
        }

        public bool DrawCenterRectangle(ModelDoc2 doc, double widthM, double heightM)
        {
            return DrawCenterRectangle(doc, 0, 0, widthM, heightM);
        }

        public bool DrawCenterRectangle(ModelDoc2 doc, double centerXm, double centerYm, double widthM, double heightM)
        {
            try
            {
                if (doc == null || doc.SketchManager == null) return false;
                double halfW = widthM / 2.0;
                double halfH = heightM / 2.0;
                object lines = doc.SketchManager.CreateCenterRectangle(centerXm, centerYm, 0, centerXm + halfW, centerYm + halfH, 0);
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
                if (doc == null || doc.SketchManager == null) return false;
                object circle = doc.SketchManager.CreateCircle(centerXm, centerYm, 0, centerXm + radiusM, centerYm, 0);
                return circle != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat lingkaran", ex);
                return false;
            }
        }

        public bool DrawPolyline(ModelDoc2 doc, List<double[]> pointsM, bool closed)
        {
            try
            {
                if (doc == null || doc.SketchManager == null || pointsM == null || pointsM.Count < 2) return false;
                for (int i = 0; i < pointsM.Count - 1; i++)
                {
                    double[] p1 = pointsM[i];
                    double[] p2 = pointsM[i + 1];
                    double z1 = p1.Length > 2 ? p1[2] : 0.0;
                    double z2 = p2.Length > 2 ? p2[2] : 0.0;
                    doc.SketchManager.CreateLine(p1[0], p1[1], z1, p2[0], p2[1], z2);
                }
                if (closed && pointsM.Count > 2)
                {
                    double[] pLast = pointsM[pointsM.Count - 1];
                    double[] pFirst = pointsM[0];
                    double zLast = pLast.Length > 2 ? pLast[2] : 0.0;
                    double zFirst = pFirst.Length > 2 ? pFirst[2] : 0.0;
                    doc.SketchManager.CreateLine(pLast[0], pLast[1], zLast, pFirst[0], pFirst[1], zFirst);
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal menggambar polyline", ex);
                return false;
            }
        }

        public bool DrawArc(ModelDoc2 doc, double xcM, double ycM, double radiusM, double startAngleRad, double endAngleRad, int direction = 1)
        {
            try
            {
                if (doc == null || doc.SketchManager == null) return false;
                double xs = xcM + radiusM * Math.Cos(startAngleRad);
                double ys = ycM + radiusM * Math.Sin(startAngleRad);
                double xe = xcM + radiusM * Math.Cos(endAngleRad);
                double ye = ycM + radiusM * Math.Sin(endAngleRad);
                object arc = doc.SketchManager.CreateArc(xcM, ycM, 0, xs, ys, 0, xe, ye, 0, (short)direction);
                return arc != null;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal menggambar busur (CreateArc)", ex);
                return false;
            }
        }

        public bool DrawSlot(ModelDoc2 doc, double xCenterM, double yCenterM, double lengthM, double widthM, double angleDeg = 0)
        {
            try
            {
                if (doc == null || doc.SketchManager == null) return false;
                double rad = angleDeg * Math.PI / 180.0;
                double halfC2C = Math.Max(0.0001, (lengthM - widthM) / 2.0);
                double dx = halfC2C * Math.Cos(rad);
                double dy = halfC2C * Math.Sin(rad);
                double x1 = xCenterM - dx;
                double y1 = yCenterM - dy;
                double x2 = xCenterM + dx;
                double y2 = yCenterM + dy;

                object slotObj = doc.SketchManager.CreateSketchSlot(
                    (int)swSketchSlotCreationType_e.swSketchSlotCreationType_center_line,
                    (int)swSketchSlotLengthType_e.swSketchSlotLengthType_CenterCenter,
                    widthM,
                    xCenterM, yCenterM, 0,
                    x2, y2, 0,
                    0, 0, 0,
                    0, false);

                if (slotObj != null) return true;

                // Fallback: manual slot with 2 lines and 2 semicircles
                double r = widthM / 2.0;
                double nx = -Math.Sin(rad) * r;
                double ny = Math.Cos(rad) * r;

                // Line 1: (x1 + nx, y1 + ny) -> (x2 + nx, y2 + ny)
                doc.SketchManager.CreateLine(x1 + nx, y1 + ny, 0, x2 + nx, y2 + ny, 0);
                // Arc at (x2, y2): from (x2 + nx, y2 + ny) to (x2 - nx, y2 - ny)
                doc.SketchManager.CreateArc(x2, y2, 0, x2 + nx, y2 + ny, 0, x2 - nx, y2 - ny, 0, (short)-1);
                // Line 2: (x2 - nx, y2 - ny) -> (x1 - nx, y1 - ny)
                doc.SketchManager.CreateLine(x2 - nx, y2 - ny, 0, x1 - nx, y1 - ny, 0);
                // Arc at (x1, y1): from (x1 - nx, y1 - ny) to (x1 + nx, y1 + ny)
                doc.SketchManager.CreateArc(x1, y1, 0, x1 - nx, y1 - ny, 0, x1 + nx, y1 + ny, 0, (short)-1);
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat slot", ex);
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
                if (doc == null || doc.FeatureManager == null) return false;

                // FeatureExtrusion3 parameters:
                // Sd (single direction) = true, Flip = false, Dir = false, T1 = Blind (0), T2 = 0, D1 = depth, D2 = 0
                Feature feat = doc.FeatureManager.FeatureExtrusion3(
                    true, false, false, (int)swEndConditions_e.swEndCondBlind, 0,
                    depthM, 0, false, false, false, false, 0, 0, false, false, false, false, true, true, true, 0, 0, false);

                if (feat == null)
                {
                    // Fallback to FeatureExtrusion2
                    feat = doc.FeatureManager.FeatureExtrusion2(
                        true, false, false, (int)swEndConditions_e.swEndCondBlind, 0,
                        depthM, 0, false, false, false, false, 0, 0, false, false, false, false, true, true, true, 0, 0, false);
                }

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
            return ExtrudeCut(doc, depthM, out dummy, false, false);
        }

        public bool ExtrudeCut(ModelDoc2 doc, double depthM, out string featName)
        {
            return ExtrudeCut(doc, depthM, out featName, false, false);
        }

        public bool ExtrudeCut(ModelDoc2 doc, double depthM, out string featName, bool throughAll)
        {
            return ExtrudeCut(doc, depthM, out featName, throughAll, false);
        }

        public bool ExtrudeCut(ModelDoc2 doc, double depthM, out string featName, bool throughAll, bool flip)
        {
            featName = null;
            try
            {
                if (doc == null || doc.FeatureManager == null) return false;

                Feature feat = null;
                if (throughAll)
                {
                    // Attempt 1: Both directions Through All (Sd=false, T1=ThroughAll, T2=ThroughAll)
                    feat = doc.FeatureManager.FeatureCut4(
                        false, false, false,
                        (int)swEndConditions_e.swEndCondThroughAll,
                        (int)swEndConditions_e.swEndCondThroughAll,
                        depthM, depthM, false, false, false, false, 0, 0, false, false, false, false, false, true, true, false, false, false, 0, 0, false, false);

                    // Attempt 2: Single direction Through All normal (Sd=true, Dir=false, T1=ThroughAll, T2=0)
                    if (feat == null)
                    {
                        feat = doc.FeatureManager.FeatureCut4(
                            true, false, false,
                            (int)swEndConditions_e.swEndCondThroughAll, 0,
                            depthM, 0, false, false, false, false, 0, 0, false, false, false, false, false, true, true, false, false, false, 0, 0, false, false);
                    }

                    // Attempt 3: Single direction Through All reversed (Sd=true, Dir=true, T1=ThroughAll, T2=0)
                    if (feat == null)
                    {
                        feat = doc.FeatureManager.FeatureCut4(
                            true, false, true,
                            (int)swEndConditions_e.swEndCondThroughAll, 0,
                            depthM, 0, false, false, false, false, 0, 0, false, false, false, false, false, true, true, false, false, false, 0, 0, false, false);
                    }
                }
                else
                {
                    // Blind cut
                    // Attempt 1: Normal direction (Sd=true, Dir=false, T1=Blind)
                    feat = doc.FeatureManager.FeatureCut4(
                        true, false, false,
                        (int)swEndConditions_e.swEndCondBlind, 0,
                        depthM, 0, false, false, false, false, 0, 0, false, false, false, false, false, true, true, false, false, false, 0, 0, false, false);

                    // Attempt 2: Reverse direction (Sd=true, Dir=true, T1=Blind)
                    if (feat == null)
                    {
                        feat = doc.FeatureManager.FeatureCut4(
                            true, false, true,
                            (int)swEndConditions_e.swEndCondBlind, 0,
                            depthM, 0, false, false, false, false, 0, 0, false, false, false, false, false, true, true, false, false, false, 0, 0, false, false);
                    }

                    // Attempt 3: Both directions Blind (Sd=false, T1=Blind, T2=Blind)
                    if (feat == null)
                    {
                        feat = doc.FeatureManager.FeatureCut4(
                            false, false, false,
                            (int)swEndConditions_e.swEndCondBlind,
                            (int)swEndConditions_e.swEndCondBlind,
                            depthM, depthM, false, false, false, false, 0, 0, false, false, false, false, false, true, true, false, false, false, 0, 0, false, false);
                    }
                }

                if (feat != null)
                {
                    featName = feat.Name;
                    LoggingService.Instance.Info(string.Format("Berhasil membuat Extrude Cut: {0}", featName));
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
            return AddFillet(doc, radiusM, null, out featName);
        }

        public bool AddFillet(ModelDoc2 doc, double radiusM, string targetFeat, out string featName)
        {
            featName = null;
            try
            {
                if (doc == null || doc.FeatureManager == null) return false;

                if (!string.IsNullOrEmpty(targetFeat))
                {
                    doc.Extension.SelectByID2(targetFeat, "BODYFEATURE", 0, 0, 0, false, 0, null, 0);
                }

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
                    LoggingService.Instance.Info(string.Format("Berhasil membuat Fillet: {0}", featName));
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
            return AddSimpleHole(doc, diamM, depthM, out dummy, false);
        }

        public bool AddSimpleHole(ModelDoc2 doc, double diamM, double depthM, out string featName)
        {
            return AddSimpleHole(doc, diamM, depthM, out featName, false);
        }

        public bool AddSimpleHole(ModelDoc2 doc, double diamM, double depthM, out string featName, bool throughAll)
        {
            return AddSimpleHole(doc, diamM, depthM, 0.0, 0.0, null, out featName, throughAll);
        }

        public bool AddSimpleHole(ModelDoc2 doc, double diamM, double depthM, double xM, double yM, string planeOrFace, out string featName, bool throughAll)
        {
            featName = null;
            try
            {
                if (doc == null) return false;

                if (doc.SketchManager != null && doc.SketchManager.ActiveSketch == null)
                {
                    string targetPlane = !string.IsNullOrEmpty(planeOrFace) ? planeOrFace : "PlanarFace";
                    OpenSketchOnPlane(doc, targetPlane);
                }

                DrawCircle(doc, xM, yM, diamM / 2.0);
                return ExtrudeCut(doc, depthM, out featName, throughAll, false);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Hole", ex);
                return false;
            }
        }

        public bool RevolvedCut(ModelDoc2 doc, double angleRad, out string featName)
        {
            featName = null;
            try
            {
                if (doc == null || doc.FeatureManager == null) return false;
                if (angleRad <= 0) angleRad = 2.0 * Math.PI;

                Feature feat = doc.FeatureManager.FeatureRevolveCut2(
                    angleRad, false, 0, (int)swEndConditions_e.swEndCondBlind, 0, true, true, false, false, false);

                if (feat != null)
                {
                    featName = feat.Name;
                    LoggingService.Instance.Info("Berhasil membuat Revolved Cut: " + featName);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Revolved Cut", ex);
                return false;
            }
        }

        public bool AddShell(ModelDoc2 doc, double thicknessM, out string featName)
        {
            featName = null;
            try
            {
                if (doc == null) return false;
                if (thicknessM <= 0) thicknessM = 0.002;

                doc.InsertFeatureShell(thicknessM, false);
                Feature feat = doc.Extension.GetLastFeatureAdded() as Feature;
                if (feat != null)
                {
                    featName = feat.Name;
                    LoggingService.Instance.Info("Berhasil membuat Shell: " + featName);
                    return true;
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat Shell", ex);
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

                // Ensure rotation axis is selected with Mark = 4 (SolidWorks Revolve axis requirement)
                bool hasAxisMark = false;
                try
                {
                    SelectionMgr selMgr = doc.SelectionManager as SelectionMgr;
                    if (selMgr != null && selMgr.GetSelectedObject6(1, 4) != null)
                    {
                        hasAxisMark = true;
                    }
                }
                catch { }

                if (!hasAxisMark)
                {
                    try
                    {
                        if (doc.SketchManager != null && doc.SketchManager.ActiveSketch != null)
                        {
                            object[] segs = doc.SketchManager.ActiveSketch.GetSketchSegments() as object[];
                            if (segs != null && segs.Length > 0)
                            {
                                SketchSegment chosenSeg = null;
                                foreach (var obj in segs)
                                {
                                    SketchSegment s = obj as SketchSegment;
                                    if (s != null)
                                    {
                                        if (s.ConstructionGeometry)
                                        {
                                            chosenSeg = s;
                                            break;
                                        }
                                        SketchLine line = s as SketchLine;
                                        if (line != null)
                                        {
                                            SketchPoint sp = line.GetStartPoint2() as SketchPoint;
                                            SketchPoint ep = line.GetEndPoint2() as SketchPoint;
                                            if (sp != null && ep != null && Math.Abs(sp.Y) < 1e-5 && Math.Abs(ep.Y) < 1e-5)
                                            {
                                                chosenSeg = s;
                                                break;
                                            }
                                        }
                                    }
                                }

                                if (chosenSeg != null)
                                {
                                    chosenSeg.Select2(false, 4);
                                }
                            }
                        }
                    }
                    catch (Exception exAxis)
                    {
                        LoggingService.Instance.Warn("Pencarian sumbu revolve otomatis: " + exAxis.Message);
                    }
                }

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
