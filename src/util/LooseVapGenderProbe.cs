using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;

namespace VPB.src.util
{
    public static class LooseVapGenderProbe
    {
        /// <summary>Matches AppearanceGender ordering: 0=Unknown, 1=Female, 2=Male, 3=Futa.</summary>
        public enum Gender { Unknown = 0, Female = 1, Male = 2, Futa = 3 }

        private static readonly Dictionary<string, Gender> s_MemCache =
            new Dictionary<string, Gender>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_MemCacheLock = new object();
        private const int MemCacheMaxEntries = 4096;

        private static void PutMemCache(string filePath, Gender g)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            lock (s_MemCacheLock)
            {
                if (s_MemCache.Count >= MemCacheMaxEntries && !s_MemCache.ContainsKey(filePath))
                    s_MemCache.Clear();
                s_MemCache[filePath] = g;
            }
        }

        /// <summary>Gender for <paramref name="filePath"/>: in-process cache, then SQLite, then file parse. Never throws.</summary>
        public static Gender Classify(string filePath)
        {
            return Classify(filePath, null);
        }

        public static Gender Classify(string filePath, LooseVapGenderBulkCache bulk)
        {
            if (string.IsNullOrEmpty(filePath)) return Gender.Unknown;

            lock (s_MemCacheLock)
            {
                Gender cached;
                if (s_MemCache.TryGetValue(filePath, out cached)) return FoldFutaForUi(cached);
            }

            long wtBin = 0, sz = 0;
            bool haveStat = false;
            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Exists)
                {
                    wtBin = fi.LastWriteTimeUtc.ToBinary();
                    sz = fi.Length;
                    haveStat = true;
                }
            }
            catch { haveStat = false; }

            if (!haveStat) return Gender.Unknown;

            bool mapReady = false;
            try { mapReady = JSONExtensions.IsCharacterGenderMapInitComplete(); } catch { mapReady = false; }

            int dbGender;
            bool haveDbRow;
            if (bulk != null) haveDbRow = bulk.TryGet(filePath, wtBin, sz, out dbGender);
            else haveDbRow = VpbLocalDatabase.TryReadLooseVapGender(filePath, wtBin, sz, out dbGender);
            if (haveDbRow)
            {
                Gender g = (Gender)dbGender;
                // An Unknown verdict cached before the DAZ character map finished loading is provisional.
                if (g != Gender.Unknown || !mapReady)
                {
                    PutMemCache(filePath, g);
                    return FoldFutaForUi(g);
                }
            }

            string characterName = null;
            bool useFemaleMorphsOnMale = false;
            if (!TryReadGeometryFields(filePath, out characterName, out useFemaleMorphsOnMale))
            {
                // Parse failed; do NOT cache so a transient read error doesn't pin an Unknown verdict.
                return Gender.Unknown;
            }

            Gender resolved = Resolve(characterName, useFemaleMorphsOnMale);

            // Only persist verdicts that won't change once the map finishes loading.
            bool persist = (resolved != Gender.Unknown) || mapReady;
            if (persist)
            {
                if (bulk != null) bulk.Enqueue(filePath, wtBin, sz, (int)resolved);
                else { try { VpbLocalDatabase.WriteLooseVapGender(filePath, wtBin, sz, (int)resolved); } catch { } }
            }
            PutMemCache(filePath, resolved);
            return FoldFutaForUi(resolved);
        }

        public static Gender ClassifyStorables(JSONNode node)
        {
            if (node == null) return Gender.Unknown;
            try
            {
                JSONArray storables = node["storables"].AsArray;
                if (storables == null) return Gender.Unknown;
                for (int i = 0; i < storables.Count; i++)
                {
                    JSONNode entry = storables[i];
                    if (entry == null) continue;
                    string id = entry["id"];
                    if (string.IsNullOrEmpty(id) || !string.Equals(id, "geometry", StringComparison.Ordinal)) continue;

                    JSONNode charNode = entry["character"];
                    return Resolve(charNode != null ? charNode.Value : null, ReadUseFemaleMorphsOnMale(entry["useFemaleMorphsOnMale"]));
                }
            }
            catch { }
            return Gender.Unknown;
        }

        public static Gender Resolve(string characterName, bool useFemaleMorphsOnMale)
        {
            string name = (characterName ?? "").Trim();
            if (name.Length == 0) return Gender.Unknown;

            bool? isMaleFromMap = null;
            try
            {
                if (JSONExtensions.IsCharacterGenderMapInitComplete()
                    && JSONExtensions.CharacterGenderMap != null
                    && JSONExtensions.CharacterGenderMap.TryGetValue(name, out string mapped))
                {
                    if (string.Equals(mapped, "Male", StringComparison.OrdinalIgnoreCase)) isMaleFromMap = true;
                    else if (string.Equals(mapped, "Female", StringComparison.OrdinalIgnoreCase)) isMaleFromMap = false;
                }
            }
            catch { isMaleFromMap = null; }

            Gender g;
            if (IsFutaCharacterName(name)) g = Gender.Futa;
            else if (isMaleFromMap.HasValue) g = isMaleFromMap.Value ? Gender.Male : Gender.Female;
            else if (StartsWithToken(name, "Female")) g = Gender.Female;
            else if (StartsWithToken(name, "Male")) g = Gender.Male;
            else g = Gender.Unknown;

            if (useFemaleMorphsOnMale && g == Gender.Male) return Gender.Futa;
            return g;
        }

        public static bool IsFutaCharacterName(string name)
        {
            return StartsWithToken((name ?? "").Trim(), "Futa");
        }

        private static Gender FoldFutaForUi(Gender g) { return g == Gender.Futa ? Gender.Male : g; }

        private static bool ReadUseFemaleMorphsOnMale(JSONNode flagNode)
        {
            if (flagNode == null) return false;
            try { if (flagNode.AsBool) return true; } catch { }
            try
            {
                string v = flagNode.Value;
                return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(v, "1", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static bool StartsWithToken(string name, string token)
        {
            if (name == null || token == null) return false;
            if (!name.StartsWith(token, StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Length == token.Length) return true;
            char next = name[token.Length];
            return next == ' ' || next == '_' || next == '-';
        }

        private static bool TryReadGeometryFields(string filePath, out string characterName, out bool useFemaleMorphsOnMale)
        {
            characterName = null;
            useFemaleMorphsOnMale = false;

            string text;
            try { text = File.ReadAllText(filePath); }
            catch { return false; }
            if (string.IsNullOrEmpty(text)) return false;

            JSONNode root;
            try { root = JSON.Parse(text); }
            catch { return false; }
            if (root == null) return false;

            JSONArray storables = root["storables"].AsArray;
            if (storables == null) return false;

            for (int i = 0; i < storables.Count; i++)
            {
                JSONNode entry = storables[i];
                if (entry == null) continue;
                string id = entry["id"];
                if (string.IsNullOrEmpty(id) || !string.Equals(id, "geometry", StringComparison.Ordinal)) continue;

                JSONNode charNode = entry["character"];
                if (charNode != null) characterName = charNode.Value;

                useFemaleMorphsOnMale = ReadUseFemaleMorphsOnMale(entry["useFemaleMorphsOnMale"]);
                return characterName != null;
            }
            return false;
        }

        /// <summary>Drops the in-process cache. Called after package refresh (new characters may resolve names that were previously Unknown).</summary>
        public static void InvalidateMemoryCache()
        {
            lock (s_MemCacheLock) { s_MemCache.Clear(); }
        }
    }

    public sealed class LooseVapGenderBulkCache
    {
        private readonly Dictionary<string, VpbLocalDatabase.LooseVapGenderRow> _rows =
            new Dictionary<string, VpbLocalDatabase.LooseVapGenderRow>(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<string, VpbLocalDatabase.LooseVapGenderRow>> _pending =
            new List<KeyValuePair<string, VpbLocalDatabase.LooseVapGenderRow>>();

        public LooseVapGenderBulkCache()
        {
            try { VpbLocalDatabase.TryLoadAllLooseVapGender(_rows); } catch { }
        }

        public bool TryGet(string path, long wtBin, long size, out int gender)
        {
            gender = 0;
            if (string.IsNullOrEmpty(path)) return false;
            VpbLocalDatabase.LooseVapGenderRow r;
            if (!_rows.TryGetValue(path, out r)) return false;
            if (r.Wtime != wtBin || r.Size != size) return false;
            gender = r.Gender;
            return true;
        }

        public void Enqueue(string path, long wtBin, long size, int gender)
        {
            if (string.IsNullOrEmpty(path)) return;
            VpbLocalDatabase.LooseVapGenderRow r;
            r.Wtime = wtBin;
            r.Size = size;
            r.Gender = gender;
            _rows[path] = r;
            _pending.Add(new KeyValuePair<string, VpbLocalDatabase.LooseVapGenderRow>(path, r));
        }

        public void Flush()
        {
            if (_pending.Count == 0) return;
            try { VpbLocalDatabase.WriteLooseVapGenderBatch(_pending); } catch { }
            _pending.Clear();
        }
    }
}
