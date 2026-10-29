using System;

namespace AICADAssistant.Core.Gemini
{
    public class GeminiException : Exception
    {
        public int? StatusCode { get; private set; }
        public bool IsRateLimit { get; private set; }
        public bool IsTimeout { get; private set; }
        public bool IsAuthError { get; private set; }
        public bool IsNetworkError { get; private set; }

        public GeminiException(string message, int? statusCode = null, bool isRateLimit = false, bool isTimeout = false, bool isAuthError = false, bool isNetworkError = false, Exception inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            IsRateLimit = isRateLimit;
            IsTimeout = isTimeout;
            IsAuthError = isAuthError;
            IsNetworkError = isNetworkError;
        }

        public static GeminiException RateLimitException()
        {
            return new GeminiException("Batas kuota Gemini API (Rate Limit / Quota Exceeded) telah tercapai. Harap tunggu beberapa saat sebelum mencoba kembali.", 429, isRateLimit: true);
        }

        public static GeminiException TimeoutException()
        {
            return new GeminiException("Permintaan ke Gemini API melebihi batas waktu (Timeout). Periksa koneksi internet Anda.", null, isTimeout: true);
        }

        public static GeminiException AuthException()
        {
            return new GeminiException("Autentikasi Gemini API gagal (API Key tidak valid atau tidak memiliki izin akses). Silakan periksa pengaturan API Key.", 401, isAuthError: true);
        }

        public static GeminiException NetworkException(string details)
        {
            return new GeminiException("Gagal menghubungi server Gemini API: " + details, null, isNetworkError: true);
        }

        public static GeminiException InvalidResponseException(string details)
        {
            return new GeminiException("Respons dari Gemini API tidak sesuai format yang diharapkan: " + details);
        }
    }
}
