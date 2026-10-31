using System;

namespace AICADAssistant.Core.Features
{
    public class ExtrudeCutParams
    {
        public bool ThroughAll { get; set; }
        public bool BothDirections { get; set; }
        public double DepthMm { get; set; }
        public bool ReverseDirection { get; set; }
        public bool FlipSideToCut { get; set; }

        public ExtrudeCutParams()
        {
            ThroughAll = true;
            BothDirections = true;
            DepthMm = 50.0;
            ReverseDirection = false;
            FlipSideToCut = false; // Always false for cutting hole / slot inside profile!
        }
    }

    public class HoleParams
    {
        public double DiameterMm { get; set; }
        public double DepthMm { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public bool ThroughAll { get; set; }

        public HoleParams()
        {
            DiameterMm = 10.0;
            DepthMm = 50.0;
            CenterX = 0.0;
            CenterY = 0.0;
            ThroughAll = true;
        }
    }

    public class BossParams
    {
        public double DepthMm { get; set; }
        public bool Merge { get; set; }
        public bool ReverseDirection { get; set; }
        public double DraftAngleDeg { get; set; }

        public BossParams()
        {
            DepthMm = 10.0;
            Merge = true;
            ReverseDirection = false;
            DraftAngleDeg = 0.0;
        }
    }

    public class RevolveParams
    {
        public double AngleDeg { get; set; }
        public bool ReverseDirection { get; set; }
        public bool IsHemisphere { get; set; }
        public double DiameterMm { get; set; }

        public RevolveParams()
        {
            AngleDeg = 360.0;
            ReverseDirection = false;
            IsHemisphere = false;
            DiameterMm = 50.0;
        }
    }
}

