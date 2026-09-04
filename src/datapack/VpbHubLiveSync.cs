using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;

namespace VPB
{
    internal static class VpbHubLiveSync
    {
        const string HubApiUrl = "https://hub.virtamate.com/citizenx/api.php";
        const string CrawlSort = "Latest Update";
        const int PageSize = 500;
        const int MaxPages = 20;
        const int RequestTimeoutSec = 60;
        const float PageGapSeconds = 1f;
        const int NoSeedFallbackDays = 30;
        internal const int AutoSyncIntervalHours = 24;

        static readonly object s_Sync = new object();
        static bool s_Running;
        static string s_StatusLine = "";
        static int s_StatusRevision;
        static bool s_AutoAttempted;

        internal static bool IsRunning { get { lock (s_Sync) { return s_Running; } } }
        internal static int StatusRevision { get { return Thread.VolatileRead(ref s_StatusRevision); } }

        internal static string StatusLine
        {
            get { lock (s_Sync) { return s_StatusLine ?? ""; } }
        }

        static void SetStatus(string line)
        {
            lock (s_Sync) { s_StatusLine = line ?? ""; }
            Interlocked.Increment(ref s_StatusRevision);
        }

        internal static void ResetAutoAttempt()
        {
            lock (s_Sync) { s_AutoAttempted = false; }
        }

        internal static bool LiveSyncConfigured()
        {
            try
            {
                var cfg = VPBConfig.Instance;
                return cfg != null && cfg.DataPackHubTagsEnabled && cfg.DataPackHubLiveSyncEnabled;
            }
            catch { return false; }
        }

        internal static void RequestAutoSync(MonoBehaviour host)
        {
            if (host == null || !LiveSyncConfigured()) return;
            lock (s_Sync)
            {
                if (s_AutoAttempted || s_Running) return;
                s_AutoAttempted = true;
            }

            VpbLocalDatabase.HubLiveSyncState state;
            if (VpbLocalDatabase.TryGetHubLiveSyncState(out state) && state.LastSyncUnix > 0)
            {
                long age = VpbLocalDatabase.UnixNowSeconds() - state.LastSyncUnix;
                if (age >= 0 && age < AutoSyncIntervalHours * 3600L) return;
            }
            TryStart(host, false);
        }

        internal static bool TryStart(MonoBehaviour host, bool manual)
        {
            if (host == null) return false;
            if (!LiveSyncConfigured())
            {
                if (manual) SetStatus("Turn the Hub data pack and live update on first");
                return false;
            }
            if (!HubReachable())
            {
                if (manual) SetStatus("VaM's Hub is disabled");
                return false;
            }

            lock (s_Sync)
            {
                if (s_Running) return false;
                s_Running = true;
            }
            SetStatus("Contacting the Hub...");
            try
            {
                host.StartCoroutine(SyncCoroutine());
                return true;
            }
            catch (Exception ex)
            {
                lock (s_Sync) { s_Running = false; }
                SetStatus("Could not start");
                try { LogUtil.LogWarning("[VPB] hub live sync start failed: " + ex.Message); } catch { }
                return false;
            }
        }

        static bool HubReachable()
        {
            try
            {
                HubBrowse hub = HubBrowse.singleton;
                return hub != null && hub.HubEnabled;
            }
            catch { return false; }
        }

        sealed class PageJob
        {
            internal byte[] Payload;
            internal long WatermarkUnix;
            internal volatile bool Done;
            internal List<VpbLocalDatabase.HubLiveEntry> Entries;
            internal bool ReachedWatermark;
            internal long MaxUpdateUnix;
            internal int TotalPages;
            internal string Error;
        }

        sealed class ApplyJob
        {
            internal List<VpbLocalDatabase.HubLiveEntry> Entries;
            internal long WatermarkUnix;
            internal volatile bool Done;
            internal bool Ok;
            internal int Applied;
            internal int Links;
            internal string Error;
        }

