using System;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    public enum GalleryDockSide
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 3
    }

    /// <summary>
    /// One screen edge's docked-pane record. <see cref="WidthFree"/> keeps the legacy
    /// <c>DesktopCustomWidth</c> meaning: the fraction of screen width left FREE, not occupied.
    /// </summary>
    public sealed class GalleryDockSlot
    {
        public bool Occupied;
        public string PanelId = "";
        public float WidthFree = GalleryUiDesignTokens.GoldenRatioMajor;
        public float CustomHeight = 0.5f;
        public int HeightMode;
        public bool Collapsed;
        public bool AutoHide = true;

        private readonly string _kOccupied;
        private readonly string _kPanelId;
        private readonly string _kWidthFree;
        private readonly string _kCustomHeight;
        private readonly string _kHeightMode;
        private readonly string _kCollapsed;
        private readonly string _kAutoHide;

        internal GalleryDockSlot(string keyPrefix)
        {
            _kOccupied = keyPrefix + "Occupied";
            _kPanelId = keyPrefix + "PanelId";
            _kWidthFree = keyPrefix + "WidthFree";
            _kCustomHeight = keyPrefix + "CustomHeight";
            _kHeightMode = keyPrefix + "HeightMode";
            _kCollapsed = keyPrefix + "Collapsed";
            _kAutoHide = keyPrefix + "AutoHide";
        }

        public void Reset()
        {
            Occupied = false;
            PanelId = "";
            WidthFree = GalleryUiDesignTokens.GoldenRatioMajor;
            CustomHeight = 0.5f;
            HeightMode = 0;
            Collapsed = false;
            AutoHide = true;
        }

        public void CopyGeometryFrom(GalleryDockSlot other)
        {
            if (other == null) return;
            WidthFree = other.WidthFree;
            CustomHeight = other.CustomHeight;
            HeightMode = other.HeightMode;
            Collapsed = other.Collapsed;
            AutoHide = other.AutoHide;
        }

        internal bool HasAnyKey(JSONNode node)
        {
            return node != null && (node[_kOccupied] != null || node[_kWidthFree] != null);
        }

        internal void Load(JSONNode node)
        {
            if (node == null) return;
            if (node[_kOccupied] != null) Occupied = node[_kOccupied].AsBool;
            if (node[_kPanelId] != null) PanelId = node[_kPanelId].Value ?? "";
            if (node[_kWidthFree] != null) WidthFree = node[_kWidthFree].AsFloat;
            if (node[_kCustomHeight] != null) CustomHeight = node[_kCustomHeight].AsFloat;
            if (node[_kHeightMode] != null) HeightMode = node[_kHeightMode].AsInt;
            if (node[_kCollapsed] != null) Collapsed = node[_kCollapsed].AsBool;
            if (node[_kAutoHide] != null) AutoHide = node[_kAutoHide].AsBool;
        }

        internal void Save(JSONClass node)
        {
            if (node == null) return;
            node[_kOccupied].AsBool = Occupied;
            node[_kPanelId] = PanelId ?? "";
            node[_kWidthFree].AsFloat = WidthFree;
            node[_kCustomHeight].AsFloat = CustomHeight;
            node[_kHeightMode].AsInt = HeightMode;
            node[_kCollapsed].AsBool = Collapsed;
            node[_kAutoHide].AsBool = AutoHide;
        }
    }

    /// <summary>
    /// Sole owner of docked-pane screen partitioning. Top claims full width; Left and Right claim
    /// the band beneath it. Cross-slot constraints engage only when more than one edge is occupied,
    /// so a single docked pane resolves to exactly the pre-multi-dock anchors.
    /// Panes read <see cref="Version"/> and only rewrite anchors when it changes.
    /// </summary>
    public static class GalleryDockLayout
    {
        public const float MinCrossAnchor = 0.05f;
        public const float MaxCrossAnchor = 0.85f;

        /// <summary>
        /// Vertical band a side dock keeps when Top is also occupied. Top may grow until only this is
        /// left — the old rule capped Top at 60% of the screen, which read as "cannot drag past the middle".
        /// </summary>
        public const float MinSideBandHeight = 0.18f;

        public const float MaxSideWidthSum = 0.9f;
        public const float MinSideWidth = 0.1f;

        /// <summary>Share of a dock's free band that its auto-hide reveal strip spans.</summary>
        private const float TriggerBandFill = 0.6f;

        /// <summary>Suppresses auto-collapse right after any dock expands, so a mouse sweep cannot cascade edges.</summary>
        public const float ExpandGraceSeconds = 0.25f;

        private static int s_version = 1;
        private static float s_lastExpandTime = -100f;

        public static int Version
        {
            get { return s_version; }
        }

        public static void NotifyExpanded()
        {
            s_lastExpandTime = Time.unscaledTime;
        }

        public static bool InExpandGrace()
        {
            return Time.unscaledTime - s_lastExpandTime < ExpandGraceSeconds;
        }

        public static void BumpVersion()
        {
            s_version++;
            if (s_version == int.MaxValue) s_version = 1;
        }

        public static GalleryDockSide Parse(string side)
        {
            if (string.IsNullOrEmpty(side)) return GalleryDockSide.Right;
            if (string.Equals(side, "Left", StringComparison.OrdinalIgnoreCase)) return GalleryDockSide.Left;
            if (string.Equals(side, "Top", StringComparison.OrdinalIgnoreCase)) return GalleryDockSide.Top;
            if (string.Equals(side, "Right", StringComparison.OrdinalIgnoreCase)) return GalleryDockSide.Right;
            return GalleryDockSide.Right;
        }

        public static string ToConfigString(GalleryDockSide side)
        {
            if (side == GalleryDockSide.Left) return "Left";
            if (side == GalleryDockSide.Top) return "Top";
            return "Right";
        }

        public static GalleryDockSlot Slot(GalleryDockSide side)
        {
            VPBConfig cfg = VPBConfig.Instance;
            return cfg != null ? cfg.DockSlotFor(side) : null;
        }

        public static float BottomAnchorOf(GalleryDockSlot slot)
        {
            if (slot == null || slot.HeightMode != 1) return 0f;
            return Mathf.Clamp(slot.CustomHeight, MinCrossAnchor, MaxCrossAnchor);
        }

        public static int OccupiedCount()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return 0;
            int n = 0;
            if (cfg.DockLeft.Occupied) n++;
            if (cfg.DockTop.Occupied) n++;
            if (cfg.DockRight.Occupied) n++;
            return n;
        }

        private static bool AnySideOccupied()
        {
            VPBConfig cfg = VPBConfig.Instance;
            return cfg != null && (cfg.DockLeft.Occupied || cfg.DockRight.Occupied);
        }

        private static float TopBottomAnchor()
        {
            GalleryDockSlot top = Slot(GalleryDockSide.Top);
            float bottom = BottomAnchorOf(top);
            if (AnySideOccupied() && bottom < MinSideBandHeight)
                bottom = MinSideBandHeight;
            return bottom;
        }

        /// <summary>Lowest bottom anchor the Top dock may take right now — the resize handle's own floor.</summary>
        public static float TopBottomAnchorFloor()
        {
            return AnySideOccupied() ? MinSideBandHeight : MinCrossAnchor;
        }

        /// <summary>Highest bottom anchor a side dock may take right now, so it can never invert under Top.</summary>
        public static float SideBottomAnchorCeiling()
        {
            float ceiling = TopBandStart() - MinSideBandHeight;
            if (ceiling > MaxCrossAnchor) ceiling = MaxCrossAnchor;
            if (ceiling < MinCrossAnchor) ceiling = MinCrossAnchor;
            return ceiling;
        }

        /// <summary>Upper Y bound available to the Left and Right docks.</summary>
        public static float TopBandStart()
        {
            GalleryDockSlot top = Slot(GalleryDockSide.Top);
            if (top == null || !top.Occupied || top.Collapsed) return 1f;
            return TopBottomAnchor();
        }

        /// <summary>Occupied width fraction for a side dock, after cross-slot contention clamping.</summary>
        public static float SideWidth(GalleryDockSide side)
        {
            GalleryDockSlot slot = Slot(side);
            if (slot == null) return 0f;
            float own = 1f - slot.WidthFree;

            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return own;
            if (!cfg.DockLeft.Occupied || !cfg.DockRight.Occupied) return own;

            float left = 1f - cfg.DockLeft.WidthFree;
            float right = 1f - cfg.DockRight.WidthFree;
            float sum = left + right;
            if (sum <= MaxSideWidthSum || sum <= 0f) return own;

            float scale = MaxSideWidthSum / sum;
            float scaled = own * scale;
            return scaled < MinSideWidth ? MinSideWidth : scaled;
        }

        public static bool TryGetRect(GalleryDockSide side, out Vector2 anchorMin, out Vector2 anchorMax)
        {
            anchorMin = Vector2.zero;
            anchorMax = Vector2.one;

            GalleryDockSlot slot = Slot(side);
            if (slot == null) return false;

            if (side == GalleryDockSide.Top)
            {
                anchorMin = new Vector2(0f, TopBottomAnchor());
                anchorMax = new Vector2(1f, 1f);
                return true;
            }

            float yTop = TopBandStart();
            float width = SideWidth(side);
            float bottom = BottomAnchorOf(slot);
            if (bottom > yTop) bottom = yTop;

            if (side == GalleryDockSide.Left)
            {
                anchorMin = new Vector2(0f, bottom);
                anchorMax = new Vector2(width, yTop);
            }
            else
            {
                anchorMin = new Vector2(1f - width, bottom);
                anchorMax = new Vector2(1f, yTop);
            }
            return true;
        }

        /// <summary>
        /// Horizontal span for the Top dock's hover-to-expand strip, in screen fractions. A full-width
        /// strip would sit on top of the Left/Right panes — which reach the screen top whenever Top is
        /// collapsed — and steal their first row of chrome, so it is confined to the free band between them.
        /// </summary>
        public static void TopTriggerBand(out float min, out float max)
        {
            float left = 0f;
            float right = 1f;

            VPBConfig cfg = VPBConfig.Instance;
            if (cfg != null)
            {
                if (cfg.DockLeft.Occupied) left = SideWidth(GalleryDockSide.Left);
                if (cfg.DockRight.Occupied) right = 1f - SideWidth(GalleryDockSide.Right);
            }
            CentredBand(left, right, out min, out max);
        }

        /// <summary>Vertical span for a Left/Right dock's reveal strip — the mirror case, kept clear of Top.</summary>
        public static void SideTriggerBand(out float min, out float max)
        {
            CentredBand(0f, TopBandStart(), out min, out max);
        }

        private static void CentredBand(float lo, float hi, out float min, out float max)
        {
            float span = hi - lo;
            if (span < MinSideWidth)
            {
                // The other docks ate the whole axis: fall back to a centred sliver, not an inverted rect.
                float mid = (lo + hi) * 0.5f;
                min = mid - MinSideWidth * 0.5f;
                max = mid + MinSideWidth * 0.5f;
                return;
            }

            float inset = span * (1f - TriggerBandFill) * 0.5f;
            min = lo + inset;
            max = hi - inset;
        }

        public static GalleryDockSide SideOf(string panelId)
        {
            if (string.IsNullOrEmpty(panelId)) return GalleryDockSide.None;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return GalleryDockSide.None;
            if (cfg.DockLeft.Occupied && string.Equals(cfg.DockLeft.PanelId, panelId, StringComparison.Ordinal))
                return GalleryDockSide.Left;
            if (cfg.DockTop.Occupied && string.Equals(cfg.DockTop.PanelId, panelId, StringComparison.Ordinal))
                return GalleryDockSide.Top;
            if (cfg.DockRight.Occupied && string.Equals(cfg.DockRight.PanelId, panelId, StringComparison.Ordinal))
                return GalleryDockSide.Right;
            return GalleryDockSide.None;
        }

        public static bool IsFreeFor(GalleryDockSide side, string panelId)
        {
            GalleryDockSlot slot = Slot(side);
            if (slot == null) return false;
            if (!slot.Occupied) return true;
            return string.Equals(slot.PanelId, panelId, StringComparison.Ordinal);
        }

        public static GalleryDockSide FirstFreeSide(GalleryDockSide preferred, string panelId)
        {
            if (preferred != GalleryDockSide.None && IsFreeFor(preferred, panelId)) return preferred;
            if (IsFreeFor(GalleryDockSide.Right, panelId)) return GalleryDockSide.Right;
            if (IsFreeFor(GalleryDockSide.Left, panelId)) return GalleryDockSide.Left;
            if (IsFreeFor(GalleryDockSide.Top, panelId)) return GalleryDockSide.Top;
            return GalleryDockSide.None;
        }

        public static bool TryClaim(GalleryDockSide side, string panelId)
        {
            if (string.IsNullOrEmpty(panelId)) return false;
            GalleryDockSlot slot = Slot(side);
            if (slot == null) return false;
            if (slot.Occupied && !string.Equals(slot.PanelId, panelId, StringComparison.Ordinal))
                return false;

            ReleaseInternal(panelId, side);
            slot.Occupied = true;
            slot.PanelId = panelId;
            BumpVersion();
            return true;
        }

        public static void Release(string panelId)
        {
            if (ReleaseInternal(panelId, GalleryDockSide.None))
                BumpVersion();
        }

        private static bool ReleaseInternal(string panelId, GalleryDockSide keep)
        {
            if (string.IsNullOrEmpty(panelId)) return false;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return false;

            bool changed = false;
            changed |= ReleaseOne(cfg.DockLeft, panelId, keep == GalleryDockSide.Left);
            changed |= ReleaseOne(cfg.DockTop, panelId, keep == GalleryDockSide.Top);
            changed |= ReleaseOne(cfg.DockRight, panelId, keep == GalleryDockSide.Right);
            return changed;
        }

        private static bool ReleaseOne(GalleryDockSlot slot, string panelId, bool keep)
        {
            if (slot == null || keep || !slot.Occupied) return false;
            if (!string.Equals(slot.PanelId, panelId, StringComparison.Ordinal)) return false;
            slot.Occupied = false;
            slot.PanelId = "";
            return true;
        }

        /// <summary>Move a claim to another edge, carrying the pane's sizing so a dock-side change keeps its shape.</summary>
        public static bool TryMove(string panelId, GalleryDockSide to)
        {
            GalleryDockSlot dest = Slot(to);
            if (dest == null || string.IsNullOrEmpty(panelId)) return false;
            if (dest.Occupied && !string.Equals(dest.PanelId, panelId, StringComparison.Ordinal))
                return false;

            GalleryDockSide from = SideOf(panelId);
            if (from == to) return true;

            GalleryDockSlot src = Slot(from);
            if (src != null) dest.CopyGeometryFrom(src);

            ReleaseInternal(panelId, to);
            dest.Occupied = true;
            dest.PanelId = panelId;
            BumpVersion();
            return true;
        }

        /// <summary>Drops claims whose owning pane is gone, so a stale id can never hold an edge hostage.</summary>
        public static void SelfHeal()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            bool changed = false;
            changed |= HealOne(cfg.DockLeft);
            changed |= HealOne(cfg.DockTop);
            changed |= HealOne(cfg.DockRight);
            if (changed) BumpVersion();
        }

        private static bool HealOne(GalleryDockSlot slot)
        {
            if (slot == null || !slot.Occupied) return false;
            if (!string.IsNullOrEmpty(slot.PanelId) && Gallery.HasPanelWithId(slot.PanelId)) return false;
            slot.Occupied = false;
            slot.PanelId = "";
            return true;
        }

        internal static void LoadSlotsFromConfigNode(JSONNode node, VPBConfig cfg)
        {
            if (node == null || cfg == null) return;

            bool any = cfg.DockLeft.HasAnyKey(node)
                || cfg.DockTop.HasAnyKey(node)
                || cfg.DockRight.HasAnyKey(node);

            if (any)
            {
                cfg.DockLeft.Load(node);
                cfg.DockTop.Load(node);
                cfg.DockRight.Load(node);
            }
            else
            {
                GalleryDockSlot active = cfg.ActiveDockSlot;
                if (active != null)
                {
                    active.Occupied = cfg.DesktopFixedMode;
                    active.PanelId = active.Occupied ? GalleryPanel.PrimaryPanelId : "";
                }
            }
            BumpVersion();
        }
    }
}
