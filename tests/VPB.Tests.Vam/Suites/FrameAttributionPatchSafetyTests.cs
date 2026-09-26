using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
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

        public sealed class InstrumentedProbe
        {
            public int Steps;

            public bool MoveNext()
            {
                Steps++;
                if (Steps > 3) return false;
                for (int i = 0; i < 2; i++)
                {
                    if (Steps == 2 && i == 1) return true;
                }
                try
                {
                    if (Steps == 3) throw new InvalidOperationException("probe");
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
                return true;
            }
        }

        [Fact]
        public void AnInstrumentedMethodBehavesExactlyAsBeforeAndIsTimedOnlyWhileArmed()
        {
            var harmony = new Harmony("vpb.tests.frame-attribution");
            MethodInfo moveNext = typeof(InstrumentedProbe).GetMethod("MoveNext");
            bool wasEnabled = VamFrameAttributionProfiler.Enabled;
            try
            {
                VamFrameAttributionProfiler.TryPatch(harmony, typeof(InstrumentedProbe), "MoveNext",
                    new HarmonyMethod(typeof(VamFrameAttributionProfiler), "Instrument"));
                Assert.True(VamFrameAttributionProfiler.CallsRecordedFor(moveNext) >= 0, "Precondition: the probe did not get a profiler slot.");

                var idle = new InstrumentedProbe();
                idle.MoveNext();
                Assert.Equal(0L, VamFrameAttributionProfiler.CallsRecordedFor(moveNext));

                VamFrameAttributionProfiler.Enabled = true;
                VamFrameAttributionProfiler.BeginLoad();
                var probe = new InstrumentedProbe();
                var results = new List<bool>();
                for (int i = 0; i < 5; i++) results.Add(probe.MoveNext());
                long calls = VamFrameAttributionProfiler.CallsRecordedFor(moveNext);
                VamFrameAttributionProfiler.EndLoad();

                Assert.True(new[] { true, true, true, false, false }.SequenceEqual(results),
                    "An instrumented Update/MoveNext returned different values than the original, so enabling " +
                    "AttributeSlowFrames would change how VaM coroutines and behaviours run.");
                Assert.True(calls == 5,
                    "Every return path, including loop exits and returns from a catch block, must record its time; got " + calls + ".");
            }
            finally
            {
                VamFrameAttributionProfiler.Enabled = wasEnabled;
                harmony.UnpatchAll(harmony.Id);
            }
        }

        [Fact]
        public void TailCallsAreLeftUninstrumented()
        {
            var body = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Tailcall),
                new CodeInstruction(OpCodes.Call, typeof(Math).GetMethod("Abs", new[] { typeof(int) })),
                new CodeInstruction(OpCodes.Ret)
            };
            Assert.False(VamFrameAttributionProfiler.CanInstrument(body),
                "A tail. call must be followed directly by ret; inserting the timing call between them makes the method unloadable.");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        static extern void MissingInternalCall();

        static void CallsMissingInternalCall()
        {
            MissingInternalCall();
        }

        [Fact]
        public void MethodCallingPluginInternalCallIsSkipped()
        {
            MethodInfo method = typeof(FrameAttributionPatchSafetyTests).GetMethod(nameof(CallsMissingInternalCall), BindingFlags.Static | BindingFlags.NonPublic);
            Assert.True(VamFrameAttributionProfiler.CallsGameNativeMethod(method),
                "Rewriting a game or plugin internal call can prevent scene-loading coroutines from completing.");
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

        static IEnumerable<int> SynchronousIterator()
        {
            yield return 1;
        }

        static System.Collections.IEnumerator CoroutineIterator()
        {
            yield return null;
        }

        [Fact]
        public void SynchronousIteratorsAreSkippedButCoroutinesStayProfiled()
        {
            Type iterator = SynchronousIterator().GetType();
            Type coroutine = CoroutineIterator().GetType();

            Assert.True(VamFrameAttributionProfiler.IsSynchronousIterator(iterator),
                "An IEnumerable iterator runs inside its caller's frame; profiling it double-counts time already charged to the caller and adds startup patch cost.");
            Assert.False(VamFrameAttributionProfiler.IsSynchronousIterator(coroutine),
                "A coroutine state machine is what Unity drives per frame; skipping it blinds the slow-frame profiler to coroutine work.");
        }

        [Fact]
        public void FastNativeScanAgreesWithHarmonyScanOnEveryProfiledMethod()
        {
            var assemblies = new[] { typeof(SuperController).Assembly, typeof(VamFrameAttributionProfiler).Assembly };
            int compared = 0;
            int unparsed = 0;
            var mismatches = new List<string>();
            foreach (Assembly asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                foreach (Type t in types)
                {
                    if (t == null || t.IsInterface || t.IsGenericTypeDefinition) continue;
                    foreach (string name in new[] { "Update", "LateUpdate", "FixedUpdate", "MoveNext" })
                    {
                        MethodInfo m;
                        try
                        {
                            m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                                null, Type.EmptyTypes, null);
                        }
                        catch { continue; }
                        if (m == null || m.IsAbstract || m.ContainsGenericParameters) continue;
                        byte[] il = VamFrameAttributionProfiler.IlBytes(m);
                        if (il == null) continue;
                        bool ignored;
                        if (!VamIlCallScanner.TryScan(m, il, c => false, out ignored)) unparsed++;
                        bool viaProfiler = VamFrameAttributionProfiler.CallsGameNativeMethod(m, il);
                        bool slow = VamFrameAttributionProfiler.CallsGameNativeMethodSlow(m);
                        compared++;
                        if (viaProfiler != slow) mismatches.Add(t.FullName + "." + name + " fast=" + viaProfiler + " harmony=" + slow);
                    }
                }
            }

            Assert.True(compared > 200, "Precondition: expected hundreds of Update/MoveNext bodies to compare, got " + compared + ".");
            Assert.True(unparsed * 20 < compared,
                "The fast IL scan could not read " + unparsed + " of " + compared + " bodies; each falls back to Harmony's slow scan and startup patching stays slow.");
            Assert.True(mismatches.Count == 0,
                "The fast IL scan disagrees with Harmony's scan, so the profiler would either rewrite a method that binds a native call (hanging or crashing it in game) or stop profiling a safe one: "
                + string.Join("; ", mismatches.Take(10).ToArray()));
        }

        [Fact]
        public void DeferredPatchingCoversVamBeforeVpbAndCheapMethodsFirst()
        {
            Func<string, int, VamFrameAttributionProfiler.PatchCandidateInfo> make = (asm, il) =>
                new VamFrameAttributionProfiler.PatchCandidateInfo
                {
                    Label = asm + "/" + il,
                    AssemblyName = asm,
                    AssemblyRank = VamFrameAttributionProfiler.AssemblyRank(asm),
                    IlLength = il
                };
            var list = new List<VamFrameAttributionProfiler.PatchCandidateInfo>
            {
                make("VPB", 10), make("SteamVR", 500), make("Assembly-CSharp", 9000), make("Assembly-CSharp", 40), make("VPB", 5000)
            };
            list.Sort(VamFrameAttributionProfiler.ComparePatchOrder);

            Assert.True(new[] { "Assembly-CSharp/40", "Assembly-CSharp/9000", "SteamVR/500", "VPB/10", "VPB/5000" }
                    .SequenceEqual(list.Select(c => c.Label)),
                "Patching runs a few ms per frame after startup; VaM's own methods must be covered first so the first scene loads are attributed, "
                + "and cheap methods first so most coverage arrives before the expensive ones. Got: " + string.Join(", ", list.Select(c => c.Label).ToArray()));
        }
    }
}
