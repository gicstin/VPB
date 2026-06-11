using System;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private static GalleryPanel ResolveGalleryPanelForModal()
        {
            try
            {
                var p = GalleryPanel.GetAnchoredInstance();
                if (p != null) return p;
            }
            catch { }

            try
            {
                var g = Gallery.singleton;
                var list = g != null ? g.Panels : null;
                if (list == null || list.Count == 0) return null;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p != null && p.IsVisible) return p;
                }
                return list[0];
            }
            catch { return null; }
        }

        public void OpenScanWhitelistManagerFromGallery()
        {
            var panel = ResolveGalleryPanelForModal();
            if (panel == null) return;
            try { panel.ShowScanWhitelistEditorModal(); } catch { }
        }

        public void OpenQuickMenuPositionEditorFromGallery()
        {
            var panel = ResolveGalleryPanelForModal();
            if (panel == null) return;
            try { panel.ShowQuickMenuPositionEditorModal(); } catch { }
        }

        public void RequestScanWhitelistDisableConfirmFromGallery()
        {
            var panel = ResolveGalleryPanelForModal();
            if (panel == null) return;
            try { panel.ShowScanWhitelistDisableConfirmModal(); } catch { }
        }

        public void OpenBenchEditorFromGallery()
        {
            var panel = ResolveGalleryPanelForModal();
            if (panel == null) return;
            try { panel.ShowBenchEditorModal(); } catch { }
        }

        public void RequestBenchRunFromGallery()
        {
            try { VpbBenchRunner.RequestRunNow(this); } catch (Exception ex) { LogUtil.LogError("[VPB.Bench] Run: " + ex.Message); }
        }

        public void GalleryPreviewQuickMenuGrid(float relCreateX, float relCreateY)
        {
            try
            {
                QuickMenuApplyGridLayoutFromAnchor(QuickMenuAnchorBaseline + new Vector2(relCreateX, relCreateY));
            }
            catch { }
        }

        public void GalleryRestoreQuickMenuGridPreview(Vector2 savedCreateAnchor)
        {
            try { QuickMenuApplyGridLayoutFromAnchor(savedCreateAnchor); }
            catch { }
        }

        public void GallerySaveQuickMenuGridPosition(float relCreateX, float relCreateY, float relCreateXVR, float relCreateYVR, bool sameInVr)
        {
            var newCreate = QuickMenuAnchorBaseline + new Vector2(relCreateX, relCreateY);
            var newCreateVr = sameInVr
                ? newCreate
                : (QuickMenuAnchorBaseline + new Vector2(relCreateXVR, relCreateYVR));

            if (Settings.Instance != null)
            {
                if (Settings.Instance.QuickMenuCreateGalleryPosDesktop != null)
                    Settings.Instance.QuickMenuCreateGalleryPosDesktop.Value = newCreate;
                if (Settings.Instance.QuickMenuCreateGalleryPosVR != null)
                    Settings.Instance.QuickMenuCreateGalleryPosVR.Value = newCreateVr;
                if (Settings.Instance.QuickMenuCreateGalleryUseSameInVR != null)
                    Settings.Instance.QuickMenuCreateGalleryUseSameInVR.Value = sameInVr;
            }
            try { Config.Save(); } catch { }
        }

        public void GalleryResetQuickMenuGridPositionDefaults(out float relX, out float relY)
        {
            relX = 0f;
            relY = 0f;
            GalleryPreviewQuickMenuGrid(relX, relY);
        }

        public bool CanOpenQuickMenuPositionEditor()
        {
            return !XrUtils.IsVrActive();
        }
    }
}
