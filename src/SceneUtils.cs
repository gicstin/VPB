using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    public static class SceneLoadingUtils
    {
        static int sceneLoadSerial;
        static int lastScheduledSceneLoadSerial;

        public static bool EnsureInstalled(FileEntry entry)
        {
            if (entry == null) return false;

            try
            {
                bool flag = false;
                if (entry is VarFileEntry varEntry && varEntry.Package != null)
                {
                    flag = varEntry.Package.InstallRecursive();
                }
                else if (entry is SystemFileEntry sysEntry && sysEntry.package != null)
                {
                    flag = sysEntry.package.InstallRecursive();
                }

                // Scan for internal dependencies if it's a JSON-like file
                if (!string.IsNullOrEmpty(entry.Path))
                {
                    string ext = Path.GetExtension(entry.Path).ToLowerInvariant();
                    if (ext == ".json" || ext == ".vap" || ext == ".cslist")
                    {
                        using (var reader = entry.OpenStreamReader())
                        {
                            string content = reader.ReadToEnd();
                            if (!string.IsNullOrEmpty(content))
                            {
                                if (FileButton.EnsureInstalledByText(content))
                                {
                                    flag = true;
                                }
                            }
                        }
                    }
                }

                return flag;
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] EnsureInstalled error: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public static bool EnsureInstalled(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            
            FileEntry entry = FileManager.GetFileEntry(path);
            if (entry != null)
            {
                return EnsureInstalled(entry);
            }
            return false;
        }

        public static void NotifySceneLoadStarting(string saveName, bool loadMerge)
        {
            try
            {
                if (!loadMerge)
                {
                    unchecked { sceneLoadSerial++; }
                }
            }
            catch { }
        }

        public static void SchedulePostSceneLoadFixup()
        {
            try
            {
                int serial = sceneLoadSerial;
                if (serial == lastScheduledSceneLoadSerial) return;
                lastScheduledSceneLoadSerial = serial;

                if (SuperController.singleton != null)
                {
                    SuperController.singleton.StartCoroutine(PostSceneLoadFixupCoroutine(serial));
                }
            }
            catch { }
        }

        public static void SchedulePostPersonApplyFixup(Atom atom, List<KeyValuePair<JSONStorable, JSONClass>> lateRestoreTargets = null)
        {
            if (atom == null) return;
            if (SuperController.singleton == null) return;
            if (atom.type != "Person") return;

            try
            {
                SuperController.singleton.StartCoroutine(PostPersonApplyFixupCoroutine(atom, lateRestoreTargets));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] SchedulePostPersonApplyFixup error: " + ex.Message);
            }
        }

        static IEnumerator PostPersonApplyFixupCoroutine(Atom atom, List<KeyValuePair<JSONStorable, JSONClass>> lateRestoreTargets)
        {
            yield return new WaitForEndOfFrame();

            if (atom == null) yield break;
            if (atom.type != "Person") yield break;

            if (lateRestoreTargets != null)
            {
                for (int i = 0; i < lateRestoreTargets.Count; i++)
                {
                    try
                    {
                        var kvp = lateRestoreTargets[i];
                        if (kvp.Key != null && kvp.Value != null)
                        {
                            kvp.Key.LateRestoreFromJSON(kvp.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] LateRestoreFromJSON error: " + ex.Message);
                    }
                }
            }

            yield return new WaitForEndOfFrame();

            try
            {
                ResetAllSimClothing(atom);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] ResetAllSimClothing error: " + ex.Message);
            }
        }

        static IEnumerator PostSceneLoadFixupCoroutine(int serial)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            if (serial != sceneLoadSerial) yield break;

            var sc = SuperController.singleton;
            if (sc == null) yield break;

            try
            {
                var atoms = sc.GetAtoms();
                for (int i = 0; i < atoms.Count; i++)
                {
                    var a = atoms[i];
                    if (a != null && a.type == "Person")
                    {
                        ResetAllSimClothing(a);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] PostSceneLoadFixup error: " + ex.Message);
            }
        }

        static void ResetAllSimClothing(Atom atom)
        {
            if (atom == null) return;

            List<string> ids;
            try { ids = atom.GetStorableIDs(); }
            catch { return; }
            if (ids == null) return;

            for (int i = 0; i < ids.Count; i++)
            {
                string receiverName = ids[i];
                JSONStorable receiver = null;
                try { receiver = atom.GetStorableByID(receiverName); }
                catch { }

                if (receiver == null) continue;

                string storeId = receiver.storeId;
                if (string.IsNullOrEmpty(storeId) || storeId.Length < 3) continue;
                if (!storeId.EndsWith("Sim", StringComparison.Ordinal)) continue;

                try
                {
                    var colors = receiver.GetColorParamNames();
                    if (colors != null && colors.Contains("rootColor")) continue;

                    JSONStorableBool simEnabledBool = receiver.GetBoolJSONParam("simEnabled");
                    if (simEnabledBool != null && simEnabledBool.val)
                    {
                        receiver.CallAction("Reset");
                    }
                }
                catch { }
            }
        }
    }
}
