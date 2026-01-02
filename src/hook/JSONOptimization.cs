using System;
using System.Collections.Generic;
using SimpleJSON;

namespace VPB
{
    public static class JSONOptimization
    {
        private const string TIMELINE_PLUGIN_SUFFIX = "_VamTimeline.AtomPlugin";

        public struct JSONScanResult
        {
            public bool HasTimeline;
            public HashSet<string> VariableReferences;
            public int TimelineCount;
        }

        public static JSONScanResult ScanJSONForDependencies(JSONNode rootNode)
        {
            var result = new JSONScanResult
            {
                VariableReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                HasTimeline = false,
                TimelineCount = 0
            };

            if (rootNode == null) return result;

            ScanNodeRecursive(rootNode, result);
            return result;
        }

        private static void ScanNodeRecursive(JSONNode node, JSONScanResult result)
        {
            if (node == null) return;

            if (node is JSONClass jclass)
            {
                foreach (string key in jclass.Keys)
                {
                    JSONNode child = jclass[key];
                    
                    if (key == "id" && child.Value != null && child.Value.EndsWith(TIMELINE_PLUGIN_SUFFIX))
                    {
                        result.HasTimeline = true;
                        result.TimelineCount++;
                    }

                    ScanNodeRecursive(child, result);
                }
            }
            else if (node is JSONArray jarray)
            {
                for (int i = 0; i < jarray.Count; i++)
                {
                    ScanNodeRecursive(jarray[i], result);
                }
            }
            else
            {
                string value = node.Value;
                if (!string.IsNullOrEmpty(value))
                {
                    ExtractVariableReferences(value, result.VariableReferences);
                }
            }
        }

        private static void ExtractVariableReferences(string text, HashSet<string> references)
        {
            if (string.IsNullOrEmpty(text)) return;
            VarNameParser.Parse(text, references);
        }

        public static JSONClass FilterTimelinePlugins(JSONClass sourceJSON)
        {
            if (sourceJSON == null) return null;

            JSONClass filtered = new JSONClass();

            foreach (string key in sourceJSON.Keys)
            {
                JSONNode node = sourceJSON[key];

                if (key == "storables")
                {
                    JSONArray storables = node as JSONArray;
                    if (storables != null)
                    {
                        JSONArray filteredArray = new JSONArray();
                        for (int i = 0; i < storables.Count; i++)
                        {
                            JSONNode storable = storables[i];
                            JSONNode idNode = storable["id"];

                            if (idNode == null || !idNode.Value.EndsWith(TIMELINE_PLUGIN_SUFFIX))
                            {
                                filteredArray.Add(storable);
                            }
                        }
                        filtered.Add(key, filteredArray);
                    }
                    else
                    {
                        filtered.Add(key, node);
                    }
                }
                else
                {
                    filtered.Add(key, node);
                }
            }

            return filtered;
        }

        public static bool HasTimelinePlugin(JSONNode node)
        {
            if (node == null) return false;

            if (node is JSONClass jclass)
            {
                foreach (string key in jclass.Keys)
                {
                    if (key == "storables")
                    {
                        JSONArray array = jclass[key] as JSONArray;
                        if (array != null)
                        {
                            for (int i = 0; i < array.Count; i++)
                            {
                                var idNode = array[i]["id"];
                                if (idNode != null && idNode.Value != null && idNode.Value.EndsWith(TIMELINE_PLUGIN_SUFFIX))
                                {
                                    return true;
                                }
                            }
                        }
                        return false;
                    }
                }
            }

            return false;
        }

        public static void ExtractAllVariableReferences(JSONNode node, HashSet<string> results)
        {
            if (node == null) return;

            if (node is JSONClass jclass)
            {
                foreach (string key in jclass.Keys)
                {
                    ExtractAllVariableReferences(jclass[key], results);
                }
            }
            else if (node is JSONArray jarray)
            {
                for (int i = 0; i < jarray.Count; i++)
                {
                    ExtractAllVariableReferences(jarray[i], results);
                }
            }
            else
            {
                string value = node.Value;
                if (!string.IsNullOrEmpty(value))
                {
                    ExtractVariableReferences(value, results);
                }
            }
        }
    }
}
