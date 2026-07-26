using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using SimpleJSON;

namespace VPB
{
    // Diagnostic-only patches for issue #52: MacGruber StateMachine state list empty under VPB.
    // Discriminates between three hypotheses on the same atom:
    //   1. Atom.lastRestoredData gets overwritten between plugin#0 and plugin#1 CreateScriptController
    //   2. JSONStorable.insideRestore is false when plugin#0 CreateScriptController runs (RestoreFromLast skipped)
    //   3. JSONStorable.RestoreFromJSON receives a thinned JSON node for plugin#0
    //
    // Gated by Settings.Instance.LogStateMachineApply; harmless overhead when disabled (early-return).
    // Filter is narrow: only logs for atoms whose uid contains "_logic" and storables whose id contains
    // "MacGruber.StateMachine". Reduces volume to a handful of lines per scene load.
    public static class StateMachineDiagnostic
    {
        const string AtomFilterFragment = "_logic";
        const string StorableFilterFragment = "MacGruber.StateMachine";

        static bool s_Patched;
        static FieldInfo s_InsideRestoreField;
        static FieldInfo s_LastRestoredDataField;

        public static void PatchAll(Harmony harmony)
        {
            if (s_Patched) return;
            try
            {
                s_InsideRestoreField = AccessTools.Field(typeof(JSONStorable), "insideRestore");
                s_LastRestoredDataField = AccessTools.Field(typeof(Atom), "lastRestoredData");

                var mAtomRestore = AccessTools.Method(
                    typeof(Atom),
                    "Restore",
                    new[]
                    {
                        typeof(JSONClass), typeof(bool), typeof(bool), typeof(bool),
                        typeof(JSONArray), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
                    });
                if (mAtomRestore != null)
                {
                    harmony.Patch(
                        mAtomRestore,
                        prefix: new HarmonyMethod(typeof(StateMachineDiagnostic), nameof(PreAtomRestore)));
                }

                var mRestoreFromLast = AccessTools.Method(typeof(Atom), "RestoreFromLast", new[] { typeof(JSONStorable) });
                if (mRestoreFromLast != null)
                {
                    harmony.Patch(
                        mRestoreFromLast,
                        prefix: new HarmonyMethod(typeof(StateMachineDiagnostic), nameof(PreRestoreFromLast)));
                }

                var mCreateScriptController = AccessTools.Method(typeof(MVRPluginManager), "CreateScriptController");
                if (mCreateScriptController != null)
                {
                    harmony.Patch(
                        mCreateScriptController,
                        prefix: new HarmonyMethod(typeof(StateMachineDiagnostic), nameof(PreCreateScriptController)),
                        postfix: new HarmonyMethod(typeof(StateMachineDiagnostic), nameof(PostCreateScriptController)));
                }

                var mLateRestore = AccessTools.Method(
                    typeof(MVRPluginManager),
                    "LateRestoreFromJSON",
                    new[] { typeof(JSONClass), typeof(bool), typeof(bool), typeof(bool) });
                if (mLateRestore != null)
                {
                    harmony.Patch(
                        mLateRestore,
                        prefix: new HarmonyMethod(typeof(StateMachineDiagnostic), nameof(PreLateRestoreFromJSON)),
                        postfix: new HarmonyMethod(typeof(StateMachineDiagnostic), nameof(PostLateRestoreFromJSON)));
                }

                var mJsRestore = AccessTools.Method(
                    typeof(JSONStorable),
                    "RestoreFromJSON",
                    new[] { typeof(JSONClass), typeof(bool), typeof(bool), typeof(JSONArray), typeof(bool) });
                if (mJsRestore != null)
                {
                    harmony.Patch(
                        mJsRestore,
                        postfix: new HarmonyMethod(typeof(StateMachineDiagnostic), nameof(PostJsRestoreFromJSON)));
                }

                // LateRestoreFromJSON is the call that populates MacGruber's myStates list. The base
                // method gets called from inside MacGruber's override; patching the base IL fires our
                // postfix on every base call including via overrides. Empty jc here means states clear.
                var mJsLateRestore = AccessTools.Method(
                    typeof(JSONStorable),
                    "LateRestoreFromJSON",
                    new[] { typeof(JSONClass), typeof(bool), typeof(bool), typeof(bool) });
                if (mJsLateRestore != null)
                {
                    harmony.Patch(
                        mJsLateRestore,
                        postfix: new HarmonyMethod(typeof(StateMachineDiagnostic), nameof(PostJsLateRestoreFromJSON)));
                }

                // Atom.LateRestore drives the second-pass storable cleanup that calls
                // LateRestoreFromJSON(new JSONClass()) on any storable not found in the jc.
                // Logging entry lets us correlate which atom.LateRestore call wiped plugin#0.
                var mAtomLateRestore = AccessTools.Method(
                    typeof(Atom),
                    "LateRestore",
                    new[] { typeof(JSONClass), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
                if (mAtomLateRestore != null)
                {
                    harmony.Patch(
                        mAtomLateRestore,
                        prefix: new HarmonyMethod(typeof(StateMachineDiagnostic), nameof(PreAtomLateRestore)));
                }

                s_Patched = true;
                LogUtil.Log("[VPB.DiagSM] patches registered (active only when LogStateMachineApply=true)");
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.DiagSM] PatchAll failed: " + ex.Message);
            }
        }

        static bool Enabled()
        {
            try { return Settings.Instance != null && Settings.Instance.LogStateMachineApply != null && Settings.Instance.LogStateMachineApply.Value; }
            catch { return false; }
        }

        public static void PreAtomRestore(Atom __instance, JSONClass jc, bool isSubSceneRestore)
        {
            if (!Enabled() || __instance == null || jc == null) return;
            string uid = __instance.uid ?? "";
            if (uid.IndexOf(AtomFilterFragment, StringComparison.OrdinalIgnoreCase) < 0) return;

            int total = 0;
            int matched = 0;
            var sb = new StringBuilder();
            var storables = jc["storables"].AsArray;
            if (storables != null)
            {
                for (int i = 0; i < storables.Count; i++)
                {
                    total++;
                    string id = storables[i]["id"];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (id.IndexOf(StorableFilterFragment, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    matched++;
                    int statesCount = -1;
                    var statesNode = storables[i]["States"];
                    if (statesNode != null && statesNode.AsArray != null) statesCount = statesNode.AsArray.Count;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(id).Append("(States=").Append(statesCount).Append(")");
                }
            }
            LogUtil.Log("[VPB.DiagSM] Atom.Restore uid=" + uid + " isSubSceneRestore=" + isSubSceneRestore
                + " totalStorables=" + total + " matchedSM=" + matched + " [" + sb + "]");
        }

        public static void PreRestoreFromLast(Atom __instance, JSONStorable js)
        {
            if (!Enabled() || __instance == null || js == null) return;
            string storeId = js.storeId ?? "";
            if (storeId.IndexOf(StorableFilterFragment, StringComparison.OrdinalIgnoreCase) < 0) return;
            string uid = __instance.uid ?? "";

            JSONClass lrd = null;
            try { lrd = s_LastRestoredDataField != null ? (JSONClass)s_LastRestoredDataField.GetValue(__instance) : null; } catch { }

            int statesInLrd = -1;
            bool matchedInLrd = false;
            if (lrd != null)
            {
                var storables = lrd["storables"].AsArray;
                if (storables != null)
                {
                    for (int i = 0; i < storables.Count; i++)
                    {
                        if ((string)storables[i]["id"] == storeId)
                        {
                            matchedInLrd = true;
                            var statesNode = storables[i]["States"];
                            if (statesNode != null && statesNode.AsArray != null) statesInLrd = statesNode.AsArray.Count;
                            break;
                        }
                    }
                }
            }
            LogUtil.Log("[VPB.DiagSM] Atom.RestoreFromLast uid=" + uid + " storeId=" + storeId
                + " lastRestoredDataNull=" + (lrd == null) + " matchedInLrd=" + matchedInLrd + " statesInLrd=" + statesInLrd);
        }

        public static void PreCreateScriptController(MVRPluginManager __instance, MVRPlugin mvrp)
        {
            if (!Enabled() || __instance == null) return;
            string atomUid = "";
            try { atomUid = __instance.containingAtom != null ? __instance.containingAtom.uid : ""; } catch { }
            if (atomUid.IndexOf(AtomFilterFragment, StringComparison.OrdinalIgnoreCase) < 0) return;

            string url = "";
            try { url = mvrp != null && mvrp.pluginURLJSON != null ? mvrp.pluginURLJSON.val : ""; } catch { }
            string mvrpUid = "";
            try { mvrpUid = mvrp != null ? mvrp.uid : ""; } catch { }

            bool insideRestore = false;
            try { if (s_InsideRestoreField != null) insideRestore = (bool)s_InsideRestoreField.GetValue(__instance); } catch { }

            LogUtil.Log("[VPB.DiagSM] CreateScriptController PRE atom=" + atomUid + " mvrp=" + mvrpUid
                + " url=" + url + " insideRestore=" + insideRestore);
        }

        public static void PostCreateScriptController(MVRPluginManager __instance, MVRPlugin mvrp)
        {
            if (!Enabled() || __instance == null) return;
            string atomUid = "";
            try { atomUid = __instance.containingAtom != null ? __instance.containingAtom.uid : ""; } catch { }
            if (atomUid.IndexOf(AtomFilterFragment, StringComparison.OrdinalIgnoreCase) < 0) return;
            string mvrpUid = "";
            try { mvrpUid = mvrp != null ? mvrp.uid : ""; } catch { }
            bool insideRestore = false;
            try { if (s_InsideRestoreField != null) insideRestore = (bool)s_InsideRestoreField.GetValue(__instance); } catch { }
            LogUtil.Log("[VPB.DiagSM] CreateScriptController POST atom=" + atomUid + " mvrp=" + mvrpUid + " insideRestore=" + insideRestore);
        }

        public static void PreLateRestoreFromJSON(MVRPluginManager __instance, JSONClass jc)
        {
            if (!Enabled() || __instance == null || jc == null) return;
            string atomUid = "";
            try { atomUid = __instance.containingAtom != null ? __instance.containingAtom.uid : ""; } catch { }
            if (atomUid.IndexOf(AtomFilterFragment, StringComparison.OrdinalIgnoreCase) < 0) return;

            var plugins = jc["plugins"].AsObject;
            var sb = new StringBuilder();
            if (plugins != null)
            {
                foreach (string k in plugins.Keys)
                {
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(k).Append('=').Append((string)plugins[k]);
                }
            }
            LogUtil.Log("[VPB.DiagSM] MVRPluginManager.LateRestoreFromJSON PRE atom=" + atomUid + " plugins=[" + sb + "]");
        }

        public static void PostLateRestoreFromJSON(MVRPluginManager __instance)
        {
            if (!Enabled() || __instance == null) return;
            string atomUid = "";
            try { atomUid = __instance.containingAtom != null ? __instance.containingAtom.uid : ""; } catch { }
            if (atomUid.IndexOf(AtomFilterFragment, StringComparison.OrdinalIgnoreCase) < 0) return;
            LogUtil.Log("[VPB.DiagSM] MVRPluginManager.LateRestoreFromJSON POST atom=" + atomUid);
        }

        public static void PostJsRestoreFromJSON(JSONStorable __instance, JSONClass jc)
        {
            if (!Enabled() || __instance == null || jc == null) return;
            string storeId = __instance.storeId ?? "";
            if (storeId.IndexOf(StorableFilterFragment, StringComparison.OrdinalIgnoreCase) < 0) return;
            string atomUid = "";
            try { atomUid = __instance.containingAtom != null ? __instance.containingAtom.uid : ""; } catch { }

            int statesCount = -1;
            var statesNode = jc["States"];
            if (statesNode != null && statesNode.AsArray != null) statesCount = statesNode.AsArray.Count;
            int jcKeys = 0;
            try { jcKeys = jc.Count; } catch { }

            LogUtil.Log("[VPB.DiagSM] JSONStorable.RestoreFromJSON POST atom=" + atomUid + " storeId=" + storeId
                + " jcKeys=" + jcKeys + " statesCount=" + statesCount);
        }

        public static void PostJsLateRestoreFromJSON(JSONStorable __instance, JSONClass jc)
        {
            if (!Enabled() || __instance == null || jc == null) return;
            string storeId = __instance.storeId ?? "";
            if (storeId.IndexOf(StorableFilterFragment, StringComparison.OrdinalIgnoreCase) < 0) return;
            string atomUid = "";
            try { atomUid = __instance.containingAtom != null ? __instance.containingAtom.uid : ""; } catch { }

            int statesCount = -1;
            var statesNode = jc["States"];
            if (statesNode != null && statesNode.AsArray != null) statesCount = statesNode.AsArray.Count;
            int jcKeys = 0;
            try { jcKeys = jc.Count; } catch { }

            // Bracket with caller frame to identify which Atom.LateRestore branch invoked us
            // (the "missing storable cleanup" branch passes new JSONClass() so jcKeys=0).
            string stackHint = "";
            try
            {
                var st = new System.Diagnostics.StackTrace(2, false);
                int frames = Math.Min(st.FrameCount, 4);
                var sb = new StringBuilder();
                for (int i = 0; i < frames; i++)
                {
                    var f = st.GetFrame(i);
                    var m = f != null ? f.GetMethod() : null;
                    if (m == null) continue;
                    if (sb.Length > 0) sb.Append("<-");
                    sb.Append(m.DeclaringType != null ? m.DeclaringType.Name : "?").Append('.').Append(m.Name);
                }
                stackHint = sb.ToString();
            }
            catch { }

            LogUtil.Log("[VPB.DiagSM] JSONStorable.LateRestoreFromJSON POST atom=" + atomUid + " storeId=" + storeId
                + " jcKeys=" + jcKeys + " statesCount=" + statesCount + " stack=" + stackHint);
        }

        public static void PreAtomLateRestore(Atom __instance, JSONClass jc, bool isSubSceneRestore, bool setMissingToDefault)
        {
            if (!Enabled() || __instance == null) return;
            string uid = __instance.uid ?? "";
            if (uid.IndexOf(AtomFilterFragment, StringComparison.OrdinalIgnoreCase) < 0) return;

            int total = 0;
            int matched = 0;
            if (jc != null)
            {
                var storables = jc["storables"].AsArray;
                if (storables != null)
                {
                    for (int i = 0; i < storables.Count; i++)
                    {
                        total++;
                        string id = storables[i]["id"];
                        if (!string.IsNullOrEmpty(id) && id.IndexOf(StorableFilterFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                            matched++;
                    }
                }
            }
            LogUtil.Log("[VPB.DiagSM] Atom.LateRestore uid=" + uid + " jcNull=" + (jc == null)
                + " isSubSceneRestore=" + isSubSceneRestore + " setMissingToDefault=" + setMissingToDefault
                + " totalStorables=" + total + " matchedSM=" + matched);
        }
    }
}
