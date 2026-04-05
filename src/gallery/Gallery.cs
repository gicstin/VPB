using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Prime31.MessageKit;
using UnityEngine;

namespace VPB
{
    public class Gallery : MonoBehaviour
    {
        public static Gallery singleton;

        private DateTime lastObservedPackageRefreshTime = DateTime.MinValue;
        
        // Suppress auto-refresh when gallery is loading content (to preserve scroll position and state)
        private static bool suppressAutoRefresh = false;
        private static readonly object suppressLock = new object();
        public static void SuppressAutoRefresh(bool suppress) 
        { 
            lock (suppressLock) 
            { 
                suppressAutoRefresh = suppress; 
                if (suppress)
                {
                    LogUtil.Log("[VPB] Gallery auto-refresh SUPPRESSED");
                }
                else
                {
                    LogUtil.Log("[VPB] Gallery auto-refresh ENABLED");
                }
            } 
        }
        
        public static bool IsSuppressed()
        {
            lock (suppressLock)
            {
                return suppressAutoRefresh;
            }
        }

        public struct Category
        {
            public string name;
            public string extension;
            public string path;
            public List<string> paths;
        }

        private List<Category> categories = new List<Category>();
        
        // Panels management
        private List<GalleryPanel> panels = new List<GalleryPanel>();
        // private GalleryPanel mainPanel; // Removed

        // IsVisible property checks if ANY panel is visible
        public bool IsVisible 
        {
            get 
            {
                return panels.Any(p => p.IsVisible);
            }
        }

        public int PanelCount => panels.Count;
        public List<GalleryPanel> Panels => panels;

        private Coroutine autoRefreshCoroutine;
        private bool autoRefreshPending;

        void Awake()
        {
            singleton = this;
        }

        void OnEnable()
        {
            MessageKit.addObserver(MessageDef.FileManagerRefresh, OnFileManagerRefresh);
        }

        void OnDisable()
        {
            MessageKit.removeObserver(MessageDef.FileManagerRefresh, OnFileManagerRefresh);
        }

        private void OnFileManagerRefresh()
        {
            if (VPBConfig.Instance != null && VPBConfig.Instance.GalleryManualRefreshOnly)
            {
                LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh SKIPPED (manual refresh only)");
                return;
            }

            if (IsSuppressed())
            {
                LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh SKIPPED (suppressed)");
                return;
            }
            
            LogUtil.Log("[VPB] Gallery.OnFileManagerRefresh TRIGGERED");

            DateTime refreshTime = DateTime.MinValue;
            try { refreshTime = FileManager.lastPackageRefreshTime; } catch { }

            if (refreshTime <= lastObservedPackageRefreshTime) return;
            lastObservedPackageRefreshTime = refreshTime;

            if (autoRefreshCoroutine != null)
            {
                autoRefreshPending = true;
                return;
            }
            autoRefreshCoroutine = StartCoroutine(AutoRefreshAfterPackageScan());
        }

        private IEnumerator AutoRefreshAfterPackageScan()
        {
            yield return null;
            try
            {
                while (true)
                {
                    autoRefreshPending = false;

                    DateTime refreshTime = DateTime.MinValue;
                    try { refreshTime = FileManager.lastPackageRefreshTime; } catch { }

                    // Snapshot the delta lists so all panels see the same set of changes.
                    List<VarPackage> added = null;
                    List<VarPackage> removed = null;
                    try
                    {
                        added  = new List<VarPackage>(FileManager.lastAddedPackages);
                        removed = new List<VarPackage>(FileManager.lastRemovedPackages);
                    }
                    catch { }

                    foreach (var p in panels)
                    {
                        if (p == null) continue;
                        if (p.IsHubMode) continue;

                        bool changed = false;
                        try { changed = p.NotifyPackagesChanged(refreshTime); } catch { changed = true; }

                        if (!p.IsVisible) continue;
                        if (changed) p.ApplyPackageDelta(added, removed);
                    }

                    if (!autoRefreshPending) break;
                    yield return null;
                }
            }
            finally
            {
                autoRefreshCoroutine = null;
                autoRefreshPending = false;
            }
        }

        void OnDestroy()
        {
            // Panels clean themselves up usually, but we can ensure destruction
            foreach (var p in panels.ToList())
            {
                if (p != null && p.gameObject != null) Destroy(p.gameObject);
            }
            panels.Clear();
        }

        public void AddPanel(GalleryPanel p)
        {
            if (!panels.Contains(p)) panels.Add(p);
        }

        public void RemovePanel(GalleryPanel p)
        {
            if (panels.Contains(p)) panels.Remove(p);
            // if (p == mainPanel) mainPanel = null; // Removed
        }

        public void Init()
        {
            // VamHookPlugin calls this on hotkey if panels are hidden or empty.
            // We no longer automatically create a pane here to avoid ghosts.
        }

        public void SetCategories(List<Category> cats)
        {
            categories = cats;
            foreach (var p in panels)
            {
                p.SetCategories(categories);
            }
        }

