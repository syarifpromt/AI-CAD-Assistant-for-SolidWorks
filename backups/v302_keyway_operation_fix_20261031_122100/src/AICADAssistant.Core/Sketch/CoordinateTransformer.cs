using System;

namespace AICADAssistant.Core.Sketch
{
    public class CoordinateTransformer
    {
        public double[] Origin { get; private set; }
        public double[] Normal { get; private set; }
        public double[] UAxis { get; private set; }
        public double[] VAxis { get; private set; }

        public CoordinateTransformer(double[] origin = null, double[] normal = null)
        {
            Origin = origin != null && origin.Length >= 3 ? (double[])origin.Clone() : new double[] { 0, 0, 0 };
            Normal = normal != null && normal.Length >= 3 ? Normalize((double[])normal.Clone()) : new double[] { 0, 0, 1 };

            ComputeOrthonormalBasis();
        }

        private void ComputeOrthonormalBasis()
        {
            // Pick an arbitrary vector not parallel to Normal
            double[] arbitrary = Math.Abs(Normal[0]) < 0.9 ? new double[] { 1, 0, 0 } : new double[] { 0, 1, 0 };

            // U = Arbitrary x Normal (normalized)
            UAxis = Normalize(CrossProduct(arbitrary, Normal));

            // V = Normal x U (normalized)
            VAxis = Normalize(CrossProduct(Normal, UAxis));
        }

        public double[] Transform2DTo3D(double u, double v)
        {
            return new double[]
            {
                Origin[0] + u * UAxis[0] + v * VAxis[0],
                Origin[1] + u * UAxis[1] + v * VAxis[1],
                Origin[2] + u * UAxis[2] + v * VAxis[2]
            };
        }

        public double[] Transform3DTo2D(double x, double y, double z)
        {
            double dx = x - Origin[0];
            double dy = y - Origin[1];
            double dz = z - Origin[2];

            double u = dx * UAxis[0] + dy * UAxis[1] + dz * UAxis[2];
            double v = dx * VAxis[0] + dy * VAxis[1] + dz * VAxis[2];

            return new double[] { u, v };
        }

        public static double[] CrossProduct(double[] a, double[] b)
        {
            return new double[]
            {
                a[1] * b[2] - a[2] * b[1],
                a[2] * b[0] - a[0] * b[2],
                a[0] * b[1] - a[1] * b[0]
            };
        }

        public static double[] Normalize(double[] v)
        {
            double len = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (len < 1e-12)
                return new double[] { 0, 0, 1 };

            return new double[] { v[0] / len, v[1] / len, v[2] / len };
        }
    }
}

