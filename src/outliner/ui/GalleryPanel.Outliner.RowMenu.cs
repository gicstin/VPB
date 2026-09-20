using System;
using UnityEngine;
using UnityEngine.UI;
using VPB.Outliner;

namespace VPB
{
    public partial class GalleryPanel
    {
        GameObject _outlinerRowMenuGO;

        void CloseOutlinerRowMenu()
        {
            if (_outlinerRowMenuGO != null)
            {
                try { UnityEngine.Object.Destroy(_outlinerRowMenuGO); } catch { }
                _outlinerRowMenuGO = null;
            }
        }

        void ShowOutlinerRowMenu(string atomUid, string nodeId, GameObject anchor)
        {
            CloseOutlinerRowMenu();
            if (_outlinerRoot == null || string.IsNullOrEmpty(atomUid)) return;
            Atom atom = OutlinerEdits.GetAtom(atomUid);
            float s = OutlinerLiveScale();
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
            GameObject root = UI.CreatePopupMenuRoot(_outlinerRoot, "VPB_OutlinerRowMenu", CloseOutlinerRowMenu);
            _outlinerRowMenuGO = root;
            GameObject panel = UI.CreatePopupMenuPanel(root, "Panel", AnchorPresets.middleCenter,
                new Vector2(GalleryUiDesignTokens.OverflowMenuPanelWidthRef * s, rowH), Vector2.zero, TextAnchor.UpperLeft);

            bool prot = SceneUtils.IsSystemProtectedAtom(atom);
            AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.select", "Select"), rowH, () =>
            {
                CloseOutlinerRowMenu();
                _outlinerSelection.SelectOnly(atomUid);
                OutlinerEdits.SelectInVam(atom, false);
                RebuildOutlinerInspector();
            });
            AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.select_root", "Select Root"), rowH, () =>
            {
                CloseOutlinerRowMenu();
                SelectOutlinerRoot(atom);
            });
            AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.isolate_targets", "Isolate targets"), rowH, () =>
            {
                CloseOutlinerRowMenu();
                IsolateOutlinerTargets(atomUid);
            });
            AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.frame", "Frame"), rowH, () =>
            {
                CloseOutlinerRowMenu();
                FrameOutlinerAtom(atomUid);
            });
            if (!prot)
            {
                AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.rename", "Rename"), rowH, () =>
                {
                    CloseOutlinerRowMenu();
                    _outlinerSelection.SelectOnly(atomUid);
                    RebuildOutlinerInspector();
                    if (_outlinerUidField != null) _outlinerUidField.ActivateInputField();
                });
                AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.duplicate", "Duplicate"), rowH, () =>
                {
                    CloseOutlinerRowMenu();
                    StartCoroutine(OutlinerEdits.DuplicateRoutine(atom));
                    QueueOutlinerRebuild();
                });
                AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.parent", "Parent to selected"), rowH, () =>
                {
                    CloseOutlinerRowMenu();
                    ParentOutlinerToPrimary(atom);
                });
                AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.parent_root", "Parent to Root"), rowH, () =>
                {
                    CloseOutlinerRowMenu();
                    ParentOutlinerToRoot(atom);
                });
                AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.solo", "Hide others"), rowH, () =>
                {
                    CloseOutlinerRowMenu();
                    SuperController sc = SuperController.singleton;
                    if (sc != null)
                        OutlinerEdits.SoloHideOthers(atom, sc.GetAtoms());
                    QueueOutlinerRebuild();
                });
            }
            AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.copy_xform", "Copy coordinates"), rowH, () =>
            {
                CloseOutlinerRowMenu();
                CopyOutlinerTransform(atom);
            });
            if (!prot)
            {
                AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.paste_xform", "Paste coordinates"), rowH, () =>
                {
                    CloseOutlinerRowMenu();
                    PasteOutlinerTransform(atom, OutlinerPasteMode.PositionAndRotation);
                });
            }
            AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.copy_uid", "Copy uid"), rowH, () =>
            {
                CloseOutlinerRowMenu();
                try { GUIUtility.systemCopyBuffer = atomUid; } catch { }
            });
            AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.open_vam", "Open in VaM UI"), rowH, () =>
            {
                CloseOutlinerRowMenu();
                OutlinerEdits.SelectInVam(atom, false);
            });
            if (SceneUtils.IsPersonLikeAtom(atom) || OutlinerEdits.ResolveStorable(atom, OutlinerPlugins.ManagerStorableId) != null)
            {
                AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.add_plugin", "Add plugin…"), rowH, () =>
                {
                    CloseOutlinerRowMenu();
                    OpenOutlinerPluginWorkflow(atom);
                });
            }
            if (OutlinerPoseMorphs.Supports(atom))
            {
                AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.zero_pose", "Zero pose morphs"), rowH, () =>
                {
                    CloseOutlinerRowMenu();
                    ZeroOutlinerPoseMorphs(atom, OutlinerPoseMorphs.AllBuckets);
                });
            }
            AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.reveal_pkg", "Reveal source package"), rowH, () =>
            {
                CloseOutlinerRowMenu();
                RevealOutlinerPackage(atomUid);
            });
            if (!prot)
            {
                AddOutlinerMenuItem(panel, VPBTranslation.T("outliner.menu.delete", "Delete"), rowH, () =>
                {
                    CloseOutlinerRowMenu();
                    DeleteOutlinerAtom(atomUid);
                });
            }

            LayoutOutlinerPopup(root, panel, s, anchor);
            try { SetLayerRecursiveLocal(root, _outlinerRoot.layer); } catch { }
        }

        void PlaceOutlinerPopupAtAnchor(GameObject root, GameObject panel, float width, float s, GameObject anchor)
        {
            if (panel == null) return;
            RectTransform panelRT = panel.GetComponent<RectTransform>();
            RectTransform overlayRT = root != null
                ? root.GetComponent<RectTransform>()
                : (_outlinerRoot != null ? _outlinerRoot.GetComponent<RectTransform>() : null);
            if (panelRT == null || overlayRT == null) return;

            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(1f, 1f);

            Camera cam = null;
            try
            {
                Canvas canvas = overlayRT.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            }
            catch { }

            Vector2 local = Vector2.zero;
            bool mapped = false;
            if (anchor != null)
            {
                RectTransform aRT = anchor.GetComponent<RectTransform>();
                if (aRT != null)
                {
                    Vector3[] corners = new Vector3[4];
                    aRT.GetWorldCorners(corners);
                    Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                    mapped = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        overlayRT, screen, cam, out local);
                }
            }
            if (!mapped)
            {
                try
                {
                    mapped = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        overlayRT, Input.mousePosition, cam, out local);
                }
                catch { mapped = false; }
            }

            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
            if (mapped) panelRT.anchoredPosition = new Vector2(local.x, local.y - gap);
            else panelRT.anchoredPosition = new Vector2(width * 0.5f, -gap);

            float pad = GalleryUiDesignTokens.ControlGapRef * s;
            UI.ClampPopupMenuPanelX(panelRT, overlayRT, pad);
            UI.ClampPopupMenuPanelY(panelRT, overlayRT, pad);
        }

        void AddOutlinerMenuItem(GameObject panel, string label, float rowH, UnityEngine.Events.UnityAction act)
        {
            GameObject row = UI.AddStretchPopupMenuRow(panel.transform, label, act, false, true, rowH, null);
            if (row == null) return;
            Text t = row.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        void LayoutOutlinerPopup(GameObject root, GameObject panel, float s, GameObject anchor)
        {
            float widthRef = MeasureOutlinerPopupWidthRef(panel);
            ScaleVerticalPopupMenuRows(
                panel,
                s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                widthRef);
            ClipOutlinerPopupRowText(panel);
            PlaceOutlinerPopupAtAnchor(root, panel, widthRef * s, s, anchor);
        }

        void ScaleOutlinerPopupPanel(GameObject panel, float s)
        {
            float widthRef = MeasureOutlinerPopupWidthRef(panel);
            ScaleVerticalPopupMenuRows(
                panel,
                s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                widthRef);
            ClipOutlinerPopupRowText(panel);
        }

        static void ClipOutlinerPopupRowText(GameObject panel)
        {
            if (panel == null) return;
            Transform tr = panel.transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                Transform ch = tr.GetChild(i);
                if (ch == null) continue;
                Text t = ch.GetComponentInChildren<Text>(true);
                if (t == null) continue;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        static float MeasureOutlinerPopupWidthRef(GameObject panel)
        {
            int longest = 0;
            if (panel != null)
            {
                Transform tr = panel.transform;
                for (int i = 0; i < tr.childCount; i++)
                {
                    Transform ch = tr.GetChild(i);
                    if (ch == null) continue;
                    Text t = ch.GetComponentInChildren<Text>(true);
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    if (t.text.Length > longest) longest = t.text.Length;
                }
            }
            return OutlinerPlugins.PopupWidthRefForCharCount(longest);
        }

        void ParentOutlinerToRoot(Atom child)
        {
            if (child == null) return;
            string before = "";
            try
            {
                Atom p = child.parentAtom;
                if (p != null) before = p.uid;
            }
            catch { }
            if (OutlinerEdits.SetParent(child, null))
            {
                _outlinerUndo.Push(child.uid + "|parent", "Parent", before, "");
                QueueOutlinerRebuild();
            }
        }

        void SelectOutlinerRoot(Atom atom)
        {
            if (atom == null) return;
            string uid = "";
            try { uid = atom.uid; } catch { }
            if (!string.IsNullOrEmpty(uid))
                _outlinerSelection.SelectOnly(uid);
            if (!OutlinerEdits.SelectRoot(atom, false))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.select_root_fail", "This atom has no Root controller."), 2f);
                return;
            }
            RebuildOutlinerInspector();
        }

        void ParentOutlinerToPrimary(Atom child)
        {
            if (child == null) return;
            string target = _outlinerSelection.PrimaryUid;
            if (string.IsNullOrEmpty(target) || string.Equals(target, child.uid, StringComparison.Ordinal))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.parent_need", "Select a different atom first."), 2f);
                return;
            }
            Atom parent = OutlinerEdits.GetAtom(target);
            string before = "";
            try
            {
                Atom p = child.parentAtom;
                if (p != null) before = p.uid;
            }
            catch { }
            if (OutlinerEdits.SetParent(child, parent))
            {
                _outlinerUndo.Push(child.uid + "|parent", "Parent", before, target);
                QueueOutlinerRebuild();
            }
        }

        void DeleteOutlinerAtom(string uid)
        {
            Atom atom = OutlinerEdits.GetAtom(uid);
            if (atom == null) return;
            string snapshot = OutlinerEdits.CaptureAtomJson(atom);
            if (!OutlinerEdits.Delete(atom))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked",
                    "Cannot edit this while Play mode is on, or the atom is locked."), 2f);
                return;
            }
            if (!string.IsNullOrEmpty(snapshot))
            {
                _outlinerUndo.Push(uid + "|delete",
                    VPBTranslation.T("outliner.delete.label", "Delete"), snapshot, "");
                ShowTemporaryStatus(VPBTranslation.T("outliner.delete.done", "Deleted ") + uid
                    + VPBTranslation.T("outliner.delete.undo_hint", " — Undo restores it."), 4f);
            }
            else
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.delete.done_no_undo", "Deleted ") + uid, 3f);
            }
            _outlinerLastFocused = true;
            QueueOutlinerRebuild();
        }

        void RevealOutlinerPackage(string uid)
        {
            if (_outlinerModel == null) return;
            OutlinerNode n = _outlinerModel.Find("atom:" + uid);
            if (n == null || string.IsNullOrEmpty(n.PackageUid))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.no_package", "No source package on this atom."), 2f);
                return;
            }
            try { ApplySearchWithinFilter(n.PackageUid); } catch { }
        }
    }
}
