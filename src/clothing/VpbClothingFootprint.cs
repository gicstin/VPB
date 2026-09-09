using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MVR.FileManagement;
using ICSharpCode.SharpZipLib.Zip;
using UnityEngine;

namespace VPB
{
    internal sealed class VpbClothingFootprint
    {
        internal ulong[] Bits;
        internal int VertCount;
        internal uint RegionMask;
        internal float Standoff;
        internal int Gender;

        internal string Space;

        internal bool IsDegenerate
        {
            get { return VertCount < 12 || Standoff > 0.25f || Standoff < -0.25f; }
        }

        internal bool IsUsable { get { return Bits != null && VertCount > 0 && !IsDegenerate; } }

        internal bool IsBodySpace { get { return string.IsNullOrEmpty(Space); } }

        internal bool ComparableWith(VpbClothingFootprint other)
        {
            if (other == null) return false;
            if (Gender != other.Gender) return false;
            return string.Equals(Space ?? "", other.Space ?? "", StringComparison.Ordinal);
        }

        internal static int WordsFor(int vertexCount)
        {
            return (vertexCount + 63) >> 6;
        }

        internal static int PopCount(ulong v)
        {
            v = v - ((v >> 1) & 0x5555555555555555UL);
            v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
            v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((v * 0x0101010101010101UL) >> 56);
        }

        private ulong[] _cellBits;
        private int _cellCount;
        private bool _cellsTried;

        internal ulong[] CellBits
        {
            get { EnsureCells(); return _cellBits; }
        }

        private void EnsureCells()
        {
            if (_cellsTried) return;
            _cellsTried = true;
            if (Bits == null || !IsBodySpace) return;

            VpbSkinAreaMap.EnsureLoaded(Gender);
            int[] map = VpbSkinAreaMap.Get(Gender);
            if (map == null || map.Length == 0) return;

            int maxCell = 0;
            for (int i = 0; i < map.Length; i++)
                if (map[i] > maxCell) maxCell = map[i];

            ulong[] bits = new ulong[WordsFor(maxCell + 1)];
            int count = 0;
            for (int w = 0; w < Bits.Length; w++)
            {
                ulong word = Bits[w];
                while (word != 0UL)
                {
                    int b = TrailingZeroCount(word);
                    word &= word - 1UL;
                    int v = (w << 6) + b;
                    if (v >= map.Length) continue;
                    int cell = map[v];
                    int cw = cell >> 6;
                    ulong bit = 1UL << (cell & 63);
                    if ((bits[cw] & bit) != 0UL) continue;
                    bits[cw] |= bit;
                    count++;
                }
            }

            if (count == 0) return;
            _cellBits = bits;
            _cellCount = count;
        }

        private static int Overlap(ulong[] a, ulong[] b)
        {
            if (a == null || b == null) return 0;
            int n = a.Length < b.Length ? a.Length : b.Length;
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                ulong w = a[i] & b[i];
                if (w != 0UL) total += PopCount(w);
            }
            return total;
        }

        internal bool TryGetMutualCoverage(VpbClothingFootprint other, out float coverThis, out float coverOther,
                                           out bool byArea)
        {
            coverThis = 0f;
            coverOther = 0f;
            byArea = false;
            if (other == null) return false;

            ulong[] a = CellBits;
            ulong[] b = other.CellBits;
            if (a != null && b != null && _cellCount > 0 && other._cellCount > 0)
            {
                if (IntersectionCount(other) == 0)
                {
                    byArea = true;
                    return false;
                }

                int shared = Overlap(a, b);
                coverThis = (float)shared / _cellCount;
                coverOther = (float)shared / other._cellCount;
                byArea = true;
                return shared > 0;
            }

            int sv = IntersectionCount(other);
            if (VertCount > 0) coverThis = (float)sv / VertCount;
            if (other.VertCount > 0) coverOther = (float)sv / other.VertCount;
            return sv > 0;
        }

