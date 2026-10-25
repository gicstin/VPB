using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.Checksums;
using ICSharpCode.SharpZipLib.Zip;
using SimpleJSON;

namespace VPB
{
    internal static class VpbVacImport
    {
        private static readonly object CacheLock = new object();
        private const long MaxExpandedBytes = 16L * 1024 * 1024 * 1024;
        private const int MaxEntries = 100000;

        internal static string Prepare(string archivePath, string cacheRoot, out List<string> packages)
        {
            lock (CacheLock)
            {
                FileInfo archive = new FileInfo(archivePath);
                string signature = archive.FullName + "|" + archive.Length + "|" + archive.LastWriteTimeUtc.Ticks;
                string key;
                using (SHA256 hash = SHA256.Create())
                    key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(signature))).Replace("-", "");
                cacheRoot = Path.GetFullPath(cacheRoot);
                RejectReparsePoints(cacheRoot);
                Directory.CreateDirectory(cacheRoot);
                string destination = Path.Combine(cacheRoot, key);
                string staging = Path.Combine(cacheRoot, key + "." + Guid.NewGuid().ToString("N"));
                packages = new List<string>();
                using (ZipFile zip = VarPackage.OpenZipFileForRead(archivePath, VarPackage.DetectZipNameCodePage(archivePath)))
                {
                    var files = new Dictionary<string, ZipEntry>(StringComparer.OrdinalIgnoreCase);
                    long total = 0;
                    int entries = 0;
                    foreach (ZipEntry entry in zip)
                    {
                        if (++entries > MaxEntries) throw new InvalidDataException("VAC contains too many entries");
                        string name = ValidateEntryName(entry.Name.TrimEnd('/', '\\'));
                        if (!entry.IsFile) continue;
                        if (entry.IsCrypted || entry.Size < 0 || entry.Size > MaxExpandedBytes - total)
                            throw new InvalidDataException("VAC is encrypted or exceeds the 16 GiB extraction limit");
                        total += entry.Size;
                        if (files.ContainsKey(name)) throw new InvalidDataException("VAC has duplicate paths: " + name);
                        files.Add(name, entry);
                        if (name.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                        {
                            int version;
                            if (!ForceLatestDependencyPolicy.TryParseVersionOfUid(Path.GetFileNameWithoutExtension(name), out version))
                                throw new InvalidDataException("VAC contains an invalid package name: " + name);
                            packages.Add(Path.Combine(destination, name));
                        }
                    }
                    string document = SelectDocument(files, Path.GetFileNameWithoutExtension(archivePath));
                    if (files[document].Size > 256L * 1024 * 1024)
                        throw new InvalidDataException("VAC scene or preset exceeds the 256 MiB JSON limit");
                    if (!Directory.Exists(destination))
                    {
                        Directory.CreateDirectory(staging);
                        try
                        {
                            byte[] buffer = new byte[81920];
                            foreach (KeyValuePair<string, ZipEntry> file in files)
                            {
                                string output = Path.Combine(staging, file.Key);
                                Directory.CreateDirectory(Path.GetDirectoryName(output));
                                using (Stream input = zip.GetInputStream(file.Value))
                                using (FileStream writer = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                                {
                                    long written = 0;
                                    var crc = new Crc32();
                                    int count;
                                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        written += count;
                                        if (written > file.Value.Size) throw new InvalidDataException("VAC entry exceeds its declared size");
                                        writer.Write(buffer, 0, count);
                                        crc.Update(buffer, 0, count);
                                    }
                                    if (written != file.Value.Size || crc.Value != file.Value.Crc)
                                        throw new InvalidDataException("VAC entry is incomplete or corrupt: " + file.Key);
                                }
                            }
                            Directory.Move(staging, destination);
                        }
                        finally
                        {
                            if (Directory.Exists(staging))
                            {
                                RejectReparsePoints(staging);
                                Directory.Delete(staging, true);
                            }
                        }
                    }
                    foreach (KeyValuePair<string, ZipEntry> file in files)
                    {
                        string path = Path.Combine(destination, file.Key);
                        RejectReparsePoints(path);
                        if (!File.Exists(path) || new FileInfo(path).Length != file.Value.Size)
                            throw new InvalidDataException("VAC import cache is incomplete: " + path);
                    }
                    return Path.Combine(destination, document);
                }
            }
        }

