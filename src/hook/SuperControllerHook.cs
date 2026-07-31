using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx;
using UnityEngine;
using HarmonyLib;
using Prime31.MessageKit;
using GPUTools.Hair.Scripts.Settings;
using SimpleJSON;

namespace VPB
{
    public class SuperControllerHook
    {
        private static readonly Regex s_HubResourcePathRegex =
            new Regex(@"^/resources/(?<id>\d+)(/|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly object pluginCreateLock = new object();
        private static readonly Dictionary<int, Stopwatch> pluginCreateSwByThread = new Dictionary<int, Stopwatch>();
        private static readonly Dictionary<int, string> pluginCreateNameByThread = new Dictionary<int, string>();

        // Registry of confirmed simulation texture paths extracted from preset files
        private static HashSet<string> simTextureRegistry = new HashSet<string>();
        private static HashSet<string> simTexturePatchedThisLoad = new HashSet<string>();
        private static readonly object registryLock = new object();

        /// <summary>
        /// Registers a texture path as a simulation texture based on preset parsing
        /// </summary>
        public static void RegisterSimTexture(string path)
        {
            RegisterSimTexture(path, null);
        }

        public static void RegisterSimTexture(string path, string presetPath)
        {
            if (string.IsNullOrEmpty(path)) return;
            List<string> candidates = BuildSimTextureRegistryCandidates(path, presetPath);
            lock (registryLock)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    simTextureRegistry.Add(candidates[i]);
                }
            }
        }

        /// <summary>
        /// Clears the sim texture registry when loading a new scene
        /// </summary>
        public static void ClearSimTextureRegistry()
        {
            lock (registryLock)
            {
                simTextureRegistry.Clear();
                simTexturePatchedThisLoad.Clear();
            }
        }

