using ICSharpCode.SharpZipLib.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using UnityEngine;
using Prime31.MessageKit;

namespace VPB
{
    public class FileManager : MonoBehaviour
    {
        public static bool IsScanning { get { return singleton != null && singleton.m_Co != null; } }

        public delegate void OnRefresh();

        public static bool debug;

        public static FileManager singleton;

        Coroutine m_Co = null;
        Coroutine m_RefreshCo = null;
        Coroutine m_StartScanCo = null;
        bool m_RefreshPending = false;
        bool m_RefreshPendingInit = false;
        bool m_RefreshPendingClean = false;
        bool m_RefreshPendingRemoveOldVersion = false;

        protected static Dictionary<string, VarPackage> packagesByUid;
        public static Dictionary<string, VarPackage> PackagesByUid
        {
            get
            {
                return packagesByUid;

            }
        }

        protected static Dictionary<string, VarPackage> packagesByPath;

        protected static Dictionary<string, VarPackageGroup> packageGroups;
        protected static HashSet<VarFileEntry> allVarFileEntries;
        protected static Dictionary<string, VarFileEntry> uidToVarFileEntry;
        protected static Dictionary<string, VarFileEntry> pathToVarFileEntry;
        protected static OnRefresh onRefreshHandlers;
        protected static HashSet<string> restrictedReadPaths;

        protected static HashSet<string> secureReadPaths;

        protected static HashSet<string> secureInternalWritePaths;

        protected static HashSet<string> securePluginWritePaths;

        protected static HashSet<string> pluginWritePathsThatDoNotNeedConfirm;

        public Transform userConfirmContainer;

        public Transform userConfirmPrefab;

        public Transform userConfirmPluginActionPrefab;

        protected static Dictionary<string, string> pluginHashToPluginPath;

        //protected AsyncFlag userConfirmFlag;

        //protected static HashSet<string> userConfirmedPlugins;

        //protected static HashSet<string> userDeniedPlugins;

        protected static LinkedList<string> loadDirStack;

        public static int s_InstalledCount = 0;
        public static DateTime lastPackageRefreshTime
        {
            get;
            protected set;
        }

        public static string CurrentLoadDir
        {
            get
            {
                if (loadDirStack != null && loadDirStack.Count > 0)
                {
                    return loadDirStack.Last.Value;
                }
                return null;
            }
        }

        public static string CurrentPackageUid
        {
            get
            {
                string currentLoadDir = CurrentLoadDir;
                if (currentLoadDir != null)
                {
                    VarDirectoryEntry varDirectoryEntry = GetVarDirectoryEntry(currentLoadDir);
                    if (varDirectoryEntry != null)
                    {
                        return varDirectoryEntry.Package.Uid;
                    }
                }
                return null;
            }
        }

        public static string TopLoadDir
        {
            get
            {
                if (loadDirStack != null && loadDirStack.Count > 0)
                {
                    return loadDirStack.First.Value;
                }
                return null;
            }
        }

        public static string TopPackageUid
        {
            get
            {
                string topLoadDir = TopLoadDir;
                if (topLoadDir != null)
                {
                    VarDirectoryEntry varDirectoryEntry = GetVarDirectoryEntry(topLoadDir);
                    if (varDirectoryEntry != null)
                    {
                        return varDirectoryEntry.Package.Uid;
                    }
                }
                return null;
            }
        }

        public static string CurrentSaveDir
        {
            get;
            protected set;
        }

        protected static string packagePathToUid(string vpath)
        {
            string input = vpath.Replace('\\', '/');
            input = Regex.Replace(input, "\\.(var|zip)$", string.Empty);
            return Regex.Replace(input, ".*/", string.Empty);
        }

        protected static VarPackage RegisterPackage(string vpath, bool clean = false)
        {
            if (debug)
            {
                LogUtil.Log("RegisterPackage " + vpath);
            }
            string cleanPath = CleanFilePath(vpath);
            string text = packagePathToUid(cleanPath).Trim();
            string[] array = text.Split('.');

            bool isDuplicated = false;
            if (array.Length == 3)
            {
                string text2 = array[0];
                string text3 = array[1];
                string shortName = text2 + "." + text3;
                string s = array[2];
                try
                {
                    int version;
                    if (!int.TryParse(s, out version))
                    {
                        // Relaxed parsing for malformed versions like "1_1" -> 11, "1 (1)" -> 11
                        string cleanS = Regex.Replace(s, "[^0-9]", "");
                        if (!int.TryParse(cleanS, out version))
                        {
                            throw new FormatException($"Cannot parse version from '{s}'");
                        }
                        LogUtil.LogWarning($"[VPB] Parsed malformed version '{s}' as '{version}' for package {text}");
                    }

                    if (!packagesByUid.ContainsKey(text))
                    {
                        VarPackageGroup value;
                        if (!packageGroups.TryGetValue(shortName, out value))
                        {
                            value = new VarPackageGroup(shortName);
                            packageGroups.Add(shortName, value);
                        }
                        VarPackage varPackage = new VarPackage(text, cleanPath, value, text2, text3, version);
                        packagesByUid.Add(text, varPackage);

                        packagesByPath.Add(varPackage.Path, varPackage);
                        value.AddPackage(varPackage);

                        // Disabling a var package means creating a "disable" file in the same path
                        if (varPackage.Enabled)
                        {
                            if (varPackage.FileEntries != null)
                            {
                                foreach (VarFileEntry fileEntry in varPackage.FileEntries)
                                {
                                    allVarFileEntries.Add(fileEntry);
                                    uidToVarFileEntry.Add(fileEntry.Uid, fileEntry);
                                    pathToVarFileEntry.Add(fileEntry.Path, fileEntry);
                                }
                            }
                        }
                        return varPackage;
                    }
                    isDuplicated = true;
                    VarPackage existing;
                    if (packagesByUid.TryGetValue(text, out existing))
                    {
                        string existingPath = CleanFilePath(existing.Path);
                        if (string.Equals(existingPath, cleanPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!packagesByPath.ContainsKey(cleanPath))
                            {
                                packagesByPath.Add(cleanPath, existing);
                            }
                            return existing;
                        }

                        string existingFileId;
                        string newFileId;
                        if (TryGetWindowsFileId(existingPath, out existingFileId)
                            && TryGetWindowsFileId(cleanPath, out newFileId)
                            && existingFileId == newFileId)
                        {
                            LogUtil.LogWarning("Duplicate package uid " + text + " points to same file via different path. Existing: " + existing.Path + " New: " + cleanPath + ". Skipping duplicate registration");
                            if (!packagesByPath.ContainsKey(cleanPath))
                            {
                                packagesByPath.Add(cleanPath, existing);
                            }
                            return existing;
                        }

                        LogUtil.LogError("Duplicate package uid " + text + ". Existing: " + existing.Path + " New: " + cleanPath + ". Cannot register");
                    }
                    else
                    {
                        LogUtil.LogError("Duplicate package uid " + text + ". Cannot register");
                    }
                }
                catch (Exception)
                {
                    LogUtil.LogError("VAR file " + vpath + " does not use integer version field in name <creator>.<name>.<version>");
                }
            }
            else
            {
                LogUtil.LogError("VAR file " + vpath + " is not named with convention <creator>.<name>.<version>");
            }

            // Reaching here means it is invalid
            if (clean)
            {
                if (isDuplicated)
                {
                    RemoveToInvalid(vpath, "Duplicated");
                }
                else
                    RemoveToInvalid(vpath, "InvalidName");
            }
            return null;
        }

        public struct CleanupItem
        {
            public string Path;
            public string Uid;
            public string Type; // "Duplicated", "InvalidName", "OldVersion", "InvalidZip"
        }

