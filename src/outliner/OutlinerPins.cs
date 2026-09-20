using System;
using System.Collections.Generic;
using SimpleJSON;

namespace VPB.Outliner
{
    internal static class OutlinerPins
    {
        internal static Dictionary<string, List<string>> Parse(string json)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json)) return map;
            JSONNode node = null;
            try { node = JSON.Parse(json); }
            catch { return map; }
            JSONClass obj = node != null ? node.AsObject : null;
            if (obj == null) return map;
            foreach (KeyValuePair<string, JSONNode> kv in obj)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                JSONArray arr = kv.Value.AsArray;
                if (arr == null) continue;
                var list = new List<string>(arr.Count);
                for (int i = 0; i < arr.Count; i++)
                {
                    string id = arr[i] != null ? arr[i].Value : "";
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!list.Contains(id)) list.Add(id);
                }
                map[kv.Key] = list;
            }
            return map;
        }

        internal static string Serialize(Dictionary<string, List<string>> map)
        {
            var obj = new JSONClass();
            if (map != null)
            {
                foreach (KeyValuePair<string, List<string>> kv in map)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                    var arr = new JSONArray();
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        if (string.IsNullOrEmpty(kv.Value[i])) continue;
                        arr.Add(kv.Value[i]);
                    }
                    obj[kv.Key] = arr;
                }
            }
            return obj.ToString();
        }

        internal static bool IsPinned(Dictionary<string, List<string>> map, string atomType, string qualified)
        {
            if (map == null || string.IsNullOrEmpty(atomType) || string.IsNullOrEmpty(qualified))
                return false;
            List<string> list;
            if (!map.TryGetValue(atomType, out list) || list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], qualified, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal static void SetPinned(
            Dictionary<string, List<string>> map,
            string atomType,
            string qualified,
            bool pinned)
        {
            if (map == null || string.IsNullOrEmpty(atomType) || string.IsNullOrEmpty(qualified))
                return;
            List<string> list;
            if (!map.TryGetValue(atomType, out list) || list == null)
            {
                list = new List<string>(4);
                map[atomType] = list;
            }
            int idx = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], qualified, StringComparison.Ordinal))
                {
                    idx = i;
                    break;
                }
            }
            if (pinned)
            {
                if (idx < 0) list.Add(qualified);
            }
            else if (idx >= 0)
            {
                list.RemoveAt(idx);
            }
        }
    }
}
