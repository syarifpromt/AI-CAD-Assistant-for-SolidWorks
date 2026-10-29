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
        public bool IsModelNotFound { get; private set; }
        public string ServerStatus { get; private set; }

        public GeminiException(
            string message,
            int? statusCode = null,
            bool isRateLimit = false,
            bool isTimeout = false,
            bool isAuthError = false,
            bool isNetworkError = false,
            bool isModelNotFound = false,
            string serverStatus = null,
            Exception inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            IsRateLimit = isRateLimit;
            IsTimeout = isTimeout;
            IsAuthError = isAuthError;
            IsNetworkError = isNetworkError;
            IsModelNotFound = isModelNotFound;
            ServerStatus = serverStatus;
        }

        public static GeminiException RateLimitException(string serverDetails = null)
        {
            string detail = !string.IsNullOrWhiteSpace(serverDetails) ? " (" + serverDetails + ")" : "";
            return new GeminiException(
                "Batas kuota Gemini API (Rate Limit / Quota Exceeded) telah tercapai untuk akun Anda. Harap tunggu beberapa saat atau periksa kuota Anda di Google AI Studio." + detail,
                429,
                isRateLimit: true,
                serverStatus: "RESOURCE_EXHAUSTED");
        }

        public static GeminiException TimeoutException(int timeoutSeconds = 90)
        {
            return new GeminiException(
                string.Format("Permintaan ke Gemini API melebihi batas waktu ({0} detik). Periksa koneksi internet Anda atau perpanjang batas waktu di Pengaturan.", timeoutSeconds),
                null,
                isTimeout: true,
                serverStatus: "DEADLINE_EXCEEDED");
        }

        public static GeminiException AuthException(string serverDetails = null)
        {
            string detail = !string.IsNullOrWhiteSpace(serverDetails) ? " (" + serverDetails + ")" : "";
            return new GeminiException(
                "Autentikasi Gemini API gagal (API Key tidak valid atau tidak memiliki izin akses). Silakan periksa pengaturan API Key." + detail,
                401,
                isAuthError: true,
                serverStatus: "UNAUTHENTICATED");
        }

        public static GeminiException ModelNotFoundException(string modelName, string serverDetails = null)
        {
            string detail = !string.IsNullOrWhiteSpace(serverDetails) ? " Detail server: " + serverDetails : "";
            return new GeminiException(
                string.Format("Model Gemini '{0}' tidak ditemukan atau tidak didukung untuk generateContent. Silakan pilih atau ketik nama model yang valid (misal: 'gemini-3.8-flash' atau 'gemini-3.5-flash-lite') di Pengaturan.{1}", modelName, detail),
                404,
                isModelNotFound: true,
                serverStatus: "NOT_FOUND");
        }

        public static GeminiException NetworkException(string details)
        {
            return new GeminiException(
                "Gagal menghubungi server Gemini API: " + details,
                null,
                isNetworkError: true);
        }

        public static GeminiException InvalidResponseException(string details)
        {
            return new GeminiException("Respons dari Gemini API tidak sesuai format yang diharapkan: " + details);
        }
    }
}
