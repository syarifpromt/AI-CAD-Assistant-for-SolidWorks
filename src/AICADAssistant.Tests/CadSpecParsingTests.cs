using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class CadSpecParsingTests
    {
        private CadPlanner _planner;

        [TestInitialize]
        public void Setup()
        {
            _planner = new CadPlanner();
        }

        [TestMethod]
        public void ParseCadSpec_ValidJson_ReturnsPopulatedCadSpec()
        {
            string json = @"
            {
                ""RequestId"": ""req-123"",
                ""DocumentType"": 0,
                ""Units"": ""mm"",
                ""Source"": 0,
                ""ExplicitDimensions"": { ""length"": 100.0, ""width"": 50.0 },
                ""Unknowns"": [],
                ""Operations"": [
                    {
                        ""Id"": ""sk1"",
                        ""Type"": 1,
                        ""Parameters"": { ""plane"": ""Front"" }
                    }
                ],
                ""ExportTargets"": [""STEP""]
            }";

            var spec = _planner.ParseCadSpec(json);
            Assert.IsNotNull(spec);
            Assert.AreEqual("req-123", spec.RequestId);
            Assert.AreEqual(DocumentType.Part, spec.DocumentType);
            Assert.AreEqual("mm", spec.Units);
            Assert.AreEqual(1, spec.Operations.Count);
            Assert.AreEqual(OperationType.NewSketch, spec.Operations[0].Type);
        }

        [TestMethod]
        public void ParseCadSpec_JsonInMarkdownFence_ExtractsAndParses()
        {
            string raw = "Berikut adalah rencana CAD yang disusun:\n```json\n" +
                         "{\n" +
                         "  \"RequestId\": \"fence-001\",\n" +
                         "  \"DocumentType\": 0,\n" +
                         "  \"Units\": \"mm\",\n" +
                         "  \"Operations\": [\n" +
                         "    { \"Id\": \"op1\", \"Type\": 0, \"Parameters\": {} }\n" +
                         "  ]\n" +
                         "}\n```\nSemoga membantu!";

            var spec = _planner.ParseCadSpec(raw);
            Assert.IsNotNull(spec);
            Assert.AreEqual("fence-001", spec.RequestId);
            Assert.AreEqual(1, spec.Operations.Count);
        }

        [TestMethod]
        public void ParseCadSpec_InvalidJson_ReturnsNull()
        {
            string invalid = "{ ini bukan json valid }";
            var spec = _planner.ParseCadSpec(invalid);
            Assert.IsNull(spec);
        }

        [TestMethod]
        public void ExtractJsonFromResponse_PullsPureJson()
        {
            string text = "Tentu, ini hasilnya: {\"test\": 123} silakan dicoba.";
            string extracted = _planner.ExtractJsonFromResponse(text);
            Assert.AreEqual("{\"test\": 123}", extracted);
        }

        [TestMethod]
        public void BuildClarificationMessage_FormatsCorrectly()
        {
            var missing = new List<string> { "diameter lubang", "ketebalan pelat" };
            string msg = _planner.BuildClarificationMessage(missing);
            Assert.IsTrue(msg.Contains("1. diameter lubang"));
            Assert.IsTrue(msg.Contains("2. ketebalan pelat"));
        }
    }
}
