using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    /// <summary>
    /// Scene Tools: sticky power-user scene-authoring mode (toolbox toggle).
    /// When ON, toolbox reveals Scene Tools actions (Strip Scene, …). Distinct from Creators author list.
    /// </summary>
    public partial class GalleryPanel
    {
        private bool creatorModeActive;
        private bool creatorModeStripBusy;
        private Coroutine creatorModeStripRoutine;
        private GameObject tboxCreatorModeBtn;
        private GameObject tboxCreatorStripSceneBtn;
        private GameObject tboxCreatorCompressCacheBtn;
        private Image tboxCreatorModeBtnImage;
        private Image tboxCreatorStripSceneBtnImage;

        /// <summary>
        /// When true, SuperControllerHook.PostRemoveAtom skips per-atom gallery target sync.
        /// </summary>
        internal static bool SuppressAtomRemovedGalleryNotify;

        /// <summary>Side-rail / hotkey / QM toggle.</summary>
        internal void ToggleCreatorMode(bool fromLeftRailButton = false, bool rightClick = false)
        {
            if (creatorModeActive) ExitCreatorMode();
            else EnterCreatorMode();
            // fromLeft/rightClick reserved for future side-panel affinity (parity with Scene Eraser).
        }

        /// <summary>
        /// Open Strip keep selector. Enters Scene Tools when needed (hotkey / QM direct path).
        /// </summary>
        private void OpenSceneStripKeepSelector()
        {
            if (!creatorModeActive)
                EnterCreatorMode();
            TboxCreatorStripSceneOpenKeepSelector();
        }

        /// <summary>
        /// Ctrl+Shift+S — toggle Strip Scene window (canvas-hosted; independent of pane collapse).
        /// Enters Scene Tools when opening.
        /// </summary>
        private void HotkeyOpenStripSceneDirect()
        {
            if (IsStripKeepSelectorOpen())
            {
                HideStripKeepSelector();
                return;
            }
            OpenSceneStripKeepSelector();
        }

        private void TboxToggleCreatorMode()
        {
            ToggleCreatorMode();
        }

        private void EnterCreatorMode()
        {
            if (creatorModeActive) return;
            if (!GateStickyEnterWhileTryOn(StickyToolMode.Creator)) return;

            try { ExitOtherStickyToolModes(StickyToolMode.Creator); } catch { }

            creatorModeActive = true;
            RefreshCreatorModeChrome();
            try { RefreshTboxConditionalActionButtons(); } catch { }
            // Snapshot only — never Raise/ForceReset here (would abort an in-flight scene load).
            try { VpbLoadingIconUtil.LogFlagSnapshot("scene-tools-enter", raise: false); } catch { }
            try { RefreshModeAmbientChrome(); } catch { }
            ShowTemporaryStatus(
                VPBTranslation.T(
                    "gallery.creator.entered",
                    "Scene Tools on — strip/authoring ready. Esc exits."),
                2f);
            LogUtil.LogWarning("[VPB] Scene Tools ON");
        }

        private void ExitCreatorMode(bool force = false)
        {
            if (!creatorModeActive && !creatorModeStripBusy) return;

            if (creatorModeStripBusy && !force)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.creator.strip_busy", "Strip in progress — wait."),
                    1.5f);
                return;
            }

            try { HideStripKeepSelector(); } catch { }
            if (_stripKeepSubScenePickActive)
            {
                try { StripKeepAbortSubScenePickMode(reopenStrip: false); } catch { }
            }

            if (force && creatorModeStripRoutine != null)
            {
                try { StopCoroutine(creatorModeStripRoutine); } catch { }
                creatorModeStripRoutine = null;
                creatorModeStripBusy = false;
                SuppressAtomRemovedGalleryNotify = false;
                // Do not Raise/ForceReset while VaM Load from strip may still be running —
                // RecoverAfterBulkAtomTeardown would orphan mid-rebuild AsyncFlags.
                bool loadOngoing = false;
                try
                {
                    if (SuperController.singleton != null)
                        loadOngoing = SuperController.singleton.isLoading;
                }
                catch { }
                try { if (!loadOngoing && LogUtil.IsSceneLoading()) loadOngoing = true; } catch { }
                if (!loadOngoing)
                {
                    try { VpbLoadingIconUtil.RecoverAfterBulkAtomTeardown("scene-tools-force-exit"); } catch { }
                }
                else
                {
                    try { VpbLoadingIconUtil.LogFlagSnapshot("scene-tools-force-exit-load-ongoing"); } catch { }
                    LogUtil.LogWarning("[VPB] Scene Tools force-exit during strip load — left VaM load alone");
                }
            }

            creatorModeActive = false;
            RefreshCreatorModeChrome();
            try { RefreshTboxConditionalActionButtons(); } catch { }
            try { RefreshModeAmbientChrome(); } catch { }
            if (!force)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.creator.exited", "Scene Tools off."),
                    1.25f);
            }
            LogUtil.LogWarning("[VPB] Scene Tools OFF");
            try { ResetArmedApplySemanticsIfIdle(toast: !force); } catch { }
        }

        private void RefreshCreatorModeChrome()
        {
            if (tboxCreatorStripSceneBtnImage != null)
            {
                try
                {
                    tboxCreatorStripSceneBtnImage.color = IsStripKeepSelectorOpen()
                        ? new Color(0.72f, 0.22f, 0.22f, 1f)
                        : ColorDangerAllRow;
                }
                catch { }
            }
            if (tboxCreatorModeBtnImage != null)
            {
                try
                {
                    tboxCreatorModeBtnImage.color = creatorModeActive ? UI.AccentBlue : GalleryUiColorTokens.ChromeIconWell;
                }
                catch { }
            }
        }

        private static bool CreatorModeJsonAtomShouldKeep(JSONNode atomNode, HashSet<string> keepUids)
        {
            if (atomNode == null) return false;
            string id = null;
            try
            {
                if (atomNode["id"] != null) id = atomNode["id"].Value;
            }
            catch { }

            if (SceneUtils.IsSystemProtectedAtomId(id)) return true;
            // Environment sky/sphere — always drop for blank new-scene look.
            string atomType = null;
            try
            {
                if (atomNode["type"] != null) atomType = atomNode["type"].Value;
            }
            catch { atomType = null; }
            if (SceneUtils.IsCreatorStripAlwaysDropAtomType(atomType)) return false;
            if (!string.IsNullOrEmpty(id)
                && string.Equals(id, "Environment", StringComparison.Ordinal))
                return false;
            if (string.IsNullOrEmpty(id) || keepUids == null) return false;
            return keepUids.Contains(id);
        }

        private bool TryBuildCreatorStripKeepersSceneJson(
            HashSet<string> keepUids,
            Dictionary<string, string> renames,
            out string tempPath,
            out string undoScenePath,
            out int keepCount,
            out int dropCount)
        {
            tempPath = null;
            undoScenePath = null;
            keepCount = 0;
            dropCount = 0;
            SuperController sc = SuperController.singleton;
            if (sc == null) return false;

            JSONClass sceneJson = null;
            try { sceneJson = sc.GetSaveJSON(null, true, true); }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Creator Strip GetSaveJSON failed: " + ex.Message);
                return false;
            }
            if (sceneJson == null) return false;

            // Pre-strip full scene for Ctrl+Z (one GetSaveJSON — write before filter mutate).
            // Cold path: disk IO OK. LoadScene(merge:false) on undo matches strip replace.
            try
            {
                undoScenePath = Path.Combine(
                    sc.savesDir,
                    "vpb_temp_undo_redo_scene_" + Guid.NewGuid().ToString() + ".json");
                File.WriteAllText(undoScenePath, JsonSerializationUtil.Serialize(sceneJson, 200_000));
                if (!File.Exists(undoScenePath)) undoScenePath = null;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Creator Strip undo snapshot failed: " + ex.Message);
                undoScenePath = null;
            }

            JSONArray src = null;
            try { src = sceneJson["atoms"].AsArray; }
            catch { src = null; }
            if (src == null)
            {
                TryDeleteStripTempFile(undoScenePath);
                undoScenePath = null;
                return false;
            }

            JSONArray keepArr = new JSONArray();
            for (int i = 0; i < src.Count; i++)
            {
                JSONNode node = src[i];
                if (CreatorModeJsonAtomShouldKeep(node, keepUids))
                {
                    keepArr.Add(node);
                    keepCount++;
                }
                else
                {
                    dropCount++;
                }
            }
            sceneJson["atoms"] = keepArr;

            if (dropCount <= 0)
            {
                TryDeleteStripTempFile(undoScenePath);
                undoScenePath = null;
                return false;
            }

            // CoreControl always kept — blank Skyshop sphere so strip ≠ inherit old backdrop.
            CreatorStripBlankSkyInSceneJson(sceneJson);

            if (renames != null && renames.Count > 0)
                ApplyCreatorStripRenamesInJson(sceneJson, renames);

            try
            {
                tempPath = Path.Combine(
                    sc.savesDir,
                    "vpb_temp_creator_strip_" + Guid.NewGuid().ToString() + ".json");
                File.WriteAllText(tempPath, JsonSerializationUtil.Serialize(sceneJson, 200_000));
                return File.Exists(tempPath);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Creator Strip write temp failed: " + ex.Message);
                tempPath = null;
                TryDeleteStripTempFile(undoScenePath);
                undoScenePath = null;
                return false;
            }
        }

        private static void TryDeleteStripTempFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Remap atom ids in keepers JSON. Updates atom id, then rewrites atom-reference
        /// string values (parentAtom / receiverAtom / <c>uid:storable</c> links).
        /// Skips non-ref keys (<c>type</c>/<c>id</c>/urls) so renaming uid <c>Person</c>
        /// cannot corrupt every <c>"type":"Person"</c>. Cold path only.
        /// </summary>
        private static void ApplyCreatorStripRenamesInJson(JSONClass sceneJson, Dictionary<string, string> renames)
        {
            if (sceneJson == null || renames == null || renames.Count == 0) return;

            JSONArray atoms = null;
            try { atoms = sceneJson["atoms"].AsArray; } catch { atoms = null; }
            if (atoms == null) return;

            for (int i = 0; i < atoms.Count; i++)
            {
                JSONNode node = atoms[i];
                if (node == null) continue;
                string id = null;
                try
                {
                    if (node["id"] != null) id = node["id"].Value;
                }
                catch { id = null; }
                if (string.IsNullOrEmpty(id)) continue;
                string newId;
                if (!renames.TryGetValue(id, out newId) || string.IsNullOrEmpty(newId)) continue;
                if (string.Equals(id, newId, StringComparison.Ordinal)) continue;
                try { node["id"] = newId; } catch { }
            }

            // Two-phase remap: old→temp→new so Person→Actor1 + Actor1→Actor2 cannot collide.
            List<string> fromList = new List<string>(renames.Count);
            List<string> toList = new List<string>(renames.Count);
            Dictionary<string, string>.Enumerator en = renames.GetEnumerator();
            while (en.MoveNext())
            {
                string fromUid = en.Current.Key;
                string toUid = en.Current.Value;
                if (string.IsNullOrEmpty(fromUid) || string.IsNullOrEmpty(toUid)) continue;
                if (string.Equals(fromUid, toUid, StringComparison.Ordinal)) continue;
                fromList.Add(fromUid);
                toList.Add(toUid);
            }
            for (int i = 0; i < fromList.Count; i++)
            {
                string tempId = "__vpb_strip_ren_" + i.ToString();
                CreatorStripRemapAtomUidRefs(sceneJson, fromList[i], tempId);
            }
            for (int i = 0; i < fromList.Count; i++)
            {
                string tempId = "__vpb_strip_ren_" + i.ToString();
                CreatorStripRemapAtomUidRefs(sceneJson, tempId, toList[i]);
            }
        }

        /// <summary>
        /// Keys that hold atom types / storable ids / paths — never rewrite exact uid matches here.
        /// Compound <c>uid:</c> prefixes still remapped (they cannot appear as type strings).
        /// </summary>
        private static bool CreatorStripJsonKeyIsNonAtomRef(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            if (string.Equals(key, "type", StringComparison.Ordinal)) return true;
            if (string.Equals(key, "id", StringComparison.Ordinal)) return true;
            if (string.Equals(key, "name", StringComparison.Ordinal)) return true;
            if (string.Equals(key, "storeId", StringComparison.Ordinal)) return true;
            if (string.Equals(key, "url", StringComparison.Ordinal)) return true;
            if (string.Equals(key, "uid", StringComparison.Ordinal)) return true;
            if (key.EndsWith("Path", StringComparison.Ordinal)
                || key.EndsWith("Url", StringComparison.Ordinal)
                || key.EndsWith("URL", StringComparison.Ordinal))
                return true;
            return false;
        }

        private static void CreatorStripRemapAtomUidRefs(JSONNode node, string fromUid, string toUid)
        {
            if (node == null || string.IsNullOrEmpty(fromUid) || string.IsNullOrEmpty(toUid)) return;

            // JSONArray before JSONClass: some SimpleJSON forks derive JSONArray from JSONClass.
            JSONArray ja = node as JSONArray;
            if (ja != null)
            {
                for (int i = 0; i < ja.Count; i++)
                    CreatorStripRemapAtomUidRefs(ja[i], fromUid, toUid);
                return;
            }

            JSONClass jc = node as JSONClass;
            if (jc != null)
            {
                foreach (string key in jc.Keys)
                {
                    JSONNode child = jc[key];
                    if (child == null) continue;
                    // Leaf under non-ref key: only rewrite uid:storable compounds, not bare equals.
                    JSONArray childArr = child as JSONArray;
                    JSONClass childObj = child as JSONClass;
                    if (childArr == null && childObj == null)
                    {
                        CreatorStripRemapAtomUidLeaf(child, fromUid, toUid, CreatorStripJsonKeyIsNonAtomRef(key));
                        continue;
                    }
                    CreatorStripRemapAtomUidRefs(child, fromUid, toUid);
                }
                return;
            }

            CreatorStripRemapAtomUidLeaf(node, fromUid, toUid, exactMatchBlocked: false);
        }

        private static void CreatorStripRemapAtomUidLeaf(
            JSONNode node, string fromUid, string toUid, bool exactMatchBlocked)
        {
            if (node == null) return;
            try
            {
                string val = node.Value;
                if (string.IsNullOrEmpty(val)) return;
                if (!exactMatchBlocked && string.Equals(val, fromUid, StringComparison.Ordinal))
                {
                    node.Value = toUid;
                    return;
                }
                // Compound atom:storable / atom:control links (VaM trigger / parent refs).
                string fromPrefix = fromUid + ":";
                if (val.StartsWith(fromPrefix, StringComparison.Ordinal))
                    node.Value = toUid + val.Substring(fromUid.Length);
            }
            catch { }
        }

        /// <summary>
        /// Strip keepers JSON: force blank Skyshop sphere (CoreControl). Cold path only.
        /// </summary>
        private static void CreatorStripBlankSkyInSceneJson(JSONClass sceneJson)
        {
            if (sceneJson == null) return;
            JSONArray atoms = null;
            try { atoms = sceneJson["atoms"].AsArray; } catch { atoms = null; }
            if (atoms == null) return;

            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass atom = atoms[i] as JSONClass;
                if (atom == null) continue;
                string id = null;
                try
                {
                    if (atom["id"] != null) id = atom["id"].Value;
                }
                catch { id = null; }
                if (string.IsNullOrEmpty(id) || !id.StartsWith("CoreControl", StringComparison.Ordinal))
                    continue;

                JSONArray storables = null;
                try { storables = atom["storables"].AsArray; } catch { storables = null; }
                if (storables == null) continue;

                for (int s = 0; s < storables.Count; s++)
                {
                    JSONClass st = storables[s] as JSONClass;
                    if (st == null) continue;
                    CreatorStripBlankSkyStorable(st);
                }
            }
        }

        private static void CreatorStripBlankSkyStorable(JSONClass st)
        {
            if (st == null) return;

            // SkyshopLightController + any ImageControl-like url on CoreControl.
            if (st["skyName"] != null)
                st["skyName"] = "";
            if (st["showSkybox"] != null)
                st["showSkybox"].AsBool = false;
            if (st["url"] != null)
                st["url"] = "";
            if (st["skyboxIntensity"] != null)
                st["skyboxIntensity"].AsFloat = 0f;
            if (st["masterIntensity"] != null)
                st["masterIntensity"].AsFloat = 0f;
            if (st["diffuseIntensity"] != null)
                st["diffuseIntensity"].AsFloat = 0f;
            if (st["specularIntensity"] != null)
                st["specularIntensity"].AsFloat = 0f;
            if (st["unityAmbientIntensity"] != null)
                st["unityAmbientIntensity"].AsFloat = 0f;
            if (st["camExposure"] != null)
                st["camExposure"].AsFloat = 0.35f;

            JSONClass ambient = st["unityAmbientColor"] as JSONClass;
            if (ambient != null)
            {
                if (ambient["h"] != null) ambient["h"].AsFloat = 0f;
                if (ambient["s"] != null) ambient["s"].AsFloat = 0f;
                if (ambient["v"] != null) ambient["v"].AsFloat = 0f;
                // Some saves use r/g/b.
                if (ambient["r"] != null) ambient["r"].AsFloat = 0f;
                if (ambient["g"] != null) ambient["g"].AsFloat = 0f;
                if (ambient["b"] != null) ambient["b"].AsFloat = 0f;
            }
        }

        /// <summary>
        /// Runtime blank after load — covers params not forced in JSON / live Skyshop URL.
        /// </summary>
        private static void CreatorModeBlankBackdropSkyRuntime()
        {
            try
            {
                SkyshopLightController sky = SkyshopLightController.singleton;
                if (sky != null)
                {
                    try { sky.url = ""; } catch { }
                    try
                    {
                        JSONStorableBool show = sky.GetBoolJSONParam("showSkybox");
                        if (show != null) show.val = false;
                    }
                    catch { }
                    try
                    {
                        JSONStorableFloat skyI = sky.GetFloatJSONParam("skyboxIntensity");
                        if (skyI != null) skyI.val = 0f;
                    }
                    catch { }
                    try
                    {
                        JSONStorableFloat master = sky.GetFloatJSONParam("masterIntensity");
                        if (master != null) master.val = 0f;
                    }
                    catch { }
                    try
                    {
                        JSONStorableFloat diff = sky.GetFloatJSONParam("diffuseIntensity");
                        if (diff != null) diff.val = 0f;
                    }
                    catch { }
                    try
                    {
                        JSONStorableFloat spec = sky.GetFloatJSONParam("specularIntensity");
                        if (spec != null) spec.val = 0f;
                    }
                    catch { }
                    try
                    {
                        JSONStorableFloat ambI = sky.GetFloatJSONParam("unityAmbientIntensity");
                        if (ambI != null) ambI.val = 0f;
                    }
                    catch { }
                    try
                    {
                        JSONStorableColor amb = sky.GetColorJSONParam("unityAmbientColor");
                        if (amb != null)
                        {
                            HSVColor hsv = new HSVColor();
                            hsv.H = 0f;
                            hsv.S = 0f;
                            hsv.V = 0f;
                            amb.val = hsv;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            try { RenderSettings.ambientLight = Color.black; } catch { }
        }

        private IEnumerator CreatorModeStripSceneRoutine(
            HashSet<string> keepUids,
            Dictionary<string, string> renames,
            bool spawnDefault3PLights,
            string importDefaultSubScenePath,
            bool applyPossessable,
            bool possessClear,
            bool possessAddMale,
            bool possessAddFemale)
        {
            creatorModeStripBusy = true;
            SuppressAtomRemovedGalleryNotify = true;
            string tempPath = null;
            bool deleteTempWhenDone = true;
            try
            {
                SuperController scEarly = SuperController.singleton;
                if (scEarly == null)
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.creator.no_sc", "Scene controller unavailable."),
                        1.5f);
                    yield break;
                }
                bool alreadyLoading = false;
                try { alreadyLoading = scEarly.isLoading; } catch { }
                try { if (!alreadyLoading && LogUtil.IsSceneLoading()) alreadyLoading = true; } catch { }
                if (alreadyLoading)
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T(
                            "gallery.creator.strip_scene_busy",
                            "Scene still loading — wait, then strip again."),
                        2.5f);
                    yield break;
                }

                // Stale selector: drop keep uids that vanished since modal opened.
                if (keepUids != null && keepUids.Count > 0)
                {
                    HashSet<string> live = new HashSet<string>(StringComparer.Ordinal);
                    List<Atom> atomsNow = null;
                    try { atomsNow = scEarly.GetAtoms(); } catch { atomsNow = null; }
                    if (atomsNow != null)
                    {
                        for (int i = 0; i < atomsNow.Count; i++)
                        {
                            Atom a = atomsNow[i];
                            if (a == null) continue;
                            string uid = null;
                            try { uid = a.uid; } catch { uid = null; }
                            if (!string.IsNullOrEmpty(uid)) live.Add(uid);
                        }
                        List<string> stale = null;
                        foreach (string uid in keepUids)
                        {
                            if (live.Contains(uid)) continue;
                            if (stale == null) stale = new List<string>(4);
                            stale.Add(uid);
                        }
                        if (stale != null)
                        {
                            for (int i = 0; i < stale.Count; i++)
                                keepUids.Remove(stale[i]);
                            LogUtil.LogWarning("[VPB] Creator Strip pruned " + stale.Count
                                + " stale keep uid(s)");
                        }
                    }
                }

                int keepCount;
                int dropCount;
                string undoScenePath;
                if (!TryBuildCreatorStripKeepersSceneJson(
                    keepUids, renames, out tempPath, out undoScenePath, out keepCount, out dropCount))
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.creator.strip_failed", "Strip failed — could not build keepers scene."),
                        2.5f);
                    yield break;
                }

                LogUtil.LogWarning("[VPB] Creator Strip native Load START keep=" + keepCount
                    + " drop=" + dropCount
                    + " uids=" + (keepUids != null ? keepUids.Count : 0)
                    + " renames=" + (renames != null ? renames.Count : 0)
                    + " spawn3P=" + spawnDefault3PLights
                    + " importSubScene=" + (!string.IsNullOrEmpty(importDefaultSubScenePath)
                        ? importDefaultSubScenePath
                        : "-")
                    + " possess=" + applyPossessable
                    + " path=" + tempPath);
                try { VpbLoadingIconUtil.LogFlagSnapshot("creator-strip-before-load"); } catch { }

                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.creator.strip_loading", "Rebuilding scene (keepers only)…"),
                    2f);

                bool ok = false;
                try { ok = SceneLoadingUtils.LoadScene(tempPath, false); }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Creator Strip LoadScene exception: " + ex.Message);
                    ok = false;
                }
                if (!ok)
                {
                    TryDeleteStripTempFile(undoScenePath);
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.creator.strip_failed", "Strip failed — could not build keepers scene."),
                        2.5f);
                    yield break;
                }

                // Push undo only after replace succeeds (cold path; captures path string only).
                if (!string.IsNullOrEmpty(undoScenePath))
                {
                    string undoPath = undoScenePath;
                    try
                    {
                        PushUndo(
                            () =>
                            {
                                try
                                {
                                    SuperController scU = SuperController.singleton;
                                    if (scU != null)
                                    {
                                        bool busy = false;
                                        try { busy = scU.isLoading; } catch { }
                                        try { if (!busy && LogUtil.IsSceneLoading()) busy = true; } catch { }
                                        if (busy) return;
                                    }
                                    if (File.Exists(undoPath))
                                        SceneLoadingUtils.LoadScene(undoPath, false);
                                }
                                catch (Exception ex)
                                {
                                    LogUtil.LogError("[VPB] Strip Scene undo failed: " + ex.Message);
                                }
                                finally
                                {
                                    TryDeleteStripTempFile(undoPath);
                                }
                            },
                            VPBTranslation.T("gallery.undo.strip_scene", "Strip Scene"));
                    }
                    catch
                    {
                        TryDeleteStripTempFile(undoPath);
                    }
                }

                float deadline = Time.realtimeSinceStartup + 180f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    bool loading = false;
                    try
                    {
                        if (SuperController.singleton != null)
                            loading = SuperController.singleton.isLoading;
                    }
                    catch { }
                    try
                    {
                        if (!loading && LogUtil.IsSceneLoading())
                            loading = true;
                    }
                    catch { }
                    if (!loading) break;
                    yield return null;
                }

                bool stillLoading = false;
                try
                {
                    if (SuperController.singleton != null)
                        stillLoading = SuperController.singleton.isLoading;
                }
                catch { }
                try
                {
                    if (!stillLoading && LogUtil.IsSceneLoading())
                        stillLoading = true;
                }
                catch { }
                if (stillLoading)
                {
                    // VaM may still hold the temp path — delay delete; orphan cleaner sweeps later.
                    deleteTempWhenDone = false;
                    LogUtil.LogError("[VPB] Creator Strip load timed out (still isLoading) path=" + tempPath);
                    ShowTemporaryStatus(
                        VPBTranslation.T(
                            "gallery.creator.strip_load_timeout",
                            "Strip timed out — scene still loading. Wait for VaM, then retry."),
                        3.5f);
                    try { VpbLoadingIconUtil.LogFlagSnapshot("creator-strip-timeout"); } catch { }
                    yield break;
                }

                yield return null;
                yield return null;

                // Blank sky again after load (CoreControl restore can re-apply old sky).
                try { CreatorModeBlankBackdropSkyRuntime(); } catch { }

                if (applyPossessable)
                {
                    // Let Person FreeControllerV3 finish post-load before flipping possessable.
                    for (int settle = 0; settle < 8; settle++)
                        yield return null;
                    try
                    {
                        ShowTemporaryStatus(
                            VPBTranslation.T(
                                "gallery.creator.strip_possess_apply",
                                "Applying possessable policy…"),
                            1.25f);
                    }
                    catch { }
                    try
                    {
                        int n = VpbCreatorStripPossessable.ApplyToAllPersonsInScene(
                            possessClear, possessAddMale, possessAddFemale);
                        LogUtil.LogWarning("[VPB] Creator Strip possessable apply persons=" + n
                            + " clear=" + possessClear
                            + " male=" + possessAddMale
                            + " female=" + possessAddFemale);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Creator Strip possessable apply failed: " + ex.Message);
                    }
                    yield return null;
                }

                // Mutual exclusive fills — prefer SubScene when path set (caller should not pass both).
                // Explicit MoveNext — Unity 2018 nested yield return IEnumerator is unreliable here.
                if (!string.IsNullOrEmpty(importDefaultSubScenePath))
                {
                    try
                    {
                        ShowTemporaryStatus(
                            VPBTranslation.T(
                                "gallery.creator.strip_import_subscene_busy",
                                "Importing default SubScene…"),
                            1.5f);
                    }
                    catch { }
                    IEnumerator importIe = CreatorStripImportDefaultSubSceneRoutine(importDefaultSubScenePath);
                    while (importIe != null && importIe.MoveNext())
                        yield return importIe.Current;
                }
                else if (spawnDefault3PLights)
                {
                    try
                    {
                        ShowTemporaryStatus(
                            VPBTranslation.T(
                                "gallery.creator.strip_spawn_3p",
                                "Spawning default 3P lights…"),
                            1.5f);
                    }
                    catch { }
                    List<string> lightUids = new List<string>(3);
                    IEnumerator lightsIe = VpbThreePointLightSetup.SpawnForCreatorStrip(lightUids);
                    while (lightsIe != null && lightsIe.MoveNext())
                        yield return lightsIe.Current;
                }

                try
                {
                    if (CustomImageLoaderThreaded.singleton != null)
                        CustomImageLoaderThreaded.singleton.ForceResetLoadingProgress("creator-strip-after-load");
                }
                catch { }
                try { VpbLoadingIconUtil.RecoverAfterBulkAtomTeardown("creator-strip-after-load"); } catch { }
                try { VpbLoadingIconUtil.LogFlagSnapshot("creator-strip-settle"); } catch { }

                LogUtil.LogWarning("[VPB] Creator Strip native Load DONE keep=" + keepCount + " drop=" + dropCount);

                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T(
                            "gallery.creator.strip_done_native",
                            "Scene rebuilt. Kept {0} atom(s), removed {1}."),
                        keepCount, dropCount),
                    2.5f);

                try { NotifyAllPanelsSceneTargetsChanged(); } catch { }
                try { UpdateTabs(); } catch { }
            }
            finally
            {
                SuppressAtomRemovedGalleryNotify = false;
                creatorModeStripBusy = false;
                creatorModeStripRoutine = null;
                if (deleteTempWhenDone && !string.IsNullOrEmpty(tempPath))
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
                RefreshCreatorModeChrome();
            }
        }
    }
}
