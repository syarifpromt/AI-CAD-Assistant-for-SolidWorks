using System;
using System.IO;
using System.Web.Script.Serialization;

namespace AICADAssistant.Core.Models
{
    /// <summary>
    /// Application settings loaded from and saved to a JSON file in %APPDATA%.
    /// </summary>
    public class AppSettings
    {
        private static readonly string SettingsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AICADAssistant");

        private static readonly string SettingsFilePath =
            Path.Combine(SettingsDirectory, "settings.json");

        // ── Properties ──────────────────────────────────────────────────────────

        public string GeminiModel { get; set; }
        public string DefaultUnit { get; set; }
        public string OutputDirectory { get; set; }
        public int MaxRetryAttempts { get; set; }
        public int RequestTimeoutSeconds { get; set; }
        public bool ShowConfirmationDialogs { get; set; }
        public string LogLevel { get; set; }

        public AppSettings()
        {
            GeminiModel = "gemini-1.5-pro";
            DefaultUnit = "mm";
            OutputDirectory = "";
            MaxRetryAttempts = 2;
            RequestTimeoutSeconds = 90;
            ShowConfirmationDialogs = true;
            LogLevel = "Info";
        }

        // ── Static helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Loads settings from %APPDATA%\AICADAssistant\settings.json.
        /// Returns a default instance when the file does not exist or cannot be parsed.
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                    return new AppSettings();

                string json = File.ReadAllText(SettingsFilePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new AppSettings();

                var serializer = new JavaScriptSerializer();
                var loaded = serializer.Deserialize<AppSettings>(json);
                return loaded ?? new AppSettings();
            }
            catch
            {
                // Return defaults rather than crashing on corrupt settings.
                return new AppSettings();
            }
        }

        /// <summary>
        /// Saves the current settings to %APPDATA%\AICADAssistant\settings.json.
        /// </summary>
        public void Save()
        {
            try
            {
                EnsureDirectoryExists(SettingsDirectory);

                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Best-effort; caller can surface errors via logging service.
            }
        }

        /// <summary>
        /// Returns the default output directory: Documents\AICADAssistant.
        /// </summary>
        public static string GetDefaultOutputDirectory()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "AICADAssistant");
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}
