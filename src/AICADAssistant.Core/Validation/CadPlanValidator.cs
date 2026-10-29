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
            foreach (var op in spec.Operations)
            {
                if (string.IsNullOrWhiteSpace(op.Id))
                {
                    result.AddError("Terdapat operasi tanpa Id.");
                }
                else if (operationIds.Contains(op.Id))
                {
                    result.AddError("Terdapat duplikasi Id operasi: " + op.Id);
                }
                else
                {
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
            if (!AllowedOperations.Whitelist.Contains(op.Type))
            {
                result.AddError(string.Format("Operasi '{0}' dengan tipe '{1}' tidak ada dalam whitelist operasi yang diizinkan.", op.Id, op.Type));
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
                    // plane is optional or can be Front/Top/Right
                    break;

                case OperationType.AddRectangle:
                    ValidatePositiveDouble(op, "width", result);
                    ValidatePositiveDouble(op, "height", result);
                    break;

                case OperationType.AddCircle:
                    ValidatePositiveDouble(op, "diameter", "radius", result);
                    break;

                case OperationType.ExtrudeBoss:
                case OperationType.ExtrudeCut:
                    ValidatePositiveDouble(op, "depth", result);
                    break;

                case OperationType.Fillet:
                    ValidatePositiveDouble(op, "radius", result);
                    break;

                case OperationType.Chamfer:
                    ValidatePositiveDouble(op, "distance", result);
                    break;

                case OperationType.SimpleHole:
                    ValidatePositiveDouble(op, "diameter", result, isRequiredHoleDimension: true);
                    ValidatePositiveDouble(op, "depth", result);
                    break;

                case OperationType.LinearPattern:
                    ValidatePositiveDouble(op, "spacing", result);
                    double count;
                    if (op.TryGetDouble("instances", out count) && count < 2)
                    {
                        result.AddError(string.Format("Operasi '{0}' ({1}): Jumlah instance pattern minimal 2.", op.Id, op.Type));
                    }
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
    }
}
