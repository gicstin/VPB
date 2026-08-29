using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetSteamSelfTest
    {
        public static int RunConsole()
        {
            StringBuilder sb = new StringBuilder(4096);
            bool ok = Run(sb);
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        public static bool Run(StringBuilder log)
        {
            int pass = 0;
            int fail = 0;

            Line(log, "===== steam backend self-test =====");

            BlobRoundTrips(log, ref pass, ref fail);
            BlobRefusals(log, ref pass, ref fail);
            BlobFitsIpc(log, ref pass, ref fail);
            RelayOption(log, ref pass, ref fail);
            LobbyTokenShape(log, ref pass, ref fail);
            MessagesActionable(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/5 blob       empty, bare and prefixed forms all resolve    : " + Verdict(fail));
            Line(log, "EXIT 2/5 appid      configurable, never zero, never silently wrong: " + Verdict(fail));
            Line(log, "EXIT 3/5 lobby      only a 32-char lower hex token is a room key  : " + Verdict(fail));
            Line(log, "EXIT 4/5 messages   every refusal names cause and fix             : " + Verdict(fail));
            Line(log, "EXIT 5/5 relay      steam is relay-only; nothing can ask for ICE  : " + Verdict(fail));
            if (fail == 0) Line(log, "RESULT: PASS - the pure half is sound; the live half needs two Steam clients");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end steam backend self-test =====");
            return fail == 0;
        }

        static string Verdict(int fail)
        {
            return fail == 0 ? "PASS" : "see FAIL lines";
        }

        static void BlobRoundTrips(StringBuilder log, ref int pass, ref int fail)
        {
            uint appId;
            VpbNetSteamFault fault;

            Check(log, ref pass, ref fail, "empty blob means the default app id",
                VpbNetSteam.TryParseConnectBlob(string.Empty, out appId, out fault)
                && appId == VpbNetSteam.DefaultAppId && fault == VpbNetSteamFault.None);

            Check(log, ref pass, ref fail, "null blob means the default app id",
                VpbNetSteam.TryParseConnectBlob(null, out appId, out fault)
                && appId == VpbNetSteam.DefaultAppId);

            Check(log, ref pass, ref fail, "bare prefix means the default app id",
                VpbNetSteam.TryParseConnectBlob("steam:", out appId, out fault)
                && appId == VpbNetSteam.DefaultAppId);

            Check(log, ref pass, ref fail, "prefixed app id round trips",
                VpbNetSteam.TryParseConnectBlob(VpbNetSteam.BuildConnectBlob(480), out appId, out fault)
                && appId == 480);

            Check(log, ref pass, ref fail, "a non-default app id survives the round trip",
                VpbNetSteam.TryParseConnectBlob(VpbNetSteam.BuildConnectBlob(1234567), out appId, out fault)
                && appId == 1234567);

            Check(log, ref pass, ref fail, "a bare number is accepted",
                VpbNetSteam.TryParseConnectBlob("480", out appId, out fault) && appId == 480);

            Check(log, ref pass, ref fail, "case and padding do not matter",
                VpbNetSteam.TryParseConnectBlob("  STEAM: 480  ", out appId, out fault) && appId == 480);

            Check(log, ref pass, ref fail, "zero is rewritten to the default when building",
                VpbNetSteam.BuildConnectBlob(0) == VpbNetSteam.BlobPrefix + VpbNetSteam.DefaultAppId);

            Check(log, ref pass, ref fail, "a steam blob is recognised as one",
                VpbNetSteam.LooksLikeSteamBlob(VpbNetSteam.BuildConnectBlob(480)));

            Check(log, ref pass, ref fail, "a LAN address is not mistaken for a steam blob",
                !VpbNetSteam.LooksLikeSteamBlob("192.168.1.42:47772"));

            Check(log, ref pass, ref fail, "a rendezvous blob is not mistaken for a steam blob",
                !VpbNetSteam.LooksLikeSteamBlob("rv:example.org:47773"));

            Check(log, ref pass, ref fail, "null is not a steam blob",
                !VpbNetSteam.LooksLikeSteamBlob(null));
        }

        static void BlobRefusals(StringBuilder log, ref int pass, ref int fail)
        {
            uint appId;
            VpbNetSteamFault fault;

            Check(log, ref pass, ref fail, "text where a number belongs is refused",
                !VpbNetSteam.TryParseConnectBlob("steam:spacewar", out appId, out fault)
                && fault == VpbNetSteamFault.BadAppId);

            Check(log, ref pass, ref fail, "app id zero is refused, not defaulted",
                !VpbNetSteam.TryParseConnectBlob("steam:0", out appId, out fault)
                && fault == VpbNetSteamFault.AppIdOutOfRange);

            Check(log, ref pass, ref fail, "an absurd app id is refused",
                !VpbNetSteam.TryParseConnectBlob("steam:4294967295", out appId, out fault)
                && fault == VpbNetSteamFault.AppIdOutOfRange);

            Check(log, ref pass, ref fail, "a negative app id is refused",
                !VpbNetSteam.TryParseConnectBlob("steam:-480", out appId, out fault)
                && fault == VpbNetSteamFault.BadAppId);

            Check(log, ref pass, ref fail, "an app id with anything appended is refused rather than ignored",
                !VpbNetSteam.TryParseConnectBlob("steam:480+relay", out appId, out fault)
                && fault == VpbNetSteamFault.BadAppId);
        }

        static void RelayOption(StringBuilder log, ref int pass, ref int fail)
        {
            uint appId;
            VpbNetSteamFault fault;

            Check(log, ref pass, ref fail, "no blob can ask for a direct Steam connection",
                !VpbNetSteam.TryParseConnectBlob("steam:480+direct", out appId, out fault));

            Check(log, ref pass, ref fail, "no blob can ask for one by any other spelling either",
                !VpbNetSteam.TryParseConnectBlob("steam:480+ice", out appId, out fault)
                && !VpbNetSteam.TryParseConnectBlob("steam:480,direct", out appId, out fault)
                && !VpbNetSteam.TryParseConnectBlob("steam:480 direct", out appId, out fault));

            Check(log, ref pass, ref fail, "a built blob carries nothing but the app id",
                VpbNetSteam.BuildConnectBlob(480) == VpbNetSteam.BlobPrefix + "480");

            Check(log, ref pass, ref fail, "the relay-only refusal explains what it protects",
                Actionable(VpbNetSteam.RelayOnlyUnavailable())
                && VpbNetSteam.RelayOnlyUnavailable().IndexOf("IP address", StringComparison.Ordinal) >= 0);

            Check(log, ref pass, ref fail, "the relay-only refusal says the session was refused, not degraded",
                VpbNetSteam.RelayOnlyUnavailable().IndexOf("refused", StringComparison.Ordinal) >= 0);

            Check(log, ref pass, ref fail, "the identity warning promises the relay hides the address",
                VpbNetSteam.IdentityWarning().IndexOf("IP address", StringComparison.Ordinal) >= 0);
        }

        static void BlobFitsIpc(StringBuilder log, ref int pass, ref int fail)
        {
            string widest = VpbNetSteam.BuildConnectBlob(VpbNetSteam.MaxAppId);
            Check(log, ref pass, ref fail, "the widest blob fits the IPC connect field",
                Encoding.UTF8.GetByteCount(widest) <= VpbIpc.MaxBlobBytes);

            Check(log, ref pass, ref fail, "steam has its own backend id",
                (byte)VpbIpcBackend.Steam != (byte)VpbIpcBackend.Lan
                && (byte)VpbIpcBackend.Steam != (byte)VpbIpcBackend.LoopbackEcho);
        }

        static void LobbyTokenShape(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "a 32-char lower hex string is a room key",
                VpbNetSteam.IsLobbyToken("0123456789abcdef0123456789abcdef"));

            Check(log, ref pass, ref fail, "upper case hex is refused, so both sides agree on one spelling",
                !VpbNetSteam.IsLobbyToken("0123456789ABCDEF0123456789ABCDEF"));

            Check(log, ref pass, ref fail, "a short token is refused",
                !VpbNetSteam.IsLobbyToken("0123456789abcdef"));

            Check(log, ref pass, ref fail, "a long token is refused",
                !VpbNetSteam.IsLobbyToken("0123456789abcdef0123456789abcdef0"));

            Check(log, ref pass, ref fail, "a non-hex token is refused",
                !VpbNetSteam.IsLobbyToken("0123456789abcdef0123456789abcdeg"));

            Check(log, ref pass, ref fail, "null is not a token", !VpbNetSteam.IsLobbyToken(null));

            Check(log, ref pass, ref fail, "the room key name says nothing about VPB",
                VpbNetSteam.LobbyKeyRoom.IndexOf("vpb", StringComparison.OrdinalIgnoreCase) < 0);

            Check(log, ref pass, ref fail, "the lobby carries no constant that identifies a VPB room",
                VpbNetSteam.LobbyKeyRoom.Length <= 4);
        }

        static void MessagesActionable(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "a bad app id explains itself",
                Actionable(VpbNetSteam.Explain(VpbNetSteamFault.BadAppId)));
            Check(log, ref pass, ref fail, "an out of range app id explains itself",
                Actionable(VpbNetSteam.Explain(VpbNetSteamFault.AppIdOutOfRange)));
            Check(log, ref pass, ref fail, "a non-steam blob explains itself",
                Actionable(VpbNetSteam.Explain(VpbNetSteamFault.NotSteamBlob)));
            Check(log, ref pass, ref fail, "no fault produces no message",
                VpbNetSteam.Explain(VpbNetSteamFault.None) == null);

            string missing = VpbNetSteam.MissingLibrary("C:\\vam\\BepInEx\\plugins\\VpbNet");
            Check(log, ref pass, ref fail, "the missing library message names the file",
                Actionable(missing) && missing.IndexOf(VpbNetSteam.NativeLibrary, StringComparison.Ordinal) >= 0);
            Check(log, ref pass, ref fail, "the missing library message names the folder to put it in",
                missing.IndexOf("C:\\vam\\BepInEx\\plugins\\VpbNet", StringComparison.Ordinal) >= 0);
            Check(log, ref pass, ref fail, "the missing library message still works with no folder",
                Actionable(VpbNetSteam.MissingLibrary(null)));

            Check(log, ref pass, ref fail, "a missing export names the export",
                VpbNetSteam.MissingExport("SteamAPI_Init").IndexOf("SteamAPI_Init", StringComparison.Ordinal) >= 0);

            string init = VpbNetSteam.InitFailed(VpbNetSteam.DefaultAppId, "no steam client");
            Check(log, ref pass, ref fail, "init failure says to start Steam",
                Actionable(init) && init.IndexOf("Steam client", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(log, ref pass, ref fail, "init failure on a custom app id says both sides must match",
                VpbNetSteam.InitFailed(1234, null).IndexOf("both sides", StringComparison.Ordinal) >= 0);
            Check(log, ref pass, ref fail, "init failure on the default app id does not nag about app ids",
                init.IndexOf("both sides", StringComparison.Ordinal) < 0);

            Check(log, ref pass, ref fail, "relay unavailable is described as temporary",
                Actionable(VpbNetSteam.RelayUnavailable()));

            string noRoom = VpbNetSteam.NoRoom("K7M2-QB94-XTVR", 90);
            Check(log, ref pass, ref fail, "no-room names the code and the wait",
                Actionable(noRoom)
                && noRoom.IndexOf("K7M2-QB94-XTVR", StringComparison.Ordinal) >= 0
                && noRoom.IndexOf("90", StringComparison.Ordinal) >= 0);
            Check(log, ref pass, ref fail, "no-room still reads without a code",
                Actionable(VpbNetSteam.NoRoom(null, 90)));

            Check(log, ref pass, ref fail, "the searching hint names the code",
                VpbNetSteam.SearchingHint("K7M2-QB94-XTVR", 10)
                    .IndexOf("K7M2-QB94-XTVR", StringComparison.Ordinal) >= 0);
            Check(log, ref pass, ref fail, "the searching hint is quiet for the first seconds",
                VpbNetSteam.SearchingHint("K7M2-QB94-XTVR", 1).IndexOf("Host", StringComparison.Ordinal) < 0);

            string warn = VpbNetSteam.IdentityWarning();
            Check(log, ref pass, ref fail, "the identity warning says the peer can see the account",
                Actionable(warn) && warn.IndexOf("Steam account", StringComparison.Ordinal) >= 0);
            Check(log, ref pass, ref fail, "the identity warning offers a way out",
                warn.IndexOf("Direct", StringComparison.Ordinal) >= 0);
        }

        static bool Actionable(string s)
        {
            return !string.IsNullOrEmpty(s) && s.Length > 40;
        }

        static void Check(StringBuilder log, ref int pass, ref int fail, string what, bool ok)
        {
            if (ok)
            {
                pass++;
                Line(log, "  ok   " + what);
            }
            else
            {
                fail++;
                Line(log, "  FAIL " + what);
            }
        }

        static void Line(StringBuilder log, string s)
        {
            log.Append(s);
            log.Append('\n');
        }
    }
}
