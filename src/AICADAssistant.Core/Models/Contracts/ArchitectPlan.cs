using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Models.Contracts
{
    public class ArchitectPlan
    {
        public string PlanId { get; set; }
        public CADBrief Brief { get; set; }
        public string DatumPlane { get; set; }
        public string OriginStrategy { get; set; }
        public string Symmetry { get; set; }
        public List<string> SketchPlanes { get; set; }
        public List<string> FeatureOrder { get; set; }
        public double EstimatedVolumeMm3 { get; set; }
        public double[] BoundingBoxMm { get; set; }
        public string Rationale { get; set; }

        public ArchitectPlan()
        {
            PlanId = Guid.NewGuid().ToString("N");
            DatumPlane = "Top";
            OriginStrategy = "Center";
            Symmetry = "None";
            SketchPlanes = new List<string>();
            FeatureOrder = new List<string>();
            BoundingBoxMm = new double[3];
        }

        public override string ToString()
        {
            return string.Format("ArchitectPlan[{0}] Plane={1} Origin={2} Features={3}",
                PlanId ?? "?",
                DatumPlane ?? "-",
                OriginStrategy ?? "-",
                FeatureOrder != null ? FeatureOrder.Count : 0);
        }
    }
}

