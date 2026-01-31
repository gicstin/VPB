using ICSharpCode.SharpZipLib.Zip;
using SimpleJSON;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using System.Runtime.InteropServices;
using Valve.Newtonsoft.Json;
using System.Threading;
using System.Diagnostics;

namespace VPB
{
    [System.Serializable]
    public class SerializableVarPackage
    {
        public List<string> FileEntryNames;
        public List<long> FileEntryLastWriteTimeUtcTicks;
        public List<long> FileEntrySizes;
        public List<string> RecursivePackageDependencies;
        public long VarFileSize;
        public long VarLastWriteTimeUtcTicks;
        public bool IsInvalid;

        public List<string> ClothingFileEntryNames;
        public List<string> ClothingTags;
        public List<string> HairFileEntryNames;
        public List<string> HairTags;

		public void Read(BinaryReader reader)
		{
            Read(reader, true);
        }

        public void Read(BinaryReader reader, bool includeVarMeta)
        {
            //FileEntryNames
            {
                var count = reader.ReadInt32();
                if (count > 0)
                {
                    FileEntryNames = new List<string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        FileEntryNames.Add(reader.ReadString());
                    }
                }
            }
            //FileEntryLastWriteTimeUtcTicks
            {
                var count = reader.ReadInt32();
                if (count > 0)
                {
                    FileEntryLastWriteTimeUtcTicks = new List<long>(count);
                    for (int i = 0; i < count; i++)
                    {
                        FileEntryLastWriteTimeUtcTicks.Add(reader.ReadInt64());
                    }
                }
            }
            //FileEntrySizes
            {
                var count = reader.ReadInt32();
                if (count > 0)
                {
                    FileEntrySizes = new List<long>(count);
                    for (int i = 0; i < count; i++)
                    {
                        FileEntrySizes.Add(reader.ReadInt64());
                    }
                }
            }
            //RecursivePackageDependencies
            {
                var count = reader.ReadInt32();
                if (count > 0)
                {
                    RecursivePackageDependencies = new List<string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        RecursivePackageDependencies.Add(reader.ReadString());
                    }
                }
            }

