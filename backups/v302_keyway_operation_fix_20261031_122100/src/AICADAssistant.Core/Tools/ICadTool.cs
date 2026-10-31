using System;
using System.Collections.Generic;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Models.Contracts;

namespace AICADAssistant.Core.Tools
{
    public enum ToolSupportStatus
    {
        FullySupported = 0,
        PartiallySupported = 1,
        Experimental = 2,
        Unsupported = 3
    }

    public class ToolParameterSchema
    {
        public string Name { get; set; }
        public Type ParameterType { get; set; }
        public bool IsRequired { get; set; }
        public object DefaultValue { get; set; }
        public string Description { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }

        public ToolParameterSchema(string name, Type type, bool isRequired = true, string description = "")
        {
            Name = name;
            ParameterType = type;
            IsRequired = isRequired;
            Description = description;
            MinValue = double.MinValue;
            MaxValue = double.MaxValue;
        }
    }

    public interface ICadTool
    {
        string Name { get; }
        OperationType OperationType { get; }
        ToolSupportStatus SupportStatus { get; }
        string Description { get; }
        List<ToolParameterSchema> ParameterSchemas { get; }
        List<string> Preconditions { get; }
        List<string> Postconditions { get; }

        bool ValidateParameters(CadOperation operation, out string errorMessage);
    }
}

