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
            _tempCredFile = Path.Combine(Path.GetTempPath(), "test_cred_" + Guid.NewGuid().ToString() + ".dat");
            _credentialService = new CredentialService(_tempCredFile);
            _settings = new AppSettings
            {
                GeminiModel = "gemini-1.5-flash",
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
        public void GeminiException_RateLimit_FlagsSetProperly()
        {
            var ex = GeminiException.RateLimitException();
            Assert.IsTrue(ex.IsRateLimit);
            Assert.AreEqual(429, ex.StatusCode);
        }

        [TestMethod]
        public void GeminiException_Timeout_FlagsSetProperly()
        {
            var ex = GeminiException.TimeoutException();
            Assert.IsTrue(ex.IsTimeout);
        }

        [TestMethod]
        public void GeminiException_Auth_FlagsSetProperly()
        {
            var ex = GeminiException.AuthException();
            Assert.IsTrue(ex.IsAuthError);
            Assert.AreEqual(401, ex.StatusCode);
        }
    }
}
