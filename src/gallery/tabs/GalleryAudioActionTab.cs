using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class GalleryAudioActionTab : GalleryActionTabBase
    {
        public GalleryAudioActionTab(GalleryActionsPanel parent, GameObject container) : base(parent, container) { }

        public override void RefreshUI(List<FileEntry> selectedFiles, Hub.GalleryHubItem selectedHubItem)
        {
            ClearUI();

            if (selectedHubItem != null)
            {
                CreateLabel("HUB PREVIEW", 14, Color.yellow);
                CreateLabel("Audio previews are not available for Hub items in this view.", 14, Color.gray);
            }
            else if (selectedFiles != null && selectedFiles.Count > 0)
            {
                CreateLabel("AUDIO ACTIONS", 14, Color.green);
                if (selectedFiles.Count == 1)
                {
                    FileEntry file = selectedFiles[0];
                    string pathLower = file.Path.ToLowerInvariant();
                    bool isAudio = pathLower.EndsWith(".mp3") || pathLower.EndsWith(".wav") || pathLower.EndsWith(".ogg");

                    if (isAudio)
                    {
                        CreateLabel($"File: {file.Name}", 14, Color.white);
                        CreateActionButton(1, "Play Preview", (dragger) => dragger.PlayAudioPreview(file.Path), file, selectedHubItem);
                        CreateActionButton(2, "Stop Preview", (dragger) => dragger.StopAudioPreview(), file, selectedHubItem);
                        CreateLabel("\n* Audio playback requires an InvisibleAudioSource or AudioSource atom in the scene.", 12, Color.gray);
                    }
                    else
                    {
                        CreateLabel("Selected file is not an audio resource.", 14, Color.gray);
                    }
                }
                else
                {
                    CreateLabel($"Multiple items selected ({selectedFiles.Count})", 14, Color.white);
                    CreateLabel("Bulk audio actions are not supported.", 12, Color.gray);
                }
            }
            else
            {
                CreateLabel("Select an audio file to view controls", 16, Color.gray);
            }
        }

        private void CreateLabel(string text, int fontSize = 16, Color? color = null)
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

        private void CreateButton(string label, UnityEngine.Events.UnityAction action)
        {
            GameObject btn = UI.CreateUIButton(containerGO, 340, 40, label, 18, 0, 0, AnchorPresets.middleCenter, action);
            uiElements.Add(btn);
        }
    }
}
