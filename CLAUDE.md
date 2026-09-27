# CLAUDE.md

Context for continuing work on MacOSMoltenPatcher. Read this before changing anything.
Renamed from MoltenVKUpdater (repo `ksa-macos-moltenvk-patcher`) on 2026-09-27 (never released).

## What this project is

A StarMap mod for Kitten Space Agency (KSA) on macOS. KSA is a Windows game; on macOS it
runs in a **Sikarugir** wrapper (Wine, successor of Wineskin). Before the game starts, the
mod does two things, each one a patch file in `patches/` applied by `Mod.cs`:

1. **MoltenVK**: replaces the wrapper's MoltenVK (Vulkan → Metal) with a newer build.
2. **Shaders**: patches KSA shader files in the game install that render wrong under MoltenVK.

Files:
- `Mod.cs` — the patch engine (~150 lines, one patch per file). Keep it small and dependency-free.
- `patches/*.patch` — everything the mod changes, one file per change: title + why message, a `---`
  line, then the change (git commit-message style, without `format-patch`'s email headers). Shipped in the mod folder, so users can read them. Two kinds:
  - `0001-update-moltenvk.patch` — a *replacement*: after `---`, `replace wrapper:Frameworks/libMoltenVK.dylib`,
    `sha256 <hash>`. The new file lives in `files/` at the target's path (`files/wrapper/…` or
    `files/game/…`), so the patch doesn't name it. Our own mini-format (git's binary
    patches embed the file). The workflow fills in the sha256 and the `MoltenVK:`/`SPIRV-Cross:`
    lines of the message; for a local build, update them by hand or the mod skips it.
  - `0002-fix-textures-treated-as-depth.patch` — a *text patch*: normal diff, one game file.
  Add/remove a patch by adding/deleting a file; the number sets the order.
- `files/` — replacement files at their target paths; only used through a patch. Locally holds
  `files/wrapper/Frameworks/libMoltenVK.dylib` (gitignored).
- `MacOSMoltenPatcher.csproj` — net10.0, x64. Copies `mod.toml`, `patches/` and `files/` to output.
- `mod.toml` — StarMap manifest (`name`, `[StarMap] EntryAssembly`).
- `.github/workflows/build.yml` — builds MoltenVK + mod on `macos-latest`, manual trigger only.
- `README.md` — user-facing docs.

## The problem it fixes (root cause, verified from logs)

- KSA's shaders use **descriptor sets 0–9** (10 sets). Checked by counting
  `OpDecorate … DescriptorSet N` in a `MVK_CONFIG_DEBUG=1` log.
- MoltenVK's `maxBoundDescriptorSets` = `kMVKMaxDescriptorSetCount` =
  SPIRV-Cross `kMaxArgumentBuffers`, which was **8**.
- Sets ≥ 8 get no Metal argument buffer, fall back to discrete slots that collide with
  argument-buffer slots → MSL compile error
  `cannot reserve 'buffer' resource location at index N` → broken pipelines →
  `vkQueueSubmit` fails → crash (Wine asserts in `winevulkan/loader_thunks.c`).
- SPIRV-Cross commit `76301541` (2026-09-01) "MSL: Bump the descriptor set limit to 16" fixes it.
- As of 2026-09-26, MoltenVK `main` (and v1.4.2) still pinned SPIRV-Cross `6c09849…`
  (2026-07-06, limit 8). So we build MoltenVK with a **newer SPIRV-Cross** by writing its
  commit into `ExternalRevisions/SPIRV-Cross_repo_revision` before `./fetchDependencies --macos`.
- Stock Sikarugir template ships MoltenVK 1.4.2.

When MoltenVK upstream pins a SPIRV-Cross from 2026-09-01 or later, the SPIRV-Cross bump is
no longer needed, and eventually a stock MoltenVK release may be enough.

## Shader patch: textures treated as depth (TextureSet.glsl)

- Source: https://forums.ahwoo.com/forums/kitten-space-agency/bug-reports/some-textures-are-treated-as-depths-under-moltenvk.1089/
  (reported 2026-07-28 against v2026.7.9.5018; still unfixed in v2026.9.X.5482).
- `Content/Core/Shaders/Common/TextureSet.glsl` declares `globalTextures[]` / `globalTextureArrays[]`
  and uses them for normal sampling *and* for `SHADOW_SAMPLER` / `SHADOW_ARRAY_SAMPLER`
  (`sampler2DShadow(...)`). SPIRV-Cross marks the whole variable as depth
  (`Compiler::is_depth_image` → `comparison_ids`, filled by `add_hierarchy_to_comparison_ids`),
  so MSL declares it `depth2d` → sampling returns 1 channel instead of 4 → wrong clouds/planet colours.
