using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;

namespace AICADAssistant.Core.CAD
{
    /// <summary>
    /// Reconciles CAD generation context between active SOLIDWORKS model state,
    /// conversation history, and incoming CadSpec operations.
    /// Distinguishes between 'Create New Model' and 'Modify Existing Model',
    /// preventing duplicate creation of base features while preserving dependency chains.
    /// </summary>
    public static class CadContextReconciler
    {
        private static readonly Regex NewModelPattern = new Regex(
            @"\b(?:buat(?:\s+(?:model|part|komponen|dokumen))?\s+baru|model\s+baru|part\s+baru|dokumen\s+baru|mulai\s+dari\s+awal|reset\s+model)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ModificationKeywordsPattern = new Regex(
            @"\b(?:tambah|tambahkan|pasang|pasangkan|buatkan\s+lubang|buat\s+lubang|lubangi|coak|potong|potongkan|extrude|fillet|chamfer|revisi|ubah|ganti|perpanjang|perpendek|modifikasi|di\s+tengah|pada\s+permukaan|pada\s+sisi|di\s+atas|di\s+bawah|di\s+dasar)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Determines whether the user prompt represents a modification to an existing model
        /// rather than a request to start a completely new part.
        /// </summary>
        public static bool IsModificationRequest(string text, CadSpec currentSpec, int activeBodyCount)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string clean = text.Trim();

            // 1. Explicit request to create a brand new model takes precedence
            if (NewModelPattern.IsMatch(clean))
            {
                return false;
            }

            // 2. If there is no active spec and no existing solid bodies in SOLIDWORKS,
            // then it MUST be a new model request
            if (currentSpec == null && activeBodyCount <= 0)
            {
                return false;
            }

            // 3. Check for modification action verbs and location prepositions
            if (ModificationKeywordsPattern.IsMatch(clean))
            {
                return true;
            }

            // 4. If an existing model with solid bodies exists, and the user prompt
            // starts with phrases like "tangkai...", "lubang...", "chamfer...", "fillet..."
            if (activeBodyCount > 0 || currentSpec != null)
            {
                if (Regex.IsMatch(clean, @"^(?:tangkai|batang|lubang|silinder|fillet|chamfer|coakan)\b", RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reconciles an incoming CadSpec for an existing model.
        /// Identifies which operations are already present on the active model,
        /// verifies dependency integrity, and populates existingCompletedOpIds.
        /// </summary>
        public static bool TryReconcileRevisionSpec(
            CadSpec incomingSpec,
            CadSpec currentSpec,
            int activeBodyCount,
            out HashSet<string> existingCompletedOpIds,
            out string reconciliationNote)
        {
            existingCompletedOpIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            reconciliationNote = string.Empty;

            if (incomingSpec == null)
            {
                reconciliationNote = "Incoming CadSpec bernilai null.";
                return false;
            }

            // If there are existing solid bodies on the active part, register completed operations from currentSpec
            var priorOpMap = new Dictionary<string, CadOperation>(StringComparer.OrdinalIgnoreCase);
            if (currentSpec != null && currentSpec.Operations != null)
            {
                foreach (var op in currentSpec.Operations)
                {
                    if (op != null && !string.IsNullOrWhiteSpace(op.Id))
                    {
                        existingCompletedOpIds.Add(op.Id);
                        priorOpMap[op.Id] = op;
                    }
                }
            }

            if (incomingSpec.Operations == null || incomingSpec.Operations.Count == 0)
            {
                reconciliationNote = "Incoming CadSpec tidak memiliki operasi.";
                return true;
            }

            // Check if incomingSpec duplicates the base feature (e.g. CreateHemisphere or NewPart)
            // when solid bodies already exist in SOLIDWORKS
            if (activeBodyCount > 0)
            {
                for (int i = 0; i < incomingSpec.Operations.Count; i++)
                {
                    var op = incomingSpec.Operations[i];
                    if (op == null) continue;

                    // If incoming op is NewPart or CreateHemisphere while we already have a solid model
                    if (op.Type == OperationType.NewPart)
                    {
                        existingCompletedOpIds.Add(op.Id);
                        reconciliationNote += string.Format("Melompati duplikasi NewPart [{0}]. ", op.Id);
                    }
                    else if (op.Type == OperationType.CreateHemisphere)
                    {
                        // Current model already has solid body; mark CreateHemisphere as completed
                        existingCompletedOpIds.Add(op.Id);
                        reconciliationNote += string.Format("Melompati duplikasi CreateHemisphere [{0}] pada model aktif. ", op.Id);
                    }
                    else if (priorOpMap.ContainsKey(op.Id))
                    {
                        // Same operation ID as previously executed base op
                        existingCompletedOpIds.Add(op.Id);
                        reconciliationNote += string.Format("Operasi [{0}] ({1}) sudah selesai pada model aktif. ", op.Id, op.Type);
                    }
                }
            }

            // Check if ALL incoming operations are marked as existing (nothing new to do)
            int newOpsCount = 0;
            foreach (var op in incomingSpec.Operations)
            {
                if (op != null && !existingCompletedOpIds.Contains(op.Id))
                {
                    newOpsCount++;
                }
            }

            if (newOpsCount == 0 && incomingSpec.Operations.Count > 0)
            {
                reconciliationNote += "Semua operasi dalam rencana sudah ada pada model aktif.";
            }
            else
            {
                reconciliationNote += string.Format("Terdapat {0} operasi baru yang akan dieksekusi.", newOpsCount);
            }

            return true;
        }

        /// <summary>
        /// Merges executed operations into the cumulative CadSpec to maintain the complete model history.
        /// </summary>
        public static CadSpec MergeSpecs(CadSpec currentSpec, CadSpec executedSpec)
        {
            if (currentSpec == null) return executedSpec;
            if (executedSpec == null) return currentSpec;

            var merged = new CadSpec
            {
                RequestId = executedSpec.RequestId ?? currentSpec.RequestId,
                DocumentType = executedSpec.DocumentType,
                Units = !string.IsNullOrWhiteSpace(executedSpec.Units) ? executedSpec.Units : currentSpec.Units,
                Source = executedSpec.Source,
                RequiresConfirmation = executedSpec.RequiresConfirmation,
                Notes = executedSpec.Notes ?? currentSpec.Notes,
                Operations = new List<CadOperation>()
            };

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add existing operations
            if (currentSpec.Operations != null)
            {
                foreach (var op in currentSpec.Operations)
                {
                    if (op != null && !string.IsNullOrWhiteSpace(op.Id) && !seenIds.Contains(op.Id))
                    {
                        merged.Operations.Add(op);
                        seenIds.Add(op.Id);
                    }
                }
            }

            // Add new operations from executed spec
            if (executedSpec.Operations != null)
            {
                foreach (var op in executedSpec.Operations)
                {
                    if (op != null && !string.IsNullOrWhiteSpace(op.Id) && !seenIds.Contains(op.Id))
                    {
                        merged.Operations.Add(op);
                        seenIds.Add(op.Id);
                    }
                }
            }

            // Merge explicit dimensions
            merged.ExplicitDimensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (currentSpec.ExplicitDimensions != null)
            {
                foreach (var kvp in currentSpec.ExplicitDimensions)
                {
                    merged.ExplicitDimensions[kvp.Key] = kvp.Value;
                }
            }
            if (executedSpec.ExplicitDimensions != null)
            {
                foreach (var kvp in executedSpec.ExplicitDimensions)
                {
                    merged.ExplicitDimensions[kvp.Key] = kvp.Value;
                }
            }

            return merged;
        }
    }
}

