using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using Valve.Newtonsoft.Json;
using System.Threading;
using SimpleJSON;
using VPB.src.util;

namespace VPB
{
    internal enum VpbImportSourceKind { Scene, Appearance }

    internal static class VpbImportSource
    {
        internal static readonly VpbResourceType[] ApplyOrder = {
            VpbResourceType.Appearance, VpbResourceType.Morphs, VpbResourceType.Skin,
            VpbResourceType.Clothing, VpbResourceType.Hair, VpbResourceType.BreastPhysics,
            VpbResourceType.Glute, VpbResourceType.General, VpbResourceType.Plugins, VpbResourceType.Pose
        };

        internal static JSONClass Preset(JSONClass root, VpbImportSourceKind kind, string atomId)
        {
            if (root == null) return null;
            if (kind == VpbImportSourceKind.Appearance)
                return root["storables"] is JSONArray ? root : null;
            JSONArray atoms = root["atoms"] as JSONArray;
            if (atoms == null || string.IsNullOrEmpty(atomId)) return null;
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass atom = atoms[i] as JSONClass;
                if (atom == null || atom["type"].Value != "Person") continue;
                string id = atom["id"].Value;
                if (string.IsNullOrEmpty(id)) id = "Person_" + i;
                if (id == atomId && atom["storables"] is JSONArray)
                    return VpbImport.WrapAtomNodeAsPreset(atom);
            }
            return null;
        }

        internal static JSONClass Clone(JSONClass node)
        {
            if (node == null) return null;
            JSONClass clone = JSON.Parse(JsonSerializationUtil.Serialize(node, 8192)) as JSONClass;
            if (clone == null) throw new InvalidDataException("Cannot copy import data");
            return clone;
        }

