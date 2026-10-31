using System;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Models.Contracts;

namespace AICADAssistant.Core.Features
{
    public class FilletChamferBuilder
    {
        public static CadOperation CreateFilletOperation(string id, double radiusMm, string targetFeature = null, double[] targetPoint = null)
        {
            var op = new CadOperation
            {
                Id = id ?? ("fillet_" + Guid.NewGuid().ToString("N").Substring(0, 8)),
                Type = OperationType.Fillet,
                Description = string.Format("Terapkan fillet radius {0:G} mm", radiusMm)
            };

            op.Parameters["radius"] = radiusMm;
            if (!string.IsNullOrEmpty(targetFeature))
            {
                op.Parameters["targetFeature"] = targetFeature;
                op.DependsOn.Add(targetFeature);
            }

            if (targetPoint != null && targetPoint.Length >= 3)
            {
                op.Parameters["x"] = targetPoint[0];
                op.Parameters["y"] = targetPoint[1];
                op.Parameters["z"] = targetPoint[2];
            }

            return op;
        }

        public static CadOperation CreateChamferOperation(string id, double distanceMm, string targetFeature = null, double[] targetPoint = null)
        {
            var op = new CadOperation
            {
                Id = id ?? ("chamfer_" + Guid.NewGuid().ToString("N").Substring(0, 8)),
                Type = OperationType.Chamfer,
                Description = string.Format("Terapkan chamfer {0:G} mm", distanceMm)
            };

            op.Parameters["distance"] = distanceMm;
            if (!string.IsNullOrEmpty(targetFeature))
            {
                op.Parameters["targetFeature"] = targetFeature;
                op.DependsOn.Add(targetFeature);
            }

            if (targetPoint != null && targetPoint.Length >= 3)
            {
                op.Parameters["x"] = targetPoint[0];
                op.Parameters["y"] = targetPoint[1];
                op.Parameters["z"] = targetPoint[2];
            }

            return op;
        }

        public static bool ValidateFilletRadius(double radiusMm, double[] boundingBoxMm, out string error)
        {
            error = null;
            if (radiusMm <= 0.0)
            {
                error = "Radius fillet harus bernilai angka positif lebih besar dari nol";
                return false;
            }

            if (boundingBoxMm != null && boundingBoxMm.Length >= 3)
            {
                double minDim = Math.Min(boundingBoxMm[0], Math.Min(boundingBoxMm[1], boundingBoxMm[2]));
                if (minDim > 0 && radiusMm >= minDim * 0.5)
                {
                    error = string.Format("Radius fillet ({0:G} mm) terlalu besar untuk ketebalan minimum model ({1:G} mm). Maksimum radius yang dianjurkan: {2:F2} mm",
                        radiusMm, minDim, minDim * 0.49);
                    return false;
                }
            }

            return true;
        }
    }
}

