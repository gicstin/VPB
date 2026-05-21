# VPB — VaM Package Browser

VPB is a **fork** of the venerable **`var_browser`** project by **sFisherE**.

- **Upstream project**: `var_browser` by sFisherE
- **This fork**: VPB — focused on working great alongside **VPM** workflows and adding quality-of-life + performance/automation features.

## What this is

VPB is a **BepInEx plugin for Virt-A-Mate (VaM)** that provides an in-game package/browser experience and related tooling.

This repo builds a `VPB.dll` plugin, deployed to `BepInEx/plugins/`.

## Screenshots

### Screenshots

A visual tour so you can find where the new stuff lives.


**Gallery layout**

Panel docking mode:

<img width="771" height="193" alt="image" src="https://github.com/user-attachments/assets/6f8f0206-1fda-478f-9d7e-1713eebcb9a0" />

*Three docking modes: left, top, right.*

---

Column count via ctrl+scroll

<img width="620" height="98" alt="image" src="https://github.com/user-attachments/assets/32d20668-6e16-4a96-bc2d-33d24cd5f776" />

*Ctrl+scroll inside the gallery to change column count on the fly.*


**Search and sort**

Date sorting, Hide old version, New vs Updated.

<img width="343" height="876" alt="image" src="https://github.com/user-attachments/assets/8c985324-57bb-472b-9706-b6cf083d8b48" />
<img width="219" height="318" alt="image" src="https://github.com/user-attachments/assets/e9855a2d-e564-4c4d-887a-775a0abbf5dc" />


Pretty names

<img width="1193" height="685" alt="image" src="https://github.com/user-attachments/assets/2d7ad5bc-1ec4-4d4f-b0d8-28c352047aa2" />


History tab

<img width="784" height="892" alt="image" src="https://github.com/user-attachments/assets/2a6a622f-ad95-4ab8-ba53-62bf39041c94" />


---


**Tags**

<img width="783" height="557" alt="image" src="https://github.com/user-attachments/assets/04938e3c-687b-431e-a781-db53bba6f49e" />
<img width="216" height="287" alt="image" src="https://github.com/user-attachments/assets/9877f62d-1e05-4ce1-b831-144ae54b0070" />
<img width="494" height="617" alt="image" src="https://github.com/user-attachments/assets/efbf41c5-2bf6-44cd-8beb-436e06f270db" />
<img width="253" height="423" alt="image" src="https://github.com/user-attachments/assets/3cb4ab6c-9396-4fbe-8e50-c38ef171f8d6" />



**Toolbox**

**Suppress-scale toggle**

<img width="565" height="103" alt="image" src="https://github.com/user-attachments/assets/87baf3e3-a096-4ef1-92b6-8b1965df0681" />



**load/unload texture cache button**

<img width="1171" height="1281" alt="image" src="https://github.com/user-attachments/assets/621d3593-9905-4d02-b3e1-d4aecdd9b3a3" />

<img width="1014" height="427" alt="image" src="https://github.com/user-attachments/assets/208563f0-e859-41c6-9d05-5b81ebff95f7" />


**texture caching**

<img width="1493" height="465" alt="image" src="https://github.com/user-attachments/assets/41a1a95e-36c6-4f07-ab1f-7fe1bed9559e" />



**Scan whitelist**

**Scan whitelist settings**

<img width="949" height="814" alt="image" src="https://github.com/user-attachments/assets/1cc666b4-279d-4311-8e01-8a0a95a3b0be" />

*Pick which folders or packages VaM is allowed to scan at startup; everything else stays on disk, hidden, and gets loaded on demand.*



**VaM integration**

**VaM Quick Menu assignable grid**

<img width="590" height="871" alt="image" src="https://github.com/user-attachments/assets/7c247080-7c1a-4c50-9415-0ae5c731ea9e" />

<img width="207" height="187" alt="image" src="https://github.com/user-attachments/assets/06124e3f-fff5-44db-9408-8e925254bade" />

*4×4 grid across 10 pages of VPB actions wired into VaM's Quick Menu.*


**VaM Hub browser**

Hub pagination and hide-downloaded

<img width="1166" height="430" alt="image" src="https://github.com/user-attachments/assets/fde3aba7-055f-4b61-8fdf-1fbfd38e2b7e" />

<img width="1096" height="1254" alt="image" src="https://github.com/user-attachments/assets/d088ad77-c360-436a-b4ac-4eedc75e4e10" />

*Pagination, hide-downloaded toggle, download-all, persistent Hub config.*


**Settings**

**Reorganized settings panel, color picker for outlines**
<img width="1241" height="513" alt="image" src="https://github.com/user-attachments/assets/8221d526-0ea7-4fb5-8355-311933db6708" />

<img width="797" height="283" alt="image" src="https://github.com/user-attachments/assets/0563a3c1-c3be-437d-8be6-dc4e249242d1" />


**Auto-updater**

<img width="1218" height="462" alt="image" src="https://github.com/user-attachments/assets/e765e42a-38e2-4831-8fc2-ce6fd3fa451c" />


## Features (high-level)

