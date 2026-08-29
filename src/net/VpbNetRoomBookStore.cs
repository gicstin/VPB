using System;
using System.Text;
using VpbNet;

namespace VPB
{
    // Plugin persist for host inventory + join recents. Cold load, mutate on user actions.
    public static class VpbNetRoomBookStore
    {
        static readonly VpbNetRoomBookState _state = new VpbNetRoomBookState();
        static readonly StringBuilder _labelSb = new StringBuilder(48);
        static readonly string[] _hostLabels = new string[VpbNetRoomBook.Cap];
        static readonly string[] _joinLabels = new string[VpbNetRoomBook.Cap];
        static bool _loaded;
        static int _revision;

        public static int Revision
        {
            get
            {
                EnsureLoaded();
                return _revision;
            }
        }

        public static int Snapshot()
        {
            EnsureLoaded();
            return VpbNetRoomBook.Snapshot(_state) ^ _revision;
        }

        public static int HostCount
        {
            get
            {
                EnsureLoaded();
                return _state.Hosts.Count;
            }
        }

        public static int JoinCount
        {
            get
            {
                EnsureLoaded();
                return _state.Joins.Count;
            }
        }

        public static VpbNetRoomBookEntry HostAt(int i)
        {
            EnsureLoaded();
            if (i < 0 || i >= _state.Hosts.Count) return null;
            return _state.Hosts[i];
        }

        public static VpbNetRoomBookEntry JoinAt(int i)
        {
            EnsureLoaded();
            if (i < 0 || i >= _state.Joins.Count) return null;
            return _state.Joins[i];
        }

        public static string HostLabel(int i)
        {
            EnsureLoaded();
            if (i < 0 || i >= _state.Hosts.Count) return string.Empty;
            return _hostLabels[i] ?? string.Empty;
        }

        public static string JoinLabel(int i)
        {
            EnsureLoaded();
            if (i < 0 || i >= _state.Joins.Count) return string.Empty;
            return _joinLabels[i] ?? string.Empty;
        }

        public static string JoinToken(int i)
        {
            VpbNetRoomBookEntry e = JoinAt(i);
            return VpbNetRoomBook.DisplayToken(e);
        }

        public static string SelectedHostCode
        {
            get
            {
                EnsureLoaded();
                VpbNetRoomBookEntry e = VpbNetRoomBook.SelectedHost(_state);
                return e != null ? e.Key : string.Empty;
            }
        }

        public static int SelectedHostIndex
        {
            get
            {
                EnsureLoaded();
                return VpbNetRoomBook.IndexOfHost(_state, _state.SelectedHostKey);
            }
        }

        public static bool SelectedHostLocked
        {
            get
            {
                EnsureLoaded();
                return VpbNetRoomBook.HostLocked(_state);
            }
        }

        public static string LastJoinRaw
        {
            get
            {
                EnsureLoaded();
                try
                {
                    Settings s = Settings.Instance;
                    if (s != null && s.NetJoinRoomCode != null && !string.IsNullOrEmpty(s.NetJoinRoomCode.Value))
                        return s.NetJoinRoomCode.Value;
                }
                catch { }
                if (_state.Joins.Count == 0) return string.Empty;
                return VpbNetRoomBook.DisplayToken(_state.Joins[0]);
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                Settings s = Settings.Instance;
                string blob = string.Empty;
                string lan = string.Empty;
                bool locked = false;
                if (s != null)
                {
                    if (s.NetRoomBook != null && s.NetRoomBook.Value != null)
                        blob = s.NetRoomBook.Value;
                    if (s.NetLanRoomCode != null && s.NetLanRoomCode.Value != null)
                        lan = s.NetLanRoomCode.Value;
                    if (s.NetRoomCodeLocked != null) locked = s.NetRoomCodeLocked.Value;
                }

                if (!VpbNetRoomBook.TryDecode(blob, _state))
                    VpbNetRoomBook.TryDecode(string.Empty, _state);

                if (_state.Hosts.Count == 0 && VpbNetRoomBook.CanonicalRoom(lan) != null)
                    VpbNetRoomBook.SeedFromLegacy(_state, lan, locked, NowTicks());

                string host = string.Empty;
                if (s != null && s.NetHostRoomCode != null && s.NetHostRoomCode.Value != null)
                    host = s.NetHostRoomCode.Value;
                string hostNorm = VpbNetRoomBook.CanonicalRoom(host);
                if (hostNorm != null && VpbNetRoomBook.FindHost(_state, hostNorm) != null)
                    _state.SelectedHostKey = hostNorm;
                else if (VpbNetRoomBook.SelectedHost(_state) == null && hostNorm != null)
                    VpbNetRoomBook.AddHost(_state, hostNorm, NowTicks());

                RebuildLabels();
                _revision++;
                WriteMirrors(true);
            }
            catch
            {
                RebuildLabels();
            }
        }

