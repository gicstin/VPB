using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VPB.Outliner
{
    internal sealed class OutlinerLookItem
    {
        internal string Label;
        internal string Group;
        internal string ItemUid;
        internal string PackageUid;
        internal string ThumbPath;
        internal bool Hidden;

        internal OutlinerLookItem()
        {
            Label = "";
            Group = "";
            ItemUid = "";
            PackageUid = "";
            ThumbPath = "";
            Hidden = false;
        }
    }

    internal static class OutlinerLookPreview
    {
        internal static bool Supports(Atom atom)
        {
            if (atom == null) return false;
            string type = "";
            try { type = atom.type; } catch { return false; }
            return SceneUtils.IsPersonLikeAtomType(type);
        }

        internal const string GeometryStorableId = "geometry";

        internal static DAZCharacterSelector SelectorOf(Atom atom)
        {
            if (atom == null) return null;
            try { return atom.GetStorableByID(GeometryStorableId) as DAZCharacterSelector; }
            catch { return null; }
        }

        internal static void Collect(Atom atom, List<OutlinerLookItem> into)
        {
            int clothingMore;
            int hairMore;
            Collect(atom, into, 12, out clothingMore, out hairMore);
        }

        internal static void Collect(Atom atom, List<OutlinerLookItem> into, int maxPerGroup)
        {
            int clothingMore;
            int hairMore;
            Collect(atom, into, maxPerGroup, out clothingMore, out hairMore);
        }

        internal static void Collect(Atom atom, List<OutlinerLookItem> into, int maxPerGroup,
            out int clothingMore, out int hairMore)
        {
            clothingMore = 0;
            hairMore = 0;
            if (into == null) return;
            into.Clear();
            if (!Supports(atom)) return;
            DAZCharacterSelector sel = SelectorOf(atom);
            if (sel == null) return;
            int cap = maxPerGroup < 1 ? 1 : maxPerGroup;

            AppendCharacter(sel, into);
            int clothingAdded = 0;
            AppendWornFromContainer(sel.femaleClothingContainer, ClothingGroup, into, cap,
                ref clothingAdded, ref clothingMore);
            AppendWornFromContainer(sel.maleClothingContainer, ClothingGroup, into, cap,
                ref clothingAdded, ref clothingMore);
            int hairAdded = 0;
            AppendWornFromContainer(sel.femaleHairContainer, HairGroup, into, cap,
                ref hairAdded, ref hairMore);
            AppendWornFromContainer(sel.maleHairContainer, HairGroup, into, cap,
                ref hairAdded, ref hairMore);
        }

        internal static int CountGroup(List<OutlinerLookItem> items, string group)
        {
            if (items == null || string.IsNullOrEmpty(group)) return 0;
            int n = 0;
            for (int i = 0; i < items.Count; i++)
            {
                OutlinerLookItem it = items[i];
                if (it != null && string.Equals(it.Group, group, StringComparison.Ordinal)) n++;
            }
            return n;
        }

        internal static int OmittedAfterCap(int total, int cap)
        {
            if (cap < 0) cap = 0;
            if (total <= cap) return 0;
            return total - cap;
        }

        static void AppendWornFromContainer(Transform container, string group, List<OutlinerLookItem> into,
            int maxForGroup, ref int added, ref int omitted)
        {
            if (container == null) return;
            DAZDynamicItem[] items = null;
            try { items = container.GetComponentsInChildren<DAZDynamicItem>(false); }
            catch { return; }
            AppendItems(items, group, into, maxForGroup, ref added, ref omitted);
        }

        static void AppendCharacter(DAZCharacterSelector sel, List<OutlinerLookItem> into)
        {
            DAZCharacter ch = null;
            try { ch = sel.selectedCharacter; } catch { }
            if (ch == null) return;
            string name = "";
            try { name = ch.displayName; } catch { }
            if (string.IsNullOrEmpty(name))
            {
                try { name = ch.name; } catch { }
            }
            if (string.IsNullOrEmpty(name)) return;
            var item = new OutlinerLookItem();
            item.Group = CharacterGroup;
            item.Label = name;
            FillCharacterPaths(ch, item);
            into.Add(item);
        }

        static void AppendItems(DAZDynamicItem[] items, string group, List<OutlinerLookItem> into,
            int maxForGroup, ref int added, ref int omitted)
        {
            if (items == null) return;
            for (int i = 0; i < items.Length; i++)
            {
                DAZDynamicItem it = items[i];
                if (it == null) continue;
                bool active = false;
                try { active = it.active; } catch { }
                if (!active) continue;
                string uid = "";
                try { uid = it.uid; } catch { }
                if (string.IsNullOrEmpty(uid)) continue;
                if (added >= maxForGroup)
                {
                    omitted++;
                    continue;
                }
                var entry = new OutlinerLookItem();
                entry.Group = group;
                entry.ItemUid = uid;
                entry.Label = LabelOf(it, uid);
                entry.Hidden = IsHidden(it);
                FillDynamicItemPaths(it, uid, entry);
                into.Add(entry);
                added++;
            }
        }

        static string LabelOf(DAZDynamicItem it, string uid)
        {
            string label = "";
            try { label = it.displayName; } catch { }
            if (!string.IsNullOrEmpty(label)) return label;
            int slash = uid.LastIndexOf('/');
            string leaf = slash >= 0 ? uid.Substring(slash + 1) : uid;
            int dot = leaf.LastIndexOf('.');
            return dot > 0 ? leaf.Substring(0, dot) : leaf;
        }

        internal const string ClothingGroup = "clothing";
        internal const string HairGroup = "hair";
        internal const string CharacterGroup = "character";

        internal static bool IsWearable(OutlinerLookItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.ItemUid)) return false;
            return string.Equals(item.Group, ClothingGroup, StringComparison.Ordinal)
                || string.Equals(item.Group, HairGroup, StringComparison.Ordinal);
        }

        internal static string GeometryParamId(OutlinerLookItem item)
        {
            if (!IsWearable(item)) return "";
            return item.Group + ":" + item.ItemUid;
        }

        internal static bool IsWornParam(string storableId, string paramId)
        {
            if (!string.Equals(storableId, "geometry", StringComparison.Ordinal)) return false;
            if (string.IsNullOrEmpty(paramId)) return false;
            return paramId.StartsWith("clothing:", StringComparison.Ordinal)
                || paramId.StartsWith("hair:", StringComparison.Ordinal);
        }

        internal static DAZDynamicItem FindWorn(Atom atom, OutlinerLookItem item)
        {
            if (atom == null || item == null || string.IsNullOrEmpty(item.ItemUid)) return null;
            DAZCharacterSelector sel = SelectorOf(atom);
            if (sel == null) return null;
            bool clothing = string.Equals(item.Group, ClothingGroup, StringComparison.Ordinal);
            DAZDynamicItem[] pool = null;
            try { pool = clothing ? (DAZDynamicItem[])sel.clothingItems : (DAZDynamicItem[])sel.hairItems; }
            catch { pool = null; }
            if (pool == null) return null;
            for (int i = 0; i < pool.Length; i++)
            {
                DAZDynamicItem it = pool[i];
                if (it == null) continue;
                string uid = "";
                try { uid = it.uid; } catch { }
                if (string.Equals(uid, item.ItemUid, StringComparison.Ordinal)) return it;
            }
            return null;
        }

        internal static bool SetWorn(Atom atom, OutlinerLookItem item, bool worn)
        {
            if (atom == null || !IsWearable(item)) return false;
            string paramId = GeometryParamId(item);
            if (OutlinerEdits.WriteBool(atom, "geometry", paramId, worn)) return true;
            return SetWornFallback(atom, item, worn);
        }

        static bool SetWornFallback(Atom atom, OutlinerLookItem item, bool worn)
        {
            DAZCharacterSelector sel = SelectorOf(atom);
            DAZDynamicItem dyn = FindWorn(atom, item);
            if (sel == null || dyn == null) return false;
            try
            {
                sel.SetActiveDynamicItem(dyn, worn, false);
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.Outliner] SetWorn fallback: " + ex.Message); }
                catch { }
                return false;
            }
        }

        internal const string HideMaterialParamId = "hideMaterial";
        static readonly List<JSONStorableBool> _hideParams = new List<JSONStorableBool>(8);

        static void CollectHideParams(DAZDynamicItem dyn, List<JSONStorableBool> into)
        {
            into.Clear();
            if (dyn == null) return;
            MaterialOptions[] opts = null;
            try { opts = dyn.GetComponentsInChildren<MaterialOptions>(true); }
            catch { return; }
            if (opts == null) return;
            for (int i = 0; i < opts.Length; i++)
            {
                if (opts[i] == null) continue;
                JSONStorableBool p = null;
                try { p = opts[i].GetBoolJSONParam(HideMaterialParamId); } catch { p = null; }
                if (p != null) into.Add(p);
            }
        }

        static readonly List<RenderSuspend> _renderSuspends = new List<RenderSuspend>(8);

        static bool UsesRenderSuspend(DAZDynamicItem dyn)
        {
            return dyn is DAZHairGroup;
        }

        static void CollectRenderSuspends(DAZDynamicItem dyn, List<RenderSuspend> into)
        {
            into.Clear();
            if (dyn == null) return;
            RenderSuspend[] found = null;
            try { found = dyn.GetComponentsInChildren<RenderSuspend>(true); }
            catch { return; }
            if (found == null) return;
            for (int i = 0; i < found.Length; i++)
                if (found[i] != null) into.Add(found[i]);
        }

        internal static bool IsHidden(DAZDynamicItem dyn)
        {
            if (dyn == null) return false;
            if (UsesRenderSuspend(dyn))
            {
                CollectRenderSuspends(dyn, _renderSuspends);
                if (_renderSuspends.Count == 0) return false;
                for (int i = 0; i < _renderSuspends.Count; i++)
                {
                    bool v = false;
                    try { v = _renderSuspends[i].renderSuspend; } catch { }
                    if (!v) return false;
                }
                return true;
            }
            CollectHideParams(dyn, _hideParams);
            if (_hideParams.Count == 0) return false;
            for (int i = 0; i < _hideParams.Count; i++)
            {
                bool v = false;
                try { v = _hideParams[i].val; } catch { }
                if (!v) return false;
            }
            return true;
        }

        internal static bool IsHidden(Atom atom, OutlinerLookItem item)
        {
            return IsHidden(FindWorn(atom, item));
        }

        internal static bool SetHidden(Atom atom, OutlinerLookItem item, bool hidden)
        {
            if (!IsWearable(item)) return false;
            DAZDynamicItem dyn = FindWorn(atom, item);
            if (dyn == null) return false;
            bool any = false;
            if (UsesRenderSuspend(dyn))
            {
                CollectRenderSuspends(dyn, _renderSuspends);
                for (int i = 0; i < _renderSuspends.Count; i++)
                {
                    try
                    {
                        _renderSuspends[i].renderSuspend = hidden;
                        any = true;
                    }
                    catch { }
                }
                return any;
            }
            CollectHideParams(dyn, _hideParams);
            for (int i = 0; i < _hideParams.Count; i++)
            {
                try
                {
                    _hideParams[i].val = hidden;
                    any = true;
                }
                catch { }
            }
            return any;
        }

        internal static void CollectWornParamIds(Atom atom, string group, List<string> into)
        {
            if (into == null) return;
            into.Clear();
            if (atom == null || string.IsNullOrEmpty(group)) return;
            DAZCharacterSelector sel = SelectorOf(atom);
            if (sel == null) return;
            bool clothing = string.Equals(group, ClothingGroup, StringComparison.Ordinal);
            DAZDynamicItem[] pool = null;
            try { pool = clothing ? (DAZDynamicItem[])sel.clothingItems : (DAZDynamicItem[])sel.hairItems; }
            catch { pool = null; }
            if (pool == null) return;
            for (int i = 0; i < pool.Length; i++)
            {
                DAZDynamicItem it = pool[i];
                if (it == null) continue;
                bool active = false;
                try { active = it.active; } catch { }
                if (!active) continue;
                string uid = "";
                try { uid = it.uid; } catch { }
                if (string.IsNullOrEmpty(uid)) continue;
                into.Add(group + ":" + uid);
            }
        }

        static FieldInfo _dynInternalId;
        static FieldInfo _dynContainingDir;
        static FieldInfo _dynItemPath;
        static bool _dynFieldsTried;

        static void FillDynamicItemPaths(DAZDynamicItem it, string uid, OutlinerLookItem entry)
        {
            string pkg = "";
            try { pkg = it.packageUid; } catch { }
            string backup = "";
            try { backup = it.backupId; } catch { }
            string runtimePath = "";
            try { runtimePath = it.dynamicRuntimeLoadPath; } catch { }
            string internalUid = "";
            try { internalUid = it.internalUid; } catch { }
            if (string.IsNullOrEmpty(pkg)) pkg = PackageUidOf(uid);
            if (string.IsNullOrEmpty(pkg)) pkg = PackageUidOf(runtimePath);
            if (string.IsNullOrEmpty(pkg)) pkg = PackageUidOf(backup);
            entry.PackageUid = pkg ?? "";
            entry.ThumbPath = ThumbPathFromParts(uid, entry.PackageUid, backup, "", runtimePath);
            if (string.IsNullOrEmpty(entry.ThumbPath))
                entry.ThumbPath = ThumbPathFromParts(internalUid, entry.PackageUid, "", "", "");
            if (VPB.src.util.VPBLogger.Verbose)
            {
                try
                {
                    LogUtil.Log("[VPB.Outliner] worn item uid='" + uid + "' pkg='" + pkg + "' backup='" + backup
                        + "' runtime='" + runtimePath + "' internal='" + internalUid + "' thumb='" + entry.ThumbPath + "'");
                }
                catch { }
            }
        }

        static void FillCharacterPaths(DAZCharacter ch, OutlinerLookItem entry)
        {
            if (ch == null) return;
            string uid = ReadObjectString(ch, "uid");
            string pkg = ReadObjectString(ch, "packageUid");
            string backup = ReadObjectString(ch, "backupId");
            if (string.IsNullOrEmpty(pkg)) pkg = PackageUidOf(uid);
            if (string.IsNullOrEmpty(pkg)) pkg = PackageUidOf(backup);
            entry.ItemUid = uid ?? "";
            entry.PackageUid = pkg ?? "";
            entry.ThumbPath = ThumbPathFromParts(uid, entry.PackageUid, backup, "", "");
        }

        static void EnsureDynFields()
        {
            if (_dynFieldsTried) return;
            _dynFieldsTried = true;
            Type t = typeof(DAZDynamicItem);
            BindingFlags b = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            try { _dynInternalId = t.GetField("internalId", b); } catch { }
            try { _dynContainingDir = t.GetField("containingVAMDir", b); } catch { }
            try { _dynItemPath = t.GetField("itemPath", b); } catch { }
        }

        static string ReadDynField(DAZDynamicItem it, FieldInfo field)
        {
            if (it == null || field == null) return "";
            try
            {
                string v = field.GetValue(it) as string;
                return v ?? "";
            }
            catch { return ""; }
        }

        static string ReadObjectString(object o, string name)
        {
            if (o == null || string.IsNullOrEmpty(name)) return "";
            try
            {
                Type t = o.GetType();
                BindingFlags b = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                FieldInfo f = t.GetField(name, b);
                if (f != null)
                {
                    string fv = f.GetValue(o) as string;
                    return fv ?? "";
                }
                PropertyInfo p = t.GetProperty(name, b);
                if (p != null)
                {
                    string pv = p.GetValue(o, null) as string;
                    return pv ?? "";
                }
            }
            catch { }
            return "";
        }

        internal static string PackageUidOf(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "";
            string p = uid.Replace('\\', '/');
            int sep = p.IndexOf(":/", StringComparison.Ordinal);
            if (sep <= 0) return "";
            string head = p.Substring(0, sep);
            if (head.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
            {
                head = head.Substring(0, head.Length - 4);
                int slash = head.LastIndexOf('/');
                if (slash >= 0) head = head.Substring(slash + 1);
                return head;
            }
            if (head.Length == 1 && char.IsLetter(head[0])) return "";
            if (head.IndexOf('/') >= 0) return "";
            return head;
        }

        internal static string SisterJpgPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "";
            string p = filePath.Replace('\\', '/');
            int slash = p.LastIndexOf('/');
            int dot = p.LastIndexOf('.');
            if (dot > slash) return p.Substring(0, dot) + ".jpg";
            return p + ".jpg";
        }

        internal static string QualifyInPackage(string path, string packageUid)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string p = path.Replace('\\', '/');
            if (p.Length > 0 && p[0] == '/') p = p.Substring(1);
            int sep = p.IndexOf(":/", StringComparison.Ordinal);
            if (sep == 0)
                p = p.Substring(2);
            else if (sep > 0)
            {
                string head = p.Substring(0, sep);
                string pkgFromHead = PackageUidOf(p);
                if (!string.IsNullOrEmpty(pkgFromHead) && !string.Equals(head, pkgFromHead, StringComparison.Ordinal))
                    return pkgFromHead + ":/" + p.Substring(sep + 2);
                return p;
            }
            if (string.IsNullOrEmpty(packageUid)) return p;
            if (p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':') return p;
            return packageUid + ":/" + p;
        }

        internal static string ThumbPathFromParts(string uid, string packageUid, string backupId,
            string containingDir, string internalOrItemPath)
        {
            string file = FirstFilePath(uid, backupId, JoinContaining(containingDir, internalOrItemPath));
            if (string.IsNullOrEmpty(file)) return "";
            return QualifyInPackage(SisterJpgPath(file), packageUid);
        }

        static string JoinContaining(string dir, string leaf)
        {
            if (string.IsNullOrEmpty(leaf)) return dir ?? "";
            string n = leaf.Replace('\\', '/');
            if (n.IndexOf(":/", StringComparison.Ordinal) >= 0
                || n.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase))
                return n;
            if (string.IsNullOrEmpty(dir)) return n;
            string d = dir.Replace('\\', '/').TrimEnd('/');
            if (n.Length > 0 && n[0] == '/') n = n.Substring(1);
            return d + "/" + n;
        }

        static string FirstFilePath(string a, string b, string c)
        {
            if (LooksLikeFilePath(a)) return a;
            if (LooksLikeFilePath(b)) return b;
            if (LooksLikeFilePath(c)) return c;
            if (!string.IsNullOrEmpty(c)) return c;
            if (!string.IsNullOrEmpty(a)) return a;
            return b ?? "";
        }

        static bool LooksLikeFilePath(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf('/') >= 0 || s.IndexOf('\\') >= 0;
        }

        internal static FileEntry ResolveThumbFile(OutlinerLookItem item)
        {
            if (item == null) return null;
            string path = item.ThumbPath;
            if (string.IsNullOrEmpty(path)) return null;
            FileEntry found = TryGetThumbEntry(path);
            if (found != null) return found;

            string inner = InnerPath(path);
            if (!string.IsNullOrEmpty(inner)
                && inner.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase))
            {
                string uidPath = null;
                try
                {
                    if (FileManager.TryResolveCustomInternalPathToUidPath(inner, out uidPath)
                        && !string.IsNullOrEmpty(uidPath))
                        found = TryGetThumbEntry(SisterJpgPath(uidPath));
                }
                catch { found = null; }
                if (found != null) return found;
                string vam = inner;
                int slash = vam.LastIndexOf('/');
                int dot = vam.LastIndexOf('.');
                if (dot > slash) vam = vam.Substring(0, dot) + ".vam";
                else vam = vam + ".vam";
                uidPath = null;
                try
                {
                    if (FileManager.TryResolveCustomInternalPathToUidPath(vam, out uidPath)
                        && !string.IsNullOrEmpty(uidPath))
                        found = TryGetThumbEntry(SisterJpgPath(uidPath));
                }
                catch { found = null; }
                if (found != null) return found;
            }

            int sep = path.IndexOf(":/", StringComparison.Ordinal);
            if (sep <= 0) return null;
            string pkgUid = path.Substring(0, sep);
            inner = path.Substring(sep + 2);
            VarPackage pkg = null;
            try { pkg = FileManager.GetPackageForDependency(pkgUid, false); }
            catch { pkg = null; }
            if (pkg != null && !string.IsNullOrEmpty(pkg.Path))
            {
                try
                {
                    VarFileEntry fromPkg = FileManager.GetVarFileEntry(pkg.Path + ":/" + inner);
                    if (fromPkg != null) return fromPkg;
                }
                catch { }
            }
            try { return FileManager.GetVarFileEntry(pkgUid + ".var:/" + inner); }
            catch { return null; }
        }

        static string InnerPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string p = path.Replace('\\', '/');
            if (p.Length > 0 && p[0] == '/') p = p.Substring(1);
            int sep = p.IndexOf(":/", StringComparison.Ordinal);
            if (sep == 0) return p.Substring(2);
            if (sep > 0) return p.Substring(sep + 2);
            return p;
        }

        static FileEntry TryGetThumbEntry(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                FileEntry fe = FileManager.GetFileEntry(path, false);
                if (fe != null) return fe;
            }
            catch { }
            try { return FileManager.GetVarFileEntry(path); }
            catch { return null; }
        }
    }
}
