using System;
using System.IO;
using System.Drawing;
using System.Text.RegularExpressions;

namespace AICADAssistant.Core.Services
{
    public class FileValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }

        public FileValidationResult(bool isValid, string errorMessage = null)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }
    }

    public class ImagePreviewInfo
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public long FileSizeBytes { get; set; }
        public string Extension { get; set; }
        public int PageCount { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsValid { get; set; }
        public string ValidationError { get; set; }

        public ImagePreviewInfo()
        {
            PageCount = 1;
        }

        public string DisplaySize
        {
            get
            {
                if (FileSizeBytes < 1024)
                    return string.Format("{0} B", FileSizeBytes);
                if (FileSizeBytes < 1024 * 1024)
                    return string.Format("{0:F1} KB", FileSizeBytes / 1024.0);
                return string.Format("{0:F2} MB", FileSizeBytes / (1024.0 * 1024.0));
            }
        }
    }

    public static class ImageService
    {
        public const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB

        public static FileValidationResult ValidateFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return new FileValidationResult(false, "Jalur berkas tidak boleh kosong.");

            if (!File.Exists(filePath))
                return new FileValidationResult(false, "Berkas tidak ditemukan: " + filePath);

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".webp" && ext != ".pdf")
            {
                return new FileValidationResult(false, "Format berkas tidak didukung ('" + ext + "'). Gunakan format JPG, PNG, WebP, atau PDF.");
            }

            var info = new FileInfo(filePath);
            if (info.Length == 0)
                return new FileValidationResult(false, "Berkas kosong (0 byte).");

            if (info.Length > MaxFileSizeBytes)
                return new FileValidationResult(false, string.Format("Ukuran berkas ({0:F1} MB) melebihi batas maksimal 20 MB.", info.Length / (1024.0 * 1024.0)));

            return new FileValidationResult(true, null);
        }

        public static string GetMimeType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".png":
                    return "image/png";
                case ".webp":
                    return "image/webp";
                case ".pdf":
                    return "application/pdf";
                default:
                    return "application/octet-stream";
            }
        }

        public static string ToBase64(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            return Convert.ToBase64String(bytes);
        }

        public static ImagePreviewInfo GetImagePreviewInfo(string filePath)
        {
            var valResult = ValidateFile(filePath);
            var fileInfo = new FileInfo(filePath);
            var result = new ImagePreviewInfo
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                Extension = Path.GetExtension(filePath).ToLowerInvariant(),
                IsValid = valResult.IsValid,
                ValidationError = valResult.ErrorMessage
            };

            if (!valResult.IsValid) return result;

            try
            {
                if (result.Extension == ".pdf")
                {
                    result.PageCount = EstimatePdfPages(filePath);
                }
                else if (result.Extension != ".webp")
                {
                    using (var img = Image.FromFile(filePath))
                    {
                        result.Width = img.Width;
                        result.Height = img.Height;
                    }
                }
            }
            catch
            {
                // Silently ignore image header read issues
            }

            return result;
        }

        private static int EstimatePdfPages(string filePath)
        {
            try
            {
                byte[] buffer = new byte[Math.Min(128 * 1024, new FileInfo(filePath).Length)];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Read(buffer, 0, buffer.Length);
                }
                string content = System.Text.Encoding.ASCII.GetString(buffer);
                var matches = Regex.Matches(content, @"/Type\s*/Page[^s]");
                return Math.Max(1, matches.Count);
            }
            catch
            {
                return 1;
            }
        }
    }
}
