using System;
using System.Collections.Generic;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Core.CAD
{
    /// <summary>
    /// Maps raw Gemini operation type representations (numeric, snake_case, PascalCase, or colloquial aliases)
    /// to strongly-typed OperationType enum values.
    /// Returns OperationType.Unknown when the type is missing or unrecognized.
    /// Strictly ignores operation IDs to prevent false categorization.
    /// </summary>
    public static class OperationTypeMapper
    {
        private static readonly Dictionary<string, OperationType> StringMap =
            new Dictionary<string, OperationType>(StringComparer.OrdinalIgnoreCase);

        static OperationTypeMapper()
        {
            // 1. Direct enum names and normalized versions
            foreach (OperationType val in Enum.GetValues(typeof(OperationType)))
            {
                if (val == OperationType.Unknown) continue;
                string name = val.ToString();
                StringMap[name] = val;
                StringMap[Normalize(name)] = val;
            }

            // 2. Comprehensive aliases and colloquial variants
            RegisterAlias(OperationType.NewPart, "newpart", "part", "createpart", "new_part", "create_part");
            RegisterAlias(OperationType.NewSketch, "newsketch", "sketch", "createsketch", "opensketch", "new_sketch", "create_sketch", "open_sketch");
            RegisterAlias(OperationType.AddLine, "addline", "line", "createline", "add_line", "draw_line");
            RegisterAlias(OperationType.AddRectangle, "addrectangle", "rectangle", "rect", "add_rectangle", "draw_rectangle");
            RegisterAlias(OperationType.AddCenterRectangle, "addcenterrectangle", "centerrectangle", "centerrect", "add_center_rectangle");
            RegisterAlias(OperationType.AddCircle, "addcircle", "circle", "add_circle", "draw_circle");
            RegisterAlias(OperationType.AddDimension, "adddimension", "dimension", "dim", "add_dimension");
            RegisterAlias(OperationType.ExtrudeBoss, "extrudeboss", "extrude", "boss", "extrusion", "extrude_boss");
            RegisterAlias(OperationType.ExtrudeCut, "extrudecut", "cutextrude", "cut", "extrusioncut", "extrude_cut", "cut_extrude", "cutout");
            RegisterAlias(OperationType.RevolvedBoss, "revolvedboss", "revolve", "revolveboss", "revolved", "revolved_boss", "revolve_boss");
            RegisterAlias(OperationType.RevolvedCut, "revolvedcut", "revolvecut", "cutrevolve", "revolved_cut", "cut_revolve");
            RegisterAlias(OperationType.Fillet, "fillet", "addfillet", "add_fillet", "round");
            RegisterAlias(OperationType.Chamfer, "chamfer", "addchamfer", "add_chamfer");
            RegisterAlias(OperationType.SimpleHole, "simplehole", "hole", "addhole", "simple_hole", "add_hole");
            RegisterAlias(OperationType.LinearPattern, "linearpattern", "patternlinear", "linear_pattern", "pattern_linear");
            RegisterAlias(OperationType.CircularPattern, "circularpattern", "patterncircular", "circular_pattern", "pattern_circular");
            RegisterAlias(OperationType.SavePart, "savepart", "save", "save_part");
            RegisterAlias(OperationType.ExportStep, "exportstep", "step", "export_step");
            RegisterAlias(OperationType.ExportStl, "exportstl", "stl", "export_stl");
            RegisterAlias(OperationType.CreateHemisphere, "createhemisphere", "hemisphere", "create_hemisphere", "kubah");
            RegisterAlias(OperationType.EndSketch, "endsketch", "closesketch", "exitsketch", "end_sketch", "close_sketch", "exit_sketch");
            RegisterAlias(OperationType.AddPolyline, "addpolyline", "polyline", "contour", "add_polyline", "polygonprofile");
            RegisterAlias(OperationType.AddArc, "addarc", "arc", "add_arc", "draw_arc");
            RegisterAlias(OperationType.AddSlot, "addslot", "slot", "add_slot", "keywayslot", "alur_pasak");
            RegisterAlias(OperationType.AddPolygon, "addpolygon", "polygon", "add_polygon");
            RegisterAlias(OperationType.AddPoint, "addpoint", "point", "add_point");
            RegisterAlias(OperationType.AddConstraint, "addconstraint", "constraint", "add_constraint");
            RegisterAlias(OperationType.SweepBoss, "sweepboss", "sweep", "sweep_boss");
            RegisterAlias(OperationType.Shell, "shell", "addshell", "add_shell");
            RegisterAlias(OperationType.Mirror, "mirror", "mirrortool", "add_mirror");
            RegisterAlias(OperationType.CreatePlane, "createplane", "plane", "offsetplane", "referenceplane", "refplane", "create_plane", "offset_plane", "bidang_referensi");
            RegisterAlias(OperationType.ActivateDocument, "activatedocument", "activate", "activate_document");
            RegisterAlias(OperationType.Rebuild, "rebuild", "rebuildmodel", "rebuild_model");
        }

        private static void RegisterAlias(OperationType opType, params string[] aliases)
        {
            if (aliases == null) return;
            foreach (var alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;
                StringMap[alias] = opType;
                StringMap[Normalize(alias)] = opType;
            }
        }

        private static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace("_", "")
                        .Replace("-", "")
                        .Replace(" ", "")
                        .Trim()
                        .ToLowerInvariant();
        }

        /// <summary>
        /// Parses an arbitrary raw object (int, string, enum) into an OperationType.
        /// Returns OperationType.Unknown if raw is null or unrecognized.
        /// </summary>
        public static OperationType Parse(object raw)
        {
            if (raw == null)
                return OperationType.Unknown;

            if (raw is OperationType directOp)
                return directOp;

            // Numeric check
            if (raw is int intVal)
            {
                if (intVal >= 0 && Enum.IsDefined(typeof(OperationType), intVal))
                    return (OperationType)intVal;
                return OperationType.Unknown;
            }

            if (raw is long longVal)
            {
                int asInt = (int)longVal;
                if (asInt >= 0 && Enum.IsDefined(typeof(OperationType), asInt))
                    return (OperationType)asInt;
                return OperationType.Unknown;
            }

            if (raw is double dVal)
            {
                int asInt = (int)Math.Round(dVal);
                if (asInt >= 0 && Enum.IsDefined(typeof(OperationType), asInt))
                    return (OperationType)asInt;
                return OperationType.Unknown;
            }

            string str = raw.ToString().Trim();
            if (string.IsNullOrWhiteSpace(str))
                return OperationType.Unknown;

            // Numeric string check
            if (int.TryParse(str, out int parsedNum))
            {
                if (parsedNum >= 0 && Enum.IsDefined(typeof(OperationType), parsedNum))
                    return (OperationType)parsedNum;
                return OperationType.Unknown;
            }

            // Dictionary lookup (original)
            if (StringMap.TryGetValue(str, out OperationType matched))
                return matched;

            // Dictionary lookup (normalized)
            string norm = Normalize(str);
            if (StringMap.TryGetValue(norm, out matched))
                return matched;

            // Enum.TryParse fallback
            if (Enum.TryParse<OperationType>(str, true, out var enumResult) && enumResult != OperationType.Unknown)
            {
                return enumResult;
            }

            return OperationType.Unknown;
        }
    }
}

