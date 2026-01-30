using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MVR.FileManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SimpleJSON;

namespace VPB
{
    public partial class UIDraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private void ShowImportCategories(FileEntry entry, Atom targetAtom)
        {
            List<ContextMenuPanel.Option> options = new List<ContextMenuPanel.Option>();

            options.Add(new ContextMenuPanel.Option("Clothing", () => {
                ShowSourceAtomsForImport(entry, targetAtom, "Clothing", "merge");
            }, false, true));

            options.Add(new ContextMenuPanel.Option("Appearance", () => {
                ShowAppearanceClothingModes(entry, targetAtom);
            }, false, true));

            ContextMenuPanel.Instance.PushPage("Import Category", options);
        }

        private string GetAppearanceModeLabel(string clothingMode)
        {
            if (clothingMode == "replace") return "Replace Clothing";
            if (clothingMode == "merge") return "Merge Clothing";
            return "Keep Existing Clothing";
        }

        private void ApplyAppearanceMode(string clothingMode, Action<string> action)
        {
            if (string.IsNullOrEmpty(clothingMode)) clothingMode = "keep";
            _lastAppearanceClothingMode = clothingMode;
            action(clothingMode);
        }

        private void AddAppearanceOptions(List<ContextMenuPanel.Option> options, Action<string> handler)
        {
            string mode = string.IsNullOrEmpty(_lastAppearanceClothingMode) ? "keep" : _lastAppearanceClothingMode;
            string lastLabel = "Last Used (" + GetAppearanceModeLabel(mode) + ")";

            options.Add(new ContextMenuPanel.Option(lastLabel, () => {
                ApplyAppearanceMode(mode, handler);
            }));

            options.Add(new ContextMenuPanel.Option("Keep Existing Clothing", () => {
                ApplyAppearanceMode("keep", handler);
            }));

            options.Add(new ContextMenuPanel.Option("Replace Clothing", () => {
                ApplyAppearanceMode("replace", handler);
            }));

            options.Add(new ContextMenuPanel.Option("Merge Clothing", () => {
                ApplyAppearanceMode("merge", handler);
            }));
        }

        private void ShowAppearanceClothingModes(FileEntry entry, Atom targetAtom)
        {
            List<ContextMenuPanel.Option> options = new List<ContextMenuPanel.Option>();
            AddAppearanceOptions(options, mode => ShowSourceAtomsForImport(entry, targetAtom, "Appearance", mode));
            ContextMenuPanel.Instance.PushPage("Appearance Options", options);
        }

        private void ShowSourceAtomsForImport(FileEntry entry, Atom targetAtom, string category, string clothingMode)
        {
            if (ContextMenuPanel.Instance != null)
            {
                ContextMenuPanel.Instance.StartCoroutine(ParseSceneAndShowAtoms(entry, targetAtom, category, clothingMode));
            }
        }

        private System.Collections.IEnumerator ParseSceneAndShowAtoms(FileEntry entry, Atom targetAtom, string category, string clothingMode)
        {
            string content = null;
            using (var reader = entry.OpenStreamReader())
            {
                content = reader.ReadToEnd();
            }

            if (string.IsNullOrEmpty(content)) yield break;

            JSONNode root = JSON.Parse(content);
            if (root == null) yield break;

            JSONArray atoms = root["atoms"].AsArray;
            List<ContextMenuPanel.Option> atomOptions = new List<ContextMenuPanel.Option>();
            List<JSONClass> personNodes = new List<JSONClass>();

            foreach (JSONNode node in atoms)
            {
                if (node["type"].Value == "Person")
                {
                    personNodes.Add(node.AsObject);
                    string atomId = node["id"].Value;

                    atomOptions.Add(new ContextMenuPanel.Option(atomId, () => {
                        if (targetAtom == null)
                        {
                            LogUtil.LogError("No target atom selected for import.");
                        }
                        else
                        {
                            ApplyImport(node.AsObject, targetAtom, category, clothingMode, entry.Path);
                        }
                    }));
                }
            }

            if (personNodes.Count == 1 && targetAtom != null)
            {
                ApplyImport(personNodes[0], targetAtom, category, clothingMode, entry.Path);
                yield break;
            }

            ContextMenuPanel.Instance.PushPage("Select Source Person", atomOptions);
        }