        internal static int Count(JSONClass preset, VpbResourceType type)
        {
            JSONArray storables = preset != null ? preset["storables"] as JSONArray : null;
            if (storables == null) return 0;
            int count = 0;
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s == null) continue;
                string id = s["id"].Value;
                if (id == "geometry")
                {
                    string key = type == VpbResourceType.Clothing ? "clothing"
                        : type == VpbResourceType.Hair ? "hair"
                        : type == VpbResourceType.Morphs ? "morphs" : null;
                    JSONArray items = key != null ? s[key] as JSONArray : null;
                    if (items != null)
                        foreach (JSONNode item in items)
                            if (item != null && !string.Equals(item["enabled"].Value, "false", StringComparison.OrdinalIgnoreCase)) count++;
                    if (type == VpbResourceType.Appearance && s.Count > 1) count++;
                }
                if (type == VpbResourceType.Plugins && id == "PluginManager")
                {
                    JSONClass plugins = s["plugins"] as JSONClass;
                    if (plugins != null) count += plugins.Count;
                }
                if (s.Count > 1 && Matches(id, type)) count++;
            }
            return count;
        }

        internal static bool Matches(string id, VpbResourceType type)
        {
            if (string.IsNullOrEmpty(id) || id.EndsWith("Presets", StringComparison.OrdinalIgnoreCase)) return false;
            bool item = id.StartsWith("clothing", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("hair", StringComparison.OrdinalIgnoreCase);
            switch (type)
            {
                case VpbResourceType.Skin:
                    return !item && (id.StartsWith("skin", StringComparison.OrdinalIgnoreCase)
                        || id.StartsWith("texture", StringComparison.OrdinalIgnoreCase));
                case VpbResourceType.BreastPhysics:
                    return !item && id.IndexOf("Breast", StringComparison.OrdinalIgnoreCase) >= 0
                        && id.IndexOf("Physics", StringComparison.OrdinalIgnoreCase) >= 0;
                case VpbResourceType.Glute:
                    return !item && id.IndexOf("Glute", StringComparison.OrdinalIgnoreCase) >= 0
                        && id.IndexOf("Physics", StringComparison.OrdinalIgnoreCase) >= 0;
                case VpbResourceType.Pose:
                    return id == "control" || id.EndsWith("Control", StringComparison.Ordinal);
                case VpbResourceType.Appearance:
                    return Matches(id, VpbResourceType.Skin);
                case VpbResourceType.General:
                    return id == "Preset";
                default: return false;
            }
        }

        internal static JSONClass Slice(JSONClass preset, VpbResourceType type)
        {
            if (preset == null) return null;
            if (type == VpbResourceType.Appearance || type == VpbResourceType.General || type == VpbResourceType.Plugins)
                return Clone(preset);
            if (type == VpbResourceType.Clothing || type == VpbResourceType.Hair)
                return VpbImport.BuildOutfitComponentSlice(preset, type);
            JSONClass view = new JSONClass();
            JSONArray result = new JSONArray();
            JSONArray storables = preset["storables"] as JSONArray;
            if (storables == null) return null;
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s == null) continue;
                string id = s["id"].Value;
                if (id == "geometry")
                {
                    string key = type == VpbResourceType.Morphs || type == VpbResourceType.Pose ? "morphs" : null;
                    if (key == null || !(s[key] is JSONArray)) continue;
                    JSONClass geometry = new JSONClass();
                    geometry["id"] = "geometry";
                    geometry[key] = s[key];
                    foreach (KeyValuePair<string, JSONNode> pair in s)
                        if (pair.Key.StartsWith(key + ":", StringComparison.Ordinal)
                            || (key == "morphs" && (pair.Key == "useFemaleMorphsOnMale" || pair.Key == "useMaleMorphsOnFemale")))
                            geometry[pair.Key] = pair.Value;
                    result.Add(geometry);
                }
                else if (Matches(id, type)
                    || (type == VpbResourceType.Morphs && id == "MorphPresets")
                    || (type == VpbResourceType.Pose && id == "PosePresets")) result.Add(s);
            }
            view["storables"] = result;
            if (preset["setUnlistedParamsToDefault"] != null)
                view["setUnlistedParamsToDefault"] = preset["setUnlistedParamsToDefault"];
            return result.Count > 0 ? Clone(view) : null;
        }

        internal static void Select(HashSet<VpbResourceType> selected, VpbResourceType type)
        {
            if (type == VpbResourceType.Appearance) selected.Clear();
            else selected.Remove(VpbResourceType.Appearance);
            selected.Add(type);
        }
    }

    internal sealed class VpbImportReadRequest
    {
        internal string Path;
        internal string InternalPath;
        internal int CodePage;
        internal long Size;
        internal long WriteTicks;
        internal int Generation;
        internal bool WritePersonCache;

        internal bool IsCurrent()
        {
            try
            {
                FileInfo file = new FileInfo(Path);
                return file.Exists && file.Length == Size && file.LastWriteTimeUtc.Ticks == WriteTicks;
            }
            catch { return false; }
        }

        internal JSONClass Read()
        {
            if (!IsCurrent()) throw new IOException("Import source changed or disappeared");
            JSONClass root;
            if (InternalPath == null)
            {
                using (var reader = new StreamReader(Path)) root = ReadJson(reader);
            }
            else
            {
                int codePage = CodePage == int.MinValue ? VarPackage.DetectZipNameCodePage(Path) : CodePage;
                using (var zip = VarPackage.OpenZipFileForRead(Path, codePage))
                {
                    var entry = zip.GetEntry(InternalPath);
                    if (entry == null || !entry.IsFile) throw new IOException("Import preset missing from package");
                    using (var stream = zip.GetInputStream(entry))
                    using (var reader = new StreamReader(stream)) root = ReadJson(reader);
                }
            }
            if (!IsCurrent()) throw new IOException("Import source changed while reading");
            return root;
        }
        internal static JSONClass ReadJson(TextReader input)
        {
            var stack = new Stack<JSONNode>();
            JSONNode root = null;
            string key = null;
            using (var reader = new JsonTextReader(input))
            {
                reader.DateParseHandling = DateParseHandling.None;
                while (reader.Read())
                {
                    JsonToken token = reader.TokenType;
                    if (token == JsonToken.Comment) continue;
                    if (token == JsonToken.PropertyName) { key = (string)reader.Value; continue; }
                    if (token == JsonToken.EndObject || token == JsonToken.EndArray) { stack.Pop(); continue; }
                    bool container = token == JsonToken.StartObject || token == JsonToken.StartArray;
                    JSONNode node;
                    if (container) node = token == JsonToken.StartObject ? (JSONNode)new JSONClass() : new JSONArray();
                    else if (token == JsonToken.String || token == JsonToken.Integer || token == JsonToken.Float
                        || token == JsonToken.Boolean || token == JsonToken.Null)
                        node = new JSONData(token == JsonToken.Null ? "null" : token == JsonToken.Boolean
                            ? ((bool)reader.Value ? "true" : "false") : Convert.ToString(reader.Value, CultureInfo.InvariantCulture));
                    else throw new InvalidDataException("Unsupported import JSON token");
                    if (stack.Count == 0)
                    {
                        if (root != null) throw new InvalidDataException("Multiple import JSON roots");
                        root = node;
                    }
                    else stack.Peek().Add(key, node);
                    key = null;
                    if (container) stack.Push(node);
                }
            }
            if (stack.Count != 0 || !(root is JSONClass)) throw new InvalidDataException("Incomplete import JSON object");
            return (JSONClass)root;
        }
    }

    internal sealed class VpbImportReadQueue
    {
        internal sealed class Result
        {
            internal VpbImportReadRequest Request;
            internal JSONClass Root;
            internal Exception Error;
        }
        private readonly object gate = new object();
        private VpbImportReadRequest latest;
        private Result completed;
        private bool running;
        private readonly Func<VpbImportReadRequest, JSONClass> read;

        internal VpbImportReadQueue() : this(request => request.Read()) { }
        internal VpbImportReadQueue(Func<VpbImportReadRequest, JSONClass> read) { this.read = read; }

        internal void Submit(VpbImportReadRequest request)
        {
            lock (gate)
            {
                latest = request;
                completed = null;
                if (running) return;
                running = true;
                try
                {
                    if (!ThreadPool.QueueUserWorkItem(_ => Run())) throw new InvalidOperationException("Cannot queue import read");
                }
                catch { running = false; latest = null; throw; }
            }
        }

        internal void Cancel()
        {
            lock (gate) { latest = null; completed = null; }
        }

        internal Result Take()
        {
            lock (gate)
            {
                Result result = completed;
                completed = null;
                return result;
            }
        }

        private void Run()
        {
            while (true)
            {
                VpbImportReadRequest request;
                lock (gate)
                {
                    request = latest;
                    if (request == null) { running = false; return; }
                }
                Result result = new Result { Request = request };
                try { result.Root = read(request); }
                catch (Exception ex) { result.Error = ex; }
                lock (gate)
                {
                    if (latest == request) { completed = result; latest = null; }
                }
            }
        }
    }
}
