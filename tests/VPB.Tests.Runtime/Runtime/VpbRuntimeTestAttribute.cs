using System;

namespace VPB.Tests.Runtime
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class VpbRuntimeTestAttribute : Attribute
    {
        public string Reason;

        public float TimeoutSeconds;

        public bool Skip;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class VpbRuntimeSuiteAttribute : Attribute
    {
        public string Name;

        public int Order;
    }
}
