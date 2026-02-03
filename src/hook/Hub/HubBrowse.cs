using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using ZenFulcrum.EmbeddedBrowser;
//using MVR.Hub;

namespace VPB
{
    public class HubBrowse : JSONStorable
    {
        public delegate void BinaryRequestStartedCallback();

        public delegate void BinaryRequestSuccessCallback(byte[] data, Dictionary<string, string> responseHeaders);

        public delegate void RequestSuccessCallback(SimpleJSON.JSONNode jsonNode);

        public delegate void RequestErrorCallback(string err);

        public delegate void RequestProgressCallback(float progress,ulong downloadedBytes);

        public delegate void EnableHubCallback();

        public delegate void EnableWebBrowserCallback();

        public delegate void PreShowCallback();

        public delegate void OnHideCallback();

        public class DownloadRequest
        {
            public string url;

            public string promotionalUrl;

            public BinaryRequestStartedCallback startedCallback;

            public RequestProgressCallback progressCallback;

            public BinaryRequestSuccessCallback successCallback;

            public RequestErrorCallback errorCallback;

            public bool stop = false;
        }

        public static HubBrowse singleton;

        public string cookieHost = "hub.virtamate.com";

        public string apiUrl = "https://hub.virtamate.com/citizenx/api.php";

        public string packagesJSONUrl = "https://s3cdn.virtamate.com/data/packages.json";

        protected bool _hubEnabled;

        protected JSONStorableBool hubEnabledJSON;

        public EnableHubCallback enableHubCallbacks;

        protected JSONStorableAction enableHubAction;

        protected bool _webBrowserEnabled;

        protected JSONStorableBool webBrowserEnabledJSON;

        public EnableWebBrowserCallback enableWebBrowserCallbacks;

        protected JSONStorableAction enableWebBrowserAction;

        protected MVR.Hub.HubBrowseUI hubBrowseUI;

        public RectTransform itemPrefab;

        protected RectTransform itemContainer;

        protected ScrollRect itemScrollRect;

        protected List<HubResourceItemUI> items;

        public RectTransform resourceDetailPrefab;

        protected GameObject detailPanel;

        public RectTransform packageDownloadPrefab;

        public RectTransform creatorSupportButtonPrefab;

        protected RectTransform resourceDetailContainer;

        protected Browser browser;

        protected VRWebBrowser webBrowser;

        protected GameObject isWebLoadingIndicator;

        public string CurrentPage
        {
            get
            {
                return _currentPageString;
            }
        }

        protected GameObject refreshIndicator;

        protected bool _isShowing;

        public PreShowCallback preShowCallbacks;

        public OnHideCallback onHideCallbacks;

        protected bool _hasBeenRefreshed;

        protected Coroutine refreshResourcesRoutine;

        protected JSONStorableAction refreshResourcesAction;

        protected JSONStorableString numResourcesJSON;

        protected JSONStorableString pageInfoJSON;

        protected int _numPagesInt;

        protected JSONStorableString numPagesJSON;

        protected int _numPerPageInt = 48;

        protected JSONStorableFloat numPerPageJSON;

        protected string _currentPageString = "1";

        protected int _currentPageInt = 1;

        protected JSONStorableString currentPageJSON;

        protected JSONStorableAction firstPageAction;

        protected JSONStorableAction previousPageAction;

        protected JSONStorableAction nextPageAction;

        protected JSONStorableAction clearFiltersAction;

        protected string _hostedOption = "Hub And Dependencies";

        protected JSONStorableStringChooser hostedOptionChooser;

        protected string _payTypeFilter = "Free";

        protected JSONStorableStringChooser payTypeFilterChooser;

        protected const float _triggerDelay = 0.5f;

        protected float triggerCountdown;

        protected Coroutine triggerResetRefreshRoutine;

        protected string _minLengthSearchFilter = string.Empty;

        protected string _searchFilter = string.Empty;

        protected JSONStorableString searchFilterJSON;

        protected string _categoryFilter = "All";

        protected JSONStorableStringChooser categoryFilterChooser;

        protected string _creatorFilter = "All";

        protected JSONStorableStringChooser creatorFilterChooser;

        protected string _tagsFilter = "All";

        protected JSONStorableStringChooser tagsFilterChooser;

        protected string _sortPrimary = "Latest Update";

        protected JSONStorableStringChooser sortPrimaryChooser;

        protected string _sortSecondary = "None";

        protected JSONStorableStringChooser sortSecondaryChooser;

        protected bool hubSettingsApplied;
        protected bool suppressHubSettingsSave;

        protected Dictionary<string, HubResourceItemDetailUI> savedResourceDetailsPanels;

        protected Stack<HubResourceItemDetailUI> resourceDetailStack;

        //public PackageBuilder packageManager;

        protected GameObject missingPackagesPanel;

        protected RectTransform missingPackagesContainer;

        protected List<string> checkMissingPackageNames;

        protected List<HubResourcePackageUI> missingPackages;

        protected JSONStorableAction openMissingPackagesPanelAction;

        protected JSONStorableAction closeMissingPackagesPanelAction;

        protected JSONStorableAction downloadAllMissingPackagesAction;

        protected GameObject updatesPanel;

        protected RectTransform updatesContainer;

        protected List<string> checkUpdateNames;

        protected List<HubResourcePackageUI> updates;

        protected Dictionary<string, int> packageGroupToLatestVersion;

        protected Dictionary<string, string> packageIdToResourceId;

        protected JSONStorableAction openUpdatesPanelAction;

        protected JSONStorableAction closeUpdatesPanelAction;

        protected JSONStorableAction downloadAllUpdatesAction;

        protected JSONStorableBool isDownloadingJSON;

        protected JSONStorableString downloadQueuedCountJSON;

        protected Queue<DownloadRequest> downloadQueue;

        protected List<string> hubCookies;

        protected Coroutine GetBrowserCookiesRoutine;

        protected JSONStorableAction openDownloadingAction;

        protected RectTransform refreshingGetInfoPanel;

        protected RectTransform failedGetInfoPanel;

        protected Text getInfoErrorText;

        protected bool hubInfoSuccess;

        protected bool hubInfoCompleted;

        protected bool hubInfoRefreshing;

        protected Coroutine hubInfoCoroutine;

        protected JSONStorableAction cancelGetHubInfoAction;

        protected JSONStorableAction retryGetHubInfoAction;

        public bool HubEnabled
        {
            get
            {
                return _hubEnabled;
            }
            set
            {
                if (hubEnabledJSON != null)
                {
                    hubEnabledJSON.val = value;
                }
                else
                {
                    _hubEnabled = value;
                }
            }
        }

        public bool WebBrowserEnabled
        {
            get
            {
                return _webBrowserEnabled;
            }
            set
            {
                if (webBrowserEnabledJSON != null)
                {
                    webBrowserEnabledJSON.val = value;
                }
                else
                {
                    _webBrowserEnabled = value;
                }
            }
        }

        public string HostedOption
        {
            get
            {
                return _hostedOption;
            }
            set
            {
                hostedOptionChooser.val = value;
            }
        }

        public string PayTypeFilter
        {
            get
            {
                return _payTypeFilter;
            }
            set
            {
                payTypeFilterChooser.val = value;
            }
        }

        public string SearchFilter
        {
            get
            {
                return _searchFilter;
            }
            set
            {
                searchFilterJSON.val = value;
            }
        }

        public string CategoryFilter
        {
            get
            {
                return _categoryFilter;
            }
            set
            {
                categoryFilterChooser.val = value;
            }
        }

        public string CreatorFilter
        {
            get
            {
                return _creatorFilter;
            }
            set
            {
                _hostedOption = "All";
                hostedOptionChooser.valNoCallback = "All";
                creatorFilterChooser.val = value;
            }
        }

        public string CreatorFilterOnly
        {
            get
            {
                return _creatorFilter;
            }
            set
            {
                CloseAllDetails();
                ResetFilters();
                _hostedOption = "All";
                hostedOptionChooser.valNoCallback = "All";
                creatorFilterChooser.val = value;
            }
        }

        public string TagsFilter
        {
            get
            {
                return _tagsFilter;
            }
            set
            {
                tagsFilterChooser.val = value;
            }
        }

        public string TagsFilterOnly
        {
            get
            {
                return _tagsFilter;
            }
            set
            {
                ResetFilters();
                tagsFilterChooser.val = value;
            }
        }

        private IEnumerator GetRequest(string uri, RequestSuccessCallback callback, RequestErrorCallback errorCallback)
        {
            Stopwatch totalSw = Stopwatch.StartNew();
            if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                LogUtil.Log($"HubBrowse.GetRequest START uri={uri}");

            Stopwatch sw = Stopwatch.StartNew();
            using (UnityWebRequest webRequest = UnityWebRequest.Get(uri))
            {
                long createMs = sw.ElapsedMilliseconds;
                if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                    LogUtil.Log($"HubBrowse.GetRequest CREATED uri={uri} ms={createMs}");

                sw.Reset();
                sw.Start();
                var op = webRequest.SendWebRequest();
                long sendMs = sw.ElapsedMilliseconds;
                if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                    LogUtil.Log($"HubBrowse.GetRequest SEND uri={uri} ms={sendMs}");

                sw.Reset();
                sw.Start();
                while (!webRequest.isDone)
                {
                    yield return null;
                }
                long waitMs = sw.ElapsedMilliseconds;

                if (webRequest.isNetworkError)
                {
                    LogUtil.LogError($"HubBrowse.GetRequest DONE_ERROR uri={uri} waitMs={waitMs} totalMs={totalSw.ElapsedMilliseconds} code={webRequest.responseCode} err={webRequest.error}");
                    if (errorCallback != null)
                    {
                        errorCallback(webRequest.error);
                    }
                }
                else
                {
                    string text = webRequest.downloadHandler != null ? webRequest.downloadHandler.text : null;
                    int textLen = text != null ? text.Length : 0;
                    if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                        LogUtil.Log($"HubBrowse.GetRequest DONE_OK uri={uri} waitMs={waitMs} totalMs={totalSw.ElapsedMilliseconds} code={webRequest.responseCode} bytes={webRequest.downloadedBytes} textLen={textLen}");

                    sw.Reset();
                    sw.Start();
                    SimpleJSON.JSONNode jsonNode = JSON.Parse(text);
                    long parseMs = sw.ElapsedMilliseconds;
                    if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                        LogUtil.Log($"HubBrowse.GetRequest PARSED uri={uri} parseMs={parseMs} totalMs={totalSw.ElapsedMilliseconds}");

                    sw.Reset();
                    sw.Start();
                    if (callback != null)
                    {
                        callback(jsonNode);
                    }
                    long callbackMs = sw.ElapsedMilliseconds;
                    if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                        LogUtil.Log($"HubBrowse.GetRequest CALLBACK_DONE uri={uri} callbackMs={callbackMs} totalMs={totalSw.ElapsedMilliseconds}");
                }
            }
        }

