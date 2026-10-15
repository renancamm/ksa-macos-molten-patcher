# CLAUDE.md

StarMap mod for Kitten Space Agency (KSA) on macOS, where KSA runs in a **Sikarugir** (Wine)
wrapper. Before the game starts, `Mod.cs` applies the files in `patches/` to the wrapper and the
game folder.

## Files

- `Mod.cs` — the patch engine. Keep it small and dependency-free.
- `patches/NNNN-name.patch` (a diff) or `NNNN-name.replace` (a whole file) — one per change,
  applied in name order. Title + why, a `---` line, then the change. Shipped to users, keep readable.
  A `.disabled` suffix turns a patch off.
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
- `uninstall.toml` `revert_changes = true`: instead of applying, each patch moves `<target>.orig`
  back (if present). No checks (a KSA reinstall fixes a stale one).

## Patches

Each patch file states its own why and removal condition. Facts the patch files don't carry:

- **0001 MoltenVK**: KSA uses descriptor sets 0–9; released MoltenVK (≤ 1.4.2) allows 8
  (SPIRV-Cross `kMaxArgumentBuffers`), `main` allows 16. The `.replace` builds `main`.
- **0002 TextureSet.glsl** (removed 2026-10-15): gave shadow lookups their own `globalShadows[]`
  arrays so SPIRV-Cross would not declare `globalTextures[]` as `depth2d`. Not needed with the
  `main` MoltenVK build: KSA 2026.10.13.5602 renders correctly without it.
- **0003 App icon**: the wrapper's icon is `Resources/Configure.icns` (Info.plist
  `CFBundleIconFile`). `sips --cropOffset` silently skips the crop if an offset is 0, and `-z` runs
  before `-c` in the same call.
- **0004 KosmicKrisp manifest**: Wine 11 wrappers ship
  `Resources/vulkan/icd.d/{MoltenVK,kosmickrisp_mesa}_icd.json`; the launcher binary sets
  `VK_DRIVER_FILES` to the KosmicKrisp one. Environment variables set by the mod or a StartupScript
  do not reach the loader, so the manifest itself is patched.

## Runtime facts

- `[StarMapBeforeMain]` runs before KSA's `Main`: Vulkan not loaded, shaders compiled later
  (shaderc at startup), so wrapper and shader patches work in the same launch.
- StarMap attributes are declared locally in `namespace StarMap.API` (matched by name).
- Wrapper = `WINEPREFIX` cut at `.app/Contents`; no `WINEPREFIX` → do nothing. Mac paths via `Z:`.
- Wine 10 and WineCX load `Frameworks/libMoltenVK.dylib` directly. Wine 11 goes through the
  Vulkan loader (`Frameworks/libvulkan.1.dylib`) and the manifest the launcher selects (see `0004`).
  KSA crashes on KosmicKrisp.
- Replaced dylib loses its executable bit; dlopen doesn't care.

## Status

- Verified in-game 2026-10-15 from a fresh wrapper with a local build: KSA 2026.10.13.5602, Apple M5,
  WS12WineSikarugir11.0_1, macOS 26.5.1. Earlier: 2026-09-28, KSA 2026.9.22.5482, WS12WineSikarugir10.0_6.

## Running StarMap in the wrapper

- Needs .NET 10 **win-x64** runtime in the prefix; `StarMapConfig.json` `GameLocation` =
  `C:\\Program Files\\Kitten Space Agency`.
- Launch: `"<wrapper>.app/Contents/MacOS/Sikarugir" "<wrapper>.app/Contents/SharedSupport/prefix/drive_c/Program Files/StarMap/StarMap.exe"`.
- Borea instance: set `STARMAP_INSTANCE_PATH` to its Windows path (`Z:\Users\…\Instances\<id>`).
- Debug: `MVK_CONFIG_DEBUG=1`, then grep `LastRunWine.log` for `MoltenVK version`, `[mvk-error]`,
  ` error: `, `cannot reserve`.

## Style

- Simple, non-intrusive, small readable code over features.
- Only touch files named in `patches/`.
- Each step: a short why comment, what it changes, an "already done" check.
- README stays generic (intent, install, build, permanence warning, licenses): no per-patch details,
  so it stays correct when patches change. Patch files and the workflow document those.
- Workflow runs manually only. Release zips must include the dylib's licenses (`licenses/`).