        public static List<CleanupItem> GetCleanupList(bool checkOldVersions)
        {
            List<CleanupItem> items = new List<CleanupItem>();

            // Get all var files
            List<string> allFiles = new List<string>();
            if (Directory.Exists("AddonPackages"))
                SafeGetFiles("AddonPackages", "*.var", allFiles);
            if (Directory.Exists("AllPackages"))
                SafeGetFiles("AllPackages", "*.var", allFiles);

            // 1. Check for Invalid Names and Duplicates
            // We build a temporary index to find duplicates
            Dictionary<string, string> uidToPath = new Dictionary<string, string>();

            HashSet<string> seenFileIds = new HashSet<string>();
            foreach (string _varPath in allFiles)
            {
                string fileId;
                if (TryGetWindowsFileId(_varPath, out fileId))
                {
                    if (!seenFileIds.Add(fileId))
                    {
                        continue;
                    }
                }
                string varPath = CleanFilePath(_varPath);
                string uidText = packagePathToUid(varPath).Trim();
                string[] array = uidText.Split('.');

                if (array.Length == 3)
                {
                    string s = array[2];
                    int version;
                    if (int.TryParse(s, out version))
                    {
                        // Valid name format
                        if (uidToPath.ContainsKey(uidText))
                        {
                            // Duplicate
                            if (packagesByUid != null && packagesByUid.ContainsKey(uidText))
                            {
                                var registered = packagesByUid[uidText];
                                if (registered.Path != varPath)
                                {
                                    items.Add(new CleanupItem { Path = varPath, Uid = uidText, Type = "Duplicated" });
                                }
                            }
                            else
                            {
                                items.Add(new CleanupItem { Path = varPath, Uid = uidText, Type = "Duplicated" });
                            }
                        }
                        else
                        {
                            uidToPath[uidText] = varPath;
                        }
                    }
                    else
                    {
                        items.Add(new CleanupItem { Path = varPath, Uid = uidText, Type = "InvalidName" });
                    }
                }
                else
                {
                    items.Add(new CleanupItem { Path = varPath, Uid = uidText, Type = "InvalidName" });
                }
            }

            // 2. Check for Invalid Zips (must be in PackagesByUid and marked invalid)
            if (packagesByUid != null)
            {
                foreach (var pkg in packagesByUid.Values)
                {
                    if (pkg.invalid)
                    {
                        items.Add(new CleanupItem { Path = pkg.Path, Uid = pkg.Uid, Type = "InvalidZip" });
                    }
                }
            }

            // 3. Check for Old Versions
            if (checkOldVersions && packageGroups != null)
            {
                HashSet<string> referenced = GetReferencedPackage();
                foreach (var group in packageGroups.Values)
                {
                    foreach (var pkg in group.Packages)
                    {
                        if (pkg.Version != group.NewestVersion)
                        {
                            if (!referenced.Contains(pkg.Uid))
                            {
                                bool exists = false;
                                foreach (var it in items) { if (it.Path == pkg.Path) { exists = true; break; } }

                                if (!exists)
                                {
                                    items.Add(new CleanupItem { Path = pkg.Path, Uid = pkg.Uid, Type = "OldVersion" });
                                }
                            }
                        }
                    }
                }
            }

            return items;
        }

