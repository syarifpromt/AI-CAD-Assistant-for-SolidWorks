using System;
using System.IO;

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

        public void Log(string level, string message, Exception ex = null)
        {
            try
            {
                string fileName = string.Format("aicad_{0:yyyyMMdd}.log", DateTime.Now);
                string filePath = Path.Combine(_logDirectory, fileName);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                string logLine = string.Format("[{0}] [{1}] {2}", timestamp, level.ToUpperInvariant(), message);
                if (ex != null)
                {
                    logLine += Environment.NewLine + string.Format("   Exception: {0}: {1}", ex.GetType().Name, ex.Message);
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

        public void Error(string message, Exception ex = null)
        {
            Log("ERROR", message, ex);
        }

        public void Debug(string message)
        {
            Log("DEBUG", message);
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
