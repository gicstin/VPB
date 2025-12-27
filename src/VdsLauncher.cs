using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace var_browser
{
    static class VdsLauncher
    {
        sealed class VdsRequest
        {
            public bool Enabled;
            public string Scene;
            public string Mode;
            public readonly Dictionary<string, string> Flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        static bool parsed;
        static bool executed;
        static bool overridesApplied;
        static int parseFrame;
        static VdsRequest request;

        static bool restoreHooked;
        static readonly Dictionary<object, object> originalConfigEntryValues = new Dictionary<object, object>();
        static object configFile;
        static bool? configSaveOnConfigSetOriginal;
        static bool configBackupDone;

        static string NormalizeArgValue(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            string s = v.Trim();
            if (s.Length >= 2)
            {
                if ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '\'' && s[s.Length - 1] == '\''))
                {
                    s = s.Substring(1, s.Length - 2);
                }
            }
            return s.Trim();
        }

        public static void ParseOnce()
        {
            if (parsed) return;
            parsed = true;
            parseFrame = Time.frameCount;

            var req = new VdsRequest();
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        string arg = args[i];
                        if (string.IsNullOrEmpty(arg)) continue;

                        if (arg.Equals("--vpb.vds", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("--vpb.vds=", StringComparison.OrdinalIgnoreCase))
                        {
                            req.Enabled = true;
                            continue;
                        }

                        if (!arg.StartsWith("--vpb.vds.", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        req.Enabled = true;

                        int eq = arg.IndexOf('=');
                        string key;
                        string val;
                        if (eq >= 0)
                        {
                            key = arg.Substring("--vpb.vds.".Length, eq - "--vpb.vds.".Length);
                            val = arg.Substring(eq + 1);
                        }
                        else
                        {
                            key = arg.Substring("--vpb.vds.".Length);
                            val = "";
                            if (i + 1 < args.Length)
                            {
                                string next = args[i + 1];
                                if (!string.IsNullOrEmpty(next) && !next.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                                {
                                    val = next;
                                    i++;
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(key)) continue;

                        val = NormalizeArgValue(val);
                        req.Flags[key] = val;
                        if (key.Equals("scene", StringComparison.OrdinalIgnoreCase)) req.Scene = val;
                        if (key.Equals("mode", StringComparison.OrdinalIgnoreCase)) req.Mode = val;
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("VDS parse failed: " + ex.Message);
            }

            request = req;
            if (req.Enabled)
            {
                LogUtil.Log("VDS detected");
            }
        }

        public static void TryExecuteOnce()
        {
            if (!parsed || executed) return;
            if (request == null || !request.Enabled) return;

            if ((Time.frameCount - parseFrame) < 10) return;

            var sc = SuperController.singleton;
            if (sc == null) return;

            ApplyOverridesOnce();

            if (string.IsNullOrEmpty(request.Scene))
            {
                LogUtil.LogError("VDS missing --vpb.vds.scene");
                executed = true;
                return;
            }

            string resolved = ResolveScene(request.Scene);
            if (string.IsNullOrEmpty(resolved))
            {
                LogUtil.LogError("VDS could not resolve scene: " + request.Scene);
                executed = true;
                return;
            }

            try
            {
                // In normal UI usage, VPB starts the "scene click" timer when the user clicks a scene file.
                // For VDS launches there is no click, so start it here to keep timing/telemetry consistent.
                LogUtil.BeginSceneClick(resolved);
                if (!LogUtil.IsSceneLoadActive())
                {
                    LogUtil.BeginSceneLoad(resolved);
                }
            }
            catch { }

            LogUtil.Log("VDS loading scene: " + resolved);
            bool ok = InvokeLoad(sc, resolved);
            if (!ok)
            {
                LogUtil.LogError("VDS could not invoke scene load");
            }
            executed = true;
        }

        static void ApplyOverridesOnce()
        {
            if (overridesApplied) return;
            overridesApplied = true;

            if (!restoreHooked)
            {
                restoreHooked = true;
                try
                {
                    Application.quitting += RestoreOriginalSettings;
                }
                catch { }
            }

            try
            {
                if (request == null || request.Flags == null || request.Flags.Count == 0) return;

                string clearTexDisk;
                if (request.Flags.TryGetValue("cache.textures.clearDisk", out clearTexDisk) || request.Flags.TryGetValue("cache.textures.overwrite", out clearTexDisk))
                {
                    if (ParseBool(clearTexDisk))
                    {
                        TryClearDirectory(VamHookPlugin.GetCacheDir());
                    }
                }

                string clearTexMem;
                if (request.Flags.TryGetValue("cache.textures.clearMem", out clearTexMem))
                {
                    if (ParseBool(clearTexMem))
                    {
                        if (ImageLoadingMgr.singleton != null)
                        {
                            ImageLoadingMgr.singleton.ClearCache();
                        }
                    }
                }

                string clearAbDisk;
                if (request.Flags.TryGetValue("cache.ab.clearDisk", out clearAbDisk))
                {
                    if (ParseBool(clearAbDisk))
                    {
                        TryClearDirectory(VamHookPlugin.GetAssetBundleCacheDir());
                    }
                }

                foreach (var kv in request.Flags)
                {
                    if (kv.Key == null) continue;
                    if (!kv.Key.StartsWith("set.", StringComparison.OrdinalIgnoreCase)) continue;

                    string settingName = kv.Key.Substring("set.".Length);
                    if (string.IsNullOrEmpty(settingName)) continue;

                    TryApplySetting(settingName, kv.Value);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("VDS overrides failed: " + ex.Message);
            }
        }

        static void RestoreOriginalSettings()
        {
            try
            {
                foreach (var kv in originalConfigEntryValues)
                {
                    var entry = kv.Key;
                    if (entry == null) continue;
                    var entryType = entry.GetType();
                    var prop = entryType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                    if (prop == null || !prop.CanWrite) continue;
                    try { prop.SetValue(entry, kv.Value, null); } catch { }
                }

                RestoreConfigAutosave();
            }
            catch { }
        }

        static void RestoreConfigAutosave()
        {
            try
            {
                if (configFile == null || !configSaveOnConfigSetOriginal.HasValue) return;
                var prop = configFile.GetType().GetProperty("SaveOnConfigSet", BindingFlags.Instance | BindingFlags.Public);
                if (prop == null || !prop.CanWrite) return;
                prop.SetValue(configFile, configSaveOnConfigSetOriginal.Value, null);
            }
            catch { }
        }

        static void DisableConfigAutosaveAndBackupOnce()
        {
            try
            {
                if (Settings.Instance == null) return;
                if (configFile == null)
                {
                    // Grab the ConfigFile from any ConfigEntry (all of these belong to the same config file).
                    var t = Settings.Instance.GetType();
                    var f = t.GetField("UIKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null)
                    {
                        var entry = f.GetValue(Settings.Instance);
                        if (entry != null)
                        {
                            var cfProp = entry.GetType().GetProperty("ConfigFile", BindingFlags.Instance | BindingFlags.Public);
                            if (cfProp != null)
                            {
                                configFile = cfProp.GetValue(entry, null);
                            }
                        }
                    }
                }

                if (configFile == null) return;

                // Backup the config file once per session, best-effort.
                if (!configBackupDone)
                {
                    configBackupDone = true;
                    try
                    {
                        var pathProp = configFile.GetType().GetProperty("ConfigFilePath", BindingFlags.Instance | BindingFlags.Public);
                        if (pathProp != null)
                        {
                            var pathObj = pathProp.GetValue(configFile, null);
                            var path = pathObj as string;
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                var bak = path + ".vds.bak";
                                if (!File.Exists(bak))
                                {
                                    File.Copy(path, bak, false);
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Disable autosave during session overrides so a crash does not persist temporary values.
                var saveProp = configFile.GetType().GetProperty("SaveOnConfigSet", BindingFlags.Instance | BindingFlags.Public);
                if (saveProp != null && saveProp.CanRead && saveProp.CanWrite)
                {
                    if (!configSaveOnConfigSetOriginal.HasValue)
                    {
                        try
                        {
                            var cur = saveProp.GetValue(configFile, null);
                            if (cur is bool b) configSaveOnConfigSetOriginal = b;
                        }
                        catch { }
                    }
                    saveProp.SetValue(configFile, false, null);
                }
            }
            catch { }
        }

        static void TryClearDirectory(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!Directory.Exists(path)) return;

                string[] files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }

                string[] dirs = Directory.GetDirectories(path, "*", SearchOption.AllDirectories);
                for (int i = dirs.Length - 1; i >= 0; i--)
                {
                    try { Directory.Delete(dirs[i], false); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("VDS clear cache failed: " + ex.Message);
            }
        }

        static bool ParseBool(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            string s = NormalizeArgValue(v);
            if (string.IsNullOrEmpty(s)) return false;

            if (s.Equals("1") || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("0") || s.Equals("false", StringComparison.OrdinalIgnoreCase) || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;

            bool b;
            if (bool.TryParse(s, out b)) return b;
            return false;
        }

        static void TryApplySetting(string fieldName, string rawValue)
        {
            try
            {
                if (Settings.Instance == null) return;

                DisableConfigAutosaveAndBackupOnce();

                var t = Settings.Instance.GetType();
                var f = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null) return;

                object entry = f.GetValue(Settings.Instance);
                if (entry == null) return;

                var entryType = entry.GetType();
                if (!entryType.IsGenericType) return;

                var genericDef = entryType.GetGenericTypeDefinition();
                if (genericDef == null) return;
                if (!string.Equals(genericDef.FullName, "BepInEx.Configuration.ConfigEntry`1", StringComparison.Ordinal)) return;

                Type valueType = entryType.GetGenericArguments()[0];
                object converted;
                if (!TryConvertValue(valueType, rawValue, out converted)) return;

                var prop = entryType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                if (prop == null || !prop.CanWrite) return;

                if (!originalConfigEntryValues.ContainsKey(entry))
                {
                    try
                    {
                        originalConfigEntryValues[entry] = prop.GetValue(entry, null);
                    }
                    catch { }
                }

                prop.SetValue(entry, converted, null);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("VDS set failed: " + fieldName + "=" + rawValue + " | " + ex.Message);
            }
        }

        static bool TryConvertValue(Type t, string raw, out object result)
        {
            result = null;
            string s = NormalizeArgValue(raw);
            if (t == typeof(string))
            {
                result = s;
                return true;
            }
            if (t == typeof(bool))
            {
                result = ParseBool(s);
                return true;
            }
            if (t == typeof(int))
            {
                int v;
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
                result = v;
                return true;
            }
            if (t == typeof(float))
            {
                float v;
                if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
                result = v;
                return true;
            }
            if (t == typeof(Vector2))
            {
                string[] parts = s.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts == null || parts.Length != 2) return false;
                float x;
                float y;
                if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
                if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
                result = new Vector2(x, y);
                return true;
            }

            try
            {
                result = Convert.ChangeType(s, t, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static string ResolveScene(string sceneSpec)
        {
            if (string.IsNullOrEmpty(sceneSpec)) return null;
            string s = NormalizeArgValue(sceneSpec);
            if (s.Length == 0) return null;

            string sNorm = s.Replace('\\', '/');

            if (sNorm.IndexOf(":/Saves/scene/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return s;
            }

            if (sNorm.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase))
            {
                return s.Replace('/', '\\');
            }

            string fileName = s;
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".json";
            }

            try
            {
                if (!Directory.Exists("Saves/scene")) return null;

                string[] matches = Directory.GetFiles("Saves/scene", fileName, SearchOption.AllDirectories);
                if (matches != null && matches.Length == 1)
                {
                    return matches[0];
                }
                if (matches != null && matches.Length > 1)
                {
                    LogUtil.LogError("VDS scene ambiguous: " + sceneSpec);
                    for (int i = 0; i < matches.Length && i < 10; i++)
                    {
                        LogUtil.LogError("VDS match: " + matches[i]);
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("VDS resolve failed: " + ex.Message);
            }

            return null;
        }

        static bool InvokeLoad(SuperController sc, string saveName)
        {
            try
            {
                var t = sc.GetType();

                var m1 = t.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(string) }, null);
                if (m1 != null)
                {
                    m1.Invoke(sc, new object[] { saveName });
                    return true;
                }

                var m3 = t.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(string), typeof(bool), typeof(bool) }, null);
                if (m3 != null)
                {
                    m3.Invoke(sc, new object[] { saveName, false, false });
                    return true;
                }

                var mi = t.GetMethod("LoadInternal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(string), typeof(bool), typeof(bool) }, null);
                if (mi != null)
                {
                    mi.Invoke(sc, new object[] { saveName, false, false });
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("VDS invoke failed: " + ex.Message);
            }

            return false;
        }
    }
}
