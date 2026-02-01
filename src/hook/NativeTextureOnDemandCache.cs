using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    public static class NativeTextureOnDemandCache
    {
        private const int DefaultSizedCacheWidth = 512;
        private const int DefaultSizedCacheHeight = 512;
        private const float OnDemandLogThrottleSeconds = 0.5f;

        private static bool s_OnDemandBusy;
        private static float s_OnDemandLastLogTime;

        public static bool IsOnDemandBusy => s_OnDemandBusy;

        public static void TryBuildSceneCacheOnDemand(MonoBehaviour host)
        {
            if (host == null) return;

            if (s_OnDemandBusy)
            {
                ThrottledLog("[VPB] On-demand cache already running.");
                return;
            }

            string scenePath = ResolveCurrentScenePath();
            if (string.IsNullOrEmpty(scenePath))
            {
                ThrottledLog("[VPB] On-demand cache skipped: no current scene path.");
                return;
            }

            s_OnDemandBusy = true;
            host.StartCoroutine(BuildSceneCacheCoroutine(scenePath));
        }

        public static void TryBuildSceneCacheOnDemand(MonoBehaviour host, string scenePath)
        {
            if (host == null) return;

            if (s_OnDemandBusy)
            {
                ThrottledLog("[VPB] On-demand cache already running.");
                return;
            }

            string normalized = NormalizeScenePath(scenePath);
            if (string.IsNullOrEmpty(normalized))
            {
                ThrottledLog("[VPB] On-demand cache skipped: no scene path.");
                return;
            }

            s_OnDemandBusy = true;
            host.StartCoroutine(BuildSceneCacheCoroutine(normalized));
        }

        public static void TryBuildPackageCacheOnDemand(MonoBehaviour host, string packagePath)
        {
            if (host == null) return;

            if (s_OnDemandBusy)
            {
                ThrottledLog("[VPB] On-demand cache already running.");
                return;
            }

            if (string.IsNullOrEmpty(packagePath))
            {
                ThrottledLog("[VPB] On-demand cache skipped: no package path.");
                return;
            }

            s_OnDemandBusy = true;
            host.StartCoroutine(BuildPackageCacheCoroutine(packagePath));
        }

        private static string ResolveCurrentScenePath()
        {
            string scenePath = LogUtil.GetSceneLoadName();
            scenePath = NormalizeScenePath(scenePath);
            if (!string.IsNullOrEmpty(scenePath)) return scenePath;

            var sc = SuperController.singleton;
            if (sc == null) return null;

            string resolved = TryGetScenePathFromSuperController(sc);
            resolved = NormalizeScenePath(resolved);
            if (!string.IsNullOrEmpty(resolved)) return resolved;

            return null;
        }

        private static string NormalizeScenePath(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return scenePath;
            string normalized = scenePath.Replace('\\', '/').Trim();

            if (normalized.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "var:/" + normalized.Substring("AllPackages/".Length);
            }

            if (normalized.StartsWith("var:/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            if (normalized.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("var:".Length);
                if (!string.IsNullOrEmpty(normalized) && normalized[0] != '/') normalized = "/" + normalized;
                return "var:" + normalized;
            }

            return normalized;
        }

        private static string TryGetScenePathFromSuperController(SuperController sc)
        {
            try
            {
                var t = sc.GetType();
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

                string[] candidateNames =
                {
                    "currentSaveName",
                    "currentSaveFile",
                    "currentScenePath",
                    "currentSceneSaveName",
                    "lastSaveName",
                    "lastScenePath",
                    "sceneFilePath",
                    "currentSceneFile",
                    "loadedScenePath",
                    "loadedSceneName"
                };

                for (int i = 0; i < candidateNames.Length; i++)
                {
                    string name = candidateNames[i];

                    try
                    {
                        var p = t.GetProperty(name, flags);
                        if (p != null && p.PropertyType == typeof(string))
                        {
                            string value = p.GetValue(sc, null) as string;
                            if (LooksLikeScenePath(value)) return value;
                        }
                    }
                    catch { }

                    try
                    {
                        var f = t.GetField(name, flags);
                        if (f != null && f.FieldType == typeof(string))
                        {
                            string value = f.GetValue(sc) as string;
                            if (LooksLikeScenePath(value)) return value;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private static bool LooksLikeScenePath(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string v = value.Replace('\\', '/').ToLowerInvariant();
            if (!v.EndsWith(".json")) return false;
            return v.Contains("saves/scene") || v.Contains(":/");
        }

        private static IEnumerator BuildSceneCacheCoroutine(string scenePath)
        {
            ThrottledLog("[VPB] On-demand cache start: " + scenePath);

            try
            {
                yield return BuildCacheForSceneTexturesUnity(scenePath);
                ThrottledLog("[VPB] On-demand cache finished.");
            }
            finally
            {
                s_OnDemandBusy = false;
            }
        }

        private static IEnumerator BuildPackageCacheCoroutine(string packagePath)
        {
            ThrottledLog("[VPB] On-demand package cache start: " + packagePath);

            try
            {
                yield return BuildCacheForSelectedPackageUnity(packagePath);
                ThrottledLog("[VPB] On-demand package cache finished.");
            }
            finally
            {
                s_OnDemandBusy = false;
            }
        }

        private static IEnumerator BuildCacheForSceneTexturesUnity(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) yield break;

            string sceneText = null;
            try { sceneText = FileManager.ReadAllText(scenePath); }
            catch (Exception ex)
            {
                ThrottledLog("[VPB] On-demand cache abort: cannot read scene: " + ex.Message);
                yield break;
            }

            if (string.IsNullOrEmpty(sceneText))
            {
                ThrottledLog("[VPB] On-demand cache abort: empty scene");
                yield break;
            }

            string selfUid = null;
            try
            {
                string normalized = scenePath.Replace('\\', '/');
                if (normalized.StartsWith("var:/", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring("var:/".Length);
                }
                else if (normalized.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring("var:".Length);
                    if (!string.IsNullOrEmpty(normalized) && normalized[0] == '/') normalized = normalized.Substring(1);
                }
                int idx = normalized.IndexOf(":/", StringComparison.Ordinal);
                if (idx > 0)
                {
                    string pkgId = NormalizePackageId(normalized.Substring(0, idx));
                    VarPackage p = FileManager.GetPackageForDependency(pkgId, true);
                    if (p != null) selfUid = p.Uid;
                }
            }
            catch { }

            JSONNode sceneNode = null;
            try { sceneNode = JSON.Parse(sceneText); } catch { }
            if (sceneNode == null)
            {
                ThrottledLog("[VPB] On-demand cache abort: JSON parse failed");
                yield break;
            }

            var required = new List<RequiredTexture>();
            var jsonRefs = new List<RequiredJsonFile>();
            ExtractSceneUrlsRecursive(sceneNode, selfUid, required, jsonRefs);

            int maxJsonDepth = 2;
            var visitedJson = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<KeyValuePair<RequiredJsonFile, int>>();
            if (jsonRefs != null)
            {
                for (int i = 0; i < jsonRefs.Count; i++)
                {
                    queue.Enqueue(new KeyValuePair<RequiredJsonFile, int>(jsonRefs[i], 1));
                }
            }

            while (queue.Count > 0)
            {
                var kv = queue.Dequeue();
                RequiredJsonFile rj = kv.Key;
                int depth = kv.Value;
                if (depth > maxJsonDepth) continue;
                if (string.IsNullOrEmpty(rj.PackageId) || string.IsNullOrEmpty(rj.InternalPath)) continue;

                VarPackage jp = null;
                string depPkgId = NormalizePackageId(rj.PackageId);
                try { jp = FileManager.GetPackageForDependency(depPkgId, true); } catch { }
                if (jp == null && !depPkgId.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
                {
                    try { jp = FileManager.GetPackageForDependency(depPkgId + ".latest", true); } catch { }
                }
                if (jp == null) continue;

                string jsonUidPath = jp.Uid + ":/" + rj.InternalPath;
                string visitKey = jsonUidPath.ToLowerInvariant();
                if (!visitedJson.Add(visitKey)) continue;

                string txt = null;
                try { txt = FileManager.ReadAllText(jsonUidPath); } catch { txt = null; }
                if (string.IsNullOrEmpty(txt)) continue;

                JSONNode n = null;
                try { n = JSON.Parse(txt); } catch { n = null; }
                if (n == null) continue;

                var nestedTex = new List<RequiredTexture>();
                var nestedJson = new List<RequiredJsonFile>();
                ExtractSceneUrlsRecursive(n, jp.Uid, rj.InternalPath, nestedTex, nestedJson);
                if (nestedTex != null && nestedTex.Count > 0) required.AddRange(nestedTex);
                if (nestedJson != null && nestedJson.Count > 0)
                {
                    for (int i = 0; i < nestedJson.Count; i++)
                    {
                        queue.Enqueue(new KeyValuePair<RequiredJsonFile, int>(nestedJson[i], depth + 1));
                    }
                }
            }

            if (required.Count == 0)
            {
                ThrottledLog("[VPB] On-demand cache: no texture URLs found");
                yield break;
            }

            var byPkgFlags = new Dictionary<string, Dictionary<string, List<TextureFlags>>>(StringComparer.OrdinalIgnoreCase);
            var byPkgOrig = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < required.Count; i++)
            {
                RequiredTexture rt = required[i];
                if (string.IsNullOrEmpty(rt.PackageId) || string.IsNullOrEmpty(rt.InternalPath)) continue;

                VarPackage pkg = null;
                string texPkgId = NormalizePackageId(rt.PackageId);
                try { pkg = FileManager.GetPackageForDependency(texPkgId, true); } catch { }
                if (pkg == null && !texPkgId.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
                {
                    try { pkg = FileManager.GetPackageForDependency(texPkgId + ".latest", true); } catch { }
                }
                if (pkg == null) continue;

                string pkgUid = pkg.Uid;
                string internalLower = rt.InternalPath.ToLowerInvariant();

                Dictionary<string, List<TextureFlags>> flagsMap;
                if (!byPkgFlags.TryGetValue(pkgUid, out flagsMap))
                {
                    flagsMap = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
                    byPkgFlags[pkgUid] = flagsMap;
                }

                Dictionary<string, string> origMap;
                if (!byPkgOrig.TryGetValue(pkgUid, out origMap))
                {
                    origMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    byPkgOrig[pkgUid] = origMap;
                }

                AddFlagVariant(flagsMap, internalLower, rt.Flags);
                if (!origMap.ContainsKey(internalLower))
                {
                    origMap[internalLower] = rt.InternalPath;
                }
            }

            foreach (var kv in byPkgFlags)
            {
                string pkgUid = kv.Key;
                Dictionary<string, List<TextureFlags>> flagsMap = kv.Value;

                Dictionary<string, string> origMap;
                byPkgOrig.TryGetValue(pkgUid, out origMap);

                VarPackage pkg = null;
                try { pkg = FileManager.GetPackageForDependency(pkgUid, true); } catch { }
                if (pkg == null) continue;

                yield return WorkerBuildSelectiveUnityCoroutine(pkg, flagsMap, origMap);
            }
        }

        private struct TextureFlags
        {
            public bool compress;
            public bool linear;
            public bool isNormalMap;
            public bool createAlphaFromGrayscale;
            public bool createNormalFromBump;
            public bool invert;
            public float bumpStrength;
        }

        private struct RequiredTexture
        {
            public string PackageId;
            public string InternalPath;
            public TextureFlags Flags;
        }

        private struct RequiredJsonFile
        {
            public string PackageId;
            public string InternalPath;
        }

        private struct ImageEntry
        {
            public string InternalPath;
            public DateTime EntryTime;
            public long EntrySize;
        }

        private static List<ImageEntry> EnumerateAllImagesInVar(VarPackage pkg)
        {
            var list = new List<ImageEntry>();
            if (pkg == null) return list;

            string varPath = pkg.Path;
            if (string.IsNullOrEmpty(varPath)) return list;
            if (!File.Exists(varPath)) return list;

            try
            {
                using (FileStream fs = File.Open(varPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write | FileShare.Delete))
                using (ZipFile zf = new ZipFile(fs))
                {
                    zf.IsStreamOwner = false;
                    foreach (ZipEntry ze in zf)
                    {
                        if (ze == null || !ze.IsFile) continue;
                        string name = ze.Name;
                        if (string.IsNullOrEmpty(name)) continue;
                        string lower = name.ToLowerInvariant();
                        if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg"))
                        {
                            list.Add(new ImageEntry
                            {
                                InternalPath = name,
                                EntryTime = ze.DateTime,
                                EntrySize = ze.Size
                            });
                        }
                    }
                }
            }
            catch { }

            return list;
        }

        private static string GetFlagsSignature(TextureFlags flags)
        {
            string sig = string.Empty;
            if (flags.compress) sig += "_C";
            if (flags.linear) sig += "_L";
            if (flags.isNormalMap) sig += "_N";
            if (flags.createAlphaFromGrayscale) sig += "_A";
            if (flags.createNormalFromBump) sig += "_BN" + flags.bumpStrength;
            if (flags.invert) sig += "_I";
            return sig;
        }

        private static void AddFlagVariant(Dictionary<string, List<TextureFlags>> map, string internalLower, TextureFlags flags)
        {
            if (map == null || string.IsNullOrEmpty(internalLower)) return;
            List<TextureFlags> list;
            if (!map.TryGetValue(internalLower, out list) || list == null)
            {
                list = new List<TextureFlags>();
                map[internalLower] = list;
            }

            string sig = GetFlagsSignature(flags);
            for (int i = 0; i < list.Count; i++)
            {
                if (GetFlagsSignature(list[i]) == sig) return;
            }

            list.Add(flags);
        }

        private static bool ShouldBuildSizedCache(TextureFlags flags, string internalPath)
        {
            if (flags.isNormalMap) return false;
            if (flags.createNormalFromBump) return false;
            if (!flags.compress) return false;
            if (!string.IsNullOrEmpty(internalPath))
            {
                string ext = Path.GetExtension(internalPath);
                if (!string.IsNullOrEmpty(ext))
                {
                    if (!ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                        && !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static string NormalizePackageId(string pkgId)
        {
            if (string.IsNullOrEmpty(pkgId)) return pkgId;
            string p = pkgId.Trim();
            p = p.Replace('\\', '/');
            if (p.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring("AllPackages/".Length);
            }
            int slash = p.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < p.Length) p = p.Substring(slash + 1);
            if (p.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring(0, p.Length - 4);
            }
            return p;
        }

        private static string StripSuffixAfterKnownExtension(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return internalPath;
            string p = internalPath;
            string lower = p.ToLowerInvariant();
            string[] exts = new[] { ".png", ".jpg", ".jpeg", ".vap", ".json", ".vaj", ".vam", ".vmi" };
            int bestEnd = -1;
            for (int i = 0; i < exts.Length; i++)
            {
                int idx = lower.LastIndexOf(exts[i], StringComparison.Ordinal);
                if (idx >= 0)
                {
                    int end = idx + exts[i].Length;
                    if (end > bestEnd) bestEnd = end;
                }
            }
            if (bestEnd > 0 && bestEnd < p.Length)
            {
                return p.Substring(0, bestEnd);
            }
            return p;
        }

        private static bool LooksLikeVamFileRef(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string v = value;
            int idx = v.IndexOf(":/", StringComparison.Ordinal);
            if (idx <= 0) return false;
            string l = v.ToLowerInvariant();
            return l.Contains(".png") || l.Contains(".jpg") || l.Contains(".jpeg")
                || l.Contains(".vap") || l.Contains(".json") || l.Contains(".vaj") || l.Contains(".vam") || l.Contains(".vmi");
        }

        private static bool LooksLikeImagePath(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string v = value.Trim().ToLowerInvariant();
            return v.EndsWith(".png") || v.EndsWith(".jpg") || v.EndsWith(".jpeg");
        }

        private static string NormalizeInternalPath(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return internalPath;
            string p = internalPath.Replace('\\', '/');
            while (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
            if (p.StartsWith("/", StringComparison.Ordinal)) p = p.Substring(1);
            return p;
        }

        private static string GetInternalDirectory(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return null;
            string p = internalPath.Replace('\\', '/');
            int slash = p.LastIndexOf('/');
            if (slash <= 0) return null;
            return p.Substring(0, slash);
        }

        private static bool TryResolveTextureRef(string rawValue, string selfPackageUid, string referencingInternalPath, out string pkgId, out string internalPath)
        {
            pkgId = null;
            internalPath = null;
            if (string.IsNullOrEmpty(rawValue)) return false;

            if (TryParseVamRef(rawValue, selfPackageUid, out pkgId, out internalPath)) return true;

            if (!LooksLikeImagePath(rawValue)) return false;
            if (string.IsNullOrEmpty(selfPackageUid)) return false;

            pkgId = NormalizePackageId(selfPackageUid);

            string rel = rawValue.Trim().Replace('\\', '/');
            if (rel.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
            {
                rel = rel.Substring("AllPackages/".Length);
            }

            if (rel.IndexOf('/') < 0)
            {
                string dir = GetInternalDirectory(referencingInternalPath);
                if (!string.IsNullOrEmpty(dir)) rel = dir + "/" + rel;
            }

            internalPath = StripSuffixAfterKnownExtension(NormalizeInternalPath(rel));
            return !string.IsNullOrEmpty(internalPath);
        }

        private static bool TryParseVamRef(string rawValue, string selfPackageUid, out string pkgId, out string internalPath)
        {
            pkgId = null;
            internalPath = null;

            if (string.IsNullOrEmpty(rawValue)) return false;

            string normalized = rawValue.Trim();
            normalized = normalized.Replace('\\', '/');

            if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(7);
            }
            else if (normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(5);
            }

            if (normalized.StartsWith("var:/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("var:/".Length);
            }
            else if (normalized.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("var:".Length);
                if (!string.IsNullOrEmpty(normalized) && normalized[0] == '/') normalized = normalized.Substring(1);
            }

            if (!string.IsNullOrEmpty(selfPackageUid) && normalized.StartsWith("SELF:/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = selfPackageUid + ":/" + normalized.Substring("SELF:/".Length);
            }
            else if (!string.IsNullOrEmpty(selfPackageUid) && normalized.StartsWith("SELF:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = selfPackageUid + ":" + normalized.Substring("SELF:".Length);
            }

            if (normalized.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("AllPackages/".Length);
            }

            int idx = normalized.IndexOf(":/", StringComparison.Ordinal);
            if (idx <= 0 || idx + 2 >= normalized.Length) return false;

            pkgId = NormalizePackageId(normalized.Substring(0, idx));
            internalPath = normalized.Substring(idx + 2);

            if (!string.IsNullOrEmpty(internalPath) && internalPath[0] == '/') internalPath = internalPath.Substring(1);
            internalPath = internalPath.Replace('\\', '/');
            int q = internalPath.IndexOfAny(new[] { '?', '#' });
            if (q >= 0) internalPath = internalPath.Substring(0, q);
            try { internalPath = Uri.UnescapeDataString(internalPath); } catch { }
            internalPath = StripSuffixAfterKnownExtension(internalPath);

            return !string.IsNullOrEmpty(pkgId) && !string.IsNullOrEmpty(internalPath);
        }

        private static void ApplyPathHeuristics(string internalPath, ref TextureFlags flags)
        {
            if (string.IsNullOrEmpty(internalPath)) return;
            string file = Path.GetFileName(internalPath) ?? internalPath;
            string l = file.ToLowerInvariant();

            if (!flags.isNormalMap)
            {
                if (l.Contains("normal") || l.Contains("norm") || l.Contains("normalmap") || l.Contains("nrm") || l.Contains("_nm") || l.Contains("-nm") || l.Contains(" nm")
                    || l.EndsWith("n.png") || l.EndsWith("n.jpg") || l.EndsWith("n.jpeg"))
                {
                    flags.isNormalMap = true;
                    flags.linear = true;
                    flags.compress = false;
                }
            }

            if (!flags.linear)
            {
                if (flags.isNormalMap || l.Contains("spec") || l.Contains("gloss") || l.Contains("rough") || l.Contains("metal") || l.Contains("lut") || l.EndsWith("s.jpg") || l.EndsWith("g.jpg"))
                {
                    flags.linear = true;
                }
            }
        }

        private static bool TryGetFlagsFromVapKey(string key, out TextureFlags flags)
        {
            flags = new TextureFlags
            {
                compress = true,
                linear = false,
                isNormalMap = false,
                createAlphaFromGrayscale = false,
                createNormalFromBump = false,
                invert = false,
                bumpStrength = 1f
            };

            if (string.IsNullOrEmpty(key)) return false;

            string k = key;

            if (k.IndexOf("customTexture_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bool isMain = k.IndexOf("MainTex", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSpec = k.IndexOf("SpecTex", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isVajGloss = k.IndexOf("GlossTex", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isBump = k.IndexOf("BumpMap", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isAlpha = k.IndexOf("AlphaTex", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isBump)
                {
                    flags.isNormalMap = true;
                    flags.linear = true;
                    flags.compress = false;
                    return true;
                }

                if (isSpec || isVajGloss)
                {
                    flags.linear = true;
                    return true;
                }

                if (isAlpha)
                {
                    flags.createAlphaFromGrayscale = true;
                    return true;
                }

                if (isMain) return true;
            }

            bool isNormal = k.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSpecular = k.IndexOf("Specular", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isGloss = k.IndexOf("Gloss", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isBumpUrl = k.IndexOf("BumpUrl", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isRough = k.IndexOf("Rough", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isMetal = k.IndexOf("Metal", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isAO = k.IndexOf("Occlusion", StringComparison.OrdinalIgnoreCase) >= 0 || k.IndexOf("AoUrl", StringComparison.OrdinalIgnoreCase) >= 0;

            flags.isNormalMap = isNormal;

            if (flags.isNormalMap)
            {
                flags.compress = false;
            }

            if (isBumpUrl && !flags.isNormalMap)
            {
                flags.createNormalFromBump = true;
            }

            if (isNormal || isSpecular || isGloss || isBumpUrl || isRough || isMetal || isAO)
            {
                flags.linear = true;
            }

            if (k.IndexOf("DiffuseUrl", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("DecalUrl", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("DetailUrl", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (isNormal || isSpecular || isGloss || isBumpUrl || isRough || isMetal || isAO) return true;

            return false;
        }

        private static void ExtractSceneUrlsRecursive(JSONNode node, string selfPackageUid, List<RequiredTexture> outTextures, List<RequiredJsonFile> outJsonFiles)
        {
            ExtractSceneUrlsRecursive(node, selfPackageUid, null, outTextures, outJsonFiles);
        }

        private static void ExtractSceneUrlsRecursive(JSONNode node, string selfPackageUid, string referencingInternalPath, List<RequiredTexture> outTextures, List<RequiredJsonFile> outJsonFiles)
        {
            if (node == null) return;

            var obj = node.AsObject;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JSONNode> kv in obj)
                {
                    string key = kv.Key;
                    JSONNode valueNode = kv.Value;

                    bool isStringLike = valueNode != null && valueNode.AsObject == null && valueNode.AsArray == null && !string.IsNullOrEmpty(valueNode.Value);
                    if (isStringLike && (!string.IsNullOrEmpty(key) && key.IndexOf("Url", StringComparison.OrdinalIgnoreCase) >= 0 || LooksLikeVamFileRef(valueNode.Value) || LooksLikeImagePath(valueNode.Value)))
                    {
                        string rawUrl = valueNode.Value;
                        string pkgId;
                        string internalPath;
                        if (TryResolveTextureRef(rawUrl, selfPackageUid, referencingInternalPath, out pkgId, out internalPath))
                        {
                            string il = (internalPath ?? string.Empty).ToLowerInvariant();
                            if ((il.EndsWith(".png") || il.EndsWith(".jpg") || il.EndsWith(".jpeg")) && outTextures != null)
                            {
                                TextureFlags flags;
                                if (!TryGetFlagsFromVapKey(key, out flags))
                                {
                                    flags = new TextureFlags { compress = true, linear = false, isNormalMap = false, createAlphaFromGrayscale = false, createNormalFromBump = false, invert = false, bumpStrength = 1f };
                                }
                                ApplyPathHeuristics(internalPath, ref flags);

                                outTextures.Add(new RequiredTexture
                                {
                                    PackageId = pkgId,
                                    InternalPath = internalPath,
                                    Flags = flags
                                });
                            }
                            else if ((il.EndsWith(".vap") || il.EndsWith(".json") || il.EndsWith(".vaj") || il.EndsWith(".vam") || il.EndsWith(".vmi")) && outJsonFiles != null)
                            {
                                outJsonFiles.Add(new RequiredJsonFile
                                {
                                    PackageId = pkgId,
                                    InternalPath = internalPath
                                });

                                if (il.EndsWith(".vam") && !string.IsNullOrEmpty(internalPath))
                                {
                                    string vajPath = null;
                                    try { vajPath = internalPath.Substring(0, internalPath.Length - 4) + ".vaj"; } catch { vajPath = null; }
                                    if (!string.IsNullOrEmpty(vajPath))
                                    {
                                        outJsonFiles.Add(new RequiredJsonFile
                                        {
                                            PackageId = pkgId,
                                            InternalPath = vajPath
                                        });
                                    }
                                }
                            }
                        }
                    }

                    ExtractSceneUrlsRecursive(valueNode, selfPackageUid, referencingInternalPath, outTextures, outJsonFiles);
                }
                return;
            }

            var arr = node.AsArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    ExtractSceneUrlsRecursive(arr[i], selfPackageUid, referencingInternalPath, outTextures, outJsonFiles);
                }
            }
        }

        private static string GetNativeCachePathDynamic(string imgPath, TextureFlags flags, long explicitSize, DateTime explicitLastWriteTime, int targetWidth = 0, int targetHeight = 0)
        {
            FileEntry fileEntry = null;
            try { fileEntry = FileManager.GetFileEntry(imgPath); } catch { fileEntry = null; }
            string textureCacheDir = null;
            try { textureCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir(); } catch { textureCacheDir = null; }
            if (string.IsNullOrEmpty(textureCacheDir)) return null;

            long size = explicitSize;
            DateTime lastWriteTime = explicitLastWriteTime;

            if (fileEntry != null)
            {
                size = fileEntry.Size;
                try
                {
                    VarFileEntry vfe = fileEntry as VarFileEntry;
                    if (vfe != null) size = vfe.EntrySize;
                }
                catch { }

                lastWriteTime = fileEntry.LastWriteTime;
            }

            if (size <= 0) return null;
            if (lastWriteTime == default(DateTime)) return null;

            string sizeStr = size.ToString();
            string timeStr = lastWriteTime.ToFileTime().ToString();
            string fileName = Path.GetFileName(imgPath);
            fileName = fileName.Replace('.', '_');

            string sig = string.Empty;
            if (targetWidth > 0 && targetHeight > 0) sig += targetWidth + "_" + targetHeight;
            if (flags.compress) sig += "_C";
            if (flags.linear) sig += "_L";
            if (flags.isNormalMap) sig += "_N";
            if (flags.createAlphaFromGrayscale) sig += "_A";
            if (flags.createNormalFromBump) sig = sig + "_BN" + flags.bumpStrength;
            if (flags.invert) sig += "_I";

            return textureCacheDir + "/" + fileName + "_" + sizeStr + "_" + timeStr + "_" + sig + ".vamcache";
        }

        private static IEnumerator WorkerBuildSelectiveUnityCoroutine(VarPackage pkg, Dictionary<string, List<TextureFlags>> internalLowerToFlags, Dictionary<string, string> internalLowerToOriginal)
        {
            if (pkg == null || internalLowerToOriginal == null || internalLowerToOriginal.Count == 0) yield break;

            foreach (var kv in internalLowerToOriginal)
            {
                string internalLower = kv.Key;
                string internalPath = kv.Value;
                if (string.IsNullOrEmpty(internalPath)) continue;

                IEnumerator work = null;
                try
                {
                    List<TextureFlags> variants;
                    if (internalLowerToFlags == null || !internalLowerToFlags.TryGetValue(internalLower, out variants) || variants == null || variants.Count == 0)
                    {
                        variants = new List<TextureFlags>
                        {
                            new TextureFlags
                            {
                                compress = true,
                                linear = false,
                                isNormalMap = false,
                                createAlphaFromGrayscale = false,
                                createNormalFromBump = false,
                                invert = false,
                                bumpStrength = 1f
                            }
                        };
                    }

                    string imgUidPath = pkg.Uid + ":/" + internalPath;
                    if (TryFileEntryExists(imgUidPath))
                    {
                        work = WriteNativeCacheForImageVariantsCoroutine(imgUidPath, internalPath, variants, 0, default(DateTime));
                    }
                }
                catch { work = null; }

                if (work != null) yield return work;
                yield return null;
            }
        }

        private static bool TryFileEntryExists(string uidPath)
        {
            try { return FileManager.GetFileEntry(uidPath) != null; }
            catch { return false; }
        }

        private static IEnumerator WriteNativeCacheForImageVariantsCoroutine(string imgUidPath, string internalPath, List<TextureFlags> variants, long entrySize, DateTime entryTime)
        {
            if (string.IsNullOrEmpty(imgUidPath) || string.IsNullOrEmpty(internalPath) || variants == null || variants.Count == 0) yield break;

            float frameStart = Time.realtimeSinceStartup;

            for (int v = 0; v < variants.Count; v++)
            {
                TextureFlags flags = variants[v];
                ApplyPathHeuristics(internalPath, ref flags);
                variants[v] = flags;

                bool buildSized = ShouldBuildSizedCache(flags, internalPath);
                int[] sizedWidths = buildSized ? new[] { 0, DefaultSizedCacheWidth } : new[] { 0 };
                int[] sizedHeights = buildSized ? new[] { 0, DefaultSizedCacheHeight } : new[] { 0 };

                for (int si = 0; si < sizedWidths.Length; si++)
                {
                    int targetWidth = sizedWidths[si];
                    int targetHeight = sizedHeights[si];

                    string cachePath = GetNativeCachePathDynamic(imgUidPath, flags, entrySize, entryTime, targetWidth, targetHeight);
                    if (string.IsNullOrEmpty(cachePath)) continue;

                    if (File.Exists(cachePath) && File.Exists(cachePath + "meta")) continue;

                    byte[] payload;
                    int w;
                    int h;
                    TextureFormat tf;
                    bool hasAlpha;
                    string err;
                    bool ok = TryBuildCachePayloadUnity(imgUidPath, internalPath, flags, targetWidth, targetHeight, out payload, out w, out h, out tf, out hasAlpha, out err);
                    if (!ok || payload == null) continue;

                    try
                    {
                        string dir = Path.GetDirectoryName(cachePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    }
                    catch { }

                    JSONClass meta = new JSONClass();
                    meta["type"] = "image";
                    meta["width"] = w.ToString();
                    meta["height"] = h.ToString();
                    meta["format"] = tf.ToString();

                    File.WriteAllText(cachePath + "meta", meta.ToString(string.Empty));
                    File.WriteAllBytes(cachePath, payload);

                    if (Time.realtimeSinceStartup - frameStart >= 0.008f)
                    {
                        frameStart = Time.realtimeSinceStartup;
                        yield return null;
                    }
                }

                if (Time.realtimeSinceStartup - frameStart >= 0.008f)
                {
                    frameStart = Time.realtimeSinceStartup;
                    yield return null;
                }
            }
        }

        private static void WriteNativeCacheForImageVariants(string imgUidPath, string internalPath, List<TextureFlags> variants, long entrySize, DateTime entryTime)
        {
            var it = WriteNativeCacheForImageVariantsCoroutine(imgUidPath, internalPath, variants, entrySize, entryTime);
            while (it != null && it.MoveNext()) { }
        }

        private static Dictionary<string, List<TextureFlags>> BuildInternalPathToFlagsFromPackagePresets(VarPackage pkg)
        {
            Dictionary<string, List<TextureFlags>> internalPathToFlags = new Dictionary<string, List<TextureFlags>>(StringComparer.OrdinalIgnoreCase);
            if (pkg == null) return internalPathToFlags;

            try
            {
                var seedJson = new List<RequiredJsonFile>();
                var seedTex = new List<RequiredTexture>();

                foreach (var fe in pkg.FileEntries)
                {
                    if (fe == null) continue;
                    if (string.IsNullOrEmpty(fe.InternalPath)) continue;

                    bool isPreset = fe.InternalPath.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".vaj", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".vam", StringComparison.OrdinalIgnoreCase)
                        || fe.InternalPath.EndsWith(".vmi", StringComparison.OrdinalIgnoreCase);
                    if (!isPreset) continue;

                    try
                    {
                        string uidPath = pkg.Uid + ":/" + fe.InternalPath;
                        string text = FileManager.ReadAllText(uidPath);
                        if (string.IsNullOrEmpty(text)) continue;

                        JSONNode node = null;
                        try { node = JSON.Parse(text); } catch { node = null; }
                        if (node == null) continue;

                        ExtractSceneUrlsRecursive(node, pkg.Uid, fe.InternalPath, seedTex, seedJson);
                    }
                    catch { }
                }

                int maxJsonDepth = 2;
                var visitedJson = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<KeyValuePair<RequiredJsonFile, int>>();
                for (int i = 0; i < seedJson.Count; i++)
                {
                    queue.Enqueue(new KeyValuePair<RequiredJsonFile, int>(seedJson[i], 1));
                }

                while (queue.Count > 0)
                {
                    var kv = queue.Dequeue();
                    RequiredJsonFile rj = kv.Key;
                    int depth = kv.Value;
                    if (depth > maxJsonDepth) continue;
                    if (string.IsNullOrEmpty(rj.PackageId) || string.IsNullOrEmpty(rj.InternalPath)) continue;

                    if (!NormalizePackageId(rj.PackageId).Equals(NormalizePackageId(pkg.Uid), StringComparison.OrdinalIgnoreCase)) continue;

                    string jsonUidPath = pkg.Uid + ":/" + rj.InternalPath;
                    if (!visitedJson.Add(jsonUidPath)) continue;

                    string txt = null;
                    try { txt = FileManager.ReadAllText(jsonUidPath); } catch { txt = null; }
                    if (string.IsNullOrEmpty(txt)) continue;

                    JSONNode n = null;
                    try { n = JSON.Parse(txt); } catch { n = null; }
                    if (n == null) continue;

                    var nestedTex = new List<RequiredTexture>();
                    var nestedJson = new List<RequiredJsonFile>();
                    ExtractSceneUrlsRecursive(n, pkg.Uid, rj.InternalPath, nestedTex, nestedJson);

                    if (nestedTex != null && nestedTex.Count > 0) seedTex.AddRange(nestedTex);
                    if (nestedJson != null && nestedJson.Count > 0)
                    {
                        for (int i = 0; i < nestedJson.Count; i++)
                        {
                            queue.Enqueue(new KeyValuePair<RequiredJsonFile, int>(nestedJson[i], depth + 1));
                        }
                    }
                }

                for (int i = 0; i < seedTex.Count; i++)
                {
                    var rt = seedTex[i];
                    if (string.IsNullOrEmpty(rt.PackageId) || string.IsNullOrEmpty(rt.InternalPath)) continue;
                    if (!NormalizePackageId(rt.PackageId).Equals(NormalizePackageId(pkg.Uid), StringComparison.OrdinalIgnoreCase)) continue;

                    string k = rt.InternalPath.ToLowerInvariant();
                    if (string.IsNullOrEmpty(k)) continue;

                    AddFlagVariant(internalPathToFlags, k, rt.Flags);
                }
            }
            catch { }

            return internalPathToFlags;
        }

        private static IEnumerator BuildCacheForSelectedPackageUnity(string packagePath)
        {
            if (string.IsNullOrEmpty(packagePath)) yield break;

            VarPackage pkg = null;
            try { pkg = FileManager.GetPackage(packagePath, false); }
            catch { pkg = null; }

            if (pkg == null) yield break;

            try { pkg.Scan(); }
            catch { }

            Dictionary<string, List<TextureFlags>> internalPathToFlags = BuildInternalPathToFlagsFromPackagePresets(pkg);

            List<ImageEntry> images = null;
            try { images = EnumerateAllImagesInVar(pkg); } catch { images = null; }
            if (images == null || images.Count == 0) yield break;

            for (int imgIndex = 0; imgIndex < images.Count; imgIndex++)
            {
                var img = images[imgIndex];
                if (string.IsNullOrEmpty(img.InternalPath)) continue;

                string internalLower = img.InternalPath.ToLowerInvariant();

                List<TextureFlags> variants;
                if (internalPathToFlags == null || !internalPathToFlags.TryGetValue(internalLower, out variants) || variants == null || variants.Count == 0)
                {
                    variants = new List<TextureFlags>
                    {
                        new TextureFlags
                        {
                            compress = true,
                            linear = false,
                            isNormalMap = false,
                            createAlphaFromGrayscale = false,
                            createNormalFromBump = false,
                            invert = false,
                            bumpStrength = 1f
                        }
                    };
                }

                string imgUidPath = pkg.Uid + ":/" + img.InternalPath;

                yield return WriteNativeCacheForImageVariantsCoroutine(imgUidPath, img.InternalPath, variants, img.EntrySize, img.EntryTime);

                // Avoid freezing the main thread for huge packages.
                if ((imgIndex & 7) == 0) yield return null;
            }
        }

        private static bool TryBuildCachePayloadUnity(string imgUidPath, string internalPath, TextureFlags flags, int targetWidth, int targetHeight, out byte[] payload, out int width, out int height, out TextureFormat format, out bool hasAlpha, out string error)
        {
            payload = null;
            width = 0;
            height = 0;
            format = TextureFormat.RGBA32;
            hasAlpha = false;
            error = null;

            byte[] src = null;
            try { src = FileManager.ReadAllBytes(imgUidPath); }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (src == null || src.Length == 0) return false;

            Texture2D tex = null;
            Texture2D working = null;
            RenderTexture rt = null;

            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, flags.linear);
                if (!tex.LoadImage(src))
                {
                    error = "LoadImage failed";
                    return false;
                }

                if (targetWidth > 0 && targetHeight > 0 && (tex.width != targetWidth || tex.height != targetHeight))
                {
                    rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32, flags.linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);
                    Graphics.Blit(tex, rt);
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;

                    working = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, true, flags.linear);
                    working.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0, true);
                    working.Apply(true, false);

                    RenderTexture.active = prev;
                }
                else
                {
                    working = tex;
                }

                if (flags.isNormalMap || flags.invert || flags.createAlphaFromGrayscale || flags.createNormalFromBump)
                {
                    Color32[] colors = working.GetPixels32();
                    if (colors != null && colors.Length > 0)
                    {
                        if (flags.isNormalMap)
                        {
                            for (int i = 0; i < colors.Length; i++)
                            {
                                var c = colors[i];
                                c.a = 255;
                                colors[i] = c;
                            }
                        }

                        if (flags.invert)
                        {
                            for (int i = 0; i < colors.Length; i++)
                            {
                                var c = colors[i];
                                c.r = (byte)(255 - c.r);
                                c.g = (byte)(255 - c.g);
                                c.b = (byte)(255 - c.b);
                                c.a = (byte)(255 - c.a);
                                colors[i] = c;
                            }
                        }

                        if (flags.createAlphaFromGrayscale)
                        {
                            for (int i = 0; i < colors.Length; i++)
                            {
                                var c = colors[i];
                                int avg = (c.r + c.g + c.b) / 3;
                                c.a = (byte)avg;
                                colors[i] = c;
                            }
                        }

                        if (flags.createNormalFromBump)
                        {
                            int w = working.width;
                            int h = working.height;
                            float[][] hMap = new float[h][];
                            for (int yy = 0; yy < h; yy++)
                            {
                                hMap[yy] = new float[w];
                                for (int xx = 0; xx < w; xx++)
                                {
                                    int idx = yy * w + xx;
                                    var c = colors[idx];
                                    hMap[yy][xx] = (c.r + c.g + c.b) / 768f;
                                }
                            }

                            Vector3 v = default(Vector3);
                            for (int yy = 0; yy < h; yy++)
                            {
                                for (int xx = 0; xx < w; xx++)
                                {
                                    float h21 = 0.5f, h22 = 0.5f, h23 = 0.5f, h24 = 0.5f, h25 = 0.5f, h26 = 0.5f, h27 = 0.5f, h28 = 0.5f;
                                    int xm1 = xx - 1, xp1 = xx + 1, yp1 = yy + 1, ym1 = yy - 1;

                                    if (yp1 < h && xm1 >= 0) h21 = hMap[yp1][xm1];
                                    if (xm1 >= 0) h22 = hMap[yy][xm1];
                                    if (ym1 >= 0 && xm1 >= 0) h23 = hMap[ym1][xm1];
                                    if (yp1 < h) h24 = hMap[yp1][xx];
                                    if (ym1 >= 0) h25 = hMap[ym1][xx];
                                    if (yp1 < h && xp1 < w) h26 = hMap[yp1][xp1];
                                    if (xp1 < w) h27 = hMap[yy][xp1];
                                    if (ym1 >= 0 && xp1 < w) h28 = hMap[ym1][xp1];

                                    float nx = h26 + 2f * h27 + h28 - h21 - 2f * h22 - h23;
                                    float ny = h23 + 2f * h25 + h28 - h21 - 2f * h24 - h26;
                                    v.x = nx * flags.bumpStrength;
                                    v.y = ny * flags.bumpStrength;
                                    v.z = 1f;
                                    v.Normalize();

                                    int idx = yy * w + xx;
                                    colors[idx] = new Color32(
                                        (byte)((v.x * 0.5f + 0.5f) * 255f),
                                        (byte)((v.y * 0.5f + 0.5f) * 255f),
                                        (byte)((v.z * 0.5f + 0.5f) * 255f),
                                        255);
                                }
                            }
                        }

                        working.SetPixels32(colors);
                        working.Apply(true, false);
                    }
                }

                if (flags.compress)
                {
                    working.Compress(true);
                    working.Apply(true, false);
                }

                width = working.width;
                height = working.height;
                format = working.format;

                payload = working.GetRawTextureData();
                if (payload == null || payload.Length == 0)
                {
                    error = "GetRawTextureData failed";
                    return false;
                }

                if (format == TextureFormat.DXT5 || format == TextureFormat.RGBA32)
                {
                    hasAlpha = true;
                }
                else if (format == TextureFormat.DXT1 || format == TextureFormat.RGB24)
                {
                    hasAlpha = false;
                }
                else
                {
                    hasAlpha = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (working != null && working != tex) UnityEngine.Object.Destroy(working);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        private static void ThrottledLog(string msg)
        {
            float now = Time.unscaledTime;
            if (now - s_OnDemandLastLogTime < OnDemandLogThrottleSeconds) return;
            s_OnDemandLastLogTime = now;
            LogUtil.Log(msg);
        }
    }

    internal static class OnDemandTextureCacheHook
    {
        private static bool s_HotkeyDown;
        private static bool s_MultiRunning;

        public static void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7)) s_HotkeyDown = true;

            if (!s_HotkeyDown) return;
            s_HotkeyDown = false;

            var sc = SuperController.singleton;
            if (sc == null) return;

            if (s_MultiRunning)
            {
                return;
            }

            var selectedScenePaths = new List<string>();
            var selectedPackagePaths = new List<string>();
            CollectSelectedGalleryTargets(selectedScenePaths, selectedPackagePaths);

            if ((selectedScenePaths != null && selectedScenePaths.Count > 0) || (selectedPackagePaths != null && selectedPackagePaths.Count > 0))
            {
                sc.StartCoroutine(RunMultiSelection(sc, selectedScenePaths, selectedPackagePaths));
                return;
            }

            // Fallback: no selection, try current scene
            NativeTextureOnDemandCache.TryBuildSceneCacheOnDemand(sc);
        }

        private static IEnumerator RunMultiSelection(MonoBehaviour host, List<string> scenePaths, List<string> packagePaths)
        {
            s_MultiRunning = true;
            try
            {
                // Scenes first
                if (scenePaths != null)
                {
                    for (int i = 0; i < scenePaths.Count; i++)
                    {
                        string p = scenePaths[i];
                        if (string.IsNullOrEmpty(p)) continue;

                        NativeTextureOnDemandCache.TryBuildSceneCacheOnDemand(host, p);
                        while (NativeTextureOnDemandCache.IsOnDemandBusy) yield return null;
                        yield return null;
                    }
                }

                // Packages second
                if (packagePaths != null)
                {
                    for (int i = 0; i < packagePaths.Count; i++)
                    {
                        string p = packagePaths[i];
                        if (string.IsNullOrEmpty(p)) continue;

                        NativeTextureOnDemandCache.TryBuildPackageCacheOnDemand(host, p);
                        while (NativeTextureOnDemandCache.IsOnDemandBusy) yield return null;
                        yield return null;
                    }
                }
            }
            finally
            {
                s_MultiRunning = false;
            }
        }

        private static void CollectSelectedGalleryTargets(List<string> scenePathsOut, List<string> packagePathsOut)
        {
            if (scenePathsOut == null || packagePathsOut == null) return;

            var sceneDedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pkgDedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var gallery = Gallery.singleton;
            if (gallery == null || gallery.Panels == null) return;

            for (int i = 0; i < gallery.Panels.Count; i++)
            {
                var panel = gallery.Panels[i];
                if (panel == null || panel.selectedFiles == null || panel.selectedFiles.Count == 0) continue;

                for (int j = 0; j < panel.selectedFiles.Count; j++)
                {
                    FileEntry fe = panel.selectedFiles[j];
                    if (fe == null) continue;

                    string selectedPath = fe.Path ?? string.Empty;
                    if (!string.IsNullOrEmpty(selectedPath))
                    {
                        string lower = selectedPath.ToLowerInvariant();
                        if (lower.EndsWith(".json"))
                        {
                            if (sceneDedup.Add(selectedPath)) scenePathsOut.Add(selectedPath);
                        }
                    }

                    string packagePath = null;
                    if (fe is VarFileEntry vfe && vfe.Package != null)
                    {
                        packagePath = vfe.Package.Path;
                    }
                    else
                    {
                        string p = selectedPath;
                        int idx = !string.IsNullOrEmpty(p) ? p.IndexOf(":/", StringComparison.Ordinal) : -1;
                        packagePath = idx > 0 ? p.Substring(0, idx) : p;
                    }

                    if (!string.IsNullOrEmpty(packagePath) && packagePath.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pkgDedup.Add(packagePath)) packagePathsOut.Add(packagePath);
                    }
                }
            }
        }
    }
}
