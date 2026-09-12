# Releasing VPB

Users update in-game. `VpbUpdaterService` fetches from `raw.githubusercontent.com` at a **ref**,
and a ref is a branch or a tag — that is the whole mechanism behind both updates and rollback.

## Shipping a build

**Build. Commit. That is the whole process.** Everything below happens on its own:

| When | What runs | Result |
|---|---|---|
| build | `PostBuildDeploy.ps1` → `BuildPatchManifest.ps1` | `patch_manifest2.json` regenerated with a git blob SHA1 and size per shipped file |
| commit touching `VPB.dll` | `post-commit` hook → `PublishRelease.ps1` | release recorded in `releases/index.json`, `build-*` tag created (on the release branch), follow-up commit made |
| any other commit | nothing | the hook exits immediately |
| push | `pre-push` hook | publishes this branch's `build-*` rollback tags |
| push | `tests.yml` → `CheckReleaseIndexFresh.ps1` | fails the build if the index does not name the shipped version |

Hooks install with a Debug build of `VPB.csproj`, or `pwsh -File tests/run.ps1 -InstallHooks`.

Tags publish themselves on `git push`, so there is no manual release step. The `pre-push` hook
sends the rollback tags with the push you are already making — never `--tags`, only `build-*` tags
whose commit is an ancestor of what you are pushing. Unrelated tags (`rescue/*`, `v0.x`) stay
private, and nothing publishes work that is not going public with this push anyway. A tag only
adds a ref; it never moves a branch or rewrites history. Set `VPB_NO_TAG_PUSH=1` to skip it.

If hooks are not installed, nothing is lost and nothing is silently wrong — CI fails the push and
tells you to run `tests/run.ps1 -InstallHooks`. To record and publish by hand:
`pwsh -File scripts/PublishRelease.ps1 -CreateTags` then `git push origin --tags`.

### Why the index arrives in a second commit

It stores the commit sha of the build, and that sha does not exist until the commit does — so
pre-commit cannot write it and amending would invalidate what it just wrote. The hook therefore
records the build against the binary commit and puts the index in a follow-up
`chore(release): record VPB x.y.z`. Clients read the index from the **branch tip**, never from a
tag, so the one-commit lag inside history is invisible to them. The follow-up touches no binary,
so it does not re-trigger the hook.

## What each file does

| File | Read by | Purpose |
|---|---|---|
| `vam_patch/patch_manifest.json` | every VPB ever shipped | the file list. **Shape is frozen** — clients in the field parse it to find their update, so a change strands them with no remote fix. |
| `vam_patch/patch_manifest2.json` | 0.32.681+ | version, schema, and per-file SHA1. One fetch replaces `plugin_version.txt` + `patch_manifest.json` + the GitHub tree API. |
| `releases/index.json` | 0.32.681+ | the rollback list. Fetched from the **channel branch**, never the pinned ref, because old tags do not contain it. |

New fields go in a new file. Never reshape one an older client already reads.

## Rollback

Tags are flat: `build-0.32.610`, never `build/0.32.610`. A slash lands in the tree API path as
`git/trees/build/0.32.610`, which is not an endpoint — it 404s, the updater gets no SHAs, and it
then refetches all 42 shipped files with no checksum verification at all.

`MinRollbackVersion` is `0.32.406`, the commit that moved the plugin into a single folder
(`86132935`). The patcher prune and the `VpbLegacyLayout` sweep only migrate forward, so older
builds are kept out of the index and never tagged — a ref that does not exist cannot be pinned to.

A reset or rebase leaves tags pointing at commits no branch contains. Those are dead rather than
another branch's, so `PublishRelease.ps1` removes them, and a tag whose version the index still
lists is re-pointed at the replacement commit instead of being reported as a version collision.

`-Keep` (default 60) bounds the index. Every client fetches it on every check, and at this build
cadence an unbounded list reaches four figures within a year. Tags follow the index: any `build-*`
tag naming a release the index no longer lists is deleted on the next `-CreateTags` run. A user
pinned to a pruned build is detected and offered "return to latest" rather than a fetch error.

## Two people shipping at once

