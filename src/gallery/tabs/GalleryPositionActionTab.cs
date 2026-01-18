using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class GalleryPositionActionTab : GalleryActionTabBase
    {
        private float moveStep = 0.1f;
        private float rotateStep = 15f;

        public GalleryPositionActionTab(GalleryActionsPanel parent, GameObject container) : base(parent, container) { }

        public override void RefreshUI(List<FileEntry> selectedFiles, Hub.GalleryHubItem selectedHubItem)
        {
            ClearUI();

            Atom target = parentPanel.GetBestTargetAtom();
            if (target == null)
            {
                CreateLabel("No target atom selected.", 16, Color.red);
                CreateLabel("Please select a Person or Atom to use positioning tools.", 14, Color.white);
                return;
            }

            CreateLabel($"TARGET: {target.uid}", 14, Color.yellow);
            
            GameObject stepsRow = CreateRow();
            CreateSmallButton(stepsRow, "Step: 0.01m", () => SetMoveStep(0.01f), moveStep == 0.01f);
            CreateSmallButton(stepsRow, "Step: 0.1m", () => SetMoveStep(0.1f), moveStep == 0.1f);
            CreateSmallButton(stepsRow, "Step: 1.0m", () => SetMoveStep(1.0f), moveStep == 1.0f);

            CreateButton("Teleport to Camera", () => TeleportToCamera(target));
            
            GameObject resetRow = CreateRow();
            CreateSmallButton(resetRow, "Reset Rotation", () => ResetRotation(target));
            CreateSmallButton(resetRow, "Reset Position", () => ResetPosition(target));
            
            CreateLabel("\nRotation (Y-Axis):", 14, Color.gray);
            GameObject rotRow = CreateRow();
            CreateSmallButton(rotRow, $"-{rotateStep}°", () => RotateTarget(target, Vector3.up, -rotateStep));
            CreateSmallButton(rotRow, $"+{rotateStep}°", () => RotateTarget(target, Vector3.up, rotateStep));
            CreateSmallButton(rotRow, "Flip 180°", () => RotateTarget(target, Vector3.up, 180));

            CreateLabel($"\nOffsets (Current: {moveStep}m):", 14, Color.gray);
            Transform t = GetMainTransform(target);
            
            GameObject row = CreateRow();
            CreateSmallButton(row, "Forward", () => MoveTarget(target, t.forward * moveStep));
            CreateSmallButton(row, "Back", () => MoveTarget(target, -t.forward * moveStep));
            
            GameObject row2 = CreateRow();
            CreateSmallButton(row2, "Left", () => MoveTarget(target, -t.right * moveStep));
            CreateSmallButton(row2, "Right", () => MoveTarget(target, t.right * moveStep));

            GameObject row3 = CreateRow();
            CreateSmallButton(row3, "Up", () => MoveTarget(target, Vector3.up * moveStep));
            CreateSmallButton(row3, "Down", () => MoveTarget(target, Vector3.down * moveStep));
        }

        private void SetMoveStep(float step)
        {
            moveStep = step;
            parentPanel.UpdateUI();
        }

        private Transform GetMainTransform(Atom target)
        {
            return (target.mainController != null) ? target.mainController.transform : target.transform;
        }

        private void TeleportToCamera(Atom target)
        {
            if (Camera.main == null) return;
            Transform cam = Camera.main.transform;
            Transform t = GetMainTransform(target);
            t.position = cam.position + cam.forward * 2.0f;
            t.rotation = Quaternion.LookRotation(-cam.forward, Vector3.up);
        }

        private void ResetRotation(Atom target)
        {
            GetMainTransform(target).rotation = Quaternion.identity;
        }

        private void ResetPosition(Atom target)
        {
            GetMainTransform(target).position = Vector3.zero;
        }

        private void MoveTarget(Atom target, Vector3 offset)
        {
            GetMainTransform(target).position += offset;
        }

        private void RotateTarget(Atom target, Vector3 axis, float angle)
        {
            GetMainTransform(target).Rotate(axis, angle, Space.Self);
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

        private GameObject CreateRow()
        {
            GameObject row = new GameObject("Row");
            row.transform.SetParent(containerGO.transform, false);
            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.spacing = 5;

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 35;
            le.flexibleWidth = 1;
            
            uiElements.Add(row);
            return row;
        }

        private void CreateButton(string label, UnityEngine.Events.UnityAction action)
        {
            GameObject btn = UI.CreateUIButton(containerGO, 340, 40, label, 18, 0, 0, AnchorPresets.middleCenter, action);
            uiElements.Add(btn);
        }

        private void CreateSmallButton(GameObject parent, string label, UnityEngine.Events.UnityAction action, bool active = false)
        {
            GameObject btn = UI.CreateUIButton(parent, 0, 30, label, 14, 0, 0, AnchorPresets.middleCenter, action);
            if (active)
            {
                Image img = btn.GetComponent<Image>();
                if (img != null) img.color = new Color(0.15f, 0.45f, 0.6f, 1f);
            }
            uiElements.Add(btn);
        }
    }
}
