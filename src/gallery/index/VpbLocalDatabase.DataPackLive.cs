using System;
using System.Collections.Generic;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        internal sealed class HubLiveEntry
        {
            internal long EntryId;
            internal string SrcId = "";
            internal string Title = "";
            internal string Creator = "";
            internal string Category = "";
            internal string PayType = "";
            internal string ResourceId = "";
            internal string FirstRelease = "";
            internal string LastUpdate = "";
            internal string TagLine = "";
            internal string Version = "";
            internal string License = "";
            internal string MinVam = "";
            internal string RatingAvg = "0";
            internal long Downloads;
            internal long RatingCount;
            internal long DepCount;
            internal long SizeKb;
            internal long Flags;
            internal long LastUpdateUnix;
            internal readonly List<string> VarKeys = new List<string>(2);
            internal readonly List<string> VarVersions = new List<string>(2);
            internal readonly List<string> TagNamespaces = new List<string>(8);
            internal readonly List<string> TagTexts = new List<string>(8);
            internal readonly List<string> IdentKeys = new List<string>(6);
        }

        internal const string HubLivePackId = "hublive";

        internal static bool TryApplyHubLiveEntries(
            List<HubLiveEntry> entries, long watermarkUnix, out int applied, out int links, out string error)
        {
            applied = 0;
            links = 0;
            error = null;
            if (entries == null || entries.Count == 0)
            {
                error = "no entries";
                return false;
            }
            if (!VpbSqlite3.IsAvailable)
            {
                error = "sqlite unavailable";
                return false;
            }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureDataPackSchema(conn);

                    try
                    {
                        conn.ExecUtf8("BEGIN IMMEDIATE;");

                        using (var delShipped = conn.Prepare(
                            "DELETE FROM datapack_entry WHERE pack_id=? AND entry_id=?"))
                        using (var delShippedTag = conn.Prepare(
                            "DELETE FROM datapack_tag WHERE pack_id=? AND entry_id=?"))
                        using (var delShippedVar = conn.Prepare(
                            "DELETE FROM datapack_var WHERE pack_id=? AND entry_id=?"))
                        using (var delShippedIdent = conn.Prepare(
                            "DELETE FROM datapack_ident WHERE pack_id=? AND entry_id=?"))
                        using (var delShippedLink = conn.Prepare(
                            "DELETE FROM datapack_link WHERE pack_id=? AND entry_id=?"))
                        using (var insEntry = conn.Prepare(
                            "INSERT OR REPLACE INTO datapack_entry(pack_id,entry_id,src_id,subject,title,creator," +
                            "category,pay_type,resource_id,link,first_release,last_update," +
                            "tag_line,version,license,min_vam,rating_avg,downloads,rating_count,dep_count,size_kb,flags) " +
                            "VALUES(?,?,?,'',?,?,?,?,?,'',?,?,?,?,?,?,?,?,?,?,?,?)"))
                        using (var insTag = conn.Prepare(
                            "INSERT OR IGNORE INTO datapack_tag(pack_id,entry_id,ns,tag) VALUES(?,?,?,?)"))
                        using (var insVar = conn.Prepare(
                            "INSERT OR IGNORE INTO datapack_var(pack_id,entry_id,var_key,alias_key,var_version,exact) " +
                            "VALUES(?,?,?,?,?,1)"))
                        using (var insIdent = conn.Prepare(
                            "INSERT OR IGNORE INTO datapack_ident(pack_id,entry_id,ident_key) VALUES(?,?,?)"))
                        {
                            for (int i = 0; i < entries.Count; i++)
                            {
                                HubLiveEntry e = entries[i];
                                if (e == null || e.EntryId <= 0) continue;

                                DeleteDataPackEntryRows(delShipped, VpbDataPackService.HubTagsPackId, e.EntryId);
                                DeleteDataPackEntryRows(delShippedTag, VpbDataPackService.HubTagsPackId, e.EntryId);
                                DeleteDataPackEntryRows(delShippedVar, VpbDataPackService.HubTagsPackId, e.EntryId);
                                DeleteDataPackEntryRows(delShippedIdent, VpbDataPackService.HubTagsPackId, e.EntryId);
                                DeleteDataPackEntryRows(delShippedLink, VpbDataPackService.HubTagsPackId, e.EntryId);

                                DeleteDataPackEntryRows(delShippedTag, HubLivePackId, e.EntryId);
                                DeleteDataPackEntryRows(delShippedVar, HubLivePackId, e.EntryId);
                                DeleteDataPackEntryRows(delShippedIdent, HubLivePackId, e.EntryId);

                                insEntry.BindText(1, HubLivePackId);
                                insEntry.BindInt64(2, e.EntryId);
                                insEntry.BindText(3, e.SrcId);
                                insEntry.BindText(4, e.Title);
                                insEntry.BindText(5, e.Creator);
                                insEntry.BindText(6, e.Category);
                                insEntry.BindText(7, e.PayType);
                                insEntry.BindText(8, e.ResourceId);
                                insEntry.BindText(9, e.FirstRelease);
                                insEntry.BindText(10, e.LastUpdate);
                                insEntry.BindText(11, e.TagLine);
                                insEntry.BindText(12, e.Version);
                                insEntry.BindText(13, e.License);
                                insEntry.BindText(14, e.MinVam);
                                insEntry.BindText(15, e.RatingAvg);
                                insEntry.BindInt64(16, e.Downloads);
                                insEntry.BindInt64(17, e.RatingCount);
                                insEntry.BindInt64(18, e.DepCount);
                                insEntry.BindInt64(19, e.SizeKb);
                                insEntry.BindInt64(20, e.Flags);
                                insEntry.Step();
                                insEntry.Reset();
                                applied++;

                                for (int t = 0; t < e.TagTexts.Count; t++)
                                {
                                    insTag.BindText(1, HubLivePackId);
                                    insTag.BindInt64(2, e.EntryId);
                                    insTag.BindText(3, e.TagNamespaces[t]);
                                    insTag.BindText(4, e.TagTexts[t]);
                                    insTag.Step();
                                    insTag.Reset();
                                }

                                for (int v = 0; v < e.VarKeys.Count; v++)
                                {
                                    insVar.BindText(1, HubLivePackId);
                                    insVar.BindInt64(2, e.EntryId);
                                    insVar.BindText(3, e.VarKeys[v]);
                                    insVar.BindText(4, VpbDataPackReader.AliasForVarKey(e.VarKeys[v]));
                                    insVar.BindText(5, e.VarVersions[v]);
                                    insVar.Step();
                                    insVar.Reset();
                                }

                                for (int k = 0; k < e.IdentKeys.Count; k++)
                                {
                                    insIdent.BindText(1, HubLivePackId);
                                    insIdent.BindInt64(2, e.EntryId);
                                    insIdent.BindText(3, e.IdentKeys[k]);
                                    insIdent.Step();
                                    insIdent.Reset();
                                }
                            }
                        }

                        int total = (int)ScalarInt64(conn,
                            "SELECT COUNT(*) FROM datapack_entry WHERE pack_id='" + HubLivePackId + "';");
                        WriteHubLivePackRow(conn, total, watermarkUnix);

                        RefreshPkgUidAliases(conn, true);
                        links = RebuildDataPackLinks(conn, HubLivePackId);
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }

                SetDataPackIndexReady(true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try { LogUtil.LogWarning("[VPB.DB] hub live apply failed: " + ex.Message); } catch { }
                return false;
            }
        }

        static void DeleteDataPackEntryRows(VpbSqlite3.Statement st, string packId, long entryId)
        {
            st.BindText(1, packId);
            st.BindInt64(2, entryId);
            st.Step();
            st.Reset();
        }

        static void WriteHubLivePackRow(VpbSqlite3.Connection conn, int entryCount, long watermarkUnix)
        {
            using (var ins = conn.Prepare(
                "INSERT OR REPLACE INTO datapack(pack_id,pack_version,built_date,content_hash,source_url," +
                "attribution,entry_count,applied_utc,sync_watermark,sync_utc) VALUES(?,?,?,'',?,?,?,?,?,?)"))
            {
                ins.BindText(1, HubLivePackId);
                ins.BindText(2, "live");
                ins.BindText(3, DateTime.UtcNow.ToString("yyyy-MM-dd"));
                ins.BindText(4, "https://hub.virtamate.com/");
                ins.BindText(5, "VaM Hub");
                ins.BindInt64(6, entryCount);
                ins.BindInt64(7, DateTime.UtcNow.ToBinary());
                ins.BindText(8, watermarkUnix.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ins.BindInt64(9, UnixNowSeconds());
                ins.Step();
            }
        }

        internal static long UnixNowSeconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        internal struct HubLiveSyncState
        {
            public bool Installed;
            public int EntryCount;
            public long WatermarkUnix;
            public long LastSyncUnix;
        }

        internal static bool TryGetHubLiveSyncState(out HubLiveSyncState state)
        {
            state = new HubLiveSyncState();
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn)) return true;
                    if (!DataPackSyncColumnsPresent(conn)) return true;
                    using (var st = conn.Prepare(
                        "SELECT entry_count, ifnull(sync_watermark,''), ifnull(sync_utc,0) " +
                        "FROM datapack WHERE pack_id=?"))
                    {
                        st.BindText(1, HubLivePackId);
                        if (st.Step() != VpbSqlite3.SqliteRow) return true;
                        state.Installed = true;
                        state.EntryCount = (int)st.ColumnInt64(0);
                        long wm;
                        long.TryParse(st.ColumnText(1) ?? "", out wm);
                        state.WatermarkUnix = wm;
                        state.LastSyncUnix = st.ColumnInt64(2);
                    }
                }
                return true;
            }
            catch { return false; }
        }

        static bool DataPackSyncColumnsPresent(VpbSqlite3.Connection conn)
        {
            try
            {
                using (var st = conn.Prepare("SELECT sync_watermark FROM datapack LIMIT 1"))
                {
                    st.Step();
                    return true;
                }
            }
            catch { return false; }
        }

        internal static long ComputeHubLiveSeedWatermark()
        {
            if (!VpbSqlite3.IsAvailable) return 0;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn)) return 0;
                    string maxIso = null;
                    using (var st = conn.Prepare(
                        "SELECT MAX(ifnull(last_update,'')) FROM datapack_entry WHERE pack_id=?"))
                    {
                        st.BindText(1, VpbDataPackService.HubTagsPackId);
                        if (st.Step() == VpbSqlite3.SqliteRow) maxIso = st.ColumnText(0);
                    }
                    int ymd = ParseYmd(maxIso ?? "");
                    if (ymd <= 0) return 0;
                    var dt = new DateTime(ymd / 10000, (ymd / 100) % 100, ymd % 100, 0, 0, 0, DateTimeKind.Utc);
                    dt = dt.AddDays(-1);
                    return (long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                }
            }
            catch { return 0; }
        }

        internal static bool TryRemoveHubLivePack()
        {
            return TryRemoveDataPack(HubLivePackId);
        }
    }
}
