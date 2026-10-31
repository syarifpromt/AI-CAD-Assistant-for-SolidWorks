using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AICADAssistant.AddIn.UI.Converters;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Gemini;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class HotfixV302Tests
    {
        [TestMethod]
        public void Test01_SuccessfulRequest_TransitionsToConnected()
        {
            var service = GeminiConnectionService.Instance;
            service.Initialize(true, "gemini-3.8-flash");
            Assert.AreEqual(GeminiConnectionStatus.ConfiguredUnverified, service.Status);

            service.ReportConnecting("gemini-3.8-flash");
            Assert.AreEqual(GeminiConnectionStatus.Connecting, service.Status);

            service.ReportConnected("gemini-3.8-flash", 1250);
            Assert.AreEqual(GeminiConnectionStatus.Connected, service.Status);
            Assert.AreEqual(1250, service.LastResponseTimeMs);
            Assert.AreEqual("gemini-3.8-flash", service.ActiveModel);

            string label = service.GetStatusLabel();
            Assert.IsTrue(label.Contains("Terhubung"), "Label should contain 'Terhubung'");
            Assert.IsTrue(label.Contains("gemini-3.8-flash"), "Label should contain active model name");
            Assert.IsTrue(label.Contains("1,250 ms"), "Label should contain formatted latency");
        }

        [TestMethod]
        public void Test02_CredentialPresence_InitializesToConfiguredUnverified()
        {
            var service = GeminiConnectionService.Instance;

            // When API key is present, must be ConfiguredUnverified (NOT Connected)
            service.Initialize(true, "gemini-3.8-flash");
            Assert.AreEqual(GeminiConnectionStatus.ConfiguredUnverified, service.Status);
            Assert.AreNotEqual(GeminiConnectionStatus.Connected, service.Status);
            Assert.IsTrue(service.GetStatusLabel().Contains("Terkonfigurasi"), "Should mention Terkonfigurasi");
            Assert.IsTrue(service.GetStatusLabel().Contains("Belum Terverifikasi"), "Should mention Belum Terverifikasi");

            // When API key is missing, must be Unconfigured
            service.Initialize(false, "gemini-3.8-flash");
            Assert.AreEqual(GeminiConnectionStatus.Unconfigured, service.Status);
            Assert.IsTrue(service.GetStatusLabel().Contains("API Key Belum Dikonfigurasi"), "Should mention API Key Belum Dikonfigurasi");
        }

        [TestMethod]
        public void Test03_ConversationalRequest_ReturnsPlainTextWithoutCadPlanner()
        {
            // Verify conversational request config
            var request = new GeminiTextRequest();
            request.generationConfig.temperature = 0.7;
            request.generationConfig.maxOutputTokens = 4096;
            request.generationConfig.responseMimeType = null; // Plain text / markdown (Conversational Fast Lane)
            request.generationConfig.thinkingLevel = "low";

            Assert.IsNull(request.generationConfig.responseMimeType, "Conversational requests must not enforce JSON responseMimeType.");
            Assert.AreEqual("low", request.generationConfig.thinkingLevel, "Conversational requests must use low thinking level.");
            Assert.AreEqual(0.7, request.generationConfig.temperature, "Conversational requests use natural temperature 0.7.");
        }

        [TestMethod]
        public void Test04_ConversationalRequest_DoesNotProduceCadSpec()
        {
            var planner = new CadPlanner();
            string conversationalAnswer = "Extrude Boss digunakan untuk menambahkan material padat dari profil sketsa 2D, " +
                                         "sedangkan Extrude Cut digunakan untuk memotong atau membuang material yang ada.";

            CadSpec spec = planner.ParseCadSpec(conversationalAnswer);
            Assert.IsNull(spec, "Conversational explanations must not parse into a CadSpec.");
        }

        [TestMethod]
        public void Test05_ConversationalRequest_DoesNotCallGeometryValidatorOrExecutor()
        {
            string question = "Apa fungsi Revolve feature di SOLIDWORKS?";
            UserIntent intent = IntentClassifier.Classify(question, null, 0);

            Assert.AreEqual(UserIntent.Conversational, intent,
                "Technical question must be classified as Conversational, bypassing CAD validator and executor.");
        }

        [TestMethod]
        public void Test06_ConversationalIntentClassification_IndonesianPhrases()
        {
            string[] conversationalQueries = new string[]
            {
                "Jelaskan perbedaan Extrude Boss, Extrude Cut, Revolve, dan Sweep.",
                "Apa itu fillet?",
                "Bagaimana cara membuat ulir di SOLIDWORKS?",
                "Mengapa model saya gagal rebuild?",
                "Halo apa kabar?",
                "Bisa bantu saya mendesain komponen mekanik?",
                "Apa perbedaan chamfer dan fillet?"
            };

            foreach (var q in conversationalQueries)
            {
                var intent = IntentClassifier.Classify(q, null, 0);
                Assert.AreEqual(UserIntent.Conversational, intent, string.Format("Query '{0}' should classify as Conversational.", q));
            }

            // Verify CAD commands do NOT classify as conversational
            var cadIntent = IntentClassifier.Classify("Buat pelat 100x60 tebal 5 mm.", null, 0);
            Assert.AreEqual(UserIntent.CreateNewModel, cadIntent);

            var exportIntent = IntentClassifier.Classify("Ekspor ke berkas STL", null, 1);
            Assert.AreEqual(UserIntent.Export, exportIntent);
        }

        [TestMethod]
        public void Test07_Conversational_UsesThinkingLevelLow()
        {
            ThinkingLevel level = AdaptiveThinkingManager.DetermineLevel("Jelaskan cara kerja Revolve di SOLIDWORKS", false);
            Assert.AreEqual(ThinkingLevel.Low, level);
            Assert.AreEqual("low", AdaptiveThinkingManager.ToApiString(level));
        }

        [TestMethod]
        public void Test08_AdaptiveThinking_MechanicalMedium_ComplexHigh()
        {
            // Simple CAD
            ThinkingLevel simpleLevel = AdaptiveThinkingManager.DetermineLevel("Buat kubus 50 mm", false);
            Assert.AreEqual(ThinkingLevel.Low, simpleLevel);
            Assert.AreEqual("low", AdaptiveThinkingManager.ToApiString(simpleLevel));

            // Mechanical CAD
            ThinkingLevel mechLevel = AdaptiveThinkingManager.DetermineLevel("Buatkan poros bertingkat panjang 200 mm dengan alur pasak dan ulir M12", false);
            Assert.AreEqual(ThinkingLevel.Medium, mechLevel);
            Assert.AreEqual("medium", AdaptiveThinkingManager.ToApiString(mechLevel));

            // Complex / Aerospace Geometry
            ThinkingLevel compLevel = AdaptiveThinkingManager.DetermineLevel("Buatkan impeller pompa sentrifugal 5 sudu kurva aerodinamis kompleks", false);
            Assert.AreEqual(ThinkingLevel.High, compLevel);
            Assert.AreEqual("high", AdaptiveThinkingManager.ToApiString(compLevel));
        }

        [TestMethod]
        public void Test09_PerformanceMetadata_ExtractionAndRecording()
        {
            string mockJson = "{" +
                "\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"Penjelasan teknik.\"}], \"role\": \"model\"}}]," +
                "\"usageMetadata\": {\"promptTokenCount\": 120, \"candidatesTokenCount\": 340, \"thoughtsTokenCount\": 450, \"totalTokenCount\": 910}" +
                "}";

            var serializer = new JavaScriptSerializer();
            var resp = serializer.Deserialize<GeminiResponse>(mockJson);

            Assert.IsNotNull(resp);
            Assert.IsNotNull(resp.usageMetadata);
            Assert.AreEqual(120, resp.usageMetadata.promptTokenCount);
            Assert.AreEqual(340, resp.usageMetadata.candidatesTokenCount);
            Assert.AreEqual(450, resp.usageMetadata.thoughtsTokenCount);
            Assert.AreEqual(910, resp.usageMetadata.totalTokenCount);
            Assert.AreEqual("Penjelasan teknik.", resp.GetFirstText());
        }

        [TestMethod]
        public void Test10_RoleToVisibilityConverter_PreventsDualRendering()
        {
            var converter = new RoleToVisibilityConverter();

            // When message role is User:
            // User border: Visible
            object userForUser = converter.Convert(MessageRole.User, typeof(Visibility), "User", null);
            Assert.AreEqual(Visibility.Visible, userForUser, "User message must be visible in User container.");

            // Assistant border: Collapsed
            object userForAssistant = converter.Convert(MessageRole.User, typeof(Visibility), "Assistant", null);
            Assert.AreEqual(Visibility.Collapsed, userForAssistant, "User message must NEVER be visible in Assistant container (prevents dual-rendering).");

            // When message role is Assistant:
            // User border: Collapsed
            object assistantForUser = converter.Convert(MessageRole.Assistant, typeof(Visibility), "User", null);
            Assert.AreEqual(Visibility.Collapsed, assistantForUser, "Assistant message must NEVER be visible in User container.");

            // Assistant border: Visible
            object assistantForAssistant = converter.Convert(MessageRole.Assistant, typeof(Visibility), "Assistant", null);
            Assert.AreEqual(Visibility.Visible, assistantForAssistant, "Assistant message must be visible in Assistant container.");
        }

        [TestMethod]
        public void Test11_ExceptionHandling_CleansStateAndReportsStatus()
        {
            var service = GeminiConnectionService.Instance;

            // 401 Auth error
            service.ReportError(new Exception("Kredensial ditolak"), 401, "gemini-3.8-flash", 120);
            Assert.AreEqual(GeminiConnectionStatus.AuthenticationFailed, service.Status);
            Assert.IsTrue(service.GetStatusLabel().Contains("Autentikasi Gagal"), "Status label should mention auth failed");

            // 429 Rate limit
            service.ReportError(new Exception("Rate limit exceeded"), 429, "gemini-3.8-flash", 90);
            Assert.AreEqual(GeminiConnectionStatus.RateLimited, service.Status);
            Assert.IsTrue(service.GetStatusLabel().Contains("Batas Kuota Tercapai"), "Status label should mention rate limit");

            // 503 Service unavailable
            service.ReportError(new Exception("High demand"), 503, "gemini-3.8-flash", 450);
            Assert.AreEqual(GeminiConnectionStatus.ServiceUnavailable, service.Status);
            Assert.IsTrue(service.GetStatusLabel().Contains("Layanan Gemini Tidak Tersedia"), "Status label should mention service unavailable");

            // Network error
            service.ReportError(new System.Net.Http.HttpRequestException("Connection reset"), 0, "gemini-3.8-flash", 2500);
            Assert.AreEqual(GeminiConnectionStatus.NetworkError, service.Status);
            Assert.IsTrue(service.GetStatusLabel().Contains("Kesalahan Jaringan"), "Status label should mention network error");
        }

        [TestMethod]
        public void Test12_FastPath_CadOperations_DeterministicNoRegression()
        {
            // 1. Cube (NewSketch, AddRectangle, ExtrudeBoss)
            CadSpec cubeSpec;
            bool okCube = CadFastPathEngine.TryCreateSpec("Buat kubus 50 mm.", out cubeSpec);
            Assert.IsTrue(okCube);
            Assert.IsNotNull(cubeSpec);
            Assert.AreEqual(3, cubeSpec.Operations.Count);

            // 2. Cylinder (NewSketch, AddCircle, ExtrudeBoss)
            CadSpec cylSpec;
            bool okCyl = CadFastPathEngine.TryCreateSpec("Buat silinder diameter 30 mm tinggi 80 mm.", out cylSpec);
            Assert.IsTrue(okCyl);
            Assert.IsNotNull(cylSpec);
            Assert.AreEqual(3, cylSpec.Operations.Count);

            // 3. Plate (NewSketch, AddRectangle, ExtrudeBoss)
            CadSpec plateSpec;
            bool okPlate = CadFastPathEngine.TryCreateSpec("Buat pelat 100x60x10 mm.", out plateSpec);
            Assert.IsTrue(okPlate);
            Assert.IsNotNull(plateSpec);
            Assert.AreEqual(3, plateSpec.Operations.Count);

            // 4. Solid Hemisphere (Bugfix verification: single CreateHemisphere operation)
            CadSpec hemiSpec;
            bool okHemi = CadFastPathEngine.TryCreateSpec("Buat setengah bola pejal dengan diameter 100 mm.", out hemiSpec);
            Assert.IsTrue(okHemi);
            Assert.IsNotNull(hemiSpec);
            Assert.AreEqual(1, hemiSpec.Operations.Count);
            Assert.AreEqual(OperationType.CreateHemisphere, hemiSpec.Operations[0].Type);
            Assert.AreEqual(100.0, Convert.ToDouble(hemiSpec.Operations[0].Parameters["diameter"]));
        }

        [TestMethod]
        public void Test13_ContextAwareRevision_DeterministicNoRegression()
        {
            // Base cylinder model
            CadSpec baseSpec;
            bool okBase = CadFastPathEngine.TryCreateSpec("Buat silinder diameter 40 mm tinggi 100 mm.", out baseSpec);
            Assert.IsTrue(okBase);

            // Revision: add through-hole (NewSketch, AddCircle, ExtrudeCut)
            CadSpec revSpec;
            bool okRev = CadFastPathEngine.TryCreateRevisionSpec("Tambahkan lubang tembus diameter 10 mm di tengah.", baseSpec, out revSpec);
            Assert.IsTrue(okRev);
            Assert.IsNotNull(revSpec);
            Assert.AreEqual(3, revSpec.Operations.Count);
            Assert.AreEqual(OperationType.NewSketch, revSpec.Operations[0].Type);
            Assert.AreEqual(OperationType.AddCircle, revSpec.Operations[1].Type);
            Assert.AreEqual(OperationType.ExtrudeCut, revSpec.Operations[2].Type);
            Assert.AreEqual(10.0, Convert.ToDouble(revSpec.Operations[1].Parameters["diameter"]));
        }
    }
}
