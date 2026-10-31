using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AICADAssistant.Core.Models
{
    public class ModelBoundingBox
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }

        public double LengthX => Math.Max(0.0, MaxX - MinX);
        public double LengthY => Math.Max(0.0, MaxY - MinY);
        public double LengthZ => Math.Max(0.0, MaxZ - MinZ);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[{0:F2}, {1:F2}, {2:F2}] to [{3:F2}, {4:F2}, {5:F2}] mm (Size: {6:F2} x {7:F2} x {8:F2} mm)",
                MinX, MinY, MinZ, MaxX, MaxY, MaxZ, LengthX, LengthY, LengthZ);
        }
    }

    public class ModelFeatureInfo
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public bool IsSuppressed { get; set; }
        public string Description { get; set; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Description)
                ? $"{Name} ({TypeName})"
                : $"{Name} ({TypeName}: {Description})";
        }
    }

    public class ModelContextSnapshot
    {
        public string DocumentId { get; set; }
        public string DocumentTitle { get; set; }
        public string DocumentPath { get; set; }
        public int BodyCount { get; set; }
        public int SolidBodyCount { get; set; }
        public int SurfaceBodyCount { get; set; }
        public double VolumeMm3 { get; set; }
        public ModelBoundingBox BoundingBox { get; set; }
        public List<ModelFeatureInfo> FeatureList { get; set; }
        public List<string> SketchList { get; set; }
        public List<string> RevolveFeatures { get; set; }
        public List<string> CutFeatures { get; set; }
        public List<string> BossFeatures { get; set; }
        public bool IsRevolvedBaseShaft { get; set; }
        public bool HasAxialHole { get; set; }
        public DateTime Timestamp { get; set; }

        public ModelContextSnapshot()
        {
            FeatureList = new List<ModelFeatureInfo>();
            SketchList = new List<string>();
            RevolveFeatures = new List<string>();
            CutFeatures = new List<string>();
            BossFeatures = new List<string>();
            BoundingBox = new ModelBoundingBox();
            Timestamp = DateTime.UtcNow;
        }

        public string ToPromptContextString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Model aktif saat ini pada SOLIDWORKS:");
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "- Dokumen: {0} ({1} solid body, volume: {2:N0} mm³)\n",
                DocumentTitle ?? DocumentId ?? "ActivePart",
                SolidBodyCount > 0 ? SolidBodyCount : BodyCount,
                VolumeMm3);

            if (BoundingBox != null && (BoundingBox.LengthX > 0 || BoundingBox.LengthY > 0 || BoundingBox.LengthZ > 0))
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "- Bounding Box: {0}\n", BoundingBox.ToString());
            }

            if (FeatureList.Count > 0)
            {
                sb.AppendLine("- Fitur utama yang SUDAH ADA pada pohon fitur:");
                foreach (var feat in FeatureList)
                {
                    if (feat.IsSuppressed) continue;
                    sb.AppendLine($"  * {feat}");
                }
            }

            if (IsRevolvedBaseShaft)
            {
                sb.AppendLine("- Catatan Model: Poros bertingkat dasar (revolved shaft) sudah ada pada model.");
            }
            if (HasAxialHole)
            {
                sb.AppendLine("- Catatan Model: Lubang aksial sudah dibuat pada model.");
            }

            sb.AppendLine("- ATURAN REVISI KRUSIAL:");
            sb.AppendLine("  1. JANGAN membuat ulang fitur-fitur di atas!");
            sb.AppendLine("  2. JANGAN memasukkan operasi NewPart karena model solid sudah aktif!");
            sb.AppendLine("  3. Tambahkan HANYA fitur revisi yang diminta (misal: penambahan alur pasak/keyway, chamfer, fillet, dsb).");
            sb.AppendLine("  4. Berikan Id yang unik dan jelas untuk setiap operasi baru.");

            return sb.ToString();
        }
    }
}

