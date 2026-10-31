using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using AICADAssistant.AddIn;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SolidWorks.Interop.sldworks;

namespace AICADAssistant.Tests
{
    /// <summary>
    /// Helper mock dispatcher using RealProxy to intercept COM interface calls on SOLIDWORKS API.
    /// </summary>
    public class SimpleComMock<T> : RealProxy where T : class
    {
        private readonly Func<IMethodCallMessage, object> _handler;

        public SimpleComMock(Func<IMethodCallMessage, object> handler) : base(typeof(T))
        {
            _handler = handler;
        }

        public override IMessage Invoke(IMessage msg)
        {
            var call = msg as IMethodCallMessage;
            if (call == null) return null;

            if (call.MethodName == "GetType")
            {
                return new ReturnMessage(typeof(T), null, 0, call.LogicalCallContext, call);
            }

            object result = null;
            if (_handler != null)
            {
                result = _handler(call);
            }
            // Console.WriteLine(string.Format("[MOCK CALL] {0}.{1} -> {2}", typeof(T).Name, call.MethodName, result != null ? result.GetType().Name : "null"));
            return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
        }

        public T Proxy => (T)GetTransparentProxy();
    }

    /// <summary>
    /// Regression and validation tests for SOLIDWORKS Revolve Selection Fix (v3.0.2):
    /// 1. Revolve with sketch active (auto-close sketch then revolve).
    /// 2. Revolve after EndSketch called (ActiveSketch == null, revolve succeeds).
    /// 3. Sketch and axis selected with Mark = 0 and Mark = 4.
    /// 4. ActiveSketch == null does not cause NullReferenceException.
    /// 5. Axis not found produces predictable failure without unhandled exception.
    /// 6. FeatureRevolve2 returning null is not reported as success.
    /// 7. Stepped shaft pipeline stops if Revolve fails (does not proceed to Cut-Extrude).
    /// </summary>
    [TestClass]
    public class SolidWorksRevolveTests
    {
        private class MockCadSession
        {
            public bool SketchActive { get; set; } = true;
            public bool InsertSketchCalled { get; set; } = false;
            public bool ClearSelectionCalled { get; set; } = false;

            public string SelectedSketchName { get; set; }
            public int SelectedSketchMark { get; set; } = -1;
            public bool SelectedSketchAppend { get; set; } = true;

            public int SelectedAxisMark { get; set; } = -1;
            public bool SelectedAxisAppend { get; set; } = false;

            public bool FeatureRevolve2Called { get; set; } = false;
            public bool FeatureRevolve2UseAutoSelect { get; set; } = true;
            public bool FeatureRevolve2ShouldSucceed { get; set; } = true;

            public SimpleComMock<ModelDoc2> DocMock;
            public SimpleComMock<SketchManager> SketchMgrMock;
            public SimpleComMock<Sketch> SketchMock;
            public SimpleComMock<SketchSegment> AxisSegmentMock;
            public SimpleComMock<ModelDocExtension> ExtMock;
            public SimpleComMock<SelectionMgr> SelMgrMock;
            public SimpleComMock<FeatureManager> FeatMgrMock;
            public SimpleComMock<Feature> FeatureMock;
            public SimpleComMock<ISldWorks> AppMock;

