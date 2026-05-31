using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Assigned when the footer compress-cache button is built. That build path is not wired, so
        // the field stays null and the hover handlers null-guard and no-op (dormant, not dead).
#pragma warning disable 0649
        private GameObject footerCompressCacheBtn;
#pragma warning restore 0649
        private int _footerCompressCacheCount = -1;
        private bool _footerCompressCacheHovering;
        private bool _footerCompressCacheCounting;
        private float _footerCompressCacheCountTimer;
        private int _footerCompressCacheTooltipKey = int.MinValue;

        public static Sprite LoadCompressCacheIconSprite(Color tint)
        {
            Sprite s = UI.LoadIconSprite("vpb_icons/compress.png", tint);
            if (s != null) return s;
            return UI.LoadIconSprite("vpb_icons/cache_texture.png", tint);
        }

        public static void GalleryTriggerBulkZstdCompression()
        {
            try
            {
                var mgr = ImageLoadingMgr.singleton;
                if (mgr == null) return;
                if (mgr.CurrentZstdStats != null && mgr.CurrentZstdStats.IsRunning)
                {
                    LogUtil.Log("[VPB] Bulk Zstd already running.");
                    return;
                }
                mgr.StartBulkZstdCompression();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] GalleryTriggerBulkZstdCompression: " + ex.Message);
            }
        }

        private void FooterCompressCacheHoverTick()
        {
            if (!_footerCompressCacheHovering || footerCompressCacheBtn == null) return;
            if (_footerCompressCacheCounting) return;
            if (Time.unscaledTime - _footerCompressCacheCountTimer < 2f) return;
            _footerCompressCacheCountTimer = Time.unscaledTime;
            _footerCompressCacheCounting = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int count = 0;
                try
                {
                    var plugin = VamHookPlugin.singleton;
                    if (plugin != null) count = plugin.GetVamCacheCompressibleCount();
                }
                catch { }
                _footerCompressCacheCount = count;
                _footerCompressCacheCounting = false;
            });
        }

        private void FooterCompressCachePollHoverTooltip()
        {
            if (!_footerCompressCacheHovering || footerCompressCacheBtn == null) return;
            int key = _footerCompressCacheCounting ? -2 : _footerCompressCacheCount;
            if (key == _footerCompressCacheTooltipKey) return;
            _footerCompressCacheTooltipKey = key;
            FooterCompressCacheUpdateHoverTooltip();
        }

        private string FooterCompressCacheTooltipText()
        {
            if (_footerCompressCacheCount > 0)
                return string.Format(VPBTranslation.T("hook.compress_cache_count", "Compress Cache ({0})"), _footerCompressCacheCount);
            if (_footerCompressCacheCounting || _footerCompressCacheCount < 0)
                return VPBTranslation.T("hook.compress_cache", "Compress Cache") + " …";
            return VPBTranslation.T("hook.compress_cache", "Compress Cache");
        }

        private void FooterCompressCacheUpdateHoverTooltip()
        {
            if (!_footerCompressCacheHovering || footerCompressCacheBtn == null) return;
            if (temporaryStatusCoroutine != null)
            {
                try { StopCoroutine(temporaryStatusCoroutine); } catch { }
                temporaryStatusCoroutine = null;
            }
            temporaryStatusMsg = FooterCompressCacheTooltipText();
            temporaryStatusOwner = footerCompressCacheBtn;
        }

        private void RegisterFooterCompressCacheHover(GameObject go)
        {
            if (go == null) return;
            var del = go.GetComponent<UIHoverDelegate>();
            if (del == null) del = go.AddComponent<UIHoverDelegate>();
            del.OnHoverChange += (enter) =>
            {
                if (enter) hoverCount++;
                else hoverCount--;
                if (hoverCount < 0) hoverCount = 0;
                _footerCompressCacheHovering = enter;
                if (enter)
                {
                    _footerCompressCacheCountTimer = 0f;
                    FooterCompressCacheUpdateHoverTooltip();
                    FooterCompressCacheHoverTick();
                }
                else
                {
                    _footerCompressCacheTooltipKey = int.MinValue;
                    if (temporaryStatusOwner == go)
                    {
                        temporaryStatusMsg = null;
                        temporaryStatusOwner = null;
                    }
                }
            };
            del.OnPointerEnterEvent += (d) => { currentPointerData = d; };
        }
    }
}