**Gallery and browsing**
- In-game gallery for VAR packages and loose-file presets (scenes, appearances, poses)
- Loose `.vap` appearance presets are classified by gender (M / F / Futa) instead of being uncategorized
- Multi-source filter (All / Local / Var) with per-category overrides
- Sort by name, description, date added, date updated, or usage count; collapse package families to their latest version with one toggle
- Pretty entry names hide internal `Preset_` / `Plugins_` prefixes
- History tab tracking recently-used items
- Dependencies filter to browse by what a package depends on

**Tagging and organization**
- 0-5 star ratings per package
- User-tag system: rename in place, tag inheritance through package versions (tag once, applies to all versions of that package family)
- Per-package hide preferences with session-only unhide (right-click on the item)

**Performance**
- Native TurboJPEG decoder for thumbnails, decoding off the main thread
- SQLite-backed gallery state (package index, tags, history, hide prefs) with indexed reads, instead of thousands of flat JSON files
- Zstd-compressed texture cache with on-demand load/unload and stale-entry cleanup
- Hub thumbnail pixel-packing on Unity Jobs (off main thread)
- Configurable texture downscaling with persistent cache
- AssetBundle cache, in-flight request de-duplication (optional), scene texture pre-warming (optional), face / hair texture priority heuristics (optional)
- Opt-in perf diagnostic flags (`VPB_PERF_TELEMETRY`, `LogSavePerf`, `LogPerfDiagnostics`) for users helping investigate regressions

**VaM integration**
- VaM Hub browser enhancements: pagination, hide-downloaded toggle, download-all, persistent Hub config, open-Hub-page action
- VaM Quick Menu integration: 4×4 grid of assignable shortcut slots across 10 pages, wire each slot to a VPB action (open category, random preset, gallery show/hide, target atom switcher, cleanup, FPS counter)
- Panel docking modes: left, top, or right anchors
- VR support including thumbstick scroll

**Quality of life**
- In-plugin auto-updater (version check + download from inside VaM)
- BrowserAssist migration tool: one-time import of user tags and hide preferences from a BA install
- Color picker for UI customization, configurable padding and border, hover border color
- Configurable hotkeys (default `Ctrl+V` to show / hide)

**Automation**
- VDS mode: command-line driven scene loading with cache control and runtime config overrides (see below)

## Installation

### Option A: VPM (recommended for first install)

This fork is intended to work best in combination with **VPM**.

https://github.com/gicstin/VPM

### Option B: Manual install

1. Build or obtain `VPB.dll`.
2. Copy it to:
   - `VaM\BepInEx\plugins\VPB.dll`
3. Start VaM.

### Updating

Once VPB is installed (either method above), you don't need VPM or a manual download for future updates. The in-plugin auto-updater checks for new versions and lets you download them directly from inside VaM.

