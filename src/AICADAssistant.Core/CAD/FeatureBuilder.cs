using System;
using System.Collections.Generic;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Core.CAD
{
    /// <summary>
    /// Helper to structure 3D feature definitions (Extrude, Cut, Revolve, Fillet, Hole, Pattern).
    /// </summary>
    public class FeatureBuilder
    {
        public CadOperation Operation { get; set; }

        public FeatureBuilder(CadOperation op)
        {
            if (op == null)
                throw new ArgumentNullException("op");
            Operation = op;
        }

        public double GetDepthMeters(string unit)
        {
            double depth;
            if (Operation.TryGetDouble("depth", out depth))
            {
                return UnitConverter.ConvertToInternal(depth, unit);
            }
            return 0.01; // default 10mm
        }

        public double GetRadiusMeters(string unit)
        {
            double radius;
            if (Operation.TryGetDouble("radius", out radius))
            {
                return UnitConverter.ConvertToInternal(radius, unit);
            }
            return 0.002; // default 2mm
        }

        public double GetDiameterMeters(string unit)
        {
            double diam;
            if (Operation.TryGetDouble("diameter", out diam))
            {
                return UnitConverter.ConvertToInternal(diam, unit);
            }
            double rad;
            if (Operation.TryGetDouble("radius", out rad))
            {
                return UnitConverter.ConvertToInternal(rad * 2.0, unit);
            }
            return 0.005; // default 5mm
        }
    }
}
