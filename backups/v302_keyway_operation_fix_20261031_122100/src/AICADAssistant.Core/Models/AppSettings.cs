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

        public void Sanitize()
        {
            if (string.IsNullOrWhiteSpace(GeminiModel) ||
                GeminiModel == "gemini-1.5-pro" ||
                GeminiModel == "gemini-1.5-flash" ||
                GeminiModel == "gemini-2.0-flash")
            {
                GeminiModel = DefaultGeminiModel;
            }

            if (RequestTimeoutSeconds < 30 || RequestTimeoutSeconds > 300)
            {
                RequestTimeoutSeconds = 120;
            }

            if (MaxRetryAttempts < 0 || MaxRetryAttempts > 3)
            {
                MaxRetryAttempts = 2;
            }
        }

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

                int prevTimeout = loaded.RequestTimeoutSeconds;
                int prevRetries = loaded.MaxRetryAttempts;
                string prevModel = loaded.GeminiModel;

                loaded.Sanitize();

                bool needsResave = (prevTimeout != loaded.RequestTimeoutSeconds ||
                                    prevRetries != loaded.MaxRetryAttempts ||
                                    !string.Equals(prevModel, loaded.GeminiModel, StringComparison.Ordinal));

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
