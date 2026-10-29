using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.CAD
{
    /// <summary>
    /// Centralized unit converter for AI CAD Assistant.
    /// SOLIDWORKS uses meters (m) internally for length and radians for angles.
    /// </summary>
    public static class UnitConverter
    {
        public const double MM_TO_M = 0.001;
        public const double CM_TO_M = 0.01;
        public const double INCH_TO_M = 0.0254;
        public const double FT_TO_M = 0.3048;
        public const double M_TO_MM = 1000.0;

        private static readonly HashSet<string> ValidUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mm", "millimeter", "millimeters",
            "cm", "centimeter", "centimeters",
            "m", "meter", "meters",
            "in", "inch", "inches",
            "ft", "foot", "feet"
        };

        public static bool IsValidUnit(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit))
                return false;
            return ValidUnits.Contains(unit.Trim());
        }

        public static string ParseUnit(string unitString)
        {
            if (string.IsNullOrWhiteSpace(unitString))
                throw new ArgumentException("Unit string cannot be empty", "unitString");

            string lower = unitString.Trim().ToLowerInvariant();
            switch (lower)
            {
                case "mm":
                case "millimeter":
                case "millimeters":
                    return "mm";

                case "cm":
                case "centimeter":
                case "centimeters":
                    return "cm";

                case "m":
                case "meter":
                case "meters":
                    return "m";

                case "in":
                case "inch":
                case "inches":
                    return "in";

                case "ft":
                case "foot":
                case "feet":
                    return "ft";

                default:
                    throw new ArgumentException("Unit tidak dikenali: " + unitString, "unitString");
            }
        }

        public static double FromMillimeters(double mm)
        {
            return mm * MM_TO_M;
        }

        public static double ToMillimeters(double meters)
        {
            return meters * M_TO_MM;
        }

        public static double ConvertToInternal(double value, string sourceUnit)
        {
            string normalized = ParseUnit(sourceUnit);
            switch (normalized)
            {
                case "mm":
                    return value * MM_TO_M;
                case "cm":
                    return value * CM_TO_M;
                case "m":
                    return value;
                case "in":
                    return value * INCH_TO_M;
                case "ft":
                    return value * FT_TO_M;
                default:
                    throw new ArgumentException("Unit tidak didukung: " + sourceUnit, "sourceUnit");
            }
        }

        public static double ConvertFromInternal(double valueInMeters, string targetUnit)
        {
            string normalized = ParseUnit(targetUnit);
            switch (normalized)
            {
                case "mm":
                    return valueInMeters * M_TO_MM;
                case "cm":
                    return valueInMeters / CM_TO_M;
                case "m":
                    return valueInMeters;
                case "in":
                    return valueInMeters / INCH_TO_M;
                case "ft":
                    return valueInMeters / FT_TO_M;
                default:
                    throw new ArgumentException("Unit tidak didukung: " + targetUnit, "targetUnit");
            }
        }
    }
}
