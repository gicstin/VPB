using System;
using System.Text;

namespace VpbNet
{
    public enum VpbNetRoomCodeFault
    {
        None = 0,
        Empty = 1,
        TooShort = 2,
        TooLong = 3,
        BadCharacter = 4
    }

    public static class VpbNetRoomCode
    {
        public const int Chars = 12;
        public const int EntropyBits = 60;
        public const int EntropyBytes = 8;
        public const int GroupSize = 4;

        public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public static int DecodeChar(char ch)
        {
            return Decode(ch);
        }

        public static bool TryDecodeEntropy(string normalized, byte[] into, int offset)
        {
            if (normalized == null || normalized.Length != Chars) return false;
            if (into == null || offset < 0 || offset + EntropyBytes > into.Length) return false;

            ulong bits = 0UL;
            for (int i = 0; i < Chars; i++)
            {
                int v = Decode(normalized[i]);
                if (v < 0) return false;
                bits = (bits << 5) | (uint)v;
            }

            for (int i = EntropyBytes - 1; i >= 0; i--)
            {
                into[offset + i] = (byte)(bits & 0xFF);
                bits >>= 8;
            }
            return true;
        }

        public static string FromEntropy(byte[] random, int offset)
        {
            if (random == null || offset < 0 || offset + EntropyBytes > random.Length) return null;

            ulong bits = 0UL;
            for (int i = 0; i < EntropyBytes; i++) bits = (bits << 8) | random[offset + i];

            char[] c = new char[Chars];
            for (int i = Chars - 1; i >= 0; i--)
            {
                c[i] = Alphabet[(int)(bits & 31UL)];
                bits >>= 5;
            }
            return new string(c);
        }

        public static string Normalize(string input)
        {
            VpbNetRoomCodeFault fault;
            return Normalize(input, out fault);
        }

        public static string Normalize(string input, out VpbNetRoomCodeFault fault)
        {
            fault = VpbNetRoomCodeFault.None;
            if (input == null || input.Length == 0)
            {
                fault = VpbNetRoomCodeFault.Empty;
                return null;
            }

            char[] c = new char[Chars];
            int n = 0;
            for (int i = 0; i < input.Length; i++)
            {
                char ch = input[i];
                if (ch == '-' || ch == ' ' || ch == '\t' || ch == '_') continue;

                int v = Decode(ch);
                if (v < 0)
                {
                    fault = VpbNetRoomCodeFault.BadCharacter;
                    return null;
                }
                if (n >= Chars)
                {
                    fault = VpbNetRoomCodeFault.TooLong;
                    return null;
                }
                c[n++] = Alphabet[v];
            }

            if (n == 0)
            {
                fault = VpbNetRoomCodeFault.Empty;
                return null;
            }
            if (n < Chars)
            {
                fault = VpbNetRoomCodeFault.TooShort;
                return null;
            }
            return new string(c);
        }

        public static bool IsWellFormed(string input)
        {
            return Normalize(input) != null;
        }

        public static string Canonical(string input)
        {
            string norm = Normalize(input);
            if (norm != null) return norm;
            return input == null ? string.Empty : input.Trim();
        }

        public static string Group(string normalized)
        {
            if (normalized == null || normalized.Length != Chars) return normalized;

            int groups = Chars / GroupSize;
            char[] c = new char[Chars + groups - 1];
            int w = 0;
            for (int i = 0; i < Chars; i++)
            {
                if (i > 0 && (i % GroupSize) == 0) c[w++] = '-';
                c[w++] = normalized[i];
            }
            return new string(c);
        }

        public static string Explain(string input)
        {
            VpbNetRoomCodeFault fault;
            string norm = Normalize(input, out fault);
            if (norm != null) return null;

            switch (fault)
            {
                case VpbNetRoomCodeFault.Empty:
                    return "Room code is empty. Ask the host for their code, or press Generate to host one.";
                case VpbNetRoomCodeFault.TooShort:
                    return "Room code is too short. It is " + Chars + " characters (hyphens optional), like " + Group("K7M2QB94XTVR") + ".";
                case VpbNetRoomCodeFault.TooLong:
                    return "Room code is too long. It is exactly " + Chars + " characters, hyphens and spaces ignored.";
                case VpbNetRoomCodeFault.BadCharacter:
                    return "Room code contains a character that is not part of the alphabet. It never uses I, L, O or U - if you see one, it is a 1, a 1, a 0, or a typo.";
                default:
                    return "Room code is not valid.";
            }
        }

        static int Decode(char ch)
        {
            if (ch >= '0' && ch <= '9') return ch - '0';

            char u = ch;
            if (u >= 'a' && u <= 'z') u = (char)(u - 32);

            if (u == 'I' || u == 'L') return 1;
            if (u == 'O') return 0;
            if (u == 'U') return -1;

            for (int i = 10; i < Alphabet.Length; i++)
            {
                if (Alphabet[i] == u) return i;
            }
            return -1;
        }
    }
}
