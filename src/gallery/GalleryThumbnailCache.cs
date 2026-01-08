using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VPB
{
    public class GalleryThumbnailCache
    {
        private static GalleryThumbnailCache _instance;
        private static readonly object instanceLock = new object();

        public static GalleryThumbnailCache Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (instanceLock)
                    {
                        if (_instance == null) _instance = new GalleryThumbnailCache();
                    }
                }
                return _instance;
            }
        }

        private string cacheFilePath;
        private FileStream fileStream;
        private BinaryWriter writer;
        private BinaryReader reader;
        private readonly object lockObj = new object();

        private Dictionary<string, CacheEntry> index = new Dictionary<string, CacheEntry>();

        private struct CacheEntry
        {
            public long Offset;
            public int Length;
            public long LastWriteTime;
            public int Width;
            public int Height;
            public int Format;
        }

        public GalleryThumbnailCache()
        {
            string cacheDir = Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Cache"), "VPB");
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
            cacheFilePath = Path.Combine(cacheDir, "gallery_thumbnails.bin");
            Initialize();
            CleanCache();
        }

        private void Initialize()
        {
            lock (lockObj)
            {
                try
                {
                    // Use larger buffer (64KB) for better performance
                    fileStream = new FileStream(cacheFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 65536, FileOptions.RandomAccess);
                    writer = new BinaryWriter(fileStream);
                    reader = new BinaryReader(fileStream);

                    BuildIndex();
                }
                catch (Exception ex)
                {
                    Debug.LogError("GalleryThumbnailCache: Failed to initialize cache: " + ex.Message);
                    if (fileStream != null) fileStream.Dispose();
                    fileStream = null;
                }
            }
        }

        private int GetExpectedRawDataSize(int w, int h, int fmt)
        {
            TextureFormat format = (TextureFormat)fmt;
            switch (format)
            {
                case TextureFormat.Alpha8: return w * h;
                case TextureFormat.RGB24: return w * h * 3;
                case TextureFormat.RGBA32: return w * h * 4;
                case TextureFormat.ARGB32: return w * h * 4;
                case TextureFormat.DXT1: return (Mathf.Max(1, (w + 3) / 4) * Mathf.Max(1, (h + 3) / 4)) * 8;
                case TextureFormat.DXT5: return (Mathf.Max(1, (w + 3) / 4) * Mathf.Max(1, (h + 3) / 4)) * 16;
                default: return 0; 
            }
        }

        private void BuildIndex()
        {
            index.Clear();
            if (fileStream.Length == 0) return;

            fileStream.Position = 0;
            long lastValidPos = 0;
            try
            {
                // Simple append-only format:
                // [PathLength(4)][PathBytes(N)][LastWriteTime(8)][Width(4)][Height(4)][Format(4)][DataLen(4)][Data(M)]
                while (fileStream.Position < fileStream.Length)
                {
                    long entryStart = fileStream.Position;
                    
                    // Check if we have enough bytes for the header (at least 4 bytes for path length)
                    if (fileStream.Position + 4 > fileStream.Length) break;

                    int pathLen = reader.ReadInt32();
                    if (pathLen < 0 || fileStream.Position + pathLen > fileStream.Length) break; // Corrupt

                    byte[] pathBytes = reader.ReadBytes(pathLen);
                    if (pathBytes.Length != pathLen) break; // Unexpected EOF
                    
                    string path = Encoding.UTF8.GetString(pathBytes);

                    // Check remaining header size (8+4+4+4+4 = 24 bytes)
                    if (fileStream.Position + 24 > fileStream.Length) break;

                    long lastWriteTime = reader.ReadInt64();
                    int width = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    int format = reader.ReadInt32();
                    int dataLen = reader.ReadInt32();

                    if (dataLen < 0 || fileStream.Position + dataLen > fileStream.Length) break; // Corrupt

                    long dataOffset = fileStream.Position;
                    
                    // Skip data
                    fileStream.Seek(dataLen, SeekOrigin.Current);
                    
                    // Mark as valid up to this point
                    lastValidPos = fileStream.Position;

                    // Validate dataLen against expected size for known formats
                    int expected = GetExpectedRawDataSize(width, height, format);
                    if (expected > 0 && dataLen < expected)
                    {
                        // Corrupt entry or format mismatch, ignore it
                        continue;
                    }

                    // Add/Update index
                    CacheEntry entry = new CacheEntry
                    {
                        Offset = dataOffset,
                        Length = dataLen,
                        LastWriteTime = lastWriteTime,
                        Width = width,
                        Height = height,
                        Format = format
                    };
                    
                    if (index.ContainsKey(path))
                    {
                        index[path] = entry;
                    }
                    else
                    {
                        index.Add(path, entry);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("GalleryThumbnailCache: Error building index: " + ex.Message);
            }
            
            // Truncate any corrupt tail
            if (lastValidPos < fileStream.Length)
            {
                try
                {
                    Debug.LogWarning("GalleryThumbnailCache: Truncating corrupt cache file from " + fileStream.Length + " to " + lastValidPos);
                    fileStream.SetLength(lastValidPos);
                }
                catch(Exception ex)
                {
                    Debug.LogError("GalleryThumbnailCache: Failed to truncate cache file: " + ex.Message);
                }
            }
        }

        public string GetCacheKey(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Normalize path separators
            string normalizedPath = path.Replace('\\', '/');

            // Handle .var package paths: AddonPackages/Author.Name.Version.var:/Internal/Path
            // We want to normalize "AddonPackages/Author.Name.Version.var" and "AllPackages/Author.Name.Version.var"
            // to a location-independent key like "VAR:/Author.Name.Version:/Internal/Path"
            
            if (normalizedPath.Contains(":/"))
            {
                int colonIndex = normalizedPath.IndexOf(":/");
                string pkgPath = normalizedPath.Substring(0, colonIndex);
                string internalPath = normalizedPath.Substring(colonIndex + 2);

                if (pkgPath.EndsWith(".var", StringComparison.OrdinalIgnoreCase) || pkgPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string pkgName = Path.GetFileName(pkgPath);
                    return "VAR:/" + pkgName + ":/" + internalPath;
                }
            }
            // Also handle the package file itself being the target (e.g. for scene gallery)
            else if (normalizedPath.EndsWith(".var", StringComparison.OrdinalIgnoreCase) || normalizedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (normalizedPath.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase) || 
                    normalizedPath.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
                {
                    return "VAR:/" + Path.GetFileName(normalizedPath);
                }
            }

            return normalizedPath;
        }

        public bool TryGetThumbnail(string path, long fileLastWriteTime, out byte[] data, out int width, out int height, out TextureFormat format)
        {
            string key = GetCacheKey(path);
            data = null;
            width = 0;
            height = 0;
            format = TextureFormat.RGBA32;

            if (fileStream == null) return false;

            lock (lockObj)
            {
                if (index.TryGetValue(key, out CacheEntry entry))
                {
                    if (entry.LastWriteTime == fileLastWriteTime)
                    {
                        try
                        {
                            fileStream.Position = entry.Offset;
                            if (entry.Width <= 0 || entry.Height <= 0)
                            {
                                return false;
                            }
                            data = ByteArrayPool.Rent(entry.Length);
                            int bytesRead = fileStream.Read(data, 0, entry.Length);
                            if (bytesRead == entry.Length)
                            {
                                width = entry.Width;
                                height = entry.Height;
                                format = (TextureFormat)entry.Format;
                                return true;
                            }
                            else
                            {
                                ByteArrayPool.Return(data);
                                data = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError("GalleryThumbnailCache: Error reading thumbnail: " + ex.Message);
                            if (data != null)
                            {
                                ByteArrayPool.Return(data);
                                data = null;
                            }
                        }
                    }
                }
            }
            return false;
        }

        public void SaveThumbnail(string path, byte[] data, int dataLength, int width, int height, TextureFormat format, long lastWriteTime)
        {
            if (fileStream == null || width <= 0 || height <= 0) return;
            
            // Validate dataLength
            int expected = GetExpectedRawDataSize(width, height, (int)format);
            if (expected > 0 && dataLength < expected)
            {
                Debug.LogWarning($"GalleryThumbnailCache: Refusing to save thumbnail with insufficient data. {path} {width}x{height} {format} Expected {expected}, got {dataLength}");
                return;
            }

            string key = GetCacheKey(path);

            lock (lockObj)
            {
                try
                {
                    fileStream.Seek(0, SeekOrigin.End);
                    long entryStart = fileStream.Position;

                    byte[] pathBytes = Encoding.UTF8.GetBytes(key);
                    writer.Write(pathBytes.Length);
                    writer.Write(pathBytes);
                    writer.Write(lastWriteTime);
                    writer.Write(width);
                    writer.Write(height);
                    writer.Write((int)format);
                    writer.Write(dataLength);
                    
                    long dataOffset = fileStream.Position;
                    writer.Write(data, 0, dataLength);
                    writer.Flush(); // Ensure written

                    CacheEntry entry = new CacheEntry
                    {
                        Offset = dataOffset,
                        Length = dataLength,
                        LastWriteTime = lastWriteTime,
                        Width = width,
                        Height = height,
                        Format = (int)format
                    };

                    if (index.ContainsKey(key))
                    {
                        index[key] = entry;
                    }
                    else
                    {
                        index.Add(key, entry);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("GalleryThumbnailCache: Error saving thumbnail: " + ex.Message);
                }
            }
        }

        public void CleanCache()
        {
            if (fileStream == null) return;

            // Wait for FileManager to be ready or skip if no packages found yet to avoid aggressive deletion
            if (FileManager.PackagesByUid == null || FileManager.PackagesByUid.Count == 0)
            {
                Debug.LogWarning("GalleryThumbnailCache: Skipping CleanCache because FileManager packages are not loaded yet.");
                return;
            }

            lock (lockObj)
            {
                try
                {
                    List<string> keysToRemove = new List<string>();
                    foreach (var kvp in index)
                    {
                        string key = kvp.Key;
                        bool keep = false;

                        // 1. Handle Package Files (VAR:/...)
                        if (key.StartsWith("VAR:/"))
                        {
                            int secondSlash = key.IndexOf(":/", 5);
                            string pkgNameWithExt = "";
                            if (secondSlash > 5)
                            {
                                pkgNameWithExt = key.Substring(5, secondSlash - 5);
                            }
                            else if (key.EndsWith(".var") || key.EndsWith(".zip"))
                            {
                                pkgNameWithExt = key.Substring(5);
                            }

                            if (!string.IsNullOrEmpty(pkgNameWithExt))
                            {
                                // Strip extension for UID check (e.g. "Author.Name.1.var" -> "Author.Name.1")
                                string uid = pkgNameWithExt;
                                if (uid.EndsWith(".var", StringComparison.OrdinalIgnoreCase)) uid = uid.Substring(0, uid.Length - 4);
                                else if (uid.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) uid = uid.Substring(0, uid.Length - 4);

                                // Check if package is registered
                                if (FileManager.PackagesByUid.ContainsKey(uid))
                                {
                                    keep = true;
                                }
                                // Fallback: Check if the file exists on disk even if not registered
                                else if (FileManager.FileExists("AddonPackages/" + pkgNameWithExt) || 
                                         FileManager.FileExists("AllPackages/" + pkgNameWithExt))
                                {
                                    keep = true;
                                }
                            }
                        }
                        // 2. Handle SELF references (internal paths within packages)
                        else if (key.IndexOf("SELF:", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                 key.IndexOf("SELF/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 key.IndexOf("%SELF%", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // We preserve these as they are likely valid internal references that might 
                            // not be easily verifiable without full context.
                            keep = true; 
                        }
                        // 3. Handle Loose Files and protected folders
                        else
                        {
                            if (FileManager.FileExists(key))
                            {
                                keep = true;
                            }
                            else
                            {
                                // Check if it's in a protected/active folder and verify with physical disk check
                                if (key.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase) ||
                                    key.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                    key.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase) ||
                                    key.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Robust check: use physical File.Exists to ensure we don't delete thumbs
                                    // for files that exist but might not be in the VPB index yet.
                                    try {
                                        if (File.Exists(key)) keep = true;
                                    } catch {}
                                }
                            }
                        }

                        if (!keep)
                        {
                            keysToRemove.Add(key);
                        }
                    }

                    if (keysToRemove.Count == 0) return;

                    Debug.Log($"GalleryThumbnailCache: Cleaning {keysToRemove.Count} orphaned entries...");

                    foreach (var key in keysToRemove)
                    {
                        index.Remove(key);
                    }

                    // Rewrite the file to actually free space
                    string tempPath = cacheFilePath + ".tmp";
                    using (FileStream tempFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (BinaryWriter tempWriter = new BinaryWriter(tempFs))
                    {
                        // We need to read from the old stream while writing to the new one
                        // But since entries might be in any order in the file, we'll use the index to write them
                        Dictionary<string, CacheEntry> newIndex = new Dictionary<string, CacheEntry>();

                        foreach (var kvp in index)
                        {
                            string key = kvp.Key;
                            CacheEntry entry = kvp.Value;

                            fileStream.Position = entry.Offset;
                            byte[] data = new byte[entry.Length];
                            fileStream.Read(data, 0, entry.Length);

                            long entryStart = tempFs.Position;

                            byte[] pathBytes = Encoding.UTF8.GetBytes(key);
                            tempWriter.Write(pathBytes.Length);
                            tempWriter.Write(pathBytes);
                            tempWriter.Write(entry.LastWriteTime);
                            tempWriter.Write(entry.Width);
                            tempWriter.Write(entry.Height);
                            tempWriter.Write(entry.Format);
                            tempWriter.Write(entry.Length);
                            
                            long dataOffset = tempFs.Position;
                            tempWriter.Write(data, 0, entry.Length);

                            newIndex[key] = new CacheEntry
                            {
                                Offset = dataOffset,
                                Length = entry.Length,
                                LastWriteTime = entry.LastWriteTime,
                                Width = entry.Width,
                                Height = entry.Height,
                                Format = entry.Format
                            };
                        }
                        tempWriter.Flush();
                        
                        // Replace index
                        index = newIndex;
                    }

                    // Swap files
                    Close();
                    File.Delete(cacheFilePath);
                    File.Move(tempPath, cacheFilePath);
                    Initialize();

                    Debug.Log("GalleryThumbnailCache: Cache cleaning complete.");
                }
                catch (Exception ex)
                {
                    Debug.LogError("GalleryThumbnailCache: Error during CleanCache: " + ex.Message);
                }
            }
        }

        public void Close()
        {
            lock (lockObj)
            {
                if (writer != null) writer.Close();
                if (reader != null) reader.Close();
                if (fileStream != null) fileStream.Dispose();
                fileStream = null;
            }
        }
    }
}
