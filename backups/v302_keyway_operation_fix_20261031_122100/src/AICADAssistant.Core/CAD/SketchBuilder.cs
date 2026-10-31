using System;
using System.Collections.Generic;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Core.CAD
{
    /// <summary>
    /// Helper to organize sketch profile operations before dispatching to SOLIDWORKS COM.
    /// </summary>
    public class SketchBuilder
    {
        public string SketchId { get; set; }
        public string PlaneName { get; set; }
        public List<CadOperation> Entities { get; set; }

        public SketchBuilder(string sketchId, string planeName = "Front")
        {
            SketchId = sketchId;
            PlaneName = string.IsNullOrWhiteSpace(planeName) ? "Front" : planeName;
            Entities = new List<CadOperation>();
        }

        public void AddEntity(CadOperation op)
        {
            if (op != null)
            {
                Entities.Add(op);
            }
        }
    }
}
