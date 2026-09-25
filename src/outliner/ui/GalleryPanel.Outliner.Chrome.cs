using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private bool _outlinerVrCached;
        private int _outlinerVrCachedFrame = -1;

        private bool OutlinerVr()
        {
            int frame = Time.frameCount;
            if (frame == _outlinerVrCachedFrame) return _outlinerVrCached;
            _outlinerVrCachedFrame = frame;
            bool vr = false;
            try { vr = XrUtils.IsVrActive(); }
            catch { vr = false; }
            _outlinerVrCached = vr;
            return vr;
        }

        private float OutlinerSlotRef()
        {
            return OutlinerVr()
                ? GalleryUiDesignTokens.OutlinerVrRowHeightRef
                : GalleryUiDesignTokens.ControlSlotHeightRef;
        }

        private float OutlinerSlotH(float s)
        {
            return OutlinerSlotRef() * s;
        }

        private static readonly CreatorStripKeepKind[] OutlinerAtomKindOrder =
        {
            CreatorStripKeepKind.Persons,
            CreatorStripKeepKind.Lights,
            CreatorStripKeepKind.Sound,
            CreatorStripKeepKind.Cameras,
            CreatorStripKeepKind.Animation,
            CreatorStripKeepKind.Assets,
            CreatorStripKeepKind.Props,
            CreatorStripKeepKind.Toys,
            CreatorStripKeepKind.Forces,
            CreatorStripKeepKind.UI,
            CreatorStripKeepKind.SubScenes,
            CreatorStripKeepKind.Other
        };

        private static string OutlinerAtomKindIcon(CreatorStripKeepKind kind)
        {
            switch (kind)
            {
                case CreatorStripKeepKind.Persons: return "user";
                case CreatorStripKeepKind.Lights: return "bulb";
                case CreatorStripKeepKind.Sound: return "volume";
                case CreatorStripKeepKind.Cameras: return "camera";
                case CreatorStripKeepKind.Animation: return "timeline";
                case CreatorStripKeepKind.Assets: return "box";
                case CreatorStripKeepKind.Props: return "armchair";
                case CreatorStripKeepKind.Toys: return "wand";
                case CreatorStripKeepKind.Forces: return "wind";
                case CreatorStripKeepKind.UI: return "forms";
                case CreatorStripKeepKind.SubScenes: return "sitemap";
                default: return "apps";
            }
        }

        private static string OutlinerAtomKindLabel(CreatorStripKeepKind kind)
        {
            switch (kind)
            {
                case CreatorStripKeepKind.Persons: return VPBTranslation.T("outliner.kind.persons", "People");
                case CreatorStripKeepKind.Lights: return VPBTranslation.T("outliner.kind.lights", "Lights");
                case CreatorStripKeepKind.Sound: return VPBTranslation.T("outliner.kind.sound", "Sound");
                case CreatorStripKeepKind.Cameras: return VPBTranslation.T("outliner.kind.cameras", "Cameras");
                case CreatorStripKeepKind.Animation: return VPBTranslation.T("outliner.kind.animation", "Animation");
                case CreatorStripKeepKind.Assets: return VPBTranslation.T("outliner.kind.assets", "Custom assets");
                case CreatorStripKeepKind.Props: return VPBTranslation.T("outliner.kind.props", "Props and shapes");
                case CreatorStripKeepKind.Toys: return VPBTranslation.T("outliner.kind.toys", "Toys");
                case CreatorStripKeepKind.Forces: return VPBTranslation.T("outliner.kind.forces", "Forces and cloth");
                case CreatorStripKeepKind.UI: return VPBTranslation.T("outliner.kind.ui", "UI and triggers");
                case CreatorStripKeepKind.SubScenes: return VPBTranslation.T("outliner.kind.subscenes", "SubScenes");
                default: return VPBTranslation.T("outliner.kind.other", "Other");
            }
        }

        private static float OutlinerBarInset(float s)
        {
            return GalleryUiDesignTokens.ControlRimGutterRef * 2f * s;
        }

        private static float OutlinerBarIconSize(float barH, float s)
        {
            return Mathf.Max(GalleryUiDesignTokens.FontMinRef * s, barH - OutlinerBarInset(s) * 2f);
        }

        private static void OutlinerUseInwardHoverRim(GameObject go)
        {
            if (go == null) return;
            UIHoverBorder hb = go.GetComponent<UIHoverBorder>();
            if (hb == null) return;
            hb.inward = true;
            try { hb.ApplyBorderSettings(); } catch { }
        }

        private void FitOutlinerCornerRadius(GameObject go, float minSidePx)
        {
            if (go == null) return;
            float frac = UI.ResolveGalleryElementCornerRadiusFraction();
            float refSide = GalleryUiDesignTokens.ButtonSizeRef
                * (_outlinerChromeScale > 0.01f ? _outlinerChromeScale : 1f);
            float wantPx = frac * refSide;
            float use = minSidePx > 1f ? Mathf.Clamp(wantPx / minSidePx, 0.02f, frac) : frac;
            RoundedRect rr = go.GetComponent<RoundedRect>();
            if (rr != null)
            {
                rr.excludeFromGlobalRadiusSync = true;
                rr.cornerRadiusFraction = use;
            }
            UIHoverBorder hb = go.GetComponent<UIHoverBorder>();
            if (hb != null)
            {
                try { hb.ApplyBorderSettings(); } catch { }
            }
        }

    }
}
