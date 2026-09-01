using System;
using System.Text;

namespace VpbNet
{
    public enum VpbNetInviteFault
    {
        None = 0,
        Empty = 1,
        BadCharacter = 2,
        BadLength = 3,
        BadVersion = 4,
        BadFamily = 5,
        BadChecksum = 6,
        BadEndpoint = 7
    }

    public static class VpbNetInviteCode
    {
        public const byte Version = 1;
        public const int ChecksumBytes = 2;
        public const int V4Bytes = 1 + 4 + 2 + VpbNetRoomCode.EntropyBytes + ChecksumBytes;
        public const int V6Bytes = 1 + 16 + 2 + VpbNetRoomCode.EntropyBytes + ChecksumBytes;
        public const int V4Chars = (V4Bytes * 8 + 4) / 5;
        public const int V6Chars = (V6Bytes * 8 + 4) / 5;
        public const int GroupSize = 5;

        public static string Create(string roomCode, VpbNetEndpoint host)
        {
            string norm = VpbNetRoomCode.Normalize(roomCode);
            if (norm == null || !host.IsPresent) return null;

            int addrLen = VpbNetEndpoint.AddressBytesFor(host.Family);
            if (addrLen < 0 || host.Address.Length != addrLen) return null;

            int total = 1 + addrLen + 2 + VpbNetRoomCode.EntropyBytes + ChecksumBytes;
            byte[] raw = new byte[total];

            raw[0] = (byte)((Version << 4) | (host.Family & 0x0F));
            Buffer.BlockCopy(host.Address, 0, raw, 1, addrLen);

            int w = 1 + addrLen;
            raw[w] = (byte)(host.Port & 0xFF);
            raw[w + 1] = (byte)((host.Port >> 8) & 0xFF);
            w += 2;

            if (!VpbNetRoomCode.TryDecodeEntropy(norm, raw, w)) return null;
            w += VpbNetRoomCode.EntropyBytes;

            ushort sum = Checksum(raw, w);
            raw[w] = (byte)(sum & 0xFF);
            raw[w + 1] = (byte)((sum >> 8) & 0xFF);

            return Encode(raw, total);
        }

        public static bool TryParse(string invite, out string roomCode, out VpbNetEndpoint host, out VpbNetInviteFault fault)
        {
            roomCode = null;
            host = new VpbNetEndpoint();
            fault = VpbNetInviteFault.Empty;

            if (invite == null || invite.Length == 0) return false;

            int chars = 0;
            for (int i = 0; i < invite.Length; i++)
            {
                char ch = invite[i];
                if (ch == '-' || ch == ' ' || ch == '\t' || ch == '_') continue;
                if (VpbNetRoomCode.DecodeChar(ch) < 0)
                {
                    fault = VpbNetInviteFault.BadCharacter;
                    return false;
                }
                chars++;
            }

            if (chars == 0) return false;

            int expectBytes;
            if (chars == V4Chars) expectBytes = V4Bytes;
            else if (chars == V6Chars) expectBytes = V6Bytes;
            else
            {
                fault = VpbNetInviteFault.BadLength;
                return false;
            }

            byte[] raw = new byte[expectBytes];
            if (!Decode(invite, raw, expectBytes))
            {
                fault = VpbNetInviteFault.BadCharacter;
                return false;
            }

            int body = expectBytes - ChecksumBytes;
            ushort want = (ushort)(raw[body] | (raw[body + 1] << 8));
            if (want != Checksum(raw, body))
            {
                fault = VpbNetInviteFault.BadChecksum;
                return false;
            }

            if ((raw[0] >> 4) != Version)
            {
                fault = VpbNetInviteFault.BadVersion;
                return false;
            }

            byte family = (byte)(raw[0] & 0x0F);
            int addrLen = VpbNetEndpoint.AddressBytesFor(family);
            if (addrLen < 0 || 1 + addrLen + 2 + VpbNetRoomCode.EntropyBytes + ChecksumBytes != expectBytes)
            {
                fault = VpbNetInviteFault.BadFamily;
                return false;
            }

            byte[] addr = new byte[addrLen];
            Buffer.BlockCopy(raw, 1, addr, 0, addrLen);

            int r = 1 + addrLen;
            ushort port = (ushort)(raw[r] | (raw[r + 1] << 8));
            r += 2;

            if (port == 0)
            {
                fault = VpbNetInviteFault.BadEndpoint;
                return false;
            }

            roomCode = VpbNetRoomCode.FromEntropy(raw, r);
            if (roomCode == null)
            {
                fault = VpbNetInviteFault.BadEndpoint;
                return false;
            }

            host.Family = family;
            host.Address = addr;
            host.Port = port;
            fault = VpbNetInviteFault.None;
            return true;
        }

