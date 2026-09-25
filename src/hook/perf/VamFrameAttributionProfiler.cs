using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
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
        internal static int NativeCallSkips;
        internal static int IteratorSkips;
        internal static int EmptyBodySkips;
        internal static int LargeBodySkips;
        internal static int MaxInstrumentIlBytes = 12000;
        internal static long PatchBudgetMsPerFrame = 6;

        const int TopPerFrame = 6;
        const int TopPerLoad = 12;

        static readonly List<string> s_SlotNames = new List<string>(1024);
        static long[] s_SlotTicks = new long[1024];
        static long[] s_SlotCalls = new long[1024];
        static long[] s_FrameBaseline = new long[1024];
        static readonly Dictionary<MethodBase, int> s_SlotOf = new Dictionary<MethodBase, int>();

        static long s_GetTypesTicks;
        static long s_LookupTicks;
        static long s_NativeScanTicks;
        static long s_PatchTicks;
        static int s_FastScanFallbacks;
        static readonly Dictionary<string, PatchCost> s_AssemblyCosts = new Dictionary<string, PatchCost>();
        static readonly List<PatchCandidateInfo> s_Pending = new List<PatchCandidateInfo>();
        static readonly HashSet<MethodBase> s_Queued = new HashSet<MethodBase>();
        static readonly List<string> s_LargeSkips = new List<string>();
        static int s_PendingNext;
        static int s_PumpFrames;
        static long s_PumpStartedAt;
        static Harmony s_Harmony;
        static HarmonyMethod s_Transpiler;

        internal sealed class PatchCandidateInfo
        {
            public MethodInfo Method;
            public string Label;
            public int IlLength;
            public string AssemblyName;
            public int AssemblyRank;
        }
        static readonly List<PatchCost> s_SlowestPatches = new List<PatchCost>();
        const int SlowestPatchesShown = 8;

        sealed class PatchCost
        {
            public string Label;
            public long Ticks;
            public int Count;
        }

        static MethodInfo s_EnterMethod;
        static MethodInfo s_ExitMethod;

        static bool s_Armed;
        static long s_FrameStart;
        static readonly StringBuilder s_Sb = new StringBuilder(256);

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !Enabled) return;
            s_Harmony = harmony;
            var sw = Stopwatch.StartNew();
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!IsCandidateAssembly(asm)) continue;
                    CollectAssembly(asm);
                    if (s_Pending.Count >= MaxPatchedMethods) break;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Perf] frame attribution scan aborted: " + ex.Message);
            }
            s_Pending.Sort(ComparePatchOrder);
            sw.Stop();
            LogUtil.LogWarning("[VPB.Perf] frame attribution queued: candidates=" + s_Pending.Count
                + " skipped_native_calls=" + NativeCallSkips + " skipped_iterators=" + IteratorSkips
                + " skipped_empty=" + EmptyBodySkips + " skipped_large=" + LargeBodySkips
                + " in " + sw.ElapsedMilliseconds + "ms | patching runs " + PatchBudgetMsPerFrame
                + "ms per frame after startup, paused during scene loads" + LargeSkipList());
        }

        internal static int PendingCount { get { return s_Pending.Count - s_PendingNext; } }

        public static void PumpPendingPatches()
        {
            if (s_Harmony == null || s_PendingNext >= s_Pending.Count) return;
            if (!LogUtil.IsStartupReadyLogged() || LogUtil.IsSceneLoading()) return;

            if (s_PumpStartedAt == 0L) s_PumpStartedAt = Stopwatch.GetTimestamp();
            s_PumpFrames++;
            long frameStarted = Stopwatch.GetTimestamp();
            long budgetTicks = PatchBudgetMsPerFrame * Stopwatch.Frequency / 1000L;
            var transpiler = Transpiler();
            do
            {
                PatchCandidate(s_Harmony, s_Pending[s_PendingNext++], transpiler);
            }
            while (s_PendingNext < s_Pending.Count && Stopwatch.GetTimestamp() - frameStarted < budgetTicks);

            if (s_PendingNext < s_Pending.Count) return;
            long wallTicks = Stopwatch.GetTimestamp() - s_PumpStartedAt;
            LogUtil.LogWarning("[VPB.Perf] frame attribution armed: patched=" + PatchedCount
                + " failed=" + PatchFailures + " frames=" + s_PumpFrames
                + " wall=" + TicksToMs(wallTicks) + "ms | " + PhaseBreakdown());
            s_Pending.Clear();
            s_PendingNext = 0;
        }

        internal static int ComparePatchOrder(PatchCandidateInfo a, PatchCandidateInfo b)
        {
            int byAssembly = a.AssemblyRank.CompareTo(b.AssemblyRank);
            if (byAssembly != 0) return byAssembly;
            return a.IlLength.CompareTo(b.IlLength);
        }

        internal static int AssemblyRank(string name)
        {
            if (name == "Assembly-CSharp") return 0;
            if (name == "VPB") return 2;
            return 1;
        }

        static string LargeSkipList()
        {
            if (s_LargeSkips.Count == 0) return "";
            var sb = new StringBuilder(128);
            sb.Append(" | skipped_large_methods=");
            for (int i = 0; i < s_LargeSkips.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(s_LargeSkips[i]);
            }
            return sb.ToString();
        }

        static HarmonyMethod Transpiler()
        {
            return s_Transpiler ?? (s_Transpiler = new HarmonyMethod(typeof(VamFrameAttributionProfiler), nameof(Instrument)));
        }

        static string PhaseBreakdown()
        {
            var sb = new StringBuilder(256);
            sb.Append("get_types=").Append(TicksToMs(s_GetTypesTicks))
                .Append("ms;method_lookup=").Append(TicksToMs(s_LookupTicks))
                .Append("ms;native_scan=").Append(TicksToMs(s_NativeScanTicks))
                .Append("ms;harmony_patch=").Append(TicksToMs(s_PatchTicks))
                .Append("ms;native_scan_slow_fallbacks=").Append(s_FastScanFallbacks)
                .Append(" | assemblies=").Append(s_AssemblyCosts.Count).Append(" top_assemblies=");
            var costs = new List<PatchCost>(s_AssemblyCosts.Values);
            costs.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
            int shown = Math.Min(5, costs.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(',');
                PatchCost cost = costs[i];
                sb.Append(cost.Label).Append('=').Append(TicksToMs(cost.Ticks)).Append("ms/").Append(cost.Count)
                    .Append(" avg=").Append(cost.Count > 0 ? (cost.Ticks * 1000.0 / Stopwatch.Frequency / cost.Count).ToString("0.0") : "0").Append("ms");
            }
            sb.Append(" | slowest_patches=");
            for (int i = 0; i < s_SlowestPatches.Count; i++)
            {
                if (i > 0) sb.Append(',');
                PatchCost cost = s_SlowestPatches[i];
                sb.Append(cost.Label).Append('=').Append(TicksToMs(cost.Ticks)).Append("ms/il").Append(cost.Count);
            }
            return sb.ToString();
        }

        static void RecordSlowPatch(string label, long ticks, int ilBytes)
        {
            if (s_SlowestPatches.Count >= SlowestPatchesShown && ticks <= s_SlowestPatches[s_SlowestPatches.Count - 1].Ticks) return;
            int at = s_SlowestPatches.Count;
            while (at > 0 && s_SlowestPatches[at - 1].Ticks < ticks) at--;
            s_SlowestPatches.Insert(at, new PatchCost { Label = label, Ticks = ticks, Count = ilBytes });
            if (s_SlowestPatches.Count > SlowestPatchesShown) s_SlowestPatches.RemoveAt(s_SlowestPatches.Count - 1);
        }

        static void RecordAssemblyPatch(string assembly, long ticks)
        {
            PatchCost cost;
            if (!s_AssemblyCosts.TryGetValue(assembly, out cost))
            {
                cost = new PatchCost { Label = assembly };
                s_AssemblyCosts[assembly] = cost;
            }
            cost.Ticks += ticks;
            cost.Count++;
        }

        static long TicksToMs(long ticks)
        {
            return ticks * 1000L / Stopwatch.Frequency;
        }

        static string AssemblyName(Assembly asm)
        {
            try { return asm.GetName().Name; }
            catch { return "?"; }
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

        static void CollectAssembly(Assembly asm)
        {
            Type[] types;
            long typesStarted = Stopwatch.GetTimestamp();
            try { types = asm.GetTypes(); }
            catch { return; }
            finally { s_GetTypesTicks += Stopwatch.GetTimestamp() - typesStarted; }

            string assemblyName = AssemblyName(asm);
            int rank = AssemblyRank(assemblyName);
            for (int i = 0; i < types.Length; i++)
            {
                if (s_Pending.Count >= MaxPatchedMethods) return;
                Type t = types[i];
                if (t == null || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (t == typeof(VamFrameAttributionProfiler)) continue;

                bool isBehaviour = !t.IsValueType && typeof(MonoBehaviour).IsAssignableFrom(t);
                bool isEnumerator = !t.IsValueType && typeof(IEnumerator).IsAssignableFrom(t);
                if (!isBehaviour && !isEnumerator) continue;
                if (isEnumerator && !isBehaviour && IsSynchronousIterator(t))
                {
                    IteratorSkips++;
                    continue;
                }

                if (isBehaviour)
                {
                    Enqueue(t, "Update", assemblyName, rank);
                    Enqueue(t, "LateUpdate", assemblyName, rank);
                    Enqueue(t, "FixedUpdate", assemblyName, rank);
                }
                if (isEnumerator)
                {
                    Enqueue(t, "MoveNext", assemblyName, rank);
                }
            }
        }

        static void Enqueue(Type t, string name, string assemblyName, int rank)
        {
            PatchCandidateInfo candidate = TryCreateCandidate(t, name);
            if (candidate == null || !s_Queued.Add(candidate.Method)) return;
            if (candidate.IlLength > MaxInstrumentIlBytes)
            {
                LargeBodySkips++;
                s_LargeSkips.Add(candidate.Label + "/il" + candidate.IlLength);
                return;
            }
            candidate.AssemblyName = assemblyName;
            candidate.AssemblyRank = rank;
            s_Pending.Add(candidate);
        }

        internal static bool IsSynchronousIterator(Type t)
        {
            return t != null && typeof(IEnumerator).IsAssignableFrom(t) && typeof(IEnumerable).IsAssignableFrom(t);
        }

        static PatchCandidateInfo TryCreateCandidate(Type t, string name)
        {
            try
            {
                long lookupStarted = Stopwatch.GetTimestamp();
                MethodInfo m = t.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                s_LookupTicks += Stopwatch.GetTimestamp() - lookupStarted;
                if (m == null || m.IsAbstract || m.ContainsGenericParameters) return null;
                if (s_SlotOf.ContainsKey(m)) return null;
                long scanStarted = Stopwatch.GetTimestamp();
                byte[] il = IlBytes(m);
                bool callsNative = CallsGameNativeMethod(m, il);
                s_NativeScanTicks += Stopwatch.GetTimestamp() - scanStarted;
                if (callsNative)
                {
                    NativeCallSkips++;
                    return null;
                }
                if (il != null && il.Length <= 1)
                {
                    EmptyBodySkips++;
                    return null;
                }
                return new PatchCandidateInfo
                {
                    Method = m,
                    Label = Label(t, name),
                    IlLength = il != null ? il.Length : -1,
                    AssemblyName = AssemblyName(t.Assembly)
                };
            }
            catch
            {
                PatchFailures++;
                return null;
            }
        }

        internal static void TryPatch(Harmony harmony, Type t, string name, HarmonyMethod transpiler)
        {
            PatchCandidateInfo candidate = TryCreateCandidate(t, name);
            if (candidate != null) PatchCandidate(harmony, candidate, transpiler);
        }

        static void PatchCandidate(Harmony harmony, PatchCandidateInfo candidate, HarmonyMethod transpiler)
        {
            if (s_SlotOf.ContainsKey(candidate.Method)) return;
            int slot = NewSlot(candidate.Label);
            s_SlotOf[candidate.Method] = slot;
            long patchStarted = Stopwatch.GetTimestamp();
            try
            {
                harmony.Patch(candidate.Method, transpiler: transpiler);
                PatchedCount++;
            }
            catch
            {
                PatchFailures++;
            }
            finally
            {
                long patchTicks = Stopwatch.GetTimestamp() - patchStarted;
                s_PatchTicks += patchTicks;
                RecordSlowPatch(candidate.Label, patchTicks, candidate.IlLength);
                RecordAssemblyPatch(candidate.AssemblyName ?? "?", patchTicks);
            }
        }

        internal static byte[] IlBytes(MethodBase method)
        {
            try
            {
                MethodBody body = method.GetMethodBody();
                return body != null ? body.GetILAsByteArray() : null;
            }
            catch { return null; }
        }

        internal static bool CallsGameNativeMethod(MethodBase method)
        {
            return CallsGameNativeMethod(method, IlBytes(method));
        }

        internal static bool CallsGameNativeMethod(MethodBase method, byte[] il)
        {
            bool callsNative;
            if (il != null && VamIlCallScanner.TryScan(method, il, IsGameNativeCallee, out callsNative))
                return callsNative;
            s_FastScanFallbacks++;
            return CallsGameNativeMethodSlow(method);
        }

        static bool IsGameNativeCallee(MethodBase callee)
        {
            bool native;
            try
            {
                native = (callee.Attributes & MethodAttributes.PinvokeImpl) != 0
                    || (callee.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0;
            }
            catch { native = true; }
            if (!native) return false;
            Type owner = callee.DeclaringType;
            return owner == null || IsCandidateAssembly(owner.Assembly);
        }

        internal static bool CallsGameNativeMethodSlow(MethodBase method)
        {
            List<CodeInstruction> body;
            try { body = PatchProcessor.GetOriginalInstructions(method); }
            catch { return true; }
            if (body == null) return true;
            for (int i = 0; i < body.Count; i++)
            {
                MethodBase callee = body[i].operand as MethodBase;
                if (callee != null && IsGameNativeCallee(callee)) return true;
            }
            return false;
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

        internal static long CallsRecordedFor(MethodBase method)
        {
            int slot;
            return method != null && s_SlotOf.TryGetValue(method, out slot) ? s_SlotCalls[slot] : -1L;
        }

        public static long Enter()
        {
            return s_Armed ? Stopwatch.GetTimestamp() : 0L;
        }

        public static void Exit(long started, int slot)
        {
            if (started == 0L) return;
            s_SlotTicks[slot] += Stopwatch.GetTimestamp() - started;
            s_SlotCalls[slot]++;
        }

        internal static IEnumerable<CodeInstruction> Instrument(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original)
        {
            var body = new List<CodeInstruction>(instructions);
            int slot;
            if (original == null || generator == null || !s_SlotOf.TryGetValue(original, out slot) || !CanInstrument(body))
                return body;

            MethodInfo enter = s_EnterMethod ?? (s_EnterMethod = AccessTools.Method(typeof(VamFrameAttributionProfiler), nameof(Enter)));
            MethodInfo exit = s_ExitMethod ?? (s_ExitMethod = AccessTools.Method(typeof(VamFrameAttributionProfiler), nameof(Exit)));
            LocalBuilder started = generator.DeclareLocal(typeof(long));

            var result = new List<CodeInstruction>(body.Count + 8);
            result.Add(new CodeInstruction(OpCodes.Call, enter));
            result.Add(new CodeInstruction(OpCodes.Stloc, started));
            for (int i = 0; i < body.Count; i++)
            {
                CodeInstruction instruction = body[i];
                if (instruction.opcode == OpCodes.Ret)
                {
                    var load = new CodeInstruction(OpCodes.Ldloc, started);
                    load.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks);
                    instruction.blocks.Clear();
                    result.Add(load);
                    result.Add(new CodeInstruction(OpCodes.Ldc_I4, slot));
                    result.Add(new CodeInstruction(OpCodes.Call, exit));
                }
                result.Add(instruction);
            }
            return result;
        }

        internal static bool CanInstrument(List<CodeInstruction> body)
        {
            if (body == null || body.Count == 0) return false;
            for (int i = 0; i < body.Count; i++)
            {
                OpCode op = body[i].opcode;
                if (op == OpCodes.Tailcall || op == OpCodes.Jmp) return false;
            }
            return true;
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
                if (PendingCount > 0)
                {
                    s_Sb.Append(" not_yet_patched=");
                    s_Sb.Append(PendingCount);
                }
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
