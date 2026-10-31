using System;

namespace AICADAssistant.Core.Models.Contracts
{
    public enum SelectorKind
    {
        PlanarFaceTop = 0,
        PlanarFaceBottom = 1,
        PlanarFaceLargest = 2,
        PlanarFaceByNormal = 3,
        CylindricalFace = 4,
        EdgeCircular = 5,
        EdgeByFeature = 6,
        PersistentId = 7,
        ExplicitName = 8
    }

    public class GeometryReference
    {
        public SelectorKind Kind { get; set; }
        public string FeatureName { get; set; }
        public double[] NormalVector { get; set; }
        public double OffsetDistance { get; set; }
        public byte[] PersistentId { get; set; }
        public int FaceIndex { get; set; }
        public int EdgeIndex { get; set; }
        public string ExplicitName { get; set; }

        public GeometryReference()
        {
            Kind = SelectorKind.PlanarFaceTop;
            NormalVector = new double[] { 0, 0, 1 };
        }

        public static GeometryReference TopFace()
        {
            return new GeometryReference { Kind = SelectorKind.PlanarFaceTop, NormalVector = new double[] { 0, 0, 1 } };
        }

        public static GeometryReference BottomFace()
        {
            return new GeometryReference { Kind = SelectorKind.PlanarFaceBottom, NormalVector = new double[] { 0, 0, -1 } };
        }

        public static GeometryReference LargestFace()
        {
            return new GeometryReference { Kind = SelectorKind.PlanarFaceLargest };
        }

        public static GeometryReference Named(string name)
        {
            return new GeometryReference { Kind = SelectorKind.ExplicitName, ExplicitName = name };
        }

        public static GeometryReference FeatureFace(string featureName, int faceIndex = 0)
        {
            return new GeometryReference
            {
                Kind = SelectorKind.PlanarFaceByNormal,
                FeatureName = featureName,
                FaceIndex = faceIndex
            };
        }

        public override string ToString()
        {
            return string.Format("GeoRef[{0}] Feature={1} Name={2}",
                Kind,
                FeatureName ?? "-",
                ExplicitName ?? "-");
        }
    }
}

