using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;

namespace VPB.Hub
{
    public class GalleryHubController : MonoBehaviour
    {
        private static GalleryHubController _instance;
        public static GalleryHubController Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("GalleryHubController");
                    _instance = go.AddComponent<GalleryHubController>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private const string API_URL = "https://hub.virtamate.com/citizenx/api.php";
        private Dictionary<string, List<GalleryHubItem>> _cache = new Dictionary<string, List<GalleryHubItem>>();
        private Dictionary<string, float> _cacheTime = new Dictionary<string, float>();
        private const float CACHE_DURATION = 300f; // 5 minutes cache

        public event Action<string> OnLog;
        private HubInfo _cachedInfo = null;
        private float _infoCacheTime = 0f;

        public class HubInfo
        {
            public List<string> Categories = new List<string>();
            public List<string> Tags = new List<string>();
            public List<string> Creators = new List<string>();
            public List<string> PayTypes = new List<string>();
        }

        public void GetInfo(Action<HubInfo> onSuccess, Action<string> onError)
        {
            if (_cachedInfo != null && (Time.time - _infoCacheTime < CACHE_DURATION))
            {
                onSuccess?.Invoke(_cachedInfo);
                return;
            }

            JSONClass json = new JSONClass();
            json["source"] = "VaM";
            json["action"] = "getInfo";
            string postData = json.ToString();

            StartCoroutine(PostRequest(API_URL, postData, (node) => {
                HubInfo info = new HubInfo();
                
                // Categories (Type)
                info.Categories.Add("All");
                if (node["type"] != null)
                {
                    foreach (JSONNode n in node["type"].AsArray)
                    {
                        info.Categories.Add(n);
                    }
                }
                
                // PayTypes (Category)
                info.PayTypes.Add("All");
                if (node["category"] != null)
                {
                    foreach (JSONNode n in node["category"].AsArray)
                    {
                        info.PayTypes.Add(n);
                    }
                }

                // Creators (Users)
                info.Creators.Add("All");
                if (node["users"] != null)
                {
                    foreach (string key in node["users"].AsObject.Keys)
                    {
                        info.Creators.Add(key);
                    }
                    info.Creators.Sort();
                }

                // Tags
                if (node["tags"] != null)
                {
                    foreach (string key in node["tags"].AsObject.Keys)
                    {
                        info.Tags.Add(key);
                    }
                    info.Tags.Sort();
                }

                _cachedInfo = info;
                _infoCacheTime = Time.time;
                onSuccess?.Invoke(info);
            }, onError));
        }

        public void GetCategories(Action<List<string>> onSuccess, Action<string> onError)
        {
            GetInfo((info) => onSuccess?.Invoke(info.Categories), onError);
        }

        public void GetTags(Action<List<string>> onSuccess, Action<string> onError)
        {
            GetInfo((info) => onSuccess?.Invoke(info.Tags), onError);
        }

        public void GetCreators(Action<List<string>> onSuccess, Action<string> onError)
        {
            GetInfo((info) => onSuccess?.Invoke(info.Creators), onError);
        }

        public void GetPayTypes(Action<List<string>> onSuccess, Action<string> onError)
        {
            GetInfo((info) => onSuccess?.Invoke(info.PayTypes), onError);
        }

        public void GetResources(string category, string creator, string payType, string search, List<string> tags, int page, Action<List<GalleryHubItem>> onSuccess, Action<string> onError)
        {
            string tagsStr = tags != null && tags.Count > 0 ? string.Join(",", tags.ToArray()) : "";
            string cacheKey = $"{category}|{creator}|{payType}|{search}|{tagsStr}|{page}";
            if (_cache.ContainsKey(cacheKey) && Time.time - _cacheTime[cacheKey] < CACHE_DURATION)
            {
                onSuccess?.Invoke(_cache[cacheKey]);
                return;
            }

            JSONClass json = new JSONClass();
            json["source"] = "VaM";
            json["action"] = "getResources";
            json["latest_image"] = "Y";
            json["perpage"] = "50";
            json["page"] = page.ToString();

            if (!string.IsNullOrEmpty(category) && category != "All") json["type"] = category;
            if (!string.IsNullOrEmpty(creator) && creator != "All") json["username"] = creator;
            if (!string.IsNullOrEmpty(payType) && payType != "All") json["category"] = payType;
            if (!string.IsNullOrEmpty(search)) 
            {
                json["search"] = search;
                json["searchall"] = "true";
            }
            if (!string.IsNullOrEmpty(tagsStr))
            {
                json["tags"] = tagsStr;
            }

            StartCoroutine(PostRequest(API_URL, json.ToString(), (node) =>
            {
                List<GalleryHubItem> items = new List<GalleryHubItem>();
                if (node["resources"] != null)
                {
                    foreach (JSONNode n in node["resources"].AsArray)
                    {
                        items.Add(new GalleryHubItem(n));
                    }
                }
                
                _cache[cacheKey] = items;
                _cacheTime[cacheKey] = Time.time;
                onSuccess?.Invoke(items);
            }, onError));
        }

        private IEnumerator PostRequest(string uri, string postData, Action<JSONNode> callback, Action<string> errorCallback)
        {
            OnLog?.Invoke($"Hub Request: {postData}"); // Log request for debug
            using (UnityWebRequest webRequest = UnityWebRequest.Post(uri, postData))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(postData));
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Accept", "application/json");
                yield return webRequest.SendWebRequest();

                if (webRequest.isNetworkError || webRequest.isHttpError)
                {
                    string err = webRequest.error;
                    if (string.IsNullOrEmpty(err)) err = "Unknown Network Error";
                    OnLog?.Invoke($"Hub Error: {err}");
                    errorCallback?.Invoke(err);
                }
                else
                {
                    try
                    {
                        JSONNode node = JSON.Parse(webRequest.downloadHandler.text);
                        if (node == null) throw new Exception("Invalid JSON response");
                        
                        // Check for API level error
                        if (node["error"] != null)
                        {
                            throw new Exception(node["error"]);
                        }

                        callback?.Invoke(node);
                    }
                    catch (Exception e)
                    {
                        OnLog?.Invoke($"Hub Parse Error: {e.Message}");
                        errorCallback?.Invoke(e.Message);
                    }
                }
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
            _cacheTime.Clear();
            _cachedInfo = null;
            _infoCacheTime = 0f;
        }
    }
}
