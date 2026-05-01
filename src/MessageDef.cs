using System;

namespace VPB
{
    class MessageDef
    {
        public static int UpdateLoading = 1;
        public static int DeactivateWorldUI = 2;
        public static int FileManagerRefresh = 3;
        public static int FileManagerInit = 4;
        /// <summary>Posted after gallery item usage is recorded in SQLite so History can refresh live.</summary>
        public static int GalleryItemUsageRecorded = 5;
    }
}
