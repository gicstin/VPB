using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SimpleJSON;

namespace VPB
{
    public class TagsManager
    {
        private static TagsManager _instance;
        public static TagsManager Instance => _instance ?? (_instance = new TagsManager());

        private readonly object userTagsLock = new object();
        private Dictionary<string, HashSet<string>> userTags = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private string tagsPath => Path.Combine(GlobalInfo.PluginInfoDirectory, "UserTags.json");

        private TagsManager()
        {
            Load();
        }

        public void Load()
        {
            lock (userTagsLock)
            {
                userTags.Clear();
                if (File.Exists(tagsPath))
                {
                    try
                    {
                        string json = File.ReadAllText(tagsPath);
                        JSONClass root = JSON.Parse(json).AsObject;
                        if (root != null)
                        {
                            foreach (string key in root.Keys)
                            {
                                JSONArray tagsArray = root[key].AsArray;
                                HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (JSONNode node in tagsArray)
                                {
                                    tags.Add(node.Value);
                                }
                                userTags[key] = tags;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Failed to load UserTags: " + ex);
                    }
                }
            }
        }

        public void Save()
        {
            try
            {
                JSONClass root = new JSONClass();
                lock (userTagsLock)
                {
                    foreach (var kvp in userTags)
                    {
                        if (kvp.Value.Count == 0) continue;
                        JSONArray tagsArray = new JSONArray();
                        foreach (var tag in kvp.Value)
                        {
                            tagsArray.Add(tag);
                        }
                        root[kvp.Key] = tagsArray;
                    }
                }

                if (!Directory.Exists(GlobalInfo.PluginInfoDirectory))
                    Directory.CreateDirectory(GlobalInfo.PluginInfoDirectory);

                File.WriteAllText(tagsPath, root.ToString());
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Failed to save UserTags: " + ex);
            }
        }

        public HashSet<string> GetTags(string uid)
        {
            lock (userTagsLock)
            {
                if (userTags.TryGetValue(uid, out HashSet<string> tags))
                    return new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void AddTag(string uid, string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            bool added = false;
            lock (userTagsLock)
            {
                if (!userTags.ContainsKey(uid))
                    userTags[uid] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                added = userTags[uid].Add(tag);
            }
            if (added) Save();
        }

        public void RemoveTag(string uid, string tag)
        {
            bool changed = false;
            lock (userTagsLock)
            {
                if (userTags.TryGetValue(uid, out HashSet<string> tags))
                {
                    if (tags.Remove(tag))
                    {
                        if (tags.Count == 0) userTags.Remove(uid);
                        changed = true;
                    }
                }
            }
            if (changed) Save();
        }
        
        public void ToggleTag(string uid, string tag)
        {
            if (HasTag(uid, tag)) RemoveTag(uid, tag);
            else AddTag(uid, tag);
        }

        public bool HasTag(string uid, string tag)
        {
            lock (userTagsLock)
            {
                if (userTags.TryGetValue(uid, out HashSet<string> tags))
                    return tags.Contains(tag);
                return false;
            }
        }

        public List<string> GetAllUserTags()
        {
            HashSet<string> allTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (userTagsLock)
            {
                foreach (var tags in userTags.Values)
                {
                    foreach (var tag in tags) allTags.Add(tag);
                }
            }
            return new List<string>(allTags);
        }
    }
}
