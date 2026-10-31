using System;
using System.Collections.Generic;
using System.Linq;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICADAssistant.Tests
{
    /// <summary>
    /// Regression and validation test suite for Operation Type Mapping, Schema Generation,
    /// and Sketch/Cut Lifecycle (v3.0.2 Critical Fix for Level 3A Keyway):
    /// 1. Snake_case Operation Type Mapping
    /// 2. Numeric Operation Type Mapping
    /// 3. Operation Key Aliases (operation, operation_type, action)
    /// 4. Missing Operation Type defaults to Unknown
    /// 5. Unrecognized Operation Type defaults to Unknown and records RawTypeString
    /// 6. Never Guess Operation Type from Id
    /// 7. CadPlanValidator rejects Unknown OperationType with diagnostic
    /// 8. CadPlanValidator accepts Valid OperationTypes
    /// 9. CadContextReconciler preserves Unknown and Valid revision operations (only strips NewPart)
    /// 10. CadPlanner revision prompt contains structured schema example forbidding NewPart
    /// 11. OperationTypeMapper covers all 33 enum values
    /// 12. OperationTypeMapper handles colloquial aliases
    /// </summary>
    [TestClass]
    public class CadOperationTypeMappingTests
    {
        private CadPlanner _planner;
        private CadPlanValidator _validator;

        [TestInitialize]
        public void Setup()
        {
            _planner = new CadPlanner();
            _validator = new CadPlanValidator();
        }

        /// <summary>
        /// Test 1: Snake_case strings for OperationType map correctly without throwing exceptions.
        /// </summary>
        [TestMethod]
        public void Test01_ParseCadSpec_SnakeCaseOperationType_MapsCorrectly()
        {
            string json = @"
{
  ""RequestId"": ""req_snake_01"",
  ""Units"": ""mm"",
  ""Operations"": [
    {
      ""Id"": ""sk_1"",
      ""Type"": ""new_sketch"",
      ""Parameters"": { ""plane"": ""Top"" }
    },
    {
      ""Id"": ""cut_1"",
      ""Type"": ""extrude_cut"",
      ""Parameters"": { ""depth"": 4.0 }
    }
  ]
}";
            var spec = _planner.ParseCadSpec(json);
            Assert.IsNotNull(spec, "Spec should not be null.");
            Assert.AreEqual(2, spec.Operations.Count, "Should have parsed 2 operations.");
            Assert.AreEqual(OperationType.NewSketch, spec.Operations[0].Type, "new_sketch should map to NewSketch.");
            Assert.AreEqual(OperationType.ExtrudeCut, spec.Operations[1].Type, "extrude_cut should map to ExtrudeCut.");
        }

        /// <summary>
        /// Test 2: Numeric integers (e.g. 1, 3, 7, 30) map accurately to OperationType.
        /// </summary>
        [TestMethod]
        public void Test02_ParseCadSpec_NumericOperationType_MapsCorrectly()
        {
            string json = @"
{
  ""RequestId"": ""req_numeric_01"",
  ""Units"": ""mm"",
  ""Operations"": [
    { ""Id"": ""pl1"", ""Type"": 30, ""Parameters"": { ""offset"": 12.5 } },
    { ""Id"": ""sk1"", ""Type"": 1, ""Parameters"": { ""plane"": ""pl1"" } },
    { ""Id"": ""rect1"", ""Type"": 3, ""Parameters"": { ""width"": 8.0, ""height"": 50.0 } },
    { ""Id"": ""cut1"", ""Type"": 7, ""Parameters"": { ""depth"": 4.0 } }
  ]
}";
            var spec = _planner.ParseCadSpec(json);
            Assert.IsNotNull(spec);
            Assert.AreEqual(4, spec.Operations.Count);
            Assert.AreEqual(OperationType.CreatePlane, spec.Operations[0].Type);
            Assert.AreEqual(OperationType.NewSketch, spec.Operations[1].Type);
            Assert.AreEqual(OperationType.AddRectangle, spec.Operations[2].Type);
            Assert.AreEqual(OperationType.ExtrudeCut, spec.Operations[3].Type);
        }

        /// <summary>
        /// Test 3: Key aliases (operation, operation_type, action) are recognized correctly.
        /// </summary>
        [TestMethod]
        public void Test03_ParseCadSpec_OperationKeyAlias_MapsCorrectly()
        {
            string json = @"
{
  ""RequestId"": ""req_alias_01"",
  ""Units"": ""mm"",
  ""Operations"": [
    { ""Id"": ""sk_alias"", ""operation"": ""NewSketch"", ""Parameters"": { ""plane"": ""Top"" } },
    { ""Id"": ""slot_alias"", ""operation_type"": ""add_slot"", ""Parameters"": { ""width"": 8.0, ""length"": 50.0 } },
    { ""Id"": ""cut_alias"", ""action"": ""extrude_cut"", ""Parameters"": { ""depth"": 4.0 } }
  ]
}";
            var spec = _planner.ParseCadSpec(json);
            Assert.IsNotNull(spec);
            Assert.AreEqual(3, spec.Operations.Count);
            Assert.AreEqual(OperationType.NewSketch, spec.Operations[0].Type);
            Assert.AreEqual(OperationType.AddSlot, spec.Operations[1].Type);
            Assert.AreEqual(OperationType.ExtrudeCut, spec.Operations[2].Type);
        }

        /// <summary>
        /// Test 4: Missing operation type defaults to OperationType.Unknown rather than NewPart (0).
        /// </summary>
        [TestMethod]
        public void Test04_ParseCadSpec_MissingOperationType_DefaultsToUnknown()
        {
            string json = @"
{
  ""RequestId"": ""req_missing_type"",
  ""Units"": ""mm"",
  ""Operations"": [
    {
      ""Id"": ""rev_sk_keyway"",
      ""Description"": ""Open sketch without type field"",
      ""Parameters"": { ""plane"": ""Top"" }
    }
  ]
}";
            var spec = _planner.ParseCadSpec(json);
            Assert.IsNotNull(spec);
            Assert.AreEqual(1, spec.Operations.Count);
            Assert.AreEqual(OperationType.Unknown, spec.Operations[0].Type, "Missing type must be Unknown, NEVER NewPart (0)!");
            Assert.IsNull(spec.Operations[0].RawTypeString, "RawTypeString should be null when omitted.");
        }

        /// <summary>
        /// Test 5: Unrecognized operation type defaults to OperationType.Unknown and records RawTypeString.
        /// </summary>
        [TestMethod]
        public void Test05_ParseCadSpec_UnrecognizedOperationType_DefaultsToUnknown()
        {
            string json = @"
{
  ""RequestId"": ""req_unrecognized"",
  ""Units"": ""mm"",
  ""Operations"": [
    {
      ""Id"": ""op_invalid"",
      ""Type"": ""invalid_operation_foo"",
      ""Parameters"": {}
    }
  ]
}";
            var spec = _planner.ParseCadSpec(json);
            Assert.IsNotNull(spec);
            Assert.AreEqual(1, spec.Operations.Count);
            Assert.AreEqual(OperationType.Unknown, spec.Operations[0].Type);
            Assert.AreEqual("invalid_operation_foo", spec.Operations[0].RawTypeString);
        }

        /// <summary>
        /// Test 6: Operation type is NEVER guessed from operation Id.
        /// </summary>
        [TestMethod]
        public void Test06_ParseCadSpec_DoesNotGuessTypeFromId()
        {
            // Case A: Id has 'rev_cut' but type explicitly says 'new_sketch'
            string json = @"
{
  ""RequestId"": ""req_no_guess"",
  ""Units"": ""mm"",
  ""Operations"": [
    {
      ""Id"": ""rev_cut_keyway"",
      ""operation"": ""NewSketch"",
      ""Parameters"": { ""plane"": ""Top"" }
    },
    {
      ""Id"": ""rev_sk_keyway"",
      ""Parameters"": { ""plane"": ""Top"" }
    }
  ]
}";
            var spec = _planner.ParseCadSpec(json);
            Assert.IsNotNull(spec);
            Assert.AreEqual(2, spec.Operations.Count);
            Assert.AreEqual(OperationType.NewSketch, spec.Operations[0].Type, "Should follow explicit type, not the Id name 'rev_cut'.");
            Assert.AreEqual(OperationType.Unknown, spec.Operations[1].Type, "Should not guess NewSketch from Id 'rev_sk' when type is missing.");
        }

        /// <summary>
        /// Test 7: CadPlanValidator rejects OperationType.Unknown with diagnostic message.
        /// </summary>
        [TestMethod]
        public void Test07_CadPlanValidator_RejectsUnknownOperationType()
        {
            var spec = new CadSpec
            {
                RequestId = "req_val_unknown",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "op_bad",
                        Type = OperationType.Unknown,
                        RawTypeString = "unsupported_magic_op"
                    }
                }
            };

            var valResult = _validator.Validate(spec);
            Assert.IsFalse(valResult.IsValid, "Spec with OperationType.Unknown must fail validation.");
            Assert.IsTrue(valResult.Errors.Any(e => e.Contains("op_bad") && e.Contains("unsupported_magic_op")),
                "Error message should mention the op Id and raw type.");
        }

        /// <summary>
        /// Test 8: CadPlanValidator accepts valid operations (CreatePlane, NewSketch, AddSlot, ExtrudeCut).
        /// </summary>
        [TestMethod]
        public void Test08_CadPlanValidator_AcceptsValidOperationTypes()
        {
            var spec = new CadSpec
            {
                RequestId = "req_val_valid",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "pl_keyway",
                        Type = OperationType.CreatePlane,
                        Parameters = new Dictionary<string, object> { { "offset", 12.5 } }
                    },
                    new CadOperation
                    {
                        Id = "sk_keyway",
                        Type = OperationType.NewSketch,
                        DependsOn = new List<string> { "pl_keyway" },
                        Parameters = new Dictionary<string, object> { { "plane", "pl_keyway" } }
                    },
                    new CadOperation
                    {
                        Id = "slot_keyway",
                        Type = OperationType.AddSlot,
                        DependsOn = new List<string> { "sk_keyway" },
                        Parameters = new Dictionary<string, object> { { "width", 8.0 }, { "length", 50.0 } }
                    },
                    new CadOperation
                    {
                        Id = "cut_keyway",
                        Type = OperationType.ExtrudeCut,
                        DependsOn = new List<string> { "slot_keyway" },
                        Parameters = new Dictionary<string, object> { { "depth", 4.0 } }
                    }
                }
            };

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid, "Valid keyway operation sequence must pass validation. Errors: " + string.Join("; ", valResult.Errors));
        }

        /// <summary>
        /// Test 9: CadContextReconciler preserves valid revision operations and Unknown operations,
        /// only removing genuine NewPart operations when solid bodies exist.
        /// </summary>
        [TestMethod]
        public void Test09_CadContextReconciler_PreservesUnknownAndValidRevisionOperations()
        {
            var incomingSpec = new CadSpec
            {
                RequestId = "req_reconcile_test",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "np1", Type = OperationType.NewPart },
                    new CadOperation { Id = "rev_sk_keyway", Type = OperationType.NewSketch },
                    new CadOperation { Id = "rev_slot_keyway", Type = OperationType.AddSlot },
                    new CadOperation { Id = "rev_cut_keyway", Type = OperationType.ExtrudeCut },
                    new CadOperation { Id = "op_unk", Type = OperationType.Unknown, RawTypeString = "mystery" }
                }
            };

            var currentSpec = new CadSpec
            {
                RequestId = "req_base_shaft",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "sk_base", Type = OperationType.NewSketch },
                    new CadOperation { Id = "rev_base", Type = OperationType.RevolvedBoss }
                }
            };

            HashSet<string> existingIds;
            string note;
            bool ok = CadContextReconciler.TryReconcileRevisionSpec(incomingSpec, currentSpec, 1, out existingIds, out note);

            Assert.IsTrue(ok, "Reconciler should succeed.");
            // NewPart should have been removed
            Assert.IsFalse(incomingSpec.Operations.Any(o => o.Type == OperationType.NewPart), "NewPart must be removed from revision.");
            // Valid revision operations and unknown must NOT be removed
            Assert.AreEqual(4, incomingSpec.Operations.Count, "4 operations must remain (NewSketch, AddSlot, ExtrudeCut, and Unknown).");
            Assert.IsTrue(incomingSpec.Operations.Any(o => o.Id == "rev_sk_keyway"));
            Assert.IsTrue(incomingSpec.Operations.Any(o => o.Id == "rev_slot_keyway"));
            Assert.IsTrue(incomingSpec.Operations.Any(o => o.Id == "rev_cut_keyway"));
            Assert.IsTrue(incomingSpec.Operations.Any(o => o.Id == "op_unk"));
        }

        /// <summary>
        /// Test 10: CadPlanner.BuildPromptForRevision produces structured schema example
        /// containing CreatePlane (30), NewSketch (1), AddSlot (22) / AddRectangle (3),
        /// ExtrudeCut (7), and explicitly forbids NewPart.
        /// </summary>
        [TestMethod]
        public void Test10_CadPlanner_BuildPromptForRevision_ContainsStructuredSchema()
        {
            var currentSpec = new CadSpec
            {
                RequestId = "req_stepped_shaft",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "rev1", Type = OperationType.RevolvedBoss }
                }
            };

            var snapshot = new ModelContextSnapshot
            {
                DocumentTitle = "ShaftPart",
                SolidBodyCount = 1,
                VolumeMm3 = 49260.17,
                BoundingBox = new ModelBoundingBox
                {
                    MinX = 0, MaxX = 120.0,
                    MinY = -15.0, MaxY = 15.0,
                    MinZ = -15.0, MaxZ = 15.0
                }
            };

            string prompt = _planner.BuildPromptForRevision("Tambahkan alur pasak lebar 8mm dan dalam 4mm di tengah", currentSpec, snapshot);

            Assert.IsTrue(prompt.Contains("CreatePlane") || prompt.Contains("30"), "Prompt must specify CreatePlane.");
            Assert.IsTrue(prompt.Contains("NewSketch") || prompt.Contains("Type\": 1"), "Prompt must specify NewSketch.");
            Assert.IsTrue(prompt.Contains("ExtrudeCut") || prompt.Contains("7"), "Prompt must specify ExtrudeCut.");
            Assert.IsTrue(prompt.Contains("DILARANG NewPart") || prompt.Contains("JANGAN membuat part baru"),
                "Prompt must strictly forbid NewPart.");
            Assert.IsTrue(prompt.Contains("alur pasak") || prompt.Contains("keyway"), "Prompt must reference keyway context.");
        }

        /// <summary>
        /// Test 11: OperationTypeMapper accurately maps all 33 valid enum values (0 to 32)
        /// from both their integer values and exact enum string names.
        /// </summary>
        [TestMethod]
        public void Test11_OperationTypeMapper_AllEnumValuesHandled()
        {
            foreach (OperationType op in Enum.GetValues(typeof(OperationType)))
            {
                if (op == OperationType.Unknown) continue;

                // From int
                OperationType fromInt = OperationTypeMapper.Parse((int)op);
                Assert.AreEqual(op, fromInt, string.Format("OperationType {0} failed integer mapping.", op));

                // From string name
                OperationType fromName = OperationTypeMapper.Parse(op.ToString());
                Assert.AreEqual(op, fromName, string.Format("OperationType {0} failed name mapping.", op));

                // From lowercase string
                OperationType fromLower = OperationTypeMapper.Parse(op.ToString().ToLowerInvariant());
                Assert.AreEqual(op, fromLower, string.Format("OperationType {0} failed lowercase mapping.", op));
            }
        }

        /// <summary>
        /// Test 12: OperationTypeMapper handles colloquial aliases (slot, cut, boss, plane, sketch, revolve, etc.).
        /// </summary>
        [TestMethod]
        public void Test12_OperationTypeMapper_CommonAliasesHandled()
        {
            Assert.AreEqual(OperationType.NewSketch, OperationTypeMapper.Parse("sketch"));
            Assert.AreEqual(OperationType.NewSketch, OperationTypeMapper.Parse("new_sketch"));
            Assert.AreEqual(OperationType.ExtrudeBoss, OperationTypeMapper.Parse("boss"));
            Assert.AreEqual(OperationType.ExtrudeBoss, OperationTypeMapper.Parse("extrude"));
            Assert.AreEqual(OperationType.ExtrudeCut, OperationTypeMapper.Parse("cut"));
            Assert.AreEqual(OperationType.ExtrudeCut, OperationTypeMapper.Parse("extrude_cut"));
            Assert.AreEqual(OperationType.ExtrudeCut, OperationTypeMapper.Parse("cut_extrude"));
            Assert.AreEqual(OperationType.RevolvedBoss, OperationTypeMapper.Parse("revolve"));
            Assert.AreEqual(OperationType.AddSlot, OperationTypeMapper.Parse("slot"));
            Assert.AreEqual(OperationType.AddSlot, OperationTypeMapper.Parse("add_slot"));
            Assert.AreEqual(OperationType.AddSlot, OperationTypeMapper.Parse("keywayslot"));
            Assert.AreEqual(OperationType.CreatePlane, OperationTypeMapper.Parse("plane"));
            Assert.AreEqual(OperationType.CreatePlane, OperationTypeMapper.Parse("create_plane"));
            Assert.AreEqual(OperationType.CreatePlane, OperationTypeMapper.Parse("offset_plane"));
            Assert.AreEqual(OperationType.Fillet, OperationTypeMapper.Parse("round"));
            Assert.AreEqual(OperationType.Chamfer, OperationTypeMapper.Parse("chamfer"));
            Assert.AreEqual(OperationType.SimpleHole, OperationTypeMapper.Parse("hole"));
            Assert.AreEqual(OperationType.Unknown, OperationTypeMapper.Parse("unknown_nonsense_op"));
        }
    }
}
