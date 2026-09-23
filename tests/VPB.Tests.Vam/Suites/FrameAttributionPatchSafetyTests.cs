using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class FrameAttributionPatchSafetyTests
    {
        public FrameAttributionPatchSafetyTests(VamFixture vam) { }

        [DllImport("vpb_missing_native_library")]
        static extern void MissingNative();

        static void CallsMissingNative()
        {
            MissingNative();
        }

        static double CallsFrameworkOnly(double v)
        {
            return Math.Sqrt(v) + Math.Abs(v);
        }

        [Fact]
        public void SceneLoadCoroutineIsNeverRewrittenByTheProfiler()
        {
            Type loadCo = null;
            foreach (Type nested in typeof(SuperController).GetNestedTypes(BindingFlags.NonPublic))
            {
                if (nested.Name.StartsWith("<LoadCo>", StringComparison.Ordinal))
                {
                    loadCo = nested;
                    break;
                }
            }
            Assert.True(loadCo != null, "Precondition: SuperController.LoadCo state machine must exist in this VaM build.");

            MethodInfo moveNext = loadCo.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.True(VamFrameAttributionProfiler.CallsGameNativeMethod(moveNext),
                "LoadCo calls VaM's EnableGC/DisableGC internal calls; a Harmony copy cannot bind them, so profiling it throws inside the scene-load coroutine and every scene load hangs.");
        }

        [Fact]
        public void MethodCallingPluginPInvokeIsSkipped()
        {
            MethodInfo m = typeof(FrameAttributionPatchSafetyTests).GetMethod(nameof(CallsMissingNative), BindingFlags.Static | BindingFlags.NonPublic);

            Assert.True(VamFrameAttributionProfiler.CallsGameNativeMethod(m),
                "A method calling a game or plugin native import must not be rewritten by the slow-frame profiler.");
        }

        [Fact]
        public void MethodCallingOnlyFrameworkIntrinsicsStaysProfiled()
        {
            MethodInfo m = typeof(FrameAttributionPatchSafetyTests).GetMethod(nameof(CallsFrameworkOnly), BindingFlags.Static | BindingFlags.NonPublic);

            Assert.False(VamFrameAttributionProfiler.CallsGameNativeMethod(m),
                "Framework intrinsics like Math.Sqrt bind fine from patched copies; skipping them would blind the profiler to most frames.");
        }
    }
}