`plugin_version.txt` holds a **per-working-copy** counter. Two people building from the same base
both produce `0.32.681` from different source. That is the main multi-developer hazard here, and
three things now catch it:

- `PublishRelease.ps1` refuses to record a version already in the index at a different commit. Not
  overridable by `-Force` — two binaries cannot share one version, because the tag would point at
  one while the picker describes the other.
- Every run verifies each `build-*` tag resolves to the commit the index names, and fails if not.
- A merge that mangles the index or the manifest is caught by the static suite: malformed JSON,
  duplicate versions, and hashes that no longer match `vam_patch/` all fail CI.

The resolution is always the same: bump the **base** version in `plugin_version.txt`, rebuild, re-run.

Merge conflicts to expect when two builds land together: `plugin_version.txt` (counter),
`releases/index.json` (both prepend), `patch_manifest2.json` (different hashes), and the binary
itself. Resolve by taking one side and re-running the build plus `PublishRelease.ps1`; never
hand-merge hashes.

Retention deletes tags **locally**. A tag already pushed stays on the remote and returns on the
next `fetch --tags`; it keeps working for anyone pinned to it and is simply no longer listed. The
script prints the `git push origin :refs/tags/...` lines if you want them gone for real.

## Branches

**Every branch is independently rollbackable.** A user following `multiplayer` sees and can pin
`multiplayer`'s builds; someone on `main` sees `main`'s. That works because the two artefacts have
different scopes and the tooling respects both:

- `releases/index.json` is a **tracked file**, so it is per-branch. Each branch publishes its own,
  and a client fetches whichever branch it follows.
- **Tags are repository-wide** — one flat namespace shared by every branch.

A shared tag namespace is safe because a version identifies exactly one build: `build-0.32.684`
means one commit, whoever produced it. So branches coexist in it without collision, and
`PublishRelease.ps1` tags this branch's builds **on whatever branch you run it**.

Pruning is the only part that could damage another branch, so it is scoped by **ownership**: a tag
this branch's index does not list is only deleted if its commit is in this branch's history.
Anything else belongs to a branch that tags its own builds and is reported and left alone
("N tag(s) belong to other branches and were left untouched").

The one genuine conflict is two builds claiming one version — two branches whose
`plugin_version.txt` counters advanced independently. The tag can only point at one, so the picker
would describe a different build than users receive. Any tag naming a version this index lists but
pointing at a different commit is a **hard error** regardless of which branch that commit is on;
the fix is to bump the base version on this branch and rebuild.

Tags are created locally and published by the `pre-push` hook with your next `git push`, scoped to
this branch's `build-*` tags. Until they reach the remote, `PublishRelease.ps1` warns on every run
that the index advertises builds nobody can fetch.

### Version numbers are not monotonic

A branch can be versioned ahead (multiplayer running on a future base), and changing the base in
`plugin_version.txt` **resets the build counter to 1** — so work coming back to `0.32` lands as
`0.32.1`, numerically below everything already shipped. Nothing in the pipeline may assume a
larger number means a later build:

- **"Latest" is decided by date, not version.** `releases/index.json` is sorted newest-first by
  commit date, so a `0.32.500` built today sits above a `0.32.600` from last week.
- **The update check is an inequality, not a greater-than.** `remoteVersion == localVersion` means
  a lower-numbered build still installs; files are diffed by SHA1, which does not care about
  numbering at all.
- **Rollback-vs-update is decided by ship order.** `VpbReleaseCatalog.IsOlderThan` looks up both
  versions in the index and compares positions, falling back to a numeric compare only when the
  index cannot answer. Otherwise a forward update would be labelled "Rolling back".
- **The floor is ancestry, not a number.** `-MinRollbackCommit` (default `86132935`) decides
  whether a build has the single-folder layout by asking whether that commit is an ancestor. A
  version compare would have wrongly excluded a `0.32.1` produced after a base change.
- **Two builds may never share a version.** If a branch produces a number already in the index at
  a different commit, `PublishRelease.ps1` refuses and tells you to bump the base — not
  overridable, because the tag would point at one build while the picker describes the other.

