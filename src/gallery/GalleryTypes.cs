using System;

namespace VPB
{
    public enum ContentType { Category, Creator, Status, License, Tags, Hub, HubTags, HubPayTypes, HubCreators, Ratings, Size }
    public enum ApplyMode { SingleClick, DoubleClick }
    public enum TabSide { Hidden, Left, Right }
    public enum GalleryLayoutMode { Grid, VerticalCard }
    
    public struct CreatorCacheEntry 
    { 
        public string Name; 
        public int Count; 
    }
}
