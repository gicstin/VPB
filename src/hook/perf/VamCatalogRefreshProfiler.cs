using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using HarmonyLib;

namespace VPB
{
    internal static class VamCatalogRefreshProfiler
    {
        [ThreadStatic] static Scope s_Current;
        static int s_HookMask;

        internal sealed class Scope
        {
            internal Scope Previous;
            internal string Reason;
            internal int ThreadId;
            internal int NativeDepth;
            internal readonly int[] Exceptions = new int[4];
            internal readonly long[] Ticks = new long[4];
            internal readonly int[] Calls = new int[4];
        }

        internal struct CallToken
        {
            internal Scope Owner;
            internal long Started;
            internal bool NativeRoot;
        }

        internal static Scope Begin(string reason)
        {
            try
            {
                if (!VamStartupProfiler.CachedEnabled) return null;
                var scope = new Scope
                {
                    Previous = s_Current,
                    Reason = reason ?? "unknown",
                    ThreadId = Thread.CurrentThread.ManagedThreadId
                };
                s_Current = scope;
                return scope;
            }
            catch { return null; }
        }

        internal static void End(Scope scope)
        {
            if (scope == null) return;
            if (!ReferenceEquals(s_Current, scope)) return;
            s_Current = scope.Previous;
            try
            {
                double toMs = 1000.0 / Stopwatch.Frequency;
                LogUtil.Log(string.Format(CultureInfo.InvariantCulture,
                    "[VPB.Catalog.Timing] reason={0} native_ms={1:F2} native_calls={2}"
                    + " metadata_ms={3:F2} metadata_calls={4} group_init_ms={5:F2} group_init_calls={6}"
                    + " morph_handler_ms={7:F2} morph_handler_calls={8}"
                    + " inclusive=1 scope_thread={9} hooks={10}{11}{12}{13}"
                    + " native_exceptions={14} metadata_exceptions={15} group_init_exceptions={16} morph_handler_exceptions={17}",
                    scope.Reason, scope.Ticks[0] * toMs, scope.Calls[0],
                    scope.Ticks[1] * toMs, scope.Calls[1], scope.Ticks[2] * toMs, scope.Calls[2],
                    scope.Ticks[3] * toMs, scope.Calls[3], scope.ThreadId,
                    (s_HookMask & 1) != 0 ? 1 : 0, (s_HookMask & 2) != 0 ? 1 : 0,
                    (s_HookMask & 4) != 0 ? 1 : 0, (s_HookMask & 8) != 0 ? 1 : 0,
                    scope.Exceptions[0], scope.Exceptions[1], scope.Exceptions[2], scope.Exceptions[3]));
            }
            catch { }
        }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null) return;
            s_HookMask = 0;
            Patch(harmony, typeof(MVR.FileManagement.FileManager), "Refresh", nameof(PreNative), nameof(FinalizeNative), 1);
            Patch(harmony, typeof(MVR.FileManagement.VarPackage), "LoadMetaData", nameof(PreMetadata), nameof(FinalizeMetadata), 2);
            Patch(harmony, typeof(MVR.FileManagement.VarPackageGroup), "Init", nameof(PreGroup), nameof(FinalizeGroup), 4);
            Patch(harmony, typeof(DAZCharacterRun), "RefreshPackageMorphs", nameof(PreMorphHandler), nameof(FinalizeMorphHandler), 8);
        }

        static void Patch(Harmony harmony, Type type, string name, string prefix, string finalizer, int mask)
        {
            try
            {
                var method = AccessTools.Method(type, name, Type.EmptyTypes);
                if (method == null || method.ReturnType != typeof(void))
                    throw new MissingMethodException(type.FullName, name);
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(VamCatalogRefreshProfiler), prefix),
                    finalizer: new HarmonyMethod(typeof(VamCatalogRefreshProfiler), finalizer));
                s_HookMask |= mask;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.Catalog.Timing] patch failed " + type.Name + "." + name + ": " + ex.Message); }
                catch { }
            }
        }

        static CallToken Start(bool native)
        {
            var owner = s_Current;
            if (owner == null) return default(CallToken);
            try
            {
                var token = new CallToken { Owner = owner, Started = Stopwatch.GetTimestamp() };
                if (native) token.NativeRoot = owner.NativeDepth++ == 0;
                return token;
            }
            catch { return default(CallToken); }
        }

        static Exception Finish(CallToken token, int bucket, Exception exception)
        {
            if (token.Owner == null) return exception;
            try
            {
                long ticks = Stopwatch.GetTimestamp() - token.Started;
                var owner = token.Owner;
                owner.Calls[bucket]++;
                if (exception != null) owner.Exceptions[bucket]++;
                if (bucket == 0)
                {
                    owner.NativeDepth--;
                    if (token.NativeRoot) owner.Ticks[0] += ticks;
                }
                else
                    owner.Ticks[bucket] += ticks;
            }
            catch { }
            return exception;
        }

        internal static void PreNative(out CallToken __state) { __state = Start(true); }
        internal static void PreMetadata(out CallToken __state) { __state = Start(false); }
        internal static void PreGroup(out CallToken __state) { __state = Start(false); }
        internal static void PreMorphHandler(out CallToken __state) { __state = Start(false); }

        internal static Exception FinalizeNative(CallToken __state, Exception __exception) { return Finish(__state, 0, __exception); }
        internal static Exception FinalizeMetadata(CallToken __state, Exception __exception) { return Finish(__state, 1, __exception); }
        internal static Exception FinalizeGroup(CallToken __state, Exception __exception) { return Finish(__state, 2, __exception); }
        internal static Exception FinalizeMorphHandler(CallToken __state, Exception __exception) { return Finish(__state, 3, __exception); }
    }
}
