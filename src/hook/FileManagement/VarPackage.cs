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
        /// <summary>Internal "created" from zip header (meta.json entry time), stored as <see cref="DateTime.ToBinary"/>.</summary>
        public long VarInternalCreationTimeBinary;

        public List<string> ClothingFileEntryNames;
        public List<string> ClothingTags;
        public List<string> HairFileEntryNames;
        public List<string> HairTags;
        public List<string> MorphFileEntryNames;
        /// <summary>Windows code page for zip entry names; 0 = system default (legacy rows).</summary>
        public int ZipNameCodePage;
        /// <summary>
        /// Trailing int after <see cref="ZipNameCodePage"/>. 0 = pre-scene-json scan rule.
        /// <see cref="VarPackage.CurrentScanRuleStamp"/> marks caches written after VAR scene JSON
        /// is indexed even when sister jpg GetEntry misses.
        /// </summary>
        public int ScanRuleStamp;

        const int MorphIndexPayloadMagic = 0x56504D49;
        const int MorphIndexPayloadVersion = 1;
        const int MaxMorphIndexEntriesPerPackage = 250000;

		public void Read(BinaryReader reader)
		{
            Read(reader, true);
        }

        public void ReadPayloadBody(BinaryReader reader)
        {
            Read(reader, false);
        }

        public byte[] SerializePayloadBody()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(ms))
                {
                    WritePayloadBody(writer);
                    WriteMorphIndexPayloadTrailer(writer);
                    writer.Flush();
                }
                return ms.ToArray();
            }
        }

        public void DeserializePayloadBody(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            using (MemoryStream ms = new MemoryStream(data, false))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                ReadPayloadBody(reader);
                TryReadMorphIndexPayloadTrailer(reader);
            }
        }

        void WriteMorphIndexPayloadTrailer(BinaryWriter writer)
        {
            writer.Write(MorphIndexPayloadMagic);
            writer.Write(MorphIndexPayloadVersion);
            int count = MorphFileEntryNames != null ? MorphFileEntryNames.Count : -1;
            writer.Write(count);
            for (int i = 0; i < count; i++)
                writer.Write(MorphFileEntryNames[i] ?? string.Empty);
        }

        void TryReadMorphIndexPayloadTrailer(BinaryReader reader)
        {
            MorphFileEntryNames = null;
            if (reader == null || reader.BaseStream == null) return;
            if (reader.BaseStream.Length - reader.BaseStream.Position < 12) return;
            try
            {
                int magic = reader.ReadInt32();
                int version = reader.ReadInt32();
                int count = reader.ReadInt32();
                if (magic != MorphIndexPayloadMagic || version != MorphIndexPayloadVersion)
                    return;
                if (count == -1) return;
                if (count < 0 || count > MaxMorphIndexEntriesPerPackage) return;

                List<string> paths = new List<string>(count);
                for (int i = 0; i < count; i++)
                    paths.Add(reader.ReadString());
                MorphFileEntryNames = paths;
            }
            catch
            {
                // Truncated/corrupt optional trailer stays unknown; migration repairs it from ZIP directory.
                MorphFileEntryNames = null;
            }
        }

        public void Read(BinaryReader reader, bool includeVarMeta)
        {
            Read(reader, includeVarMeta, 7);
        }

        public void Read(BinaryReader reader, bool includeVarMeta, int legacyVersion)
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
            ScanRuleStamp = 0;
            if (!includeVarMeta
                && reader.BaseStream != null
                && reader.BaseStream.Length - reader.BaseStream.Position >= 4)
            {
                try { ScanRuleStamp = reader.ReadInt32(); } catch { ScanRuleStamp = 0; }
            }
            if (includeVarMeta)
            {
                VarFileSize = reader.ReadInt64();
                VarLastWriteTimeUtcTicks = reader.ReadInt64();
                IsInvalid = reader.ReadBoolean();
                VarInternalCreationTimeBinary = reader.ReadInt64();
                ZipNameCodePage = 0;
                // v7 added trailing ZipNameCodePage int. For v6, those 4 bytes belong to the next
                // record's string-length prefix; reading them here shifts position and corrupts the rest.
                if (legacyVersion >= 7
                    && reader.BaseStream != null
                    && reader.BaseStream.Length - reader.BaseStream.Position >= 4)
                {
                    try { ZipNameCodePage = reader.ReadInt32(); } catch { ZipNameCodePage = 0; }
                }
            }
        }

		public void Write(BinaryWriter writer)
		{
            WritePayloadBody(writer);
            writer.Write(VarFileSize);
            writer.Write(VarLastWriteTimeUtcTicks);
            writer.Write(IsInvalid);
            writer.Write(VarInternalCreationTimeBinary);
            writer.Write(ZipNameCodePage);
        }

        public void WritePayloadBody(BinaryWriter writer)
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
            writer.Write(ScanRuleStamp);
        }
    }

	public class VarPackage
	{
		List<string> cachedFileEntryNames;
		List<long> cachedFileEntryLastWriteTimeUtcTicks;
		List<long> cachedFileEntrySizes;
		Dictionary<string, int> cachedInternalPathToIndex;
		readonly object cachedEntriesLock = new object();
		volatile List<VarFileEntry> fileEntries;
		readonly object catalogContentKindsLock = new object();
		int cachedCatalogContentKinds = -1;
		private const int CodePageUtf8 = 65001;
		private const int CodePageGbk = 936;
		private const int CodePageSystemDefault = 0;

		/// <summary>Plugins under Custom/Scripts as .cs / .cslist / .dll (paths may use \ or / in zip).</summary>
		private static bool IsPluginScriptZipEntry(string entryName)
		{
			if (string.IsNullOrEmpty(entryName)) return false;
			string norm = entryName.Replace('\\', '/');
			if (!norm.StartsWith("Custom/Scripts/", StringComparison.OrdinalIgnoreCase)) return false;
			return norm.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
				|| norm.EndsWith(".cslist", StringComparison.OrdinalIgnoreCase)
				|| norm.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
		}

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

		/// <summary>Per-package code page from manifest; <see cref="int.MinValue"/> when unset.</summary>
		int _resolvedZipNameCodePage = int.MinValue;

		// Only central-directory name decode shares global code-page lock; enumeration is isolated after open.
		static ZipFile OpenZipFileForRead(string varPath, int codePage)
		{
			FileStream file = File.Open(varPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write | FileShare.Delete);
			if (codePage == CodePageSystemDefault)
			{
				ZipFile zf = new ZipFile(file);
				zf.IsStreamOwner = true;
				return zf;
			}
			lock (ZipDefaultCodePageLock)
			{
				int prev = ZipConstants.DefaultCodePage;
				try
				{
					ZipConstants.DefaultCodePage = codePage;
					ZipFile zf = new ZipFile(file);
					zf.IsStreamOwner = true;
					return zf;
				}
				finally
				{
					ZipConstants.DefaultCodePage = prev;
				}
			}
		}

		internal static string NormalizeZipInternalPath(string name)
		{
			if (string.IsNullOrEmpty(name)) return name;
			return name.Replace('\\', '/');
		}

		/// <summary>
		/// Gallery scene/subscene JSON inside a VAR. Local scenes do not require a sister jpg;
		/// VAR scan used to, and <see cref="ZipFile.GetEntry"/> misses packers that store backslashes
		/// (same workaround as cslist lookup). Always index these even when sister lookup fails.
		/// </summary>
		internal static bool IsZipGallerySceneJsonInternalPath(string internalPath)
		{
			string n = NormalizeZipInternalPath(internalPath);
			if (string.IsNullOrEmpty(n) || !n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				return false;
			return n.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase)
				|| n.StartsWith("Custom/SubScene/", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// SharpZipLib <see cref="ZipFile.GetEntry"/> is exact on stored slashes. Try <c>/</c> then <c>\</c>.
		/// </summary>
		internal static ZipEntry FindZipEntry(ZipFile zf, string name)
		{
			if (zf == null || string.IsNullOrEmpty(name)) return null;
			ZipEntry e = zf.GetEntry(name);
			if (e != null) return e;
			if (name.IndexOf('/') >= 0)
				return zf.GetEntry(name.Replace('/', '\\'));
			if (name.IndexOf('\\') >= 0)
				return zf.GetEntry(name.Replace('\\', '/'));
			return null;
		}

		internal const int CurrentScanRuleStamp = 1;

		/// <summary>
		/// Old caches dropped VAR scene JSON when sister jpg GetEntry missed.
		/// CUA+scene packs look like assetbundles with no Saves/scene json.
		/// Stamp 0 + that shape → one zip rescan; dump writes stamp 1 so genuine CUA does not loop.
		/// </summary>
		internal static bool CachedNamesNeedSceneJsonRescan(IList<string> names, int stamp)
		{
			if (stamp >= CurrentScanRuleStamp) return false;
			if (names == null || names.Count == 0) return false;
			bool hasAssetBundle = false;
			for (int i = 0; i < names.Count; i++)
			{
				string n = names[i];
				if (IsZipGallerySceneJsonInternalPath(n)) return false;
				if (!hasAssetBundle && n != null
					&& n.EndsWith(".assetbundle", StringComparison.OrdinalIgnoreCase))
					hasAssetBundle = true;
			}
			return hasAssetBundle;
		}

		void RememberZipNameCodePage(int codePage)
		{
			if (codePage == 0) codePage = CodePageSystemDefault;
			_resolvedZipNameCodePage = codePage;
		}

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

		private int GetZipNameCodePageForVar(string varPath)
		{
			if (_resolvedZipNameCodePage != int.MinValue)
				return _resolvedZipNameCodePage;

			if (!string.IsNullOrEmpty(Uid) && VarPackageMgr.singleton != null)
			{
				try
				{
					SerializableVarPackage manifest = VarPackageMgr.singleton.TryGetCache(Uid);
					if (manifest != null && manifest.ZipNameCodePage != 0)
					{
						RememberZipNameCodePage(manifest.ZipNameCodePage);
						return _resolvedZipNameCodePage;
					}
				}
				catch { }
			}

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
					RememberZipNameCodePage(cached.CodePage);
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
			RememberZipNameCodePage(detected);
			return detected;
		}

		private static int DetectZipNameCodePage(string cleanPath)
		{
			// Clean default decode skips fallback candidates because UTF-8 and ASCII packages dominate.
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
				using (ZipFile zf = OpenZipFileForRead(cleanPath, codePage))
				{
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
						if (inspected >= 10)
						{
							double avg = total / inspected;
							if (avg >= earlyExitAvgThreshold) break;
						}
					}

					if (inspected == 0)
						return 0.0;
					return total / inspected;
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

		readonly object m_ZipFileLock = new object();

		ZipFile m_ZipFile;
		public ZipFile ZipFile
		{
			get
			{
				if (m_ZipFile == null)
				{
					lock (m_ZipFileLock)
					{
						if (m_ZipFile == null)
						{
							int cp = GetZipNameCodePageForVar(Path);
							m_ZipFile = OpenZipFileForRead(Path, cp);
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
			set;
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

		private bool _referenceVersionOptionsLoaded;

		internal void SetStandardReferenceVersionOption(ReferenceVersionOption option)
		{
			StandardReferenceVersionOption = option;
		}

		internal void SetScriptReferenceVersionOption(ReferenceVersionOption option)
		{
			ScriptReferenceVersionOption = option;
		}

		internal void MarkReferenceVersionOptionsLoaded()
		{
			_referenceVersionOptionsLoaded = true;
		}

		internal bool AreReferenceVersionOptionsLoaded
		{
			get { return _referenceVersionOptionsLoaded; }
		}

		// Cache-hit scans skip meta.json, so UI metadata loads lazily outside normalization hot path.
		public bool TryEnsureReferenceVersionOptions()
		{
			if (_referenceVersionOptionsLoaded)
				return true;
			TryEnsureMetaJsonLiteFields();
			_referenceVersionOptionsLoaded = true;
			return true;
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

		public long InternalCreationTimeBinary
		{
			get;
			protected set;
		} = long.MinValue;

		public long FirstScannedBinary { get; set; } = long.MinValue;

		private static long NormalizeZipHeaderTimeBinary(long binaryOrMin)
		{
			if (binaryOrMin == long.MinValue) return long.MinValue;
			try
			{
				var dt = DateTime.FromBinary(binaryOrMin);
				// ZIP/DOS timestamps commonly cannot represent < 1980; some libs surface invalid as 1979-12-31.
				if (dt.Year < 1980) return long.MinValue;
				// Reject clearly bogus future timestamps (clock skew / invalid header).
				if (dt > DateTime.Now.AddDays(2)) return long.MinValue;
				return binaryOrMin;
			}
			catch
			{
				return long.MinValue;
			}
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

		// Browse manifest omits morphs and preview-less items, so catalog absence requires complete ZIP directory.
		internal bool TryGetCompleteCatalogContent(out bool hasClothing, out bool hasHair, out bool hasMorphs)
		{
			hasClothing = false;
			hasHair = false;
			hasMorphs = false;

			lock (catalogContentKindsLock)
			{
				if (cachedCatalogContentKinds < 0)
				{
					int kinds = 0;
					try
					{
						int codePage = GetZipNameCodePageForVar(Path);
						using (ZipFile zip = OpenZipFileForRead(Path, codePage))
						{
							foreach (ZipEntry entry in zip)
							{
								if (entry == null || !entry.IsFile || string.IsNullOrEmpty(entry.Name)) continue;
								string name = entry.Name.Replace('\\', '/');
								if (name.EndsWith(".vam", StringComparison.OrdinalIgnoreCase))
								{
									if (name.StartsWith("Custom/Clothing/", StringComparison.OrdinalIgnoreCase)) kinds |= 1;
									if (name.StartsWith("Custom/Hair/", StringComparison.OrdinalIgnoreCase)) kinds |= 2;
								}
								else if (name.EndsWith(".vmi", StringComparison.OrdinalIgnoreCase)
									&& name.StartsWith("Custom/Atom/Person/Morphs/", StringComparison.OrdinalIgnoreCase))
								{
									kinds |= 4;
								}

								if (kinds == 7) break;
							}
						}
					}
					catch
					{
						return false;
					}
					cachedCatalogContentKinds = kinds;
				}

				hasClothing = (cachedCatalogContentKinds & 1) != 0;
				hasHair = (cachedCatalogContentKinds & 2) != 0;
				hasMorphs = (cachedCatalogContentKinds & 4) != 0;
				return true;
			}
		}

		internal bool EnsureMorphFileEntryNamesIndexed()
		{
			if (MorphFileEntryNames != null) return true;
			string scanPath = Path;
			if (string.IsNullOrEmpty(scanPath)) return false;

			DateTime creationTime;
			DateTime lastWriteTime;
			long size;
			if (!FileStat.TryGetFileStat(scanPath, out creationTime, out lastWriteTime, out size)) return false;
			long mtimeTicks = lastWriteTime.ToUniversalTime().Ticks;
			List<string> paths = new List<string>();
			try
			{
				int codePage = GetZipNameCodePageForVar(scanPath);
				using (ZipFile zip = OpenZipFileForRead(scanPath, codePage))
				{
					foreach (ZipEntry entry in zip)
					{
						if (entry == null || !entry.IsFile || string.IsNullOrEmpty(entry.Name)) continue;
						string name = entry.Name.Replace('\\', '/');
						if (name.StartsWith("Custom/Atom/Person/Morphs/", StringComparison.OrdinalIgnoreCase)
							&& name.EndsWith(".vmi", StringComparison.OrdinalIgnoreCase))
							paths.Add(name);
					}
				}

				DateTime creationAfter;
				DateTime writeAfter;
				long sizeAfter;
				if (!FileStat.TryGetFileStat(scanPath, out creationAfter, out writeAfter, out sizeAfter)
					|| sizeAfter != size || writeAfter.ToUniversalTime().Ticks != mtimeTicks)
					return false;

				if (VarPackageMgr.singleton == null
					|| !VarPackageMgr.singleton.TrySetMorphFileEntryNames(Uid, size, mtimeTicks, paths))
					return false;
				MorphFileEntryNames = paths;
				return true;
			}
			catch (Exception ex)
			{
				LogScanErrorOnce(Uid, "Morph index scan", ex);
				return false;
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

		/// <summary>Top-level meta.json <c>licenseType</c> when present (CC BY, etc.).</summary>
		public string LicenseType
		{
			get;
			protected set;
		}

		/// <summary>Top-level meta.json <c>tags</c> (comma-separated), when present. Distinct from clothing/hair item tags.</summary>
		public List<string> PackageMetaTags;

		/// <summary>True after <see cref="TryEnsureMetaJsonLiteFields"/> ran (success or miss).</summary>
		private bool _metaJsonLiteLoaded;
		public List<string> PackageDependencies
		{
			get;
			protected set;
		}

		// Whether all missing dependencies have been checked
		public bool MissingDependenciesChecked = false;
		public bool Scaned = false;
		public List<string> RecursivePackageDependencies;
		// Number of installed packages that list this package as a (transitive) dependency.
		// -1 = not computed; >=0 cached (FileManager.ResolveDependentCount / RebuildDependentCounts).
		public int DependentCount = -1;
		// Cached count of missing dependencies from the recursive dependency list
		public int MissingDepsCount = -1;

		public HashSet<string> GetDependenciesDeep(int maxDepth = 2)
		{
			return FileManager.GetDependenciesDeep(this.Uid, maxDepth);
		}

		public List<string> ClothingFileEntryNames;
		public List<string> ClothingTags;
		public List<string> HairFileEntryNames;
		public List<string> HairTags;
		public List<string> MorphFileEntryNames;
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
			PackageDependenciesMissing = new HashSet<string>();
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
			FileManagerBridge.Refresh("package_delete", RefreshScope.Both);
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
		void InvalidateScanState()
		{
			Scaned = false;
			invalid = false;
			IsCorruptedArchive = false;
			MissingDependenciesChecked = false;
			metaEntry = null;
			InternalCreationTimeBinary = long.MinValue;
			try
			{
				lock (m_ZipFileLock)
				{
					if (m_ZipFile != null)
					{
						m_ZipFile.Close();
						m_ZipFile = null;
					}
				}
			}
			catch { }
			lock (cachedEntriesLock)
			{
				fileEntries = null;
				cachedFileEntryNames = null;
				cachedFileEntryLastWriteTimeUtcTicks = null;
				cachedFileEntrySizes = null;
				cachedInternalPathToIndex = null;
			}
			lock (catalogContentKindsLock)
				cachedCatalogContentKinds = -1;
			ClothingFileEntries = null;
			HairFileEntries = null;
			ClothingFileEntryNames = null;
			ClothingTags = null;
			HairFileEntryNames = null;
			HairTags = null;
			MorphFileEntryNames = null;
			PackageMetaTags = null;
			Description = null;
			PromotionalLink = null;
			LicenseType = null;
			_metaJsonLiteLoaded = false;
			_referenceVersionOptionsLoaded = false;
			StandardReferenceVersionOption = ReferenceVersionOption.Latest;
			ScriptReferenceVersionOption = ReferenceVersionOption.Latest;
			RecursivePackageDependencies = null;
			PackageDependencies = null;
		}

		// Missing or corrupt meta is cached so UI hover does not reopen archive repeatedly.
		public bool TryEnsureMetaJsonLiteFields()
		{
			// Early-out on attempt flag only. Old gate also required _referenceVersionOptionsLoaded;
			// miss/fail left that false → every tip/hover reopened ZIP (main-thread stall).
			if (_metaJsonLiteLoaded)
			{
				return !string.IsNullOrEmpty(Description)
					|| !string.IsNullOrEmpty(PromotionalLink)
					|| !string.IsNullOrEmpty(LicenseType)
					|| (PackageMetaTags != null && PackageMetaTags.Count > 0)
					|| _referenceVersionOptionsLoaded;
			}

			_metaJsonLiteLoaded = true;
			try
			{
				if (string.IsNullOrEmpty(Path) || !File.Exists(Path))
					return false;

				int cp = GetZipNameCodePageForVar(Path);
				using (ZipFile zf = OpenZipFileForRead(Path, cp))
				{
					if (zf == null) return false;
					ZipEntry entry = zf.GetEntry("meta.json");
					if (entry == null || entry.IsDirectory) return false;
					using (Stream stream = zf.GetInputStream(entry))
					using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
					{
						string aJSON = reader.ReadToEnd();
						if (string.IsNullOrEmpty(aJSON)) return false;
						JSONNode root = JSON.Parse(aJSON);
						JSONClass asObject = root != null ? root.AsObject : null;
						if (asObject == null) return false;

						try
						{
							JSONNode descNode = asObject["description"];
							if (descNode != null)
							{
								string d = descNode.Value;
								if (!string.IsNullOrEmpty(d)) Description = d.Trim();
							}
						}
						catch { }

						try
						{
							JSONNode promoNode = asObject["promotionalLink"];
							if (promoNode != null)
							{
								string p = promoNode.Value;
								if (!string.IsNullOrEmpty(p)) PromotionalLink = p.Trim();
							}
						}
						catch { }

						try
						{
							JSONNode licenseNode = asObject["licenseType"];
							if (licenseNode != null)
							{
								string lic = licenseNode.Value;
								if (!string.IsNullOrEmpty(lic)
									&& !string.Equals(lic, "null", StringComparison.OrdinalIgnoreCase))
									LicenseType = lic.Trim();
							}
						}
						catch { }

						try
						{
							JSONNode tagsNode = asObject["tags"];
							if (tagsNode != null)
							{
								string raw = tagsNode.Value;
								if (!string.IsNullOrEmpty(raw))
								{
									var list = new List<string>();
									string[] parts = raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
									for (int i = 0; i < parts.Length; i++)
									{
										string t = parts[i] != null ? parts[i].Trim() : "";
										if (!string.IsNullOrEmpty(t) && !list.Contains(t))
											list.Add(t);
									}
									if (list.Count > 0) PackageMetaTags = list;
								}
							}
						}
						catch { }

						try { PackageReferenceVersionResolver.ApplyFromMetaJson(this, asObject); } catch { }
					}
				}
			}
			catch
			{
				return false;
			}
			finally
			{
				// Miss / corrupt / no tags still counts as done — hover must not retry ZIP.
				if (!_referenceVersionOptionsLoaded)
					_referenceVersionOptionsLoaded = true;
			}

			return !string.IsNullOrEmpty(Description)
				|| !string.IsNullOrEmpty(PromotionalLink)
				|| !string.IsNullOrEmpty(LicenseType)
				|| (PackageMetaTags != null && PackageMetaTags.Count > 0)
				|| _referenceVersionOptionsLoaded;
		}
		// Empty results persist as positive cache hits, while bulk scans defer SQLite writes to avoid writer contention.
		private void PersistCslistReferencedPathsToDb()
		{
			try
			{
				if (string.IsNullOrEmpty(Uid)) return;
				if (FileManager.IsBulkDeepScanActive)
					return;

				// Already persisted under this LastWriteTime: skip the zip-open and SQLite write.
				// An unchanged VAR costs only the in-memory sig lookup.
				string sig = "0";
				try { sig = LastWriteTime.ToBinary().ToString(); } catch { }
				string existingSig;
				if (VpbLocalDatabase.TryGetCslistRefVarSig(Uid, out existingSig)
					&& string.Equals(existingSig, sig, StringComparison.Ordinal))
				{
					return;
				}

				// Fast path populates cachedFileEntryNames; slow path populates FileEntries.
				// Snapshot whichever is available to avoid holding the lock during zip I/O.
				List<string> namesCopy = null;
				lock (cachedEntriesLock)
				{
					if (cachedFileEntryNames != null && cachedFileEntryNames.Count > 0)
					{
						namesCopy = new List<string>(cachedFileEntryNames.Count);
						for (int i = 0; i < cachedFileEntryNames.Count; i++)
							namesCopy.Add(cachedFileEntryNames[i]);
					}
					else if (fileEntries != null && fileEntries.Count > 0)
					{
						namesCopy = new List<string>(fileEntries.Count);
						for (int i = 0; i < fileEntries.Count; i++)
						{
							var fe = fileEntries[i];
							if (fe != null && !string.IsNullOrEmpty(fe.InternalPath))
								namesCopy.Add(fe.InternalPath);
						}
					}
				}

				var cslistNames = new List<string>();
				if (namesCopy != null)
				{
					for (int i = 0; i < namesCopy.Count; i++)
					{
						string n = namesCopy[i];
						if (string.IsNullOrEmpty(n)) continue;
						string norm = n.Replace('\\', '/');
						if (!norm.StartsWith("Custom/Scripts/", StringComparison.OrdinalIgnoreCase)) continue;
						if (!norm.EndsWith(".cslist", StringComparison.OrdinalIgnoreCase)) continue;
						cslistNames.Add(norm);
					}
				}

				// Tree rows fill lazily because scan-time deletes contend with manifest loading.
				if (cslistNames.Count == 0)
				{
					try { VpbLocalDatabase.StampCslistRefVarSigOnly(Uid, sig); } catch { }
					return;
				}

				var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				if (File.Exists(Path))
				{
					int codePage = _resolvedZipNameCodePage != int.MinValue ? _resolvedZipNameCodePage : CodePageUtf8;
					ZipFile zf = null;
					try
					{
						zf = OpenZipFileForRead(Path, codePage);
						for (int i = 0; i < cslistNames.Count; i++)
						{
							string cslistInternal = cslistNames[i];
							try
							{
								ZipEntry ze = FindZipEntry(zf, cslistInternal);
								if (ze == null) continue;

								string cslistDir = cslistInternal;
								int lastSlash = cslistDir.LastIndexOf('/');
								cslistDir = lastSlash > 0 ? cslistDir.Substring(0, lastSlash) : string.Empty;

								using (var s = zf.GetInputStream(ze))
								{
									var refs = VPB.src.util.CslistParser.ParseReferencedCsPaths(s, cslistDir);
									for (int ri = 0; ri < refs.Count; ri++)
									{
										string rel = refs[ri];
										if (string.IsNullOrEmpty(rel)) continue;
										string fullAddr = (Uid + ":/" + rel).ToLowerInvariant();
										referenced.Add(fullAddr);
									}
								}
							}
							catch { }
						}
					}
					finally
					{
						try { if (zf != null) zf.Close(); } catch { }
					}
				}

				var rows = new List<VpbLocalDatabase.SystemFileRow>(referenced.Count);
				foreach (var rp in referenced)
				{
					var r = new VpbLocalDatabase.SystemFileRow();
					r.Path = rp;
					r.LastWriteBinaryOrInvalid = long.MinValue;
					r.SizeOrInvalid = long.MinValue;
					rows.Add(r);
				}
				try { VpbLocalDatabase.WriteCslistReferencedForVar(Uid, sig, rows); } catch { }
			}
			catch { }
		}

		/// <summary>Post-bulk-scan ThreadPool: fill cslist-ref SQL without contending scan workers.</summary>
		internal void PersistCslistReferencedPathsToDbDeferred()
		{
			PersistCslistReferencedPathsToDb();
		}

		public void Scan()
		{
			if (Scaned)
			{
				try
				{
					DateTime creationTime;
					DateTime lastWriteTime;
					long size;
					if (FileStat.TryGetFileStat(Path, out creationTime, out lastWriteTime, out size))
					{
						long prevTicks = 0;
						try { prevTicks = LastWriteTime.ToUniversalTime().Ticks; } catch { }
						long newTicks = 0;
						try { newTicks = lastWriteTime.ToUniversalTime().Ticks; } catch { }
						if (size == Size && newTicks == prevTicks) return;
					}
				}
				catch { }
				InvalidateScanState();
			}

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
						DateTime creationTime;
						DateTime lastWriteTime;
						long size;
						if (FileStat.TryGetFileStat(Path, out creationTime, out lastWriteTime, out size))
						{
							LastWriteTime = lastWriteTime;
							CreationTime = creationTime;
							Size = size;
						}
						else
						{
							FileInfo fileInfo = new FileInfo(Path);
							LastWriteTime = fileInfo.LastWriteTime;
							CreationTime = fileInfo.CreationTime;
							Size = fileInfo.Length;
						}
						lastWriteUtcTicks = LastWriteTime.ToUniversalTime().Ticks;
						SerializableVarPackage vp = VarPackageMgr.singleton.TryGetCacheValidated(this.Uid, Size, lastWriteUtcTicks);
						if (vp != null && vp.FileEntryNames != null
							&& CachedNamesNeedSceneJsonRescan(vp.FileEntryNames, vp.ScanRuleStamp))
						{
							vp = null;
						}
						if (vp != null && vp.FileEntryNames != null)
						{
							if (vp.IsInvalid)
							{
								invalid = true;
								VpbPackageIndexDiagnostics.Log(this.Uid, "scanCacheInvalid", "reason=manifest_IsInvalid path='" + (Path ?? "") + "'");
								Scaned = true;
								return;
							}
							if (vp.ZipNameCodePage != 0)
								RememberZipNameCodePage(vp.ZipNameCodePage);
							// Fast path: keep cached lists and defer VarFileEntry object creation
							lock (cachedEntriesLock)
							{
								fileEntries = null;
								cachedFileEntryNames = vp.FileEntryNames;
								cachedFileEntryLastWriteTimeUtcTicks = vp.FileEntryLastWriteTimeUtcTicks;
								cachedFileEntrySizes = vp.FileEntrySizes;
								cachedInternalPathToIndex = null;
							}
							this.ClothingFileEntryNames = vp.ClothingFileEntryNames;
							this.ClothingTags = vp.ClothingTags;
							this.HairFileEntryNames = vp.HairFileEntryNames;
							this.HairTags = vp.HairTags;
							this.MorphFileEntryNames = vp.MorphFileEntryNames;
							this.RecursivePackageDependencies = vp.RecursivePackageDependencies;
							this.InternalCreationTimeBinary = NormalizeZipHeaderTimeBinary(vp.VarInternalCreationTimeBinary);
							flag = true;
							Interlocked.Increment(ref scanCacheHit);
							Interlocked.Increment(ref scanCacheValidatedHit);
							PersistCslistReferencedPathsToDb();
							Scaned = true;
							return;
						}

						if (!flag)
						{
							Interlocked.Increment(ref scanZip);
							int scanCodePage = GetZipNameCodePageForVar(Path);
							zipFile = OpenZipFileForRead(Path, scanCodePage);
							HashSet<string> set = new HashSet<string>();
							List<string> morphFileEntryNames = new List<string>();
							IEnumerator enumerator = zipFile.GetEnumerator();
							try
							{
								while (enumerator.MoveNext())
								{
									ZipEntry zipEntry = (ZipEntry)enumerator.Current;
									if (zipEntry.IsFile)
									{
										string entryName = zipEntry.Name;
										if (entryName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
										{
											if (string.Equals(zipEntry.Name, "meta.json", StringComparison.OrdinalIgnoreCase))
											{
												try { InternalCreationTimeBinary = NormalizeZipHeaderTimeBinary(zipEntry.DateTime.ToBinary()); } catch { InternalCreationTimeBinary = long.MinValue; }
												VarFileEntry varFileEntry = new VarFileEntry(this, zipEntry.Name, zipEntry.DateTime, zipEntry.Size);
												FileEntries.Add(varFileEntry);
												metaEntry = varFileEntry;
											}
											else
											{
												string jpgName = entryName.Substring(0, entryName.Length - 5) + ".jpg";
												ZipEntry jpgEntry = FindZipEntry(zipFile, jpgName);
												bool isSceneJson = IsZipGallerySceneJsonInternalPath(entryName);
												if (jpgEntry != null || isSceneJson)
												{
													FileEntries.Add(new VarFileEntry(this, zipEntry.Name, zipEntry.DateTime, zipEntry.Size));
													if (jpgEntry != null)
													{
														string jpgKey = NormalizeZipInternalPath(jpgEntry.Name);
														if (jpgKey == null) jpgKey = jpgName;
														if (!set.Contains(jpgKey) && !set.Contains(jpgName))
														{
															FileEntries.Add(new VarFileEntry(this, jpgEntry.Name, jpgEntry.DateTime, jpgEntry.Size));
															set.Add(jpgKey);
														}
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
													ZipEntry sisterZipEntry = FindZipEntry(zipFile, sisterPath);
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
												ZipEntry jpgEntry = FindZipEntry(zipFile, entry);
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
										else if (entryName.EndsWith(".vmi", StringComparison.OrdinalIgnoreCase))
										{
											string morphPath = entryName.Replace('\\', '/');
											if (morphPath.StartsWith("Custom/Atom/Person/Morphs/", StringComparison.OrdinalIgnoreCase))
												morphFileEntryNames.Add(morphPath);
										}
										else if (entryName.EndsWith(".assetbundle"))
										{
											VarFileEntry varFileEntry = new VarFileEntry(this, zipEntry.Name, zipEntry.DateTime, zipEntry.Size);
											FileEntries.Add(varFileEntry);
											// liu modification: add asset preview image
											string entry = entryName.Substring(0, entryName.Length - 12) + ".jpg";
											//SuperController.LogMessage("assetbundle:"+ entry);
											if (!set.Contains(entry))
											{
												ZipEntry jpgEntry = FindZipEntry(zipFile, entry);
												if (jpgEntry != null)
												{

													VarFileEntry varFileEntry2 = new VarFileEntry(this, jpgEntry.Name, jpgEntry.DateTime, jpgEntry.Size);
													FileEntries.Add(varFileEntry2);
													set.Add(entry);
												}
											}
										}
										// Session plugins in VARs.
										else if (IsPluginScriptZipEntry(entryName))
										{
											VarFileEntry varFileEntry = new VarFileEntry(this, entryName, zipEntry.DateTime, zipEntry.Size);
											FileEntries.Add(varFileEntry);
											string baseNoExt = entryName;
											int dotIdx = baseNoExt.LastIndexOf('.');
											if (dotIdx > 0) baseNoExt = baseNoExt.Substring(0, dotIdx);
											string[] scriptSisterExts = { ".jpg", ".png" };
											foreach (var sExt in scriptSisterExts)
											{
												string sisterPath = baseNoExt + sExt;
												if (set.Contains(sisterPath)) continue;
												ZipEntry sisterZe = FindZipEntry(zipFile, sisterPath);
												if (sisterZe != null)
												{
													FileEntries.Add(new VarFileEntry(this, sisterZe.Name, sisterZe.DateTime, sisterZe.Size));
													set.Add(sisterPath);
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
						MorphFileEntryNames = morphFileEntryNames;
					}

						if (metaEntry != null)
						{
							flag = true;
						}
						DumpVarPackage(zipFile);
						// Manifest lists are resident now; keeping every scanned archive open retains its central directory.
						zipFile.Close();
						zipFile = null;
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
							VpbPackageIndexDiagnostics.Log(this.Uid, "scanInvalid", "reason=zip_scan path='" + (Path ?? "") + "'");
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
					if (!File.Exists(Path))
					{
						// Registered from a stale cached path inventory (file moved to InvalidPackages or
						// deleted since the inventory was saved). Drop the row so it stops resurrecting.
						VpbLocalDatabase.NoteMissingVarPath(Path);
						VpbPackageIndexDiagnostics.Log(this.Uid, "scanInvalid", "reason=file_missing path='" + (Path ?? "") + "'");
					}
					else
						VpbPackageIndexDiagnostics.Log(this.Uid, "scanInvalid", "reason=no_meta_json path='" + (Path ?? "") + "'");
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
						string tag;
						if (tags.TryGetValue(item.InternalPath, out tag))
						{
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
									if (!TagFilter.AllClothingTags.Contains(t))
									{
										lock (TagFilter.ClothingUnknownTagsLock)
										{
											if (!TagFilter.ClothingUnknownTags.Contains(t))
											{
												TagFilter.ClothingUnknownTags.Add(t);
											}
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
						string tag;
						if (tags.TryGetValue(item.InternalPath, out tag))
						{
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
				PersistCslistReferencedPathsToDb();
				Scaned = true;
			}
		}
		static string ReadZipEntryText(ZipFile zipFile, string internalPath)
		{
			if (zipFile == null)
				throw new ArgumentNullException("zipFile");
			ZipEntry entry = zipFile.GetEntry(internalPath);
			if (entry == null)
				throw new Exception("Could not find entry " + internalPath + " in zip file");
			using (Stream stream = zipFile.GetInputStream(entry))
			using (StreamReader reader = new StreamReader(stream))
				return reader.ReadToEnd();
		}

		void DumpVarPackage(ZipFile zipFile)
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
						string aJSON = ReadZipEntryText(zipFile, item.InternalPath);
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
						string aJSON = ReadZipEntryText(zipFile, item.InternalPath);
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
					string aJSON = ReadZipEntryText(zipFile, metaEntry.InternalPath);
					JSONClass asObject = JSON.Parse(aJSON).AsObject;
					if (asObject != null)
					{
						HashSet<string> directDepends = new HashSet<string>();
						try
						{
							JSONClass depObj = asObject["dependencies"].AsObject;
							if (depObj != null)
							{
								foreach (string key in depObj.Keys)
								{
									if (!string.IsNullOrEmpty(key)) directDepends.Add(key);
								}
							}
						}
						catch { }
						try
						{
							DependencyExtractor.ScanAllStringsForDependencies(asObject, directDepends);
						}
						catch { }

						// Keep the existing recursive list for backwards compatibility and older caches.
						HashSet<string> depends = new HashSet<string>();
						GetDependenciesRecursive(asObject, depends);

						var directList = directDepends.ToList();
						try { this.PackageDependencies = directList; } catch { }
						svp.RecursivePackageDependencies = depends.ToList();

						try
						{
							var descNode = asObject["description"];
							if (descNode != null)
							{
								string d = descNode.Value;
								if (!string.IsNullOrEmpty(d)) Description = d.Trim();
							}
						}
						catch { }
                            try
                            {
                                var promoNode = asObject["promotionalLink"];
                                if (promoNode != null)
                                {
                                    string p = promoNode.Value;
                                    if (!string.IsNullOrEmpty(p)) PromotionalLink = p.Trim();
                                }
                            }
                            catch { }

                            try
                            {
                                JSONNode licenseNode = asObject["licenseType"];
                                if (licenseNode != null)
                                {
                                    string lic = licenseNode.Value;
                                    if (!string.IsNullOrEmpty(lic)
                                        && !string.Equals(lic, "null", StringComparison.OrdinalIgnoreCase))
                                        LicenseType = lic.Trim();
                                }
                            }
                            catch { }

                            try { PackageReferenceVersionResolver.ApplyFromMetaJson(this, asObject); } catch { }

                            // Full scan already read lite meta fields — skip ZIP reopen on filter/hover.
                            _metaJsonLiteLoaded = true;

						if (!FileManager.IsBulkDeepScanActive)
							FindMissingDependenciesRecursive(asObject);
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
			svp.VarInternalCreationTimeBinary = NormalizeZipHeaderTimeBinary(InternalCreationTimeBinary);
			if (clothingFileList != null && clothingFileList.Count > 0)
                svp.ClothingFileEntryNames = clothingFileList;//.ToArray();
			if (clothingTags != null && clothingTags.Count > 0)
                svp.ClothingTags = clothingTags;//.ToArray();
			if (hairFileList != null && hairFileList.Count > 0)
                svp.HairFileEntryNames = hairFileList;//.ToArray();
			if (hairTags != null && hairTags.Count > 0)
                svp.HairTags = hairTags;//.ToArray();
			svp.MorphFileEntryNames = MorphFileEntryNames != null
				? new List<string>(MorphFileEntryNames)
				: null;

			this.RecursivePackageDependencies = svp.RecursivePackageDependencies;
			this.ClothingFileEntryNames = svp.ClothingFileEntryNames;
			this.ClothingTags = svp.ClothingTags;
			this.HairFileEntryNames = svp.HairFileEntryNames;
			this.HairTags = svp.HairTags;
			this.MorphFileEntryNames = svp.MorphFileEntryNames;

			try
			{
				int cp = GetZipNameCodePageForVar(Path);
				svp.ZipNameCodePage = cp != 0 ? cp : CodePageSystemDefault;
			}
			catch { svp.ZipNameCodePage = CodePageSystemDefault; }
			svp.ScanRuleStamp = CurrentScanRuleStamp;

		if (VarPackageMgr.singleton != null)
			{
				VarPackageMgr.singleton.SetCache(this.Uid, svp);
			}
			lock (cachedEntriesLock)
			{
				cachedFileEntryNames = svp.FileEntryNames;
				cachedFileEntryLastWriteTimeUtcTicks = svp.FileEntryLastWriteTimeUtcTicks;
				cachedFileEntrySizes = svp.FileEntrySizes;
				cachedInternalPathToIndex = null;
			}
		}


		protected void FindMissingDependenciesRecursive(JSONClass jc)
		{
			if (FileManager.IsBulkDeepScanActive)
				return;
			PackageReferenceVersionResolver.BeginReferrerContext(Uid);
			try
			{
				FindMissingDependenciesRecursiveCore(jc);
			}
			finally
			{
				PackageReferenceVersionResolver.EndReferrerContext();
			}
		}

		void FindMissingDependenciesRecursiveCore(JSONClass jc)
		{
			// First, try the explicit "dependencies" key (for well-formed packages)
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
						FindMissingDependenciesRecursiveCore(asObject2);
					}
				}
			}

			// Also scan all strings for dependencies (catches local/custom references)
			HashSet<string> scannedDeps = new HashSet<string>();
			DependencyExtractor.ScanAllStringsForDependencies(jc, scannedDeps);
			foreach (string dep in scannedDeps)
			{
				if (!PackageDependenciesMissing.Contains(dep))
				{
					VarPackage package = FileManager.GetPackageForDependency(dep, false);
					if (package == null)
					{
						HasMissingDependencies = true;
						PackageDependenciesMissing.Add(dep);
					}
				}
			}
		}

		void GetDependenciesRecursive(JSONClass jc, HashSet<string> depends)
		{
			// First, collect from explicit "dependencies" key (for well-formed packages)
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

			// Also scan all strings for dependencies (catches local/custom references and embedded deps)
			DependencyExtractor.ScanAllStringsForDependencies(jc, depends);
		}
		public bool InstallRecursive()
        {
			if (Settings.Instance != null && Settings.Instance.LoadDependenciesWithPackage != null && !Settings.Instance.LoadDependenciesWithPackage.Value)
			{
				return InstallSelf();
			}
            return InstallRecursive(new HashSet<string>(), null);
        }

		public bool InstallRecursive(List<string> outMovedPackageUids)
		{
			if (Settings.Instance != null && Settings.Instance.LoadDependenciesWithPackage != null && !Settings.Instance.LoadDependenciesWithPackage.Value)
			{
				bool moved = InstallSelf();
				if (moved && outMovedPackageUids != null && !string.IsNullOrEmpty(Uid))
					outMovedPackageUids.Add(Uid);
				return moved;
			}
			return InstallRecursive(new HashSet<string>(), outMovedPackageUids);
		}

		private bool InstallRecursive(HashSet<string> visited, List<string> outMovedPackageUids)
		{
            if (visited.Contains(this.Uid)) return false;
            visited.Add(this.Uid);

			bool flag = false;
			bool dirty= InstallSelf();
			if (dirty)
			{
				flag = true;
				if (outMovedPackageUids != null && !string.IsNullOrEmpty(Uid))
					outMovedPackageUids.Add(Uid);
			}
			
			//string linkvar = "AddonPackages/" + this.Uid + ".var";
            if (this.RecursivePackageDependencies != null)
            {
				PackageReferenceVersionResolver.BeginReferrerContext(Uid);
				try
				{
					foreach (var key in this.RecursivePackageDependencies)
					{
						VarPackage package = FileManager.GetPackageForDependency(key, false);
						if (package != null)
						{
							bool dirty2= package.InstallRecursive(visited, outMovedPackageUids);
							if (dirty2) flag = true;
						}
					}
				}
				finally
				{
					PackageReferenceVersionResolver.EndReferrerContext();
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

			// Final rename stays on destination volume so VaM never observes partially copied archive.
			string dir = System.IO.Path.GetDirectoryName(linkvar);
			try
			{
				if (!Directory.Exists(dir))
					Directory.CreateDirectory(dir);
			}
			catch (Exception ex)
			{
				LogUtil.LogError($"InstallSelf: Failed to create directory {dir}: {ex.Message}");
				return false;
			}

			string sourcePath = this.Path;
			string tempTarget = linkvar + ".installing";
			try
			{
				if (File.Exists(tempTarget)) File.Delete(tempTarget);
				File.Copy(sourcePath, tempTarget, false);
				File.Move(tempTarget, linkvar);
				File.Delete(sourcePath);
			}
			catch (Exception ex)
			{
				LogUtil.LogError($"InstallSelf: Failed to install {Uid} from {sourcePath} to {linkvar}: {ex.Message}");
				try { if (File.Exists(tempTarget)) File.Delete(tempTarget); } catch { }
				return false;
			}
			finally
			{
				try { if (File.Exists(tempTarget)) File.Delete(tempTarget); } catch { }
			}
			this.Path = linkvar.Replace('\\', '/');
			if (this.Path.StartsWith("AddonPackages/"))
				RelativePath = this.Path.Substring("AddonPackages/".Length);
			// Close stale ZipFile opened from the old AllPackages path so it reopens cleanly from AddonPackages
			lock (m_ZipFileLock)
			{
				if (m_ZipFile != null) { m_ZipFile.Close(); m_ZipFile = null; }
			}
			FileInfo info = new FileInfo(linkvar);
			if (info != null)
				info.Refresh();
			return true;
		}

		public void CloseZipFile()
		{
			lock (m_ZipFileLock)
			{
				if (m_ZipFile != null) { m_ZipFile.Close(); m_ZipFile = null; }
			}
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
