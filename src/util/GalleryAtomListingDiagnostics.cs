using System;
using System.Collections.Generic;
using SimpleJSON;
using MVR.FileManagement;

namespace VPB.src.util
{
    /// <summary>
    /// Ctrl+Shift+L diagnostic: lists every atom in the gallery's selected scene/preset file(s).
    /// </summary>
    public static class GalleryAtomListingDiagnostics
    {
        public static void DumpGallerySelection(IList<FileEntry> selectedFiles, string categoryTitle, string tag)
        {
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                LogUtil.Log("[VPB][ATOMS][dump:" + tag + "] no gallery item selected.");
                return;
            }

            LogUtil.Log("[VPB][ATOMS][dump:" + tag + "] category='" + (categoryTitle ?? string.Empty)
                + "' selectionCount=" + selectedFiles.Count);

            for (int i = 0; i < selectedFiles.Count; i++)
                DumpFileEntry(selectedFiles[i], tag, i + 1, selectedFiles.Count);
        }

        private static void DumpFileEntry(FileEntry entry, string tag, int index, int total)
        {
            if (entry == null)
            {
                LogUtil.Log("[VPB][ATOMS][dump:" + tag + "] [" + index + "/" + total + "] (null entry)");
                return;
            }

            string path = entry.Path ?? string.Empty;
            string uid = entry.Uid ?? string.Empty;
            string name = entry.Name ?? string.Empty;
            LogUtil.Log("[VPB][ATOMS][dump:" + tag + "] [" + index + "/" + total + "] name='" + name
                + "' path='" + path + "' uid='" + uid + "'");

            JSONClass root = TryReadJsonRoot(entry);
            if (root == null)
            {
                LogUtil.Log("[VPB][ATOMS][dump:" + tag + "]   (not readable JSON — no atom list)");
                return;
            }

            if (root.HasKey("atoms"))
            {
                DumpSceneAtoms(root, tag);
                return;
            }

            // Single-atom / preset JSON without a scene wrapper.
            if (root.HasKey("storables"))
            {
                string atomId = root["id"] != null ? root["id"].Value : "(preset)";
                string atomType = root["type"] != null ? root["type"].Value : "Person";
                LogUtil.Log("[VPB][ATOMS][dump:" + tag + "]   preset atom id='" + atomId + "' type=" + atomType);
                return;
            }

            LogUtil.Log("[VPB][ATOMS][dump:" + tag + "]   JSON has no atoms[] array.");
        }

        private static void DumpSceneAtoms(JSONClass root, string tag)
        {
            JSONArray atoms = root["atoms"].AsArray;
            if (atoms == null || atoms.Count == 0)
            {
                LogUtil.Log("[VPB][ATOMS][dump:" + tag + "]   atoms[] empty.");
                return;
            }

            string anchorPersonId = FindFirstPersonAtomId(atoms);
            Dictionary<string, bool> cuaLinks = BuildCuaLinkMap(root, anchorPersonId);
            var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            LogUtil.Log("[VPB][ATOMS][dump:" + tag + "]   atomCount=" + atoms.Count
                + (string.IsNullOrEmpty(anchorPersonId) ? "" : "  anchorPerson='" + anchorPersonId + "'"));

            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass atom = atoms[i].AsObject;
                if (atom == null) continue;

                string type = atom["type"] != null ? atom["type"].Value : string.Empty;
                if (string.IsNullOrEmpty(type)) type = "?";
                string atomId = ResolveAtomId(atom, i);

                int tc;
                typeCounts[type] = typeCounts.TryGetValue(type, out tc) ? tc + 1 : 1;

                string line = "   [" + i + "] '" + atomId + "'  " + type;

                if (type == "CustomUnityAsset" && cuaLinks.ContainsKey(atomId))
                {
                    bool onPerson;
                    if (cuaLinks.TryGetValue(atomId, out onPerson) && onPerson)
                        line += "  (on person)";
                }

                string linkTo = ReadControlLinkTo(atom);
                if (!string.IsNullOrEmpty(linkTo))
                    line += "  link=" + linkTo;

                string assetHint = ReadAtomAssetHint(atom, type);
                if (!string.IsNullOrEmpty(assetHint))
                    line += "  asset=" + assetHint;

                if (SceneAtomImporter.AtomAlreadyInScene(atomId))
                    line += "  (exists in live scene)";

                LogUtil.Log("[VPB][ATOMS][dump:" + tag + "]" + line);
            }

            if (typeCounts.Count > 0)
            {
                var parts = new List<string>(typeCounts.Count);
                foreach (KeyValuePair<string, int> kv in typeCounts)
                    parts.Add(kv.Key + "=" + kv.Value);
                parts.Sort(StringComparer.Ordinal);
                LogUtil.Log("[VPB][ATOMS][dump:" + tag + "]   byType: " + string.Join(", ", parts.ToArray()));
            }
        }

        private static Dictionary<string, bool> BuildCuaLinkMap(JSONClass root, string anchorPersonId)
        {
            var map = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(anchorPersonId)) return map;
            foreach (CUAAtomImporter.CuaEntry ce in CUAAtomImporter.EnumerateSceneCUAs(root, anchorPersonId))
                map[ce.Id] = ce.LinksToPerson;
            return map;
        }

        private static string FindFirstPersonAtomId(JSONArray atoms)
        {
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass atom = atoms[i].AsObject;
                if (atom == null) continue;
                string type = atom["type"] != null ? atom["type"].Value : string.Empty;
                if (!SceneUtils.IsPersonLikeAtomType(type)) continue;
                return ResolveAtomId(atom, i);
            }
            return null;
        }

        private static string ReadControlLinkTo(JSONClass atom)
        {
            JSONClass control = atom.GetStorable("control");
            if (control == null || !control.HasKey("linkTo")) return null;
            string v = control["linkTo"].Value;
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static string ReadAtomAssetHint(JSONClass atom, string type)
        {
            if (type != "CustomUnityAsset" && type != "SubScene") return null;
            JSONArray storables = atom["storables"] != null ? atom["storables"].AsArray : null;
            if (storables == null) return null;

            for (int i = 0; i < storables.Count; i++)
            {
                JSONClass s = storables[i].AsObject;
                if (s == null) continue;
                if (s.HasKey("url"))
                {
                    string url = s["url"].Value;
                    if (!string.IsNullOrEmpty(url)) return ShortenUrl(url);
                }
                if (s.HasKey("assetUrl"))
                {
                    string url = s["assetUrl"].Value;
                    if (!string.IsNullOrEmpty(url)) return ShortenUrl(url);
                }
            }
            return null;
        }

        private static string ShortenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            string u = url.Replace('\\', '/');
            if (u.Length <= 72) return u;
            return "…" + u.Substring(u.Length - 69);
        }

        private static JSONClass TryReadJsonRoot(FileEntry entry)
        {
            if (entry == null) return null;
            try
            {
                string path = UI.NormalizePath(entry.Path);
                JSONNode node = UI.LoadJSONWithFallback(path, entry);
                return node != null ? node.AsObject : null;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB][ATOMS] read failed for '" + entry.Uid + "': " + ex.Message);
                return null;
            }
        }

        private static string ResolveAtomId(JSONClass atom, int index)
        {
            string type = atom["type"] != null ? atom["type"].Value : "Atom";
            return (atom["id"] != null && !string.IsNullOrEmpty(atom["id"].Value))
                ? atom["id"].Value
                : (type + "_" + index);
        }
    }
}
