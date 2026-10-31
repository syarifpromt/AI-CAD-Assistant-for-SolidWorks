using System;
using System.Collections.Generic;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Features;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Models.Contracts;
using AICADAssistant.Core.Sketch;
using AICADAssistant.Core.Tools;
using AICADAssistant.Core.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class CadArchitectureV3Tests
    {
        [TestMethod]
        public void Contracts_CADBrief_InitializesCorrectly()
        {
            var brief = new CADBrief
            {
                UserPrompt = "Buat kunci pas 10 mm",
                ComponentType = ComponentType.WrenchTool,
                TargetDimensionsMm = new double[] { 120, 24, 5 },
                TargetVolumeMm3 = 7000.0
            };

            Assert.IsNotNull(brief.IntentId);
            Assert.AreEqual("mm", brief.Units);
            Assert.AreEqual(ComponentType.WrenchTool, brief.ComponentType);
            Assert.AreEqual(120.0, brief.TargetDimensionsMm[0]);
        }

        [TestMethod]
        public void Contracts_CADProgram_RoundTripsWithCadSpec()
        {
            var spec = new CadSpec
            {
                RequestId = "test_req_01",
                Units = "mm",
                Notes = "Test spec notes"
            };
            spec.ExplicitDimensions["Length"] = 100.0;
            spec.ExplicitDimensions["Width"] = 60.0;
            spec.ExplicitDimensions["Height"] = 5.0;
            spec.Operations.Add(new CadOperation
            {
                Id = "op1",
                Type = OperationType.NewPart,
                Description = "Create part"
            });

            var program = CADProgram.FromCadSpec(spec);
            Assert.IsNotNull(program);
            Assert.AreEqual("test_req_01", program.ProgramId);
            Assert.AreEqual(1, program.Operations.Count);

            var roundTripSpec = program.ToCadSpec();
            Assert.IsNotNull(roundTripSpec);
            Assert.AreEqual("test_req_01", roundTripSpec.RequestId);
            Assert.AreEqual(1, roundTripSpec.Operations.Count);
        }

        [TestMethod]
        public void Contracts_GeometryReference_ConstructorsWork()
        {
            var topFace = GeometryReference.TopFace();
            Assert.AreEqual(SelectorKind.PlanarFaceTop, topFace.Kind);
            Assert.AreEqual(1.0, topFace.NormalVector[2]);

            var bottomFace = GeometryReference.BottomFace();
            Assert.AreEqual(SelectorKind.PlanarFaceBottom, bottomFace.Kind);
            Assert.AreEqual(-1.0, bottomFace.NormalVector[2]);

            var namedRef = GeometryReference.Named("Front Plane");
            Assert.AreEqual(SelectorKind.ExplicitName, namedRef.Kind);
            Assert.AreEqual("Front Plane", namedRef.ExplicitName);
        }

        [TestMethod]
        public void Contracts_CADDiagnostic_CreationSetsAttributes()
        {
            var diag = CADDiagnostic.CreateError(DiagnosticStage.PreValidation, "ERR_PARAM", "Parameter tidak valid", "Gunakan nilai positif", "op_test");
            Assert.AreEqual(DiagnosticSeverity.Error, diag.Severity);
            Assert.AreEqual(DiagnosticStage.PreValidation, diag.Stage);
            Assert.AreEqual("ERR_PARAM", diag.Code);
            Assert.AreEqual("op_test", diag.TargetOperationId);
        }

        [TestMethod]
        public void ToolRegistry_ToolsRegisteredAndValidated()
        {
            Assert.IsTrue(CadToolRegistry.IsSupported(OperationType.NewPart));
            Assert.IsTrue(CadToolRegistry.IsSupported(OperationType.ExtrudeBoss));
            Assert.IsTrue(CadToolRegistry.IsSupported(OperationType.ExtrudeCut));
            Assert.IsTrue(CadToolRegistry.IsSupported(OperationType.SimpleHole));
            Assert.IsTrue(CadToolRegistry.IsSupported(OperationType.CreateHemisphere));

            var bossTool = CadToolRegistry.GetTool(OperationType.ExtrudeBoss);
            Assert.IsNotNull(bossTool);

            var validOp = new CadOperation { Id = "op_b", Type = OperationType.ExtrudeBoss };
            validOp.Parameters["depth"] = 25.0;

            string error;
            bool ok = bossTool.ValidateParameters(validOp, out error);
            Assert.IsTrue(ok);
            Assert.IsNull(error);

            var invalidOp = new CadOperation { Id = "op_b2", Type = OperationType.ExtrudeBoss };
            invalidOp.Parameters["depth"] = -5.0;
            bool invalidOk = bossTool.ValidateParameters(invalidOp, out error);
            Assert.IsFalse(invalidOk);
            Assert.IsNotNull(error);
        }

        [TestMethod]
        public void SketchLifecycleManager_StateMachineTransitionsProperly()
        {
            var mgr = new SketchLifecycleManager();
            Assert.AreEqual(SketchState.Idle, mgr.CurrentState);

            string err;
            bool began = mgr.BeginSketch("Top Plane", out err);
            Assert.IsTrue(began);
            Assert.AreEqual(SketchState.SketchStarted, mgr.CurrentState);

            mgr.RecordGeometry(4);
            Assert.AreEqual(SketchState.GeometryCreated, mgr.CurrentState);

            bool valid = mgr.ValidateGeometry(true, 150.0, out err);
            Assert.IsTrue(valid);
            Assert.AreEqual(SketchState.GeometryValidated, mgr.CurrentState);

            mgr.MarkSketchClosed("Sketch1");
            Assert.AreEqual(SketchState.SketchClosed, mgr.CurrentState);

            mgr.MarkProfileSelected("Sketch1");
            Assert.AreEqual(SketchState.ProfileSelected, mgr.CurrentState);

            mgr.MarkFeatureExecuted();
            Assert.AreEqual(SketchState.FeatureExecuted, mgr.CurrentState);
        }

        [TestMethod]
        public void CoordinateTransformer_Transforms2DTo3DAndBack()
        {
            // Face on Top plane (Normal = 0, 0, 1)
            var transformer = new CoordinateTransformer(new double[] { 10, 20, 30 }, new double[] { 0, 0, 1 });

            double[] pt3D = transformer.Transform2DTo3D(5.0, 15.0);
            Assert.IsNotNull(pt3D);
            Assert.AreEqual(3, pt3D.Length);
            Assert.AreEqual(30.0, pt3D[2]); // Z matches origin

            double[] pt2D = transformer.Transform3DTo2D(pt3D[0], pt3D[1], pt3D[2]);
            Assert.IsTrue(Math.Abs(pt2D[0] - 5.0) < 1e-6);
            Assert.IsTrue(Math.Abs(pt2D[1] - 15.0) < 1e-6);
        }

        [TestMethod]
        public void ProfileValidator_ClosedPolygon_CalculatesAccurateShoelaceArea()
        {
            // 100 x 50 rectangle: expected area = 5000.0
            var segments = new List<ProfileSegment>
            {
                new ProfileSegment { X1 = 0, Y1 = 0, X2 = 100, Y2 = 0 },
                new ProfileSegment { X1 = 100, Y1 = 0, X2 = 100, Y2 = 50 },
                new ProfileSegment { X1 = 100, Y1 = 50, X2 = 0, Y2 = 50 },
                new ProfileSegment { X1 = 0, Y1 = 50, X2 = 0, Y2 = 0 }
            };

            double area;
            string error;
            bool ok = ProfileValidator.ValidateSegments(segments, out area, out error);
            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.IsTrue(Math.Abs(area - 5000.0) < 1e-4);
        }

        [TestMethod]
        public void ProfileValidator_OpenProfile_ReportsDiscontinuityError()
        {
            var segments = new List<ProfileSegment>
            {
                new ProfileSegment { X1 = 0, Y1 = 0, X2 = 100, Y2 = 0 },
                new ProfileSegment { X1 = 100, Y1 = 0, X2 = 100, Y2 = 50 },
                new ProfileSegment { X1 = 100, Y1 = 50, X2 = 0, Y2 = 50 }
                // Missing closing segment back to (0,0)
            };

            double area;
            string error;
            bool ok = ProfileValidator.ValidateSegments(segments, out area, out error);
            Assert.IsFalse(ok);
            Assert.IsNotNull(error);
            Assert.IsTrue(error.Contains("Profil tidak tertutup"));
        }

        [TestMethod]
        public void ThroughHoleBuilder_CreatesAndValidatesOperation()
        {
            var op = ThroughHoleBuilder.CreateThroughHoleOperation("hole_1", 12.0, 50.0, 25.0, "Top Plane", "BaseBoss");
            Assert.AreEqual("hole_1", op.Id);
            Assert.AreEqual(OperationType.SimpleHole, op.Type);
            Assert.IsTrue(op.DependsOn.Contains("BaseBoss"));

            string err;
            bool valid = ThroughHoleBuilder.ValidateHoleOperation(op, out err);
            Assert.IsTrue(valid);
            Assert.IsNull(err);
        }

        [TestMethod]
        public void FilletChamferBuilder_ValidatesRadiusAgainstBoundingBox()
        {
            double[] bbox = new double[] { 100.0, 50.0, 10.0 }; // min thickness = 10 mm
            string error;

            // Fillet radius 2 mm on 10 mm thickness is safe
            bool valid = FilletChamferBuilder.ValidateFilletRadius(2.0, bbox, out error);
            Assert.IsTrue(valid);
            Assert.IsNull(error);

            // Fillet radius 6 mm on 10 mm thickness is oversized (>= 5 mm)
            bool oversized = FilletChamferBuilder.ValidateFilletRadius(6.0, bbox, out error);
            Assert.IsFalse(oversized);
            Assert.IsNotNull(error);
        }

        [TestMethod]
        public void CadExecutionEngine_FastPathExecution_ExecutesSynchronously()
        {
            var engine = new CadExecutionEngine();

            // Mock native executor that returns simulated solid body
            Func<CadSpec, ExecutionResult> mockExecutor = spec =>
            {
                return new ExecutionResult
                {
                    Success = true,
                    OverallStatus = ExecutionStatus.Success,
                    SolidBodyCount = 1,
                    VolumeMm3 = 30000.0,
                    BoundingBoxMm = new double[] { 100.0, 60.0, 5.0 },
                    RebuildSuccess = true
                };
            };

            var resp = engine.ProcessAndExecute("Buat pelat 100x60x5 mm", null, 0, 0, mockExecutor);
            Assert.IsTrue(resp.Success);
            Assert.IsTrue(resp.UsedFastPath);
            Assert.IsNotNull(resp.FinalSpec);
            Assert.IsNotNull(resp.Verification);
            Assert.AreEqual(1, resp.Verification.SolidBodyCount);
            Assert.IsTrue(resp.Verification.IsPhysicallySound);
        }

        [TestMethod]
        public void Wrench10mm_FastPath_GeneratesPolylineHoleAndFillet()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("Buatkan kunci pas ukuran 10 mm panjang 120 mm tebal 5 mm dengan lubang gantung", out spec);
            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);

            // Verify operations contain Polyline, Extrude, Sketch, Hole, Fillet
            bool hasPoly = false, hasExtrude = false, hasHole = false, hasFillet = false;
            foreach (var op in spec.Operations)
            {
                if (op.Type == OperationType.AddPolyline) hasPoly = true;
                if (op.Type == OperationType.ExtrudeBoss) hasExtrude = true;
                if (op.Type == OperationType.ExtrudeCut || op.Type == OperationType.SimpleHole) hasHole = true;
                if (op.Type == OperationType.Fillet) hasFillet = true;
            }

            Assert.IsTrue(hasPoly);
            Assert.IsTrue(hasExtrude);
            Assert.IsTrue(hasHole);
            Assert.IsTrue(hasFillet);

            // Pre-execution validation must pass 100%
            var validator = new CadPlanValidator();
            var valResult = validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void LBracket_FastPath_GeneratesAccurateSpec()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("Buat L-bracket 80x100x40 mm tebal 5 mm", out spec);
            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);

            var validator = new CadPlanValidator();
            var valResult = validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void Pulley_FastPath_GeneratesRevolvedBossSpec()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("Buat puli v-groove OD 100 mm bore 20 mm lebar 30 mm", out spec);
            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);

            var validator = new CadPlanValidator();
            var valResult = validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void SteppedShaft_FastPath_GeneratesRevolvedBossSpec()
        {
            CadSpec spec;
            bool matched = CadFastPathEngine.TryCreateSpec("Buat poros bertingkat 3 seksi", out spec);
            Assert.IsTrue(matched);
            Assert.IsNotNull(spec);

            var validator = new CadPlanValidator();
            var valResult = validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }
    }
}
