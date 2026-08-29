using System;
using System.Collections.Generic;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public sealed class VpbNetContentCatalog : IVpbNetContractCatalog
    {
        public bool TryResolveExact(string uid, out uint contentHash)
        {
            contentHash = 0;
            if (string.IsNullOrEmpty(uid)) return false;

            try
            {
                Dictionary<string, VarPackage> byUid = FileManager.PackagesByUid;
                if (byUid == null) return false;

                VarPackage pkg;
                lock (FileManager.packagesLock)
                {
                    if (!byUid.TryGetValue(uid, out pkg)) return false;
                }
                if (pkg == null) return false;

                contentHash = HashOf(pkg);
                return true;
            }
            catch { return false; }
        }

        public bool TryResolveFamily(string family, out string installedUid)
        {
            installedUid = null;
            if (string.IsNullOrEmpty(family)) return false;

            try
            {
                VarPackageGroup g = FileManager.GetPackageGroup(family);
                if (g == null) return false;

                VarPackage pkg = g.NewestEnabledPackage;
                if (pkg == null) pkg = g.NewestPackage;
                if (pkg == null) return false;

                installedUid = pkg.Uid;
                return !string.IsNullOrEmpty(installedUid);
            }
            catch { return false; }
        }

        public static uint HashOf(VarPackage pkg)
        {
            long size;
            try { size = pkg.Size; }
            catch { return 0; }
            if (size <= 0) return 0;

            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < 8; i++)
                {
                    h ^= (byte)((size >> (i * 8)) & 0xFF);
                    h *= 16777619u;
                }
                return h == 0 ? 1u : h;
            }
        }
    }

    public static class VpbNetContentContract
    {
        public const int MaxDependencyDepth = 2;

        public static bool Build(VpbNetContract contract, Atom person, bool asHost, out string note)
        {
            note = string.Empty;
            if (contract == null) return false;
            contract.Clear();

            string sceneUid = asHost ? CurrentSceneUid() : null;
            string scenePackage = PackageUidOf(sceneUid);

            if (!string.IsNullOrEmpty(sceneUid)) contract.SetScene(sceneUid, 0);

            int sceneDeps = 0;
            if (!string.IsNullOrEmpty(scenePackage))
            {
                sceneDeps = AddPackageTree(contract, scenePackage);
            }

            AddLookPackages(contract, person);

            if (asHost)
            {
                if (string.IsNullOrEmpty(scenePackage))
                    note = "scene is not from a package; its dependencies were not exchanged";
                else if (sceneDeps == 0)
                    note = "scene package " + scenePackage + " resolved no dependencies";
            }

            if (contract.Truncated)
            {
                note = (note.Length > 0 ? note + "; " : string.Empty)
                    + contract.Omitted + " packages did not fit the contract and were not exchanged";
            }

            return true;
        }

        // Manifest from the scene's package, not what's loaded.
        public static bool BuildManifest(string scenePath, VpbNetManifest manifest, out string note)
        {
            note = string.Empty;
            if (manifest == null) return false;
            manifest.Clear();

            string packageUid = PackageUidOf(scenePath);
            if (string.IsNullOrEmpty(packageUid))
            {
                note = "that scene is not in a package, so there is nothing the other machine can fetch";
                return false;
            }

            manifest.TryAdd(packageUid, VpbNetContractRole.Scene);
            manifest.AddKiB(SizeKiB(packageUid));

            HashSet<string> deps = null;
            try { deps = FileManager.GetDependenciesDeep(packageUid, MaxDependencyDepth); }
            catch { deps = null; }

            if (deps != null)
            {
                foreach (string dep in deps)
                {
                    if (string.IsNullOrEmpty(dep)) continue;
                    if (string.Equals(dep, packageUid, StringComparison.OrdinalIgnoreCase)) continue;
                    if (manifest.TryAdd(dep, VpbNetContractRole.Look)) manifest.AddKiB(SizeKiB(dep));
                }
            }

            if (manifest.Truncated)
                note = manifest.Omitted + " packages did not fit the list, so the size shown is low";

            return true;
        }

        public static uint SizeKiB(string packageUid)
        {
            try
            {
                VarPackage pkg = FileManager.GetPackage(packageUid, false);
                if (pkg == null) return 0;
                long size = pkg.Size;
                if (size <= 0) return 0;
                long kib = size / 1024L;
                return kib > uint.MaxValue ? uint.MaxValue : (uint)kib;
            }
            catch { return 0; }
        }

        public static string TitleOf(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return string.Empty;
            string p = scenePath.Replace('\\', '/');
            int slash = p.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < p.Length) p = p.Substring(slash + 1);
            int dot = p.LastIndexOf('.');
            if (dot > 0) p = p.Substring(0, dot);
            if (p.Length > VpbNetOfferLimits.MaxTitle) p = p.Substring(0, VpbNetOfferLimits.MaxTitle);
            return p;
        }

        /// <summary>Offer path vs SceneState uid. Not ScenesMatch.</summary>
        public static bool SameSceneRef(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string ka = SceneKey(a);
            string kb = SceneKey(b);
            if (ka.Length == 0 || kb.Length == 0) return false;
            if (string.Equals(ka, kb, StringComparison.OrdinalIgnoreCase)) return true;
            string ta = TitleOf(a);
            string tb = TitleOf(b);
            return ta.Length > 0 && string.Equals(ta, tb, StringComparison.OrdinalIgnoreCase);
        }

        static string SceneKey(string s)
        {
            string p = s.Replace('\\', '/');
            if (p.Length > 5 && p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                p = p.Substring(0, p.Length - 5);
            return p;
        }

        static int AddPackageTree(VpbNetContract contract, string packageUid)
        {
            int added = 0;
            if (contract.TryAdd(packageUid, HashFor(packageUid), VpbNetContractRole.Scene)) added++;

            HashSet<string> deps = null;
            try { deps = FileManager.GetDependenciesDeep(packageUid, MaxDependencyDepth); }
            catch { deps = null; }
            if (deps == null) return added;

            foreach (string dep in deps)
            {
                if (string.IsNullOrEmpty(dep)) continue;
                if (string.Equals(dep, packageUid, StringComparison.OrdinalIgnoreCase)) continue;
                if (contract.TryAdd(dep, HashFor(dep), VpbNetContractRole.Look)) added++;
            }
            return added;
        }

        static int AddLookPackages(VpbNetContract contract, Atom person)
        {
            if (person == null) return 0;

            DAZCharacterSelector sel = null;
            try { sel = person.GetComponentInChildren<DAZCharacterSelector>(true); }
            catch { }
            if (sel == null) return 0;

            int added = 0;
            added += AddActiveItems(contract, SafeClothing(sel));
            added += AddActiveItems(contract, SafeHair(sel));
            return added;
        }

        static int AddActiveItems(VpbNetContract contract, DAZDynamicItem[] items)
        {
            if (items == null) return 0;

            int added = 0;
            for (int i = 0; i < items.Length; i++)
            {
                DAZDynamicItem item = items[i];
                if (item == null) continue;

                bool active;
                string uid;
                try
                {
                    active = item.active;
                    uid = item.uid;
                }
                catch { continue; }
                if (!active || string.IsNullOrEmpty(uid)) continue;

                string pkg = PackageUidOf(uid);
                if (string.IsNullOrEmpty(pkg)) continue;

                if (contract.TryAdd(pkg, HashFor(pkg), VpbNetContractRole.Look)) added++;
            }
            return added;
        }

        static DAZDynamicItem[] SafeClothing(DAZCharacterSelector sel)
        {
            try { return sel.clothingItems; }
            catch { return null; }
        }

        static DAZDynamicItem[] SafeHair(DAZCharacterSelector sel)
        {
            try { return sel.hairItems; }
            catch { return null; }
        }

        public static uint HashFor(string packageUid)
        {
            try
            {
                Dictionary<string, VarPackage> byUid = FileManager.PackagesByUid;
                if (byUid == null) return 0;

                VarPackage pkg;
                lock (FileManager.packagesLock)
                {
                    if (!byUid.TryGetValue(packageUid, out pkg)) return 0;
                }
                return pkg == null ? 0u : VpbNetContentCatalog.HashOf(pkg);
            }
            catch { return 0; }
        }

        public static string PackageUidOf(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            int colon = uid.IndexOf(':');
            if (colon <= 0) return null;

            string pkg = uid.Substring(0, colon);
            return VpbNetEventCodec.IsSafeIdentifier(pkg) ? pkg : null;
        }

        public static string CurrentSceneUid()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return null;

                string dir = sc.currentLoadDir;
                string name = sc.LoadedSceneName;

                string uid;
                if (string.IsNullOrEmpty(name)) uid = dir;
                else if (string.IsNullOrEmpty(dir)) uid = name;
                else if (name.IndexOf(':') >= 0 || name.IndexOf('/') >= 0
                    || name.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) uid = name;
                else uid = dir + "/" + name;

                if (string.IsNullOrEmpty(uid)) return null;
                if (uid.Length > VpbNetContractLimits.MaxSceneUidChars) uid = dir;
                if (string.IsNullOrEmpty(uid) || uid.Length > VpbNetContractLimits.MaxSceneUidChars) return null;

                if (!VpbNetEventCodec.IsSafeIdentifier(uid) || VpbNetEventCodec.IsPluginReference(uid))
                    return null;
                return uid;
            }
            catch { return null; }
        }

        // LoadedSceneName has no extension — restore .json, else gallery index (subfolder Saves/scene).
        public static string CurrentScenePath()
        {
            string uid = CurrentSceneUid();
            if (string.IsNullOrEmpty(uid)) return null;

            string p = uid.Replace('\\', '/');
            if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return Exists(p) ? p : ResolveFromIndex(p);

            string guess = p + ".json";
            if (Exists(guess)) return guess;

            return ResolveFromIndex(guess);
        }

        static string ResolveFromIndex(string jsonPath)
        {
            string leaf = jsonPath;
            int slash = leaf.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < leaf.Length) leaf = leaf.Substring(slash + 1);
            if (leaf.Length > 5) leaf = leaf.Substring(0, leaf.Length - 5);
            else return null;

            string resolved;
            try
            {
                if (!VpbLocalDatabase.TryResolveSceneJsonPath(PackageUidOf(jsonPath), leaf, out resolved))
                    return null;
            }
            catch { return null; }

            if (string.IsNullOrEmpty(resolved) || !Exists(resolved)) return null;
            return resolved;
        }

        static bool Exists(string path)
        {
            try { return FileManager.FileExists(path); }
            catch { return false; }
        }
    }
}
