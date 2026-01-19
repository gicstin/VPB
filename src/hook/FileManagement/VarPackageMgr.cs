using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityEngine;
using Valve.Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;

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
        public Dictionary<string, SerializableVarPackage> lookup = new Dictionary<string, SerializableVarPackage>();
        
        public SerializableVarPackage TryGetCache(string uid)
        {
            if (lookup.ContainsKey(uid))
            {
                return lookup[uid];
            }
            return null;
        }
        public bool existCache = false;
        public void Init()
        {
            existCache = false;
            if (File.Exists(CachePath))
            {
                existCache = true;
                using (FileStream stream = new FileStream(CachePath, FileMode.Open))
                {
                    if (stream != null)
                    {
                        BinaryReader reader=new BinaryReader(stream);
                        var count = reader.ReadInt32();
                        if (count > 0)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var key = reader.ReadString();
                                SerializableVarPackage pkg = new SerializableVarPackage();
                                pkg.Read(reader);
                                var pair = new KeyValuePair<string, SerializableVarPackage>(key, pkg);
                                if (!lookup.ContainsKey(key))
                                    lookup.Add(key, pkg);

                            }
                        }
                    }
                }
            }
        }
        public void Refresh()
        {
            bool dirty = false;
            foreach(var item in FileManager.PackagesByUid)
            {
                var uid = item.Key;
                if (!lookup.ContainsKey(uid))
                {
					string cacheJson = "Cache/AllPackagesJSON/" + uid + ".json";
                    if (File.Exists(cacheJson))
                    {
                        string text = File.ReadAllText(cacheJson);
                        SerializableVarPackage vp;
                        try
                        {
                            lock (LogUtil.JsonLock)
                            {
                                vp = Valve.Newtonsoft.Json.JsonConvert.DeserializeObject<SerializableVarPackage>(text);
                            }
                            lookup.Add(uid, vp);
                            dirty = true;
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("Failed to deserialize cache for " + uid + ": " + ex.Message);
                            try { File.Delete(cacheJson); } catch { }
                        }
                    }
                }
            }
            if (dirty)
            {
                string tempPath = CachePath + ".tmp";
                try
                {
                    using (FileStream stream = new FileStream(tempPath, FileMode.Create))
                    {
                        BinaryWriter writer = new BinaryWriter(stream);
                        writer.Write(lookup.Count);
                        foreach (var item in lookup)
                        {
                            writer.Write(item.Key);
                            item.Value.Write(writer);
                        }
                        writer.Flush();
                        writer.Close();
                    }
                    if (File.Exists(CachePath)) File.Delete(CachePath);
                    File.Move(tempPath, CachePath);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Failed to write main cache: " + ex.Message);
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            }
        }
    }
}
