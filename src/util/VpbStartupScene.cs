using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace VPB
{
    internal static class VpbStartupScene
    {
        const float ResolveTimeoutSec = 90f;
        const float ResolveRetrySec = 0.25f;

        static bool s_loadScheduled;
        static bool s_matchReady;
        static string s_matchRaw;
        static string s_matchPath;
        static string s_matchPkgPrefix;

        internal static string GetPath()
        {
            try
            {
                if (Settings.Instance == null || Settings.Instance.StartupScenePath == null)
                    return "";
                return Normalize(Settings.Instance.StartupScenePath.Value);
            }
            catch
            {
                return "";
            }
        }

        internal static bool HasPath()
        {
            return !string.IsNullOrEmpty(GetPath());
        }

        internal static void SetPath(string path)
        {
            string normalized = Normalize(path);
            try
            {
                if (Settings.Instance == null || Settings.Instance.StartupScenePath == null)
                    return;
                Settings.Instance.StartupScenePath.Value = normalized;
                Settings.SaveConfig();
                InvalidateMatchCache();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] StartupScenePath save failed: " + ex.Message);
            }
        }

        static void InvalidateMatchCache()
        {
            s_matchReady = false;
            s_matchRaw = null;
            s_matchPath = null;
            s_matchPkgPrefix = null;
        }

        static void EnsureMatchCache()
        {
            string raw = "";
            try
            {
                if (Settings.Instance != null && Settings.Instance.StartupScenePath != null)
                    raw = Settings.Instance.StartupScenePath.Value ?? "";
            }
            catch { raw = ""; }

            if (s_matchReady && string.Equals(raw, s_matchRaw, StringComparison.Ordinal))
                return;

            s_matchRaw = raw;
            s_matchPath = Normalize(raw);
            s_matchPkgPrefix = ExtractPackagePrefix(s_matchPath);
            s_matchReady = true;
        }

        static string ExtractPackagePrefix(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int i = path.IndexOf(":/", StringComparison.Ordinal);
            if (i <= 1) return null;
            return path.Substring(0, i);
        }

        internal static bool CategoryLooksLikeScenes(string categoryTitle)
        {
            if (string.IsNullOrEmpty(categoryTitle)) return false;
            if (categoryTitle.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return categoryTitle.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Warm-path identity check for gallery rows. Exact uid/path, or scene-category
        /// package row whose package uid matches the stored scene's package prefix.
        /// </summary>
        internal static bool MatchesGalleryRow(FileEntry file, bool sceneCategoryLikely)
        {
            EnsureMatchCache();
            if (string.IsNullOrEmpty(s_matchPath) || file == null) return false;

            try
            {
                if (!string.IsNullOrEmpty(file.Uid)
                    && string.Equals(file.Uid, s_matchPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(file.Path)
                    && string.Equals(file.Path, s_matchPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            if (!sceneCategoryLikely || string.IsNullOrEmpty(s_matchPkgPrefix)) return false;

            string pkgUid = null;
            try
            {
                VarFileEntry vfe = file as VarFileEntry;
                if (vfe != null && vfe.Package != null)
                    pkgUid = vfe.Package.Uid;
            }
            catch { }
            if (string.IsNullOrEmpty(pkgUid))
            {
                try
                {
                    PackageListEntry ple = file as PackageListEntry;
                    if (ple != null && ple.Package != null)
                        pkgUid = ple.Package.Uid;
                }
                catch { }
            }
            if (string.IsNullOrEmpty(pkgUid)) return false;
            return string.Equals(pkgUid, s_matchPkgPrefix, StringComparison.OrdinalIgnoreCase);
        }

        internal static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string p = path.Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(p)) return "";
            try
            {
                string n = FileManager.NormalizePath(p);
                if (!string.IsNullOrEmpty(n))
                    p = n.Replace('\\', '/');
            }
            catch { }
            return p;
        }

        internal static bool PathsEqual(string a, string b)
        {
            string na = Normalize(a);
            string nb = Normalize(b);
            if (string.IsNullOrEmpty(na) || string.IsNullOrEmpty(nb)) return false;
            return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }

        internal static string PathFromFileEntry(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                if (!string.IsNullOrEmpty(file.Uid))
                    return Normalize(file.Uid);
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(file.Path))
                    return Normalize(file.Path);
            }
            catch { }
            return "";
        }

        internal static string DisplayNameFromPath(string path)
        {
            string p = Normalize(path);
            if (string.IsNullOrEmpty(p)) return "";
            int slash = p.LastIndexOf('/');
            string name = slash >= 0 && slash < p.Length - 1 ? p.Substring(slash + 1) : p;
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && name.Length > 5)
                name = name.Substring(0, name.Length - 5);
            return string.IsNullOrEmpty(name) ? p : name;
        }

        internal static bool TryScheduleLoad(SuperController sc)
        {
            if (s_loadScheduled) return HasPath();
            s_loadScheduled = true;
            if (!HasPath()) return false;

            string path = GetPath();
            MonoBehaviour runner = null;
            try { runner = Messager.singleton; } catch { }
            if (runner == null) runner = sc;
            if (runner == null) return false;

            try { runner.StartCoroutine(LoadWhenReady(sc, path)); }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Startup scene schedule failed: " + ex.Message);
                return false;
            }
            return true;
        }

        static IEnumerator LoadWhenReady(SuperController sc, string path)
        {
            while (!LogUtil.IsStartupReadyLogged())
                yield return null;

            if (string.IsNullOrEmpty(path)) yield break;

            try
            {
                if (sc != null) sc.DeactivateWorldUI();
            }
            catch { }

            string bannerName = DisplayNameFromPath(path);
            if (string.IsNullOrEmpty(bannerName)) bannerName = "startup scene";
            try { VpbProgressService.BeginSceneLoadPrep(bannerName); } catch { }

            float start = Time.realtimeSinceStartup;
            FileEntry fe = null;
            while (Time.realtimeSinceStartup - start < ResolveTimeoutSec)
            {
                bool userLoad = false;
                try { userLoad = LogUtil.IsSceneLoadActive() || LogUtil.IsSceneLoading(); }
                catch { }
                if (userLoad)
                {
                    try { VpbProgressService.EndSceneLoad(); } catch { }
                    yield break;
                }

                fe = TryResolveFileEntry(path);
                if (fe != null) break;

                float waitUntil = Time.realtimeSinceStartup + ResolveRetrySec;
                while (Time.realtimeSinceStartup < waitUntil)
                    yield return null;
            }

            if (fe == null)
            {
                try { VpbProgressService.EndSceneLoad(); } catch { }
                LogUtil.LogWarning("[VPB] Startup scene not found after wait: " + path);
                try { SceneLoadingUtils.LoadScene(path, false); }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Startup scene LoadScene fallback failed: " + ex.Message);
                }
                yield break;
            }

            try { VpbProgressService.EndSceneLoad(); } catch { }
            try { UI.LoadSceneFile(fe, null); }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Startup scene load failed: " + ex.Message);
            }
        }

        static FileEntry TryResolveFileEntry(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                FileEntry fe = FileManager.GetFileEntry(path);
                if (fe != null) return fe;
            }
            catch { }
            try
            {
                if (File.Exists(path))
                    return FileManager.GetFileEntry(path);
            }
            catch { }
            return null;
        }
    }
}
