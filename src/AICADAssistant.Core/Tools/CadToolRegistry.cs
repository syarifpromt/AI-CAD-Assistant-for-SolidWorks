using System;
using System.Collections.Generic;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Core.Tools
{
    public class BaseCadTool : ICadTool
    {
        public string Name { get; set; }
        public OperationType OperationType { get; set; }
        public ToolSupportStatus SupportStatus { get; set; }
        public string Description { get; set; }
        public List<ToolParameterSchema> ParameterSchemas { get; set; }
        public List<string> Preconditions { get; set; }
        public List<string> Postconditions { get; set; }

        public BaseCadTool()
        {
            ParameterSchemas = new List<ToolParameterSchema>();
            Preconditions = new List<string>();
            Postconditions = new List<string>();
            SupportStatus = ToolSupportStatus.FullySupported;
        }

        public virtual bool ValidateParameters(CadOperation operation, out string errorMessage)
        {
            errorMessage = null;
            if (operation == null)
            {
                errorMessage = "Operation is null";
                return false;
            }

            foreach (var schema in ParameterSchemas)
            {
                if (schema.IsRequired)
                {
                    if (operation.Parameters == null || !operation.Parameters.ContainsKey(schema.Name))
                    {
                        errorMessage = string.Format("Missing required parameter: {0}", schema.Name);
                        return false;
                    }

                    if (schema.ParameterType == typeof(double))
                    {
                        double dVal;
                        if (!operation.TryGetDouble(schema.Name, out dVal))
                        {
                            errorMessage = string.Format("Parameter {0} must be a valid number", schema.Name);
                            return false;
                        }
                        if (dVal < schema.MinValue || dVal > schema.MaxValue)
                        {
                            errorMessage = string.Format("Parameter {0} ({1}) is out of bounds [{2}, {3}]", schema.Name, dVal, schema.MinValue, schema.MaxValue);
                            return false;
                        }
                    }
                }
            }

            return true;
        }
    }

    public static class CadToolRegistry
    {
        private static readonly Dictionary<OperationType, ICadTool> _tools = new Dictionary<OperationType, ICadTool>();

        static CadToolRegistry()
        {
            RegisterTool(new BaseCadTool
            {
                Name = "NewPart",
                OperationType = OperationType.NewPart,
                Description = "Creates a new native SOLIDWORKS part document",
                SupportStatus = ToolSupportStatus.FullySupported,
                Postconditions = new List<string> { "ActiveDocumentNotNull" }
            });

            RegisterTool(new BaseCadTool
            {
                Name = "NewSketch",
                OperationType = OperationType.NewSketch,
                Description = "Opens a new 2D sketch on specified plane or planar face",
                SupportStatus = ToolSupportStatus.FullySupported,
                Preconditions = new List<string> { "ActiveDocumentNotNull" },
                Postconditions = new List<string> { "SketchActive" }
            });

            RegisterTool(new BaseCadTool
            {
                Name = "EndSketch",
                OperationType = OperationType.EndSketch,
                Description = "Exits the active 2D sketch mode",
                SupportStatus = ToolSupportStatus.FullySupported,
                Preconditions = new List<string> { "SketchActive" },
                Postconditions = new List<string> { "SketchClosed" }
            });

            var bossTool = new BaseCadTool
            {
                Name = "ExtrudeBoss",
                OperationType = OperationType.ExtrudeBoss,
                Description = "Extrudes an active or selected closed sketch profile into a solid body",
                SupportStatus = ToolSupportStatus.FullySupported,
                Preconditions = new List<string> { "ClosedSketchAvailable" },
                Postconditions = new List<string> { "SolidBodiesCountIncremented" }
            };
            bossTool.ParameterSchemas.Add(new ToolParameterSchema("depth", typeof(double), true, "Extrusion depth in mm") { MinValue = 0.001 });
            RegisterTool(bossTool);

            var cutTool = new BaseCadTool
            {
                Name = "ExtrudeCut",
                OperationType = OperationType.ExtrudeCut,
                Description = "Cuts material through solid body using a closed sketch profile",
                SupportStatus = ToolSupportStatus.FullySupported,
                Preconditions = new List<string> { "ExistingSolidBody", "ClosedSketchAvailable" }
            };
            cutTool.ParameterSchemas.Add(new ToolParameterSchema("depth", typeof(double), false, "Cut depth in mm (0 for through-all)") { MinValue = 0.0 });
            RegisterTool(cutTool);

            var holeTool = new BaseCadTool
            {
                Name = "SimpleHole",
                OperationType = OperationType.SimpleHole,
                Description = "Creates a through or blind hole on a planar face",
                SupportStatus = ToolSupportStatus.FullySupported,
                Preconditions = new List<string> { "ExistingSolidBody" }
            };
            holeTool.ParameterSchemas.Add(new ToolParameterSchema("diameter", typeof(double), true, "Hole diameter in mm") { MinValue = 0.01 });
            RegisterTool(holeTool);

            var hemiTool = new BaseCadTool
            {
                Name = "CreateHemisphere",
                OperationType = OperationType.CreateHemisphere,
                Description = "Constructs a solid hemisphere via 180-deg arc revolve",
                SupportStatus = ToolSupportStatus.FullySupported,
                Postconditions = new List<string> { "SolidBodiesCountIncremented" }
            };
            hemiTool.ParameterSchemas.Add(new ToolParameterSchema("diameter", typeof(double), true, "Hemisphere diameter in mm") { MinValue = 0.01 });
            RegisterTool(hemiTool);

            var filletTool = new BaseCadTool
            {
                Name = "Fillet",
                OperationType = OperationType.Fillet,
                Description = "Applies constant radius fillet to target edges",
                SupportStatus = ToolSupportStatus.FullySupported,
                Preconditions = new List<string> { "ExistingSolidBody" }
            };
            filletTool.ParameterSchemas.Add(new ToolParameterSchema("radius", typeof(double), true, "Fillet radius in mm") { MinValue = 0.01 });
            RegisterTool(filletTool);

            var chamferTool = new BaseCadTool
            {
                Name = "Chamfer",
                OperationType = OperationType.Chamfer,
                Description = "Applies equal-distance or angle chamfer to target edges",
                SupportStatus = ToolSupportStatus.FullySupported,
                Preconditions = new List<string> { "ExistingSolidBody" }
            };
            chamferTool.ParameterSchemas.Add(new ToolParameterSchema("distance", typeof(double), true, "Chamfer distance in mm") { MinValue = 0.01 });
            RegisterTool(chamferTool);

            var revolveTool = new BaseCadTool
            {
                Name = "RevolvedBoss",
                OperationType = OperationType.RevolvedBoss,
                Description = "Revolves a closed sketch profile around an axis centerline",
                SupportStatus = ToolSupportStatus.FullySupported,
                Preconditions = new List<string> { "ClosedSketchWithCenterline" }
            };
            revolveTool.ParameterSchemas.Add(new ToolParameterSchema("angle", typeof(double), false, "Revolution angle in degrees") { MinValue = 1, MaxValue = 360 });
            RegisterTool(revolveTool);
        }

        public static void RegisterTool(ICadTool tool)
        {
            if (tool != null)
            {
                _tools[tool.OperationType] = tool;
            }
        }

        public static ICadTool GetTool(OperationType type)
        {
            ICadTool tool;
            if (_tools.TryGetValue(type, out tool))
                return tool;
            return null;
        }

        public static bool IsSupported(OperationType type)
        {
            ICadTool tool;
            if (_tools.TryGetValue(type, out tool))
            {
                return tool.SupportStatus == ToolSupportStatus.FullySupported ||
                       tool.SupportStatus == ToolSupportStatus.PartiallySupported;
            }
            return false;
        }
    }
}