![Auto-updater UI](https://gist.github.com/user-attachments/assets/58e865d6-5e2d-4172-8ea8-ca5a76957df8)

You can disable the auto-check in Settings if you'd rather update manually.

## Usage

### Show / hide UI

Default hotkey (from config):

- `Ctrl+V`

You can change this in the BepInEx config for the plugin.

### Session plugin (optional)

This repo also contains a **VaM session plugin script** you can use to trigger common VPB actions from a UI panel:

- `Custom/Scripts/VPB/VPB-SessionPlugin.cs`

It exposes actions such as:

- Refresh
- Remove Invalid Vars
- Uninstall All
- Hub Browse
- Open various “Custom / Category / Preset” browsers

## Advanced: VDS mode

VDS mode is a command-line driven workflow intended for automation and repeatability.

At a high level:

- VPB checks VaM’s process arguments.
- If VDS flags are present, VPB will (after startup) **load a scene automatically**.
- You can optionally apply **temporary runtime config overrides** and **cache actions**.

### Quick start

Add arguments to your VaM shortcut (or launch VaM from a terminal) like:

```text
--vpb.vds --vpb.vds.scene="Saves\scene\MyScene.json"
```

`--vpb.vds.scene` is required.

### Scene resolution rules

From the implementation (`src/VdsLauncher.cs`), `--vpb.vds.scene` accepts:

- An absolute scene path already containing `:/Saves/scene/` (passed through)
- A relative path starting with `Saves/scene/` or `Saves\\scene\\` (used as-is)
- A bare filename (with or without `.json`), in which case VPB searches under `Saves/scene/**`.

Notes:

- If multiple scenes match the same filename, VPB treats it as **ambiguous** and will not load.

### Supported VDS flags

VDS is enabled by either:

- `--vpb.vds`
- `--vpb.vds.*` (any sub-flag)

#### Required

- `--vpb.vds.scene=<sceneSpec>`

#### Cache / housekeeping

- `--vpb.vds.cache.textures.clearDisk=true|false`
  - Clears VPB’s texture cache directory on disk.
- `--vpb.vds.cache.textures.clearMem=true|false`
  - Clears VaM’s in-memory image cache (best-effort).
- `--vpb.vds.cache.ab.clearDisk=true|false`
  - Clears VPB’s AssetBundle cache directory on disk.

#### Temporary settings overrides

You can override VPB settings at runtime using:

- `--vpb.vds.set.<SettingFieldName>=<value>`

Important details:

- Field names are **case-sensitive** and must match the backing field names in `src/Settings.cs`.
- Overrides are intended to be **session-only**. VPB will try to disable autosave during overrides and restore original values on exit.

Common useful fields (see `Settings.cs` for the full list):

- `ReduceTextureSize` (bool)
- `MinTextureSize` (int)
- `MaxTextureSize` (int)
- `ForceTextureToMinSize` (bool)
- `CacheAssetBundle` (bool)
- `InflightDedupEnabled` (bool)
- `PrioritizeFaceTextures` (bool)
- `PrioritizeHairTextures` (bool)
- `ScenePrewarmEnabled` (bool)
- `UIScale` (float)
- `UIPosition` (`x,y` as `Vector2`, e.g. `120,80`)
- `MiniMode` (bool)


### Windows `.bat` launcher template

A ready-to-edit template is included in this repo:

- `Launch_VaM_VDS_Template.bat`

You can also copy/paste this and customize the variables:

```bat
@echo off
setlocal

set "VAM_DIR=C:\\Path\\To\\VaM"
set "VAM_EXE=VaM.exe"

set "SCENE=Saves\\scene\\MyScene.json"

pushd "%VAM_DIR%" || exit /b 1

start "" "%VAM_EXE%" ^
  --vpb.vds ^
  --vpb.vds.scene="%SCENE%" ^
  --vpb.vds.cache.textures.clearDisk=true ^
  --vpb.vds.cache.ab.clearDisk=true ^
  --vpb.vds.set.ReduceTextureSize=true ^
  --vpb.vds.set.MinTextureSize=1024

popd
endlocal
```
### Concrete example of `.bat` launcher template

This will launch the "3Deezel.Lilith" scene using desktop mode, loggin enabled, texture resize set to 4K etc.

```bat
@echo off
set "VAM_EXE=VaM.exe"
set "LOG_DIR=%~dp0logs"
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

set "TS=%DATE:~-4%%DATE:~4,2%%DATE:~7,2%_%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
set "TS=%TS: =0%"
set "LOG_FILE=%LOG_DIR%\vam_%TS%.log"

START "VaM" "%VAM_EXE%" ^
  -vrmode None ^
  -logFile "%LOG_FILE%" ^
  --vpb.vds ^
  --vpb.vds.scene="3Deezel.Lilith.3:/Saves/scene/Lilith..json" ^
  --vpb.vds.set.MinTextureSize=4096 ^
  --vpb.vds.set.ForceTextureToMinSize=true ^
  --vpb.vds.set.CacheAssetBundle=true ^
  --vpb.vds.set.ThumbnailSize=512 ^
  --vpb.vds.set.MaxTextureSize=4096 
```

### Example: deterministic “benchmark-like” scene load

Clear caches, reduce textures, and load a scene:

```text
--vpb.vds \
--vpb.vds.cache.textures.clearDisk=true \
--vpb.vds.cache.ab.clearDisk=true \
--vpb.vds.set.ReduceTextureSize=true \
--vpb.vds.set.MinTextureSize=1024 \
--vpb.vds.set.MaxTextureSize=4096 \
--vpb.vds.set.ScenePrewarmEnabled=true \
--vpb.vds.scene="Saves\\scene\\MyBenchmarkScene.json"
```

### Example: “fast start” (skip prewarm, keep caches)

```text
--vpb.vds \
--vpb.vds.set.ScenePrewarmEnabled=false \
--vpb.vds.scene="MyScene"
```

(`MyScene` will resolve to `MyScene.json` somewhere under `Saves/scene/` if it is unique.)

### Troubleshooting VDS

- If nothing happens, confirm `--vpb.vds.scene` is present and resolves to exactly one file.
- If a setting override “does nothing”, confirm you used the exact **field name** from `Settings.cs`.
- Check logs:
  - Standard BepInEx log output


## Building from source

### Requirements

- Visual Studio (the solution targets VS 2019 format)
- .NET Framework **3.5** (project targets `v3.5`)
- A local VaM install folder (for reference assemblies)

### Configure VaM path

The project uses an MSBuild property called `VaMPath`.

- Default: `C:\vam`
- You can set it by editing `VPB.local.props` (preferred, keeps your path out of source) or passing on the command line: `msbuild VPB.sln /p:Configuration=Release /p:VaMPath="C:\path\to\VaM"`

### Build

Open `VPB.sln` and build `Release`.

Post-build, the project copies the resulting DLL to:

- `$(VaMPath)\BepInEx\plugins\VPB.dll`
- `vam_patch\BepInEx\plugins\VPB.dll`

Plugin version is sourced from `plugin_version.txt`.

## License

This repository includes a `LICENSE` file (GPLv3).

## Credits

- **sFisherE** — original `var_browser` project and foundation this fork builds on.
- **Contributors to this fork** — improvements, fixes, VPM-oriented workflow changes.
