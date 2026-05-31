using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MVR.FileManagement;
using UnityEngine.Events;

namespace VPB
{
    /// <summary>
    /// VaM + VPB thumbnail caches and plugin save overwrite prompts.
    /// Must target VaM's <c>Assembly-CSharp</c> <see cref="FileManager"/> — not VPB's hook copy (separate static state).
    /// </summary>
    internal static class VpbSaveCacheSupport
    {
        private static readonly int[] ThumbnailScaleDenoms = { 1, 2, 4, 8 };

        private static readonly string[] PluginSaveWritePathsNoConfirm =
        {
            "Saves",
            "Saves/scene",
            "Saves\\scene",
            "Custom",
            "Custom\\Atom",
            "Custom/Atom",
        };

        private static Type _gameFileManagerType;
        private static MethodInfo _registerPluginSecureWritePath;
        private static MethodInfo _isPluginWritePathThatNeedsConfirm;

        private static Type GameFileManagerType
        {
            get
            {
                if (_gameFileManagerType != null) return _gameFileManagerType;
                try
                {
                    _gameFileManagerType = Type.GetType("MVR.FileManagement.FileManager, Assembly-CSharp", false);
                    if (_gameFileManagerType != null) return _gameFileManagerType;
                }
                catch { }

                try
                {
                    if (SuperController.singleton != null)
                    {
                        _gameFileManagerType = SuperController.singleton.GetType().Assembly.GetType("MVR.FileManagement.FileManager");
                        if (_gameFileManagerType != null) return _gameFileManagerType;
                    }
                }
                catch { }

                _gameFileManagerType = typeof(FileManager);
                return _gameFileManagerType;
            }
        }

        internal static void RegisterPluginSaveWritePathsNoConfirm()
        {
            try
            {
                const bool noConfirm = true;
                for (int i = 0; i < PluginSaveWritePathsNoConfirm.Length; i++)
                {
                    string p = PluginSaveWritePathsNoConfirm[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    InvokeGameFileManagerStatic("RegisterPluginSecureWritePath", p, noConfirm);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Failed to register plugin save write paths: " + ex.Message);
            }
        }

        /// <summary>Register the target file's parent folder on VaM's FileManager (covers nested Saves/scene/... paths).</summary>
        internal static void RegisterPluginSaveWritePathForFile(string filePath)
        {
            RegisterPluginSaveWritePathsNoConfirm();
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                string full = InvokeGameFileManagerGetFullPath(filePath);
                if (string.IsNullOrEmpty(full)) return;
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir))
                    InvokeGameFileManagerStatic("RegisterPluginSecureWritePath", dir, true);
            }
            catch { }
        }

