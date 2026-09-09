using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using GPUTools.Hair.Scripts.Geometry.Create;
using GPUTools.Skinner.Scripts.Providers;

namespace VPB
{
    internal static class VpbHairFootprint
    {
        private const string HairTag = "RuntimeHairGeometryCreator";

        private static readonly Dictionary<string, int[][]> s_scalpToBody =
            new Dictionary<string, int[][]>(StringComparer.Ordinal);

        private static readonly Dictionary<string, int> s_scalpMisses =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private const int MaxScalpLookupMisses = 4;

        private static bool s_sweptForScalps;

        internal static void ResetSessionState()
        {
            s_scalpToBody.Clear();
            s_scalpMisses.Clear();
            s_sweptForScalps = false;
        }

        private static VpbClothingFootprint RejectIfBodyWide(VpbClothingFootprint fp, int gender, string what)
        {
            if (fp == null || !fp.IsBodySpace) return fp;

            VpbSkinRegionMap.EnsureLoaded(gender);
            byte[] map = VpbSkinRegionMap.Get(gender);
            if (map == null || map.Length == 0) return fp;

            if (fp.VertCount * 5 <= map.Length) return fp;

            return null;
        }

        internal static string SpaceKey(string scalpName, int scalpVertexCount)
        {
            return (string.IsNullOrEmpty(scalpName) ? "?" : scalpName) + "#" + scalpVertexCount;
        }

        internal static VpbClothingFootprint FromLiveItem(DAZHairGroup item)
        {
            if (item == null) return null;

            int gender = VpbSkinRegionMap.GenderFemale;
            try { if (item.gender == DAZDynamicItem.Gender.Male) gender = VpbSkinRegionMap.GenderMale; }
            catch { }

            VpbClothingFootprint meshFp = VpbClothingFootprint.FromLiveWraps(item, gender);
            if (meshFp != null) return meshFp;

            RuntimeHairGeometryCreator[] creators = null;
            try { creators = item.GetComponentsInChildren<RuntimeHairGeometryCreator>(true); }
            catch { creators = null; }
            if (creators == null || creators.Length == 0) return null;

            var rooted = new List<int>(1024);
            string space = null;

            for (int i = 0; i < creators.Length; i++)
            {
                RuntimeHairGeometryCreator c = creators[i];
                if (c == null) continue;

                bool[] mask = null;
                try { mask = c.strandsMask != null ? c.strandsMask.vertices : null; }
                catch { mask = null; }
                if (mask == null || mask.Length == 0) continue;

                DAZSkinWrap scalpWrap = null;
                try { scalpWrap = c.ScalpProvider as DAZSkinWrap; }
                catch { scalpWrap = null; }

                if (scalpWrap != null && AppendProjected(rooted, mask, scalpWrap))
                {
                    space = "";
                    continue;
                }

                bool scalpIsSkin = false;
                try { scalpIsSkin = c.ScalpProvider is DAZSkinV2; }
                catch { }

                string thisSpace = scalpIsSkin ? "" : SpaceKey(ScalpName(c), mask.Length);
                if (space != null && !string.Equals(space, thisSpace, StringComparison.Ordinal)) continue;
                space = thisSpace;

                for (int v = 0; v < mask.Length; v++)
                    if (mask[v]) rooted.Add(v);
            }

            if (rooted.Count == 0) return null;
            return VpbClothingFootprint.FromVertexList(rooted, gender, space ?? "");
        }

        private static string ScalpName(RuntimeHairGeometryCreator c)
        {
            try
            {
                if (!string.IsNullOrEmpty(c.ScalpProviderName)) return c.ScalpProviderName;
                PreCalcMeshProvider p = c.ScalpProvider;
                if (p != null) return p.name;
            }
            catch { }
            return "?";
        }