        public static bool TryResolveJoinTarget(string roomField, string addressField,
            out string roomCode, out string connectBlob, out bool addressOverridden, out VpbNetInviteFault fault)
        {
            roomCode = roomField;
            connectBlob = addressField;
            addressOverridden = false;
            fault = VpbNetInviteFault.None;

            if (!LooksLikeInvite(roomField)) return true;

            VpbNetEndpoint host;
            string decoded;
            if (!TryParse(roomField, out decoded, out host, out fault)) return false;

            StringBuilder sb = new StringBuilder(48);
            host.Describe(sb);
            string fromInvite = sb.ToString();

            string typed = addressField == null ? string.Empty : addressField.Trim();
            addressOverridden = typed.Length > 0
                && !string.Equals(typed, fromInvite, StringComparison.OrdinalIgnoreCase);

            roomCode = decoded;
            connectBlob = fromInvite;
            return true;
        }

        public static bool LooksLikeInvite(string text)
        {
            if (text == null) return false;
            int chars = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '-' || ch == ' ' || ch == '\t' || ch == '_') continue;
                if (VpbNetRoomCode.DecodeChar(ch) < 0) return false;
                chars++;
            }
            return chars == V4Chars || chars == V6Chars;
        }

        public static string Compact(string input)
        {
            if (input == null || input.Length == 0) return null;

            char[] buf = new char[V6Chars];
            int n = 0;
            for (int i = 0; i < input.Length; i++)
            {
                char ch = input[i];
                if (ch == '-' || ch == ' ' || ch == '\t' || ch == '_') continue;
                int d = VpbNetRoomCode.DecodeChar(ch);
                if (d < 0) return null;
                if (n >= buf.Length) return null;
                buf[n++] = VpbNetRoomCode.Alphabet[d];
            }
            if (n != V4Chars && n != V6Chars) return null;
            return new string(buf, 0, n);
        }

        public static string Group(string code)
        {
            if (code == null || code.Length == 0) return code;

            StringBuilder sb = new StringBuilder(code.Length + code.Length / GroupSize + 1);
            for (int i = 0; i < code.Length; i++)
            {
                if (i > 0 && (i % GroupSize) == 0) sb.Append('-');
                sb.Append(code[i]);
            }
            return sb.ToString();
        }

        public static string Explain(VpbNetInviteFault fault)
        {
            switch (fault)
            {
                case VpbNetInviteFault.Empty:
                    return "The invite is empty. Ask the host to copy theirs and paste the whole thing.";
                case VpbNetInviteFault.BadCharacter:
                    return "The invite contains a character it never uses. It never uses I, L, O or U - if you see one, it is a 1, a 1, a 0, or a typo.";
                case VpbNetInviteFault.BadLength:
                    return "The invite is the wrong length - it is " + V4Chars + " characters, or " + V6Chars
                        + " for an IPv6 host. Paste the whole thing, including every group.";
                case VpbNetInviteFault.BadChecksum:
                    return "The invite did not check out, so at least one character is wrong. Paste it again rather than retyping it - a single wrong character points at a different address.";
                case VpbNetInviteFault.BadVersion:
                    return "This invite was made by a newer version of VPB. Update, or ask the host for a plain room code and address.";
                case VpbNetInviteFault.BadFamily:
                    return "The invite names an address type this build does not understand.";
                case VpbNetInviteFault.BadEndpoint:
                    return "The invite decoded but names no reachable host. Ask the host to generate a new one.";
                default:
                    return null;
            }
        }

        static string Encode(byte[] raw, int len)
        {
            int chars = (len * 8 + 4) / 5;
            char[] c = new char[chars];

            int bitPos = 0;
            for (int i = 0; i < chars; i++)
            {
                int v = 0;
                for (int b = 0; b < 5; b++)
                {
                    v <<= 1;
                    int byteIndex = bitPos >> 3;
                    if (byteIndex < len)
                    {
                        int bitIndex = 7 - (bitPos & 7);
                        v |= (raw[byteIndex] >> bitIndex) & 1;
                    }
                    bitPos++;
                }
                c[i] = VpbNetRoomCode.Alphabet[v];
            }
            return new string(c);
        }

        static bool Decode(string text, byte[] into, int len)
        {
            int bitPos = 0;
            int totalBits = len * 8;
            for (int i = 0; i < into.Length; i++) into[i] = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '-' || ch == ' ' || ch == '\t' || ch == '_') continue;

                int v = VpbNetRoomCode.DecodeChar(ch);
                if (v < 0) return false;

                for (int b = 4; b >= 0; b--)
                {
                    if (bitPos >= totalBits)
                    {
                        bitPos++;
                        continue;
                    }
                    if (((v >> b) & 1) != 0)
                    {
                        into[bitPos >> 3] |= (byte)(1 << (7 - (bitPos & 7)));
                    }
                    bitPos++;
                }
            }
            return true;
        }

        static ushort Checksum(byte[] raw, int len)
        {
            uint h = 2166136261u;
            for (int i = 0; i < len; i++)
            {
                h ^= raw[i];
                h *= 16777619u;
            }
            return (ushort)((h ^ (h >> 16)) & 0xFFFF);
        }
    }
}
