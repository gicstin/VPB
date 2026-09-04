# VBP Gallery Help

Tip: press **{key:help_alt}** or **{key:help}** to open **Hotkeys** (Esc closes). Title-bar help button opens the full searchable panel. Colored icon links in the text show a preview when you hover them.

## Filtering

### Pick what you are browsing
1. Click the **category name** at the top-left of the title bar (Scenes, Looks, Clothing, and so on).
2. Pick a category from the menu.

Or tap the red **Category** side button — {{icon:category|Category}} grid icon on the left or right rail. The blue folder icon is {{icon:path|Path}}, not Category.

**Item labels** — Under each square thumbnail: **leaf** left + **creator** right on the first line; **package** on the second (opaque bar). Cell grows taller so the image stays **1:1**. When old package versions are shown, package line uses **`package.vN`**. Single-name rows keep one line (+ creator when known). Tooltip / detail still show full `creator.package.version:/path`.

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
- Empty draft: **Ctrl+Backspace** removes last chip. **ESC** closes search without clearing chips.
- Bare words, tags, badges, loaded/starred, **AND** / **OR** / **IF** — full syntax in **Advanced search** (help nav).
- **Side-column search** — filters only the **open list** (categories / creators / tags / paths), not the grid.
- {{icon:filter|Filter presets}} — funnel icon; save the current filter setup or load a saved preset. **{key:filter_presets}** toggles the **floating** presets window (detaches if needed; **hide keeps float position**). Top of list: **Save Preset** | **Update** (when a preset is active) | **Merge to New**, then **Float as Window** when docked. Search presets by name; **sort** cycles Manual / A→Z / Z→A / Pinned first. Float chrome: **chevron** collapses body (collapsed title shows **pinned Dice** shortcuts) / expands; **X** hides and keeps float; footer **Dock** reattaches under the title chip; footer **Undo** appears briefly after deleting a preset (**U** also undoes). Title-bar funnel still toggles visibility. **Merge to New** enters select mode (2–6 presets); **Create** names the result from selected names joined with `+` — **click** applies all member filters as one OR browse; **dice** loads a random item from each member in order, then restores your view. Side **Replace/Add** applies once per clothing/hair family when dice runs a merged set. Pinned presets sit at the top and appear in title **…** as **Apply:** / **Rnd:**. Recent applies appear as **Recent:**. Row **●** marks the active preset; **●*** means filters drifted — use **Update** (or **Ctrl+S**). **More** (edit icon / right-click) shows **pin**, **inline rename**, and **delete** (Confirm / Cancel on the row; Esc cancels; soft-undo for a few seconds). Dice **randomizes** without closing the list. Keyboard while open: **↑↓** select, **Enter** apply, **D** dice, **Ctrl+S** save/update, **Esc** cancel mode then hide.
- **Creator filter** (title bar) — multi-select authors to limit the **grid** (or **…** overflow when narrow). Rail {{icon:creator|Creator}} opens the **creator list**.
- **Category** — title name/menu = **quick switch**; rail {{icon:category|Category}} opens the **full category list**.
- **Source filter** — All, Local, or VAR packages.
- **License** — under **Filter** menu; pick a VaM license type (CC BY, PC, FC, …). Click license in the info strip to filter by that type (click again clears).
- {{icon:star|Rated / Not rated (★)}} — cycle: rated only → not rated → off (VR laser OK). Right-click clears.
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

Title-bar search is a **universal filter**. Bare words match name/path/creator/uid, user-tag text, **or** Look-A-Pedia “looks like” names (so typing `jinx` finds a look packaged as Kinx).

### Filter chips (Enter to commit)
- Type `@creatorA` then **Enter**, then `#tagB` then **Enter** — each becomes its own chip under **Include**.
- **Shift+Enter** — commit draft to **Exclude** (bare → broad exclude `-term`; `#tag` → `-#tag`).
- Paste a full query (`#wet -nsfw -#old @Acid`) and press **Enter** — tokens explode into chips.
- **Exclude** row stays visible whenever any chip exists (hint: *Drop chip here to exclude*).
- **X** left of Include/Exclude — clears **all** search chips (and draft). Same as search-field clear. **{key:undo}** within 5s undoes.
- **{key:focus_search}** — focus title search (opens compact popup if needed) and select draft text. Brief highlight cues open. Popup stays until **Esc**, click outside (not chip rows), or click the compact search icon again. **In Settings**, the same key focuses the side-rail settings filter instead (no popup over the list).
- **Tab** / **↓** (in search) — leave search and focus the grid (arrows work; draft and chips kept). **Tab** from the grid returns to search. **↑** on the first grid item also returns to search. **In Settings**, Tab/↓ from the filter focuses the settings list; Tab / ↑ from the first row returns to the filter; the **Apply selection** binding (**{key:apply}**) toggles the selected setting.
- **Drag** a chip onto **Exclude** / **Include** to change polarity (tags and bare terms). Chip **×** removes it.
- **Ctrl+Backspace** on an empty draft removes the last chip. **ESC** closes the search field without clearing chips. Search **X** clears all search chips.
- Drag a tag from the **Tags** side list (or detail tag chip) onto Include/Exclude to add a search chip (does not apply the tag to items).

