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
            if (string.Equals(clothingMode, "mergeoutfit", StringComparison.OrdinalIgnoreCase)) return "Merge Outfit";
            if (string.Equals(clothingMode, "clothingonly", StringComparison.OrdinalIgnoreCase)) return "Steal Clothing";
            return "Keep Clothing";
        }

        private void ApplyAppearanceMode(string clothingMode, Action<string> action)
        {
            if (string.IsNullOrEmpty(clothingMode)) clothingMode = "keep";
            _lastAppearanceClothingMode = clothingMode;
            if (VPBConfig.Instance != null)
            {
                if (clothingMode == "keep") VPBConfig.Instance.AppearanceClothingApplyMode = "keep";
                else if (clothingMode == "replace") VPBConfig.Instance.AppearanceClothingApplyMode = "replace";
                else if (string.Equals(clothingMode, "clothingonly", StringComparison.OrdinalIgnoreCase))
                    VPBConfig.Instance.AppearanceClothingApplyMode = "clothingonly";
                else if (string.Equals(clothingMode, "mergeoutfit", StringComparison.OrdinalIgnoreCase))
                    VPBConfig.Instance.AppearanceClothingApplyMode = "mergeoutfit";
                try { VPBConfig.Instance.Save(false); } catch { }
            }
            action(clothingMode);
        }

        private void AddAppearanceOptions(List<ContextMenuPanel.Option> options, Action<string> handler)
        {
            string mode = string.IsNullOrEmpty(_lastAppearanceClothingMode) ? "keep" : _lastAppearanceClothingMode;
            string lastLabel = "Last Used (" + GetAppearanceModeLabel(mode) + ")";

            // isSubMenu so the click handler skips Hide(); a later PushPage won't re-activate a hidden canvas.
            options.Add(new ContextMenuPanel.Option(lastLabel, () => {
                ApplyAppearanceMode(mode, handler);
            }, false, true));

            options.Add(new ContextMenuPanel.Option("Keep Clothing", () => {
                ApplyAppearanceMode("keep", handler);
            }, false, true));

            options.Add(new ContextMenuPanel.Option("Replace Clothing", () => {
                ApplyAppearanceMode("replace", handler);
            }, false, true));

            options.Add(new ContextMenuPanel.Option("Merge Clothing", () => {
                ApplyAppearanceMode("merge", handler);
            }, false, true));

            options.Add(new ContextMenuPanel.Option("Merge Outfit", () => {
                ApplyAppearanceMode("mergeoutfit", handler);
            }, false, true));

            options.Add(new ContextMenuPanel.Option("Steal Clothing", () => {
                ApplyAppearanceMode("clothingonly", handler);
            }, false, true));
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
                            ApplyImport(node.AsObject, targetAtom, category, clothingMode, entry);
                        }
                    }));
                }
            }

            if (personNodes.Count == 1 && targetAtom != null)
            {
                ApplyImport(personNodes[0], targetAtom, category, clothingMode, entry);
                yield break;
            }

            ContextMenuPanel.Instance.PushPage("Select Source Person", atomOptions);
        }

        private void ApplyImport(JSONClass sourceAtomNode, Atom targetAtom, string category, string clothingMode, FileEntry sourceEntry)
        {
            JSONClass preset = VpbImport.WrapAtomNodeAsPreset(sourceAtomNode);

            // Translate category string to VpbResourceType
            VpbResourceType resourceType;
            if (category == "Appearance")
            {
                resourceType = VpbResourceType.Appearance;
            }
            else if (category == "Clothing")
            {
                resourceType = VpbResourceType.Clothing;
            }
            else
            {
                LogUtil.LogWarning($"[VPB] ApplyImport: unrecognized category '{category}'; aborting.");
                return;
            }

            // Translate clothingMode string to ClothingApplyMode
            ClothingApplyMode applyMode;
            if (clothingMode == "keep")
            {
                applyMode = ClothingApplyMode.Keep;
            }
            else if (clothingMode == "replace")
            {
                applyMode = ClothingApplyMode.Replace;
            }
            else if (clothingMode == "merge")
            {
                applyMode = ClothingApplyMode.Merge;
            }
            else if (string.Equals(clothingMode, "mergeoutfit", StringComparison.OrdinalIgnoreCase))
            {
                applyMode = ClothingApplyMode.MergeOutfit;
            }
            else if (string.Equals(clothingMode, "clothingonly", StringComparison.OrdinalIgnoreCase))
            {
                applyMode = ClothingApplyMode.ClothingOnly;
            }
            else
            {
                // Null or unrecognized defaults to Replace
                applyMode = ClothingApplyMode.Replace;
            }

            if (applyMode == ClothingApplyMode.MergeOutfit && resourceType == VpbResourceType.Appearance)
            {
                ContextMenuPanel.Instance.Hide();
                if (Panel != null)
                {
                    // Picker pushes its own undo on confirm; do not snapshot here (cancel must not undo).
                    Panel.ShowMergeOutfitPicker(sourceEntry, targetAtom, preset);
                    return;
                }
                LogUtil.LogWarning("[VPB] Merge Outfit: no gallery panel for picker; merging all clothing items.");
            }

            try
            {
                Gallery.SuppressAutoRefresh(true);

                if (Messager.singleton != null)
                {
                    Messager.singleton.StartCoroutine(UI.DisableSuppressionAfterDelay(2f));
                }
                else
                {
                    Gallery.SuppressAutoRefresh(false);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] ApplyImport UI suppression error: {ex.Message}");
                Gallery.SuppressAutoRefresh(false);
            }

            // Context-menu "import with options" previously never pushed undo — footer Undo was a no-op.
            if (Panel != null)
                Panel.PushUndoAtomSnapshot(targetAtom);

            VpbImport.LoadPreset(sourceEntry, targetAtom, resourceType, applyMode, presetJC: preset);

            ContextMenuPanel.Instance.Hide();
        }
    }

}