        static IEnumerator SyncCoroutine()
        {
            long watermark = 0;
            var collected = new List<VpbLocalDatabase.HubLiveEntry>(512);
            var seen = new HashSet<long>();
            bool caughtUp = false;
            long newestSeen = 0;
            int pagesRead = 0;
            string failure = null;

            VpbLocalDatabase.HubLiveSyncState state;
            if (VpbLocalDatabase.TryGetHubLiveSyncState(out state) && state.Installed && state.WatermarkUnix > 0)
                watermark = state.WatermarkUnix;
            else
                watermark = VpbLocalDatabase.ComputeHubLiveSeedWatermark();
            if (watermark <= 0)
                watermark = VpbLocalDatabase.UnixNowSeconds() - NoSeedFallbackDays * 86400L;

            for (int page = 1; page <= MaxPages && !caughtUp; page++)
            {
                SetStatus(page == 1 ? "Contacting the Hub..." : ("Reading page " + page + "..."));

                byte[] payload = null;
                string requestError = null;
                IEnumerator req = PostPage(page, r => payload = r, e => requestError = e);
                while (req.MoveNext()) yield return req.Current;

                if (requestError != null || payload == null || payload.Length == 0)
                {
                    failure = requestError ?? "empty response";
                    break;
                }

                var job = new PageJob { Payload = payload, WatermarkUnix = watermark };
                bool queued = false;
                try { queued = ThreadPool.QueueUserWorkItem(ParsePageWorker, job); }
                catch { queued = false; }
                if (!queued)
                {
                    failure = "could not queue parse";
                    break;
                }
                while (!job.Done) yield return null;

                if (job.Error != null)
                {
                    failure = job.Error;
                    break;
                }

                pagesRead++;
                if (job.MaxUpdateUnix > newestSeen) newestSeen = job.MaxUpdateUnix;
                if (job.Entries != null)
                {
                    for (int i = 0; i < job.Entries.Count; i++)
                    {
                        VpbLocalDatabase.HubLiveEntry e = job.Entries[i];
                        if (e == null || !seen.Add(e.EntryId)) continue;
                        collected.Add(e);
                    }
                }
                if (job.ReachedWatermark) caughtUp = true;
                if (job.TotalPages > 0 && page >= job.TotalPages) caughtUp = true;
                if (caughtUp) break;

                yield return new WaitForSeconds(PageGapSeconds);
            }

            if (failure != null)
            {
                Finish("Update failed: " + failure, bumpPackRevision: false);
                yield break;
            }
            if (collected.Count == 0)
            {
                TouchSyncStamp(watermark);
                Finish("Up to date", bumpPackRevision: false);
                yield break;
            }

            SetStatus("Applying " + collected.Count + " updates...");
            long newWatermark = caughtUp && newestSeen > 0 ? newestSeen : watermark;

            var apply = new ApplyJob { Entries = collected, WatermarkUnix = newWatermark };
            bool applyQueued = false;
            try { applyQueued = ThreadPool.QueueUserWorkItem(ApplyWorker, apply); }
            catch { applyQueued = false; }
            if (!applyQueued)
            {
                Finish("Update failed: could not queue apply", bumpPackRevision: false);
                yield break;
            }
            while (!apply.Done) yield return null;

            if (!apply.Ok)
            {
                Finish("Update failed: " + (apply.Error ?? "apply error"), bumpPackRevision: false);
                yield break;
            }

            string tail = caughtUp ? "" : " (more remain — run again)";
            Finish(apply.Applied + " resources updated · " + apply.Links + " linked" + tail, bumpPackRevision: true);
            try
            {
                LogUtil.Log("[VPB] hub live sync: pages=" + pagesRead
                    + " entries=" + apply.Applied + " links=" + apply.Links
                    + " watermark=" + newWatermark + " caughtUp=" + caughtUp);
            }
            catch { }
        }

        static void Finish(string status, bool bumpPackRevision)
        {
            lock (s_Sync) { s_Running = false; }
            SetStatus(status);
            if (bumpPackRevision)
            {
                try { VpbLocalDatabase.InvalidateDataPackLookOverlayCache(); } catch { }
                try { VpbDataPackService.BumpStatusRevision(); } catch { }
            }
        }