            public MockCadSession()
            {
                // Feature Mock
                FeatureMock = new SimpleComMock<Feature>(call =>
                {
                    if (call.MethodName == "get_Name") return "Revolve1";
                    if (call.MethodName == "GetTypeName2") return "ProfileFeature";
                    return null;
                });

                // Sketch Mock
                SketchMock = new SimpleComMock<Sketch>(call =>
                {
                    if (call.MethodName == "GetSketchSegments") return new object[0];
                    return null;
                });

                // Axis Segment Mock
                AxisSegmentMock = new SimpleComMock<SketchSegment>(call =>
                {
                    if (call.MethodName == "Select2")
                    {
                        SelectedAxisAppend = (bool)call.Args[0];
                        SelectedAxisMark = (int)call.Args[1];
                        return true;
                    }
                    if (call.MethodName == "GetName") return "Line_Axis@Sketch1";
                    if (call.MethodName == "get_ConstructionGeometry") return true;
                    return null;
                });

                // SketchManager Mock
                SketchMgrMock = new SimpleComMock<SketchManager>(call =>
                {
                    if (call.MethodName == "get_ActiveSketch")
                    {
                        return SketchActive ? SketchMock.Proxy : null;
                    }
                    if (call.MethodName == "InsertSketch")
                    {
                        InsertSketchCalled = true;
                        SketchActive = !SketchActive;
                        return null;
                    }
                    if (call.MethodName == "CreateLine")
                    {
                        return AxisSegmentMock.Proxy;
                    }
                    return null;
                });

                // SelectionManager Mock
                SelMgrMock = new SimpleComMock<SelectionMgr>(call =>
                {
                    if (call.MethodName == "GetSelectedObjectCount2")
                    {
                        int mark = (int)call.Args[0];
                        if (mark == -1) return 2;
                        if (mark == 0) return 1;
                        if (mark == 4) return 1;
                        return 0;
                    }
                    return null;
                });

                // FeatureManager Mock
                FeatMgrMock = new SimpleComMock<FeatureManager>(call =>
                {
                    if (call.MethodName == "FeatureRevolve2")
                    {
                        FeatureRevolve2Called = true;
                        // Parameter 19: UseAutoSelect (index 19 in 0-based indexing)
                        if (call.Args.Length > 19 && call.Args[19] is bool)
                        {
                            FeatureRevolve2UseAutoSelect = (bool)call.Args[19];
                        }
                        return FeatureRevolve2ShouldSucceed ? FeatureMock.Proxy : null;
                    }
                    return null;
                });

                // Extension Mock
                ExtMock = new SimpleComMock<ModelDocExtension>(call =>
                {
                    if (call.MethodName == "SelectByID2")
                    {
                        SelectedSketchName = (string)call.Args[0];
                        string type = (string)call.Args[1];
                        SelectedSketchAppend = (bool)call.Args[5];
                        SelectedSketchMark = (int)call.Args[6];
                        return true;
                    }
                    return null;
                });

                // ModelDoc2 Mock
                DocMock = new SimpleComMock<ModelDoc2>(call =>
                {
                    if (call.MethodName == "get_SketchManager") return SketchMgrMock.Proxy;
                    if (call.MethodName == "get_Extension") return ExtMock.Proxy;
                    if (call.MethodName == "get_SelectionManager") return SelMgrMock.Proxy;
                    if (call.MethodName == "get_FeatureManager") return FeatMgrMock.Proxy;
                    if (call.MethodName == "GetTitle") return "Part1";
                    if (call.MethodName == "GetBodies2") return new object[1];
                    if (call.MethodName == "FirstFeature") return FeatureMock.Proxy;
                    if (call.MethodName == "ClearSelection2")
                    {
                        ClearSelectionCalled = true;
                        return null;
                    }
                    return null;
                });

                // ISldWorks App Mock
                AppMock = new SimpleComMock<ISldWorks>(call =>
                {
                    if (call.MethodName == "get_ActiveDoc") return DocMock.Proxy;
                    return null;
                });
            }
        }

        /// <summary>
        /// Test 1: Revolve dengan sketch yang masih aktif (harus otomatis di-close lalu di-revolve).
        /// </summary>
        [TestMethod]
        public void Test01_RevolveWithActiveSketch_AutomaticallyClosesSketchAndRevolves()
        {
            var session = new MockCadSession { SketchActive = true };
            var service = new SolidWorksAutomationService(session.AppMock.Proxy);

            string featName;
            bool success = service.RevolvedBoss(
                session.DocMock.Proxy,
                2.0 * Math.PI,
                "Sketch1",
                session.AxisSegmentMock.Proxy,
                out featName);

            Assert.IsTrue(success, "RevolvedBoss harus berhasil.");
            Assert.IsTrue(session.InsertSketchCalled, "InsertSketch(true) harus dipanggil untuk menutup sketch yang aktif.");
            Assert.IsFalse(session.SketchActive, "Sketch harus berstatus tertutup setelah operasi.");
            Assert.AreEqual("Revolve1", featName, "Nama feature yang dihasilkan harus sesuai.");
        }

