using System;
using System.Text;

namespace VpbNet
{
    // Redact logs only — panel still shows the real code/address.
    public static class VpbNetRedact
    {
        // Room codes are 12 chars; invites are longer.
        public const int MinSecretRun = VpbNetRoomCode.Chars;
        public const int KeepChars = 4;

        const char Mask = 'x';

        public static string Code(string raw)
        {
            string norm = VpbNetRoomCode.Normalize(raw);
            if (norm == null) return "an unreadable code";
            return MaskCode(VpbNetRoomCode.Group(norm));
        }

        // Keep RFC1918 subnet; mask public IPs wholly.
        public static string Endpoint(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return "an address that was never published";

            string text = blob.Trim();
            string host = text;
            string port = null;

            int close = text.LastIndexOf(']');
            int colon = text.LastIndexOf(':');
            if (colon > close && colon > 0 && colon < text.Length - 1)
            {
                host = text.Substring(0, colon);
                port = text.Substring(colon + 1);
            }
            if (host.Length >= 2 && host[0] == '[' && host[host.Length - 1] == ']')
                host = host.Substring(1, host.Length - 2);

            StringBuilder sb = new StringBuilder(28);
            int end, a, b;
            if (QuadAt(host, 0, out end, out a, out b) && end == host.Length)
                AppendMaskedQuad(sb, host, a, b);
            else if (host.IndexOf(':') >= 0) sb.Append("[x::x]");
            else if (host.Length == 0) sb.Append("an address that was never published");
            else sb.Append("a named host");

            if (port != null && IsAllDigits(port))
            {
                sb.Append(':');
                sb.Append(port);
            }
            return sb.ToString();
        }

        // Catch codes/IPs in free-text logs.
        public static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            StringBuilder sb = new StringBuilder(text.Length + 8);
            int i = 0;
            while (i < text.Length)
            {
                int end, a, b;
                if (SecretRunAt(text, i, out end))
                {
                    AppendMaskedSecret(sb, text, i, end);
                    i = end;
                    continue;
                }
                if (QuadAt(text, i, out end, out a, out b) && !IsVersionAt(text, i, end))
                {
                    AppendMaskedQuad(sb, text, a, b);
                    i = end;
                    continue;
                }
                if (V6At(text, i, out end))
                {
                    sb.Append("[x::x]");
                    i = end;
                    continue;
                }
                sb.Append(text[i]);
                i++;
            }
            return sb.ToString();
        }

        static string MaskCode(string grouped)
        {
            if (grouped == null) return "an unreadable code";
            StringBuilder sb = new StringBuilder(grouped.Length);
            AppendMaskedSecret(sb, grouped, 0, grouped.Length);
            return sb.ToString();
        }

        // Keep first group; mask the rest.
        static void AppendMaskedSecret(StringBuilder sb, string s, int start, int end)
        {
            int kept = 0;
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (!IsCodeChar(c))
                {
                    sb.Append(c);
                    continue;
                }
                if (kept < KeepChars)
                {
                    sb.Append(c);
                    kept++;
                }
                else sb.Append(Mask);
            }
        }

        static void AppendMaskedQuad(StringBuilder sb, string s, int a, int b)
        {
            if (!IsPrivateV4(a, b))
            {
                sb.Append("x.x.x.x");
                return;
            }

            int seen = 0;
            for (int i = 0; i < s.Length && seen < 3; i++)
            {
                char c = s[i];
                sb.Append(c);
                if (c == '.') seen++;
            }
            sb.Append(Mask);
        }

        static bool IsPrivateV4(int a, int b)
        {
            if (a == 10 || a == 127) return true;
            if (a == 192 && b == 168) return true;
            if (a == 169 && b == 254) return true;
            if (a == 172 && b >= 16 && b <= 31) return true;
            return false;
        }

        static bool SecretRunAt(string s, int start, out int end)
        {
            end = start;
            if (!AtTokenStart(s, start)) return false;

            int i = start;
            int chars = 0;
            int last = start - 1;
            while (i < s.Length)
            {
                char c = s[i];
                if (IsCodeChar(c))
                {
                    chars++;
                    last = i;
                    i++;
                    continue;
                }
                if (c == '-' && chars > 0)
                {
                    i++;
                    continue;
                }
                break;
            }

            if (chars < MinSecretRun) return false;

            int stop = last + 1;
            // Mixed-case word, not a code.
            if (stop < s.Length && IsTail(s[stop])) return false;
            end = stop;
            return true;
        }

        static bool QuadAt(string s, int start, out int end, out int a, out int b)
        {
            end = start;
            a = 0;
            b = 0;
            if (!AtTokenStart(s, start)) return false;

            int i = start;
            for (int part = 0; part < 4; part++)
            {
                if (part > 0)
                {
                    if (i >= s.Length || s[i] != '.') return false;
                    i++;
                }

                int digits = 0;
                int value = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9' && digits < 3)
                {
                    value = value * 10 + (s[i] - '0');
                    digits++;
                    i++;
                }
                if (digits == 0 || value > 255) return false;
                if (part == 0) a = value;
                else if (part == 1) b = value;
            }

            // Fifth dotted part → version, not address.
            if (i < s.Length && (s[i] == '.' || (s[i] >= '0' && s[i] <= '9'))) return false;
            end = i;
            return true;
        }

        // Port or prefix distinguishes 1.2.3.4 from an address.
        static bool IsVersionAt(string s, int start, int end)
        {
            if (end < s.Length && s[end] == ':'
                && end + 1 < s.Length && s[end + 1] >= '0' && s[end + 1] <= '9')
                return false;

            int i = start - 1;
            while (i >= 0 && s[i] == ' ') i--;
            if (i < 0) return false;

            int wordEnd = i + 1;
            while (i >= 0 && !IsSpace(s[i])) i--;
            return WordEquals(s, i + 1, wordEnd, "version")
                || WordEquals(s, i + 1, wordEnd, "ver")
                || WordEquals(s, i + 1, wordEnd, "build");
        }

        static bool IsSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r';
        }

        static bool WordEquals(string s, int start, int end, string word)
        {
            if (end - start != word.Length) return false;
            for (int i = 0; i < word.Length; i++)
            {
                char a = s[start + i];
                if (a >= 'A' && a <= 'Z') a = (char)(a + 32);
                if (a != word[i]) return false;
            }
            return true;
        }

        static bool V6At(string s, int start, out int end)
        {
            end = start;
            if (start >= s.Length || s[start] != '[') return false;

            int colons = 0;
            for (int i = start + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ']')
                {
                    if (colons < 2) return false;
                    end = i + 1;
                    return true;
                }
                if (c == ':')
                {
                    colons++;
                    continue;
                }
                if (!IsHexChar(c)) return false;
            }
            return false;
        }

        static bool AtTokenStart(string s, int at)
        {
            if (at <= 0) return true;
            char prev = s[at - 1];
            return !IsCodeChar(prev) && !IsTail(prev) && prev != '-' && prev != '.';
        }

        static bool IsCodeChar(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        static bool IsTail(char c)
        {
            return (c >= 'a' && c <= 'z') || c == '_';
        }

        static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
            }
            return true;
        }
    }
}
