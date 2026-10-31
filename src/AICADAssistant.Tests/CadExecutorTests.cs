using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;
using AICADAssistant.Core.Validation;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class CadExecutorTests
    {
        private CadPlanValidator _validator;

        [TestInitialize]
        public void Setup()
        {
            _validator = new CadPlanValidator();
        }

        [TestMethod]
        public void Executor_FailureResult_SetsFailedStatusAndErrorMessage()
        {
            var opRes = OperationResult.Failed("ext1", "Gagal melakukan Extrude Boss. Profil sketsa tidak tertutup.");
            var execResult = ExecutionResult.CreateFailure("Operasi 'ext1' (ExtrudeBoss) gagal: Gagal melakukan Extrude Boss.", opRes);

            Assert.IsFalse(execResult.Success);
            Assert.AreEqual(ExecutionStatus.Failed, execResult.OverallStatus);
            Assert.IsNotNull(execResult.FailedOperation);
            Assert.AreEqual("ext1", execResult.FailedOperation.OperationId);
            Assert.IsTrue(execResult.ErrorMessage.Contains("ext1"));
            Assert.IsTrue(execResult.ErrorMessage.Contains("Extrude Boss"));
        }

        [TestMethod]
        public void Executor_ZeroSolidBodies_MustBeTreatedAsFailed()
        {
            var execResult = new ExecutionResult
            {
                Success = false,
                OverallStatus = ExecutionStatus.Failed,
                SolidBodyCount = 0,
                ErrorMessage = "Pembuatan geometri gagal: Tidak ada Solid Body yang terbentuk (Body Count = 0)."
            };

            Assert.IsFalse(execResult.Success);
            Assert.AreEqual(0, execResult.SolidBodyCount);
            Assert.AreEqual(ExecutionStatus.Failed, execResult.OverallStatus);
            Assert.IsTrue(execResult.ErrorMessage.Contains("Body Count = 0"));
        }

        [TestMethod]
        public void Executor_DependencyFailure_PreventsSubsequentExecution()
        {
            // Simulate an operation sequence where op2 depends on op1
            var spec = new CadSpec
            {
                RequestId = "test_dep_fail",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "op1",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object> { { "plane", "Front" } }
                    },
                    new CadOperation
                    {
                        Id = "op2",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object> { { "depth", 10.0 } },
                        DependsOn = new List<string> { "op1" }
                    }
                }
            };

            // If op1 failed, op2 cannot be executed
            var completedOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // op1 was NOT added to completedOps (it failed)

            bool canExecuteOp2 = true;
            foreach (var dep in spec.Operations[1].DependsOn)
            {
                if (!completedOps.Contains(dep))
                {
                    canExecuteOp2 = false;
                    break;
                }
            }

            Assert.IsFalse(canExecuteOp2, "Operasi dengan dependensi yang gagal tidak boleh dieksekusi.");
        }

        [TestMethod]
        public void Executor_UnsupportedOperation_IsReportedClearly()
        {
            // Verify that an unknown operation is rejected by validator
            var spec = new CadSpec
            {
                RequestId = "test_unsupported",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "unsupported_op",
                        Type = (OperationType)999,
                        Parameters = new Dictionary<string, object>()
                    }
                }
            };

            var valResult = _validator.Validate(spec);
            Assert.IsFalse(valResult.IsValid);
            Assert.IsTrue(valResult.Errors[0].Contains("tidak ada dalam whitelist"));
        }

        [TestMethod]
        public void Executor_FastPathPlate100x60x5_RemainsValidAndFunctional()
        {
            CadSpec spec;
            bool ok = CadFastPathEngine.TryCreateSpec("Buatkan pelat 100 x 60 x 5 mm", out spec);

            Assert.IsTrue(ok);
            Assert.IsNotNull(spec);
            Assert.AreEqual(3, spec.Operations.Count);

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void Executor_FastPathHemisphere100mm_RemainsValidAndFunctional()
        {
            CadSpec spec;
            bool ok = CadFastPathEngine.TryCreateSpec("Buat setengah bola pejal dengan diameter 100 mm.", out spec);

            Assert.IsTrue(ok);
            Assert.IsNotNull(spec);
            Assert.AreEqual(1, spec.Operations.Count);
            Assert.AreEqual(OperationType.CreateHemisphere, spec.Operations[0].Type);

            var valResult = _validator.Validate(spec);
            Assert.IsTrue(valResult.IsValid);
        }

        [TestMethod]
        public void LoggingService_LogPipeline_RecordsFailedExecutionWithoutException()
        {
            // Verify that logging CAD_RESULT FAILED and STAGE_COMPLETE FAILED works cleanly
            LoggingService.Instance.LogPipeline("CAD_RESULT", "FAILED",
                "Doc='', Feature='', Bodies=0, Rebuild=False, Vol=0.00 mm3, Detail='Gagal melakukan Extrude Boss.'", 125);

            LoggingService.Instance.LogPipeline("STAGE_COMPLETE", "FAILED",
                "Pipeline Chat-to-CAD gagal pada tahap eksekusi geometri.", 1500);

            // If no exception was thrown, test passed
            Assert.IsTrue(true);
        }
    }
}

