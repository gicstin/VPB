using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using VpbNet;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void AppendDevSettingDefinitions(List<InternalSettingDefinition> defs)
        {
            Settings s = null;
            try { s = Settings.Instance; }
            catch { }
            if (s == null) return;

            AppendDevNetSettings(defs, s);
            AppendDevClipSettings(defs, s);
        }

        private void AppendDevNetSettings(List<InternalSettingDefinition> defs, Settings s)
        {
            AddDevToggle(defs, s.NetEnabled, "dev.net.enabled", "dev_net",
                VPBTranslation.T("settings.dev.net_enabled", "Multiplayer enabled (kill switch)"),
                VPBTranslation.T("settings.tip.dev.net_enabled",
                    "Master switch. While this is off, the broker process VpbNet.exe can never be launched by anything and multiplayer is entirely absent. Even when on, nothing starts until a session or a test asks for it."));

            InternalSettingDefinition host = new InternalSettingDefinition
            {
                Key = "dev.net.host_session",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.host_session", "Host LAN session"),
                Tooltip = VPBTranslation.T("settings.tip.dev.host_session",
                    "Start a pose session as host. Needs two Person atoms in the scene (you + the remote puppet). The joiner uses the address printed to the log and shown on the session panel. Uses the room code below. Turns Join off. Turn both off to leave."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => DevBool(s.NetHostSession),
                SetBool = v =>
                {
                    DevWriteBool(s.NetHostSession, v);
                    if (v)
                    {
                        DevWriteBool(s.NetJoinSession, false);
                        DevWriteBool(s.NetSessionUi, true);
                    }
                },
                RowVisible = () => DevNetEnabled()
            };
            host.SetDefault(DevDefaultBool(s.NetHostSession));
            defs.Add(host);

            InternalSettingDefinition join = new InternalSettingDefinition
            {
                Key = "dev.net.join_session",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.join_session", "Join LAN session"),
                Tooltip = VPBTranslation.T("settings.tip.dev.join_session",
                    "Join the host at the LAN address below, with the same room code. Needs two Person atoms. Turns Host off. Turn both off to leave."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => DevBool(s.NetJoinSession),
                SetBool = v =>
                {
                    DevWriteBool(s.NetJoinSession, v);
                    if (v)
                    {
                        DevWriteBool(s.NetHostSession, false);
                        DevWriteBool(s.NetSessionUi, true);
                    }
                },
                RowVisible = () => DevNetEnabled()
            };
            join.SetDefault(DevDefaultBool(s.NetJoinSession));
            defs.Add(join);

            AddDevToggle(defs, s.NetReopenOnStart, "dev.net.reopen_on_start", "dev_net",
                VPBTranslation.T("settings.dev.net_reopen_on_start", "Reopen my room on next launch"),
                VPBTranslation.T("settings.tip.dev.net_reopen_on_start",
                    "Off (default): closing the game always shuts the room, and only your saved room codes come back — a crash mid-session cannot put you back on the network without asking. On: if you were still hosting or joined when the game closed, the same room opens again by itself at startup. The session panel stays as you left it either way."),
                () => DevNetEnabled());

            AddDevToggle(defs, s.NetSessionUi, "dev.net.session_ui", "dev_net",
                VPBTranslation.T("settings.dev.session_ui", "Session panel"),
                VPBTranslation.T("settings.tip.dev.session_ui",
                    "Floating Play with a friend window. Also opens when you host or join. A live session collapses to a HUD instead of closing; Leave on the panel ends it. Permission asks appear as their own prompt."),
                () => DevNetEnabled());

            AddDevText(defs, s.NetLocalAtom, "dev.net.local_atom", "dev_net",
                VPBTranslation.T("settings.dev.local_atom", "Local avatar atom uid"),
                VPBTranslation.T("settings.tip.dev.local_atom",
                    "Person to ride automatically when a session starts, if the scene has one by that name. Empty starts you as a spectator; pick a person from the buttons on the session panel instead. You can change person mid-session."),
                () => DevNetEnabled());

            AddDevToggle(defs, s.NetLockRoot, "dev.net.lock_root", "dev_net",
                VPBTranslation.T("settings.dev.lock_root", "Lock remote root (skip control position)"),
                VPBTranslation.T("settings.tip.dev.lock_root",
                    "Live session. When on, remote Person keeps its scene placement and peer root motion is ignored — grabbing control on the other side will not move this Person. Off (default) syncs root so a grab or animation that moves control shows on both sides. Use ReplayLockRoot to pin clip playback instead."),
                () => DevNetEnabled());

            defs.Add(new InternalSettingDefinition
            {
                Key = "dev.pose.dual_ask_again",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.dual_pose_ask_again",
                    "Ask again which half of a two-person pose is mine"),
                Tooltip = VPBTranslation.T("settings.tip.dev.dual_pose_ask_again",
                    "You ticked \"Don't ask again this session\" in the two-person pose window, so every pose since has used that answer. This puts the question back. It also comes back on its own when you load a scene."),
                ControlType = InternalSettingControlType.Button,
                OnAction = VpbDualPoseModal.ForgetRememberedChoice,
                RowVisible = () => VpbDualPoseModal.HasRememberedChoice
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "dev.net.kick_peer",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.kick_peer", "Remove the other person (host)"),
                Tooltip = VPBTranslation.T("settings.tip.dev.kick_peer",
                    "Host only. Sends them out of the session. Close the room if you do not want them back - there are no player names to keep a list of."),
                ControlType = InternalSettingControlType.Button,
                OnAction = VpbNetPresence.KickPeer,
                RowVisible = () => DevNetEnabled()
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "dev.net.repair_bodies",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.repair_bodies", "Put every body back together now"),
                Tooltip = VPBTranslation.T("settings.tip.dev.repair_bodies",
                    "Runs the repair by hand on every person in the scene: each bone snapped back onto its controller, velocities zeroed, collisions off while they settle. The same thing the automatic guard does when it catches a body flying apart."),
                ControlType = InternalSettingControlType.Button,
                OnAction = VpbNetPresence.RepairBodies,
                RowVisible = () => DevNetEnabled()
            });

            AddDevSlider(defs, s.NetSamplerHz, "dev.net.sampler_hz", "dev_net",
                VPBTranslation.T("settings.dev.sampler_hz", "Pose rate (Hz)"),
                VPBTranslation.T("settings.tip.dev.sampler_hz",
                    "How many times a second your avatar's pose is sampled and sent. 45 is the default. Asking for more than the physics rate cannot be met and is reported as rate slips."),
                1f, 200f, 1f, null);

            AddDevToggle(defs, s.NetOverlay, "dev.net.overlay", "dev_net",
                VPBTranslation.T("settings.dev.net_overlay", "Multiplayer diagnostics overlay"),
                VPBTranslation.T("settings.tip.dev.net_overlay",
                    "A floating window showing session state, transport, RTT, jitter, loss, buffer delay and depth, frame age and sampler/applier microseconds. Drag the title bar to move it, collapse it to the title bar, or close it - position and collapsed state are remembered. Free while off or collapsed."));

            AddDevText(defs, s.NetLanAddress, "dev.net.lan_address", "dev_net",
                VPBTranslation.T("settings.dev.lan_address", "LAN address"),
                VPBTranslation.T("settings.tip.dev.lan_address",
                    "Joiner: the address:port the host printed to the log, for example 192.168.1.42:47772. Host: leave empty to bind port 47772 on every interface."),
                () => DevNetEnabled() && !VpbNetTransportChoice.IsSteam());

            InternalSettingDefinition room = new InternalSettingDefinition
            {
                Key = "dev.net.lan_room",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.lan_room", "Room code"),
                Tooltip = VPBTranslation.T("settings.tip.dev.lan_room",
                    "Selected host room, 12 characters. Joining someone else does not overwrite this. Case does not matter, hyphens and spaces are ignored. Also accepts a pasted invite in the session panel's join field, not here."),
                ControlType = InternalSettingControlType.TextArea,
                SingleLineText = true,
                GetString = () =>
                {
                    string code = VpbNetRoomBookStore.SelectedHostCode;
                    if (VpbNetRoomCode.IsWellFormed(code)) return VpbNetRoomCode.Group(code);
                    return DevReadString(s.NetLanRoomCode, "");
                },
                SetString = v =>
                {
                    if (VpbNetPresence.RoomCodeLocked) return;
                    string norm = VpbNetRoomCode.Normalize(v);
                    if (norm != null)
                    {
                        VpbNetRoomBookStore.ReplaceSelectedHost(norm);
                        return;
                    }
                    DevWriteString(s.NetLanRoomCode, v);
                },
                TextReadOnly = () => VpbNetPresence.RoomCodeLocked,
                RowVisible = () => DevNetEnabled()
            };
            room.SetDefault(DevDefaultString(s.NetLanRoomCode));
            defs.Add(room);

            defs.Add(new InternalSettingDefinition
            {
                Key = "dev.net.gen_room",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.gen_room", "Add a host room"),
                Tooltip = VPBTranslation.T("settings.tip.dev.gen_room",
                    "Adds a new 12-character host room and selects it. Old rooms stay in the list. 60 bits from the system random number generator - not something to invent by hand. Never contains I, L, O or U."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VPBTranslation.T("settings.row.generate", "GENERATE"),
                ActionEnabled = () => true,
                OnAction = VpbNetPresence.AddHostRoom,
                RowVisible = () => DevNetEnabled()
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "dev.net.lock_room",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.lock_room", "Protect the room code from being replaced"),
                Tooltip = VPBTranslation.T("settings.tip.dev.lock_room",
                    "Protects the selected host room: that code cannot be replaced, so the person waiting on it cannot be stranded. New still adds another room. The same button releases it. This protects the value, not the session - it does not stop anyone who already has the code from joining."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VpbNetPresence.RoomCodeLocked
                    ? VPBTranslation.T("settings.row.unprotect", "UNLOCK")
                    : VPBTranslation.T("settings.row.protect", "LOCK"),
                OnAction = VpbNetPresence.ToggleRoomCodeLock,
                RowVisible = () => DevNetEnabled()
            });

            InternalSettingDefinition transport = new InternalSettingDefinition
            {
                Key = "dev.net.transport",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.net_transport", "How a session connects"),
                Tooltip = VPBTranslation.T("settings.tip.dev.net_transport",
                    "\"steam\" is the default: Steam carries the connection - neither of you learns the other's IP, nothing has to be forwarded, and the room code is the only thing you exchange. What it costs is that the other player can see which Steam account you are signed into; the session panel makes you say you understand that once. \"direct\" connects the two machines to each other: you exchange an invite, and each of you learns the other's IP address - no privacy. The session panel warns you before Direct host/join cards appear. Both sides must pick the same one. Steam also needs the Steam client running and a 64-bit " + VpbNet.VpbNetSteam.NativeLibrary + " next to VpbNet.exe."),
                ControlType = InternalSettingControlType.Cycle,
                Options = new[] { VpbNetTransportChoice.Steam, VpbNetTransportChoice.Direct },
                GetString = () => VpbNetTransportChoice.Current(),
                SetString = v =>
                {
                    VpbNetTransportChoice.Set(v);
                    VpbNetTransportChoice.ForgetLibrary();
                },
                RowVisible = () => DevNetEnabled()
            };
            transport.SetDefault(DevDefaultString(s.NetTransport));
            defs.Add(transport);

            InternalSettingDefinition steamApp = new InternalSettingDefinition
            {
                Key = "dev.net.steam_appid",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.steam_appid", "Steam app id"),
                Tooltip = VPBTranslation.T("settings.tip.dev.steam_appid",
                    "The app the session identifies itself to Steam as. 480 is Spacewar, Valve's public sample app: any signed-in account can use it without owning or buying anything, which is why it is the default - and why VPB is not, and will not be, distributed on Steam. Both sides must match this exactly or they cannot see each other at all. Change it only if you and the other player both own the same Steam game and would rather use its id."),
                ControlType = InternalSettingControlType.TextArea,
                SingleLineText = true,
                GetString = () =>
                {
                    try { return s.NetSteamAppId.Value.ToString(CultureInfo.InvariantCulture); }
                    catch { return VpbNet.VpbNetSteam.DefaultAppId.ToString(CultureInfo.InvariantCulture); }
                },
                SetString = v =>
                {
                    uint parsed;
                    VpbNet.VpbNetSteamFault fault;
                    if (!VpbNet.VpbNetSteam.TryParseConnectBlob(v, out parsed, out fault))
                    {
                        LogUtil.LogError("[VPB.Net] " + VpbNet.VpbNetSteam.Explain(fault));
                        return;
                    }
                    try { s.NetSteamAppId.Value = (int)parsed; }
                    catch { }
                },
                RowVisible = () => DevNetEnabled() && VpbNetTransportChoice.IsSteam()
            };
            steamApp.SetDefault(VpbNet.VpbNetSteam.DefaultAppId.ToString(CultureInfo.InvariantCulture));
            defs.Add(steamApp);

            AddDevText(defs, s.NetSteamApiPath, "dev.net.steam_api", "dev_net",
                VPBTranslation.T("settings.dev.steam_api", "steam_api64.dll"),
                VPBTranslation.T("settings.tip.dev.steam_api",
                    "Full path to a 64-bit " + VpbNet.VpbNetSteam.NativeLibrary + ", or the folder holding one. Empty searches next to VpbNet.exe, then the plugins folder, then the VaM install folder - so a Steam build of VaM already satisfies it. The file ships with Steam games and with the Steamworks SDK. Run \"VpbNet.exe --steam-probe\" from a command prompt to see exactly which copy is found and whether Steam answers."),
                () => DevNetEnabled() && VpbNetTransportChoice.IsSteam());

            defs.Add(new InternalSettingDefinition
            {
                Key = "dev.net.steam_ack",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.steam_ack", "Steam identity warning"),
                Tooltip = VPBTranslation.T("settings.tip.dev.steam_ack",
                    "A Steam session is addressed by your Steam account, so the person you connect to can see which account you are signed into. VPB never displays it and never stores it, but that is a UI choice, not a guarantee. The session panel shows this once and will not offer the Steam buttons until you accept it. Reset shows it again."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VpbNetTransportChoice.IdentityAcknowledged()
                    ? VPBTranslation.T("settings.row.shown_again", "SHOW AGAIN")
                    : VPBTranslation.T("settings.row.not_seen", "NOT SEEN YET"),
                ActionEnabled = () => VpbNetTransportChoice.IdentityAcknowledged(),
                OnAction = () =>
                {
                    try
                    {
                        Settings st = Settings.Instance;
                        if (st != null && st.NetSteamIdentityAck != null) st.NetSteamIdentityAck.Value = false;
                    }
                    catch { }
                },
                RowVisible = () => DevNetEnabled() && VpbNetTransportChoice.IsSteam()
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "dev.net.direct_ack",
                GroupKey = "dev_net",
                Label = VPBTranslation.T("settings.dev.direct_ack", "Direct IP warning"),
                Tooltip = VPBTranslation.T("settings.tip.dev.direct_ack",
                    "A Direct session exposes each player's IP address to the other. The session panel shows this once and will not offer Direct host/join until you accept it. Reset shows it again."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VpbNetTransportChoice.DirectIpAcknowledged()
                    ? VPBTranslation.T("settings.row.shown_again", "SHOW AGAIN")
                    : VPBTranslation.T("settings.row.not_seen", "NOT SEEN YET"),
                ActionEnabled = () => VpbNetTransportChoice.DirectIpAcknowledged(),
                OnAction = () =>
                {
                    try
                    {
                        Settings st = Settings.Instance;
                        if (st != null && st.NetDirectIpAck != null) st.NetDirectIpAck.Value = false;
                    }
                    catch { }
                },
                RowVisible = () => DevNetEnabled() && !VpbNetTransportChoice.IsSteam()
            });

            AddDevText(defs, s.NetRendezvousAddress, "dev.net.rendezvous", "dev_net",
                VPBTranslation.T("settings.dev.rendezvous", "Rendezvous address"),
                VPBTranslation.T("settings.tip.dev.rendezvous",
                    "Empty by design, and VPB will never fill it in: there is no default, no built-in list and no recommended endpoint. Use one you were given, or run your own with \"VpbNet.exe --rendezvous 47773\". Its operator sees two addresses and an opaque token for a few seconds and can never read your session - but it is still someone else's machine, which is why the choice is yours. Empty means direct or LAN only."),
                () => DevNetEnabled() && !VpbNetTransportChoice.IsSteam());

            AddDevText(defs, s.NetBrokerPath, "dev.net.broker_path", "dev_net",
                VPBTranslation.T("settings.dev.broker_path", "Broker path"),
                VPBTranslation.T("settings.tip.dev.broker_path",
                    "Full path to VpbNet.exe. Empty uses VpbNet\\VpbNet.exe next to VPB.dll."),
                () => DevNetEnabled());
        }

        private void AppendDevClipSettings(List<InternalSettingDefinition> defs, Settings s)
        {
            InternalSettingDefinition rec = new InternalSettingDefinition
            {
                Key = "dev.clip.record",
                GroupKey = "dev_clip",
                Label = VPBTranslation.T("settings.dev.record", "Record pose to clip"),
                Tooltip = VPBTranslation.T("settings.tip.dev.record",
                    "Records one Person's network controllers to a .vpbclip file while on. Drive the Person however you like - recording never touches it. Turn off to close the file."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => DevBool(s.NetRecordEnabled),
                SetBool = v =>
                {
                    DevWriteBool(s.NetRecordEnabled, v);
                    if (v) DevWriteBool(s.NetReplayEnabled, false);
                }
            };
            rec.SetDefault(DevDefaultBool(s.NetRecordEnabled));
            defs.Add(rec);

            InternalSettingDefinition rep = new InternalSettingDefinition
            {
                Key = "dev.clip.replay",
                GroupKey = "dev_clip",
                Label = VPBTranslation.T("settings.dev.replay", "Replay clip onto a Person"),
                Tooltip = VPBTranslation.T("settings.tip.dev.replay",
                    "Drives one Person from a recorded clip. No second Person, no network, identical every run - this is how the applier gets debugged alone. Turning it on turns recording off."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => DevBool(s.NetReplayEnabled),
                SetBool = v =>
                {
                    DevWriteBool(s.NetReplayEnabled, v);
                    if (v) DevWriteBool(s.NetRecordEnabled, false);
                }
            };
            rep.SetDefault(DevDefaultBool(s.NetReplayEnabled));
            defs.Add(rep);

            AddDevText(defs, s.NetClipFile, "dev.clip.file", "dev_clip",
                VPBTranslation.T("settings.dev.clip_file", "Clip file"),
                VPBTranslation.T("settings.tip.dev.clip_file",
                    "A bare name resolves inside BepInEx\\plugins\\VPB\\VpbNet\\clips; a full path is used as given. Empty records to a timestamped name and replays the newest clip in that folder."),
                null);

            defs.Add(new InternalSettingDefinition
            {
                Key = "dev.clip.info",
                GroupKey = "dev_clip",
                Label = DescribeClipFolder(),
                Tooltip = VPBTranslation.T("settings.tip.dev.clip_info", "Where clips are written, and the newest one currently there."),
                ControlType = InternalSettingControlType.Button,
                OnAction = null
            });

            AddDevSlider(defs, s.NetRecordHz, "dev.clip.record_hz", "dev_clip",
                VPBTranslation.T("settings.dev.record_hz", "Record rate (Hz)"),
                VPBTranslation.T("settings.tip.dev.record_hz", "Frames per second written to the clip. 45 is the planned pose rate."),
                1f, 200f, 1f, null);

            AddDevText(defs, s.NetRecordAtom, "dev.clip.record_atom", "dev_clip",
                VPBTranslation.T("settings.dev.record_atom", "Record atom uid"),
                VPBTranslation.T("settings.tip.dev.record_atom", "Atom uid to record. Empty records the first Person-like atom in the scene."),
                null);

            AddDevText(defs, s.NetReplayAtom, "dev.clip.replay_atom", "dev_clip",
                VPBTranslation.T("settings.dev.replay_atom", "Replay atom uid"),
                VPBTranslation.T("settings.tip.dev.replay_atom", "Atom uid to drive. Empty drives the first Person-like atom in the scene."),
                null);

            AddDevToggle(defs, s.NetReplayLockRoot, "dev.clip.replay_lock_root", "dev_clip",
                VPBTranslation.T("settings.dev.replay_lock_root", "Lock replay root (skip control position)"),
                VPBTranslation.T("settings.tip.dev.replay_lock_root",
                    "Clip replay. Person stays where it was placed. Clip root motion plays in place so it does not walk into the other Person."),
                null);

            AddDevToggle(defs, s.NetReplayLoop, "dev.clip.replay_loop", "dev_clip",
                VPBTranslation.T("settings.dev.replay_loop", "Loop the clip"),
                VPBTranslation.T("settings.tip.dev.replay_loop",
                    "The seam is ramped over 2.5s rather than snapped: the last pose and the first pose are unrelated, and a jump over 0.5m slings the body."),
                null);

            InternalSettingDefinition speed = new InternalSettingDefinition
            {
                Key = "dev.clip.replay_speed",
                GroupKey = "dev_clip",
                Label = VPBTranslation.T("settings.dev.replay_speed", "Replay speed"),
                Tooltip = VPBTranslation.T("settings.tip.dev.replay_speed",
                    "Slow motion is the point: it is how you watch the applier resolve a fast movement."),
                ControlType = InternalSettingControlType.Slider,
                Min = 0.05f,
                Max = 4f,
                Step = 0.05f,
                Decimals = 2,
                GetFloat = () => s.NetReplaySpeed != null ? s.NetReplaySpeed.Value : 1f,
                SetFloat = v => { if (s.NetReplaySpeed != null) s.NetReplaySpeed.Value = v; },
            };
            speed.SetDefault(DevDefaultFloat(s.NetReplaySpeed, 1f));
            defs.Add(speed);
        }

        private static bool DevNetEnabled()
        {
            try { return Settings.Instance != null && DevBool(Settings.Instance.NetEnabled); }
            catch { return false; }
        }

        private static bool DevBool(ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }

        private static void DevWriteBool(ConfigEntry<bool> entry, bool value)
        {
            try { if (entry != null) entry.Value = value; }
            catch { }
        }

        private static int DevInt(ConfigEntry<int> entry)
        {
            try { return entry != null ? entry.Value : 0; }
            catch { return 0; }
        }

        private static void DevWriteInt(ConfigEntry<int> entry, int value)
        {
            try { if (entry != null) entry.Value = value; }
            catch { }
        }

        private static bool DevDefaultBool(ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.DefaultValue is bool && (bool)entry.DefaultValue; }
            catch { return false; }
        }

        private static string DevDefaultString(ConfigEntry<string> entry)
        {
            try { return entry != null && entry.DefaultValue != null ? entry.DefaultValue.ToString() : ""; }
            catch { return ""; }
        }

        private static float DevDefaultFloat(ConfigEntryBase entry, float fallback)
        {
            try
            {
                if (entry != null && entry.DefaultValue != null)
                    return Convert.ToSingle(entry.DefaultValue, CultureInfo.InvariantCulture);
            }
            catch { }
            return fallback;
        }

        private static string DevReadString(ConfigEntry<string> entry, string fallback)
        {
            try { return entry != null && entry.Value != null ? entry.Value : fallback; }
            catch { return fallback; }
        }

        private static void DevWriteString(ConfigEntry<string> entry, string value)
        {
            try { if (entry != null) entry.Value = value ?? ""; }
            catch { }
        }

        private void AddDevToggle(List<InternalSettingDefinition> defs, ConfigEntry<bool> entry,
            string key, string group, string label, string tooltip)
        {
            AddDevToggle(defs, entry, key, group, label, tooltip, null);
        }

        private void AddDevToggle(List<InternalSettingDefinition> defs, ConfigEntry<bool> entry,
            string key, string group, string label, string tooltip, Func<bool> rowVisible)
        {
            if (entry == null) return;
            InternalSettingDefinition d = new InternalSettingDefinition
            {
                Key = key,
                GroupKey = group,
                Label = label,
                Tooltip = tooltip,
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => DevBool(entry),
                SetBool = v => DevWriteBool(entry, v),
                RowVisible = rowVisible
            };
            d.SetDefault(DevDefaultBool(entry));
            defs.Add(d);
        }

        private void AddDevText(List<InternalSettingDefinition> defs, ConfigEntry<string> entry,
            string key, string group, string label, string tooltip, Func<bool> rowVisible)
        {
            if (entry == null) return;
            InternalSettingDefinition d = new InternalSettingDefinition
            {
                Key = key,
                GroupKey = group,
                Label = label,
                Tooltip = tooltip,
                ControlType = InternalSettingControlType.TextArea,
                SingleLineText = true,
                GetString = () => DevReadString(entry, ""),
                SetString = v => DevWriteString(entry, v),
                RowVisible = rowVisible
            };
            d.SetDefault(DevDefaultString(entry));
            defs.Add(d);
        }

        private void AddDevSlider(List<InternalSettingDefinition> defs, ConfigEntry<int> entry,
            string key, string group, string label, string tooltip, float min, float max, float step, Func<bool> rowVisible)
        {
            if (entry == null) return;
            InternalSettingDefinition d = new InternalSettingDefinition
            {
                Key = key,
                GroupKey = group,
                Label = label,
                Tooltip = tooltip,
                ControlType = InternalSettingControlType.Slider,
                Min = min,
                Max = max,
                Step = step,
                Decimals = 0,
                GetFloat = () => { try { return entry.Value; } catch { return min; } },
                SetFloat = v => { try { entry.Value = (int)Math.Round(v); } catch { } },
                RowVisible = rowVisible
            };
            d.SetDefault(DevDefaultFloat(entry, min));
            defs.Add(d);
        }

        private static string DescribeClipFolder()
        {
            string newest = null;
            int count = 0;
            try
            {
                string dir = VpbNetClipFormat.ClipDirectory;
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*" + VpbNetClipFormat.ClipExtension);
                    count = files.Length;
                    string path = VpbNetClipFormat.NewestClipPath();
                    if (!string.IsNullOrEmpty(path)) newest = Path.GetFileName(path);
                }
            }
            catch { }

            if (count == 0) return VPBTranslation.T("settings.dev.clip_none", "No clips recorded yet");
            return string.Format(CultureInfo.InvariantCulture,
                VPBTranslation.T("settings.dev.clip_count", "{0} clip(s), newest: {1}"), count, newest ?? "?");
        }
    }
}
