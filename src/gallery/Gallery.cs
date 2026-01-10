using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VPB
{
    public class Gallery : MonoBehaviour
    {
        public static Gallery singleton;

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

        void Awake()
        {
            singleton = this;
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
                CreatePane();
                // CreatePane calls Show internally with default category, 
                // but we might want to override it with the requested path if it's specific?
                // Actually CreatePane uses 'categories[0]' by default.
                // If Show() is called with specific params, we should apply them to the new pane.
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

        public void CreatePane()
        {
            if (panels.Count >= MaxPanels)
            {
                // Optionally warn user?
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
                
                // If Locked rotation setting is checked, we might want to enforce it here, 
                // but GalleryPanel.Show handles it on first show if !hasBeenPositioned.
                // However, since we manually position it here, we might want to set hasBeenPositioned?
                // Actually, GalleryPanel.Init sets hasBeenPositioned = false.
                // If we position it here, we should probably set a flag or let Show handle it.
                // If we let Show handle it, Show will position it at 1.5f distance.
                // If we want 0.8f or "relative to viewer", we can leave it to Show if 1.5f is acceptable.
                // The user said "relative to viewer". Show's default is relative to viewer (head).
                // Let's rely on Show for consistency, unless we want to force it.
                // But Show only positions if !hasBeenPositioned.
                
                // Let's trust GalleryPanel.Show to handle initial positioning.
            }
            
            // Show default category
            if (categories.Count > 0)
                 p.Show(categories[0].name, categories[0].extension, categories[0].path);
        }

        public void Hide()
        {
            foreach(var p in panels)
            {
                p.Hide();
            }
        }
    }
}
