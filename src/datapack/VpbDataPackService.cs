using System;
using System.Threading;

namespace VPB
{
    internal static class VpbDataPackService
    {
        internal const string LookapediaPackId = "lookapedia";
        internal const string HubTagsPackId = "hubtags";
        internal const string HubLivePackId = "hublive";

        internal const int PackIndexLookapedia = 0;
        internal const int PackIndexHubTags = 1;
        internal const int PackCount = 2;

        static readonly string[] s_PackIds = { LookapediaPackId, HubTagsPackId };
        static readonly string[] s_PackPaths =
        {
            "assets/datapacks/lookapedia.tsv",
            "assets/datapacks/hubtags.tsv",
        };

        static readonly object s_Sync = new object();
        static bool s_Running;
        static bool s_Pending;
        static bool s_InitialSyncRequested;
        static readonly bool[] s_PendingEnabled = new bool[PackCount];
        static readonly bool[] s_LoggedMissingPackFile = new bool[PackCount];
        static readonly bool[] s_DesiredEnabled = new bool[PackCount];

        static readonly string[] s_StatusLines = { "", "" };
        static int s_StatusRevision;

        internal static int StatusRevision { get { return Thread.VolatileRead(ref s_StatusRevision); } }

        internal static string StatusLineFor(int packIndex)
        {
            if (packIndex < 0 || packIndex >= PackCount) return "";
            string s;
            lock (s_Sync) { s = s_StatusLines[packIndex]; }
            return s ?? "";
        }

        internal static void BumpStatusRevision()
        {
            Interlocked.Increment(ref s_StatusRevision);
        }

        static void SetStatus(int packIndex, string line)
        {
            lock (s_Sync) { s_StatusLines[packIndex] = line ?? ""; }
            Interlocked.Increment(ref s_StatusRevision);
        }

        internal static string ResolvePackPath(int packIndex)
        {
            if (packIndex < 0 || packIndex >= PackCount) return null;
            try { return VpbPaths.FindFile(s_PackPaths[packIndex]); }
            catch { return null; }
        }

        internal static void RequestInitialSync(bool lookapediaEnabled, bool hubTagsEnabled)
        {
            lock (s_Sync)
            {
                if (s_InitialSyncRequested) return;
                s_InitialSyncRequested = true;
            }
            RequestSync(lookapediaEnabled, hubTagsEnabled);
        }

        internal static void RequestSync(bool lookapediaEnabled, bool hubTagsEnabled)
        {
            lock (s_Sync)
            {
                s_PendingEnabled[PackIndexLookapedia] = lookapediaEnabled;
                s_PendingEnabled[PackIndexHubTags] = hubTagsEnabled;
                if (s_Running)
                {
                    s_Pending = true;
                    return;
                }
                s_Running = true;
            }

            bool queued = false;
            try { queued = ThreadPool.QueueUserWorkItem(SyncWorker); }
            catch { queued = false; }
            if (!queued)
            {
                lock (s_Sync) { s_Running = false; s_Pending = false; }
            }
        }

        static void SyncWorker(object state)
        {
            for (; ; )
            {
                lock (s_Sync)
                {
                    for (int i = 0; i < PackCount; i++) s_DesiredEnabled[i] = s_PendingEnabled[i];
                }

                for (int i = 0; i < PackCount; i++)
                {
                    try { SyncOnce(i, s_DesiredEnabled[i]); }
                    catch (Exception ex)
                    {
                        SetStatus(i, "Data pack sync failed");
                        try { LogUtil.LogWarning("[VPB] Data pack '" + s_PackIds[i] + "' sync failed: " + ex.Message); }
                        catch { }
                    }
                }

                try { VpbLocalDatabase.RefreshDataPackIndexReady(); } catch { }

                lock (s_Sync)
                {
                    if (!s_Pending)
                    {
                        s_Running = false;
                        return;
                    }
                    s_Pending = false;
                }
            }
        }

        static void SyncOnce(int packIndex, bool enabled)
        {
            string packId = s_PackIds[packIndex];

            if (!VpbSqlite3.IsAvailable)
            {
                SetStatus(packIndex, "Unavailable (no SQLite)");
                return;
            }

            if (!enabled)
            {
                VpbLocalDatabase.TryRemoveDataPack(packId);
                SetStatus(packIndex, "Off");
                return;
            }

            string path = ResolvePackPath(packIndex);
            VpbDataPackHeader shipped;
            if (!VpbDataPackReader.TryReadHeader(path, out shipped))
            {
                if (!s_LoggedMissingPackFile[packIndex])
                {
                    s_LoggedMissingPackFile[packIndex] = true;
                    try
                    {
                        LogUtil.LogWarning("[VPB] Data pack file is missing or unreadable: "
                            + (path ?? s_PackPaths[packIndex])
                            + " (is it listed in patch_manifest.json?)");
                    }
                    catch { }
                }
                SetStatus(packIndex, "Pack file missing");
                return;
            }

            SetStatus(packIndex, "Updating...");

            VpbLocalDatabase.DataPackStatus installed;
            bool haveStatus = VpbLocalDatabase.TryGetDataPackStatus(packId, out installed);

            bool upToDate = haveStatus
                && installed.Installed
                && !string.IsNullOrEmpty(shipped.ContentHash)
                && string.Equals(installed.ContentHash, shipped.ContentHash, StringComparison.Ordinal);

            if (upToDate)
            {
                int relinked;
                VpbLocalDatabase.TryRelinkDataPack(packId, out relinked);
            }
            else
            {
                int entries, links;
                string error;
                if (!VpbLocalDatabase.TryApplyDataPackFile(
                        packId, path, out entries, out links, out error))
                {
                    SetStatus(packIndex, "Import failed");
                    return;
                }
            }

            VpbLocalDatabase.DataPackStatus after;
            if (VpbLocalDatabase.TryGetDataPackStatus(packId, out after) && after.Installed)
                SetStatus(packIndex, BuildStatusLine(after));
            else
                SetStatus(packIndex, "On");
        }

        static string BuildStatusLine(VpbLocalDatabase.DataPackStatus s)
        {
            string version = string.IsNullOrEmpty(s.PackVersion) ? "?" : s.PackVersion;
            return version
                + " · " + s.EntryCount + " entries"
                + " · " + s.LinkedPackages + " matched in your library";
        }
    }
}
