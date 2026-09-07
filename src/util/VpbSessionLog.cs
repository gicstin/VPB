using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace VPB.src.util
{
    internal sealed class VpbSessionLog : IDisposable
    {
        private static readonly Regex OwnedName = new Regex(@"^VPB-\d{8}T\d{9}Z-[0-9a-f]{32}\.log$", RegexOptions.CultureInvariant);
        private readonly object gate = new object();
        private StreamWriter writer;
        private Timer timer;
        private string error;
        internal string FilePath { get; private set; }

        internal VpbSessionLog(string directory, string sessionName)
        {
            if (!OwnedName.IsMatch(sessionName)) throw new ArgumentException("Invalid VPB session filename", "sessionName");
            FilePath = Path.Combine(directory, sessionName);
            try
            {
                Directory.CreateDirectory(directory);
                writer = new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                writer.WriteLine("LOG_SESSION utc=" + DateTime.UtcNow.ToString("o") + " id=" + sessionName);
                writer.Flush();
                try { Prune(directory); }
                catch (IOException ex) { error = "Session retention: " + ex.Message; }
                catch (UnauthorizedAccessException ex) { error = "Session retention: " + ex.Message; }
                timer = new Timer(delegate { Flush(); }, null, 2000, 2000);
            }
            catch (IOException ex) { Disable(ex); }
            catch (UnauthorizedAccessException ex) { Disable(ex); }
        }

        internal static string NewSessionName()
        {
            return "VPB-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N") + ".log";
        }

        private void Prune(string directory)
        {
            var files = new List<string>();
            foreach (string path in Directory.GetFiles(directory, "VPB-*.log"))
                if (OwnedName.IsMatch(Path.GetFileName(path)) && path != FilePath) files.Add(path);
            files.Sort(StringComparer.Ordinal);
            // Active instances deny delete sharing. Never replace their logs to meet retention count.
            for (int i = 0; i < files.Count - 4; i++)
            {
                try { File.Delete(files[i]); }
                catch (IOException ex) { error = "Session retention: " + ex.Message; }
                catch (UnauthorizedAccessException ex) { error = "Session retention: " + ex.Message; }
            }
        }

        internal void Write(string line, bool flush)
        {
            lock (gate)
            {
                if (writer == null) return;
                try
                {
                    writer.WriteLine(line);
                    if (flush) writer.Flush();
                }
                catch (IOException ex) { Disable(ex); }
                catch (UnauthorizedAccessException ex) { Disable(ex); }
            }
        }

        internal void Flush()
        {
            lock (gate)
            {
                if (writer == null) return;
                try { writer.Flush(); }
                catch (IOException ex) { Disable(ex); }
                catch (UnauthorizedAccessException ex) { Disable(ex); }
            }
        }

        internal string TakeError()
        {
            lock (gate)
            {
                string result = error;
                error = null;
                return result;
            }
        }

        private void Disable(Exception ex)
        {
            error = "VPB session log unavailable: " + ex.Message;
            CloseWriter();
        }

        private void CloseWriter()
        {
            var previous = writer;
            writer = null;
            if (previous == null) return;
            try { previous.Dispose(); }
            catch (IOException ex) { error = "VPB session log close failed: " + ex.Message; }
            catch (UnauthorizedAccessException ex) { error = "VPB session log close failed: " + ex.Message; }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (timer != null) timer.Dispose();
                timer = null;
                CloseWriter();
            }
        }
    }
}
