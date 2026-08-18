using System;
using System.Collections.Generic;
using System.Text;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    public enum LayoutPresetMode
    {
        Desktop = 0,
        VR = 1
    }

    public enum LayoutFloatKind
    {
        Settings = 0,
        Plugins = 1,
        QuickFilters = 2,
        ImportSidebar = 3,
        DetailStripTagMenu = 4,
        CreatorStrip = 5,
        QuickMenuAssign = 6
    }

    /// <summary>Serializable copy of one dock edge, decoupled from the live slot's config key names.</summary>
    public sealed class LayoutDockSlotState
    {
        public bool Occupied;
        public float WidthFree = GalleryUiDesignTokens.GoldenRatioMajor;
        public float CustomHeight = 0.5f;
        public int HeightMode;
        public bool Collapsed;
        public bool AutoHide = true;

        public void CaptureFrom(GalleryDockSlot slot)
        {
            if (slot == null) return;
            Occupied = slot.Occupied;
            WidthFree = slot.WidthFree;
            CustomHeight = slot.CustomHeight;
            HeightMode = slot.HeightMode;
            Collapsed = slot.Collapsed;
            AutoHide = slot.AutoHide;
        }

        /// <summary>Writes sizing only. Occupancy is owned by the apply reconciler, never by the payload.</summary>
        public void ApplySizingTo(GalleryDockSlot slot)
        {
            if (slot == null) return;
            slot.WidthFree = WidthFree;
            slot.CustomHeight = CustomHeight;
            slot.HeightMode = HeightMode;
            slot.Collapsed = Collapsed;
            slot.AutoHide = AutoHide;
        }

        public JSONNode ToJSON()
        {
            var n = new JSONClass();
            n["occ"].AsBool = Occupied;
            n["wf"].AsFloat = WidthFree;
            n["ch"].AsFloat = CustomHeight;
            n["hm"].AsInt = HeightMode;
            n["col"].AsBool = Collapsed;
            n["ah"].AsBool = AutoHide;
            return n;
        }

        public static LayoutDockSlotState FromJSON(JSONNode n)
        {
            var s = new LayoutDockSlotState();
            if (n == null) return s;
            if (n["occ"] != null) s.Occupied = n["occ"].AsBool;
            if (n["wf"] != null) s.WidthFree = n["wf"].AsFloat;
            if (n["ch"] != null) s.CustomHeight = n["ch"].AsFloat;
            if (n["hm"] != null) s.HeightMode = n["hm"].AsInt;
            if (n["col"] != null) s.Collapsed = n["col"].AsBool;
            if (n["ah"] != null) s.AutoHide = n["ah"].AsBool;
            return s;
        }
    }

    public sealed class LayoutFloatState
    {
        public int Kind;
        public bool Open;
        public bool Collapsed;
        public Vector2 PosCenterRef;
        public Vector2 SizeRef;

        public JSONNode ToJSON()
        {
            var n = new JSONClass();
            n["k"].AsInt = Kind;
            n["o"].AsBool = Open;
            n["c"].AsBool = Collapsed;
            n["px"].AsFloat = PosCenterRef.x;
            n["py"].AsFloat = PosCenterRef.y;
            n["sw"].AsFloat = SizeRef.x;
            n["sh"].AsFloat = SizeRef.y;
            return n;
        }

        public static LayoutFloatState FromJSON(JSONNode n)
        {
            var f = new LayoutFloatState();
            if (n == null) return f;
            if (n["k"] != null) f.Kind = n["k"].AsInt;
            if (n["o"] != null) f.Open = n["o"].AsBool;
            if (n["c"] != null) f.Collapsed = n["c"].AsBool;
            float px = n["px"] != null ? n["px"].AsFloat : 0f;
            float py = n["py"] != null ? n["py"].AsFloat : 0f;
            float sw = n["sw"] != null ? n["sw"].AsFloat : 0f;
            float sh = n["sh"] != null ? n["sh"].AsFloat : 0f;
            f.PosCenterRef = new Vector2(px, py);
            f.SizeRef = new Vector2(sw, sh);
            return f;
        }
    }

    /// <summary>Chrome and dock state shared by every pane in a preset. Only the preset's own mode is written back.</summary>
    public sealed class LayoutGlobalState
    {
        public int GalleryLayoutMode;
        public float InnerPaneScale = 1f;

        public bool DetailStripExpanded = true;
        public bool DetailStripSideInfo = true;
        public bool DetailStripThumbOnRight;
        public float DetailStripHeightRef;

        public bool OnlyWhenVamMenuVisible;

        public bool DesktopFixedMode;
        public string DefaultDockSide = "Right";
        public bool EnforceDockSide;
        public string EnforcedDockSide = "Right";
        public float AutoHideSeconds = 1f;
        public LayoutDockSlotState DockLeft = new LayoutDockSlotState();
        public LayoutDockSlotState DockTop = new LayoutDockSlotState();
        public LayoutDockSlotState DockRight = new LayoutDockSlotState();

        public bool AnchorToVamMenu = true;
        public Vector3 AnchorOffset = new Vector3(0f, 0.1f, -0.1f);
        public float VrMenuAnchorTiltDeg = 10f;
        public bool AnchorYieldsToVamPanels = true;

        public string FollowEyeHeight = "VR";
        public string FollowDistance = "Off";
        public string FollowAngle = "Off";

        public JSONNode ToJSON()
        {
            var n = new JSONClass();
            n["lm"].AsInt = GalleryLayoutMode;
            n["ips"].AsFloat = InnerPaneScale;
            n["dse"].AsBool = DetailStripExpanded;
            n["dss"].AsBool = DetailStripSideInfo;
            n["dst"].AsBool = DetailStripThumbOnRight;
            n["dsh"].AsFloat = DetailStripHeightRef;
            n["owm"].AsBool = OnlyWhenVamMenuVisible;
            n["dfm"].AsBool = DesktopFixedMode;
            n["dds"] = DefaultDockSide ?? "Right";
            n["eds"].AsBool = EnforceDockSide;
            n["eds2"] = EnforcedDockSide ?? "Right";
            n["ahs"].AsFloat = AutoHideSeconds;
            n["dkL"] = DockLeft.ToJSON();
            n["dkT"] = DockTop.ToJSON();
            n["dkR"] = DockRight.ToJSON();
            n["avm"].AsBool = AnchorToVamMenu;
            n["aox"].AsFloat = AnchorOffset.x;
            n["aoy"].AsFloat = AnchorOffset.y;
            n["aoz"].AsFloat = AnchorOffset.z;
            n["tilt"].AsFloat = VrMenuAnchorTiltDeg;
            n["ayp"].AsBool = AnchorYieldsToVamPanels;
            n["feh"] = FollowEyeHeight ?? "Off";
            n["fds"] = FollowDistance ?? "Off";
            n["fan"] = FollowAngle ?? "Off";
            return n;
        }

        public static LayoutGlobalState FromJSON(JSONNode n)
        {
            var g = new LayoutGlobalState();
            if (n == null) return g;
            if (n["lm"] != null) g.GalleryLayoutMode = n["lm"].AsInt;
            if (n["ips"] != null) g.InnerPaneScale = n["ips"].AsFloat;
            if (n["dse"] != null) g.DetailStripExpanded = n["dse"].AsBool;
            if (n["dss"] != null) g.DetailStripSideInfo = n["dss"].AsBool;
            if (n["dst"] != null) g.DetailStripThumbOnRight = n["dst"].AsBool;
            if (n["dsh"] != null) g.DetailStripHeightRef = n["dsh"].AsFloat;
            if (n["owm"] != null) g.OnlyWhenVamMenuVisible = n["owm"].AsBool;
            if (n["dfm"] != null) g.DesktopFixedMode = n["dfm"].AsBool;
            if (n["dds"] != null) g.DefaultDockSide = n["dds"].Value ?? "Right";
            if (n["eds"] != null) g.EnforceDockSide = n["eds"].AsBool;
            if (n["eds2"] != null) g.EnforcedDockSide = n["eds2"].Value ?? "Right";
            if (n["ahs"] != null) g.AutoHideSeconds = n["ahs"].AsFloat;
            if (n["dkL"] != null) g.DockLeft = LayoutDockSlotState.FromJSON(n["dkL"]);
            if (n["dkT"] != null) g.DockTop = LayoutDockSlotState.FromJSON(n["dkT"]);
            if (n["dkR"] != null) g.DockRight = LayoutDockSlotState.FromJSON(n["dkR"]);
            if (n["avm"] != null) g.AnchorToVamMenu = n["avm"].AsBool;
            float ax = n["aox"] != null ? n["aox"].AsFloat : 0f;
            float ay = n["aoy"] != null ? n["aoy"].AsFloat : 0.1f;
            float az = n["aoz"] != null ? n["aoz"].AsFloat : -0.1f;
            g.AnchorOffset = new Vector3(ax, ay, az);
            if (n["tilt"] != null) g.VrMenuAnchorTiltDeg = n["tilt"].AsFloat;
            if (n["ayp"] != null) g.AnchorYieldsToVamPanels = n["ayp"].AsBool;
            if (n["feh"] != null) g.FollowEyeHeight = n["feh"].Value ?? "Off";
            if (n["fds"] != null) g.FollowDistance = n["fds"].Value ?? "Off";
            if (n["fan"] != null) g.FollowAngle = n["fan"].Value ?? "Off";
            return g;
        }
    }

    public sealed class LayoutPaneState
    {
        /// <summary><see cref="GalleryDockSide"/> as int. None = floating.</summary>
        public int DockSlot;

        /// <summary>Pose in the player-UI root frame (see VpbWorldSpaceUiScale). Never a world pose.</summary>
        public Vector3 LocalPos;
        public Quaternion LocalRot = Quaternion.identity;
        public Vector2 SizeRef = new Vector2(1200f, 800f);
        public bool AnchoredToVamMenu = true;
        public bool FollowUser;
        public bool Collapsed;

        public string CategoryTitle = "";
        public string CategoryPath = "";
        public string CategoryExtension = "";

        /// <summary><see cref="ContentType"/> as int, or -1 when that rail is closed.</summary>
        public int LeftContent = -1;
        public int RightContent = -1;

        public bool ImportOpen;
        public bool ImportOnLeft;
        public bool ImportFloating;

        /// <summary>Grid columns 1–12. 0 = unset (legacy presets keep the pane's current count).</summary>
        public int GridColumnCount;

        public List<LayoutFloatState> Floats = new List<LayoutFloatState>();
        public QuickFilterEntry Filter;

        public JSONNode ToJSON()
        {
            var n = new JSONClass();
            n["ds"].AsInt = DockSlot;
            n["px"].AsFloat = LocalPos.x;
            n["py"].AsFloat = LocalPos.y;
            n["pz"].AsFloat = LocalPos.z;
            n["rx"].AsFloat = LocalRot.x;
            n["ry"].AsFloat = LocalRot.y;
            n["rz"].AsFloat = LocalRot.z;
            n["rw"].AsFloat = LocalRot.w;
            n["sw"].AsFloat = SizeRef.x;
            n["sh"].AsFloat = SizeRef.y;
            n["avm"].AsBool = AnchoredToVamMenu;
            n["fu"].AsBool = FollowUser;
            n["col"].AsBool = Collapsed;
            n["ct"] = CategoryTitle ?? "";
            n["cp"] = CategoryPath ?? "";
            n["ce"] = CategoryExtension ?? "";
            n["lc"].AsInt = LeftContent;
            n["rc"].AsInt = RightContent;
            n["io"].AsBool = ImportOpen;
            n["il"].AsBool = ImportOnLeft;
            n["if"].AsBool = ImportFloating;
            n["gc"].AsInt = GridColumnCount;

            var arr = new JSONArray();
            if (Floats != null)
            {
                for (int i = 0; i < Floats.Count; i++)
                {
                    if (Floats[i] == null) continue;
                    arr.Add(Floats[i].ToJSON());
                }
            }
            n["fl"] = arr;

            if (Filter != null) n["qf"] = Filter.ToJSON();
            return n;
        }

        public static LayoutPaneState FromJSON(JSONNode n)
        {
            var p = new LayoutPaneState();
            if (n == null) return p;
            if (n["ds"] != null) p.DockSlot = n["ds"].AsInt;

            float px = n["px"] != null ? n["px"].AsFloat : 0f;
            float py = n["py"] != null ? n["py"].AsFloat : 0f;
            float pz = n["pz"] != null ? n["pz"].AsFloat : 0f;
            p.LocalPos = new Vector3(px, py, pz);

            float rx = n["rx"] != null ? n["rx"].AsFloat : 0f;
            float ry = n["ry"] != null ? n["ry"].AsFloat : 0f;
            float rz = n["rz"] != null ? n["rz"].AsFloat : 0f;
            float rw = n["rw"] != null ? n["rw"].AsFloat : 1f;
            Quaternion q = new Quaternion(rx, ry, rz, rw);
            p.LocalRot = q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f ? Quaternion.identity : q;

            float sw = n["sw"] != null ? n["sw"].AsFloat : 1200f;
            float sh = n["sh"] != null ? n["sh"].AsFloat : 800f;
            p.SizeRef = new Vector2(sw, sh);

            if (n["avm"] != null) p.AnchoredToVamMenu = n["avm"].AsBool;
            if (n["fu"] != null) p.FollowUser = n["fu"].AsBool;
            if (n["col"] != null) p.Collapsed = n["col"].AsBool;
            if (n["ct"] != null) p.CategoryTitle = n["ct"].Value ?? "";
            if (n["cp"] != null) p.CategoryPath = n["cp"].Value ?? "";
            if (n["ce"] != null) p.CategoryExtension = n["ce"].Value ?? "";
            if (n["lc"] != null) p.LeftContent = n["lc"].AsInt;
            if (n["rc"] != null) p.RightContent = n["rc"].AsInt;
            if (n["io"] != null) p.ImportOpen = n["io"].AsBool;
            if (n["il"] != null) p.ImportOnLeft = n["il"].AsBool;
            if (n["if"] != null) p.ImportFloating = n["if"].AsBool;
            if (n["gc"] != null) p.GridColumnCount = n["gc"].AsInt;

            JSONNode arr = n["fl"];
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    LayoutFloatState f = LayoutFloatState.FromJSON(arr[i]);
                    if (f != null) p.Floats.Add(f);
                }
            }

            JSONNode qf = n["qf"];
            if (qf != null)
            {
                try { p.Filter = QuickFilterEntry.FromJSON(qf); }
                catch { p.Filter = null; }
            }
            return p;
        }
    }

    /// <summary>
    /// A named window arrangement. <see cref="Mode"/> is stamped at capture and immutable — a VR
    /// preset never applies on desktop and vice versa (screen-anchor ratios and metre poses do not map).
    /// </summary>
    public sealed class GalleryLayoutPreset
    {
        public const int CurrentRev = 1;

        public int Id;
        public string Name = "";
        public int SortOrder;
        public bool Pinned;
        public int Mode;
        public int Rev = CurrentRev;
        public Color ButtonColor = UI.ChromePanel;
        public long UpdatedUtc;
        public bool RestoreFilters = true;

        /// <summary>False for rows listed without their JSON payload (lazy manager list).</summary>
        public bool PayloadLoaded = true;

        /// <summary>
        /// Shipped baseline arrangement. Lives in memory only — never written to SQLite, never renamed,
        /// deleted or reordered. Duplicate produces an ordinary editable copy.
        /// </summary>
        public bool IsBuiltIn;

        /// <summary>
        /// Apply writes dock shape only and leaves unrelated chrome (detail strip, follow modes, inner
        /// scale, VR anchoring) exactly as the user left it. Set on the built-ins, which describe a
        /// window arrangement rather than a whole settings snapshot.
        /// </summary>
        public bool DockShapeOnly;

        public LayoutGlobalState Global = new LayoutGlobalState();
        public List<LayoutPaneState> Panes = new List<LayoutPaneState>();

        public bool IsVrPreset
        {
            get { return Mode == (int)LayoutPresetMode.VR; }
        }

        public JSONNode ToJSON()
        {
            var n = new JSONClass();
            n["Id"].AsInt = Id;
            n["Name"] = Name ?? "";
            n["SortOrder"].AsInt = SortOrder;
            n["Pinned"].AsBool = Pinned;
            n["Mode"].AsInt = Mode;
            n["Rev"].AsInt = Rev;
            n["UpdatedUtc"] = UpdatedUtc.ToString(System.Globalization.CultureInfo.InvariantCulture);
            n["RestoreFilters"].AsBool = RestoreFilters;
            n["dso"].AsBool = DockShapeOnly;
            n["cr"].AsFloat = ButtonColor.r;
            n["cg"].AsFloat = ButtonColor.g;
            n["cb"].AsFloat = ButtonColor.b;
            n["ca"].AsFloat = ButtonColor.a;
            n["Global"] = Global != null ? Global.ToJSON() : new LayoutGlobalState().ToJSON();

            var arr = new JSONArray();
            if (Panes != null)
            {
                for (int i = 0; i < Panes.Count; i++)
                {
                    if (Panes[i] == null) continue;
                    arr.Add(Panes[i].ToJSON());
                }
            }
            n["Panes"] = arr;
            return n;
        }

        public static GalleryLayoutPreset FromJSON(JSONNode n)
        {
            if (n == null) return null;
            var e = new GalleryLayoutPreset();
            if (n["Id"] != null) e.Id = n["Id"].AsInt;
            if (n["Name"] != null) e.Name = n["Name"].Value ?? "";
            if (n["SortOrder"] != null) e.SortOrder = n["SortOrder"].AsInt;
            if (n["Pinned"] != null) e.Pinned = n["Pinned"].AsBool;
            if (n["Mode"] != null) e.Mode = n["Mode"].AsInt;
            if (n["Rev"] != null) e.Rev = n["Rev"].AsInt;
            if (n["UpdatedUtc"] != null)
            {
                long ticks;
                if (long.TryParse(n["UpdatedUtc"].Value, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out ticks))
                    e.UpdatedUtc = ticks;
            }
            if (n["RestoreFilters"] != null) e.RestoreFilters = n["RestoreFilters"].AsBool;
            // IsBuiltIn is deliberately not read back — an exported built-in imports as an ordinary preset.
            if (n["dso"] != null) e.DockShapeOnly = n["dso"].AsBool;

            float cr = n["cr"] != null ? n["cr"].AsFloat : UI.ChromePanel.r;
            float cg = n["cg"] != null ? n["cg"].AsFloat : UI.ChromePanel.g;
            float cb = n["cb"] != null ? n["cb"].AsFloat : UI.ChromePanel.b;
            float ca = n["ca"] != null ? n["ca"].AsFloat : UI.ChromePanel.a;
            e.ButtonColor = new Color(cr, cg, cb, ca);

            e.Global = LayoutGlobalState.FromJSON(n["Global"]);

            JSONNode arr = n["Panes"];
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    LayoutPaneState p = LayoutPaneState.FromJSON(arr[i]);
                    if (p != null) e.Panes.Add(p);
                }
            }
            return e;
        }

        public string ToJsonString()
        {
            try { return VPB.src.util.JsonSerializationUtil.Serialize(ToJSON(), 32768); }
            catch
            {
                try { return ToJSON().ToString(); }
                catch { return "{}"; }
            }
        }

        public static GalleryLayoutPreset FromJsonString(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                JSONNode n = JSON.Parse(json);
                return n != null ? FromJSON(n) : null;
            }
            catch { return null; }
        }

        private static readonly StringBuilder s_SigSb = new StringBuilder(512);

        /// <summary>
        /// Identity of what a preset would restore. Positions are quantised so drag jitter does not
        /// leave the active preset permanently marked as modified.
        /// </summary>
        public static string BuildContentSignature(GalleryLayoutPreset e)
        {
            if (e == null) return "";
            StringBuilder sb = s_SigSb;
            sb.Length = 0;

            sb.Append(e.Mode).Append('|');

            // A dock-shape preset restores edges and nothing else, so only edges may mark it modified —
            // otherwise every unrelated chrome tweak would light it up as drifted the moment it applies.
            if (e.DockShapeOnly)
            {
                LayoutGlobalState dg = e.Global;
                if (dg != null)
                {
                    AppendDock(sb, dg.DockLeft);
                    AppendDock(sb, dg.DockTop);
                    AppendDock(sb, dg.DockRight);
                }
                sb.Append('|');
                if (e.Panes != null)
                {
                    for (int i = 0; i < e.Panes.Count; i++)
                    {
                        LayoutPaneState dp = e.Panes[i];
                        if (dp == null) continue;
                        sb.Append(dp.DockSlot).Append('#');
                    }
                }
                return sb.ToString();
            }

            sb.Append(e.RestoreFilters ? '1' : '0').Append('|');

            LayoutGlobalState g = e.Global;
            if (g != null)
            {
                sb.Append(g.GalleryLayoutMode).Append(',');
                AppendQ(sb, g.InnerPaneScale, 1000f).Append(',');
                sb.Append(g.DetailStripExpanded ? '1' : '0');
                sb.Append(g.DetailStripSideInfo ? '1' : '0');
                sb.Append(g.DetailStripThumbOnRight ? '1' : '0').Append(',');
                AppendQ(sb, g.DetailStripHeightRef, 1f).Append(',');
                sb.Append(g.DesktopFixedMode ? '1' : '0').Append(',');
                AppendDock(sb, g.DockLeft);
                AppendDock(sb, g.DockTop);
                AppendDock(sb, g.DockRight);
                sb.Append(g.AnchorToVamMenu ? '1' : '0').Append(',');
                AppendQ(sb, g.AnchorOffset.x, 1000f).Append(',');
                AppendQ(sb, g.AnchorOffset.y, 1000f).Append(',');
                AppendQ(sb, g.AnchorOffset.z, 1000f).Append(',');
                AppendQ(sb, g.VrMenuAnchorTiltDeg, 10f).Append(',');
                sb.Append(g.FollowEyeHeight).Append(g.FollowDistance).Append(g.FollowAngle);
            }
            sb.Append('|');

            if (e.Panes != null)
            {
                for (int i = 0; i < e.Panes.Count; i++)
                {
                    LayoutPaneState p = e.Panes[i];
                    if (p == null) continue;
                    sb.Append(p.DockSlot).Append(',');
                    AppendQ(sb, p.LocalPos.x, 1000f).Append(',');
                    AppendQ(sb, p.LocalPos.y, 1000f).Append(',');
                    AppendQ(sb, p.LocalPos.z, 1000f).Append(',');
                    AppendQ(sb, p.LocalRot.eulerAngles.x, 2f).Append(',');
                    AppendQ(sb, p.LocalRot.eulerAngles.y, 2f).Append(',');
                    AppendQ(sb, p.LocalRot.eulerAngles.z, 2f).Append(',');
                    AppendQ(sb, p.SizeRef.x, 1f).Append(',');
                    AppendQ(sb, p.SizeRef.y, 1f).Append(',');
                    sb.Append(p.CategoryTitle).Append(',');
                    sb.Append(p.CategoryExtension).Append(',');
                    sb.Append(p.LeftContent).Append(',').Append(p.RightContent).Append(',');
                    sb.Append(p.ImportOpen ? '1' : '0');
                    sb.Append(p.ImportOnLeft ? '1' : '0');
                    sb.Append(p.ImportFloating ? '1' : '0').Append(',');
                    sb.Append(p.Collapsed ? '1' : '0').Append(',');
                    sb.Append(p.GridColumnCount).Append(',');

                    if (p.Floats != null)
                    {
                        for (int f = 0; f < p.Floats.Count; f++)
                        {
                            LayoutFloatState fs = p.Floats[f];
                            if (fs == null) continue;
                            sb.Append(fs.Kind).Append(fs.Open ? '1' : '0').Append(fs.Collapsed ? '1' : '0');
                            AppendQ(sb, fs.PosCenterRef.x, 1f);
                            AppendQ(sb, fs.PosCenterRef.y, 1f);
                            AppendQ(sb, fs.SizeRef.x, 1f);
                            AppendQ(sb, fs.SizeRef.y, 1f).Append(';');
                        }
                    }

                    if (e.RestoreFilters && p.Filter != null)
                    {
                        try { sb.Append(QuickFilterEntry.BuildContentSignature(p.Filter)); }
                        catch { }
                    }
                    sb.Append('#');
                }
            }

            return sb.ToString();
        }

        private static StringBuilder AppendQ(StringBuilder sb, float v, float scale)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
            sb.Append(Mathf.RoundToInt(v * scale));
            return sb;
        }

        private static void AppendDock(StringBuilder sb, LayoutDockSlotState d)
        {
            if (d == null) { sb.Append("-,"); return; }
            sb.Append(d.Occupied ? '1' : '0');
            sb.Append(d.HeightMode);
            sb.Append(d.AutoHide ? '1' : '0');
            AppendQ(sb, d.WidthFree, 1000f).Append('/');
            AppendQ(sb, d.CustomHeight, 1000f).Append(',');
        }
    }
}
