using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void LoadLayoutFloatGeometryFromConfig()
        {
            _layoutFloatSavedPosCenter = null;
            _layoutFloatSavedSizeRef = null;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg == null) return;

                FloatGeometrySlot slot = cfg.GalleryLayoutPresetsFloatGeometry.Current;
                if (slot == null) return;

                _layoutFloatSavedPosCenter = slot.SavedPos;
                _layoutFloatSavedSizeRef = slot.SavedSize(
                    new Vector2(LayoutFloatMinWidthRef, LayoutFloatMinHeightRef),
                    new Vector2(LayoutFloatMaxWidthRef, LayoutFloatMaxHeightRef));
            }
            catch { }
        }

        private void CaptureLayoutFloatGeometryToMemory()
        {
            if (_layoutFloatPanelRT == null) return;
            float s = _layoutFloatChromeScale > 0f ? _layoutFloatChromeScale : 1f;

            Vector2 topLeft = _layoutFloatPanelRT.anchoredPosition;
            Vector2 size = _layoutFloatPanelRT.sizeDelta;
            _layoutFloatSavedPosCenter = new Vector2(topLeft.x + size.x * 0.5f, topLeft.y - size.y * 0.5f);
            _layoutFloatSavedSizeRef = new Vector2(
                Mathf.Clamp(size.x / s, LayoutFloatMinWidthRef, LayoutFloatMaxWidthRef),
                Mathf.Clamp(size.y / s, LayoutFloatMinHeightRef, LayoutFloatMaxHeightRef));
        }

        private void PersistLayoutFloatGeometry()
        {
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg == null) return;

                FloatGeometrySlot slot = cfg.GalleryLayoutPresetsFloatGeometry.Current;
                if (slot == null) return;

                slot.StorePos(_layoutFloatSavedPosCenter);
                slot.StoreSize(_layoutFloatSavedSizeRef);
            }
            catch { return; }
            try { ScheduleQuickFiltersConfigSave(); } catch { }
        }

        private void OnLayoutFloatMoved()
        {
            CaptureLayoutFloatGeometryToMemory();
            PersistLayoutFloatGeometry();
        }

        private void OnLayoutFloatResized()
        {
            CaptureLayoutFloatGeometryToMemory();
            PersistLayoutFloatGeometry();
            // Row window depends on viewport height, so a resize must recompute the visible slice.
            _layoutFloatWindowStart = -1;
            RebuildLayoutPresetWindow(true);
        }
    }
}
