using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Models.Contracts
{
    public class CADProgram
    {
        public string ProgramId { get; set; }
        public CADBrief Brief { get; set; }
        public ArchitectPlan Plan { get; set; }
        public List<CadOperation> Operations { get; set; }
        public string Units { get; set; }
        public string Notes { get; set; }

        public CADProgram()
        {
            ProgramId = Guid.NewGuid().ToString("N");
            Operations = new List<CadOperation>();
            Units = "mm";
        }

        public CadSpec ToCadSpec()
        {
            var spec = new CadSpec
            {
                RequestId = ProgramId,
                Units = Units,
                Notes = Notes,
                Operations = Operations != null ? new List<CadOperation>(Operations) : new List<CadOperation>()
            };

            if (Brief != null && Brief.TargetDimensionsMm != null && Brief.TargetDimensionsMm.Length >= 3)
            {
                spec.ExplicitDimensions["Length"] = Brief.TargetDimensionsMm[0];
                spec.ExplicitDimensions["Width"] = Brief.TargetDimensionsMm[1];
                spec.ExplicitDimensions["Height"] = Brief.TargetDimensionsMm[2];
            }

            return spec;
        }

        public static CADProgram FromCadSpec(CadSpec spec)
        {
            if (spec == null) return null;

            var prog = new CADProgram
            {
                ProgramId = spec.RequestId ?? Guid.NewGuid().ToString("N"),
                Units = spec.Units ?? "mm",
                Notes = spec.Notes,
                Operations = spec.Operations != null ? new List<CadOperation>(spec.Operations) : new List<CadOperation>()
            };

            prog.Brief = new CADBrief
            {
                IntentId = prog.ProgramId,
                Units = prog.Units
            };

            return prog;
        }

        public override string ToString()
        {
            return string.Format("CADProgram[{0}] Operations={1} Units={2}",
                ProgramId ?? "?",
                Operations != null ? Operations.Count : 0,
                Units ?? "mm");
        }
    }
}

