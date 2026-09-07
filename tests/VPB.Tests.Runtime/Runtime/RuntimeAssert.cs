using System;
using System.Collections.Generic;

namespace VPB.Tests.Runtime
{
    public sealed class RuntimeAssertException : Exception
    {
        public RuntimeAssertException(string message) : base(message) { }
    }

    public sealed class RuntimeSkipException : Exception
    {
        public RuntimeSkipException(string reason) : base(reason) { }
    }

    public static class RuntimeAssert
    {
        public static void Inconclusive(string reason)
        {
            throw new RuntimeSkipException(reason);
        }

        public static void True(bool condition, string message)
        {
            if (!condition) throw new RuntimeAssertException(message);
        }

        public static void False(bool condition, string message)
        {
            if (condition) throw new RuntimeAssertException(message);
        }

        public static void NotNull(object value, string message)
        {
            if (value == null) throw new RuntimeAssertException(message);
        }

        public static void Null(object value, string message)
        {
            if (value != null) throw new RuntimeAssertException(message + " (got " + Describe(value) + ")");
        }

        public static void Equal(object expected, object actual, string message)
        {
            if (Equals(expected, actual)) return;
            throw new RuntimeAssertException(
                message + Environment.NewLine +
                "  expected: " + Describe(expected) + Environment.NewLine +
                "  actual:   " + Describe(actual));
        }

        public static void NotEqual(object notExpected, object actual, string message)
        {
            if (!Equals(notExpected, actual)) return;
            throw new RuntimeAssertException(message + " (both were " + Describe(actual) + ")");
        }

        public static void InRange(long value, long minInclusive, long maxInclusive, string message)
        {
            if (value >= minInclusive && value <= maxInclusive) return;
            throw new RuntimeAssertException(
                message + " (got " + value + ", expected " + minInclusive + ".." + maxInclusive + ")");
        }

        public static void Approximately(float expected, float actual, float tolerance, string message)
        {
            float delta = expected > actual ? expected - actual : actual - expected;
            if (delta <= tolerance) return;
            throw new RuntimeAssertException(
                message + " (expected " + expected + " +/- " + tolerance + ", got " + actual + ")");
        }

        public static void Contains<T>(ICollection<T> collection, T value, string message)
        {
            if (collection != null && collection.Contains(value)) return;
            throw new RuntimeAssertException(message + " (looking for " + Describe(value) + ")");
        }

        public static Exception Throws(Action action, string message)
        {
            try { action(); }
            catch (Exception ex) { return ex; }
            throw new RuntimeAssertException(message + " (nothing was thrown)");
        }

        public static void DoesNotThrow(Action action, string message)
        {
            try { action(); }
            catch (Exception ex)
            {
                throw new RuntimeAssertException(message + Environment.NewLine + "  threw " + Unwrap(ex));
            }
        }

        public static string Unwrap(Exception ex)
        {
            Exception e = ex;
            while (e.InnerException != null && (e is System.Reflection.TargetInvocationException || e is TypeInitializationException))
                e = e.InnerException;
            return e.GetType().Name + ": " + e.Message;
        }

        private static string Describe(object value)
        {
            if (value == null) return "<null>";
            string s = value as string;
            if (s != null) return "\"" + s + "\"";
            return value.ToString();
        }
    }
}