            //ClothingFileEntryNames
            {
                var count = reader.ReadInt32();
                if (count > 0)
                {
                    ClothingFileEntryNames = new List<string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        ClothingFileEntryNames.Add(reader.ReadString());
                    }
                }
            }
            //ClothingTags
            {
                var count = reader.ReadInt32();
                if (count > 0)
                {
                    ClothingTags = new List<string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        ClothingTags.Add(reader.ReadString());
                    }
                }
            }
            //HairFileEntryNames
            {
                var count = reader.ReadInt32();
                if (count > 0)
                {
                    HairFileEntryNames = new List<string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        HairFileEntryNames.Add(reader.ReadString());
                    }
                }
            }
            //HairTags
            {
                var count = reader.ReadInt32();
                if (count > 0)
                {
                    HairTags = new List<string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        HairTags.Add(reader.ReadString());
                    }
                }
            }
            if (includeVarMeta)
            {
                VarFileSize = reader.ReadInt64();
                VarLastWriteTimeUtcTicks = reader.ReadInt64();
                IsInvalid = reader.ReadBoolean();
            }
        }

		public void Write(BinaryWriter writer)
		{
            //FileEntryNames
            {
                var count = FileEntryNames?.Count ?? 0;
                writer.Write(count);
                if (count > 0)
                {
                    for (int i = 0; i < FileEntryNames.Count; i++)
                    {
						writer.Write(FileEntryNames[i]);
                    }
                }
            }
            //FileEntryLastWriteTimeUtcTicks
            {
                var count = FileEntryLastWriteTimeUtcTicks?.Count ?? 0;
                writer.Write(count);
                if (count > 0)
                {
                    for (int i = 0; i < FileEntryLastWriteTimeUtcTicks.Count; i++)
                    {
                        writer.Write(FileEntryLastWriteTimeUtcTicks[i]);
                    }
                }
            }
            //FileEntrySizes
            {
                var count = FileEntrySizes?.Count ?? 0;
                writer.Write(count);
                if (count > 0)
                {
                    for (int i = 0; i < FileEntrySizes.Count; i++)
                    {
                        writer.Write(FileEntrySizes[i]);
                    }
                }
            }
            //RecursivePackageDependencies
            {
                var count = RecursivePackageDependencies?.Count ?? 0;
                writer.Write(count);
                if (count > 0)
                {
                    for (int i = 0; i < RecursivePackageDependencies.Count; i++)
                    {
                        writer.Write(RecursivePackageDependencies[i]);
                    }
                }
            }

            //ClothingFileEntryNames
            {
                var count = ClothingFileEntryNames?.Count ?? 0;
                writer.Write(count);
                if (count > 0)
                {
                    for (int i = 0; i < ClothingFileEntryNames.Count; i++)
                    {
                        writer.Write(ClothingFileEntryNames[i]);
                    }
                }
            }
            //ClothingTags
            {
                var count = ClothingTags?.Count ?? 0;
                writer.Write(count);
                if (count > 0)
                {
                    for (int i = 0; i < ClothingTags.Count; i++)
                    {
                        writer.Write(ClothingTags[i]);
                    }
                }
            }
            //HairFileEntryNames
            {
                var count = HairFileEntryNames?.Count ?? 0;
                writer.Write(count);
                if (count > 0)
                {
                    for (int i = 0; i < HairFileEntryNames.Count; i++)
                    {
                        writer.Write(HairFileEntryNames[i]);
                    }
                }
            }
            //HairTags
            {
                var count = HairTags?.Count ?? 0;
                writer.Write(count);
                if (count > 0)
                {
                    for (int i = 0; i < HairTags.Count; i++)
                    {
                        writer.Write(HairTags[i]);
                    }
                }
            }
            writer.Write(VarFileSize);
            writer.Write(VarLastWriteTimeUtcTicks);
            writer.Write(IsInvalid);
        }
    }

	public class VarPackage
	{
		List<string> cachedFileEntryNames;
		List<long> cachedFileEntryLastWriteTimeUtcTicks;
		List<long> cachedFileEntrySizes;
		Dictionary<string, int> cachedInternalPathToIndex;
		readonly object cachedEntriesLock = new object();
		List<VarFileEntry> fileEntries;
		private const int CodePageUtf8 = 65001;
		private const int CodePageGbk = 936;
		private const int CodePageSystemDefault = 0;

		private struct ZipNameEncodingCacheItem
		{
			public long Size;
			public long LastWriteTimeUtcTicks;
			public int CodePage;
		}

		// ZipConstants.DefaultCodePage is a global static used by SharpZipLib.
		// VPB scans packages in parallel, so any interaction with DefaultCodePage must be synchronized.
		private static readonly object ZipDefaultCodePageLock = new object();
		private static readonly object ZipNameEncodingCacheLock = new object();
		private static readonly Dictionary<string, ZipNameEncodingCacheItem> ZipNameEncodingCache = new Dictionary<string, ZipNameEncodingCacheItem>(StringComparer.OrdinalIgnoreCase);

		static long scanTotal;
		static long scanCacheValidatedHit;
		static long scanCacheHit;
		static long scanZip;
		static readonly object scanErrorLogLock = new object();
		static readonly HashSet<string> scanErrorLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		static readonly HashSet<string> scanErrorLoggedByUid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		static void LogScanErrorOnce(string uid, string context, Exception ex)
		{
			string uidKey = uid ?? "";
			if (!string.IsNullOrEmpty(uidKey))
			{
				lock (scanErrorLogLock)
				{
					if (scanErrorLoggedByUid.Contains(uidKey)) return;
					scanErrorLoggedByUid.Add(uidKey);
				}
			}

			string key = (uid ?? "") + "|" + (context ?? "");
			lock (scanErrorLogLock)
			{
				if (scanErrorLogged.Contains(key)) return;
				scanErrorLogged.Add(key);
			}
			string msg = (ex != null) ? ex.Message : "";
			LogUtil.LogWarning(context + " " + uid + " : " + msg);
		}

		private static int GetZipNameCodePageForVar(string varPath)
		{
			if (string.IsNullOrEmpty(varPath))
			{
				return CodePageSystemDefault;
			}

			string cleanPath = varPath.Replace('\\', '/');
			FileInfo fi;
			try
			{
				fi = new FileInfo(cleanPath);
			}
			catch
			{
				return CodePageSystemDefault;
			}

			long size = 0;
			long ticks = 0;
			try
			{
				if (!fi.Exists) return CodePageSystemDefault;
				size = fi.Length;
				ticks = fi.LastWriteTimeUtc.Ticks;
			}
			catch
			{
				return CodePageSystemDefault;
			}

			ZipNameEncodingCacheItem cached;
			lock (ZipNameEncodingCacheLock)
			{
				if (ZipNameEncodingCache.TryGetValue(cleanPath, out cached)
					&& cached.Size == size
					&& cached.LastWriteTimeUtcTicks == ticks)
				{
					return cached.CodePage;
				}
			}

			int detected = DetectZipNameCodePage(cleanPath);
			lock (ZipNameEncodingCacheLock)
			{
				ZipNameEncodingCache[cleanPath] = new ZipNameEncodingCacheItem
				{
					Size = size,
					LastWriteTimeUtcTicks = ticks,
					CodePage = detected
				};
			}
			return detected;
		}

		private static int DetectZipNameCodePage(string cleanPath)
		{
			// Fast path:
			// Most packages are either correctly UTF-8 flagged or pure ASCII. In those cases,
			// the system-default decode will look clean and we should not try extra candidates.
			double sysScore = ScoreZipNames(cleanPath, CodePageSystemDefault);
			if (sysScore <= 0.5)
			{
				return CodePageSystemDefault;
			}

			// Suspicious decode: compare against UTF-8 and GBK.
			double utf8Score = ScoreZipNames(cleanPath, CodePageUtf8);
			double gbkScore = ScoreZipNames(cleanPath, CodePageGbk);

			// Pick the lowest score; tie-break: prefer UTF-8.
			int best = CodePageSystemDefault;
			double bestScore = sysScore;
			if (utf8Score < bestScore || (Math.Abs(utf8Score - bestScore) < 0.0001 && best != CodePageUtf8))
			{
				best = CodePageUtf8;
				bestScore = utf8Score;
			}
			if (gbkScore < bestScore)
			{
				best = CodePageGbk;
				bestScore = gbkScore;
			}

			return best;
		}

		private static double ScoreZipNames(string cleanPath, int codePage)
		{
			// Lower is better.
			// Heuristic: penalize replacement chars, control chars, suspicious mojibake sequences, and excessive '?'.
			try
			{
				lock (ZipDefaultCodePageLock)
				{
					int prev = ZipConstants.DefaultCodePage;
					try
					{
						ZipConstants.DefaultCodePage = codePage;
						using (FileStream file = File.Open(cleanPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write | FileShare.Delete))
						using (ZipFile zf = new ZipFile(file))
						{
							zf.IsStreamOwner = true;
							int inspected = 0;
							double total = 0.0;
							const int maxInspect = 60;
							const double earlyExitAvgThreshold = 12.0;
							foreach (ZipEntry ze in zf)
							{
								if (ze == null) continue;
								string name = ze.Name;
								total += ScoreNameString(name);
								inspected++;
								if (inspected >= maxInspect) break;
								// Early exit if it is clearly bad.
								if (inspected >= 10)
								{
									double avg = total / inspected;
									if (avg >= earlyExitAvgThreshold) break;
								}
							}

							if (inspected == 0)
							{
								// Empty archives aren't typical .var; treat as neutral.
								return 0.0;
							}
							return total / inspected;
						}
					}
					catch
					{
						return 1e9;
					}
					finally
					{
						ZipConstants.DefaultCodePage = prev;
					}
				}
			}
			catch
			{
				return 1e9;
			}
		}

		private static double ScoreNameString(string s)
		{
			if (string.IsNullOrEmpty(s)) return 0.0;

			int len = s.Length;
			double score = 0.0;
			int cjk = 0;
			int latin1 = 0;
			int question = 0;

			for (int i = 0; i < len; i++)
			{
				char ch = s[i];
				if (ch == '\uFFFD')
				{
					score += 20.0;
					continue;
				}
				if (ch < 32)
				{
					score += 10.0;
					continue;
				}
				if (ch == '?')
				{
					question++;
					continue;
				}
				if (ch >= 0x4E00 && ch <= 0x9FFF)
				{
					cjk++;
					continue;
				}
				if (ch >= 0x00C0 && ch <= 0x00FF)
				{
					latin1++;
					continue;
				}
			}

			// Penalize many '?' relative to length.
			if (question > 0)
			{
				score += (question * 2.0);
			}

			// Mojibake often yields a lot of Latin-1 supplement chars (Ã, Â, etc) while not producing any CJK.
			if (latin1 > 0 && cjk == 0)
			{
				score += latin1 * 1.5;
				if (s.IndexOf('Ã') >= 0) score += 8.0;
				if (s.IndexOf('Â') >= 0) score += 6.0;
				if (s.IndexOf('Ð') >= 0) score += 6.0;
			}

			return score;
		}

		public static void ResetScanCounters()
		{
			Interlocked.Exchange(ref scanTotal, 0);
			Interlocked.Exchange(ref scanCacheValidatedHit, 0);
			Interlocked.Exchange(ref scanCacheHit, 0);
			Interlocked.Exchange(ref scanZip, 0);
		}

		public static void GetScanCounters(out long total, out long cacheValidatedHit, out long cacheHit, out long zipScan)
		{
			total = Interlocked.Read(ref scanTotal);
			cacheValidatedHit = Interlocked.Read(ref scanCacheValidatedHit);
			cacheHit = Interlocked.Read(ref scanCacheHit);
			zipScan = Interlocked.Read(ref scanZip);
		}

		public enum ReferenceVersionOption
		{
			Latest,
			Minimum,
			Exact
		}

		protected bool _enabled;
		public bool isNewestVersion;
		public bool isNewestEnabledVersion;
		protected string[] cacheFilePatterns = new string[2]
		{
			"*.vmi",
			"*.vam"
		};

		protected JSONClass jsonCache;

		protected VarFileEntry metaEntry;

		public bool invalid
		{
			get;
			protected set;
		}
		public bool IsCorruptedArchive = false;
		public bool Enabled
		{
			get
			{
				return true;
			}

		}

		public bool PluginsAlwaysEnabled
		{
			get
			{
				return true;
			}
		}

		public bool PluginsAlwaysDisabled
		{
			get
			{
				return false;
			}
		}

		public float packProgress
		{
			get;
			protected set;
		}

		public bool IsUnpacking
		{
			get
			{
				return false;
			}
		}

		public bool IsRepacking
		{
			get
			{
				return false;
			}
		}

		public bool HasOriginalCopy
		{
			get
			{
				string path = Path + ".orig";
				return FileManager.FileExists(path);
			}
		}

		// The extracted form is called "simulated"
		public bool IsSimulated
		{
			get { return false; }
		}

		ZipFile m_ZipFile;
		public ZipFile ZipFile
		{
			get
			{
				if (m_ZipFile == null)
				{
					int cp = GetZipNameCodePageForVar(Path);
					if (cp == CodePageSystemDefault)
					{
						FileStream file = File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write | FileShare.Delete);
						m_ZipFile = new ZipFile(file);
						m_ZipFile.IsStreamOwner = true;
					}
					else
					{
						lock (ZipDefaultCodePageLock)
						{
							int prev = ZipConstants.DefaultCodePage;
							try
							{
								ZipConstants.DefaultCodePage = cp;
								FileStream file = File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write | FileShare.Delete);
								m_ZipFile = new ZipFile(file);
								m_ZipFile.IsStreamOwner = true;
							}
							finally
							{
								ZipConstants.DefaultCodePage = prev;
							}
						}
					}
				}
				return m_ZipFile;
			}
			protected set
			{
				m_ZipFile = value;
			}
		}

		public string Uid
		{
			get;
			protected set;
		}

		string m_UidLowerInvariant;
		public string UidLowerInvariant
		{
			get
			{
				if (m_UidLowerInvariant == null)
				{
					m_UidLowerInvariant = this.Uid.ToLowerInvariant();
				}
				return m_UidLowerInvariant;
			}
		}

		public string Path
		{
			get;
			protected set;
		}
		public bool IsInstalled()
        {
			return Path.StartsWith("AddonPackages/");
		}

		public string RelativePath;
		public VarPackageGroup Group
		{
			get;
			protected set;
		}

		public string Name
		{
			get;
			protected set;
		}

		public int Version
		{
			get;
			protected set;
		}

		public ReferenceVersionOption StandardReferenceVersionOption
		{
			get;
			protected set;
		}

		public ReferenceVersionOption ScriptReferenceVersionOption
		{
			get;
			protected set;
		}

		public DateTime LastWriteTime
		{
			get;
			protected set;
		}
		public DateTime CreationTime
		{
			get;
			protected set;
		}

		public long Size
		{
			get;
			protected set;
		}

		public List<VarFileEntry> FileEntries
		{
			get
			{
				EnsureFileEntriesMaterialized();
				return fileEntries;
			}
			protected set
			{
				fileEntries = value;
				if (value != null)
				{
					cachedFileEntryNames = null;
					cachedFileEntryLastWriteTimeUtcTicks = null;
					cachedFileEntrySizes = null;
					cachedInternalPathToIndex = null;
				}
			}
		}

		void EnsureFileEntriesMaterialized()
		{
			if (fileEntries != null) return;
			if (cachedFileEntryNames == null || cachedFileEntryLastWriteTimeUtcTicks == null || cachedFileEntrySizes == null) return;
			lock (cachedEntriesLock)
			{
				if (fileEntries != null) return;
				int count = cachedFileEntryNames.Count;
				fileEntries = new List<VarFileEntry>(count);
				metaEntry = null;
				for (int i = 0; i < count; i++)
				{
					string internalPath = cachedFileEntryNames[i];
					DateTime entryTime = new DateTime(cachedFileEntryLastWriteTimeUtcTicks[i], DateTimeKind.Utc).ToLocalTime();
					VarFileEntry vfe = new VarFileEntry(this, internalPath, entryTime, cachedFileEntrySizes[i]);
					fileEntries.Add(vfe);
					if (internalPath == "meta.json") metaEntry = vfe;
				}
				cachedInternalPathToIndex = null;
			}
		}

		public bool TryCreateVarFileEntryFromCache(string internalPath, out VarFileEntry entry)
		{
			entry = null;
			if (string.IsNullOrEmpty(internalPath)) return false;
			if (cachedFileEntryNames == null || cachedFileEntryLastWriteTimeUtcTicks == null || cachedFileEntrySizes == null) return false;
			lock (cachedEntriesLock)
			{
				if (cachedInternalPathToIndex == null)
				{
					cachedInternalPathToIndex = new Dictionary<string, int>(cachedFileEntryNames.Count, StringComparer.OrdinalIgnoreCase);
					for (int i = 0; i < cachedFileEntryNames.Count; i++)
					{
						string p = cachedFileEntryNames[i];
						if (!cachedInternalPathToIndex.ContainsKey(p)) cachedInternalPathToIndex.Add(p, i);
					}
				}
				int idx;
				if (!cachedInternalPathToIndex.TryGetValue(internalPath, out idx))
				{
					return false;
				}
				DateTime entryTime = new DateTime(cachedFileEntryLastWriteTimeUtcTicks[idx], DateTimeKind.Utc).ToLocalTime();
				entry = new VarFileEntry(this, cachedFileEntryNames[idx], entryTime, cachedFileEntrySizes[idx]);
				return true;
			}
		}

		public bool TryGetCachedFileEntryData(out List<string> names, out List<long> lastWriteTimeUtcTicks, out List<long> sizes)
		{
			names = null;
			lastWriteTimeUtcTicks = null;
			sizes = null;
			if (cachedFileEntryNames == null || cachedFileEntryLastWriteTimeUtcTicks == null || cachedFileEntrySizes == null) return false;
			lock (cachedEntriesLock)
			{
				if (cachedFileEntryNames == null || cachedFileEntryLastWriteTimeUtcTicks == null || cachedFileEntrySizes == null) return false;
				names = cachedFileEntryNames;
				lastWriteTimeUtcTicks = cachedFileEntryLastWriteTimeUtcTicks;
				sizes = cachedFileEntrySizes;
				return true;
			}
		}
		public List<VarFileEntry> ClothingFileEntries
		{
			get;
			protected set;
		}
		public List<VarFileEntry> HairFileEntries
		{
			get;
			protected set;
		}
		public string Description
		{
			get;
			protected set;
		}

		public string Credits
		{
			get { return null; }
		}

		public string Instructions
		{
			get { return null; }
		}

		public string PromotionalLink
		{
			get;
			protected set;
		}
		public List<string> PackageDependencies
		{
			get;
			protected set;
		}

		// Whether all missing dependencies have been checked
		public bool MissingDependenciesChecked = false;
		public bool Scaned = false;
		public List<string> RecursivePackageDependencies;

		public HashSet<string> GetDependenciesDeep(int maxDepth = 2)
		{
			return FileManager.GetDependenciesDeep(this.Uid, maxDepth);
		}

		public List<string> ClothingFileEntryNames;
		public List<string> ClothingTags;
		public List<string> HairFileEntryNames;
		public List<string> HairTags;
		public bool HasMissingDependencies
		{
			get;
			protected set;
		}

		public HashSet<string> PackageDependenciesMissing
		{
			get;
			protected set;
		}

		public List<VarPackage> PackageDependenciesResolved
		{
			get;
			protected set;
		}

		public bool HadReferenceIssues
		{
			get;
			protected set;
		}
		public bool IsOnHub
		{
			get
			{
				if (HubBrowse.singleton != null)
				{
					string packageHubResourceId = HubBrowse.singleton.GetPackageHubResourceId(Uid);
					if (packageHubResourceId != null)
					{
						return true;
					}
				}
				return false;
			}
		}
		public string Creator
		{
			get;
			protected set;
		}
		public VarPackage(string uid, string path, VarPackageGroup group, string creator, string name, int version)
		{
			Uid = uid;// e.g. VAM_GS.Yinping_1_3.2
			Path = path.Replace('\\', '/');// e.g. AllPackages/ReignMocap.RM-ActiveMaleSex.1.var

			if (Path.StartsWith("AddonPackages/"))
				RelativePath = this.Path.Substring("AddonPackages/".Length);
			else if (Path.StartsWith("AllPackages/"))
				RelativePath = this.Path.Substring("AllPackages/".Length);
			else
				LogUtil.LogError("wrong path:"+Path);

			//Debug.Log("VarPackage " + Path+" "+ Uid+ " "+ name);
			Name = name;
			Group = group;
			//GroupName = group.Name;
			Creator = creator;

			Version = version;
			HadReferenceIssues = false;

			//PackageDependencies = new List<string>();
			//PackageDependenciesMissing = new HashSet<string>();
			//PackageDependenciesResolved = new List<VarPackage>();
			if (FileManager.debug)
			{
				//Debug.Log("New package\n Uid: " + Uid + "\n Path: " + Path + "\n FullPath: " + FullPath + "\n SlashPath: " + SlashPath + "\n Name: " + Name + "\n GroupName: " + GroupName + "\n Creator: " + Creator + "\n Version: " + Version);
			}
		}

		protected void SyncEnabled()
		{
			_enabled = true;// !FileManager.FileExists(Path + ".disabled");
		}

		public void Delete()
		{
			if (m_ZipFile != null)
			{
				m_ZipFile.Close();
				m_ZipFile = null;
			}
			if (File.Exists(Path))
			{
				FileManager.DeleteFile(Path);
			}
			else if (Directory.Exists(Path))
			{
				FileManager.DeleteDirectory(Path, true);
			}
			string path = Path + ".disabled";
			if (File.Exists(path))
			{
				FileManager.DeleteFile(path);
			}
			FileManager.Refresh();
		}

		public void RestoreFromOriginal()
		{
			string text = Path + ".orig";
			if (!FileManager.FileExists(text))
			{
				return;
			}
			if (FileManager.DirectoryExists(Path))
			{
				FileManager.DeleteDirectory(Path, true);
			}
			else if (FileManager.FileExists(Path))
			{
				if (m_ZipFile != null)
				{
					m_ZipFile.Close();
					m_ZipFile = null;
				}
				FileManager.DeleteFile(Path);
			}
			FileManager.MoveFile(text, Path);
		}
		public bool HasMatchingDirectories(string dir)
		{
			return false;
		}

		public void FindFiles(string dir, string pattern, List<FileEntry> foundFiles)
		{
			string pattern2 = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
			foreach (VarFileEntry fileEntry in FileEntries)
			{
				if (fileEntry.InternalPath.StartsWith(dir) && Regex.IsMatch(fileEntry.Name, pattern2))
				{
					foundFiles.Add(fileEntry);
				}
			}
		}

		public void OpenOnHub()
		{
			if (VPB.HubBrowse.singleton != null)
			{
				string packageHubResourceId = VPB.HubBrowse.singleton.GetPackageHubResourceId(Uid);
				if (packageHubResourceId != null)
				{
					VPB.HubBrowse.singleton.OpenDetail(packageHubResourceId);
				}
			}
		}

		public void Dispose()
		{
			if (m_ZipFile != null)
			{
				m_ZipFile.Close();
				m_ZipFile = null;
			}
		}

		public void SyncJSONCache()
		{
		}
		public void Scan()
		{
			if (Scaned) return;

			{
				if (IsCorruptedArchive)
				{
					invalid = true;
					Scaned = true;
					return;
				}
				Interlocked.Increment(ref scanTotal);
				FileEntries = new List<VarFileEntry>();
				//ClothingFileEntries = new List<VarFileEntry>();
				//HairFileEntries = new List<VarFileEntry>();
				SyncEnabled();
				bool flag = false;
				if (File.Exists(Path))
				{
					ZipFile zipFile = null;
					long lastWriteUtcTicks = 0;
					try
					{
						metaEntry = null;
						FileInfo fileInfo = new FileInfo(Path);
						LastWriteTime = fileInfo.LastWriteTime;
						CreationTime = fileInfo.CreationTime;
						Size = fileInfo.Length;
						lastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks;
						SerializableVarPackage vp = VarPackageMgr.singleton.TryGetCacheValidated(this.Uid, Size, lastWriteUtcTicks);
						if (vp != null && vp.FileEntryNames != null)
						{
							if (vp.IsInvalid)
							{
								invalid = true;
								Scaned = true;
								return;
							}
							// Fast path: keep cached lists and defer VarFileEntry object creation
							fileEntries = null;
							cachedFileEntryNames = vp.FileEntryNames;
							cachedFileEntryLastWriteTimeUtcTicks = vp.FileEntryLastWriteTimeUtcTicks;
							cachedFileEntrySizes = vp.FileEntrySizes;
							cachedInternalPathToIndex = null;
							this.ClothingFileEntryNames = vp.ClothingFileEntryNames;
							this.ClothingTags = vp.ClothingTags;
							this.HairFileEntryNames = vp.HairFileEntryNames;
							this.HairTags = vp.HairTags;
							this.RecursivePackageDependencies = vp.RecursivePackageDependencies;
							flag = true;
							Interlocked.Increment(ref scanCacheHit);
							Interlocked.Increment(ref scanCacheValidatedHit);
							Scaned = true;
							return;
						}

						if (!flag)
						{
							Interlocked.Increment(ref scanZip);
							FileStream file = File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write | FileShare.Delete);
							zipFile = new ZipFile(file);
							zipFile.IsStreamOwner = true;
							HashSet<string> set = new HashSet<string>();
							IEnumerator enumerator = zipFile.GetEnumerator();
							try
							{
								while (enumerator.MoveNext())
								{
									ZipEntry zipEntry = (ZipEntry)enumerator.Current;
									if (zipEntry.IsFile)
									{
										string entryName = zipEntry.Name;
										if (entryName.EndsWith(".json"))
										{
											if (zipEntry.Name == "meta.json")
											{
												VarFileEntry varFileEntry = new VarFileEntry(this, zipEntry.Name, zipEntry.DateTime, zipEntry.Size);
												FileEntries.Add(varFileEntry);
												if (zipEntry.Name == "meta.json")
												{
													metaEntry = varFileEntry;
												}
											}
											else
											{
												string entry = entryName.Substring(0, entryName.Length - 5) + ".jpg";
												if (!set.Contains(entry))
												{
													ZipEntry jpgEntry = zipFile.GetEntry(entry);
													if (jpgEntry != null)
													{
														VarFileEntry varFileEntry = new VarFileEntry(this, zipEntry.Name, zipEntry.DateTime, zipEntry.Size);
														FileEntries.Add(varFileEntry);
														VarFileEntry varFileEntry2 = new VarFileEntry(this, jpgEntry.Name, jpgEntry.DateTime, jpgEntry.Size);
														FileEntries.Add(varFileEntry2);

														set.Add(entry);

													}
												}
											}
										}
										else if (entryName.EndsWith(".vap"))
										{
											VarFileEntry varFileEntry = new VarFileEntry(this, entryName, zipEntry.DateTime, zipEntry.Size);
											FileEntries.Add(varFileEntry);

											string baseName = entryName.Substring(0, entryName.Length - 4);
											string[] sisterExts = { ".jpg", ".png" };
											foreach (var sExt in sisterExts)
											{
												string sisterPath = baseName + sExt;
												if (!set.Contains(sisterPath))
												{
													ZipEntry sisterZipEntry = zipFile.GetEntry(sisterPath);
													if (sisterZipEntry != null)
													{
														FileEntries.Add(new VarFileEntry(this, sisterZipEntry.Name, sisterZipEntry.DateTime, sisterZipEntry.Size));
														set.Add(sisterPath);
													}
												}
											}
										}
										else if (entryName.EndsWith(".vam"))
										{
											string entry = entryName.Substring(0, entryName.Length - 4) + ".jpg";
											if (!set.Contains(entry))
											{
												ZipEntry jpgEntry = zipFile.GetEntry(entry);
												if (jpgEntry != null)
												{
													VarFileEntry varFileEntry = new VarFileEntry(this, zipEntry.Name, zipEntry.DateTime, zipEntry.Size);
													FileEntries.Add(varFileEntry);
													VarFileEntry varFileEntry2 = new VarFileEntry(this, jpgEntry.Name, jpgEntry.DateTime, jpgEntry.Size);
													FileEntries.Add(varFileEntry2);
													set.Add(entry);

													if(zipEntry.Name.StartsWith("Custom/Clothing/"))
                                                {
                                                    if (ClothingFileEntries == null)
                                                        ClothingFileEntries = new List<VarFileEntry>();
                                                    ClothingFileEntries.Add(varFileEntry);
													}
													if (zipEntry.Name.StartsWith("Custom/Hair/"))
													{
														if (HairFileEntries == null)
															HairFileEntries = new List<VarFileEntry>();
														HairFileEntries.Add(varFileEntry);
													}
												}
											}
										}
										// There are too many morphs; over 2,000 packages can contain ~80k morphs
										//else if (entryName.EndsWith(".vmi"))
         //                           {
									//	VarFileEntry varFileEntry = new VarFileEntry(this, zipEntry.Name, zipEntry.DateTime, zipEntry.Size);
									//	FileEntries.Add(varFileEntry);
									//}
										else if (entryName.EndsWith(".assetbundle"))
										{
											VarFileEntry varFileEntry = new VarFileEntry(this, zipEntry.Name, zipEntry.DateTime, zipEntry.Size);
											FileEntries.Add(varFileEntry);
											// liu modification: add asset preview image
											string entry = entryName.Substring(0, entryName.Length - 12) + ".jpg";
											//SuperController.LogMessage("assetbundle:"+ entry);
											if (!set.Contains(entry))
											{
												ZipEntry jpgEntry = zipFile.GetEntry(entry);
												if (jpgEntry != null)
												{

													VarFileEntry varFileEntry2 = new VarFileEntry(this, jpgEntry.Name, jpgEntry.DateTime, jpgEntry.Size);
													FileEntries.Add(varFileEntry2);
													set.Add(entry);
												}
											}
										}
									}
								}
							}
						finally
						{
							IDisposable disposable;
							if ((disposable = (enumerator as IDisposable)) != null)
							{
								disposable.Dispose();
							}
						}
					}

						if (metaEntry != null)
						{
							flag = true;
						}
						ZipFile = zipFile;
						DumpVarPackage();
						if (invalid)
						{
							try
							{
								SerializableVarPackage bad = new SerializableVarPackage();
								bad.VarFileSize = Size;
								bad.VarLastWriteTimeUtcTicks = lastWriteUtcTicks;
								bad.IsInvalid = true;
								VarPackageMgr.singleton.SetCache(this.Uid, bad);
							}
							catch { }
							Scaned = true;
							return;
						}
					}
					catch (Exception ex)
					{
						zipFile?.Close();
						if (ex is ZipException)
						{
							IsCorruptedArchive = true;
							invalid = true;
							LogScanErrorOnce(Uid, "Exception during zip file scan of", ex);
						}
						else
						{
							LogScanErrorOnce(Uid, "Exception during zip file scan of", ex);
						}
						try
						{
							SerializableVarPackage bad = new SerializableVarPackage();
							bad.VarFileSize = Size;
							bad.VarLastWriteTimeUtcTicks = lastWriteUtcTicks;
							bad.IsInvalid = true;
							VarPackageMgr.singleton.SetCache(this.Uid, bad);
						}
						catch { }
						Scaned = true;
						return;
					}
				}
				if (!flag)
				{
					invalid = true;
				}
				else
				{
					SyncJSONCache();
				}
				if (invalid)
				{
					Scaned = true;
					return;
				}

				// Initialize clothing tags
				if (ClothingFileEntryNames != null && ClothingFileEntryNames.Count > 0)
				{
					Dictionary<string, string> tags = new Dictionary<string, string>();
					for(int i = 0; i < ClothingFileEntryNames.Count; i++)
					{
						tags.Add(ClothingFileEntryNames[i], ClothingTags[i]);
					}
					foreach (var item in FileEntries)
					{
						if (tags.ContainsKey(item.InternalPath))
						{
							string tag = tags[item.InternalPath];
							string[] splits = tag.Split(',');

							if (item.ClothingTags == null)
								item.ClothingTags = new List<string>();
							else
								item.ClothingTags.Clear();

							for (int i = 0; i < splits.Length; i++)
							{
								string t = splits[i].Trim();
								if(!string.IsNullOrEmpty(t))
								{
									lock (TagFilter.ClothingUnknownTagsLock)
									{
										if (!TagFilter.AllClothingTags.Contains(t) && !TagFilter.ClothingUnknownTags.Contains(t))
										{
											TagFilter.ClothingUnknownTags.Add(t);
											//LogUtil.Log("clothing tag " + t);
										}
									}
									item.ClothingTags.Add(t);
								}
							}
						}
					}
				}

				if (HairFileEntryNames != null && HairFileEntryNames.Count > 0)
				{
					Dictionary<string, string> tags = new Dictionary<string, string>();
					for (int i = 0; i < HairFileEntryNames.Count; i++)
					{
						tags.Add(HairFileEntryNames[i], HairTags[i]);
					}
					foreach (var item in FileEntries)
					{
						if (tags.ContainsKey(item.InternalPath))
						{
							string tag = tags[item.InternalPath];
							string[] splits = tag.Split(',');

							if (item.HairTags == null)
								item.HairTags = new List<string>();
							else
								item.HairTags.Clear();

							for (int i = 0; i < splits.Length; i++)
							{
								string t = splits[i].Trim();
								if (!string.IsNullOrEmpty(t))
								{
									if (!TagFilter.AllHairTags.Contains(t))
									{
										lock (TagFilter.HairUnknownTagsLock)
										{
											if (!TagFilter.HairUnknownTags.Contains(t))
											{
												TagFilter.HairUnknownTags.Add(t);
												//LogUtil.Log("hair tag " + t);
											}
										}
									}
									item.HairTags.Add(t);
								}
							}
						}
					}
				}
				Scaned = true;
			}
		}
        //void FixVarName(string createName, string packageName)
        //{
        //    string uid = createName + "." + packageName + "." + Version;
			
        //    if (this.Uid != uid)
        //    {
        //        this.Uid = uid;
        //        fixUid = true;
        //    }
        //}
        void DumpVarPackage()
		{
			if (IsCorruptedArchive)
				return;
			SerializableVarPackage svp = new SerializableVarPackage();
			List<string> clothingFileList = null;
			List<string> clothingTags = null;
            if (ClothingFileEntries != null)
            {
				clothingFileList = new List<string>();
				clothingTags = new List<string>();
				foreach (var item in ClothingFileEntries)
				{
					try
					{
						using (VarFileEntryStreamReader varFileEntryStreamReader = new VarFileEntryStreamReader(item))
						{
							string aJSON = varFileEntryStreamReader.ReadToEnd();
							JSONClass asObject = JSON.Parse(aJSON).AsObject;
							if (asObject != null)
							{
								if (asObject["tags"] != null)
								{
									string tag = asObject["tags"];
									tag = tag.Trim();
									if (!string.IsNullOrEmpty(tag))
									{
										clothingFileList.Add(item.InternalPath);
										clothingTags.Add(tag.ToLowerInvariant());
									}
								}
							}
						}
					}
					catch (Exception ex)
					{
						if (ex is ZipException)
						{
							IsCorruptedArchive = true;
							invalid = true;
							LogScanErrorOnce(Uid, "DumpVarPackage ClothingFileEntries", ex);
							return;
						}
						LogScanErrorOnce(Uid, "DumpVarPackage ClothingFileEntries", ex);
					}
				}
			}

			List<string> hairFileList = null;
			List<string> hairTags = null;
            if (HairFileEntries != null)
            {
				hairFileList = new List<string>();
				hairTags = new List<string>();
				foreach (var item in HairFileEntries)
				{
					try
					{
						using (VarFileEntryStreamReader varFileEntryStreamReader = new VarFileEntryStreamReader(item))
						{
							string aJSON = varFileEntryStreamReader.ReadToEnd();
							JSONClass asObject = JSON.Parse(aJSON).AsObject;
							if (asObject != null)
							{
								if (asObject["tags"] != null)
								{
									string tag = asObject["tags"];
									tag = tag.Trim();
									if (!string.IsNullOrEmpty(tag))
									{
										hairFileList.Add(item.InternalPath);
										hairTags.Add(tag.ToLowerInvariant());
									}
								}
							}
						}
					}
					catch (Exception ex)
					{
						if (ex is ZipException)
						{
							IsCorruptedArchive = true;
							invalid = true;
							LogScanErrorOnce(Uid, "DumpVarPackage HairFileEntries", ex);
							return;
						}
						LogScanErrorOnce(Uid, "DumpVarPackage HairFileEntries", ex);
					}
				}
			}


			if (metaEntry != null)
			{
				try
				{
					using (VarFileEntryStreamReader varFileEntryStreamReader = new VarFileEntryStreamReader(metaEntry))
					{
						string aJSON = varFileEntryStreamReader.ReadToEnd();
						JSONClass asObject = JSON.Parse(aJSON).AsObject;
						if (asObject != null)
						{
                            HashSet<string> depends = new HashSet<string>();
							// Find dependencies
							GetDependenciesRecursive(asObject, depends);
							HashSet<string> scripts = new HashSet<string>();

							svp.RecursivePackageDependencies = depends.ToList();
						}
					}
				}
				catch (Exception ex3)
				{
					if (ex3 is ZipException)
					{
						IsCorruptedArchive = true;
						invalid = true;
						LogScanErrorOnce(Uid, "DumpVarPackage", ex3);
						return;
					}
					LogScanErrorOnce(Uid, "DumpVarPackage", ex3);
				}
			}

			List<string> list1 = new List<string>();
			List<long> list2 = new List<long>();
			List<long> list3 = new List<long>();
			foreach (var item in FileEntries)
			{
				list1.Add(item.InternalPath);
				list2.Add(item.LastWriteTime.ToUniversalTime().Ticks);
				list3.Add(item.Size);
			}
			svp.FileEntryNames = list1;//.ToArray();
			svp.FileEntryLastWriteTimeUtcTicks = list2;//.ToArray();
			svp.FileEntrySizes = list3;//.ToArray();
			svp.VarFileSize = Size;
			svp.VarLastWriteTimeUtcTicks = LastWriteTime.ToUniversalTime().Ticks;
			if (clothingFileList != null && clothingFileList.Count > 0)
                svp.ClothingFileEntryNames = clothingFileList;//.ToArray();
			if (clothingTags != null && clothingTags.Count > 0)
                svp.ClothingTags = clothingTags;//.ToArray();
			if (hairFileList != null && hairFileList.Count > 0)
                svp.HairFileEntryNames = hairFileList;//.ToArray();
			if (hairTags != null && hairTags.Count > 0)
                svp.HairTags = hairTags;//.ToArray();

			this.RecursivePackageDependencies = svp.RecursivePackageDependencies;
			this.ClothingFileEntryNames = svp.ClothingFileEntryNames;
			this.ClothingTags = svp.ClothingTags;
			this.HairFileEntryNames = svp.HairFileEntryNames;
			this.HairTags = svp.HairTags;

			if (VarPackageMgr.singleton != null)
			{
				VarPackageMgr.singleton.SetCache(this.Uid, svp);
			}
		}

		protected void FindMissingDependenciesRecursive(JSONClass jc)
		{
			JSONClass asObject = jc["dependencies"].AsObject;
			if (asObject != null)
			{
				foreach (string key in asObject.Keys)
				{
					VarPackage package = FileManager.GetPackageForDependency(key, false);
					if (package == null)
					{
						HasMissingDependencies = true;
						PackageDependenciesMissing.Add(key);
					}
					JSONClass asObject2 = asObject[key].AsObject;
					if (asObject2 != null)
					{
						FindMissingDependenciesRecursive(asObject2);
					}
				}
			}
		}
		void GetDependenciesRecursive(JSONClass jc, HashSet<string> depends)
		{
			JSONClass asObject = jc["dependencies"].AsObject;
			if (asObject != null)
			{
				foreach (string key in asObject.Keys)
				{
					depends.Add(key);
					JSONClass asObject2 = asObject[key].AsObject;
					if (asObject2 != null)
					{
						GetDependenciesRecursive(asObject2, depends);
					}
				}
			}
		}
		public bool InstallRecursive()
        {
			if (Settings.Instance != null && Settings.Instance.LoadDependenciesWithPackage != null && !Settings.Instance.LoadDependenciesWithPackage.Value)
			{
				return InstallSelf();
			}
            return InstallRecursive(new HashSet<string>());
        }

		public bool InstallRecursive(HashSet<string> visited)
		{
            if (visited.Contains(this.Uid)) return false;
            visited.Add(this.Uid);

			bool flag = false;
			bool dirty= InstallSelf();
			if (dirty) flag = true;
			
			//string linkvar = "AddonPackages/" + this.Uid + ".var";
            if (this.RecursivePackageDependencies != null)
            {
				foreach (var key in this.RecursivePackageDependencies)
				{
					VarPackage package = FileManager.GetPackageForDependency(key, false);
					if (package != null)
					{
						bool dirty2= package.InstallRecursive(visited);
						if (dirty2) flag = true;
					}
				}
			}
			if(flag)
				return true;
			return false;
		}
		public bool InstallSelf()
		{
			if (this.Path.StartsWith("AddonPackages/")) return false;

			string linkvar = null;
			if (this.Path.StartsWith("AllPackages/"))
			{
				linkvar = "AddonPackages" + this.Path.Substring("AllPackages".Length);
			}
			else
			{
				linkvar = "AddonPackages/" + System.IO.Path.GetFileName(this.Path);
			}

			if (File.Exists(linkvar)) return false;
			if (Directory.Exists(linkvar))// A directory with the same name may exist
            {
				LogUtil.LogError("InstallSelf " + this.Path+" exist directory with same name");
				return false;
            }

			LogUtil.Log($"Installing package: {Uid} from {this.Path} to {linkvar}");

			// Install must be atomic.
			// On Windows, File.Move across volumes falls back to copy+delete, which can expose a partially-copied
			// file at the final path. VaM may read it immediately and throw ZipException (wrong local header).
			string dir = System.IO.Path.GetDirectoryName(linkvar);
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			string sourcePath = this.Path;
			string tempTarget = linkvar + ".installing";
			try
			{
				if (File.Exists(tempTarget)) File.Delete(tempTarget);
				File.Copy(sourcePath, tempTarget, false);
				File.Move(tempTarget, linkvar);
				File.Delete(sourcePath);
			}
			finally
			{
				try { if (File.Exists(tempTarget)) File.Delete(tempTarget); } catch { }
			}
			this.Path = linkvar.Replace('\\', '/');
			if (this.Path.StartsWith("AddonPackages/"))
				RelativePath = this.Path.Substring("AddonPackages/".Length);
			FileInfo info = new FileInfo(linkvar);
			if (info != null)
				info.Refresh();
			return true;
		}
		public bool UninstallSelf()
        {
			if (!this.Path.StartsWith("AddonPackages/"))
            {
				LogUtil.LogError("Uninstall From AddonPackages "+this.Path);
				return false;
            }
			string linkvar = "AllPackages" + this.Path.Substring("AddonPackages".Length);
			if (File.Exists(linkvar))
            {
				LogUtil.LogError("Uninstall From AddonPackages Exists "+this.Path);
				return false;
			}
			// Move the file
			string dir = System.IO.Path.GetDirectoryName(linkvar);
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);
			File.Move(this.Path, linkvar);
			this.Path = linkvar.Replace('\\', '/');
			FileInfo info = new FileInfo(linkvar);
            if (info != null)
                info.Refresh();
            return true;
		}
	}

}
