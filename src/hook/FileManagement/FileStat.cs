using System;
using System.IO;

namespace VPB
{
    internal static class FileStat
    {
        public static bool TryGetFileStat(string path, out DateTime creationTime, out DateTime lastWriteTime, out long size)
        {
            creationTime = DateTime.MinValue;
            lastWriteTime = DateTime.MinValue;
            size = 0;

            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                FileInfo fi = new FileInfo(path.Replace('/', '\\'));
                if (!fi.Exists) return false;
                creationTime = fi.CreationTime;
                lastWriteTime = fi.LastWriteTime;
                size = fi.Length;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static DateTime GetLastWriteTimeOrMin(string path)
        {
            DateTime creation, lastWrite;
            long size;
            return TryGetFileStat(path, out creation, out lastWrite, out size) ? lastWrite : DateTime.MinValue;
        }

        public static DateTime GetCreationTimeOrMin(string path)
        {
            DateTime creation, lastWrite;
            long size;
            return TryGetFileStat(path, out creation, out lastWrite, out size) ? creation : DateTime.MinValue;
        }

        public static long GetSizeOrZero(string path)
        {
            DateTime creation, lastWrite;
            long size;
            return TryGetFileStat(path, out creation, out lastWrite, out size) ? size : 0;
        }
    }
}