        /// <summary>
        /// Test 2: Revolve setelah EndSketch sudah dipanggil (ActiveSketch == null, tapi revolve tetap berhasil).
        /// </summary>
        [TestMethod]
        public void Test02_RevolveAfterEndSketch_ActiveSketchIsNull_RevolveSucceeds()
        {
            var session = new MockCadSession { SketchActive = false }; // Sketch already closed by prior EndSketch
            var service = new SolidWorksAutomationService(session.AppMock.Proxy);

            string featName;
            bool success = service.RevolvedBoss(
                session.DocMock.Proxy,
                2.0 * Math.PI,
                "Sketch1",
                session.AxisSegmentMock.Proxy,
                out featName);

            Assert.IsTrue(success, "RevolvedBoss harus berhasil meskipun ActiveSketch bernilai null.");
            Assert.AreEqual("Revolve1", featName);
        }

        /// <summary>
        /// Test 3: Seleksi sketch dan axis menghasilkan Mark = 0 dan Mark = 4.
        /// UseAutoSelect harus bernilai false karena seleksi sudah eksplisit.
        /// </summary>
        [TestMethod]
        public void Test03_SketchAndAxisSelection_ProduceMark0AndMark4()
        {
            var session = new MockCadSession { SketchActive = false };
            var service = new SolidWorksAutomationService(session.AppMock.Proxy);

            string featName;
            bool success = service.RevolvedBoss(
                session.DocMock.Proxy,
                2.0 * Math.PI,
                "Sketch1",
                session.AxisSegmentMock.Proxy,
                out featName);

            Assert.IsTrue(success);
            Assert.IsTrue(session.ClearSelectionCalled, "ClearSelection2 harus dipanggil sebelum seleksi baru.");
            Assert.AreEqual("Sketch1", session.SelectedSketchName, "Profile sketch harus terpilih.");
            Assert.AreEqual(0, session.SelectedSketchMark, "Profile sketch harus memiliki Mark = 0.");
            Assert.IsFalse(session.SelectedSketchAppend, "Seleksi profile sketch pertama harus replace (Append = false).");

            Assert.AreEqual(4, session.SelectedAxisMark, "Axis of revolution harus memiliki Mark = 4.");
            Assert.IsTrue(session.SelectedAxisAppend, "Seleksi axis harus Append = true agar sketch tidak ter-deselect.");

            Assert.IsTrue(session.FeatureRevolve2Called, "FeatureRevolve2 harus dipanggil.");
            Assert.IsFalse(session.FeatureRevolve2UseAutoSelect, "UseAutoSelect harus false karena seleksi dilakukan secara manual/eksplisit.");
        }

        /// <summary>
        /// Test 4: ActiveSketch bernilai null tidak menyebabkan NullReferenceException.
        /// </summary>
        [TestMethod]
        public void Test04_ActiveSketchNull_DoesNotCauseNullReferenceException()
        {
            var session = new MockCadSession { SketchActive = false };
            var service = new SolidWorksAutomationService(session.AppMock.Proxy);

            string featName1;
            // 1. Revolve with null doc must safely return false
            bool res1 = service.RevolvedBoss(null, 2.0 * Math.PI, "Sketch1", null, out featName1);
            Assert.IsFalse(res1);
            Assert.IsNull(featName1);

            // 2. Revolve with doc having null ActiveSketch must not throw NullReferenceException
            string featName2;
            bool res2 = service.RevolvedBoss(session.DocMock.Proxy, 2.0 * Math.PI, "Sketch1", session.AxisSegmentMock.Proxy, out featName2);
            Assert.IsTrue(res2);
            Assert.IsNotNull(featName2);
        }

        /// <summary>
        /// Test 5: Axis tidak ditemukan menghasilkan kegagalan terprediksi (bukan exception tak tertangani).
        /// </summary>
        [TestMethod]
        public void Test05_AxisNotFound_ProducesPredictableFailureWithoutUnhandledException()
        {
            var session = new MockCadSession { SketchActive = false };
            var service = new SolidWorksAutomationService(session.AppMock.Proxy);

            string featName;
            // Pass null axisSegment and no tracked axis
            bool success = service.RevolvedBoss(
                session.DocMock.Proxy,
                2.0 * Math.PI,
                "Sketch1",
                null,
                out featName);

            Assert.IsFalse(success, "RevolvedBoss harus gagal secara terprediksi saat axis tidak ditemukan.");
            Assert.IsNull(featName, "featName harus bernilai null jika RevolvedBoss gagal.");
            Assert.IsFalse(session.FeatureRevolve2Called, "FeatureRevolve2 tidak boleh dipanggil jika axis tidak ditemukan.");
        }

