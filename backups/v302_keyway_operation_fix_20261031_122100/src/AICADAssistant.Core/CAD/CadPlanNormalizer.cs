using System;
using System.Collections.Generic;
using System.Linq;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Core.CAD
{
    public static class CadPlanNormalizer
    {
        /// <summary>
        /// Normalizes operation IDs in the CadSpec before validation and reconciliation.
        /// Fills missing or whitespace IDs deterministically and checks for intra-spec duplicates.
        /// </summary>
        public static bool TryNormalizeOperationIds(
            CadSpec spec,
            IEnumerable<string> existingOpIds,
            out int missingIdCount,
            out int generatedIdCount,
            out int duplicateIdCount,
            out string diagnosticMessage)
        {
            missingIdCount = 0;
            generatedIdCount = 0;
            duplicateIdCount = 0;
            diagnosticMessage = string.Empty;

            if (spec == null)
            {
                diagnosticMessage = "CadSpec bernilai null.";
                return false;
            }

            if (spec.Operations == null || spec.Operations.Count == 0)
            {
                diagnosticMessage = "Tidak ada operasi dalam CadSpec.";
                return true;
            }

            var existingSet = existingOpIds != null
                ? new HashSet<string>(existingOpIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var seenInSpec = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string basePrefix = !string.IsNullOrWhiteSpace(spec.RequestId)
                ? spec.RequestId.Trim()
                : "op";

            for (int i = 0; i < spec.Operations.Count; i++)
            {
                var op = spec.Operations[i];
                if (op == null) continue;

                if (string.IsNullOrWhiteSpace(op.Id))
                {
                    missingIdCount++;
                    string candidateId = $"{basePrefix}_{i + 1}_{op.Type.ToString().ToLowerInvariant()}";
                    int suffix = 1;
                    while (seenInSpec.Contains(candidateId) || existingSet.Contains(candidateId))
                    {
                        candidateId = $"{basePrefix}_{i + 1}_{op.Type.ToString().ToLowerInvariant()}_{suffix++}";
                    }

                    op.Id = candidateId;
                    op.IsIdGenerated = true;
                    generatedIdCount++;
                }

                if (!seenInSpec.Add(op.Id))
                {
                    duplicateIdCount++;
                    duplicateNames.Add(op.Id);
                }
            }

            if (duplicateIdCount > 0)
            {
                diagnosticMessage = $"Spesifikasi mengandung {duplicateIdCount} ID operasi duplikat: {string.Join(", ", duplicateNames)}.";
                return false;
            }

            diagnosticMessage = missingIdCount > 0
                ? $"Berhasil menormalisasi {missingIdCount} ID operasi yang sebelumnya kosong."
                : "Semua ID operasi valid dan unik.";
            return true;
        }
    }
}

