using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Sketch
{
    public class ProfileSegment
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public bool IsArc { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Radius { get; set; }

        public double Length
        {
            get
            {
                double dx = X2 - X1;
                double dy = Y2 - Y1;
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }
    }

    public class ProfileValidator
    {
        private const double Epsilon = 1e-4;

        public static bool ValidateSegments(List<ProfileSegment> segments, out double area, out string error)
        {
            area = 0;
            error = null;

            if (segments == null || segments.Count == 0)
            {
                error = "Daftar segmen profil kosong";
                return false;
            }

            // 1. Check zero length segments
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].Length < 1e-5)
                {
                    error = string.Format("Segmen {0} memiliki panjang mendekati nol", i + 1);
                    return false;
                }
            }

            // 2. Check chain connectivity
            for (int i = 0; i < segments.Count - 1; i++)
            {
                double dx = segments[i].X2 - segments[i + 1].X1;
                double dy = segments[i].Y2 - segments[i + 1].Y1;
                double gap = Math.Sqrt(dx * dx + dy * dy);
                if (gap > Epsilon)
                {
                    error = string.Format("Diskontinuitas antara segmen {0} dan {1} (celah={2:F4} mm)", i + 1, i + 2, gap);
                    return false;
                }
            }

            // 3. Check closure between last and first
            var first = segments[0];
            var last = segments[segments.Count - 1];
            double closingDx = last.X2 - first.X1;
            double closingDy = last.Y2 - first.Y1;
            double closingGap = Math.Sqrt(closingDx * closingDx + closingDy * closingDy);
            if (closingGap > Epsilon)
            {
                error = string.Format("Profil tidak tertutup rapat: celah antara titik akhir dan awal adalah {0:F4} mm", closingGap);
                return false;
            }

            // 4. Calculate polygonal area via Shoelace formula
            double sum = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                sum += (segments[i].X1 * segments[i].Y2) - (segments[i].X2 * segments[i].Y1);
            }
            area = Math.Abs(sum) * 0.5;

            if (area <= 1e-5)
            {
                error = "Profil tertutup memiliki luas area nol atau terdegradasi";
                return false;
            }

            return true;
        }

        public static bool ValidateCircle(double diameter, out double area, out string error)
        {
            area = 0;
            error = null;
            if (diameter <= 0.0)
            {
                error = "Diameter lingkaran harus bernilai positif lebih besar dari nol";
                return false;
            }

            double r = diameter * 0.5;
            area = Math.PI * r * r;
            return true;
        }

        public static bool ValidateRectangle(double width, double height, out double area, out string error)
        {
            area = 0;
            error = null;
            if (width <= 0.0 || height <= 0.0)
            {
                error = "Lebar dan tinggi persegi panjang harus bernilai positif lebih besar dari nol";
                return false;
            }

            area = width * height;
            return true;
        }
    }
}

