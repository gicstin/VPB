using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class GalleryInfoActionTab : GalleryActionTabBase
    {
        public GalleryInfoActionTab(GalleryActionsPanel parent, GameObject container) : base(parent, container) { }

        public override void RefreshUI(List<FileEntry> selectedFiles, Hub.GalleryHubItem selectedHubItem)
        {
            ClearUI();

            if (selectedHubItem != null)
            {
                DisplayHubInfo(selectedHubItem);
            }
            else if (selectedFiles != null && selectedFiles.Count > 0)
            {
                if (selectedFiles.Count == 1)
                {
                    DisplayFileInfo(selectedFiles[0]);
                }
                else
                {
                    DisplayMultiFileInfo(selectedFiles);
                }
            }
            else
            {
                CreateLabel("Select an item to view information", 16, Color.gray);
            }
        }

        private void DisplayHubInfo(Hub.GalleryHubItem item)
        {
            CreateLabel("HUB RESOURCE", 14, Color.yellow);
            CreateLabel($"Title: {item.Title}", 18, Color.white);
            CreateLabel($"Author: {item.Creator}", 16, Color.white);
            CreateLabel($"Category: {item.Category}", 14, Color.gray);
            CreateLabel($"Downloads: {item.DownloadCount:N0}", 14, Color.gray);
            CreateLabel($"Rating: {item.Rating:F1} / 5.0", 14, Color.gray);
            CreateLabel($"Resource ID: {item.ResourceId}", 12, Color.cyan);
        }

        private void DisplayFileInfo(FileEntry file)
        {
            CreateLabel("LOCAL FILE", 14, Color.green);
            CreateLabel($"Name: {file.Name}", 18, Color.white);
            CreateLabel($"Path: {file.Path}", 12, Color.gray);
            
            string sizeStr = FormatBytes(file.Size);
            CreateLabel($"Size: {sizeStr}", 14, Color.white);
            CreateLabel($"Modified: {file.LastWriteTime:yyyy-MM-dd HH:mm}", 14, Color.white);
            
            if (file.Uid.Contains(".var:"))
            {
                string[] parts = file.Uid.Split(':');
                if (parts.Length >= 2)
                {
                    CreateLabel($"Package: {parts[0]}", 12, Color.cyan);
                }
            }
        }

        private void DisplayMultiFileInfo(List<FileEntry> files)
        {
            CreateLabel("MULTIPLE SELECTION", 14, Color.yellow);
            CreateLabel($"Items Selected: {files.Count}", 18, Color.white);
            
            long totalSize = files.Sum(f => f.Size);
            CreateLabel($"Total Size: {FormatBytes(totalSize)}", 16, Color.white);
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
            t.horizontalOverflow = HorizontalWrapMode.Wrap;

            LayoutElement le = labelGO.AddComponent<LayoutElement>();
            le.preferredHeight = -1; // Let it auto-size height if wrapping
            le.minHeight = fontSize + 4;
            le.flexibleWidth = 1;

            uiElements.Add(labelGO);
        }

        private string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return String.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }
    }
}
