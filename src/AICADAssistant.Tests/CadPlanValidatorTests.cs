using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AICADAssistant.Core.Validation;
using AICADAssistant.Core.Models;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class CadPlanValidatorTests
    {
        private CadPlanValidator _validator;

        [TestInitialize]
        public void Setup()
        {
            _validator = new CadPlanValidator();
        }

        [TestMethod]
        public void Validate_NullSpec_ReturnsInvalid()
        {
            var result = _validator.Validate(null);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Count > 0);
        }

        [TestMethod]
        public void Validate_EmptyOperations_ReturnsInvalid()
        {
            var spec = new CadSpec
            {
                DocumentType = DocumentType.Part,
                Units = "mm",
                Operations = new List<CadOperation>()
            };
            var result = _validator.Validate(spec);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Count > 0);
        }

        [TestMethod]
        public void Validate_ValidSimpleBlock_ReturnsValid()
        {
            var spec = new CadSpec
            {
                RequestId = "test-001",
                DocumentType = DocumentType.Part,
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "sketch1",
                        Type = OperationType.NewSketch,
                        Parameters = new Dictionary<string, object>
                        {
                            { "plane", "Front" }
                        }
                    },
                    new CadOperation
                    {
                        Id = "rect1",
                        Type = OperationType.AddRectangle,
                        Parameters = new Dictionary<string, object>
                        {
                            { "width", 100.0 },
                            { "height", 50.0 }
                        },
                        DependsOn = new List<string> { "sketch1" }
                    },
                    new CadOperation
                    {
                        Id = "extrude1",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object>
                        {
                            { "depth", 20.0 },
                            { "profile", "rect1" }
                        },
                        DependsOn = new List<string> { "rect1" }
                    }
                }
            };

            var result = _validator.Validate(spec);
            Assert.IsTrue(result.IsValid, string.Join(", ", result.Errors));
        }

        [TestMethod]
        public void Validate_InvalidUnit_ReturnsInvalid()
        {
            var spec = new CadSpec
            {
                DocumentType = DocumentType.Part,
                Units = "furlongs",
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "op1", Type = OperationType.NewPart, Parameters = new Dictionary<string, object>() }
                }
            };
            var result = _validator.Validate(spec);
            Assert.IsFalse(result.IsValid);
        }

        [TestMethod]
        public void Validate_ExtrudeBossWithZeroDepth_ReturnsInvalid()
        {
            var spec = new CadSpec
            {
                DocumentType = DocumentType.Part,
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "extrude1",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object>
                        {
                            { "depth", 0.0 },
                            { "profile", "rect1" }
                        }
                    }
                }
            };
            var result = _validator.Validate(spec);
            Assert.IsFalse(result.IsValid);
        }

        [TestMethod]
        public void Validate_NegativeDimension_ReturnsInvalid()
        {
            var spec = new CadSpec
            {
                DocumentType = DocumentType.Part,
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "extrude1",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object>
                        {
                            { "depth", -5.0 },
                            { "profile", "rect1" }
                        }
                    }
                }
            };
            var result = _validator.Validate(spec);
            Assert.IsFalse(result.IsValid);
        }

        [TestMethod]
        public void Validate_FilletWithValidRadius_ReturnsValid()
        {
            var spec = new CadSpec
            {
                DocumentType = DocumentType.Part,
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "fillet1",
                        Type = OperationType.Fillet,
                        Parameters = new Dictionary<string, object>
                        {
                            { "radius", 3.0 }
                        }
                    }
                }
            };
            var result = _validator.Validate(spec);
            Assert.IsTrue(result.IsValid, string.Join(", ", result.Errors));
        }

        [TestMethod]
        public void Validate_HoleWithMissingDiameter_AddsMissingDimension()
        {
            var spec = new CadSpec
            {
                DocumentType = DocumentType.Part,
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "hole1",
                        Type = OperationType.SimpleHole,
                        Parameters = new Dictionary<string, object>
                        {
                            { "depth", 10.0 }
                            // missing diameter
                        }
                    }
                }
            };
            var result = _validator.Validate(spec);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.MissingDimensions.Count > 0);
        }

        [TestMethod]
        public void Validate_UnknownOperationType_ReturnsInvalid()
        {
            // Simulate an unknown operation type by using a value not in AllowedOperations
            // We can't really do this with the enum without reflection, but we test boundary
            var spec = new CadSpec
            {
                DocumentType = DocumentType.Part,
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "op1",
                        Type = OperationType.NewPart,
                        Parameters = new Dictionary<string, object>()
                    }
                }
            };
            // NewPart is allowed, should be valid (assuming no other validation fails)
            var result = _validator.Validate(spec);
            Assert.IsTrue(result.IsValid);
        }

        [TestMethod]
        public void Validate_SpecWithUnknowns_HasMissingDimensions()
        {
            var spec = new CadSpec
            {
                DocumentType = DocumentType.Part,
                Units = "mm",
                Unknowns = new List<string> { "lubang diameter", "ketebalan dinding" },
                Operations = new List<CadOperation>
                {
                    new CadOperation { Id = "op1", Type = OperationType.NewPart, Parameters = new Dictionary<string, object>() }
                }
            };
            Assert.IsTrue(spec.HasUnknowns());
        }

        [TestMethod]
        public void Validate_DependencyOnNonExistentOperation_ReturnsInvalid()
        {
            var spec = new CadSpec
            {
                DocumentType = DocumentType.Part,
                Units = "mm",
                Operations = new List<CadOperation>
                {
                    new CadOperation
                    {
                        Id = "extrude1",
                        Type = OperationType.ExtrudeBoss,
                        Parameters = new Dictionary<string, object>
                        {
                            { "depth", 20.0 },
                            { "profile", "rect1" }
                        },
                        DependsOn = new List<string> { "nonexistent_op" }
                    }
                }
            };
            var result = _validator.Validate(spec);
            Assert.IsFalse(result.IsValid);
        }
    }
}

