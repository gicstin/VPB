using System;
using System.Collections.Generic;
using SimpleJSON;

namespace VPB
{
    /// <summary>
    /// Extracts texture paths + <see cref="ImageLoaderThreaded.QueuedImage"/> flags from DAZ Studio
    /// shader_material JSON (<c>MeshVR.DAZImportMaterial</c> in <c>ref/vam</c>).
    /// </summary>
    internal static class VaMDazImportMaterialExtractor
    {
        internal struct DazTextureEntry
        {
            public string ImagePath;
            public NativeTextureOnDemandCache.TextureFlags Flags;
        }

        private sealed class MaterialState
        {
            public bool UseSpecularAsGlossMap;
            public bool CopyBumpAsSpecularColorMap;
            public bool ForceBumpAsNormalMap;

            public string DiffusePath;
            public string AlphaPath;
            public string SpecularColorPath;
            public string SpecularStrengthPath;
            public string GlossPath;
            public string RoughnessPath;
            public string BumpPath;
            public float BumpStrength = 1f;
            public bool HasBumpMap;
            public string NormalPath;
            public bool HasNormalMap;
            public string SubsurfaceColorPath;
            public string SubsurfaceStrengthPath;
            public string TranslucencyColorPath;
            public string TranslucencyStrengthPath;
        }

        public static bool TryCollectFromShaderMaterialNode(JSONNode node, List<DazTextureEntry> outEntries)
        {
            if (node == null || outEntries == null) return false;
            JSONClass sm = node.AsObject;
            if (sm == null || !LooksLikeDazShaderMaterial(sm)) return false;

            var state = new MaterialState();
            ApplyImportOptionsFromNode(sm, state);
            ProcessJsonChannels(sm, state);
            EmitImportImageEntries(state, outEntries);
            return outEntries.Count > 0;
        }

        private static bool LooksLikeDazShaderMaterial(JSONClass sm)
        {
            if (sm == null) return false;
            if (HasChannelMap(sm, "diffuse")) return true;
            if (HasChannelMap(sm, "bump")) return true;
            if (HasChannelMap(sm, "normal")) return true;
            if (HasChannelMap(sm, "specular")) return true;
            if (HasChannelMap(sm, "glossiness")) return true;
            if (HasChannelMap(sm, "transparency")) return true;
            return HasStudioMaterialChannels(sm);
        }

        private static bool HasChannelMap(JSONClass sm, string key)
        {
            JSONNode n = sm[key];
            if (n == null) return false;
            JSONNode ch = n["channel"];
            return ch != null && (ch["image_file"] != null || ch["current_value"] != null);
        }

        private static bool HasStudioMaterialChannels(JSONClass sm)
        {
            JSONNode extra = sm["extra"];
            if (extra == null) return false;
            JSONArray arr = extra.AsArray;
            if (arr == null) return false;
            for (int i = 0; i < arr.Count; i++)
            {
                JSONNode item = arr[i];
                if (item == null) continue;
                if (item["type"] != null && item["type"].Value == "studio_material_channels" && item["channels"] != null)
                    return true;
            }
            return false;
        }

        private static void ApplyImportOptionsFromNode(JSONClass sm, MaterialState state)
        {
            // Optional overrides when embedded in a DAZ import preset node (DAZImport fields).
            state.UseSpecularAsGlossMap = ReadBool(sm, "useSpecularAsGlossMap", false);
            state.CopyBumpAsSpecularColorMap = ReadBool(sm, "copyBumpAsSpecularColorMap", false);
            state.ForceBumpAsNormalMap = ReadBool(sm, "forceBumpAsNormalMap", false);
        }