        public static void RemoveToInvalid(string vpath, string subPath = null)
        {
            if (!Directory.Exists("InvalidPackages"))
                Directory.CreateDirectory("InvalidPackages");

            if (!string.IsNullOrEmpty(subPath))
            {
                if (!Directory.Exists("InvalidPackages/" + subPath))
                    Directory.CreateDirectory("InvalidPackages/" + subPath);
            }

            string moveToPath = null;
            if (vpath.StartsWith("AllPackages"))
            {
                moveToPath = "InvalidPackages" + vpath.Substring("AllPackages".Length);
                if (!string.IsNullOrEmpty(subPath))
                {
                    moveToPath = "InvalidPackages/" + subPath + "/" + vpath.Substring("AllPackages".Length);
                }
            }
            else if (vpath.StartsWith("AddonPackages"))
            {
                moveToPath = "InvalidPackages" + vpath.Substring("AddonPackages".Length);
                if (!string.IsNullOrEmpty(subPath))
                {
                    moveToPath = "InvalidPackages/" + subPath + "/" + vpath.Substring("AddonPackages".Length);
                }
            }
            string dir = Path.GetDirectoryName(moveToPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            while (File.Exists(moveToPath))
            {
                moveToPath += "(clone)";
            }
            File.Move(vpath, moveToPath);
        }

        public static void UnregisterPackage(VarPackage vp)
        {
            LogUtil.Log("UnregisterPackage " + vp.Path);
            if (vp != null)
            {
                if (vp.Group != null)
                {
                    vp.Group.RemovePackage(vp);
                }
                packagesByUid.Remove(vp.Uid);
                packagesByPath.Remove(vp.Path);
                if (vp.FileEntries != null)
                {
                    foreach (VarFileEntry fileEntry in vp.FileEntries)
                    {
                        allVarFileEntries.Remove(fileEntry);
                        uidToVarFileEntry.Remove(fileEntry.Uid);
                        pathToVarFileEntry.Remove(fileEntry.Path);
                    }
                }
                vp.Dispose();
            }
        }

        public static void RegisterRefreshHandler(OnRefresh refreshHandler)
        {
            onRefreshHandlers = (OnRefresh)Delegate.Combine(onRefreshHandlers, refreshHandler);
        }

        public static void UnregisterRefreshHandler(OnRefresh refreshHandler)
        {
            onRefreshHandlers = (OnRefresh)Delegate.Remove(onRefreshHandlers, refreshHandler);
        }

        protected static void ClearAll()
        {
            foreach (VarPackage value in packagesByUid.Values)
            {
                value.Dispose();
            }
            if (packagesByUid != null)
            {
                packagesByUid.Clear();
            }
            if (packagesByPath != null)
            {
                packagesByPath.Clear();
            }
            if (packageGroups != null)
            {
                packageGroups.Clear();
            }
            if (allVarFileEntries != null)
            {
                allVarFileEntries.Clear();
            }
            if (uidToVarFileEntry != null)
            {
                uidToVarFileEntry.Clear();
            }
            if (pathToVarFileEntry != null)
            {
                pathToVarFileEntry.Clear();
            }
        }

        public static void Refresh(bool init = false, bool clean = false, bool removeOldVersion = false)
        {
            if (singleton != null)
            {
                // Coalesce refresh requests.
                // Refresh triggers a full var enumeration which is expensive on large libraries.
                // Some UI actions can call Refresh multiple times in short succession; stopping/restarting
                // the coroutine causes repeated enumerations.
                if (singleton.m_RefreshCo != null)
                {
                    singleton.m_RefreshPending = true;
                    singleton.m_RefreshPendingInit |= init;
                    singleton.m_RefreshPendingClean |= clean;
                    singleton.m_RefreshPendingRemoveOldVersion |= removeOldVersion;
                    return;
                }
                singleton.m_RefreshCo = singleton.StartCoroutine(singleton.RefreshCo(init, clean, removeOldVersion));
            }
        }

        private IEnumerator RefreshCo(bool init, bool clean, bool removeOldVersion)
        {
#if DEBUG
            string stackTrace = new System.Diagnostics.StackTrace().ToString();
            LogUtil.LogWarning("Refresh " + stackTrace);
#endif
            {
                LogUtil.LogWarning(string.Format("FileManager Refresh({0},{1},{2})", init, clean, removeOldVersion));
            }
            Stopwatch swTotal = Stopwatch.StartNew();
            if (packagesByUid == null)
            {
                packagesByUid = new Dictionary<string, VarPackage>(StringComparer.OrdinalIgnoreCase);
            }
            if (packagesByPath == null)
            {
                packagesByPath = new Dictionary<string, VarPackage>(StringComparer.OrdinalIgnoreCase);
            }
            if (packageGroups == null)
            {
                packageGroups = new Dictionary<string, VarPackageGroup>(StringComparer.OrdinalIgnoreCase);
            }
            if (allVarFileEntries == null)
            {
                allVarFileEntries = new HashSet<VarFileEntry>();
            }
            if (uidToVarFileEntry == null)
            {
                uidToVarFileEntry = new Dictionary<string, VarFileEntry>(StringComparer.OrdinalIgnoreCase);
            }
            if (pathToVarFileEntry == null)
            {
                pathToVarFileEntry = new Dictionary<string, VarFileEntry>(StringComparer.OrdinalIgnoreCase);
            }

            bool flag = false;
            try
            {
                if (!Directory.Exists("Cache/AllPackagesJSON"))
                {
                    Directory.CreateDirectory("Cache/AllPackagesJSON");
                }
                if (!Directory.Exists("AddonPackages"))
                {
                    CreateDirectory("AddonPackages");
                }
                if (!Directory.Exists("AllPackages"))
                {
                    CreateDirectory("AllPackages");
                }
            }
            catch (Exception arg)
            {
                LogUtil.LogError("Exception during package refresh initialization " + arg);
            }

            if (Directory.Exists("AllPackages"))
            {
                Stopwatch swEnumerate = Stopwatch.StartNew();
                List<string> allVarFiles = new List<string>();
                string[] scanRoots = new string[] { "AddonPackages", "AllPackages", "Custom", "Saves", "BuiltinPackages", "VaM_Data/StreamingAssets/BuiltinPackages" };

                ManualResetEvent doneEvent = new ManualResetEvent(false);
                ThreadPool.QueueUserWorkItem((state) => {
                    try {
                        foreach (string root in scanRoots)
                        {
                            if (Directory.Exists(root))
                            {
                                SafeGetFiles(root, "*.var", allVarFiles);
                            }
                        }
                    } finally {
                        doneEvent.Set();
                    }
                });

                while (!doneEvent.WaitOne(0)) 
                {
                    yield return null;
                }
                doneEvent.Close();

                try
                {
                    string[] varPaths = allVarFiles.ToArray();
                    swEnumerate.Stop();
                    LogUtil.Log("FileManager Refresh enumerate vars: " + varPaths.Length + " in " + swEnumerate.ElapsedMilliseconds + "ms");

                    HashSet<string> hashSet = new HashSet<string>();
                    HashSet<string> addSet = new HashSet<string>();
                    if (varPaths != null)
                    {
                        string[] _varPaths = varPaths;
                        HashSet<string> seenFileIds = new HashSet<string>();
                        foreach (string _varPath in _varPaths)
                        {
                            string fileId;
                            if (TryGetWindowsFileId(_varPath, out fileId))
                            {
                                if (!seenFileIds.Add(fileId))
                                {
                                    continue;
                                }
                            }
                            string varPath = CleanFilePath(_varPath);
                            hashSet.Add(varPath);

                            VarPackage value2;
                            if (packagesByPath.TryGetValue(varPath, out value2))
                            {
                            }
                            else
                            {
                                addSet.Add(varPath);
                            }
                        }
                    }

                    Stopwatch swUpdate = Stopwatch.StartNew();
                    HashSet<VarPackage> removeSet = new HashSet<VarPackage>();
                    foreach (VarPackage value3 in packagesByUid.Values)
                    {
                        if (!hashSet.Contains(value3.Path))
                        {
                            removeSet.Add(value3);
                        }
                    }
                    HashSet<string> oldVersion = new HashSet<string>();
                    if (removeOldVersion)
                    {
                        HashSet<string> referenced = GetReferencedPackage();
                        foreach (var item in packageGroups)
                        {
                            var group = item.Value;
                            foreach (var item2 in group.Packages)
                            {
                                if (item2.Version != group.NewestVersion)
                                {
                                    if (!referenced.Contains(item2.Uid))
                                    {
                                        removeSet.Add(item2);
                                        oldVersion.Add(item2.Path);
                                    }
                                }
                            }
                        }
                    }

                    foreach (VarPackage item2 in removeSet)
                    {
                        UnregisterPackage(item2);
                        flag = true;
                    }
                    foreach (string item3 in addSet)
                    {
                        RegisterPackage(item3, clean);
                        flag = true;
                    }
                    if (removeOldVersion)
                    {
                        foreach (var item in oldVersion)
                        {
                            RemoveToInvalid(item, "OldVersion");
                        }
                    }
                    swUpdate.Stop();
                    LogUtil.Log("FileManager Refresh update: add=" + addSet.Count + " remove=" + removeSet.Count + " oldVersion=" + oldVersion.Count + " in " + swUpdate.ElapsedMilliseconds + "ms");
                }
                catch (Exception arg)
                {
                    LogUtil.LogError("Exception during package refresh processing " + arg);
                }
            }
            
            try
            {
                StartScan(init, flag, clean, true);

                swTotal.Stop();
                LogUtil.Log("FileManager Refresh pre-scan completed in " + swTotal.ElapsedMilliseconds + "ms");
            }
            catch (Exception arg)
            {
                LogUtil.LogError("Exception during package refresh finalization " + arg);
            }
            lastPackageRefreshTime = DateTime.Now;

            s_InstalledCount = 0;
            foreach (var item in packagesByUid)
            {
                if (item.Value.IsInstalled())
                {
                    s_InstalledCount++;
                }
            }
            m_RefreshCo = null;
            if (m_RefreshPending)
            {
                bool nextInit = m_RefreshPendingInit;
                bool nextClean = m_RefreshPendingClean;
                bool nextRemoveOld = m_RefreshPendingRemoveOldVersion;
                m_RefreshPending = false;
                m_RefreshPendingInit = false;
                m_RefreshPendingClean = false;
                m_RefreshPendingRemoveOldVersion = false;
                m_RefreshCo = StartCoroutine(RefreshCo(nextInit, nextClean, nextRemoveOld));
            }
        }

		public void StartScan(bool init, bool flag, bool clean, bool runCo)
		{
			if (m_StartScanCo != null)
			{
				StopCoroutine(m_StartScanCo);
				m_StartScanCo = null;
			}
			m_StartScanCo = StartCoroutine(StartScanCo(init, flag, clean, runCo));
		}

		IEnumerator StartScanCo(bool init, bool flag, bool clean, bool runCo)
		{
			Stopwatch swScan = Stopwatch.StartNew();
			int pkgCount = (packagesByUid != null) ? packagesByUid.Count : 0;
			LogUtil.Log(string.Format("FileManager StartScan begin runCo={0} packages={1}", runCo ? "True" : "False", pkgCount));

			VarPackage.ResetScanCounters();
			List<VarPackage> invalid = new List<VarPackage>();
			if (runCo)
			{
				if (m_Co != null)
				{
					StopCoroutine(m_Co);
					m_Co = null;
				}
				m_Co = StartCoroutine(ScanVarPackagesCo(clean, invalid));
				yield return m_Co;
			}
			else
			{
				if (packagesByUid != null)
				{
					foreach (var item in packagesByUid)
					{
						VarPackage pkg = item.Value;
						if (pkg == null) continue;
						pkg.Scan();
						if (pkg.invalid)
						{
							invalid.Add(pkg);
						}
					}
				}
			}

			if (clean && invalid.Count > 0)
			{
				foreach (var pkg in invalid)
				{
					string path = pkg.Path;
					UnregisterPackage(pkg);
					RemoveToInvalid(path, "InvalidZip");
				}
			}

			if (init)
			{
				if (onRefreshHandlers != null) onRefreshHandlers();
			}
			else
			{
				if (flag && onRefreshHandlers != null) onRefreshHandlers();
			}
			MessageKit.post(MessageDef.FileManagerRefresh);

			VarPackage.GetScanCounters(out long total, out long cacheValidatedHit, out long cacheHit, out long zipScan);
			swScan.Stop();
			LogUtil.Log(string.Format("FileManager StartScan complete in {0}ms total={1} cacheValidated={2} cacheFallback={3} zipScan={4} invalid={5}", swScan.ElapsedMilliseconds, total, cacheValidatedHit, cacheHit, zipScan, invalid.Count));
			m_StartScanCo = null;
		}

		IEnumerator ScanVarPackagesCo(bool clean, List<VarPackage> invalid)
		{
			if (packagesByUid == null) yield break;
			List<string> list = new List<string>(packagesByUid.Keys);
			int idx = 0;
			int allCount = list.Count;
			int step = (VarPackageMgr.singleton != null && VarPackageMgr.singleton.existCache) ? 200 : 20;
			int cnt = 0;
			for (int i = 0; i < list.Count; i++)
			{
				string uid = list[i];
				if (packagesByUid.ContainsKey(uid))
				{
					VarPackage pkg = packagesByUid[uid];
					if (pkg != null)
					{
						pkg.Scan();
						if (pkg.invalid)
						{
							invalid.Add(pkg);
						}
					}
				}
				idx++;
				MessageKit<string>.post(MessageDef.UpdateLoading, idx + "/" + allCount);
				cnt++;
				if (cnt > step)
				{
					yield return null;
					cnt = 0;
				}
			}
		}

		public List<string> GetAllVars()
		{
			List<string> ret = new List<string>();
			if (packagesByUid != null)
			{
				foreach (VarPackage value4 in packagesByUid.Values)
				{
					if (value4 != null) ret.Add(value4.Path);
				}
			}
			return ret;
		}

		public static HashSet<string> GetReferencedPackage()
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (packagesByUid != null)
			{
				foreach (var item in packagesByUid)
				{
					VarPackage vp = item.Value;
					if (vp != null && vp.RecursivePackageDependencies != null)
					{
						foreach (var key in vp.RecursivePackageDependencies)
						{
							if (key != null && !key.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
							{
								hashSet.Add(key);
							}
						}
					}
				}
			}
			return hashSet;
		}

		public List<string> GetMissingDependenciesNames()
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (packagesByUid != null)
			{
				foreach (var item in packagesByUid)
				{
					VarPackage vp = item.Value;
					if (vp != null && vp.RecursivePackageDependencies != null)
					{
						foreach (var key in vp.RecursivePackageDependencies)
						{
							VarPackage pkg = FileManager.GetPackage(key);
							if (pkg == null)
							{
								hashSet.Add(key);
							}
						}
					}
				}
			}
			LogUtil.Log("GetMissingDependenciesNames " + hashSet.Count);
			return hashSet.ToList();
		}

