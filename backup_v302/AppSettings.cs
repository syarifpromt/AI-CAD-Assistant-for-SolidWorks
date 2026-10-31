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
        public const string DefaultGeminiModel = "gemini-3.5-flash-lite";
        public const string FallbackGeminiModel = "gemini-3.8-flash";

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
            GeminiModel = DefaultGeminiModel;
            DefaultUnit = "mm";
            OutputDirectory = "";
            MaxRetryAttempts = 2;
            RequestTimeoutSeconds = 120;
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
                if (loaded == null)
                    return new AppSettings();

                // If loaded model is empty or obsolete, default to verified model
                if (string.IsNullOrWhiteSpace(loaded.GeminiModel) ||
                    loaded.GeminiModel == "gemini-1.5-pro" ||
                    loaded.GeminiModel == "gemini-1.5-flash" ||
                    loaded.GeminiModel == "gemini-2.0-flash")
                {
                    loaded.GeminiModel = DefaultGeminiModel;
                }

                // Sanitize and clamp timeout and retry limits to prevent runaway values (e.g. 9000 seconds or 10 retries)
                bool needsResave = false;
                if (loaded.RequestTimeoutSeconds < 30 || loaded.RequestTimeoutSeconds > 300)
                {
                    loaded.RequestTimeoutSeconds = 120;
                    needsResave = true;
                }
                if (loaded.MaxRetryAttempts < 0 || loaded.MaxRetryAttempts > 3)
                {
                    loaded.MaxRetryAttempts = 2;
                    needsResave = true;
                }

                if (needsResave)
                {
                    loaded.Save();
                }

                return loaded;
            }
            catch
            {
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
