using System;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;
using VpbNet;

namespace VPB
{
    // Names preset file on the wire, not bytes. Off by default; path-checked; every load logged.
    public static class VpbNetPresetRelay
    {
        public const string HostName = "VPB_NetPresetApply";

        static Func<string, string, bool, bool> _sender;
        static int _sent;
        static int _applied;
        static int _refused;

        public static int Sent { get { return _sent; } }
        public static int Applied { get { return _applied; } }
        public static int Refused { get { return _refused; } }

        // Plugin presets use Scene rule (Allowed only, never Ask).
        public static bool AllowPluginPresets
        {
            get { return VpbNetRulebook.Allows(VpbNetRuleDomain.Scene, VpbNetRuleAxis.Control); }
        }

        public static void SetSender(Func<string, string, bool, bool> sender)
        {
            _sender = sender;
        }

        public static void ResetCounters()
        {
            _sent = 0;
            _applied = 0;
            _refused = 0;
        }

        // Expectations, not limits. Appearance can run tens of seconds on a cold library.
        public static int BusySeconds(string action)
        {
            if (string.Equals(action, "LoadAppearance", StringComparison.Ordinal)) return 45;
            if (string.Equals(action, "LoadPose", StringComparison.Ordinal)) return 30;
            if (string.Equals(action, "LoadSkin", StringComparison.Ordinal)) return 30;
            return 20;
        }

        public static byte BusyKind(string action)
        {
            if (string.Equals(action, "LoadAppearance", StringComparison.Ordinal)) return VpbNetBusyKind.Appearance;
            if (string.Equals(action, "LoadPose", StringComparison.Ordinal)) return VpbNetBusyKind.Pose;
            if (string.Equals(action, "LoadSkin", StringComparison.Ordinal)) return VpbNetBusyKind.Skin;
            if (string.Equals(action, "LoadClothing", StringComparison.Ordinal)) return VpbNetBusyKind.Clothing;
            if (string.Equals(action, "LoadHair", StringComparison.Ordinal)) return VpbNetBusyKind.Hair;
            if (string.Equals(action, "LoadMorphs", StringComparison.Ordinal)) return VpbNetBusyKind.Morphs;
            return VpbNetBusyKind.Content;
        }

        // Before load (thread blocks). End() must match even if session drops mid-apply.
        public static void NotifyApplying(string action)
        {
            VpbNetBusy.Begin(BusyKind(action), BusySeconds(action));
        }

        public static void NotifyApplyDone()
        {
            VpbNetBusy.End();
        }

        // Relay only this player's avatar or the peer's — other scene atoms stay local.
        public static void NotifyApplied(string action, FileEntry entry, Atom target)
        {
            if (_sender == null || target == null || entry == null) return;
            if (!VpbNetStorableWhitelist.IsKnownPresetAction(action)) return;

            bool mine = VpbNetAvatarGuard.IsMyAvatar(target);
            bool toPeer = !mine && VpbNetAvatarGuard.IsPeerAvatar(target);
            if (!mine && !toPeer) return;

            string reference = EntryReference(entry);
            if (reference == null) return;

            GuardPoseLoad(action, target, "you applied " + reference);

            VpbNetStorableVerdict v = VpbNetStorableWhitelist.CheckPresetRef(reference);
            if (v != VpbNetStorableVerdict.Allowed)
            {
                LogUtil.LogWarning("[VPB.Net] not sending " + reference + ": "
                    + VpbNetStorableWhitelist.Explain(v, reference, reference));
                return;
            }

            // Advisory: their table re-checks. Skip send so the log does not claim a delivery they refuse.
            if (toPeer)
            {
                byte domain;
                if (!VpbNetRuleDomain.FromPresetAction(action, out domain)) return;
                if (!VpbNetRulebook.PeerWouldAccept(domain, VpbNetRuleAxis.Control))
                {
                    LogUtil.LogWarning("[VPB.Net] not sending " + reference
                        + ": their session rules do not let you change their "
                        + VpbNetRuleDomain.Name(domain) + ". It changed on your copy of them"
                        + " here, and their next update will put it back.");
                    return;
                }
            }

            bool ok = false;
            try { ok = _sender(action, reference, toPeer); }
            catch { ok = false; }
            if (!ok) return;

            _sent++;
            LogUtil.LogWarning("[VPB.Net] told the other side to apply " + reference
                + " (" + action + ") to " + (toPeer ? "their own avatar" : "your avatar there"));
        }

        // Target is always this side's avatar; receive vs send are separate rule decisions.
        public static bool Apply(string action, string reference, Atom target)
        {
            if (target == null) return false;

            if (!VpbNetStorableWhitelist.IsKnownPresetAction(action))
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] refused a peer look: unknown action " + action);
                return false;
            }

