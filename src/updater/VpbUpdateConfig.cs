using System;
using System.IO;
using SimpleJSON;

namespace VPB
{
    public class VpbUpdateConfig
    {
        private const string FileName = "vpb_update_config.json";

        public string Branch = "main";
        public bool AutoCheck = false;
        public string LastCheckUtc = "";
        public string LastStagedVersion = "";

        private string _filePath;

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

                if (node["branch"] != null)
                    config.Branch = node["branch"].Value;
                if (node["autoCheck"] != null)
                    config.AutoCheck = node["autoCheck"].AsBool;
                if (node["lastCheckUtc"] != null)
                    config.LastCheckUtc = node["lastCheckUtc"].Value;
                if (node["lastStagedVersion"] != null)
                    config.LastStagedVersion = node["lastStagedVersion"].Value;
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
                node["branch"] = Branch ?? "main";
                node["autoCheck"].AsBool = AutoCheck;
                node["lastCheckUtc"] = LastCheckUtc ?? "";
                node["lastStagedVersion"] = LastStagedVersion ?? "";
                File.WriteAllText(_filePath, node.ToString());
            }
            catch { }
        }

        public void SetFilePath(string gameRoot)
        {
            _filePath = Path.Combine(gameRoot, FileName);
        }
    }
}
