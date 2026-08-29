using System;
using System.Collections.Generic;
using System.Text;

namespace VpbNet
{
    public sealed class VpbNetRoomBookEntry
    {
        public string Key;
        public string Nick;
        public bool Locked;
        public bool Invite;
        public long Ticks;
    }

    public sealed class VpbNetRoomBookState
    {
        public string SelectedHostKey = string.Empty;
        public readonly List<VpbNetRoomBookEntry> Hosts = new List<VpbNetRoomBookEntry>(VpbNetRoomBook.Cap);
        public readonly List<VpbNetRoomBookEntry> Joins = new List<VpbNetRoomBookEntry>(VpbNetRoomBook.Cap);
    }

    // Bounded host inventory + join recents. Codec only — persist lives in the plugin.
    public static class VpbNetRoomBook
    {
        public const int Cap = 8;
        public const int Version = 1;
        const char Sep = '|';
        const string EmptyNick = ".";
        const int MaxNick = 24;

        public static string CanonicalRoom(string raw)
        {
            return VpbNetRoomCode.Normalize(raw);
        }

        public static string CanonicalJoin(string raw)
        {
            string room = VpbNetRoomCode.Normalize(raw);
            if (room != null) return room;
            return VpbNetInviteCode.Compact(raw);
        }

        public static bool IsInviteKey(string key)
        {
            return VpbNetInviteCode.LooksLikeInvite(key);
        }

        public static string DisplayToken(VpbNetRoomBookEntry e)
        {
            if (e == null || e.Key == null) return string.Empty;
            if (e.Invite) return VpbNetInviteCode.Group(e.Key);
            return VpbNetRoomCode.Group(e.Key);
        }

        public static string DisplayLabel(VpbNetRoomBookEntry e, StringBuilder sb)
        {
            string token = DisplayToken(e);
            if (e == null) return token;
            if (e.Nick == null || e.Nick.Length == 0) return token;
            if (sb == null) return e.Nick + "  ·  " + token;
            sb.Length = 0;
            sb.Append(e.Nick);
            sb.Append("  ·  ");
            sb.Append(token);
            return sb.ToString();
        }

        public static VpbNetRoomBookEntry FindHost(VpbNetRoomBookState state, string key)
        {
            if (state == null || key == null || key.Length == 0) return null;
            for (int i = 0; i < state.Hosts.Count; i++)
            {
                VpbNetRoomBookEntry e = state.Hosts[i];
                if (e != null && string.Equals(e.Key, key, StringComparison.Ordinal)) return e;
            }
            return null;
        }

