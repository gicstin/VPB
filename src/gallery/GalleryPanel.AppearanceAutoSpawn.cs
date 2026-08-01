using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private Coroutine _appearanceAutoSpawnCo;
        private bool _appearanceAutoSpawnBusy;
        private readonly List<string> _appearanceAutoSpawnLightUids = new List<string>(3);

        /// <summary>
        /// Appearance click/apply: use existing Person target, or spawn one when scene has none.
        /// Power-user path — no confirm dialog; brief status covers change blindness.
        /// </summary>
        private bool TryLoadAppearanceAutoSpawningIfNeeded(FileEntry file, UIDraggableItem existingDragger)
        {
            Atom target = GetBestTargetAtom();
            if (target != null)
            {
                if (existingDragger != null) existingDragger.LoadAppearance(target);
                return true;
            }

            if (file == null) return false;

            if (_appearanceAutoSpawnBusy)
            {
                try
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.status.appearance_spawn_busy", "Person spawn already in progress…"),
                        2f);
                }
                catch { }
                return true;
            }

            if (_appearanceAutoSpawnCo != null)
            {
                try { StopCoroutine(_appearanceAutoSpawnCo); } catch { }
                _appearanceAutoSpawnCo = null;
            }

            _appearanceAutoSpawnCo = StartCoroutine(SpawnPersonThenLoadAppearanceCo(file));
            return true;
        }

        private static Vector3 GetDefaultAppearancePersonSpawnPosition()
        {
            SuperController sc = SuperController.singleton;
            if (sc != null && sc.centerCameraTarget != null)
            {
                Transform cam = sc.centerCameraTarget.transform;
                return cam.position + cam.forward * 1.5f;
            }
            return Vector3.zero;
        }

        private static void OrientAtomTowardCamera(Atom atom)
        {
            if (atom == null || atom.mainController == null) return;
            SuperController sc = SuperController.singleton;
            if (sc == null || sc.centerCameraTarget == null) return;

            try
            {
                Transform cam = sc.centerCameraTarget.transform;
                Quaternion targetRot = Quaternion.LookRotation(-cam.forward, Vector3.up);
                Vector3 euler = targetRot.eulerAngles;
                euler.x = 0f;
                euler.z = 0f;
                atom.mainController.transform.rotation = Quaternion.Euler(euler);
            }
            catch { }
        }

        private void SelectSpawnedPersonTarget(Atom spawned)
        {
            if (spawned == null) return;

            string uid = null;
            try { uid = spawned.uid; } catch { }
            if (string.IsNullOrEmpty(uid)) return;

            try { RefreshTargetDropdown(); } catch { }

            if (personAtoms == null) return;
            for (int i = 0; i < personAtoms.Count; i++)
            {
                Atom a = personAtoms[i];
                if (a == null) continue;
                try
                {
                    if (!string.Equals(a.uid, uid, StringComparison.Ordinal)) continue;
                    targetDropdownValue = i;
                    try { UpdateTargetDropdownUI(); } catch { }
                    try { RefreshTboxPersonAtomButtonsAfterSceneLoad(); } catch { }
                    return;
                }
                catch { }
            }
        }

        private IEnumerator SpawnPersonThenLoadAppearanceCo(FileEntry file)
        {
            _appearanceAutoSpawnBusy = true;
            _appearanceAutoSpawnLightUids.Clear();
            try
            {
                if (file == null) yield break;
                if (SuperController.singleton == null) yield break;

                try
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T(
                            "gallery.status.spawning_person_for_appearance",
                            "No Person in scene — spawning Person + lights…"),
                        2.5f);
                }
                catch { }

                Vector3 pos = GetDefaultAppearancePersonSpawnPosition();
                Atom spawned = null;
                SpawnAtomElement.SpawnSuppressionHandle suppression = null;

                // Suppressed spawn: hide/freeze until appearance apply settles (warm path, not per-frame).
                yield return SpawnAtomElement.SpawnPersonAtFloorSuppressed(pos, (a, h) =>
                {
                    spawned = a;
                    suppression = h;
                });

                if (spawned == null)
                {
                    LogUtil.LogWarning("[VPB] Appearance load: failed to spawn Person atom.");
                    yield break;
                }

                OrientAtomTowardCamera(spawned);
                SelectSpawnedPersonTarget(spawned);

                // 3-point InvisibleLight rig (MeshedVR defaults) — skip if scene already lit.
                yield return VpbThreePointLightSetup.SpawnAroundPerson(spawned, _appearanceAutoSpawnLightUids);

                GameObject go = null;
                try
                {
                    go = new GameObject("VPB_AppearanceAutoSpawnRunner");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    UIDraggableItem dragger = go.AddComponent<UIDraggableItem>();
                    dragger.FileEntry = file;
                    dragger.Panel = this;
                    dragger.LoadAppearance(spawned);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Appearance auto-spawn apply failed: " + ex);
                }
                finally
                {
                    if (go != null)
                    {
                        try { UnityEngine.Object.Destroy(go); } catch { }
                    }
                }

                // Match world-drop spawn settle window before restoring renderers/collisions.
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();

                try { SceneLoadingUtils.SchedulePostPersonApplyFixup(spawned); } catch { }

                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();

                try
                {
                    if (suppression != null) suppression.Restore();
                }
                catch { }

                // Undo removes lights + Person (forgiveness for accidental blank-scene apply).
                try
                {
                    string spawnedUid = null;
                    try { spawnedUid = spawned.uid; } catch { }
                    if (!string.IsNullOrEmpty(spawnedUid))
                    {
                        GalleryPanel panelRef = this;
                        string[] lightUidsCopy = null;
                        if (_appearanceAutoSpawnLightUids.Count > 0)
                        {
                            lightUidsCopy = new string[_appearanceAutoSpawnLightUids.Count];
                            for (int i = 0; i < _appearanceAutoSpawnLightUids.Count; i++)
                                lightUidsCopy[i] = _appearanceAutoSpawnLightUids[i];
                        }

                        PushUndo(() =>
                        {
                            try
                            {
                                if (lightUidsCopy != null)
                                    VpbThreePointLightSetup.RemoveByUids(lightUidsCopy);
                            }
                            catch { }

                            try
                            {
                                if (SuperController.singleton == null) return;
                                Atom a = null;
                                try { a = SuperController.singleton.GetAtomByUid(spawnedUid); } catch { a = null; }
                                if (a != null) SuperController.singleton.RemoveAtom(a);
                            }
                            catch { }

                            try
                            {
                                if (panelRef != null) panelRef.RefreshTargetDropdown();
                            }
                            catch { }
                        });
                    }
                }
                catch { }

                try { RefreshTargetDropdown(); } catch { }
            }
            finally
            {
                _appearanceAutoSpawnBusy = false;
                _appearanceAutoSpawnCo = null;
            }
        }
    }
}