        private void ApplyImport(JSONClass sourceAtomNode, Atom targetAtom, string category, string clothingMode, string path = null)
        {
            JSONClass preset = new JSONClass();
            JSONArray storables = new JSONArray();
            preset["storables"] = storables;

            JSONArray sourceStorables = sourceAtomNode["storables"].AsArray;

            foreach (JSONNode snode in sourceStorables)
            {
                string id = snode["id"].Value;
                string url = snode["url"] != null ? snode["url"].Value : "";

                bool include = false;

                bool isAnimation = id.EndsWith("Animation", StringComparison.OrdinalIgnoreCase) && snode["steps"] != null;
                bool isPlugin = id.IndexOf("plugin#", StringComparison.OrdinalIgnoreCase) >= 0 || id.Equals("PluginManager", StringComparison.OrdinalIgnoreCase);
                bool isClothing = id.StartsWith("clothing", StringComparison.OrdinalIgnoreCase) || id.StartsWith("wearable", StringComparison.OrdinalIgnoreCase) || url.IndexOf("/Clothing/", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isHair = id.StartsWith("hair", StringComparison.OrdinalIgnoreCase) || url.IndexOf("/Hair/", StringComparison.OrdinalIgnoreCase) >= 0;

                if (category == "Clothing")
                {
                    if (isClothing || isHair) include = true;
                }
                else if (category == "Appearance")
                {
                    if (!isAnimation && !isPlugin)
                    {
                        if (clothingMode == "keep")
                        {
                            if (!isClothing && !isHair) include = true;
                        }
                        else
                        {
                            include = true;
                        }
                    }
                }

                if (include) storables.Add(snode.AsObject);
            }

            string presetJson = preset.ToString();
            if (FileButton.EnsureInstalledByText(presetJson))
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh();
            }

            bool appliedViaPresetManager = false;

            if (category == "Appearance" && storables.Count > 0)
            {
                preset["setUnlistedParamsToDefault"].AsBool = true;

                JSONStorable presetStorable = targetAtom.GetStorableByID("AppearancePresets");
                MeshVR.PresetManager presetManager = presetStorable != null ? presetStorable.GetComponentInChildren<MeshVR.PresetManager>() : null;

                if (presetManager != null)
                {
                    try
                    {
                        if (clothingMode == "replace")
                        {
                            JSONStorable geometry = targetAtom.GetStorableByID("geometry");
                            if (geometry != null)
                            {
                                foreach (var name in geometry.GetBoolParamNames())
                                {
                                    if (name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase) || name.StartsWith("hair:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        JSONStorableBool p = geometry.GetBoolJSONParam(name);
                                        if (p != null) p.val = false;
                                    }
                                }
                            }
                        }

                        targetAtom.SetLastRestoredData(preset, true, true);
                        LogUtil.Log($"[Import] Applying Appearance preset via PresetManager.LoadPresetFromJSON ({storables.Count} storables)");
                        
                        try
                        {
                            if (!string.IsNullOrEmpty(path)) MVR.FileManagement.FileManager.PushLoadDirFromFilePath(UI.NormalizePath(path));
                            presetManager.LoadPresetFromJSON(preset, false);
                        }
                        finally
                        {
                            if (!string.IsNullOrEmpty(path)) MVR.FileManagement.FileManager.PopLoadDir();
                        }

                        appliedViaPresetManager = true;
                        LogUtil.Log("[Import] Appearance preset application successful.");
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[Import] Appearance preset load failed: " + ex.Message);
                    }
                }
                else
                {
                    LogUtil.LogWarning("[Import] AppearancePresets storable or PresetManager missing on target atom. Falling back to direct storable restoration.");
                }
            }
            
            if (!appliedViaPresetManager)
            {
                LogUtil.Log($"[Import] Restoring {storables.Count} storables directly to atom {targetAtom.name}");
                int directApplied = 0;
                int directSkipped = 0;
                
                if (category == "Appearance" && clothingMode == "replace")
                {
                    JSONStorable geometry = targetAtom.GetStorableByID("geometry");
                    if (geometry != null)
                    {
                        foreach (var name in geometry.GetBoolParamNames())
                        {
                            if (name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase) || name.StartsWith("hair:", StringComparison.OrdinalIgnoreCase))
                            {
                                JSONStorableBool p = geometry.GetBoolJSONParam(name);
                                if (p != null) p.val = false;
                            }
                        }
                    }
                }

                foreach (JSONNode snode in storables)
                {
                    string id = snode["id"].Value;
                    JSONStorable storable = targetAtom.GetStorableByID(id);
                    if (storable != null)
                    {
                        storable.RestoreFromJSON(snode.AsObject);
                        directApplied++;
                    }
                    else
                    {
                        directSkipped++;
                    }
                }
                LogUtil.Log($"[Import] Direct restoration complete: {directApplied} applied, {directSkipped} skipped.");
            }

            ContextMenuPanel.Instance.Hide();
        }
    }

}
