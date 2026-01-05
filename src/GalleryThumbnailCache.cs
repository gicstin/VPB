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

        public bool TryGetThumbnail(string path, long fileLastWriteTime, out byte[] data, out int width, out int height, out TextureFormat format)
        {
            data = null;
            width = 0;
            height = 0;
            format = TextureFormat.RGBA32;

            if (fileStream == null) return false;

            lock (lockObj)
            {
                if (index.TryGetValue(path, out CacheEntry entry))
                {
                    if (entry.LastWriteTime == fileLastWriteTime)
                    {
                        try
                        {
                            fileStream.Position = entry.Offset;
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
            if (fileStream == null) return;

            lock (lockObj)
            {
                try
                {
                    fileStream.Seek(0, SeekOrigin.End);
                    long entryStart = fileStream.Position;

                    byte[] pathBytes = Encoding.UTF8.GetBytes(path);
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

                    if (index.ContainsKey(path))
                    {
                        index[path] = entry;
                    }
                    else
                    {
                        index.Add(path, entry);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("GalleryThumbnailCache: Error saving thumbnail: " + ex.Message);
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
