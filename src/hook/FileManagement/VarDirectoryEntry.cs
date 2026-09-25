using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace VPB
{
	public class VarDirectoryEntry : DirectoryEntry
	{
		protected HashSet<VarDirectoryEntry> varSubDirectories;

		protected List<VarFileEntry> varFileEntries;

		public VarPackage Package { get; protected set; }

		public string InternalPath { get; protected set; }

		public override List<FileEntry> Files
		{
			get
			{
				List<FileEntry> list = new List<FileEntry>();
				foreach (VarFileEntry varFileEntry in varFileEntries)
				{
					list.Add(varFileEntry);
				}
				return list;
			}
			protected set
			{
				throw new NotImplementedException();
			}
		}

		public override List<DirectoryEntry> SubDirectories
		{
			get
			{
				List<DirectoryEntry> list = new List<DirectoryEntry>();
				foreach (VarDirectoryEntry varSubDirectory in varSubDirectories)
				{
					list.Add(varSubDirectory);
				}
				return list;
			}
			protected set
			{
				throw new NotImplementedException();
			}
		}

		public List<VarDirectoryEntry> VarSubDirectories
		{
			get
			{
				return varSubDirectories.ToList();
			}
			protected set
			{
				throw new NotImplementedException();
			}
		}

		public VarDirectoryEntry(VarPackage vp, string entryName, VarDirectoryEntry parent = null)
		{
			Package = vp;
			bool flag = false;
			if (entryName == string.Empty)
			{
				flag = true;
				Name = vp.Uid + ".var:";
			}
			InternalPath = entryName;
			if (flag)
			{
				Uid = vp.Uid + ":";
				Path = vp.Path + ":";
			}
			else
			{
				Uid = vp.Uid + ":/" + InternalPath;
				Path = vp.Path + ":/" + InternalPath;
			}
			Name = VamPathFastPaths.StripThroughLastSlash(Path);
			UidLowerInvariant = Uid.ToLowerInvariant();
			LastWriteTime = vp.LastWriteTime;
			Parent = parent;
			varSubDirectories = new HashSet<VarDirectoryEntry>();
			varFileEntries = new List<VarFileEntry>();
			if (FileManager.debug)
			{
			}
		}

		public void AddSubDirectory(VarDirectoryEntry subDir)
		{
			varSubDirectories.Add(subDir);
		}

		public void AddFileEntry(VarFileEntry varFileEntry)
		{
			varFileEntries.Add(varFileEntry);
		}
	}
}