        private static bool AppendProjected(List<int> into, bool[] mask, DAZSkinWrap scalpWrap)
        {
            DAZSkinWrapStore.SkinWrapVert[] wv = null;
            try { wv = scalpWrap.wrapStore != null ? scalpWrap.wrapStore.wrapVertices : null; }
            catch { wv = null; }
            if (wv == null || wv.Length == 0) return false;

            int n = mask.Length < wv.Length ? mask.Length : wv.Length;
            int added = 0;
            for (int v = 0; v < n; v++)
            {
                if (!mask[v]) continue;
                into.Add(wv[v].Vertex1);
                into.Add(wv[v].Vertex2);
                into.Add(wv[v].Vertex3);
                added++;
            }
            return added > 0;
        }

        internal static VpbClothingFootprint FromVabBytes(byte[] data, int gender, Atom atom)
        {
            if (data == null || data.Length < 32) return null;

            string scalpName;
            int scalpVertexCount;
            bool[] mask = TryReadScalpMask(data, out scalpName, out scalpVertexCount);

            if (mask == null)
            {
                return VpbClothingFootprint.FromVabBytes(data, gender);
            }

            int[][] projection = null;

            if (scalpName != null && scalpName.StartsWith("Custom", StringComparison.OrdinalIgnoreCase))
                projection = VpbClothingFootprint.TryReadWrapStoreTriples(data, scalpVertexCount);

            if (projection == null)
                projection = TryGetScalpProjection(atom, scalpName, scalpVertexCount);

            var verts = new List<int>(1024);

            if (projection != null)
            {
                int n = mask.Length < projection.Length ? mask.Length : projection.Length;
                for (int v = 0; v < n; v++)
                {
                    if (!mask[v]) continue;
                    int[] tri = projection[v];
                    if (tri == null) continue;
                    verts.Add(tri[0]); verts.Add(tri[1]); verts.Add(tri[2]);
                }
                if (verts.Count > 0)
                {
                    VpbClothingFootprint projected =
                        RejectIfBodyWide(VpbClothingFootprint.FromVertexList(verts, gender, ""), gender, scalpName);
                    if (projected != null) return projected;
                    return null;
                }
                verts.Clear();
            }

            for (int v = 0; v < mask.Length; v++)
                if (mask[v]) verts.Add(v);
            if (verts.Count == 0) return null;

            return VpbClothingFootprint.FromVertexList(verts, gender, SpaceKey(scalpName, scalpVertexCount));
        }

        internal static bool[] TryReadScalpMask(byte[] data, out string scalpName, out int scalpVertexCount)
        {
            scalpName = null;
            scalpVertexCount = 0;
            if (data == null) return null;

            byte[] marker = Encoding.UTF8.GetBytes(HairTag);
            int at = VpbClothingFootprint.IndexOfSection(data, marker, 0);
            if (at < 0) return null;

            int p = at;
            if (VpbClothingFootprint.ReadString(data, ref p) != HairTag) return null;

            string schema = VpbClothingFootprint.ReadString(data, ref p);
            if (schema != "1.0" && schema != "1.1") return null;

            scalpName = VpbClothingFootprint.ReadString(data, ref p);
            if (scalpName == null) return null;

            if (p + 8 > data.Length) return null;
            p += 8;

            if (VpbClothingFootprint.ReadString(data, ref p) == null) return null;

            if (p + 4 > data.Length) return null;
            int count = VpbClothingFootprint.ReadInt32(data, ref p);
            if (count <= 0 || (long)p + count > data.Length) return null;

            bool[] mask = new bool[count];
            int on = 0;
            for (int i = 0; i < count; i++)
            {
                if (data[p + i] != 0) { mask[i] = true; on++; }
            }
            p += count;

            if (p + 4 <= data.Length)
            {
                int declared = VpbClothingFootprint.ReadInt32(data, ref p);
                if (declared != count) return null;
            }

            scalpVertexCount = count;
            return on > 0 ? mask : null;
        }

