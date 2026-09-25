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

    public sealed class GalleryDockSlot
    {
        public bool Occupied;

        public bool Wanted;
        public string PanelId = "";
        public float WidthFree = GalleryUiDesignTokens.GoldenRatioMajor;
        public float CustomHeight = 0.5f;
        public int HeightMode;
        public bool Collapsed;
        public bool AutoHide = true;

        private readonly string _kOccupied;
        private readonly string _kWanted;
        private readonly string _kPanelId;
        private readonly string _kWidthFree;
        private readonly string _kCustomHeight;
        private readonly string _kHeightMode;
        private readonly string _kCollapsed;
        private readonly string _kAutoHide;

        internal GalleryDockSlot(string keyPrefix)
        {
            _kOccupied = keyPrefix + "Occupied";
            _kWanted = keyPrefix + "Wanted";
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
            Wanted = false;
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
            Wanted = node[_kWanted] != null ? node[_kWanted].AsBool : Occupied;
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
            node[_kWanted].AsBool = Wanted;
            node[_kPanelId] = PanelId ?? "";
            node[_kWidthFree].AsFloat = WidthFree;
            node[_kCustomHeight].AsFloat = CustomHeight;
            node[_kHeightMode].AsInt = HeightMode;
            node[_kCollapsed].AsBool = Collapsed;
            node[_kAutoHide].AsBool = AutoHide;
        }
    }

    public static class GalleryDockLayout
    {
        public const float MinCrossAnchor = 0.05f;
        public const float MaxCrossAnchor = 0.85f;

        public const float MinSideBandHeight = 0.18f;

        public const float MaxSideWidthSum = 0.9f;
        public const float MinSideWidth = 0.1f;

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

        public static bool WantsSide(GalleryDockSide side)
        {
            GalleryDockSlot slot = Slot(side);
            return slot != null && slot.Wanted;
        }

        public static int WantedSideCount()
        {
            int n = 0;
            if (WantsSide(GalleryDockSide.Left)) n++;
            if (WantsSide(GalleryDockSide.Top)) n++;
            if (WantsSide(GalleryDockSide.Right)) n++;
            return n;
        }

        public static bool AnySideWanted()
        {
            return WantsSide(GalleryDockSide.Left)
                || WantsSide(GalleryDockSide.Top)
                || WantsSide(GalleryDockSide.Right);
        }

        public static void SyncWantedToOccupied()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            cfg.DockLeft.Wanted = cfg.DockLeft.Occupied;
            cfg.DockTop.Wanted = cfg.DockTop.Occupied;
            cfg.DockRight.Wanted = cfg.DockRight.Occupied;
        }

        public static GalleryDockSide WantedSideForPanel(string panelId)
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null || string.IsNullOrEmpty(panelId)) return GalleryDockSide.None;

            if (cfg.DockLeft.Wanted && string.Equals(cfg.DockLeft.PanelId, panelId, StringComparison.Ordinal))
                return GalleryDockSide.Left;
            if (cfg.DockTop.Wanted && string.Equals(cfg.DockTop.PanelId, panelId, StringComparison.Ordinal))
                return GalleryDockSide.Top;
            if (cfg.DockRight.Wanted && string.Equals(cfg.DockRight.PanelId, panelId, StringComparison.Ordinal))
                return GalleryDockSide.Right;
            return GalleryDockSide.None;
        }

        public static void ReconcileDesktopStateFromClaims(bool syncWanted)
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;

            cfg.DesktopFixedMode = OccupiedCount() > 0;
            if (syncWanted) SyncWantedToOccupied();

            GalleryDockSide primary = SideOf(GalleryPanel.PrimaryPanelId);
            if (primary == GalleryDockSide.None) primary = FirstOccupiedSide();
            if (primary != GalleryDockSide.None)
                cfg.DesktopFixedDockSide = ToConfigString(primary);
        }

        public static GalleryDockSide FirstOccupiedSide()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return GalleryDockSide.None;
            if (cfg.DockLeft.Occupied) return GalleryDockSide.Left;
            if (cfg.DockTop.Occupied) return GalleryDockSide.Top;
            if (cfg.DockRight.Occupied) return GalleryDockSide.Right;
            return GalleryDockSide.None;
        }

        public static GalleryDockSide FirstUnclaimedWantedSide()
        {
            if (WantsSide(GalleryDockSide.Left) && !Slot(GalleryDockSide.Left).Occupied) return GalleryDockSide.Left;
            if (WantsSide(GalleryDockSide.Top) && !Slot(GalleryDockSide.Top).Occupied) return GalleryDockSide.Top;
            if (WantsSide(GalleryDockSide.Right) && !Slot(GalleryDockSide.Right).Occupied) return GalleryDockSide.Right;
            return GalleryDockSide.None;
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

        public static void SideTriggerBand(out float min, out float max)
        {
            CentredBand(0f, TopBandStart(), out min, out max);
        }

        private static void CentredBand(float lo, float hi, out float min, out float max)
        {
            float span = hi - lo;
            if (span < MinSideWidth)
            {
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
            slot.Wanted = true;
            slot.PanelId = panelId;
            BumpVersion();
            GalleryPanel.MarkSessionArrangementDirty();
            return true;
        }

        public static void Release(string panelId)
        {
            if (ReleaseInternal(panelId, GalleryDockSide.None))
                BumpVersion();
        }

        public static void ReleaseByUser(string panelId)
        {
            if (string.IsNullOrEmpty(panelId)) return;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg != null)
            {
                if (string.Equals(cfg.DockLeft.PanelId, panelId, StringComparison.Ordinal)) cfg.DockLeft.Wanted = false;
                if (string.Equals(cfg.DockTop.PanelId, panelId, StringComparison.Ordinal)) cfg.DockTop.Wanted = false;
                if (string.Equals(cfg.DockRight.PanelId, panelId, StringComparison.Ordinal)) cfg.DockRight.Wanted = false;
            }
            Release(panelId);
            GalleryPanel.MarkSessionArrangementDirty();
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
            if (!slot.Wanted) slot.PanelId = "";
            return true;
        }

        public static bool TryMove(string panelId, GalleryDockSide to)
        {
            GalleryDockSlot dest = Slot(to);
            if (dest == null || string.IsNullOrEmpty(panelId)) return false;
            if (dest.Occupied && !string.Equals(dest.PanelId, panelId, StringComparison.Ordinal))
                return false;

            GalleryDockSide from = SideOf(panelId);
            if (from == to) return true;

            GalleryDockSlot src = Slot(from);
            if (src != null)
            {
                dest.CopyGeometryFrom(src);
                src.Wanted = false;
            }

            ReleaseInternal(panelId, to);
            dest.Occupied = true;
            dest.Wanted = true;
            dest.PanelId = panelId;
            BumpVersion();
            GalleryPanel.MarkSessionArrangementDirty();
            return true;
        }

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
            if (!slot.Wanted) slot.PanelId = "";
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
                    active.Wanted = cfg.DesktopFixedMode;
                    active.PanelId = active.Occupied ? GalleryPanel.PrimaryPanelId : "";
                }
            }
            BumpVersion();
        }
    }
}
