using System.IO;

namespace VPB
{
    internal static class VpbAtomicTextFile
    {
        internal const long MinValidBytes = 2;

        internal static bool TryWriteWithBackup(string path, string text)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text)) return false;

            string tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, text);
            if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length < MinValidBytes) return false;

            string backupPath = path + ".bak";
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > MinValidBytes)
                {
                    try
                    {
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                        File.Move(path, backupPath);
                    }
                    catch
                    {
                        if (File.Exists(path)) File.Delete(path);
                    }
                }
                else
                {
                    File.Delete(path);
                }
            }

            File.Move(tmpPath, path);
            return true;
        }

        internal static bool TryReadSharedText(string path, out string text)
        {
            text = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                text = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(text) || text.Trim().Length < MinValidBytes)
            {
                text = null;
                return false;
            }
            return true;
        }
    }
}