        private IEnumerator BinaryGetRequest(DownloadRequest request,string uri, BinaryRequestStartedCallback startedCallback, BinaryRequestSuccessCallback successCallback, RequestErrorCallback errorCallback, RequestProgressCallback progressCallback, List<string> cookies = null)
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get(uri))
            {
                string cookieHeader = "vamhubconsent=1";
                if (cookies != null)
                {
                    foreach (string cookie in cookies)
                    {
                        cookieHeader = cookieHeader + ";" + cookie;
                    }
                }
                webRequest.SetRequestHeader("Cookie", cookieHeader);
                webRequest.SendWebRequest();
                if (startedCallback != null)
                {
                    startedCallback();
                }
                while (!webRequest.isDone)
                {
                    
                    if (progressCallback != null)
                    {
                        progressCallback(webRequest.downloadProgress, webRequest.downloadedBytes);
                    }
                    if (request.stop)
                    {
                        break;
                    }
                    yield return null;
                }
                if (request.stop || webRequest.isNetworkError)
                {
                    LogUtil.LogError(uri + ": Error: " + webRequest.error);
                    if (errorCallback != null)
                    {
                        errorCallback(webRequest.error);
                    }
                }
                else
                {
                    Dictionary<string, string> responseHeaders = webRequest.GetResponseHeaders();
                    if (successCallback != null)
                    {
                        successCallback(webRequest.downloadHandler.data, responseHeaders);
                    }
                }
            }
        }

        private IEnumerator PostRequest(string uri, string postData, RequestSuccessCallback callback, RequestErrorCallback errorCallback)
        {
            Stopwatch totalSw = Stopwatch.StartNew();
            int postLen = postData != null ? postData.Length : 0;
            if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                LogUtil.Log($"HubBrowse.PostRequest START uri={uri} postLen={postLen}");

            Stopwatch sw = Stopwatch.StartNew();
            using (UnityWebRequest webRequest = UnityWebRequest.Post(uri, postData))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(postData));
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Accept", "application/json");
                long createMs = sw.ElapsedMilliseconds;
                if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                    LogUtil.Log($"HubBrowse.PostRequest CREATED uri={uri} ms={createMs}");

                sw.Reset();
                sw.Start();
                var op = webRequest.SendWebRequest();
                long sendMs = sw.ElapsedMilliseconds;
                if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                    LogUtil.Log($"HubBrowse.PostRequest SEND uri={uri} ms={sendMs}");

                sw.Reset();
                sw.Start();
                while (!webRequest.isDone)
                {
                    yield return null;
                }
                long waitMs = sw.ElapsedMilliseconds;

                string[] pages = uri.Split('/');
                int page = pages.Length - 1;
                if (webRequest.isNetworkError)
                {
                    LogUtil.LogError($"HubBrowse.PostRequest DONE_ERROR uri={uri} page={pages[page]} waitMs={waitMs} totalMs={totalSw.ElapsedMilliseconds} code={webRequest.responseCode} err={webRequest.error}");
                    if (errorCallback != null)
                    {
                        errorCallback(webRequest.error);
                    }
                    yield break;
                }

                string text = webRequest.downloadHandler != null ? webRequest.downloadHandler.text : null;
                int textLen = text != null ? text.Length : 0;
                if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                    LogUtil.Log($"HubBrowse.PostRequest DONE_OK uri={uri} page={pages[page]} waitMs={waitMs} totalMs={totalSw.ElapsedMilliseconds} code={webRequest.responseCode} bytes={webRequest.downloadedBytes} textLen={textLen}");

                sw.Reset();
                sw.Start();
                SimpleJSON.JSONNode jSONNode = JSON.Parse(text);
                long parseMs = sw.ElapsedMilliseconds;
                if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                    LogUtil.Log($"HubBrowse.PostRequest PARSED uri={uri} parseMs={parseMs} totalMs={totalSw.ElapsedMilliseconds}");
                if (jSONNode == null)
                {
                    string errText = "Error - Invalid JSON response: " + text;
                    //Debug.LogError(pages[page] + ": " + text);
                    if (errorCallback != null)
                    {
                        errorCallback(errText);
                    }
                }
                else if (callback != null)
                {
                    sw.Reset();
                    sw.Start();
                    callback(jSONNode);
                    long callbackMs = sw.ElapsedMilliseconds;
                    if (Settings.Instance != null && Settings.Instance.LogHubRequests != null && Settings.Instance.LogHubRequests.Value)
                        LogUtil.Log($"HubBrowse.PostRequest CALLBACK_DONE uri={uri} callbackMs={callbackMs} totalMs={totalSw.ElapsedMilliseconds}");
                }
            }
        }

        protected void SyncHubEnabled(bool b)
        {
            _hubEnabled = b;
            if (_hubEnabled)
            {
                LogUtil.Log("HubBrowse hub enabled");
                if (_isShowing)
                {
                    GetHubInfo();
                    RefreshResources();
                }
            }
        }

        protected void EnableHub()
        {
            if (enableHubCallbacks != null)
            {
                enableHubCallbacks();
            }
        }

        protected void SyncWebBrowserEnabled(bool b)
        {
            _webBrowserEnabled = b;
            if (_webBrowserEnabled && resourceDetailStack != null && resourceDetailStack.Count > 0)
            {
                HubResourceItemDetailUI hubResourceItemDetailUI = resourceDetailStack.Peek();
                if (hubResourceItemDetailUI.connectedItem != null)
                {
                    hubResourceItemDetailUI.gameObject.SetActive(true);//sf
                    hubResourceItemDetailUI.connectedItem.NavigateToOverview();
                }
            }
        }

        protected void EnableWebBrowser()
        {
            if (enableWebBrowserCallbacks != null)
            {
                enableWebBrowserCallbacks();
            }
        }

        public void Show()
        {
            if (preShowCallbacks != null)
            {
                preShowCallbacks();
            }
            _isShowing = true;
            if (hubBrowseUI != null)
            {
                hubBrowseUI.gameObject.SetActive(true);
            }
            else if (UITransform != null)
            {
                UITransform.gameObject.SetActive(true);
            }
            if (!_hubEnabled)
            {
                return;
            }

            if (!hubInfoRefreshing && !hubInfoSuccess)
            {
                GetHubInfo();
            }
            if (_hasBeenRefreshed)
            {
                if (items == null)
                {
                    return;
                }
                {
                    foreach (HubResourceItemUI item in items)
                    {
                        if (item.connectedItem != null)
                        {
                            item.connectedItem.Show();
                        }
                    }
                    return;
                }
            }
            RefreshResources();
        }

        public void Hide()
        {
            _isShowing = false;
            if (hubBrowseUI != null)
            {
                hubBrowseUI.gameObject.SetActive(false);
            }
            if (onHideCallbacks != null)
            {
                onHideCallbacks();
            }
            if (items == null)
            {
                return;
            }
            foreach (HubResourceItemUI item in items)
            {
                if (item.connectedItem != null)
                {
                    item.connectedItem.Hide();
                }
            }
        }

        protected void RefreshErrorCallback(string err)
        {
            if (refreshIndicator != null)
            {
                refreshIndicator.SetActive(false);
            }
            SuperController.LogError("Error during hub request " + err);
        }

        protected void RefreshCallback(SimpleJSON.JSONNode jsonNode, string page)
        {
            if (refreshIndicator != null)
            {
                refreshIndicator.SetActive(false);
            }
            if (!(jsonNode != null))
            {
                return;
            }
            JSONClass asObject = jsonNode.AsObject;
            if (!(asObject != null))
            {
                return;
            }
            string text = asObject["status"];
            if (text == "success")
            {
                JSONClass asObject2 = asObject["pagination"].AsObject;
                if (!(asObject2 != null))
                {
                    return;
                }
                numResourcesJSON.val = "Total: " + asObject2["total_found"];
                numPagesJSON.val = asObject2["total_pages"];
                if (items != null)
                {
                    foreach (HubResourceItemUI item in items)
                    {
                        if (item.connectedItem != null)
                        {
                            item.connectedItem.Destroy();
                        }
                        UnityEngine.Object.Destroy(item.gameObject);
                    }
                    items.Clear();
                }
                else
                {
                    items = new List<HubResourceItemUI>();
                }
                if (itemScrollRect != null)
                {
                    itemScrollRect.verticalNormalizedPosition = 1f;
                }
                JSONArray asArray = asObject["resources"].AsArray;
                if (!(itemContainer != null) || !(itemPrefab != null) || !(asArray != null))
                {
                    return;
                }
                IEnumerator enumerator2 = asArray.GetEnumerator();
                try
                {
                    while (enumerator2.MoveNext())
                    {
                        JSONClass resource = (JSONClass)enumerator2.Current;
                        bool canShow = true;
                        bool asBool = resource["hubDownloadable"].AsBool;
                        if(onlyDownloadable.val)
                        {
                            canShow = asBool;
                        }
                        // Do not show items that cannot be downloaded
                        if (canShow)
                        {
                            HubResourceItem hubResourceItem = new HubResourceItem(resource, this, page);
                            hubResourceItem.Refresh();

                            RectTransform rectTransform = UnityEngine.Object.Instantiate(itemPrefab);
                            rectTransform.SetParent(itemContainer, false);
                            HubResourceItemUI component = rectTransform.GetComponent<HubResourceItemUI>();
                            if (component != null)
                            {
                                hubResourceItem.RegisterUI(component);
                                items.Add(component);
                            }
                        }
                    }
                    return;
                }
                finally
                {
                    IDisposable disposable;
                    if ((disposable = enumerator2 as IDisposable) != null)
                    {
                        disposable.Dispose();
                    }
                }
            }
            string text2 = jsonNode["error"];
            //LogUtil.Log("Refresh returned error " + text2);
        }

        public void RefreshResources()
        {
            _hasBeenRefreshed = true;
            if (_hubEnabled)
            {
                if (refreshResourcesRoutine != null)
                {
                    StopCoroutine(refreshResourcesRoutine);
                }
                JSONClass jSONClass = new JSONClass();
                jSONClass["source"] = "VaM";
                jSONClass["action"] = "getResources";
                jSONClass["latest_image"] = "Y";
                jSONClass["perpage"] = _numPerPageInt.ToString();
                string page = _currentPageString;
                jSONClass["page"] = page;
                if (_hostedOption != "All")
                {
                    jSONClass["location"] = _hostedOption;
                }
                if (_searchFilter != string.Empty)
                {
                    jSONClass["search"] = _searchFilter;
                    jSONClass["searchall"] = "true";
                }
                if (_payTypeFilter != "All")
                {
                    jSONClass["category"] = _payTypeFilter;
                }
                // If "Only Downloadable" is checked, payType must be free, reducing the returned data
                if (onlyDownloadable.val)
                {
                    jSONClass["category"] = "Free";
                }
                if (_categoryFilter != "All")
                {
                    jSONClass["type"] = _categoryFilter;
                }
                if (_creatorFilter != "All")
                {
                    jSONClass["username"] = _creatorFilter;
                }
                if (_tagsFilter != "All")
                {
                    jSONClass["tags"] = _tagsFilter;
                }
                string text = _sortPrimary;
                if (_sortSecondary != null && _sortSecondary != string.Empty && _sortSecondary != "None")
                {
                    text = text + "," + _sortSecondary;
                }
                jSONClass["sort"] = text;
                string postData = jSONClass.ToString();
                refreshResourcesRoutine = StartCoroutine(PostRequest(apiUrl, postData, 
                    jsonNode => { RefreshCallback(jsonNode, page); }, 
                    RefreshErrorCallback));
                if (refreshIndicator != null)
                {
                    refreshIndicator.SetActive(true);
                }
            }
        }

        protected void SyncNumResources(string s)
        {
        }

        protected void SetPageInfo()
        {
            pageInfoJSON.val = "Page " + currentPageJSON.val + " of " + numPagesJSON.val;
        }

        protected void SyncNumPages(string s)
        {
            int result;
            if (int.TryParse(s, out result))
            {
                _numPagesInt = result;
            }
            SetPageInfo();
        }

        protected void SyncNumPerPage(float f)
        {
            _numPerPageInt = (int)f;
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubItemsPerPage != null)
            {
                Settings.Instance.HubItemsPerPage.Value = _numPerPageInt;
            }
            ResetRefresh();
        }

        protected void CancelOldPageImages()
        {
            if (VPB.HubImageLoaderThreaded.singleton != null)
            {
                VPB.HubImageLoaderThreaded.singleton.CancelGroup("HubPage_" + _currentPageString);
            }
        }

        protected void ResetRefresh()
        {
            CancelOldPageImages();
            _currentPageString = "1";
            _currentPageInt = 1;
            currentPageJSON.valNoCallback = _currentPageString;
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubCurrentPage != null)
            {
                Settings.Instance.HubCurrentPage.Value = 1;
            }
            SetPageInfo();
            RefreshResources();
        }

        protected void SyncCurrentPage(string s)
        {
            CancelOldPageImages();
            _currentPageString = s;
            int result;
            if (int.TryParse(s, out result))
            {
                _currentPageInt = result;
            }
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubCurrentPage != null)
            {
                Settings.Instance.HubCurrentPage.Value = _currentPageInt;
            }
            SetPageInfo();
            RefreshResources();
        }

        protected void FirstPage()
        {
            currentPageJSON.val = "1";
        }

        protected void PreviousPage()
        {
            if (_currentPageInt > 1)
            {
                currentPageJSON.val = (_currentPageInt - 1).ToString();
            }
        }

        protected void NextPage()
        {
            if (_currentPageInt < _numPagesInt)
            {
                currentPageJSON.val = (_currentPageInt + 1).ToString();
            }
        }

        protected void ResetFilters()
        {
            _payTypeFilter = "All";
            payTypeFilterChooser.valNoCallback = "All";
            _searchFilter = string.Empty;
            searchFilterJSON.valNoCallback = string.Empty;
            _categoryFilter = "All";
            categoryFilterChooser.valNoCallback = "All";
            _creatorFilter = "All";
            creatorFilterChooser.valNoCallback = "All";
            _tagsFilter = "All";
            tagsFilterChooser.valNoCallback = "All";
        }

        protected void ResetFiltersAndRefresh()
        {
            ResetFilters();
            ResetRefresh();
        }

        protected void SyncHostedOption(string s)
        {
            _hostedOption = s;
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubHostedOption != null)
            {
                Settings.Instance.HubHostedOption.Value = _hostedOption;
            }
            ResetRefresh();
        }

        protected void SyncPayTypeFilter(string s)
        {
            _payTypeFilter = s;
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubPayTypeFilter != null)
            {
                Settings.Instance.HubPayTypeFilter.Value = _payTypeFilter;
            }
            if (_payTypeFilter != "Free" && _hostedOption != "All")
            {
                hostedOptionChooser.val = "All";
            }
            else
            {
                ResetRefresh();
            }
        }

        protected IEnumerator TriggerResetRefesh()
        {
            while (triggerCountdown > 0f)
            {
                triggerCountdown -= Time.unscaledDeltaTime;
                yield return null;
            }
            triggerResetRefreshRoutine = null;
            ResetRefresh();
        }

        protected void SyncSearchFilter(string s)
        {
            _searchFilter = s;
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubSearchText != null)
            {
                Settings.Instance.HubSearchText.Value = _searchFilter;
            }
            bool flag = false;
            if (_searchFilter.Length > 2)
            {
                if (_minLengthSearchFilter != _searchFilter)
                {
                    _minLengthSearchFilter = _searchFilter;
                    flag = true;
                }
            }
            else if (_minLengthSearchFilter != string.Empty)
            {
                _minLengthSearchFilter = string.Empty;
                flag = true;
            }
            if (flag)
            {
                triggerCountdown = 0.5f;
                if (triggerResetRefreshRoutine == null)
                {
                    triggerResetRefreshRoutine = StartCoroutine(TriggerResetRefesh());
                }
            }
        }

        protected void SyncCategoryFilter(string s)
        {
            _categoryFilter = s;
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubCategoryFilter != null)
            {
                Settings.Instance.HubCategoryFilter.Value = _categoryFilter;
            }
            ResetRefresh();
        }

        public void SetPayTypeAndCategoryFilter(string payType, string category, bool onlyTheseFilters = true)
        {
            if (onlyTheseFilters)
            {
                CloseAllDetails();
                ResetFilters();
            }
            _payTypeFilter = payType;
            payTypeFilterChooser.valNoCallback = payType;
            _categoryFilter = category;
            categoryFilterChooser.valNoCallback = category;
            ResetRefresh();
        }

        protected void SyncCreatorFilter(string s)
        {
            _creatorFilter = s;
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubCreatorFilter != null)
            {
                Settings.Instance.HubCreatorFilter.Value = _creatorFilter;
            }
            ResetRefresh();
        }

        protected void SyncTagsFilter(string s)
        {
            _tagsFilter = s;
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubTagsFilter != null)
            {
                Settings.Instance.HubTagsFilter.Value = _tagsFilter;
            }
            ResetRefresh();
        }

        protected void SyncSortPrimary(string s)
        {
            _sortPrimary = s;
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubSortPrimary != null)
            {
                Settings.Instance.HubSortPrimary.Value = _sortPrimary;
            }
            ResetRefresh();
        }

        protected void SyncSortSecondary(string s)
        {
            _sortSecondary = s;
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubSortSecondary != null)
            {
                Settings.Instance.HubSortSecondary.Value = _sortSecondary;
            }
            ResetRefresh();
        }

        public void NavigateWebPanel(string url)
        {
            if (webBrowser != null && webBrowser.url != url && _webBrowserEnabled)
            {
                if (isWebLoadingIndicator != null)
                {
                    isWebLoadingIndicator.SetActive(true);
                }
                webBrowser.url = url;
            }
        }

        public void ShowHoverUrl(string url)
        {
            if (webBrowser != null)
            {
                webBrowser.HoveredURL = url;
            }
        }

        protected void GetResourceDetailErrorCallback(string err, HubResourceItemDetailUI hridui)
        {
            //LogUtil.Log("Error during fetch of resource detail from Hub");
            CloseDetail(null);
        }

        protected void GetResourceDetailCallback(SimpleJSON.JSONNode jsonNode, HubResourceItemDetailUI hridui)
        {
            if (jsonNode != null && hridui != null)
            {
                JSONClass asObject = jsonNode.AsObject;
                if (asObject != null)
                {
                    HubResourceItemDetail hubResourceItemDetail = new HubResourceItemDetail(asObject, this);
                    hubResourceItemDetail.Refresh();
                    hubResourceItemDetail.RegisterUI(hridui);
                }
            }
        }

        public void OpenDetail(string resource_id, bool isPackageName = false)
        {
            if (_hubEnabled)
            {
                if (!(resourceDetailPrefab != null) || !(resourceDetailContainer != null))
                {
                    return;
                }
                Show();

                HubResourceItemDetailUI hridui;
                // All detail panels not in the stack are stored in savedResourceDetailsPanels
                if (savedResourceDetailsPanels.TryGetValue(resource_id, out hridui))
                {
                    savedResourceDetailsPanels.Remove(resource_id);
                    hridui.gameObject.SetActive(true);
                    resourceDetailStack.Push(hridui);
                    hridui.transform.SetAsLastSibling();// Move to the end to ensure correct display order
                }
                else
                {
                    RectTransform rectTransform = UnityEngine.Object.Instantiate(resourceDetailPrefab);
                    rectTransform.SetParent(resourceDetailContainer, false);
                    hridui = rectTransform.GetComponent<HubResourceItemDetailUI>();
                    resourceDetailStack.Push(hridui);
                    hridui.transform.SetAsLastSibling();// Move to the end to ensure correct display order

                    JSONClass jSONClass = new JSONClass();
                    jSONClass["source"] = "VaM";
                    jSONClass["action"] = "getResourceDetail";
                    jSONClass["latest_image"] = "Y";
                    if (isPackageName)
                    {
                        jSONClass["package_name"] = resource_id;
                    }
                    else
                    {
                        jSONClass["resource_id"] = resource_id;
                    }
                    string postData = jSONClass.ToString();
                    StartCoroutine(PostRequest(apiUrl, postData,
                        jsonNode => { GetResourceDetailCallback(jsonNode, hridui); },
                        err => { this.GetResourceDetailErrorCallback(err, hridui); }));
                }
                if (detailPanel != null)
                {
                    detailPanel.SetActive(true);
                }
            }
            else
            {
                LogUtil.LogError("Cannot perform action. Hub is disabled in User Preferences");
            }
        }

        public void CloseDetail(string resource_id)
        {
            // When closing, if there is still data in the stack
            if (resourceDetailStack.Count > 0)
            {
                HubResourceItemDetailUI hubResourceItemDetailUI = resourceDetailStack.Pop();
                if (hubResourceItemDetailUI.connectedItem != null && hubResourceItemDetailUI.connectedItem.IsDownloading)
                {
                    hubResourceItemDetailUI.gameObject.SetActive(false);
                    if (!savedResourceDetailsPanels.ContainsKey(resource_id))
                        savedResourceDetailsPanels.Add(resource_id, hubResourceItemDetailUI);
                }
                else
                {
                    // If the download is finished, remove it directly
                    if (resource_id != null)
                    {
                        savedResourceDetailsPanels.Remove(resource_id);
                    }
                    UnityEngine.Object.Destroy(hubResourceItemDetailUI.gameObject);
                }
            }
            if (resourceDetailStack.Count == 0)
            {
                if (detailPanel != null)
                {
                    detailPanel.SetActive(false);
                }
            }
            else
            {
                HubResourceItemDetailUI hubResourceItemDetailUI2 = resourceDetailStack.Peek();
                if (hubResourceItemDetailUI2.connectedItem != null)
                {
                    // Display the next item in the stack
                    hubResourceItemDetailUI2.gameObject.SetActive(true);
                    hubResourceItemDetailUI2.connectedItem.NavigateToOverview();
                }
            }

            // Remove all detail panels that are not being downloaded
            List<string> removes = new List<string>();
            foreach (string key in savedResourceDetailsPanels.Keys)
            {
                var hubResourceItemDetailUI = savedResourceDetailsPanels[key];
                if (hubResourceItemDetailUI.connectedItem != null && hubResourceItemDetailUI.connectedItem.IsDownloading)
                {
                    // Keep it
                }
                else
                {
                    // Remove it
                    removes.Add(key);
                }
            }
            // Remove all detail panels that are not being downloaded
            foreach (var key in removes)
            {
                var hubResourceItemDetailUI = savedResourceDetailsPanels[key];
                savedResourceDetailsPanels.Remove(key);
                UnityEngine.Object.Destroy(hubResourceItemDetailUI.gameObject);
            }

        }

        protected void CloseAllDetails()
        {
            while (resourceDetailStack.Count > 0)
            {
                HubResourceItemDetailUI hubResourceItemDetailUI = resourceDetailStack.Pop();
                if (hubResourceItemDetailUI.connectedItem != null && hubResourceItemDetailUI.connectedItem.IsDownloading)
                {
                    hubResourceItemDetailUI.gameObject.SetActive(false);
                    savedResourceDetailsPanels.Add(hubResourceItemDetailUI.connectedItem.ResourceId, hubResourceItemDetailUI);
                    continue;
                }
                if (hubResourceItemDetailUI.connectedItem != null)
                {
                    savedResourceDetailsPanels.Remove(hubResourceItemDetailUI.connectedItem.ResourceId);
                }
                UnityEngine.Object.Destroy(hubResourceItemDetailUI.gameObject);
            }
            if (detailPanel != null)
            {
                detailPanel.SetActive(false);
            }
        }

        public RectTransform CreateDownloadPrefabInstance()
        {
            RectTransform result = null;
            if (packageDownloadPrefab != null)
            {
                result = UnityEngine.Object.Instantiate(packageDownloadPrefab);
            }
            return result;
        }

        public RectTransform CreateCreatorSupportButtonPrefabInstance()
        {
            RectTransform result = null;
            if (creatorSupportButtonPrefab != null)
            {
                result = UnityEngine.Object.Instantiate(creatorSupportButtonPrefab);
            }
            return result;
        }

        protected void FindMissingPackagesErrorCallback(string err)
        {
            //SuperController.LogError("Error during hub request " + err);
        }

        protected void FindMissingPackagesCallback(SimpleJSON.JSONNode jsonNode)
        {
            if (!(jsonNode != null))
            {
                return;
            }
            JSONClass asObject = jsonNode.AsObject;
            if (!(asObject != null))
            {
                return;
            }
            string text = asObject["status"];
            if (text != null && text == "error")
            {
                string text2 = jsonNode["error"];
                LogUtil.LogError("findPackages returned error " + text2);
                return;
            }
            JSONClass asObject2 = jsonNode["packages"].AsObject;
            if (!(asObject2 != null))
            {
                return;
            }
            if (missingPackages != null)
            {
                foreach (HubResourcePackageUI missingPackage in missingPackages)
                {
                    UnityEngine.Object.Destroy(missingPackage.gameObject);
                }
                missingPackages.Clear();
            }
            else
            {
                missingPackages = new List<HubResourcePackageUI>();
            }
            foreach (string checkMissingPackageName in checkMissingPackageNames)
            {
                JSONClass jSONClass = asObject2[checkMissingPackageName].AsObject;
                if (jSONClass == null)
                {
                    jSONClass = new JSONClass();
                    jSONClass["filename"] = checkMissingPackageName;
                    jSONClass["downloadUrl"] = "null";
                }
                else
                {
                    if (Regex.IsMatch(checkMissingPackageName, "[0-9]+$"))
                    {
                        string text3 = jSONClass["filename"];
                        if (text3 == null || text3 == "null" || text3 != checkMissingPackageName + ".var")
                        {
                            LogUtil.LogError("Missing file name " + text3 + " does not match missing package name " + checkMissingPackageName);
                            jSONClass["filename"] = checkMissingPackageName;
                            jSONClass["file_size"] = "null";
                            jSONClass["licenseType"] = "null";
                            jSONClass["downloadUrl"] = "null";
                        }
                    }
                    else
                    {
                        string text4 = jSONClass["filename"];
                        if (text4 == null || text4 == "null")
                        {
                            jSONClass["filename"] = checkMissingPackageName;
                        }
                    }
                    string text5 = jSONClass["resource_id"];
                    if (text5 == null || text5 == "null")
                    {
                        jSONClass["downloadUrl"] = "null";
                    }
                }
                HubResourcePackage hubResourcePackage = new HubResourcePackage(jSONClass, this, true);
                RectTransform rectTransform = CreateDownloadPrefabInstance();
                if (rectTransform != null)
                {
                    rectTransform.SetParent(missingPackagesContainer, false);
                    HubResourcePackageUI component = rectTransform.GetComponent<HubResourcePackageUI>();
                    if (component != null)
                    {
                        missingPackages.Add(component);
                        hubResourcePackage.RegisterUI(component);
                    }
                }
            }
        }

        public void OpenMissingPackagesPanel()
        {
            if (_hubEnabled)
            {
                if ((missingPackagesPanel == null) || (missingPackagesContainer == null))
                {
                    return;
                }

                Show();
                if (missingPackagesPanel != null)
                {
                    missingPackagesPanel.SetActive(true);
                }
                if (downloadQueue.Count != 0)
                {
                    return;
                }


                List<string> missingPackageNames = FileManager.singleton.GetMissingDependenciesNames();
                if (missingPackageNames.Count > 0)
                {
                    JSONClass jSONClass = new JSONClass();
                    jSONClass["source"] = "VaM";
                    jSONClass["action"] = "findPackages";
                    checkMissingPackageNames = missingPackageNames;
                    jSONClass["packages"] = string.Join(",", missingPackageNames.ToArray());
                    string postData = jSONClass.ToString();
                    StartCoroutine(PostRequest(apiUrl, postData, FindMissingPackagesCallback, FindMissingPackagesErrorCallback));
                }
                else if (missingPackages != null)
                {
                    foreach (HubResourcePackageUI missingPackage in missingPackages)
                    {
                        UnityEngine.Object.Destroy(missingPackage.gameObject);
                    }
                    missingPackages.Clear();
                }
                else
                {
                    missingPackages = new List<HubResourcePackageUI>();
                }
            }
            else
            {
                LogUtil.LogWarning("[VPB]Cannot perform action. Hub is disabled in User Preferences");
            }
        }

        public void CloseMissingPackagesPanel()
        {
            if (missingPackagesPanel != null)
            {
                missingPackagesPanel.SetActive(false);
            }
        }

        public void DownloadAllMissingPackages()
        {
            if (missingPackages == null)
            {
                return;
            }
            foreach (HubResourcePackageUI missingPackage in missingPackages)
            {
                missingPackage.connectedItem.Download();
            }
        }

        public string GetPackageHubResourceId(string packageId)
        {
            string value = null;
            if (packageIdToResourceId != null)
            {
                packageIdToResourceId.TryGetValue(packageId, out value);
            }
            return value;
        }

        protected void GetPackagesJSONErrorCallback(string err)
        {
            //SuperController.LogError("Error during hub request for packages.json " + err);
        }

        protected void GetPackagesJSONCallback(SimpleJSON.JSONNode jsonNode)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (!(jsonNode != null))
            {
                return;
            }
            JSONClass asObject = jsonNode.AsObject;
            if (!(asObject != null))
            {
                return;
            }
            packageGroupToLatestVersion = new Dictionary<string, int>();
            packageIdToResourceId = new Dictionary<string, string>();
            int processed = 0;
            foreach (string key2 in asObject.Keys)
            {
                string text = Regex.Replace(key2, "\\.var$", string.Empty);
                string text2 = FileManager.PackageIDToPackageVersion(text);
                int result;
                if (text2 == null || !int.TryParse(text2, out result))
                {
                    continue;
                }
                string value = asObject[key2];
                packageIdToResourceId.Add(text, value);
                string key = FileManager.PackageIDToPackageGroupID(text);
                int value2;
                if (packageGroupToLatestVersion.TryGetValue(key, out value2))
                {
                    if (result > value2)
                    {
                        packageGroupToLatestVersion.Remove(key);
                        packageGroupToLatestVersion.Add(key, result);
                    }
                }
                else
                {
                    packageGroupToLatestVersion.Add(key, result);
                }
                processed++;
            }
            LogUtil.Log($"HubBrowse.GetPackagesJSONCallback DONE processed={processed} groups={(packageGroupToLatestVersion != null ? packageGroupToLatestVersion.Count : 0)} ids={(packageIdToResourceId != null ? packageIdToResourceId.Count : 0)} ms={sw.ElapsedMilliseconds}");
        }

        protected void FindUpdatesErrorCallback(string err)
        {
            //LogUtil.Log("Error during hub request " + err);
        }

        protected void FindUpdatesCallback(SimpleJSON.JSONNode jsonNode)
        {
            if (!(jsonNode != null))
            {
                return;
            }
            JSONClass asObject = jsonNode.AsObject;
            if (!(asObject != null))
            {
                return;
            }
            string text = asObject["status"];
            if (text != null && text == "error")
            {
                string text2 = jsonNode["error"];
                //LogUtil.Log("findPackages returned error " + text2);
                return;
            }
            JSONClass asObject2 = jsonNode["packages"].AsObject;
            if (!(asObject2 != null))
            {
                return;
            }
            if (updates != null)
            {
                foreach (HubResourcePackageUI update in updates)
                {
                    UnityEngine.Object.Destroy(update.gameObject);
                }
                updates.Clear();
            }
            else
            {
                updates = new List<HubResourcePackageUI>();
            }
            foreach (string checkUpdateName in checkUpdateNames)
            {
                JSONClass jSONClass = asObject2[checkUpdateName].AsObject;
                if (jSONClass == null)
                {
                    jSONClass = new JSONClass();
                    jSONClass["filename"] = checkUpdateName;
                    jSONClass["downloadUrl"] = "null";
                }
                else
                {
                    string text3 = jSONClass["filename"];
                    if (text3 == null || text3 == "null")
                    {
                        jSONClass["filename"] = checkUpdateName;
                    }
                }
                HubResourcePackage hubResourcePackage = new HubResourcePackage(jSONClass, this, false);
                RectTransform rectTransform = CreateDownloadPrefabInstance();
                if (rectTransform != null)
                {
                    rectTransform.SetParent(updatesContainer, false);
                    HubResourcePackageUI component = rectTransform.GetComponent<HubResourcePackageUI>();
                    if (component != null)
                    {
                        updates.Add(component);
                        hubResourcePackage.RegisterUI(component);
                    }
                }
            }
        }

        public void OpenUpdatesPanel()
        {
            if (_hubEnabled)
            {
                if (!(updatesPanel != null) || !(updatesContainer != null))
                {
                    return;
                }
                Show();
                if (updatesPanel != null)
                {
                    updatesPanel.SetActive(true);
                }
                if (downloadQueue.Count != 0)
                {
                    return;
                }
                checkUpdateNames = new List<string>();
                if (packageGroupToLatestVersion != null)
                {
                    foreach (VarPackageGroup packageGroup in FileManager.GetPackageGroups())
                    {
                        int value;
                        if (packageGroupToLatestVersion.TryGetValue(packageGroup.Name, out value) && packageGroup.NewestVersion < value)
                        {
                            checkUpdateNames.Add(packageGroup.Name + ".latest");
                        }
                    }
                }
                if (checkUpdateNames.Count > 0)
                {
                    JSONClass jSONClass = new JSONClass();
                    jSONClass["source"] = "VaM";
                    jSONClass["action"] = "findPackages";
                    jSONClass["packages"] = string.Join(",", checkUpdateNames.ToArray());
                    string postData = jSONClass.ToString();
                    StartCoroutine(PostRequest(apiUrl, postData, FindUpdatesCallback, FindUpdatesErrorCallback));
                }
                else if (updates != null)
                {
                    foreach (HubResourcePackageUI update in updates)
                    {
                        UnityEngine.Object.Destroy(update.gameObject);
                    }
                    updates.Clear();
                }
                else
                {
                    updates = new List<HubResourcePackageUI>();
                }
            }
            else
            {
                //LogUtil.Log("Cannot perform action. Hub is disabled in User Preferences");
            }
        }

        public void CloseUpdatesPanel()
        {
            if (updatesPanel != null)
            {
                updatesPanel.SetActive(false);
            }
        }

        public void DownloadAllUpdates()
        {
            if (updates == null)
            {
                return;
            }
            foreach (HubResourcePackageUI update in updates)
            {
                update.connectedItem.Download();
            }
        }

        protected void RefreshCookies()
        {
            if (GetBrowserCookiesRoutine == null && browser != null)
            {
                GetBrowserCookiesRoutine = StartCoroutine(GetBrowserCookies());
            }
        }

        protected IEnumerator GetBrowserCookies()
        {
            if (hubCookies == null)
            {
                hubCookies = new List<string>();
            }
            while (!browser.IsReady)
            {
                yield return null;
            }
            IPromise<List<Cookie>> promise = browser.CookieManager.GetCookies();
            yield return promise.ToWaitFor(true);
            hubCookies.Clear();
            foreach (Cookie item in promise.Value)
            {
                if (item.domain == cookieHost)
                {
                    hubCookies.Add(string.Format("{0}={1}", item.name, item.value));
                }
            }
            GetBrowserCookiesRoutine = null;
        }

        protected IEnumerator DownloadRoutine()
        {
            while (true)
            {
                if (downloadQueue.Count > 0)
                {
                    isDownloadingJSON.val = true;
                    downloadQueuedCountJSON.val = "Queued: " + downloadQueue.Count;
                    DownloadRequest request = downloadQueue.Dequeue();
                    yield return BinaryGetRequest(request,request.url, request.startedCallback, request.successCallback, request.errorCallback, request.progressCallback, hubCookies);
                    if (downloadQueue.Count == 0)
                    {
                        VPB.FileManager.Refresh();
                    }
                }
                else
                {
                    isDownloadingJSON.val = false;
                }
                yield return null;
            }
        }

        protected void OnPackageRefresh()
        {
            if (items != null)
            {
                foreach (HubResourceItemUI item in items)
                {
                    item.connectedItem.Refresh();
                }
            }
            if (missingPackages != null)
            {
                foreach (HubResourcePackageUI missingPackage in missingPackages)
                {
                    missingPackage.connectedItem.Refresh();
                }
            }
            if (updates != null)
            {
                foreach (HubResourcePackageUI update in updates)
                {
                    update.connectedItem.Refresh();
                }
            }
            if (resourceDetailStack == null)
            {
                return;
            }
            foreach (HubResourceItemDetailUI item2 in resourceDetailStack)
            {
                item2.connectedItem.Refresh();
            }
        }

        public DownloadRequest QueueDownload(string url, string promotionalUrl, BinaryRequestStartedCallback startedCallback, RequestProgressCallback progressCallback, BinaryRequestSuccessCallback successCallback, RequestErrorCallback errorCallback)
        {
            DownloadRequest downloadRequest = new DownloadRequest();
            downloadRequest.url = url;
            if (downloadQueue.Count == 0)
            {
                downloadRequest.promotionalUrl = promotionalUrl;
            }
            downloadRequest.startedCallback = startedCallback;
            downloadRequest.progressCallback = progressCallback;
            downloadRequest.successCallback = successCallback;
            downloadRequest.errorCallback = errorCallback;
            downloadQueue.Enqueue(downloadRequest);
            return downloadRequest;
        }

        protected void OpenDownloading()
        {
            if (savedResourceDetailsPanels == null)
            {
                return;
            }


            List<string> shows = new List<string>();
            foreach (string key in savedResourceDetailsPanels.Keys)
            {
                shows.Add(key);
            }
            foreach (string key in shows)
            {
                OpenDetail(key);
            }
        }

        protected void GetInfoCallback(SimpleJSON.JSONNode jsonNode)
        {
            if (refreshingGetInfoPanel != null)
            {
                refreshingGetInfoPanel.gameObject.SetActive(false);
            }
            if (failedGetInfoPanel != null)
            {
                failedGetInfoPanel.gameObject.SetActive(false);
            }
            hubInfoCoroutine = null;
            hubInfoRefreshing = false;
            hubInfoSuccess = true;
            JSONClass asObject = jsonNode.AsObject;
            if (!(asObject != null))
            {
                return;
            }
            if (asObject["location"] != null)
            {
                JSONArray asArray = asObject["location"].AsArray;
                if (asArray != null)
                {
                    List<string> list = new List<string>();
                    list.Add("All");
                    IEnumerator enumerator = asArray.GetEnumerator();
                    try
                    {
                        while (enumerator.MoveNext())
                        {
                            SimpleJSON.JSONNode jSONNode = (SimpleJSON.JSONNode)enumerator.Current;
                            list.Add(jSONNode);
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
                    hostedOptionChooser.choices = list;
                }
            }
            if (asObject["category"] != null)
            {
                JSONArray asArray2 = asObject["category"].AsArray;
                if (asArray2 != null)
                {
                    List<string> list2 = new List<string>();
                    list2.Add("All");
                    IEnumerator enumerator2 = asArray2.GetEnumerator();
                    try
                    {
                        while (enumerator2.MoveNext())
                        {
                            SimpleJSON.JSONNode jSONNode2 = (SimpleJSON.JSONNode)enumerator2.Current;
                            list2.Add(jSONNode2);
                        }
                    }
                    finally
                    {
                        IDisposable disposable2;
                        if ((disposable2 = enumerator2 as IDisposable) != null)
                        {
                            disposable2.Dispose();
                        }
                    }
                    payTypeFilterChooser.choices = list2;
                }
            }
            if (asObject["type"] != null)
            {
                JSONArray asArray3 = asObject["type"].AsArray;
                if (asArray3 != null)
                {
                    List<string> list3 = new List<string>();
                    list3.Add("All");
                    IEnumerator enumerator3 = asArray3.GetEnumerator();
                    try
                    {
                        while (enumerator3.MoveNext())
                        {
                            SimpleJSON.JSONNode jSONNode3 = (SimpleJSON.JSONNode)enumerator3.Current;
                            list3.Add(jSONNode3);
                        }
                    }
                    finally
                    {
                        IDisposable disposable3;
                        if ((disposable3 = enumerator3 as IDisposable) != null)
                        {
                            disposable3.Dispose();
                        }
                    }
                    categoryFilterChooser.choices = list3;
                }
            }
            if (asObject["users"] != null)
            {
                JSONClass asObject2 = asObject["users"].AsObject;
                if (asObject2 != null)
                {
                    List<string> list4 = new List<string>();
                    list4.Add("All");
                    foreach (string key in asObject2.Keys)
                    {
                        list4.Add(key);
                    }
                    creatorFilterChooser.choices = list4;
                }
            }
            if (asObject["tags"] != null)
            {
                JSONClass asObject3 = asObject["tags"].AsObject;
                if (asObject3 != null)
                {
                    List<string> list5 = new List<string>();
                    list5.Add("All");
                    foreach (string key2 in asObject3.Keys)
                    {
                        list5.Add(key2);
                    }
                    tagsFilterChooser.choices = list5;
                }
            }
            if (asObject["sort"] != null)
            {
                JSONArray asArray4 = asObject["sort"].AsArray;
                if (asArray4 != null)
                {
                    List<string> list6 = new List<string>();
                    List<string> list7 = new List<string>();
                    list7.Add("None");
                    IEnumerator enumerator6 = asArray4.GetEnumerator();
                    try
                    {
                        while (enumerator6.MoveNext())
                        {
                            SimpleJSON.JSONNode jSONNode4 = (SimpleJSON.JSONNode)enumerator6.Current;
                            list6.Add(jSONNode4);
                            list7.Add(jSONNode4);
                        }
                    }
                    finally
                    {
                        IDisposable disposable4;
                        if ((disposable4 = enumerator6 as IDisposable) != null)
                        {
                            disposable4.Dispose();
                        }
                    }
                    sortPrimaryChooser.choices = list6;
                    sortSecondaryChooser.choices = list7;
                }
            }

            ApplyPersistedHubSettingsIfNeeded();

            string text = asObject["last_update"];
            if (packagesJSONUrl != null && packagesJSONUrl != string.Empty && text != null)
            {
                string uri = packagesJSONUrl + "?" + text;
                LogUtil.Log($"HubBrowse requesting packages.json uri={uri}");
                StartCoroutine(GetRequest(uri, GetPackagesJSONCallback, GetPackagesJSONErrorCallback));
            }
        }

        protected static string NormalizeHubSetting(string v)
        {
            if (v == null) return string.Empty;
            v = v.Trim();
            if (string.Equals(v, "null", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return v;
        }

        protected static string ValidateChoice(JSONStorableStringChooser chooser, string desired, string fallback)
        {
            string d = NormalizeHubSetting(desired);
            if (string.IsNullOrEmpty(d)) return fallback;
            if (chooser == null || chooser.choices == null) return fallback;
            foreach (var c in chooser.choices)
            {
                if (string.Equals(c, d, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }
            return fallback;
        }

        protected static int ClampInt(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        protected void ApplyPersistedHubSettingsIfNeeded()
        {
            if (hubSettingsApplied) return;
            if (Settings.Instance == null) return;

            suppressHubSettingsSave = true;
            try
            {
                bool onlyDl = (Settings.Instance.HubOnlyDownloadable != null) ? Settings.Instance.HubOnlyDownloadable.Value : true;
                if (onlyDownloadable != null)
                {
                    onlyDownloadable.valNoCallback = onlyDl;
                }

                string hosted = (Settings.Instance.HubHostedOption != null) ? Settings.Instance.HubHostedOption.Value : _hostedOption;
                hosted = ValidateChoice(hostedOptionChooser, hosted, "All");
                _hostedOption = hosted;
                if (hostedOptionChooser != null) hostedOptionChooser.valNoCallback = hosted;

                string payType = (Settings.Instance.HubPayTypeFilter != null) ? Settings.Instance.HubPayTypeFilter.Value : _payTypeFilter;
                payType = ValidateChoice(payTypeFilterChooser, payType, "All");
                if (onlyDl) payType = "Free";
                _payTypeFilter = payType;
                if (payTypeFilterChooser != null) payTypeFilterChooser.valNoCallback = payType;

                string category = (Settings.Instance.HubCategoryFilter != null) ? Settings.Instance.HubCategoryFilter.Value : _categoryFilter;
                category = ValidateChoice(categoryFilterChooser, category, "All");
                _categoryFilter = category;
                if (categoryFilterChooser != null) categoryFilterChooser.valNoCallback = category;

                string creator = (Settings.Instance.HubCreatorFilter != null) ? Settings.Instance.HubCreatorFilter.Value : _creatorFilter;
                creator = ValidateChoice(creatorFilterChooser, creator, "All");
                _creatorFilter = creator;
                if (creatorFilterChooser != null) creatorFilterChooser.valNoCallback = creator;

                string tags = (Settings.Instance.HubTagsFilter != null) ? Settings.Instance.HubTagsFilter.Value : _tagsFilter;
                tags = ValidateChoice(tagsFilterChooser, tags, "All");
                _tagsFilter = tags;
                if (tagsFilterChooser != null) tagsFilterChooser.valNoCallback = tags;

                string sortPrimary = (Settings.Instance.HubSortPrimary != null) ? Settings.Instance.HubSortPrimary.Value : _sortPrimary;
                sortPrimary = ValidateChoice(sortPrimaryChooser, sortPrimary, "Latest Update");
                _sortPrimary = sortPrimary;
                if (sortPrimaryChooser != null) sortPrimaryChooser.valNoCallback = sortPrimary;

                string sortSecondary = (Settings.Instance.HubSortSecondary != null) ? Settings.Instance.HubSortSecondary.Value : _sortSecondary;
                sortSecondary = ValidateChoice(sortSecondaryChooser, sortSecondary, "None");
                _sortSecondary = sortSecondary;
                if (sortSecondaryChooser != null) sortSecondaryChooser.valNoCallback = sortSecondary;

                string search = (Settings.Instance.HubSearchText != null) ? Settings.Instance.HubSearchText.Value : _searchFilter;
                search = NormalizeHubSetting(search);
                _searchFilter = search;
                _minLengthSearchFilter = (search.Length > 2) ? search : string.Empty;
                if (searchFilterJSON != null) searchFilterJSON.valNoCallback = search;

                int perPage = (Settings.Instance.HubItemsPerPage != null) ? Settings.Instance.HubItemsPerPage.Value : _numPerPageInt;
                _numPerPageInt = ClampInt(perPage, 1, 500);

                int page = (Settings.Instance.HubCurrentPage != null) ? Settings.Instance.HubCurrentPage.Value : _currentPageInt;
                _currentPageInt = ClampInt(page, 1, 99999);
                _currentPageString = _currentPageInt.ToString();
                if (currentPageJSON != null) currentPageJSON.valNoCallback = _currentPageString;
                SetPageInfo();
            }
            finally
            {
                suppressHubSettingsSave = false;
            }

            hubSettingsApplied = true;

            if (_hubEnabled && _isShowing)
            {
                RefreshResources();
            }
        }

        protected void GetInfoErrorCallback(string err)
        {
            if (refreshingGetInfoPanel != null)
            {
                refreshingGetInfoPanel.gameObject.SetActive(false);
            }
            if (failedGetInfoPanel != null)
            {
                failedGetInfoPanel.gameObject.SetActive(true);
            }
            if (getInfoErrorText != null)
            {
                getInfoErrorText.text = err;
            }
            hubInfoCoroutine = null;
            hubInfoRefreshing = false;
            hubInfoSuccess = false;
        }

        protected void GetHubInfo()
        {
            if (!hubInfoRefreshing)
            {
                LogUtil.Log("HubBrowse.GetHubInfo START");
                if (failedGetInfoPanel != null)
                {
                    failedGetInfoPanel.gameObject.SetActive(false);
                }
                JSONClass jSONClass = new JSONClass();
                jSONClass["source"] = "VaM";
                jSONClass["action"] = "getInfo";
                string postData = jSONClass.ToString();
                LogUtil.Log($"HubBrowse.GetHubInfo POST prepared len={(postData != null ? postData.Length : 0)}");
                hubInfoRefreshing = true;
                if (refreshingGetInfoPanel != null)
                {
                    refreshingGetInfoPanel.gameObject.SetActive(true);
                }
                hubInfoCoroutine = StartCoroutine(PostRequest(apiUrl, postData, GetInfoCallback, GetInfoErrorCallback));
            }
        }

        protected void CancelGetHubInfo()
        {
            if (hubInfoRefreshing && hubInfoCoroutine != null)
            {
                StopCoroutine(hubInfoCoroutine);
                GetInfoErrorCallback("Cancelled");
            }
        }

        protected void Init()
        {
            LogUtil.LogVerboseUi("HubBrowse Init");

            if (Settings.Instance != null)
            {
                if (Settings.Instance.HubHostedOption != null) _hostedOption = NormalizeHubSetting(Settings.Instance.HubHostedOption.Value);
                if (Settings.Instance.HubPayTypeFilter != null) _payTypeFilter = NormalizeHubSetting(Settings.Instance.HubPayTypeFilter.Value);
                if (Settings.Instance.HubCategoryFilter != null) _categoryFilter = NormalizeHubSetting(Settings.Instance.HubCategoryFilter.Value);
                if (Settings.Instance.HubCreatorFilter != null) _creatorFilter = NormalizeHubSetting(Settings.Instance.HubCreatorFilter.Value);
                if (Settings.Instance.HubTagsFilter != null) _tagsFilter = NormalizeHubSetting(Settings.Instance.HubTagsFilter.Value);
                if (Settings.Instance.HubSortPrimary != null) _sortPrimary = NormalizeHubSetting(Settings.Instance.HubSortPrimary.Value);
                if (Settings.Instance.HubSortSecondary != null) _sortSecondary = NormalizeHubSetting(Settings.Instance.HubSortSecondary.Value);
                if (Settings.Instance.HubSearchText != null)
                {
                    _searchFilter = NormalizeHubSetting(Settings.Instance.HubSearchText.Value);
                    _minLengthSearchFilter = (_searchFilter.Length > 2) ? _searchFilter : string.Empty;
                }
                if (Settings.Instance.HubItemsPerPage != null) _numPerPageInt = ClampInt(Settings.Instance.HubItemsPerPage.Value, 1, 500);
                if (Settings.Instance.HubCurrentPage != null)
                {
                    _currentPageInt = ClampInt(Settings.Instance.HubCurrentPage.Value, 1, 99999);
                    _currentPageString = _currentPageInt.ToString();
                }
            }

            hubEnabledJSON = new JSONStorableBool("hubEnabled", _hubEnabled, SyncHubEnabled);
            enableHubAction = new JSONStorableAction("EnableHub", EnableHub);
            webBrowserEnabledJSON = new JSONStorableBool("webBrowserEnabled", _webBrowserEnabled, SyncWebBrowserEnabled);
            enableWebBrowserAction = new JSONStorableAction("EnableWebBrowser", EnableWebBrowser);
            cancelGetHubInfoAction = new JSONStorableAction("CancelGetHubInfo", CancelGetHubInfo);
            retryGetHubInfoAction = new JSONStorableAction("RetryGetHubInfo", GetHubInfo);
            numResourcesJSON = new JSONStorableString("numResources", "0", SyncNumResources);
            pageInfoJSON = new JSONStorableString("pageInfo", "Page 0 of 0");
            numPagesJSON = new JSONStorableString("numPages", "0", SyncNumPages);
            currentPageJSON = new JSONStorableString("currentPage", _currentPageString, SyncCurrentPage);
            firstPageAction = new JSONStorableAction("FirstPage", FirstPage);
            previousPageAction = new JSONStorableAction("PreviousPage", PreviousPage);
            RegisterAction(previousPageAction);
            nextPageAction = new JSONStorableAction("NextPage", NextPage);
            RegisterAction(nextPageAction);
            refreshResourcesAction = new JSONStorableAction("RefreshResources", ResetRefresh);
            RegisterAction(refreshResourcesAction);
            clearFiltersAction = new JSONStorableAction("ResetFilters", ResetFiltersAndRefresh);
            RegisterAction(clearFiltersAction);

            numPerPageJSON = new JSONStorableFloat("numPerPage", _numPerPageInt, SyncNumPerPage, 1f, 500f, true, false);
            numPerPageJSON.isStorable = false;
            numPerPageJSON.isRestorable = false;
            RegisterFloat(numPerPageJSON);

            List<string> list = new List<string>();
            list.Add("All");
            List<string> choicesList = list;
            hostedOptionChooser = new JSONStorableStringChooser("hostedOption", choicesList, _hostedOption, "Hosted Option", SyncHostedOption);
            hostedOptionChooser.isStorable = false;
            hostedOptionChooser.isRestorable = false;
            RegisterStringChooser(hostedOptionChooser);

            searchFilterJSON = new JSONStorableString("searchFilter", string.Empty, SyncSearchFilter);
            searchFilterJSON.enableOnChange = true;
            searchFilterJSON.isStorable = false;
            searchFilterJSON.isRestorable = false;
            RegisterString(searchFilterJSON);

            list = new List<string>();
            list.Add("All");
            List<string> choicesList2 = list;
            payTypeFilterChooser = new JSONStorableStringChooser("payType", choicesList2, _payTypeFilter, "Pay Type", SyncPayTypeFilter);
            payTypeFilterChooser.isStorable = false;
            payTypeFilterChooser.isRestorable = false;
            RegisterStringChooser(payTypeFilterChooser);
            list = new List<string>();
            list.Add("All");
            List<string> choicesList3 = list;
            categoryFilterChooser = new JSONStorableStringChooser("category", choicesList3, _categoryFilter, "Category", SyncCategoryFilter);
            categoryFilterChooser.isStorable = false;
            categoryFilterChooser.isRestorable = false;
            RegisterStringChooser(categoryFilterChooser);
            list = new List<string>();
            list.Add("All");
            List<string> choicesList4 = list;
            creatorFilterChooser = new JSONStorableStringChooser("creator", choicesList4, _creatorFilter, "Creator", SyncCreatorFilter);
            creatorFilterChooser.isStorable = false;
            creatorFilterChooser.isRestorable = false;
            RegisterStringChooser(creatorFilterChooser);
            list = new List<string>();
            list.Add("All");
            List<string> choicesList5 = list;
            tagsFilterChooser = new JSONStorableStringChooser("tags", choicesList5, _tagsFilter, "Tags", SyncTagsFilter);
            tagsFilterChooser.isStorable = false;
            tagsFilterChooser.isRestorable = false;
            RegisterStringChooser(tagsFilterChooser);
            list = new List<string>();
            list.Add("Latest Update");
            List<string> choicesList6 = list;
            sortPrimaryChooser = new JSONStorableStringChooser("sortPrimary", choicesList6, _sortPrimary, "Primary Sort", SyncSortPrimary);
            sortPrimaryChooser.isStorable = false;
            sortPrimaryChooser.isRestorable = false;
            RegisterStringChooser(sortPrimaryChooser);
            list = new List<string>();
            list.Add("None");
            List<string> choicesList7 = list;
            sortSecondaryChooser = new JSONStorableStringChooser("sortSecondary", choicesList7, _sortSecondary, "Secondary Sort", SyncSortSecondary);
            sortSecondaryChooser.isStorable = false;
            sortSecondaryChooser.isRestorable = false;
            RegisterStringChooser(sortSecondaryChooser);
            openMissingPackagesPanelAction = new JSONStorableAction("OpenMissingPackagesPanel", OpenMissingPackagesPanel);
            RegisterAction(openMissingPackagesPanelAction);
            closeMissingPackagesPanelAction = new JSONStorableAction("CloseMissingPackagesPanel", CloseMissingPackagesPanel);
            RegisterAction(closeMissingPackagesPanelAction);
            downloadAllMissingPackagesAction = new JSONStorableAction("DownloadAllMissingPackages", DownloadAllMissingPackages);
            RegisterAction(downloadAllMissingPackagesAction);
            openUpdatesPanelAction = new JSONStorableAction("OpenUpdatesPanel", OpenUpdatesPanel);
            RegisterAction(openUpdatesPanelAction);
            closeUpdatesPanelAction = new JSONStorableAction("CloseUpdatesPanel", CloseUpdatesPanel);
            RegisterAction(closeUpdatesPanelAction);
            downloadAllUpdatesAction = new JSONStorableAction("DownloadAllUpdates", DownloadAllUpdates);
            RegisterAction(downloadAllUpdatesAction);
            isDownloadingJSON = new JSONStorableBool("isDownloading", false);
            downloadQueuedCountJSON = new JSONStorableString("downloadQueuedCount", "Queued: 0");
            openDownloadingAction = new JSONStorableAction("OpenDownloading", OpenDownloading);
            RegisterAction(openDownloadingAction);
            resourceDetailStack = new Stack<HubResourceItemDetailUI>();
            savedResourceDetailsPanels = new Dictionary<string, HubResourceItemDetailUI>();
            downloadQueue = new Queue<DownloadRequest>();
            StartCoroutine(DownloadRoutine());

        }

        protected override void InitUI(Transform t, bool isAlt)
        {
            LogUtil.LogVerboseUi("HubBrowse InitUI");
            if (t == null) return;
            MVR.Hub.HubBrowseUI componentInChildren = t.GetComponentInChildren<MVR.Hub.HubBrowseUI>(true);
            if (componentInChildren == null) return;
            if (!isAlt)
            {
                hubBrowseUI = componentInChildren;

                itemContainer = componentInChildren.itemContainer;
                itemScrollRect = componentInChildren.itemScrollRect;
                refreshingGetInfoPanel = componentInChildren.refreshingGetInfoPanel;
                if (refreshingGetInfoPanel != null && hubInfoRefreshing)
                {
                    refreshingGetInfoPanel.gameObject.SetActive(true);
                }
                failedGetInfoPanel = componentInChildren.failedGetInfoPanel;
                if (failedGetInfoPanel != null && !hubInfoSuccess && !hubInfoRefreshing)
                {
                    failedGetInfoPanel.gameObject.SetActive(true);
                }
                getInfoErrorText = componentInChildren.getInfoErrorText;
                detailPanel = componentInChildren.detailPanel;
                resourceDetailContainer = componentInChildren.resourceDetailContainer;
                browser = componentInChildren.browser;
                webBrowser = componentInChildren.webBrowser;
                isWebLoadingIndicator = componentInChildren.isWebLoadingIndicator;
                refreshIndicator = componentInChildren.refreshIndicator;
                missingPackagesPanel = componentInChildren.missingPackagesPanel;
                missingPackagesContainer = componentInChildren.missingPackagesContainer;
                updatesPanel = componentInChildren.updatesPanel;
                updatesContainer = componentInChildren.updatesContainer;
            }
            hubEnabledJSON.RegisterNegativeIndicator(componentInChildren.hubEnabledNegativeIndicator, isAlt);
            enableHubAction.RegisterButton(componentInChildren.enableHubButton, isAlt);
            webBrowserEnabledJSON.RegisterNegativeIndicator(componentInChildren.webBrowserEnabledNegativeIndicator, isAlt);
            enableWebBrowserAction.RegisterButton(componentInChildren.enabledWebBrowserButton, isAlt);
            cancelGetHubInfoAction.RegisterButton(componentInChildren.cancelGetHubInfoButton, isAlt);
            retryGetHubInfoAction.RegisterButton(componentInChildren.retryGetHubInfoButton, isAlt);
            pageInfoJSON.RegisterText(componentInChildren.pageInfoText, isAlt);
            numResourcesJSON.RegisterText(componentInChildren.numResourcesText, isAlt);
            firstPageAction.RegisterButton(componentInChildren.firstPageButton, isAlt);
            previousPageAction.RegisterButton(componentInChildren.previousPageButton, isAlt);
            nextPageAction.RegisterButton(componentInChildren.nextPageButton, isAlt);
            refreshResourcesAction.RegisterButton(componentInChildren.refreshButton, isAlt);
            clearFiltersAction.RegisterButton(componentInChildren.clearFiltersButton, isAlt);

            try
            {
                componentInChildren.hostedOptionPopup.useFiltering = false;
                componentInChildren.hostedOptionPopup.numPopupValues = 0;
                hostedOptionChooser.RegisterPopup(componentInChildren.hostedOptionPopup, isAlt);
            }
            catch(Exception)
            {
            }


            searchFilterJSON.RegisterInputField(componentInChildren.searchInputField, isAlt);

            try
            {
                componentInChildren.payTypeFilterPopup.useFiltering = false;
                componentInChildren.payTypeFilterPopup.numPopupValues = 0;
                payTypeFilterChooser.RegisterPopup(componentInChildren.payTypeFilterPopup, isAlt);
            }
            catch(Exception e)
            {
                LogUtil.LogError("payTypeFilterPopup " + e.ToString());
            }

            try
            {
                componentInChildren.categoryFilterPopup.useFiltering = false;
                componentInChildren.categoryFilterPopup.numPopupValues = 0;
                categoryFilterChooser.RegisterPopup(componentInChildren.categoryFilterPopup, isAlt);
            }
            catch (Exception e)
            {
                LogUtil.LogError("categoryFilterPopup " + e.ToString());
            }
            try
            {
                componentInChildren.creatorFilterPopup.useFiltering = false;
                componentInChildren.creatorFilterPopup.numPopupValues = 0;
                creatorFilterChooser.RegisterPopup(componentInChildren.creatorFilterPopup, isAlt);
            }
            catch (Exception e)
            {
                LogUtil.LogError("creatorFilterPopup " + e.ToString());
            }
            try
            {
                componentInChildren.tagsFilterPopup.useFiltering = false;
                componentInChildren.tagsFilterPopup.numPopupValues = 0;
                tagsFilterChooser.RegisterPopup(componentInChildren.tagsFilterPopup, isAlt);

            }
            catch (Exception e)
            {
                LogUtil.LogError("tagsFilterPopup " + e.ToString());
            }
            //LogUtil.LogWarning("sortPrimaryChooser RegisterPopup");
            try
            {
                componentInChildren.sortPrimaryPopup.useFiltering = false;
                componentInChildren.sortPrimaryPopup.numPopupValues = 0;
                sortPrimaryChooser.RegisterPopup(componentInChildren.sortPrimaryPopup, isAlt);
            }
            catch (Exception e)
            {
                LogUtil.LogError("sortPrimaryPopup " + e.ToString());
            }
            //LogUtil.LogWarning("sortSecondaryChooser RegisterPopup");
            try
            {
                componentInChildren.sortSecondaryPopup.useFiltering = false;
                componentInChildren.sortSecondaryPopup.numPopupValues = 0;
                sortSecondaryChooser.RegisterPopup(componentInChildren.sortSecondaryPopup, isAlt);
            }
            catch (Exception e)
            {
                LogUtil.LogError("sortSecondaryPopup " + e.ToString());
            }
            openMissingPackagesPanelAction.RegisterButton(componentInChildren.openMissingPackagesPanelButton, isAlt);
            closeMissingPackagesPanelAction.RegisterButton(componentInChildren.closeMissingPackagesPanelButton, isAlt);
            downloadAllMissingPackagesAction.RegisterButton(componentInChildren.downloadAllMissingPackagesButton, isAlt);

            openUpdatesPanelAction.RegisterButton(componentInChildren.openUpdatesPanelButton, isAlt);
            closeUpdatesPanelAction.RegisterButton(componentInChildren.closeUpdatesPanelButton, isAlt);
            downloadAllUpdatesAction.RegisterButton(componentInChildren.downloadAllUpdatesButton, isAlt);
            isDownloadingJSON.RegisterIndicator(componentInChildren.isDownloadingIndicator, isAlt);
            downloadQueuedCountJSON.RegisterText(componentInChildren.downloadQueuedCountText, isAlt);
            openDownloadingAction.RegisterButton(componentInChildren.openDownloadingButton, isAlt);


            var openMissingPackagesPanelButton = componentInChildren.openMissingPackagesPanelButton;
            var relPos = openMissingPackagesPanelButton.transform.localPosition;
            Transform parent = openMissingPackagesPanelButton.transform.parent;

            bool initialOnlyDownloadable = (Settings.Instance != null && Settings.Instance.HubOnlyDownloadable != null) ? Settings.Instance.HubOnlyDownloadable.Value : true;
            onlyDownloadable = new JSONStorableBool("Only Downloadable", initialOnlyDownloadable, SyncOnlyDownloadable);
            var manager = SuperController.singleton.transform.Find("ScenePluginManager").GetComponent<MVRPluginManager>();
            if (manager != null && manager.configurableTogglePrefab != null)
            {
                RectTransform transform = UnityEngine.Object.Instantiate(manager.configurableTogglePrefab, parent) as RectTransform;
                transform.localPosition = new Vector3(relPos.x, relPos.y+80, relPos.z);
                transform.gameObject.SetActive(true);

                var uIDynamicToggle = transform.GetComponent<UIDynamicToggle>();
                if (uIDynamicToggle != null)
                {
                    uIDynamicToggle.label = onlyDownloadable.name;
                    onlyDownloadable.toggle = uIDynamicToggle.toggle;

                    uIDynamicToggle.backgroundImage.color = new Color32(133,255,133,255);
                }
            }
            

                LogUtil.LogVerboseUi("HubBrowse Init End");
        }
        JSONStorableBool onlyDownloadable;

        protected void SyncOnlyDownloadable(bool b)
        {
            if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubOnlyDownloadable != null)
            {
                Settings.Instance.HubOnlyDownloadable.Value = b;
            }

            if (b)
            {
                // PayType must be Free when Only Downloadable is enabled.
                if (_payTypeFilter != "Free")
                {
                    _payTypeFilter = "Free";
                    if (payTypeFilterChooser != null) payTypeFilterChooser.valNoCallback = "Free";
                    if (!suppressHubSettingsSave && Settings.Instance != null && Settings.Instance.HubPayTypeFilter != null)
                    {
                        Settings.Instance.HubPayTypeFilter.Value = "Free";
                    }
                }
            }

            ResetRefresh();
        }
        protected void OnLoad(ZenFulcrum.EmbeddedBrowser.JSONNode loadData)
        {
            try
            {
                if (browser != null && browser.IsReady)
                {
                    browser.EvalJS("\r\n\t\t\t\twindow.scrollTo(0,0);\r\n\t\t\t");
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB] HubBrowse OnLoad EvalJS failed: {ex.Message}");
            }

            try
            {
                RefreshCookies();
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB] HubBrowse RefreshCookies failed: {ex.Message}");
            }
        }

        protected override void Awake()
        {
            if (!awakecalled)
            {
                singleton = this;
                base.Awake();
                Init();
                InitUI();
                InitUIAlt();
                if (browser != null)
                {
                    browser.onLoad += OnLoad;
                }
            }
        }
        void OnDestroy()
        {
            singleton = null;
        }
        public void Prepare()
        {
            Init();
            InitUI();
            InitUIAlt();
        }

        protected void Update()
        {
            if (browser != null && isWebLoadingIndicator != null)
            {
                isWebLoadingIndicator.SetActive(browser.IsLoadingRaw);
            }
        }
    }
}
