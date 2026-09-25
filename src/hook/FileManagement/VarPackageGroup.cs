using System;
using System.Linq;
using System.Collections.Generic;
using SimpleJSON;

namespace VPB
{
	public class VarPackageGroup
	{
		protected List<int> _versions;

		protected List<int> _enabledVersions;

		public string Name { get; protected set; }

		public List<int> Versions
		{
			get
			{
				List<int> list = _versions.ToList();
				list.Sort();
				return list;
			}
		}

		public int NewestVersion
		{
			get
			{
				if (NewestPackage != null)
				{
					return NewestPackage.Version;
				}
				return 0;
			}
		}

		public int NewestEnabledVersion
		{
			get
			{
				if (NewestPackage != null)
				{
					return NewestEnabledPackage.Version;
				}
				return 0;
			}
		}

		public List<VarPackage> Packages { get; protected set; }

		public VarPackage NewestPackage { get; protected set; }

		public VarPackage NewestEnabledPackage { get; protected set; }

		public string UserNotes
		{
			get
			{
				return null;
			}
		}

		public VarPackageGroup(string name)
		{
			Name = name;
			_versions = new List<int>();
			_enabledVersions = new List<int>();
			Packages = new List<VarPackage>();
		}

		public VarPackage GetClosestMatchingPackageVersion(int requestVersion, bool onlyUseEnabledPackages = true, bool returnLatestOnMissing = true)
		{
			int num = -1;
			List<int> list = ((!onlyUseEnabledPackages) ? _versions : _enabledVersions);
			foreach (int item in list)
			{
				if (requestVersion <= item)
				{
					num = item;
					break;
				}
			}
			if (num == -1)
			{
				if (returnLatestOnMissing)
				{
					if (onlyUseEnabledPackages)
					{
						return NewestEnabledPackage;
					}
					return NewestPackage;
				}
			}
			else
			{
				foreach (VarPackage package in Packages)
				{
					if (package.Version == num)
					{
						return package;
					}
				}
			}
			return null;
		}

		protected void SyncNewestVersion()
		{
			NewestPackage = null;
			NewestEnabledPackage = null;
			if (_versions.Count <= 0)
			{
				return;
			}
			int num = _versions[_versions.Count - 1];
			foreach (VarPackage package in Packages)
			{
				if (package.Version == num)
				{
					package.isNewestVersion = true;
					NewestPackage = package;
				}
				else
				{
					package.isNewestVersion = false;
				}
				if (package.Enabled)
				{
					if (_enabledVersions.Count <= 0)
					{
						package.isNewestEnabledVersion = false;
					}
					else
					{
						int num2 = _enabledVersions[_enabledVersions.Count - 1];
						if (package.Version == num2)
						{
							package.isNewestEnabledVersion = true;
							NewestEnabledPackage = package;
						}
						else
						{
							package.isNewestEnabledVersion = false;
						}
					}
				}
				else
				{
					package.isNewestEnabledVersion = false;
				}
			}
		}

		public bool GetCustomOption(string optionName)
		{
			bool value = false;
			return value;
		}

		public void SetCustomOption(string optionName, bool optionValue)
		{
		}

		protected void LoadUserPrefs()
		{
		}

		protected void SaveUserPrefs()
		{
		}

		public void AddPackage(VarPackage vp)
		{
			Packages.Add(vp);
			if (_versions.Contains(vp.Version))
			{
				throw new Exception("Tried to add package to group " + Name + " with version " + vp.Version + " that was already added");
			}
			_versions.Add(vp.Version);
			_versions.Sort();
			if (vp.Enabled)
			{
				_enabledVersions.Add(vp.Version);
				_enabledVersions.Sort();
			}
			SyncNewestVersion();
		}

		public void RemovePackage(VarPackage vp)
		{
			Packages.Remove(vp);
			_versions.Remove(vp.Version);
			_versions.Sort();
			if (vp.Enabled)
			{
				_enabledVersions.Remove(vp.Version);
				_enabledVersions.Sort();
			}
			SyncNewestVersion();
		}

		public void Init()
		{
		}
	}
}
