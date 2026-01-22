using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    public enum ActionUITabType
    {
        Primary,
        Tags,
        Info,
        Dependencies,
        Audio,
        Position
    }

    public abstract class GalleryActionTabBase
    {
        protected GalleryActionsPanel parentPanel;
        protected GameObject containerGO;
        protected List<GameObject> uiElements = new List<GameObject>();
        
        // Shortcut tracking for Primary tab (or others that might want it)
        protected List<UnityAction<UIDraggableItem>> activeActions = new List<UnityAction<UIDraggableItem>>();
        protected List<UIDraggableItem> activeDraggables = new List<UIDraggableItem>();

        public GalleryActionTabBase(GalleryActionsPanel parent, GameObject container)
        {
            this.parentPanel = parent;
            this.containerGO = container;
        }

        public virtual void OnOpen() { }
        public virtual void OnClose() 
        {
            ClearUI();
        }
        public virtual void Update() { }
        
        public abstract void RefreshUI(List<FileEntry> selectedFiles, Hub.GalleryHubItem selectedHubItem);

        public virtual void ClearUI()
        {
            foreach (var go in uiElements)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            uiElements.Clear();
            activeActions.Clear();
            activeDraggables.Clear();
        }

        public void ExecuteShortcut(int index)
        {
            if (index >= 0 && index < activeActions.Count)
            {
                try
                {
                    activeActions[index]?.Invoke(activeDraggables[index]);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error executing action shortcut: " + ex);
                }
            }
        }

        public virtual bool ExecuteAutoAction()
        {
            if (activeActions.Count > 0)
            {
                ExecuteShortcut(0);
                return true;
            }
            return false;
        }

        protected GameObject CreateActionButton(int number, string label, UnityAction<UIDraggableItem> action, FileEntry selectedFile, Hub.GalleryHubItem selectedHubItem)
        {
            string prefix = number <= 9 ? number + ". " : "";
            string fullLabel = prefix + label;
            
            GameObject btn = UI.CreateUIButton(containerGO, 340, 80, fullLabel, 20, 0, 0, AnchorPresets.middleCenter, () => {});
            uiElements.Add(btn);

            RectTransform btnRT = btn.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0, 1);
            btnRT.anchorMax = new Vector2(1, 1);
            btnRT.pivot = new Vector2(0.5f, 1);
            btnRT.sizeDelta = new Vector2(0, 80);

            LayoutElement btnLE = btn.GetComponent<LayoutElement>();
            if (btnLE == null) btnLE = btn.AddComponent<LayoutElement>();
            btnLE.preferredHeight = 80;
            btnLE.flexibleWidth = 1;
            
            // Interaction support
            UIDraggableItem draggable = btn.AddComponent<UIDraggableItem>();
            draggable.FileEntry = selectedFile;
            draggable.HubItem = selectedHubItem;
            draggable.Panel = parentPanel.ParentPanel;

            // Set the button action to call our delegate with the dragger
            btn.GetComponent<Button>().onClick.AddListener(() => action(draggable));
            
            // Store for keyboard shortcuts
            if (number <= 9)
            {
                activeActions.Add(action);
                activeDraggables.Add(draggable);
            }
            return btn;
        }

        protected GameObject CreateToggle(string label, bool initialValue, UnityAction<bool> onValueChanged)
        {
            GameObject toggleGO = UI.CreateUIToggle(containerGO, 340, 40, label, 16, 0, 0, AnchorPresets.middleCenter, onValueChanged);
            uiElements.Add(toggleGO);

            Toggle toggle = toggleGO.GetComponent<Toggle>();
            toggle.isOn = initialValue;

            RectTransform rt = toggleGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 40);

            LayoutElement le = toggleGO.AddComponent<LayoutElement>();
            le.preferredHeight = 40;
            le.flexibleWidth = 1;

            return toggleGO;
        }
    }
}
