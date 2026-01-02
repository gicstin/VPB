using System.IO;
namespace VPB
{
	public class SystemFileEntryStreamReader : FileEntryStreamReader
	{
		public SystemFileEntryStreamReader(SystemFileEntry entry)
			: base(entry)
		{
			StreamReader = new StreamReader(entry.Path);
		}
	}
}
