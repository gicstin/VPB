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

                if (slot.PosSaved)
                    _layoutFloatSavedPosCenter = new Vector2(slot.PosX, slot.PosY);

                if (slot.SizeSaved
                    && slot.WidthRef >= LayoutFloatMinWidthRef
                    && slot.HeightRef >= LayoutFloatMinHeightRef)
                {
                    _layoutFloatSavedSizeRef = new Vector2(
                        Mathf.Clamp(slot.WidthRef, LayoutFloatMinWidthRef, LayoutFloatMaxWidthRef),
                        Mathf.Clamp(slot.HeightRef, LayoutFloatMinHeightRef, LayoutFloatMaxHeightRef));
                }
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

                if (_layoutFloatSavedPosCenter.HasValue)
                {
                    slot.PosSaved = true;
                    slot.PosX = _layoutFloatSavedPosCenter.Value.x;
                    slot.PosY = _layoutFloatSavedPosCenter.Value.y;
                }
                if (_layoutFloatSavedSizeRef.HasValue)
                {
                    slot.SizeSaved = true;
                    slot.WidthRef = _layoutFloatSavedSizeRef.Value.x;
                    slot.HeightRef = _layoutFloatSavedSizeRef.Value.y;
                }
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