        private static int[][] TryGetScalpProjection(Atom atom, string scalpName, int scalpVertexCount)
        {
            if (atom == null || string.IsNullOrEmpty(scalpName) || scalpVertexCount <= 0) return null;

            string key = SpaceKey(scalpName, scalpVertexCount);
            int[][] cached;
            if (s_scalpToBody.TryGetValue(key, out cached)) return cached;

            int[][] stored = VpbLocalDatabase.TryLoadScalpProjection(key);
            if (stored != null && stored.Length >= scalpVertexCount)
            {
                s_scalpToBody[key] = stored;
                s_scalpMisses.Remove(key);
                return stored;
            }

            int misses;
            s_scalpMisses.TryGetValue(key, out misses);
            if (misses >= MaxScalpLookupMisses) return null;

            int[][] built = BuildScalpProjection(atom, scalpName, scalpVertexCount);
            if (built != null)
            {
                s_scalpToBody[key] = built;
                s_scalpMisses.Remove(key);
                VpbLocalDatabase.TrySaveScalpProjection(key, built);
            }
            else
            {
                s_scalpMisses[key] = misses + 1;
                s_sweptForScalps = false;
            }
            return built;
        }

        internal static void EnsureScalpsLearned(Atom atom)
        {
            if (s_sweptForScalps) return;
            s_sweptForScalps = true;
            HarvestAvailableScalps(atom);
        }

        internal static void HarvestAvailableScalps(Atom atom)
        {
            if (atom == null) return;

            DAZSkinWrap[] wraps = null;
            try { wraps = atom.GetComponentsInChildren<DAZSkinWrap>(true); }
            catch { wraps = null; }
            if (wraps == null) return;

            for (int i = 0; i < wraps.Length; i++)
            {
                DAZSkinWrap w = wraps[i];
                if (w == null) continue;

                string n = null;
                try { n = w.name; } catch { }
                if (string.IsNullOrEmpty(n) || n.IndexOf("scalp", StringComparison.OrdinalIgnoreCase) < 0) continue;

                DAZSkinWrapStore.SkinWrapVert[] wv = null;
                try { wv = w.wrapStore != null ? w.wrapStore.wrapVertices : null; }
                catch { wv = null; }
                if (wv == null || wv.Length == 0) continue;

                string key = SpaceKey(n, wv.Length);
                if (s_scalpToBody.ContainsKey(key)) continue;
                if (VpbLocalDatabase.TryLoadScalpProjection(key) != null) { s_scalpMisses.Remove(key); continue; }

                int[][] map = new int[wv.Length][];
                for (int v = 0; v < wv.Length; v++)
                    map[v] = new[] { wv[v].Vertex1, wv[v].Vertex2, wv[v].Vertex3 };

                s_scalpToBody[key] = map;
                s_scalpMisses.Remove(key);
                VpbLocalDatabase.TrySaveScalpProjection(key, map);
            }
        }

        private static int[][] BuildScalpProjection(Atom atom, string scalpName, int scalpVertexCount)
        {
            DAZSkinWrap[] wraps = null;
            try { wraps = atom.GetComponentsInChildren<DAZSkinWrap>(true); }
            catch { wraps = null; }
            if (wraps == null) return null;

            for (int i = 0; i < wraps.Length; i++)
            {
                DAZSkinWrap w = wraps[i];
                if (w == null) continue;

                string n = null;
                try { n = w.name; } catch { }
                if (!string.Equals(n, scalpName, StringComparison.OrdinalIgnoreCase)) continue;

                DAZSkinWrapStore.SkinWrapVert[] wv = null;
                try { wv = w.wrapStore != null ? w.wrapStore.wrapVertices : null; }
                catch { wv = null; }
                if (wv == null || wv.Length < scalpVertexCount) continue;

                int[][] map = new int[scalpVertexCount][];
                for (int v = 0; v < scalpVertexCount; v++)
                    map[v] = new[] { wv[v].Vertex1, wv[v].Vertex2, wv[v].Vertex3 };

                return map;
            }

            return null;
        }
    }
}
