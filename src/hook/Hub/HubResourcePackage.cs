using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VPB
{
    public class HubResourcePackage
    {
        protected HubBrowse browser;

        private static readonly string[] SizeSuffixes = new string[9] { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        protected string package_id;

        protected string resource_id;

        protected string resolvedVarName;

        protected JSONStorableAction goToResourceAction;

        protected JSONStorableBool isDependencyJSON;

        protected JSONStorableString nameJSON;

        protected JSONStorableString licenseTypeJSON;
        protected string licenseTypeValue;
        protected Text licenseTypeTextUI;
        protected Color defaultLicenseTypeTextColor = Color.white;
        protected Image licenseTypeBackgroundUI;
        protected RectTransform licenseTypeBackgroundRect;
        protected Vector2 licenseTypeBackgroundBasePosition;
        protected Shadow licenseTypeBackgroundShadowUI;
        protected Sprite licenseTypeChipSprite;
        protected Color defaultLicenseTypeBackgroundColor = new Color(0f, 0f, 0f, 0f);
        protected FontStyle defaultLicenseTypeFontStyle = FontStyle.Normal;
        protected Outline licenseTypeOutlineUI;

        protected JSONStorableString fileSizeJSON;

        protected JSONStorableBool alreadyHaveJSON;

        protected JSONStorableString updateMsgJSON;

        protected JSONStorableBool updateAvailableJSON;

        protected JSONStorableBool alreadyHaveSceneJSON;

        protected string alreadyHaveScenePath;

        protected JSONStorableBool notOnHubJSON;

        public string promotionalUrl;

        protected string downloadUrl;

        protected string latestUrl;

        protected string localPackagePath;

        protected string thumbnailUrl;
        public Texture2D thumbnailTexture;
        protected HubImageLoaderThreaded.QueuedImage thumbnailQueuedImage;
        protected RawImage thumbnailImageUI;
        public RawImage mainThumbnailImage;
        public Texture originalMainThumbnailTexture;
        protected DependencyThumbnailHover hoverHandler;
        private const float ThumbnailRetryIntervalSeconds = 1.25f;
        private const float ThumbnailInFlightStaleSeconds = 20f;
        private float _lastThumbnailQueueTime = -10f;
        private bool _thumbnailRequestInFlight;
        private string _localThumbnailPath;
        private bool _localThumbnailResolved;
        private bool _localThumbnailTried;
        private int _cacheInvalidateAttempts;

        protected JSONStorableFloat downloadProgressJSON;

        protected JSONStorableBool isDownloadQueuedJSON;

        protected JSONStorableBool isDownloadingJSON;

        protected JSONStorableBool isDownloadedJSON;

        protected JSONStorableAction downloadAction;

        protected JSONStorableAction updateAction;

        protected Button deleteButton;
        protected Button deleteSceneButton;
        protected Button updateDeleteButton;
        private const long LargeDeleteConfirmationThresholdBytes = 128L * 1024L * 1024L;
        protected RectTransform alreadyHaveIndicatorRect;
        protected RectTransform deleteButtonRect;
        protected RectTransform alreadyHaveSceneIndicatorRect;
        protected RectTransform deleteSceneButtonRect;
        protected RectTransform updateDeleteButtonRect;

        protected JSONStorableAction openInPackageManagerAction;

        protected JSONStorableAction openSceneAction;

        public string GroupName { get; protected set; }

        public string Creator { get; protected set; }

        public string Version { get; protected set; }

        public int LatestVersion { get; protected set; }

        public string Category { get; protected set; }

        public RectTransform RowTransform { get; private set; }

        public Action CategoryChanged;

        public bool IsDependency
        {
            get { return isDependencyJSON != null && isDependencyJSON.val; }
        }

        public string Name
        {
            get
            {
                return nameJSON.val;
            }
        }

        public string ResourceId
        {
            get
            {
                return resource_id;
            }
        }

        public string ThumbnailUrl
        {
            get
            {
                return thumbnailUrl;
            }
        }

        public string LicenseType
        {
            get
            {
                return licenseTypeValue;
            }
        }

        public int FileSize { get; protected set; }

        public bool NeedsDownload
        {
            get
            {
                return !alreadyHaveJSON.val || updateAvailableJSON.val;
            }
        }

        public bool HasValidDownloadUrl
        {
            get
            {
                if (downloadUrl == null) return false;
                if (downloadUrl == string.Empty) return false;
                if (downloadUrl == "null") return false;
                return true;
            }
        }

        public bool IsDownloading
        {
            get
            {
                return isDownloadingJSON.val;
            }
        }

        public bool IsDownloadQueued
        {
            get
            {
                return isDownloadQueuedJSON.val;
            }
        }

        public float DownloadProgress01
        {
            get
            {
                return downloadProgressJSON.val;
            }
        }

        public void SetMainThumbnail(RawImage mainThumbnail)
        {
            mainThumbnailImage = mainThumbnail;
            if (mainThumbnailImage != null && mainThumbnailImage.texture != null)
            {
                originalMainThumbnailTexture = mainThumbnailImage.texture;
            }
        }

        public HubResourcePackage(JSONClass package, HubBrowse hubBrowse, bool isDependency)
        {
            browser = hubBrowse;
            package_id = package["package_id"];
            resource_id = package["resource_id"];
            // Derive CDN thumbnail URL from resource_id when available
            int ridInt;
            if (!string.IsNullOrEmpty(resource_id) && resource_id != "null" && int.TryParse(resource_id, out ridInt) && ridInt > 0)
                thumbnailUrl = $"https://1424104733.rsc.cdn77.org/data/resource_icons/{ridInt / 1000}/{ridInt}.jpg";
            string input = package["filename"];
            input = Regex.Replace(input, ".var$", string.Empty);
            GroupName = Regex.Replace(input, "(.*)\\..*", "$1");
            Creator = Regex.Replace(GroupName, "(.*)\\..*", "$1");
            Version = package["version"];
            if (Version == null)
            {
                Version = Regex.Replace(input, ".*\\.([0-9]+)$", "$1");
            }
            resolvedVarName = GroupName + "." + Version + ".var";
            string text = package["latest_version"];
            if (text == null)
            {
                text = Version;
            }
            if (text != null)
            {
                int result;
                if (int.TryParse(text, out result))
                {
                    LatestVersion = result;
                }
                else
                {
                    LatestVersion = -1;
                }
            }
            licenseTypeValue = package["licenseType"];
            Category = ExtractPackageCategory(package);
            string s = package["file_size"];
            SyncFileSize(s);
            string startingValue2 = SizeSuffix(FileSize);
            downloadUrl = package["downloadUrl"];
            if (downloadUrl == null)
            {
                downloadUrl = package["urlHosted"];
            }
            latestUrl = package["latestUrl"];
            if (latestUrl == null)
            {
                latestUrl = downloadUrl;
            }
            bool startingValue3 = downloadUrl == "null";
            promotionalUrl = package["promotional_link"];
            goToResourceAction = new JSONStorableAction("GoToResource", GoToResource);
            isDependencyJSON = new JSONStorableBool("isDependency", isDependency);
            nameJSON = new JSONStorableString("name", input);
            licenseTypeJSON = new JSONStorableString("licenseType", FormatCategoryAndLicense(Category, licenseTypeValue, true));
            fileSizeJSON = new JSONStorableString("fileSize", startingValue2);
            alreadyHaveJSON = new JSONStorableBool("alreadyHave", false);
            alreadyHaveSceneJSON = new JSONStorableBool("alreadyHaveScene", false);
            updateAvailableJSON = new JSONStorableBool("updateAvailable", false);
            updateMsgJSON = new JSONStorableString("updateMsg", "Direct Update");
            updateAction = new JSONStorableAction("Update", Update);
            notOnHubJSON = new JSONStorableBool("notOnHub", startingValue3);
            downloadAction = new JSONStorableAction("Download", Download);
            isDownloadQueuedJSON = new JSONStorableBool("isDownloadQueued", false);
            isDownloadingJSON = new JSONStorableBool("isDownloading", false);
            isDownloadedJSON = new JSONStorableBool("isDownloaded", false);
            downloadProgressJSON = new JSONStorableFloat("downloadProgress", 0f, 0f, 1f, true, false);
            openInPackageManagerAction = new JSONStorableAction("OpenInPackageManager", OpenInPackageManager);
            openSceneAction = new JSONStorableAction("OpenScene", OpenScene);
            Refresh();
        }

        private static string SizeSuffix(int value, int decimalPlaces = 1)
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

        private static string ExtractPackageCategory(JSONClass package)
        {
            if (package == null) return string.Empty;
            string value = package["type"];
            if (string.IsNullOrEmpty(value) || value == "null") value = package["category"];
            if (string.IsNullOrEmpty(value) || value == "null") value = package["resource_type"];
            if (string.IsNullOrEmpty(value) || value == "null") return string.Empty;
            return value.Trim();
        }

        private static string FormatCategoryAndLicense(string category, string license, bool showCategory)
        {
            bool hasCategory = !string.IsNullOrEmpty(category) && category != "null";
            bool hasLicense = !string.IsNullOrEmpty(license) && license != "null";
            if (showCategory && hasCategory) return category;
            return hasLicense ? license : string.Empty;
        }

        private void SyncCategory(string category)
        {
            if (string.IsNullOrEmpty(category) || category == "null") return;
            Category = category.Trim();
            SyncLicenseCategoryText();
            if (CategoryChanged != null) CategoryChanged();
        }

        public void SetCategory(string category)
        {
            SyncCategory(category);
        }

        public void SetLicenseColumnShowsCategory(bool showCategory)
        {
            showCategoryInLicenseColumn = showCategory;
            SyncLicenseCategoryText();
        }

        protected bool showCategoryInLicenseColumn = true;
        private static readonly Dictionary<string, Color> CategoryTextColorCache = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        protected void SyncLicenseCategoryText()
        {
            licenseTypeJSON.val = FormatCategoryAndLicense(Category, licenseTypeValue, showCategoryInLicenseColumn);
            SyncLicenseCategoryTextStyle();
        }

        private void SyncLicenseCategoryTextStyle()
        {
            if (licenseTypeTextUI == null) return;
            if (showCategoryInLicenseColumn && !string.IsNullOrEmpty(Category) && Category != "null")
            {
                Color categoryColor = ResolveCategoryTextColor(Category, 0.9f);
                ApplyCategoryChipStyle(categoryColor);
                return;
            }
            ApplyDefaultLicenseStyle();
        }

        private void ApplyCategoryChipStyle(Color backgroundColor)
        {
            if (licenseTypeBackgroundUI != null)
            {
                licenseTypeBackgroundUI.enabled = true;
                licenseTypeBackgroundUI.color = backgroundColor;
            }
            if (licenseTypeBackgroundRect != null)
            {
                // Slight offset gives it a native button-like feel.
                licenseTypeBackgroundRect.anchoredPosition = licenseTypeBackgroundBasePosition + new Vector2(1f, -1f);
            }
            if (licenseTypeBackgroundShadowUI != null)
            {
                licenseTypeBackgroundShadowUI.enabled = true;
                licenseTypeBackgroundShadowUI.effectDistance = new Vector2(0.8f, -0.8f);
                licenseTypeBackgroundShadowUI.effectColor = new Color(0f, 0f, 0f, 0.28f);
            }

            float luminance = (backgroundColor.r * 0.2126f) + (backgroundColor.g * 0.7152f) + (backgroundColor.b * 0.0722f);
            licenseTypeTextUI.color = luminance > 0.58f
                ? new Color(0.08f, 0.08f, 0.08f, defaultLicenseTypeTextColor.a)
                : new Color(0.98f, 0.98f, 0.98f, defaultLicenseTypeTextColor.a);
            licenseTypeTextUI.fontStyle = FontStyle.Bold;

            if (licenseTypeOutlineUI != null)
            {
                licenseTypeOutlineUI.enabled = true;
                licenseTypeOutlineUI.effectDistance = new Vector2(1f, -1f);
                licenseTypeOutlineUI.effectColor = luminance > 0.58f
                    ? new Color(1f, 1f, 1f, 0.22f)
                    : new Color(0f, 0f, 0f, 0.45f);
            }
        }

        private void ApplyDefaultLicenseStyle()
        {
            licenseTypeTextUI.color = defaultLicenseTypeTextColor;
            licenseTypeTextUI.fontStyle = defaultLicenseTypeFontStyle;

            if (licenseTypeBackgroundUI != null)
            {
                licenseTypeBackgroundUI.color = defaultLicenseTypeBackgroundColor;
                licenseTypeBackgroundUI.enabled = defaultLicenseTypeBackgroundColor.a > 0.001f;
            }
            if (licenseTypeBackgroundRect != null)
            {
                licenseTypeBackgroundRect.anchoredPosition = licenseTypeBackgroundBasePosition;
            }
            if (licenseTypeBackgroundShadowUI != null)
            {
                licenseTypeBackgroundShadowUI.enabled = false;
            }

            if (licenseTypeOutlineUI != null)
            {
                licenseTypeOutlineUI.enabled = false;
            }
        }

        private static Color ResolveCategoryTextColor(string category, float alpha)
        {
            if (string.IsNullOrEmpty(category)) return new Color(1f, 1f, 1f, alpha);

            Color cachedColor;
            if (CategoryTextColorCache.TryGetValue(category, out cachedColor))
            {
                cachedColor.a = alpha;
                return cachedColor;
            }

            // Deterministic hash so each category keeps a stable color across sessions.
            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(category);
            float hue = Mathf.Abs(hash % 360) / 360f;

            // Slightly darker/saturated colors work better as chip backgrounds.
            Color generated = Color.HSVToRGB(hue, 0.62f, 0.68f);
            generated.a = alpha;
            CategoryTextColorCache[category] = generated;
            return generated;
        }

        protected void GoToResource()
        {
            if (resource_id != "null")
            {
                browser.OpenDetail(resource_id);
            }
        }

        protected void SyncFileSize(string s)
        {
            int result;
            if (int.TryParse(s, out result))
            {
                FileSize = result;
            }
        }

        protected void DownloadStarted()
        {
            isDownloadQueuedJSON.val = false;
            isDownloadingJSON.val = true;
        }

        protected void DownloadProgress(float f,ulong downloadedBytes)
        {
            downloadProgressJSON.val = f;

            fileSizeJSON.val = string.Format("{0}/{1}", SizeSuffix((int)downloadedBytes, 2), SizeSuffix(FileSize));
        }

        protected void DownloadComplete(byte[] data, Dictionary<string, string> responseHeaders)
        {
            fileSizeJSON.val = SizeSuffix(FileSize);

            isDownloadingJSON.val = false;
            isDownloadedJSON.val = true;
            string value;
            string text;
            if (responseHeaders.TryGetValue("Content-Disposition", out value))
            {
                value = Regex.Replace(value, ";$", string.Empty);
                text = Regex.Replace(value, ".*filename=\"?([^\"]+)\"?.*", "$1");
            }
            else
            {
                text = resolvedVarName;
            }
            try
            {
                FileManager.CreateDirectory("AddonPackages");
                FileManager.WriteAllBytes("AddonPackages/" + text, data);
                localPackagePath = "AddonPackages/" + text;
                alreadyHaveJSON.val = true;
                updateAvailableJSON.val = false;
                isDownloadedJSON.val = false;
                SyncDeleteButton();

                if (browser != null)
                {
                    // Defer heavy refresh work until the download queue drains.
                    browser.DeferRefreshUntilQueueDrains();
                }
            }
            catch (Exception)
            {
                LogUtil.Log("Error while trying to save file AddonPackages/" + text + " after download");
                isDownloadQueuedJSON.val = false;
                isDownloadingJSON.val = false;
            }
        }

        protected void DownloadError(string err)
        {
            isDownloadQueuedJSON.val = false;
            isDownloadingJSON.val = false;
            LogUtil.Log("Error while downloading " + Name + ": " + err);
        }
 
        protected void SyncThumbnailTexture(HubImageLoaderThreaded.QueuedImage qi)
        {
            _thumbnailRequestInFlight = false;
            thumbnailQueuedImage = null;
            thumbnailTexture = qi.tex;
            if (thumbnailTexture != null)
            {
                _cacheInvalidateAttempts = 0;
                if (thumbnailImageUI != null)
                {
                    thumbnailImageUI.texture = thumbnailTexture;
                    thumbnailImageUI.color = Color.white;
                }
                
                // If we are currently hovered, update the main thumbnail too
                var hover = hoverHandler;
                if (hover != null && hover.IsHovered && hover.package == this)
                {
                    hover.UpdateMainThumbnail(thumbnailTexture);
                }
            }
            else
            {
                // If a cached web thumb is bad (shows as permanent gray / decode fail), invalidate once and retry.
                if (!string.IsNullOrEmpty(qi.imgPath)
                    && Regex.IsMatch(qi.imgPath, "^http")
                    && qi.hadError
                    && !qi.cancel
                    && (!string.IsNullOrEmpty(qi.errorText) &&
                        (qi.errorText.IndexOf("cache", StringComparison.OrdinalIgnoreCase) >= 0
                        || qi.errorText.IndexOf("loadrawtexturedata", StringComparison.OrdinalIgnoreCase) >= 0
                        || qi.errorText.IndexOf("decode", StringComparison.OrdinalIgnoreCase) >= 0
                        || qi.errorText.IndexOf("empty", StringComparison.OrdinalIgnoreCase) >= 0))
                    && _cacheInvalidateAttempts < 1
                    && HubImageLoaderThreaded.singleton != null)
                {
                    _cacheInvalidateAttempts++;
                    HubImageLoaderThreaded.singleton.InvalidateWebCache(qi.imgPath);
                    EnsureThumbnailQueued(true);
                }
            }
        }

        private static bool IsImagePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.ToLowerInvariant();
            return p.EndsWith(".jpg") || p.EndsWith(".jpeg") || p.EndsWith(".png");
        }

        private string ResolveLocalThumbnailPath()
        {
            if (_localThumbnailResolved) return _localThumbnailPath;
            _localThumbnailResolved = true;
            _localThumbnailPath = null;

            VarPackage pkg = ResolveLocalPackage();
            if (pkg == null || string.IsNullOrEmpty(pkg.Path)) return null;

            try
            {
                List<string> names;
                List<long> ticks;
                List<long> sizes;
                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null) return null;

                string chosen = null;
                for (int i = 0; i < names.Count; i++)
                {
                    string n = names[i];
                    if (!IsImagePath(n)) continue;
                    string ln = n.ToLowerInvariant();
                    if (ln.Contains("preview") || ln.Contains("thumbnail") || ln.Contains("thumb") || ln.Contains("screenshot"))
                    {
                        chosen = n;
                        break;
                    }
                }
                if (chosen == null)
                {
                    for (int i = 0; i < names.Count; i++)
                    {
                        if (IsImagePath(names[i]))
                        {
                            chosen = names[i];
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(chosen))
                {
                    _localThumbnailPath = pkg.Path + ":/" + chosen.Replace('\\', '/');
                }
            }
            catch { }

            return _localThumbnailPath;
        }
 
        protected void LoadThumbnail()
        {
            EnsureThumbnailQueued(true);
        }

        internal void EnsureThumbnailQueued(bool immediateQueue)
        {
            float now = Time.realtimeSinceStartup;
            if (HubImageLoaderThreaded.singleton == null)
            {
                return;
            }

            // Prefer local package preview image when available.
            if (!_localThumbnailTried)
            {
                string localPath = ResolveLocalThumbnailPath();
                if (!string.IsNullOrEmpty(localPath) && FileManager.FileExists(localPath))
                {
                    _localThumbnailTried = true;
                    HubImageLoaderThreaded.QueuedImage localQi = HubImageLoaderThreaded.singleton.GetQI();
                    localQi.imgPath = localPath;
                    localQi.isThumbnail = true;
                    string localIdPart = !string.IsNullOrEmpty(resource_id) && resource_id != "null"
                        ? resource_id
                        : (Name ?? "unknown");
                    localQi.groupId = "depThumbLocal:" + localIdPart;
                    localQi.callback = SyncThumbnailTexture;
                    thumbnailQueuedImage = localQi;
                    _thumbnailRequestInFlight = true;
                    if (immediateQueue) HubImageLoaderThreaded.singleton.QueueThumbnailImmediate(localQi);
                    else HubImageLoaderThreaded.singleton.QueueThumbnail(localQi);
                    return;
                }
                _localThumbnailTried = true;
            }

            if (string.IsNullOrEmpty(thumbnailUrl))
            {
                return;
            }

            // Already loaded: keep UI synced.
            if (thumbnailTexture != null)
            {
                SyncThumbnailTexture(new HubImageLoaderThreaded.QueuedImage { tex = thumbnailTexture, imgPath = thumbnailUrl });
                return;
            }

            // Keep in-flight requests alive, but recover if they go stale.
            if (_thumbnailRequestInFlight)
            {
                float inFlightAge = now - _lastThumbnailQueueTime;
                if (inFlightAge < ThumbnailInFlightStaleSeconds)
                {
                    if (thumbnailQueuedImage != null) thumbnailQueuedImage.cancel = false;
                    return;
                }

                // Stale in-flight marker (can happen when pooled queued-image refs are recycled).
                _thumbnailRequestInFlight = false;
                thumbnailQueuedImage = null;
            }

            // Prevent hot-looping when a request fails (network/cache).
            if (now - _lastThumbnailQueueTime < ThumbnailRetryIntervalSeconds)
            {
                return;
            }
            _lastThumbnailQueueTime = now;

            HubImageLoaderThreaded.QueuedImage qi = HubImageLoaderThreaded.singleton.GetQI();
            qi.imgPath = thumbnailUrl;
            qi.isThumbnail = true;
            string idPart = !string.IsNullOrEmpty(resource_id) && resource_id != "null"
                ? resource_id
                : (Name ?? "unknown");
            qi.groupId = "depThumb:" + idPart;
            qi.callback = SyncThumbnailTexture;
            thumbnailQueuedImage = qi;
            _thumbnailRequestInFlight = true;
            if (immediateQueue) HubImageLoaderThreaded.singleton.QueueThumbnailImmediate(qi);
            else HubImageLoaderThreaded.singleton.QueueThumbnail(qi);
        }
 
        HubBrowse.DownloadRequest request;
        public void Download()
        {
            if (browser != null && downloadUrl != null && downloadUrl != string.Empty && downloadUrl != "null" && !isDownloadQueuedJSON.val && (!alreadyHaveJSON.val || updateAvailableJSON.val))
            {
                if (!alreadyHaveJSON.val)
                {
                    isDownloadQueuedJSON.val = true;
                    request=browser.QueueDownload(downloadUrl, promotionalUrl, DownloadStarted, DownloadProgress, DownloadComplete, DownloadError);
                }
                else if (updateAvailableJSON.val && latestUrl != null && latestUrl != string.Empty && latestUrl != "null")
                {
                    isDownloadQueuedJSON.val = true;
                    request = browser.QueueDownload(latestUrl, promotionalUrl, DownloadStarted, DownloadProgress, DownloadComplete, DownloadError);
                }
            }
        }

        public void Update()
        {
            if (browser != null && latestUrl != null && latestUrl != string.Empty && !isDownloadQueuedJSON.val && updateAvailableJSON.val)
            {
                isDownloadQueuedJSON.val = true;
                request = browser.QueueDownload(latestUrl, promotionalUrl, DownloadStarted, DownloadProgress, DownloadComplete, DownloadError);
            }
        }

        public void OpenInPackageManager()
        {
            // Don't auto-install from Hub UI inspection.
            VarPackage package = FileManager.GetPackage(nameJSON.val, ensureInstalled: false);
            if (package != null)
            {
                //SuperController.singleton.OpenPackageInManager(nameJSON.val);
            }
        }

        protected void OpenScene()
        {
            if (alreadyHaveScenePath != null)
            {
                //SuperController.singleton.Load(alreadyHaveScenePath);
            }
        }

        public void Refresh()
        {
            isDownloadedJSON.val = false;
            VarPackage package = null;
            if (isDependencyJSON.val)
            {
                package = FileManager.GetPackage(nameJSON.val, ensureInstalled: false);
            }
            else
            {
                string text = FileManager.PackageIDToPackageGroupID(nameJSON.val);
                string packageUidOrPath = text + ".latest";
                package = FileManager.GetPackage(packageUidOrPath, ensureInstalled: false);
            }
            if (package != null)
            {
                alreadyHaveJSON.val = true;
                if ((Version == "latest" || !isDependencyJSON.val) && LatestVersion != -1)
                {
                    if (package.Version < LatestVersion)
                    {
                        updateAvailableJSON.val = true;
                        updateMsgJSON.val = "Direct Update " + package.Version + " -> " + LatestVersion;
                    }
                    else
                    {
                        updateAvailableJSON.val = false;
                    }
                }
                else
                {
                    updateAvailableJSON.val = false;
                }
                List<FileEntry> list = new List<FileEntry>();
                package.FindFiles("Saves/scene", "*.json", list);
                if (list.Count > 0)
                {
                    FileEntry fileEntry = list[0];
                    alreadyHaveScenePath = fileEntry.Uid;
                    alreadyHaveSceneJSON.val = true;
                }
                else
                {
                    alreadyHaveScenePath = null;
                    alreadyHaveSceneJSON.val = false;
                }
            }
            else
            {
                alreadyHaveJSON.val = false;
                alreadyHaveScenePath = null;
                alreadyHaveSceneJSON.val = false;
            }
            SyncDeleteButton();
        }

        private VarPackage ResolveLocalPackage()
        {
            try
            {
                if (isDependencyJSON.val)
                {
                    return FileManager.GetPackage(nameJSON.val, ensureInstalled: false);
                }
                string packageGroupId = FileManager.PackageIDToPackageGroupID(nameJSON.val);
                return FileManager.GetPackage(packageGroupId + ".latest", ensureInstalled: false);
            }
            catch
            {
                return null;
            }
        }

        private void SyncDeleteButton()
        {
            bool canDelete = alreadyHaveJSON != null && alreadyHaveJSON.val && !isDownloadingJSON.val && !isDownloadQueuedJSON.val;
            if (deleteButton != null) deleteButton.gameObject.SetActive(canDelete);
            if (deleteSceneButton != null) deleteSceneButton.gameObject.SetActive(canDelete);
            if (updateDeleteButton != null) updateDeleteButton.gameObject.SetActive(canDelete && updateAvailableJSON != null && updateAvailableJSON.val);
        }

        private void ConfigureHalfWidthIndicatorLabel(GameObject indicator, string fromText, string toText)
        {
            if (indicator == null) return;
            Text[] texts = indicator.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null)
                {
                    if (texts[i].text == fromText)
                    {
                        texts[i].text = toText;
                    }
                    else if (!string.IsNullOrEmpty(fromText) && texts[i].text != null && texts[i].text.Replace("\r\n", "\n") == fromText.Replace("\r\n", "\n"))
                    {
                        texts[i].text = toText;
                    }
                }
                RectTransform textRect = texts[i].GetComponent<RectTransform>();
                if (textRect != null)
                {
                    textRect.anchorMin = Vector2.zero;
                    textRect.anchorMax = new Vector2(0.5f, 1f);
                    textRect.offsetMin = Vector2.zero;
                    textRect.offsetMax = Vector2.zero;
                    texts[i].alignment = TextAnchor.MiddleCenter;
                }
            }
        }

        private Button CreateHalfWidthDeleteButton(GameObject indicator, RectTransform deleteSource, out RectTransform deleteRect)
        {
            deleteRect = null;
            if (indicator == null || deleteSource == null) return null;

            RectTransform deleteObj = UnityEngine.Object.Instantiate(deleteSource, indicator.transform) as RectTransform;
            deleteObj.gameObject.SetActive(false);
            deleteRect = deleteObj;
            deleteRect.anchorMin = new Vector2(0.5f, 0f);
            deleteRect.anchorMax = Vector2.one;
            deleteRect.pivot = new Vector2(0.5f, 0.5f);
            deleteRect.offsetMin = Vector2.zero;
            deleteRect.offsetMax = Vector2.zero;
            deleteRect.localScale = Vector3.one;
            LayoutElement layout = deleteObj.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;
            Transform deleteText = deleteObj.Find("Text");
            if (deleteText != null)
            {
                Text textComponent = deleteText.GetComponent<Text>();
                if (textComponent != null)
                {
                    textComponent.text = "X";
                    textComponent.alignment = TextAnchor.MiddleCenter;
                    RectTransform textRect = textComponent.GetComponent<RectTransform>();
                    if (textRect != null)
                    {
                        textRect.anchorMin = Vector2.zero;
                        textRect.anchorMax = Vector2.one;
                        textRect.offsetMin = Vector2.zero;
                        textRect.offsetMax = Vector2.zero;
                    }
                }
            }
            deleteObj.GetComponent<Image>().color = new Color32(255, 122, 122, 255);
            Button button = deleteObj.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(DeleteLocalPackage);
            return button;
        }

        private void ConfigureHalfWidthButtonLabel(RectTransform buttonRect)
        {
            if (buttonRect == null) return;
            Text[] texts = buttonRect.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Text txt = texts[i];
                if (txt == null) continue;
                RectTransform textRect = txt.GetComponent<RectTransform>();
                if (textRect == null) continue;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = new Vector2(0.5f, 1f);
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
                txt.alignment = TextAnchor.MiddleCenter;
            }
        }

        private void DeleteLocalPackage()
        {
            VarPackage package = ResolveLocalPackage();
            string uid = package != null && !string.IsNullOrEmpty(package.Uid) ? package.Uid : Name;
            string sourcePath = package != null ? package.Path : localPackagePath;
            if (string.IsNullOrEmpty(sourcePath))
            {
                LogUtil.LogWarning("[VPB] Hub delete: local package not found for " + Name);
                Refresh();
                return;
            }

            sourcePath = sourcePath.Replace('\\', '/');
            if (!File.Exists(sourcePath))
            {
                LogUtil.LogWarning("[VPB] Hub delete: file not found for " + uid + " at " + sourcePath);
                Refresh();
                return;
            }

            long fileSizeBytes = 0L;
            try
            {
                fileSizeBytes = new FileInfo(sourcePath).Length;
            }
            catch { }

            if (fileSizeBytes > LargeDeleteConfirmationThresholdBytes && SuperController.singleton != null)
            {
                string sizeText = SizeSuffix((int)Math.Min(fileSizeBytes, int.MaxValue), 2);
                string message = "This package is " + sizeText + ".\n\nDelete permanently from disk?\n\nConfirm = Permanent delete\nCancel = Move to DeletedPackages";
                try
                {
                    SuperController.singleton.Alert(message, () =>
                    {
                        PermanentlyDeletePackage(package, uid, sourcePath);
                    }, () =>
                    {
                        MovePackageToDeletedPackages(package, uid, sourcePath);
                    });
                    return;
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB] Hub delete: confirmation dialog failed for " + uid + ": " + ex.Message);
                    // Continue with default permanent delete when prompt fails.
                }
            }

            PermanentlyDeletePackage(package, uid, sourcePath);
        }

        private void PermanentlyDeletePackage(VarPackage package, string uid, string sourcePath)
        {
            try
            {
                if (package != null) package.CloseZipFile();
                File.Delete(sourcePath);
                if (package != null) try { FileManager.UnregisterPackage(package); } catch { }
                localPackagePath = null;
                LogUtil.Log("[VPB] Hub delete: permanently deleted " + uid);

                try { FileManager.Refresh(); } catch { }
                try { if (MVR.FileManagement.FileManager.singleton != null) MVR.FileManagement.FileManager.Refresh(); } catch { }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Hub delete failed for " + uid + ": " + ex.Message);
            }
            Refresh();
        }

        private void MovePackageToDeletedPackages(VarPackage package, string uid, string sourcePath)
        {
            try
            {
                string deletedDir = Path.Combine(Directory.GetCurrentDirectory(), "DeletedPackages");
                if (!Directory.Exists(deletedDir)) Directory.CreateDirectory(deletedDir);

                string fileName = Path.GetFileName(sourcePath);
                if (string.IsNullOrEmpty(fileName)) fileName = uid + ".var";

                string destinationPath = Path.Combine(deletedDir, fileName);
                if (File.Exists(destinationPath))
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    destinationPath = Path.Combine(
                        deletedDir,
                        Path.GetFileNameWithoutExtension(fileName) + "__" + stamp + ".var");
                }

                if (package != null) package.CloseZipFile();
                File.Move(sourcePath, destinationPath);
                if (package != null) try { FileManager.UnregisterPackage(package); } catch { }
                localPackagePath = null;
                LogUtil.Log("[VPB] Hub delete: moved " + uid + " to DeletedPackages");

                try { FileManager.Refresh(); } catch { }
                try { if (MVR.FileManagement.FileManager.singleton != null) MVR.FileManagement.FileManager.Refresh(); } catch { }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Hub delete failed for " + uid + ": " + ex.Message);
            }
            Refresh();
        }

        public void RegisterUI(HubResourcePackageUI ui)
        {
            if (ui != null)
            {
                ui.connectedItem = this;
                RowTransform = ui.GetComponent<RectTransform>();
                goToResourceAction.button = ui.resourceButton;
                if (ui.resourceButton != null)
                {
                    ui.resourceButton.interactable = !notOnHubJSON.val && isDependencyJSON.val;
                }
                isDependencyJSON.indicator = ui.isDependencyIndicator;
                nameJSON.text = ui.nameText;
                licenseTypeJSON.text = ui.licenseTypeText;
                licenseTypeTextUI = ui.licenseTypeText;
                if (licenseTypeTextUI != null)
                {
                    defaultLicenseTypeTextColor = licenseTypeTextUI.color;
                    defaultLicenseTypeFontStyle = licenseTypeTextUI.fontStyle;
                    Image chipTemplateImage = ui.downloadButton != null ? ui.downloadButton.GetComponent<Image>() : null;
                    if (chipTemplateImage == null && ui.updateButton != null)
                    {
                        chipTemplateImage = ui.updateButton.GetComponent<Image>();
                    }
                    if (chipTemplateImage != null)
                    {
                        licenseTypeChipSprite = chipTemplateImage.sprite;
                    }
                    RectTransform textRect = licenseTypeTextUI.GetComponent<RectTransform>();
                    if (textRect != null && textRect.parent != null)
                    {
                        Transform existingChip = textRect.parent.Find("__VPBCategoryChipBg");
                        GameObject chipObj = existingChip != null ? existingChip.gameObject : null;
                        if (chipObj == null)
                        {
                            chipObj = new GameObject("__VPBCategoryChipBg", typeof(RectTransform), typeof(Image));
                            chipObj.transform.SetParent(textRect.parent, false);
                        }

                        licenseTypeBackgroundRect = chipObj.GetComponent<RectTransform>();
                        licenseTypeBackgroundRect.anchorMin = textRect.anchorMin;
                        licenseTypeBackgroundRect.anchorMax = textRect.anchorMax;
                        licenseTypeBackgroundRect.pivot = textRect.pivot;
                        licenseTypeBackgroundRect.anchoredPosition = textRect.anchoredPosition;
                        licenseTypeBackgroundRect.sizeDelta = textRect.sizeDelta + new Vector2(12f, 6f);
                        licenseTypeBackgroundRect.localScale = textRect.localScale;
                        licenseTypeBackgroundRect.SetSiblingIndex(textRect.GetSiblingIndex());
                        licenseTypeBackgroundBasePosition = licenseTypeBackgroundRect.anchoredPosition;

                        licenseTypeBackgroundUI = chipObj.GetComponent<Image>();
                    }
                    if (licenseTypeBackgroundUI != null)
                    {
                        if (licenseTypeChipSprite != null)
                        {
                            licenseTypeBackgroundUI.sprite = licenseTypeChipSprite;
                            licenseTypeBackgroundUI.type = Image.Type.Sliced;
                        }
                        licenseTypeBackgroundUI.raycastTarget = false;
                        defaultLicenseTypeBackgroundColor = licenseTypeBackgroundUI.color;
                        licenseTypeBackgroundUI.enabled = false;
                        licenseTypeBackgroundShadowUI = licenseTypeBackgroundUI.GetComponent<Shadow>();
                        if (licenseTypeBackgroundShadowUI == null)
                        {
                            licenseTypeBackgroundShadowUI = licenseTypeBackgroundUI.gameObject.AddComponent<Shadow>();
                        }
                        if (licenseTypeBackgroundShadowUI != null)
                        {
                            licenseTypeBackgroundShadowUI.enabled = false;
                            licenseTypeBackgroundShadowUI.useGraphicAlpha = true;
                        }
                    }
                    licenseTypeOutlineUI = licenseTypeTextUI.GetComponent<Outline>();
                    if (licenseTypeOutlineUI == null)
                    {
                        licenseTypeOutlineUI = licenseTypeTextUI.gameObject.AddComponent<Outline>();
                    }
                    if (licenseTypeOutlineUI != null)
                    {
                        licenseTypeOutlineUI.enabled = false;
                    }
                }
                SyncLicenseCategoryTextStyle();

                fileSizeJSON.text = ui.fileSizeText;
                // Make the display area a bit larger
                ui.fileSizeText.GetComponent<RectTransform>().sizeDelta = new Vector2(-20,0);

                notOnHubJSON.indicator = ui.notOnHubIndicator;
                alreadyHaveJSON.indicator = ui.alreadyHaveIndicator;
                alreadyHaveSceneJSON.indicator = ui.alreadyHaveSceneIndicator;
                updateAvailableJSON.indicator = ui.updateAvailableIndicator;
                updateMsgJSON.text = ui.updateMsgText;
                updateAction.button = ui.updateButton;
                downloadAction.button = ui.downloadButton;
                if (ui.updateButton != null)
                {
                    Image updateImage = ui.updateButton.GetComponent<Image>();
                    if (updateImage != null)
                    {
                        updateImage.color = new Color32(163, 111, 214, 255);
                    }
                    Text updateText = ui.updateButton.GetComponentInChildren<Text>(true);
                    if (updateText != null)
                    {
                        updateText.color = new Color32(255, 255, 255, 255);
                    }
                }
                isDownloadQueuedJSON.indicator = ui.isDownloadQueuedIndicator;
                isDownloadingJSON.indicator = ui.isDownloadingIndicator;
                isDownloadedJSON.indicator = ui.isDownloadedIndicator;
                downloadProgressJSON.slider = ui.progressSlider;
                openInPackageManagerAction.button = ui.openInPackageManagerButton;
                openSceneAction.button = ui.openSceneButton;

                var parent= ui.progressSlider.GetComponent<RectTransform>();
                RectTransform newObj = UnityEngine.Object.Instantiate(ui.updateButton.transform, parent) as RectTransform;
                newObj.gameObject.SetActive(true);
                newObj.localScale = new Vector3(0.8f,1,1);
                newObj.sizeDelta = new Vector2(80,-10);
                newObj.anchoredPosition = new Vector2(-20, 0);
                newObj.Find("Text").GetComponent<Text>().text = "stop";
                newObj.GetComponent<Image>().color = new Color32(255, 122, 122, 255);
                var btn = newObj.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(StopDownloading);

                alreadyHaveIndicatorRect = ui.alreadyHaveIndicator != null ? ui.alreadyHaveIndicator.GetComponent<RectTransform>() : null;
                if (alreadyHaveIndicatorRect != null)
                {
                    ConfigureHalfWidthIndicatorLabel(ui.alreadyHaveIndicator, "In Library", "In Lib");
                }
                alreadyHaveSceneIndicatorRect = ui.alreadyHaveSceneIndicator != null ? ui.alreadyHaveSceneIndicator.GetComponent<RectTransform>() : null;
                if (alreadyHaveSceneIndicatorRect != null)
                {
                    ConfigureHalfWidthIndicatorLabel(ui.alreadyHaveSceneIndicator, "In Library\nOpen Scene", "Open");
                    ConfigureHalfWidthIndicatorLabel(ui.alreadyHaveSceneIndicator, "Open Scene", "Open");
                }

                RectTransform deleteSource = ui.downloadButton != null
                    ? ui.downloadButton.GetComponent<RectTransform>()
                    : ui.updateButton.GetComponent<RectTransform>();
                deleteButton = CreateHalfWidthDeleteButton(ui.alreadyHaveIndicator, deleteSource, out deleteButtonRect);
                deleteSceneButton = CreateHalfWidthDeleteButton(ui.alreadyHaveSceneIndicator, deleteSource, out deleteSceneButtonRect);

                if (ui.updateButton != null)
                {
                    RectTransform updateRect = ui.updateButton.GetComponent<RectTransform>();
                    ConfigureHalfWidthButtonLabel(updateRect);
                    updateDeleteButton = CreateHalfWidthDeleteButton(ui.updateButton.gameObject, updateRect, out updateDeleteButtonRect);
                }
                SyncDeleteButton();

                if (string.IsNullOrEmpty(Category) && browser != null && !string.IsNullOrEmpty(resource_id) && resource_id != "null")
                {
                    browser.ResolveResourceCategory(resource_id, SyncCategory);
                }
 
                // Add Hub thumbnail preview if we have a CDN URL (resource_id was resolved)
                if (!string.IsNullOrEmpty(thumbnailUrl))
                {
                    GameObject thumbGO;
                    if (ui.thumbnailImage != null)
                    {
                        thumbGO = ui.thumbnailImage.gameObject;
                        thumbnailImageUI = ui.thumbnailImage;
                    }
                    else
                    {
                        thumbGO = new GameObject("HubThumbnail");
                        thumbGO.transform.SetParent(ui.transform, false);
                        var rt = thumbGO.AddComponent<RectTransform>();
                        rt.anchorMin = new Vector2(0f, 1f);
                        rt.anchorMax = new Vector2(0f, 1f);
                        rt.pivot = new Vector2(0f, 1f);
                        rt.sizeDelta = new Vector2(64f, 64f);
                        rt.anchoredPosition = new Vector2(10f, 0f);
                        thumbnailImageUI = thumbGO.AddComponent<RawImage>();
                    }
                    
                    thumbnailImageUI.color = new Color(0.25f, 0.25f, 0.25f, 0.8f); // dark placeholder
                    
                    // Add hover handler for preview in main thumbnail area
                    hoverHandler = thumbGO.AddComponent<DependencyThumbnailHover>();
                    hoverHandler.package = this;
                    var retryLoader = thumbGO.GetComponent<DependencyThumbnailRetryLoader>();
                    if (retryLoader == null) retryLoader = thumbGO.AddComponent<DependencyThumbnailRetryLoader>();
                    retryLoader.package = this;
                    
                    LoadThumbnail();
                }
            }
        }
        void StopDownloading()
        {
            if (request != null)
            {
                request.stop = true;
            }
        }
    }

    public class DependencyThumbnailHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public HubResourcePackage package;
        private Texture storedOriginalTexture;
        private bool isHovered;
        public bool IsHovered { get { return isHovered; } }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
            if (package != null && package.mainThumbnailImage != null)
            {
                storedOriginalTexture = package.mainThumbnailImage.texture;
                if (package.thumbnailTexture != null)
                {
                    package.mainThumbnailImage.texture = package.thumbnailTexture;
                    package.mainThumbnailImage.color = Color.white;
                }
            }
        }

        public void UpdateMainThumbnail(Texture tex)
        {
            if (isHovered && package != null && package.mainThumbnailImage != null)
            {
                package.mainThumbnailImage.texture = tex;
                package.mainThumbnailImage.color = Color.white;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
            if (package != null && package.mainThumbnailImage != null && storedOriginalTexture != null)
            {
                package.mainThumbnailImage.texture = storedOriginalTexture;
            }
        }
    }

    public class DependencyThumbnailRetryLoader : MonoBehaviour
    {
        public HubResourcePackage package;
        private float _nextRetryAt;

        private void OnEnable()
        {
            _nextRetryAt = 0f;
            TryQueue(false);
        }

        private void Update()
        {
            if (package == null || package.thumbnailTexture != null)
            {
                enabled = false;
                return;
            }

            if (Time.unscaledTime >= _nextRetryAt)
            {
                TryQueue(false);
            }
        }

        private void TryQueue(bool immediate)
        {
            if (package == null) return;
            package.EnsureThumbnailQueued(immediate);
            _nextRetryAt = Time.unscaledTime + 0.75f;
        }
    }

}
