using System;
using System.IO;
using MVR.FileManagementSecure;

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
                string p = path.Replace('/', '\\');

                bool exists = false;
                try
                {
                    exists = FileManagerSecure.FileExists(p);
                }
                catch
                {
                    exists = File.Exists(p);
                }

                if (!exists) return false;

                try
                {
                    creationTime = FileManagerSecure.FileCreationTime(p);
                    lastWriteTime = FileManagerSecure.FileLastWriteTime(p);
                }
                catch
                {
                    FileInfo fi = new FileInfo(p);
                    creationTime = fi.CreationTime;
                    lastWriteTime = fi.LastWriteTime;
                }

                try
                {
                    FileInfo fi = new FileInfo(p);
                    size = fi.Length;
                }
                catch
                {
                    size = 0;
                }

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
