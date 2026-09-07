using System;
using System.IO;
using System.Reflection;

namespace VPB.Tests
{
    public sealed class TempInstall : IDisposable
    {
        private readonly string _previousCwd;

        public string Root { get; }
        public string PluginDataDir { get; }
        public string AddonPackagesDir { get; }

        public TempInstall(string label = "vpb")
        {
            Root = Path.Combine(Path.GetTempPath(), "vpbtest_" + label + "_" + Guid.NewGuid().ToString("N"));
            PluginDataDir = Combine(Root, "Saves", "PluginData", "VPB");
            AddonPackagesDir = Path.Combine(Root, "AddonPackages");

            Directory.CreateDirectory(PluginDataDir);
            Directory.CreateDirectory(AddonPackagesDir);
            Directory.CreateDirectory(Combine(Root, "Cache", "VPB"));
            Directory.CreateDirectory(Combine(Root, "Saves", "scene"));
            Directory.CreateDirectory(Path.Combine(Root, "Custom"));

            _previousCwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Root);
            ResetPluginDataLatch();
            VPBConfig.ReloadFromDisk();
        }

        public string DatabasePath
        {
            get { return VpbLocalDatabase.GetLocalDatabasePathForDiagnostics(); }
        }

        private static void ResetPluginDataLatch()
        {
            SetStatic(typeof(GlobalInfo), "pathsInitialized", false);
            SetStatic(typeof(GlobalInfo), "pluginInfoDirectory", null);
            SetStatic(typeof(VpbHideIndex), "s_prefsDirFull", null);
            VpbHideIndex.Invalidate();
        }

        private static void SetStatic(Type type, string fieldName, object value)
        {
            FieldInfo f = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null)
                throw new InvalidOperationException(
                    type.Name + "." + fieldName + " no longer exists. TempInstall resets it so each sandbox gets its " +
                    "own Saves/PluginData/VPB path; without the reset every test after the first writes to the first " +
                    "sandbox. Update TempInstall.ResetPluginDataLatch.");
            f.SetValue(null, value);
        }

        public string PathTo(params string[] parts)
        {
            string p = Root;
            foreach (string part in parts) p = Path.Combine(p, part);
            return p;
        }

        public string EnsureDir(params string[] parts)
        {
            string p = PathTo(parts);
            Directory.CreateDirectory(p);
            return p;
        }

        private static string Combine(string a, string b, string c) { return Path.Combine(Path.Combine(a, b), c); }
        private static string Combine(string a, string b, string c, string d) { return Path.Combine(Combine(a, b, c), d); }

        public void Dispose()
        {
            try { Directory.SetCurrentDirectory(_previousCwd); } catch { }
            ResetPluginDataLatch();
            VPBConfig.ReloadFromDisk();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { Directory.Delete(Root, true); return; }
                catch (IOException) { System.Threading.Thread.Sleep(50); }
                catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(50); }
                catch { return; }
            }
        }
    }
}
