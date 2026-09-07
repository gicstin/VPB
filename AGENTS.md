# Agent instructions — VPB

Read this before changing code. Full test documentation: [tests/README.md](tests/README.md).

## Build and verify

- `VPB.csproj` targets **.NET 3.5 / Unity 2018.1.9f2 / Mono**, loaded by BepInEx 5 into VaM.
  No `async`/`await`, `Task`, `Concurrent*`, `Span<T>`, `ValueTuple`, `Lazy<T>`, `Tuple<T>`,
  `Array.Empty<T>()`, `IReadOnlyList<T>`, `Directory.Enumerate*`, 3-argument `Path.Combine`.
  `VPB.Tests.Static -> BannedApiTests` enforces this list; add to it rather than
  rediscovering the rule.
- **The `<Compile Include>` list in `VPB.csproj` is explicit.** A new `.cs` file under `src/`
  must be added there or it is never compiled — no error, no log line. The build warns and
  `CompileListTests` fails if you forget.
- Do not run a build unless asked; the user builds manually.

## Tests

```powershell
.\tests\run.ps1                  # both headless suites, ~10s
.\tests\run.ps1 -Filter Config   # substring match on test name
.\tests\run.ps1 -Report          # compact summary + full failure messages, paste-ready
.\tests\run.ps1 -Runtime         # arm the in-game suite, then launch VaM
.\tests\run.ps1 -RuntimeResults  # read what the in-game run reported
```

`tests/` is developer-only and no test code ships in `VPB.dll`. A Debug build of `VPB.csproj`
does three things for the tests: it installs `tests/hooks/*` into `.git/hooks` (so pre-commit and
pre-push work with no per-developer setup); it builds, deploys **and arms** `VPB.Tests.Runtime`,
the in-game plugin, so the next VaM launch runs it; and it reports the *previous* launch's results
as MSBuild warnings, which is how in-game failures reach the IDE error list. All of it is
WarnAndContinue - a test problem never blocks the plugin build.

So the in-game loop is: build, launch VaM, build again and read the warnings. `-RuntimeResults` is
still there if you want it in a terminal.

### When you must add or update a test

| You changed | Do this |
|---|---|
| Added a field to `VPBConfig` | Nothing — `ConfigSerializationTests` and `ConfigRoundTripTests` pick it up automatically. If they go red, you forgot `Save()` or `Load()`. |
| Added a `[HarmonyPatch]` or an `AccessTools.*` lookup | Nothing — `HarmonyPatchTargetTests` / `ReflectionLookupTests` cover it automatically. Prefer `typeof(X)` over a `Type` variable so the lookup is statically checkable. |
| Added a new icon | Nothing — `AssetLinkTests` checks both directions of the `icon_map.json` link. |
| Added or changed a **parser, codec, path/uid rule, cache key, comparer, sort, filter predicate, or serialization format** | **Add a test** in `tests/VPB.Tests.Vam/Suites/`. These are the failures that never show up in a log. |
| Added UI, layout, or anything needing a live `GameObject`, real `Texture2D`, or VaM's package-aware path resolution | Add a `[VpbRuntimeTest]` in `tests/VPB.Tests.Runtime/Suites/` — the in-game plugin. Do **not** fake it in the headless tier. |
| Fixed a bug in any of the above | Add the failing case as a test **before** the fix, so the test demonstrably fails first. |

### Writing one

New file in `tests/VPB.Tests.Vam/Suites/`; the project globs its own sources, so there is no
list to edit. The whole VPB source tree is compiled into the test assembly, so every
`internal` type is directly callable — no `InternalsVisibleTo`, no mocks.

```csharp
[Collection(VamCollection.Name)]
public class ThingTests
{
    public ThingTests(VamFixture vam) { }

    [Theory]
    [InlineData("input", "expected")]
    public void DescribesTheInvariantNotTheMethodName(string input, string expected) { }
}
```

- `[Collection(VamCollection.Name)]` is mandatory — `TempInstall` changes the process working
  directory and the collection serialises the suites.
- Use `using (var install = new TempInstall("label"))` for anything touching `VPBConfig`,
  SQLite, or `Saves/PluginData`.
- Assertion messages state what the defect means **in the game**, not what the assertion
  compared.
- No code comments in production or test code. Names and assertion messages carry the intent.

### Rules that are not negotiable

- **Never** silence a failing test by adding an entry to a `tests/known-*.txt` allowlist
  unless you have established the exclusion is deliberate, and write the reason in the file.
  These files exist to record decisions, not to make the board green.
- If a test hits `SecurityException: ECall methods must be packaged into a system module`, the
  stack names the VPB method that touched the engine. Add one `PatchPrefix(...)` to
  `HeadlessVam.Install()`. If the method *itself* contains the Unity call it cannot be
  patched — isolate the call into a `[MethodImpl(MethodImplOptions.NoInlining)]` helper, as
  `LogUtil.EngineRealtime` and `XrUtils.XrSettingsEnabled` do.
- Report test results honestly. A skipped or unwritten test is stated, never implied.
- An in-game test whose precondition is missing (no packages installed, empty index, no Person
  atom) calls `RuntimeAssert.Inconclusive(reason)`. Never `RuntimeAssert.True(true, ...)` - that
  reports green for a path the run never touched, and `TestSuiteHygieneTests` fails on it.
