using System.IO;


namespace VPB
{
	public class SystemFileEntryStream : FileEntryStream
	{
		public SystemFileEntryStream(SystemFileEntry entry)
			: base(entry)
		{
			base.Stream = File.Open(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
		}
	}
}
