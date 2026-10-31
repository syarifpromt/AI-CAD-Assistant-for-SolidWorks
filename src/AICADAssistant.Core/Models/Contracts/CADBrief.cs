using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Models.Contracts
{
    public enum ComponentType
    {
        Prismatic = 0,
        Rotational = 1,
        SheetMetal = 2,
        Fastener = 3,
        Bracket = 4,
        Shaft = 5,
        Enclosure = 6,
        WrenchTool = 7,
        Hemisphere = 8,
        Custom = 9
    }

    public class CADBrief
    {
        public string IntentId { get; set; }
        public string UserPrompt { get; set; }
        public ComponentType ComponentType { get; set; }
        public double[] TargetDimensionsMm { get; set; }
        public string Units { get; set; }
        public List<string> RequiredFeatures { get; set; }
        public double TargetVolumeMm3 { get; set; }
        public double ToleranceMm { get; set; }
        public bool IsRevision { get; set; }
        public string TargetParentFeature { get; set; }

        public CADBrief()
        {
            IntentId = Guid.NewGuid().ToString("N");
            Units = "mm";
            ToleranceMm = 0.1;
            TargetDimensionsMm = new double[3];
            RequiredFeatures = new List<string>();
        }

        public override string ToString()
        {
            return string.Format("CADBrief[{0}] Type={1} Revision={2} Prompt='{3}'",
                IntentId ?? "?",
                ComponentType,
                IsRevision,
                UserPrompt ?? string.Empty);
        }
    }
}

