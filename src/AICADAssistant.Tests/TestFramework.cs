using System;

namespace Microsoft.VisualStudio.TestTools.UnitTesting
{
    [AttributeUsage(AttributeTargets.Class)]
    public class TestClassAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestMethodAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestInitializeAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestCleanupAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class ExpectedExceptionAttribute : Attribute
    {
        public Type ExceptionType { get; private set; }
        public ExpectedExceptionAttribute(Type exceptionType)
        {
            ExceptionType = exceptionType;
        }
    }

    public class AssertFailedException : Exception
    {
        public AssertFailedException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void IsTrue(bool condition, string message = null)
        {
            if (!condition)
                throw new AssertFailedException(message ?? "Assert.IsTrue failed.");
        }

        public static void IsFalse(bool condition, string message = null)
        {
            if (condition)
                throw new AssertFailedException(message ?? "Assert.IsFalse failed.");
        }

        public static void AreEqual<T>(T expected, T actual, string message = null)
        {
            if (!object.Equals(expected, actual))
                throw new AssertFailedException(string.Format("{0} Expected: <{1}>. Actual: <{2}>.", message ?? "Assert.AreEqual failed.", expected, actual));
        }

        public static void AreEqual(double expected, double actual, double delta, string message = null)
        {
            if (Math.Abs(expected - actual) > delta)
                throw new AssertFailedException(string.Format("{0} Expected: <{1}> within delta <{2}>. Actual: <{3}>.", message ?? "Assert.AreEqual failed.", expected, delta, actual));
        }

        public static void IsNotNull(object value, string message = null)
        {
            if (value == null)
                throw new AssertFailedException(message ?? "Assert.IsNotNull failed.");
        }

        public static void IsNull(object value, string message = null)
        {
            if (value != null)
                throw new AssertFailedException(message ?? "Assert.IsNull failed.");
        }

        public static void Fail(string message = null)
        {
            throw new AssertFailedException(message ?? "Assert.Fail.");
        }
    }
}
