using System.Collections.Generic;

namespace VPB
{
    internal enum VpbBenchMode
    {
        Baseline,
        Compare
    }

    internal enum VpbBenchTrigger
    {
        Hotkey,
        Auto,
        Manual
    }

    internal sealed class VpbBenchCandidateConfig
    {
        public string Id;
        public string Label;
        public readonly List<string> PackageUids = new List<string>();
    }

    internal sealed class VpbBenchConfig
    {
        public bool Enabled;
        public VpbBenchMode Mode = VpbBenchMode.Baseline;
        public string BaselineId;
        public VpbBenchTrigger Trigger = VpbBenchTrigger.Hotkey;
        public string Hotkey = "Ctrl+F10";
        public float AutoDelaySeconds = 15f;
        public bool TrackStartup = true;
        public bool TrackSceneLoad = true;
        public int MaxCandidates = 4;
        public readonly List<string> SelectedCandidates = new List<string>();
        public bool ClearTextureCache;
        public bool ClearAbCache;
        public VpbBenchPresetKind Preset = VpbBenchPresetKind.Standard;
        public float WaitBetweenScenesSeconds = 5f;
        public bool CaptureSceneScreenshots = true;
        public bool ReloadSceneBetweenRuns = true;
        public string OutputDir = "bench";
        public readonly List<string> Scenes = new List<string>();
        public readonly Dictionary<string, VpbBenchCandidateConfig> Candidates =
            new Dictionary<string, VpbBenchCandidateConfig>(System.StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> SettingOverrides =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        public string SourceConfigPath;
    }
}
