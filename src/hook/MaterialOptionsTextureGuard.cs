using HarmonyLib;
using System;
using System.Collections.Generic;

namespace VPB
{
    /// <summary>
    /// Issue #80 (second half): last-writer-wins race on MaterialOptions custom texture slots.
    ///
    /// A dynamic garment restores its OWN baked customTexture_* urls from its .vaj
    /// (MeshVR.DAZDynamic.Load -> RestoreStorables), then the preset overwrites the same slots with
    /// the user's urls. Vanilla VaM is safe because ImageLoaderThreaded is a single FIFO worker, so
    /// the preset's OnTexture*Loaded always lands last. VPB replaces that queue with an unordered
    /// ThreadPool decompress + per-frame main-thread pump, so completion order follows payload size:
    /// a small user texture finishes first and the garment's 4K factory texture lands after it and
    /// overwrites the slot. The url JSON is never rewritten, so the UI still shows the correct path
    /// while the GPU holds the default texture.
    ///
    /// Fix: make the url authoritative instead of arrival order. Record the url last queued for each
    /// (MaterialOptions, slot) and drop any OnTexture*Loaded whose imgPath is no longer the expected
    /// one. Restores exactly the guarantee FIFO used to provide, without serialising the loader.
    ///
    /// Fail-open by design: a slot with no recorded expectation always accepts its callback, so if a
    /// patch fails to install nothing changes behaviourally.
    /// </summary>
    public static class MaterialOptionsTextureGuard
    {
        internal const int SlotCount = 7;   // customTexture1..6 + simTexture
        internal const int SimSlot = 6;

        // Guard against unbounded growth if a reset is ever missed; clearing only costs a
        // one-time fail-open window, never correctness of already-applied slots.
        const int MaxTrackedMaterialOptions = 4096;

        sealed class SlotState
        {
            // Url last handed to QueueCustomTexture for this slot (what SHOULD win).
            public readonly string[] Expected = new string[SlotCount];
            // Url of the callback we last let through (what the slot actually holds).
            public readonly string[] Applied = new string[SlotCount];
        }

        // Keyed on MaterialOptions.GetInstanceID() — int keys compare far faster than
        // UnityEngine.Object references on Mono. Written from QueueCustomTexture and read from the
        // image callback; both are main-thread in practice, but ImageLoadingMgr has a
        // Messager-is-null fallback that can call back off-thread, so keep the access locked.
        static readonly Dictionary<int, SlotState> s_slots = new Dictionary<int, SlotState>();
        static readonly object s_lock = new object();

        static bool s_installed;

        public static void PatchAll(Harmony harmony)
        {
            if (s_installed) return;

            try
            {
                var mQueue = AccessTools.Method(typeof(MaterialOptions), "QueueCustomTexture");
                if (mQueue == null)
                {
                    LogUtil.LogWarning("MaterialOptionsTextureGuard: MaterialOptions.QueueCustomTexture not found; custom texture ordering guard disabled.");
                    return;
                }

                harmony.Patch(mQueue, prefix: new HarmonyMethod(typeof(MaterialOptionsTextureGuard), nameof(PreQueueCustomTexture)));

                // One thin prefix per slot so the slot index is a compile-time constant.
                PatchSlot(harmony, typeof(MaterialOptions), "OnTexture1Loaded", nameof(PreOnTexture1Loaded));
                PatchSlot(harmony, typeof(MaterialOptions), "OnTexture2Loaded", nameof(PreOnTexture2Loaded));
                PatchSlot(harmony, typeof(MaterialOptions), "OnTexture3Loaded", nameof(PreOnTexture3Loaded));
                PatchSlot(harmony, typeof(MaterialOptions), "OnTexture4Loaded", nameof(PreOnTexture4Loaded));
                PatchSlot(harmony, typeof(MaterialOptions), "OnTexture5Loaded", nameof(PreOnTexture5Loaded));
                PatchSlot(harmony, typeof(MaterialOptions), "OnTexture6Loaded", nameof(PreOnTexture6Loaded));
                PatchSlot(harmony, typeof(DAZSkinWrapMaterialOptions), "OnSimTextureLoaded", nameof(PreOnSimTextureLoaded));

                s_installed = true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("MaterialOptionsTextureGuard PatchAll failed: " + ex);
            }
        }

