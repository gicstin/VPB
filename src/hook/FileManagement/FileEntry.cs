using System;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace VPB
{
	public abstract class FileEntry
	{
		public virtual string Uid { get; protected set; }

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
			//protected set;
		}
		public virtual string Path { get; protected set; }
		public virtual string Name { get; protected set; }
		public virtual bool Exists { get; protected set; }
		public virtual DateTime LastWriteTime { get; protected set; }
		public virtual long Size { get; protected set; }

		public FileEntry()
		{
		}

		public FileEntry(string path)
		{
			if (path == null)
			{
				throw new Exception("Null path in FileEntry constructor");
			}
			Path = path.Replace('\\', '/'); //path.Replace('/', '\\');
			Uid = Path;
			Name = Regex.Replace(Path, ".*/", string.Empty);
		}

		public override string ToString()
		{
			return Path;
		}

		public abstract FileEntryStream OpenStream();

		public abstract FileEntryStreamReader OpenStreamReader();

		public virtual bool HasFlagFile(string flagName)
		{
			return false;
		}

		public virtual void SetFlagFile(string flagName, bool b)
		{
		}

		public virtual bool IsInstalled()
		{
			return false;
		}

		public virtual bool IsAutoInstall()
        {
			return false;
        }
        public virtual bool SetAutoInstall(bool b)
        {
			return false;
        }

        public virtual bool IsHidden()
		{
			return false;
		}

		public virtual void SetHidden(bool b)
		{
		}

		protected static HashSet<string> s_AutoInstallLookup;
		public static HashSet<string> AutoInstallLookup
		{
            get
            {
				if (s_AutoInstallLookup == null)
				{
					s_AutoInstallLookup = new HashSet<string>();
					if (File.Exists(GlobalInfo.AutoInstallPath))
					{
						string txt = File.ReadAllText(GlobalInfo.AutoInstallPath);
						var favorites = JsonUtility.FromJson<SerializableNames>(txt);
						if (favorites != null && favorites.Names != null)
						{
							foreach (var item in favorites.Names)
							{
								s_AutoInstallLookup.Add(item);
							}
						}
					}
				}

				return s_AutoInstallLookup;
			}
        }

		public void SetAutoInstallInternal(string key,bool b)
		{
			//string key = this.Package.Uid;
			if (b)
			{
				AutoInstallLookup.Add(key);
			}
			else
			{
				AutoInstallLookup.Remove(key);
			}

			if (!Directory.Exists(GlobalInfo.PluginInfoDirectory))
			{
				Directory.CreateDirectory(GlobalInfo.PluginInfoDirectory);
			}

			SerializableNames sf = new SerializableNames();
			var list = new List<string>();
			foreach (var item in AutoInstallLookup)
			{
				list.Add(item);
			}
			sf.Names = list.ToArray();
			File.WriteAllText(GlobalInfo.AutoInstallPath, JsonUtility.ToJson(sf));
		}
	}

}