        private static void ProcessJsonChannels(JSONClass sm, MaterialState state)
        {
            ReadStandardChannel(sm, "diffuse", ref state.DiffusePath, null);
            ReadStandardChannel(sm, "transparency", ref state.AlphaPath, null);
            ReadBumpChannel(sm, state);

            JSONNode normalCh = GetChannel(sm, "normal");
            if (normalCh != null && normalCh["image_file"] != null && (!state.HasBumpMap || !state.ForceBumpAsNormalMap))
            {
                state.NormalPath = normalCh["image_file"].Value;
                state.HasNormalMap = true;
            }

            JSONNode spec = GetChannel(sm, "specular");
            if (spec != null && spec["image_file"] != null)
            {
                string path = spec["image_file"].Value;
                if (state.UseSpecularAsGlossMap)
                {
                    state.GlossPath = path;
                }
                state.SpecularColorPath = path;
            }

            JSONNode specStr = GetChannel(sm, "specular_strength");
            if (specStr != null && specStr["image_file"] != null)
            {
                string path = specStr["image_file"].Value;
                if (state.UseSpecularAsGlossMap) state.GlossPath = path;
                state.SpecularStrengthPath = path;
            }

            JSONNode gloss = GetChannel(sm, "glossiness");
            if (gloss != null && gloss["image_file"] != null)
            {
                state.GlossPath = gloss["image_file"].Value;
            }

            ReadStandardChannel(sm, "subsurface", ref state.SubsurfaceColorPath, null);
            ReadStandardChannel(sm, "subsurface_strength", ref state.SubsurfaceStrengthPath, null);

            ProcessStudioExtraChannels(sm, state);
        }

        private static void ReadBumpChannel(JSONClass sm, MaterialState state)
        {
            JSONNode ch = GetChannel(sm, "bump");
            if (ch == null) return;
            if (ch["current_value"] != null)
            {
                try { state.BumpStrength = ch["current_value"].AsFloat; }
                catch { state.BumpStrength = 1f; }
            }
            if (ch["image_file"] != null)
            {
                state.BumpPath = ch["image_file"].Value;
                state.HasBumpMap = true;
            }
        }

        private static void ReadStandardChannel(JSONClass sm, string mapKey, ref string targetPath, Action onImage)
        {
            JSONNode ch = GetChannel(sm, mapKey);
            if (ch == null) return;
            if (ch["image_file"] == null) return;
            targetPath = ch["image_file"].Value;
            if (onImage != null) onImage();
        }

        private static JSONNode GetChannel(JSONClass sm, string mapKey)
        {
            JSONNode map = sm[mapKey];
            if (map == null) return null;
            return map["channel"];
        }

        private static void ProcessStudioExtraChannels(JSONClass sm, MaterialState state)
        {
            JSONNode extra = sm["extra"];
            if (extra == null) return;
            JSONArray arr = extra.AsArray;
            if (arr == null) return;

            JSONNode channels = null;
            for (int i = 0; i < arr.Count; i++)
            {
                JSONNode item = arr[i];
                if (item == null || item["type"] == null) continue;
                if (item["type"].Value != "studio_material_channels") continue;
                channels = item["channels"];
                break;
            }

            JSONArray chArr = channels != null ? channels.AsArray : null;
            if (chArr == null) return;

            for (int i = 0; i < chArr.Count; i++)
            {
                JSONNode item = chArr[i];
                if (item == null) continue;
                JSONNode ch = item["channel"];
                if (ch == null) continue;
                JSONNode visible = ch["visible"];
                if (visible != null)
                {
                    try { if (!visible.AsBool) continue; }
                    catch { }
                }

                string id = ch["id"] != null ? ch["id"].Value : null;
                if (string.IsNullOrEmpty(id)) continue;

                switch (id)
                {
                    case "Diffuse Color":
                        if (ch["image_file"] != null) state.DiffusePath = ch["image_file"].Value;
                        break;
                    case "Cutout Opacity":
                        if (ch["image_file"] != null) state.AlphaPath = ch["image_file"].Value;
                        break;
                    case "Specular Color":
                    case "Glossy Color":
                        if (ch["image_file"] != null)
                        {
                            string path = ch["image_file"].Value;
                            if (state.UseSpecularAsGlossMap) state.GlossPath = path;
                            state.SpecularColorPath = path;
                        }
                        break;
                    case "Glossy Layered Weight":
                    case "Glossy Weight":
                    case "Glossiness":
                        if (ch["image_file"] != null) state.GlossPath = ch["image_file"].Value;
                        break;
                    case "Glossy Roughness":
                        if (ch["image_file"] != null) state.RoughnessPath = ch["image_file"].Value;
                        break;
                    case "Bump Strength":
                        if (ch["current_value"] != null)
                        {
                            try { state.BumpStrength = ch["current_value"].AsFloat; }
                            catch { }
                        }
                        if (ch["image_file"] != null)
                        {
                            state.BumpPath = ch["image_file"].Value;
                            state.HasBumpMap = true;
                        }
                        break;
                    case "Normal Map":
                        if (ch["image_file"] != null && (!state.HasBumpMap || !state.ForceBumpAsNormalMap))
                        {
                            state.NormalPath = ch["image_file"].Value;
                            state.HasNormalMap = true;
                        }
                        break;
                    case "Subsurface Color":
                        if (ch["image_file"] != null) state.SubsurfaceColorPath = ch["image_file"].Value;
                        break;
                    case "Subsurface Strength":
                        if (ch["image_file"] != null) state.SubsurfaceStrengthPath = ch["image_file"].Value;
                        break;
                    case "Translucency Color":
                        if (ch["image_file"] != null) state.TranslucencyColorPath = ch["image_file"].Value;
                        break;
                    case "Translucency Strength":
                        if (ch["image_file"] != null) state.TranslucencyStrengthPath = ch["image_file"].Value;
                        break;
                }
            }
        }

