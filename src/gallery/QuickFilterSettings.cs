using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    [Serializable]
    public class QuickFilterEntry
    {
        public string Name;
        public string CategoryPath;
        public string SearchText;
        public string Creator;
        public List<string> Tags = new List<string>();
        public SortState SortState;
        
        // Visual customization
        public Color ButtonColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        public Color TextColor = Color.white;

        public QuickFilterEntry() { }

        public JSONNode ToJSON()
        {
            var node = new JSONClass();
            node["Name"] = Name;
            node["CategoryPath"] = CategoryPath;
            node["SearchText"] = SearchText;
            node["Creator"] = Creator;
            
            var tagsArr = new JSONArray();
            foreach (var t in Tags) tagsArr.Add(t);
            node["Tags"] = tagsArr;

            if (SortState != null)
            {
                var sortNode = new JSONClass();
                sortNode["Type"].AsInt = (int)SortState.Type;
                sortNode["Direction"].AsInt = (int)SortState.Direction;
                node["SortState"] = sortNode;
            }

            // Colors
            node["ButtonColor"] = ColorToHex(ButtonColor);
            node["TextColor"] = ColorToHex(TextColor);

            return node;
        }

        public static QuickFilterEntry FromJSON(JSONNode node)
        {
            var entry = new QuickFilterEntry();
            entry.Name = node["Name"] ?? "New Filter";
            entry.CategoryPath = node["CategoryPath"] ?? "";
            entry.SearchText = node["SearchText"] ?? "";
            entry.Creator = node["Creator"] ?? "";

            var tagsArr = node["Tags"].AsArray;
            if (tagsArr != null)
            {
                foreach (JSONNode t in tagsArr) entry.Tags.Add(t);
            }

            var sortNode = node["SortState"];
            if (sortNode != null)
            {
                entry.SortState = new SortState(
                    (SortType)sortNode["Type"].AsInt,
                    (SortDirection)sortNode["Direction"].AsInt
                );
            }

            if (node["ButtonColor"] != null) entry.ButtonColor = HexToColor(node["ButtonColor"]);
            if (node["TextColor"] != null) entry.TextColor = HexToColor(node["TextColor"]);

            return entry;
        }

        public static string ColorToHex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGBA(c);
        }

        public static Color HexToColor(string hex)
        {
            Color c;
            if (ColorUtility.TryParseHtmlString(hex, out c)) return c;
            return Color.white;
        }
    }

    public class QuickFilterSettings
    {
        private static QuickFilterSettings _instance;
        public static QuickFilterSettings Instance
        {
            get
            {
                if (_instance == null) _instance = new QuickFilterSettings();
                return _instance;
            }
        }

        public List<QuickFilterEntry> Filters = new List<QuickFilterEntry>();
        private string filePath;

        public QuickFilterSettings()
        {
            string cacheDir = Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Cache"), "VPB");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            filePath = Path.Combine(cacheDir, "quick_filters.json");
            Load();
        }

        public void AddFilter(QuickFilterEntry entry)
        {
            Filters.Add(entry);
            Save();
        }

        public void RemoveFilter(QuickFilterEntry entry)
        {
            if (Filters.Contains(entry))
            {
                Filters.Remove(entry);
                Save();
            }
        }

        public void RenameFilter(QuickFilterEntry entry, string newName)
        {
            if (entry != null && !string.IsNullOrEmpty(newName))
            {
                entry.Name = newName;
                Save();
            }
        }
        
        public void MoveFilter(QuickFilterEntry entry, int direction)
        {
            int index = Filters.IndexOf(entry);
            if (index < 0) return;
            
            int newIndex = index + direction;
            if (newIndex >= 0 && newIndex < Filters.Count)
            {
                Filters.RemoveAt(index);
                Filters.Insert(newIndex, entry);
                Save();
            }
        }

        public void Load()
        {
            if (!File.Exists(filePath)) return;

            try
            {
                string json = File.ReadAllText(filePath);
                var root = JSON.Parse(json);
                var arr = root.AsArray;
                
                Filters.Clear();
                foreach (JSONNode node in arr)
                {
                    Filters.Add(QuickFilterEntry.FromJSON(node));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Failed to load quick filters: " + ex.Message);
            }
        }

        public void Save()
        {
            try
            {
                var arr = new JSONArray();
                foreach (var f in Filters) arr.Add(f.ToJSON());
                File.WriteAllText(filePath, arr.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Failed to save quick filters: " + ex.Message);
            }
        }
    }
}
