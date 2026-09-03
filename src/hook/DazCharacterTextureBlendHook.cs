using System;
using HarmonyLib;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Safety net for VaM auto genital blend (<c>autoBlendGenitalTextures</c>).
    /// Native path: blend torso+genital via CPU GetPixels into a new RAM Texture2D, SetTexture on
    /// genital materials, Destroy previous lastGenerated*. Never written to .vamcache.
    /// VPB zstd/RAM serve historically used Apply(makeNoLongerReadable:true), so blend GetPixel
    /// failed and materials kept unblended/stale genital maps. Primary fix keeps CharacterQueuedImage
    /// readable in ImageLoadingMgr; this prefix covers any remaining non-readable inputs.
    /// </summary>
    internal static class DazCharacterTextureBlendHook
    {
        private struct BlendReadableTemps
        {
            public Texture2D TorsoTemp;
            public Texture2D GenTemp;
        }

        public static void PatchAll(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                var blend = AccessTools.Method(typeof(DAZCharacterTextureControl), "BlendGenitalTexture");
                if (blend == null)
                {
                    LogUtil.LogWarning("[VPB] DazCharacterTextureBlendHook: BlendGenitalTexture not found");
                    return;
                }

                harmony.Patch(blend,
                    prefix: new HarmonyMethod(typeof(DazCharacterTextureBlendHook), nameof(BlendGenitalTexture_Prefix)),
                    postfix: new HarmonyMethod(typeof(DazCharacterTextureBlendHook), nameof(BlendGenitalTexture_Postfix)));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] DazCharacterTextureBlendHook.PatchAll failed: " + ex.Message);
            }
        }

        private static void BlendGenitalTexture_Prefix(
            ref Texture2D inTorsoTex,
            ref Texture2D inGenTex,
            bool linear,
            out BlendReadableTemps __state)
        {
            __state = default(BlendReadableTemps);
            try
            {
                Texture2D torso = SuperControllerHook.EnsureCpuReadableTexture(inTorsoTex, linear, false, "BlendGenital");
                if (torso != null && torso != inTorsoTex)
                {
                    __state.TorsoTemp = torso;
                    inTorsoTex = torso;
                }

                Texture2D gen = SuperControllerHook.EnsureCpuReadableTexture(inGenTex, linear, false, "BlendGenital");
                if (gen != null && gen != inGenTex)
                {
                    __state.GenTemp = gen;
                    inGenTex = gen;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] BlendGenitalTexture_Prefix failed: " + ex.Message);
            }
        }

        private static void BlendGenitalTexture_Postfix(BlendReadableTemps __state)
        {
            try
            {
                if (__state.TorsoTemp != null)
                    UnityEngine.Object.Destroy(__state.TorsoTemp);
                if (__state.GenTemp != null)
                    UnityEngine.Object.Destroy(__state.GenTemp);
            }
            catch { }
        }
    }
}