        internal int IntersectionCount(VpbClothingFootprint other)
        {
            if (other == null || Bits == null || other.Bits == null) return 0;
            ulong[] a = Bits;
            ulong[] b = other.Bits;
            int n = a.Length < b.Length ? a.Length : b.Length;
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                ulong w = a[i] & b[i];
                if (w != 0UL) total += PopCount(w);
            }
            return total;
        }

        internal static VpbClothingFootprint Union(List<VpbClothingFootprint> parts)
        {
            if (parts == null || parts.Count == 0) return null;

            int words = 0;
            float standoff = 0f;
            int gender = -1;
            for (int i = 0; i < parts.Count; i++)
            {
                VpbClothingFootprint p = parts[i];
                if (p == null || !p.IsUsable || !p.IsBodySpace) continue;
                if (gender < 0) gender = p.Gender;
                else if (gender != p.Gender) continue;
                if (p.Bits.Length > words) words = p.Bits.Length;
                if (p.Standoff > standoff) standoff = p.Standoff;
            }
            if (words == 0 || gender < 0) return null;

            ulong[] bits = new ulong[words];
            for (int i = 0; i < parts.Count; i++)
            {
                VpbClothingFootprint p = parts[i];
                if (p == null || !p.IsUsable || !p.IsBodySpace || p.Gender != gender) continue;
                ulong[] b = p.Bits;
                for (int w = 0; w < b.Length; w++) bits[w] |= b[w];
            }

            int count = 0;
            for (int w = 0; w < bits.Length; w++)
                if (bits[w] != 0UL) count += PopCount(bits[w]);
            if (count == 0) return null;

            var fp = new VpbClothingFootprint
            {
                Bits = bits,
                VertCount = count,
                Gender = gender,
                Space = "",
                Standoff = standoff,
            };
            fp.RegionMask = ComputeRegionMask(fp);
            return fp;
        }

        internal static VpbClothingFootprint FromVertexList(List<int> verts, int gender, string space)
        {
            if (verts == null || verts.Count == 0) return null;
            int[] arr = verts.ToArray();
            return Build(arr, arr.Length, null, 0, gender, space);
        }

        private static VpbClothingFootprint Build(int[] verts, int vertsLen, float[] projections, int projLen, int gender)
        {
            return Build(verts, vertsLen, projections, projLen, gender, "");
        }

        private static VpbClothingFootprint Build(int[] verts, int vertsLen, float[] projections, int projLen,
                                                  int gender, string space)
        {
            if (verts == null || vertsLen <= 0) return null;

            int maxIndex = 0;
            for (int i = 0; i < vertsLen; i++)
            {
                int v = verts[i];
                if (v > maxIndex) maxIndex = v;
            }
            if (maxIndex < 0) return null;

            ulong[] bits = new ulong[WordsFor(maxIndex + 1)];
            int distinct = 0;
            for (int i = 0; i < vertsLen; i++)
            {
                int v = verts[i];
                if (v < 0) continue;
                int w = v >> 6;
                ulong bit = 1UL << (v & 63);
                if ((bits[w] & bit) != 0UL) continue;
                bits[w] |= bit;
                distinct++;
            }
            if (distinct == 0) return null;

            var fp = new VpbClothingFootprint
            {
                Bits = bits,
                VertCount = distinct,
                Gender = gender,
                Space = space ?? "",
                Standoff = Median(projections, projLen),
            };
            if (fp.IsBodySpace) fp.RegionMask = ComputeRegionMask(fp);
            return fp;
        }

        private static float Median(float[] values, int len)
        {
            if (values == null || len <= 0) return 0f;
            const int MaxSamples = 512;
            int step = len > MaxSamples ? len / MaxSamples : 1;
            int n = 0;
            float[] sample = new float[(len + step - 1) / step];
            for (int i = 0; i < len && n < sample.Length; i += step) sample[n++] = values[i];
            Array.Sort(sample, 0, n);
            return n > 0 ? sample[n >> 1] : 0f;
        }

        internal static uint ComputeRegionMask(VpbClothingFootprint fp)
        {
            if (fp == null || fp.Bits == null) return 0u;
            VpbSkinRegionMap.EnsureLoaded(fp.Gender);
            byte[] map = VpbSkinRegionMap.Get(fp.Gender);
            if (map == null || map.Length == 0) return 0u;

            int[] tally = new int[VpbBodyRegions.Count];
            ulong[] bits = fp.Bits;
            for (int w = 0; w < bits.Length; w++)
            {
                ulong word = bits[w];
                while (word != 0UL)
                {
                    int b = TrailingZeroCount(word);
                    word &= word - 1UL;
                    int v = (w << 6) + b;
                    if (v >= map.Length) continue;
                    byte r = map[v];
                    if (r >= VpbBodyRegions.Count) continue;
                    tally[r]++;
                }
            }

            int threshold = fp.VertCount / 50;
            if (threshold < 1) threshold = 1;
            uint mask = 0u;
            for (int i = 0; i < tally.Length; i++)
                if (tally[i] >= threshold) mask |= 1u << i;
            return mask;
        }

        private static int TrailingZeroCount(ulong v)
        {
            if (v == 0UL) return 64;
            int n = 0;
            if ((v & 0xFFFFFFFFUL) == 0UL) { n += 32; v >>= 32; }
            if ((v & 0xFFFFUL) == 0UL) { n += 16; v >>= 16; }
            if ((v & 0xFFUL) == 0UL) { n += 8; v >>= 8; }
            if ((v & 0xFUL) == 0UL) { n += 4; v >>= 4; }
            if ((v & 0x3UL) == 0UL) { n += 2; v >>= 2; }
            if ((v & 0x1UL) == 0UL) n += 1;
            return n;
        }

        internal static VpbClothingFootprint FromLiveItem(DAZClothingItem item)
        {
            if (item == null) return null;

            int gender = VpbSkinRegionMap.GenderFemale;
            try { if (item.gender == DAZDynamicItem.Gender.Male) gender = VpbSkinRegionMap.GenderMale; }
            catch { }

            return FromLiveWraps(item, gender);
        }

        internal static VpbClothingFootprint FromLiveWraps(Component item, int gender)
        {
            if (item == null) return null;

            DAZSkinWrap[] wraps = null;
            try { wraps = item.GetComponentsInChildren<DAZSkinWrap>(true); }
            catch { wraps = null; }
            if (wraps == null || wraps.Length == 0) return null;

            int total = 0;
            for (int i = 0; i < wraps.Length; i++)
            {
                DAZSkinWrap w = wraps[i];
                if (w == null || w.wrapStore == null || w.wrapStore.wrapVertices == null) continue;
                total += w.wrapStore.wrapVertices.Length;
            }
            if (total == 0) return null;

            int[] verts = new int[total * 3];
            float[] proj = new float[total];
            int vi = 0, pi = 0;
            for (int i = 0; i < wraps.Length; i++)
            {
                DAZSkinWrap w = wraps[i];
                if (w == null || w.wrapStore == null) continue;
                DAZSkinWrapStore.SkinWrapVert[] wv = w.wrapStore.wrapVertices;
                if (wv == null) continue;
                for (int k = 0; k < wv.Length; k++)
                {
                    verts[vi++] = wv[k].Vertex1;
                    verts[vi++] = wv[k].Vertex2;
                    verts[vi++] = wv[k].Vertex3;
                    proj[pi++] = wv[k].surfaceNormalProjection;
                }
            }

            return Build(verts, vi, proj, pi, gender);
        }


        private const string WrapStoreTag = "DAZSkinWrapStore";
        private const string WrapStoreSchema = "1.0";
        private const int WrapVertStride = 40;

        internal static VpbClothingFootprint FromVabBytes(byte[] data, int gender)
        {
            if (data == null || data.Length < 32) return null;

            byte[] marker = Encoding.UTF8.GetBytes(WrapStoreTag);
            int pos = 0;
            while (true)
            {
                int at = IndexOfSection(data, marker, pos);
                if (at < 0) return null;
                pos = at + 1;

                int p = at;
                string tag = ReadString(data, ref p);
                if (tag != WrapStoreTag) continue;
                string schema = ReadString(data, ref p);
                if (schema != WrapStoreSchema) continue;
                if (p + 4 > data.Length) continue;

                int count = ReadInt32(data, ref p);
                if (count < 0) continue;
                long end = (long)p + (long)count * WrapVertStride;
                if (end > data.Length) continue;
                if (count == 0) return null;

                int[] verts = new int[count * 3];
                float[] proj = new float[count];
                int vi = 0;
                int o = p;
                for (int k = 0; k < count; k++)
                {
                    verts[vi++] = ReadInt32At(data, o + 4);
                    verts[vi++] = ReadInt32At(data, o + 8);
                    verts[vi++] = ReadInt32At(data, o + 12);
                    proj[k] = ReadSingleAt(data, o + 16);
                    o += WrapVertStride;
                }
                return Build(verts, vi, proj, count, gender);
            }
        }

        internal static int[][] TryReadWrapStoreTriples(byte[] data, int expectedCount)
        {
            if (data == null || expectedCount <= 0) return null;

            byte[] marker = Encoding.UTF8.GetBytes(WrapStoreTag);
            int pos = 0;
            while (true)
            {
                int at = IndexOfSection(data, marker, pos);
                if (at < 0) return null;
                pos = at + 1;

                int p = at;
                if (ReadString(data, ref p) != WrapStoreTag) continue;
                if (ReadString(data, ref p) != WrapStoreSchema) continue;
                if (p + 4 > data.Length) continue;

                int count = ReadInt32(data, ref p);
                if (count != expectedCount) continue;
                long end = (long)p + (long)count * WrapVertStride;
                if (end > data.Length) continue;

                int[][] map = new int[count][];
                int o = p;
                for (int k = 0; k < count; k++)
                {
                    map[k] = new[] { ReadInt32At(data, o + 4), ReadInt32At(data, o + 8), ReadInt32At(data, o + 12) };
                    o += WrapVertStride;
                }
                return map;
            }
        }

        internal static int IndexOfSection(byte[] data, byte[] marker, int from)
        {
            byte len = (byte)marker.Length;
            int last = data.Length - marker.Length - 1;
            for (int i = from; i <= last; i++)
            {
                if (data[i] != len) continue;
                int j = 0;
                while (j < marker.Length && data[i + 1 + j] == marker[j]) j++;
                if (j == marker.Length) return i;
            }
            return -1;
        }

        internal static string ReadString(byte[] data, ref int p)
        {
            int n = 0, shift = 0;
            while (true)
            {
                if (p >= data.Length) return null;
                byte b = data[p++];
                n |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 28) return null;
            }
            if (n < 0 || p + n > data.Length) return null;
            string s = Encoding.UTF8.GetString(data, p, n);
            p += n;
            return s;
        }

        internal static int ReadInt32(byte[] data, ref int p)
        {
            int v = ReadInt32At(data, p);
            p += 4;
            return v;
        }

        private static int ReadInt32At(byte[] d, int p)
        {
            return d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24);
        }

        private static float ReadSingleAt(byte[] d, int p)
        {
            return BitConverter.ToSingle(d, p);
        }

        internal static byte[] TryReadVabBytes(string itemUid, FileEntry entry)
        {
            FileEntry vab = ResolveVabEntry(itemUid, entry);
            byte[] bytes = vab != null ? ReadEntryBytes(vab) : null;
            if (bytes != null) return bytes;

            return TryReadVabFromPackage(itemUid, entry);
        }

        private static byte[] ReadEntryBytes(FileEntry file)
        {
            if (file == null) return null;
            try
            {
                using (var fs = file.OpenStream())
                {
                    if (fs == null || fs.Stream == null) return null;
                    long size = 0;
                    try { size = file.Size; } catch { size = 0; }
                    if (size <= 0) size = 1 << 20;
                    if (size > (32L << 20)) return null;

                    byte[] buf = new byte[size];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int got = fs.Stream.Read(buf, read, buf.Length - read);
                        if (got <= 0) break;
                        read += got;
                    }
                    if (read <= 0) return null;
                    if (read == buf.Length) return buf;
                    byte[] exact = new byte[read];
                    Buffer.BlockCopy(buf, 0, exact, 0, read);
                    return exact;
                }
            }
            catch { return null; }
        }

        private static byte[] TryReadVabFromPackage(string itemUid, FileEntry entry)
        {
            string uid = null;
            if (entry != null) { try { uid = entry.Uid; } catch { uid = null; } }
            if (string.IsNullOrEmpty(uid)) uid = itemUid;
            if (string.IsNullOrEmpty(uid)) return null;

            int colon = uid.IndexOf(":/", StringComparison.Ordinal);
            if (colon < 0) colon = uid.IndexOf(":\\", StringComparison.Ordinal);
            if (colon <= 0) return null;

            string pkgUid = uid.Substring(0, colon);
            string rawInternal = uid.Substring(colon + 2).Replace('\\', '/');
            if (string.IsNullOrEmpty(pkgUid) || string.IsNullOrEmpty(rawInternal)) return null;

            int lastSlash = pkgUid.LastIndexOfAny(new[] { '/', '\\' });
            if (lastSlash >= 0) pkgUid = pkgUid.Substring(lastSlash + 1);
            if (pkgUid.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                pkgUid = pkgUid.Substring(0, pkgUid.Length - 4);

            VarPackage pkg = ResolvePackage(pkgUid);
            if (pkg == null)
            {
                return null;
            }

            string internalPath = rawInternal.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)
                ? ResolvePresetItemInternalPath(pkg, rawInternal)
                : SwapExtensionToVab(rawInternal);
            if (string.IsNullOrEmpty(internalPath)) return null;

            VarFileEntry vabEntry = FindPackageEntry(pkg, internalPath);
            if (vabEntry != null)
            {
                byte[] bytes = ReadEntryBytes(vabEntry);
                if (bytes != null) return bytes;
            }

            return ReadFromVarOnDisk(pkg, internalPath);
        }

        private static byte[] ReadFromVarOnDisk(VarPackage pkg, string internalPath)
        {
            string varPath = null;
            try { varPath = pkg.Path; } catch { varPath = null; }
            if (string.IsNullOrEmpty(varPath) || !File.Exists(varPath)) return null;

            try
            {
                using (var fs = File.Open(varPath, FileMode.Open, FileAccess.Read,
                                          FileShare.Read | FileShare.Write | FileShare.Delete))
                using (var zf = new ZipFile(fs))
                {
                    ZipEntry ze = zf.GetEntry(internalPath);
                    if (ze == null || ze.Size <= 0 || ze.Size > (32L << 20)) return null;

                    byte[] buf = new byte[ze.Size];
                    using (var zs = zf.GetInputStream(ze))
                    {
                        int read = 0;
                        while (read < buf.Length)
                        {
                            int got = zs.Read(buf, read, buf.Length - read);
                            if (got <= 0) break;
                            read += got;
                        }
                        if (read < buf.Length) return null;
                    }
                    return buf;
                }
            }
            catch
            {
                return null;
            }
        }

        private static VarFileEntry FindPackageEntry(VarPackage pkg, string internalPath)
        {
            VarFileEntry vabEntry = null;
            try
            {
                VarFileEntry cached;
                if (pkg.TryCreateVarFileEntryFromCache(internalPath, out cached)) vabEntry = cached;
            }
            catch { }
            if (vabEntry != null) return vabEntry;

            try
            {
                List<VarFileEntry> entries = pkg.FileEntries;
                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        VarFileEntry e = entries[i];
                        if (e == null) continue;
                        string ip = e.InternalPath;
                        if (string.IsNullOrEmpty(ip)) continue;
                        if (string.Equals(ip.Replace('\\', '/'), internalPath, StringComparison.OrdinalIgnoreCase))
                            return e;
                    }
                }
            }
            catch { }

            try
            {
                if (pkg.ZipFile != null)
                {
                    ZipEntry ze = pkg.ZipFile.GetEntry(internalPath);
                    if (ze != null) return new VarFileEntry(pkg, ze.Name, ze.DateTime, ze.Size);
                }
            }
            catch { }

            return null;
        }

        private static VarPackage ResolvePackage(string pkgUid)
        {
            try
            {
                VarPackage pkg = FileManager.GetPackage(pkgUid, ensureInstalled: false);
                if (pkg != null) return pkg;

                pkg = FileManager.GetPackage("AddonPackages/" + pkgUid + ".var", false);
                if (pkg != null) return pkg;

                pkg = FileManager.GetPackage("AllPackages/" + pkgUid + ".var", false);
                if (pkg != null) return pkg;

                string latest = LatestUidFor(pkgUid);
                if (latest != null) return FileManager.GetPackage(latest, false);

                return null;
            }
            catch { return null; }
        }

        private static string LatestUidFor(string pkgUid)
        {
            if (string.IsNullOrEmpty(pkgUid)) return null;

            int dot = pkgUid.LastIndexOf('.');
            if (dot <= 0 || dot == pkgUid.Length - 1) return null;

            for (int i = dot + 1; i < pkgUid.Length; i++)
                if (pkgUid[i] < '0' || pkgUid[i] > '9') return null;

            return pkgUid.Substring(0, dot) + ".latest";
        }

        private static FileEntry ResolveVabEntry(string itemUid, FileEntry entry)
        {
            string path = null;
            if (entry != null) { try { path = entry.Uid; } catch { path = null; } }
            if (string.IsNullOrEmpty(path)) path = itemUid;
            if (string.IsNullOrEmpty(path)) return null;

            string vabUid = SwapExtensionToVab(path);
            if (string.IsNullOrEmpty(vabUid)) return null;

            try
            {
                FileEntry e = FileManager.GetVarFileEntry(vabUid);
                if (e != null) return e;
            }
            catch { }
            try
            {
                FileEntry e = FileManager.GetFileEntry(vabUid);
                if (e != null) return e;
            }
            catch { }
            return null;
        }

        private static string SwapExtensionToVab(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            int dot = uid.LastIndexOf('.');
            int slash = uid.LastIndexOfAny(new[] { '/', '\\' });
            if (dot > slash && dot >= 0)
            {
                string ext = uid.Substring(dot + 1);
                if (ext.Equals("vam", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals("vaj", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals("vab", StringComparison.OrdinalIgnoreCase))
                    return uid.Substring(0, dot) + ".vab";

                return null;
            }
            return uid + ".vab";
        }

        private static string ResolvePresetItemInternalPath(VarPackage pkg, string vapInternalPath)
        {
            if (pkg == null || string.IsNullOrEmpty(vapInternalPath)) return null;

            int slash = vapInternalPath.LastIndexOf('/');
            if (slash <= 0) return null;
            string presetBase = vapInternalPath.Substring(slash + 1);
            int dot = presetBase.LastIndexOf('.');
            if (dot > 0) presetBase = presetBase.Substring(0, dot);
            if (presetBase.Length == 0) return null;

            string bestPath = null;
            int bestLen = -1;
            bool bestIsSibling = false;
            string dir = vapInternalPath.Substring(0, slash + 1);

            try
            {
                List<VarFileEntry> entries = pkg.FileEntries;
                if (entries == null) return null;

                for (int i = 0; i < entries.Count; i++)
                {
                    VarFileEntry e = entries[i];
                    if (e == null) continue;

                    string ip = e.InternalPath;
                    if (string.IsNullOrEmpty(ip)) continue;
                    ip = ip.Replace('\\', '/');
                    if (!ip.EndsWith(".vam", StringComparison.OrdinalIgnoreCase)) continue;

                    int s = ip.LastIndexOf('/');
                    string itemBase = ip.Substring(s + 1, ip.Length - s - 5);
                    if (itemBase.Length == 0) continue;
                    if (!presetBase.StartsWith(itemBase, StringComparison.OrdinalIgnoreCase)) continue;

                    bool sibling = string.Equals(ip.Substring(0, s + 1), dir, StringComparison.OrdinalIgnoreCase);

                    if (itemBase.Length > bestLen || (itemBase.Length == bestLen && sibling && !bestIsSibling))
                    {
                        bestLen = itemBase.Length;
                        bestIsSibling = sibling;
                        bestPath = ip.Substring(0, ip.Length - 4) + ".vab";
                    }
                }
            }
            catch { return null; }

            return bestPath;
        }

        internal static int GenderFromPath(string pathOrUid)
        {
            if (string.IsNullOrEmpty(pathOrUid)) return VpbSkinRegionMap.GenderFemale;
            if (pathOrUid.IndexOf("/Male/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathOrUid.IndexOf("\\Male\\", StringComparison.OrdinalIgnoreCase) >= 0)
                return VpbSkinRegionMap.GenderMale;
            return VpbSkinRegionMap.GenderFemale;
        }
    }
}
