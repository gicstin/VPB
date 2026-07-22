# VBP Gallery Help

Tip: open this panel with **?** on the title bar. Use the search box to find topics. Colored icon links in the text show a preview when you hover them.

## Filtering

### Pick what you are browsing
1. Click the **category name** at the top-left of the title bar (Scenes, Looks, Clothing, and so on).
2. Pick a category from the menu.

Or tap the red **Category** side button — {{icon:category|Category}} grid icon on the left or right rail. The blue folder icon is {{icon:path|Path}}, not Category.

Some categories use two columns:
- **Main list** — packages or presets in that category.
- **Sub-list** — extra filters (tags in Clothing/Hair, scene sources in Scenes, etc.).

### Side lists
Open a side column, then search or sort inside it:

- {{icon:creator|Creator}} — green icon; filter by package author.
- {{icon:path|Path}} — blue folder; browse by file path. Folder list shows all known package folders; **counts** match the **current category** (folders with `0` stay listed, dimmed).
- {{icon:history|History}} — purple clock; recently used items.

**Right-click** any side-rail button to open the same panel on the **opposite** edge.

Tap the **colored header bar** at the top of a side column to collapse that list.

### Title bar search and filters
- **Search** (center) — type a term and press **Enter** to commit a **filter chip**. Compact mode shows a {{icon:search|search icon}}; click to type.
- **Include / Exclude** rows appear under search when chips are active. **Drag** a chip to **Exclude** to negate tags/bare terms. Drag tags from the side Tags list onto Include/Exclude.
- Empty draft: **Backspace** removes last chip. **ESC** closes search without clearing chips.
- Bare words, tags, badges, loaded/starred, **AND** / **OR** / **IF** — full syntax in **Advanced search** (help nav).
- **Side-column search** — filters only the **open list** (categories / creators / tags / paths), not the grid.
- {{icon:filter|Filter presets}} — funnel icon; save the current filter setup or load a saved preset.
- **Creator filter** (title bar) — multi-select authors to limit the **grid** (or **…** overflow when narrow). Rail {{icon:creator|Creator}} opens the **creator list**.
- **Category** — title name/menu = **quick switch**; rail {{icon:category|Category}} opens the **full category list**.
- **Source filter** — All, Local, or VAR packages.
- {{icon:star|Rated only (★)}} — show only starred items.
- **Sort** — **Az** opens sort type; arrow toggles ascending/descending.
- {{icon:refresh|Refresh}} — reload the list.

When filters are active, the **footer** shows **Back** (undo one step) and **Clear filters** (reset all). If nothing matches, the grid may offer **Clear search** or **Clear filters**.

### Narrow title bar
On small widths, language, presets, creator, source, ★, and FPS move into the **…** overflow menu next to Settings.

### Narrow footer bar
When the footer runs out of room, chips collapse one-by-one into the **…** menu (utilities → follow → zoom → quality → Hub/Undo). Extreme narrow: only **…** remains (plus resize grips / filter Back·Clear). Menu rows show icons; the panel shifts inward if it would clip the edge.

### Short side rails
When pane height is tight, side-rail **zone gaps close first**, then spacing packs flush, then chips move into a side **…** menu (Floating/Fixed stays). Same fit applies to the top-dock footer side-button strip.

### Language
The language button on the title bar (e.g. EN) switches UI text. Help content loads from `vpb_help/{language}.md` when that file exists, otherwise English.

## Advanced search

Title-bar search is a **universal filter**. Bare words match name/path/creator/uid **or** user-tag text.

### Filter chips (Enter to commit)
- Type `@creatorA` then **Enter**, then `#tagB` then **Enter** — each becomes its own chip under **Include**.
- **Shift+Enter** — commit draft to **Exclude** (bare → broad exclude `-term`; `#tag` → `-#tag`).
- Paste a full query (`#wet -nsfw -#old @Acid`) and press **Enter** — tokens explode into chips.
- **Exclude** row stays visible whenever any chip exists (hint: *Drop chip here to exclude*).
- **X** left of Include/Exclude — clears **all** search chips (and draft). Same as search-field clear. **Ctrl+Z** within 5s undoes.
- **Ctrl+F** — focus title search (opens compact popup if needed) and select draft text. Brief highlight cues open. Popup stays until **Esc**, click outside (not chip rows), or click the compact search icon again.
- **Drag** a chip onto **Exclude** / **Include** to change polarity (tags and bare terms). Chip **×** removes it.
- **Backspace** on an empty draft removes the last chip. **ESC** closes the search field without clearing chips. Search **X** clears all search chips.
- Drag a tag from the **Tags** side list (or detail tag chip) onto Include/Exclude to add a search chip (does not apply the tag to items).

