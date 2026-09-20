using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VPB.Outliner;

namespace VPB
{
    public partial class GalleryPanel
    {
        Image _outlinerDropHoverImg;
        Color _outlinerDropHoverIdle;
        bool _outlinerDropHoverOn;

        internal bool IsPointerOverSceneOutliner(PointerEventData eventData)
        {
            if (!IsSceneOutlinerOpen() || _outlinerPanelRT == null || eventData == null)
                return false;
            Camera cam = eventData.pressEventCamera;
            if (cam == null) cam = eventData.enterEventCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(
                _outlinerPanelRT, eventData.position, cam);
        }

        internal Atom ResolveOutlinerDropAtom()
        {
            return OutlinerEdits.GetAtom(_outlinerSelection.PrimaryUid);
        }

        internal string DescribeOutlinerDrop(PointerEventData eventData, string itemName)
        {
            if (!IsPointerOverSceneOutliner(eventData)) return null;
            string uid = _outlinerSelection.PrimaryUid;
            string who = uid;
            Atom atom = ResolveOutlinerDropAtom();
            if (atom != null)
            {
                try { who = atom.uid; } catch { who = uid; }
            }
            return OutlinerPlugins.DropHover(itemName, who);
        }

        internal void SetOutlinerDropHover(PointerEventData eventData)
        {
            bool on = IsPointerOverSceneOutliner(eventData);
            if (on == _outlinerDropHoverOn) return;
            _outlinerDropHoverOn = on;
            ApplyOutlinerDropHoverVisual();
        }

        internal void ClearOutlinerDropHover()
        {
            if (!_outlinerDropHoverOn) return;
            _outlinerDropHoverOn = false;
            ApplyOutlinerDropHoverVisual();
        }

        void ApplyOutlinerDropHoverVisual()
        {
            if (_outlinerDropHoverImg == null) return;
            _outlinerDropHoverImg.color = _outlinerDropHoverOn
                ? Color.Lerp(_outlinerDropHoverIdle, GalleryUiColorTokens.AccentSelected, 0.22f)
                : _outlinerDropHoverIdle;
        }

        void BindOutlinerDropHoverImage(Image img)
        {
            _outlinerDropHoverImg = img;
            if (img != null) _outlinerDropHoverIdle = img.color;
        }

        internal bool TryConsumeOutlinerDrop(PointerEventData eventData, UIDraggableItem drag)
        {
            ClearOutlinerDropHover();
            if (drag == null || drag.FileEntry == null) return false;
            if (!IsPointerOverSceneOutliner(eventData)) return false;

            Atom atom = ResolveOutlinerDropAtom();
            if (atom == null)
            {
                ShowTemporaryStatus(VPBTranslation.T(
                    "outliner.drop.need_atom",
                    "Select an atom in Scene Overview, then drop."), 2f);
                return true;
            }

            if (!drag.ApplyToOutlinerAtom(atom))
            {
                string who = "";
                try { who = atom.uid; } catch { }
                ShowTemporaryStatus(VPBTranslation.T(
                    "outliner.drop.rejected",
                    "That item cannot apply to ") + who, 2.2f);
                return true;
            }

            QueueOutlinerRebuild();
            return true;
        }

        void OpenOutlinerPluginWorkflow(Atom atom)
        {
            if (atom == null) return;
            string uid = "";
            try { uid = atom.uid; } catch { }

            try { OpenPluginsFloat(forceShow: true); }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Outliner] plugins list failed: " + ex.Message);
            }

            if (SceneUtils.IsPersonLikeAtom(atom) && !string.IsNullOrEmpty(uid))
                OpenPluginsFloatPersonUi(uid);
            else
            {
                OutlinerEdits.SelectInVam(atom, false);
                StartCoroutine(OpenOutlinerAtomTabRoutine(atom, "plugin"));
            }

            string who = uid;
            ShowTemporaryStatus(
                OutlinerPlugins.DropHover(
                    VPBTranslation.T("outliner.drop.plugin_noun", "a plugin"),
                    who),
                2.4f);
            LogUtil.Log("[VPB.Outliner] plugin workflow opened for " + who);
        }
    }
}