        /// <summary>
        /// Parses a clothing preset (.vaj/.vap) to find sim-enabled textures
        /// </summary>
        public static void ParsePresetForSimTextures(string presetPath)
        {
            if (string.IsNullOrEmpty(presetPath)) return;
            
            try
            {
                JSONNode root = UI.LoadJSONWithFallback(presetPath, null);
                if (root == null) return;

                ParseNodeForSimTexturesRecursive(root, presetPath);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB SIM] Failed to parse preset for sim textures: {presetPath} - {ex.Message}");
            }
        }

        public static void ParsePresetForSimTextures(JSONNode root, string presetPath)
        {
            if (root == null) return;

            try
            {
                ParseNodeForSimTexturesRecursive(root, presetPath);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB SIM] Failed to parse preset JSON for sim textures: " + ex.Message);
            }
        }

        private static void ParseNodeForSimTexturesRecursive(JSONNode node, string presetPath)
        {
            if (node == null) return;

            var obj = node.AsObject;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JSONNode> kv in obj)
                {
                    string key = kv.Key;
                    JSONNode value = kv.Value;

                    if (IsSimulationTextureKey(key) && LooksLikeImagePath(value != null ? value.Value : null))
                    {
                        string textureUrl = value.Value;
                        RegisterSimTexture(textureUrl, presetPath);
                        LogUtil.Log($"[VPB SIM] Registered sim texture from key '{key}': {textureUrl}");
                    }

                    // Check if this entry has simEnabled="true"
                    if (key.Equals("simEnabled", StringComparison.OrdinalIgnoreCase))
                    {
                        string valStr = value != null ? value.Value?.ToLowerInvariant() : null;
                        if (valStr == "true" || valStr == "1")
                        {
                            // Look for texture URL in the parent or sibling nodes
                            // The structure is typically: { "id": "...", "simEnabled": "true", "textureUrl": "..." }
                            // Or: { "id": "...Sim", "simEnabled": "true", ... }
                            
                            // Try to find a texture URL in the same object
                            string textureUrl = FindTextureUrlInObject(obj);
                            if (!string.IsNullOrEmpty(textureUrl))
                            {
                                RegisterSimTexture(textureUrl, presetPath);
                                LogUtil.Log($"[VPB SIM] Registered sim texture from preset: {textureUrl}");
                            }
                        }
                    }

                    // Recurse into child nodes
                    ParseNodeForSimTexturesRecursive(value, presetPath);
                }
            }

            var arr = node.AsArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    ParseNodeForSimTexturesRecursive(arr[i], presetPath);
                }
            }
        }

        private static string FindTextureUrlInObject(JSONClass obj)
        {
            if (obj == null) return null;

            // Common keys for texture URLs in clothing presets
            string[] urlKeys = new[] { "simTexture", "SimTexture", "simulationTexture", "SimulationTexture",
                                       "stimulationTexture", "StimulationTexture", "physicsTexture", "PhysicsTexture",
                                       "url", "Url", "textureUrl", "TextureUrl",
                                       "diffuseUrl", "normalUrl", "specularUrl", "glossUrl" };

            foreach (var key in urlKeys)
            {
                if (obj[key] != null)
                {
                    string val = obj[key].Value;
                    if (LooksLikeImagePath(val))
                    {
                        return val;
                    }
                }
            }

            // Also check "id" field which often contains the texture reference
            if (obj["id"] != null)
            {
                string id = obj["id"].Value;
                // If id ends with "Sim" and there's a nearby texture reference
                if (id.EndsWith("Sim", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to find any URL field
                    foreach (KeyValuePair<string, JSONNode> kv in obj)
                    {
                        if (kv.Key.IndexOf("Url", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            kv.Key.IndexOf("url", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string val = kv.Value?.Value;
                            if (LooksLikeImagePath(val))
                                return val;
                        }
                    }
                }
            }

            return null;
        }

        private static List<string> BuildSimTextureRegistryCandidates(string path, string presetPath)
        {
            var candidates = new List<string>();
            AddNormalizedSimTextureCandidate(candidates, path);

            string resolved = ResolveTexturePathRelativeToPreset(path, presetPath);
            AddNormalizedSimTextureCandidate(candidates, resolved);

            if (!string.IsNullOrEmpty(path))
            {
                string trimmed = path.Trim();
                if (trimmed.StartsWith("./", StringComparison.Ordinal) || trimmed.StartsWith(".\\", StringComparison.Ordinal))
                {
                    AddNormalizedSimTextureCandidate(candidates, trimmed.Substring(2));
                }
            }

            return candidates;
        }

        private static void AddNormalizedSimTextureCandidate(List<string> candidates, string path)
        {
            string normalized = NormalizeSimTexturePath(path);
            if (string.IsNullOrEmpty(normalized)) return;
            if (!candidates.Contains(normalized)) candidates.Add(normalized);
        }

        private static string NormalizeSimTexturePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string normalized = path.Trim().Replace('\\', '/').ToLowerInvariant();
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);

            return normalized;
        }

        private static string ResolveTexturePathRelativeToPreset(string texturePath, string presetPath)
        {
            if (string.IsNullOrEmpty(texturePath) || string.IsNullOrEmpty(presetPath)) return null;

            string tex = texturePath.Trim().Replace('\\', '/');
            if (tex.IndexOf(":/", StringComparison.Ordinal) >= 0 || tex.StartsWith("/", StringComparison.Ordinal))
                return tex;

            while (tex.StartsWith("./", StringComparison.Ordinal))
                tex = tex.Substring(2);

            string preset = presetPath.Trim().Replace('\\', '/');
            int slash = preset.LastIndexOf('/');
            if (slash < 0) return null;

            return preset.Substring(0, slash + 1) + tex;
        }

        private static bool RegistryContainsSimulationTexture(string lowerPath)
        {
            string normalized = NormalizeSimTexturePath(lowerPath);
            if (string.IsNullOrEmpty(normalized)) return false;

            lock (registryLock)
            {
                if (simTextureRegistry.Contains(normalized))
                    return true;

                foreach (string registered in simTextureRegistry)
                {
                    if (string.IsNullOrEmpty(registered) || registered.Length < 8) continue;
                    if (normalized.EndsWith("/" + registered, StringComparison.Ordinal)) return true;
                    if (registered.EndsWith("/" + normalized, StringComparison.Ordinal)) return true;
                }
            }

            return false;
        }

        private static bool LooksLikeImagePath(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsSimulationTextureKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            if (key.Equals("simTexture", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.Equals("simulationTexture", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.Equals("stimulationTexture", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.Equals("physicsTexture", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.IndexOf("simTexture", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (key.IndexOf("simulationTexture", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (key.IndexOf("stimulationTexture", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (key.IndexOf("physicsTexture", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        private static bool IsClothingSimulationMapName(string lowerPath, string filename)
        {
            if (string.IsNullOrEmpty(lowerPath) || string.IsNullOrEmpty(filename)) return false;
            if (lowerPath.IndexOf("/clothing/", StringComparison.Ordinal) < 0) return false;

            if (filename.IndexOf("simtexture", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("simulationtexture", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("stimulationtexture", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("physicstexture", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("heatmap", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("heat_map", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("heat-map", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("heatzone", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("heat_zone", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("heat-zone", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("weightmap", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("weight_map", StringComparison.Ordinal) >= 0) return true;
            if (filename.IndexOf("weight-map", StringComparison.Ordinal) >= 0) return true;

            if (Regex.IsMatch(filename, @"(^|[_\-])stim(ulation)?([_\-]|\d|$)", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(filename, @"(^|[_\-])heat([_\-]|\d|$)", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(filename, @"(^|[_\-])zone([_\-]|\d|$)", RegexOptions.IgnoreCase)) return true;

            return false;
        }

        private static bool IsTextureReadableCompat(Texture2D tex)
        {
            if (tex == null) return false;
            try
            {
                // Unity versions used by VaM may not expose Texture2D.isReadable.
                // Probe via GetPixel, which throws when the texture is non-readable.
                tex.GetPixel(0, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// <see cref="DAZCharacterTextureControl"/> queues nested <c>CharacterQueuedImage</c>.
        /// Auto genital blend (<c>BlendGenitalTexture</c>) needs CPU <c>GetPixels</c> on torso/genital maps.
        /// Native <c>ImageLoaderThreaded.Finish</c> uses <c>Apply()</c> (keeps readable); VPB zstd serve must match.
        /// </summary>
        internal static bool IsCharacterTextureQueuedImage(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return false;
            try
            {
                Type t = qi.GetType();
                return t != null && t.DeclaringType == typeof(DAZCharacterTextureControl);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Textures that must stay CPU-readable after VPB cache serve (sim maps + character skin).</summary>
        internal static bool NeedsCpuReadableTexture(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return false;
            if (IsCharacterTextureQueuedImage(qi)) return true;
            return IsSimulationTexturePath(qi.imgPath);
        }

        /// <summary>GPU blit → readable RGBA32 copy. Caller owns destroy of returned texture when different from <paramref name="src"/>.</summary>
        internal static Texture2D EnsureCpuReadableTexture(Texture2D src, bool linear, string logTag)
        {
            if (src == null) return null;
            if (IsTextureReadableCompat(src)) return src;

            RenderTexture rt = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                Texture2D readableTex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, linear);
                readableTex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                readableTex.Apply(false, false);
                if (!string.IsNullOrEmpty(logTag))
                    LogUtil.Log("[VPB] " + logTag + ": made texture CPU-readable " + src.width + "x" + src.height);
                return readableTex;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] EnsureCpuReadableTexture failed: " + ex.Message);
                return src;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static bool IsPluginsAlwaysEnabledSettingOn()
        {
            return true;
        }

        static Dictionary<string, int> _priorityCache = new Dictionary<string, int>(StringComparer.Ordinal);
        static object _priorityCacheLock = new object();

        static bool Has(string source, string value)
        {
            if (source == null || value == null) return false;
            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int GetImagePriority(string path)
        {
            if (string.IsNullOrEmpty(path)) return 1000;
            
            int priority;
            lock (_priorityCacheLock)
            {
                if (_priorityCache.TryGetValue(path, out priority))
                    return priority;
            }

            priority = CalculateImagePriority(path);

            lock (_priorityCacheLock)
            {
                if (_priorityCache.Count >= 10000) _priorityCache.Clear();
                if (!_priorityCache.ContainsKey(path))
                    _priorityCache.Add(path, priority);
            }
            return priority;
        }

        internal static bool IsSimulationTexturePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string lower = path.ToLowerInvariant();

            // First check the registry of confirmed sim textures from preset parsing.
            // Many VaM clothing presets use neutral names for their sim/heat maps, so
            // preset keys are more reliable than filename heuristics when available.
            if (RegistryContainsSimulationTexture(lower))
                return true;

            // Fall back to heuristic detection
            if (lower.Contains("phys")) return true;
            if (lower.Contains("simulation")) return true;

            string[] pathSegments = lower.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pathSegments.Length - 1; i++)
            {
                string segment = pathSegments[i];
                if (segment == "sim" || segment == "simulation" || segment == "phys" || segment == "physics")
                    return true;
            }

            int lastSlash = lower.LastIndexOfAny(new char[] { '/', '\\' });
            string filename = lastSlash >= 0 ? lower.Substring(lastSlash + 1) : lower;
            int lastDot = filename.LastIndexOf('.');
            if (lastDot > 0) filename = filename.Substring(0, lastDot);

            if (IsClothingSimulationMapName(lower, filename)) return true;

            // Conservative fallback only: match "sim" as a token to avoid false positives like "simone".
            // Accepted examples: sim_foo, foo_sim, foo-sim1, sim1, phys_foo, physics-2
            if (Regex.IsMatch(filename, @"(^|[_\-])sim([_\-]|\d|$)", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(filename, @"(^|[_\-])phys(ics)?([_\-]|\d|$)", RegexOptions.IgnoreCase)) return true;

            // Some clothing packages use names like "pnt3lsim1" without delimiters.
            // Treat a trailing "sim<digits>" / "phys<digits>" as SIM to prevent non-readable loads.
            if (Regex.IsMatch(filename, @"sim\d+$", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(filename, @"phys(ics)?\d+$", RegexOptions.IgnoreCase)) return true;

            return false;
        }

        internal static bool IsLutTexturePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string lower = path.ToLowerInvariant();
            // LUT (Look-Up Table) textures for color grading
            if (lower.Contains("lut")) return true;
            if (lower.Contains("lensdirt")) return true;
            if (lower.IndexOf("lens dirt", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        internal static bool IsTiffTexturePath(string path)
        {
            return TextureUtil.IsTiffTexturePath(path);
        }

        static int CalculateImagePriority(string path)
        {
            if (string.IsNullOrEmpty(path)) return 1000;
            string p = path;

            if (IsSimulationTexturePath(p)) return -1;

            if (Has(p, "/hair/") || Has(p, "/hairstyles/") || Has(p, "/textures/hair") || Has(p, "hair_") || Has(p, "scalp") || Has(p, "strand") || Has(p, "hairtex")) return 0;

            if (Has(p, "/textures/makeups/") || Has(p, "/textures/makeup/") || Has(p, "/makeups/")) return 1;
            if (Has(p, "/textures/decals/") || Has(p, "/textures/decal/") || Has(p, "/decals/") || Has(p, "/decal/")) return 1;
            if (Has(p, "/textures/overlays/") || Has(p, "/textures/overlay/") || Has(p, "/overlays/") || Has(p, "/overlay/")) return 1;
            if (Has(p, "facemask") || Has(p, "face_mask") || Has(p, "mask") || Has(p, "opacity") || Has(p, "alpha"))
            {
                if (Has(p, "face") || Has(p, "makeup") || Has(p, "makeups") || Has(p, "freckle") || Has(p, "blush")) return 1;
            }
            if (Has(p, "freckle") || Has(p, "blush") || Has(p, "eyeshadow") || Has(p, "eye_shadow") || Has(p, "eyeliner") || Has(p, "eye_liner") || Has(p, "lipstick") || Has(p, "lip") || Has(p, "brow") || Has(p, "eyebrow") || Has(p, "foundation") || Has(p, "concealer") || Has(p, "highlight") || Has(p, "highlighter") || Has(p, "contour") || Has(p, "powder")) return 1;
            if (Has(p, "/textures/") && (Has(p, "/face") || Has(p, "faced") || Has(p, "face_"))) return 1;
            if (Has(p, "mouth")) return 2;
            if (Has(p, "eye") || Has(p, "iris") || Has(p, "cornea") || Has(p, "eyeball")) return 3;
            if (Has(p, "head")) return 4;
            if (Has(p, "torso") || Has(p, "body")) return 5;
            if (Has(p, "limb") || Has(p, "arms") || Has(p, "legs")) return 6;
            return 100;
        }

        static string GetImageCategory(string path)
        {
            int pri = GetImagePriority(path);
            if (pri == 0) return "hair";
            if (pri == 1) return "face";
            if (pri == 2) return "mouth";
            if (pri == 3) return "eyes";
            if (pri == 4) return "head";
            if (pri == 5) return "body";
            if (pri == 6) return "limbs";
            return "other";
        }

        static string RewriteVdsPathIfNeeded(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return path;
                if (!VdsLauncher.IsVdsEnabled()) return path;
                if (!LogUtil.IsSceneLoadActive()) return path;

                string scenePkg = LogUtil.GetSceneLoadPackageUid();

                string p = path.Replace('\\', '/');
                if (p.StartsWith("SELF:/", StringComparison.OrdinalIgnoreCase))
                {
                    string curPkg = null;
                    try { curPkg = MVR.FileManagement.FileManager.CurrentPackageUid; } catch { }
                    string pkg = !string.IsNullOrEmpty(curPkg) ? curPkg : scenePkg;
                    if (string.IsNullOrEmpty(pkg)) return path;
                    return pkg + ":/" + p.Substring("SELF:/".Length);
                }
                if (p.Contains(":/")) return path;
                if (!p.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase)) return path;

                if (string.IsNullOrEmpty(scenePkg)) return path;

                string candidate = scenePkg + ":/" + p;
                if (VPB.FileManager.GetVarFileEntry(candidate) != null)
                {
                    return candidate;
                }
                return path;
            }
            catch
            {
                return path;
            }
        }

        public static void PatchOptional(Harmony harmony)
        {
            PatchFileExists(harmony);
            PatchProcessImage(harmony);
            PatchGetFiles(harmony);
        }

        // VaM's FileManager.GetFiles THROWS "Attempted to get files at non-existent path" when the
        // directory is not present in its registered (simulated) filesystem, instead of returning an
        // empty array like standard .NET enumeration. With the scan whitelist enabled, package-content
        // directories are only registered on demand, so plugins that enumerate a directory of a
        // not-yet-registered package (e.g. MacGruber PostMagic's UserLUT enumerating its LUT folder
        // inside a deferred image callback) crash the throw straight through their own callback.
        //
        // PREFIX: for a package-prefixed directory path, trigger on-demand registration so the
        //         enumeration resolves to the real files.
        // FINALIZER: swallow the "non-existent path" throw and return an empty array, matching the
        //            standard semantics plugins assume, so an unregistered directory degrades to
        //            "no files" instead of aborting the caller.
        static void PatchGetFiles(Harmony harmony)
        {
            var fm = typeof(MVR.FileManagement.FileManager);
            var prefix = AccessTools.Method(typeof(SuperControllerHook), nameof(PreGetFiles));
            var finalizer = AccessTools.Method(typeof(SuperControllerHook), nameof(FinalizeGetFiles));
            if (prefix == null && finalizer == null) return;
            var candidates = new Type[][]
            {
                new[] { typeof(string), typeof(string), typeof(bool) },
                new[] { typeof(string), typeof(string) },
                new[] { typeof(string) }
            };
            foreach (var sig in candidates)
            {
                var m = AccessTools.Method(fm, "GetFiles", sig);
                if (m == null) continue;
                harmony.Patch(m,
                    prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                    finalizer: finalizer != null ? new HarmonyMethod(finalizer) : null);
                return;
            }
        }

        public static void PreGetFiles(string __0)
        {
            try
            {
                if (VamOnDemandLoader.s_InOnDemand) return;
                if (ScanWhitelistManager.Instance == null || !ScanWhitelistManager.Instance.IsEnabled) return;

                string uid = VamOnDemandLoader.UidFromEntryPath(__0);
                if (string.IsNullOrEmpty(uid)) return;

                VamOnDemandLoader.s_InOnDemand = true;
                try { VamOnDemandLoader.TryRegisterPackageOnDemand(uid); }
                finally { VamOnDemandLoader.s_InOnDemand = false; }
            }
            catch { }
        }

        public static Exception FinalizeGetFiles(Exception __exception, string __0, ref string[] __result)
        {
            if (__exception == null) return null;
            // Only neutralize the specific "directory does not exist in the simulated filesystem"
            // throw; let any other failure (IO errors, etc.) propagate unchanged.
            string msg = __exception.Message ?? string.Empty;
            if (msg.IndexOf("non-existent path", StringComparison.OrdinalIgnoreCase) < 0)
                return __exception;

            __result = new string[0];
            LogUtil.LogWarning("[VPB] FileManager.GetFiles non-existent path suppressed (returned empty): " + __0);
            return null;
        }

        static void PatchFileExists(Harmony harmony)
        {
            var fm = typeof(MVR.FileManagement.FileManager);
            var prefix = AccessTools.Method(typeof(SuperControllerHook), nameof(PreFileExists));
            if (prefix == null) return;
            var candidates = new Type[][]
            {
                new[] { typeof(string), typeof(bool), typeof(bool) },
                new[] { typeof(string), typeof(bool) },
                new[] { typeof(string) }
            };
            foreach (var sig in candidates)
            {
                var m = AccessTools.Method(fm, "FileExists", sig);
                if (m == null) continue;
                string postfixName = sig.Length == 3
                    ? nameof(PostFileExists3)
                    : (sig.Length == 2 ? nameof(PostFileExists2) : nameof(PostFileExists1));
                var postfix = AccessTools.Method(typeof(SuperControllerHook), postfixName);
                harmony.Patch(m,
                    prefix: new HarmonyMethod(prefix),
                    postfix: postfix != null ? new HarmonyMethod(postfix) : null);
                return;
            }
        }

        static void PatchProcessImage(Harmony harmony)
        {
            var ilt = typeof(ImageLoaderThreaded);
            var prefix = AccessTools.Method(typeof(SuperControllerHook), nameof(PreProcessImage));
            var postfix = AccessTools.Method(typeof(SuperControllerHook), nameof(PostProcessImage));
            if (prefix == null) return;
            var methods = AccessTools.GetDeclaredMethods(ilt);
            if (methods == null) return;
            foreach (var m in methods)
            {
                if (m == null) continue;
                if (!string.Equals(m.Name, "ProcessImage", StringComparison.Ordinal)) continue;
                var p = m.GetParameters();
                if (p == null || p.Length == 0) continue;
                if (p[0].ParameterType != typeof(ImageLoaderThreaded.QueuedImage)) continue;
                harmony.Patch(m, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                return;
            }
        }

        static int s_ImgqEventLogged;
        static int s_ImgqEventSilenced;
        static bool s_ImgqEventSummaryLogged;
        const int ImgqEventLogMax = 10;
        static readonly object s_ImgqEventLogLock = new object();

        static bool TryLogImgqEvent()
        {
            lock (s_ImgqEventLogLock)
            {
                if (s_ImgqEventLogged < ImgqEventLogMax)
                {
                    s_ImgqEventLogged++;
                    return true;
                }
                s_ImgqEventSilenced++;
                if (!s_ImgqEventSummaryLogged)
                {
                    s_ImgqEventSummaryLogged = true;
                    LogUtil.Log("[VPB] Silenced further IMGQ logs (first " + ImgqEventLogMax
                        + " shown; additional image queue events suppressed)");
                }
                return false;
            }
        }

        static void LogImageQueueEvent(string evt, ImageLoaderThreaded.QueuedImage qi, int queueCount, int numRealQueuedImages, bool moved)
        {
            if (qi == null) return;
            try
            {
                if (Settings.Instance != null && Settings.Instance.LogImageQueueEvents != null && !Settings.Instance.LogImageQueueEvents.Value)
                {
                    return;
                }
            }
            catch { }
            if (!TryLogImgqEvent()) return;
            string scene = LogUtil.GetSceneLoadName();
            int pri = GetImagePriority(qi.imgPath);
            string cat = GetImageCategory(qi.imgPath);
            string thumb = qi.isThumbnail ? "thumb" : "img";
            LogUtil.Log(string.Format("IMGQ {0} scene={1} type={2} cat={3} pri={4} moved={5} q={6} realq={7} path={8}", evt, scene, thumb, cat, pri, moved ? "1" : "0", queueCount, numRealQueuedImages, qi.imgPath));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static void PreRefresh()
        {
            try { PackageHidePrefs.InvalidateHideMarkerCache(); } catch { }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "NormalizeLoadPath", new Type[] { typeof(string) })]
        public static void PreNormalizeLoadPath(ref string path)
        {
            // Defense in depth: NormalizeLoadPath is on the trigger-action restore path. Any
            // exception escaping this prefix unwinds out of MacGruber's per-state loop in
            // LateRestoreFromJSON and silently drops every state after the throw point.
            try
            {
                string rewritten = RewriteVdsPathIfNeeded(path);
                if (!string.Equals(rewritten, path, StringComparison.Ordinal))
                {
                    path = rewritten;
                }

                string best = VamOnDemandLoader.RewriteEntryPathToBestAvailable(path, attemptRegister: true);
                if (!string.Equals(best, path, StringComparison.OrdinalIgnoreCase))
                {
                    path = best;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] PreNormalizeLoadPath swallowed exception for path='" + path + "': " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static bool PreFileExists(string __0, ref bool __result)
        {
            if (VamStartupOptimizations.ShouldSkipVamXFileExistsWork(__0))
            {
                __result = false;
                return false;
            }

            // Same defensive wrap as PreNormalizeLoadPath: FileExists is on the trigger restore
            // path too, and an unguarded throw breaks MacGruber-style per-state JSON loops.
            try
            {
                string rewritten = RewriteVdsPathIfNeeded(__0);
                if (!string.Equals(rewritten, __0, StringComparison.Ordinal))
                {
                    __0 = rewritten;
                }

                string best = VamOnDemandLoader.RewriteEntryPathToBestAvailable(__0, attemptRegister: true);
                if (!string.Equals(best, __0, StringComparison.OrdinalIgnoreCase))
                {
                    __0 = best;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] PreFileExists swallowed exception for path='" + __0 + "': " + ex.GetType().Name + ": " + ex.Message);
            }
            return true;
        }

        public static void PostFileExists3(string __0, bool __1, ref bool __result)
        {
            PostFileExistsOnDemand(__0, __1, ref __result);
            LogUtil.RecordFileExistsResult(__result);
        }

        public static void PostFileExists2(string __0, bool __1, ref bool __result)
        {
            PostFileExistsOnDemand(__0, __1, ref __result);
            LogUtil.RecordFileExistsResult(__result);
        }

        public static void PostFileExists1(string __0, ref bool __result)
        {
            PostFileExistsOnDemand(__0, false, ref __result);
            LogUtil.RecordFileExistsResult(__result);
        }

        private static void PostFileExistsOnDemand(string path, bool onlySystemFiles, ref bool result)
        {
            try
            {
                if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.FileExistsHook++;
                if (result) return;
                if (onlySystemFiles) return;
                if (!ScanWhitelistManager.Instance.IsEnabled) return;
                if (VamOnDemandLoader.s_InOnDemand) return;

                string uid = VamOnDemandLoader.UidFromEntryPath(path);
                if (string.IsNullOrEmpty(uid)) return;
                LogUtil.RecordVarEntryMiss();

                if (VamOnDemandLoader.ShouldDeferStartupOnDemandForPath(path, uid))
                    return;

                if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.FileExistsHookHeavy++;
                LogUtil.RecordOnDemandRetry();
                VamOnDemandLoader.TryRegisterPackageOnDemandForEntryPath(path);
                VamOnDemandLoader.s_InOnDemand = true;
                try
                {
                    if (MVR.FileManagement.FileManager.GetVarFileEntry(path) != null)
                    {
                        result = true;
                        return;
                    }

                    // VaM may check FileExists against a *.latest:/... plugin path even
                    // after the package registered under its concrete UID.
                    string rewritten = VamOnDemandLoader.TryRewriteLatestEntryPath(path, attemptRegister: true);
                    if (!string.IsNullOrEmpty(rewritten) && !string.Equals(rewritten, path, StringComparison.OrdinalIgnoreCase))
                    {
                        LogUtil.RecordOnDemandRetry();
                        if (MVR.FileManagement.FileManager.GetVarFileEntry(rewritten) != null)
                            result = true;
                    }

                    // VaM may also request a specific version that isn't installed anymore
                    // (e.g. Author.Pkg.11), while a newer version exists (e.g. .14).
                    string rewrittenBest = VamOnDemandLoader.TryRewriteBestAvailableEntryPath(path, attemptRegister: true);
                    if (!string.IsNullOrEmpty(rewrittenBest) && !string.Equals(rewrittenBest, path, StringComparison.OrdinalIgnoreCase))
                    {
                        LogUtil.RecordOnDemandRetry();
                        if (MVR.FileManagement.FileManager.GetVarFileEntry(rewrittenBest) != null)
                            result = true;
                    }
                }
                finally
                {
                    VamOnDemandLoader.s_InOnDemand = false;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB OnDemand] PostFileExistsOnDemand error: " + ex.Message);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "OpenStream", new Type[] { typeof(string), typeof(bool) })]
        public static void PreOpenStream(ref string path)
        {
            string rewritten = RewriteVdsPathIfNeeded(path);
            if (!string.Equals(rewritten, path, StringComparison.Ordinal))
            {
                path = rewritten;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "OpenStream", new Type[] { typeof(string), typeof(bool) })]
        public static void PostOpenStream(object __result)
        {
            LogUtil.RecordOpenStreamResult(__result != null);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "OpenStreamReader", new Type[] { typeof(string), typeof(bool) })]
        public static void PreOpenStreamReader(ref string path)
        {
            string rewritten = RewriteVdsPathIfNeeded(path);
            if (!string.Equals(rewritten, path, StringComparison.Ordinal))
            {
                path = rewritten;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "OpenStreamReader", new Type[] { typeof(string), typeof(bool) })]
        public static void PostOpenStreamReader(object __result)
        {
            LogUtil.RecordOpenStreamResult(__result != null);
        }

        // Click "Return To Scene View"
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "DeactivateWorldUI")]
        public static void PostDeactivateWorldUI(SuperController __instance)
        {
            MessageKit.post(MessageDef.DeactivateWorldUI);
        }

        static bool s_ReturnToSceneViewOnStartupApplied;

        static bool IsReturnToSceneViewOnStartupEnabled()
        {
            try
            {
                return Settings.Instance != null
                    && Settings.Instance.ReturnToSceneViewOnStartup != null
                    && Settings.Instance.ReturnToSceneViewOnStartup.Value;
            }
            catch { return false; }
        }

        /// <summary>Re-arm so return-to-scene-view applies again after a scene reload (Hard Reset) re-shows the world UI.</summary>
        public static void ResetReturnToSceneViewOnStartupForSceneReload()
        {
            s_ReturnToSceneViewOnStartupApplied = false;
        }

        static void TryReturnToSceneViewOnStartup(SuperController sc)
        {
            if (s_ReturnToSceneViewOnStartupApplied || sc == null) return;
            if (!IsReturnToSceneViewOnStartupEnabled())
            {
                s_ReturnToSceneViewOnStartupApplied = true;
                return;
            }
            if (LogUtil.IsSceneLoadActive()) return;

            s_ReturnToSceneViewOnStartupApplied = true;
            try { sc.DeactivateWorldUI(); }
            catch (Exception ex) { LogUtil.LogWarning("[VPB] ReturnToSceneViewOnStartup failed: " + ex.Message); }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "ActivateWorldUI")]
        public static void PostActivateWorldUI(SuperController __instance)
        {
            TryReturnToSceneViewOnStartup(__instance);
            LogUtil.LogStartupReadyOnce("World UI activated");
            LogUtil.MarkScenePhaseWorldUiActivated();
            LogUtil.EndSceneLoadTotal("WorldUI.Activate");
        }

        /// <summary>Gallery VR pointer over pane: thumbstick scroll consumes forward navigate axis.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "GetFreeNavigateVector")]
        public static void PostGetFreeNavigateVector(ref Vector4 __result)
        {
            if (!GalleryVrThumbstickScroll.ShouldSuppressFreeNavigate) return;
            __result.x = 0f;
            __result.y = 0f;
            __result.z = 0f;
            __result.w = 0f;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "OpenLinkInBrowser", new Type[] { typeof(string) })]
        public static bool PreOpenLinkInBrowser(string url)
        {
            if (string.IsNullOrEmpty(url)) return true;
            if (VamHookPlugin.singleton == null || HubBrowse.singleton == null) return true;

            Uri uri;
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return true;
            }
            catch { return true; }

            // Redirect Hub web opens to VPB's in-game Hub browser ("Hook Hub").
            // This catches buttons/links anywhere in VaM that open hub.virtamate.com pages.
            if (!string.Equals(uri.Host, "hub.virtamate.com", StringComparison.OrdinalIgnoreCase)) return true;

            // Always open the Hook Hub UI; optionally jump to a resource detail.
            try { VamHookPlugin.singleton.OpenHubBrowse(); } catch { return true; }

            string resourceId = null;
            try
            {
                string path = uri.AbsolutePath ?? "";
                var m = s_HubResourcePathRegex.Match(path);
                if (m.Success) resourceId = m.Groups["id"]?.Value;
            }
            catch { resourceId = null; }

            try
            {
                if (!string.IsNullOrEmpty(resourceId)) HubBrowse.singleton.OpenDetail(resourceId);
                else HubBrowse.singleton.Show();
            }
            catch { return true; }

            // Skip the original browser-open.
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.Hub.HubBrowse), "Show")]
        public static bool PreVamNativeHubShow()
        {
            // Redirect VaM's native Hub UI to VPB "Hook Hub".
            var plugin = VamHookPlugin.singleton;
            if (plugin == null) return true;

            try { plugin.OpenHubBrowse(); }
            catch { return true; }

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVRPluginManager), "CreateScriptController")]
        public static void PreCreateScriptController(object mvrp, object type)
        {
            try
            {
                if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.ScriptCtrlCreate++;
                string pluginName = "unknown";
                try
                {
                    if (mvrp != null)
                    {
                        string uid = null;
                        string path = null;
                        var t = mvrp.GetType();
                        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        try
                        {
                            var p = t.GetProperty("storeId", flags);
                            if (p != null && p.PropertyType == typeof(string))
                                uid = p.GetValue(mvrp, null) as string;
                        }
                        catch { }
                        try
                        {
                            if (string.IsNullOrEmpty(uid))
                            {
                                var f = t.GetField("storeId", flags);
                                if (f != null && f.FieldType == typeof(string))
                                    uid = f.GetValue(mvrp) as string;
                            }
                        }
                        catch { }
                        try
                        {
                            var p = t.GetProperty("pluginPath", flags);
                            if (p != null && p.PropertyType == typeof(string))
                                path = p.GetValue(mvrp, null) as string;
                        }
                        catch { }
                        try
                        {
                            if (string.IsNullOrEmpty(path))
                            {
                                var f = t.GetField("pluginPath", flags);
                                if (f != null && f.FieldType == typeof(string))
                                    path = f.GetValue(mvrp) as string;
                            }
                        }
                        catch { }
                        pluginName = !string.IsNullOrEmpty(uid) ? uid : (!string.IsNullOrEmpty(path) ? path : mvrp.GetType().Name);
                    }
                }
                catch { }

                string scriptType = type != null ? type.ToString() : "unknown";
                int tid = System.Threading.Thread.CurrentThread.ManagedThreadId;
                var sw = Stopwatch.StartNew();
                lock (pluginCreateLock)
                {
                    pluginCreateSwByThread[tid] = sw;
                    pluginCreateNameByThread[tid] = pluginName + "|" + scriptType;
                }
                LogUtil.Log("[VPB.Startup] plugin_create START tid=" + tid + " plugin=" + pluginName + " type=" + scriptType);
            }
            catch { }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(MVRPluginManager), "CreateScriptController")]
        public static Exception FinalizeCreateScriptController(Exception __exception)
        {
            try
            {
                int tid = System.Threading.Thread.CurrentThread.ManagedThreadId;
                Stopwatch sw = null;
                string name = "unknown";
                lock (pluginCreateLock)
                {
                    if (pluginCreateSwByThread.TryGetValue(tid, out sw))
                        pluginCreateSwByThread.Remove(tid);
                    if (pluginCreateNameByThread.TryGetValue(tid, out name))
                        pluginCreateNameByThread.Remove(tid);
                }
                long ms = 0;
                try { if (sw != null) { sw.Stop(); ms = sw.ElapsedMilliseconds; } } catch { }
                if (__exception == null)
                {
                    LogUtil.Log("[VPB.Startup] plugin_create DONE tid=" + tid + " target=" + name + " ms=" + ms);
                }
                else
                {
                    LogUtil.LogWarning("[VPB.Startup] plugin_create FAIL tid=" + tid + " target=" + name + " ms=" + ms + " ex=" + __exception.GetType().Name + ": " + __exception.Message);
                }
            }
            catch { }
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "Load", new Type[] { typeof(string) })]
        public static void PreLoad(SuperController __instance, string saveName)
        {
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "LoadMerge", new Type[] { typeof(string) })]
        public static void PreLoadMerge(SuperController __instance, string saveName)
        {
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "LoadInternal", new Type[] {
            typeof(string),typeof(bool),typeof(bool)
        })]
        public static void PreLoadInternal(SuperController __instance,
            string saveName, bool loadMerge, bool editMode)
        {
            LogUtil.Log("PreLoadInternal " + saveName + " " + loadMerge + " " + editMode);
            LogUtil.BeginSceneLoad(saveName);
            LogUtil.MarkScenePhasePreLoadInternal();
            try { ThirdPartyFixHook.TryClearInGameLogsOnSceneLaunch(__instance, loadMerge); } catch { }
            // Scene Loader / VAM Browser / triggers all funnel here — record History outside VPB gallery UI.
            try { VpbLocalDatabase.TryRecordItemUseFromPath(saveName, "scene"); } catch { }
            try
            {
                // Clear sim texture registry for new scene
                ClearSimTextureRegistry();
                ParsePresetForSimTextures(saveName);

                try
                {
                    SceneLoadingUtils.NotifySceneLoadStarting(saveName, loadMerge);
                }
                catch { }

                try
                {
                    VpbPerfController.OnSceneLoadStarting(saveName, loadMerge);
                }
                catch { }

                if (ImageLoadingMgr.singleton != null)
                {
                    ImageLoadingMgr.singleton.PrepareForSceneLoad();
                }

                if (!string.IsNullOrEmpty(saveName))
                {
                    // Track current scene package UID for UninstallAll protection
                    int idx = saveName.IndexOf(":/");
                    if (idx >= 0)
                    {
                        VamHookPlugin.CurrentScenePackageUid = saveName.Substring(0, idx);
                    }
                    else if (!loadMerge)
                    {
                        // Only clear if not merging (merging implies we are adding to current scene)
                        VamHookPlugin.CurrentScenePackageUid = null;
                    }
                }

                if (!LogUtil.IsSceneClickActive())
                {
                    LogUtil.BeginSceneClick(saveName);
                }
            }
            catch { }

            if (saveName == "Saves\\scene\\MeshedVR\\default.json")
            {
                if (File.Exists(saveName))
                {
                    string text = File.ReadAllText(saveName);
                    FileButton.EnsureInstalledInternal(text);
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "LoadInternal", new Type[] {
            typeof(string),typeof(bool),typeof(bool)
        })]
        public static void PostLoadInternal(SuperController __instance,
            string saveName, bool loadMerge, bool editMode)
        {
            LogUtil.MarkScenePhasePostLoadInternal();
            LogUtil.EndSceneLoadInternal("LoadInternal");
            try { SceneLoadingUtils.ScheduleGalleryTargetListRefresh(); } catch { }
        }

        /// <summary>
        /// Keep gallery target picker in sync when an atom is removed (no polling).
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "RemoveAtom", new Type[] { typeof(Atom) })]
        public static void PostRemoveAtom(SuperController __instance, Atom atom)
        {
            try { GalleryPanel.NotifyAllPanelsSceneTargetsChanged(); } catch { }
        }

        /// <summary>
        /// Always set Allow Always
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.VarPackage), "LoadUserPrefs")]
        public static void PostLoadUserPrefs(MVR.FileManagement.VarPackage __instance)
        {
            if (__instance == null) return;
            try
            {
                Traverse.Create(__instance).Field("_pluginsAlwaysEnabled").SetValue(true);
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.VarPackage), "get_PluginsAlwaysEnabled")]
        public static void PostGetPluginsAlwaysEnabled(ref bool __result)
        {
            __result = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.VarPackage), "get_PluginsAlwaysDisabled")]
        public static void PostGetPluginsAlwaysDisabled(ref bool __result)
        {
            __result = false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "ProcessImageImmediate", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PreProcessImageImmediate(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            LogUtil.MarkImageActivity();

            ImageLoadingMgr.currentProcessingPath = qi.imgPath;
            ImageLoadingMgr.currentProcessingIsThumbnail = qi.isThumbnail;
            ImageLoadingMgr.currentProcessingQI = qi;

            if (!Settings.Instance.EnableZstdCompression.Value) return;

            bool immOk = ImageLoadingMgr.singleton.RequestImmediate(qi);
            try { int lvl = Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0; if (lvl >= 1 && (qi.createAlphaFromGrayscale || (qi.imgPath != null && qi.imgPath.IndexOf("Alphamidpart", StringComparison.OrdinalIgnoreCase) >= 0))) LogUtil.Log("[VPB IMM] path=" + qi.imgPath + " A=" + qi.createAlphaFromGrayscale + " ok=" + immOk + " tex=" + (qi.tex != null ? qi.tex.format.ToString() + " " + qi.tex.width + "x" + qi.tex.height : "null")); } catch { }
            if (immOk)
            {
                qi.skipCache = true;
                qi.processed = true;
                qi.finished = true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "ProcessImageImmediate", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PostProcessImageImmediate(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            ImageLoadingMgr.currentProcessingPath = null;
            ImageLoadingMgr.currentProcessingIsThumbnail = false;
            ImageLoadingMgr.currentProcessingQI = null;

            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            if (!Settings.Instance.EnableZstdCompression.Value) return;
        }

        public static void PreProcessImage(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage __0)
        {
            var qi = __0;
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            LogUtil.MarkImageActivity();

            ImageLoadingMgr.currentProcessingPath = qi.imgPath;
            ImageLoadingMgr.currentProcessingIsThumbnail = qi.isThumbnail;
        }

        public static void PostProcessImage(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage __0)
        {
            ImageLoadingMgr.currentProcessingPath = null;
            ImageLoadingMgr.currentProcessingIsThumbnail = false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueThumbnail", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static bool PreQueueThumbnail(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return true;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            try
            {
                if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                {
                    LogImageRequestDetails("thumb", qi);
                }
            }
            catch { }

            if (Settings.Instance == null || Settings.Instance.EnableZstdCompression == null) return true;
            if (!Settings.Instance.EnableZstdCompression.Value) return true;

            if (ImageLoadingMgr.singleton == null)
            {
                LogUtil.LogWarning("[VPB] PreQueueThumbnail: ImageLoadingMgr.singleton is null");
                return true;
            }

            if (qi.imgPath.EndsWith(".jpg")) qi.textureFormat = TextureFormat.RGB24;
            if (qi.imgPath.EndsWith(".png")) qi.textureFormat = TextureFormat.RGBA32;

            qi.isThumbnail = true;
            if (ImageLoadingMgr.singleton.Request(qi))
            {
                // Served from VPB cache: ensure VaM's thumbnail cache is populated.
                try
                {
                    var thumbCache = Traverse.Create(__instance).Field("thumbnailCache").GetValue() as Dictionary<string, Texture2D>;
                    if (thumbCache != null && qi.tex != null && !thumbCache.ContainsKey(qi.imgPath))
                    {
                        thumbCache.Add(qi.imgPath, qi.tex);
                    }
                }
                catch { }

                // Skip VaM threaded processing for this request.
                return false;
            }

            try
            {
                var tr = Traverse.Create(__instance);
                var q = tr.Field("queuedImages").GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                int qCount = q != null ? q.Count : 0;
                int realQ = 0;
                try { realQ = (int)tr.Field("numRealQueuedImages").GetValue(); } catch { }
                bool moved = false;
                LogImageQueueEvent("enqueue.thumb", qi, qCount, realQ, moved);
            }
            catch { }
            return true;
        }

        private static void LogImageRequestDetails(string kind, ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;

            string imgPath = qi.imgPath;
            string nativeCachePath = null;
            bool nativeExists = false;
            bool metaExists = false;
            bool zstdExists = false;
            FileEntry fe = null;

            try { fe = FileManager.GetFileEntry(imgPath); } catch { fe = null; }

            try
            {
                nativeCachePath = TextureUtil.FindVaMNativeDiskCachePath(
                    imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                    qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength);
                if (!string.IsNullOrEmpty(nativeCachePath))
                {
                    nativeExists = File.Exists(nativeCachePath);
                    metaExists = File.Exists(nativeCachePath + "meta");
                }
            }
            catch { }

            try
            {
                string zstdPath = TextureUtil.ResolveServeZstdCachePath(
                    imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                    qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength, IsSimulationTexturePath(imgPath));
                zstdExists = !string.IsNullOrEmpty(zstdPath);
            }
            catch { }

            string feInfo = fe != null ? ("fe=1 size=" + fe.Size + " ts=" + fe.LastWriteTime.ToFileTime()) : "fe=0";
            string cacheInfo = "cache=1 native=" + (nativeExists ? "1" : "0")
                + " zstd=" + (zstdExists ? "1" : "0")
                + " meta=" + (metaExists ? "1" : "0");
            LogUtil.Log("[VPB] [VaMLoad] " + kind + " | " + imgPath + " | " + feInfo + " | " + cacheInfo);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueThumbnailImmediate", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static bool PreQueueThumbnailImmediate(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return true;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            try
            {
                if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                {
                    LogImageRequestDetails("thumb.immediate", qi);
                }
            }
            catch { }

            if (Settings.Instance == null || Settings.Instance.EnableZstdCompression == null) return true;
            if (!Settings.Instance.EnableZstdCompression.Value) return true;

            if (ImageLoadingMgr.singleton == null) return true;

            if (ImageLoadingMgr.singleton.Request(qi))
            {
                // Served from VPB cache: ensure VaM's thumbnail cache is populated.
                try
                {
                    var thumbCache = Traverse.Create(__instance).Field("thumbnailCache").GetValue() as Dictionary<string, Texture2D>;
                    if (thumbCache != null && qi.tex != null && !thumbCache.ContainsKey(qi.imgPath))
                    {
                        thumbCache.Add(qi.imgPath, qi.tex);
                    }
                }
                catch { }

                // Skip VaM threaded processing for this request.
                return false;
            }

            try
            {
                var tr = Traverse.Create(__instance);
                var q = tr.Field("queuedImages").GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                int qCount = q != null ? q.Count : 0;
                int realQ = 0;
                try { realQ = (int)tr.Field("numRealQueuedImages").GetValue(); } catch { }
                bool moved = false;
                LogImageQueueEvent("enqueue.thumb.immediate", qi, qCount, realQ, moved);
            }
            catch { }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueImage", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static bool PreQueueImage(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return true;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            // Robust SIM detection: do not rely on filename heuristics.
            // If this request's callback is VaM's sim-texture handler, register the exact path as SIM now.
            // This makes the rest of VPB treat it as readable and purge any corrupted .zvamcache before use.
            try
            {
                var cb = qi.callback;
                if (cb != null)
                {
                    var m = cb.Method;
                    string mName = m != null ? (m.Name ?? "") : "";
                    string tName = (m != null && m.DeclaringType != null) ? (m.DeclaringType.FullName ?? m.DeclaringType.Name ?? "") : "";

                    bool looksLikeSimCallback =
                        mName.Equals("OnSimTextureLoaded", StringComparison.Ordinal)
                        || mName.Equals("LoadSimTextureCallback", StringComparison.Ordinal)
                        || (tName.IndexOf("DAZSkinWrapMaterialOptions", StringComparison.Ordinal) >= 0
                            && mName.IndexOf("SimTexture", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (looksLikeSimCallback)
                    {
                        RegisterSimTexture(qi.imgPath);
                        if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 1)
                            LogUtil.Log($"[VPB SIM] Detected SIM request by callback: {qi.imgPath} | cb={tName}.{mName}");
                    }
                }
            }
            catch { }

            try
            {
                if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                {
                    LogImageRequestDetails("img", qi);
                }
            }
            catch { }

            if (Settings.Instance == null || Settings.Instance.EnableZstdCompression == null) return true;
            if (!Settings.Instance.EnableZstdCompression.Value) return true;

            if (ImageLoadingMgr.singleton == null) return true;

            if (qi.imgPath.EndsWith(".jpg")) qi.textureFormat = TextureFormat.RGB24;
            if (qi.imgPath.EndsWith(".png")) qi.textureFormat = TextureFormat.RGBA32;

            if (ImageLoadingMgr.singleton.Request(qi))
            {
                return false;
            }

            try { int lvl = Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0; if (lvl >= 1 && (qi.createAlphaFromGrayscale || (qi.imgPath != null && qi.imgPath.IndexOf("Alphamidpart", StringComparison.OrdinalIgnoreCase) >= 0))) LogUtil.Log("[VPB QUEUE] MISS path=" + qi.imgPath + " A=" + qi.createAlphaFromGrayscale); } catch { }

            try
            {
                var tr = Traverse.Create(__instance);
                var q = tr.Field("queuedImages").GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                int qCount = q != null ? q.Count : 0;
                int realQ = 0;
                try { realQ = (int)tr.Field("numRealQueuedImages").GetValue(); } catch { }
                bool moved = false;
                LogImageQueueEvent("enqueue.img", qi, qCount, realQ, moved);
            }
            catch { }

            if (ImageLoadingMgr.singleton != null)
            {
                ImageLoadingMgr.singleton.TrackCandidate(qi);
            }
            return true;
        }

        internal static void RequeueVaMImageLoad(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            try
            {
                qi.tex = null;
                // Skip cache on requeue: if cache invalidation failed (file locked / permissions),
                // VaM's loader would re-read the same corrupt cache, fail again, and bounce back here.
                try { qi.skipCache = true; } catch { }
                var loader = ImageLoaderThreaded.singleton;
                if (loader == null)
                {
                    LogUtil.LogWarning("[VPB] RequeueVaMImageLoad: ImageLoaderThreaded.singleton is null for " + qi.imgPath);
                    return;
                }
                loader.QueueImage(qi);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] RequeueVaMImageLoad failed for " + qi.imgPath + ": " + ex.Message);
            }
        }

        // It is added to cache before the callback, so we need to set skipCache one step earlier.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded.QueuedImage), "Finish")]
        public static void PostFinish_QueuedImage(ImageLoaderThreaded.QueuedImage __instance)
        {
            if (string.IsNullOrEmpty(__instance.imgPath) || __instance.imgPath == "NULL") return;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            if (Settings.Instance == null || Settings.Instance.EnableZstdCompression == null) return;
            if (!Settings.Instance.EnableZstdCompression.Value) return;



            // Ignore hub browse
            if (__instance.tex != null)
            {
                // Sim maps + DAZCharacterTextureControl maps: VaM needs CPU read (sim plugins / auto genital blend).
                if (NeedsCpuReadableTexture(__instance))
                {
                    try
                    {
                        bool alreadyPatched;
                        lock (registryLock)
                        {
                            alreadyPatched = simTexturePatchedThisLoad.Contains(__instance.imgPath);
                            if (!alreadyPatched) simTexturePatchedThisLoad.Add(__instance.imgPath);
                        }

                        // Avoid repeatedly running expensive conversion for the same asset path in one scene load.
                        if (!alreadyPatched)
                        {
                            var tex = __instance.tex as Texture2D;
                            if (tex != null && !IsTextureReadableCompat(tex))
                            {
                                string tag = IsCharacterTextureQueuedImage(__instance) ? "CHAR" : "SIM";
                                LogUtil.Log("[VPB " + tag + "] PostFinish: Fixing up non-readable texture: " + __instance.imgPath);

                                Texture2D readableTex = EnsureCpuReadableTexture(tex, __instance.linear, null);
                                if (readableTex != null && readableTex != tex)
                                {
                                    UnityEngine.Object.Destroy(tex);
                                    __instance.tex = readableTex;
                                    LogUtil.Log("[VPB " + tag + "] PostFinish: Fixed texture to be readable: " + __instance.imgPath);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] PostFinish: Failed to fix up readable texture " + __instance.imgPath + ": " + ex.Message);
                    }
                }

                if (ImageLoadingMgr.singleton != null)
                {
                    ImageLoadingMgr.singleton.ResolveInflightForQueuedImage(__instance);
                    ImageLoadingMgr.singleton.TryEnqueueResizeCache(__instance);
                }
            }

        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DAZMorph), "LoadDeltas")]
        public static void PostLoadDeltasFromBinaryFile(DAZMorph __instance)
        {
            var path = __instance.deltasLoadPath;
            if (string.IsNullOrEmpty(path)) return;
            if (__instance.deltasLoaded) return;
            __instance.deltasLoaded = true;

            if (DAZMorphMgr.singleton.cache.ContainsKey(path))
            {
                LogUtil.Log("LoadDeltas use cache:" + path);
                __instance.deltas = DAZMorphMgr.singleton.cache[path];
                return;
            }

            using (var fileEntryStream = MVR.FileManagement.FileManager.OpenStream(path, true))
            {
                using (BinaryReader binaryReader = new BinaryReader(fileEntryStream.Stream))
                {
                    var numDeltas = binaryReader.ReadInt32();
                    var deltas = new DAZMorphVertex[numDeltas];
                    Vector3 delta = default(Vector3);
                    for (int i = 0; i < numDeltas; i++)
                    {
                        DAZMorphVertex dAZMorphVertex = new DAZMorphVertex();
                        dAZMorphVertex.vertex = binaryReader.ReadInt32();
                        delta.x = binaryReader.ReadSingle();
                        delta.y = binaryReader.ReadSingle();
                        delta.z = binaryReader.ReadSingle();
                        dAZMorphVertex.delta = delta;
                        deltas[i] = dAZMorphVertex;
                    }

                    __instance.deltas = deltas;
                    DAZMorphMgr.singleton.cache.Add(path, deltas);
                }
            }
        }

    }

    class PatchAssetLoader
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.AssetLoader), "QueueLoadAssetBundleFromFile")]
        static bool QueueLoadAssetBundleFromFile(MeshVR.AssetLoader.AssetBundleFromFileRequest abffr)
        {
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.AssetLoader), "QueueLoadSceneIntoTransform")]
        static bool QueueLoadSceneIntoTransform(MeshVR.AssetLoader.SceneLoadIntoTransformRequest slr)
        {
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.AssetLoader), "DoneWithAssetBundleFromFile")]
        static bool DoneWithAssetBundleFromFile(string path)
        {
            return true;
        }

        // --- Scan Whitelist Patches ---

        /// <summary>
        /// Blocks VaM from registering non-whitelisted packages during its startup scan.
        /// PREFIX patch so VaM never opens the .var zip or reads the manifest for excluded
        /// packages — the expensive I/O is skipped entirely, not just cleaned up afterward.
        /// On-demand registration (via VamOnDemandLoader) bypasses this via s_AllowRegistration.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "RegisterPackage")]
        public static bool PreRegisterPackageScanFilter(string __0)
        {
            try
            {
                if (VamOnDemandLoader.s_AllowRegistration) return true;
                if (string.IsNullOrEmpty(__0)) return true;

                string norm = __0.Replace('\\', '/');
                if (!norm.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)) return true;

                if (!ScanWhitelistManager.Instance.IsEnabled) return true;

                string uid = System.IO.Path.GetFileNameWithoutExtension(norm);
                if (ScanWhitelistManager.Instance.IsUidOverrideIncluded(uid))
                {
                    VamScanFilter.RecordScanAllowed();
                    return true;
                }

                bool allowed = ScanWhitelistManager.Instance.IsPathWhitelisted(norm);
                if (allowed) VamScanFilter.RecordScanAllowed();
                else VamScanFilter.RecordScanBlocked();
                return allowed;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB ScanFilter] PreRegisterPackageScanFilter error: " + ex.Message);
                return true; // fail open
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static void PreRefreshResetScanCounters()
        {
            if (VamStartupOptimizations.IsBootstrapNativeRefreshSkipArmed()) return;
            VamScanFilter.MarkVamRefreshBegin();
            VamScanFilter.ResetScanCounters();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static void PostRefreshLogScanResult()
        {
            if (VamStartupOptimizations.IsNativeRefreshInvocationSkipped())
            {
                VamStartupOptimizations.ClearNativeRefreshInvocationSkipped();
                return;
            }
            try
            {
                if (!LogUtil.IsStartupReadyLogged() && !LogUtil.IsReadyLogged())
                    VamStartupOptimizations.NoteVpbInitNativeRefreshDone();
            }
            catch { }
            VamScanFilter.MarkVamRefreshed();
            VamScanFilter.LogScanResult();
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static Exception FinalizeRefresh(Exception __exception)
        {
            if (VamStartupOptimizations.IsNativeRefreshInvocationSkipped())
                return __exception;
            VamScanFilter.MarkVamRefreshEnd();
            return __exception;
        }

        /// <summary>
        /// After VaM's GetVarFileEntry returns null for a scan-excluded package,
        /// register the package on-demand in VaM's FileManager and retry.
        /// This ensures MVRScript plugins can still load dependencies from
        /// non-whitelisted packages without requiring a full scan.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "GetVarFileEntry", new Type[] { typeof(string) })]
        public static void PostGetVarFileEntryOnDemand(string path, ref MVR.FileManagement.VarFileEntry __result)
        {
            try
            {
                if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.GetVarEntryHook++;
                if (__result != null) return;
                if (!ScanWhitelistManager.Instance.IsEnabled) return;
                if (VamOnDemandLoader.s_InOnDemand) return;

                string uid = VamOnDemandLoader.UidFromEntryPath(path);
                if (string.IsNullOrEmpty(uid)) return;
                LogUtil.RecordVarEntryMiss();

                if (VamOnDemandLoader.ShouldDeferStartupOnDemandForPath(path, uid))
                    return;

                if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.GetVarEntryHookHeavy++;
                LogUtil.RecordOnDemandRetry();
                VamOnDemandLoader.TryRegisterPackageOnDemandForEntryPath(path);
                VamOnDemandLoader.s_InOnDemand = true;
                try
                {
                    __result = MVR.FileManagement.FileManager.GetVarFileEntry(path);
                    if (__result != null) return;

                    // Some VaM call sites pass *.latest:/... and do not resolve aliases
                    // after registration. Retry with a concrete UID path when possible.
                    string rewritten = VamOnDemandLoader.TryRewriteLatestEntryPath(path, attemptRegister: true);
                    if (!string.IsNullOrEmpty(rewritten) && !string.Equals(rewritten, path, StringComparison.OrdinalIgnoreCase))
                    {
                        LogUtil.RecordOnDemandRetry();
                        __result = MVR.FileManagement.FileManager.GetVarFileEntry(rewritten);
                    }

                    // Also handle versioned UIDs where the requested version doesn't exist.
                    string rewrittenBest = VamOnDemandLoader.TryRewriteBestAvailableEntryPath(path, attemptRegister: true);
                    if (__result == null && !string.IsNullOrEmpty(rewrittenBest) && !string.Equals(rewrittenBest, path, StringComparison.OrdinalIgnoreCase))
                    {
                        LogUtil.RecordOnDemandRetry();
                        __result = MVR.FileManagement.FileManager.GetVarFileEntry(rewrittenBest);
                    }
                }
                finally
                {
                    VamOnDemandLoader.s_InOnDemand = false;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB OnDemand] PostGetVarFileEntryOnDemand error: " + ex.Message);
            }
        }
    }

}
