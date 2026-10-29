using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Validation
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> MissingDimensions { get; set; }
        public List<string> AmbiguousItems { get; set; }

        public ValidationResult()
        {
            IsValid = true;
            Errors = new List<string>();
            Warnings = new List<string>();
            MissingDimensions = new List<string>();
            AmbiguousItems = new List<string>();
        }

        public void AddError(string error)
        {
            IsValid = false;
            Errors.Add(error);
        }

        public void AddWarning(string warning)
        {
            Warnings.Add(warning);
        }

        public void AddMissingDimension(string dimension)
        {
            IsValid = false;
            MissingDimensions.Add(dimension);
            Errors.Add("Dimensi wajib tidak ditemukan: " + dimension);
        }

        public void AddAmbiguity(string item)
        {
            AmbiguousItems.Add(item);
            Warnings.Add("Informasi ambigu: " + item);
        }

        public void Merge(ValidationResult other)
        {
            if (other == null) return;
            if (!other.IsValid) IsValid = false;
            Errors.AddRange(other.Errors);
            Warnings.AddRange(other.Warnings);
            MissingDimensions.AddRange(other.MissingDimensions);
            AmbiguousItems.AddRange(other.AmbiguousItems);
        }

        public static ValidationResult Valid()
        {
            return new ValidationResult { IsValid = true };
        }

        public static ValidationResult Invalid(string error)
        {
            var res = new ValidationResult { IsValid = false };
            res.Errors.Add(error);
            return res;
        }
    }
}
