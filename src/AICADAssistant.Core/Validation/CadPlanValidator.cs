using System;
using System.Collections.Generic;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Core.Validation
{
    /// <summary>
    /// Validates a CadSpec before execution against SOLIDWORKS API.
    /// Ensures geometric safety, allowed operations, positive parameters, and dependency integrity.
    /// </summary>
    public class CadPlanValidator
    {
        public ValidationResult Validate(CadSpec spec)
        {
            return Validate(spec, null);
        }

        public ValidationResult Validate(CadSpec spec, IEnumerable<string> existingOperationIds)
        {
            var result = new ValidationResult();

            if (spec == null)
            {
                result.AddError("CadSpec bernilai null.");
                return result;
            }

            // 1. Validate Units
            if (string.IsNullOrWhiteSpace(spec.Units) || !UnitConverter.IsValidUnit(spec.Units))
            {
                result.AddError("Satuan ukuran tidak valid atau tidak didukung: '" + spec.Units + "'. Gunakan mm, cm, m, atau in.");
            }

            // 2. Validate Operations existence
            if (spec.Operations == null || spec.Operations.Count == 0)
            {
                result.AddError("Rencana CAD tidak memiliki daftar operasi yang harus dieksekusi.");
                return result;
            }

            // 3. Collect operation IDs for dependency checks
            var operationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingSet = existingOperationIds != null
                ? new HashSet<string>(existingOperationIds, StringComparer.OrdinalIgnoreCase)
                : null;

            if (existingSet != null)
            {
                foreach (var id in existingSet)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        operationIds.Add(id);
                    }
                }
            }

            var seenInSpec = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var op in spec.Operations)
            {
                if (op == null) continue;
                if (string.IsNullOrWhiteSpace(op.Id))
                {
                    result.AddError("Terdapat operasi tanpa Id.");
                }
                else if (seenInSpec.Contains(op.Id))
                {
                    result.AddError("Terdapat duplikasi Id operasi: " + op.Id);
                }
                else
                {
                    seenInSpec.Add(op.Id);
                    operationIds.Add(op.Id);
                }
            }

            // 4. Validate each operation
            for (int i = 0; i < spec.Operations.Count; i++)
            {
                var op = spec.Operations[i];
                ValidateOperation(op, operationIds, result);
            }

            // 5. Check for unknown dimensions
            if (spec.Unknowns != null && spec.Unknowns.Count > 0)
            {
                foreach (var unknown in spec.Unknowns)
                {
                    result.AddWarning("Dimensi atau parameter belum diketahui: " + unknown);
                }
            }

            // 6. Check for inferred values
            if (spec.InferredValues != null && spec.InferredValues.Count > 0)
            {
                foreach (var kvp in spec.InferredValues)
                {
                    result.AddWarning(string.Format("Nilai '{0}' = {1} merupakan estimasi/asumsi AI.", kvp.Key, kvp.Value));
                }
            }

            return result;
        }

        private void ValidateOperation(CadOperation op, HashSet<string> knownIds, ValidationResult result)
        {
            if (op == null)
            {
                result.AddError("Ditemukan operasi null di dalam daftar operasi.");
                return;
            }

            // Whitelist check
            if (op.Type == OperationType.Unknown)
            {
                result.AddError(string.Format("Operasi '{0}' memiliki tipe tidak valid atau tidak ditentukan (Raw: '{1}', Parsed: Unknown).", op.Id ?? "?", op.RawTypeString ?? "none"));
                return;
            }
            if (!AllowedOperations.Whitelist.Contains(op.Type))
            {
                result.AddError(string.Format("Operasi '{0}' dengan tipe '{1}' (raw: '{2}') tidak ada dalam whitelist operasi yang diizinkan.", op.Id ?? "?", op.Type, op.RawTypeString ?? "none"));
                return;
            }

            // Dependency check
            if (op.DependsOn != null)
            {
                foreach (var dep in op.DependsOn)
                {
                    if (!knownIds.Contains(dep))
                    {
                        result.AddError(string.Format("Operasi '{0}' bergantung pada operasi '{1}' yang tidak ditemukan dalam rencana CAD.", op.Id, dep));
                    }
                    if (string.Equals(dep, op.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddError(string.Format("Operasi '{0}' memiliki ketergantungan melingkar pada dirinya sendiri.", op.Id));
                    }
                }
            }

            // Parameters check per type
            switch (op.Type)
            {
                case OperationType.NewSketch:
                case OperationType.EndSketch:
                case OperationType.ActivateDocument:
                case OperationType.Rebuild:
                case OperationType.SavePart:
                case OperationType.ExportStep:
                case OperationType.ExportStl:
                    // Parameterless or optional parameters
                    break;

                case OperationType.AddLine:
                    ValidateDoubleExists(op, "x1", result);
                    ValidateDoubleExists(op, "y1", result);
                    ValidateDoubleExists(op, "x2", result);
                    ValidateDoubleExists(op, "y2", result);
                    break;

                case OperationType.AddPolyline:
                    List<double[]> polyPts;
                    if (!op.TryGetPoints(out polyPts) || polyPts.Count < 2)
                    {
                        result.AddError(string.Format("Operasi '{0}' ({1}): Parameter 'points' harus berisi daftar koordinat titik (minimal 2 titik untuk segmen terbuka, minimal 3 titik untuk profil tertutup).", op.Id, op.Type));
                        break;
                    }
                    bool isClosed = true;
                    op.TryGetBool("closed", out isClosed);
                    if (isClosed && polyPts.Count < 3)
                    {
                        result.AddError(string.Format("Operasi '{0}' ({1}): Profil tertutup (closed=true) memerlukan minimal 3 titik koordinat (diterima: {2}).", op.Id, op.Type, polyPts.Count));
                        break;
                    }
                    for (int pIdx = 0; pIdx < polyPts.Count; pIdx++)
                    {
                        if (double.IsNaN(polyPts[pIdx][0]) || double.IsInfinity(polyPts[pIdx][0]) ||
                            double.IsNaN(polyPts[pIdx][1]) || double.IsInfinity(polyPts[pIdx][1]))
                        {
                            result.AddError(string.Format("Operasi '{0}' ({1}): Titik #{2} memiliki koordinat NaN atau tak hingga.", op.Id, op.Type, pIdx + 1));
                        }
                    }
                    for (int pIdx = 0; pIdx < polyPts.Count - 1; pIdx++)
                    {
                        double dx = polyPts[pIdx + 1][0] - polyPts[pIdx][0];
                        double dy = polyPts[pIdx + 1][1] - polyPts[pIdx][1];
                        if (Math.Sqrt(dx * dx + dy * dy) < 1e-6)
                        {
                            result.AddWarning(string.Format("Operasi '{0}' ({1}): Segmen antara titik #{2} dan #{3} memiliki panjang mendekati nol.", op.Id, op.Type, pIdx + 1, pIdx + 2));
                        }
                    }
                    break;

                case OperationType.AddRectangle:
                case OperationType.AddCenterRectangle:
                    ValidatePositiveDouble(op, "width", result);
                    ValidatePositiveDouble(op, "height", result);
                    break;

                case OperationType.AddCircle:
                    ValidatePositiveDouble(op, "diameter", "radius", result);
                    break;

                case OperationType.AddArc:
                    ValidatePositiveDouble(op, "radius", result);
                    break;

                case OperationType.AddSlot:
                    ValidatePositiveDouble(op, "length", result);
                    ValidatePositiveDouble(op, "width", result);
                    break;

                case OperationType.AddPolygon:
                    ValidatePositiveDouble(op, "sides", result);
                    ValidatePositiveDouble(op, "radius", result);
                    break;

                case OperationType.AddPoint:
                    ValidateDoubleExists(op, "x", result);
                    ValidateDoubleExists(op, "y", result);
                    break;

                case OperationType.ExtrudeBoss:
                    ValidatePositiveDouble(op, "depth", result);
                    break;

                case OperationType.ExtrudeCut:
                    bool throughAll;
                    op.TryGetBool("throughAll", out throughAll);
                    if (!throughAll)
                    {
                        ValidatePositiveDouble(op, "depth", result);
                    }
                    break;

                case OperationType.RevolvedBoss:
                case OperationType.RevolvedCut:
                    double revAngle;
                    if (op.TryGetDouble("angle", out revAngle) && revAngle <= 0)
                    {
                        result.AddError(string.Format("Operasi '{0}' ({1}): Parameter 'angle' harus bernilai positif (diterima: {2}).", op.Id, op.Type, revAngle));
                    }
                    break;

                case OperationType.CreateHemisphere:
                    ValidatePositiveDouble(op, "diameter", "radius", result);
                    break;

                case OperationType.Fillet:
                    ValidatePositiveDouble(op, "radius", result);
                    break;

                case OperationType.Chamfer:
                    ValidatePositiveDouble(op, "distance", result);
                    break;

                case OperationType.SimpleHole:
                    ValidatePositiveDouble(op, "diameter", result, isRequiredHoleDimension: true);
                    bool holeThrough;
                    op.TryGetBool("throughAll", out holeThrough);
                    if (!holeThrough)
                    {
                        ValidatePositiveDouble(op, "depth", result);
                    }
                    break;

                case OperationType.Shell:
                    ValidatePositiveDouble(op, "thickness", result);
                    break;

                case OperationType.LinearPattern:
                    ValidatePositiveDouble(op, "spacing", result);
                    double count;
                    if (op.TryGetDouble("instances", out count) && count < 2)
                    {
                        result.AddError(string.Format("Operasi '{0}' ({1}): Jumlah instance pattern minimal 2.", op.Id, op.Type));
                    }
                    break;

                case OperationType.CircularPattern:
                    double circCount;
                    if (op.TryGetDouble("instances", out circCount) && circCount < 2)
                    {
                        result.AddError(string.Format("Operasi '{0}' ({1}): Jumlah instance circular pattern minimal 2.", op.Id, op.Type));
                    }
                    break;

                case OperationType.Mirror:
                case OperationType.CreatePlane:
                case OperationType.SweepBoss:
                case OperationType.AddDimension:
                case OperationType.AddConstraint:
                    // Valid parameters checked conditionally
                    break;
            }
        }

        private void ValidatePositiveDouble(CadOperation op, string paramName, ValidationResult result, bool isRequiredHoleDimension = false)
        {
            double val;
            if (!op.TryGetDouble(paramName, out val))
            {
                if (isRequiredHoleDimension)
                {
                    result.AddMissingDimension(string.Format("{0}.{1}", op.Id, paramName));
                }
                else
                {
                    result.AddError(string.Format("Operasi '{0}' ({1}): Parameter wajib '{2}' tidak ditemukan atau bukan angka valid.", op.Id, op.Type, paramName));
                }
                return;
            }

            if (val <= 0)
            {
                result.AddError(string.Format("Operasi '{0}' ({1}): Parameter '{2}' harus bernilai positif lebih dari nol (diterima: {3}).", op.Id, op.Type, paramName, val));
            }
        }

        private void ValidatePositiveDouble(CadOperation op, string paramName1, string paramName2, ValidationResult result)
        {
            double val1;
            double val2;
            bool has1 = op.TryGetDouble(paramName1, out val1);
            bool has2 = op.TryGetDouble(paramName2, out val2);

            if (!has1 && !has2)
            {
                result.AddError(string.Format("Operasi '{0}' ({1}): Harus menyertakan salah satu parameter '{2}' atau '{3}'.", op.Id, op.Type, paramName1, paramName2));
                return;
            }

            if (has1 && val1 <= 0)
            {
                result.AddError(string.Format("Operasi '{0}' ({1}): Parameter '{2}' harus bernilai positif lebih dari nol (diterima: {3}).", op.Id, op.Type, paramName1, val1));
            }
            if (has2 && val2 <= 0)
            {
                result.AddError(string.Format("Operasi '{0}' ({1}): Parameter '{2}' harus bernilai positif lebih dari nol (diterima: {3}).", op.Id, op.Type, paramName2, val2));
            }
        }

        private void ValidateDoubleExists(CadOperation op, string paramName, ValidationResult result)
        {
            double val;
            if (!op.TryGetDouble(paramName, out val))
            {
                result.AddError(string.Format("Operasi '{0}' ({1}): Parameter wajib '{2}' tidak ditemukan atau bukan angka valid.", op.Id, op.Type, paramName));
            }
        }
    }
}
