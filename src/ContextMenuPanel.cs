using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    public class ContextMenuPanel : MonoBehaviour
    {
        public struct Option
        {
            public string Label;
            public UnityAction Action;
            public bool IsLarge;
            public bool IsSubMenu; // New: Adds ">" visual

            public Option(string label, UnityAction action, bool isLarge = false, bool isSubMenu = false)
            {
                Label = label;
                Action = action;
                IsLarge = isLarge;
                IsSubMenu = isSubMenu;
            }
        }

        private class Page
        {
            public string Title;
            public List<Option> Options;
        }

        private static ContextMenuPanel _instance;
        private Stack<Page> pageStack = new Stack<Page>();
        
        // UI Elements for Header
        private GameObject headerGO;
        private Text headerText;
        private Button backButton;
        
        public static ContextMenuPanel Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("VPB_ContextMenu");
                    _instance = go.AddComponent<ContextMenuPanel>();
                    // Don't destroy on load if needed, but for now just keep it simple
                }
                return _instance;
            }
        }

        private GameObject canvasGO;
        private RectTransform panelRT;
        private List<GameObject> buttonPool = new List<GameObject>();

        void Awake()
        {
            // Create Canvas
            canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(transform, false);
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<GraphicRaycaster>();
            
            RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
            canvasRT.localScale = new Vector3(0.001f, 0.001f, 0.001f); // VR friendly scale
            
            // Create Panel
            GameObject panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            panelRT = panelGO.AddComponent<RectTransform>();
            // Size will be controlled by ContentSizeFitter
            
            Image bg = panelGO.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            
            VerticalLayoutGroup vlg = panelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 10;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            
            ContentSizeFitter csf = panelGO.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            // Create Header
            CreateHeader(panelGO);
        }
        
        private void CreateHeader(GameObject parent)
        {
            headerGO = new GameObject("Header");
            headerGO.transform.SetParent(parent.transform, false);
            LayoutElement le = headerGO.AddComponent<LayoutElement>();
            le.minHeight = 50;
            le.preferredHeight = 50;
            le.minWidth = 400;
            
            HorizontalLayoutGroup hlg = headerGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = false;
            hlg.childForceExpandWidth = false;
            hlg.padding = new RectOffset(5, 5, 5, 5);
            
            // Back Button
            GameObject backBtnGO = new GameObject("BackBtn");
            backBtnGO.transform.SetParent(headerGO.transform, false);
            Image backImg = backBtnGO.AddComponent<Image>();
            backImg.color = new Color(0.4f, 0.4f, 0.4f);
            backButton = backBtnGO.AddComponent<Button>();
            backButton.onClick.AddListener(PopPage);
            
            LayoutElement backLE = backBtnGO.AddComponent<LayoutElement>();
            backLE.minWidth = 80;
            backLE.preferredWidth = 80;
            
            Text backText = CreateText(backBtnGO, "< Back", 20);
            
            // Title
            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(headerGO.transform, false);
            LayoutElement titleLE = titleGO.AddComponent<LayoutElement>();
            titleLE.minWidth = 300;
            titleLE.flexibleWidth = 1;
            
            headerText = CreateText(titleGO, "Menu", 24);
            headerText.alignment = TextAnchor.MiddleCenter;
        }
        
        private Text CreateText(GameObject parent, string content, int size)
        {
            GameObject tGO = new GameObject("Text");
            tGO.transform.SetParent(parent.transform, false);
            RectTransform rt = tGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            
            Text t = tGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = content;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            return t;
        }

        public void Hide()
        {
            if (canvasGO != null) canvasGO.SetActive(false);
        }

        public void Show(Vector3 position, List<Option> options)
        {
            // Reset stack
            pageStack.Clear();
            PushPage("Menu", options); // Initial page
            
            transform.position = position;
            if (canvasGO != null) canvasGO.SetActive(true);
            
            // Face camera
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 lookPos = transform.position - cam.transform.position;
                lookPos.y = 0; // Keep vertical? Or face directly?
                if (lookPos != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(lookPos);
            }
            
            // Ensure main object is active
            gameObject.SetActive(true);
        }
        
        public void PushPage(string title, List<Option> options)
        {
            Page p = new Page { Title = title, Options = options };
            pageStack.Push(p);
            RefreshUI();
        }
        
        public void PopPage()
        {
            if (pageStack.Count > 1)
            {
                pageStack.Pop();
                RefreshUI();
            }
            else
            {
                Hide();
            }
        }
        
        private void RefreshUI()
        {
            if (pageStack.Count == 0) return;
            Page currentPage = pageStack.Peek();
            
            headerText.text = currentPage.Title;
            backButton.gameObject.SetActive(pageStack.Count > 1);
            
            RenderOptions(currentPage.Options);
        }

        private void RenderOptions(List<Option> options)
        {
            // Clear old buttons
            foreach (var btn in buttonPool) btn.SetActive(false);

            // Create new buttons
            for (int i = 0; i < options.Count; i++)
            {
                GameObject btnGO;
                if (i < buttonPool.Count)
                {
                    btnGO = buttonPool[i];
                    btnGO.SetActive(true);
                }
                else
                {
                    btnGO = CreateButton();
                    buttonPool.Add(btnGO);
                }
                
                SetupButton(btnGO, options[i]);
            }
            
            // Add Cancel button automatically if root page? Or always?
            // If subpage, Back handles it. If root, maybe Cancel.
            // Let's add Cancel only if stack count is 1.
            if (pageStack.Count == 1)
            {
                int cancelIdx = options.Count;
                GameObject cancelBtn;
                if (cancelIdx < buttonPool.Count)
                {
                    cancelBtn = buttonPool[cancelIdx];
                    cancelBtn.SetActive(true);
                }
                else
                {
                    cancelBtn = CreateButton();
                    buttonPool.Add(cancelBtn);
                }
                SetupButton(cancelBtn, new Option("Cancel", Hide), true);
            }
        }

        private GameObject CreateButton()
        {
            GameObject btnGO = new GameObject("Button");
            btnGO.transform.SetParent(panelRT, false);
            
            Image img = btnGO.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.3f);
            
            Button btn = btnGO.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.3f, 0.3f, 0.3f);
            cb.highlightedColor = new Color(0.5f, 0.5f, 0.5f);
            cb.pressedColor = new Color(0.2f, 0.2f, 0.2f);
            btn.colors = cb;
            
            LayoutElement le = btnGO.AddComponent<LayoutElement>();
            le.minHeight = 60; 
            le.preferredHeight = 60;
            le.minWidth = 300;
            le.preferredWidth = 400;
            
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            RectTransform textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10, 0);
            textRT.offsetMax = new Vector2(-10, 0);
            
            Text text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 28;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            
            return btnGO;
        }

        private void SetupButton(GameObject btnGO, Option option, bool isCancel = false)
        {
            Text t = btnGO.GetComponentInChildren<Text>();
            if (t) 
            {
                t.text = option.Label + (option.IsSubMenu ? " >" : "");
            }
            
            Image img = btnGO.GetComponent<Image>();
            if (isCancel)
            {
                img.color = new Color(0.4f, 0.1f, 0.1f);
            }
            else
            {
                img.color = new Color(0.3f, 0.3f, 0.3f);
            }

            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            if (le != null)
            {
                if (option.IsLarge)
                {
                     le.minHeight = 120;
                     le.preferredHeight = 120;
                }
                else
                {
                     le.minHeight = 60;
                     le.preferredHeight = 60;
                }
            }

            Button b = btnGO.GetComponent<Button>();
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => {
                // Do not Hide() immediately if it's a submenu navigation
                // We rely on the Action to push/pop or hide
                // But for regular actions we might want to hide?
                // The Action itself should call PushPage or Hide if needed.
                // But typically actions close the menu.
                // Let's hide by default unless it's submenu.
                
                if (!option.IsSubMenu)
                {
                     Hide();
                }
                
                option.Action?.Invoke();
            });
        }
    }
}
