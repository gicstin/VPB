using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class GalleryDependenciesActionTab : GalleryActionTabBase
    {
        public GalleryDependenciesActionTab(GalleryActionsPanel parent, GameObject container) : base(parent, container) { }

        public override void RefreshUI(List<FileEntry> selectedFiles, Hub.GalleryHubItem selectedHubItem)
        {
            ClearUI();

            if (selectedHubItem != null)
            {
                CreateLabel("HUB DEPENDENCIES", 14, Color.yellow);
                CreateLabel("Dependency information is available on the Hub website.", 14, Color.gray);
                CreateButton("View on Hub", () => Application.OpenURL("https://hub.virtamate.com/resources/" + selectedHubItem.ResourceId));
            }
            else if (selectedFiles != null && selectedFiles.Count > 0)
            {
                CreateLabel("LOCAL DEPENDENCIES", 14, Color.green);
                if (selectedFiles.Count == 1)
                {
                    FileEntry file = selectedFiles[0];
                    if (file is VarFileEntry vfe && vfe.Package != null)
                    {
                        VarPackage pkg = vfe.Package;
                        CreateLabel($"Package: {pkg.Name}", 18, Color.white);
                        CreateLabel($"Creator: {pkg.Creator}", 14, Color.white);
                        CreateLabel($"Version: {pkg.Version}", 14, Color.white);
                        
                        if (pkg.RecursivePackageDependencies != null && pkg.RecursivePackageDependencies.Count > 0)
                        {
                            CreateLabel($"\nDependencies ({pkg.RecursivePackageDependencies.Count}):", 14, Color.yellow);
                            foreach (var dep in pkg.RecursivePackageDependencies.Take(15))
                            {
                                CreateLabel("- " + dep, 12, Color.gray);
                            }
                            if (pkg.RecursivePackageDependencies.Count > 15)
                            {
                                CreateLabel($"+ {pkg.RecursivePackageDependencies.Count - 15} more...", 12, Color.gray);
                            }
                        }
                        else
                        {
                            CreateLabel("\nNo package dependencies found.", 14, Color.gray);
                        }
                    }
                    else if (file.Uid.Contains(".var:"))
                    {
                        CreateLabel($"File: {file.Name}", 14, Color.white);
                        CreateLabel("Deep dependency analysis for .var files is not yet implemented.", 12, Color.gray);
                    }
                    else
                    {
                        CreateLabel("Selected file is not a package.", 14, Color.gray);
                    }
                }
                else
                {
                    CreateLabel($"Multiple items selected ({selectedFiles.Count})", 14, Color.white);
                    CreateLabel("Bulk dependency analysis is not supported.", 12, Color.gray);
                }
            }
            else
            {
                CreateLabel("Select an item to view dependencies", 16, Color.gray);
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
