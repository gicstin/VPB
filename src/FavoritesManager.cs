using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VPB
{
    public class FavoritesManager
    {
        private static FavoritesManager _instance;
        public static FavoritesManager Instance
        {
            get
            {
                if (_instance == null) _instance = new FavoritesManager();
                return _instance;
            }
        }

        private string cacheFilePath;
        private HashSet<string> favoriteUids = new HashSet<string>();
        private readonly object lockObj = new object();

        public FavoritesManager()
        {
            try
            {
                string baseDir = Directory.GetCurrentDirectory();
                string saveDir = Path.Combine(baseDir, "Saves");
                saveDir = Path.Combine(saveDir, "PluginData");
                saveDir = Path.Combine(saveDir, "VPB");

                if (!Directory.Exists(saveDir))
                {
                    Directory.CreateDirectory(saveDir);
                }
                cacheFilePath = Path.Combine(saveDir, "favorites.bin");

                LoadCache();
                CompactCache();
            }
            catch (Exception ex)
            {
                Debug.LogError("FavoritesManager initialization failed: " + ex.Message);
            }
        }

        private void LoadCache()
        {
            if (string.IsNullOrEmpty(cacheFilePath)) return;

            lock (lockObj)
            {
                favoriteUids.Clear();
                
                string backupPath = cacheFilePath + ".bak";

                // Restore backup if main file is missing but backup exists
                if (!File.Exists(cacheFilePath) && File.Exists(backupPath))
                {
                    try
                    {
                        File.Copy(backupPath, cacheFilePath);
                        Debug.Log("FavoritesManager: Restored from backup.");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("FavoritesManager: Failed to restore backup: " + ex.Message);
                    }
                }

                if (!File.Exists(cacheFilePath)) return;

                // Create backup on successful startup (if not exists or maybe just always?)
                // A simple approach is to update backup if the current file is valid.
                // But we don't know if it's valid yet.
                // Let's try to load.

                long lastValidPos = 0;
                long fileLength = 0;

                try
                {
                    using (FileStream fs = new FileStream(cacheFilePath, FileMode.Open, FileAccess.Read))
                    {
                        fileLength = fs.Length;
                        using (BinaryReader reader = new BinaryReader(fs))
                        {
                            while (fs.Position < fs.Length)
                            {
                                long entryStart = fs.Position;
                                try 
                                {
                                    // Format: [Action(1)][PathLength(4)][PathBytes(N)]
                                    // Header Check: Ensure we have enough bytes for Action + Length
                                    if (fs.Position + 5 > fs.Length) break;

                                    byte action = reader.ReadByte();
                                    int length = reader.ReadInt32();

                                    if (length < 0 || fs.Position + length > fs.Length) break;

                                    byte[] bytes = reader.ReadBytes(length);
                                    string uid = Encoding.UTF8.GetString(bytes);

                                    if (uid.Contains("KarmaggedonVAM.DanaD3Lor3nz0_KARVAM.3.var") ||
                                        uid.Contains("caelryn.Jade_r.6.var") ||
                                        uid.Contains("caelryn.Aiko.3.var") ||
                                        uid.Contains("GilgameshVR.Gabriela.1.var") ||
                                        uid.Contains("via5.Synergy.3.var") ||
                                        uid.Contains("SupaRioAmateur.Scarlet_FF7R.4.var") ||
                                        uid.Contains("MeshedVR.BonusScenes.9.var") ||
                                        uid.Contains("MeshedVR.OlderContent.1.var"))
                                    {
                                        Debug.Log($"[VPB-DEBUG] Loading favorite from cache: {uid} | Action: {action}");
                                    }

                                    if (action == 1)
                                    {
                                        if (!favoriteUids.Contains(uid)) favoriteUids.Add(uid);
                                    }
                                    else
                                    {
                                        if (favoriteUids.Contains(uid)) favoriteUids.Remove(uid);
                                    }
                                    lastValidPos = fs.Position;
                                }
                                catch (Exception) 
                                {
                                    break; 
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("FavoritesManager: Error loading cache: " + ex.Message);
                }

                // Perform Truncation if needed
                if (lastValidPos > 0 && lastValidPos < fileLength)
                {
                     try
                     {
                         using (FileStream writeFs = new FileStream(cacheFilePath, FileMode.Open, FileAccess.Write))
                         {
                             writeFs.SetLength(lastValidPos);
                         }
                         Debug.LogWarning("FavoritesManager: Truncated corrupt cache from " + fileLength + " to " + lastValidPos);
                     }
                     catch(Exception ex)
                     {
                         Debug.LogError("FavoritesManager: Failed to truncate corrupt file: " + ex.Message);
                     }
                }

                // If load was successful (at least partially), update backup
                if (favoriteUids.Count > 0)
                {
                    try
                    {
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                        File.Copy(cacheFilePath, backupPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("FavoritesManager: Error creating backup: " + ex.Message);
                    }
                }
            }
        }

        private void CompactCache()
        {
            if (string.IsNullOrEmpty(cacheFilePath)) return;

            lock (lockObj)
            {
                string tmpPath = cacheFilePath + ".tmp";
                try
                {
                    using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
                    using (BinaryWriter writer = new BinaryWriter(fs))
                    {
                        foreach (string uid in favoriteUids)
                        {
                             byte[] bytes = Encoding.UTF8.GetBytes(uid);
                             writer.Write((byte)1); // Action Add
                             writer.Write(bytes.Length);
                             writer.Write(bytes);
                        }
                    }

                    if (File.Exists(cacheFilePath)) File.Delete(cacheFilePath);
                    File.Move(tmpPath, cacheFilePath);
                    
                    // Update Backup
                    string backupPath = cacheFilePath + ".bak";
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Copy(cacheFilePath, backupPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError("FavoritesManager: Error compacting cache: " + ex.Message);
                    // Attempt cleanup
                    if (File.Exists(tmpPath)) File.Delete(tmpPath);
                }
            }
        }

        private void AppendToCache(string uid, bool isAdd)
        {
            if (string.IsNullOrEmpty(cacheFilePath)) return;

            lock (lockObj)
            {
                try
                {
                    using (FileStream fs = new FileStream(cacheFilePath, FileMode.Append, FileAccess.Write))
                    using (BinaryWriter writer = new BinaryWriter(fs))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(uid);
                        writer.Write((byte)(isAdd ? 1 : 0));
                        writer.Write(bytes.Length);
                        writer.Write(bytes);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("FavoritesManager: Error appending to cache: " + ex.Message);
                }
            }
        }

        public bool IsFavorite(FileEntry entry)
        {
            if (entry == null) return false;
            return favoriteUids.Contains(entry.Uid);
        }

        public event Action<string, bool> OnFavoriteChanged;

        public void SetFavorite(FileEntry entry, bool isFavorite)
        {
            if (entry == null) return;
            
            if (entry.Uid.Contains("KarmaggedonVAM.DanaD3Lor3nz0_KARVAM.3.var") ||
                entry.Uid.Contains("caelryn.Jade_r.6.var") ||
                entry.Uid.Contains("caelryn.Aiko.3.var") ||
                entry.Uid.Contains("GilgameshVR.Gabriela.1.var") ||
                entry.Uid.Contains("via5.Synergy.3.var") ||
                entry.Uid.Contains("SupaRioAmateur.Scarlet_FF7R.4.var") ||
                entry.Uid.Contains("MeshedVR.BonusScenes.9.var") ||
                entry.Uid.Contains("MeshedVR.OlderContent.1.var"))
            {
                Debug.Log($"[VPB-DEBUG] SetFavorite: {entry.Uid} | NewValue: {isFavorite}");
            }

            bool current = IsFavorite(entry);
            if (current == isFavorite) return;

            // Update in-memory set
            if (isFavorite) favoriteUids.Add(entry.Uid);
            else favoriteUids.Remove(entry.Uid);

            // Update Cache File
            AppendToCache(entry.Uid, isFavorite);

            // Update Native .fav file
            UpdateNativeFile(entry, isFavorite);

            OnFavoriteChanged?.Invoke(entry.Uid, isFavorite);
        }

        private void UpdateNativeFile(FileEntry entry, bool isFavorite)
        {
            try
            {
                string favPath = GetFavPath(entry);
                if (string.IsNullOrEmpty(favPath)) return;

                if (isFavorite)
                {
                    string dir = Path.GetDirectoryName(favPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    if (!File.Exists(favPath))
                    {
                        File.WriteAllBytes(favPath, new byte[0]);
                    }
                }
                else
                {
                    if (File.Exists(favPath))
                    {
                        File.Delete(favPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("FavoritesManager: Error updating native file for " + entry.Uid + ": " + ex.Message);
            }
        }

        private string GetFavPath(FileEntry entry)
        {
            if (entry is SystemFileEntry)
            {
                return entry.Path + ".fav";
            }
            else if (entry is VarFileEntry varEntry)
            {
                // AddonPackagesFilePrefs/[PackageUID]/[InternalPath].fav
                string internalPath = varEntry.InternalPath.Replace('/', Path.DirectorySeparatorChar);
                string packageUid = varEntry.Package.Uid;
                
                string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackagesFilePrefs");
                return Path.Combine(baseDir, Path.Combine(packageUid, internalPath + ".fav"));
            }
            return null;
        }
    }
}
