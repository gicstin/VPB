using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using VPB.src.util;

namespace VPB.Outliner
{
    internal static class OutlinerEdits
    {
        internal static bool SceneBusy()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return true;
                if (sc.isLoading) return true;
            }
            catch { return true; }
            return false;
        }

        static bool s_editModeAutoSwitched;

        internal static bool ConsumeEditModeAutoSwitch()
        {
            if (!s_editModeAutoSwitched) return false;
            s_editModeAutoSwitched = false;
            return true;
        }

        internal static bool EnsureEditMode()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return false;
                if (sc.gameMode == SuperController.GameMode.Edit) return true;
                if (sc.isLoading) return false;
                sc.gameMode = SuperController.GameMode.Edit;
                if (sc.gameMode != SuperController.GameMode.Edit) return false;
                s_editModeAutoSwitched = true;
                return true;
            }
            catch { return false; }
        }

        internal static bool CanWrite(Atom atom)
        {
            if (SceneBusy()) return false;
            if (atom == null) return false;
            if (SceneUtils.IsSystemProtectedAtom(atom)) return false;
            return EnsureEditMode();
        }

        internal static JSONStorable ResolveStorable(Atom atom, string storableId)
        {
            if (atom == null || string.IsNullOrEmpty(storableId)) return null;
            if (string.Equals(storableId, OutlinerParamCatalog.AtomStorableId, StringComparison.Ordinal))
                return atom;
            try
            {
                JSONStorable st = atom.GetStorableByID(storableId);
                if (st != null) return st;
            }
            catch { }
            try
            {
                if (string.Equals(storableId, atom.uid, StringComparison.Ordinal)) return atom;
            }
            catch { }
            return null;
        }

        internal static Atom GetAtom(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return null;
                return sc.GetAtomByUid(uid);
            }
            catch { return null; }
        }

        internal static bool SetOn(Atom atom, bool on)
        {
            if (!CanWrite(atom)) return false;
            try { atom.SetOn(on); return true; }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] SetOn failed: " + ex.Message);
                return false;
            }
        }

        internal static bool SetHidden(Atom atom, bool hidden)
        {
            if (!CanWrite(atom)) return false;
            try
            {
                atom.hidden = hidden;
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] SetHidden failed: " + ex.Message);
                return false;
            }
        }

        internal static bool SetCollision(Atom atom, bool on)
        {
            if (!CanWrite(atom)) return false;
            try
            {
                atom.collisionEnabled = on;
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] SetCollision failed: " + ex.Message);
                return false;
            }
        }

        internal static JSONStorableBool FreezePhysicsParam(Atom atom)
        {
            if (atom == null) return null;
            try
            {
                JSONStorableBool b = atom.GetBoolJSONParam("freezePhysics");
                if (b != null) return b;
            }
            catch { }
            try
            {
                JSONStorable control = atom.GetStorableByID("control");
                return control != null ? control.GetBoolJSONParam("freezePhysics") : null;
            }
            catch { return null; }
        }

        internal static bool SetPhysicsFrozen(Atom atom, bool frozen)
        {
            if (!CanWrite(atom)) return false;
            try
            {
                JSONStorableBool b = FreezePhysicsParam(atom);
                if (b != null)
                {
                    b.val = frozen;
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] freezePhysics failed: " + ex.Message);
            }
            return false;
        }

        internal static bool SetParent(Atom child, Atom parent)
        {
            if (!CanWrite(child)) return false;
            if (parent != null && SceneUtils.IsSystemProtectedAtom(child)) return false;
            try
            {
                child.SetParentAtom(parent);
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] SetParent failed: " + ex.Message);
                return false;
            }
        }

        internal static bool Rename(Atom atom, string newUid)
        {
            if (!CanWrite(atom)) return false;
            if (string.IsNullOrEmpty(newUid)) return false;
            SuperController sc = SuperController.singleton;
            if (sc == null) return false;
            try
            {
                Atom clash = sc.GetAtomByUid(newUid);
                if (clash != null && clash != atom) return false;
                sc.RenameAtom(atom, newUid);
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] Rename failed: " + ex.Message);
                return false;
            }
        }

        internal static bool Delete(Atom atom)
        {
            if (!CanWrite(atom)) return false;
            SuperController sc = SuperController.singleton;
            if (sc == null) return false;
            try
            {
                sc.RemoveAtom(atom);
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] RemoveAtom failed: " + ex.Message);
                return false;
            }
        }

        internal static string CaptureAtomJson(Atom atom)
        {
            if (atom == null) return "";
            try
            {
                var arr = new JSONArray();
                atom.Store(arr);
                if (arr.Count == 0) return "";
                JSONClass jc = arr[0].AsObject;
                return jc != null ? jc.ToString() : "";
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] atom snapshot failed: " + ex.Message);
                return "";
            }
        }

        internal static IEnumerator RestoreAtomRoutine(string json)
        {
            if (string.IsNullOrEmpty(json)) yield break;
            SuperController sc = SuperController.singleton;
            if (sc == null) yield break;
            if (SceneBusy()) yield break;
            if (!EnsureEditMode()) yield break;
            JSONClass store = null;
            string type = "";
            string uid = "";
            try
            {
                JSONNode node = JSON.Parse(json);
                store = node != null ? node.AsObject : null;
                if (store != null)
                {
                    type = store["type"];
                    uid = store["id"];
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] atom snapshot parse failed: " + ex.Message);
                yield break;
            }
            if (store == null || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(uid)) yield break;
            if (sc.GetAtomByUid(uid) != null) yield break;

            yield return sc.AddAtomByType(type, uid);
            Atom atom = sc.GetAtomByUid(uid);
            if (atom == null) yield break;
            try { atom.SetOn(true); } catch { }
            yield return null;
            try
            {
                atom.RestoreTransform(store);
                atom.RestoreParentAtom(store);
                atom.Restore(store, true, true, true);
                atom.LateRestore(store);
                atom.PostRestore();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] atom restore failed: " + ex.Message);
            }
        }

        internal static FreeControllerV3 GetRootController(Atom atom)
        {
            if (atom == null) return null;
            try
            {
                JSONStorable st = atom.GetStorableByID("control");
                FreeControllerV3 named = st as FreeControllerV3;
                if (named != null) return named;
            }
            catch { }
            try { return atom.mainController; }
            catch { return null; }
        }

        internal static bool SelectInVam(Atom atom, bool lookAt)
        {
            return SelectRoot(atom, lookAt, false);
        }

        internal static bool SelectRoot(Atom atom, bool lookAt)
        {
            return SelectRoot(atom, lookAt, false);
        }

        internal static bool SelectRoot(Atom atom, bool lookAt, bool openUI)
        {
            if (atom == null) return false;
            SuperController sc = SuperController.singleton;
            if (sc == null) return false;
            try
            {
                FreeControllerV3 fc = GetRootController(atom);
                if (fc != null)
                {
                    bool alignView;
                    bool alignRotationOnly;
                    bool alignUpDown;
                    bool openSelectedUI;
                    MapSelectControllerArgs(lookAt, openUI,
                        out alignView, out alignRotationOnly, out alignUpDown, out openSelectedUI);
                    sc.SelectController(fc, alignView, alignRotationOnly, alignUpDown, openSelectedUI);
                }
                return fc != null;
            }
            catch
            {
                return false;
            }
        }

        internal static void MapSelectControllerArgs(
            bool lookAt,
            bool openUI,
            out bool alignView,
            out bool alignRotationOnly,
            out bool alignUpDown,
            out bool openSelectedUI)
        {
            alignView = lookAt;
            alignRotationOnly = true;
            alignUpDown = true;
            openSelectedUI = openUI;
        }

        internal static bool WriteFloat(Atom atom, string storableId, string paramId, float value)
        {
            JSONStorableFloat p = GetFloat(atom, storableId, paramId);
            if (p == null || !CanWrite(atom)) { return false; }
            try
            {
                p.val = value;
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] float write failed: " + ex.Message);
                return false;
            }
        }

        internal static bool ResetFloat(Atom atom, string storableId, string paramId)
        {
            JSONStorableFloat p = GetFloat(atom, storableId, paramId);
            if (p == null) return false;
            float def = p.val;
            try { def = p.defaultVal; } catch { }
            return WriteFloat(atom, storableId, paramId, def);
        }

        internal static bool ResetBool(Atom atom, string storableId, string paramId)
        {
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                JSONStorableBool p = st != null ? st.GetBoolJSONParam(paramId) : null;
                if (p == null) return false;
                bool def = p.val;
                try { def = p.defaultVal; } catch { }
                return WriteBool(atom, storableId, paramId, def);
            }
            catch { return false; }
        }

        internal static bool ResetString(Atom atom, string storableId, string paramId)
        {
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                JSONStorableString p = st != null ? st.GetStringJSONParam(paramId) : null;
                if (p == null) return false;
                string def = "";
                try { def = p.defaultVal ?? ""; } catch { }
                return WriteString(atom, storableId, paramId, def);
            }
            catch { return false; }
        }

        internal static bool ResetChooser(Atom atom, string storableId, string paramId)
        {
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                JSONStorableStringChooser p = st != null ? st.GetStringChooserJSONParam(paramId) : null;
                if (p == null) return false;
                string def = "";
                try { def = p.defaultVal ?? ""; } catch { }
                return WriteChooser(atom, storableId, paramId, def);
            }
            catch { return false; }
        }

        internal static bool ResetColor(Atom atom, string storableId, string paramId)
        {
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                JSONStorableColor p = st != null ? st.GetColorJSONParam(paramId) : null;
                if (p == null) return false;
                HSVColor def = p.val;
                try { def = p.defaultVal; } catch { }
                return WriteColor(atom, storableId, paramId, def);
            }
            catch { return false; }
        }

        internal static JSONStorableFloat ScaleParam(Atom atom)
        {
            JSONStorableFloat p = GetFloat(atom, "scale", "scale");
            if (p != null) return p;
            return GetFloat(atom, "rescaleObject", "scale");
        }

        internal static string ScaleStorableId(Atom atom)
        {
            return GetFloat(atom, "scale", "scale") != null ? "scale" : "rescaleObject";
        }

        internal static bool TryReadBool(Atom atom, string storableId, string paramId, out bool value)
        {
            value = false;
            if (atom == null || string.IsNullOrEmpty(paramId)) return false;
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                if (st == null) return false;
                JSONStorableBool p = st.GetBoolJSONParam(paramId);
                if (p != null)
                {
                    value = p.val;
                    return true;
                }
                value = st.GetBoolParamValue(paramId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool WriteBool(Atom atom, string storableId, string paramId, bool value)
        {
            if (!CanWrite(atom)) { return false; }
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                if (st == null) { return false; }
                JSONStorableBool p = null;
                try { p = st.GetBoolJSONParam(paramId); } catch { p = null; }
                if (p != null) p.val = value;
                try { st.SetBoolParamValue(paramId, value); } catch { }
                if (p != null) return p.val == value;
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] bool write failed: " + ex.Message);
                return false;
            }
        }

        internal static bool WriteString(Atom atom, string storableId, string paramId, string value)
        {
            if (!CanWrite(atom)) { return false; }
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                if (st == null) { return false; }
                JSONStorableString p = st.GetStringJSONParam(paramId);
                if (p == null) { return false; }
                p.val = value ?? "";
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] string write failed: " + ex.Message);
                return false;
            }
        }

        internal static bool WriteChooser(Atom atom, string storableId, string paramId, string value)
        {
            if (!CanWrite(atom)) { return false; }
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                if (st == null) { return false; }
                JSONStorableStringChooser p = st.GetStringChooserJSONParam(paramId);
                if (p == null) { return false; }
                p.val = value ?? "";
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] chooser write failed: " + ex.Message);
                return false;
            }
        }

        internal static bool InvokeAction(Atom atom, string storableId, string paramId)
        {
            if (!CanWrite(atom)) { return false; }
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                if (st == null) { return false; }
                JSONStorableAction act = null;
                try { act = st.GetAction(paramId); } catch { act = null; }
                if (act != null && act.actionCallback != null)
                {
                    act.actionCallback();
                    return true;
                }
                st.CallAction(paramId);
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] action failed: " + ex.Message);
                return false;
            }
        }

        internal static bool WriteColor(Atom atom, string storableId, string paramId, HSVColor hsv)
        {
            if (!CanWrite(atom)) { return false; }
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                if (st == null) { return false; }
                JSONStorableColor p = st.GetColorJSONParam(paramId);
                if (p == null) { return false; }
                p.val = hsv;
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] color write failed: " + ex.Message);
                return false;
            }
        }

        internal static bool TryReadColor(Atom atom, string storableId, string paramId, out HSVColor hsv)
        {
            hsv = new HSVColor();
            if (atom == null) return false;
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                if (st == null) return false;
                JSONStorableColor p = st.GetColorJSONParam(paramId);
                if (p == null) return false;
                hsv = p.val;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static JSONStorableFloat GetFloat(Atom atom, string storableId, string paramId)
        {
            if (atom == null) return null;
            try
            {
                JSONStorable st = ResolveStorable(atom, storableId);
                if (st == null) return null;
                return st.GetFloatJSONParam(paramId);
            }
            catch { return null; }
        }

        internal static Transform ControlTransform(Atom atom)
        {
            if (atom == null) return null;
            try
            {
                FreeControllerV3 fc = atom.mainController;
                if (fc == null) return null;
                return fc.control != null ? fc.control : fc.transform;
            }
            catch { return null; }
        }

        internal static string LastWriteBlockReason;

        static System.Reflection.FieldInfo s_linkConnectorField;
        static bool s_linkConnectorFieldResolved;

        static Transform LinkConnector(FreeControllerV3 fc)
        {
            if (!s_linkConnectorFieldResolved)
            {
                s_linkConnectorFieldResolved = true;
                try
                {
                    s_linkConnectorField = typeof(FreeControllerV3).GetField("_linkToConnector",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                }
                catch { s_linkConnectorField = null; }
            }
            if (s_linkConnectorField == null || fc == null) return null;
            try { return s_linkConnectorField.GetValue(fc) as Transform; }
            catch { return null; }
        }

        static Transform PositionAnchor(FreeControllerV3 fc, out bool blocked, out string reason)
        {
            blocked = false;
            reason = null;
            switch (fc.currentPositionState)
            {
                case FreeControllerV3.PositionState.Off:
                    return fc.followWhenOff;
                case FreeControllerV3.PositionState.ParentLink:
                case FreeControllerV3.PositionState.PhysicsLink:
                    Transform link = LinkConnector(fc);
                    if (link == null)
                    {
                        blocked = true;
                        reason = "position is driven by a link";
                    }
                    return link;
                case FreeControllerV3.PositionState.Following:
                    blocked = true;
                    reason = "position is following another object";
                    return null;
                case FreeControllerV3.PositionState.Lock:
                    blocked = true;
                    reason = "position is locked";
                    return null;
                default:
                    return null;
            }
        }

        static Transform RotationAnchor(FreeControllerV3 fc, out bool blocked, out string reason)
        {
            blocked = false;
            reason = null;
            switch (fc.currentRotationState)
            {
                case FreeControllerV3.RotationState.Off:
                    return fc.followWhenOff;
                case FreeControllerV3.RotationState.ParentLink:
                case FreeControllerV3.RotationState.PhysicsLink:
                    Transform link = LinkConnector(fc);
                    if (link == null)
                    {
                        blocked = true;
                        reason = "rotation is driven by a link";
                    }
                    return link;
                case FreeControllerV3.RotationState.Following:
                    blocked = true;
                    reason = "rotation is following another object";
                    return null;
                case FreeControllerV3.RotationState.LookAt:
                    blocked = true;
                    reason = "rotation is aimed at a look-at target";
                    return null;
                case FreeControllerV3.RotationState.Lock:
                    blocked = true;
                    reason = "rotation is locked";
                    return null;
                default:
                    return null;
            }
        }

        static void NotifyControllerMoved(FreeControllerV3 fc, bool position, bool rotation)
        {
            try
            {
                if (position && fc.onPositionChangeHandlers != null) fc.onPositionChangeHandlers(fc);
                if (rotation && fc.onRotationChangeHandlers != null) fc.onRotationChangeHandlers(fc);
                if (fc.onMovementHandlers != null) fc.onMovementHandlers(fc);
            }
            catch { }
        }

        internal static bool WriteMainControllerPosition(Atom atom, Vector3 pos)
        {
            if (!CanWrite(atom) || atom.mainController == null) return false;
            try
            {
                FreeControllerV3 fc = atom.mainController;
                bool blocked;
                string reason;
                Transform anchor = PositionAnchor(fc, out blocked, out reason);
                if (blocked)
                {
                    LastWriteBlockReason = reason;
                    return false;
                }
                Transform ctrl = fc.control != null ? fc.control : fc.transform;
                pos = OutlinerLock.MaskLockedPosition(fc, pos, ctrl.position);
                ctrl.position = pos;
                if (fc.control != null && fc.control != fc.transform) fc.transform.position = pos;
                if (anchor != null) anchor.position = pos;
                NotifyControllerMoved(fc, true, false);
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] position failed: " + ex.Message);
                return false;
            }
        }

        internal static bool WriteMainControllerRotation(Atom atom, Vector3 euler)
        {
            if (!CanWrite(atom) || atom.mainController == null) return false;
            try
            {
                FreeControllerV3 fc = atom.mainController;
                bool blocked;
                string reason;
                Transform anchor = RotationAnchor(fc, out blocked, out reason);
                if (blocked)
                {
                    LastWriteBlockReason = reason;
                    return false;
                }
                Transform ctrl = fc.control != null ? fc.control : fc.transform;
                euler = OutlinerLock.MaskLockedRotation(fc, euler, ctrl.eulerAngles);
                Quaternion rot = Quaternion.Euler(euler);
                ctrl.rotation = rot;
                if (fc.control != null && fc.control != fc.transform) fc.transform.rotation = rot;
                if (anchor != null) anchor.rotation = rot;
                NotifyControllerMoved(fc, false, true);
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] rotation failed: " + ex.Message);
                return false;
            }
        }

        internal static IEnumerator DuplicateRoutine(Atom atom)
        {
            if (!CanWrite(atom)) yield break;
            SuperController sc = SuperController.singleton;
            if (sc == null) yield break;
            string type = "";
            string uid = "";
            JSONClass store = null;
            try
            {
                type = atom.type;
                uid = atom.uid;
                var arr = new JSONArray();
                atom.Store(arr);
                if (arr.Count > 0) store = arr[0].AsObject;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] duplicate store failed: " + ex.Message);
                yield break;
            }
            if (string.IsNullOrEmpty(type)) yield break;
            string newUid = uid + "_copy";
            int n = 2;
            while (sc.GetAtomByUid(newUid) != null && n < 50)
            {
                newUid = uid + "_copy" + n.ToString();
                n++;
            }
            yield return sc.AddAtomByType(type, newUid);
            Atom copy = sc.GetAtomByUid(newUid);
            if (copy == null || store == null) yield break;
            store["id"] = newUid;
            RestoreLikeSceneLoad(copy, store);
        }

        static void RestoreLikeSceneLoad(Atom atom, JSONClass store)
        {
            try
            {
                atom.PreRestore(true, true);
                atom.RestoreTransform(store);
                atom.RestoreParentAtom(store);
                atom.Restore(store, true, true, true);
                atom.LateRestore(store, true, true, true);
                atom.PostRestore(true, true);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] duplicate restore failed: " + ex.Message);
            }
        }

        internal static void SoloHideOthers(Atom keep, IList<Atom> all)
        {
            if (!CanWrite(keep) || all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                Atom a = all[i];
                if (a == null || a == keep) continue;
                if (SceneUtils.IsSystemProtectedAtom(a)) continue;
                try { a.hidden = true; } catch { }
            }
            try { keep.hidden = false; } catch { }
        }
    }
}