        internal static string ValidateEntryName(string name)
        {
            string normalized = (name ?? "").Replace('\\', '/');
            if (normalized.Length == 0 || Path.IsPathRooted(normalized))
                throw new InvalidDataException("VAC contains an absolute or empty path");
            foreach (string segment in normalized.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".."
                    || segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal)
                    || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.IndexOf(':') >= 0)
                    throw new InvalidDataException("VAC contains an unsafe path: " + name);
                string stem = segment.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL"
                    || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal)
                        || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '0' && stem[3] <= '9'))
                    throw new InvalidDataException("VAC contains a reserved filename: " + name);
            }
            return normalized;
        }

        private static void RejectReparsePoints(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((Directory.Exists(current) || File.Exists(current))
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Import path contains a filesystem link: " + current);
        }

        private static string SelectDocument(Dictionary<string, ZipEntry> files, string archiveName)
        {
            var candidates = new List<string>();
            foreach (string name in files.Keys)
            {
                if (name.IndexOf('/') >= 0 || string.Equals(name, "meta.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".vap", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(name);
            }
            foreach (string name in candidates)
                if (string.Equals(Path.GetFileNameWithoutExtension(name), archiveName, StringComparison.OrdinalIgnoreCase))
                    return name;
            if (candidates.Count == 1) return candidates[0];
            throw new InvalidDataException(candidates.Count == 0
                ? "VAC contains no root scene or preset JSON"
                : "VAC contains several scenes or presets with no matching archive name");
        }

        internal static void ResolveAssetPaths(JSONNode node, string sourceDirectory)
        {
            JSONClass obj = node as JSONClass;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JSONNode> pair in obj)
                {
                    if (pair.Key == "displayName" || pair.Key == "name" || pair.Key == "label"
                        || pair.Key == "type" || pair.Key == "character") continue;
                    if (pair.Key == "id" && pair.Value is JSONData
                        && pair.Value.Value.IndexOf('/') < 0 && pair.Value.Value.IndexOf('\\') < 0
                        && !pair.Value.Value.EndsWith(".vam", StringComparison.OrdinalIgnoreCase)) continue;
                    ResolveAssetPaths(pair.Value, sourceDirectory);
                }
                return;
            }
            JSONArray array = node as JSONArray;
            if (array != null)
            {
                foreach (JSONNode child in array) ResolveAssetPaths(child, sourceDirectory);
                return;
            }
            if (node == null || string.IsNullOrEmpty(node.Value)) return;
            string value = node.Value.Replace('\\', '/');
            if (value.StartsWith("./", StringComparison.Ordinal)) value = value.Substring(2);
            if (value.IndexOf(':') >= 0 || Path.IsPathRooted(value)) return;
            try
            {
                string path = Path.GetFullPath(Path.Combine(sourceDirectory, value));
                if (LocalSceneGallerySupport.IsStrictFilePathInsideDirectory(path, sourceDirectory) && File.Exists(path))
                    node.Value = path.Replace('\\', '/');
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (PathTooLongException) { }
        }

        internal static void InstallEmbeddedPackages(List<string> packages)
        {
            if (packages == null || packages.Count == 0) return;
            string directory = Path.GetFullPath("AddonPackages");
            RejectReparsePoints(directory);
            Directory.CreateDirectory(directory);
            foreach (string packagePath in packages)
            {
                string uid = Path.GetFileNameWithoutExtension(packagePath);
                if (FileManager.GetPackage(uid, false) != null) continue;
                string destination = Path.Combine(directory, Path.GetFileName(packagePath));
                RejectReparsePoints(destination);
                if (!File.Exists(destination))
                {
                    string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.Copy(packagePath, staging, false);
                        File.Move(staging, destination);
                    }
                    finally { if (File.Exists(staging)) File.Delete(staging); }
                }
                if (FileManager.RegisterHubDownloadedPackage(destination) == null)
                    throw new IOException("Cannot register VAC package: " + uid);
                LogUtil.Log("[VPB import] Registered bundled package " + uid);
            }
        }
    }
}
