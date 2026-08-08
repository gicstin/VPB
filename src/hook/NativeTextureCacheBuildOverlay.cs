namespace VPB
{
    /// <summary>Compatibility shim — use <see cref="VpbBusyChrome"/>.</summary>
    internal static class NativeTextureCacheBuildOverlay
    {
        public static void EnsureCreated()
        {
            VpbBusyChrome.EnsureCreated();
        }
    }
}
