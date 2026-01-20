using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshVR;
using MVR;
using MVR.FileManagement;

namespace VPB
{
    public partial class GalleryPanel
    {
        private List<FileEntry> GetActiveSceneEntries()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LogUtil.Log($"[VPB] GetActiveSceneEntries category: {currentActiveItemCategory ?? "All"}");
            HashSet<FileEntry> entries = new HashSet<FileEntry>();
            string category = currentActiveItemCategory;

            var atoms = SuperController.singleton.GetAtoms();
            LogUtil.Log($"[VPB] Scanning {atoms.Count} atoms for active items...");

            // 1. Clothing & Hair
            if (category == "Clothing" || category == "Hair" || category == "All" || string.IsNullOrEmpty(category))
            {
                foreach (Atom atom in atoms)
                {
                    if (atom == null) continue;
                    if (atom.type == "Person")
                    {
                        DAZCharacterSelector dcs = atom.GetComponentInChildren<DAZCharacterSelector>();
                        if (dcs == null) continue;

                        if (category == "Clothing" || category == "All" || string.IsNullOrEmpty(category))
                        {
                            if (dcs.clothingItems != null)
                            {
                                foreach (var item in dcs.clothingItems)
                                {
                                    if (item == null || !item.active) continue;
                                    
                                    // DAZClothingItem properties: item.uid, item.name, item.internalId, item.containingVAMDir
                                    // Use reflection if direct access failed previously or just try multiple common patterns
                                    string uid = item.uid;
                                    string path = uid;
                                    
                                    if (!uid.Contains(":/") && !uid.Contains(":\\"))
                                    {
                                        // Try to construct path from components if uid is short
                                        string internalId = null;
                                        string containingVAMDir = null;
                                        
                                        var type = item.GetType();
                                        
                                        if (!_internalIdFieldCache.TryGetValue(type, out var fieldInternalId))
                                        {
                                            fieldInternalId = type.GetField("internalId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            _internalIdFieldCache[type] = fieldInternalId;
                                        }
                                        internalId = fieldInternalId?.GetValue(item) as string;

                                        if (!_vamDirFieldCache.TryGetValue(type, out var fieldVamDir))
                                        {
                                            fieldVamDir = type.GetField("containingVAMDir", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            _vamDirFieldCache[type] = fieldVamDir;
                                        }
                                        containingVAMDir = fieldVamDir?.GetValue(item) as string;
                                        
                                        if (string.IsNullOrEmpty(internalId))
                                        {
                                            if (!_itemPathFieldCache.TryGetValue(type, out var fieldItemPath))
                                            {
                                                fieldItemPath = type.GetField("itemPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                                _itemPathFieldCache[type] = fieldItemPath;
                                            }
                                            internalId = fieldItemPath?.GetValue(item) as string;
                                        }

                                        if (!string.IsNullOrEmpty(containingVAMDir) && !string.IsNullOrEmpty(internalId))
                                        {
                                            path = GetVaMPath(containingVAMDir, internalId);
                                        }
                                        else
                                        {
                                            // Fallback: Check if there's a storable with this name that has a URL
                                            JSONStorable st = atom.GetStorableByID(uid);
                                            if (st == null) st = atom.GetStorableByID("Clothing_" + uid);
                                            
                                            if (st != null)
                                            {
                                                path = st.GetStringJSONParam("url")?.val ?? st.GetStringJSONParam("path")?.val;
                                            }
                                        }
                                    }

                                    if (IsScenePath(path)) continue;

                                    LogUtil.Log($"[VPB] Detected active Clothing: {uid} -> resolved path: {path} on {atom.uid}");
                                    
                                    // Filter by extension for Clothing
                                    if (!path.EndsWith(".vam", StringComparison.OrdinalIgnoreCase) && 
                                        !path.EndsWith(".vap", StringComparison.OrdinalIgnoreCase) && 
                                        !path.EndsWith(".vmi", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    FileEntry fe = FileManager.GetFileEntry(path);
                                    if (fe != null) entries.Add(fe);
                                    else LogUtil.Log($"[VPB]   Warning: Could not find FileEntry for clothing path: {path}");
                                }
                            }
                        }

                        if (category == "Hair" || category == "All" || string.IsNullOrEmpty(category))
                        {
                            if (dcs.hairItems != null)
                            {
                                foreach (var item in dcs.hairItems)
                                {
                                    if (item == null || !item.active) continue;
                                    
                                    string uid = item.uid;
                                    string path = uid;

                                    if (!uid.Contains(":/") && !uid.Contains(":\\"))
                                    {
                                        string internalId = null;
                                        string containingVAMDir = null;
                                        
                                        var type = item.GetType();
                                        
                                        if (!_internalIdFieldCache.TryGetValue(type, out var fieldInternalId))
                                        {
                                            fieldInternalId = type.GetField("internalId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            _internalIdFieldCache[type] = fieldInternalId;
                                        }
                                        internalId = fieldInternalId?.GetValue(item) as string;

                                        if (!_vamDirFieldCache.TryGetValue(type, out var fieldVamDir))
                                        {
                                            fieldVamDir = type.GetField("containingVAMDir", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            _vamDirFieldCache[type] = fieldVamDir;
                                        }
                                        containingVAMDir = fieldVamDir?.GetValue(item) as string;
                                        
                                        if (string.IsNullOrEmpty(internalId))
                                        {
                                            if (!_itemPathFieldCache.TryGetValue(type, out var fieldItemPath))
                                            {
                                                fieldItemPath = type.GetField("itemPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                                _itemPathFieldCache[type] = fieldItemPath;
                                            }
                                            internalId = fieldItemPath?.GetValue(item) as string;
                                        }

                                        if (!string.IsNullOrEmpty(containingVAMDir) && !string.IsNullOrEmpty(internalId))
                                        {
                                            path = GetVaMPath(containingVAMDir, internalId);
                                        }
                                        else
                                        {
                                            JSONStorable st = atom.GetStorableByID(uid);
                                            if (st == null) st = atom.GetStorableByID("Hair_" + uid);
                                            
                                            if (st != null)
                                            {
                                                path = st.GetStringJSONParam("url")?.val ?? st.GetStringJSONParam("path")?.val;
                                            }
                                        }
                                    }

                                    if (IsScenePath(path)) continue;

                                    LogUtil.Log($"[VPB] Detected active Hair: {uid} -> resolved path: {path} on {atom.uid}");
                                    
                                    // Filter by extension for Hair
                                    if (!path.EndsWith(".vam", StringComparison.OrdinalIgnoreCase) && 
                                        !path.EndsWith(".vap", StringComparison.OrdinalIgnoreCase) && 
                                        !path.EndsWith(".vmi", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    FileEntry fe = FileManager.GetFileEntry(path);
                                    if (fe != null) entries.Add(fe);
                                    else LogUtil.Log($"[VPB]   Warning: Could not find FileEntry for hair path: {path}");
                                }
                            }
                        }
                    }
                }
            }

            // 2. Plugins
            if (category == "Plugins" || category == "All" || string.IsNullOrEmpty(category))
            {
                LogUtil.Log("[VPB] Scanning for active Plugins...");
                // Session plugins
                AddPluginsToEntries(SuperController.singleton.GetComponents<MVRScript>(), entries);

                // Atom plugins
                foreach (Atom atom in atoms)
                {
                    if (atom == null) continue;
                    AddPluginsToEntries(atom.GetComponentsInChildren<MVRScript>(), entries);
                }
            }

            // 3. Atoms (SubScenes/Atoms that are mapped to files)
            if (category == "Atoms" || category == "Audio" || category == "All" || string.IsNullOrEmpty(category))
            {
                 LogUtil.Log("[VPB] Scanning for active Atoms/SubScenes/CUAs/Audio...");
                 foreach (Atom atom in atoms)
                 {
                     if (atom == null) continue;
                     
                     string path = null;
                     if (atom.type == "SubScene")
                     {
                         JSONStorable resort = atom.GetStorableByID("resort");
                         path = resort?.GetStringJSONParam("path")?.val;
                     }
                     
                     // Generic path/url check for atoms that reference files
                     if (string.IsNullOrEmpty(path)) path = atom.GetStringJSONParam("path")?.val;
                     if (string.IsNullOrEmpty(path)) path = atom.GetStringJSONParam("url")?.val;
                     if (string.IsNullOrEmpty(path)) path = atom.GetStringJSONParam("assetUrl")?.val;
                     
                     // CUA / SubScene / AudioSource fallback
                     if (string.IsNullOrEmpty(path))
                     {
                         // Optimization: Check "resort" storable first as many CUAs/SubScenes use it
                         JSONStorable resort = atom.GetStorableByID("resort");
                         if (resort != null)
                         {
                             path = resort.GetStringJSONParam("path")?.val;
                         }
                         
                         if (string.IsNullOrEmpty(path) && atom.type != "Person")
                         {
                             foreach (var storableID in atom.GetStorableIDs())
                             {
                                 // Skip common non-path storables to save time/resources
                                 if (storableID == "geometry" || storableID == "enabled" || storableID == "scale") continue;

                                 var st = atom.GetStorableByID(storableID);
                                 if (st == null) continue;
                                 
                                 // Try JSONStorableUrl
                                 var jurl = (object)st as JSONStorableUrl;
                                 if (jurl != null && !string.IsNullOrEmpty(jurl.val))
                                 {
                                     string val = jurl.val.ToLower();
                                     if (val.EndsWith(".assetbundle") || val.EndsWith(".unity3d") || val.EndsWith(".wav") || val.EndsWith(".mp3") || val.EndsWith(".ogg") || val.EndsWith(".vac") || val.EndsWith(".vmd"))
                                     {
                                         path = jurl.val;
                                         break;
                                     }
                                 }
                                 
                                 // Try JSONStorableString with common keys
                                 foreach (var paramName in new[] { "url", "path", "assetUrl", "clip" })
                                 {
                                     JSONStorableString jstr = st.GetStringJSONParam(paramName);
                                     if (jstr != null && !string.IsNullOrEmpty(jstr.val))
                                     {
                                         string val = jstr.val;
                                         string valLower = val.ToLower();
                                         if (valLower.Contains("/") || valLower.Contains("\\"))
                                         {
                                             if (valLower.EndsWith(".assetbundle") || valLower.EndsWith(".unity3d") || valLower.EndsWith(".wav") || valLower.EndsWith(".mp3") || valLower.EndsWith(".ogg") || valLower.EndsWith(".vac") || valLower.EndsWith(".vmd"))
                                             {
                                                 path = val;
                                                 break;
                                             }
                                         }
                                     }
                                 }
                                 if (!string.IsNullOrEmpty(path)) break;
                             }
                         }
                     }

                     if (!string.IsNullOrEmpty(path))
                     {
                         if (IsScenePath(path)) continue;

                         // Filter by category if specific
                         if (category == "Audio")
                         {
                             if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && 
                                 !path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && 
                                 !path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                             {
                                 continue;
                             }
                         }
                         else if (category == "Atoms")
                         {
                             if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || 
                                 path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || 
                                 path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                             {
                                 continue;
                             }
                         }

                         LogUtil.Log($"[VPB] Detected active Item ({atom.type}): {path} ({atom.uid})");
                         FileEntry fe = FileManager.GetFileEntry(path);
                         if (fe != null) entries.Add(fe);
                     }
                 }
            }

            // 4. Presets (Experimental detection)
            if (category == "Pose" || category == "Appearance" || category == "All" || string.IsNullOrEmpty(category))
            {
                LogUtil.Log("[VPB] Scanning for active Presets (Pose/Appearance)...");
                foreach (Atom atom in atoms)
                {
                    if (atom == null) continue;

                    var presetStorables = new Dictionary<string, string[]> {
                        { "Pose", new[] { "PosePresets" } },
                        { "Appearance", new[] { "AppearancePresets", "SkinPresets", "MorphPresets" } }
                    };

                    foreach (var kvp in presetStorables)
                    {
                        string presetCat = kvp.Key;
                        if (category != "All" && !string.IsNullOrEmpty(category) && category != presetCat) continue;

                        foreach (string sid in kvp.Value)
                        {
                            JSONStorable storable = atom.GetStorableByID(sid);
                            if (storable == null) continue;

                            // Check for common path parameters
                            foreach (string paramName in new[] { "lastPresetPath", "path", "url", "browsePath" })
                            {
                                string path = storable.GetStringJSONParam(paramName)?.val;
                                if (!string.IsNullOrEmpty(path) && (path.EndsWith(".json") || path.EndsWith(".vap") || path.EndsWith(".vmi")))
                                {
                                    if (IsScenePath(path)) continue;
                                    LogUtil.Log($"[VPB] Detected active {presetCat} Preset in {sid}: {path} on {atom.uid}");
                                    FileEntry fe = FileManager.GetFileEntry(path);
                                    if (fe != null) entries.Add(fe);
                                    break; 
                                }
                            }
                        }
                    }
                }
            }

            LogUtil.Log($"[VPB] GetActiveSceneEntries complete. Found {entries.Count} unique active entries. Took {sw.ElapsedMilliseconds}ms");
            return entries.OrderBy(e => e.Name).ToList();
        }

        private string GetVaMPath(string dir, string id)
        {
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(id)) return id;
            if (dir.EndsWith("/") || dir.EndsWith("\\")) return dir + id;
            return dir + "/" + id;
        }

        private bool IsScenePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string cleanPath = path.Replace("\\", "/").ToLower().Trim();
            
            // Any path ending with default.json is definitely a scene
            if (cleanPath.EndsWith("default.json")) return true;
            
            // Paths containing scene markers and ending in .json are likely scenes
            if (cleanPath.EndsWith(".json"))
            {
                if (cleanPath.Contains("/scene/") || 
                    cleanPath.StartsWith("scene/") || 
                    cleanPath.StartsWith("saves/") ||
                    cleanPath.Contains("/saves/scene/"))
                    return true;
            }
            
            return false;
        }

        private void AddPluginsToEntries(IEnumerable<MVRScript> scripts, HashSet<FileEntry> entries)
        {
            if (scripts == null) return;
            
            foreach (MVRScript script in scripts)
            {
                if (script == null) continue;
                
                string path = null;
                var scriptType = script.GetType();
                
                if (!_pathToScriptFieldCache.TryGetValue(scriptType, out var field))
                {
                    field = scriptType.GetField("pathToScript", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) 
                         ?? typeof(MVRScript).GetField("pathToScript", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    _pathToScriptFieldCache[scriptType] = field;
                }
                
                if (field != null) path = field.GetValue(script) as string;
                
                if (string.IsNullOrEmpty(path))
                {
                    if (!_pathToScriptPropCache.TryGetValue(scriptType, out var prop))
                    {
                         prop = scriptType.GetProperty("pathToScript", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                             ?? typeof(MVRScript).GetProperty("pathToScript", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                         _pathToScriptPropCache[scriptType] = prop;
                    }
                    if (prop != null) path = prop.GetValue(script, null) as string;
                }
                
                if (!string.IsNullOrEmpty(path))
                {
                    if (IsScenePath(path)) continue;
                    LogUtil.Log($"[VPB] Detected active Plugin: {path}");
                    FileEntry fe = FileManager.GetFileEntry(path);
                    if (fe != null) entries.Add(fe);
                    else LogUtil.Log($"[VPB]   Warning: Could not find FileEntry for plugin path: {path}");
                }
                else
                {
                    // Fallback: search JSONStorables in the script for path-like values
                    if (!_storableFieldsCache.TryGetValue(scriptType, out var fields))
                    {
                        fields = scriptType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        _storableFieldsCache[scriptType] = fields;
                    }
                    
                    foreach (var fieldInfo in fields)
                    {
                        string val = null;
                        if (fieldInfo.FieldType == typeof(JSONStorableUrl))
                        {
                            var jurl = (object)fieldInfo.GetValue(script) as JSONStorableUrl;
                            if (jurl != null) val = jurl.val;
                        }
                        else if (fieldInfo.FieldType == typeof(JSONStorableString))
                        {
                            var jstr = (object)fieldInfo.GetValue(script) as JSONStorableString;
                            if (jstr != null) val = jstr.val;
                        }
                        
                        if (!string.IsNullOrEmpty(val) && (val.Contains("/") || val.Contains("\\")) && (val.EndsWith(".cs") || val.EndsWith(".cslist")))
                        {
                            if (IsScenePath(val)) continue;
                            LogUtil.Log($"[VPB] Detected active Plugin via storable ({fieldInfo.Name}): {val}");
                            FileEntry fe = FileManager.GetFileEntry(val);
                            if (fe != null) entries.Add(fe);
                        }
                    }
                }
            }
        }
    }
}
