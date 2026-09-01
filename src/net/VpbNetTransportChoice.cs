using System;
using VpbNet;

namespace VPB
{
    public static class VpbNetTransportChoice
    {
        public const string Direct = "direct";
        public const string Steam = "steam";

        public static string Current()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null || s.NetTransport == null) return Steam;
                return Normalize(s.NetTransport.Value);
            }
            catch { return Steam; }
        }

        public static string Normalize(string raw)
        {
            if (raw == null) return Steam;
            string t = raw.Trim();
            if (t.Length == 0) return Steam;
            if (string.Equals(t, Direct, StringComparison.OrdinalIgnoreCase)) return Direct;
            return Steam;
        }

        public static bool IsSteam()
        {
            return Current() == Steam;
        }

        public static void Set(string choice)
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null || s.NetTransport == null) return;
                s.NetTransport.Value = Normalize(choice);
            }
            catch { }
        }

        public static VpbIpcBackend Backend()
        {
            return IsSteam() ? VpbIpcBackend.Steam : VpbIpcBackend.Lan;
        }

        public static uint AppId()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null || s.NetSteamAppId == null) return VpbNetSteam.DefaultAppId;
                int v = s.NetSteamAppId.Value;
                if (v <= 0 || (long)v > VpbNetSteam.MaxAppId) return VpbNetSteam.DefaultAppId;
                return (uint)v;
            }
            catch { return VpbNetSteam.DefaultAppId; }
        }

        public static string ConnectBlob()
        {
            return VpbNetSteam.BuildConnectBlob(AppId());
        }

        public static string ApiPathHint()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null || s.NetSteamApiPath == null) return string.Empty;
                string v = s.NetSteamApiPath.Value;
                return v == null ? string.Empty : v.Trim();
            }
            catch { return string.Empty; }
        }

        const int LibraryCacheMs = 2000;

        static string _libraryPath;
        static string _libraryFor;
        static int _libraryCheckedAt;
        static bool _libraryChecked;

        public static string LibraryPath()
        {
            string hint = ApiPathHint();
            int now = Environment.TickCount;
            if (_libraryChecked
                && string.Equals(_libraryFor, hint, StringComparison.Ordinal)
                && (uint)(now - _libraryCheckedAt) < LibraryCacheMs)
                return _libraryPath;

            _libraryChecked = true;
            _libraryFor = hint;
            _libraryCheckedAt = now;
            try { _libraryPath = VpbNetSteam.FindLibrary(hint, VpbNetBrokerLink.BrokerDirectory); }
            catch { _libraryPath = null; }
            return _libraryPath;
        }

        public static void ForgetLibrary()
        {
            _libraryChecked = false;
            _libraryPath = null;
        }

        public static bool LibraryPresent()
        {
            return !string.IsNullOrEmpty(LibraryPath());
        }

        public static string MissingLibraryMessage()
        {
            string folder = null;
            try { folder = VpbNetBrokerLink.BrokerDirectory; }
            catch { }
            return VpbNetSteam.MissingLibrary(folder);
        }

        public static bool IdentityAcknowledged()
        {
            try
            {
                Settings s = Settings.Instance;
                return s != null && s.NetSteamIdentityAck != null && s.NetSteamIdentityAck.Value;
            }
            catch { return false; }
        }

        public static void AcknowledgeIdentity()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetSteamIdentityAck != null) s.NetSteamIdentityAck.Value = true;
            }
            catch { }
        }

        public static bool DirectIpAcknowledged()
        {
            try
            {
                Settings s = Settings.Instance;
                return s != null && s.NetDirectIpAck != null && s.NetDirectIpAck.Value;
            }
            catch { return false; }
        }

        public static void AcknowledgeDirectIp()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetDirectIpAck != null) s.NetDirectIpAck.Value = true;
            }
            catch { }
        }

        public static string DirectIpWarning()
        {
            return VPBTranslation.T("net_session.direct_ip.warn",
                "No privacy. Direct connects the two machines to each other, so each of you sees the other's IP address. There is no relay and nothing hides the address. Use Steam if you do not want to share it.");
        }

        public static bool ReadyToConnect()
        {
            return BlockedReason() == null;
        }

        public static string BlockedReason()
        {
            if (IsSteam())
            {
                if (!IdentityAcknowledged()) return VpbNetSteam.IdentityWarning();
                if (!LibraryPresent()) return MissingLibraryMessage();
                return null;
            }
            if (!DirectIpAcknowledged()) return DirectIpWarning();
            return null;
        }
    }
}
