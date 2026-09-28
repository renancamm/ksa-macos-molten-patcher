# CLAUDE.md

StarMap mod for Kitten Space Agency (KSA) on macOS, where KSA runs in a **Sikarugir** (Wine)
wrapper. Before the game starts, `Mod.cs` applies the files in `patches/`: a newer MoltenVK in the
wrapper, and a shader fix in the game folder.

## Files

- `Mod.cs` — the patch engine (~160 lines). Keep it small and dependency-free.
- `patches/NNNN-name.patch` (a diff) or `NNNN-name.replace` (a whole file) — one per change,
  applied in name order. Title + why, a `---` line, then the change. Shipped to users, keep readable.
- `build.sh` — builds what the `.replace` patches install into `files/` (at the target's path:
  `files/wrapper/…` or `files/game/…`) and `licenses/`. Both gitignored.
- `.github/workflows/build.yml` — `build.sh` + `dotnet build` + zip (read-only job), then a separate
  release job (the only one with write access, `main` only, zip sha256 in the notes). Manual
  trigger only. Actions pinned by SHA, runner by version.
- `README.md` — user-facing, generic (see Style).

## Patch engine rules

- **Prefer text patches**: more transparent and easier to audit. `.replace` only for binary files.
- Targets are relative to the game folder (= working directory), or to the wrapper's
  `.app/Contents` with a `wrapper:` prefix (both patch types). **One patch per target file.**
  Targets must stay inside their folder: no rooted paths, no `..` (else skipped).
- No `---` line → skipped.
- Replacement (`.replace`): `target <target>`, then `$ ` lines: the shell steps that build the file
  (run by `build.sh` in an empty temp folder, `$OUT` = file to write, `$LICENSES` = folder for
  license texts; the mod ignores them). Builds aren't reproducible, so no hash in the repo. Mod:
  done if target equals the file in `files/`. `.orig` written only once. Wrapper targets touch the
  `.app`. Never creates new files.
- Text patch: normal diff of exactly one file, hunks matched by text (`@@` numbers ignored). Done
  if all new lines are present; applied only if all old lines occur exactly once; else skipped.
  Keeps CRLF. UTF-8/ASCII files only. `.orig` overwritten on each apply.
- Make one: scratch git repo with the original file (LF, target path), edit,
  `git diff --stat -p > patches/NNNN-name.patch`, add title/why/`---`, check with `git apply --check`.
  Keep 3 lines of context.
- Removing a patch file does not revert already-patched installs.

## Why the patches exist

- **MoltenVK**: KSA uses descriptor sets 0–9. MoltenVK's limit comes from SPIRV-Cross
  `kMaxArgumentBuffers` = 8 → `cannot reserve 'buffer' resource location` → crash. SPIRV-Cross
  `76301541` (2026-09-01) raised it to 16, but MoltenVK (≤ 1.4.2, `main` as of 2026-09-26) pins an
  older one, so we override `ExternalRevisions/SPIRV-Cross_repo_revision`. Drop the override once
  MoltenVK pins a newer SPIRV-Cross.
- **TextureSet.glsl**: shadow macros sample `globalTextures[]` / `globalTextureArrays[]`, so
  SPIRV-Cross declares them `depth2d` (1 channel). Fix adds `globalShadows[]` / `globalShadowsArray[]`
  at the same set/binding. All shadow sampling goes through those macros. Remove once KSA fixes it.
- **App icon**: the wrapper's icon is `Resources/Configure.icns` (Info.plist `CFBundleIconFile`);
  `0003` replaces it with the bottom square of KSA's title card. `sips --cropOffset` silently skips
  the crop if an offset is 0, and `-z` runs before `-c` in the same call.

## Runtime facts

- `[StarMapBeforeMain]` runs before KSA's `Main`: Vulkan not loaded, shaders compiled later
  (shaderc at startup), so both patches work in the same launch.
- StarMap attributes are declared locally in `namespace StarMap.API` (matched by name).
- Wrapper = `WINEPREFIX` cut at `.app/Contents`; no `WINEPREFIX` → do nothing. Mac paths via `Z:`.
- Wine 10 and WineCX load `Frameworks/libMoltenVK.dylib` directly. **Wine 11 is not supported**:
  on macOS 26 it uses KosmicKrisp instead, where KSA crashes (and the mouse misbehaves).
- Replaced dylib loses its executable bit; dlopen doesn't care.

## Status

- Verified in-game 2026-09-28 from a fresh install with the CI zip: KSA 2026.9.22.5482, Apple M5,
  WS12WineSikarugir10.0_6 (recommended engine).

## Running StarMap in the wrapper

- Needs .NET 10 **win-x64** runtime in the prefix; `StarMapConfig.json` `GameLocation` =
  `C:\\Program Files\\Kitten Space Agency`.
- Launch: `"<wrapper>.app/Contents/MacOS/Sikarugir" "<wrapper>.app/Contents/SharedSupport/prefix/drive_c/Program Files/StarMap/StarMap.exe"`
  (the `Sikarugir run <file>` form does not work).
- Borea instance: set `STARMAP_INSTANCE_PATH` to its Windows path (`Z:\Users\…\Instances\<id>`).
- Debug: `MVK_CONFIG_DEBUG=1`, then grep `LastRunWine.log` for `MoltenVK version`, `[mvk-error]`,
  ` error: `, `cannot reserve`.

## Rejected (don't redo)

- Private-API MoltenVK build: only removes primitive-restart warnings.
- Ray-tracing MoltenVK fork: GPU page fault when changing anti-aliasing.
- `MVK_CONFIG_*` / `MTL_*` env vars, `settings.toml` tweaks: not needed.
## Style

- Simple, non-intrusive, small readable code over features.
- Only touch `Frameworks/libMoltenVK.dylib` and files named in `patches/`.
- Each step: a short why comment, what it changes, an "already done" check.
- README stays generic (intent, install, build, permanence warning, licenses): no per-patch details,
  so it stays correct when patches change. Patch files and the workflow document those.
- Workflow runs manually only. Release zips must include the dylib's licenses (`licenses/`).