        static void TouchSyncStamp(long watermark)
        {
            try { VpbLocalDatabase.TryTouchHubLiveSyncStamp(watermark); } catch { }
        }

        delegate void PayloadCallback(byte[] data);
        delegate void ErrorCallback(string error);

        static IEnumerator PostPage(int page, PayloadCallback onData, ErrorCallback onError)
        {
            var body = new JSONClass();
            body["source"] = "VaM";
            body["action"] = "getResources";
            body["latest_image"] = "N";
            body["perpage"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            body["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            body["sort"] = CrawlSort;
            string postData = body.ToString();

            using (UnityWebRequest req = UnityWebRequest.Post(HubApiUrl, postData))
            {
                try { req.timeout = RequestTimeoutSec; } catch { }
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(postData));
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");
                req.SendWebRequest();

                float start = Time.realtimeSinceStartup;
                while (!req.isDone)
                {
                    if (Time.realtimeSinceStartup - start > RequestTimeoutSec)
                    {
                        try { req.Abort(); } catch { }
                        if (onError != null) onError("timed out");
                        yield break;
                    }
                    yield return null;
                }

                if (req.isNetworkError || req.isHttpError)
                {
                    if (onError != null) onError(req.error + " (" + req.responseCode + ")");
                    yield break;
                }
                if (onData != null) onData(req.downloadHandler.data);
            }
        }

        static void ParsePageWorker(object state)
        {
            var job = (PageJob)state;
            try
            {
                string text = Encoding.UTF8.GetString(job.Payload);
                job.Payload = null;
                JSONNode root = JSON.Parse(text);
                if (root == null)
                {
                    job.Error = "unreadable response";
                    return;
                }

                JSONNode pagination = root["pagination"];
                if (pagination != null) job.TotalPages = pagination["total_pages"].AsInt;

                JSONArray resources = root["resources"].AsArray;
                if (resources == null)
                {
                    job.Error = "no resources in response";
                    return;
                }

                var list = new List<VpbLocalDatabase.HubLiveEntry>(resources.Count);
                for (int i = 0; i < resources.Count; i++)
                {
                    JSONClass res = resources[i].AsObject;
                    if (res == null) continue;
                    VpbLocalDatabase.HubLiveEntry e = BuildEntry(res);
                    if (e == null) continue;
                    if (e.LastUpdateUnix > job.MaxUpdateUnix) job.MaxUpdateUnix = e.LastUpdateUnix;
                    if (e.LastUpdateUnix > 0 && e.LastUpdateUnix <= job.WatermarkUnix)
                    {
                        job.ReachedWatermark = true;
                        continue;
                    }
                    list.Add(e);
                }
                job.Entries = list;
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
            }
            finally
            {
                job.Done = true;
            }
        }

        static void ApplyWorker(object state)
        {
            var job = (ApplyJob)state;
            try
            {
                int applied, links;
                string error;
                job.Ok = VpbLocalDatabase.TryApplyHubLiveEntries(
                    job.Entries, job.WatermarkUnix, out applied, out links, out error);
                job.Applied = applied;
                job.Links = links;
                job.Error = error;
            }
            catch (Exception ex)
            {
                job.Ok = false;
                job.Error = ex.Message;
            }
            finally
            {
                job.Done = true;
            }
        }

        const int IdentMaxTokens = 5;
        const int IdentMinPrefix = 3;
        const int IdentMinFull = 2;
        const int FlagHubHosted = 1;
        const int FlagHubDownloadable = 2;
        const int MaxTagLength = 64;

        static VpbLocalDatabase.HubLiveEntry BuildEntry(JSONClass res)
        {
            string rid = NodeText(res, "resource_id");
            if (string.IsNullOrEmpty(rid)) return null;
            long entryId;
            if (!long.TryParse(rid, out entryId) || entryId <= 0) return null;

            var e = new VpbLocalDatabase.HubLiveEntry();
            e.EntryId = entryId;
            e.ResourceId = rid;
            e.SrcId = NodeText(res, "package_id");
            e.Title = CleanCell(NodeText(res, "title"));
            e.Creator = CleanCell(NodeText(res, "username"));
            e.Category = CleanCell(NodeText(res, "type"));
            e.PayType = CleanCell(NodeText(res, "category"));
            e.TagLine = CleanCell(NodeText(res, "tag_line"));
            e.Version = CleanCell(NodeText(res, "version_string"));
            e.RatingAvg = NodeText(res, "rating_avg");
            if (string.IsNullOrEmpty(e.RatingAvg)) e.RatingAvg = "0";
            e.RatingCount = NodeLong(res, "rating_count");
            e.DepCount = NodeLong(res, "dependency_count");
            e.Downloads = CoarseCount(NodeLong(res, "download_count"));
            e.LastUpdateUnix = NodeLong(res, "last_update");
            e.FirstRelease = IsoDate(NodeLong(res, "resource_date"));
            e.LastUpdate = IsoDate(e.LastUpdateUnix);

            long sizeBytes = 0;
            var licenses = new List<string>(2);
            JSONArray files = res["hubFiles"].AsArray;
            if (files != null)
            {
                for (int i = 0; i < files.Count; i++)
                {
                    JSONClass f = files[i].AsObject;
                    if (f == null) continue;
                    string key, version;
                    if (TryVarKeyFromFilename(NodeText(f, "filename"), out key, out version)
                        && !e.VarKeys.Contains(key))
                    {
                        e.VarKeys.Add(key);
                        e.VarVersions.Add(version ?? "");
                    }
                    string lic = CleanCell(NodeText(f, "licenseType"));
                    if (lic.Length > 0 && !licenses.Contains(lic)) licenses.Add(lic);
                    string pv = NodeText(f, "programVersion");
                    if (pv.Length > 0 && (e.MinVam.Length == 0 || string.CompareOrdinal(pv, e.MinVam) < 0))
                        e.MinVam = pv;
                    sizeBytes += NodeLong(f, "file_size");
                }
            }
            e.License = string.Join(",", licenses.ToArray());
            e.SizeKb = sizeBytes / 1024;

            AppendIdentKeys(e.Creator, e.Title, e.IdentKeys);

            AddHubTags(res["tags"], e);
            if (e.Category.Length > 0) AddTag(e, "meta", "category:" + e.Category.ToLowerInvariant());
            if (e.PayType.Length > 0) AddTag(e, "meta", "type:" + e.PayType.ToLowerInvariant());

            if (string.Equals(NodeText(res, "hubHosted"), "true", StringComparison.OrdinalIgnoreCase))
                e.Flags |= FlagHubHosted;
            if (string.Equals(NodeText(res, "hubDownloadable"), "true", StringComparison.OrdinalIgnoreCase))
                e.Flags |= FlagHubDownloadable;

            return e;
        }

        static void AddHubTags(JSONNode tagsNode, VpbLocalDatabase.HubLiveEntry e)
        {
            if (tagsNode == null) return;
            JSONArray arr = tagsNode.AsArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                    AddTag(e, "hub", NormalizeTag(arr[i].Value));
                return;
            }
            string raw = tagsNode.Value;
            if (string.IsNullOrEmpty(raw)) return;
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
                AddTag(e, "hub", NormalizeTag(parts[i]));
        }