### Boolean logic (AND / OR / IF)

- **AND** (default) — every term in a group must match. Space or the word `AND` means the same thing: `dress AND #wet` ≡ `dress #wet`.
- **OR** — either side may match: `tag:wet OR tag:shiny`. Groups on each side of `OR` are still AND inside: `dress #wet OR #shiny` = (dress and #wet) or (#shiny).
- **IF** — optional preface before a status/badge word. Same as writing the status alone: `IF loaded` ≡ `loaded`. Use it when you think “only IF this is true…”.

### Text and tags
- Bare words — `dress man` (each word must match somewhere; AND). Also matches Look-A-Pedia **looks like** names when that pack is on.
- **Broad exclude** — `-wet` (must **not** match name/path/creator/uid, user-tag text, or Look-A-Pedia subject). Chip Exclude / Shift+Enter on a bare word uses this.
- User tag — `tag:wet` or `#wet`
- **Multiple tags** (comma list) — `tag:wet,shiny,-nsfw` or `#wet,#shiny,-nsfw` or shorthand `wet,shiny,-nsfw`
  - plain name = must have that tag
  - `-name` = must **not** have that tag
- Exclude tag — `-tag:nsfw` or `-#nsfw` (user-tag only; different from bare `-wet`)
- Creator — `creator:Acid` or `@Acid` (comma list OK: `@Acid,@Other`)
- **Looks like** — **Looks like** section in {{icon:tags|Tags}} (amber, read-only), or `looks:jinx` (Look-A-Pedia subject only; comma list = **any of**). Click **Looks like** on the detail strip to commit this chip.
- **Hub tag** — **Hub tags** section in {{icon:tags|Tags}} (green, read-only), or `hubtag:nier` (comma list = **any of**). With the Hub data pack on, every package the Hub knows carries its Hub tags, not just Look-A-Pedia entries. Look-A-Pedia and Hub rows that share a Hub resource id also share package matches (Hub filenames fill empty Look-A-Pedia `vars`, and the other way around). Select an item: Hub tags on the **detail strip** are clickable filters (right-click excludes), same as the Tags list. Open the tag list to see them in green under your own tags: read-only there, but **click** hides one on that package and **right-click** hides it everywhere (Settings shows the count and can undo the lot). Same string can still be **Create Tag** as a user tag — different namespace, no conflict.
- **Any pack field** — `lap:gaming` (subject, title, creator, category, tag line, or tags — across both packs). Leading `-` excludes (`-looks:`, `-hubtag:`, `-lap:`).

### Status and badges
Grid letter badges: **A** auto-install, **H** hidden, **W** scan-whitelist excluded, **T** user tags.

- `loaded` / `installed` — under AddonPackages (or local loose files)
- `unloaded` — not loaded (e.g. AllPackages)
- `starred` / `rated` / `badge:star` — has a star rating
- `unrated` / `not-rated` — no star rating
- `tagged` / `badge:t` — has user-tag badge **T**
- `untagged` — no user tags
- `autoinstall` / `badge:a` — auto-install badge **A**
- `hidden` / `badge:h` — hide badge **H**
- `whitelist` / `badge:w` — scan-excluded badge **W**

### Examples
- Open {{icon:tags|Tags}} and expand **Looks like** or **Hub tags** — no syntax
- `jinx` — name/path **or** Look-A-Pedia “looks like” (finds Kinx)
- `looks:jinx` — only Look-A-Pedia subject (expert)
- `hubtag:nier` — Hub tag from the Hub / Look-A-Pedia packs (Tags list Hub tags section)
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

**Looks like** (Look-A-Pedia pack) sits in an **amber Looks like (N)** header frozen at the **top** of Available (above pinned tags). **Hub tags** sit in a **green Hub tags (N)** header under that. Both stay on screen while you scroll, so you can collapse without scrolling back up. Expand either, or type in the side search to show matches. Click filters the grid (`looks:` / `hubtag:`); they cannot be applied. Type the same Hub-tag name and use **Create Tag** if you want that string as a user tag.

Use **Edit** in the Tags column header to open the tag editor **Database** mode (create, rename, merge, purge, categories, YAML). Same window as detail-strip **Set Tags** / **T** — switch **Tag** ↔ **Database** in the mode tabs.

**Copy tags between items** — on the detail strip tags row: **Copy Tags** from a source item (or multi-select for a union), select targets, then **Paste Tags** (merge) or **Replace Tags** (overwrite). **Shift+Paste** also replaces. With a mixed multi-selection, **Stamp from first** applies the first item’s tags to the rest in one click.

## Import

**Scenes category only.**

{{icon:import|Import}} on the side rail — blue icon **above Tags**.

- **Left-click** — open import sidebar on the same side as the button.
- **Right-click** — open it on the **opposite** side.
- Header **float** chip / **{key:import_sidebar}** — detach as a **resizable floating window** (position and size remembered). **X** / **Esc** hide and keep float; footer **Dock** reattaches as the side column and **stays open**. Floating Import does not push the grid inset.

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
- **{key:select_all}** — select all visible rows.
- **Escape** — clear selection when no menu/mode claimed Esc (see Hotkeys).

Select one or more rows to expand the **toolbox** at the bottom (hover the bar if it is collapsed).

**Detail strip** — select a row for an info card above the toolbox: thumb + status badges (A/H/W/T), facts, and clickable chips. Collapse with the chevron left of the item name; expand again from the Details button (top-left in the toolbox action row). Hover the preview thumb: scroll wheel steps selection; hold right-click and scroll to raise/lower the star rating; double-click to launch or apply. Drag the thin bar at the top of the strip to resize height (preview stays square; path/desc/tags hide when short). Settings → Visuals → **Detail preview side** puts the image left or right. Preferences are remembered.
- **Description & package tags** — wide+short pane uses a side column (scrollable description + native tags). Tall strip moves those into regular rows under actions; narrow pane keeps a short description row. **Hub tags** in that line are clickable chips (filter / right-click exclude), same as user-tag chips. Turn off **Show description & package tags** in Settings → Visuals to hide them.
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
Lower side-rail buttons help edit the open scene (not the package grid). Three different “remove” families:

- **Unequip clothing / hair** — list and unequip wearables on persons (not package Delete).
- **Unequip / remove atom** — remove atoms from the scene via the side list.
- **Scene Eraser** (separate rail tool) — point-and-click erase in the 3D scene; **Esc** exits.
- **Delete** (toolbox) — move packages/scenes to Deleted* folders on disk.
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
- {{icon:settings|Settings}} side tab — appearance, layout, browse, input, hotkeys, performance, plugin options. **{key:focus_search}** filters this list; **Tab** / **↓** moves into rows; the **Apply selection** binding toggles.

### Footer utilities
- **U / R** — Undo / Redo
- **Rdm** — load random item from the **current** filtered view (not a saved preset; use Filter presets dice for that)
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
- **Apply** — single-click or double-click (Settings → Browse → Interaction; side-rail toggle). Commits to the target Person.
- **Try-On** (Settings → Interaction) — eligible clothing/hair/skin/morph/appearance/pose/plugin applies preview first. Toolbox bar: **Keep** / **Revert** / Compare; **Esc** reverts. Next try auto-keeps the previous preview (status toast).
- **Import to atom** — Scene Import sidebar button (not grid Apply): copy resources from a scene onto a Person.
- **Load scene** — scenes/vars use load/drag paths, not Apply.
- **Enter** / **Space** — same as click Apply / Try-On intercept.
- {{icon:hold|Hold-to-launch}} (footer) — hold mouse or controller on a thumbnail to apply (overrides 1-Click toggle).
- **Replace vs merge** (side rail in appearance categories) — toggles whether clothing/hair replaces or merges.

### Drag and drop
Settings → Browse → **Enable drag & drop** (off by default). Drag thumbnails onto persons or the scene. The replace/merge side button affects drop behavior.

### Saving
**Save** on the side rail — preset and scene save flows for the active category.

Use {{icon:undo|Undo}} / **Redo** in the footer after supported edits.

## Hotkeys

Press **{key:help_alt}** or **{key:help}** anytime the gallery is focused to jump here. **Esc** closes this panel.

**Every key below is rebindable.** Open **Settings → Shortcuts** to see the whole list in one place: click a binding chip (or **SET**) and press the keys you want, or press **CLEAR** to switch that one shortcut off. Rows that share a key are flagged in red, and hovering the chip names the other action. Bindings match modifiers exactly, so a Ctrl+Z binding never fires on Ctrl+Shift+Z. **Esc** is the one key that is not rebindable — it is the universal way out of every menu and mode.

Three rules at the top of that page decide when shortcuts are live at all:

- **Only while the VaM window is focused** (on by default) — VaM keeps running in the background, so keys typed in another app could otherwise reach the plugin. Always bypassed in VR, where the session legitimately runs unfocused.
- **Only while a gallery pane is on screen** (on by default) — with the panes hidden, everything goes quiet except the hotkeys that *open* something (Show/Hide Panes, Create Gallery Pane, Open Hub Browser), so you can always get back in.
- **0–9 jump to category** — turn off if you want the number row for something else.

A bare key assigned to a global hotkey (say **Space** on Show/Hide Panes) is reserved: it will not also trigger a gallery shortcut on the same press.

### Gallery chrome
- **{key:plugin.gallery}** — show / hide gallery (configurable in **Settings → Shortcuts → Global**)
- **{key:help}** / **{key:help_alt}** — open / close this Hotkeys sheet
- **{key:command_palette}** — command palette (also footer left chip next to Undo/Redo). Type to filter commands + categories; ↑↓ / Enter / Esc. Groups: Edit, Browse, Modes, Selection, Packages, View, Help. Recent list on open. Grey rows = unavailable (need selection / mode).
- **Esc** — close help/menus/search, then exit Scene Tools / Scene Eraser / Try-On (revert) / Cleanup / Import, else clear selection (ladder: innermost first)
- **{key:ui_scale_up}** / **{key:ui_scale_down}** — gallery UI scale up / down (keypad variants have their own rows; separate from grid zoom). New installs auto-pick a desktop starting scale from screen height; desktop chrome also multiplies by VaM Monitor UI Scale.
- **Ctrl + mouse wheel** — grid columns (+ / − in footer too)

### Browse and search
- **{key:focus_search}** — focus title search (opens compact popup if needed). **In Settings**, focuses the side-rail settings filter (no popup over the list).
- **Tab** / **↓** (in search) — focus grid for arrow selection (keeps draft/chips). **Tab** from grid returns to search. **↑** on first item returns to search. **In Settings**, Tab/↓ from filter → list; Tab / ↑ from first row → filter; the **Apply selection** binding (**{key:apply}**) toggles the selected setting.
- **{key:filter_presets}** — open / close floating filter presets (hide keeps float; Dock reattaches)
- **{key:import_sidebar}** — open / close floating Scene Import (detach if needed; hide keeps float; Dock reattaches as side panel and stays open)
- Filter presets open: **↑↓** / **Enter** / **D** / **Ctrl+S** / **Esc** / **U** (soft-delete undo)
- **Enter** — commit draft search to an Include chip
- **Shift+Enter** — commit draft to Exclude
- **Esc** (in search) — close search field without clearing chips
- **{key:undo}** — undo clear-all search chips (within 5s)

### Selection
- **Arrow keys** — move selection in the grid (Shift = range, Ctrl = add). Not rebindable.
- **Apply selection** — currently **{key:apply}** / **{key:apply_alt}**. Ships **unassigned**: Space and Enter are left free so they cannot load a scene by accident. Assign either slot in **Settings → Shortcuts → Selection**. Double-click / click in the grid always applies regardless.
- **{key:select_all}** — select all visible items
- **{key:delete}** / **{key:delete_alt}** — delete eligible selection (History: remove from history)
- Context menu **1–9** — run numbered actions while the menu is open
- **0–9** — jump to numbered category (same order as the category quick-switch menu)

### Undo / redo
- **{key:undo}** — undo (footer Undo stays pinned; tooltip names next action; also undoes recent History remove / search clear within 5s)
- **{key:redo}** / **{key:redo_alt}** — redo
- **{key:history_refresh}** — refresh History browse (History category only)

### Scene Tools (not Creators list)
- **{key:scene_tools}** — toggle Scene Tools
- **{key:strip_scene}** — open / close Strip Scene keep picker
- While Strip Scene is open: **?** help · **/** filter · **↑↓** nav · **Space** toggle · **←→** expand · **F2** rename · **Enter** strip · **Esc** back
- Status bar shows sticky modes (Scene Eraser · Scene Tools · Try-On · Import · Cleanup · …); soft toasts append after the mode line. **Esc** exits tool modes listed there.

### Scene Eraser
- Side-rail eraser / **{key:scene_eraser}** — point and click to erase scene items; **Esc** exits
- Pointer popup says **Erase …** (scene edit). Wearable side lists say **Unequip**. Toolbox **Delete** is disk only.

### Try-On
- Settings → Interaction → **Try-On Mode** (also command palette). Eligible grid applies preview; bar **Keep** / **Revert** / Compare; **Esc** reverts. Next try auto-keeps previous (toast).

### Filters
- Active browse filters show as chips under the title bar (**Clear all**). Switching category restores that category’s saved filters and toasts when any are active.

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
