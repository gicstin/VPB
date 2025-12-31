using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace var_browser
{
    public class Gallery : MonoBehaviour
    {
        public static Gallery singleton;

        public struct Category
        {
            public string name;
            public string extension;
            public string path;
        }

        private List<Category> categories = new List<Category>();
        
        // Panels management
        private List<GalleryPanel> panels = new List<GalleryPanel>();
        private GalleryPanel mainPanel;

        // IsVisible property checks if ANY panel is visible
        public bool IsVisible 
        {
            get 
            {
                return panels.Any(p => p.IsVisible);
            }
        }

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
            if (p == mainPanel) mainPanel = null;
        }

        public void Init()
        {
            if (mainPanel != null) return;

            // Create Main Panel
            GameObject go = new GameObject("GalleryPanel_Main");
            mainPanel = go.AddComponent<GalleryPanel>();
            mainPanel.SetCategories(categories);
            mainPanel.Init(false); // Main panel is not undocked
        }

        public void SetCategories(List<Category> cats)
        {
            categories = cats;
            if (mainPanel != null)
            {
                mainPanel.SetCategories(categories);
            }
        }

        public void UndockCreator(string creatorName)
        {
            var existing = panels.FirstOrDefault(pan => pan.IsUndocked && pan.UndockedCreator == creatorName);
            if (existing != null)
            {
                // What to show? Default to first category
                if (categories.Count > 0)
                    existing.Show(categories[0].name, categories[0].extension, categories[0].path);
                return;
            }

            GameObject go = new GameObject("GalleryPanel_Creator_" + creatorName);
            GalleryPanel p = go.AddComponent<GalleryPanel>();
            p.UndockedCreator = creatorName;
            
            p.Init(true);
            
            // For undocked creator panel, we give it ALL categories
            p.SetCategories(categories);
            
            // Show default category
            if (categories.Count > 0)
                p.Show(categories[0].name, categories[0].extension, categories[0].path);
        }

        // Called by Main Panel to undock a category
        public void Undock(Category cat)
        {
            var existing = panels.FirstOrDefault(pan => pan.IsUndocked && pan.UndockedCategory?.name == cat.name);
            if (existing != null)
            {
                existing.Show(cat.name, cat.extension, cat.path);
                return;
            }

            GameObject go = new GameObject("GalleryPanel_" + cat.name);
            GalleryPanel p = go.AddComponent<GalleryPanel>();
            p.UndockedCategory = cat;
            
            p.Init(true);
            
            // For undocked panel, we only give it the one category
            p.SetCategories(new List<Category> { cat });
            
            p.Show(cat.name, cat.extension, cat.path);
        }

        public void Show(string title, string extension, string path)
        {
            if (mainPanel == null) Init();
            
            // Show main panel
            mainPanel.Show(title, extension, path);
            
            // Show all undocked panels too (restore session)
            foreach(var p in panels)
            {
                if (p != mainPanel && !p.IsVisible)
                {
                    if (p.IsUndocked)
                    {
                        if (p.UndockedCategory.HasValue)
                        {
                            var c = p.UndockedCategory.Value;
                            p.Show(c.name, c.extension, c.path);
                        }
                        else if (!string.IsNullOrEmpty(p.UndockedCreator) && categories.Count > 0)
                        {
                             p.Show(categories[0].name, categories[0].extension, categories[0].path);
                        }
                    }
                }
            }
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
