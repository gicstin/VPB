using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace VPB
{
    internal static class VamFrameAttributionProfiler
    {
        internal static bool Enabled;
        internal static int ReportFrameMs = 250;
        internal static int MaxPatchedMethods = 6000;

        internal static int PatchedCount;
        internal static int PatchFailures;

        const int TopPerFrame = 6;
        const int TopPerLoad = 12;

        static readonly List<string> s_SlotNames = new List<string>(1024);
        static long[] s_SlotTicks = new long[1024];
        static long[] s_SlotCalls = new long[1024];
        static long[] s_FrameBaseline = new long[1024];
        static readonly Dictionary<MethodBase, int> s_SlotOf = new Dictionary<MethodBase, int>();

        static bool s_Armed;
        static long s_FrameStart;
        static readonly StringBuilder s_Sb = new StringBuilder(256);

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !Enabled) return;
            var sw = Stopwatch.StartNew();
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!IsCandidateAssembly(asm)) continue;
                    PatchAssembly(harmony, asm);
                    if (PatchedCount >= MaxPatchedMethods) break;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Perf] frame attribution patching aborted: " + ex.Message);
            }
            sw.Stop();
            LogUtil.LogWarning("[VPB.Perf] frame attribution armed: patched=" + PatchedCount
                + " failed=" + PatchFailures + " in " + sw.ElapsedMilliseconds + "ms");
        }

        static bool IsCandidateAssembly(Assembly asm)
        {
            string n;
            try { n = asm.GetName().Name; }
            catch { return false; }
            if (string.IsNullOrEmpty(n)) return false;
            if (n.StartsWith("UnityEngine", StringComparison.Ordinal)) return false;
            if (n.StartsWith("System", StringComparison.Ordinal)) return false;
            if (n.StartsWith("Mono.", StringComparison.Ordinal)) return false;
            if (n.StartsWith("0Harmony", StringComparison.Ordinal)) return false;
            if (n.StartsWith("BepInEx", StringComparison.Ordinal)) return false;
            if (n == "mscorlib" || n == "netstandard") return false;
            return true;
        }

        static void PatchAssembly(Harmony harmony, Assembly asm)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { return; }

            var prefix = new HarmonyMethod(typeof(VamFrameAttributionProfiler), nameof(Pre));
            var finalizer = new HarmonyMethod(typeof(VamFrameAttributionProfiler), nameof(Post));

            for (int i = 0; i < types.Length; i++)
            {
                if (PatchedCount >= MaxPatchedMethods) return;
                Type t = types[i];
                if (t == null || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (t == typeof(VamFrameAttributionProfiler)) continue;

                bool isBehaviour = !t.IsValueType && typeof(MonoBehaviour).IsAssignableFrom(t);
                bool isEnumerator = !t.IsValueType && typeof(IEnumerator).IsAssignableFrom(t);
                if (!isBehaviour && !isEnumerator) continue;

                if (isBehaviour)
                {
                    TryPatch(harmony, t, "Update", prefix, finalizer);
                    TryPatch(harmony, t, "LateUpdate", prefix, finalizer);
                    TryPatch(harmony, t, "FixedUpdate", prefix, finalizer);
                }
                if (isEnumerator)
                {
                    TryPatch(harmony, t, "MoveNext", prefix, finalizer);
                }
            }
        }

        static void TryPatch(Harmony harmony, Type t, string name, HarmonyMethod prefix, HarmonyMethod finalizer)
        {
            try
            {
                MethodInfo m = t.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (m == null || m.IsAbstract || m.ContainsGenericParameters) return;
                if (s_SlotOf.ContainsKey(m)) return;

                int slot = NewSlot(Label(t, name));
                s_SlotOf[m] = slot;
                harmony.Patch(m, prefix: prefix, finalizer: finalizer);
                PatchedCount++;
            }
            catch
            {
                PatchFailures++;
            }
        }

        static string Label(Type t, string method)
        {
            string typeName = t.Name;
            if (typeName.Length > 0 && typeName[0] == '<' && t.DeclaringType != null)
            {
                int close = typeName.IndexOf('>');
                string inner = close > 1 ? typeName.Substring(1, close - 1) : typeName;
                return t.DeclaringType.Name + "." + inner + "()";
            }
            return typeName + "." + method;
        }

        static int NewSlot(string label)
        {
            int slot = s_SlotNames.Count;
            s_SlotNames.Add(label);
            if (slot >= s_SlotTicks.Length)
            {
                int cap = s_SlotTicks.Length * 2;
                Array.Resize(ref s_SlotTicks, cap);
                Array.Resize(ref s_SlotCalls, cap);
                Array.Resize(ref s_FrameBaseline, cap);
            }
            return slot;
        }

        public static void Pre(out long __state)
        {
            __state = s_Armed ? Stopwatch.GetTimestamp() : 0L;
        }

        public static void Post(MethodBase __originalMethod, long __state)
        {
            if (__state == 0L) return;
            int slot;
            if (!s_SlotOf.TryGetValue(__originalMethod, out slot)) return;
            s_SlotTicks[slot] += Stopwatch.GetTimestamp() - __state;
            s_SlotCalls[slot]++;
        }

        public static void BeginLoad()
        {
            if (!Enabled) return;
            for (int i = 0; i < s_SlotTicks.Length; i++) { s_SlotTicks[i] = 0; s_SlotCalls[i] = 0; s_FrameBaseline[i] = 0; }
            s_FrameStart = Stopwatch.GetTimestamp();
            s_Armed = true;
        }

        public static void Tick()
        {
            if (!s_Armed) return;
            long now = Stopwatch.GetTimestamp();
            double frameMs = (now - s_FrameStart) * 1000.0 / Stopwatch.Frequency;
            s_FrameStart = now;

            if (frameMs >= ReportFrameMs) ReportFrame(frameMs);

            int n = s_SlotNames.Count;
            for (int i = 0; i < n; i++) s_FrameBaseline[i] = s_SlotTicks[i];
        }

        static void ReportFrame(double frameMs)
        {
            try
            {
                s_Sb.Length = 0;
                s_Sb.Append("[VPB.Perf] SLOW_FRAME ");
                s_Sb.Append(frameMs.ToString("0"));
                s_Sb.Append("ms | ");

                double attributedMs = 0.0;
                int n = s_SlotNames.Count;
                for (int i = 0; i < n; i++)
                    attributedMs += (s_SlotTicks[i] - s_FrameBaseline[i]) * 1000.0 / Stopwatch.Frequency;

                s_Sb.Append("attributed=");
                s_Sb.Append(attributedMs.ToString("0"));
                s_Sb.Append("ms unattributed=");
                s_Sb.Append(Math.Max(0.0, frameMs - attributedMs).ToString("0"));
                s_Sb.Append("ms | top=");
                AppendTop(s_Sb, TopPerFrame, true);
                LogUtil.LogWarning(s_Sb.ToString());
            }
            catch { }
        }

        public static void EndLoad()
        {
            if (!s_Armed) return;
            s_Armed = false;
            try
            {
                s_Sb.Length = 0;
                s_Sb.Append("[VPB.Perf] LOAD_ATTRIBUTION patched=");
                s_Sb.Append(PatchedCount);
                s_Sb.Append(" | top=");
                for (int i = 0; i < s_SlotNames.Count; i++) s_FrameBaseline[i] = 0;
                AppendTop(s_Sb, TopPerLoad, false);
                LogUtil.LogWarning(s_Sb.ToString());
            }
            catch { }
        }

        static void AppendTop(StringBuilder sb, int count, bool frameDelta)
        {
            int n = s_SlotNames.Count;
            for (int rank = 0; rank < count; rank++)
            {
                int best = -1;
                long bestTicks = 0;
                for (int i = 0; i < n; i++)
                {
                    long ticks = frameDelta ? s_SlotTicks[i] - s_FrameBaseline[i] : s_SlotTicks[i];
                    if (ticks <= bestTicks) continue;
                    bool taken = false;
                    for (int k = 0; k < rank; k++) if (s_PickBuf[k] == i) { taken = true; break; }
                    if (taken) continue;
                    bestTicks = ticks;
                    best = i;
                }
                if (best < 0) break;
                s_PickBuf[rank] = best;

                double ms = bestTicks * 1000.0 / Stopwatch.Frequency;
                if (ms < 1.0) break;
                if (rank > 0) sb.Append(", ");
                sb.Append(s_SlotNames[best]);
                sb.Append('=');
                sb.Append(ms.ToString("0"));
                sb.Append("ms");
                if (!frameDelta)
                {
                    sb.Append('/');
                    sb.Append(s_SlotCalls[best]);
                }
            }
        }

        static readonly int[] s_PickBuf = new int[TopPerLoad + 1];
    }
}