        public const int MaxPanels = 20;

        public void ClonePanel(GalleryPanel original, bool toRight)
        {
            if (panels.Count >= MaxPanels)
            {
                // Optionally warn user?
                return;
            }

            GameObject go = new GameObject("GalleryPanel_Clone");
            GalleryPanel p = go.AddComponent<GalleryPanel>();
            
            p.Init();
            // Force floating mode for clones
            p.SetFixedLocally(false);
            
            p.SetCategories(original.categories);
            
            // Sync state
            p.SetFilters(original.GetCurrentPath(), original.GetCurrentExtension(), original.GetCurrentCreator());
            p.SetLeftActiveContent(original.GetLeftActiveContent());
            p.SetRightActiveContent(original.GetRightActiveContent());
            p.SetFollowMode(original.GetFollowMode());
            
            // Sync size
            RectTransform originalRT = original.GetBackgroundRT();
            RectTransform pRT = p.GetBackgroundRT();
            if (originalRT != null && pRT != null)
            {
                // If original is fixed, it has no sizeDelta (it's stretched in ScreenSpaceOverlay).
                // Clones are always floating, so use the default 1200x800 size for fixed-to-floating clones.
                if (original.isFixedLocally)
                    pRT.sizeDelta = new Vector2(1200, 800);
                else
                    pRT.sizeDelta = originalRT.sizeDelta;
            }

            // Sync position and rotation
            Camera cam = Camera.main;
            Transform camTrans = cam != null ? cam.transform : null;
            if (camTrans == null && SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null)
                camTrans = SuperController.singleton.centerCameraTarget.transform;

            if (camTrans != null)
            {
                Vector3 camPos = camTrans.position;
                Vector3 toOriginal;
                
                if (original.isFixedLocally)
                {
                    // Fixed panels are in ScreenSpaceOverlay. Place the floating clone directly 1.5m in front of the user.
                    // We don't use the "cloning principle" (offset) here because the source is screen-pinned, not world-placed.
                    toOriginal = camTrans.forward * 1.5f;
                    p.canvas.transform.position = camPos + toOriginal;
                    p.canvas.transform.rotation = Quaternion.LookRotation(toOriginal, Vector3.up);
                }
                else
                {
                    // For floating panels, use the standard cloning principle (place it to the side)
                    toOriginal = original.canvas.transform.position - camPos;
                    float radius = toOriginal.magnitude;
                    if (radius < 0.1f) radius = 0.1f;

                    float width = originalRT != null ? originalRT.sizeDelta.x * 0.001f : 1.2f;
                    float padding = 0.05f;
                    float angle = ((width + padding) / radius) * Mathf.Rad2Deg;
                    if (!toRight) angle = -angle;

                    Quaternion rot = Quaternion.AngleAxis(angle, Vector3.up);
                    Vector3 toNew = rot * toOriginal;

                    p.canvas.transform.position = camPos + toNew;
                    p.canvas.transform.rotation = Quaternion.LookRotation(toNew, Vector3.up);
                }
            }
            else
            {
                if (original.isFixedLocally)
                {
                    p.canvas.transform.rotation = original.canvas.transform.rotation;
                    p.canvas.transform.position = original.canvas.transform.position + new Vector3(-1.25f, 0, 0);
                }
                else
                {
                    p.canvas.transform.rotation = original.canvas.transform.rotation;
                    float width = originalRT != null ? originalRT.sizeDelta.x * 0.001f : 1.2f;
                    float padding = 0.05f;
                    Vector3 offset = original.canvas.transform.right * (width + padding);
                    if (!toRight) offset = -offset;
                    p.canvas.transform.position = original.canvas.transform.position + offset;
                }
            }
            
            p.hasBeenPositioned = true;
            p.Show(original.GetTitle(), original.GetCurrentExtension(), original.GetCurrentPath());
        }

        public void Show(string title, string extension, string path)
        {
            if (panels.Count == 0) 
            {
                // Create the panel without its internal Show() so we can call Show() exactly
                // once below with the caller's own title/extension/path.  This avoids the old
                // double-Show pattern (CreatePane→p.Show + Show again) that caused two content
                // loads, duplicate thumbnail coroutines and a scroll-position reset on startup.
                CreatePane(showAfterCreate: false);
                if (panels.Count > 0)
                    panels[0].Show(title, extension, path);
            }
            else
            {
                // Show ALL panes (restore session)
                foreach(var p in panels)
                {
                    if (!p.IsVisible)
                    {
                        // Restore state
                        if (!string.IsNullOrEmpty(p.GetCurrentPath()))
                        {
                             p.Show(p.GetTitle(), p.GetCurrentExtension(), p.GetCurrentPath());
                        }
                        else
                        {
                             // Fallback
                             p.Show(title, extension, path);
                        }
                    }
                }
            }
        }

