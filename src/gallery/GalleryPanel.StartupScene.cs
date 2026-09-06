using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>Ignition orange — shape (rocket) plus hue, not color-only (Johnson).</summary>
        private static readonly Color GalleryBadgeStartupIcon = new Color(1f, 0.52f, 0.18f, 1f);
        private static readonly Color GalleryBadgeStartupRing = new Color(1f, 0.62f, 0.28f, 0.95f);
        private static readonly Color GalleryStartupCellTint = new Color(0.62f, 0.48f, 0.22f, 1f);

        private Sprite _startupSceneBadgeSprite;

        private static Sprite GridCtxStartupIcon()
        {
            Sprite s = null;
            try { s = UI.LoadIconSprite("rocket", Color.white); } catch { s = null; }
            if (s != null) return s;
            try { return UI.LoadIconSprite("clock-play", Color.white); } catch { return null; }
        }

        private static Sprite GridCtxStartupOffIcon()
        {
            Sprite s = null;
            try { s = UI.LoadIconSprite("rocket-off", Color.white); } catch { s = null; }
            if (s != null) return s;
            try { return UI.LoadIconSprite("x", Color.white); } catch { return null; }
        }

        private bool TryGetSingleSelectedSceneForStartup(out FileEntry scene)
        {
            scene = null;
            if (selectedFiles == null || selectedFiles.Count != 1) return false;
            FileEntry f = selectedFiles[0];
            if (f == null || !IsSceneGallerySelection(f)) return false;

            FileEntry resolved = null;
            try { resolved = TryResolveSceneCategoryPackageRowToSceneJson(f); }
            catch { resolved = null; }
            scene = resolved != null ? resolved : f;
            return scene != null;
        }

        private void GridCtxAddStartupSceneActions()
        {
            FileEntry scene;
            if (!TryGetSingleSelectedSceneForStartup(out scene)) return;

            string path = VpbStartupScene.PathFromFileEntry(scene);
            if (string.IsNullOrEmpty(path)) return;

            bool hasConfigured = VpbStartupScene.HasPath();
            bool isCurrent = VpbStartupScene.PathsEqual(path, VpbStartupScene.GetPath());

            GridCtxAddSeparator();

            if (isCurrent)
            {
                GridCtxAddAction(
                    VPBTranslation.T("gallery.gridctx.startup_already", "Already startup scene"),
                    KeyCode.None, "",
                    GridCtxStartupIcon(),
                    EmptyGridCtxAction,
                    enabled: false);
            }
            else
            {
                GridCtxAddAction(
                    VPBTranslation.T("gallery.gridctx.set_startup_scene", "Set as startup scene"),
                    KeyCode.F, "F",
                    GridCtxStartupIcon(),
                    () =>
                    {
                        CloseGridContextMenu();
                        try { SetStartupSceneFromSelection(); } catch { }
                    });
            }

            if (hasConfigured)
            {
                GridCtxAddAction(
                    VPBTranslation.T("gallery.gridctx.clear_startup_scene", "Clear startup scene"),
                    KeyCode.X, "X",
                    GridCtxStartupOffIcon(),
                    () =>
                    {
                        CloseGridContextMenu();
                        try { ClearStartupSceneFromMenu(); } catch { }
                    });
            }
        }

        private static readonly UnityAction EmptyGridCtxAction = () => { };

        private void SetStartupSceneFromSelection()
        {
            FileEntry scene;
            if (!TryGetSingleSelectedSceneForStartup(out scene))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.startup.select_one", "Select one scene to set as startup."),
                    2.5f);
                return;
            }

            string path = VpbStartupScene.PathFromFileEntry(scene);
            if (string.IsNullOrEmpty(path))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.startup.no_path", "Could not read a loadable path for that scene."),
                    2.5f);
                return;
            }

            if (LocalSceneGallerySupport.IsVpbGeneratedLocalScenePath(path))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.startup.temp_blocked", "VPB temp scenes cannot be startup scenes."),
                    2.5f);
                return;
            }

            string previous = VpbStartupScene.GetPath();
            if (VpbStartupScene.PathsEqual(previous, path))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.startup.already", "Already the startup scene."),
                    2f);
                return;
            }

            VpbStartupScene.SetPath(path);
            NotifyStartupSceneSettingChanged();

            string name = VpbStartupScene.DisplayNameFromPath(path);
            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("gallery.startup.set", "Startup scene: {0}"),
                    name),
                2.5f);

            PushUndo(() =>
            {
                VpbStartupScene.SetPath(previous);
                NotifyStartupSceneSettingChanged();
                if (string.IsNullOrEmpty(previous))
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.startup.cleared", "Startup scene cleared."),
                        2f);
                }
                else
                {
                    ShowTemporaryStatus(
                        string.Format(
                            VPBTranslation.T("gallery.startup.set", "Startup scene: {0}"),
                            VpbStartupScene.DisplayNameFromPath(previous)),
                        2.5f);
                }
            }, VPBTranslation.T("gallery.undo.set_startup_scene", "Set startup scene"));
        }

        private void ClearStartupSceneFromMenu()
        {
            string previous = VpbStartupScene.GetPath();
            if (string.IsNullOrEmpty(previous))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.startup.none", "No startup scene is set."),
                    2f);
                return;
            }

            VpbStartupScene.SetPath("");
            NotifyStartupSceneSettingChanged();
            ShowTemporaryStatus(
                VPBTranslation.T("gallery.startup.cleared", "Startup scene cleared."),
                2.5f);

            PushUndo(() =>
            {
                VpbStartupScene.SetPath(previous);
                NotifyStartupSceneSettingChanged();
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T("gallery.startup.set", "Startup scene: {0}"),
                        VpbStartupScene.DisplayNameFromPath(previous)),
                    2.5f);
            }, VPBTranslation.T("gallery.undo.clear_startup_scene", "Clear startup scene"));
        }

        private void NotifyStartupSceneSettingChanged()
        {
            try
            {
                if (IsSettingsPanelOpen())
                    RefreshInternalSettingsListRows(true);
            }
            catch { }
            try { RefreshVisibleGridVisualsOnly(); } catch { }
        }

        private Transform EnsureStartupSceneBadge(GameObject btnGO, FileButtonBinder b)
        {
            if (btnGO == null) return null;
            Transform existing = b != null ? b.startupSceneBadgeTr : null;
            if (existing == null)
                existing = FindGalleryBadgeTransform(btnGO.transform, "StartupSceneBadge");
            if (existing != null)
            {
                if (b != null) b.startupSceneBadgeTr = existing;
                return existing;
            }

            GameObject go = new GameObject("StartupSceneBadge");
            go.transform.SetParent(btnGO.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(32f, 32f);
            rt.anchoredPosition = new Vector2(6f, -6f);
            AddGalleryBadgeBackground(go);
            EnsureStartupBadgeRing(go);

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(go.transform, false);
            Image icon = iconGO.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.color = GalleryBadgeStartupIcon;
            RectTransform irt = icon.rectTransform;
            irt.anchorMin = new Vector2(0.18f, 0.18f);
            irt.anchorMax = new Vector2(0.82f, 0.82f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;

            UI.AddLE(go, minWidth: 32f, minHeight: 32f, preferredWidth: 32f, preferredHeight: 32f);
            go.SetActive(false);

            if (b != null) b.startupSceneBadgeTr = go.transform;
            return go.transform;
        }

        private static void EnsureStartupBadgeRing(GameObject badgeGO)
        {
            if (badgeGO == null) return;
            if (badgeGO.transform.Find("StartupRing") != null) return;

            GameObject ringGO = new GameObject("StartupRing");
            ringGO.transform.SetParent(badgeGO.transform, false);
            RectTransform rt = ringGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            RoundedRectOutline outline = ringGO.AddComponent<RoundedRectOutline>();
            outline.raycastTarget = false;
            outline.borderThickness = 2f;
            outline.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            outline.color = GalleryBadgeStartupRing;
            ringGO.SetActive(true);
        }

        private Sprite GetStartupSceneBadgeSprite()
        {
            if (_startupSceneBadgeSprite != null) return _startupSceneBadgeSprite;
            try { _startupSceneBadgeSprite = UI.LoadIconSprite("rocket", Color.white); } catch { }
            if (_startupSceneBadgeSprite == null)
            {
                try { _startupSceneBadgeSprite = UI.LoadIconSprite("clock-play", Color.white); } catch { }
            }
            return _startupSceneBadgeSprite;
        }

        private bool ApplyStartupSceneBadgeVisual(GameObject btnGO, FileEntry file)
        {
            FileButtonBinder b = btnGO != null ? FileButtonBinder.GetOrAdd(btnGO) : null;
            Transform tr = EnsureStartupSceneBadge(btnGO, b);
            if (tr == null) return false;
            GameObject badgeGO = tr.gameObject;

            bool sceneCat = VpbStartupScene.CategoryLooksLikeScenes(currentCategoryTitle);
            bool show = VpbStartupScene.MatchesGalleryRow(file, sceneCat);
            if (badgeGO.activeSelf != show)
                badgeGO.SetActive(show);
            if (!show) return false;

            EnsureStartupBadgeRing(badgeGO);
            Transform iconTr = tr.Find("Icon");
            Image icon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            if (icon != null)
            {
                Sprite spr = GetStartupSceneBadgeSprite();
                if (icon.sprite != spr) icon.sprite = spr;
                icon.color = GalleryBadgeStartupIcon;
            }

            AddTooltipPlain(
                badgeGO,
                VPBTranslation.T(
                    "gallery.badge.tip.startup_scene",
                    "Startup scene: VaM loads this scene after World UI is ready."));
            return true;
        }
    }
}
