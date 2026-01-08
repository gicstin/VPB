using System;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace VPB.Hub
{
    public class GalleryHubItem
    {
        public string ResourceId { get; private set; }
        public string Title { get; private set; }
        public string Creator { get; private set; }
        public string ThumbnailUrl { get; private set; }
        public string Category { get; private set; }
        public string PayType { get; private set; }
        public float Rating { get; private set; }
        public int DownloadCount { get; private set; }
        public bool IsOwned { get; private set; }
        public bool IsInstalled { get; private set; }

        public GalleryHubItem(JSONNode node)
        {
            ResourceId = node["resource_id"];
            Title = node["title"];
            Creator = node["username"];
            ThumbnailUrl = node["image_url"];
            Category = node["type"];
            PayType = node["category"];
            Rating = node["rating_avg"].AsFloat;
            DownloadCount = node["download_count"].AsInt;
            
            // Logic to check if owned/installed would go here, possibly querying PackageManager
        }
    }
}
