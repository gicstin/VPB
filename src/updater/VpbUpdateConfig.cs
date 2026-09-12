using System;
using System.IO;
using SimpleJSON;

namespace VPB
{
    public class VpbUpdateConfig
    {
        private const string FileName = "vpb_update_config.json";

        public const string DefaultBranch = "main";

        public string Branch = DefaultBranch;
        public bool AutoCheck = false;
        public string LastCheckUtc = "";
        public string LastStagedVersion = "";

        public bool Pinned = false;
        public string PinnedVersion = "";
        public string PinnedTag = "";
        public int PinnedSchema = 0;

        private string _filePath;

        public string EffectiveRef
        {
            get
            {
                if (Pinned && !string.IsNullOrEmpty(PinnedTag)) return PinnedTag;
                return string.IsNullOrEmpty(Branch) ? DefaultBranch : Branch;
            }
        }

        public static VpbUpdateConfig Load(string gameRoot)
        {
            var config = new VpbUpdateConfig();
            config._filePath = Path.Combine(gameRoot, FileName);

            if (!File.Exists(config._filePath))
                return config;

            try
            {
                var json = File.ReadAllText(config._filePath);
                var node = JSON.Parse(json);
                if (node == null) return config;

                if (node["autoCheck"] != null)
                    config.AutoCheck = node["autoCheck"].AsBool;
                if (node["lastCheckUtc"] != null)
                    config.LastCheckUtc = node["lastCheckUtc"].Value;
                if (node["lastStagedVersion"] != null)
                    config.LastStagedVersion = node["lastStagedVersion"].Value;
                if (node["pinned"] != null)
                    config.Pinned = node["pinned"].AsBool;
                if (node["pinnedVersion"] != null)
                    config.PinnedVersion = node["pinnedVersion"].Value;
                if (node["pinnedTag"] != null)
                    config.PinnedTag = node["pinnedTag"].Value;
                if (node["pinnedSchema"] != null)
                    config.PinnedSchema = node["pinnedSchema"].AsInt;

                if (node["channel"] != null && !string.IsNullOrEmpty(node["channel"].Value))
                    config.Branch = node["channel"].Value;
                else if (node["branch"] != null && !string.IsNullOrEmpty(node["branch"].Value))
                    config.Branch = node["branch"].Value;

                if (config.Pinned && string.IsNullOrEmpty(config.PinnedTag))
                    config.Pinned = false;

                if (config.Pinned && config.Branch == config.PinnedTag)
                    config.Branch = DefaultBranch;
            }
            catch { }

            return config;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(_filePath)) return;

            try
            {
                var node = new JSONClass();
                node["branch"] = EffectiveRef;
                node["channel"] = string.IsNullOrEmpty(Branch) ? DefaultBranch : Branch;
                node["autoCheck"].AsBool = AutoCheck;
                node["lastCheckUtc"] = LastCheckUtc ?? "";
                node["lastStagedVersion"] = LastStagedVersion ?? "";
                node["pinned"].AsBool = Pinned;
                node["pinnedVersion"] = PinnedVersion ?? "";
                node["pinnedTag"] = PinnedTag ?? "";
                node["pinnedSchema"].AsInt = PinnedSchema;
                File.WriteAllText(_filePath, VPB.src.util.JsonSerializationUtil.Serialize(node, 1024));
            }
            catch { }
        }

        public void SetFilePath(string gameRoot)
        {
            _filePath = Path.Combine(gameRoot, FileName);
        }
    }
}
