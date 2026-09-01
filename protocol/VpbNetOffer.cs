using System;
using System.Text;

namespace VpbNet
{
    // Published both sides so a stalled fetch is not mistaken for a dead peer.
    public static class VpbNetContentPhase
    {
        public const byte Unknown = 0;
        public const byte Checking = 1;
        public const byte Waiting = 2;
        public const byte Fetching = 3;
        public const byte Installing = 4;
        public const byte Ready = 5;
        public const byte Degraded = 6;
        public const byte Failed = 7;
        public const byte Refused = 8;
        public const byte Loading = 9;

        public const int Count = 10;

        public static bool IsKnown(byte p)
        {
            return p < Count;
        }

        // Handover waits for Settled, never Ready — missing paid packages never reach Ready.
        public static bool IsSettled(byte p)
        {
            return p == Ready || p == Degraded || p == Failed || p == Refused;
        }

        public static bool CanLoad(byte p)
        {
            return p == Ready || p == Degraded;
        }

        // Excludes Loading — Stop cannot undo a scene load.
        public static bool IsWorking(byte p)
        {
            return p == Checking || p == Fetching || p == Installing;
        }

        public static string Name(byte p)
        {
            switch (p)
            {
                case Checking: return "checking";
                case Waiting: return "waiting for an answer";
                case Fetching: return "downloading";
                case Installing: return "installing";
                case Ready: return "has the content";
                case Loading: return "loading the scene";
                case Degraded: return "ready without some content";
                case Failed: return "could not get the content";
                case Refused: return "declined";
            }
            return "unknown";
        }
    }

    // Code not sentence — peer-chosen text would render in the other player's panel.
    public static class VpbNetContentFail
    {
        public const byte None = 0;
        public const byte NotOnHub = 1;
        public const byte HubOffline = 2;
        public const byte NeedsLogin = 3;
        public const byte TooBig = 4;
        public const byte SaveFailed = 5;
        public const byte Cancelled = 6;
        public const byte NoScene = 7;
        public const byte Blocked = 8;
        public const byte Timeout = 9;
        public const byte HubDisabled = 10;

        public static string Describe(byte f)
        {
            switch (f)
            {
                case NotOnHub:
                    return "some of it is not on the Hub - paid, delisted, or made by the host";
                case HubOffline:
                    return "the Hub did not answer";
                case NeedsLogin:
                    return "the Hub wants you signed in for that download";
                case TooBig:
                    return "it is over the download limit set in settings";
                case SaveFailed:
                    return "a package downloaded but could not be written to disk";
                case Cancelled:
                    return "cancelled";
                case NoScene:
                    return "that scene is not in a package, so there is nothing to fetch";
                case Blocked:
                    return "your session rules do not let them fetch content here";
                case Timeout:
                    return "it took too long";
                case HubDisabled:
                    return "the Hub is switched off in VaM";
            }
            return string.Empty;
        }
    }

    public static class VpbNetOfferLimits
    {
        public const int MaxScenePath = VpbNetEventLimits.MaxEntryPath;
        public const int MaxPackageUid = VpbNetContractLimits.MaxUidChars;
        public const int MaxTitle = 48;
        public const int MaxCurrent = VpbNetContractLimits.MaxUidChars;
    }

    // Name only, not a file — receiver resolves against its own library and Hub.
    public struct VpbNetOfferInfo
    {
        public const byte FlagFromPackage = 1 << 0;
        public const byte FlagEditMode = 1 << 1;
        public const byte FlagHostForced = 1 << 2;

        public uint OfferId;
        public byte Flags;
        public string ScenePath;
        public string PackageUid;
        public uint PackageHash;
        public string Title;
        public int ManifestGen;
        public int ManifestCount;
        public uint TotalKiB;

        public bool FromPackage { get { return (Flags & FlagFromPackage) != 0; } }
        public bool EditMode { get { return (Flags & FlagEditMode) != 0; } }

        public bool IsPresent
        {
            get { return OfferId != 0 && !string.IsNullOrEmpty(ScenePath); }
        }

        public void Clear()
        {
            OfferId = 0;
            Flags = 0;
            ScenePath = string.Empty;
            PackageUid = string.Empty;
            PackageHash = 0;
            Title = string.Empty;
            ManifestGen = 0;
            ManifestCount = 0;
            TotalKiB = 0;
        }

