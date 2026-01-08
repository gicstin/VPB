using System;

namespace VPB
{
    public enum ContentType { Category, Creator, Status, License, Tags, Hub, HubTags, HubPayTypes, HubCreators }
    public enum TabSide { Hidden, Left, Right }
    
    public struct CreatorCacheEntry 
    { 
        public string Name; 
        public int Count; 
    }
}
