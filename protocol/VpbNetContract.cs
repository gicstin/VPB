using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetContractLimits
    {
        public const int MaxContractBytes = 16384;
        public const int MaxDependencies = 160;
        public const int MaxUidChars = 96;
        public const int MaxSceneUidChars = VpbNetEventLimits.MaxIdentifier;
        public const int MaxIssues = 64;
    }

    public static class VpbNetContractRole
    {
        public const byte Scene = 1 << 0;
        public const byte Look = 1 << 1;
        public const byte Mask = Scene | Look;

        public static string Name(byte role)
        {
            role &= Mask;
            if (role == (Scene | Look)) return "scene+look";
            if (role == Scene) return "scene";
            if (role == Look) return "look";
            return "unknown";
        }
    }

    public enum VpbNetContractReject
    {
        None = 0,
        BadVersion = 1,
        Truncated = 2,
        BadCount = 3,
        BadIdentifier = 4,
        PluginReference = 5,
        Oversize = 6,
        BadRole = 7,
        Duplicate = 8
    }

    public enum VpbNetContractIssueKind
    {
        None = 0,
        MissingPackage = 1,
        VersionDrift = 2,
        ContentDrift = 3,
        ListTruncated = 4
    }

    public enum VpbNetContractVerdict
    {
        Match = 0,
        Approximated = 1,
        Incomplete = 2
    }

    public static class VpbNetContractUid
    {
        public static bool TrySplit(string uid, out string family, out string version)
        {
            family = uid;
            version = string.Empty;
            if (string.IsNullOrEmpty(uid)) return false;

            int dot = uid.LastIndexOf('.');
            if (dot <= 0 || dot == uid.Length - 1) return false;

            string tail = uid.Substring(dot + 1);
            if (!IsVersionToken(tail)) return false;

            family = uid.Substring(0, dot);
            version = tail;
            return true;
        }

        public static bool IsLatest(string version)
        {
            return string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsVersionToken(string s)
        {
            if (s.Length == 0) return false;
            if (IsLatest(s)) return true;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
            }
            return true;
        }
    }

    public sealed class VpbNetContract
    {
        public const byte ProtoVersion = 1;
        public const int HeaderSize = 8;

        public const byte FlagHasScene = 1 << 0;
        public const byte FlagTruncated = 1 << 1;

        readonly string[] _uid = new string[VpbNetContractLimits.MaxDependencies];
        readonly uint[] _hash = new uint[VpbNetContractLimits.MaxDependencies];
        readonly byte[] _role = new byte[VpbNetContractLimits.MaxDependencies];

        int _count;
        int _bytes;
        bool _truncated;
        int _omitted;
        string _sceneUid = string.Empty;
        uint _sceneHash;

        public int Count { get { return _count; } }
        public bool Truncated { get { return _truncated; } }
        public int Omitted { get { return _omitted; } }
        public string SceneUid { get { return _sceneUid; } }
        public uint SceneHash { get { return _sceneHash; } }
        public bool HasScene { get { return _sceneUid.Length > 0; } }
        public int WireBytes { get { return _bytes; } }

        public string Uid(int i) { return i >= 0 && i < _count ? _uid[i] : null; }
        public uint Hash(int i) { return i >= 0 && i < _count ? _hash[i] : 0u; }
        public byte Role(int i) { return i >= 0 && i < _count ? _role[i] : (byte)0; }

        public VpbNetContract()
        {
            Clear();
        }

        public void Clear()
        {
            _count = 0;
            _truncated = false;
            _omitted = 0;
            _sceneUid = string.Empty;
            _sceneHash = 0;
            _bytes = HeaderSize + 1;
        }

        public bool SetScene(string sceneUid, uint sceneHash)
        {
            if (sceneUid == null) sceneUid = string.Empty;
            if (sceneUid.Length > VpbNetContractLimits.MaxSceneUidChars) return false;
            if (sceneUid.Length > 0
                && (!VpbNetEventCodec.IsSafeIdentifier(sceneUid) || VpbNetEventCodec.IsPluginReference(sceneUid)))
                return false;

            int n = Utf8Bytes(sceneUid);
            if (n < 0 || n > 255) return false;
            if (_count > 0) return false;

            _sceneUid = sceneUid;
            _sceneHash = sceneHash;
            _bytes = HeaderSize + 1 + n;
            return true;
        }

        public bool TryAdd(string uid, uint hash, byte role)
        {
            role &= VpbNetContractRole.Mask;
            if (role == 0) return false;
            if (uid == null || uid.Length == 0 || uid.Length > VpbNetContractLimits.MaxUidChars) return false;
            if (!VpbNetEventCodec.IsSafeIdentifier(uid) || VpbNetEventCodec.IsPluginReference(uid)) return false;

            for (int i = 0; i < _count; i++)
            {
                if (!string.Equals(_uid[i], uid, StringComparison.OrdinalIgnoreCase)) continue;
                _role[i] |= role;
                if (_hash[i] == 0) _hash[i] = hash;
                return true;
            }

            int merged = MergeLatest(uid, hash, role);
            if (merged >= 0) return true;

            int n = Utf8Bytes(uid);
            if (n < 0) return false;

            int cost = 1 + n + 5;
            if (n > 255
                || _count >= VpbNetContractLimits.MaxDependencies
                || _bytes + cost > VpbNetContractLimits.MaxContractBytes)
            {
                _truncated = true;
                _omitted++;
                return false;
            }

            _uid[_count] = uid;
            _hash[_count] = hash;
            _role[_count] = role;
            _count++;
            _bytes += cost;
            return true;
        }

        int MergeLatest(string uid, uint hash, byte role)
        {
            string family, version;
            if (!VpbNetContractUid.TrySplit(uid, out family, out version)) return -1;

            for (int i = 0; i < _count; i++)
            {
                string f, v;
                if (!VpbNetContractUid.TrySplit(_uid[i], out f, out v)) continue;
                if (!string.Equals(f, family, StringComparison.OrdinalIgnoreCase)) continue;

                bool mineLatest = VpbNetContractUid.IsLatest(version);
                bool theirsLatest = VpbNetContractUid.IsLatest(v);
                if (mineLatest == theirsLatest) continue;

                _role[i] |= role;
                if (theirsLatest)
                {
                    int delta = Utf8Bytes(uid) - Utf8Bytes(_uid[i]);
                    _uid[i] = uid;
                    _bytes += delta;
                    _hash[i] = hash;
                }
                else if (_hash[i] == 0)
                {
                    _hash[i] = hash;
                }
                return i;
            }
            return -1;
        }

        public int Write(byte[] dst)
        {
            if (dst == null || dst.Length < HeaderSize) return -1;

            byte flags = 0;
            if (HasScene) flags |= FlagHasScene;
            if (_truncated) flags |= FlagTruncated;

            dst[0] = ProtoVersion;
            dst[1] = flags;
            VpbIpc.WriteU16(dst, 2, _count);
            VpbIpc.WriteU32(dst, 4, _sceneHash);

            int o = HeaderSize;
            o = WriteString(dst, o, _sceneUid);
            if (o < 0) return -1;

            for (int i = 0; i < _count; i++)
            {
                o = WriteString(dst, o, _uid[i]);
                if (o < 0 || o + 5 > dst.Length) return -1;
                VpbIpc.WriteU32(dst, o, _hash[i]);
                dst[o + 4] = _role[i];
                o += 5;
            }

            return o > VpbNetContractLimits.MaxContractBytes ? -1 : o;
        }

        public VpbNetContractReject Read(byte[] src, int offset, int len)
        {
            if (src == null || offset < 0 || len < HeaderSize || offset + len > src.Length)
                return VpbNetContractReject.Truncated;
            if (len > VpbNetContractLimits.MaxContractBytes) return VpbNetContractReject.Oversize;
            if (src[offset] != ProtoVersion) return VpbNetContractReject.BadVersion;

            byte flags = src[offset + 1];
            int count = VpbIpc.ReadU16(src, offset + 2);
            uint sceneHash = VpbIpc.ReadU32(src, offset + 4);
            if (count > VpbNetContractLimits.MaxDependencies) return VpbNetContractReject.BadCount;

            int end = offset + len;
            int o = offset + HeaderSize;

            string sceneUid;
            o = ReadString(src, o, end, out sceneUid);
            if (o < 0) return VpbNetContractReject.Truncated;

            bool hasScene = (flags & FlagHasScene) != 0;
            if (hasScene != (sceneUid.Length > 0)) return VpbNetContractReject.BadIdentifier;
            if (sceneUid.Length > VpbNetContractLimits.MaxSceneUidChars) return VpbNetContractReject.Oversize;
            if (sceneUid.Length > 0)
            {
                if (!VpbNetEventCodec.IsSafeIdentifier(sceneUid)) return VpbNetContractReject.BadIdentifier;
                if (VpbNetEventCodec.IsPluginReference(sceneUid)) return VpbNetContractReject.PluginReference;
            }

            string[] uid = new string[count];
            uint[] hash = new uint[count];
            byte[] role = new byte[count];

            for (int i = 0; i < count; i++)
            {
                o = ReadString(src, o, end, out uid[i]);
                if (o < 0) return VpbNetContractReject.Truncated;
                if (uid[i].Length == 0 || uid[i].Length > VpbNetContractLimits.MaxUidChars)
                    return VpbNetContractReject.Oversize;
                if (!VpbNetEventCodec.IsSafeIdentifier(uid[i])) return VpbNetContractReject.BadIdentifier;
                if (VpbNetEventCodec.IsPluginReference(uid[i])) return VpbNetContractReject.PluginReference;

                if (o + 5 > end) return VpbNetContractReject.Truncated;
                hash[i] = VpbIpc.ReadU32(src, o);
                role[i] = (byte)(src[o + 4] & VpbNetContractRole.Mask);
                o += 5;
                if (role[i] == 0) return VpbNetContractReject.BadRole;

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(uid[j], uid[i], StringComparison.OrdinalIgnoreCase))
                        return VpbNetContractReject.Duplicate;
                }
            }

            Clear();
            _sceneUid = sceneUid;
            _sceneHash = sceneHash;
            for (int i = 0; i < count; i++)
            {
                _uid[i] = uid[i];
                _hash[i] = hash[i];
                _role[i] = role[i];
            }
            _count = count;
            _truncated = (flags & FlagTruncated) != 0;
            _bytes = o - offset;
            return VpbNetContractReject.None;
        }

        public static string Explain(VpbNetContractReject r)
        {
            switch (r)
            {
                case VpbNetContractReject.None: return "ok";
                case VpbNetContractReject.BadVersion: return "that peer speaks a different content-contract format. Update whichever VPB is older.";
                case VpbNetContractReject.Truncated: return "the peer's content list arrived incomplete";
                case VpbNetContractReject.BadCount: return "the peer listed more packages than the contract allows";
                case VpbNetContractReject.BadIdentifier: return "the peer sent a package name this build refuses to handle";
                case VpbNetContractReject.PluginReference: return "the peer listed a plugin file as content, which is never accepted";
                case VpbNetContractReject.Oversize: return "the peer's content list is larger than the contract allows";
                case VpbNetContractReject.BadRole: return "the peer listed a package without saying what needs it";
                case VpbNetContractReject.Duplicate: return "the peer listed the same package twice";
            }
            return "the peer's content list could not be read";
        }

        static int Utf8Bytes(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            try { return Encoding.UTF8.GetByteCount(s); }
            catch { return -1; }
        }

        static int WriteString(byte[] dst, int o, string s)
        {
            if (s == null) s = string.Empty;
            int n = Utf8Bytes(s);
            if (n < 0 || n > 255 || o + 1 + n > dst.Length) return -1;
            dst[o] = (byte)n;
            o++;
            if (n > 0)
            {
                try { Encoding.UTF8.GetBytes(s, 0, s.Length, dst, o); }
                catch { return -1; }
                o += n;
            }
            return o;
        }

        static int ReadString(byte[] src, int o, int end, out string s)
        {
            s = string.Empty;
            if (o + 1 > end) return -1;
            int n = src[o];
            o++;
            if (o + n > end) return -1;
            try { s = Encoding.UTF8.GetString(src, o, n); }
            catch { return -1; }
            return o + n;
        }
    }

    public interface IVpbNetContractCatalog
    {
        bool TryResolveExact(string uid, out uint contentHash);
        bool TryResolveFamily(string family, out string installedUid);
    }

    public sealed class VpbNetContractReport
    {
        readonly VpbNetContractIssueKind[] _kind = new VpbNetContractIssueKind[VpbNetContractLimits.MaxIssues];
        readonly string[] _uid = new string[VpbNetContractLimits.MaxIssues];
        readonly string[] _local = new string[VpbNetContractLimits.MaxIssues];
        readonly byte[] _role = new byte[VpbNetContractLimits.MaxIssues];

        int _count;
        int _overflow;
        int _checked;
        int _critical;
        VpbNetContractVerdict _verdict;

        public int IssueCount { get { return _count; } }
        public int OverflowedIssues { get { return _overflow; } }
        public int Checked { get { return _checked; } }
        public int CriticalCount { get { return _critical; } }
        public VpbNetContractVerdict Verdict { get { return _verdict; } }

        public VpbNetContractIssueKind Kind(int i) { return i >= 0 && i < _count ? _kind[i] : VpbNetContractIssueKind.None; }
        public string Uid(int i) { return i >= 0 && i < _count ? _uid[i] : null; }
        public string LocalUid(int i) { return i >= 0 && i < _count ? _local[i] : null; }
        public byte Role(int i) { return i >= 0 && i < _count ? _role[i] : (byte)0; }

        public void Clear()
        {
            _count = 0;
            _overflow = 0;
            _checked = 0;
            _critical = 0;
            _verdict = VpbNetContractVerdict.Match;
            for (int i = 0; i < _uid.Length; i++)
            {
                _uid[i] = null;
                _local[i] = null;
            }
        }

        public void NoteChecked(int n)
        {
            _checked = n;
        }

        public void Add(VpbNetContractIssueKind kind, string uid, string localUid, byte role)
        {
            VpbNetContractVerdict v = SeverityOf(kind, role);
            if (v > _verdict) _verdict = v;
            if (v == VpbNetContractVerdict.Incomplete) _critical++;

            if (_count >= _kind.Length)
            {
                _overflow++;
                return;
            }

            _kind[_count] = kind;
            _uid[_count] = uid;
            _local[_count] = localUid;
            _role[_count] = role;
            _count++;
        }

        public static VpbNetContractVerdict SeverityOf(VpbNetContractIssueKind kind, byte role)
        {
            switch (kind)
            {
                case VpbNetContractIssueKind.MissingPackage:
                    return (role & VpbNetContractRole.Scene) != 0
                        ? VpbNetContractVerdict.Incomplete
                        : VpbNetContractVerdict.Approximated;
                case VpbNetContractIssueKind.VersionDrift:
                case VpbNetContractIssueKind.ContentDrift:
                case VpbNetContractIssueKind.ListTruncated:
                    return VpbNetContractVerdict.Approximated;
            }
            return VpbNetContractVerdict.Match;
        }

        public string Describe(int i)
        {
            if (i < 0 || i >= _count) return string.Empty;
            switch (_kind[i])
            {
                case VpbNetContractIssueKind.MissingPackage:
                    return _uid[i] + " is not installed (needed by " + VpbNetContractRole.Name(_role[i]) + ")";
                case VpbNetContractIssueKind.VersionDrift:
                    return _uid[i] + " is installed as " + _local[i] + " - that avatar will be approximated";
                case VpbNetContractIssueKind.ContentDrift:
                    return _uid[i] + " is installed but its contents differ - that avatar will be approximated";
                case VpbNetContractIssueKind.ListTruncated:
                    return "the peer had more packages than the contract can carry; "
                        + _checked + " were checked and the rest are unknown";
            }
            return string.Empty;
        }

        public string Summary()
        {
            switch (_verdict)
            {
                case VpbNetContractVerdict.Match:
                    return "content matches (" + _checked + " packages checked)";
                case VpbNetContractVerdict.Approximated:
                    return _count + " content difference" + (_count == 1 ? "" : "s")
                        + "; joining anyway, affected avatars are approximated";
                case VpbNetContractVerdict.Incomplete:
                    return _critical + " package" + (_critical == 1 ? " the scene needs is" : "s the scene needs are")
                        + " missing; joining anyway, but that content will not appear";
            }
            return string.Empty;
        }
    }

    public static class VpbNetContractCheck
    {
        public static VpbNetContractVerdict Compare(VpbNetContract remote, IVpbNetContractCatalog catalog, VpbNetContractReport report)
        {
            if (report == null) return VpbNetContractVerdict.Match;
            report.Clear();
            if (remote == null) return VpbNetContractVerdict.Match;

            if (catalog == null)
            {
                report.NoteChecked(0);
                report.Add(VpbNetContractIssueKind.ListTruncated, string.Empty, null, VpbNetContractRole.Look);
                return report.Verdict;
            }

            int n = remote.Count;
            report.NoteChecked(n);

            for (int i = 0; i < n; i++)
            {
                string uid = remote.Uid(i);
                byte role = remote.Role(i);
                uint theirHash = remote.Hash(i);

                uint localHash;
                if (catalog.TryResolveExact(uid, out localHash))
                {
                    if (theirHash != 0 && localHash != 0 && theirHash != localHash)
                        report.Add(VpbNetContractIssueKind.ContentDrift, uid, uid, role);
                    continue;
                }

                string family, version;
                bool split = VpbNetContractUid.TrySplit(uid, out family, out version);

                string installed;
                if (split && catalog.TryResolveFamily(family, out installed))
                {
                    if (VpbNetContractUid.IsLatest(version)) continue;
                    report.Add(VpbNetContractIssueKind.VersionDrift, uid, installed, role);
                    continue;
                }

                report.Add(VpbNetContractIssueKind.MissingPackage, uid, null, role);
            }

            if (remote.Truncated)
                report.Add(VpbNetContractIssueKind.ListTruncated, string.Empty, null, VpbNetContractRole.Look);

            return report.Verdict;
        }
    }
}