        /// <summary>
        /// Test 6: FeatureRevolve2 menghasilkan null tidak dilaporkan sebagai sukses.
        /// </summary>
        [TestMethod]
        public void Test06_FeatureRevolve2ReturningNull_IsNotReportedAsSuccess()
        {
            var session = new MockCadSession
            {
                SketchActive = false,
                FeatureRevolve2ShouldSucceed = false // FeatureRevolve2 returns null
            };
            var service = new SolidWorksAutomationService(session.AppMock.Proxy);

            string featName;
            bool success = service.RevolvedBoss(
                session.DocMock.Proxy,
                2.0 * Math.PI,
                "Sketch1",
                session.AxisSegmentMock.Proxy,
                out featName);

            Assert.IsFalse(success, "RevolvedBoss HARUS mengembalikan false jika FeatureRevolve2 mengembalikan null.");
            Assert.IsNull(featName, "featName HARUS null jika pembuatan feature gagal.");
        }

        /// <summary>
        /// Test 7: Stepped shaft pipeline berhenti jika Revolve gagal (tidak melanjutkan ke Cut-Extrude).
        /// </summary>
        [TestMethod]
        public void Test07_SteppedShaftPipeline_StopsIfRevolveFails_DoesNotProceedToCutExtrude()
        {
            // Create full stepped shaft spec:
            // sk_shaft -> poly_shaft -> rev_shaft -> sk_hole -> circ_hole -> cut_hole
            var spec = new CadSpec
            {
                RequestId = "test_stepped_shaft_failfast",
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "sk_shaft",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object> { { "plane", "Front" } }
                    },
                    new CadOperation
                    {
                        Id = "poly_shaft",
                        Type = OperationType.AddPolyline,
                        Parameters = new Dictionary<string, object>
                        {
                            { "points", new List<double[]> { new double[] { 0, 0 }, new double[] { 0, 10 }, new double[] { 20, 10 }, new double[] { 20, 0 } } },
                            { "closed", true }
                        },
                        DependsOn = new List<string> { "sk_shaft" }
                    },
                    new CadOperation
                    {
                        Id = "rev_shaft",
                        Type = OperationType.RevolvedBoss,
                        Parameters = new Dictionary<string, object> { { "angle", 360.0 } },
                        DependsOn = new List<string> { "poly_shaft" }
                    },
                    new CadOperation
                    {
                        Id = "sk_hole",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object> { { "plane", "Right" } },
                        DependsOn = new List<string> { "rev_shaft" }
                    },
                    new CadOperation
                    {
                        Id = "circ_hole",
                        Type = OperationType.AddCircle,
                        Parameters = new Dictionary<string, object> { { "diameter", 8.0 } },
                        DependsOn = new List<string> { "sk_hole" }
                    },
                    new CadOperation
                    {
                        Id = "cut_hole",
                        Type = OperationType.ExtrudeCut,
                        Parameters = new Dictionary<string, object> { { "throughAll", true } },
                        DependsOn = new List<string> { "circ_hole" }
                    }
                }
            };

            var session = new MockCadSession
            {
                SketchActive = false,
                FeatureRevolve2ShouldSucceed = false // Revolve fails!
            };
            var service = new SolidWorksAutomationService(session.AppMock.Proxy);

            var execResult = service.ExecuteSpec(spec);

            // Assert execution failed
            Assert.IsFalse(execResult.Success, "Eksekusi harus dinyatakan gagal jika Revolve gagal.");
            Assert.AreEqual(ExecutionStatus.Failed, execResult.OverallStatus);
            Assert.IsNotNull(execResult.FailedOperation);
            Assert.AreEqual("rev_shaft", execResult.FailedOperation.OperationId, "FailedOperation harus mereferensikan rev_shaft.");

            // Verify fail-fast: subsequent operations MUST NOT be executed
            var executedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var op in execResult.CompletedOperations)
            {
                if (op.Success) executedIds.Add(op.OperationId);
            }

            Assert.IsFalse(executedIds.Contains("sk_hole"), "sk_hole tidak boleh dijalankan jika rev_shaft gagal.");
            Assert.IsFalse(executedIds.Contains("circ_hole"), "circ_hole tidak boleh dijalankan jika rev_shaft gagal.");
            Assert.IsFalse(executedIds.Contains("cut_hole"), "cut_hole tidak boleh dijalankan jika rev_shaft gagal.");
        }
    }
}

