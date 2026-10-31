using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AICADAssistant.Core.Services
{
    public class LoggingService
    {
        private static readonly object LockObj = new object();
        private static LoggingService _instance;
        private readonly string _logDirectory;

        public static LoggingService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (LockObj)
                    {
                        if (_instance == null)
                        {
                            _instance = new LoggingService();
                        }
                    }
                }
                return _instance;
            }
        }

        public LoggingService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _logDirectory = Path.Combine(appData, "AICADAssistant", "logs");
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
                CleanOldLogs(7);
            }
            catch
            {
                // Fallback if directory creation fails
            }
        }

        public static string RedactApiKey(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Redact URL query parameter e.g. key=AIzaSy... or ?key=... or &key=...
            string sanitized = Regex.Replace(input, @"([?&]key=)[^&""'\s]+", "$1[REDACTED]");

            // Redact JSON/header key patterns e.g. "apiKey": "..." or api_key=...
            sanitized = Regex.Replace(sanitized, @"(api_?key\s*[:=]\s*[""']?)[^""'\s,}]+", "$1[REDACTED]", RegexOptions.IgnoreCase);

            // Redact Google API Key pattern (starts with AIza...)
            sanitized = Regex.Replace(sanitized, @"AIza[A-Za-z0-9_\-]{16,}", "[REDACTED_API_KEY]");

            return sanitized;
        }

        public void Log(string level, string message, Exception ex = null)
        {
            try
            {
                string safeMessage = RedactApiKey(message);
                string fileName = string.Format("aicad_{0:yyyyMMdd}.log", DateTime.Now);
                string filePath = Path.Combine(_logDirectory, fileName);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                string logLine = string.Format("[{0}] [{1}] {2}", timestamp, level.ToUpperInvariant(), safeMessage);
                if (ex != null)
                {
                    string safeExMessage = RedactApiKey(ex.Message);
                    logLine += Environment.NewLine + string.Format("   Exception: {0}: {1}", ex.GetType().Name, safeExMessage);
                }

                lock (LockObj)
                {
                    File.AppendAllText(filePath, logLine + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must not throw exceptions that crash the application
            }
        }

        public void Info(string message)
        {
            Log("INFO", message);
        }

        public void Warning(string message)
        {
            Log("WARN", message);
        }

        public void Warn(string message)
        {
            Warning(message);
        }

        public void Error(string message, Exception ex = null)
        {
            Log("ERROR", message, ex);
        }

        public void Debug(string message)
        {
            Log("DEBUG", message);
        }

        public void LogDiagnostic(AICADAssistant.Core.Models.Contracts.CADDiagnostic diagnostic)
        {
            if (diagnostic == null) return;
            string level = diagnostic.Severity == AICADAssistant.Core.Models.Contracts.DiagnosticSeverity.Error ||
                           diagnostic.Severity == AICADAssistant.Core.Models.Contracts.DiagnosticSeverity.Fatal ? "ERROR" :
                           (diagnostic.Severity == AICADAssistant.Core.Models.Contracts.DiagnosticSeverity.Warning ? "WARN" : "INFO");
            Log(level, string.Format("[DIAGNOSTIC] {0}", diagnostic));
        }

        public void LogPipeline(string stage, string status, string details, long durationMs = -1, Exception ex = null)
        {
            string durStr = durationMs >= 0 ? string.Format(" [{0} ms]", durationMs) : "";
            string msg = string.Format("[PIPELINE] {0} -> {1}{2}: {3}", stage, status, durStr, details);
            Log(status == "FAILED" || status == "ERROR" ? "ERROR" : (status == "WARN" ? "WARN" : "INFO"), msg, ex);

            // Also mirror to unified addin.log in %APPDATA%\AICADAssistant
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string addinLog = Path.Combine(appData, "AICADAssistant", "addin.log");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string line = string.Format("[{0}] {1}{2}", timestamp, RedactApiKey(msg), Environment.NewLine);
                lock (LockObj)
                {
                    File.AppendAllText(addinLog, line);
                }
            }
            catch { }
        }

        private void CleanOldLogs(int daysToKeep)
        {
            try
            {
                var files = Directory.GetFiles(_logDirectory, "aicad_*.log");
                DateTime cutoff = DateTime.Now.AddDays(-daysToKeep);
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoff)
                    {
                        try { fileInfo.Delete(); } catch { }
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
