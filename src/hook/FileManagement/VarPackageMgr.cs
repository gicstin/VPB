using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityEngine;
using Valve.Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;
using System.Diagnostics;

namespace VPB
{
    [System.Serializable]
    public class AllSerializableVarPackage
    {
        public SerializableVarPackage[] Packages;
    }
    class VarPackageMgr
    {
        public static VarPackageMgr singleton=new VarPackageMgr();

        static string CachePath = "Cache/AllPackagesJSON/" + "AllPackages.bytes2";
        const int CacheMagic = 0x56504231;
        const int CacheVersion = 4;
        readonly object lookupLock = new object();
        public Dictionary<string, SerializableVarPackage> lookup = new Dictionary<string, SerializableVarPackage>();
        
        public SerializableVarPackage TryGetCache(string uid)
        {
            lock (lookupLock)
            {
                if (lookup.ContainsKey(uid))
                {
                    return lookup[uid];
                }
            }
            return null;
        }

        public SerializableVarPackage TryGetCacheValidated(string uid, long fileSize, long lastWriteTimeUtcTicks)
        {
            var cached = TryGetCache(uid);
            if (cached == null)
                return null;
            if (cached.VarFileSize <= 0 || cached.VarLastWriteTimeUtcTicks <= 0)
                return null;
            if (cached.VarFileSize != fileSize || cached.VarLastWriteTimeUtcTicks != lastWriteTimeUtcTicks)
                return null;
            return cached;
        }
        bool dirtyExternal = false;
        public bool existCache = false;
        public void SetCache(string uid, SerializableVarPackage value)
        {
            lock (lookupLock)
            {
                if (lookup.ContainsKey(uid))
                    lookup[uid] = value;
                else
                    lookup.Add(uid, value);
            }
            dirtyExternal = true;
        }
        public void Init()
        {
            existCache = false;
            int loadedCount = 0;
            Stopwatch sw = Stopwatch.StartNew();
            if (File.Exists(CachePath))
            {
                existCache = true;
                using (FileStream stream = new FileStream(CachePath, FileMode.Open))
                {
                    if (stream != null)
                    {
                        BinaryReader reader=new BinaryReader(stream);
                        int first = reader.ReadInt32();
                        int count = 0;
                        if (first == CacheMagic)
                        {
                            int version = reader.ReadInt32();
                            if (version != CacheVersion)
                            {
                                sw.Stop();
                                LogUtil.Log("VarPackageMgr cache version mismatch " + version);
                                return;
                            }
                            count = reader.ReadInt32();
                        }
                        else
                        {
                            count = first;
                        }
                        if (count > 0)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var key = reader.ReadString();
                                SerializableVarPackage pkg = new SerializableVarPackage();
                                pkg.Read(reader, first == CacheMagic);
                                var pair = new KeyValuePair<string, SerializableVarPackage>(key, pkg);
                                lock (lookupLock)
                                {
                                    if (!lookup.ContainsKey(key))
                                        lookup.Add(key, pkg);
                                }
                                loadedCount++;
                            }
                        }
                    }
                }
            }
            sw.Stop();
            if (existCache)
            {
                LogUtil.Log("VarPackageMgr cache load " + loadedCount + " in " + sw.ElapsedMilliseconds + "ms");
            }
            else
            {
                LogUtil.Log("VarPackageMgr cache missing");
            }
        }
        public void Refresh()
        {
            if (!dirtyExternal)
                return;

            Stopwatch sw = Stopwatch.StartNew();
            Dictionary<string, SerializableVarPackage> snapshot;
            lock (lookupLock)
            {
                snapshot = new Dictionary<string, SerializableVarPackage>(lookup);
            }
            if (snapshot.Count == 0)
                return;

            string tempPath = CachePath + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(tempPath, FileMode.Create))
                {
                    BinaryWriter writer = new BinaryWriter(stream);
                    writer.Write(CacheMagic);
                    writer.Write(CacheVersion);
                    writer.Write(snapshot.Count);
                    foreach (var item in snapshot)
                    {
                        writer.Write(item.Key);
                        item.Value.Write(writer);
                    }
                    writer.Flush();
                    writer.Close();
                }
                if (File.Exists(CachePath)) File.Delete(CachePath);
                File.Move(tempPath, CachePath);
                sw.Stop();
                long bytes = 0;
                if (File.Exists(CachePath)) bytes = new FileInfo(CachePath).Length;
                LogUtil.Log("VarPackageMgr cache write " + snapshot.Count + " in " + sw.ElapsedMilliseconds + "ms bytes=" + bytes);
                dirtyExternal = false;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("Failed to write main cache: " + ex.Message);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }
}
