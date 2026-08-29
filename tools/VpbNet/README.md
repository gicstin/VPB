# VpbNet — VPB multiplayer broker

Out-of-process helper for VPB's multiplayer layer. **Launched by the plugin, never run by hand** — it refuses to start without a `--plugin-port` and a launch secret on stdin.

The one exception is `--rendezvous`, which runs this same binary as a standalone NAT-traversal helper for other people's sessions. It shares nothing with the broker mode and never talks to VaM. If you are thinking of running one: **[RENDEZVOUS.md](RENDEZVOUS.md)**.

It carries loopback IPC with a version handshake, heartbeat and orphan reaping, and three session backends: a loopback echo, an authenticated **LAN UDP** backend, and **Steam**. Direct sessions between two home routers go through a rendezvous for NAT traversal, with a relay fallback for the pairs that cannot be punched.

Every codec and state machine has a headless self-test:

```
--self-test-{pose,clock,snapshot,event,session,keyframe,diag,rig,roomcode,invite,
             offer,contract,storable,rules,prop,avatar,fidelity,roombook,redact,steam,
             rendezvous,direct,discovery,reliable}
```

Each returns exit code 0 on pass and prints a `RESULT:` line. `rendezvous`, `direct`, `discovery` and `reliable` open real sockets; the rest are pure and need nothing.

## Why a separate process

1. **Appid isolation.** A Steam build of VaM has already called `SteamAPI_Init` under VaM's own appid in-process, and a second init with a different appid in the same process is not possible. Both peers must share an appid to connect at all, so without a separate process there is no universal Steam path.
2. **Runtime escape.** `VPB.csproj` targets .NET 3.5 — no async, no modern crypto, no modern threading. The broker is .NET 8 and makes all of that a non-issue for every future backend.
3. **Crash isolation.** A netcode fault kills a small helper, not a 20 GB VaM session.
4. **Future-proofing.** Swapping or adding a transport never touches the VaM-side DLL.

## Build

Building `VPB.csproj` builds the broker too: its `BuildVpbNetBroker` target publishes this project,
stages `VpbNet.exe` into `vam_patch/BepInEx/plugins/VpbNet/` and refreshes the manifest rows, so the
plugin and the broker beside it can never drift apart. The target is gated on source timestamps, so
an untouched broker adds nothing to a build; a missing dotnet SDK is a warning, since the published
exe is committed. Set `SkipVpbNetBroker=true` to skip it.

To publish the broker on its own — same script, plus an optional install:

```
tools\VpbNet\publish.cmd
tools\VpbNet\publish.cmd "C:\vam"     :: also copies into <VaM>\BepInEx\plugins\VpbNet\
```

Or by hand:

```
dotnet publish tools/VpbNet/VpbNet.csproj -c Release
```