        public void Write(VpbNetEventWriter w)
        {
            if (w == null) return;
            w.WriteU32(OfferId);
            w.WriteByte(Flags);
            w.WriteString(ScenePath ?? string.Empty, VpbNetOfferLimits.MaxScenePath);
            w.WriteString(PackageUid ?? string.Empty, VpbNetOfferLimits.MaxPackageUid);
            w.WriteU32(PackageHash);
            w.WriteString(Title ?? string.Empty, VpbNetOfferLimits.MaxTitle);
            w.WriteU16(ManifestGen);
            w.WriteU16(ManifestCount);
            w.WriteU32(TotalKiB);
        }

        public static bool TryRead(VpbNetEventReader r, out VpbNetOfferInfo o)
        {
            o = new VpbNetOfferInfo();
            o.Clear();
            if (r == null) return false;

            uint id = r.ReadU32();
            byte flags = r.ReadByte();
            string path = r.ReadIdentifier(VpbNetOfferLimits.MaxScenePath);
            if (r.Failed || path == null) return false;

            string pkg = r.ReadText(VpbNetOfferLimits.MaxPackageUid);
            if (r.Failed || pkg == null) return false;
            if (pkg.Length > 0 && !VpbNetEventCodec.IsSafeIdentifier(pkg, VpbNetOfferLimits.MaxPackageUid))
                return false;

            uint hash = r.ReadU32();
            string title = r.ReadText(VpbNetOfferLimits.MaxTitle);
            int gen = r.ReadU16();
            int count = r.ReadU16();
            uint kib = r.ReadU32();
            if (r.Failed || title == null) return false;
            if (id == 0) return false;
            if (count > VpbNetManifestLimits.MaxEntries) return false;

            o.OfferId = id;
            o.Flags = flags;
            o.ScenePath = path;
            o.PackageUid = pkg;
            o.PackageHash = hash;
            o.Title = title;
            o.ManifestGen = gen;
            o.ManifestCount = count;
            o.TotalKiB = kib;
            return true;
        }
    }

    public struct VpbNetContentStatus
    {
        public uint OfferId;
        public byte Phase;
        public byte Fail;
        public int Have;
        public int Need;
        public uint DoneKiB;
        public uint TotalKiB;
        public string Current;

        public void Clear()
        {
            OfferId = 0;
            Phase = VpbNetContentPhase.Unknown;
            Fail = VpbNetContentFail.None;
            Have = 0;
            Need = 0;
            DoneKiB = 0;
            TotalKiB = 0;
            Current = string.Empty;
        }

        public bool SameAs(VpbNetContentStatus o)
        {
            return OfferId == o.OfferId
                && Phase == o.Phase
                && Fail == o.Fail
                && Have == o.Have
                && Need == o.Need
                && DoneKiB == o.DoneKiB
                && TotalKiB == o.TotalKiB
                && string.Equals(Current ?? string.Empty, o.Current ?? string.Empty, StringComparison.Ordinal);
        }

        public float Fraction01
        {
            get
            {
                if (VpbNetContentPhase.CanLoad(Phase)
                    || Phase == VpbNetContentPhase.Loading) return 1f;
                if (TotalKiB > 0)
                {
                    float f = (float)((double)DoneKiB / TotalKiB);
                    return f < 0f ? 0f : (f > 1f ? 1f : f);
                }
                if (Need > 0)
                {
                    float f = (float)Have / Need;
                    return f < 0f ? 0f : (f > 1f ? 1f : f);
                }
                return 0f;
            }
        }

        public void Write(VpbNetEventWriter w)
        {
            if (w == null) return;
            w.WriteU32(OfferId);
            w.WriteByte(Phase);
            w.WriteByte(Fail);
            w.WriteU16(Have);
            w.WriteU16(Need);
            w.WriteU32(DoneKiB);
            w.WriteU32(TotalKiB);
            w.WriteString(Current ?? string.Empty, VpbNetOfferLimits.MaxCurrent);
        }