- Fix: two more declarations at the same set/binding (`globalShadows[]`, `globalShadowsArray[]`)
  and the two shadow macros use them. Neutral on Windows/Linux (same descriptors).
- All shadow sampling goes through those macros (checked with grep over `Content/`), so this
  one file is enough.
- KSA compiles GLSL at startup (`shaderc_shared.dll`, `Brutal.ShaderC.dll`), so editing the file in
  `[StarMapBeforeMain]` works in the same launch. Path is relative to the working directory
  (= game folder). Files are CRLF; the patch keeps the file's line endings.
- The engine (`Apply`, `Replace`, `TextPatch` in `Mod.cs`) applies `patches/*.patch` from the mod
  folder in file name order, each one on its own. Targets are relative to the game folder, or to the
  wrapper's `.app/Contents` with a `wrapper:` prefix. **One patch per target file**: combining a
  replacement and a diff on one file, or two diffs on one file, was dropped (2026-09-27) to keep
  `Mod.cs` easy to audit (`.orig` would then hold a half-patched file).
- Text patches: hunks matched by text (whole lines, LF internally; `@@` lines only separate hunks,
  their numbers are ignored). Already done if every hunk's new lines are present; applied only if every
  hunk's old lines occur exactly once; otherwise skipped (game changed the file). Line endings are kept
  (CRLF if the file has any). `.orig` is written (overwrite) whenever the patch is applied, so it's the
  current game version's original.
- Replacements: already done if the target's sha256 matches. If the mod's file is missing or doesn't
  match `sha256`, skipped. `.orig` is written only once (keeps the wrapper's original MoltenVK, not
  an older version of ours).
- Missing targets are logged and skipped; replacements never create new files.
- Make a text patch: scratch git repo with the original file (LF, game-relative path), edit it,
  `git diff --stat -p > patches/NNNN-name.patch`, then add title + why + a `---` line on top.
  Check with `git apply --check`. Keep 3-line context: it makes "already done"/"exactly once" reliable.
- Removing a patch file does not revert it in already-patched installs.
- The simplified engine was tested in a local harness with a fake wrapper and a CRLF file built from the
  hunk's old lines: apply, rerun (no-op), changed file skipped, restored file re-patched, older dylib
  replaced with `.orig` kept, wrong hash / missing file skipped.
- Tested on a copy of the real file (diff = forum fix, idempotent, skip on changed file).
  **Not yet tested in-game**, and not yet checked for MSL errors with our newer SPIRV-Cross
  (`MVK_CONFIG_DEBUG=1`, grep `cannot reserve` / ` error: `).
- Remove this patch once KSA ships the change itself (the `globalShadows` check then skips it anyway).

## Tested configuration (what works)

- Apple M5, Sikarugir wrapper, KSA via Wine under Rosetta (x86_64).
- MoltenVK `main` (reports 1.4.3) + SPIRV-Cross `aa217aeb6c9f0ace7a0ab233b28807edf45eb165`,
  plain `make macos` (no private API). Clean log: no mvk errors, survives live settings changes.
- No env vars needed. Windowed mode performs better than fullscreen.
- The file Wine actually loads is **`KSA.app/Contents/Frameworks/libMoltenVK.dylib`**
  (confirmed by the user). `Frameworks/moltenvkcx/` is the CodeWeavers variant — don't touch it.

## How the mod works (Mod.cs)

- Hook: `[StarMapBeforeMain]` on an instance `void` method with no parameters, in a
  `[StarMapMod]` class. StarMap calls it synchronously before KSA's `Main`: no window,
  Vulkan/MoltenVK not loaded, working directory = game folder. So replacing the dylib here
  takes effect in the same launch.
- The StarMap attributes are declared locally in `namespace StarMap.API` (StarMap matches by
  name; same approach as KittenExtensions), so there is no NuGet/game-DLL reference.
- Finds the wrapper from the `WINEPREFIX` env var (e.g.
  `/Users/me/Applications/Sikarugir/KSA.app/Contents/SharedSupport/prefix`), cut at
  `.app/Contents`. No `WINEPREFIX` → not Wine → do nothing.
- Reaches the Mac file system through Wine's `Z:` drive (`Z:` + unix path).
- Writes go to `.new` first, then `File.Move(overwrite)` (atomic rename, so a half-written file is
  never loaded). Every error is caught and printed; the game must always start.

### Verified on a real machine (2026-09-27)

- `Mod.cs` (the earlier, pre-patch-engine version) builds locally with `dotnet build -c Release` (macOS .NET 10 SDK, no warnings).
- Tested with the mod in `<KSA>/Content/MacOSMoltenPatcher/`, launched via StarMap in the wrapper:
  it replaced an older `Frameworks/libMoltenVK.dylib` in the same launch and left the existing
  `.orig` untouched. So `WINEPREFIX` is passed into Wine and the `Z:` drive works.
