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

        private static IEnumerator DisableSuppressionAfterSceneLoad()
        {
            LogUtil.Log("[VPB] DisableSuppressionAfterSceneLoad: Waiting for scene to finish loading...");
            
            // Wait for scene to start loading
            yield return new WaitForSeconds(0.5f);
            
            // Wait until scene loading is complete
            float timeout = 60f; // Max 60 seconds
            float elapsed = 0f;
            while (LogUtil.IsSceneLoading() && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            
            // Wait a bit more to ensure all post-load refreshes complete
            yield return new WaitForSeconds(1.0f);
            
            LogUtil.Log("[VPB] DisableSuppressionAfterSceneLoad: Scene load complete, disabling suppression");
            Gallery.SuppressAutoRefresh(false);
        }

        public static IEnumerator DisableSuppressionAfterDelay(float delay)
        {
            LogUtil.Log($"[VPB] DisableSuppressionAfterDelay: Waiting {delay}s before disabling suppression...");
            yield return new WaitForSeconds(delay);
            LogUtil.Log("[VPB] DisableSuppressionAfterDelay: Delay complete, disabling suppression");
            Gallery.SuppressAutoRefresh(false);
        }

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
                
                bool installed = false;
                
                // Suppress gallery auto-refresh to preserve scroll position and state
                // Must activate BEFORE EnsureInstalled since it may trigger FileManager.Refresh internally
                // NOTE: Suppression is NOT disabled in a finally block because sc.Load() is async
                // Instead, a coroutine will disable it after scene loading completes
                try
                {
                    Gallery.SuppressAutoRefresh(true);
                    
                    installed = EnsureInstalled(entry);
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
                }
                catch (Exception installEx)
                {
                    LogUtil.LogError($"[VPB] EnsureInstalled or FileManager refresh error: {installEx.Message}");
                    // On error, disable suppression immediately since we won't be loading
                    Gallery.SuppressAutoRefresh(false);
                    return;
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

                if (Messager.singleton == null)
                {
                    LogUtil.LogWarning("[VPB] Messager.singleton is null, cannot start load coroutines");
                    Gallery.SuppressAutoRefresh(false);
                }
                else if (installed)
                {
                    // Packages were just moved from AllPackages to AddonPackages.
                    // Yield one frame so MVR FileManager.Refresh can finish processing
                    // before LoadInternal runs, preventing atom-list race exceptions.
                    LogUtil.Log("[VPB] Packages installed; deferring sc.Load by one frame");
                    Messager.singleton.StartCoroutine(LoadSceneAfterRefresh(normalizedPath));
                }
                else
                {
                    SuperController sc = SuperController.singleton;
                    if (sc != null)
                    {
                        Messager.singleton.StartCoroutine(DisableSuppressionAfterSceneLoad());
                        LogUtil.Log($"[VPB] Calling sc.Load({normalizedPath})");
                        sc.Load(normalizedPath);
                    }
                    else
                    {
                        LogUtil.LogError("[VPB] SuperController.singleton is null!");
                        Gallery.SuppressAutoRefresh(false);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] UI.LoadSceneFile crash: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static IEnumerator LoadSceneAfterRefresh(string normalizedPath)
        {
            yield return null; // one frame for MVR refresh operations to settle
            SuperController sc = SuperController.singleton;
            if (sc == null)
            {
                LogUtil.LogError("[VPB] SuperController.singleton is null in LoadSceneAfterRefresh!");
                Gallery.SuppressAutoRefresh(false);
                yield break;
            }
            if (Messager.singleton != null)
                Messager.singleton.StartCoroutine(DisableSuppressionAfterSceneLoad());
            LogUtil.Log($"[VPB] Calling sc.Load({normalizedPath}) (after install+refresh)");
            sc.Load(normalizedPath);
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

        /// <summary>
        /// True for VaM package paths (creator.pkg.version:/internal), false for Windows drive paths (C:/...) and http(s) URLs.
        /// </summary>
        private static bool LooksLikeVarPackagePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            p = p.Replace('\\', '/');
            if (p.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return false;
            int i = p.IndexOf(":/", StringComparison.Ordinal);
            if (i < 0) return false;
            // Windows: "C:/Users/..." has ':' at index 1 after normalizing slashes
            if (i == 1 && p.Length > 2 && char.IsLetter(p[0])) return false;
            return true;
        }

        /// <summary>
        /// Use instead of raw <c>path.Contains(":")</c> so Windows drives and URLs are not mistaken for VAR references.
        /// </summary>
        public static bool IsLikelyVarPackageReference(string path)
        {
            return LooksLikeVarPackagePath(path);
        }

        /// <summary>
        /// Whether <paramref name="entry"/> refers to the same file as <paramref name="path"/> (any of path / Uid / normalized forms).
        /// </summary>
        private static bool FileEntryMatchesPathForJsonLoad(FileEntry entry, string path)
        {
            if (entry == null || string.IsNullOrEmpty(path)) return false;
            string p = path.Replace('\\', '/');
            string uid = entry.Uid?.Replace('\\', '/');
            string ep = entry.Path?.Replace('\\', '/');
            if (string.Equals(uid, p, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(ep, p, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                string norm = FileManager.NormalizePath(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(norm))
                {
                    if (string.Equals(uid, norm, StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(ep, norm, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        public static JSONNode LoadJSONWithFallback(string path, FileEntry entry = null)
        {
            if (string.IsNullOrEmpty(path)) return null;

            JSONNode root = null;
            try
            {
                if (SuperController.singleton != null)
                    root = SuperController.singleton.LoadJSON(path);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB] SuperController.LoadJSON threw for {path}: {ex.Message}");
                root = null;
            }

            if (root != null) return root;

            LogUtil.LogWarning($"[VPB] LoadJSONWithFallback: primary load failed for {path}, trying VPB stream/file fallback...");
            string content = null;
            FileEntry readEntry = null;

            try
            {
                if (entry != null && FileEntryMatchesPathForJsonLoad(entry, path))
                    readEntry = entry;
                else
                {
                    VarFileEntry vfe = FileManager.GetVarFileEntry(path);
                    if (vfe == null)
                    {
                        try
                        {
                            string norm = FileManager.NormalizePath(path);
                            if (!string.IsNullOrEmpty(norm))
                                vfe = FileManager.GetVarFileEntry(norm);
                        }
                        catch { }
                    }
                    readEntry = vfe;
                }

                if (readEntry != null)
                {
                    using (var reader = readEntry.OpenStreamReader())
                    {
                        if (reader != null)
                            content = reader.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB] LoadJSONWithFallback stream read failed for {path}: {ex.Message}");
            }

            // Loose file on disk (not a package-internal path; exclude Windows drive letters)
            if (string.IsNullOrEmpty(content))
            {
                string check = path.Replace('\\', '/');
                if (!LooksLikeVarPackagePath(check))
                {
                    try
                    {
                        if (File.Exists(path))
                            content = File.ReadAllText(path);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning($"[VPB] LoadJSONWithFallback file read failed for {path}: {ex.Message}");
                    }
                }
            }

            if (string.IsNullOrEmpty(content)) return null;

            VarFileEntry varForSelf = readEntry as VarFileEntry;
            if (varForSelf?.Package != null)
            {
                string packageUid = varForSelf.Package.Uid;
                content = content.Replace("SELF:/", packageUid + ":/");
                content = content.Replace("SELF:\\", packageUid + ":/");
            }

            try
            {
                return JSON.Parse(content);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] LoadJSONWithFallback: JSON parse failed for {path}: {ex.Message}");
                return null;
            }
        }

        public static GameObject CreateVScrollableContent(GameObject parentGO, Color backgroundColor, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset, float scrollBarWidth = 15f, float spacing = 0f, bool addBottomFlexSpacer = true)
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

            if (addBottomFlexSpacer)
            {
                // Main grid only: lets short lists fill the viewport. Sub-tab lists stay tight to the last row.
                GameObject spacer = new GameObject("BottomSpacer");
                spacer.transform.SetParent(contentGO.transform, false);
                LayoutElement le = spacer.AddComponent<LayoutElement>();
                le.preferredHeight = 0;
                le.flexibleHeight = 10000;
            }

            GameObject scrollbarGO = CreateScrollBar(scrollableContentGO, scrollBarWidth, verticalSize, Scrollbar.Direction.BottomToTop);
            Scrollbar scrollbar = scrollbarGO.GetComponent<Scrollbar>();

            ScrollRect scrollRect = scrollableContentGO.AddComponent<ScrollRect>();
            scrollRect.content = contentRT;
            scrollRect.viewport = viewportRT;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            // IMPORTANT: Do NOT assign scrollRect.verticalScrollbar directly, as it triggers Unity's 
            // internal auto-sizing which causes 1px flickering with large content heights.
            // We use ScrollbarSync instead to handle synchronization manually.
            scrollRect.verticalScrollbar = null; 
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            ScrollbarSync sync = scrollbarGO.AddComponent<ScrollbarSync>();
            sync.scrollRect = scrollRect;
            sync.scrollbar = scrollbar;
            sync.minSizePixels = 30f;

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
            scrollbar.interactable = true;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scrollbar.transition = Selectable.Transition.None;

            // Ensure the scrollbar is not blocked by a parent CanvasGroup
            CanvasGroup cg = scrollbarGO.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = true;
            cg.interactable = true;

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

            // Add BoxCollider to ensure reliable hit detection in 3D space
            var bc = scrollbarGO.AddComponent<BoxCollider>();
            bc.size = new Vector3(width, height > 0 ? height : 800f, 1f);
            bc.center = new Vector3(-width / 2, 0, 0); // Pivot is (1, 0.5)

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

        public static Sprite LoadIconSprite(string relativePathFromPluginsDir, Color? recolorTo = null)
        {
            try
            {
                string fullPath = Path.Combine(BepInEx.Paths.PluginPath, relativePathFromPluginsDir);
                if (!File.Exists(fullPath)) return null;
                byte[] bytes = File.ReadAllBytes(fullPath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                if (recolorTo.HasValue)
                {
                    Color c = recolorTo.Value;
                    Color[] pixels = tex.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                        if (pixels[i].a > 0.05f)
                            pixels[i] = new Color(c.r, c.g, c.b, pixels[i].a);
                    tex.SetPixels(pixels);
                    tex.Apply();
                }
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch { return null; }
        }

        /// <summary>Standard translucent-black backdrop applied to every icon button.</summary>
        public static readonly Color IconButtonBackdrop = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>
        /// Adds an icon Image child to <paramref name="buttonGO"/>, hides its text label, and sets
        /// the button's background to <paramref name="backdropOverride"/> (or <see cref="IconButtonBackdrop"/>
        /// when null). Pass an override only for buttons that have a meaningful accent colour (e.g. Hub).
        /// </summary>
        public static void AddIconToButton(GameObject buttonGO, Sprite icon, float padding = 4f, Color? backdropOverride = null)
        {
            // Apply unified backdrop (or explicit override for special-case buttons)
            Image btnImg = buttonGO.GetComponent<Image>();
            if (btnImg != null) btnImg.color = backdropOverride ?? IconButtonBackdrop;

            // Hide text — icon replaces it; text remains as fallback when icon is absent
            Text t = buttonGO.GetComponentInChildren<Text>(true);
            if (t != null) t.gameObject.SetActive(false);

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(buttonGO.transform, false);
            Image img = iconGO.AddComponent<Image>();
            img.sprite = icon;
            img.color = Color.white;
            img.preserveAspect = true;
            img.raycastTarget = false;
            RectTransform rt = iconGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = new Vector2(-padding * 2, -padding * 2);
            rt.anchoredPosition = Vector2.zero;
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
