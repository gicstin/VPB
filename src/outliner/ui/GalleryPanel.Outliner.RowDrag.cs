using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VPB.Outliner;

namespace VPB
{
    internal sealed class OutlinerRowDrag : MonoBehaviour,
        IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal const float HoldSeconds = 0.35f;

        internal ScrollRect Scroll;
        internal Func<GameObject, bool> CanDrag;
        internal Action<GameObject> DragBegan;
        internal Action<GameObject, GameObject> DragOver;
        internal Action<GameObject, GameObject> Dropped;
        internal Action DragEnded;

        bool _claimed;
        float _downAt;

        public void OnPointerDown(PointerEventData eventData)
        {
            _downAt = Time.unscaledTime;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _claimed = false;
            if (eventData != null && CanDrag != null && CanDrag(gameObject))
            {
                bool held = (Time.unscaledTime - _downAt) >= HoldSeconds;
                bool sideways = Mathf.Abs(eventData.delta.x) > Mathf.Abs(eventData.delta.y);
                _claimed = held || sideways;
            }
            if (_claimed)
            {
                if (DragBegan != null) DragBegan(gameObject);
                return;
            }
            Forward(eventData, ExecuteEvents.beginDragHandler);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_claimed)
            {
                Forward(eventData, ExecuteEvents.dragHandler);
                return;
            }
            if (DragOver != null) DragOver(gameObject, TargetUnder(eventData));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_claimed)
            {
                Forward(eventData, ExecuteEvents.endDragHandler);
                return;
            }
            _claimed = false;
            GameObject target = TargetUnder(eventData);
            if (Dropped != null) Dropped(gameObject, target);
            if (DragEnded != null) DragEnded();
        }

        static GameObject TargetUnder(PointerEventData eventData)
        {
            if (eventData == null) return null;
            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            while (hit != null)
            {
                if (hit.GetComponent<GalleryPanel.OutlinerRowBind>() != null) return hit;
                Transform parent = hit.transform.parent;
                hit = parent != null ? parent.gameObject : null;
            }
            return null;
        }

        void Forward<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> fn)
            where T : IEventSystemHandler
        {
            if (Scroll == null || eventData == null) return;
            try { ExecuteEvents.Execute(Scroll.gameObject, eventData, fn); }
            catch { }
        }
    }

    public partial class GalleryPanel
    {
        GameObject _outlinerDropRow;
        readonly System.Collections.Generic.List<string> _outlinerDragUids =
            new System.Collections.Generic.List<string>(8);

        void AttachOutlinerRowDrag(GameObject row)
        {
            if (row == null) return;
            OutlinerRowDrag drag = row.GetComponent<OutlinerRowDrag>();
            if (drag == null) drag = row.AddComponent<OutlinerRowDrag>();
            drag.Scroll = _outlinerTreeScroll;
            drag.CanDrag = OutlinerRowCanDrag;
            drag.DragBegan = BeginOutlinerRowDrag;
            drag.DragOver = HoverOutlinerRowDrop;
            drag.Dropped = DropOutlinerRow;
            drag.DragEnded = EndOutlinerRowDrag;
        }

        bool OutlinerRowCanDrag(GameObject row)
        {
            OutlinerRowBind bind = row != null ? row.GetComponent<OutlinerRowBind>() : null;
            if (bind == null || string.IsNullOrEmpty(bind.AtomUid)) return false;
            Atom atom = OutlinerEdits.GetAtom(bind.AtomUid);
            if (atom == null) return false;
            return !SceneUtils.IsSystemProtectedAtom(atom);
        }

        void BeginOutlinerRowDrag(GameObject row)
        {
            _outlinerDropRow = null;
            OutlinerRowBind bind = row != null ? row.GetComponent<OutlinerRowBind>() : null;
            string uid = bind != null ? bind.AtomUid : "";
            ShowTemporaryStatus(
                VPBTranslation.T("outliner.drag.begin", "Moving ") + uid
                    + VPBTranslation.T("outliner.drag.begin_tail",
                        " — drop on an atom to parent it there, on a group row to detach."),
                3f);
        }

        void HoverOutlinerRowDrop(GameObject row, GameObject target)
        {
            if (target == _outlinerDropRow) return;
            RestoreOutlinerDropRowTint();
            _outlinerDropRow = target != row ? target : null;
            if (_outlinerDropRow == null) return;
            OutlinerRowBind bind = _outlinerDropRow.GetComponent<OutlinerRowBind>();
            if (bind == null || bind.Bg == null) return;
            bind.Bg.color = GalleryUiColorTokens.ActiveSelected;
        }

        void RestoreOutlinerDropRowTint()
        {
            if (_outlinerDropRow == null) return;
            _outlinerDropRow = null;
            UpdateOutlinerVirtualVisible(true);
        }

        void DropOutlinerRow(GameObject row, GameObject target)
        {
            OutlinerRowBind from = row != null ? row.GetComponent<OutlinerRowBind>() : null;
            OutlinerRowBind onto = target != null ? target.GetComponent<OutlinerRowBind>() : null;
            if (from == null || string.IsNullOrEmpty(from.AtomUid)) return;
            if (onto == null || string.Equals(onto.NodeId, from.NodeId, StringComparison.Ordinal))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.drag.cancelled", "Move cancelled."), 1.4f);
                return;
            }

            if (string.IsNullOrEmpty(onto.AtomUid))
            {
                DetachOutlinerDragged(from.AtomUid);
                return;
            }
            ParentOutlinerDragged(from.AtomUid, onto.AtomUid);
        }

        void EndOutlinerRowDrag()
        {
            RestoreOutlinerDropRowTint();
        }

        void CollectOutlinerDragUids(string draggedUid)
        {
            _outlinerDragUids.Clear();
            if (string.IsNullOrEmpty(draggedUid)) return;
            if (_outlinerSelection.Contains(draggedUid) && _outlinerSelection.Count > 1)
            {
                _outlinerSelection.CopyUids(_outlinerDragUids);
                return;
            }
            _outlinerDragUids.Add(draggedUid);
        }

        void ParentOutlinerDragged(string draggedUid, string parentUid)
        {
            Atom parent = OutlinerEdits.GetAtom(parentUid);
            if (parent == null) return;
            CollectOutlinerDragUids(draggedUid);
            int done = 0;
            for (int i = 0; i < _outlinerDragUids.Count; i++)
            {
                string uid = _outlinerDragUids[i];
                if (string.IsNullOrEmpty(uid)) continue;
                if (string.Equals(uid, parentUid, StringComparison.Ordinal)) continue;
                Atom child = OutlinerEdits.GetAtom(uid);
                if (child == null || SceneUtils.IsSystemProtectedAtom(child)) continue;
                string before = OutlinerParentUidOf(child);
                if (!OutlinerEdits.SetParent(child, parent)) continue;
                _outlinerUndo.Push(uid + "|parent", "Parent", before, parentUid);
                done++;
            }
            if (done == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.drag.failed",
                    "Nothing could be parented there."), 2f);
                return;
            }
            ShowTemporaryStatus(done
                + VPBTranslation.T("outliner.drag.parented", " parented to ") + parentUid, 2f);
            _outlinerLastFocused = true;
            QueueOutlinerRebuild();
        }

        void DetachOutlinerDragged(string draggedUid)
        {
            CollectOutlinerDragUids(draggedUid);
            int done = 0;
            for (int i = 0; i < _outlinerDragUids.Count; i++)
            {
                string uid = _outlinerDragUids[i];
                if (string.IsNullOrEmpty(uid)) continue;
                Atom child = OutlinerEdits.GetAtom(uid);
                if (child == null || SceneUtils.IsSystemProtectedAtom(child)) continue;
                string before = OutlinerParentUidOf(child);
                if (string.IsNullOrEmpty(before)) continue;
                if (!OutlinerEdits.SetParent(child, null)) continue;
                _outlinerUndo.Push(uid + "|parent", "Parent", before, "");
                done++;
            }
            if (done == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.drag.already_root",
                    "Already parented to the scene root."), 2f);
                return;
            }
            ShowTemporaryStatus(done
                + VPBTranslation.T("outliner.drag.detached", " detached to the scene root"), 2f);
            _outlinerLastFocused = true;
            QueueOutlinerRebuild();
        }

        static string OutlinerParentUidOf(Atom atom)
        {
            if (atom == null) return "";
            try
            {
                Atom p = atom.parentAtom;
                return p != null ? p.uid : "";
            }
            catch { return ""; }
        }
    }
}