        static void AddTag(VpbLocalDatabase.HubLiveEntry e, string ns, string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            for (int i = 0; i < e.TagTexts.Count; i++)
            {
                if (e.TagNamespaces[i] == ns && e.TagTexts[i] == tag) return;
            }
            e.TagNamespaces.Add(ns);
            e.TagTexts.Add(tag);
        }

        static string NormalizeTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            var sb = new StringBuilder(tag.Length);
            bool pendingSpace = false;
            for (int i = 0; i < tag.Length; i++)
            {
                char c = tag[i];
                if (c == '|' || c == ':') c = ' ';
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    if (sb.Length > 0) pendingSpace = true;
                    continue;
                }
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            if (sb.Length == 0 || sb.Length > MaxTagLength) return "";
            return sb.ToString();
        }

        static string CleanCell(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.IndexOf('|') >= 0 ? value.Replace("|", " ") : value;
        }

        internal static bool TryVarKeyFromFilename(string filename, out string key, out string version)
        {
            key = null;
            version = null;
            if (string.IsNullOrEmpty(filename)) return false;

            string name = filename;
            if (name.Length > 4 && name.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            string[] segs = name.Split('.');
            int count = segs.Length;
            if (count >= 3)
            {
                string last = segs[count - 1];
                if (IsAllDigits(last))
                {
                    version = last;
                    count--;
                }
                else if (string.Equals(last, "latest", StringComparison.OrdinalIgnoreCase))
                {
                    count--;
                }
            }
            if (count < 2) return false;

            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append('.');
                sb.Append(segs[i]);
            }
            string k = sb.ToString().ToLowerInvariant().Trim();
            if (k.Length == 0 || k.IndexOf(':') >= 0 || k.IndexOf('|') >= 0) return false;
            key = k;
            return true;
        }