            VpbNetStorableVerdict v = VpbNetStorableWhitelist.CheckPresetRef(reference);
            if (v != VpbNetStorableVerdict.Allowed)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] refused a peer look: " + reference + " - "
                    + VpbNetStorableWhitelist.Explain(v, reference, reference));
                return false;
            }

            FileEntry entry = Resolve(reference);
            if (entry == null)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] the peer applied " + reference
                    + " but that file is not installed here, so their avatar keeps what it has");
                return false;
            }

            // Gate on file contents: PluginManager / atom.Restore can run code.
            if (CarriesPlugins(reference, entry) && !AllowPluginPresets)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] refused " + reference
                    + ": that preset carries plugins, and this machine does not run plugins the"
                    + " other player picks. Everything else they apply still crosses. Set"
                    + " \"Load scene content on my machine\" to allowed under Rules"
                    + " only if you would hand them a scene file.");
                return false;
            }

            LogUtil.LogWarning("[VPB.Net] the peer applied " + reference + " (" + action
                + "); loading it onto " + target.uid + " from this machine's own library");

            // Busy before load — blocked thread cannot ping; peer would see us as dead.
            VpbNetBusy.Begin(BusyKind(action), BusySeconds(action));

            GameObject go = null;
            try
            {
                go = new GameObject(HostName);
                go.hideFlags = HideFlags.HideAndDontSave;

                UIDraggableItem dragger = go.AddComponent<UIDraggableItem>();
                dragger.FileEntry = entry;
                try { dragger.Panel = AnyPanel(); }
                catch { }

                Run(dragger, action, target);
                GuardPoseLoad(action, target, "the peer applied " + reference);
                _applied++;
                return true;
            }
            catch (Exception e)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] could not apply " + reference + ": " + e.Message);
                return false;
            }
            finally
            {
                VpbNetBusy.End();
                if (go != null)
                {
                    try { UnityEngine.Object.Destroy(go); }
                    catch { }
                }
            }
        }

        public static void GuardPoseLoad(string action, Atom target, string why)
        {
            if (target == null) return;
            if (!string.Equals(action, "LoadPose", StringComparison.Ordinal)
                && !string.Equals(action, "LoadAppearance", StringComparison.Ordinal)) return;

            VpbNetCollisionGuard.Suspend(target, VpbNetCollisionGuard.BindFrames, why);
        }

        static void Run(UIDraggableItem dragger, string action, Atom target)
        {
            if (string.Equals(action, "LoadClothing", StringComparison.Ordinal))
                dragger.LoadClothing(target);
            else if (string.Equals(action, "LoadHair", StringComparison.Ordinal))
                dragger.LoadHair(target);
            else if (string.Equals(action, "LoadSkin", StringComparison.Ordinal))
                dragger.LoadSkin(target);
            else if (string.Equals(action, "LoadMorphs", StringComparison.Ordinal))
                dragger.LoadMorphs(target);
            else if (string.Equals(action, "LoadPose", StringComparison.Ordinal))
            {
                // Dual-person pose has its own message (VpbNetDualPoseRelay); do not guess here.
                dragger.LoadPose(target, true, false);
            }
            else if (string.Equals(action, "LoadAppearance", StringComparison.Ordinal))
                dragger.LoadAppearance(target);
        }

        // Unreadable counts as plugins — refuse harmless beats running unknown.
        static bool CarriesPlugins(string reference, FileEntry entry)
        {
            if (!LooksLikeStorableFile(reference)) return false;

            JSONNode node = null;
            try { node = UI.LoadJSONWithFallback(reference, entry); }
            catch { node = null; }
            if (node == null)
            {
                LogUtil.LogWarning("[VPB.Net] could not read " + reference
                    + " to see what it contains, so it is treated as if it carried plugins");
                return true;
            }

            try { return NamesPlugin(node, 0); }
            catch { return true; }
        }

        static bool LooksLikeStorableFile(string reference)
        {
            // .vam/.vaj are DAZ item descriptors - they hold geometry, never storables.
            return reference.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)
                || reference.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool NamesPlugin(JSONNode node, int depth)
        {
            if (node == null || depth > 8) return false;

            JSONClass obj = node as JSONClass;
            if (obj != null)
            {
                JSONNode id = obj["id"];
                if (id != null && string.Equals(id.Value, "PluginManager", StringComparison.Ordinal))
                    return true;

                foreach (string key in obj.Keys)
                {
                    if (key != null && key.IndexOf("plugin", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    if (NamesPlugin(obj[key], depth + 1)) return true;
                }
                return false;
            }

            JSONArray arr = node as JSONArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    if (NamesPlugin(arr[i], depth + 1)) return true;
                }
                return false;
            }

            string v = node.Value;
            return v != null && VpbNetEventCodec.IsPluginReference(v);
        }

        static GalleryPanel AnyPanel()
        {
            if (Gallery.singleton == null) return null;
            var panels = Gallery.singleton.Panels;
            if (panels == null) return null;
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i] != null) return panels[i];
            }
            return null;
        }

        internal static FileEntry ResolveEntry(string reference)
        {
            return Resolve(reference);
        }

        static FileEntry Resolve(string reference)
        {
            FileEntry entry = null;
            try { entry = FileManager.GetFileEntry(reference); }
            catch { entry = null; }
            if (entry != null) return entry;

            // Library may know the file before this run registered the package.
            try { VamOnDemandLoader.TryRegisterPackageOnDemandForEntryPath(reference); }
            catch { }

            try { return FileManager.GetFileEntry(reference); }
            catch { return null; }
        }

        static string EntryReference(FileEntry entry)
        {
            string uid = null;
            try { uid = entry.Uid; }
            catch { }
            if (!string.IsNullOrEmpty(uid)) return uid.Replace('\\', '/');

            string path = null;
            try { path = entry.Path; }
            catch { }
            return string.IsNullOrEmpty(path) ? null : path.Replace('\\', '/');
        }
    }
}