        public static bool SelectHost(int index)
        {
            EnsureLoaded();
            if (!VpbNetRoomBook.SelectHostAt(_state, index)) return false;
            Bump();
            WriteMirrors(true);
            ApplyHostToActive();
            return true;
        }

        public static bool ForgetHost(int index)
        {
            EnsureLoaded();
            VpbNetRoomBookEntry e = HostAt(index);
            if (e == null) return false;
            if (!VpbNetRoomBook.ForgetHost(_state, e.Key)) return false;
            Bump();
            WriteMirrors(true);
            ApplyHostToActive();
            return true;
        }

        public static bool ForgetJoin(int index)
        {
            EnsureLoaded();
            VpbNetRoomBookEntry e = JoinAt(index);
            if (e == null) return false;
            if (!VpbNetRoomBook.ForgetJoin(_state, e.Key)) return false;
            Bump();
            PersistBook();
            return true;
        }

        public static bool SetSelectedLock(bool locked)
        {
            EnsureLoaded();
            if (!VpbNetRoomBook.SetHostLock(_state, locked)) return false;
            Bump();
            WriteMirrors(true);
            return true;
        }

        public static bool AddGeneratedHost(string code)
        {
            EnsureLoaded();
            if (!VpbNetRoomBook.AddHost(_state, code, NowTicks())) return false;
            Bump();
            WriteMirrors(true);
            ApplyHostToActive();
            return true;
        }

        public static bool ReplaceSelectedHost(string code)
        {
            EnsureLoaded();
            if (!VpbNetRoomBook.ReplaceSelectedHost(_state, code, NowTicks())) return false;
            Bump();
            WriteMirrors(true);
            ApplyHostToActive();
            return true;
        }

        public static bool ApplyHostToActive()
        {
            EnsureLoaded();
            VpbNetRoomBookEntry e = VpbNetRoomBook.SelectedHost(_state);
            if (e == null || e.Key == null) return false;
            WriteActive(e.Key);
            WriteHost(e.Key);
            return true;
        }

        public static bool ApplyJoinToActive(string raw)
        {
            if (raw == null) return false;
            raw = raw.Trim();
            if (raw.Length == 0) return false;
            EnsureLoaded();
            WriteJoin(raw);
            WriteActive(raw);
            return true;
        }

        public static void RememberConnected(bool asHost, string raw, string nick)
        {
            EnsureLoaded();
            string token = raw;
            if (!asHost)
            {
                try
                {
                    Settings s = Settings.Instance;
                    if (s != null && s.NetJoinRoomCode != null && !string.IsNullOrEmpty(s.NetJoinRoomCode.Value))
                        token = s.NetJoinRoomCode.Value;
                }
                catch { }
            }
            if (!VpbNetRoomBook.RememberConnected(_state, asHost, token, nick, NowTicks())) return;
            Bump();
            WriteMirrors(true);
        }

        static void Bump()
        {
            _revision++;
            RebuildLabels();
        }

        static void RebuildLabels()
        {
            for (int i = 0; i < VpbNetRoomBook.Cap; i++)
            {
                _hostLabels[i] = i < _state.Hosts.Count
                    ? VpbNetRoomBook.DisplayLabel(_state.Hosts[i], _labelSb)
                    : string.Empty;
                _joinLabels[i] = i < _state.Joins.Count
                    ? VpbNetRoomBook.DisplayLabel(_state.Joins[i], _labelSb)
                    : string.Empty;
            }
        }

        static void WriteMirrors(bool persistBook)
        {
            VpbNetRoomBookEntry e = VpbNetRoomBook.SelectedHost(_state);
            WriteHost(e != null ? e.Key : string.Empty);
            WriteLocked(e != null && e.Locked);
            if (persistBook) PersistBook();
        }

        static void PersistBook()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null || s.NetRoomBook == null) return;
                s.NetRoomBook.Value = VpbNetRoomBook.Encode(_state);
            }
            catch { }
        }

        static void WriteActive(string v)
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetLanRoomCode != null) s.NetLanRoomCode.Value = v ?? string.Empty;
            }
            catch { }
        }

        static void WriteHost(string v)
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetHostRoomCode != null) s.NetHostRoomCode.Value = v ?? string.Empty;
            }
            catch { }
        }

        static void WriteJoin(string v)
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetJoinRoomCode != null) s.NetJoinRoomCode.Value = v ?? string.Empty;
            }
            catch { }
        }

        static void WriteLocked(bool v)
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetRoomCodeLocked != null) s.NetRoomCodeLocked.Value = v;
            }
            catch { }
        }

        static long NowTicks()
        {
            return DateTime.UtcNow.Ticks;
        }
    }
}
