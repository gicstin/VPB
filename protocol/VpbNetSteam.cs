using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace VpbNet
{
    public enum VpbNetSteamFault
    {
        None = 0,
        NotSteamBlob = 1,
        BadAppId = 2,
        AppIdOutOfRange = 3
    }

    public static class VpbNetSteam
    {
        public const uint DefaultAppId = 480;
        public const uint MaxAppId = 4000000000;

        public const string BlobPrefix = "steam:";
        public const string NativeLibrary = "steam_api64.dll";

        public const string LobbyKeyRoom = "r";

        public const int LobbyTokenHexChars = 32;
        public const int MaxLobbyResults = 12;

        public static string BuildConnectBlob(uint appId)
        {
            if (appId == 0) appId = DefaultAppId;
            return BlobPrefix + appId.ToString(CultureInfo.InvariantCulture);
        }

        public static bool LooksLikeSteamBlob(string blob)
        {
            if (blob == null) return false;
            string t = blob.Trim();
            return t.Length >= BlobPrefix.Length
                && string.Compare(t, 0, BlobPrefix, 0, BlobPrefix.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        public static bool TryParseConnectBlob(string blob, out uint appId, out VpbNetSteamFault fault)
        {
            appId = DefaultAppId;
            fault = VpbNetSteamFault.None;

            string t = blob == null ? string.Empty : blob.Trim();
            if (t.Length == 0) return true;

            if (LooksLikeSteamBlob(t)) t = t.Substring(BlobPrefix.Length).Trim();
            if (t.Length == 0) return true;

            uint parsed;
            if (!uint.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                fault = VpbNetSteamFault.BadAppId;
                return false;
            }
            if (parsed == 0 || parsed > MaxAppId)
            {
                fault = VpbNetSteamFault.AppIdOutOfRange;
                return false;
            }

            appId = parsed;
            return true;
        }

        public static bool IsLobbyToken(string token)
        {
            if (token == null || token.Length != LobbyTokenHexChars) return false;
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!ok) return false;
            }
            return true;
        }

        public static string[] SearchFolders(string brokerDir)
        {
            string plugins = null;
            string vamRoot = null;
            try
            {
                if (!string.IsNullOrEmpty(brokerDir))
                {
                    DirectoryInfo d = new DirectoryInfo(brokerDir);
                    if (d.Parent != null)
                    {
                        plugins = d.Parent.FullName;
                        if (d.Parent.Parent != null && d.Parent.Parent.Parent != null)
                            vamRoot = d.Parent.Parent.Parent.FullName;
                    }
                }
            }
            catch { }

            return new string[] { brokerDir, plugins, vamRoot };
        }

        public static string FindLibrary(string explicitPath, string brokerDir)
        {
            if (!string.IsNullOrEmpty(explicitPath))
            {
                string p = explicitPath.Trim();
                try
                {
                    if (Directory.Exists(p)) p = Path.Combine(p, NativeLibrary);
                    if (File.Exists(p)) return p;
                }
                catch { }
            }

            string[] folders = SearchFolders(brokerDir);
            for (int i = 0; i < folders.Length; i++)
            {
                if (string.IsNullOrEmpty(folders[i])) continue;
                try
                {
                    string candidate = Path.Combine(folders[i], NativeLibrary);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        public static string Explain(VpbNetSteamFault fault)
        {
            switch (fault)
            {
                case VpbNetSteamFault.NotSteamBlob:
                    return "that is not a Steam connect blob - it looks like \"" + BlobPrefix + DefaultAppId + "\".";
                case VpbNetSteamFault.BadAppId:
                    return "the Steam app id must be a number, for example " + BuildConnectBlob(DefaultAppId)
                        + ". Leave it at " + DefaultAppId + " unless you and the other player both own the same Steam game and have both changed it.";
                case VpbNetSteamFault.AppIdOutOfRange:
                    return "that Steam app id is out of range. The default is " + DefaultAppId + ".";
            }
            return null;
        }

        public static string MissingLibrary(string folder)
        {
            StringBuilder sb = new StringBuilder(384);
            sb.Append("Steam connections need ").Append(NativeLibrary);
            sb.Append(", which ships with VPB and belongs next to VpbNet.exe - and it is not in ");
            sb.Append(string.IsNullOrEmpty(folder) ? "the VpbNet folder" : folder);
            sb.Append(". Run the updater to repair the install; that is what puts it there. ");
            sb.Append("Failing that, any 64-bit copy will do - Steam games have one, ");
            sb.Append("and a Steam build of VaM has one in its install folder, which is also searched. ");
            sb.Append("Direct sessions do not need it.");
            return sb.ToString();
        }

        public static string MissingExport(string name)
        {
            return "the copy of " + NativeLibrary + " that was found is too old for this connection - it has no "
                + name + ". Replace it with a newer one (Steam's own game folders have current copies).";
        }

        public static string InitFailed(uint appId, string detail)
        {
            StringBuilder sb = new StringBuilder(320);
            sb.Append("Steam would not start up");
            if (!string.IsNullOrEmpty(detail))
            {
                sb.Append(" (");
                sb.Append(detail);
                sb.Append(')');
            }
            sb.Append(". Start the Steam client and sign in, then try again. ");
            if (appId != DefaultAppId) sb.Append("This session is set to app id ").Append(appId).Append(", not the default ").Append(DefaultAppId).Append("; both sides must match. ");
            sb.Append("Steam must be running on this machine - it is what carries the connection.");
            return sb.ToString();
        }

        public static string RelayUnavailable()
        {
            return "Steam is signed in but has not finished connecting to its relay network."
                + " This usually clears itself in a few seconds. If it does not, Steam itself is offline or blocked.";
        }

        public static string NoRoom(string groupedRoomCode, int seconds)
        {
            StringBuilder sb = new StringBuilder(320);
            sb.Append("Nobody is hosting room ");
            sb.Append(string.IsNullOrEmpty(groupedRoomCode) ? "that code" : groupedRoomCode);
            sb.Append(" on Steam. Waited ").Append(seconds).Append("s. ");
            sb.Append("They have to press Open room first, also pick Steam, ");
            sb.Append("and the code must match. ");
            sb.Append("A full room, or a room from a different VPB build, looks the same as no room.");
            return sb.ToString();
        }

        public static string SearchingHint(string groupedRoomCode, int seconds)
        {
            StringBuilder sb = new StringBuilder(160);
            sb.Append("looking for room ");
            sb.Append(string.IsNullOrEmpty(groupedRoomCode) ? "on Steam" : groupedRoomCode);
            if (seconds > 2)
            {
                sb.Append(" (").Append(seconds).Append("s) - they have to press Open room first");
            }
            return sb.ToString();
        }

        public static string IdentityWarning()
        {
            return "Over Steam, the person you connect to can see which Steam account you are signed into."
                + " The relay hides your IP address from them."
                + " Direct is not a privacy option: each of you then sees the other's IP address."
                + " Use a separate Steam account if the account itself is the problem.";
        }

        public static string RelayOnlyUnavailable()
        {
            return "this copy of " + NativeLibrary + " is too old to be told \"relay only\","
                + " which means Steam could connect the two of you directly and the other player's machine would learn your IP address."
                + " A Steam session is supposed to keep your address to itself, so this one has been refused rather than started"
                + " on a promise it cannot keep. Run the updater to repair the install, or point Net.SteamApiPath at a newer copy"
                + " (Steam's own game folders have current ones). Direct sessions are unaffected - they never hid your address to begin with.";
        }
    }
}
