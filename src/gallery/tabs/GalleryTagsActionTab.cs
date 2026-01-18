using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class GalleryTagsActionTab : GalleryActionTabBase
    {
        public GalleryTagsActionTab(GalleryActionsPanel parent, GameObject container) : base(parent, container) { }

        public override void RefreshUI(List<FileEntry> selectedFiles, Hub.GalleryHubItem selectedHubItem)
        {
            ClearUI();

            List<string> uids = new List<string>();
            if (selectedHubItem != null) uids.Add(selectedHubItem.ResourceId);
            else if (selectedFiles != null) uids.AddRange(selectedFiles.Select(f => f.Uid));

            if (uids.Count == 0)
            {
                CreateLabel("Select one or more items to manage tags");
                return;
            }

            string firstUid = uids[0];
            bool isMulti = uids.Count > 1;

            if (isMulti)
            {
                CreateLabel($"Selected Items: {uids.Count}", 16, Color.yellow);
            }

            // Current Tags Section
            CreateLabel("Current Tags:");
            HashSet<string> tags = TagsManager.Instance.GetTags(firstUid);
            
            if (tags.Count == 0)
            {
                CreateLabel("  (None)", 14, Color.gray);
            }
            else
            {
                foreach (string tag in tags.OrderBy(t => t))
                {
                    string currentTag = tag;
                    CreateTagItem(currentTag, () => {
                        foreach (var uid in uids) TagsManager.Instance.RemoveTag(uid, currentTag);
                        NotifyTagsChanged();
                        RefreshUI(selectedFiles, selectedHubItem);
                    });
                }
            }

            // Add Tag Section
            CreateLabel("\nAdd Tag:");
            string placeholder = isMulti ? "Add tag to ALL selected..." : "Enter tag name...";
            GameObject inputGO = UI.CreateTextInput(containerGO, 340, 40, placeholder, 18, 0, 0, AnchorPresets.middleCenter, (val) => {
                if (!string.IsNullOrEmpty(val))
                {
                    foreach (var uid in uids) TagsManager.Instance.AddTag(uid, val);
                    NotifyTagsChanged();
                    RefreshUI(selectedFiles, selectedHubItem);
                }
            });
            LayoutElement inputLE = inputGO.AddComponent<LayoutElement>();
            inputLE.preferredHeight = 40;
            inputLE.flexibleWidth = 1;
            uiElements.Add(inputGO);

            // Quick Tags / All Tags Section
            List<string> allUserTags = TagsManager.Instance.GetAllUserTags();
            if (allUserTags.Count > 0)
            {
                CreateLabel("\nAll Tags:");
                foreach (string tag in allUserTags.OrderBy(t => t))
                {
                    if (tags.Contains(tag)) continue;
                    string currentTag = tag;
                    CreateActionButton(0, tag, (dragger) => {
                        foreach (var uid in uids) TagsManager.Instance.AddTag(uid, currentTag);
                        NotifyTagsChanged();
                        RefreshUI(selectedFiles, selectedHubItem);
                    }, selectedFiles?.FirstOrDefault(), selectedHubItem);
                }
            }
        }

        private void NotifyTagsChanged()
        {
            if (parentPanel != null && parentPanel.ParentPanel != null)
            {
                parentPanel.ParentPanel.InvalidateTags();
                parentPanel.ParentPanel.RefreshFiles();
            }
        }

        private void CreateLabel(string text, int fontSize = 18, Color? color = null)
        {
            GameObject labelGO = new GameObject("Label_" + text);
            labelGO.transform.SetParent(containerGO.transform, false);
            Text t = labelGO.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = color ?? Color.white;
            t.alignment = TextAnchor.MiddleLeft;

            LayoutElement le = labelGO.AddComponent<LayoutElement>();
            le.preferredHeight = fontSize + 10;
            le.flexibleWidth = 1;

            uiElements.Add(labelGO);
        }

        private void CreateTagItem(string tag, Action onRemove)
        {
            GameObject row = new GameObject("TagRow_" + tag);
            row.transform.SetParent(containerGO.transform, false);
            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.spacing = 5;

            LayoutElement rowLE = row.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 30;
            rowLE.flexibleWidth = 1;

            GameObject tagTextGO = new GameObject("TagText");
            tagTextGO.transform.SetParent(row.transform, false);
            Text t = tagTextGO.AddComponent<Text>();
            t.text = "• " + tag;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 16;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            
            GameObject removeBtnGO = UI.CreateUIButton(row, 60, 25, "Remove", 12, 0, 0, AnchorPresets.middleRight, () => onRemove());
            uiElements.Add(removeBtnGO);
            uiElements.Add(row);
        }
    }
}
