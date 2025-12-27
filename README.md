# VPB — VaM Package Browser

VPB is a **fork** of the venerable **`var_browser`** project by **sFisherE**.

- **Upstream project**: `var_browser` by sFisherE (add link)
  - TODO: https://github.com/<sFisherE>/<var_browser>
- **This fork**: VPB — focused on working great alongside **VPM** workflows and adding quality-of-life + performance/automation features.

## What this is

VPB is a **BepInEx plugin for Virt-A-Mate (VaM)** that provides an in-game package/browser experience and related tooling.

This repo builds a `var_browser.dll` plugin (historical naming preserved for compatibility).

## Screenshots

Replace the placeholders below with your own images.

<!-- TODO: Add screenshot of the main VPB window -->
<!-- ![VPB Main Window](docs/images/vpb-main-window.png) -->

<!-- TODO: Add screenshot of the Hub Browse / package details view -->
<!-- ![Hub Browse](docs/images/vpb-hub-browse.png) -->

<!-- TODO: Add screenshot showing texture/cache/optimization settings -->
<!-- ![Optimization Settings](docs/images/vpb-optimization.png) -->

## Features (high-level)

- **In-game package browsing**
- **Hub browsing integration**
- **Favorites / convenience workflows**
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

- TODO: Add VPM repository URL / package ID here
- TODO: Add minimal “Add to VPM” steps here

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
- `ScenePrewarmTexturesPerFrame` (int)
- `ScenePrewarmIncludeThumbnails` (bool)
- `SceneLoadWaitForImagesIdle` (bool)
- `SceneLoadImagesIdleSeconds` (float)
- `UIScale` (float)
- `UIPosition` (`x,y` as `Vector2`, e.g. `120,80`)
- `MiniMode` (bool)
- `CleanLogEnabled` (bool)
- `CleanLogPath` (string)

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
