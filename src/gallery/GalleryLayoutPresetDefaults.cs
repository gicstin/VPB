using System.Collections.Generic;

namespace VPB
{
    /// <summary>
    /// Shipped baseline arrangements. They exist only in memory: <see cref="GalleryLayoutPresetStore"/>
    /// appends them after the SQLite rows, and the writer skips them, so no user database ever carries
    /// a copy that could drift from the build or be half-deleted.
    /// Ids sit in a reserved high band so a user preset can never collide with one.
    /// </summary>
    internal static class GalleryLayoutPresetDefaults
    {
        internal const int IdBase = 900000;

        /// <summary>Side dock occupying the golden-ratio minor share of the screen.</summary>
        private const float SingleSideWidthFree = GalleryUiDesignTokens.GoldenRatioMajor;

        /// <summary>Both side docks present: a quarter each, well inside <see cref="GalleryDockLayout.MaxSideWidthSum"/>.</summary>
        private const float PairedSideWidthFree = 0.75f;

        /// <summary>Bottom anchor of the Top dock — it takes the upper 38% of the screen.</summary>
        private const float TopBottomAnchor = 0.62f;

        internal static void Append(List<GalleryLayoutPreset> into)
        {
            if (into == null) return;

            int id = IdBase + 1;
            int desktop = (int)LayoutPresetMode.Desktop;

            into.Add(Build(id++, desktop,
                VPBTranslation.T("gallery.layout_preset.builtin_right", "Dock: Right"),
                GalleryDockSide.Right));
            into.Add(Build(id++, desktop,
                VPBTranslation.T("gallery.layout_preset.builtin_left", "Dock: Left"),
                GalleryDockSide.Left));
            into.Add(Build(id++, desktop,
                VPBTranslation.T("gallery.layout_preset.builtin_top", "Dock: Top"),
                GalleryDockSide.Top));
            into.Add(Build(id++, desktop,
                VPBTranslation.T("gallery.layout_preset.builtin_left_right", "Dock: Left + Right"),
                GalleryDockSide.Left, GalleryDockSide.Right));
            into.Add(Build(id++, desktop,
                VPBTranslation.T("gallery.layout_preset.builtin_left_top", "Dock: Left + Top"),
                GalleryDockSide.Left, GalleryDockSide.Top));
            into.Add(Build(id++, desktop,
                VPBTranslation.T("gallery.layout_preset.builtin_top_right", "Dock: Top + Right"),
                GalleryDockSide.Top, GalleryDockSide.Right));
            into.Add(Build(id++, desktop,
                VPBTranslation.T("gallery.layout_preset.builtin_left_top_right", "Dock: Left + Top + Right"),
                GalleryDockSide.Left, GalleryDockSide.Top, GalleryDockSide.Right));
            into.Add(Build(id++, desktop,
                VPBTranslation.T("gallery.layout_preset.builtin_floating", "Single floating pane"),
                GalleryDockSide.None));

            into.Add(Build(id, (int)LayoutPresetMode.VR,
                VPBTranslation.T("gallery.layout_preset.builtin_vr_single", "Single pane"),
                GalleryDockSide.None));
        }

        private static GalleryLayoutPreset Build(int id, int mode, string name, params GalleryDockSide[] sides)
        {
            var e = new GalleryLayoutPreset();
            e.Id = id;
            e.Mode = mode;
            e.Name = name;
            e.IsBuiltIn = true;
            e.DockShapeOnly = true;
            e.PayloadLoaded = true;
            // Baselines describe windows, not browsing state — a baseline must never rewrite filters.
            e.RestoreFilters = false;
            e.ButtonColor = UI.ChromeMid;

            bool pairedSides = HasSide(sides, GalleryDockSide.Left) && HasSide(sides, GalleryDockSide.Right);
            float sideWidthFree = pairedSides ? PairedSideWidthFree : SingleSideWidthFree;

            LayoutGlobalState g = e.Global;
            ConfigureSlot(g.DockLeft, HasSide(sides, GalleryDockSide.Left), sideWidthFree, 0, 0f);
            ConfigureSlot(g.DockRight, HasSide(sides, GalleryDockSide.Right), sideWidthFree, 0, 0f);
            ConfigureSlot(g.DockTop, HasSide(sides, GalleryDockSide.Top),
                GalleryUiDesignTokens.GoldenRatioMajor, 1, TopBottomAnchor);

            for (int i = 0; i < sides.Length; i++)
            {
                var p = new LayoutPaneState();
                p.DockSlot = (int)sides[i];
                // Zero pose = "place it in front of me"; a real capture never lands inside the player's head.
                p.LocalPos = UnityEngine.Vector3.zero;
                p.LeftContent = -1;
                p.RightContent = -1;
                e.Panes.Add(p);
            }
            return e;
        }

        private static bool HasSide(GalleryDockSide[] sides, GalleryDockSide want)
        {
            for (int i = 0; i < sides.Length; i++)
            {
                if (sides[i] == want) return true;
            }
            return false;
        }

        private static void ConfigureSlot(
            LayoutDockSlotState slot, bool occupied, float widthFree, int heightMode, float customHeight)
        {
            if (slot == null) return;
            slot.Occupied = occupied;
            slot.WidthFree = widthFree;
            slot.HeightMode = heightMode;
            slot.CustomHeight = heightMode == 1 ? customHeight : 0.5f;
            slot.Collapsed = false;
            slot.AutoHide = true;
        }
    }
}
