using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VpbNet.Transport.Steam
{
    public enum SteamLobbyType
    {
        Private = 0,
        FriendsOnly = 1,
        Public = 2,
        Invisible = 3
    }

    public enum SteamLobbyDistance
    {
        Close = 0,
        Default = 1,
        Far = 2,
        Worldwide = 3
    }

    public enum SteamAvailability
    {
        CannotTry = -102,
        Failed = -101,
        Previously = -100,
        Retrying = -10,
        NeverTried = 1,
        Waiting = 2,
        Attempting = 3,
        Current = 100,
        Unknown = 0
    }

    public static class SteamNative
    {
        public const int CallbackLobbyEnter = 504;
        public const int CallbackLobbyMatchList = 510;
        public const int CallbackLobbyCreated = 513;
        public const int CallbackApiCallCompleted = 703;
        public const int CallbackMessagesSessionRequest = 1251;
        public const int CallbackMessagesSessionFailed = 1252;

        public const int ComparisonEqual = 0;
        public const int ResultOk = 1;

        public const int ConfigP2PTransportIceEnable = 105;
        public const int IceDisable = 0;

        public const int SendUnreliable = 0;
        public const int SendNoNagle = 1;
        public const int SendNoDelay = 4;
        public const int SendReliable = 8;

        public const int IdentityBytes = 136;
        public const int CallbackMsgBytes = 24;

        const int InitResultOk = 0;
        const int InitErrorBytes = 1024;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int DInitFlat(IntPtr errMsg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool DInit();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void DVoid();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int DGetPipe();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void DPipe(int pipe);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool DGetNextCallback(int pipe, IntPtr msg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool DGetApiCallResult(int pipe, ulong call, IntPtr buffer, int bufferBytes,
            int expectedCallback, [MarshalAs(UnmanagedType.I1)] out bool failed);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr DIface();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate ulong DSelfToU64(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate ulong DCreateLobby(IntPtr self, int lobbyType, int maxMembers);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool DSetLobbyData(IntPtr self, ulong lobby,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr DGetLobbyData(IntPtr self, ulong lobby,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool DSetLobbyJoinable(IntPtr self, ulong lobby, [MarshalAs(UnmanagedType.I1)] bool joinable);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool DSetLobbyType(IntPtr self, ulong lobby, int lobbyType);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool DSetGlobalConfigInt32(IntPtr self, int configValue, int value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void DLeaveLobby(IntPtr self, ulong lobby);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void DAddStringFilter(IntPtr self,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
            int comparison);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void DAddIntFilter(IntPtr self, int value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate ulong DRequestLobbyList(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate ulong DLobbyByIndex(IntPtr self, int index);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate ulong DLobbyToU64(IntPtr self, ulong lobby);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int DLobbyToInt(IntPtr self, ulong lobby);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate ulong DLobbyMemberByIndex(IntPtr self, ulong lobby, int index);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void DInitRelay(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int DRelayStatus(IntPtr self, IntPtr details);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int DSendMessageToUser(IntPtr self, IntPtr identity, IntPtr data, uint dataBytes,
            int sendFlags, int remoteChannel);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int DReceiveMessages(IntPtr self, int localChannel, IntPtr outMessages, int maxMessages);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool DIdentityOp(IntPtr self, IntPtr identity);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void DIdentityClear(IntPtr identity);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void DIdentitySetSteamId(IntPtr identity, ulong steamId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate ulong DIdentityGetSteamId(IntPtr identity);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void DReleaseMessage(IntPtr message);

        static IntPtr _module;
        static DInitFlat _initFlat;
        static DInit _init;
        static DVoid _shutdown;
        static DGetPipe _getPipe;
        static DVoid _dispatchInit;
        static DPipe _runFrame;
        static DGetNextCallback _nextCallback;
        static DPipe _freeLastCallback;
        static DGetApiCallResult _apiCallResult;

        static DSelfToU64 _userGetSteamId;

        static DCreateLobby _createLobby;
        static DSetLobbyData _setLobbyData;
        static DGetLobbyData _getLobbyData;
        static DSetLobbyJoinable _setLobbyJoinable;
        static DSetLobbyType _setLobbyType;
        static DLeaveLobby _leaveLobby;
        static DAddStringFilter _addStringFilter;
        static DAddIntFilter _addDistanceFilter;
        static DAddIntFilter _addResultCountFilter;
        static DRequestLobbyList _requestLobbyList;
        static DLobbyByIndex _lobbyByIndex;
        static DLobbyToU64 _joinLobby;
        static DLobbyToU64 _lobbyOwner;
        static DLobbyToInt _numLobbyMembers;
        static DLobbyMemberByIndex _lobbyMemberByIndex;

        static DInitRelay _initRelay;
        static DRelayStatus _relayStatus;
        static DSetGlobalConfigInt32 _setGlobalConfigInt32;

        static DSendMessageToUser _sendMessage;
        static DReceiveMessages _receiveMessages;
        static DIdentityOp _acceptSession;
        static DIdentityOp _closeSession;

        static DIdentityClear _identityClear;
        static DIdentitySetSteamId _identitySet;
        static DIdentityGetSteamId _identityGet;
        static DReleaseMessage _releaseMessage;

        static IntPtr _matchmaking;
        static IntPtr _user;
        static IntPtr _messages;
        static IntPtr _utils;

        static int _pipe;
        static bool _started;
        static uint _startedAppId;

        public static bool Started { get { return _started; } }

        public static string LibraryPath { get; private set; }

        public static string SearchedFolder { get; private set; }

        public static string FindLibrary(string explicitPath, string brokerDir)
        {
            SearchedFolder = brokerDir;
            return VpbNetSteam.FindLibrary(explicitPath, brokerDir);
        }

        public static bool Load(string path, out string error)
        {
            error = null;
            if (_module != IntPtr.Zero) return true;

            try
            {
                _module = NativeLibrary.Load(path);
            }
            catch (Exception e)
            {
                error = "could not load " + path + ": " + e.Message
                    + ". A 32-bit copy will fail this way; the broker needs the 64-bit " + VpbNetSteam.NativeLibrary + ".";
                return false;
            }

            LibraryPath = path;
            string missing = Bind();
            if (missing != null)
            {
                error = VpbNetSteam.MissingExport(missing);
                Unload();
                return false;
            }
            return true;
        }

        static string Bind()
        {
            string missing;

            _initFlat = Optional<DInitFlat>("SteamAPI_InitFlat");
            _init = Optional<DInit>("SteamAPI_Init");
            if (_initFlat == null && _init == null) return "SteamAPI_InitFlat or SteamAPI_Init";

            if (!Bind(out _shutdown, "SteamAPI_Shutdown", out missing)) return missing;
            if (!Bind(out _getPipe, "SteamAPI_GetHSteamPipe", out missing)) return missing;
            if (!Bind(out _dispatchInit, "SteamAPI_ManualDispatch_Init", out missing)) return missing;
            if (!Bind(out _runFrame, "SteamAPI_ManualDispatch_RunFrame", out missing)) return missing;
            if (!Bind(out _nextCallback, "SteamAPI_ManualDispatch_GetNextCallback", out missing)) return missing;
            if (!Bind(out _freeLastCallback, "SteamAPI_ManualDispatch_FreeLastCallback", out missing)) return missing;
            if (!Bind(out _apiCallResult, "SteamAPI_ManualDispatch_GetAPICallResult", out missing)) return missing;

            if (!Bind(out _userGetSteamId, "SteamAPI_ISteamUser_GetSteamID", out missing)) return missing;

            if (!Bind(out _createLobby, "SteamAPI_ISteamMatchmaking_CreateLobby", out missing)) return missing;
            if (!Bind(out _setLobbyData, "SteamAPI_ISteamMatchmaking_SetLobbyData", out missing)) return missing;
            if (!Bind(out _getLobbyData, "SteamAPI_ISteamMatchmaking_GetLobbyData", out missing)) return missing;
            if (!Bind(out _setLobbyJoinable, "SteamAPI_ISteamMatchmaking_SetLobbyJoinable", out missing)) return missing;
            if (!Bind(out _setLobbyType, "SteamAPI_ISteamMatchmaking_SetLobbyType", out missing)) return missing;
            if (!Bind(out _leaveLobby, "SteamAPI_ISteamMatchmaking_LeaveLobby", out missing)) return missing;
            if (!Bind(out _addStringFilter, "SteamAPI_ISteamMatchmaking_AddRequestLobbyListStringFilter", out missing)) return missing;
            if (!Bind(out _addDistanceFilter, "SteamAPI_ISteamMatchmaking_AddRequestLobbyListDistanceFilter", out missing)) return missing;
            if (!Bind(out _addResultCountFilter, "SteamAPI_ISteamMatchmaking_AddRequestLobbyListResultCountFilter", out missing)) return missing;
            if (!Bind(out _requestLobbyList, "SteamAPI_ISteamMatchmaking_RequestLobbyList", out missing)) return missing;
            if (!Bind(out _lobbyByIndex, "SteamAPI_ISteamMatchmaking_GetLobbyByIndex", out missing)) return missing;
            if (!Bind(out _joinLobby, "SteamAPI_ISteamMatchmaking_JoinLobby", out missing)) return missing;
            if (!Bind(out _lobbyOwner, "SteamAPI_ISteamMatchmaking_GetLobbyOwner", out missing)) return missing;
            if (!Bind(out _numLobbyMembers, "SteamAPI_ISteamMatchmaking_GetNumLobbyMembers", out missing)) return missing;
            if (!Bind(out _lobbyMemberByIndex, "SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex", out missing)) return missing;

            if (!Bind(out _initRelay, "SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess", out missing)) return missing;
            if (!Bind(out _relayStatus, "SteamAPI_ISteamNetworkingUtils_GetRelayNetworkStatus", out missing)) return missing;
            _setGlobalConfigInt32 = Optional<DSetGlobalConfigInt32>("SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueInt32");

            if (!Bind(out _sendMessage, "SteamAPI_ISteamNetworkingMessages_SendMessageToUser", out missing)) return missing;
            if (!Bind(out _receiveMessages, "SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel", out missing)) return missing;
            if (!Bind(out _acceptSession, "SteamAPI_ISteamNetworkingMessages_AcceptSessionWithUser", out missing)) return missing;
            if (!Bind(out _closeSession, "SteamAPI_ISteamNetworkingMessages_CloseSessionWithUser", out missing)) return missing;

            if (!Bind(out _identityClear, "SteamAPI_SteamNetworkingIdentity_Clear", out missing)) return missing;
            if (!Bind(out _identitySet, "SteamAPI_SteamNetworkingIdentity_SetSteamID64", out missing)) return missing;
            if (!Bind(out _identityGet, "SteamAPI_SteamNetworkingIdentity_GetSteamID64", out missing)) return missing;
            if (!Bind(out _releaseMessage, "SteamAPI_SteamNetworkingMessage_t_Release", out missing)) return missing;

            return null;
        }

        static bool Bind<T>(out T target, string name, out string missing) where T : class
        {
            target = Optional<T>(name);
            missing = target == null ? name : null;
            return target != null;
        }

        static T Optional<T>(string name) where T : class
        {
            IntPtr fn;
            if (!NativeLibrary.TryGetExport(_module, name, out fn) || fn == IntPtr.Zero) return null;
            try { return Marshal.GetDelegateForFunctionPointer(fn, typeof(T)) as T; }
            catch { return null; }
        }

        static IntPtr Interface(string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                DIface fn = Optional<DIface>(names[i]);
                if (fn == null) continue;
                IntPtr p;
                try { p = fn(); }
                catch { continue; }
                if (p != IntPtr.Zero) return p;
            }
            return IntPtr.Zero;
        }

        public static bool Start(uint appId, string brokerDir, out string error)
        {
            error = null;
            if (_started)
            {
                if (appId == _startedAppId) return true;
                error = "this session asks for Steam app id " + appId
                    + ", but Steam was already started in this process as " + _startedAppId
                    + ". Steam cannot be re-initialised inside a running process."
                    + " Close VaM and start it again for the new app id to take effect.";
                return false;
            }
            if (_module == IntPtr.Zero)
            {
                error = "the Steam library was never loaded";
                return false;
            }

            string id = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Environment.SetEnvironmentVariable("SteamAppId", id);
            Environment.SetEnvironmentVariable("SteamGameId", id);

            string detail;
            if (!CallInit(out detail))
            {
                if (!RetryWithAppIdFile(appId, brokerDir, out detail))
                {
                    error = VpbNetSteam.InitFailed(appId, detail);
                    return false;
                }
            }

            _started = true;
            _startedAppId = appId;

            try { _dispatchInit(); }
            catch { }

            try { _pipe = _getPipe(); }
            catch { _pipe = 0; }

            _matchmaking = Interface(new string[] { "SteamAPI_SteamMatchmaking_v009" });
            _user = Interface(new string[]
            {
                "SteamAPI_SteamUser_v023", "SteamAPI_SteamUser_v022",
                "SteamAPI_SteamUser_v021", "SteamAPI_SteamUser_v020"
            });
            _messages = Interface(new string[]
            {
                "SteamAPI_SteamNetworkingMessages_SteamAPI_v002",
                "SteamAPI_SteamNetworkingMessages_SteamAPI_v001"
            });
            _utils = Interface(new string[]
            {
                "SteamAPI_SteamNetworkingUtils_SteamAPI_v004",
                "SteamAPI_SteamNetworkingUtils_SteamAPI_v003"
            });

            if (_matchmaking == IntPtr.Zero) { Stop(); error = VpbNetSteam.MissingExport("SteamAPI_SteamMatchmaking_v009"); return false; }
            if (_user == IntPtr.Zero) { Stop(); error = VpbNetSteam.MissingExport("SteamAPI_SteamUser_v0xx"); return false; }
            if (_messages == IntPtr.Zero) { Stop(); error = VpbNetSteam.MissingExport("SteamAPI_SteamNetworkingMessages_SteamAPI_v002"); return false; }
            if (_utils == IntPtr.Zero) { Stop(); error = VpbNetSteam.MissingExport("SteamAPI_SteamNetworkingUtils_SteamAPI_v004"); return false; }

            try { _initRelay(_utils); }
            catch { }

            return true;
        }

        static bool CallInit(out string detail)
        {
            detail = null;
            if (_initFlat != null)
            {
                IntPtr buf = Marshal.AllocHGlobal(InitErrorBytes);
                try
                {
                    for (int i = 0; i < 8; i++) Marshal.WriteByte(buf, i, 0);
                    int r = _initFlat(buf);
                    if (r == InitResultOk) return true;
                    string text = Marshal.PtrToStringAnsi(buf);
                    detail = string.IsNullOrEmpty(text) ? "result " + r : text;
                    return false;
                }
                catch (Exception e)
                {
                    detail = e.Message;
                    return false;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }

            try
            {
                if (_init()) return true;
                detail = "SteamAPI_Init said no";
                return false;
            }
            catch (Exception e)
            {
                detail = e.Message;
                return false;
            }
        }

        static bool RetryWithAppIdFile(uint appId, string brokerDir, out string detail)
        {
            detail = null;
            if (string.IsNullOrEmpty(brokerDir)) return false;

            string previous = null;
            try
            {
                string file = Path.Combine(brokerDir, "steam_appid.txt");
                File.WriteAllText(file, appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                previous = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(brokerDir);
            }
            catch
            {
                return false;
            }

            try { return CallInit(out detail); }
            finally
            {
                try { if (previous != null) Directory.SetCurrentDirectory(previous); }
                catch { }
            }
        }

        public static void Stop()
        {
            if (_started)
            {
                try { _shutdown(); }
                catch { }
            }
            _started = false;
            _startedAppId = 0;
            _matchmaking = IntPtr.Zero;
            _user = IntPtr.Zero;
            _messages = IntPtr.Zero;
            _utils = IntPtr.Zero;
            _pipe = 0;
        }

        static void Unload()
        {
            if (_module == IntPtr.Zero) return;
            try { NativeLibrary.Free(_module); }
            catch { }
            _module = IntPtr.Zero;
            LibraryPath = null;
        }

        public static void RunFrame(IntPtr callbackScratch, Action<int, IntPtr, int> onCallback)
        {
            if (!_started || _pipe == 0) return;

            try { _runFrame(_pipe); }
            catch { return; }

            while (true)
            {
                bool got;
                try { got = _nextCallback(_pipe, callbackScratch); }
                catch { return; }
                if (!got) return;

                int callback = Marshal.ReadInt32(callbackScratch, 4);
                IntPtr param = Marshal.ReadIntPtr(callbackScratch, 8);
                int paramBytes = Marshal.ReadInt32(callbackScratch, 16);

                try { onCallback(callback, param, paramBytes); }
                catch { }

                try { _freeLastCallback(_pipe); }
                catch { return; }
            }
        }

        public static bool GetApiCallResult(ulong call, IntPtr buffer, int bufferBytes, int expected, out bool failed)
        {
            failed = true;
            if (!_started || _pipe == 0) return false;
            try { return _apiCallResult(_pipe, call, buffer, bufferBytes, expected, out failed); }
            catch { return false; }
        }

        public static ulong SelfSteamId()
        {
            try { return _userGetSteamId(_user); }
            catch { return 0; }
        }

        public static ulong CreateLobby(SteamLobbyType type, int maxMembers)
        {
            try { return _createLobby(_matchmaking, (int)type, maxMembers); }
            catch { return 0; }
        }

        public static bool SetLobbyData(ulong lobby, string key, string value)
        {
            try { return _setLobbyData(_matchmaking, lobby, key, value); }
            catch { return false; }
        }

        public static string GetLobbyData(ulong lobby, string key)
        {
            try
            {
                IntPtr p = _getLobbyData(_matchmaking, lobby, key);
                if (p == IntPtr.Zero) return string.Empty;
                string s = Marshal.PtrToStringUTF8(p);
                return s == null ? string.Empty : s;
            }
            catch { return string.Empty; }
        }

        public static void SetLobbyJoinable(ulong lobby, bool joinable)
        {
            try { _setLobbyJoinable(_matchmaking, lobby, joinable); }
            catch { }
        }

        public static void SetLobbyType(ulong lobby, SteamLobbyType type)
        {
            try { _setLobbyType(_matchmaking, lobby, (int)type); }
            catch { }
        }

        public static bool CanSetConfig
        {
            get { return _setGlobalConfigInt32 != null; }
        }

        public static bool EnableRelayOnly()
        {
            if (_setGlobalConfigInt32 == null) return false;
            try { return _setGlobalConfigInt32(_utils, ConfigP2PTransportIceEnable, IceDisable); }
            catch { return false; }
        }

        public static void LeaveLobby(ulong lobby)
        {
            try { _leaveLobby(_matchmaking, lobby); }
            catch { }
        }

        public static ulong RequestLobbyList(string key, string value, int maxResults)
        {
            try
            {
                _addStringFilter(_matchmaking, key, value, ComparisonEqual);
                _addDistanceFilter(_matchmaking, (int)SteamLobbyDistance.Worldwide);
                _addResultCountFilter(_matchmaking, maxResults);
                return _requestLobbyList(_matchmaking);
            }
            catch { return 0; }
        }

        public static ulong LobbyByIndex(int index)
        {
            try { return _lobbyByIndex(_matchmaking, index); }
            catch { return 0; }
        }

        public static ulong JoinLobby(ulong lobby)
        {
            try { return _joinLobby(_matchmaking, lobby); }
            catch { return 0; }
        }

        public static ulong LobbyOwner(ulong lobby)
        {
            try { return _lobbyOwner(_matchmaking, lobby); }
            catch { return 0; }
        }

        public static int LobbyMemberCount(ulong lobby)
        {
            try { return _numLobbyMembers(_matchmaking, lobby); }
            catch { return 0; }
        }

        public static ulong LobbyMember(ulong lobby, int index)
        {
            try { return _lobbyMemberByIndex(_matchmaking, lobby, index); }
            catch { return 0; }
        }

        public static SteamAvailability RelayStatus()
        {
            try { return (SteamAvailability)_relayStatus(_utils, IntPtr.Zero); }
            catch { return SteamAvailability.Unknown; }
        }

        public static int SendToUser(IntPtr identity, IntPtr data, int dataBytes, int flags, int channel)
        {
            try { return _sendMessage(_messages, identity, data, (uint)dataBytes, flags, channel); }
            catch { return 0; }
        }

        public static int ReceiveMessages(int channel, IntPtr outMessages, int maxMessages)
        {
            try { return _receiveMessages(_messages, channel, outMessages, maxMessages); }
            catch { return 0; }
        }

        public static bool AcceptSession(IntPtr identity)
        {
            try { return _acceptSession(_messages, identity); }
            catch { return false; }
        }

        public static void CloseSession(IntPtr identity)
        {
            try { _closeSession(_messages, identity); }
            catch { }
        }

        public static IntPtr AllocIdentity(ulong steamId)
        {
            IntPtr p = Marshal.AllocHGlobal(IdentityBytes);
            for (int i = 0; i < IdentityBytes; i++) Marshal.WriteByte(p, i, 0);
            try
            {
                _identityClear(p);
                if (steamId != 0) _identitySet(p, steamId);
            }
            catch { }
            return p;
        }

        public static void SetIdentity(IntPtr identity, ulong steamId)
        {
            try
            {
                _identityClear(identity);
                _identitySet(identity, steamId);
            }
            catch { }
        }

        public static ulong IdentitySteamId(IntPtr identity)
        {
            try { return _identityGet(identity); }
            catch { return 0; }
        }

        public static void ReleaseMessage(IntPtr message)
        {
            try { _releaseMessage(message); }
            catch { }
        }
    }
}
