# Changelog

All notable changes to VPB are recorded here. Most recent release at the top.

## Releases

- **[v0.30, 2026-05-21](#v030)**
  - [Highlights](#v030-highlights)
  - [Added](#v030-added)
  - [Performance](#v030-performance)
  - [Fixed](#v030-fixed)
  - [Screenshots](#v030-screenshots)
  - [Internal](#v030-internal)
  - [Plans going forward](#v030-plans)
  - [Known issues](#v030-known-issues)
  - [Contributors](#v030-contributors)

---

<a id="v030"></a>
## [0.30] - 2026-05-21

<a id="v030-highlights"></a>
### Highlights

**Biggest VPB release yet: 208 commits since v0.29, 279 files touched.** The big themes: a serious performance pass (FPS up in VR, faster thumbnails, lighter memory footprint), an import-system rewrite that drops a long tail of import bugs, a tag system you can actually manage (rename, inheritance), a History tab, sort-by-usage, an in-plugin auto-updater, a redesigned VaM Hub browser that respects your installed packages despite them being hidden from VaM, and a one-time BrowserAssist migration that brings your tags and hide preferences over. 

![FPS before/after, VPB v0.29 vs v0.30](https://cdn.discordapp.com/attachments/1452368192704872558/1505381755044102284/vpb_prepost.png?ex=6a0f08c3&is=6a0db743&hm=dca809c39399d3e1ce7327c8093f8f5fd46502483d6cdfa926077afd2e874e85&animated=true)

*Red is before, blue is after. Bottom bar chart is the measured numbers.*

**Search and sort.** Pretty entry names hide internal `Preset_` / `Plugins_` prefixes. Date sorting splits into Date added (first time the package was scanned) and Date updated (latest version), with a Hide old versions toggle. Source dropdown is now global (All / Local / Var) instead of a per-category string. Two more sort modes: sort by usage count (how often you've applied an item) and sort by recency via the History tab.

**Tags.** Proper user-tag management at last: rename existing tags in place, and tags applied to a parent package now inherit down to its children automatically.

**Under the hood: one SQLite database.** The usual approach in this space is to store the state of packages, caching, etc, as flat JSON files: one per package, tags in huge files, preferences, etc. 
With a large library that turns into thousands of files scattered across your disk, and at that point the filesystem itself becomes the bottleneck. Open a file, parse it, close it, repeat: every lookup pays that cost, every filter does it again, and the plugin has to bolt on caches and bespoke workarounds just to claw the speed back. VPB collapses all of that into one SQLite database (`Cache/VPB/VpbLocalDatabase.sqlite3`) with proper indexed reads. User tags, hide preferences, History, usage counts, the package index, cache provenance, all sitting in one place with one query model and one set of indexes. It's why History, usage-based sort, and tag filters render instantly instead of after a multi-second wait, and it's why features like inheritance and cross-version tag propagation are even practical to ship.

**Scan whitelist.** VaM normally scans every package in `AddonPackages/` at startup. With a large library that becomes the dominant boot cost: hundreds of gigabytes of .var files getting opened, parsed, and indexed before you can do anything. VPB now lets you whitelist specific folders or packages, and everything outside the whitelist stays physically on disk but is hidden from VaM's startup scan. When a scene or plugin actually needs one of the hidden packages, VPB registers it with VaM on the fly so the load still works. Net result: VaM boot stays fast no matter how big your AddonPackages folder gets, while the VPB gallery and Hub browser still see the full library.

**Performance.** We doubled the FPS when VPB is loaded. Hidden gallery used to run its full Update loop, churn quick-menu icons, and draw the background panel at alpha 0 (i.e. invisible but still rendering). Now hidden costs about the same as closed: ~36 to ~110 fps in the VR test scene with the gallery hidden (~+200%). Thumbnails also load way faster: we swapped the JPEG decoder to native **TurboJPEG**, which runs off the main thread. Stutter while the gallery fills up should be much less noticeable than before, though we're not promising it's gone entirely. The texture cache got rewritten too (loads textures on-demand instead of all upfront, and prunes stale entries left behind by removed packages), and loose-file scans for scene dependencies dropped from ~813 ms to ~5 ms once warm.

**VR.** Thumbstick scroll, three docking modes (left, top, right).

---

<a id="v030-added"></a>
### Added

- Pretty entry names that strip `Preset_` / `Plugins_` prefixes and `.var` path wrappers, toggleable under Grid Labels
- Date-based sorting (Date added vs Date updated) with a Hide old versions toggle that collapses package families to their latest version
- Global source filter dropdown (All / Local / Var) with per-category overrides; your existing per-category settings migrate automatically
- Sort by description, plus search scope toggle: Path+Name / Name-only / Name-starts-with
- Suppress scale change on Appearance preset import, toggleable from the toolbox
- Everything category: unified catch-all view across all gallery content
- Ctrl+scroll to change column count on the fly
- Configurable hover border color and configurable padding / border size inside the gallery
- VR controller thumbstick scroll
- Hide panel on scene launch option
- Pin matching tags to the top of the list during search
- Disable transparency effects option
- Panel docking modes: left, top, right anchors
- VaM Quick Menu integration: 4×4 grid across 10 pages of assignable slots; wire each to a VPB action (open category, random preset, gallery show/hide/bring-front, target atom switcher, cleanup, FPS counter)
- Reworked creator dropdown menu
- In-plugin auto-updater with version check and download integration
- BrowserAssist migration: one-time import of user tags and hide preferences from a BA install, with first-boot detection and a Settings panel entry point
- Short-name aliases for Person preset subfolders (Animation, General, Morphs, Skin) and a Plugin Presets category
- Loose .vap appearance presets now show up under the correct gender filter (M / F / Futa) instead of being uncategorized
- Color picker for UI customization
- Texture cache cleanup: find and remove cache entries for packages you've deleted, run from the Toolbox Cleanup tab
- Toolbox button to manually load or unload the texture cache without restarting
- VaM Hub browser enhancements: pagination, hide-downloaded toggle, download-all, scrolling fixes, persistent Hub config, open-Hub-page action for the selected gallery item
- Dependencies filter: new filter mode and UI for browsing items by their package dependencies
- Target Person atom selection redesign: pick the import target atom from the selection context menu instead of the tab strip
- Rename existing user tags in place from the tag management UI (no more delete-and-recreate dance)
- User tag inheritance: tag a package once and the tag follows all versions of that package family automatically
- History tab: tracks recently used items, with sort and dedicated tab integration
- Usage counter: track how often an item is applied, plus a Sort by usage mode in the sort dropdown
- **Scan whitelist**: hide packages from VaM's startup scan to keep boot time fast on huge libraries. Whitelisted folders / packages get scanned normally; everything else stays on disk but skipped by VaM until a scene or plugin actually needs it (then VPB registers it with VaM on the fly). Configurable from VPB's settings, with a global plugins whitelist so MVR scripts always work
- Session-only unhide: temporarily include a scan-excluded package for the current VaM session (right-click on the item), without changing your whitelist
- VDS whitelist: drive the scan whitelist from VDS command-line args for deterministic automation runs

---

<a id="v030-performance"></a>
### Performance

- Scan whitelist for VaM startup: hide most of `AddonPackages/` from VaM's native scan, register on demand when a scene or plugin actually needs a package. VaM boot time stops scaling with library size
- Thumbnails decode through native TurboJPEG off the main thread instead of Unity's built-in image loader, so the gallery filling up should stutter much less than before
- Texture cache rewrite: textures load on demand and unload when not in use, instead of holding everything in memory at once
- Hub thumbnails pack their pixels off the main thread (Unity Jobs), so browsing the Hub stutters less
- Clicking a scene used to take ~813 ms because the dependency scan ran from scratch every time; now it's ~5 ms once the cache is warm
- Loose-file cache (scenes, presets) actually persists between plugin loads (was silently rebuilding every time before)
- Hidden gallery skips its frame-update logic entirely (was running every frame even when invisible)
- Gallery UI actually deactivates on hide: child components stop ticking, decorative graphics stop eating raycast cycles, icon sprites get reused instead of recreated every frame
- In-flight texture buffer is now capped at 100 entries / 64 MB so it can't balloon during a heavy thumbnail batch
- Saving large state files scales linearly instead of quadratically (shared StringBuilder instead of string concatenation in a loop)
- Gallery refresh consolidated: fewer redundant scans when files change on disk
- Opt-in `VPB_PERF_TELEMETRY` flag dumps a snapshot every 30 s (cache sizes, queue depths, listener counts, pool sizes, GC stats) for users helping debug FPS regressions
- Opt-in `LogSavePerf` flag breaks down scene-save timing into per-phase buckets
- Opt-in `LogPerfDiagnostics` flag emits 1 Hz frame-counter and state-transition logs

---

<a id="v030-fixed"></a>
### Fixed

- Subtle thumbnail rendering glitches at certain scale factors (output size was being rounded the wrong way)
- Futa appearance presets now classify into their correct subcategory
- Category dropdown regression
- Morphs / clothing / hair now apply on the first preset click instead of needing a second click
- Hover preview no longer gets stuck on screen
- CheesyFX no longer floods the log every frame (other plugins' diagnostics still come through)
- Gallery thumbnails no longer render with noise or stretching, especially on portrait or wide images
- Source dropdown no longer wraps around weirdly when you have a lot of items
- History category no longer shows duplicate entries
- Texture cache exclusions list is now actually respected (was silently ignored before)
- Local scenes filter no longer freezes the first time you click into it
- "Remove all clothing" button now works on the first click
- Side buttons no longer go missing in Top docked mode
- Appearance import button no longer disappears in top-anchor layout
- Toolbox buttons now scale evenly based on slot width
- VR interaction no longer broken on the latest layout
- Drag-drop styling consolidated between VR and desktop, layout corrections across the board
- Sort button rolled back to the previous behavior after a regression
- Hide / unhide working correctly again after a recent regression
- Clicks on the panel no longer pass through to the scene behind it
- Date sorts now use the right timestamps for loose .vap and scene files (mtime for updated, ctime for added)
- Settings panel cosmetics and translations cleaned up
- Thumbnail loading fixes across the general pipeline and the ALL VAR view
- BA migration: only imports your user-added tags, not the creator metadata; BA install path is cached per session; re-running the migration resets state automatically
- BA migration: handles missing FileManager state without crashing
- BA migration: no longer creates duplicate user-tag rows (regression caused by a rebase)
- Issues closed: #13, #28, #31 (regression), #34, #38, #40, #42, #48, #51, #52, #53, #57, #58, #60, #61, #62, #65, #66, #67, #74, #80, #81, #82, #84, #85, #87, #89, #91, #92, #93, #94, #95, #96, #99, #100, #101, #103, #104, #105, #106, #107, #109, #110

---

<a id="v030-screenshots"></a>
### Screenshots

#### Gallery layout

**Panel docking modes.** Three docking modes: left, top, right anchors.

![Panel docking modes](https://gist.github.com/user-attachments/assets/23e0bcb0-9b68-4308-b10d-43f49f62ca85)

**Column count via ctrl+scroll.** Ctrl+scroll inside the gallery to change column count on the fly.

![Column count via ctrl+scroll](https://gist.github.com/user-attachments/assets/34a9f58f-f5a1-4705-a972-1206011ead38)

#### Search and sort

**Date sorting and Hide old versions.** Date added vs Date updated, plus a Hide old versions toggle that collapses package families to their latest version.

![Date sorting menu](https://gist.github.com/user-attachments/assets/2c5b78f6-af12-446e-89b1-214bd3fc1c8e)

![New vs Updated dropdown](https://gist.github.com/user-attachments/assets/0634d5c2-e1f5-4336-a9dc-091b04708b69)

**Pretty entry names.** Grid Labels toggle hiding internal `Preset_` / `Plugins_` prefixes.

![Pretty entry names](https://gist.github.com/user-attachments/assets/cf9dfae4-880b-486e-a83d-acdb81343fc4)

**History tab.** Recently used items, with sort and a dedicated tab.

![History tab](https://gist.github.com/user-attachments/assets/a2333077-05c5-416b-9b7c-d98c84dab050)

#### Tags

Tag management UI, rename in place, and inheritance flowing through package versions.

![Tag management UI](https://github-production-user-asset-6210df.s3.amazonaws.com/43590729/595834024-5bcd3892-2c9c-418b-8bbe-2c4e90046ad7.png?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=AKIAVCODYLSA53PQK4ZA%2F20260521%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260521T025730Z&X-Amz-Expires=300&X-Amz-Signature=d6a7b9ac2fefa8cdaacb2b6d43c444abac5b00eae8a1bb4c4731038ec6f5ea69&X-Amz-SignedHeaders=host&response-content-type=image%2Fpng)

![Tag rename](https://gist.github.com/user-attachments/assets/bf46bb67-1272-4b65-98f6-d0a4b85bb03d)

![Tag inheritance applying through children](https://gist.github.com/user-attachments/assets/387f512c-413d-4111-b53e-4ecad6c70be5)

![Tag filter chip](https://gist.github.com/user-attachments/assets/53873186-8a51-4dce-892e-dd9b83a10957)

#### Toolbox

**Suppress-scale toggle.** Toolbox button to suppress scale change on Appearance preset import.

![Suppress-scale toolbox button](https://gist.github.com/user-attachments/assets/594b5629-c66a-4dfb-88a6-92a30458987b)

**Load / unload texture cache.** Manually load or unload the texture cache without restarting VaM.

![Texture cache view](https://private-user-images.githubusercontent.com/43590729/595826203-17bdcf8d-2fa8-4e44-af05-99d9c6dadb17.png?jwt=eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJpc3MiOiJnaXRodWIuY29tIiwiYXVkIjoicmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbSIsImtleSI6ImtleTUiLCJleHAiOjE3NzkzMzI1MzYsIm5iZiI6MTc3OTMzMjIzNiwicGF0aCI6Ii80MzU5MDcyOS81OTU4MjYyMDMtMTdiZGNmOGQtMmZhOC00ZTQ0LWFmMDUtOTlkOWM2ZGFkYjE3LnBuZz9YLUFtei1BbGdvcml0aG09QVdTNC1ITUFDLVNIQTI1NiZYLUFtei1DcmVkZW50aWFsPUFLSUFWQ09EWUxTQTUzUFFLNFpBJTJGMjAyNjA1MjElMkZ1cy1lYXN0LTElMkZzMyUyRmF3czRfcmVxdWVzdCZYLUFtei1EYXRlPTIwMjYwNTIxVDAyNTcxNlomWC1BbXotRXhwaXJlcz0zMDAmWC1BbXotU2lnbmF0dXJlPTJmNjZjZjRiM2MwYWYwZjEyY2FkNDQyNTQ2OWNlNzI2NWFiNzdiY2U5MGMzODI0YWIxNTc0MmI5YjhhMGZmZGYmWC1BbXotU2lnbmVkSGVhZGVycz1ob3N0JnJlc3BvbnNlLWNvbnRlbnQtdHlwZT1pbWFnZSUyRnBuZyJ9.OrC0Z9S-nIIQQsHNcyuErk8i4FKAiSeVJu3XZlXskSk)

#### Scan whitelist

**Scan whitelist settings.** Pick which folders or packages VaM is allowed to scan at startup; everything else stays on disk, hidden, and gets loaded on demand.

![Scan whitelist settings](https://gist.github.com/user-attachments/assets/cab664e2-391e-4f15-b518-4b4b5826c0c2)

#### VaM integration

**VaM Quick Menu assignable grid.** 4×4 grid across 10 pages of VPB actions wired into VaM's Quick Menu.

![Quick Menu assignable grid](https://gist.github.com/user-attachments/assets/f9c60f85-35ba-4a0a-ba07-b6818c34f6f2)

![Quick Menu slot detail](https://gist.github.com/user-attachments/assets/8058e229-04cf-459b-9e59-5168e3e56d56)

#### VaM Hub browser

**Pagination and hide-downloaded.** Pagination, hide-downloaded toggle, download-all, and persistent Hub config.

![Hub pagination and hide-downloaded](https://gist.github.com/user-attachments/assets/2adec068-7f1d-4374-9912-12acb48f84d8)

![Hub browser overview](https://gist.github.com/user-attachments/assets/9b154e7b-35cb-4a2d-8c97-0a5e50d10e6d)

#### Settings

**Reorganized settings panel and color picker.** Settings are now inside the gallery panel; the color picker drives hover-border and accent colors.

![Reorganized settings panel](https://gist.github.com/user-attachments/assets/56629f2c-ddfd-4b15-90d5-cf876fef8253)

![Color picker for outlines](https://gist.github.com/user-attachments/assets/08a76601-811e-45e1-a584-289508e05984)

#### Auto-updater

**Auto-updater UI.** Version check and one-click update from inside the plugin.

![Auto-updater UI](https://gist.github.com/user-attachments/assets/58e865d6-5e2d-4172-8ea8-ca5a76957df8)


---

<a id="v030-internal"></a>
### Internal

- `VpbImport` unified preset routing: single `LoadPreset` entry point for Clothing, Hair, Pose, Appearance, ClothingItem, HairItem with resource-type and apply-mode enums, deletes ~770 lines of duplicate code across import paths
- Gallery user tag SQLite APIs and BA migration infrastructure (`TryGetCategoryForItem`, `BulkMergeGalleryUserTags`, `RemoveGalleryUserTagsForItem`, `GalleryUserTagImportRow`)
- `PackageHidePrefs` VarPackage overload helpers
- `CategoryFilterState` legacy per-category source-filter string migration
- New `src/util/AppearancePresetSuppress.cs` for the suppress-scale toolbox toggle
- New `src/util/LooseVapGenderProbe.cs` for loose `.vap` gender classification
- New `src/hook/TurboJpegNative.cs` and `src/hook/TurboJpegStats.cs` for the native JPEG decode path
- New `src/gallery/CacheCleanupManager.cs` for stale texture-cache pruning, with `VpbLocalDatabase` schema extensions to track cache provenance
- New `src/gallery/GalleryPanel.Toolbox.LoadUnloadCache.cs` for the on-demand cache toolbox button
- New `src/ScanWhitelistManager.cs` (458 lines) plus `src/hook/FileManagement/VamOnDemandLoader.cs` (327 lines) and `src/hook/FileManagement/VamScanFilter.cs` (105 lines) for the scan-whitelist mechanism; routes through `SuperControllerHook` and the existing `FileManager` patch surface
- Startup path refactor: extensive changes to `FileManager`, `VamOnDemandLoader`, `Gallery`, and `JSONExtensions` (~720 lines added) to improve plugin boot behavior
- User tag persistence extracted into its own `VpbLocalDatabase.GalleryUserTags.cs` partial (1206 lines moved out of the main DB module)
- SQL layer reworked: ~285 lines of additions to `VpbLocalDatabase.cs` for query optimization and new schema work
- History and usage-counter schemas added to `VpbLocalDatabase` with indexed reads on hot paths
- Settings panel reorganized: `SettingsPanel.cs` (1651 lines) removed, settings are now in `GalleryPanel.SettingsInternal.cs` (859 lines new) accessible from the gallery panel itself
- `GalleryPanel.Tabs.cs` split from 1809 lines into 8 topical partials (`Tabs.Builders.Core`, `.Misc`, `.Remove`, `.Tags`, `.UserTags`, `.Helpers`, `.Layout`)
- Thumbnail / preview pipeline rework: ~280 lines in `GalleryPanel.Thumbnails.cs`, ~210 in `CustomImageLoaderThreaded.cs`, plus updates across `GalleryThumbnailCache`, `Behaviours`, and `FileManager`

---

<a id="v030-plans"></a>
### Plans going forward

- **Keep hunting performance wins.** The doubled-FPS pass closed the biggest hidden-gallery hole and TurboJPEG took a bite out of thumbnail stutter, but there's still slack: scan paths on huge libraries, scene-save overhead, settings-menu open time, the boot-pause window when VPB initializes. We'll keep instrumenting and trimming.
- **Close the remaining import edge cases.** The unified preset routing in this release cleaned up most of the long-tail import bugs, but a few (CUA-style clothing, certain pose chains, subscene vs appearance-flow consistency) still need work. Known Issues below has the specifics.
- **Stay easy to contribute to.** We split the god-files, cleaned up the schema layer, pulled settings out of a 1651-line monolith. Continuing that pattern means a new contributor can read one partial and understand it without a week of orientation.
- **More VPB, less VPB.** The plugin should fade into the background when you're not using it (the hidden-gallery FPS fix is a step in that direction) and feel native to VaM when you are. Custom panels, custom Quick Menu actions, custom hotkeys: more of that, less competing with VaM's own UI.
- **Act on the feedback we get.** Every issue gets read. The bug reports and feature requests from this release window are what shape the next one. If you have opened an issue, we have not forgotten.

---

<a id="v030-known-issues"></a>
### Known issues

What's still rough or broken in v0.30 and we know about it. If you're hitting something that isn't on this list, please file an issue.

**Performance**

- Saving a scene with VPB loaded takes longer than vanilla. ([#33](https://github.com/gicstin/VPB/issues/33))
- Long pause at boot after VPB starts initializing, before VaM and VPB are interactive. ([#19](https://github.com/gicstin/VPB/issues/19))

**Import / Apply**

- "Source" button on the toolbox doesn't do anything yet. ([#41](https://github.com/gicstin/VPB/issues/41))
- Loading Appearance with Keep mode doesn't behave as expected. ([#43](https://github.com/gicstin/VPB/issues/43))

**UI / UX**

- Date Updated sort doesn't actually sort by Date Updated. ([#45](https://github.com/gicstin/VPB/issues/45))
- Overwriting a scene file doesn't refresh its thumbnail in the gallery. ([#44](https://github.com/gicstin/VPB/issues/44))
- Filter Preset popup goes missing in Anchored mode. ([#11](https://github.com/gicstin/VPB/issues/11))
- Tag counts sometimes show (0) when they shouldn't. ([#26](https://github.com/gicstin/VPB/issues/26))
- Tooltip text runs too long in some places. ([#23](https://github.com/gicstin/VPB/issues/23))
- Settings for Left/Right list stops working after toggling. ([#24](https://github.com/gicstin/VPB/issues/24))

**Plugin interactions**

- Altfuta on a female Person can confuse VPB's gender tracking. ([#6](https://github.com/gicstin/VPB/issues/6))
- Hair thickness doesn't auto-update when used with GiveMeFPS. ([#16](https://github.com/gicstin/VPB/issues/16))
- Hair / scalp loading edge case. ([#18](https://github.com/gicstin/VPB/issues/18))

**Hub / Cache**

- VaM Hub browser sometimes doesn't load all thumbnails. ([#22](https://github.com/gicstin/VPB/issues/22))
- Compressed texture cache occasionally produces a glittery / shimmering look. ([#37](https://github.com/gicstin/VPB/issues/37))
- "New Added" filter doesn't always show newly-downloaded Hub items in the same session. ([#35](https://github.com/gicstin/VPB/issues/35))

If a bug here is blocking you, react with a 👍 on the issue so we can prioritize. New reports always welcome at [`gicstin/VPB`](https://github.com/gicstin/VPB/issues).

---

<a id="v030-contributors"></a>
### Contributors

Thanks to everyone who contributed, be it with code, issues, live-testing, or pushback when something was not right.

**Code in this release:**

- [@gicstin](https://github.com/gicstin): 165 commits, +58,211 / -16,807 lines
- [@hardtokidnap](https://github.com/hardtokidnap): 32 commits, +7,596 / -2,845 lines
- [@mcmerdith](https://github.com/mcmerdith): 8 commits, +1,267 / -428 lines
- [@mcmalt](https://github.com/mcmalt): 3 commits, +380 / -184 lines

**Testing, bug reports, feedback:**

> VPB's GitHub repo moved during this release cycle from an attached fork to a standalone repository. The legacy issue tracker is at [`gicstin/VPB-old`](https://github.com/gicstin/VPB-old/issues); new issues are in [`gicstin/VPB`](https://github.com/gicstin/VPB/issues). Counts below combine both trackers.

- [@VamMoose](https://github.com/VamMoose): 51 issues
- [@femdogg](https://github.com/femdogg): 42 issues
- [@hardtokidnap](https://github.com/hardtokidnap): 33 issues
- [@BStriker2](https://github.com/BStriker2): 7 issues
- [@mcmalt](https://github.com/mcmalt): 6 issues
- [@gicstin](https://github.com/gicstin): 5 issues
- [@Earthplayer1](https://github.com/Earthplayer1): 1 issue
- [@JustinRoper](https://github.com/JustinRoper) (Justin Roper): 1 issue
- [@kakhat12345](https://github.com/kakhat12345): 1 issue
- [@mikeriko](https://github.com/mikeriko): 1 issue
- [@Nohasbeencancelled](https://github.com/Nohasbeencancelled): 1 issue
- + many more in Discord.

