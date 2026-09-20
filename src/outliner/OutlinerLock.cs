using System;
using System.Collections.Generic;
using UnityEngine;
using VPB.src.util;

namespace VPB.Outliner
{
    internal static class OutlinerLock
    {
        struct PriorState
        {
            internal FreeControllerV3.PositionState Position;
            internal FreeControllerV3.RotationState Rotation;
        }

        const int MaxRemembered = 64;

        static readonly Dictionary<string, PriorState> s_prior =
            new Dictionary<string, PriorState>(8, StringComparer.Ordinal);

        internal static bool Supports(Atom atom)
        {
            if (atom == null) return false;
            try { return atom.mainController != null; }
            catch { return false; }
        }

        static FreeControllerV3 Controller(Atom atom)
        {
            if (atom == null) return null;
            try { return atom.mainController; }
            catch { return null; }
        }

        internal static bool IsLocked(Atom atom)
        {
            if (!Supports(atom)) return false;
            try
            {
                FreeControllerV3 fc = atom.mainController;
                return fc.currentPositionState == FreeControllerV3.PositionState.Lock
                    && fc.currentRotationState == FreeControllerV3.RotationState.Lock;
            }
            catch { return false; }
        }

        internal static bool IsPositionAxisLocked(Atom atom, int axis)
        {
            return IsPositionAxisLocked(Controller(atom), axis);
        }

        internal static bool IsPositionAxisLocked(FreeControllerV3 fc, int axis)
        {
            if (fc == null) return false;
            try
            {
                if (axis == 0) return fc.xLock;
                if (axis == 1) return fc.yLock;
                return fc.zLock;
            }
            catch { return false; }
        }

        internal static bool IsRotationAxisLocked(Atom atom, int axis)
        {
            return IsRotationAxisLocked(Controller(atom), axis);
        }

        internal static bool IsRotationAxisLocked(FreeControllerV3 fc, int axis)
        {
            if (fc == null) return false;
            try
            {
                if (axis == 0) return fc.xRotLock;
                if (axis == 1) return fc.yRotLock;
                return fc.zRotLock;
            }
            catch { return false; }
        }

        internal static int AxisLockCount(Atom atom)
        {
            FreeControllerV3 fc = Controller(atom);
            if (fc == null) return 0;
            int n = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                if (IsPositionAxisLocked(fc, axis)) n++;
                if (IsRotationAxisLocked(fc, axis)) n++;
            }
            return n;
        }

        internal static bool SetPositionAxisLocked(Atom atom, int axis, bool locked)
        {
            if (!Supports(atom)) return false;
            if (!OutlinerEdits.CanWrite(atom)) return false;
            try
            {
                FreeControllerV3 fc = atom.mainController;
                if (axis == 0) fc.xLock = locked;
                else if (axis == 1) fc.yLock = locked;
                else fc.zLock = locked;
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] axis lock failed: " + ex.Message);
                return false;
            }
        }

        internal static bool SetRotationAxisLocked(Atom atom, int axis, bool locked)
        {
            if (!Supports(atom)) return false;
            if (!OutlinerEdits.CanWrite(atom)) return false;
            try
            {
                FreeControllerV3 fc = atom.mainController;
                if (axis == 0) fc.xRotLock = locked;
                else if (axis == 1) fc.yRotLock = locked;
                else fc.zRotLock = locked;
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] axis lock failed: " + ex.Message);
                return false;
            }
        }

        internal static bool ClearAxisLocks(Atom atom)
        {
            if (!Supports(atom)) return false;
            if (AxisLockCount(atom) == 0) return true;
            if (!OutlinerEdits.CanWrite(atom)) return false;
            try
            {
                FreeControllerV3 fc = atom.mainController;
                fc.xLock = false;
                fc.yLock = false;
                fc.zLock = false;
                fc.xRotLock = false;
                fc.yRotLock = false;
                fc.zRotLock = false;
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] axis unlock failed: " + ex.Message);
                return false;
            }
        }

        internal static Vector3 MaskLockedPosition(FreeControllerV3 fc, Vector3 next, Vector3 current)
        {
            if (fc == null) return next;
            if (IsPositionAxisLocked(fc, 0)) next.x = current.x;
            if (IsPositionAxisLocked(fc, 1)) next.y = current.y;
            if (IsPositionAxisLocked(fc, 2)) next.z = current.z;
            return next;
        }

        internal static Vector3 MaskLockedRotation(FreeControllerV3 fc, Vector3 next, Vector3 current)
        {
            if (fc == null) return next;
            if (IsRotationAxisLocked(fc, 0)) next.x = current.x;
            if (IsRotationAxisLocked(fc, 1)) next.y = current.y;
            if (IsRotationAxisLocked(fc, 2)) next.z = current.z;
            return next;
        }

        internal static bool SetLocked(Atom atom, bool locked)
        {
            if (!Supports(atom)) return false;
            if (!OutlinerEdits.CanWrite(atom)) return false;
            try
            {
                FreeControllerV3 fc = atom.mainController;
                string uid = atom.uid ?? "";
                if (locked)
                {
                    if (fc.currentPositionState != FreeControllerV3.PositionState.Lock
                        || fc.currentRotationState != FreeControllerV3.RotationState.Lock)
                    {
                        PriorState prior;
                        prior.Position = fc.currentPositionState;
                        prior.Rotation = fc.currentRotationState;
                        if (prior.Position == FreeControllerV3.PositionState.Lock)
                            prior.Position = FreeControllerV3.PositionState.On;
                        if (prior.Rotation == FreeControllerV3.RotationState.Lock)
                            prior.Rotation = FreeControllerV3.RotationState.On;
                        if (s_prior.Count > MaxRemembered) s_prior.Clear();
                        s_prior[uid] = prior;
                    }
                    fc.currentPositionState = FreeControllerV3.PositionState.Lock;
                    fc.currentRotationState = FreeControllerV3.RotationState.Lock;
                    return true;
                }

                PriorState back;
                if (!s_prior.TryGetValue(uid, out back))
                {
                    back.Position = FreeControllerV3.PositionState.On;
                    back.Rotation = FreeControllerV3.RotationState.On;
                }
                else s_prior.Remove(uid);
                fc.currentPositionState = back.Position;
                fc.currentRotationState = back.Rotation;
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] lock failed: " + ex.Message);
                return false;
            }
        }
    }
}
