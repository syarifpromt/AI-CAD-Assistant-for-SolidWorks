using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AICADAssistant.Core.Gemini;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;

namespace AICADAssistant.Tests
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> HandlerFunc { get; set; }
        public int CallCount { get; private set; }
        public List<string> CapturedRequestBodies { get; private set; }
        public List<string> CapturedRequestUrls { get; private set; }

        public MockHttpMessageHandler()
        {
            CapturedRequestBodies = new List<string>();
            CapturedRequestUrls = new List<string>();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.RequestUri != null)
            {
                CapturedRequestUrls.Add(request.RequestUri.ToString());
            }

            if (request.Content != null)
            {
                string body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                CapturedRequestBodies.Add(body);
            }

            if (HandlerFunc != null)
            {
                return await HandlerFunc(request, cancellationToken).ConfigureAwait(false);
            }

            // Default: successful response
            return CreateJsonResponse("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Halo dari mock!\"}],\"role\":\"model\"}}]}");
        }

        public static HttpResponseMessage CreateJsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    [TestClass]
    public class GeminiHttpMockTests
    {
        private string _tempCredPath;
        private CredentialService _credentialService;
        private AppSettings _settings;

        [TestInitialize]
        public void Setup()
        {
            _tempCredPath = Path.Combine(Path.GetTempPath(), "aicad_test_cred_" + Guid.NewGuid().ToString("N") + ".dat");
            _credentialService = new CredentialService(_tempCredPath);
            _credentialService.SaveApiKey("AIzaSyMockDummyKeyForTestingOnly12345");

            _settings = new AppSettings
            {
                RequestTimeoutSeconds = 120,
                MaxRetryAttempts = 2,
                GeminiModel = "gemini-3.8-flash"
            };
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (_credentialService != null)
                {
                    _credentialService.DeleteApiKey();
                }
                if (!string.IsNullOrEmpty(_tempCredPath) && File.Exists(_tempCredPath))
                {
                    File.Delete(_tempCredPath);
                }
            }
            catch { }
        }

        [TestMethod]
        public void Test01_RequestSucceeds_ReturnsExpectedAnswer()
        {
            var mockHandler = new MockHttpMessageHandler
            {
                HandlerFunc = (req, ct) =>
                {
                    string json = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Extrude Boss menambah material solid.\"}],\"role\":\"model\"}}]}";
                    return Task.FromResult(MockHttpMessageHandler.CreateJsonResponse(json));
                }
            };

            var client = new GeminiClient(_credentialService, _settings, mockHandler);
            string result = client.SendChatAsync("Jelaskan Extrude Boss", null).Result;

            Assert.AreEqual("Extrude Boss menambah material solid.", result);
            Assert.AreEqual(1, mockHandler.CallCount);
            Assert.AreEqual(1, mockHandler.CapturedRequestBodies.Count);
        }

        [TestMethod]
        public void Test02_RequestTimeout_ActuallyTriggers()
        {
            var shortTimeoutSettings = new AppSettings
            {
                RequestTimeoutSeconds = 1, // 1 second timeout
                MaxRetryAttempts = 0
            };

            var mockHandler = new MockHttpMessageHandler
            {
                HandlerFunc = async (req, ct) =>
                {
                    // Delay longer than the 1 second timeout
                    await Task.Delay(2500, ct).ConfigureAwait(false);
                    return MockHttpMessageHandler.CreateJsonResponse("{\"candidates\":[]}");
                }
            };

            var client = new GeminiClient(_credentialService, shortTimeoutSettings, mockHandler);

            bool caughtTimeout = false;
            try
            {
                string res = client.SendChatAsync("Testing timeout", null).Result;
            }
            catch (AggregateException ex)
            {
                var baseEx = ex.InnerException as GeminiException;
                if (baseEx != null && baseEx.IsTimeout)
                {
                    caughtTimeout = true;
                    Assert.IsTrue(baseEx.Message.Contains("batas waktu"));
                }
            }

            Assert.IsTrue(caughtTimeout, "Expected GeminiException with IsTimeout=true was not thrown.");
        }

        [TestMethod]
        public void Test03_UserCancellation_DistinctFromTimeout()
        {
            var requestStarted = new TaskCompletionSource<bool>();
            var cts = new CancellationTokenSource();

            var mockHandler = new MockHttpMessageHandler
            {
                HandlerFunc = async (req, ct) =>
                {
                    requestStarted.TrySetResult(true);
                    await Task.Delay(5000, ct).ConfigureAwait(false);
                    return MockHttpMessageHandler.CreateJsonResponse("{\"candidates\":[]}");
                }
            };

            var client = new GeminiClient(_credentialService, _settings, mockHandler);

            var task = client.SendChatAsync("Testing cancel", null, cts.Token);

            // Wait until handler confirms the request is actively in-flight
            bool started = requestStarted.Task.Wait(3000);
            Assert.IsTrue(started, "Handler must have received and started processing the request.");

            // Cancel while request is in-flight
            cts.Cancel();

            bool caughtUserCancel = false;
            bool caughtTimeout = false;

            try
            {
                string res = task.Result;
            }
            catch (AggregateException ex)
            {
                var inner = ex.Flatten().InnerException;
                if (inner is OperationCanceledException)
                {
                    caughtUserCancel = true;
                }
                var geminiEx = inner as GeminiException;
                if (geminiEx != null && geminiEx.IsTimeout)
                {
                    caughtTimeout = true;
                }
            }

            Assert.IsTrue(caughtUserCancel, "Expected OperationCanceledException for in-flight user cancellation.");
            Assert.IsFalse(caughtTimeout, "User cancellation must NEVER be reported as a Timeout.");
            Assert.AreEqual(1, mockHandler.CallCount, "User cancellation in-flight must not trigger retries.");
        }

        [TestMethod]
        public void Test04_Timeout_DoesNotCauseRetry()
        {
            var timeoutSettings = new AppSettings
            {
                RequestTimeoutSeconds = 1,
                MaxRetryAttempts = 2,
                GeminiModel = "gemini-3.8-flash"
            };

            var mockHandler = new MockHttpMessageHandler
            {
                HandlerFunc = async (req, ct) =>
                {
                    await Task.Delay(2500, ct).ConfigureAwait(false);
                    return MockHttpMessageHandler.CreateJsonResponse("{\"candidates\":[]}");
                }
            };

            var client = new GeminiClient(_credentialService, timeoutSettings, mockHandler);

            GeminiException caughtTimeoutEx = null;
            try
            {
                string res = client.SendChatAsync("Testing no retry on timeout", null).Result;
            }
            catch (AggregateException ex)
            {
                var inner = ex.Flatten().InnerException as GeminiException;
                if (inner != null && inner.IsTimeout)
                {
                    caughtTimeoutEx = inner;
                }
            }

            Assert.IsNotNull(caughtTimeoutEx, "Expected GeminiException with IsTimeout = true.");
            Assert.IsTrue(caughtTimeoutEx.IsTimeout);
            Assert.AreEqual(1, mockHandler.CallCount, "Timeout must immediately abort without attempting further retries.");
            Assert.AreEqual(1, mockHandler.CapturedRequestUrls.Count);
            Assert.IsTrue(mockHandler.CapturedRequestUrls[0].Contains("gemini-3.8-flash"), "Initial request should use configured model.");
        }

        [TestMethod]
        public void Test05_RetryHttp503_SucceedsOnSecondAttempt()
        {
            MockHttpMessageHandler mockHandler = null;
            mockHandler = new MockHttpMessageHandler
            {
                HandlerFunc = (req, ct) =>
                {
                    if (mockHandler.CallCount == 1)
                    {
                        string errJson = "{\"error\":{\"code\":503,\"message\":\"The model is overloaded. Please try again later.\",\"status\":\"UNAVAILABLE\"}}";
                        return Task.FromResult(MockHttpMessageHandler.CreateJsonResponse(errJson, (HttpStatusCode)503));
                    }

                    string successJson = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Sukses setelah retry 503!\"}],\"role\":\"model\"}}]}";
                    return Task.FromResult(MockHttpMessageHandler.CreateJsonResponse(successJson, HttpStatusCode.OK));
                }
            };

            var client = new GeminiClient(_credentialService, _settings, mockHandler);
            string result = client.SendChatAsync("Test retry 503", null).Result;

            Assert.AreEqual("Sukses setelah retry 503!", result);
            Assert.AreEqual(2, mockHandler.CallCount, "Should succeed on second attempt after 503.");
            Assert.IsTrue(mockHandler.CapturedRequestUrls[0].Contains("gemini-3.8-flash"));
            Assert.IsTrue(mockHandler.CapturedRequestUrls[1].Contains(AppSettings.DefaultGeminiModel), "Fallback should use gemini-3.5-flash-lite");
        }

        [TestMethod]
        public void Test06_RetryHttp429_SucceedsOnSecondAttempt()
        {
            MockHttpMessageHandler mockHandler = null;
            mockHandler = new MockHttpMessageHandler
            {
                HandlerFunc = (req, ct) =>
                {
                    if (mockHandler.CallCount == 1)
                    {
                        string errJson = "{\"error\":{\"code\":429,\"message\":\"Resource has been exhausted (e.g. check quota).\",\"status\":\"RESOURCE_EXHAUSTED\"}}";
                        return Task.FromResult(MockHttpMessageHandler.CreateJsonResponse(errJson, (HttpStatusCode)429));
                    }

                    string successJson = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Sukses setelah fallback/retry 429!\"}],\"role\":\"model\"}}]}";
                    return Task.FromResult(MockHttpMessageHandler.CreateJsonResponse(successJson, HttpStatusCode.OK));
                }
            };

            var client = new GeminiClient(_credentialService, _settings, mockHandler);
            string result = client.SendChatAsync("Test retry 429", null).Result;

            Assert.AreEqual("Sukses setelah fallback/retry 429!", result);
            Assert.AreEqual(2, mockHandler.CallCount, "Should succeed on second attempt after 429.");
            Assert.IsTrue(mockHandler.CapturedRequestUrls[0].Contains("gemini-3.8-flash"));
            Assert.IsTrue(mockHandler.CapturedRequestUrls[1].Contains(AppSettings.DefaultGeminiModel), "Fallback should use gemini-3.5-flash-lite");
        }

        [TestMethod]
        public void Test07_RecentUserMessage_SentOnlyOnceInPayload()
        {
            var mockHandler = new MockHttpMessageHandler();
            var client = new GeminiClient(_credentialService, _settings, mockHandler);

            var history = new List<ConversationMessage>
            {
                ConversationMessage.UserText("Pesan pertama dari pengguna"),
                ConversationMessage.AssistantText("Jawaban pertama dari asisten"),
                ConversationMessage.UserText("Pesan kedua dari pengguna"),
                ConversationMessage.AssistantText("Jawaban kedua dari asisten")
            };

            string currentPrompt = "Pertanyaan baru yang sedang diajukan";

            // Case A: Standard history (does NOT contain current prompt)
            string resA = client.SendChatAsync(currentPrompt, history).Result;
            string bodyA = mockHandler.CapturedRequestBodies[0];

            int matchesA = Regex.Matches(bodyA, Regex.Escape("Pertanyaan baru yang sedang diajukan")).Count;
            Assert.AreEqual(1, matchesA, "Current prompt must appear exactly once in request payload.");

            // Case B: Defensive check - caller erroneously appends current prompt to history before calling
            var historyWithPromptAppended = new List<ConversationMessage>(history)
            {
                ConversationMessage.UserText(currentPrompt)
            };

            string resB = client.SendChatAsync(currentPrompt, historyWithPromptAppended).Result;
            string bodyB = mockHandler.CapturedRequestBodies[1];

            int matchesB = Regex.Matches(bodyB, Regex.Escape("Pertanyaan baru yang sedang diajukan")).Count;
            Assert.AreEqual(1, matchesB, "Even if caller appends prompt to history, SendChatAsync must prevent duplicate user prompt in payload.");
        }

        [TestMethod]
        public void Test08_ApiKey_NeverAppearsInLogOrErrorMessage()
        {
            string secretKey = "AIzaSyTestSecretKey9876543210";
            string rawUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent?key=" + secretKey;

            string redacted = LoggingService.RedactApiKey(rawUrl);
            Assert.IsFalse(redacted.Contains(secretKey), "Raw API key must be sanitized from URL.");
            Assert.IsTrue(redacted.Contains("key=[REDACTED]"));

            var mockHandler = new MockHttpMessageHandler
            {
                HandlerFunc = (req, ct) =>
                {
                    string errorWithKey = "Server error processing key " + secretKey;
                    return Task.FromResult(MockHttpMessageHandler.CreateJsonResponse(errorWithKey, HttpStatusCode.InternalServerError));
                }
            };

            var client = new GeminiClient(_credentialService, _settings, mockHandler);

            GeminiException caughtEx = null;
            try
            {
                string res = client.SendChatAsync("Test redact error", null).Result;
            }
            catch (AggregateException ex)
            {
                caughtEx = ex.Flatten().InnerException as GeminiException;
            }

            Assert.IsNotNull(caughtEx, "Expected GeminiException was not thrown.");

            // Direct assertion on GeminiException.Message WITHOUT calling RedactApiKey in test
            Assert.IsFalse(caughtEx.Message.Contains(secretKey), "GeminiException.Message must NOT contain dummy key: " + caughtEx.Message);

            // Direct assertion on GeminiConnectionService.LastErrorMessage WITHOUT calling RedactApiKey in test
            string lastConnError = GeminiConnectionService.Instance.LastErrorMessage;
            if (!string.IsNullOrEmpty(lastConnError))
            {
                Assert.IsFalse(lastConnError.Contains(secretKey), "GeminiConnectionService.LastErrorMessage must NOT contain dummy key: " + lastConnError);
            }
        }

        [TestMethod]
        public void Test09_PostHttpDeadlineCheck_ThrowsTimeoutAndDoesNotReturnResponse()
        {
            var timeoutSettings = new AppSettings
            {
                RequestTimeoutSeconds = 1,
                MaxRetryAttempts = 0
            };

            var mockHandler = new MockHttpMessageHandler
            {
                HandlerFunc = async (req, ct) =>
                {
                    // Delay without using linked cancellation token so PostAsync completes after deadline
                    await Task.Delay(1300).ConfigureAwait(false);
                    return MockHttpMessageHandler.CreateJsonResponse("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Late response\"}],\"role\":\"model\"}}]}");
                }
            };

            var client = new GeminiClient(_credentialService, timeoutSettings, mockHandler);

            GeminiException caughtTimeoutEx = null;
            try
            {
                string res = client.SendChatAsync("Testing post-http deadline", null).Result;
            }
            catch (AggregateException ex)
            {
                caughtTimeoutEx = ex.Flatten().InnerException as GeminiException;
            }

            Assert.IsNotNull(caughtTimeoutEx, "Expected GeminiException when deadline expires during/after PostAsync.");
            Assert.IsTrue(caughtTimeoutEx.IsTimeout, "Exception must be classified as Timeout.");
            Assert.AreEqual(1, mockHandler.CallCount, "No retries should be attempted once deadline expires.");
        }

        [TestMethod]
        public void Test10_PostHttpCancellation_ThrowsOperationCanceledException()
        {
            var cts = new CancellationTokenSource();
            var postAsyncStarted = new TaskCompletionSource<bool>();

            var mockHandler = new MockHttpMessageHandler
            {
                HandlerFunc = async (req, ct) =>
                {
                    postAsyncStarted.TrySetResult(true);
                    await Task.Delay(600).ConfigureAwait(false);
                    return MockHttpMessageHandler.CreateJsonResponse("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Response after cancel\"}],\"role\":\"model\"}}]}");
                }
            };

            var client = new GeminiClient(_credentialService, _settings, mockHandler);

            var task = client.SendChatAsync("Testing post-http user cancel", null, cts.Token);

            bool started = postAsyncStarted.Task.Wait(2000);
            Assert.IsTrue(started, "Request should have started.");

            // Cancel user token
            cts.Cancel();

            bool caughtUserCancel = false;
            bool caughtTimeout = false;

            try
            {
                string res = task.Result;
            }
            catch (AggregateException ex)
            {
                var inner = ex.Flatten().InnerException;
                if (inner is OperationCanceledException)
                {
                    caughtUserCancel = true;
                }
                var geminiEx = inner as GeminiException;
                if (geminiEx != null && geminiEx.IsTimeout)
                {
                    caughtTimeout = true;
                }
            }

            Assert.IsTrue(caughtUserCancel, "Expected OperationCanceledException when user cancels after PostAsync.");
            Assert.IsFalse(caughtTimeout, "User cancellation must not be classified as timeout.");
        }

        [TestMethod]
        public void Test11_HttpRequestException_WhenUserCancelled_ClassifiedAsUserCancellation()
        {
            var cts = new CancellationTokenSource();
            var requestStarted = new TaskCompletionSource<bool>();

            var mockHandler = new MockHttpMessageHandler
            {
                HandlerFunc = async (req, ct) =>
                {
                    requestStarted.TrySetResult(true);
                    // Wait until user cancels
                    while (!cts.IsCancellationRequested)
                    {
                        await Task.Delay(10).ConfigureAwait(false);
                    }
                    // Simulate aborted connection / socket exception thrown as HttpRequestException
                    throw new HttpRequestException("The underlying connection was closed: The request was canceled.");
                }
            };

            var client = new GeminiClient(_credentialService, _settings, mockHandler);

            var task = client.SendChatAsync("Testing network exception on cancellation", null, cts.Token);

            bool started = requestStarted.Task.Wait(3000);
            Assert.IsTrue(started, "Request should have started.");

            // Cancel token from test thread while request is in flight
            cts.Cancel();

            bool caughtUserCancel = false;
            bool caughtTimeout = false;
            bool caughtNetworkError = false;

            try
            {
                string res = task.Result;
            }
            catch (AggregateException ex)
            {
                var inner = ex.Flatten().InnerException;
                if (inner is OperationCanceledException)
                {
                    caughtUserCancel = true;
                }
                var geminiEx = inner as GeminiException;
                if (geminiEx != null)
                {
                    if (geminiEx.IsTimeout) caughtTimeout = true;
                    if (geminiEx.IsNetworkError) caughtNetworkError = true;
                }
            }

            Assert.IsTrue(caughtUserCancel, "Exception must be classified as OperationCanceledException when user cancels.");
            Assert.IsFalse(caughtTimeout, "Must NOT be classified as GeminiException.TimeoutException.");
            Assert.IsFalse(caughtNetworkError, "Must NOT be classified as GeminiException.NetworkError.");
            Assert.AreEqual(1, mockHandler.CallCount, "Cancellation must abort immediately without retries.");
        }
    }
}
