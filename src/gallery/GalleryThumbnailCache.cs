using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace VPB
{
    public class GalleryThumbnailCache
    {
        private const int CACHE_VERSION = 2;
        private const string CACHE_MAGIC = "VPBCACHE";
        private const int CACHE_HEADER_SIZE = 20;

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
        private ReaderWriterLockSlim cacheLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private int cacheFormatVersion = 0;

        private Dictionary<string, CacheEntry> index = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private struct CacheEntry
        {
            public long Offset;
            public int Length;
            public long LastWriteTime;
            public int Width;
            public int Height;
            public int Format;
            public uint DataCRC32;
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

        private uint CalculateCRC32(byte[] data, int offset, int length)
        {
            if (data == null || offset < 0 || length < 0 || offset + length > data.Length) return 0;
            
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
            {
                crc = (crc >> 8) ^ CRC32_TABLE[(crc ^ data[i]) & 0xFF];
            }
            return crc ^ 0xFFFFFFFF;
        }

        private static readonly uint[] CRC32_TABLE = InitCRC32Table();

        private static uint[] InitCRC32Table()
        {
            uint[] table = new uint[256];
            const uint poly = 0xEDB88320;
            for (int i = 0; i < 256; i++)
            {
                uint crc = (uint)i;
                for (int j = 0; j < 8; j++)
                {
                    crc = (crc & 1) == 1 ? (crc >> 1) ^ poly : (crc >> 1);
                }
                table[i] = crc;
            }
            return table;
        }

        private void Initialize()
        {
            cacheLock.EnterWriteLock();
            try
            {
                InitializeInternal();
            }
            finally
            {
                cacheLock.ExitWriteLock();
            }
        }

        private void InitializeInternal()
        {
            try
            {
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
            cacheFormatVersion = 0;
            if (fileStream.Length == 0) return;

            fileStream.Position = 0;
            
            if (fileStream.Length >= CACHE_HEADER_SIZE)
            {
                byte[] magicBytes = reader.ReadBytes(8);
                string magic = Encoding.ASCII.GetString(magicBytes);
                
                if (magic == CACHE_MAGIC)
                {
                    cacheFormatVersion = reader.ReadInt32();
                    fileStream.ReadByte();
                    fileStream.ReadByte();
                    fileStream.ReadByte();
                    fileStream.ReadByte();
                    fileStream.ReadByte();
                    fileStream.ReadByte();
                    fileStream.ReadByte();
                    fileStream.ReadByte();
                    
                    if (cacheFormatVersion == CACHE_VERSION)
                    {
                        BuildIndexV2();
                        return;
                    }
                    else if (cacheFormatVersion > CACHE_VERSION)
                    {
                        Debug.LogError("GalleryThumbnailCache: Cache version " + cacheFormatVersion + " is newer than supported " + CACHE_VERSION);
                        return;
                    }
                }
            }
            
            cacheFormatVersion = 1;
            BuildIndexV1();
            
            try
            {
                MigrateV1ToV2();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("GalleryThumbnailCache: Failed to migrate cache to V2: " + ex.Message);
            }
        }

        private void BuildIndexV1()
        {
            index.Clear();
            fileStream.Position = 0;
            long lastValidPos = 0;
            
            try
            {
                while (fileStream.Position < fileStream.Length)
                {
                    if (fileStream.Position + 4 > fileStream.Length) break;

                    int pathLen = reader.ReadInt32();
                    if (pathLen < 0 || fileStream.Position + pathLen > fileStream.Length) break;

                    byte[] pathBytes = reader.ReadBytes(pathLen);
                    if (pathBytes.Length != pathLen) break;
                    
                    string path = Encoding.UTF8.GetString(pathBytes);

                    if (fileStream.Position + 24 > fileStream.Length) break;

                    long lastWriteTime = reader.ReadInt64();
                    int width = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    int format = reader.ReadInt32();
                    int dataLen = reader.ReadInt32();

                    if (dataLen < 0 || fileStream.Position + dataLen > fileStream.Length) break;

                    long dataOffset = fileStream.Position;
                    fileStream.Seek(dataLen, SeekOrigin.Current);
                    lastValidPos = fileStream.Position;

                    int expected = GetExpectedRawDataSize(width, height, format);
                    if (expected > 0 && dataLen < expected) continue;

                    CacheEntry entry = new CacheEntry
                    {
                        Offset = dataOffset,
                        Length = dataLen,
                        LastWriteTime = lastWriteTime,
                        Width = width,
                        Height = height,
                        Format = format,
                        DataCRC32 = 0
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
                Debug.LogError("GalleryThumbnailCache: Error building V1 index: " + ex.Message);
            }
            
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

        private void BuildIndexV2()
        {
            index.Clear();
            long lastValidPos = CACHE_HEADER_SIZE;
            
            try
            {
                while (fileStream.Position < fileStream.Length)
                {
                    if (fileStream.Position + 4 > fileStream.Length) break;

                    int pathLen = reader.ReadInt32();
                    if (pathLen < 0 || fileStream.Position + pathLen > fileStream.Length) break;

                    byte[] pathBytes = reader.ReadBytes(pathLen);
                    if (pathBytes.Length != pathLen) break;
                    
                    string path = Encoding.UTF8.GetString(pathBytes);

                    if (fileStream.Position + 30 > fileStream.Length) break;

                    long lastWriteTime = reader.ReadInt64();
                    int width = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    int format = reader.ReadInt32();
                    int dataLen = reader.ReadInt32();
                    uint crc32 = reader.ReadUInt32();
                    ushort entryVersion = reader.ReadUInt16();

                    if (dataLen < 0 || fileStream.Position + dataLen > fileStream.Length) break;

                    long dataOffset = fileStream.Position;
                    fileStream.Seek(dataLen, SeekOrigin.Current);
                    lastValidPos = fileStream.Position;

                    int expected = GetExpectedRawDataSize(width, height, format);
                    if (expected > 0 && dataLen < expected) continue;

                    CacheEntry entry = new CacheEntry
                    {
                        Offset = dataOffset,
                        Length = dataLen,
                        LastWriteTime = lastWriteTime,
                        Width = width,
                        Height = height,
                        Format = format,
                        DataCRC32 = crc32
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
                Debug.LogError("GalleryThumbnailCache: Error building V2 index: " + ex.Message);
            }
            
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

        private void MigrateV1ToV2()
        {
            if (cacheFormatVersion != 1 || index.Count == 0) return;

            Debug.Log("GalleryThumbnailCache: Migrating cache from V1 to V2...");

            string tempPath = cacheFilePath + ".tmp";
            using (FileStream tempFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
            using (BinaryWriter tempWriter = new BinaryWriter(tempFs))
            {
                byte[] magicBytes = Encoding.ASCII.GetBytes(CACHE_MAGIC);
                tempWriter.Write(magicBytes);
                tempWriter.Write(CACHE_VERSION);
                tempWriter.Write(new byte[8]);

                Dictionary<string, CacheEntry> newIndex = new Dictionary<string, CacheEntry>();

                foreach (var kvp in index)
                {
                    string key = kvp.Key;
                    CacheEntry entry = kvp.Value;

                    try
                    {
                        fileStream.Position = entry.Offset;
                        byte[] data = new byte[entry.Length];
                        fileStream.Read(data, 0, entry.Length);

                        uint crc32 = CalculateCRC32(data, 0, entry.Length);

                        byte[] pathBytes = Encoding.UTF8.GetBytes(key);
                        tempWriter.Write(pathBytes.Length);
                        tempWriter.Write(pathBytes);
                        tempWriter.Write(entry.LastWriteTime);
                        tempWriter.Write(entry.Width);
                        tempWriter.Write(entry.Height);
                        tempWriter.Write(entry.Format);
                        tempWriter.Write(entry.Length);
                        tempWriter.Write(crc32);
                        
                        long dataOffset = tempFs.Position;
                        tempWriter.Write(data, 0, entry.Length);

                        newIndex[key] = new CacheEntry
                        {
                            Offset = dataOffset,
                            Length = entry.Length,
                            LastWriteTime = entry.LastWriteTime,
                            Width = entry.Width,
                            Height = entry.Height,
                            Format = entry.Format,
                            DataCRC32 = crc32
                        };
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("GalleryThumbnailCache: Failed to migrate entry " + key + ": " + ex.Message);
                    }
                }

                tempWriter.Flush();
                index = newIndex;
                cacheFormatVersion = CACHE_VERSION;
            }

            CloseInternal();
            File.Delete(cacheFilePath);
            File.Move(tempPath, cacheFilePath);
            InitializeInternal();

            Debug.Log("GalleryThumbnailCache: Migration to V2 complete.");
        }

        public bool IsPackagePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.Contains(":/") || path.EndsWith(".var", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
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
            if (IsPackagePath(path)) fileLastWriteTime = 0;
            string key = GetCacheKey(path);
            data = null;
            width = 0;
            height = 0;
            format = TextureFormat.RGBA32;

            if (fileStream == null) return false;

            cacheLock.EnterReadLock();
            try
            {
                if (index.TryGetValue(key, out CacheEntry entry))
                {
                    if (entry.LastWriteTime == fileLastWriteTime)
                    {
                        try
                        {
                            if (entry.Width <= 0 || entry.Height <= 0)
                            {
                                return false;
                            }
                            
                            fileStream.Position = entry.Offset;
                            data = ByteArrayPool.Rent(entry.Length);
                            int bytesRead = fileStream.Read(data, 0, entry.Length);
                            
                            if (bytesRead != entry.Length)
                            {
                                ByteArrayPool.Return(data);
                                data = null;
                                Debug.LogError("GalleryThumbnailCache: Incomplete read for " + key);
                                return false;
                            }

                            if (entry.DataCRC32 != 0)
                            {
                                uint calculatedCrc = CalculateCRC32(data, 0, entry.Length);
                                if (calculatedCrc != entry.DataCRC32)
                                {
                                    ByteArrayPool.Return(data);
                                    data = null;
                                    Debug.LogError("GalleryThumbnailCache: CRC mismatch for " + key + " (expected " + entry.DataCRC32 + ", got " + calculatedCrc + ")");
                                    return false;
                                }
                            }
                            
                            width = entry.Width;
                            height = entry.Height;
                            format = (TextureFormat)entry.Format;
                            return true;
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
            finally
            {
                cacheLock.ExitReadLock();
            }
            return false;
        }

        public void SaveThumbnail(string path, byte[] data, int dataLength, int width, int height, TextureFormat format, long lastWriteTime)
        {
            if (IsPackagePath(path)) lastWriteTime = 0;
            if (fileStream == null || width <= 0 || height <= 0) return;
            
            int expected = GetExpectedRawDataSize(width, height, (int)format);
            if (expected > 0 && dataLength < expected)
            {
                Debug.LogWarning($"GalleryThumbnailCache: Refusing to save thumbnail with insufficient data. {path} {width}x{height} {format} Expected {expected}, got {dataLength}");
                return;
            }

            string key = GetCacheKey(path);

            cacheLock.EnterWriteLock();
            try
            {
                try
                {
                    if (cacheFormatVersion == 0)
                    {
                        cacheFormatVersion = CACHE_VERSION;
                        fileStream.Seek(0, SeekOrigin.Begin);
                        byte[] magicBytes = Encoding.ASCII.GetBytes(CACHE_MAGIC);
                        writer.Write(magicBytes);
                        writer.Write(CACHE_VERSION);
                        writer.Write(new byte[8]);
                        writer.Flush();
                    }

                    fileStream.Seek(0, SeekOrigin.End);
                    
                    byte[] pathBytes = Encoding.UTF8.GetBytes(key);
                    uint crc32 = CalculateCRC32(data, 0, dataLength);
                    
                    writer.Write(pathBytes.Length);
                    writer.Write(pathBytes);
                    writer.Write(lastWriteTime);
                    writer.Write(width);
                    writer.Write(height);
                    writer.Write((int)format);
                    writer.Write(dataLength);
                    writer.Write(crc32);
                    writer.Write((ushort)0);
                    
                    long dataOffset = fileStream.Position;
                    writer.Write(data, 0, dataLength);
                    writer.Flush();

                    CacheEntry entry = new CacheEntry
                    {
                        Offset = dataOffset,
                        Length = dataLength,
                        LastWriteTime = lastWriteTime,
                        Width = width,
                        Height = height,
                        Format = (int)format,
                        DataCRC32 = crc32
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
            finally
            {
                cacheLock.ExitWriteLock();
            }
        }

        public System.Collections.IEnumerator GenerateAndSaveThumbnailRoutine(string path, Texture2D sourceTex, long lastWriteTime)
        {
            yield return null;

            if (sourceTex == null) yield break;

            int maxDim = 256;
            byte[] bytes = null;
            int w = sourceTex.width;
            int h = sourceTex.height;

            TextureFormat format = sourceTex.format;

            if (w <= maxDim && h <= maxDim)
            {
                bytes = sourceTex.GetRawTextureData();
            }
            else
            {
                float aspect = (float)w / h;
                if (w > h) { w = maxDim; h = Mathf.RoundToInt(maxDim / aspect); }
                else { h = maxDim; w = Mathf.RoundToInt(maxDim * aspect); }
                yield return null;

                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default);
                Graphics.Blit(sourceTex, rt);
                yield return null;
                
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                
                format = TextureFormat.RGB24;
                Texture2D newTex = new Texture2D(w, h, format, false);
                newTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                newTex.Apply();
                yield return null;
                
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                
                bytes = newTex.GetRawTextureData();
                UnityEngine.Object.Destroy(newTex);
            }

            if (bytes != null)
            {
                SaveThumbnail(path, bytes, bytes.Length, w, h, format, lastWriteTime);
            }
        }

        public void CleanCache()
        {
            if (fileStream == null) return;

            if (FileManager.PackagesByUid == null || FileManager.PackagesByUid.Count == 0)
            {
                Debug.LogWarning("GalleryThumbnailCache: Skipping CleanCache because FileManager packages are not loaded yet.");
                return;
            }

            cacheLock.EnterWriteLock();
            try
            {
                try
                {
                    List<string> keysToRemove = new List<string>();
                    foreach (var kvp in index)
                    {
                        string key = kvp.Key;
                        bool keep = false;

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
                                string uid = pkgNameWithExt;
                                if (uid.EndsWith(".var", StringComparison.OrdinalIgnoreCase)) uid = uid.Substring(0, uid.Length - 4);
                                else if (uid.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) uid = uid.Substring(0, uid.Length - 4);

                                if (FileManager.PackagesByUid.ContainsKey(uid))
                                {
                                    keep = true;
                                }
                                else if (FileManager.FileExists("AddonPackages/" + pkgNameWithExt) || 
                                         FileManager.FileExists("AllPackages/" + pkgNameWithExt))
                                {
                                    keep = true;
                                }
                            }
                        }
                        else if (key.IndexOf("SELF:", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                 key.IndexOf("SELF/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 key.IndexOf("%SELF%", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            keep = true; 
                        }
                        else
                        {
                            if (FileManager.FileExists(key))
                            {
                                keep = true;
                            }
                            else
                            {
                                if (key.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase) ||
                                    key.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                    key.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase) ||
                                    key.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
                                {
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

                    string tempPath = cacheFilePath + ".tmp";
                    using (FileStream tempFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                    using (BinaryWriter tempWriter = new BinaryWriter(tempFs))
                    {
                        byte[] magicBytes = Encoding.ASCII.GetBytes(CACHE_MAGIC);
                        tempWriter.Write(magicBytes);
                        tempWriter.Write(CACHE_VERSION);
                        tempWriter.Write(new byte[8]);

                        Dictionary<string, CacheEntry> newIndex = new Dictionary<string, CacheEntry>();

                        foreach (var kvp in index)
                        {
                            string key = kvp.Key;
                            CacheEntry entry = kvp.Value;

                            try
                            {
                                fileStream.Position = entry.Offset;
                                byte[] data = new byte[entry.Length];
                                int bytesRead = fileStream.Read(data, 0, entry.Length);
                                if (bytesRead != entry.Length) continue;

                                uint crc32 = CalculateCRC32(data, 0, entry.Length);

                                byte[] pathBytes = Encoding.UTF8.GetBytes(key);
                                tempWriter.Write(pathBytes.Length);
                                tempWriter.Write(pathBytes);
                                tempWriter.Write(entry.LastWriteTime);
                                tempWriter.Write(entry.Width);
                                tempWriter.Write(entry.Height);
                                tempWriter.Write(entry.Format);
                                tempWriter.Write(entry.Length);
                                tempWriter.Write(crc32);
                                tempWriter.Write((ushort)0);
                                
                                long dataOffset = tempFs.Position;
                                tempWriter.Write(data, 0, entry.Length);

                                newIndex[key] = new CacheEntry
                                {
                                    Offset = dataOffset,
                                    Length = entry.Length,
                                    LastWriteTime = entry.LastWriteTime,
                                    Width = entry.Width,
                                    Height = entry.Height,
                                    Format = entry.Format,
                                    DataCRC32 = crc32
                                };
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning("GalleryThumbnailCache: Failed to clean entry " + key + ": " + ex.Message);
                            }
                        }
                        tempWriter.Flush();
                        index = newIndex;
                    }

                    CloseInternal();
                    File.Delete(cacheFilePath);
                    File.Move(tempPath, cacheFilePath);
                    InitializeInternal();

                    Debug.Log("GalleryThumbnailCache: Cache cleaning complete.");
                }
                catch (Exception ex)
                {
                    Debug.LogError("GalleryThumbnailCache: Error during CleanCache: " + ex.Message);
                }
            }
            finally
            {
                cacheLock.ExitWriteLock();
            }
        }

        public void Close()
        {
            cacheLock.EnterWriteLock();
            try
            {
                CloseInternal();
            }
            finally
            {
                cacheLock.ExitWriteLock();
            }
        }

        private void CloseInternal()
        {
            if (writer != null) writer.Close();
            if (reader != null) reader.Close();
            if (fileStream != null) fileStream.Dispose();
            fileStream = null;
        }

        ~GalleryThumbnailCache()
        {
            if (cacheLock != null)
            {
                try { cacheLock.Dispose(); } catch { }
            }
        }
    }
}