        /// <summary>True when VaM will show its own plugin overwrite confirm for this path.</summary>
        internal static bool PluginSaveWillShowVaMOverwriteConfirm(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            try
            {
                MethodInfo mi = GetIsPluginWritePathThatNeedsConfirmMethod();
                if (mi == null) return true;
                object result = mi.Invoke(null, new object[] { path });
                return result is bool b && b;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// One overwrite prompt: VPB message when VaM is suppressed, else let VaM prompt on save.
        /// </summary>
        internal static void ConfirmOverwriteThenSave(string path, string vpbConfirmMessage, Action saveAction, Action cancelAction)
        {
            if (saveAction == null) return;
            RegisterPluginSaveWritePathForFile(path);

            if (PluginSaveWillShowVaMOverwriteConfirm(path))
            {
                saveAction();
                return;
            }

            if (SuperController.singleton == null)
            {
                saveAction();
                return;
            }

            try
            {
                string msg = string.IsNullOrEmpty(vpbConfirmMessage) ? ("Overwrite " + path + "?") : vpbConfirmMessage;
                UnityAction ok = () => saveAction();
                UnityAction cancel = () => { if (cancelAction != null) cancelAction(); };
                SuperController.singleton.Alert(msg, ok, cancel);
            }
            catch
            {
                saveAction();
            }
        }

        internal static void RegisterPluginSceneWritePathsNoConfirm()
        {
            RegisterPluginSaveWritePathsNoConfirm();
        }

        internal static void InvalidateAllThumbnailCaches(string savedAssetPath)
        {
            if (string.IsNullOrEmpty(savedAssetPath)) return;

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectThumbnailPathVariants(savedAssetPath, paths);

            foreach (string path in paths)
            {
                ClearInMemoryThumbnailCaches(path);
                try
                {
                    if (GalleryThumbnailCache.Instance != null)
                        GalleryThumbnailCache.Instance.InvalidateThumbnailForPath(path);
                }
                catch { }

                try
                {
                    if (SuperController.singleton != null && SuperController.singleton.mediaFileBrowserUI != null)
                        SuperController.singleton.mediaFileBrowserUI.ClearCacheImage(path);
                }
                catch { }
            }
        }

        internal static void NotifyGalleryPanelsInvalidateAfterSave(string savedAssetPath)
        {
            InvalidateAllThumbnailCaches(savedAssetPath);

            try
            {
                if (Gallery.singleton == null || Gallery.singleton.Panels == null) return;
                foreach (GalleryPanel panel in Gallery.singleton.Panels)
                {
                    if (panel == null) continue;
                    try { panel.OnGalleryAssetSavedInvalidateThumbnails(savedAssetPath); } catch { }
                }
            }
            catch { }
        }

        private static MethodInfo GetIsPluginWritePathThatNeedsConfirmMethod()
        {
            if (_isPluginWritePathThatNeedsConfirm != null) return _isPluginWritePathThatNeedsConfirm;
            _isPluginWritePathThatNeedsConfirm = GameFileManagerType.GetMethod(
                "IsPluginWritePathThatNeedsConfirm",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new Type[] { typeof(string) },
                null);
            return _isPluginWritePathThatNeedsConfirm;
        }

        private static MethodInfo GetRegisterPluginSecureWritePathMethod()
        {
            if (_registerPluginSecureWritePath != null) return _registerPluginSecureWritePath;
            _registerPluginSecureWritePath = GameFileManagerType.GetMethod(
                "RegisterPluginSecureWritePath",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new Type[] { typeof(string), typeof(bool) },
                null);
            return _registerPluginSecureWritePath;
        }

        private static void InvokeGameFileManagerStatic(string methodName, string path, bool doesNotNeedConfirm)
        {
            MethodInfo mi = string.Equals(methodName, "RegisterPluginSecureWritePath", StringComparison.Ordinal)
                ? GetRegisterPluginSecureWritePathMethod()
                : GameFileManagerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null) return;
            mi.Invoke(null, new object[] { path, doesNotNeedConfirm });
        }

        private static string InvokeGameFileManagerGetFullPath(string path)
        {
            MethodInfo mi = GameFileManagerType.GetMethod(
                "GetFullPath",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new Type[] { typeof(string) },
                null);
            if (mi == null)
            {
                try { return Path.GetFullPath(path); } catch { return path; }
            }
            object result = mi.Invoke(null, new object[] { path });
            return result as string;
        }

        private static void CollectThumbnailPathVariants(string path, HashSet<string> paths)
        {
            if (paths == null || string.IsNullOrEmpty(path)) return;

            void addRaw(string p)
            {
                if (string.IsNullOrEmpty(p)) return;
                paths.Add(p);
                paths.Add(p.Replace('\\', '/'));
                paths.Add(p.Replace('/', '\\'));
            }

            void addResolved(string p)
            {
                if (string.IsNullOrEmpty(p)) return;
                addRaw(p);

                try
                {
                    string full = InvokeGameFileManagerGetFullPath(p.Replace('\\', '/'));
                    if (!string.IsNullOrEmpty(full)) addRaw(full);
                }
                catch { }

                try
                {
                    string norm = FileManager.NormalizePath(p.Replace('\\', '/'));
                    if (!string.IsNullOrEmpty(norm)) addRaw(norm);
                }
                catch { }
            }

            addResolved(path);

            try
            {
                string norm = path.Replace('\\', '/');
                addResolved(Path.ChangeExtension(norm, ".jpg"));
                addResolved(Path.ChangeExtension(norm, ".png"));
                if (norm.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    addResolved(norm.Substring(0, norm.Length - 5) + ".jpg");
                if (norm.EndsWith(".vap", StringComparison.OrdinalIgnoreCase))
                    addResolved(norm.Substring(0, norm.Length - 4) + ".jpg");
                if (norm.EndsWith(".vac", StringComparison.OrdinalIgnoreCase))
                    addResolved(norm.Substring(0, norm.Length - 4) + ".jpg");
            }
            catch { }
        }

        private static void ClearInMemoryThumbnailCaches(string imgPath)
        {
            if (string.IsNullOrEmpty(imgPath)) return;

            try
            {
                if (CustomImageLoaderThreaded.singleton != null)
                {
                    for (int i = 0; i < ThumbnailScaleDenoms.Length; i++)
                    {
                        int d = ThumbnailScaleDenoms[i];
                        CustomImageLoaderThreaded.singleton.ClearCacheThumbnail(imgPath, d, false);
                        CustomImageLoaderThreaded.singleton.ClearCacheThumbnail(imgPath, d, true);
                    }
                }
            }
            catch { }

            try
            {
                if (ImageLoaderThreaded.singleton != null)
                    ImageLoaderThreaded.singleton.ClearCacheThumbnail(imgPath);
            }
            catch { }

            try
            {
                if (ImageLoadingMgr.singleton != null)
                    ImageLoadingMgr.singleton.InvalidateProcessedCachesForSourcePath(imgPath);
            }
            catch { }
        }
    }
}