        static void PatchSlot(Harmony harmony, Type declaringType, string methodName, string prefixName)
        {
            var m = AccessTools.Method(declaringType, methodName);
            if (m == null)
            {
                LogUtil.LogWarning("MaterialOptionsTextureGuard: " + declaringType.Name + "." + methodName
                    + " not found; that slot keeps last-writer-wins behaviour.");
                return;
            }
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(MaterialOptionsTextureGuard), prefixName));
        }

        /// <summary>Drop every recorded expectation. Scene load start and plugin teardown.</summary>
        public static void Reset()
        {
            lock (s_lock)
            {
                s_slots.Clear();
            }
        }

        // ---- slot naming -------------------------------------------------------------------

        /// <summary>
        /// VaM registers the url params as "customTexture" + textureGroup1.&lt;name&gt;, i.e.
        /// customTexture_MainTex / _SpecTex / _GlossTex / _AlphaTex / _BumpMap / _DecalTex — NOT
        /// customTexture1Url. Only the tile/offset FLOAT params are numbered. Returns 0..5, or -1.
        /// </summary>
        public static int GetCustomTextureSlotForUrlParam(MaterialOptions mo, string urlParamName)
        {
            if (mo == null || string.IsNullOrEmpty(urlParamName)) return -1;
            if (!urlParamName.StartsWith("customTexture", StringComparison.Ordinal)) return -1;

            MaterialOptionTextureGroup tg = null;
            try { tg = mo.textureGroup1; } catch { }
            if (tg == null) return -1;

            string suffix = urlParamName.Substring("customTexture".Length);
            if (suffix.Length == 0) return -1;

            // Checked in the same order SetStartingValues assigns them, so a group that repeats a
            // texture name resolves to the same slot VaM itself would use.
            if (string.Equals(suffix, tg.textureName, StringComparison.Ordinal)) return 0;
            if (string.Equals(suffix, tg.secondaryTextureName, StringComparison.Ordinal)) return 1;
            if (string.Equals(suffix, tg.thirdTextureName, StringComparison.Ordinal)) return 2;
            if (string.Equals(suffix, tg.fourthTextureName, StringComparison.Ordinal)) return 3;
            if (string.Equals(suffix, tg.fifthTextureName, StringComparison.Ordinal)) return 4;
            if (string.Equals(suffix, tg.sixthTextureName, StringComparison.Ordinal)) return 5;
            return -1;
        }

        static int SlotForCallback(ImageLoaderThreaded.ImageLoaderCallback callback)
        {
            if (callback == null) return -1;

            string name;
            try
            {
                var m = callback.Method;
                if (m == null) return -1;
                name = m.Name;
            }
            catch { return -1; }

            if (string.IsNullOrEmpty(name)) return -1;

            switch (name)
            {
                case "OnTexture1Loaded": return 0;
                case "OnTexture2Loaded": return 1;
                case "OnTexture3Loaded": return 2;
                case "OnTexture4Loaded": return 3;
                case "OnTexture5Loaded": return 4;
                case "OnTexture6Loaded": return 5;
                case "OnSimTextureLoaded": return SimSlot;
                default: return -1;
            }
        }

        // ---- state -------------------------------------------------------------------------

        static bool SameUrl(string a, string b)
        {
            // QueueCustomTexture(null) is how VaM clears a slot; treat null and "" as one value.
            bool aEmpty = string.IsNullOrEmpty(a);
            bool bEmpty = string.IsNullOrEmpty(b);
            if (aEmpty || bEmpty) return aEmpty && bEmpty;
            return string.Equals(a, b, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the slot currently holds the texture loaded for <paramref name="url"/>.
        /// Lets the deferred resync skip re-queueing slots that are already correct, while still
        /// re-queueing a slot whose newest url never arrived.
        /// </summary>
        public static bool IsSlotAppliedForUrl(MaterialOptions mo, int slot, string url)
        {
            if (mo == null || slot < 0 || slot >= SlotCount) return false;

            int id;
            try { id = mo.GetInstanceID(); } catch { return false; }

            lock (s_lock)
            {
                SlotState st;
                if (!s_slots.TryGetValue(id, out st)) return false;
                return SameUrl(st.Applied[slot], url);
            }
        }

        static void NoteQueued(MaterialOptions mo, int slot, string url)
        {
            if (mo == null || slot < 0 || slot >= SlotCount) return;

            int id;
            try { id = mo.GetInstanceID(); } catch { return; }

            lock (s_lock)
            {
                SlotState st;
                if (!s_slots.TryGetValue(id, out st))
                {
                    if (s_slots.Count >= MaxTrackedMaterialOptions)
                    {
                        // Never happens with a reset per scene load; bound it anyway rather than
                        // grow a static forever (a stale entry can only ever fail open).
                        s_slots.Clear();
                    }
                    st = new SlotState();
                    s_slots[id] = st;
                }
                st.Expected[slot] = url;
            }
        }

        /// <summary>
        /// Accept only the callback for the url this slot is currently waiting on. Marks the slot
        /// applied on accept. Unknown slot / no expectation recorded => accept.
        /// </summary>
        static bool ShouldAccept(MaterialOptions mo, int slot, ImageLoaderThreaded.QueuedImage qi)
        {
            if (mo == null || qi == null || slot < 0 || slot >= SlotCount) return true;

            // Let the original run so it emits VaM's own load-error message.
            if (qi.hadError) return true;

            int id;
            try { id = mo.GetInstanceID(); } catch { return true; }

            string arriving = qi.imgPath;

            lock (s_lock)
            {
                SlotState st;
                if (!s_slots.TryGetValue(id, out st)) return true;   // fail open

                if (!SameUrl(st.Expected[slot], arriving))
                    return false;

                st.Applied[slot] = arriving;
                return true;
            }
        }

        static void LogDropped(MaterialOptions mo, int slot, ImageLoaderThreaded.QueuedImage qi)
        {
            try
            {
                if (Settings.Instance == null || Settings.Instance.TextureLogLevel == null) return;
                if (Settings.Instance.TextureLogLevel.Value < 1) return;

                string expected = null;
                int id = mo.GetInstanceID();
                lock (s_lock)
                {
                    SlotState st;
                    if (s_slots.TryGetValue(id, out st)) expected = st.Expected[slot];
                }

                LogUtil.Log("[VPB TEX] Dropped stale custom texture callback: mo=" + mo.name
                    + " slot=" + (slot == SimSlot ? "sim" : (slot + 1).ToString())
                    + " arrived='" + (qi.imgPath ?? "<null>") + "'"
                    + " expected='" + (expected ?? "<null>") + "'");
            }
            catch { }
        }

        // ---- patches -----------------------------------------------------------------------

        public static void PreQueueCustomTexture(
            MaterialOptions __instance,
            string url,
            ImageLoaderThreaded.ImageLoaderCallback callback)
        {
            try
            {
                int slot = SlotForCallback(callback);
                if (slot < 0) return;
                NoteQueued(__instance, slot, url);
            }
            catch (Exception ex)
            {
                // A throwing prefix propagates into VaM's own call — never let that happen.
                LogUtil.LogWarning("MaterialOptionsTextureGuard: queue note failed: " + ex.Message);
            }
        }

        static bool Gate(MaterialOptions mo, int slot, ImageLoaderThreaded.QueuedImage qi)
        {
            try
            {
                if (ShouldAccept(mo, slot, qi)) return true;
                LogDropped(mo, slot, qi);
                return false;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("MaterialOptionsTextureGuard: gate failed: " + ex.Message);
                return true;
            }
        }

        public static bool PreOnTexture1Loaded(MaterialOptions __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            return Gate(__instance, 0, qi);
        }

        public static bool PreOnTexture2Loaded(MaterialOptions __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            return Gate(__instance, 1, qi);
        }

        public static bool PreOnTexture3Loaded(MaterialOptions __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            return Gate(__instance, 2, qi);
        }

        public static bool PreOnTexture4Loaded(MaterialOptions __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            return Gate(__instance, 3, qi);
        }

        public static bool PreOnTexture5Loaded(MaterialOptions __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            return Gate(__instance, 4, qi);
        }

        public static bool PreOnTexture6Loaded(MaterialOptions __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            return Gate(__instance, 5, qi);
        }

        public static bool PreOnSimTextureLoaded(DAZSkinWrapMaterialOptions __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            return Gate(__instance, SimSlot, qi);
        }
    }
}
