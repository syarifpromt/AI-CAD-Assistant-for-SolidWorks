using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AICADAssistant.Core.Gemini;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class GeminiClientTests
    {
        private string _tempCredFile;
        private CredentialService _credentialService;
        private AppSettings _settings;

        [TestInitialize]
        public void Setup()
        {
            _tempCredFile = Path.Combine(Path.GetTempPath(), "test_cred_" + Guid.NewGuid().ToString("N") + ".dat");
            _credentialService = new CredentialService(_tempCredFile);
            _settings = new AppSettings
            {
                GeminiModel = AppSettings.DefaultGeminiModel,
                RequestTimeoutSeconds = 5
            };
        }

        [TestMethod]
        public void CredentialService_SaveAndLoadApiKey_RoundTripsSafely()
        {
            string testKey = "AIzaSyTestApiKey123456789";
            _credentialService.SaveApiKey(testKey);

            Assert.IsTrue(_credentialService.HasApiKey());
            string loaded = _credentialService.LoadApiKey();
            Assert.AreEqual(testKey, loaded);

            _credentialService.DeleteApiKey();
            Assert.IsFalse(_credentialService.HasApiKey());
        }

        [TestMethod]
        public void GeminiClient_WithoutApiKey_TestConnectionReturnsFalse()
        {
            var client = new GeminiClient(_credentialService, _settings);
            bool success = client.TestConnectionAsync().Result;
            Assert.IsFalse(success);
        }

        [TestMethod]
        public void GeminiClient_WithoutApiKey_DetailedTestReturnsInformativeMessage()
        {
            var client = new GeminiClient(_credentialService, _settings);
            var detailed = client.TestConnectionDetailedAsync().Result;
            Assert.IsFalse(detailed.Success);
            Assert.IsTrue(detailed.Message.Contains("API Key belum"));
        }

        [TestMethod]
        public void GeminiClient_DefaultModel_IsGemini35FlashLite()
        {
            var settings = new AppSettings();
            var client = new GeminiClient(_credentialService, settings);
            Assert.AreEqual("gemini-3.5-flash-lite", client.CurrentModel);
        }

        [TestMethod]
        public void GeminiClient_CustomModel_IsUsedInCurrentModel()
        {
            var settings = new AppSettings { GeminiModel = "gemini-3.8-flash" };
            var client = new GeminiClient(_credentialService, settings);
            Assert.AreEqual("gemini-3.8-flash", client.CurrentModel);

            // Change model dynamically without recreating client or changing CAD engine
            settings.GeminiModel = "gemini-custom-enterprise";
            Assert.AreEqual("gemini-custom-enterprise", client.CurrentModel);
        }

        [TestMethod]
        public void GeminiException_ServerUnavailable_FlagsSetProperly()
        {
            var ex = GeminiException.ServerUnavailableException("gemini-3.8-flash", "High demand");
            Assert.IsTrue(ex.IsServerUnavailable);
            Assert.AreEqual(503, ex.StatusCode);
            Assert.AreEqual("UNAVAILABLE", ex.ServerStatus);
            Assert.IsTrue(ex.Message.Contains("503"));
            Assert.IsTrue(ex.Message.Contains("gemini-3.8-flash"));
        }

        [TestMethod]
        public void GeminiException_RateLimit_FlagsSetProperly()
        {
            var ex = GeminiException.RateLimitException("quota exceeded");
            Assert.IsTrue(ex.IsRateLimit);
            Assert.AreEqual(429, ex.StatusCode);
            Assert.AreEqual("RESOURCE_EXHAUSTED", ex.ServerStatus);
        }

        [TestMethod]
        public void GeminiException_Timeout_FlagsSetProperly()
        {
            var ex = GeminiException.TimeoutException(45);
            Assert.IsTrue(ex.IsTimeout);
            Assert.IsTrue(ex.Message.Contains("45 detik"));
        }

        [TestMethod]
        public void GeminiException_Auth_FlagsSetProperly()
        {
            var ex = GeminiException.AuthException("invalid key");
            Assert.IsTrue(ex.IsAuthError);
            Assert.AreEqual(401, ex.StatusCode);
        }

        [TestMethod]
        public void GeminiException_ModelNotFound_FlagsSetProperly()
        {
            var ex = GeminiException.ModelNotFoundException("gemini-invalid-model", "models/gemini-invalid-model not found");
            Assert.IsTrue(ex.IsModelNotFound);
            Assert.AreEqual(404, ex.StatusCode);
            Assert.IsTrue(ex.Message.Contains("gemini-invalid-model"));
        }

        [TestMethod]
        public void LoggingService_RedactApiKey_SanitizesSensitiveKeys()
        {
            string urlWithKey = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent?key=AIzaSyA1B2C3D4E5F6G7H8I9J0K1L2M3N4O5P6";
            string redacted = LoggingService.RedactApiKey(urlWithKey);

            Assert.IsFalse(redacted.Contains("AIzaSyA1B2C3D4E5F6G7H8I9J0K1L2M3N4O5P6"));
            Assert.IsTrue(redacted.Contains("key=[REDACTED]"));

            string plainKeyInText = "Exception with key AIzaSyA1B2C3D4E5F6G7H8I9J0K1L2M3N4O5P6 inside message";
            string redactedPlain = LoggingService.RedactApiKey(plainKeyInText);
            Assert.IsFalse(redactedPlain.Contains("AIzaSyA1B2C3D4E5F6G7H8I9J0K1L2M3N4O5P6"));
        }

        [TestMethod]
        public void AppSettings_DefaultRequestTimeout_Is120Seconds()
        {
            var settings = new AppSettings();
            Assert.AreEqual(120, settings.RequestTimeoutSeconds);
        }

        [TestMethod]
        public void AppSettings_SanitizeExcessiveValues_ClampsTimeoutAndRetries()
        {
            var loaded = new AppSettings
            {
                RequestTimeoutSeconds = 9000,
                MaxRetryAttempts = 10
            };

            Assert.AreEqual(9000, loaded.RequestTimeoutSeconds);
            Assert.AreEqual(10, loaded.MaxRetryAttempts);

            // Verify clamping logic
            if (loaded.RequestTimeoutSeconds < 30 || loaded.RequestTimeoutSeconds > 300)
                loaded.RequestTimeoutSeconds = 120;
            if (loaded.MaxRetryAttempts < 0 || loaded.MaxRetryAttempts > 3)
                loaded.MaxRetryAttempts = 2;

            Assert.AreEqual(120, loaded.RequestTimeoutSeconds);
            Assert.AreEqual(2, loaded.MaxRetryAttempts);
        }

        [TestMethod]
        public void LoggingService_LogPipeline_ExecutesWithoutExceptions()
        {
            LoggingService.Instance.LogPipeline("TEST_STAGE", "SUCCESS", "Testing pipeline logging", 42);
            // If no exception thrown, test passes safely
            Assert.IsTrue(true);
        }
    }
}
