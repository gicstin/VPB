namespace VPB
{
    /// <summary>Scene-load timing milestones for LogUtil (optional verbose diagnostics).</summary>
    internal static class SceneLoadDiag
    {
        internal static void Reset() { }

        internal static void SceneLoadHeartbeat() { }

        internal static void Milestone(string message) { }

        internal static void LogSummary(string context) { }
    }

    /// <summary>Reset clothing load pacing state at scene boundaries.</summary>
    internal static class SceneLoadClothingPacer
    {
        internal static void ResetForSceneLoad() { }
    }

    /// <summary>Apply scene-load compatibility hooks (clothing pace, etc.).</summary>
    internal static class SceneLoadCompat
    {
        internal static void EnsureClothingPaceForSceneLoad() { }
    }
}
