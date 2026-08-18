using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>
        /// Float windows a layout restores. Transient popups (quick-tag menu, Remap Atom UIDs, Bench,
        /// command palette) are deliberately absent — restoring a modal is never what "my layout" means.
        /// Strip Scene is a mode, not an arrangement, and the quick-menu assign float belongs to the
        /// quick menu rather than to a gallery pane.
        /// </summary>
        private static readonly LayoutFloatKind[] LayoutCapturedFloatKinds =
        {
            LayoutFloatKind.Settings,
            LayoutFloatKind.Plugins,
            LayoutFloatKind.QuickFilters,
            LayoutFloatKind.ImportSidebar
        };

        private static FloatGeometryPair LayoutFloatGeometryPair(LayoutFloatKind kind)
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return null;
            switch (kind)
            {
                case LayoutFloatKind.Settings: return cfg.GallerySettingsFloatGeometry;
                case LayoutFloatKind.Plugins: return cfg.GalleryPluginsFloatGeometry;
                case LayoutFloatKind.QuickFilters: return cfg.GalleryQuickFiltersGeometry;
                case LayoutFloatKind.ImportSidebar: return cfg.GalleryImportSidebarGeometry;
                case LayoutFloatKind.CreatorStrip: return cfg.CreatorStripPanelGeometry;
                case LayoutFloatKind.DetailStripTagMenu: return cfg.GalleryDetailStripTagMenuGeometry;
                case LayoutFloatKind.QuickMenuAssign: return cfg.QuickMenuAssignFloatGeometry;
            }
            return null;
        }

        private bool IsLayoutFloatOpen(LayoutFloatKind kind)
        {
            try
            {
                switch (kind)
                {
                    case LayoutFloatKind.Settings:
                        return IsSettingsPanelOpen();
                    case LayoutFloatKind.Plugins:
                        return IsPluginsFloatOpen();
                    case LayoutFloatKind.QuickFilters:
                        return quickFiltersUI != null && quickFiltersUI.IsDetached && quickFiltersUI.IsVisible;
                    case LayoutFloatKind.ImportSidebar:
                        return importSidebarDetached && importSidebarOpenIntent;
                }
            }
            catch { }
            return false;
        }

        private bool IsLayoutFloatCollapsed(LayoutFloatKind kind)
        {
            switch (kind)
            {
                case LayoutFloatKind.Settings: return _settingsFloatCollapsed;
                case LayoutFloatKind.Plugins: return _pluginsFloatCollapsed;
            }
            return false;
        }

        internal void CaptureFloatStates(List<LayoutFloatState> into)
        {
            if (into == null) return;
            into.Clear();
            if (VPBConfig.Instance == null) return;

            for (int i = 0; i < LayoutCapturedFloatKinds.Length; i++)
            {
                LayoutFloatKind kind = LayoutCapturedFloatKinds[i];
                FloatGeometryPair pair = LayoutFloatGeometryPair(kind);
                if (pair == null) continue;

                FloatGeometrySlot slot = pair.Current;
                if (slot == null) continue;

                var f = new LayoutFloatState();
                f.Kind = (int)kind;
                f.Open = IsLayoutFloatOpen(kind);
                f.Collapsed = IsLayoutFloatCollapsed(kind);
                f.PosCenterRef = slot.PosSaved ? new Vector2(slot.PosX, slot.PosY) : Vector2.zero;
                f.SizeRef = slot.SizeSaved ? new Vector2(slot.WidthRef, slot.HeightRef) : Vector2.zero;
                into.Add(f);
            }
        }

        internal void ApplyPaneFloats(LayoutPaneState pane)
        {
            if (pane == null || pane.Floats == null) return;
            if (VPBConfig.Instance == null) return;

            for (int i = 0; i < pane.Floats.Count; i++)
            {
                LayoutFloatState f = pane.Floats[i];
                if (f == null) continue;

                LayoutFloatKind kind = (LayoutFloatKind)f.Kind;
                try { ApplyOneLayoutFloat(kind, f); }
                catch (Exception ex) { LogUtil.LogError("[VPB][Layout] float " + kind + ": " + ex.Message); }
            }
        }

        private void ApplyOneLayoutFloat(LayoutFloatKind kind, LayoutFloatState f)
        {
            FloatGeometryPair pair = LayoutFloatGeometryPair(kind);
            if (pair == null) return;

            FloatGeometrySlot slot = pair.Current;
            if (slot == null) return;

            bool hasPos = f.PosCenterRef.x != 0f || f.PosCenterRef.y != 0f;
            bool hasSize = f.SizeRef.x > 1f && f.SizeRef.y > 1f;

            if (hasPos)
            {
                slot.PosSaved = true;
                slot.PosX = f.PosCenterRef.x;
                slot.PosY = f.PosCenterRef.y;
            }
            if (hasSize)
            {
                slot.SizeSaved = true;
                slot.WidthRef = f.SizeRef.x;
                slot.HeightRef = f.SizeRef.y;
            }

            SyncLayoutFloatFromConfig(kind);
            SetLayoutFloatOpen(kind, f.Open);
            if (f.Open) PlaceLayoutFloatFromConfig(kind, hasPos, hasSize);
        }

        /// <summary>Re-reads the per-mode config slot into the float's in-memory saved geometry.</summary>
        private void SyncLayoutFloatFromConfig(LayoutFloatKind kind)
        {
            switch (kind)
            {
                case LayoutFloatKind.Settings:
                    LoadSettingsFloatGeometryFromConfig();
                    break;
                case LayoutFloatKind.Plugins:
                    LoadPluginsFloatGeometryFromConfig();
                    break;
                case LayoutFloatKind.ImportSidebar:
                    LoadImportSidebarFloatGeometryFromConfig();
                    break;
                case LayoutFloatKind.QuickFilters:
                    if (quickFiltersUI != null) quickFiltersUI.ReloadGeometryFromConfig();
                    break;
            }
        }

        private void SetLayoutFloatOpen(LayoutFloatKind kind, bool open)
        {
            if (IsLayoutFloatOpen(kind) == open) return;

            switch (kind)
            {
                case LayoutFloatKind.Settings:
                    if (open) OpenSettingsSideTab();
                    else ExitInternalSettingsMode(true);
                    break;
                case LayoutFloatKind.Plugins:
                    OpenPluginsFloat(open);
                    break;
                case LayoutFloatKind.QuickFilters:
                    if (quickFiltersUI == null) break;
                    if (open) quickFiltersUI.EnsureDetachedAndVisible();
                    else quickFiltersUI.SetVisible(false);
                    try { SyncQuickFilterToggleState(); } catch { }
                    break;
                case LayoutFloatKind.ImportSidebar:
                    // Open state is carried by the pane's own import fields; nothing extra to toggle.
                    break;
            }
        }

        /// <summary>
        /// Writes the restored geometry onto a float that is already on screen. Without this, a float
        /// open before the apply would keep its old position until closed and reopened.
        /// </summary>
        private void PlaceLayoutFloatFromConfig(LayoutFloatKind kind, bool hasPos, bool hasSize)
        {
            switch (kind)
            {
                case LayoutFloatKind.Settings:
                    if (hasSize) { try { RescaleSettingsFloatIfOpen(_settingsFloatChromeScale); } catch { } }
                    if (hasPos) PlaceFloatPanelFromCenter(_settingsFloatPanelRT, _settingsFloatSavedPosCenter);
                    break;

                case LayoutFloatKind.Plugins:
                    if (hasSize) { try { RescalePluginsFloatIfOpen(_pluginsFloatChromeScale); } catch { } }
                    if (hasPos) PlaceFloatPanelFromCenter(_pluginsFloatPanelRT, _pluginsFloatSavedPosCenter);
                    break;

                case LayoutFloatKind.ImportSidebar:
                    if (hasSize && importSidebarRT != null && importSidebarSavedFloatSizeRef.HasValue)
                    {
                        float s = ChromeScale > 0f ? ChromeScale : 1f;
                        importSidebarRT.sizeDelta = new Vector2(
                            importSidebarSavedFloatSizeRef.Value.x * s,
                            importSidebarSavedFloatSizeRef.Value.y * s);
                    }
                    if (hasPos) PlaceFloatPanelFromCenter(importSidebarRT, importSidebarSavedFloatPosCenter);
                    break;

                case LayoutFloatKind.QuickFilters:
                    if (quickFiltersUI != null)
                    {
                        try { quickFiltersUI.ApplyLayout(ChromeScale); }
                        catch { }
                    }
                    break;
            }
        }

        /// <summary>Floats store a centre; their RectTransform pivots top-left.</summary>
        private static void PlaceFloatPanelFromCenter(RectTransform rt, Vector2? savedCenter)
        {
            if (rt == null || !savedCenter.HasValue) return;
            Vector2 size = rt.sizeDelta;
            Vector2 c = savedCenter.Value;
            rt.anchoredPosition = new Vector2(c.x - size.x * 0.5f, c.y + size.y * 0.5f);
        }
    }
}
