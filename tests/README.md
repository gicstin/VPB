# VPB tests

Developer-only test suites. **No test code ships.** Nothing here is a `ProjectReference` or a
`<Compile>` item of `VPB.csproj`, and `scripts/PostBuildDeploy.ps1` never touches it.

Two deliberate exceptions, both developer-only: a Debug build of `VPB.csproj` builds and deploys
`VPB.Tests.Runtime` (it has to run inside VaM to be worth anything), and `VPB.dll` carries a single
`[assembly: InternalsVisibleTo("VPB.Tests.Runtime")]` line so that plugin can reach internals. That
is one metadata attribute in the shipped DLL and no test code.

The suites exist for one bug class: defects that neither normal usage nor the log reveals —
a Harmony patch whose VaM target quietly stopped resolving, a setting that silently reverts
on the next launch, a source file that never gets compiled.

```powershell
.\tests\run.ps1                    # everything
.\tests\run.ps1 -Static            # repository invariants only, no VaM needed
.\tests\run.ps1 -Vam               # the VaM-linked suite only
.\tests\run.ps1 -Filter Harmony    # substring match on the test name
.\tests\run.ps1 -Report            # compact paste-ready summary, copied to the clipboard
.\tests\run.ps1 -Runtime           # arm the in-game suite; it does NOT run it
.\tests\run.ps1 -RuntimeResults    # read the last in-game run's report
.\tests\run.ps1 -InstallHooks      # manual fallback; the build normally does this
```

### Sharing a result

`-Report` runs the suites with a TRX logger, then prints (and copies to the clipboard, and
writes to `tests/TestResults/summary.md`) a compact markdown block: a counts table plus the
**full message of every failure and nothing else**. That is the form to paste into a chat or an
issue — the raw Test Explorer log is mostly adapter chatter, and it truncates the long assertion
messages these suites rely on. `-NoClipboard` skips the clipboard copy.

```
## VPB test run

repo VPB @ 3ec7bb06

| suite | total | passed | failed | skipped |
|---|---|---|---|---|
| VPB.Tests.Static | 48 | 48 | 0 | 0 |
| VPB.Tests.Vam | 448 | 448 | 0 | 0 |

All tests passed.
```

**Git hooks install themselves.** The `InstallVPBGitHooks` target in `VPB.csproj` copies
`tests/hooks/*` into `.git/hooks` on build, so a fresh clone is covered after the first
build with nothing to remember. It is incremental (a no-op once installed, re-installs a
hook you edit), silent when `.git/hooks` is absent (worktrees, exported source), and opted
out with `-p:SkipVPBGitHooks=true`. Bypass a single run with `--no-verify`.

Visual Studio picks both projects up in Test Explorer; they are in `VPB.sln`.

---

## The three suites

### `VPB.Tests.Static` — net8.0, no VaM, ~3 s

Parses `src/**/*.cs` with Roslyn (syntax only, no compilation) and checks repository
invariants. This is the suite GitHub Actions runs, because it needs nothing but the repo.