Output lands in `bin\Release\net8.0\win-x64\publish\` as **a single self-contained `VpbNet.exe`** — the .NET runtime, every managed assembly, the native host libraries and the debug symbols are all inside it. Nothing else needs to be copied, and the target machine needs no .NET installed.

> **`publish`, never `build`.** `dotnet build` — and the IDE's Build command — ignore `PublishSingleFile` completely. A self-contained *build* gives you `bin\Debug\net8.0\win-x64\VpbNet.exe`, which is a ~140 KB **apphost** that loads `VpbNet.dll` from beside it, surrounded by a few hundred loose runtime DLLs. If the exe you are holding is small and has company, you built instead of published. The single file only ever appears under a `publish\` folder.

Copy that one file to a `VpbNet\` subfolder next to `VPB.dll`:

```
BepInEx/plugins/VpbNet/VpbNet.exe
```

Override the location with the `Net.BrokerPath` setting if you keep it elsewhere.

The properties that make it one file, and why each is there:

| Property | Why |
|---|---|
| `SelfContained` + `RuntimeIdentifier` | Bundles the runtime, so no .NET install is required on the user's machine. |
| `PublishSingleFile` | Bundles managed assemblies into the exe. |
| `IncludeNativeLibrariesForSelfExtract` | Without it the native host libraries can be emitted **beside** the exe on some SDKs — the usual reason a "single file" build still has loose `.dll`s next to it. |
| `IncludeAllContentForSelfExtract` | Same, for content files. |
| `DebugType=embedded` | Symbols go inside the exe instead of a sidecar `VpbNet.pdb`, so stack traces keep line numbers without a second file. |
| `InvariantGlobalization` | Drops the ICU dependency — smaller, and one less native library to carry. |
| `SatelliteResourceLanguages=en` | No per-language satellite assemblies. |
| `EnableCompressionInSingleFile` | Roughly halves the file (~70 MB → ~35 MB). Costs a one-time decompression at launch, which is irrelevant for a process started once per session. |

Trimming (`PublishTrimmed`) is deliberately **off**. It would cut the size further, but it is the classic source of "worked in dev, threw at runtime" failures, and the broker is not size-critical. Revisit only if distribution size becomes a real complaint.

## Lifecycle

| Stage | Behaviour |
|---|---|
| Launch | Only on explicit session use, never on plugin load, and never at all while `Net.Enabled = false` (the kill switch). |
| Handshake | Plugin binds a loopback UDP socket, launches the broker with its port, writes a 32-byte launch secret to the broker's **stdin** (not the command line, which other local processes can read). The broker binds its own loopback port and prints `READY <port>`. The plugin sends `HELLO` with the secret; the broker replies `WELCOME` with a 16-byte session token that every later datagram must carry. |
| Versioning | The 28-byte header and the `Reject` payload are frozen across every IPC version, so a stale broker is answered with a legible `IpcVersionMismatch` rather than silently dropping traffic. |
| Heartbeat | Plugin pings at 1 Hz; the broker pongs. Silence is warned about at 4 s and fatal at 45 s. The gap is wide on purpose: a VaM scene or preset load blocks the main thread for seconds at a time, and a loopback stall is not a network fault. |
| Death | The broker exits on `BYE`, on heartbeat loss, on socket error, or as soon as the parent VaM process is gone. |
| Orphans | Both sides reap by PID file (`vpbnet-<pid>.pid`, beside `Environment.ProcessPath` — **not** `AppContext.BaseDirectory`, which for a self-extracting single-file build is a temp folder named after the bundle's content hash, and is therefore *shared by every install holding an identical copy of the exe*): the plugin on startup, the broker before writing its own. A PID is only killed if it is live, named `VpbNet`, **and** the VaM process recorded as its parent is gone — otherwise a second VaM instance on the same machine would reap the first one's live broker. |

## Sessions and the transport seam

The plugin never names a transport. It sends `OpenSession` with a backend id, a role, a room code, and an opaque **connect blob**; everything backend-specific lives below `ISessionTransport`. A LAN host puts `address:port` in the blob it hands back as an **invite blob**, a Steam backend would put a lobby id there, and no layer above the seam can tell the difference.

| IPC message | Direction | Carries |
|---|---|---|
| `OpenSession` | plugin → broker | backend id, host/join role, max peers, room code, connect blob |
| `CloseSession` | plugin → broker | — |
| `SessionState` | broker → plugin | idle / listening / connecting / connected / failed / closed, peer count, invite blob, human-readable reason |
| `PeerEvent` | broker → plugin | peer id + up / down / stalled / resumed, with a reason |
| `Data` | both | peer id, channel, flags, payload (≤ 1024 B) |
| `PeerStats` | broker → plugin | per peer: sent, received, lost, reordered, RTT µs, jitter µs (1 Hz) |

Peers are opaque small integers assigned by the backend. The plugin never sees an endpoint, a SteamID, or a node key.

## LAN backend

Bring-up path for every backend after it, and the permanent offline fallback. Plain UDP, IPv4, default port **47772**.

| Behaviour | Detail |
|---|---|
| Addressing | Host binds a port and prints `address:port` for the joiner. Hostnames resolve; a bare port is accepted for the bind side. |
| Authentication | Every datagram carries an 8-byte HMAC-SHA256 tag over a key stretched from the room code. A datagram that fails is dropped **without an answer** — a wrong room code is indistinguishable from an unreachable host, on purpose, so probing codes gains nothing. |
| Anti-replay | The tag proves the room code; the peer's endpoint and connection id are pinned on accept, so a captured datagram cannot be replayed from elsewhere. |
| Keepalive | Ping at 2 Hz, stamped with the raw tick counter — a LAN round trip is a fraction of a millisecond and an integer-millisecond clock reports every one of them as zero. |
| Liveness | 2 s silent → `Stalled`, 10 s → `Down`. A joiner whose host vanished returns to knocking on its own, so a host restart does not need a plugin restart. |
| Loss accounting | Sequence numbers advance on `Data` only. Sharing the counter with keepalives made every ping look like a lost frame. |
| Reliability | The `reliable` flag is honoured. Flagged datagrams carry a sequence number and are retransmitted until acknowledged, then delivered in order; up to 64 may be in flight and a sender that fills the window queues rather than drops. Unflagged traffic — the pose stream — is left alone, because a late pose is worth less than the next one. |
| Caps | ≤ 4 peers, ≤ 1024 B payload, 128 receive slots, unauthenticated-datagram counter logged once per second. |

**Windows Firewall** prompts the first time the broker binds a UDP port. Allow it on private networks; the broker is the process that asks, not VaM.

## Steam backend

The lowest-friction way to connect, and the only one where **neither side learns the other's IP address**. Both players type the same room code, pick Steam, and press Host / Join. There is no invite to paste, no port to forward, no rendezvous address to be given, and nothing to configure.

| Behaviour | Detail |
|---|---|
| Discovery | The host creates a Steam lobby whose only data is `r = <32 hex>`, the `LobbyToken` that `SessionAuth.Derive` produces from the room code. The joiner asks Steam for lobbies filtered on exactly that value. The token is publishable by construction — it is a domain-separated PBKDF2 subkey, not the session key — so the lobby is world-visible and still effectively private: finding it means already knowing the room code. Nothing else is written to the lobby, and the key name is a bare `r` so the row carries no constant that identifies VPB. Once the room is full the lobby is taken off Steam's list entirely. |
| App id | **480 (Spacewar)**, Valve's public sample app: any signed-in account can use it without owning or buying anything. It is a *configuration value*, not a constant (`Net.SteamAppId`), so two people who own the same Steam game can move off it with a settings change. Both sides must match exactly. **VPB must never itself ship on Steam while it uses 480.** |
| Transport | `ISteamNetworkingMessages` over Steam Datagram Relay, which is why this path needs no rendezvous and no punch. ICE is **disabled** (`EnableRelayOnly`), so traffic always takes Valve's relay and never a direct peer-to-peer path — that is what keeps the two IP addresses apart, and it is not optional. It costs some latency and is the whole point of the backend. |
| Authentication | Same 8-byte HMAC over the room-code-stretched key as LAN, on top of Steam's own identity-authenticated channel. A session request from outside the lobby is accepted at the Steam layer but can never produce a valid tag, so it carries nothing. |
| Keepalive / stats | Our own ping-pong and `Data`-only sequence numbers, identical to LAN. Steam's own connection status struct is deliberately not parsed — one less native layout to be wrong about. |
| Identity | Steam P2P addresses peers **by SteamID**, so the other player can see which account you are signed into. VPB never displays it, but that is a UI choice and not a guarantee. The session panel makes you accept that once (`Net.SteamIdentityAck`) before it will offer the Steam buttons. |
| Timeouts | Lobby search retries every 2 s and gives up at 90 s with a message naming the room code. A found-but-silent host gives up at 20 s. |

### steam_api64.dll

Native, loaded at runtime, and **shipped with VPB**: it lives at `vam_patch/BepInEx/plugins/VpbNet/steam_api64.dll`, is committed, is listed in `patch_manifest.json`, and so reaches users through the updater like every other payload file. It is Valve's unmodified redistributable — see `VPB_THIRD_PARTY_NOTICES.txt`.

Every broker publish — from a plugin build or from `publish.cmd` — stages `VpbNet.exe` into that same folder and runs `scripts/SyncPatchManifestVpbNet.ps1`, so the manifest rows follow whatever is on disk. Drop a new `steam_api64.dll` in by hand and the next build picks it up the same way.

At runtime the broker searches in order: `Net.SteamApiPath`, the folder holding `VpbNet.exe`, the plugins folder, the VaM install folder. A Steam build of VaM therefore still works even if the shipped copy is missing.

Bindings are hand-written P/Invoke against the flat API (`Transport/Steam/SteamNative.cs`) with callbacks on `SteamAPI_ManualDispatch_*`. Every entry point is resolved and checked at load, so an old DLL fails by **naming the missing export** rather than throwing somewhere in the poll loop. There is no Steamworks.NET dependency and no NuGet package.

To see what a machine can actually do, from the VpbNet folder:

```
VpbNet.exe --steam-probe
```

It reports the DLL it picked, whether the entry points are all present, whether Steam answered, and whether the relay network came up — in that order, so the first failing line is the one to fix.

## Security posture

- Socket is bound to `127.0.0.1` only, and the broker ignores datagrams from any port other than the plugin's.
- The launch secret goes over stdin, so it never appears in a process listing.
- Post-handshake datagrams carry a per-launch token; mismatches are dropped, not answered.
- Message rate is capped (`MaxMessagesPerSecond`), and payload sizes are bounded by `MaxDatagram` / `MaxEchoPayload`.
- Only one plugin may bind a broker; a second `HELLO` gets `AlreadyBound`.

Against a same-user attacker with administrator rights none of this is airtight — nothing local can be. It is sized to stop *another unprivileged local process* from driving your avatar, which is the threat in the design.

## Layout

```
Program.cs                        entry, args, stdin secret, pid file, orphan reaping
BrokerHost.cs                     socket, handshake, heartbeat, sessions, data routing, liveness
Transport/ISessionTransport.cs    the seam - nothing above it may learn the transport
Transport/LoopbackEchoTransport.cs  no network, sends come straight back
Transport/LanUdpTransport.cs      authenticated UDP: LAN, rendezvous-punched direct, relayed
Transport/SessionAuth.cs          room code -> keys, and the truncated-HMAC datagram tag
Transport/Steam/SteamNative.cs    flat-API P/Invoke + manual callback dispatch, no SDK dependency
Transport/Steam/SteamP2PTransport.cs  Steam backend: lobby by room code, messages over SDR
Rendezvous/RendezvousServer.cs    the --rendezvous socket loop
Rendezvous/RendezvousClient.cs    announce/poll while a direct session is being established
Rendezvous/RendezvousTable.cs     what the server decides, with no sockets in it
```

Everything above is broker-only. The protocol itself lives one level up, in **`/protocol`**, and belongs to neither side:

```
protocol/VpbNetIpc.cs             plugin <-> broker IPC layout
protocol/VpbNetProtocol.cs        POSE frame: quantized bones, negotiated count, skip-safe ext
protocol/VpbNetClock.cs           clock sync + snapshot timeline
protocol/VpbNetSnapshotBuffer.cs  jitter buffer, interpolate / extrapolate / freeze
protocol/VpbNetEvent.cs           EVENT codec, identifier and plugin-reference validation
protocol/VpbNetSession.cs         session state machine
protocol/VpbNetKeyframe.cs        full-state keyframe + fragmentation
protocol/VpbNetRig.cs             rig descriptor and capability negotiation
protocol/VpbNetFidelity.cs        optional higher-detail rig extension
protocol/VpbNetRules.cs           the two-bit permission table both sides publish
protocol/VpbNetContract.cs        content contract: what each side owns
protocol/VpbNetOffer.cs           scene offer, manifest and content state
protocol/VpbNetStorable.cs        the storable whitelist a peer may write through
protocol/VpbNetAvatar.cs          avatar seats and claims
protocol/VpbNetProp.cs            object and subscene references
protocol/VpbNetRoomCode.cs        room code alphabet, entropy and refusal messages
protocol/VpbNetInviteCode.cs      invite blob encode/decode and join-target resolution
protocol/VpbNetRendezvous.cs      rendezvous + relay wire format
protocol/VpbNetRoomBook.cs        host inventory and join recents (codec only)
protocol/VpbNetSteam.cs           app id parsing, lobby key, Steam-specific messages
protocol/VpbNetRedact.cs          strips room codes and addresses out of log text
protocol/VpbNetDiagnostics.cs     overlay stats + allocation-free formatter
protocol/*SelfTest.cs             the headless test for each of the above
```

The broker loop waits on the IPC socket **and** the backend's socket together, with a 2 ms cap for backends that have no socket to wait on. Blocking on the IPC socket alone would hold every inbound LAN datagram for the length of that block.

Every file in `/protocol` is written in .NET 3.5-compatible C# and is `<Compile Include>`d by **both** `VPB.csproj` and this project - one source, two runtimes, because no assembly reference can span .NET 3.5 Mono inside VaM and .NET 8 out here. It sits outside `tools/` deliberately: it is the contract the plugin is built against, not build tooling. Keeping it there also keeps it clear of the `.gitignore` rules that exclude the rest of `tools/`, which would otherwise leave `VPB.csproj` pointing at files a fresh clone does not have.
