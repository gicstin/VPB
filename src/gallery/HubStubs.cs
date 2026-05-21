using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB.Hub
{
    // Stub types retained only so legacy references compile.
    // The old in-gallery Hub implementation was removed from the project.
    public class GalleryHubItem
    {
        public string ResourceId { get; set; }
        public string Title { get; set; }
        public string Creator { get; set; }
        public string ThumbnailUrl { get; set; }
    }

    public class GalleryHubController : MonoBehaviour
    {
        private static GalleryHubController _instance;
        public static GalleryHubController Instance
        {
            get
            {
                if (_instance == null)
                {
                    // No-op placeholder: the removed feature should not be invoked.
                    var go = new GameObject("GalleryHubController_Stub");
                    _instance = go.AddComponent<GalleryHubController>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        public void GetInfo(Action<object> onSuccess, Action<string> onError) => onError?.Invoke("Gallery Hub removed");
        public void GetTags(Action<List<string>> onSuccess, Action<string> onError) => onError?.Invoke("Gallery Hub removed");
        public void GetResources(string category, string creator, string payType, string search, List<string> tags, int page,
            Action<List<GalleryHubItem>> onSuccess, Action<string> onError) => onError?.Invoke("Gallery Hub removed");
    }
}