        public static int IndexOfHost(VpbNetRoomBookState state, string key)
        {
            if (state == null || key == null) return -1;
            for (int i = 0; i < state.Hosts.Count; i++)
            {
                VpbNetRoomBookEntry e = state.Hosts[i];
                if (e != null && string.Equals(e.Key, key, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        public static int IndexOfJoin(VpbNetRoomBookState state, string key)
        {
            if (state == null || key == null) return -1;
            for (int i = 0; i < state.Joins.Count; i++)
            {
                VpbNetRoomBookEntry e = state.Joins[i];
                if (e != null && string.Equals(e.Key, key, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        public static VpbNetRoomBookEntry SelectedHost(VpbNetRoomBookState state)
        {
            if (state == null) return null;
            VpbNetRoomBookEntry e = FindHost(state, state.SelectedHostKey);
            if (e != null) return e;
            if (state.Hosts.Count == 0) return null;
            return state.Hosts[0];
        }

        public static bool HostLocked(VpbNetRoomBookState state)
        {
            VpbNetRoomBookEntry e = SelectedHost(state);
            return e != null && e.Locked;
        }

        public static bool SelectHost(VpbNetRoomBookState state, string key)
        {
            if (FindHost(state, key) == null) return false;
            state.SelectedHostKey = key;
            return true;
        }

        public static bool SelectHostAt(VpbNetRoomBookState state, int index)
        {
            if (state == null || index < 0 || index >= state.Hosts.Count) return false;
            VpbNetRoomBookEntry e = state.Hosts[index];
            if (e == null || e.Key == null) return false;
            state.SelectedHostKey = e.Key;
            return true;
        }

        public static bool SetHostLock(VpbNetRoomBookState state, bool locked)
        {
            VpbNetRoomBookEntry e = SelectedHost(state);
            if (e == null) return false;
            e.Locked = locked;
            return true;
        }

        public static bool AddHost(VpbNetRoomBookState state, string key, long ticks)
        {
            string norm = CanonicalRoom(key);
            if (norm == null || state == null) return false;

            VpbNetRoomBookEntry existing = FindHost(state, norm);
            if (existing != null)
            {
                existing.Ticks = ticks;
                state.SelectedHostKey = norm;
                MoveHostToFront(state, IndexOfHost(state, norm));
                return true;
            }

            if (!EvictHost(state)) return false;

            VpbNetRoomBookEntry e = new VpbNetRoomBookEntry();
            e.Key = norm;
            e.Nick = string.Empty;
            e.Locked = false;
            e.Invite = false;
            e.Ticks = ticks;
            state.Hosts.Insert(0, e);
            state.SelectedHostKey = norm;
            return true;
        }

        public static bool ReplaceSelectedHost(VpbNetRoomBookState state, string key, long ticks)
        {
            string norm = CanonicalRoom(key);
            if (norm == null || state == null) return false;

            VpbNetRoomBookEntry e = SelectedHost(state);
            if (e == null) return AddHost(state, norm, ticks);
            if (e.Locked) return false;

            VpbNetRoomBookEntry clash = FindHost(state, norm);
            if (clash != null && clash != e)
            {
                state.SelectedHostKey = clash.Key;
                clash.Ticks = ticks;
                return true;
            }

            e.Key = norm;
            e.Ticks = ticks;
            state.SelectedHostKey = norm;
            return true;
        }

        public static bool ForgetHost(VpbNetRoomBookState state, string key)
        {
            int i = IndexOfHost(state, key);
            if (i < 0) return false;
            bool wasSelected = string.Equals(state.SelectedHostKey, key, StringComparison.Ordinal);
            state.Hosts.RemoveAt(i);
            if (!wasSelected) return true;
            if (state.Hosts.Count == 0)
            {
                state.SelectedHostKey = string.Empty;
                return true;
            }
            int next = i < state.Hosts.Count ? i : 0;
            VpbNetRoomBookEntry e = state.Hosts[next];
            state.SelectedHostKey = e != null ? e.Key : string.Empty;
            return true;
        }

        public static bool RememberJoin(VpbNetRoomBookState state, string raw, string nick, long ticks)
        {
            string key = CanonicalJoin(raw);
            if (key == null || state == null) return false;

            string safe = SanitizeNick(nick);
            int i = IndexOfJoin(state, key);
            if (i >= 0)
            {
                VpbNetRoomBookEntry e = state.Joins[i];
                e.Ticks = ticks;
                if (safe.Length > 0) e.Nick = safe;
                MoveJoinToFront(state, i);
                return true;
            }

            while (state.Joins.Count >= Cap)
                state.Joins.RemoveAt(state.Joins.Count - 1);

            VpbNetRoomBookEntry n = new VpbNetRoomBookEntry();
            n.Key = key;
            n.Nick = safe;
            n.Locked = false;
            n.Invite = IsInviteKey(key);
            n.Ticks = ticks;
            state.Joins.Insert(0, n);
            return true;
        }

        public static bool ForgetJoin(VpbNetRoomBookState state, string key)
        {
            int i = IndexOfJoin(state, key);
            if (i < 0) return false;
            state.Joins.RemoveAt(i);
            return true;
        }

        public static bool RememberConnected(VpbNetRoomBookState state, bool asHost, string raw, string nick, long ticks)
        {
            if (asHost)
            {
                string room = CanonicalRoom(raw);
                if (room == null) return false;
                VpbNetRoomBookEntry e = FindHost(state, room);
                if (e == null)
                {
                    if (!AddHost(state, room, ticks)) return false;
                    e = FindHost(state, room);
                }
                if (e == null) return false;
                e.Ticks = ticks;
                string safe = SanitizeNick(nick);
                if (safe.Length > 0) e.Nick = safe;
                return true;
            }

            return RememberJoin(state, raw, nick, ticks);
        }

        public static void SeedFromLegacy(VpbNetRoomBookState state, string lanCode, bool locked, long ticks)
        {
            if (state == null) return;
            string room = CanonicalRoom(lanCode);
            string join = CanonicalJoin(lanCode);
            if (room != null)
            {
                AddHost(state, room, ticks);
                VpbNetRoomBookEntry e = FindHost(state, room);
                if (e != null) e.Locked = locked;
                state.SelectedHostKey = room;
            }
            if (join != null)
                RememberJoin(state, join, string.Empty, ticks);
        }

        public static string Encode(VpbNetRoomBookState state)
        {
            if (state == null) return string.Empty;
            StringBuilder sb = new StringBuilder(128 + (state.Hosts.Count + state.Joins.Count) * 48);
            sb.Append(Version.ToString());
            sb.Append(Sep);
            sb.Append('S');
            sb.Append(Sep);
            sb.Append(state.SelectedHostKey ?? string.Empty);
            for (int i = 0; i < state.Hosts.Count; i++)
            {
                VpbNetRoomBookEntry e = state.Hosts[i];
                if (e == null || e.Key == null || e.Key.Length == 0) continue;
                sb.Append(Sep);
                sb.Append('H');
                sb.Append(Sep);
                sb.Append(e.Key);
                sb.Append(Sep);
                sb.Append(EncodeNick(e.Nick));
                sb.Append(Sep);
                sb.Append(e.Locked ? '1' : '0');
                sb.Append(Sep);
                sb.Append(e.Ticks.ToString());
            }
            for (int i = 0; i < state.Joins.Count; i++)
            {
                VpbNetRoomBookEntry e = state.Joins[i];
                if (e == null || e.Key == null || e.Key.Length == 0) continue;
                sb.Append(Sep);
                sb.Append('J');
                sb.Append(Sep);
                sb.Append(e.Key);
                sb.Append(Sep);
                sb.Append(EncodeNick(e.Nick));
                sb.Append(Sep);
                sb.Append(e.Invite ? '1' : '0');
                sb.Append(Sep);
                sb.Append(e.Ticks.ToString());
            }
            return sb.ToString();
        }

        public static bool TryDecode(string blob, VpbNetRoomBookState into)
        {
            if (into == null) return false;
            into.SelectedHostKey = string.Empty;
            into.Hosts.Clear();
            into.Joins.Clear();
            if (blob == null || blob.Length == 0) return true;

            string[] parts = blob.Split(Sep);
            if (parts.Length < 1) return false;
            if (parts[0] != "1") return false;

            int i = 1;
            while (i < parts.Length)
            {
                string tag = parts[i];
                if (tag == "S")
                {
                    if (i + 1 >= parts.Length) return false;
                    into.SelectedHostKey = parts[i + 1] ?? string.Empty;
                    i += 2;
                    continue;
                }
                if (tag == "H")
                {
                    if (i + 4 >= parts.Length) return false;
                    string key = CanonicalRoom(parts[i + 1]);
                    if (key == null)
                    {
                        i += 5;
                        continue;
                    }
                    if (FindHost(into, key) != null)
                    {
                        i += 5;
                        continue;
                    }
                    if (into.Hosts.Count >= Cap)
                    {
                        i += 5;
                        continue;
                    }
                    VpbNetRoomBookEntry e = new VpbNetRoomBookEntry();
                    e.Key = key;
                    e.Nick = DecodeNick(parts[i + 2]);
                    e.Locked = parts[i + 3] == "1";
                    e.Invite = false;
                    long ticks;
                    if (!long.TryParse(parts[i + 4], out ticks)) ticks = 0;
                    e.Ticks = ticks;
                    into.Hosts.Add(e);
                    i += 5;
                    continue;
                }
                if (tag == "J")
                {
                    if (i + 4 >= parts.Length) return false;
                    string key = CanonicalJoin(parts[i + 1]);
                    if (key == null)
                    {
                        i += 5;
                        continue;
                    }
                    if (IndexOfJoin(into, key) >= 0)
                    {
                        i += 5;
                        continue;
                    }
                    if (into.Joins.Count >= Cap)
                    {
                        i += 5;
                        continue;
                    }
                    VpbNetRoomBookEntry e = new VpbNetRoomBookEntry();
                    e.Key = key;
                    e.Nick = DecodeNick(parts[i + 2]);
                    e.Locked = false;
                    e.Invite = parts[i + 3] == "1" || IsInviteKey(key);
                    long ticks;
                    if (!long.TryParse(parts[i + 4], out ticks)) ticks = 0;
                    e.Ticks = ticks;
                    into.Joins.Add(e);
                    i += 5;
                    continue;
                }
                return false;
            }

            if (FindHost(into, into.SelectedHostKey) == null)
            {
                into.SelectedHostKey = into.Hosts.Count > 0 && into.Hosts[0] != null
                    ? into.Hosts[0].Key
                    : string.Empty;
            }
            return true;
        }

        public static int Snapshot(VpbNetRoomBookState state)
        {
            if (state == null) return 0;
            int h = state.Hosts.Count * 397 + state.Joins.Count;
            h = h * 31 + Hash(state.SelectedHostKey);
            for (int i = 0; i < state.Hosts.Count; i++)
            {
                VpbNetRoomBookEntry e = state.Hosts[i];
                if (e == null) continue;
                h = h * 31 + Hash(e.Key);
                h = h * 31 + Hash(e.Nick);
                if (e.Locked) h ^= 0x5A5A;
            }
            for (int i = 0; i < state.Joins.Count; i++)
            {
                VpbNetRoomBookEntry e = state.Joins[i];
                if (e == null) continue;
                h = h * 31 + Hash(e.Key);
                h = h * 31 + Hash(e.Nick);
            }
            return h;
        }

        static bool EvictHost(VpbNetRoomBookState state)
        {
            if (state.Hosts.Count < Cap) return true;
            int drop = -1;
            long oldest = long.MaxValue;
            for (int i = 0; i < state.Hosts.Count; i++)
            {
                VpbNetRoomBookEntry e = state.Hosts[i];
                if (e == null || e.Locked) continue;
                if (string.Equals(e.Key, state.SelectedHostKey, StringComparison.Ordinal)) continue;
                if (e.Ticks > oldest) continue;
                oldest = e.Ticks;
                drop = i;
            }
            if (drop < 0) return false;
            state.Hosts.RemoveAt(drop);
            return true;
        }

        static void MoveHostToFront(VpbNetRoomBookState state, int index)
        {
            if (index <= 0) return;
            VpbNetRoomBookEntry e = state.Hosts[index];
            state.Hosts.RemoveAt(index);
            state.Hosts.Insert(0, e);
        }

        static void MoveJoinToFront(VpbNetRoomBookState state, int index)
        {
            if (index <= 0) return;
            VpbNetRoomBookEntry e = state.Joins[index];
            state.Joins.RemoveAt(index);
            state.Joins.Insert(0, e);
        }

        public static string SanitizeNick(string nick)
        {
            if (nick == null || nick.Length == 0) return string.Empty;
            int max = MaxNick;
            char[] buf = new char[max];
            int n = 0;
            for (int i = 0; i < nick.Length && n < max; i++)
            {
                char ch = nick[i];
                if (ch == Sep || ch < 32) continue;
                buf[n++] = ch;
            }
            if (n == 0) return string.Empty;
            return new string(buf, 0, n);
        }

        static string EncodeNick(string nick)
        {
            string s = SanitizeNick(nick);
            return s.Length == 0 ? EmptyNick : s;
        }

        static string DecodeNick(string raw)
        {
            if (raw == null || raw.Length == 0 || raw == EmptyNick) return string.Empty;
            return SanitizeNick(raw);
        }

        static int Hash(string s)
        {
            if (s == null) return 0;
            int h = s.Length;
            for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
            return h;
        }
    }
}