Database schema numbers may also go backwards across the index after merging a branch that ran
ahead. That is reported but not an error: the runtime guard compares a release's schema against
the **local** database, never against neighbouring releases.

### Switching branches and merging

Nothing needs reconfiguring. The index is **derived data** — `-Backfill` regenerates it from
history, so it is always correct for whatever branch you are standing on:

- **Switch branches:** the index switches with you. Tags stay. No action needed unless you are
  about to publish, in which case switch to `main` first and re-run.
- **Merge, conflict in `releases/index.json`:** never hand-merge it. Take either side
  (`git checkout --ours` / `--theirs`), then run `PublishRelease.ps1 -Backfill` on the merged
  result. The merged history is the source of truth and the rebuild reconciles everything.
- **Merge, conflict in `patch_manifest2.json` or the binary:** take one side's binary, rebuild, and
  let `BuildPatchManifest.ps1` regenerate the hashes. Hand-merged hashes are always wrong, and the
  static suite fails on them.

This repo merges with real merge commits, which preserve the commits tags point at. **Squash
merging a branch that contains tagged builds would break that binding** — the tag would reference a
commit no longer in `main`'s history. The script reports those as foreign rather than failing, but
re-tag from `main` afterwards if it happens.

## When something goes wrong

### Corruption in transit — cannot reach an install
Every downloaded file is checked against a git blob SHA1 before it is staged, and one mismatch
aborts the whole update without touching the install. The fast path takes the hashes from
`patch_manifest2.json`; the legacy path takes them from the GitHub tree API and now **refuses to
proceed at all** if that call returns nothing, rather than fetching unverified bytes. A manifest
missing any `Sha1` is rejected outright. Partial downloads fail the same way: `pending.json` is
written last, so an aborted update leaves nothing for the patcher to apply.

### A half-applied update — self-healing
`ApplyStagedUpdates` replaces files one at a time (rename to `.old`, move the new one in), so a
crash or power loss mid-apply leaves a mixed install. The next check compares each local file's
SHA1 against the manifest and re-downloads only what does not match, which repairs it. Locked
files are retried for three launches before the update is abandoned with a message telling the
user to reinstall.

### A bad index or manifest on the server — degrades, then reverts
A malformed `releases/index.json` leaves the catalog empty and the version picker hidden; the
updater keeps following its branch. A malformed `patch_manifest2.json` falls back to the legacy
path. Neither breaks updating. To fix it, revert the commit and push — the next client check
re-diffs against the corrected files and self-corrects.

### A build that will not load — the one case with no in-app recovery
`InitUpdater()` runs at the end of `Awake`. If a bad build throws earlier, there is no updater and
no gallery UI, so the user **cannot** roll back from inside VaM. This is the failure worth fearing;
nothing above helps.

What still works, because it does not depend on `VPB.dll`:

- `VPBPatcher` is a BepInEx **preloader patcher**. It runs before VaM's assemblies load and applies
  anything already staged, so an update staged before the breakage still lands.
- The install is plain files. Recovery is: download `vam_patch/` at a known-good tag
  (`https://github.com/gicstin/VPB/tree/build-0.32.610`) and copy it over the VaM folder. That is
  exactly what the updater would have done.
- Immediately after a bad update the previous file is still on disk as `VPB.dll.old` — but
  `CleanupOldFiles` deletes it at the very next launch, so do not rely on it.

The practical defence is procedural, not technical: do not push a build to `main` that has not
started once in VaM. Everything else in this pipeline is recoverable; this is not.

## Repository protections

These are the only part of the process that cannot live in the repo. Apply them in GitHub settings:

- **Branch protection on `main`** — no direct pushes.
- **Tag protection on `build-*`** — only the release flow creates them, so a tag always names a
  build that actually shipped.
- **Releases created by the workflow only.** The workflow's `GITHUB_TOKEN` is per-run and expires,
  so no long-lived publish credential exists for anyone to leak or share.

Contributors get repository write; nobody gets the ability to overwrite a published build. That is
the property a CDN bucket cannot give you, because a bucket key is one shared secret with no
per-person identity, no review gate, and no audit trail.
