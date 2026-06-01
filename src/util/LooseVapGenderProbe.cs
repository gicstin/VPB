using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;

namespace VPB.src.util
{
    /// <summary>
    /// Classifies a loose .vap appearance preset by reading the <c>geometry</c> storable's <c>character</c> field
    /// (DAZCharacter displayName). Resolution order:
    ///   1. Live <see cref="JSONExtensions.CharacterGenderMap"/> lookup, when the user has the pack installed —
    ///      this matches what VaM itself would set on load.
    ///   2. displayName prefix heuristic ("Female"/"Male"/"Futa") for packs not installed locally.
    ///   3. useFemaleMorphsOnMale toggle promotes a male-base classification to Futa (plugin convention).
    /// Results are cached in <c>loose_vap_gender</c> keyed by (path, mtime, size).
    /// </summary>
    public static class LooseVapGenderProbe
    {
        /// <summary>Matches <see cref="GalleryPanel.AppearanceGender"/> ordering: 0=Unknown, 1=Female, 2=Male, 3=Futa. Futa is currently folded into Male at every classifier call site so the UI surfaces only Female/Male; the enum value is kept for cache compatibility.</summary>
        public enum Gender { Unknown = 0, Female = 1, Male = 2, Futa = 3 }

        private static readonly Dictionary<string, Gender> s_MemCache =
            new Dictionary<string, Gender>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_MemCacheLock = new object();

        /// <summary>Gender for <paramref name="filePath"/>: in-process cache, then SQLite, then file parse. Never throws.</summary>
        public static Gender Classify(string filePath)
        {
            return Classify(filePath, null);
        }

        // bulk != null routes the SQLite read/write through a preloaded table snapshot + batched flush so a scan over many files doesn't open a connection (and run EnsureSchema) per file.
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
                // An Unknown verdict cached before the DAZ character map finished loading is provisional — the file
                // might reference a character that's now resolvable. Re-probe instead of trusting it.
                if (g != Gender.Unknown || !mapReady)
                {
                    lock (s_MemCacheLock) { s_MemCache[filePath] = g; }
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

            // Only persist verdicts that won't change once the map finishes loading. A definitive answer (Female /
            // Male / Futa) is stable; an Unknown computed without the map should not be locked in.
            bool persist = (resolved != Gender.Unknown) || mapReady;
            if (persist)
            {
                if (bulk != null) bulk.Enqueue(filePath, wtBin, sz, (int)resolved);
                else { try { VpbLocalDatabase.WriteLooseVapGender(filePath, wtBin, sz, (int)resolved); } catch { } }
            }
            lock (s_MemCacheLock) { s_MemCache[filePath] = resolved; }
            return FoldFutaForUi(resolved);
        }

        /// <summary>Apply the resolution rules from gender-findings/03 to a (characterName, flag) pair.</summary>
        public static Gender Resolve(string characterName, bool useFemaleMorphsOnMale)
        {
            string name = (characterName ?? "").Trim();
            if (name.Length == 0) return Gender.Unknown;

            // 1. Live prefab map (authoritative when user has the pack).
            bool? isMaleFromMap = null;
            try
            {
                if (JSONExtensions.IsCharacterGenderMapInitComplete()
                    && JSONExtensions.CharacterGenderMap != null
                    && JSONExtensions.CharacterGenderMap.TryGetValue(name, out string g))
                {
                    if (string.Equals(g, "Male", StringComparison.OrdinalIgnoreCase)) isMaleFromMap = true;
                    else if (string.Equals(g, "Female", StringComparison.OrdinalIgnoreCase)) isMaleFromMap = false;
                }
            }
            catch { isMaleFromMap = null; }

            // Futa fold: VaM treats Futa as a male-base character (isMale=true). The UI currently surfaces only
            // Female and Male, so Futa-signalled inputs land in Male here. Keep the cached `Gender.Futa` value
            // working through ReadCacheFolded so we can revive the third badge later without re-scanning.
            if (isMaleFromMap.HasValue) return isMaleFromMap.Value ? Gender.Male : Gender.Female;

            // 2. Prefix heuristic for packs not installed locally.
            if (StartsWithToken(name, "Female")) return Gender.Female;
            if (StartsWithToken(name, "Futa"))   return Gender.Male;
            if (StartsWithToken(name, "Male"))   return Gender.Male;
            return Gender.Unknown;
        }

        /// <summary>Cache rows persisted before the Futa→Male fold may hold gender=3. Fold on read.</summary>
        private static Gender FoldFutaForUi(Gender g) { return g == Gender.Futa ? Gender.Male : g; }

        private static bool StartsWithToken(string name, string token)
        {
            if (name == null || token == null) return false;
            if (!name.StartsWith(token, StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Length == token.Length) return true;
            char next = name[token.Length];
            return next == ' ' || next == '_' || next == '-';
        }

        /// <summary>Reads the smallest set of fields needed: storables[*].id=="geometry" → character + useFemaleMorphsOnMale.</summary>
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

                JSONNode flagNode = entry["useFemaleMorphsOnMale"];
                if (flagNode != null)
                {
                    string v = flagNode.Value;
                    useFemaleMorphsOnMale = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                }
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

    // Scan-scoped snapshot of loose_vap_gender: one bulk read up front, (wtime,size)-validated lookups in memory,
    // freshly-probed verdicts batched and flushed once. Replaces per-file connection + EnsureSchema churn.
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