		public static bool IsSecureReadPath(string path)
		{
			return true;
		}

		public static bool IsSecureWritePath(string path)
		{
			return true;
		}

		public static HashSet<string> GetDependenciesDeep(string uid, int maxDepth = 2)
		{
			HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(uid) || maxDepth <= 0) return result;
			VarPackage root = GetPackage(uid, false);
			if (root == null) return result;
			try { if (!root.Scaned) root.Scan(); } catch { }

			if (maxDepth >= 2 && root.RecursivePackageDependencies != null)
			{
				foreach (var dep in root.RecursivePackageDependencies)
				{
					if (!string.IsNullOrEmpty(dep)) result.Add(dep);
				}
				return result;
			}

			if (root.PackageDependencies != null)
			{
				foreach (var dep in root.PackageDependencies)
				{
					if (!string.IsNullOrEmpty(dep)) result.Add(dep);
				}
			}
			return result;
		}

        public static void RegisterPluginHashToPluginPath(string hash, string path)
        {
            if (pluginHashToPluginPath == null)
            {
                pluginHashToPluginPath = new Dictionary<string, string>();
            }
            pluginHashToPluginPath.Remove(hash);
            pluginHashToPluginPath.Add(hash, path);
        }

        protected static string GetPluginHash()
        {
            StackTrace stackTrace = new StackTrace();
            string result = null;
            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                StackFrame frame = stackTrace.GetFrame(i);
                MethodBase method = frame.GetMethod();
                AssemblyName name = method.DeclaringType.Assembly.GetName();
                string name2 = name.Name;
                if (name2.StartsWith("MVRPlugin_"))
                {
                    result = Regex.Replace(name2, "_[0-9]+$", string.Empty);
                    break;
                }
            }
            return result;
        }

        public static void AssertNotCalledFromPlugin()
        {
            string pluginHash = GetPluginHash();
            if (pluginHash != null)
            {
                throw new Exception("Plugin with signature " + pluginHash + " tried to execute forbidden operation");
            }
        }

        public static string GetFullPath(string path)
        {
            string path2 = Regex.Replace(path, "^file:///", string.Empty);
            return Path.GetFullPath(path2);
        }

        public static bool IsPackagePath(string path)
        {
            string input = path.Replace('\\', '/');
            string packageUidOrPath = Regex.Replace(input, ":/.*", string.Empty);
            VarPackage package = GetPackage(packageUidOrPath);
            return package != null;
        }

        public static bool IsSimulatedPackagePath(string path)
        {
            string input = path.Replace('\\', '/');
            string packageUidOrPath = Regex.Replace(input, ":/.*", string.Empty);
            return GetPackage(packageUidOrPath)?.IsSimulated ?? false;
        }

        public static string ConvertSimulatedPackagePathToNormalPath(string path)
        {
            string text = path.Replace('\\', '/');
            if (text.Contains(":/"))
            {
                string packageUidOrPath = Regex.Replace(text, ":/.*", string.Empty);
                VarPackage package = GetPackage(packageUidOrPath);
                if (package != null && package.IsSimulated)
                {
                    string str = Regex.Replace(text, ".*:/", string.Empty);
                    path = package.Path + "/" + str;
                }
            }
            return path;
        }

        public static string RemovePackageFromPath(string path)
        {
            string input = Regex.Replace(path, ".*:/", string.Empty);
            return Regex.Replace(input, ".*:\\\\", string.Empty);
        }

        public static string NormalizePath(string path)
        {
            string text = path;
            VarFileEntry varFileEntry = GetVarFileEntry(path);
            if (varFileEntry == null)
            {
                string fullPath = GetFullPath(path);
                string oldValue = Path.GetFullPath(".") + "\\";
                string text2 = fullPath.Replace(oldValue, string.Empty);
                if (text2 != fullPath)
                {
                    text = text2;
                }
                return text.Replace('\\', '/');
            }
            return varFileEntry.Uid;
        }

        public static string GetDirectoryName(string path, bool returnSlashPath = false)
        {
            VarFileEntry value;
            string path2 = (uidToVarFileEntry != null && uidToVarFileEntry.TryGetValue(path, out value)) ?
                //((!returnSlashPath) ? value.Path : value.SlashPath) : 
                //((!returnSlashPath) ? path.Replace('/', '\\') : path.Replace('\\', '/'));
                value.Path : path.Replace('\\', '/');
            return Path.GetDirectoryName(path2);
        }

        public static string GetSuggestedBrowserDirectoryFromDirectoryPath(string suggestedDir, string currentDir, bool allowPackagePath = true)
        {
            if (currentDir == null || currentDir == string.Empty)
            {
                return suggestedDir;
            }
            string input = suggestedDir.Replace('\\', '/');
            input = Regex.Replace(input, "/$", string.Empty);
            string text = currentDir.Replace('\\', '/');
            VarDirectoryEntry varDirectoryEntry = GetVarDirectoryEntry(text);
            if (varDirectoryEntry != null)
            {
                if (!allowPackagePath)
                {
                    return null;
                }
                string text2 = varDirectoryEntry.InternalPath.Replace(input, string.Empty);
                if (varDirectoryEntry.InternalPath != text2)
                {
                    //text2 = text2.Replace('/', '\\');
                    return varDirectoryEntry.Package.Path + ":/" + input + text2;
                }
            }
            else
            {
                string text3 = text.Replace(input, string.Empty);
                if (text != text3)
                {
                    //text3 = text3.Replace('/', '\\');
                    return suggestedDir + text3;
                }
            }
            return null;
        }

        public static VarPackage ResolveDependency(string uid)
        {
            // Try exact match
            if (packagesByUid.ContainsKey(uid)) return packagesByUid[uid];

            // Try to resolve group
            string groupId = PackageIDToPackageGroupID(uid);
            if (packageGroups.ContainsKey(groupId))
            {
                VarPackageGroup group = packageGroups[groupId];
                if (uid.EndsWith(".latest")) return group.NewestPackage;

                string verStr = PackageIDToPackageVersion(uid);
                if (verStr != null && int.TryParse(verStr, out int ver))
                {
                    return group.GetClosestMatchingPackageVersion(ver, false, true);
                }

                // Fallback to newest if we can't parse version but found group
                return group.NewestPackage;
            }
            return null;
        }

        public static void SetLoadDir(string dir, bool restrictPath = false)
        {
            if (loadDirStack != null)
            {
                loadDirStack.Clear();
            }
            PushLoadDir(dir, restrictPath);
        }

        public static void PushLoadDir(string dir, bool restrictPath = false)
        {
            string text = dir.Replace('\\', '/');
            if (text != "/")
            {
                text = Regex.Replace(text, "/$", string.Empty);
            }
            if (restrictPath && !IsSecureReadPath(text))
            {
                throw new Exception("Attempted to push load dir for non-secure dir " + text);
            }
            if (loadDirStack == null)
            {
                loadDirStack = new LinkedList<string>();
            }
            loadDirStack.AddLast(text);
        }

		public static string PopLoadDir()
		{
			string result = null;
			if (loadDirStack != null && loadDirStack.Count > 0)
			{
				result = loadDirStack.Last.Value;
				loadDirStack.RemoveLast();
			}
			return result;
		}

		public static void SetLoadDirFromFilePath(string path, bool restrictPath = false)
		{
			if (loadDirStack != null)
			{
				loadDirStack.Clear();
			}
			PushLoadDirFromFilePath(path, restrictPath);
		}

		public static void PushLoadDirFromFilePath(string path, bool restrictPath = false)
		{
			if (restrictPath && !IsSecureReadPath(path))
			{
				throw new Exception("Attempted to set load dir from non-secure path " + path);
			}
			FileEntry fileEntry = GetFileEntry(path);
			string dir;
			if (fileEntry != null)
			{
				if (fileEntry is VarFileEntry)
				{
					dir = Path.GetDirectoryName(fileEntry.Uid);
				}
				else
				{
					dir = Path.GetDirectoryName(fileEntry.Path);
					string oldValue = Path.GetFullPath(".") + "\\";
					dir = dir.Replace(oldValue, string.Empty);
				}
			}
			else
			{
				dir = Path.GetDirectoryName(GetFullPath(path));
				string oldValue2 = Path.GetFullPath(".") + "\\";
				dir = dir.Replace(oldValue2, string.Empty);
			}
			PushLoadDir(dir, restrictPath);
		}

		public static string PackageIDToPackageGroupID(string packageId)
		{
			string input = Regex.Replace(packageId, "\\.[0-9]+$", string.Empty);
			input = Regex.Replace(input, "\\.latest$", string.Empty);
			return Regex.Replace(input, "\\.min[0-9]+$", string.Empty);
		}

		public static string PackageIDToPackageVersion(string packageId)
		{
			Match match = Regex.Match(packageId, "[0-9]+$");
			if (match.Success)
			{
				return match.Value;
			}
			return null;
		}

		public static string NormalizeID(string id)
		{
			if (id.StartsWith("SELF:"))
			{
				string currentPackageUid = CurrentPackageUid;
				if (currentPackageUid != null)
				{
					return id.Replace("SELF:", currentPackageUid + ":");
				}
				return id.Replace("SELF:", string.Empty);
			}
			return NormalizeCommon(id);
		}

		protected static string NormalizeCommon(string path)
		{
			string text = path;
			Match match;
			if ((match = Regex.Match(text, "^(([^\\.]+\\.[^\\.]+)\\.latest):")).Success)
			{
				string value = match.Groups[1].Value;
				string value2 = match.Groups[2].Value;
				VarPackageGroup packageGroup = GetPackageGroup(value2);
				if (packageGroup != null)
				{
					VarPackage newestEnabledPackage = packageGroup.NewestEnabledPackage;
					if (newestEnabledPackage != null)
					{
						text = text.Replace(value, newestEnabledPackage.Uid);
					}
				}
			}
			else if ((match = Regex.Match(text, "^(([^\\.]+\\.[^\\.]+)\\.min([0-9]+)):")).Success)
			{
				string value3 = match.Groups[1].Value;
				string value4 = match.Groups[2].Value;
				int requestVersion = int.Parse(match.Groups[3].Value);
				VarPackageGroup packageGroup2 = GetPackageGroup(value4);
				if (packageGroup2 != null)
				{
					VarPackage closestMatchingPackageVersion = packageGroup2.GetClosestMatchingPackageVersion(requestVersion, true, false);
					if (closestMatchingPackageVersion != null)
					{
						text = text.Replace(value3, closestMatchingPackageVersion.Uid);
					}
				}
			}
			else if ((match = Regex.Match(text, "^([^\\.]+\\.[^\\.]+\\.[0-9]+):")).Success)
			{
				string value5 = match.Groups[1].Value;
				VarPackage package = GetPackage(value5);
				if (package == null || !package.Enabled)
				{
					string packageGroupUid = PackageIDToPackageGroupID(value5);
					VarPackageGroup packageGroup3 = GetPackageGroup(packageGroupUid);
					if (packageGroup3 != null)
					{
						package = packageGroup3.NewestEnabledPackage;
						if (package != null)
						{
							text = text.Replace(value5, package.Uid);
						}
					}
				}
			}
			return text;
		}

		public static string NormalizeLoadPath(string path)
		{
			string result = path;
			if (path != null && path != string.Empty && path != "/" && path != "NULL")
			{
				result = path.Replace('\\', '/');
				string currentLoadDir = CurrentLoadDir;
				if (currentLoadDir != null && currentLoadDir != string.Empty)
				{
					if (!result.Contains("/"))
					{
						result = currentLoadDir + "/" + result;
					}
					else if (Regex.IsMatch(result, "^\\./"))
					{
						result = Regex.Replace(result, "^\\./", currentLoadDir + "/");
					}
				}
				if (result.StartsWith("SELF:/"))
				{
					string currentPackageUid = CurrentPackageUid;
					result = ((currentPackageUid == null) ? result.Replace("SELF:/", string.Empty) : result.Replace("SELF:/", currentPackageUid + ":/"));
				}
				else
				{
					result = NormalizeCommon(result);
				}
			}
			return result;
		}

		public static void SetSaveDir(string path, bool restrictPath = true)
		{
			if (path == null || path == string.Empty)
			{
				CurrentSaveDir = string.Empty;
				return;
			}
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsPackagePath(path))
			{
				if (restrictPath && !IsSecureWritePath(path))
				{
					throw new Exception("Attempted to set save dir from non-secure path " + path);
				}
				string fullPath = GetFullPath(path);
				string oldValue = Path.GetFullPath(".") + "\\";
				fullPath = fullPath.Replace(oldValue, string.Empty);
				CurrentSaveDir = fullPath.Replace('\\', '/');
			}
		}

		public static void SetSaveDirFromFilePath(string path, bool restrictPath = true)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsPackagePath(path))
			{
				if (restrictPath && !IsSecureWritePath(path))
				{
					throw new Exception("Attempted to set save dir from non-secure path " + path);
				}
				string directoryName = Path.GetDirectoryName(GetFullPath(path));
				string oldValue = Path.GetFullPath(".") + "\\";
				directoryName = directoryName.Replace(oldValue, string.Empty);
				CurrentSaveDir = directoryName.Replace('\\', '/');
			}
		}

		public static void SetNullSaveDir()
		{
			CurrentSaveDir = null;
		}

		public static string NormalizeSavePath(string path)
		{
			string text = path;
			if (path != null && path != string.Empty && path != "/" && path != "NULL")
			{
				string path2 = Regex.Replace(path, "^file:///", string.Empty);
				string fullPath = Path.GetFullPath(path2);
				string oldValue = Path.GetFullPath(".") + "\\";
				string text2 = fullPath.Replace(oldValue, string.Empty);
				if (text2 != fullPath)
				{
					text = text2;
				}
				text = text.Replace('\\', '/');
				string fileName = Path.GetFileName(text2);
				string text3 = Path.GetDirectoryName(text2);
				if (text3 != null)
				{
					text3 = text3.Replace('\\', '/');
				}
				if (CurrentSaveDir == text3)
				{
					text = fileName;
				}
				else if (CurrentSaveDir != null && CurrentSaveDir != string.Empty && Regex.IsMatch(text3, "^" + CurrentSaveDir + "/"))
				{
					text = text3.Replace(CurrentSaveDir, ".") + "/" + fileName;
				}
			}
			return text;
		}

		public static List<VarPackage> GetPackages()
		{
			if (packagesByUid != null)
			{
				return packagesByUid.Values.ToList();
			}
			return new List<VarPackage>();
		}

		public static List<string> GetPackageUids()
		{
			List<string> list;
			if (packagesByUid != null)
			{
				list = packagesByUid.Keys.ToList();
				list.Sort();
			}
			else
			{
				list = new List<string>();
			}
			return list;
		}

		public static bool IsPackage(string packageUidOrPath)
		{
			if (packagesByUid != null && packagesByUid.ContainsKey(packageUidOrPath))
			{
				return true;
			}
			if (packagesByPath != null && packagesByPath.ContainsKey(packageUidOrPath))
			{
				return true;
			}
			return false;
		}

		public static VarPackage GetPackage(string packageUidOrPath, bool ensureInstalled = true)
		{
			VarPackage value = null;
			Match match;
			if ((match = Regex.Match(packageUidOrPath, "^([^\\.]+\\.[^\\.]+)\\.latest$")).Success)
			{
				string value2 = match.Groups[1].Value;
				VarPackageGroup packageGroup = GetPackageGroup(value2);
				if (packageGroup != null)
				{
					value = packageGroup.NewestPackage;
				}
			}
			else if ((match = Regex.Match(packageUidOrPath, "^([^\\.]+\\.[^\\.]+)\\.min([0-9]+)$")).Success)
			{
				string value3 = match.Groups[1].Value;
				int requestVersion = int.Parse(match.Groups[2].Value);
				VarPackageGroup packageGroup2 = GetPackageGroup(value3);
				if (packageGroup2 != null)
				{
					value = packageGroup2.GetClosestMatchingPackageVersion(requestVersion, false, false);
				}
			}
			else if (packagesByUid != null && packagesByUid.ContainsKey(packageUidOrPath))
			{
				packagesByUid.TryGetValue(packageUidOrPath, out value);
			}
			else if (packagesByPath != null && packagesByPath.ContainsKey(packageUidOrPath))
			{
				packagesByPath.TryGetValue(packageUidOrPath, out value);
			}
			if (value != null && ensureInstalled) EnsurePackageInstalled(value);
			return value;
		}

		private static void EnsurePackageInstalled(VarPackage package)
		{
			if (package == null) return;

			// Recursively install this package and its dependencies if needed.
			// InstallRecursive will return true if ANYTHING was moved (self or dependency).
			bool moved = package.InstallRecursive();
			if (moved)
			{
				LogUtil.Log($"[VPB] Dependencies installed/verified for: {package.Uid}");
				MVR.FileManagement.FileManager.Refresh();
				FileManager.Refresh();
			}
		}

		public static List<VarPackageGroup> GetPackageGroups()
		{
			if (packageGroups != null)
			{
				return packageGroups.Values.ToList();
			}
			return new List<VarPackageGroup>();
		}

		public static VarPackageGroup GetPackageGroup(string packageGroupUid)
		{
			VarPackageGroup value = null;
			if (packageGroups != null)
			{
				packageGroups.TryGetValue(packageGroupUid, out value);
			}
			return value;
		}

		public static string CleanFilePath(string path)
		{
			return path?.Replace('\\', '/');
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct BY_HANDLE_FILE_INFORMATION
		{
			public uint FileAttributes;
			public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
			public uint VolumeSerialNumber;
			public uint FileSizeHigh;
			public uint FileSizeLow;
			public uint NumberOfLinks;
			public uint FileIndexHigh;
			public uint FileIndexLow;
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetFileInformationByHandle(IntPtr hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern SafeFileHandle CreateFile(
			string lpFileName,
			uint dwDesiredAccess,
			uint dwShareMode,
			IntPtr lpSecurityAttributes,
			uint dwCreationDisposition,
			uint dwFlagsAndAttributes,
			IntPtr hTemplateFile);

		private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
		private const uint OPEN_EXISTING = 3;

		private static bool TryGetWindowsFileId(string path, out string fileId)
		{
			fileId = null;
			try
			{
				if (string.IsNullOrEmpty(path))
					return false;

				// Fast path: Only check file ID if it's a reparse point (junction/symlink)
				// or if we really need it for deduplication. 
				// For most files, we can skip the expensive CreateFile call.
				var attr = File.GetAttributes(path);
				if ((attr & FileAttributes.ReparsePoint) == 0)
				{
					return false;
				}

				using (SafeFileHandle handle = CreateFile(
					path,
					0x80, // FILE_READ_ATTRIBUTES
					(uint)(FileShare.ReadWrite | FileShare.Delete),
					IntPtr.Zero,
					OPEN_EXISTING,
					FILE_FLAG_BACKUP_SEMANTICS,
					IntPtr.Zero))
				{
					if (handle.IsInvalid)
						return false;

					BY_HANDLE_FILE_INFORMATION info;
					if (!GetFileInformationByHandle(handle.DangerousGetHandle(), out info))
						return false;

					ulong index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
					fileId = info.VolumeSerialNumber.ToString("X8") + ":" + index.ToString("X16");
					return true;
				}
			}
			catch
			{
				return false;
			}
		}

		public static void SafeGetFiles(string path, string pattern, List<string> results)
		{
			SafeGetFiles(path, pattern, results, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		}

		private static void SafeGetFiles(string path, string pattern, List<string> results, HashSet<string> visited)
		{
			try
			{
				string dirId;
				if (TryGetWindowsFileId(path, out dirId))
				{
					if (!visited.Add(dirId))
					{
						return;
					}
				}

				string[] files = Directory.GetFiles(path, pattern);
				if (files != null)
					results.AddRange(files);

				string[] dirs = Directory.GetDirectories(path);
				if (dirs != null)
				{
					foreach (string dir in dirs)
					{
						// Skip InvalidPackages to avoid re-scanning rejected files
						if (Path.GetFileName(dir).Equals("InvalidPackages", StringComparison.OrdinalIgnoreCase)) continue;

						SafeGetFiles(dir, pattern, results, visited);
					}
				}
			}
			catch (Exception)
			{
				// Ignore access denied or other errors
			}
		}

		public static void SafeGetDirectories(string path, string pattern, List<string> results)
		{
			SafeGetDirectories(path, pattern, results, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		}

		private static void SafeGetDirectories(string path, string pattern, List<string> results, HashSet<string> visited)
		{
			try
			{
				string dirId;
				if (TryGetWindowsFileId(path, out dirId))
				{
					if (!visited.Add(dirId))
					{
						return;
					}
				}

				string[] dirs = Directory.GetDirectories(path, pattern);
				if (dirs != null)
				{
					foreach (string dir in dirs)
					{
						results.Add(dir);
						SafeGetDirectories(dir, pattern, results, visited);
					}
				}
			}
			catch (Exception)
			{
				// Ignore
			}
		}

		//public static void FindAllFiles(string dir, string pattern, List<FileEntry> foundFiles, bool restrictPath = false)
		//{
		//	FindRegularFiles(dir, pattern, foundFiles, restrictPath);
		//	FindVarFiles(dir, pattern, foundFiles);
		//}

		//public static void FindAllFilesRegex(string dir, string regex, List<FileEntry> foundFiles, bool restrictPath = false)
		//{
		//	FindRegularFilesRegex(dir, regex, foundFiles, restrictPath);
		//	FindVarFilesRegex(dir, regex, foundFiles);
		//}

		//public static void FindRegularFiles(string dir, string pattern, List<FileEntry> foundFiles, bool restrictPath = false)
		//{
		//	if (Directory.Exists(dir))
		//	{
		//		if (restrictPath && !IsSecureReadPath(dir))
		//		{
		//			throw new Exception("Attempted to find files for non-secure path " + dir);
		//		}
		//		string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
		//		FindRegularFilesRegex(dir, regex, foundFiles, restrictPath);
		//	}
		//}

		public static bool CheckIfDirectoryChanged(string dir, DateTime previousCheckTime, bool recurse = true)
		{
			return CheckIfDirectoryChanged(dir, previousCheckTime, recurse, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		}

		private static bool CheckIfDirectoryChanged(string dir, DateTime previousCheckTime, bool recurse, HashSet<string> visited)
		{
			if (Directory.Exists(dir))
			{
				string dirId;
				if (TryGetWindowsFileId(dir, out dirId))
				{
					if (!visited.Add(dirId))
					{
						return false;
					}
				}

				DateTime lastWriteTime = Directory.GetLastWriteTime(dir);
				if (lastWriteTime > previousCheckTime)
				{
					return true;
				}
				if (recurse)
				{
					string[] directories = Directory.GetDirectories(dir);
					foreach (string dir2 in directories)
					{
						if (CheckIfDirectoryChanged(dir2, previousCheckTime, recurse, visited))
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		//public static void FindRegularFilesRegex(string dir, string regex, List<FileEntry> foundFiles, bool restrictPath = false)
		//{
		//	dir = CleanDirectoryPath(dir);
		//	if (!Directory.Exists(dir))
		//	{
		//		return;
		//	}
		//	if (restrictPath && !IsSecureReadPath(dir))
		//	{
		//		throw new Exception("Attempted to find files for non-secure path " + dir);
		//	}
		//	string[] files = Directory.GetFiles(dir);
		//	foreach (string text in files)
		//	{
		//		if (Regex.IsMatch(text, regex, RegexOptions.IgnoreCase))
		//		{
		//			SystemFileEntry systemFileEntry = new SystemFileEntry(text);
		//			if (systemFileEntry.Exists)
		//			{
		//				foundFiles.Add(systemFileEntry);
		//			}
		//			else
		//			{
		//				UnityEngine.Debug.LogError("Error in lookup SystemFileEntry for " + text);
		//			}
		//		}
		//	}
		//	string[] directories = Directory.GetDirectories(dir);
		//	foreach (string dir2 in directories)
		//	{
		//		FindRegularFilesRegex(dir2, regex, foundFiles);
		//	}
		//}

		public static void FindVarFiles(string dir, string pattern, List<FileEntry> foundFiles)
		{
			if (allVarFileEntries != null)
			{
				string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
				FindVarFilesRegex(dir, regex, foundFiles);
			}
		}

		public static void FindVarFilesRegex(string dir, string regex, List<FileEntry> foundFiles)
		{
			dir = CleanDirectoryPath(dir);
			if (allVarFileEntries != null)
			{
				foreach (VarFileEntry allVarFileEntry in allVarFileEntries)
				{
					if (allVarFileEntry.InternalPath.StartsWith(dir) && Regex.IsMatch(allVarFileEntry.Name, regex, RegexOptions.IgnoreCase))
					{
						foundFiles.Add(allVarFileEntry);
					}
				}
			}
		}

		public static bool FileExists(string path, bool onlySystemFiles = false, bool restrictPath = false)
		{
			if (string.IsNullOrEmpty(path)) return false;
			if (!onlySystemFiles && GetVarFileEntry(path) != null) return true;
			if (File.Exists(path))
			{
				if (restrictPath && !IsSecureReadPath(path))
				{
					throw new Exception("Attempted to check file existence for non-secure path " + path);
				}
				return true;
			}
			return false;
		}

		public static bool IsFileInPackage(string path)
		{
			string key = CleanFilePath(path);
			if (uidToVarFileEntry != null && uidToVarFileEntry.ContainsKey(key))
			{
				return true;
			}
			if (pathToVarFileEntry != null && pathToVarFileEntry.ContainsKey(key))
			{
				return true;
			}
			return false;
		}

		//public static bool IsHidden(string path, bool restrictPath = false)
		//{
		//	FileEntry fileEntry = GetVarFileEntry(path);
		//	if (fileEntry == null)
		//	{
		//		fileEntry = GetSystemFileEntry(path, restrictPath);
		//	}
		//	return fileEntry?.IsHidden() ?? false;
		//}

		//public static void SetHidden(string path, bool hide, bool restrictPath = false)
		//{
		//	FileEntry fileEntry = GetVarFileEntry(path);
		//	if (fileEntry == null)
		//	{
		//		fileEntry = GetSystemFileEntry(path, restrictPath);
		//	}
		//	fileEntry?.SetHidden(hide);
		//}

		public static FileEntry GetFileEntry(string path, bool restrictPath = false)
		{
			FileEntry fileEntry = GetVarFileEntry(path);
			if (fileEntry == null)
			{
				fileEntry = GetSystemFileEntry(path, restrictPath);
			}
			return fileEntry;
		}

		public static SystemFileEntry GetSystemFileEntry(string path, bool restrictPath = false)
		{
			SystemFileEntry result = null;
			if (File.Exists(path))
			{
				if (restrictPath && !IsSecureReadPath(path))
				{
					throw new Exception("Attempted to get file entry for non-secure path " + path);
				}
				result = new SystemFileEntry(path);
			}
			return result;
		}

		public static VarFileEntry GetVarFileEntry(string path)
		{
			VarFileEntry value = null;
			string key = CleanFilePath(path);
			if ((uidToVarFileEntry != null && uidToVarFileEntry.TryGetValue(key, out value))
				|| (pathToVarFileEntry != null && pathToVarFileEntry.TryGetValue(key, out value)))
			{
                return value;
			}

			if (key != null)
			{
				int colonIdx = key.IndexOf(":/");
				if (colonIdx > 0 && colonIdx + 2 < key.Length)
				{
					string pkgPath = key.Substring(0, colonIdx);
					string internalPath = key.Substring(colonIdx + 2);
					if (internalPath.Length > 0 && internalPath[0] == '/') internalPath = internalPath.Substring(1);

					VarPackage pkg = GetPackage(pkgPath, false);
					if (pkg == null)
					{
						string pkgName = Path.GetFileName(pkgPath);
						if (!string.IsNullOrEmpty(pkgName))
						{
							pkg = GetPackage("AddonPackages/" + pkgName, false);
							if (pkg == null) pkg = GetPackage("AllPackages/" + pkgName, false);
						}
					}

					if (pkg != null && pkg.ZipFile != null)
					{
						try
						{
							var ze = pkg.ZipFile.GetEntry(internalPath);
							if (ze != null)
							{
								value = new VarFileEntry(pkg, ze.Name, ze.DateTime, ze.Size);
								try
								{
									if (uidToVarFileEntry != null && !uidToVarFileEntry.ContainsKey(value.Uid))
									{
										uidToVarFileEntry.Add(value.Uid, value);
									}
									if (pathToVarFileEntry != null && !pathToVarFileEntry.ContainsKey(value.Path))
									{
										pathToVarFileEntry.Add(value.Path, value);
									}
								}
								catch { }
								return value;
							}
						}
						catch { }
					}
				}
			}

			return null;
		}

		public static void SortFileEntriesByLastWriteTime(List<FileEntry> fileEntries)
		{
			fileEntries.Sort((FileEntry e1, FileEntry e2) => e1.LastWriteTime.CompareTo(e2.LastWriteTime));
		}

		public static string CleanDirectoryPath(string path)
		{
			if (path != null)
			{
				string input = path.Replace('\\', '/');
				return Regex.Replace(input, "/$", string.Empty);
			}
			return null;
		}

		public static int FolderContentsCount(string path)
		{
			int num = Directory.GetFiles(path).Length;
			string[] directories = Directory.GetDirectories(path);
			string[] array = directories;
			foreach (string path2 in array)
			{
				num += FolderContentsCount(path2);
			}
			return num;
		}

		public static bool DirectoryExists(string path, bool onlySystemDirectories = false, bool restrictPath = false)
		{
			return false;
		}

		public static bool IsDirectoryInPackage(string path)
		{
			//string key = CleanDirectoryPath(path);
			//if (uidToVarDirectoryEntry != null && uidToVarDirectoryEntry.ContainsKey(key))
			//{
			//	return true;
			//}
			//if (pathToVarDirectoryEntry != null && pathToVarDirectoryEntry.ContainsKey(key))
			//{
			//	return true;
			//}
			return false;
		}

		//public static DirectoryEntry GetDirectoryEntry(string path, bool restrictPath = false)
		//{
		//	string path2 = Regex.Replace(path, "(/|\\\\)$", string.Empty);
		//	DirectoryEntry directoryEntry = GetVarDirectoryEntry(path2);
		//	if (directoryEntry == null)
		//	{
		//		directoryEntry = GetSystemDirectoryEntry(path2, restrictPath);
		//	}
		//	return directoryEntry;
		//}

		//public static SystemDirectoryEntry GetSystemDirectoryEntry(string path, bool restrictPath = false)
		//{
		//	SystemDirectoryEntry result = null;
		//	if (Directory.Exists(path))
		//	{
		//		if (restrictPath && !IsSecureReadPath(path))
		//		{
		//			throw new Exception("Attempted to get directory entry for non-secure path " + path);
		//		}
		//		result = new SystemDirectoryEntry(path);
		//	}
		//	return result;
		//}

		public static VarDirectoryEntry GetVarDirectoryEntry(string path)
		{
			VarDirectoryEntry value = null;
			//string key = CleanDirectoryPath(path);
			//if ((uidToVarDirectoryEntry != null && uidToVarDirectoryEntry.TryGetValue(key, out value)) 
			//	|| pathToVarDirectoryEntry == null || pathToVarDirectoryEntry.TryGetValue(key, out value))
			//{
			//}
			return value;
		}

		public static VarDirectoryEntry GetVarRootDirectoryEntryFromPath(string path)
		{
			VarDirectoryEntry value = null;
			//if (varPackagePathToRootVarDirectory != null)
			//{
			//	varPackagePathToRootVarDirectory.TryGetValue(path, out value);
			//}
			return value;
		}

		//public static string[] GetDirectories(string dir, string pattern = null, bool restrictPath = false)
		//{
		//	if (restrictPath && !IsSecureReadPath(dir))
		//	{
		//		throw new Exception("Attempted to get directories at non-secure path " + dir);
		//	}
		//	List<string> list = new List<string>();
		//	DirectoryEntry directoryEntry = GetDirectoryEntry(dir, restrictPath);
		//	if (directoryEntry == null)
		//	{
		//		throw new Exception("Attempted to get directories at non-existent path " + dir);
		//	}
		//	string text = null;
		//	if (pattern != null)
		//	{
		//		text = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
		//	}
		//	foreach (DirectoryEntry subDirectory in directoryEntry.SubDirectories)
		//	{
		//		if (text == null || Regex.IsMatch(subDirectory.Name, text))
		//		{
		//			list.Add(dir + "\\" + subDirectory.Name);
		//		}
		//	}
		//	return list.ToArray();
		//}

		//public static string[] GetFiles(string dir, string pattern = null, bool restrictPath = false)
		//{
		//	if (restrictPath && !IsSecureReadPath(dir))
		//	{
		//		throw new Exception("Attempted to get files at non-secure path " + dir);
		//	}
		//	List<string> list = new List<string>();
		//	DirectoryEntry directoryEntry = GetDirectoryEntry(dir, restrictPath);
		//	if (directoryEntry == null)
		//	{
		//		throw new Exception("Attempted to get files at non-existent path " + dir);
		//	}
		//	foreach (FileEntry file in directoryEntry.GetFiles(pattern))
		//	{
		//		list.Add(dir + "\\" + file.Name);
		//	}
		//	return list.ToArray();
		//}

		public static void CreateDirectory(string path)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!DirectoryExists(path))
			{
				//if (!IsSecureWritePath(path))
				//{
				//	throw new Exception("Attempted to create directory at non-secure path " + path);
				//}
				Directory.CreateDirectory(path);
			}
		}
		public static void DeleteDirectory(string path, bool recursive = false)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (DirectoryExists(path))
			{
				if (!IsSecureWritePath(path))
				{
					throw new Exception("Attempted to delete file at non-secure path " + path);
				}
				Directory.Delete(path, recursive);
			}
		}
		public static void MoveDirectory(string oldPath, string newPath)
		{
			oldPath = ConvertSimulatedPackagePathToNormalPath(oldPath);
			if (!IsSecureWritePath(oldPath))
			{
				throw new Exception("Attempted to move directory from non-secure path " + oldPath);
			}
			newPath = ConvertSimulatedPackagePathToNormalPath(newPath);
			if (!IsSecureWritePath(newPath))
			{
				throw new Exception("Attempted to move directory to non-secure path " + newPath);
			}
			Directory.Move(oldPath, newPath);
		}
		public static FileEntryStream OpenStream(FileEntry fe)
		{
			if (fe == null)
			{
				throw new Exception("Null FileEntry passed to OpenStreamReader");
			}
			if (fe is VarFileEntry)
			{
				return new VarFileEntryStream(fe as VarFileEntry);
			}
			if (fe is SystemFileEntry)
			{
				return new SystemFileEntryStream(fe as SystemFileEntry);
			}
			throw new Exception("Unknown FileEntry class passed to OpenStreamReader");
		}

		public static FileEntryStream OpenStream(string path, bool restrictPath = false)
		{
			FileEntry fileEntry = GetFileEntry(path, restrictPath);
			if (fileEntry == null)
			{
				throw new Exception("Path " + path + " not found");
			}
			return OpenStream(fileEntry);
		}

		public static FileEntryStreamReader OpenStreamReader(FileEntry fe)
		{
			if (fe == null)
			{
				throw new Exception("Null FileEntry passed to OpenStreamReader");
			}
			if (fe is VarFileEntry)
			{
				return new VarFileEntryStreamReader(fe as VarFileEntry);
			}
			if (fe is SystemFileEntry)
			{
				return new SystemFileEntryStreamReader(fe as SystemFileEntry);
			}
			throw new Exception("Unknown FileEntry class passed to OpenStreamReader");
		}

		public static FileEntryStreamReader OpenStreamReader(string path, bool restrictPath = false)
		{
			FileEntry fileEntry = GetFileEntry(path, restrictPath);
			if (fileEntry == null)
			{
				throw new Exception("Path " + path + " not found");
			}
			return OpenStreamReader(fileEntry);
		}

		public static IEnumerator ReadAllBytesCoroutine(FileEntry fe, byte[] result)
		{
			Thread loadThread = new Thread((ThreadStart)delegate
			{
				byte[] buffer = new byte[32768];
				using (FileEntryStream fileEntryStream = OpenStream(fe))
				{
					using (MemoryStream destination = new MemoryStream(result))
					{
						StreamUtils.Copy(fileEntryStream.Stream, destination, buffer);
					}
				}
			});
			loadThread.Start();
			while (loadThread.IsAlive)
			{
				yield return null;
			}
		}

		public static byte[] ReadAllBytes(string path, bool restrictPath = false)
		{
			FileEntry fileEntry = GetFileEntry(path, restrictPath);
			if (fileEntry == null)
			{
				throw new Exception("Path " + path + " not found");
			}
			return ReadAllBytes(fileEntry);
		}

		public static byte[] ReadAllBytes(FileEntry fe)
		{
			if (fe is VarFileEntry)
			{
				byte[] buffer = new byte[32768];
				using (FileEntryStream fileEntryStream = OpenStream(fe))
				{
					byte[] array = new byte[fe.Size];
					using (MemoryStream destination = new MemoryStream(array))
					{
						StreamUtils.Copy(fileEntryStream.Stream, destination, buffer);
					}
					return array;
				}
			}
			return File.ReadAllBytes(fe.Path);
		}

		public static string ReadAllText(string path, bool restrictPath = false)
		{
			FileEntry fileEntry = GetFileEntry(path, restrictPath);
			if (fileEntry == null)
			{
				throw new Exception("Path " + path + " not found");
			}
			return ReadAllText(fileEntry);
		}

		public static string ReadAllText(FileEntry fe)
		{
			using (FileEntryStreamReader fileEntryStreamReader = OpenStreamReader(fe))
			{
				return fileEntryStreamReader.ReadToEnd();
			}
		}

		public static FileStream OpenStreamForCreate(string path)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsSecureWritePath(path))
			{
				throw new Exception("Attempted to open stream for create at non-secure path " + path);
			}
			return File.Open(path, FileMode.Create);
		}

		public static StreamWriter OpenStreamWriter(string path)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsSecureWritePath(path))
			{
				throw new Exception("Attempted to open stream writer at non-secure path " + path);
			}
			return new StreamWriter(path);
		}

		public static void WriteAllText(string path, string text)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsSecureWritePath(path))
			{
				throw new Exception("Attempted to write all text at non-secure path " + path);
			}
			File.WriteAllText(path, text);
		}

		public static void WriteAllBytes(string path, byte[] bytes)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsSecureWritePath(path))
			{
				throw new Exception("Attempted to write all bytes at non-secure path " + path);
			}
			File.WriteAllBytes(path, bytes);
		}

		public static void SetFileAttributes(string path, FileAttributes attrs)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (!IsSecureWritePath(path))
			{
				throw new Exception("Attempted to set file attributes at non-secure path " + path);
			}
			File.SetAttributes(path, attrs);
		}

		public static void DeleteFile(string path)
		{
			path = ConvertSimulatedPackagePathToNormalPath(path);
			if (File.Exists(path))
			{
				if (!IsSecureWritePath(path))
				{
					throw new Exception("Attempted to delete file at non-secure path " + path);
				}
				File.Delete(path);
			}
		}

		protected static void DoFileCopy(string oldPath, string newPath)
		{
			FileEntry fileEntry = GetFileEntry(oldPath);
			if (fileEntry != null && fileEntry is VarFileEntry)
			{
				byte[] buffer = new byte[4096];
				using (FileEntryStream fileEntryStream = OpenStream(fileEntry))
				{
					using (FileStream destination = OpenStreamForCreate(newPath))
					{
						StreamUtils.Copy(fileEntryStream.Stream, destination, buffer);
					}
				}
			}
			else
			{
				File.Copy(oldPath, newPath);
			}
		}

		public static void CopyFile(string oldPath, string newPath, bool restrictPath = false)
		{
			oldPath = ConvertSimulatedPackagePathToNormalPath(oldPath);
			if (restrictPath && !IsSecureReadPath(oldPath))
			{
				throw new Exception("Attempted to copy file from non-secure path " + oldPath);
			}
			newPath = ConvertSimulatedPackagePathToNormalPath(newPath);
			if (!IsSecureWritePath(newPath))
			{
				throw new Exception("Attempted to copy file to non-secure path " + newPath);
			}
			DoFileCopy(oldPath, newPath);
		}


		protected static void DoFileMove(string oldPath, string newPath, bool overwrite = true)
		{
			if (File.Exists(newPath))
			{
				if (!overwrite)
				{
					throw new Exception("File " + newPath + " exists. Cannot move into");
				}
				File.Delete(newPath);
			}
			File.Move(oldPath, newPath);
		}

		public static void MoveFile(string oldPath, string newPath, bool overwrite = true)
		{
			oldPath = ConvertSimulatedPackagePathToNormalPath(oldPath);
			if (!IsSecureWritePath(oldPath))
			{
				throw new Exception("Attempted to move file from non-secure path " + oldPath);
			}
			newPath = ConvertSimulatedPackagePathToNormalPath(newPath);
			if (!IsSecureWritePath(newPath))
			{
				throw new Exception("Attempted to move file to non-secure path " + newPath);
			}
			DoFileMove(oldPath, newPath, overwrite);
		}

		private void Awake()
		{
			singleton = this;
		}

		private void OnDestroy()
		{
			ClearAll();
		}
	}

}