        public void CreatePane(string forcedInitialCategory = null, bool showAfterCreate = true)
        {
            if (panels.Count >= MaxPanels)
            {
                return;
            }

            GameObject go = new GameObject("GalleryPanel_New");
            GalleryPanel p = go.AddComponent<GalleryPanel>();
            p.Init(); // Undocked
            
            p.SetCategories(categories);

            // Position relative to viewer
            if (SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null)
            {
                Transform cameraTransform = SuperController.singleton.centerCameraTarget.transform;
                p.canvas.transform.position = cameraTransform.position + cameraTransform.forward * 0.8f;
                p.canvas.transform.rotation = cameraTransform.rotation;
            }
            
            // Show initial category
            if (categories.Count > 0)
            {
                Gallery.Category initial = categories[0];

                if (!string.IsNullOrEmpty(forcedInitialCategory))
                {
                    // Caller specified a category (e.g. "Scenes" on startup) – use it, don't restore last viewed.
                    for (int i = 0; i < categories.Count; i++)
                    {
                        if (string.Equals(categories[i].name, forcedInitialCategory, StringComparison.OrdinalIgnoreCase))
                        {
                            initial = categories[i];
                            break;
                        }
                    }
                    LogUtil.Log("[Gallery] CreatePane forced category='" + initial.name + "'");
                }
                else
                {
                    // Restore last-viewed category from saved state.
                    try
                    {
                        string last = VPBConfig.ReadLastGalleryCategoryFromDisk();
                        if (string.IsNullOrEmpty(last) && VPBConfig.Instance != null)
                            last = VPBConfig.Instance.LastGalleryCategory;

                        if (!string.IsNullOrEmpty(last))
                        {
                            last = last.Trim();
                            if (last.StartsWith("Category ", StringComparison.OrdinalIgnoreCase))
                                last = last.Substring("Category ".Length);
                            else if (last.StartsWith("Category", StringComparison.OrdinalIgnoreCase) && last.Length > "Category".Length)
                                last = last.Substring("Category".Length);

                            if (last.StartsWith("Preset ", StringComparison.OrdinalIgnoreCase))
                                last = last.Substring("Preset ".Length);
                            else if (last.StartsWith("Preset", StringComparison.OrdinalIgnoreCase) && last.Length > "Preset".Length)
                                last = last.Substring("Preset".Length);

                            last = last.Trim();

                            if (string.Equals(last, "Scene", StringComparison.OrdinalIgnoreCase))
                                last = "Scenes";

                            for (int i = 0; i < categories.Count; i++)
                            {
                                if (string.Equals(categories[i].name, last, StringComparison.OrdinalIgnoreCase))
                                {
                                    initial = categories[i];
                                    break;
                                }
                            }

                            LogUtil.Log("[Gallery] CreatePane initial category='" + initial.name + "' (saved='" + last + "')");
                        }
                    }
                    catch { }
                }

                if (showAfterCreate)
                    p.Show(initial.name, initial.extension, initial.path);
            }
        }

        public void Hide()
        {
            foreach(var p in panels)
            {
                p.Hide();
            }
        }

        public void CloseAll()
        {
            foreach (var p in panels.ToList())
            {
                if (p == null) continue;
                p.Close();
            }
        }

        public void BringAllToFront()
        {
            Transform camTrans = Camera.main != null ? Camera.main.transform : null;
            if (camTrans == null && SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null)
                camTrans = SuperController.singleton.centerCameraTarget.transform;

            if (camTrans == null) return;

            float dist = 2.0f;
            if (VPBConfig.Instance != null) dist = VPBConfig.Instance.BringToFrontDistance;

            Vector3 basePos = camTrans.position + camTrans.forward * dist;
            Vector3 right = camTrans.right;

            int count = panels.Count;
            float spacing = 0.35f;
            float start = -(count - 1) * 0.5f * spacing;

            for (int i = 0; i < panels.Count; i++)
            {
                var p = panels[i];
                if (p == null || p.canvas == null) continue;
                if (p.isFixedLocally) continue;

                Vector3 pos = basePos + right * (start + i * spacing);
                p.canvas.transform.position = pos;
                p.canvas.transform.rotation = Quaternion.LookRotation(pos - camTrans.position, Vector3.up);

                try { p.ResetFollowOffsets(); } catch { }
            }

            // Bring Context Menu to front if it is currently open
            try
            {
                var ctxMenu = ContextMenuPanel.ExistingInstance;
                if (ctxMenu != null && ctxMenu.gameObject.activeSelf)
                {
                    Transform ctxCanvas = ctxMenu.transform.Find("Canvas");
                    if (ctxCanvas != null && ctxCanvas.gameObject.activeSelf)
                    {
                        Vector3 contextPos = basePos + right * (start + panels.Count * spacing);
                        ctxMenu.transform.position = contextPos;
                        ctxMenu.transform.rotation = Quaternion.LookRotation(contextPos - camTrans.position, Vector3.up);
                    }
                }
            }
            catch { }
        }
    }
}