| Test | Catches |
|---|---|
| `CompileListTests` | A source file committed but missing from `VPB.csproj`, so it is never compiled and never reaches the shipped DLL — no build error, no log line. Also stale and duplicate `<Compile>` entries. |
| `ConfigSerializationTests` | A `VPBConfig` field missing from `Save()` (setting silently resets next launch) or from `Load()` (setting silently ignored). Covers strings and enums, which the runtime round-trip cannot mutate safely. |
| `BannedApiTests` | .NET 4+ APIs that the .NET 3.5 profile VaM runs does not have, plus the project rules: no `UnityEngine.Random`, no `new System.Random(...)` — VPB uses `VpbRandom`. Also C# tuple syntax and any file that stops parsing as C# 7.3. |
| `SettingsBindingTests` | The BepInEx half of the settings problem: a `ConfigEntry` field declared but never bound in `Settings.Load` (null on first read), two fields sharing a `(section, key)` so they silently alias one value, and any `Bind` whose section or key is computed rather than literal. Grows with the file. |
| `TestSuiteHygieneTests` | The suites themselves: every VaM test class carries `[Collection(VamCollection.Name)]` and takes a `VamFixture`, `VPB.csproj` never compiles anything from `tests/`, every `[VpbRuntimeTest]` has the shape the runner can invoke, and no in-game test asserts a constant or reaches no `RuntimeAssert` at all (following helper calls within the same type). |
| `AssetLinkTests` | `UI.LoadIconSprite("name")` calls with no `icon_map.json` entry (renders as a blank button, no error), map entries pointing at missing SVGs, orphan SVGs, and `patch_manifest.json` entries that are not on disk. |
| `RuntimeReportContractTests` | The boundary between the in-game runner (C#) and `run.ps1` (PowerShell): the JUnit root element name, the report file name, that the reader goes through `LocalName` rather than the shadowed `.Name`, that it exits non-zero on recorded failures, and that the runner emits `<skipped message=>`. Nothing compiles across that boundary, so a rename on either side is otherwise silent - and the failure mode is a failed in-game run reporting exit 0. |
| `RuntimePluginTargetingTests` | The in-game plugin's build configuration: `net35`, a `<FrameworkPathOverride>` into VaM's `VaM_Data\Managed`, and `MSB3277` an error rather than a suppressed warning. Compiled against a newer reference pack the plugin builds clean, deploys, loads, logs "armed", and then dies on its first iterator step with a `TypeLoadException` that only reaches the Unity player log. Also that the plugin writes a failing report before it runs, so a crash mid-run is a failure and not an absent file. |

### `VPB.Tests.Vam` — net472, needs a VaM install, ~4 s

Compiles **the whole VPB source tree** into the test assembly and references VaM's own
`VaM_Data\Managed` assemblies plus `BepInEx\core`. Because the sources are compiled in
rather than referenced through `VPB.dll`, every `internal` type is directly callable — no
`InternalsVisibleTo`, no reflection gymnastics, no mocks.

| Test | Catches |
|---|---|
| `HarmonyPatchTargetTests` | Every `[HarmonyPatch]` attribute in the plugin resolved against the real VaM assemblies. A patch whose target changed signature installs as a no-op in the game and says nothing. Coverage grows on its own as patches are added. |
| `ReflectionLookupTests` | The manual half of the same problem: `AccessTools.Method/Field/Property(typeof(X), "name", ...)` and `AccessTools.TypeByName("...")` string literals, resolved for real — including exact overloads where the argument types are literal `typeof(...)`. Lookups through a `Type` **variable** are traced back to the `typeof()` / `TypeByName()` that produced it and verified too; anything still untraceable fails unless allowlisted. |
| `ConfigRoundTripTests` | Every scalar setting changed one at a time, saved, reloaded, and compared. Also: `Save()` actually writes a file, a corrupt `VPB.cfg` does not throw, and reading `Instance` does not create the file as a side effect. |
| `SqliteTests` | The real `native/sqlite3.dll` bound through `VpbSqlite3`: text/int/blob round-trips, non-ASCII UTF-8 marshalling, spaced and unicode database paths, null binds, statement reuse. |
| `SearchQueryTests` | `GallerySearchQuery.Parse` — branching on `or`, tag include/exclude atoms, comma lists, quoting, and that hostile input never throws. |
| `HideMarkerTests` | `VpbHideIndex` over real marker files: package hide vs item hide never bleeding into each other, the `uid:/path` key form, slash and case normalisation, loose-file sidecars, and that only an `AddonPackages/<uid>.var` marker can hide a whole package. |
| `RandomTests` | `VpbRandom`: range bounds under 200k draws, no modulo bias, `Shuffle` is a real permutation that actually reorders, degenerate input, and that two threads do not draw the same sequence. |
| `DependencyVersionSortTests` | The version comparison behind duplicate-dependency collapsing: `10` beats `9` (ordinal string comparison gets this backwards and keeps the older package), `latest` outranks every pin, different packages never collapse together, and the result does not depend on insertion order. |
| `PackageReferenceTests` | `PackageReferenceVersionResolver`: uid extraction from every reference form including spaced names and absolute Windows paths, reference-version option parsing (unknown values rejected, not defaulted), `meta.json` application, and referrer-stack balance. |
| `ZstdTests` | The real `libzstd.dll`: round-trips at every compression level and across buffer-size boundaries, partial-length compression of pooled buffers, disk cache round-trip, and that garbage or a half-written cache file never yields partial data mistaken for a payload. |
| `DataPackTests` | `VpbDataPackReader` over fixture TSV packs: header fields, column resolution **by name not position**, var-key/tag parsing (namespace splits on the first colon only), malformed rows counted rather than aborting, a newer `pack_format_version` refused — plus a smoke check that the **shipped** packs still open with this build. |
| `ExclusiveDependencyTests` | `ExclusiveDependencyFinder.Find` over synthetic graphs: transitive exclusivity, an outside dependent protecting everything below it, locked packages and their dependencies, `latest`/`minN`/missing-exact-pin token resolution, cycles, self-references, and abort. A false positive here deletes content the user still needs. |
| `UserTagYamlTests` | User-tag export/import: item-key encode/decode, both YAML layouts carrying the same information, tag names needing quoting (colons, quotes, hashes, unicode), colour categories, a UTF-8 BOM, and that an arbitrary file is rejected rather than silently importing as "no tags". |
| `GenderProbeTests` | `LooseVapGenderProbe`: whole-token prefix matching (`Femalebot` is not female), `useFemaleMorphsOnMale` promoting Male to Futa, `ClassifyStorables` keeping Futa where `Classify(file)` folds it to Male, and malformed or missing `.vap` files reading as Unknown rather than throwing. |
| `OutfitPickTests` | `VpbImport.ListAppearanceOutfitItems`: disabled and duplicate entries skipped, display-name fallback, hair only when asked — plus the `clothingItem#N` ordinal parser and an explicit fixture stating that the ordinal is **not** an index into the clothing array. |
| `GalleryFilterTests` | The clothing/hair/appearance subfilter predicates: the default view hiding `.vap` presets, clothing and hair never leaking into each other across the shared `.vam` extension, a packaged preset not counting as the user's custom preset, and ungendered content staying visible under either gender toggle. A wrong answer here never throws - the row is just absent. |
| `FileManagerTests` | The VaM file layer through a populated registry: uid/group/version parsing, path cleaning, package vs drive-letter paths, registry and group lookups, dependency resolution (exact pin, `latest`, missing pin), the whitespace-alias reverse index, files inside a package vs loose on disk, the load-dir stack, and a junction-following directory timestamp probe. |
| `UidWhitespaceRepairTests` | `VpbUidWhitespaceIdentityRepair` against a real database: rename vs merge, dependent rows remapped on every column, the **older** `first_scanned` winning so a repaired package does not jump to the top of the New sort, `ConsumeApplied` only clearing rows the package table now carries, and tolerance of a database that predates some of the tables it remaps. |
| `PresetPathFixupTests` | `VarPresetPathFixups.Apply`: `SELF:` resolved to the owning package everywhere in the tree, already-qualified references left alone, loose presets untouched, spaced package names, idempotence, and null/empty inputs. |
| `VarPackageTests` | Real `.var` archives built from scratch by `VarFixture`, read back through `VarPackage`: `meta.json` fields, spaced/unicode package names, dependency extraction, and that a truncated or missing archive never throws out of the package layer. |
| `GalleryIndexTests` | The real SQLite index built from fixture packages: `pkg` / `cat_mem` / `pkg_dep` rows, category classification, package removal, an unstamped clock aborting the publish, and **incremental add/remove producing identical `pkg`, `pkg_dep` and `cat_mem` rows to a full rebuild**. |
| `ClothingFacetPackingTests` | The two hand-mirrored clothing filters checked against each other by exhaustion: every clothing flag combination and every hair flag combination against a fixture path list, `PassesClothingGalleryFiltersForPath` vs the packed attribute the SQL index precomputes. When they drift the same item appears or not depending on whether the index happened to cover it, which reads in the game as a gallery that changes its mind. |
| `SortStateTests` | `SortType` as a persisted value: every ordinal pinned in both directions, so neither a renumber nor a new member inserted in the middle can pass. The integers are written into snapshot cache files and into snapshot cache keys - a shift makes an install read yesterday's cache under today's meaning and come up sorted by something nobody picked. Also the four-mode side-pane sort round-tripping through its index, and `SortState.Clone` being a copy. |
| `HarnessSelfTests` | The harness itself: sources really were imported, VaM assemblies really are side by side, Harmony detours really take effect, and the ECall wall still behaves as documented. |

---

## How the VaM suite finds your install

`tests/Directory.Build.props` imports `VPB.local.props` and uses its `<VaMPath>`. If that is
missing it probes `C:\vam` then `C:\VaM`.

With no install found the project builds a single skipped placeholder test instead of
failing, so `dotnet test` still works on a machine without VaM.

## How the source list stays in sync

The project reads `VPB.csproj`'s own `<Compile Include>` items with `XmlPeek` at build time.
Add a file to `VPB.csproj` and the test project picks it up on the next build. There is no
second list to maintain and no way for the two to drift.

`PluginVersion.g.cs` is the one generated entry; it is skipped and
`Harness/PluginVersion.g.cs` is compiled instead, so a test build never runs
`PreparePluginVersion.ps1` and never bumps `plugin_version.txt`.

---

## Writing a new test

Drop a file in `tests/VPB.Tests.Vam/Suites/`. That is the whole procedure — the test project
globs its own sources.

```csharp
[Collection(VamCollection.Name)]
public class HideGranularityTests
{
    public HideGranularityTests(VamFixture vam) { }

    [Fact]
    public void PackageHideNeverFansOutIntoPerSceneMarkers()
    {
        using (var install = new TempInstall("hide"))
        {
            // install.Root is the current directory; VPBConfig and the SQLite layer
            // both resolve their paths off it.
        }
    }
}
```

`[Collection(VamCollection.Name)]` is not optional: `TempInstall` changes the process
working directory, and the collection serialises the suites so they cannot race.

### Building a `.var` to test against

`Harness/VarFixture.cs` writes a real zip with a VaM-shaped `meta.json`, so package-layer
tests need no sample library and nothing committed to the repo.

```csharp
using (var install = new TempInstall("deps"))
{
    var pack = new VarFixture("Creator", "Pack", 3)
        .WithText("Saves/scene/demo.json", VarFixture.SceneJson("Demo", new[] { "Other.Assets.4" }))
        .WithPlaceholderJpg("Saves/scene/demo.jpg")
        .WithMeta(licenseType: "PC EA", tags: new[] { "dress" }, dependencies: new[] { "Other.Assets.4" });

    pack.WriteTo(install.AddonPackagesDir);
    VarPackage p = pack.AsPackage();
    p.TryEnsureMetaJsonLiteFields();
}
```

`AsPackage()` returns a `VarPackage` whose `Path` is `AddonPackages/<uid>.var` — relative,
which resolves because `TempInstall` is the working directory.

Note `meta.json` carries package tags twice: a flat comma-separated `"tags"` string, which
is what `TryEnsureMetaJsonLiteFields` reads into `PackageMetaTags`, and the
`"packageTags": { "clothing": [...], "hair": [...] }` arrays used by the manifest tables.
`WithMeta` writes both; assert against the one the code under test actually reads.

**A content file with no sibling preview image is invisible to the gallery.** `VarPackage.Scan`
only caches a `.json` or `.vam` entry when a `.jpg` of the same base name is in the archive
(`.vap` is exempt). Fixtures that forget the image produce a package with a `pkg` row and no
`cat_mem` rows, which looks exactly like a classification bug. `IndexFixture.SceneVar` /
`ClothingVar` / `HairVar` include the image; `PreviewlessVar` deliberately does not, and
`ContentWithNoSiblingPreviewImageIsNotIndexed` pins the behaviour.

### Building an index to assert against

```csharp
using (var install = new TempInstall("idx"))
{
    IndexLibrary library = IndexFixture.Build(install,
        IndexFixture.SceneVar("Alpha", "Scene", 1, "Beta.Dress.2"),
        IndexFixture.ClothingVar("Beta", "Dress", 2));

    library.Rebuild();                       // full rebuild, deterministic clock
    library.IncrementalAdd(package);         // or the incremental path
    library.IncrementalRemove("Beta.Dress.2");

    using (var db = new IndexDb(install.DatabasePath))
        Assert.Equal(2, db.Scalar("SELECT COUNT(*) FROM pkg"));
}
```

`IndexLibrary` supplies categories, a pinned clock, and the package dictionary to
`VpbLocalDatabase.RebuildCoreWith` / `TryIncrementalGalleryIndexUpdateCoreWith`. In the game
those three come from `Gallery.singleton` and `FileManager`; the providers exist only so the
index can be driven without either. They are re-read after the write pass, exactly as before,
so a scan landing mid-rebuild still aborts the publish.

Every package carries `EVERYTHING` rows for entries no category claimed, so filter those out
(`AND category<>'EVERYTHING'`) when asserting that something was *not* categorised.

Write assertion messages that say what the defect means **in the game**, not what the
assertion compared. The point of these tests is that the failure explains itself six months
from now.

---

## The ECall wall

VaM runs on Unity 2018 and there is no Unity Editor for a mod, so the usual answer — "run
the tests in the Editor Test Runner" — does not exist here. Calling a Unity **internal call**
outside the player throws:

```
System.Security.SecurityException: ECall methods must be packaged into a system module.
```

Pure-managed things work fine: `Vector3`, `Color`, `Mathf`, `Rect`, `SimpleJSON`, the whole
`MVR.*` surface, `AccessTools` reflection. What throws is `Time.*`, `Application.*`,
`Debug.Log`, `new GameObject`, `Object.op_Equality` and friends.

`Harness/HeadlessVam.cs` Harmony-patches the VPB chokepoints that reach the engine. Today
that is one patch: `VPBLogSource.Log`, which both silences in-game logging and captures it
for assertions (`HeadlessVam.LogDump()`).

**When a test hits the ECall message,** the stack trace names the exact VPB method that
touched the engine. Add one `PatchPrefix(...)` line to `HeadlessVam.Install()`. That is the
entire maintenance loop, and `HarnessSelfTests.HarnessResolvedEverySeamItTriedToPatch` fails
if a seam is ever renamed out from under the harness.

**One catch worth knowing.** A method that *itself* contains the ECall cannot be patched —
Harmony's `PrepareMethod` JITs it and the JIT is what throws. For the same reason, a
`try/catch` around a Unity call in the *same* method never fires: the JIT failure happens
before the try block is entered. Both cases need the Unity call isolated into its own
`[MethodImpl(MethodImplOptions.NoInlining)]` method so the caller's `try/catch` can do its
job. `LogUtil.EngineRealtime` and `XrUtils.XrSettingsEnabled` are the two places in the
plugin shaped that way.

---

## `VPB.Tests.Runtime` — the in-game suite

A separate BepInEx plugin that runs inside VaM, for the things the headless tier genuinely cannot
reach: real `Texture2D` storage, whether Harmony patches were actually *installed*, VaM's own
package-aware path resolution, and live `GameObject` behaviour.

**A Debug build of `VPB.csproj` builds and deploys it for you.** The `BuildVPBRuntimeTests` target
runs after `PostBuildDeploy`, so it compiles against the `VPB.dll` that was just written and drops
`VPB.Tests.Runtime.dll` in `BepInEx\plugins\VPB.Tests.Runtime\` — deliberately **not** under
`BepInEx\plugins\VPB\`, because the patcher prunes anything there that `patch_manifest.json` does
not list. Only developers ever build from source, so nothing reaches a user.

**It can never fail your plugin build.** The child build runs through `Exec` with
`IgnoreStandardErrorWarningFormat`, so its compiler errors are printed as plain output instead of
being parsed into the parent's error list, and the target raises a single warning saying `VPB.dll`
is unaffected. A test suite that stops compiling is a test problem, not a reason to block shipping
the plugin.

`ContinueOnError="WarnAndContinue"` on the `<MSBuild>` task is **not** enough: the target continues,
but the child's `error CSxxxx` lines still land in the same build and Visual Studio reports the
solution as failed. Verified both ways by deliberately breaking a suite.

Release builds skip it entirely. `-p:SkipVPBRuntimeTests=true` opts out of a Debug build. To build
it on its own:

```powershell
dotnet build tests\VPB.Tests.Runtime -p:DeployToVaM=true
```

It references the built `VPB.dll` and needs the `[assembly: InternalsVisibleTo("VPB.Tests.Runtime")]`
line in `src/Properties/AssemblyInfo.cs`. If you ever see `CS0122: inaccessible due to its protection
level`, `bin\Debug\VPB.dll` predates that attribute — rebuild `VPB.csproj`. The project warns about
exactly that before it fails.

**It must compile against VaM's own BCL, not a reference pack.** `<TargetFramework>net35</TargetFramework>`
and `<FrameworkPathOverride>` into `VaM_Data\Managed` are both load-bearing and only work together.
Targeting `net472`, the resolver saw VaM's `mscorlib` (2.0.0.0) lose a version comparison to the 4.7.2
targeting pack and compiled the entire plugin against .NET 4.7.2 — emitting `[IteratorStateMachine]`
on every iterator and `Assembly.op_Equality`, neither of which exists in Unity 2018's Mono. The build
was clean, the DLL deployed, BepInEx loaded it and logged `armed; waiting 8s`, and then the coroutine
died on its first step with a `TypeLoadException`. Nothing upstream noticed: BepInEx's own
`LogOutput.log` does **not** capture it, the marker survived, no report was written, and
`-RuntimeResults` said "no report" — which reads exactly like "you forgot to arm it".

Three things now stand between you and a repeat: `MSB3277` (the reference-conflict warning that was
in `NoWarn` the whole time) is an **error** in that project, `RuntimePluginTargetingTests` pins all of
it, and the plugin writes a **failing** report before it starts running, so a crash mid-run shows up
as a failure rather than an absent file.

If a run does go missing, `-RuntimeResults` checks for a surviving marker and points you at
`%LOCALAPPDATA%\..\LocalLow\MeshedVR\VaM\output_log.txt`, which is the only place a Mono
`TypeLoadException` or `MissingMethodException` lands.

### Arming a run

**A Debug build arms it for you.** The deploy target drops a `run-tests.marker` beside the DLL, so
the loop is just: build, launch VaM, build again. No terminal, no flags. The marker is consumed by
the run, so a launch you did not build for stays quiet. Opt out with `-p:SkipVPBRuntimeTestArm=true`.

**The result comes back in the IDE.** The build *before* the arming reads the report the last VaM
launch wrote and raises one MSBuild warning per failed in-game test, so they appear in the Visual
Studio Error List next to everything else:

```
warning : VPB in-game test failed: VamIntegration.PackageAwarePathsResolveThroughTheFileManager ::
          FileManager.FileExists says <pkg>:/meta.json is missing, but the package is registered.
```

Warnings, never errors — an in-game result is information about the last launch, not a reason to
block a build. It also warns when the previous build armed a run that never happened (the marker is
still there, so the numbers are from older code), when the report is unreadable, when it contains
no tests, and when every test was skipped. Those four cases all otherwise read as a pass.

The logic lives in `tests/VpbRuntimeTestReport.targets`, imported by both `VPB.csproj` and
`VPB.Tests.Runtime.csproj`. It has to run from the **parent**: `VPB.csproj` builds the test plugin
through `Exec` with `IgnoreStandardErrorWarningFormat`, which is what keeps a broken suite from
failing the plugin build — and equally what would reduce the child's warnings to plain text that
never reaches the error list. The child is passed `-p:SkipVPBRuntimeTestReport=true` so nothing is
printed twice.

Arming by hand, when you want it:

- `.	estsun.ps1 -Runtime`, or
- create a `run-tests.marker` file next to the DLL, or
- launch VaM with `--vpb-runtime-tests` (persistent, survives runs).

It waits for `SuperController.singleton` to exist, then for VaM to stop loading, then a further
8 seconds of grace (capped at 240s total), runs every `[VpbRuntimeTest]` method, logs one
`PASS n/n` or `FAIL` line, and writes JUnit XML to `Saves\PluginData\VPB\vpb-runtime-tests.xml`.

### Writing an in-game test

```csharp
[VpbRuntimeSuite(Name = "MyArea", Order = 30)]
public static class MyAreaSuite
{
    [VpbRuntimeTest]
    public static void SomethingHolds()
    {
        RuntimeAssert.True(condition, "what this means in the game");
    }

    [VpbRuntimeTest(TimeoutSeconds = 60f)]
    public static IEnumerator SomethingHoldsAfterAFrame()
    {
        yield return null;
        RuntimeAssert.NotNull(thing, "...");
    }
}
```

Public static, no arguments, returning `void` or `IEnumerator`. An enumerator is driven as a
coroutine so a test can wait for frames, scene loads or asset work; it fails on timeout rather than
hanging the game. `Order` runs read-only suites before ones that mutate state. `Skip = true` with a
`Reason` reports instead of running.

There is no xUnit in here — this assembly loads inside VaM. Use `RuntimeAssert`.

## Reaching FileManager

`VamLibrary` swaps `FileManager`'s package registry for a sandboxed one and fills it the way a real
scan would: packages by uid and by path, version groups, the `VarFileEntry` lookup indexes, the
whitespace-alias maps (through `FileManager.AddWhitespaceAlias` itself, not a reimplementation of
the rule), and the `lastPackageRefreshTime` stamp. `Dispose` puts back whatever was there before.

```csharp
using (var install = new TempInstall("fm"))
using (var library = new VamLibrary(install))
{
    library.AddScene("Creator", "Pack", 3);
    Assert.True(FileManager.FileExists("Creator.Pack.3:/Saves/scene/Pack.json"));
}
```

A probe over 52 `FileManager` entry points found **zero** Unity ECall failures — the registry, not
the engine, was what made it untestable. With an empty registry `GetVarFileEntry` returns null and
`NormalizePath` falls through to `Path.GetFullPath`, which rejects the `uid:/internal` form
outright, while `ResolveDependency` dereferences a null dictionary. `FileManagerProbe` keeps that
survey runnable; it reports rather than asserts, so re-run it before assuming something is out of
reach.

`HeadlessVam.OverrideSetting("FieldName", value)` binds one BepInEx setting and assigns it onto
`Settings.Instance`, for behaviour that reads `Settings`. Take the settings a test depends on; an
unbound `ConfigEntry` is null and the plugin's `if (Settings.Instance.X != null && X.Value)` guards
all take their disabled branch, so a test would silently exercise the off path of every feature.

## Known headless boundaries

Three code paths cannot be exercised without the game. Do not try to fake them in the headless tier.

- **A full `Settings.Init`.** Five of the ~90 entries are `Vector2`, and BepInEx's stock Vector2
  TOML converter goes through Unity and throws the ECall. Every other type binds cleanly, which is
  why `OverrideSetting` takes settings one at a time instead of binding the whole file.

- **`VarPresetPathFixups.FixupUnprefixedCustomPaths`** calls `FileManagerSecure.NormalizePath`,
  which reaches VaM's `FileManager.GetFullPath`. In the game that understands the
  `Creator.Pack.1:/Custom/...` package form; under the real BCL, `FileIOPermission` rejects it
  with `NotSupportedException: The given path's format is not supported`. The `SELF:` branch of
  `Apply` is covered headlessly; the unprefixed-path branch is covered by
  `VamIntegrationSuite.UnprefixedCustomPathsAreQualifiedAgainstTheOwningPackage`.
- **`ApplyCachedRawToTexture`** needs real `Texture2D` objects, covered by `TextureCacheSuite`.

## Silent-failure trap

If the VaM assemblies are not copied next to the test assembly, xUnit reports

```
Skipping: VPB.Tests.Vam (could not find dependent assembly 'UnityEngine.CoreModule')
```

and discovers **zero tests** instead of failing. Keep `<Private>true</Private>` on the VaM
`<Reference>` items. `run.ps1` treats a zero-discovery run as a failure, and
`HarnessSelfTests.VamAssembliesAreSideBySideWithTheTestAssembly` asserts it directly.

---

## Allowlists

Six small files record deliberate exceptions. Every entry needs a written reason, because
an entry without one is indistinguishable from the bug the test exists to catch.

| File | Used by |
|---|---|
| `known-uncompiled.txt` | Source files intentionally outside `VPB.csproj` |
| `known-unpersisted-config-fields.txt` | `VPBConfig` fields intentionally not serialised |
| `known-normalized-config-fields.txt` | Settings deliberately re-derived or clamped on load |
| `known-unbound-settings.txt` | `Settings` `ConfigEntry` fields intentionally not bound in `Load` |
| `known-unresolvable-types.txt` | Types only present when an optional third-party plugin is installed |
| `known-untraceable-lookups.txt` | `AccessTools` lookups whose receiving `Type` is computed at runtime |

---

## CI

`.github/workflows/tests.yml` runs the Static suite on `windows-latest` for pushes and PRs.

The VaM suite is not run in CI and the workflow says so: it needs VaM's proprietary managed
assemblies, which cannot be committed or fetched. The pre-push hook is where that suite
gets enforced.