        static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
            }
            return true;
        }

        internal static void AppendIdentKeys(string creator, string title, List<string> dest)
        {
            if (dest == null) return;
            string c = NormalizeIdentText(creator);
            if (c.Length == 0) return;

            var names = new List<string>(IdentMaxTokens * 2 + 1);
            string t = title ?? "";
            AppendPrefixNames(t, names);
            string stripped = StripLeadingBracket(t);
            if (!string.Equals(stripped, t, StringComparison.Ordinal))
                AppendPrefixNames(stripped, names);

            string full = NormalizeIdentText(t);
            if (full.Length >= IdentMinFull && !names.Contains(full)) names.Add(full);

            for (int i = 0; i < names.Count; i++) dest.Add(c + "|" + names[i]);
        }

        static void AppendPrefixNames(string baseText, List<string> names)
        {
            if (string.IsNullOrEmpty(baseText)) return;
            var acc = new StringBuilder(32);
            int tokens = 0;
            int i = 0;
            while (i < baseText.Length && tokens < IdentMaxTokens)
            {
                if (!IsIdentChar(baseText[i])) { i++; continue; }
                int start = i;
                while (i < baseText.Length && IsIdentChar(baseText[i])) i++;
                for (int j = start; j < i; j++) acc.Append(char.ToLowerInvariant(baseText[j]));
                tokens++;
                if (acc.Length >= IdentMinPrefix)
                {
                    string s = acc.ToString();
                    if (!names.Contains(s)) names.Add(s);
                }
            }
        }

        static bool IsIdentChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        static string NormalizeIdentText(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = char.ToLowerInvariant(value[i]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            }
            return sb.ToString();
        }

        static string StripLeadingBracket(string title)
        {
            if (string.IsNullOrEmpty(title)) return title ?? "";
            int i = 0;
            while (i < title.Length && char.IsWhiteSpace(title[i])) i++;
            if (i >= title.Length) return title;
            char open = title[i];
            if (open != '[' && open != '(' && open != '{') return title;

            int j = i + 1;
            int scanned = 0;
            while (j < title.Length && scanned <= 24)
            {
                char c = title[j];
                if (c == ']' || c == ')' || c == '}')
                {
                    int end = j + 1;
                    while (end < title.Length && char.IsWhiteSpace(title[end])) end++;
                    return title.Substring(end);
                }
                j++;
                scanned++;
            }
            return title;
        }

        static long CoarseCount(long n)
        {
            if (n <= 999) return n;
            long scale = 1;
            while (n / scale >= 1000) scale *= 10;
            return (n / scale) * scale;
        }

        static string IsoDate(long unix)
        {
            if (unix <= 0) return "";
            try
            {
                DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix);
                return dt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return ""; }
        }

        static string NodeText(JSONClass node, string key)
        {
            if (node == null) return "";
            JSONNode v = node[key];
            if (v == null) return "";
            string s = v.Value;
            if (s == null || s == "null") return "";
            return s;
        }

        static long NodeLong(JSONClass node, string key)
        {
            string s = NodeText(node, key);
            if (s.Length == 0) return 0;
            long n;
            if (long.TryParse(s, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out n))
                return n;
            double d;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out d))
                return (long)d;
            return 0;
        }
    }
}
