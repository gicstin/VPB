using System;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public class HubResourceItem
    {
        protected HubBrowse browser;
        private static readonly string[] SizeSuffixes = new string[9] { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        protected string resource_id;

        protected string discussion_thread_id;

        protected JSONStorableString titleJSON;

        protected JSONStorableString tagLineJSON;

        protected JSONStorableString versionNumberJSON;

        protected JSONStorableString payTypeJSON;

        protected JSONStorableString categoryJSON;

        protected JSONStorableAction payTypeAndCategorySelectAction;

        protected JSONStorableString creatorJSON;

        protected JSONStorableAction creatorSelectAction;

        protected RawImage creatorIconImage;

        protected Texture2D creatorIconTexture;

        protected bool useQueueImmediate;

        protected VPB.HubImageLoaderThreaded.QueuedImage creatorIconQueuedImage;

        protected JSONStorableUrl creatorIconUrlJSON;

        protected RawImage thumbnailImage;

        protected Texture2D thumbnailTexture;

        protected VPB.HubImageLoaderThreaded.QueuedImage thumbnailQueuedImage;

        protected string groupId;

        protected JSONStorableUrl thumbnailUrlJSON;

        protected JSONStorableBool hubDownloadableJSON;

        protected JSONStorableBool hubHostedJSON;

        protected JSONStorableString dependencyCountJSON;

        protected JSONStorableBool hasDependenciesJSON;

        protected JSONStorableString downloadCountJSON;

        protected JSONStorableString ratingsCountJSON;

        protected JSONStorableFloat ratingJSON;

        protected JSONStorableString lastUpdateJSON;

        protected JSONArray varFilesJSONArray;
        protected JSONClass dependenciesObject;

        public JSONStorableBool inLibraryJSON;

        protected JSONStorableBool updateAvailableJSON;

        protected JSONStorableString updateMsgJSON;

        protected JSONStorableAction openDetailAction;
        protected JSONStorableAction quickDownloadAllAction;
        protected HubResourceItemUI registeredUI;
        protected List<HubResourcePackage> quickDownloadPackages;
        protected bool quickDownloadInProgress;

        public string ResourceId
        {
            get
            {
                return resource_id;
            }
        }

        public string Title
        {
            get
            {
                return titleJSON.val;
            }
        }

        public string TagLine
        {
            get
            {
                return tagLineJSON.val;
            }
        }

        public string VersionNumber
        {
            get
            {
                return versionNumberJSON.val;
            }
        }

        public string PayType
        {
            get
            {
                return payTypeJSON.val;
            }
        }

        public string Category
        {
            get
            {
                return categoryJSON.val;
            }
        }

        public string Creator
        {
            get
            {
                return creatorJSON.val;
            }
        }

        public int DownloadCount
        {
            get
            {
                int result;
                if (int.TryParse(downloadCountJSON.val, out result))
                {
                    return result;
                }
                return 0;
            }
        }

        public int RatingsCount
        {
            get
            {
                int result;
                if (int.TryParse(ratingsCountJSON.val, out result))
                {
                    return result;
                }
                return 0;
            }
        }

        public float Rating
        {
            get
            {
                return ratingJSON.val;
            }
        }

        public DateTime LastUpdateTimestamp { get; protected set; }

        public HubResourceItem(JSONClass resource, HubBrowse hubBrowse, string page, bool queueImagesImmediate = false)
        {
            groupId = "HubPage_" + page;
            useQueueImmediate = queueImagesImmediate;
            browser = hubBrowse;
            resource_id = resource["resource_id"];
            discussion_thread_id = resource["discussion_thread_id"];
            string startingValue = resource["title"];
            string startingValue2 = resource["tag_line"];
            string startingValue3 = resource["version_string"];
            string startingValue4 = resource["category"];
            string startingValue5 = resource["type"];
            string startingValue6 = resource["username"];
            string text = resource["icon_url"];
            string text2 = resource["image_url"];
            bool asBool = resource["hubDownloadable"].AsBool;
            bool asBool2 = resource["hubHosted"].AsBool;
            int asInt = resource["dependency_count"].AsInt;
            bool startingValue7 = asInt > 0;
            string startingValue8 = resource["download_count"];
            string startingValue9 = resource["rating_count"];
            float asFloat = resource["rating_avg"].AsFloat;
            int asInt2 = resource["last_update"].AsInt;
            LastUpdateTimestamp = UnixTimeStampToDateTime(asInt2);
            string startingValue10 = ((!((DateTime.Now - LastUpdateTimestamp).TotalDays > 7.0)) ? LastUpdateTimestamp.ToString("dddd h:mm tt") : LastUpdateTimestamp.ToString("MMM d, yyyy"));
            varFilesJSONArray = resource["hubFiles"].AsArray;
            titleJSON = new JSONStorableString("title", startingValue);
            tagLineJSON = new JSONStorableString("tagLine", startingValue2);
            versionNumberJSON = new JSONStorableString("versionNumber", startingValue3);
            
            // Calculate total size and append dependency info to version string
            long totalSize = 0;
            if (varFilesJSONArray != null)
            {
                foreach (JSONNode file in varFilesJSONArray)
                {
                    totalSize += (long)file["file_size"].AsDouble;
                }
            }
            
            dependenciesObject = resource["dependencies"].AsObject;
            if (dependenciesObject != null)
            {
                foreach (string key in dependenciesObject.Keys)
                {
                    JSONArray depFiles = dependenciesObject[key].AsArray;
                    if (depFiles != null)
                    {
                        foreach (JSONNode depFile in depFiles)
                        {
                            totalSize += (long)depFile["file_size"].AsDouble;
                        }
                    }
                }
            }

            string extraInfo = "";
            if (asInt > 0)
            {
                extraInfo += $" ({asInt} deps";
                if (totalSize > 0)
                {
                    extraInfo += $", {SizeSuffix(totalSize)}";
                }
                extraInfo += ")";
            }
            else if (totalSize > 0)
            {
                extraInfo += $" ({SizeSuffix(totalSize)})";
            }
            
            versionNumberJSON.val = startingValue3 + extraInfo;

            payTypeJSON = new JSONStorableString("payType", startingValue4);
            categoryJSON = new JSONStorableString("category", startingValue5);
            payTypeAndCategorySelectAction = new JSONStorableAction("PayTypeAndCategorySelect", PayTypeAndCategorySelect);
            creatorJSON = new JSONStorableString("creator", startingValue6);
            creatorSelectAction = new JSONStorableAction("CreatorSelect", CreatorSelect);
            creatorIconUrlJSON = new JSONStorableUrl("creatorIconUrl", text, SyncCreatorIconUrl);
            SyncCreatorIconUrl(text);
            thumbnailUrlJSON = new JSONStorableUrl("thumbnailUrl", text2, SyncThumbnailUrl);
            SyncThumbnailUrl(text2);
            hubDownloadableJSON = new JSONStorableBool("hubDownloadable", asBool);
            hubHostedJSON = new JSONStorableBool("hubHosted", asBool2);
            hasDependenciesJSON = new JSONStorableBool("hasDependencies", startingValue7);
            dependencyCountJSON = new JSONStorableString("dependencyCount", asInt + " Hub-Hosted Dependencies");
            downloadCountJSON = new JSONStorableString("downloadCount", startingValue8);
            ratingsCountJSON = new JSONStorableString("ratingsCount", startingValue9);
            ratingJSON = new JSONStorableFloat("rating", asFloat, 0f, 5f, true, false);
            lastUpdateJSON = new JSONStorableString("lastUpdate", startingValue10);
            openDetailAction = new JSONStorableAction("OpenDetail", OpenDetail);
            quickDownloadAllAction = new JSONStorableAction("DownloadAllFromCard", DownloadAll);
            inLibraryJSON = new JSONStorableBool("inLibrary", false);
            updateAvailableJSON = new JSONStorableBool("updateAvailable", false);
            updateMsgJSON = new JSONStorableString("updateMsg", "Update Available");
        }

        protected void PayTypeAndCategorySelect()
        {
            browser.SetPayTypeAndCategoryFilter(payTypeJSON.val, categoryJSON.val);
        }

        protected void CreatorSelect()
        {
            browser.CreatorFilterOnly = creatorJSON.val;
        }

        protected void SyncCreatorIconTexture(VPB.HubImageLoaderThreaded.QueuedImage qi)
        {
            creatorIconTexture = qi.tex;
            if (creatorIconImage != null && creatorIconTexture != null)
            {
                creatorIconImage.texture = creatorIconTexture;
            }
        }

        protected void SyncCreatorIconUrl(string url)
        {
            if (url == null || url == string.Empty) return;
            if (VPB.HubImageLoaderThreaded.singleton != null)
            {
                VPB.HubImageLoaderThreaded.QueuedImage queuedImage = VPB.HubImageLoaderThreaded.singleton.GetQI();
                queuedImage.imgPath = url;
                queuedImage.groupId = groupId;
                queuedImage.callback = SyncCreatorIconTexture;
                creatorIconQueuedImage = queuedImage;
                if (useQueueImmediate)
                {
                    VPB.HubImageLoaderThreaded.singleton.QueueThumbnailImmediate(queuedImage);
                }
                else
                {
                    VPB.HubImageLoaderThreaded.singleton.QueueThumbnail(queuedImage);
                }
            }
            else
            {
                LogUtil.LogWarning("[VPB] HubImageLoaderThreaded.singleton is null during SyncCreatorIconUrl for " + url);
                // The URL is already stored in creatorIconUrlJSON, so it might be retried later if needed, 
                // but let's try a small delay or just rely on Show() calling it again.
            }
        }

        protected void SyncThumbnailTexture(VPB.HubImageLoaderThreaded.QueuedImage qi)
        {
            thumbnailTexture = qi.tex;
            if (thumbnailImage != null)
            {
                thumbnailImage.texture = thumbnailTexture;
                if (thumbnailTexture != null)
                {
                    thumbnailImage.color = Color.white;
                }
            }
        }

        protected void SyncThumbnailUrl(string url)
        {
            if (url == null || url == string.Empty) return;
            if (VPB.HubImageLoaderThreaded.singleton != null)
            {
                VPB.HubImageLoaderThreaded.QueuedImage queuedImage = VPB.HubImageLoaderThreaded.singleton.GetQI();
                queuedImage.imgPath = url;
                queuedImage.groupId = groupId;
                queuedImage.callback = SyncThumbnailTexture;
                thumbnailQueuedImage = queuedImage;
                if (useQueueImmediate)
                {
                    VPB.HubImageLoaderThreaded.singleton.QueueThumbnailImmediate(queuedImage);
                }
                else
                {
                    VPB.HubImageLoaderThreaded.singleton.QueueThumbnail(queuedImage);
                }
            }
            else
            {
                LogUtil.LogWarning("[VPB] HubImageLoaderThreaded.singleton is null during SyncThumbnailUrl for " + url);
            }
        }
        protected DateTime UnixTimeStampToDateTime(int unixTimeStamp)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixTimeStamp).ToLocalTime();
        }

        private static string SizeSuffix(long value, int decimalPlaces = 1)
        {
            if (value < 0)
            {
                return "-" + SizeSuffix(-value);
            }
            if (value == 0)
            {
                return string.Format("{0:n" + decimalPlaces + "} bytes", 0);
            }
            int num = (int)Math.Log(value, 1024.0);
            decimal num2 = (decimal)value / (decimal)(1L << num * 10);
            if (Math.Round(num2, decimalPlaces) >= 1000m)
            {
                num++;
                num2 /= 1024m;
            }
            return string.Format("{0:n" + decimalPlaces + "} {1}", num2, SizeSuffixes[num]);
        }

        public virtual void Refresh()
        {
            if (hubDownloadableJSON.val && varFilesJSONArray != null && varFilesJSONArray.Count > 0)
            {
                bool val = true;
                bool val2 = false;
                IEnumerator enumerator = varFilesJSONArray.GetEnumerator();
                try
                {
                    while (enumerator.MoveNext())
                    {
                        JSONNode jSONNode = (JSONNode)enumerator.Current;
                        string text = jSONNode["filename"];
                        if (text == null)
                        {
                            continue;
                        }
                        text = Regex.Replace(text, ".var$", string.Empty);
                        string packageGroupUid = Regex.Replace(text, "(.*)\\..*", "$1");
                        string s = Regex.Replace(text, ".*\\.([0-9]+)$", "$1");
                        int result;
                        if (!int.TryParse(s, out result))
                        {
                            continue;
                        }
                        // Hub browsing should not auto-install packages. We only need to know if it exists locally.
                        VarPackage package = FileManager.GetPackage(text, ensureInstalled: false);
                        if (package == null)
                        {
                            VarPackageGroup packageGroup = FileManager.GetPackageGroup(packageGroupUid);
                            if (packageGroup == null || packageGroup.NewestPackage == null)
                            {
                                val = false;
                                break;
                            }
                            if (packageGroup.NewestPackage.Version < result)
                            {
                                val2 = true;
                                updateMsgJSON.val = "Update Available " + packageGroup.NewestEnabledPackage.Version + " -> " + result;
                            }
                        }
                    }
                }
                finally
                {
                    IDisposable disposable;
                    if ((disposable = enumerator as IDisposable) != null)
                    {
                        disposable.Dispose();
                    }
                }
                inLibraryJSON.val = val;
                updateAvailableJSON.val = val2;
            }
            else
            {
                inLibraryJSON.val = false;
                updateAvailableJSON.val = false;
            }
            SyncQuickDownloadCtaVisibility();
        }

        public void OpenDetail()
        {
            browser.OpenDetail(resource_id);
        }

        public virtual void DownloadAll()
        {
            if (quickDownloadInProgress)
            {
                return;
            }

            List<HubResourcePackage> packages = BuildDownloadPackageList();
            quickDownloadPackages = new List<HubResourcePackage>();
            for (int i = 0; i < packages.Count; i++)
            {
                HubResourcePackage package = packages[i];
                if (package != null && package.NeedsDownload)
                {
                    quickDownloadPackages.Add(package);
                    package.Download();
                }
            }
            quickDownloadInProgress = quickDownloadPackages.Count > 0;
            SyncQuickDownloadCtaVisibility();
        }

        protected List<HubResourcePackage> BuildDownloadPackageList()
        {
            List<HubResourcePackage> packages = new List<HubResourcePackage>();
            if (varFilesJSONArray != null)
            {
                IEnumerator enumerator = varFilesJSONArray.GetEnumerator();
                try
                {
                    while (enumerator.MoveNext())
                    {
                        JSONClass packageObject = ((JSONNode)enumerator.Current).AsObject;
                        if (packageObject != null)
                        {
                            packages.Add(new HubResourcePackage(packageObject, browser, false));
                        }
                    }
                }
                finally
                {
                    IDisposable disposable;
                    if ((disposable = enumerator as IDisposable) != null)
                    {
                        disposable.Dispose();
                    }
                }
            }

            if (dependenciesObject != null)
            {
                foreach (string key in dependenciesObject.Keys)
                {
                    JSONArray dependencyFiles = dependenciesObject[key].AsArray;
                    if (dependencyFiles == null)
                    {
                        continue;
                    }
                    IEnumerator dependencyEnumerator = dependencyFiles.GetEnumerator();
                    try
                    {
                        while (dependencyEnumerator.MoveNext())
                        {
                            JSONClass packageObject2 = ((JSONNode)dependencyEnumerator.Current).AsObject;
                            if (packageObject2 != null)
                            {
                                packages.Add(new HubResourcePackage(packageObject2, browser, true));
                            }
                        }
                    }
                    finally
                    {
                        IDisposable disposable2;
                        if ((disposable2 = dependencyEnumerator as IDisposable) != null)
                        {
                            disposable2.Dispose();
                        }
                    }
                }
            }

            return packages;
        }

        protected Button EnsureQuickDownloadButton(HubResourceItemUI ui)
        {
            if (ui is HubResourceItemDetailUI)
            {
                return null;
            }
            if (ui == null || ui.inLibraryIndicator == null)
            {
                return null;
            }
            if (ui.quickDownloadAllButton != null)
            {
                return ui.quickDownloadAllButton;
            }

            Transform parent = ui.inLibraryIndicator.transform.parent;
            if (parent == null)
            {
                parent = ui.inLibraryIndicator.transform;
            }

            GameObject buttonObject = new GameObject("QuickDownloadAllButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline), typeof(EventTrigger));
            buttonObject.transform.SetParent(parent, false);

            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            RectTransform sourceRect = ui.inLibraryIndicator.GetComponent<RectTransform>();
            if (sourceRect != null)
            {
                buttonRect.anchorMin = sourceRect.anchorMin;
                buttonRect.anchorMax = sourceRect.anchorMax;
                buttonRect.pivot = sourceRect.pivot;
                buttonRect.anchoredPosition = sourceRect.anchoredPosition;
                buttonRect.sizeDelta = sourceRect.sizeDelta;
                buttonRect.localScale = sourceRect.localScale;
                buttonRect.SetSiblingIndex(sourceRect.GetSiblingIndex());
            }
            else
            {
                buttonRect.anchorMin = new Vector2(0f, 0f);
                buttonRect.anchorMax = new Vector2(1f, 0f);
                buttonRect.pivot = new Vector2(0.5f, 0f);
                buttonRect.anchoredPosition = new Vector2(0f, 0f);
                buttonRect.sizeDelta = new Vector2(0f, 34f);
            }

            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(0.52f, 0.96f, 0.52f, 1f);

            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 1f);
            colors.highlightedColor = new Color(0.9f, 1f, 0.9f, 1f);
            colors.pressedColor = new Color(0.78f, 0.95f, 0.78f, 1f);
            button.colors = colors;

            Outline hoverOutline = buttonObject.GetComponent<Outline>();
            hoverOutline.effectDistance = new Vector2(3f, 3f);
            hoverOutline.effectColor = new Color(0.18f, 0.45f, 0.18f, 0f);
            hoverOutline.useGraphicAlpha = false;

            EventTrigger hoverTrigger = buttonObject.GetComponent<EventTrigger>();
            hoverTrigger.triggers = new List<EventTrigger.Entry>();
            EventTrigger.Entry enterEntry = new EventTrigger.Entry();
            enterEntry.eventID = EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener(delegate
            {
                if (hoverOutline != null)
                {
                    hoverOutline.effectColor = new Color(0.18f, 0.45f, 0.18f, 1f);
                }
            });
            hoverTrigger.triggers.Add(enterEntry);

            EventTrigger.Entry exitEntry = new EventTrigger.Entry();
            exitEntry.eventID = EventTriggerType.PointerExit;
            exitEntry.callback.AddListener(delegate
            {
                if (hoverOutline != null)
                {
                    hoverOutline.effectColor = new Color(0.18f, 0.45f, 0.18f, 0f);
                }
            });
            hoverTrigger.triggers.Add(exitEntry);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(3f, 1f);
            labelRect.offsetMax = new Vector2(-3f, -1f);

            Text label = labelObject.GetComponent<Text>();
            label.text = "Direct Download All";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color32(163, 111, 214, 255);
            label.fontStyle = FontStyle.Normal;
            label.resizeTextForBestFit = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            if (ui.creatorText != null && ui.creatorText.font != null)
            {
                label.font = ui.creatorText.font;
            }
            else
            {
                label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            int nativeSize = 30;
            if (ui.inLibraryIndicator != null)
            {
                Text nativeLabel = ui.inLibraryIndicator.GetComponentInChildren<Text>(true);
                if (nativeLabel != null)
                {
                    nativeSize = Mathf.Max(22, nativeLabel.fontSize);
                    if (nativeLabel.font != null)
                    {
                        label.font = nativeLabel.font;
                    }
                    label.fontStyle = nativeLabel.fontStyle;
                }
            }
            label.fontSize = nativeSize;

            ui.quickDownloadAllButton = button;
            ui.quickDownloadAllButtonBackground = buttonImage;
            ui.quickDownloadAllButtonLabel = label;
            return button;
        }

        protected void SyncQuickDownloadCtaVisibility()
        {
            if (registeredUI == null || registeredUI is HubResourceItemDetailUI)
            {
                return;
            }

            bool hasDownloadableFiles = varFilesJSONArray != null && varFilesJSONArray.Count > 0;
            bool queueActive = IsQuickDownloadQueueActive();
            bool showDownloadCta = (hubDownloadableJSON.val && hasDownloadableFiles && !inLibraryJSON.val) || queueActive;

            if (registeredUI.quickDownloadAllButton != null)
            {
                registeredUI.quickDownloadAllButton.gameObject.SetActive(showDownloadCta);
            }
            if (registeredUI.inLibraryIndicator != null)
            {
                registeredUI.inLibraryIndicator.SetActive(inLibraryJSON.val);
            }

            SyncQuickDownloadCtaPresentation(queueActive);
        }

        protected bool IsQuickDownloadQueueActive()
        {
            if (!quickDownloadInProgress || quickDownloadPackages == null || quickDownloadPackages.Count == 0)
            {
                quickDownloadInProgress = false;
                return false;
            }

            bool anyPending = false;
            for (int i = quickDownloadPackages.Count - 1; i >= 0; i--)
            {
                HubResourcePackage package = quickDownloadPackages[i];
                if (package == null)
                {
                    quickDownloadPackages.RemoveAt(i);
                    continue;
                }
                package.Refresh();
                if (package.IsDownloading || package.IsDownloadQueued || package.NeedsDownload)
                {
                    anyPending = true;
                }
            }
            quickDownloadInProgress = anyPending && quickDownloadPackages.Count > 0;
            return quickDownloadInProgress;
        }

        protected float GetQuickDownloadProgress01()
        {
            if (quickDownloadPackages == null || quickDownloadPackages.Count == 0)
            {
                return 0f;
            }

            float total = 0f;
            int count = 0;
            for (int i = 0; i < quickDownloadPackages.Count; i++)
            {
                HubResourcePackage package = quickDownloadPackages[i];
                if (package == null)
                {
                    continue;
                }
                float progress = 0f;
                if (!package.NeedsDownload)
                {
                    progress = 1f;
                }
                else if (package.IsDownloading)
                {
                    progress = Mathf.Clamp01(package.DownloadProgress01);
                }
                total += progress;
                count++;
            }
            if (count == 0)
            {
                return 0f;
            }
            return Mathf.Clamp01(total / count);
        }

        protected void SyncQuickDownloadCtaPresentation(bool queueActive)
        {
            if (registeredUI == null)
            {
                return;
            }

            Text label = registeredUI.quickDownloadAllButtonLabel;
            Image background = registeredUI.quickDownloadAllButtonBackground;
            if (label == null || background == null)
            {
                return;
            }

            if (queueActive)
            {
                label.text = "Downloading";
                label.color = new Color(0.35f, 0.06f, 0.32f, 1f);
                background.color = new Color(0.96f, 0.72f, 0.92f, 1f);
            }
            else
            {
                label.text = "Direct Download All";
                label.color = new Color32(255, 255, 255, 255);
                background.color = new Color32(163, 111, 214, 255);
            }
        }

        public void Hide()
        {
            if (creatorIconQueuedImage != null)
            {
                creatorIconQueuedImage.cancel = true;
            }
            if (thumbnailQueuedImage != null)
            {
                thumbnailQueuedImage.cancel = true;
            }
        }

        public void Show()
        {
            if (VPB.HubImageLoaderThreaded.singleton == null) return;

            if (creatorIconQueuedImage != null && !creatorIconQueuedImage.processed)
            {
                creatorIconQueuedImage.cancel = false;
                if (useQueueImmediate)
                {
                    VPB.HubImageLoaderThreaded.singleton.QueueThumbnailImmediate(creatorIconQueuedImage);
                }
                else
                {
                    VPB.HubImageLoaderThreaded.singleton.QueueThumbnail(creatorIconQueuedImage);
                }
            }
            else if (creatorIconQueuedImage == null && !string.IsNullOrEmpty(creatorIconUrlJSON.val))
            {
                SyncCreatorIconUrl(creatorIconUrlJSON.val);
            }

            if (thumbnailQueuedImage != null && !thumbnailQueuedImage.processed)
            {
                thumbnailQueuedImage.cancel = false;
                if (useQueueImmediate)
                {
                    VPB.HubImageLoaderThreaded.singleton.QueueThumbnailImmediate(thumbnailQueuedImage);
                }
                else
                {
                    VPB.HubImageLoaderThreaded.singleton.QueueThumbnail(thumbnailQueuedImage);
                }
            }
            else if (thumbnailQueuedImage == null && !string.IsNullOrEmpty(thumbnailUrlJSON.val))
            {
                SyncThumbnailUrl(thumbnailUrlJSON.val);
            }
        }

        public void Destroy()
        {
            if (creatorIconQueuedImage != null)
            {
                creatorIconQueuedImage.cancel = true;
            }
            if (thumbnailQueuedImage != null)
            {
                thumbnailQueuedImage.cancel = true;
            }
        }

        public virtual void RegisterUI(HubResourceItemUI ui)
        {
            if (ui != null)
            {
                registeredUI = ui;
                ui.connectedItem = this;

                titleJSON.text = ui.titleText;
                tagLineJSON.text = ui.tagLineText;
                versionNumberJSON.text = ui.versionText;
                // Format as "PayType: Category"
                if (ui.payTypeText != null)
                {
                    string payType = payTypeJSON.val;
                    string category = categoryJSON.val;
                    
                    // Avoid duplication if category is already in payType (e.g. "Free Looks" and "Looks")
                    if (!string.IsNullOrEmpty(category) && payType.EndsWith(category, StringComparison.OrdinalIgnoreCase))
                    {
                        // Strip the duplicate part from payType
                        payType = payType.Substring(0, payType.Length - category.Length).Trim();
                    }

                    string formattedText = payType;
                    if (!string.IsNullOrEmpty(category))
                    {
                        formattedText += ": " + category;
                    }
                    ui.payTypeText.text = formattedText;
                }
                
                // Style the main pay type badge if it's not empty
                if (ui.payTypeText != null && !string.IsNullOrEmpty(payTypeJSON.val))
                {
                    var payRT = ui.payTypeText.GetComponent<RectTransform>();
                    
                    // Check if we need to add a background image if it doesn't have one
                    Image payImg = ui.payTypeText.GetComponent<Image>();
                    if (payImg == null && payRT.parent != null)
                    {
                        // Check parent for background (standard VaM badge pattern)
                        payImg = payRT.parent.GetComponent<Image>();
                    }
                    
                    // If no background, add one to make it look like a badge
                    if (payImg == null)
                    {
                        payImg = ui.payTypeText.gameObject.AddComponent<Image>();
                        // Give it some padding
                        payRT.sizeDelta = new Vector2(payRT.sizeDelta.x + 20, payRT.sizeDelta.y + 10);
                    }

                    if (payImg != null)
                    {
                        payImg.enabled = true;
                        if (payTypeJSON.val.ToLower().Contains("free"))
                            payImg.color = new Color(0.2f, 0.6f, 0.2f, 1f); // Darker green for free
                        else
                            payImg.color = new Color(0.5f, 0f, 0.5f, 1f); // Dark magenta/purple for paid
                    }
                    
                    ui.payTypeText.alignment = TextAnchor.MiddleCenter;
                    ui.payTypeText.color = Color.white;
                    ui.payTypeText.fontStyle = FontStyle.Bold;
                }
                categoryJSON.text = ui.categoryText;
                // Hide the original category text to avoid "Free: Looks Looks"
                if (ui.categoryText != null)
                {
                    ui.categoryText.gameObject.SetActive(false);
                }
                payTypeAndCategorySelectAction.button = ui.payTypeAndCategorySelectButton;
                creatorSelectAction.button = ui.creatorSelectButton;
                creatorJSON.text = ui.creatorText;
                creatorIconImage = ui.creatorIconImage;
                if (creatorIconImage != null && creatorIconTexture != null)
                {
                    creatorIconImage.texture = creatorIconTexture;
                }
                thumbnailImage = ui.thumbnailImage;
                if (thumbnailImage != null && thumbnailTexture != null)
                {
                    thumbnailImage.texture = thumbnailTexture;
                }
                hubDownloadableJSON.indicator = ui.hubDownloadableIndicator;
                hubDownloadableJSON.negativeIndicator = ui.hubDownloadableNegativeIndicator;
                hubHostedJSON.indicator = ui.hubHostedIndicator;
                hubHostedJSON.negativeIndicator = ui.hubHostedNegativeIndicator;
                hasDependenciesJSON.indicator = ui.hasDependenciesIndicator;
                hasDependenciesJSON.negativeIndicator = ui.hasDependenciesNegativeIndicator;
                dependencyCountJSON.text = ui.dependencyCountText;
                downloadCountJSON.text = ui.downloadCountText;
                ratingsCountJSON.text = ui.ratingsCountText;
                ratingJSON.slider = ui.ratingSlider;
                lastUpdateJSON.text = ui.lastUpdateText;
                openDetailAction.button = ui.openDetailButton;
                quickDownloadAllAction.button = EnsureQuickDownloadButton(ui);

                inLibraryJSON.indicator = ui.inLibraryIndicator;
                updateAvailableJSON.indicator = ui.updateAvailableIndicator;

                updateMsgJSON.text = ui.updateMsgText;
                SyncQuickDownloadCtaVisibility();
            }
        }
    }

}
