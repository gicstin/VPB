using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class GalleryPrimaryActionTab : GalleryActionTabBase
    {
        public GalleryPrimaryActionTab(GalleryActionsPanel parent, GameObject container) : base(parent, container)
        {
        }

        public override void RefreshUI(List<FileEntry> selectedFiles, Hub.GalleryHubItem selectedHubItem)
        {
            ClearUI();
            int buttonCount = 0;
            FileEntry SelectedFile = (selectedFiles != null && selectedFiles.Count > 0) ? selectedFiles[0] : null;

            if (selectedHubItem != null)
            {
                CreateActionButton(++buttonCount, "Download", (dragger) => LogUtil.Log("Downloading: " + selectedHubItem.Title), SelectedFile, selectedHubItem);
                CreateActionButton(++buttonCount, "View on HUB", (dragger) => Application.OpenURL("https://hub.virtamate.com/resources/" + selectedHubItem.ResourceId), SelectedFile, selectedHubItem);
                CreateActionButton(++buttonCount, "Install Dependencies*", (dragger) => {}, SelectedFile, selectedHubItem);
                CreateActionButton(++buttonCount, "Quick Look*", (dragger) => {}, SelectedFile, selectedHubItem);
            }
            else if (SelectedFile != null)
            {
                string pathLower = SelectedFile.Path.ToLowerInvariant();
                string category = parentPanel.ParentPanel.CurrentCategoryTitle ?? "";

                if (pathLower.EndsWith(".var"))
                {
                    CreateActionButton(++buttonCount, "Build Texture Cache", (dragger) => {
                        try
                        {
                            if (dragger != null && dragger.Panel != null && dragger.FileEntry != null)
                            {
                                NativeTextureOnDemandCache.TryBuildPackageCacheOnDemand(dragger.Panel, dragger.FileEntry.Path);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("Build Texture Cache failed: " + ex);
                        }
                    }, SelectedFile, selectedHubItem);
                }
                
                if (pathLower.Contains("/clothing/") || pathLower.Contains("\\clothing\\") || category.Contains("Clothing"))
                {
                    CreateActionButton(++buttonCount, "Load Clothing\nto Person", (dragger) => {
                        Atom target = parentPanel.GetBestTargetAtom();
                        if (target != null) dragger.LoadClothing(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    }, SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Remove All Clothing", (dragger) => {
                        Atom target = parentPanel.GetBestTargetAtom();
                        if (target != null) dragger.RemoveAllClothing(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    }, SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Set as Default*", (dragger) => {}, SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Quick load*", (dragger) => {}, SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Wear Selected*", (dragger) => {}, SelectedFile, selectedHubItem);
                }
                else if (pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\") || category.Contains("SubScene"))
                {
                    CreateActionButton(++buttonCount, "Load SubScene", (dragger) => dragger.LoadSubScene(SelectedFile.Uid), SelectedFile, selectedHubItem);
                }
                else if ((pathLower.EndsWith(".json") && (pathLower.Contains("/scene/") || pathLower.Contains("\\scene\\") || pathLower.Contains("saves/scene"))) || category.Contains("Scene"))
                {
                    CreateActionButton(++buttonCount, "Load Scene", (dragger) => dragger.LoadSceneFile(SelectedFile.Uid), SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Merge Scene", (dragger) => dragger.MergeSceneFile(SelectedFile.Uid, false), SelectedFile, selectedHubItem);
                }
                else if (pathLower.Contains("/hair/") || pathLower.Contains("\\hair\\") || category.Contains("Hair"))
                {
                    CreateActionButton(++buttonCount, "Load Hair", (dragger) => {
                        Atom target = parentPanel.GetBestTargetAtom();
                        if (target != null) dragger.LoadHair(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    }, SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Remove All Hair", (dragger) => {
                        Atom target = parentPanel.GetBestTargetAtom();
                        if (target != null) dragger.RemoveAllHair(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    }, SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Quick Hair*", (dragger) => {}, SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Wear Selected*", (dragger) => {}, SelectedFile, selectedHubItem);
                }
                else if (pathLower.Contains("/skin/") || pathLower.Contains("\\skin\\") || category.Contains("Skin"))
                {
                    CreateActionButton(++buttonCount, "Load Skin", (dragger) => {
                        Atom target = parentPanel.GetBestTargetAtom();
                        if (target != null) dragger.LoadSkin(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    }, SelectedFile, selectedHubItem);
                }
                else if (pathLower.Contains("/morphs/") || pathLower.Contains("\\morphs\\") || category.Contains("Morphs"))
                {
                    CreateActionButton(++buttonCount, "Load Morphs", (dragger) => {
                        Atom target = parentPanel.GetBestTargetAtom();
                        if (target != null) dragger.LoadMorphs(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    }, SelectedFile, selectedHubItem);
                }
                else if (pathLower.Contains("/appearance/") || pathLower.Contains("\\appearance\\") || category.Contains("Appearance"))
                {
                    CreateActionButton(++buttonCount, "Load Appearance", (dragger) => {
                        Atom target = parentPanel.GetBestTargetAtom();
                        if (target != null) dragger.LoadAppearance(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    }, SelectedFile, selectedHubItem);
                }
                else if (pathLower.Contains("/pose/") || pathLower.Contains("\\pose\\") || pathLower.Contains("/person/") || pathLower.Contains("\\person\\") || category.Contains("Pose"))
                {
                    CreateActionButton(++buttonCount, "Load Pose", (dragger) => {
                        Atom target = parentPanel.GetBestTargetAtom();
                        if (target != null) dragger.LoadPose(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    }, SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Mirror Pose", (dragger) => {
                        Atom target = parentPanel.GetBestTargetAtom();
                        if (target != null) dragger.MirrorPose(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    }, SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Load Pose (Silent)*", (dragger) => {}, SelectedFile, selectedHubItem);
                    CreateActionButton(++buttonCount, "Transition to Pose*", (dragger) => {}, SelectedFile, selectedHubItem);
                }
                else if (pathLower.Contains("/assets/") || pathLower.Contains("\\assets\\") || pathLower.EndsWith(".assetbundle") || pathLower.EndsWith(".unity3d"))
                {
                    CreateActionButton(++buttonCount, "Load Asset", (dragger) => {
                        Atom selected = SuperController.singleton.GetSelectedAtom();
                        if (selected != null && selected.type == "CustomUnityAsset") dragger.LoadCUAIntoAtom(selected, SelectedFile.Uid);
                        else dragger.LoadCUA(SelectedFile.Uid);
                    }, SelectedFile, selectedHubItem);
                }
                else
                {
                    CreateActionButton(++buttonCount, "Add to Scene", (dragger) => LogUtil.Log("Adding to scene: " + SelectedFile.Name), SelectedFile, selectedHubItem);
                }
            }
        }
    }
}
