using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Models
{
    public enum OperationType
    {
        Unknown = -1,
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
        CreateHemisphere = 17,
        // Upgraded in v2.0
        EndSketch = 18,
        AddPolyline = 19,
        AddArc = 20,
        AddCenterRectangle = 21,
        AddSlot = 22,
        AddPolygon = 23,
        AddPoint = 24,
        AddConstraint = 25,
        RevolvedCut = 26,
        SweepBoss = 27,
        Shell = 28,
        Mirror = 29,
        CreatePlane = 30,
        ActivateDocument = 31,
        Rebuild = 32
    }

    public class CadOperation
    {
        public string Id { get; set; }
        public bool IsIdGenerated { get; set; }
        public string RawTypeString { get; set; }
        public OperationType Type { get; set; }

        public Dictionary<string, object> Parameters { get; set; }
        public List<string> DependsOn { get; set; }
        public string Description { get; set; }

        public CadOperation()
        {
            Type = OperationType.Unknown;
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

        public bool TryGetBool(string key, out bool value)
        {
            value = false;
            if (Parameters == null || !Parameters.ContainsKey(key))
                return false;

            object raw = Parameters[key];
            if (raw == null)
                return false;

            try
            {
                value = Convert.ToBoolean(raw);
                return true;
            }
            catch
            {
                string s = raw.ToString().Trim().ToLowerInvariant();
                if (s == "true" || s == "1" || s == "yes" || s == "ya")
                {
                    value = true;
                    return true;
                }
                if (s == "false" || s == "0" || s == "no" || s == "tidak")
                {
                    value = false;
                    return true;
                }
                return false;
            }
        }

        public bool TryGetPoints(out List<double[]> points)
        {
            points = new List<double[]>();
            if (Parameters == null || !Parameters.ContainsKey("points"))
                return false;

            object raw = Parameters["points"];
            if (raw == null)
                return false;

            var list = raw as System.Collections.IEnumerable;
            if (list == null)
                return false;

            foreach (var item in list)
            {
                if (item == null) continue;

                var ptCoordList = new List<double>();
                var itemEnumerable = item as System.Collections.IEnumerable;
                var itemDict = item as Dictionary<string, object>;

                if (itemDict != null)
                {
                    double x = 0, y = 0, z = 0;
                    if (itemDict.ContainsKey("x")) x = Convert.ToDouble(itemDict["x"]);
                    if (itemDict.ContainsKey("y")) y = Convert.ToDouble(itemDict["y"]);
                    if (itemDict.ContainsKey("z")) z = Convert.ToDouble(itemDict["z"]);
                    points.Add(new double[] { x, y, z });
                }
                else if (itemEnumerable != null && !(item is string))
                {
                    foreach (var coord in itemEnumerable)
                    {
                        try
                        {
                            ptCoordList.Add(Convert.ToDouble(coord));
                        }
                        catch { }
                    }

                    if (ptCoordList.Count >= 2)
                    {
                        double z = ptCoordList.Count >= 3 ? ptCoordList[2] : 0.0;
                        points.Add(new double[] { ptCoordList[0], ptCoordList[1], z });
                    }
                }
            }

            return points.Count >= 2;
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
            OperationType.CreateHemisphere,
            OperationType.EndSketch,
            OperationType.AddPolyline,
            OperationType.AddArc,
            OperationType.AddCenterRectangle,
            OperationType.AddSlot,
            OperationType.AddPolygon,
            OperationType.AddPoint,
            OperationType.AddConstraint,
            OperationType.RevolvedCut,
            OperationType.SweepBoss,
            OperationType.Shell,
            OperationType.Mirror,
            OperationType.CreatePlane,
            OperationType.ActivateDocument,
            OperationType.Rebuild
        };
    }
}