        public static bool TryRead(VpbNetEventReader r, out VpbNetContentStatus s)
        {
            s = new VpbNetContentStatus();
            s.Clear();
            if (r == null) return false;

            uint id = r.ReadU32();
            byte phase = r.ReadByte();
            byte fail = r.ReadByte();
            int have = r.ReadU16();
            int need = r.ReadU16();
            uint done = r.ReadU32();
            uint total = r.ReadU32();
            string cur = r.ReadText(VpbNetOfferLimits.MaxCurrent);
            if (r.Failed || cur == null) return false;

            s.OfferId = id;
            // Unknown phase is not settled — nothing loads on its say-so.
            s.Phase = VpbNetContentPhase.IsKnown(phase) ? phase : VpbNetContentPhase.Unknown;
            s.Fail = fail;
            s.Have = have;
            s.Need = need;
            s.DoneKiB = done;
            s.TotalKiB = total;
            s.Current = cur;
            return true;
        }

        public void Describe(StringBuilder sb)
        {
            if (sb == null) return;
            switch (Phase)
            {
                case VpbNetContentPhase.Checking:
                    sb.Append("Checking what is missing");
                    return;

                case VpbNetContentPhase.Waiting:
                    sb.Append("Waiting to answer");
                    if (Need > 0)
                    {
                        sb.Append(" - ");
                        sb.Append(Need.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        sb.Append(Need == 1 ? " package" : " packages");
                        AppendOfSize(sb, TotalKiB);
                    }
                    return;

                case VpbNetContentPhase.Fetching:
                    sb.Append("Downloading ");
                    sb.Append((Have + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append('/');
                    sb.Append(Need.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (TotalKiB > 0)
                    {
                        sb.Append("  ");
                        AppendSize(sb, DoneKiB);
                        sb.Append(" of ");
                        AppendSize(sb, TotalKiB);
                    }
                    return;

                case VpbNetContentPhase.Installing:
                    sb.Append("Installing ");
                    sb.Append(Have.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append(Have == 1 ? " package" : " packages");
                    return;

                case VpbNetContentPhase.Loading:
                    sb.Append("Loading the scene");
                    return;

                // Ready means library, not in-scene — panel rewrites once both report the same scene.
                case VpbNetContentPhase.Ready:
                    sb.Append("Content ready");
                    return;

                case VpbNetContentPhase.Degraded:
                    sb.Append("Content ready without ");
                    int short_ = Need - Have;
                    if (short_ < 1) short_ = 1;
                    sb.Append(short_.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append(short_ == 1 ? " package" : " packages");
                    return;

                case VpbNetContentPhase.Failed:
                    sb.Append("Stopped");
                    AppendReason(sb, Fail);
                    return;

                case VpbNetContentPhase.Refused:
                    sb.Append("Declined");
                    AppendReason(sb, Fail);
                    return;
            }
            sb.Append("No answer yet");
        }

        static void AppendReason(StringBuilder sb, byte fail)
        {
            string why = VpbNetContentFail.Describe(fail);
            if (why.Length == 0) return;
            sb.Append(" - ");
            sb.Append(why);
        }

        static void AppendOfSize(StringBuilder sb, uint kib)
        {
            if (kib == 0) return;
            sb.Append(", ");
            AppendSize(sb, kib);
        }

        public static void AppendSize(StringBuilder sb, uint kib)
        {
            if (sb == null) return;
            if (kib < 1024u)
            {
                sb.Append(kib.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(" KB");
                return;
            }
            if (kib < 1024u * 1024u)
            {
                AppendOneDecimal(sb, kib / 1024.0);
                sb.Append(" MB");
                return;
            }
            AppendOneDecimal(sb, kib / (1024.0 * 1024.0));
            sb.Append(" GB");
        }

        static void AppendOneDecimal(StringBuilder sb, double v)
        {
            if (v < 0.0) v = 0.0;
            int whole = (int)v;
            int tenth = (int)((v - whole) * 10.0 + 0.5);
            if (tenth >= 10) { whole++; tenth = 0; }
            sb.Append(whole.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (whole < 100 && tenth > 0)
            {
                sb.Append('.');
                sb.Append(tenth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    public static class VpbNetManifestLimits
    {
        // Cap is fragment assembler, not taste — oversized manifest never arrives.
        public const int MaxBytes = 24000;
        public const int MaxEntries = 600;
        public const int MaxUidChars = VpbNetContractLimits.MaxUidChars;
    }

    // Advisory only — receiver resolves real closure; truncate costs a bad estimate.
    public sealed class VpbNetManifest
    {
        public const byte ProtoVersion = 1;
        public const int HeaderSize = 12;
        public const byte FlagTruncated = 1 << 0;

        readonly string[] _uid = new string[VpbNetManifestLimits.MaxEntries];
        readonly byte[] _role = new byte[VpbNetManifestLimits.MaxEntries];

        int _count;
        int _bytes;
        bool _truncated;
        int _omitted;
        int _gen;
        uint _totalKiB;

        public int Count { get { return _count; } }
        public bool Truncated { get { return _truncated; } }
        public int Omitted { get { return _omitted; } }
        public int Generation { get { return _gen; } }
        public uint TotalKiB { get { return _totalKiB; } }
        public int WireBytes { get { return _bytes; } }

        public string Uid(int i) { return i >= 0 && i < _count ? _uid[i] : null; }
        public byte Role(int i) { return i >= 0 && i < _count ? _role[i] : (byte)0; }

        public VpbNetManifest()
        {
            Clear();
        }

        public void Clear()
        {
            _count = 0;
            _bytes = HeaderSize;
            _truncated = false;
            _omitted = 0;
            _gen = 0;
            _totalKiB = 0;
        }

        public void SetGeneration(int gen)
        {
            _gen = gen & 0xFFFF;
        }

        public void AddKiB(uint kib)
        {
            uint next = _totalKiB + kib;
            _totalKiB = next < _totalKiB ? uint.MaxValue : next;
        }

        public bool TryAdd(string uid, byte role)
        {
            role &= VpbNetContractRole.Mask;
            if (role == 0) return false;
            if (uid == null || uid.Length == 0 || uid.Length > VpbNetManifestLimits.MaxUidChars) return false;
            if (!VpbNetEventCodec.IsSafeIdentifier(uid) || VpbNetEventCodec.IsPluginReference(uid)) return false;

            for (int i = 0; i < _count; i++)
            {
                if (!string.Equals(_uid[i], uid, StringComparison.OrdinalIgnoreCase)) continue;
                _role[i] |= role;
                return true;
            }

            int n = Utf8Bytes(uid);
            if (n < 0 || n > 255) return false;

            int cost = 1 + n + 1;
            if (_count >= VpbNetManifestLimits.MaxEntries
                || _bytes + cost > VpbNetManifestLimits.MaxBytes)
            {
                _truncated = true;
                _omitted++;
                return false;
            }

            _uid[_count] = uid;
            _role[_count] = role;
            _count++;
            _bytes += cost;
            return true;
        }

        public int Write(byte[] dst)
        {
            if (dst == null || dst.Length < HeaderSize) return -1;

            dst[0] = ProtoVersion;
            dst[1] = _truncated ? FlagTruncated : (byte)0;
            VpbIpc.WriteU16(dst, 2, _count);
            VpbIpc.WriteU16(dst, 4, _gen);
            VpbIpc.WriteU32(dst, 6, _totalKiB);
            VpbIpc.WriteU16(dst, 10, _omitted > 0xFFFF ? 0xFFFF : _omitted);

            int o = HeaderSize;
            for (int i = 0; i < _count; i++)
            {
                string s = _uid[i];
                int n = Utf8Bytes(s);
                if (n < 0 || n > 255 || o + 1 + n + 1 > dst.Length) return -1;
                dst[o] = (byte)n;
                o++;
                try { Encoding.UTF8.GetBytes(s, 0, s.Length, dst, o); }
                catch { return -1; }
                o += n;
                dst[o] = _role[i];
                o++;
            }

            return o > VpbNetManifestLimits.MaxBytes ? -1 : o;
        }

        public VpbNetContractReject Read(byte[] src, int offset, int len)
        {
            if (src == null || offset < 0 || len < HeaderSize || offset + len > src.Length)
                return VpbNetContractReject.Truncated;
            if (len > VpbNetManifestLimits.MaxBytes) return VpbNetContractReject.Oversize;
            if (src[offset] != ProtoVersion) return VpbNetContractReject.BadVersion;

            byte flags = src[offset + 1];
            int count = VpbIpc.ReadU16(src, offset + 2);
            int gen = VpbIpc.ReadU16(src, offset + 4);
            uint kib = VpbIpc.ReadU32(src, offset + 6);
            int omitted = VpbIpc.ReadU16(src, offset + 10);
            if (count > VpbNetManifestLimits.MaxEntries) return VpbNetContractReject.BadCount;

            int end = offset + len;
            int o = offset + HeaderSize;

            string[] uid = new string[count];
            byte[] role = new byte[count];

            for (int i = 0; i < count; i++)
            {
                if (o + 1 > end) return VpbNetContractReject.Truncated;
                int n = src[o];
                o++;
                if (o + n + 1 > end) return VpbNetContractReject.Truncated;
                try { uid[i] = Encoding.UTF8.GetString(src, o, n); }
                catch { return VpbNetContractReject.BadIdentifier; }
                o += n;

                if (uid[i].Length == 0 || uid[i].Length > VpbNetManifestLimits.MaxUidChars)
                    return VpbNetContractReject.Oversize;
                if (!VpbNetEventCodec.IsSafeIdentifier(uid[i])) return VpbNetContractReject.BadIdentifier;
                if (VpbNetEventCodec.IsPluginReference(uid[i])) return VpbNetContractReject.PluginReference;

                role[i] = (byte)(src[o] & VpbNetContractRole.Mask);
                o++;
                if (role[i] == 0) return VpbNetContractReject.BadRole;

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(uid[j], uid[i], StringComparison.OrdinalIgnoreCase))
                        return VpbNetContractReject.Duplicate;
                }
            }

            Clear();
            for (int i = 0; i < count; i++)
            {
                _uid[i] = uid[i];
                _role[i] = role[i];
            }
            _count = count;
            _gen = gen;
            _totalKiB = kib;
            _omitted = omitted;
            _truncated = (flags & FlagTruncated) != 0;
            _bytes = o - offset;
            return VpbNetContractReject.None;
        }

        static int Utf8Bytes(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            try { return Encoding.UTF8.GetByteCount(s); }
            catch { return -1; }
        }
    }

    // Scene package must match exactly; deps may settle on same family.
    public sealed class VpbNetContentPlan
    {
        readonly string[] _wanted = new string[VpbNetManifestLimits.MaxEntries];
        readonly byte[] _role = new byte[VpbNetManifestLimits.MaxEntries];

        int _count;
        int _present;
        int _drift;
        int _checked;

        public int Count { get { return _count; } }
        public int Present { get { return _present; } }
        public int Drifted { get { return _drift; } }
        public int Checked { get { return _checked; } }
        public bool NeedsNothing { get { return _count == 0; } }

        public string Wanted(int i) { return i >= 0 && i < _count ? _wanted[i] : null; }
        public byte Role(int i) { return i >= 0 && i < _count ? _role[i] : (byte)0; }

        public void Clear()
        {
            for (int i = 0; i < _count; i++) _wanted[i] = null;
            _count = 0;
            _present = 0;
            _drift = 0;
            _checked = 0;
        }

        public void Build(VpbNetManifest m, IVpbNetContractCatalog catalog)
        {
            Clear();
            if (m == null) return;

            int n = m.Count;
            _checked = n;

            for (int i = 0; i < n; i++)
            {
                string uid = m.Uid(i);
                byte role = m.Role(i);
                if (string.IsNullOrEmpty(uid)) continue;

                if (catalog == null)
                {
                    Add(uid, role);
                    continue;
                }

                uint localHash;
                if (catalog.TryResolveExact(uid, out localHash))
                {
                    _present++;
                    continue;
                }

                if ((role & VpbNetContractRole.Scene) != 0)
                {
                    Add(uid, role);
                    continue;
                }

                string family, version;
                string installed;
                if (VpbNetContractUid.TrySplit(uid, out family, out version)
                    && catalog.TryResolveFamily(family, out installed))
                {
                    _present++;
                    _drift++;
                    continue;
                }

                Add(uid, role);
            }
        }

        public void AddSeed(string uid, byte role)
        {
            Add(uid, role);
        }

        void Add(string uid, byte role)
        {
            if (_count >= _wanted.Length) return;
            for (int i = 0; i < _count; i++)
            {
                if (!string.Equals(_wanted[i], uid, StringComparison.OrdinalIgnoreCase)) continue;
                _role[i] |= role;
                return;
            }
            _wanted[_count] = uid;
            _role[_count] = (byte)(role == 0 ? VpbNetContractRole.Look : role);
            _count++;
        }
    }
}
