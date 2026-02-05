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
    public static class UI
    {
        private static float _lastLoadSceneStartTime = -9999f;

        public static bool EnsureInstalled(FileEntry entry)
        {
            if (entry == null) return false;
            try
            {
                return SceneLoadingUtils.EnsureInstalled(entry);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] EnsureInstalled error: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public static void LoadSceneFile(FileEntry entry)
        {
            if (entry == null) return;

            // Guard against duplicate triggers in the same click/frame burst.
            // This can happen via UI event duplication and causes visible "default Person" flashes.
            float now = Time.unscaledTime;
            if (now - _lastLoadSceneStartTime < 0.75f)
            {
                LogUtil.LogWarning("[VPB] UI.LoadSceneFile ignored (throttled)");
                return;
            }
            _lastLoadSceneStartTime = now;

            try
            {
                string path = entry.Uid;
                LogUtil.Log($"[VPB] UI.LoadSceneFile started for: {path}");
                
                bool installed = EnsureInstalled(entry);
                LogUtil.Log($"[VPB] UI.EnsureInstalled (with dependency scan) depsChanged: {installed}");
                if (!installed)
                {
                    LogUtil.Log("[VPB] UI.EnsureInstalled: depsChanged=false means no packages were moved; missing deps (if any) are logged above by EnsureInstalled.");
                }

                if (installed)
                {
                    LogUtil.Log("[VPB] Refreshing FileManagers...");
                    if (MVR.FileManagement.FileManager.singleton != null)
                        MVR.FileManagement.FileManager.Refresh();
                    
                    FileManager.Refresh();
                }

                string normalizedPath = UI.NormalizePath(path);
                try
                {
                    if (SceneLoadingUtils.TryPrepareLocalSceneForLoad(entry, out string rewritten))
                    {
                        normalizedPath = UI.NormalizePath(rewritten);
                        LogUtil.Log($"[VPB] Using rewritten scene: {normalizedPath}");
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning($"[VPB] Scene rewrite skipped due to error: {ex.Message}");
                }

                LogUtil.Log($"[VPB] Normalized path: {normalizedPath}");
                
                SuperController sc = SuperController.singleton;
                if (sc != null)
                {
                    LogUtil.Log($"[VPB] Calling sc.Load({normalizedPath})");
                    sc.Load(normalizedPath);
                }
                else
                {
                    LogUtil.LogError("[VPB] SuperController.singleton is null!");
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] UI.LoadSceneFile crash: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            try
            {
                // FileManager.NormalizePath is more reliable in this codebase
                return FileManager.NormalizePath(path);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] FileManager.NormalizePath error: {ex.Message}");
            }
                
            string normalizedPath = path.Replace('\\', '/');
            try
            {
                string currentDir = Directory.GetCurrentDirectory().Replace('\\', '/');
                
                if (normalizedPath.StartsWith(currentDir, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPath = normalizedPath.Substring(currentDir.Length);
                    if (normalizedPath.StartsWith("/")) normalizedPath = normalizedPath.Substring(1);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] UI.NormalizePath fallback error: {ex.Message}");
            }
            return normalizedPath;
        }

        public static JSONNode LoadJSONWithFallback(string path, FileEntry entry = null)
        {
            // Use SuperController.singleton.LoadJSON which is most reliable for VARs and various paths
            JSONNode root = SuperController.singleton.LoadJSON(path);
            
            if (root == null)
            {
                LogUtil.LogWarning($"[VPB] SuperController.singleton.LoadJSON returned null for {path}, trying manual read...");
                string content = null;
                
                // If we have an entry, it's the best way to read (handles VAR streams)
                if (entry != null && (entry.Uid == path || entry.Path == path))
                {
                    using (var reader = entry.OpenStreamReader()) content = reader.ReadToEnd();
                }
                else 
                {
                    // If no entry, try to find it in the file manager or loose file
                    string normalized = UI.NormalizePath(path);
                    if (normalized.Contains(":")) // Likely a VAR path like Creator.Package:/path
                    {
                        // We don't have a direct package reader here without entry, 
                        // but sometimes entry is provided. 
                    }
                    else if (File.Exists(path))
                    {
                        content = File.ReadAllText(path);
                    }
                }
                
                if (!string.IsNullOrEmpty(content))
                {
                    // Fix SELF:/ paths if we are extracting from a VAR package
                    if (entry is VarFileEntry varEntry && varEntry.Package != null)
                    {
                        string packageUid = varEntry.Package.Uid;
                        content = content.Replace("SELF:/", packageUid + ":/");
                        content = content.Replace("SELF:\\", packageUid + ":/");
                    }
                    root = JSON.Parse(content);
                }
            }
            return root;
        }

        public static GameObject CreateVScrollableContent(GameObject parentGO, Color backgroundColor, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset, float scrollBarWidth = 15f, float spacing = 0f)
        {
            GameObject scrollableContentGO = AddChildGOImage(parentGO, backgroundColor, anchorPreset, horizontalSize, verticalSize, anchoredPositionOffset);

            GameObject viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollableContentGO.transform, false);
            RectTransform viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.sizeDelta = new Vector2(-scrollBarWidth, 0);
            viewportRT.anchoredPosition = new Vector2(-scrollBarWidth / 2 - 5, 0); // Shift left slightly to avoid clip
            viewportGO.AddComponent<RectMask2D>();

            GameObject contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.spacing = spacing;

            ContentSizeFitter csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Add a flexible spacer to the content so it doesn't leave huge empty gaps if there are few items
            GameObject spacer = new GameObject("BottomSpacer");
            spacer.transform.SetParent(contentGO.transform, false);
            LayoutElement le = spacer.AddComponent<LayoutElement>();
            le.preferredHeight = 0;
            le.flexibleHeight = 10000; // Large flexible height to consume any extra space in the parent
            
            GameObject scrollbarGO = CreateScrollBar(scrollableContentGO, scrollBarWidth, verticalSize, Scrollbar.Direction.BottomToTop);
            
            ScrollRect scrollRect = scrollableContentGO.AddComponent<ScrollRect>();
            scrollRect.content = contentRT;
            scrollRect.viewport = viewportRT;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.verticalScrollbar = scrollbarGO.GetComponent<Scrollbar>();
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            return scrollableContentGO;
        }

        public static GameObject CreateScrollBar(GameObject parentGO, float width, float height, Scrollbar.Direction direction)
        {
            GameObject scrollbarGO = new GameObject("Scrollbar");
            scrollbarGO.transform.SetParent(parentGO.transform, false);
            RectTransform rt = scrollbarGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(width, 0);

            Image bg = scrollbarGO.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);

            Scrollbar scrollbar = scrollbarGO.AddComponent<Scrollbar>();
            scrollbar.direction = direction;

            GameObject slidingArea = new GameObject("Sliding Area");
            slidingArea.transform.SetParent(scrollbarGO.transform, false);
            RectTransform slidingRT = slidingArea.AddComponent<RectTransform>();
            slidingRT.anchorMin = Vector2.zero;
            slidingRT.anchorMax = Vector2.one;
            slidingRT.sizeDelta = Vector2.zero;

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(slidingArea.transform, false);
            RectTransform handleRT = handle.AddComponent<RectTransform>();
            handleRT.sizeDelta = Vector2.zero;
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = new Color(0.6f, 0.6f, 0.6f, 1f);

            scrollbar.handleRect = handleRT;
            scrollbar.targetGraphic = handleImg;

            return scrollbarGO;
        }

        public static GameObject AddChildGOImage(GameObject parentGO, Color color, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset)
        {
            GameObject go = new GameObject("Image");
            go.transform.SetParent(parentGO.transform, false);
            Image img = go.AddComponent<Image>();
            img.color = color;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = AnchorPresets.GetAnchorMin(anchorPreset);
            rt.anchorMax = AnchorPresets.GetAnchorMax(anchorPreset);
            rt.pivot = AnchorPresets.GetPivot(anchorPreset);
            rt.anchoredPosition = anchoredPositionOffset;
            rt.sizeDelta = new Vector2(horizontalSize, verticalSize);

            return go;
        }

        public static GameObject AddChildGOChamferedImage(GameObject parentGO, Color color, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset, float chamferSize = 20f)
        {
            GameObject go = new GameObject("ChamferedImage");
            go.transform.SetParent(parentGO.transform, false);
            ChamferedRect img = go.AddComponent<ChamferedRect>();
            img.color = color;
            img.chamferSize = chamferSize;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = AnchorPresets.GetAnchorMin(anchorPreset);
            rt.anchorMax = AnchorPresets.GetAnchorMax(anchorPreset);
            rt.pivot = AnchorPresets.GetPivot(anchorPreset);
            rt.anchoredPosition = anchoredPositionOffset;
            rt.sizeDelta = new Vector2(horizontalSize, verticalSize);

            return go;
        }

        public static Mesh GenerateCurvedMesh(RectTransform targetRT, RectTransform canvasRT, float radiusBase = 2.0f, int segments = 50)
        {
            if (targetRT == null || canvasRT == null || VPBConfig.Instance == null) return null;

            float intensity = VPBConfig.Instance.CurvatureIntensity;
            float radius = radiusBase * (1.0f / (intensity > 0 ? intensity : 0.001f));

            Mesh mesh = new Mesh();
            mesh.name = "CurvedUIMesh";

            Rect rect = targetRT.rect;
            float scaleX = canvasRT.lossyScale.x;
            if (scaleX == 0) scaleX = 0.001f;

            Matrix4x4 localToCanvas = canvasRT.worldToLocalMatrix * targetRT.localToWorldMatrix;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float x = Mathf.Lerp(rect.xMin, rect.xMax, t);

                for (int j = 0; j <= 1; j++)
                {
                    float y = (j == 0) ? rect.yMin : rect.yMax;
                    Vector3 pos = new Vector3(x, y, 0);

                    // To Canvas Local Space
                    Vector3 cPos = localToCanvas.MultiplyPoint3x4(pos);

                    // Apply Cylinder Bend
                    float worldX = cPos.x * scaleX;
                    float angle = worldX / radius;

                    float newWorldX = Mathf.Sin(angle) * radius;
                    float newWorldZ = (Mathf.Cos(angle) - 1.0f) * radius;

                    cPos.x = newWorldX / scaleX;
                    cPos.z = newWorldZ / scaleX;

                    // Back to Target Local Space
                    vertices.Add(targetRT.worldToLocalMatrix.MultiplyPoint3x4(canvasRT.localToWorldMatrix.MultiplyPoint3x4(cPos)));
                    uvs.Add(new Vector2(t, j));
                }
            }

            for (int i = 0; i < segments; i++)
            {
                int baseIdx = i * 2;
                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 3);

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 3);
                triangles.Add(baseIdx + 2);
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        public static GameObject CreateUIButton(GameObject parentGO, float width, float height, string label, int fontSize, float xOffset, float yOffset, int anchorPreset, UnityAction onClick)
        {
            GameObject buttonGO = AddChildGOImage(parentGO, new Color(0.2f, 0.2f, 0.2f, 1f), anchorPreset, width, height, new Vector2(xOffset, yOffset));
            buttonGO.name = "Button_" + label;
            Button btn = buttonGO.AddComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(onClick);

            // Configure button colors to ensure dark background by default (avoiding white boxes)
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f); // Slightly brighter on hover
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f); // Darker and more transparent when disabled
            btn.colors = cb;
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(buttonGO.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.text = label;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;

            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;
            
            // Add Hover Border
            buttonGO.AddComponent<UIHoverBorder>();

            return buttonGO;
        }

        public static GameObject CreateUIToggle(GameObject parentGO, float width, float height, string label, int fontSize, float xOffset, float yOffset, int anchorPreset, UnityAction<bool> onValueChanged)
        {
            GameObject toggleGO = AddChildGOImage(parentGO, new Color(0, 0, 0, 0), anchorPreset, width, height, new Vector2(xOffset, yOffset));
            toggleGO.name = "Toggle_" + label;
            Toggle toggle = toggleGO.AddComponent<Toggle>();

            // Outer Box (Border - White)
            GameObject boxGO = new GameObject("Box");
            boxGO.transform.SetParent(toggleGO.transform, false);
            RectTransform boxRT = boxGO.AddComponent<RectTransform>();
            boxRT.anchorMin = new Vector2(0, 0.5f);
            boxRT.anchorMax = new Vector2(0, 0.5f);
            boxRT.pivot = new Vector2(0, 0.5f);
            boxRT.anchoredPosition = new Vector2(10, 0);
            boxRT.sizeDelta = new Vector2(20, 20);
            Image boxImg = boxGO.AddComponent<Image>();
            boxImg.color = Color.white;
            toggle.targetGraphic = boxImg;

            // Inner Box (Background - Black)
            GameObject innerGO = new GameObject("Inner");
            innerGO.transform.SetParent(boxGO.transform, false);
            RectTransform innerRT = innerGO.AddComponent<RectTransform>();
            innerRT.anchorMin = new Vector2(0.5f, 0.5f);
            innerRT.anchorMax = new Vector2(0.5f, 0.5f);
            innerRT.pivot = new Vector2(0.5f, 0.5f);
            innerRT.sizeDelta = new Vector2(16, 16);
            Image innerImg = innerGO.AddComponent<Image>();
            innerImg.color = Color.black;

            // Checkmark (Fill - White)
            GameObject checkGO = new GameObject("Checkmark");
            checkGO.transform.SetParent(innerGO.transform, false); 
            RectTransform checkRT = checkGO.AddComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.5f, 0.5f);
            checkRT.anchorMax = new Vector2(0.5f, 0.5f);
            checkRT.pivot = new Vector2(0.5f, 0.5f);
            checkRT.sizeDelta = new Vector2(14, 14); 
            Image checkImg = checkGO.AddComponent<Image>();
            checkImg.color = Color.white;
            toggle.graphic = checkImg;

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(toggleGO.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(1, 1);
            labelRT.offsetMin = new Vector2(35, 0);
            labelRT.offsetMax = new Vector2(0, 0);
            Text t = labelGO.AddComponent<Text>();
            t.text = label;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;

            toggle.onValueChanged.AddListener(onValueChanged);
            return toggleGO;
        }

        public static GameObject CreateToggle(GameObject parentGO, string label, float width, float height, float xOffset, float yOffset, int anchorPreset, UnityAction<bool> onValueChanged)
        {
            GameObject toggleGO = AddChildGOImage(parentGO, new Color(0, 0, 0, 0), anchorPreset, width, height, new Vector2(xOffset, yOffset));
            toggleGO.name = "Toggle_" + label;
            Toggle toggle = toggleGO.AddComponent<Toggle>();

            // Outer Box (Border - White)
            GameObject boxGO = new GameObject("Box");
            boxGO.transform.SetParent(toggleGO.transform, false);
            RectTransform boxRT = boxGO.AddComponent<RectTransform>();
            boxRT.anchorMin = new Vector2(0, 0.5f);
            boxRT.anchorMax = new Vector2(0, 0.5f);
            boxRT.pivot = new Vector2(0, 0.5f);
            boxRT.anchoredPosition = new Vector2(10, 0);
            boxRT.sizeDelta = new Vector2(20, 20);
            Image boxImg = boxGO.AddComponent<Image>();
            boxImg.color = Color.white;
            toggle.targetGraphic = boxImg;

            // Inner Box (Background - Black)
            GameObject innerGO = new GameObject("Inner");
            innerGO.transform.SetParent(boxGO.transform, false);
            RectTransform innerRT = innerGO.AddComponent<RectTransform>();
            innerRT.anchorMin = new Vector2(0.5f, 0.5f);
            innerRT.anchorMax = new Vector2(0.5f, 0.5f);
            innerRT.pivot = new Vector2(0.5f, 0.5f);
            innerRT.sizeDelta = new Vector2(16, 16);
            Image innerImg = innerGO.AddComponent<Image>();
            innerImg.color = Color.black;

            // Checkmark (Fill - White)
            GameObject checkGO = new GameObject("Checkmark");
            checkGO.transform.SetParent(innerGO.transform, false); // Parent to inner or box, doesn't matter much if positioned correctly
            RectTransform checkRT = checkGO.AddComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.5f, 0.5f);
            checkRT.anchorMax = new Vector2(0.5f, 0.5f);
            checkRT.pivot = new Vector2(0.5f, 0.5f);
            checkRT.sizeDelta = new Vector2(14, 14); // Slightly smaller to leave a hint of border or full size? Let's use 14 to leave black gap, or 16 for solid. User said "white is selected". Solid white looks best.
            // Actually if I make it 16, it covers the black inner completely, merging with white outer.
            checkRT.sizeDelta = new Vector2(16, 16); 
            Image checkImg = checkGO.AddComponent<Image>();
            checkImg.color = Color.white;
            toggle.graphic = checkImg;

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(toggleGO.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(1, 1);
            labelRT.offsetMin = new Vector2(35, 0);
            labelRT.offsetMax = new Vector2(0, 0);
            Text t = labelGO.AddComponent<Text>();
            t.text = label;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 16;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;

            toggle.onValueChanged.AddListener(onValueChanged);
            return toggleGO;
        }

        public static GameObject CreateSlider(GameObject parentGO, string label, float width, float height, float min, float max, float currentVal, UnityAction<float> onValueChanged)
        {
            GameObject container = AddChildGOImage(parentGO, new Color(0,0,0,0), AnchorPresets.middleCenter, width, height, Vector2.zero);
            
            // Label
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(container.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0.5f);
            labelRT.anchorMax = new Vector2(0.5f, 1f);
            labelRT.offsetMin = new Vector2(5, 0);
            Text t = labelGO.AddComponent<Text>();
            t.text = label + ": " + currentVal.ToString("F2");
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 14;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;

            // Slider
            GameObject sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(container.transform, false);
            RectTransform sliderRT = sliderGO.AddComponent<RectTransform>();
            sliderRT.anchorMin = new Vector2(0.5f, 0.1f);
            sliderRT.anchorMax = new Vector2(0.95f, 0.9f);
            
            Slider slider = sliderGO.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = currentVal;
            
            // Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(sliderGO.transform, false);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.2f, 0.2f);
            RectTransform bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.25f);
            bgRT.anchorMax = new Vector2(1, 0.75f);
            
            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGO.transform, false);
            RectTransform fillAreaRT = fillArea.AddComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0, 0.25f);
            fillAreaRT.anchorMax = new Vector2(1, 0.75f);
            
            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.25f, 0.5f, 0.8f);
            RectTransform fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            slider.fillRect = fillRT;
            
            // Handle
            GameObject handleArea = new GameObject("Handle Area");
            handleArea.transform.SetParent(sliderGO.transform, false);
            RectTransform handleAreaRT = handleArea.AddComponent<RectTransform>();
            handleAreaRT.anchorMin = Vector2.zero;
            handleAreaRT.anchorMax = Vector2.one;
            
            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;
            RectTransform handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(20, 0);
            slider.handleRect = handleRT;
            slider.targetGraphic = handleImg;

            slider.onValueChanged.AddListener((val) => {
                t.text = label + ": " + val.ToString("F2");
                onValueChanged(val);
            });
            
            return container;
        }

        public static GameObject CreateDropdown(GameObject parentGO, string label, float width, float height, List<string> options, int currentIdx, UnityAction<int> onValueChanged)
        {
            GameObject container = AddChildGOImage(parentGO, new Color(0,0,0,0), AnchorPresets.middleCenter, width, height, Vector2.zero);
            
            GameObject btnGO = CreateUIButton(container, width, height, label + ": " + (options.Count > currentIdx ? options[currentIdx] : ""), 14, 0, 0, AnchorPresets.middleCenter, null);
            Button btn = btnGO.GetComponent<Button>();
            Text t = btnGO.GetComponentInChildren<Text>();
            
            // Use a local variable to capture index if possible, but UnityAction works with captured vars
            // We need a wrapper class to hold state if we want it to persist, but for now closure is fine
            int idx = currentIdx;
            
            btn.onClick.AddListener(() => {
                idx = (idx + 1) % options.Count;
                t.text = label + ": " + options[idx];
                onValueChanged(idx);
            });
            
            return container;
        }

        public static GameObject CreateTextInput(GameObject parentGO, float width, float height, string defaultText, int fontSize, float xOffset, float yOffset, int anchorPreset, UnityAction<string> onEndEdit)
        {
            GameObject inputGO = AddChildGOImage(parentGO, new Color(0.1f, 0.1f, 0.1f, 1f), anchorPreset, width, height, new Vector2(xOffset, yOffset));
            inputGO.name = "TextInput";
            
            InputField inputField = inputGO.AddComponent<InputField>();
            
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(inputGO.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.supportRichText = false;
            
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = new Vector2(-10, -10);
            textRT.anchoredPosition = new Vector2(5, 0);
            
            inputField.textComponent = t;
            
            GameObject placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(inputGO.transform, false);
            Text p = placeholderGO.AddComponent<Text>();
            p.text = defaultText;
            p.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            p.fontSize = fontSize;
            p.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            p.alignment = TextAnchor.MiddleLeft;
            p.fontStyle = FontStyle.Italic;
            
            RectTransform placeholderRT = placeholderGO.GetComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.sizeDelta = new Vector2(-10, -10);
            placeholderRT.anchoredPosition = new Vector2(5, 0);
            
            inputField.placeholder = p;
            
            if (onEndEdit != null) inputField.onEndEdit.AddListener(onEndEdit);
            
            return inputGO;
        }
    }
}