        /// <summary>Mirror <see cref="MeshVR.DAZImportMaterial.ImportImages"/> CopyAndImportImage calls.</summary>
        private static void EmitImportImageEntries(MaterialState state, List<DazTextureEntry> outEntries)
        {
            AddEntry(outEntries, state.DiffusePath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: false, forceLinear: false, invert: false));
            AddEntry(outEntries, state.AlphaPath, CopyAndImportFlags(isNormalMap: false, isTransparency: true, isBumpMap: false, bumpStrength: 1f, isGlossMap: false, forceLinear: false, invert: false));
            AddEntry(outEntries, state.SpecularColorPath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: false, forceLinear: false, invert: false));
            AddEntry(outEntries, state.SpecularStrengthPath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: false, forceLinear: true, invert: false));
            AddEntry(outEntries, state.GlossPath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: true, forceLinear: false, invert: false));
            AddEntry(outEntries, state.RoughnessPath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: true, forceLinear: false, invert: true));

            if (!string.IsNullOrEmpty(state.BumpPath))
            {
                if (state.CopyBumpAsSpecularColorMap)
                {
                    AddEntry(outEntries, state.BumpPath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: false, forceLinear: true, invert: false));
                }

                AddEntry(outEntries, state.BumpPath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: true, bumpStrength: state.BumpStrength, isGlossMap: false, forceLinear: false, invert: false));
            }

            if (!string.IsNullOrEmpty(state.NormalPath) && (!state.HasBumpMap || !state.ForceBumpAsNormalMap))
            {
                AddEntry(outEntries, state.NormalPath, CopyAndImportFlags(isNormalMap: true, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: false, forceLinear: false, invert: false));
            }

            AddEntry(outEntries, state.SubsurfaceColorPath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: false, forceLinear: false, invert: false));
            AddEntry(outEntries, state.SubsurfaceStrengthPath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: false, forceLinear: true, invert: false));
            AddEntry(outEntries, state.TranslucencyColorPath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: false, forceLinear: false, invert: false));
            AddEntry(outEntries, state.TranslucencyStrengthPath, CopyAndImportFlags(isNormalMap: false, isTransparency: false, isBumpMap: false, bumpStrength: 1f, isGlossMap: false, forceLinear: true, invert: false));
        }

        /// <summary>Same assignments as DAZImportMaterial.CopyAndImportImage when building QueuedImage.</summary>
        public static NativeTextureOnDemandCache.TextureFlags CopyAndImportFlags(
            bool isNormalMap,
            bool isTransparency,
            bool isBumpMap,
            float bumpStrength,
            bool isGlossMap,
            bool forceLinear,
            bool invert)
        {
            var f = VaMTextureLoadFlags.DefaultImageLoaderFlags();
            f.isNormalMap = isNormalMap;
            f.linear = isNormalMap || isBumpMap || isGlossMap || forceLinear;
            f.createNormalFromBump = isBumpMap;
            f.bumpStrength = isBumpMap ? (0.5f * bumpStrength) : 1f;
            f.createAlphaFromGrayscale = isTransparency;
            f.compress = !isNormalMap && !isBumpMap;
            f.invert = invert;
            return f;
        }

        private static void AddEntry(List<DazTextureEntry> list, string path, NativeTextureOnDemandCache.TextureFlags flags)
        {
            if (list == null || string.IsNullOrEmpty(path) || path == "null") return;
            list.Add(new DazTextureEntry { ImagePath = path, Flags = flags });
        }

        private static bool ReadBool(JSONClass parent, string fieldName, bool defaultValue)
        {
            if (parent == null) return defaultValue;
            JSONNode node = parent[fieldName];
            if (node == null) return defaultValue;
            try { return node.AsBool; }
            catch { return defaultValue; }
        }
    }
}
