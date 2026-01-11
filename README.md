# VPB — VaM Package Browser

VPB is a **fork** of the venerable **`var_browser`** project by **sFisherE**.

- **Upstream project**: `var_browser` by sFisherE
- **This fork**: VPB — focused on working great alongside **VPM** workflows and adding quality-of-life + performance/automation features.

## What this is

VPB is a **BepInEx plugin for Virt-A-Mate (VaM)** that provides an in-game package/browser experience and related tooling.

This repo builds a `var_browser.dll` plugin (historical naming preserved for compatibility).

## Screenshots

<img width="484" height="648" alt="image" src="https://github.com/user-attachments/assets/27cd37f7-ee1f-4e53-b9ad-69e433e17d84" />

<img width="477" height="852" alt="image" src="https://github.com/user-attachments/assets/3c6943e2-a07a-48e2-a781-3d0f803ab674" />

<img width="499" height="935" alt="image" src="https://github.com/user-attachments/assets/27937d2d-ae84-4b83-8d0a-99750364a80a" />

<img width="520" height="225" alt="image" src="https://github.com/user-attachments/assets/262f1af7-f6f8-4553-94a2-d6d3e53308f8" />

## Features (high-level)

- **In-game package browsing**
- **Hub browsing integration**
- **0-5 Star Rating System**
- **Performance features** (configurable)
  - Texture downscaling + persistent cache
  - AssetBundle cache
  - In-flight request de-duplication (optional)
  - Prioritization heuristics for face/hair textures (optional)
  - Scene texture pre-warming (optional)
- **Automation / advanced launch support** via **VDS mode** (see below)

## Installation

### Option A: VPM (recommended)

This fork is intended to work best in combination with **VPM**.

https://github.com/gicstin/VPM

### Option B: Manual install

1. Build or obtain `var_browser.dll`.
2. Copy it to:
   - `VaM\BepInEx\plugins\var_browser.dll`
3. Start VaM.

## Usage

### Show / hide UI

Default hotkey (from config):

- `Ctrl+V`

You can change this in the BepInEx config for the plugin.

### Session plugin (optional)

This repo also contains a **VaM session plugin script** you can use to trigger common VPB actions from a UI panel:

- `Custom/Scripts/var_browser/VarBrowserSessionPlugin.cs`

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
- `CleanLogEnabled` (bool)
- `CleanLogPath` (string)

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
  - Optional clean log path (default): `BepInEx/LogOutput/var_browser_clean.log`

## Building from source

### Requirements

- Visual Studio (the solution targets VS 2019 format)
- .NET Framework **3.5** (project targets `v3.5`)
- A local VaM install folder (for reference assemblies)

### Configure VaM path

The project uses an MSBuild property called `VaMPath`.

- Default: `C:\\vam`
- You can set it by editing `var_browser.csproj` or overriding the MSBuild property when building.

### Build

Open `var_browser.sln` and build `Release`.

Post-build, the project copies the resulting DLL to:

- `$(VaMPath)\BepInEx\plugins\var_browser.dll`
- `vam_patch\BepInEx\plugins\var_browser.dll`

Plugin version is sourced from `plugin_version.txt`.

## License

This repository includes a `LICENSE` file (GPLv3).

## Credits

- **sFisherE** — original `var_browser` project and foundation this fork builds on.
- **Contributors to this fork** — improvements, fixes, VPM-oriented workflow changes.
