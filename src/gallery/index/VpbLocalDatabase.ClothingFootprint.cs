using System;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        private const byte ClothingFootprintBlobVersion = 2;
        private const int ClothingFootprintHeaderBytes = 16;

        internal static void EnsureClothingFootprintSchema(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS clothing_footprint (item_uid TEXT PRIMARY KEY, payload BLOB NOT NULL);" +
                "CREATE TABLE IF NOT EXISTS skin_region_map (gender INTEGER PRIMARY KEY, regions BLOB NOT NULL);" +
                "CREATE TABLE IF NOT EXISTS skin_area_map (gender INTEGER PRIMARY KEY, cells BLOB NOT NULL);" +
                "CREATE TABLE IF NOT EXISTS scalp_projection (scalp_key TEXT PRIMARY KEY, verts BLOB NOT NULL);");
        }

        internal static byte[] TryLoadSkinRegionMap(int gender)
        {
            if (!VpbSqlite3.IsAvailable) return null;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureClothingFootprintSchema(conn);
                    using (var st = conn.Prepare("SELECT regions FROM skin_region_map WHERE gender=?"))
                    {
                        st.BindInt64(1, gender);
                        if (st.Step() != VpbSqlite3.SqliteRow) return null;
                        byte[] blob = st.ColumnBlob(0);
                        return (blob != null && blob.Length > 0) ? blob : null;
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] skin_region_map read failed: " + ex.Message); } catch { }
                return null;
            }
        }

        internal static bool TrySaveSkinRegionMap(int gender, byte[] regions)
        {
            if (!VpbSqlite3.IsAvailable || regions == null || regions.Length == 0) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureClothingFootprintSchema(conn);
                    using (var st = conn.Prepare("INSERT OR REPLACE INTO skin_region_map(gender, regions) VALUES(?,?)"))
                    {
                        st.BindInt64(1, gender);
                        st.BindBlob(2, regions);
                        st.Step();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] skin_region_map write failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static int[] TryLoadSkinAreaMap(int gender)
        {
            if (!VpbSqlite3.IsAvailable) return null;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureClothingFootprintSchema(conn);
                    using (var st = conn.Prepare("SELECT cells FROM skin_area_map WHERE gender=?"))
                    {
                        st.BindInt64(1, gender);
                        if (st.Step() != VpbSqlite3.SqliteRow) return null;
                        byte[] blob = st.ColumnBlob(0);
                        if (blob == null || blob.Length < 4 || (blob.Length & 3) != 0) return null;

                        int[] cells = new int[blob.Length / 4];
                        for (int i = 0; i < cells.Length; i++) cells[i] = ReadInt32(blob, i * 4);
                        return cells;
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] skin_area_map read failed: " + ex.Message); } catch { }
                return null;
            }
        }

        internal static bool TrySaveSkinAreaMap(int gender, int[] cells)
        {
            if (!VpbSqlite3.IsAvailable || cells == null || cells.Length == 0) return false;
            try
            {
                byte[] blob = new byte[cells.Length * 4];
                for (int i = 0; i < cells.Length; i++) WriteInt32(blob, i * 4, cells[i]);

                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureClothingFootprintSchema(conn);
                    using (var st = conn.Prepare("INSERT OR REPLACE INTO skin_area_map(gender, cells) VALUES(?,?)"))
                    {
                        st.BindInt64(1, gender);
                        st.BindBlob(2, blob);
                        st.Step();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] skin_area_map write failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static int[][] TryLoadScalpProjection(string scalpKey)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(scalpKey)) return null;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureClothingFootprintSchema(conn);
                    using (var st = conn.Prepare("SELECT verts FROM scalp_projection WHERE scalp_key=?"))
                    {
                        st.BindText(1, scalpKey);
                        if (st.Step() != VpbSqlite3.SqliteRow) return null;
                        byte[] blob = st.ColumnBlob(0);
                        if (blob == null || blob.Length < 12 || blob.Length % 12 != 0) return null;

                        int n = blob.Length / 12;
                        int[][] map = new int[n][];
                        for (int i = 0; i < n; i++)
                        {
                            int o = i * 12;
                            map[i] = new[] { ReadInt32(blob, o), ReadInt32(blob, o + 4), ReadInt32(blob, o + 8) };
                        }
                        return map;
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] scalp_projection read failed: " + ex.Message); } catch { }
                return null;
            }
        }

        internal static bool TrySaveScalpProjection(string scalpKey, int[][] projection)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(scalpKey)) return false;
            if (projection == null || projection.Length == 0) return false;
            try
            {
                byte[] blob = new byte[projection.Length * 12];
                for (int i = 0; i < projection.Length; i++)
                {
                    int[] tri = projection[i];
                    int o = i * 12;
                    if (tri == null || tri.Length < 3) { WriteInt32(blob, o, -1); WriteInt32(blob, o + 4, -1); WriteInt32(blob, o + 8, -1); continue; }
                    WriteInt32(blob, o, tri[0]);
                    WriteInt32(blob, o + 4, tri[1]);
                    WriteInt32(blob, o + 8, tri[2]);
                }

                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureClothingFootprintSchema(conn);
                    using (var st = conn.Prepare("INSERT OR REPLACE INTO scalp_projection(scalp_key, verts) VALUES(?,?)"))
                    {
                        st.BindText(1, scalpKey);
                        st.BindBlob(2, blob);
                        st.Step();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] scalp_projection write failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static VpbClothingFootprint TryLoadClothingFootprint(string itemUid)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(itemUid)) return null;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureClothingFootprintSchema(conn);
                    using (var st = conn.Prepare("SELECT payload FROM clothing_footprint WHERE item_uid=?"))
                    {
                        st.BindText(1, itemUid);
                        if (st.Step() != VpbSqlite3.SqliteRow) return null;
                        return DecodeFootprint(st.ColumnBlob(0));
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] clothing_footprint read failed: " + ex.Message); } catch { }
                return null;
            }
        }

        internal static bool TrySaveClothingFootprint(string itemUid, VpbClothingFootprint fp)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(itemUid) || fp == null || !fp.IsUsable) return false;
            try
            {
                byte[] payload = EncodeFootprint(fp);
                if (payload == null) return false;

                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureClothingFootprintSchema(conn);
                    using (var st = conn.Prepare("INSERT OR REPLACE INTO clothing_footprint(item_uid, payload) VALUES(?,?)"))
                    {
                        st.BindText(1, itemUid);
                        st.BindBlob(2, payload);
                        st.Step();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] clothing_footprint write failed: " + ex.Message); } catch { }
                return false;
            }
        }

        private static byte[] EncodeFootprint(VpbClothingFootprint fp)
        {
            ulong[] bits = fp.Bits;
            if (bits == null) return null;

            byte[] space = System.Text.Encoding.UTF8.GetBytes(fp.Space ?? "");
            if (space.Length > ushort.MaxValue) return null;

            byte[] blob = new byte[ClothingFootprintHeaderBytes + space.Length + bits.Length * 8];
            blob[0] = ClothingFootprintBlobVersion;
            blob[1] = (byte)fp.Gender;
            blob[2] = (byte)space.Length;
            blob[3] = (byte)(space.Length >> 8);
            WriteInt32(blob, 4, fp.VertCount);
            WriteInt32(blob, 8, unchecked((int)fp.RegionMask));
            WriteInt32(blob, 12, (int)Math.Round(fp.Standoff * 1000000f));
            Buffer.BlockCopy(space, 0, blob, ClothingFootprintHeaderBytes, space.Length);

            int o = ClothingFootprintHeaderBytes + space.Length;
            for (int i = 0; i < bits.Length; i++)
            {
                ulong w = bits[i];
                for (int b = 0; b < 8; b++) blob[o + b] = (byte)(w >> (b * 8));
                o += 8;
            }
            return blob;
        }

        private static VpbClothingFootprint DecodeFootprint(byte[] blob)
        {
            if (blob == null || blob.Length < ClothingFootprintHeaderBytes) return null;
            if (blob[0] != ClothingFootprintBlobVersion) return null;

            int spaceLen = blob[2] | (blob[3] << 8);
            int bitBytes = blob.Length - ClothingFootprintHeaderBytes - spaceLen;
            if (bitBytes <= 0 || (bitBytes & 7) != 0) return null;

            var fp = new VpbClothingFootprint
            {
                Gender = blob[1],
                VertCount = ReadInt32(blob, 4),
                RegionMask = unchecked((uint)ReadInt32(blob, 8)),
                Standoff = ReadInt32(blob, 12) / 1000000f,
                Space = spaceLen > 0
                    ? System.Text.Encoding.UTF8.GetString(blob, ClothingFootprintHeaderBytes, spaceLen)
                    : "",
                Bits = new ulong[bitBytes / 8],
            };

            int o = ClothingFootprintHeaderBytes + spaceLen;
            for (int i = 0; i < fp.Bits.Length; i++)
            {
                ulong w = 0UL;
                for (int b = 0; b < 8; b++) w |= (ulong)blob[o + b] << (b * 8);
                fp.Bits[i] = w;
                o += 8;
            }

            return fp.VertCount > 0 ? fp : null;
        }

        private static void WriteInt32(byte[] b, int o, int v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }

        private static int ReadInt32(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
        }
    }
}
