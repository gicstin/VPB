# VPB logging convention

Normal logs should explain what happened and what failed without listing every successful item. Verbose logs provide detail for investigation.

## What belongs in normal output

1. Failures, missing inputs, and missing dependencies, with package UID, path, and exception context where available.
2. Meaningful operation outcomes: what changed, counts, elapsed time, and result.
3. Startup identification and readiness, index summaries, migrations, and recovery decisions.
4. Batch summaries instead of one success line per package or file. Preserve individual failure identities outside any top-five summary.
5. Explicit diagnostics enabled through their own switches or diagnostic hotkeys.

Keep useful Info messages, including failures and operation results. Review each call by what it reports, not severity alone.

Per-item successes, intermediate phases, navigation traces, lookup detail, and routine image-load messages belong behind a detail guard. Distinguish attempts from completed work: starting registration does not mean a package was registered, moved, or installed.

## Writing log calls

Use existing `VPBLogger` module sources from `VPB.src.util`, such as `Files`, `Gallery`, `Hub`, or `Main`. Existing `LogUtil` wrappers also reach VPB logging.

Guard detail **before constructing messages**:

```csharp
if (VPBLogger.Verbose)
    VPBLogger.Files.LogInfo("Registered package: " + uid);
```

Use that success message only after registration succeeds. Keep operation calls, counters, state updates, and cleanup outside logging guards.

UI detail can also honor its existing diagnostic switch:

```csharp
if (VPBLogger.Verbose || Settings.Instance?.LogVerboseUi?.Value == true)
    VPBLogger.Gallery.LogInfo("Opened category: " + categoryName);
```

Hub request detail can honor `LogHubRequests`; preset/startup detail can honor `LogStartupDetails`. Preserve existing timing switches and diagnostic hotkeys. Do not make an explicitly enabled diagnostic depend on global verbose mode as well.

Background worker detail should read cached `VPBLogger.Verbose`. Do not query Unity objects or bind configuration merely to decide whether to log. The global verbose flag refreshes through the main-thread poll at most once per second.

Choose severity and in-game routing deliberately. `LogInfo` defaults to file-only routing; `LogMessage`, `LogWarning`, `LogError`, and `LogFatal` default to allowing in-game display. For a file-only failure:

```csharp
VPBLogger.Files.LogError("Package load failed: " + uid + " | " + ex, showInGame: false);
```

When cleaning up existing logs, preserve failure visibility and popup behavior. Do not move failures behind verbose or add popups as a side effect of changing severity.

## Configuration and automatic summaries

Normal mode is default in `BepInEx/config/VPB.cfg`:

```ini
[Logging]
VerboseLogging = false
```

Setting `VerboseLogging = true` restores guarded detail and bypasses the exact-repeat governor. It does not enable expensive profiling, queue probes, or every separate diagnostic switch. Those remain independent.

For repeated events in normal mode:

- First 10 identical events pass within a 30-second window.
- Copy 11 produces one suppression notice; later omitted copies contribute to a count summary.
- Omitted totals emit on expiry, a later matching event, or logger flush at shutdown/teardown.
- Identity includes displayed source, severity, full message, and in-game routing. Different UIDs or paths remain distinct. Fatal always passes.
- State is bounded to 4,096 keys and 2,048 combined source/message characters per key. Oversized events and new keys at capacity pass unchanged. Unique-item floods still need caller-side guards or aggregation.

Suppression notices do not create repeated in-game popups.

Existing plugin-create statistics use one constant `plugin_create` key with sample count, failure count, and total/average/min/max milliseconds. Scene completion, periodic polling, and teardown each report and clear the collected samples. Do not label these as guaranteed whole-scene totals or add one metric key per plugin.

## Session files and limits

VPB writes `BepInEx/VPB/logs/VPB-<UTC>-<id>.log`. One process uses one filename, including plugin hard resets. Startup normally retains current session plus four previous sessions; locked files or filesystem failures can temporarily leave more.

Writer flushes every two seconds, immediately for Error/Fatal, and at shutdown/teardown. Abrupt termination can lose buffered tail data, pending repeat totals, and pending statistics. Flush does not guarantee survival of OS or hardware failure.

Only VPB-generated session filenames are pruned. Shared `BepInEx/LogOutput.log` stays untouched. Session capture includes events routed through `VPBLogSource`; direct Unity, SuperController, other-plugin, and preloader output is outside that capture. Capture starts when VPB initializes its logger.

For bug reports, include reproduction steps and corresponding VPB session file from each machine. Match runs using UTC timestamps and startup DLL identification; keeping session files does not match runs across machines automatically.

Source reference: [logger and routing](../src/util/VPBLogger.cs), [repeat governor](../src/util/VpbLogRepeatGovernor.cs), [session writer](../src/util/VpbSessionLog.cs).