- The replaced dylib loses its executable bit (`File.WriteAllBytes` creates it as 644).
  dlopen doesn't need it; the game ran fine.
- The patch-engine version (`patches/`) has **not yet been run in-game**; only in a local harness.
- The GitHub workflow has still **never been run**.

## Building

- Workflow inputs: `moltenvk_ref` (default `main`), `spirv_cross_ref` (empty = latest),
  `release_tag` (empty = artifact only; set = `gh release create`).
- Workflow mirrors MoltenVK's own CI: `macos-latest`, Python 3.11 (fetchDependencies needs it).
- Checks: SPIRV-Cross `kMaxArgumentBuffers` ≥ 16, dylib contains `x86_64` (Wine runs under
  Rosetta). The dylib is ad-hoc signed (`codesign --force --sign -`).
- MoltenVK is cloned to `$RUNNER_TEMP`, outside the repo, so the SDK's `**/*.cs` glob and
  `dotnet build` don't pick it up.
- For releases, prefer pinning both refs to a tested combination rather than latest `main`.

## Licensing

- Mod code: MIT (same as other KSA mods: HeadlessHarness, KSA_XR, KittenExtensions).
- `libMoltenVK.dylib` contains MoltenVK + SPIRV-Cross (Apache 2.0), cereal (BSD-3),
  Vulkan-Headers (Apache 2.0 / MIT). Release zips must include their license texts
  (`licenses/` folder, done by the workflow). MoltenVK source is unmodified.

## Things tried and rejected (don't redo)

- **Private API build** (`MVK_USE_METAL_PRIVATE_API=1`): works; its only visible effect for
  KSA is that primitive restart can be disabled (removes the "Metal does not support
  disabling primitive restart" warnings). Not a performance feature. Dropped in favor of a
  standard build.
- **Ray-tracing fork** (`dttdrv/MoltenVK` branch `macgaming/ray-query-pr`, upstream WIP PR
  #2771, with its own SPIRV-Cross fork at limit 32): fixed the crash too, but changing
  anti-aliasing caused a GPU address fault (`kIOGPUCommandBufferCallbackErrorPageFault`) →
  `VK_ERROR_DEVICE_LOST`. KSA enables the RT extensions but no shader used ray queries in
  tests. Not used.
- **Env vars** (`MVK_CONFIG_*`, `MTL_*` from forum lists): most were defaults or not real
  options. None are needed. `MVK_CONFIG_DEBUG=1` is useful only for diagnosing (huge logs).
- **Editing settings.toml** (`terrainTessellation`, `groundClutter` = false, from the KSA wiki
  macOS guide): not needed with the new MoltenVK. `groundClutter` works but is slow.
- In Sikarugir's Configure app, enabling D3DMetal (GPTK) also sets `MOLTENVKCX = 1`
  (CodeWeavers MoltenVK) and doesn't unset it. The FastMath checkbox is a separate key.

## Running StarMap in the wrapper

- StarMap needs the .NET 10 **win-x64** runtime in the prefix (`C:\Program Files\dotnet`);
  KSA is self-contained, so the prefix has none by default.
- `StarMapConfig.json` `GameLocation` must be a Windows path: `C:\\Program Files\\Kitten Space Agency`.
- Launch StarMap once without changing the wrapper's main program (macOS absolute path, no `run`):
  `"<wrapper>.app/Contents/MacOS/Sikarugir" "<wrapper>.app/Contents/SharedSupport/prefix/drive_c/Program Files/StarMap/StarMap.exe"`.
  The `Sikarugir run <file>` form from its help text does NOT work (treats `run` as the file).
- To use a Borea instance instead of `My Games`, set `STARMAP_INSTANCE_PATH` to its Windows path
  (`Z:\Users\…\Library\Application Support\Borea\Instances\<id>`) before that command. Wine passes
  it through; StarMap redirects KSA's documents folder there (logs, settings, saves, mods, manifest).

## Debugging tips

- The Wine log (`LastRunWine.log`) with `MVK_CONFIG_DEBUG=1` contains SPIR-V and MSL for every
  shader. Useful greps: `MoltenVK version`, `\[mvk-(error|warn)\]`, ` error: `,
  `cannot reserve`, `GPU Address Fault`, `Created VkDevice`.
- Check a dylib: `lipo -archs` (needs x86_64), `strings … | grep -c setPrimitiveRestartEnabled`
  (0 = no private API).

## Style preferences of the maintainer

- Keep things as simple and non-intrusive as possible; small readable code over features.
- Only touch `Frameworks/libMoltenVK.dylib` and the files named in `patches/`.
  Each step must be easy to follow: a comment saying why, what it changes, and an "already done" check.
- Workflow runs manually only.
