using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Models
{
    public enum OperationType
    {
        NewPart = 0,
        NewSketch = 1,
        AddLine = 2,
        AddRectangle = 3,
        AddCircle = 4,
        AddDimension = 5,
        ExtrudeBoss = 6,
        ExtrudeCut = 7,
        RevolvedBoss = 8,
        Fillet = 9,
        Chamfer = 10,
        SimpleHole = 11,
        LinearPattern = 12,
        CircularPattern = 13,
        SavePart = 14,
        ExportStep = 15,
        ExportStl = 16,
        CreateHemisphere = 17
    }

    public class CadOperation
    {
        public string Id { get; set; }
        public OperationType Type { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public List<string> DependsOn { get; set; }
        public string Description { get; set; }

        public CadOperation()
        {
            Parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            DependsOn = new List<string>();
        }

        public bool TryGetDouble(string key, out double value)
        {
            value = 0;
            if (Parameters == null || !Parameters.ContainsKey(key))
                return false;

            object raw = Parameters[key];
            if (raw == null)
                return false;

            try
            {
                value = Convert.ToDouble(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetString(string key, out string value)
        {
            value = null;
            if (Parameters == null || !Parameters.ContainsKey(key))
                return false;

            object raw = Parameters[key];
            if (raw == null)
                return false;

            value = raw.ToString();
            return !string.IsNullOrEmpty(value);
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1}: {2}", Id ?? "?", Type, Description ?? string.Empty);
        }
    }

    public class OperationResult
    {
        public string OperationId { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> WarningMessages { get; set; }
        public DateTime Timestamp { get; set; }

        public OperationResult()
        {
            WarningMessages = new List<string>();
            Timestamp = DateTime.Now;
        }

        public static OperationResult Succeeded(string operationId)
        {
            return new OperationResult { OperationId = operationId, Success = true };
        }

        public static OperationResult Failed(string operationId, string error)
        {
            return new OperationResult
            {
                OperationId = operationId,
                Success = false,
                ErrorMessage = error
            };
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1}{2}",
                OperationId ?? "?",
                Success ? "OK" : "FAILED",
                Success ? string.Empty : ": " + ErrorMessage);
        }
    }

    public static class AllowedOperations
    {
        public static readonly HashSet<OperationType> Whitelist = new HashSet<OperationType>
        {
            OperationType.NewPart,
            OperationType.NewSketch,
            OperationType.AddLine,
            OperationType.AddRectangle,
            OperationType.AddCircle,
            OperationType.AddDimension,
            OperationType.ExtrudeBoss,
            OperationType.ExtrudeCut,
            OperationType.RevolvedBoss,
            OperationType.Fillet,
            OperationType.Chamfer,
            OperationType.SimpleHole,
            OperationType.LinearPattern,
            OperationType.CircularPattern,
            OperationType.SavePart,
            OperationType.ExportStep,
            OperationType.ExportStl,
            OperationType.CreateHemisphere
        };
    }
}