### Boolean logic (AND / OR / IF)

- **AND** (default) — every term in a group must match. Space or the word `AND` means the same thing: `dress AND #wet` ≡ `dress #wet`.
- **OR** — either side may match: `tag:wet OR tag:shiny`. Groups on each side of `OR` are still AND inside: `dress #wet OR #shiny` = (dress and #wet) or (#shiny).
- **IF** — optional preface before a status/badge word. Same as writing the status alone: `IF loaded` ≡ `loaded`. Use it when you think “only IF this is true…”.

### Text and tags
- Bare words — `dress man` (each word must match somewhere; AND)
- **Broad exclude** — `-wet` (must **not** match name/path/creator/uid **or** user-tag text). Chip Exclude / Shift+Enter on a bare word uses this.
- User tag — `tag:wet` or `#wet`
- **Multiple tags** (comma list) — `tag:wet,shiny,-nsfw` or `#wet,#shiny,-nsfw` or shorthand `wet,shiny,-nsfw`
  - plain name = must have that tag
  - `-name` = must **not** have that tag
- Exclude tag — `-tag:nsfw` or `-#nsfw` (user-tag only; different from bare `-wet`)
- Creator — `creator:Acid` or `@Acid` (comma list OK: `@Acid,@Other`)

### Status and badges
Grid letter badges: **A** auto-install, **H** hidden, **W** scan-whitelist excluded, **T** user tags.

- `loaded` / `installed` — under AddonPackages (or local loose files)
- `unloaded` — not loaded (e.g. AllPackages)
- `starred` / `rated` / `badge:star` — has a star rating
- `tagged` / `badge:t` — has user-tag badge **T**
- `untagged` — no user tags
- `autoinstall` / `badge:a` — auto-install badge **A**
- `hidden` / `badge:h` — hide badge **H**
- `whitelist` / `badge:w` — scan-excluded badge **W**

### Examples
- `tag:fav,outfit,-nsfw` — has fav and outfit tags, not nsfw
- `#wet OR #shiny` — either tag
- `dress AND IF loaded` — name/path contains dress, and package is loaded
- `tag:fav loaded` — fav tag and installed
- `badge:a starred` — auto-install + rated
- `@Acid OR @Other` — either creator

Combine freely with category, creator chips, and {{icon:filter|Filter presets}}.

For **recently used** items, use the {{icon:history|History}} side view (not title-bar search).

## Tags

Open {{icon:tags|Tags}} on the side rail — **teal** button **above** Category.

At the top of the Tags column, pick a mode:

- **Tag** — assign your tags to packages (Applied / Available lists).
- **Filter** — filter the grid by selected tags (AND/OR in Settings → Gallery side lists).
- **Filter untagged** — show only items with no user tags.

Use **Edit** in the Tags column header to rename, merge, or delete tags.

**Copy tags between items** — on the detail strip tags row: **Copy Tags** from a source item (or multi-select for a union), select targets, then **Paste Tags** (merge) or **Replace Tags** (overwrite). **Shift+Paste** also replaces. With a mixed multi-selection, **Stamp from first** applies the first item’s tags to the rest in one click.

## Import

**Scenes category only.**

{{icon:import|Import}} on the side rail — blue icon **above Tags**.

- **Left-click** — open import sidebar on the same side as the button.
- **Right-click** — open it on the **opposite** side.

### Import sidebar steps
1. Select **exactly one scene** in the grid (multi-select blocks import).
2. **Package** — which scene file to use (follows grid selection).
3. **Atoms** — source person from the file and target person in the scene.
4. **Resource type** — Appearance, Clothing, Hair, Plugins, etc.
5. **Options** — merge/replace, clothing and plugin toggles, CUA cleanup.
6. **Apply** — pinned button at the bottom runs the import.

Tap a step header to collapse or expand that block. The sidebar header summarizes your current choices.

## Cleanup

Open **Cleanup** from the title-bar category menu.

The side column lists cleanup views: all, duplicates, old versions, damaged, stale cache, excluded.

Select rows, then use the **toolbox** at the bottom for cleanup actions: filter tabs, select visible/duplicates/old/damaged, add or remove exclude list, delete, and more. Normal toolbox buttons are replaced while Cleanup is active.

## Selection

### Select items in the grid
- **Click** — select one row (clears others unless Ctrl or Shift is held).
- **Ctrl+click** — add or remove a row from selection.
- **Shift+click** — select a range from the last anchor.
- **Ctrl+A** — select all visible rows.
- **Escape** — clear selection when no menu is open.

Select one or more rows to expand the **toolbox** at the bottom (hover the bar if it is collapsed).

**Detail strip** — select a row for an info card above the toolbox: thumb + status badges (A/H/W/T), facts, and clickable chips. Collapse with the chevron left of the item name; expand again from the Details button (top-left in the toolbox action row). Hover the preview thumb: scroll wheel steps selection; hold right-click and scroll to raise/lower the star rating; double-click to launch or apply. Drag the thin bar at the top of the strip to resize height (preview stays square; path/desc/tags hide when short). Settings → Visuals → **Detail preview side** puts the image left or right. Preferences are remembered.
- **Description & package tags** — wide+short pane uses a side column (scrollable description + native tags). Tall strip moves those into regular rows under actions; narrow pane keeps a short description row. Turn off **Show description & package tags** in Settings → Visuals to hide them.
- **D / M / Dn** — filter grid to dependencies / missing / dependents (hover for tip).
- **Creator** — filter by creator.
- **Tag** or tags line — quick-tag menu (Applied | Add). Filter box scopes **Add** list only. Sort button on Add cycles A→Z / Z→A / count 1→9 / 9→1 (remembers). Remove with ✓, or **New tag…**.
- **Copy Tags / Paste Tags / Replace Tags** — on the tags row: copy user tags from the selection, then paste (merge) or replace onto other items. **Shift+Paste** also replaces. When multiple items have different tags, **Stamp from first** merges the first item’s tags onto the whole selection.
- **Copy** or path / title — copy path or display name.
- Badges match grid meaning (auto-install, hidden, scan-excluded, has tags).

### Common toolbox actions
Availability depends on category and selection:

- {{icon:delete|Delete}} — remove eligible items.
- {{icon:load|Load}} / **Unload** — load or unload `.var` packages from memory.
- **Load deps** — load dependency packages for the selection.
- {{icon:select_all|Select all visible}} — select every row on the current page/filter.
- **Copy package names** — copy selected `.var` names to the clipboard.
- **Hide / Unhide** — hide from normal browse; footer **H** shows hidden packages again.
- **Autoinstall / Clear autoinstall** — mark packages for VaM autoinstall or clear it.
- **Scan whitelist (temp)** — temporary access when scan whitelist blocks a package.
- {{icon:cache_texture|Cache Textures}} — build zstd texture cache (see Advanced).
- {{icon:hub|Open on Hub}} — open selected item in Hub when available.
- **Overwrite scene** — Scenes only; save over an existing scene file.
- **Star rating** — rate packages (works with ★ filter on title bar).
- **Remove from History** — removes History entries only; does not delete files.

The **Target** dropdown in the toolbox picks which **Person** atom receives presets, clothing, hair, and imports.

### Scene helper side lists
Lower side-rail buttons help edit the open scene (not the package grid):

- **Remove clothing** — find and remove clothing on persons.
- **Remove hair** — remove hair items.
- **Remove atom** — remove atoms from the scene.
- **Target** — pick or filter target persons for apply operations.

## Layout

### First-time setup
A short **setup wizard** may appear on first use: dock edge (fixed desktop), default side lists, grid density. **Skip** anytime. Change later in {{icon:settings|Settings}}.

A **tip strip** under the title bar shows basics until you dismiss it (×).

### Grid vs list
- {{icon:layout_grid|Grid}} / {{icon:layout_list|List}} (footer) — thumbnails vs compact rows.
- **+ / −** (footer) or **Ctrl + mouse wheel** over the gallery — change column count.

### Panel position and size
- **Floating** — drag the panel; corner handles resize. Footer mode button toggles fixed dock.
- **Fixed dock** — pins to Left, Right, or Top (footer dock button cycles edge).
- **Height** (fixed dock) — footer ↕ toggles full height vs adjustable strip.
- **Auto-hide** — side rails and footer can hide when the pointer leaves the panel.
- **Follow** (footer) — angle, distance, and eye height keep the panel facing you.

### Side rails and Settings
- Side lists open from rail buttons in three clusters: **Layout** (float/follow/clone), **Browse** (import/tags/category/creator/path/history), **Tools** (remove/apply/save). Thin separators mark zones when side-button gaps are on.
- Open facet shows a brighter selection rim (plus header label on the column).
- **Right-click** a rail button to open that panel on the opposite edge.
- {{icon:settings|Settings}} side tab — appearance, layout, browse, input, hotkeys, performance, plugin options.

### Footer utilities
- **U / R** — Undo / Redo
- **Rdm** — load random item
- {{icon:hub|Hub}} — Hub browse panel
- **M** — VaM menu gate; hide gallery when VaM menu is closed
- **H** — show hidden packages
- **Spring scroll** — large scroll drag button on the scrollbar
- {{icon:hold|Hold-to-launch}} — hold click on a thumbnail to apply (see Save and Apply)

Pagination sits at the bottom-left when the list has multiple pages.

## Save and Apply

### Target person
The **Target** menu in the toolbox chooses which **Person** atom gets appearances, clothing, hair, poses, and scene imports.

### Applying from the grid
- **Single-click** or **double-click** — set in Settings → Browse → Interaction.
- {{icon:hold|Hold-to-launch}} (footer) — hold mouse or controller on a thumbnail to apply.
- **Replace vs merge** (side rail in appearance categories) — toggles whether clothing/hair replaces or merges.

### Drag and drop
Settings → Browse → **Enable drag & drop** (off by default). Drag thumbnails onto persons or the scene. The replace/merge side button affects drop behavior.

### Saving
**Save** on the side rail — preset and scene save flows for the active category.

Use {{icon:undo|Undo}} / **Redo** in the footer after supported edits.

## Hotkeys

Change defaults in **Settings → Hotkeys**:

- **Ctrl+V** — show / hide gallery
- **Arrow keys** — move selection in the grid
- **Ctrl+A** — select all visible items
- **Delete / Backspace** — delete eligible selection
- **Ctrl+Z / Ctrl+R** — undo / redo
- **Ctrl + mouse wheel** — grid columns (+ / − in footer too)
- **Ctrl+Alt+= / Ctrl+Alt+-** — gallery UI scale up / down (also keypad +/-; separate from Ctrl+scroll grid zoom)
- **Escape** — close menus, search popup, help panel
- **?** (title bar) — open this help

### VR
- **VR hover tooltips** (Settings) — short hover shows control labels.
- Hover the toolbox to expand it when items are selected.

## Advanced

### On-demand texture cache
Select `.var` packages and/or **local scene JSON**, then {{icon:cache_texture|Cache Textures}} in the toolbox.

VPB scans preset references and builds **Zstd** cache (`.zvamcache`). Progress shows in the overlay.

- **Ctrl+click** — rebuild zstd even if cache already exists
- **Ctrl+Shift+click** — purge VPB texture cache for the selection

With **Enable Zstd compression** (BepInEx / Settings), runtime loads prefer zstd over legacy `.vamcache`.

### Bulk Zstd migration
{{icon:settings|Settings}} → **Plugin** → {{icon:compress|Compress Cache}} — convert legacy `.vamcache` to zstd in bulk.

Plugin options include compression level, delete original after success, and optional 8K→4K downscale before caching.

### Package on-demand registration
When a preset references a `.var` VaM has not scanned, VPB can **register that package on demand** (common with scan whitelist). Happens during preset load, import, or missing-path fix — batched to avoid repeated full scans.

### Scan whitelist
{{icon:settings|Settings}} → **Plugin** → **Enable VaM scan whitelist** limits VaM startup scan folders. VPB's index still sees local `.var` files. **Manage Scan Whitelist** adds folders or per-package overrides. Other packages load when first referenced.

### Performance quality
{{icon:settings|Settings}} → **Performance** — optional quality steps adjust physics, hair, mirrors, MSAA, and related options. Native runtime patches can be toggled per category.

### Hub mode
Footer {{icon:hub|Hub}} or toolbox **Open on Hub** opens Hub browsing. Grid and toolbox adapt in Hub context.

### Refresh and cache tips
- One on-demand cache job at a time — wait for the overlay to finish.
- Scene import may prewarm cache for source packages.
- Click {{icon:refresh|Refresh}} after large cache or whitelist changes if the grid looks stale.
- **Manual refresh only** (Settings) — grid updates only when you press Refresh.
