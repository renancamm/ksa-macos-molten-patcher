# MacOSMoltenPatcher

KSA mod for macOS (Sikarugir wrappers). It does two things before the game starts:

1. **Updates MoltenVK** in the wrapper, to fix crashes.
2. **Patches one KSA shader**, to fix wrong colours (clouds, planets).

Both are listed as readable patch files in the mod's `patches/` folder.

## Why

**MoltenVK:** Kitten Space Agency uses 10 Vulkan descriptor sets. The MoltenVK in current Sikarugir
wrappers allows only 8, so some shaders fail to compile and the game crashes with
`VK_ERROR_DEVICE_LOST`. A MoltenVK built with SPIRV-Cross from 2026-09-01 or later
allows 16, which fixes it.

**Shader:** KSA samples shadow maps through the same texture arrays as normal textures.
MoltenVK (through SPIRV-Cross) then treats the whole array as depth textures, which
return one colour channel instead of four. Shadow lookups get their own arrays, bound
to the same textures, which fixes it. On Windows and Linux the change makes no
difference. See the
[forum report](https://forums.ahwoo.com/forums/kitten-space-agency/bug-reports/some-textures-are-treated-as-depths-under-moltenvk.1089/).

## What it does

Before the game starts, and only in a macOS wrapper, the mod applies the patches in
its `patches/` folder. They are plain text files: each starts with a title and a
message saying why it's needed, then, after a `---` line, exactly what it changes. Read them
before installing if you like. There are currently two:

1. `0001-update-moltenvk.patch` replaces `KSA.app/Contents/Frameworks/libMoltenVK.dylib`
   in your wrapper with the mod's `files/wrapper/Frameworks/libMoltenVK.dylib`. The patch records that file's
   SHA-256 and the MoltenVK and SPIRV-Cross commits it was built from; the mod refuses
   to install a file with a different hash. The first time, the wrapper's original is
   kept as `libMoltenVK.dylib.orig`.
2. `0002-fix-textures-treated-as-depth.patch` is a normal diff of
   `Content/Core/Shaders/Common/TextureSet.glsl` in the game folder: two new
   declarations (`globalShadows`, `globalShadowsArray`), and the `SHADOW_SAMPLER` and
   `SHADOW_ARRAY_SAMPLER` macros point at them. The unedited file is kept as
   `TextureSet.glsl.orig`. You can check it against a game file with
   `git apply --check` or `patch -p1 --dry-run`.

If a file doesn't look exactly as expected (for example, a game update changed it),
the mod leaves it alone and prints a message.

Nothing else is touched.

- Both run before Vulkan loads and before KSA compiles its shaders, so the fixes
  work in the same launch.
- If a Sikarugir or KSA update puts the old files back, the mod patches them again.
- On Windows, or outside a macOS wrapper, the mod does nothing.

## Installing

Requires StarMap and KSA running in a Sikarugir wrapper.

1. Download `MacOSMoltenPatcher.zip` from the latest release.
2. Extract the `MacOSMoltenPatcher` folder into the KSA mods folder inside your wrapper:
   `drive_c/users/<you>/Documents/My Games/Kitten Space Agency/mods/`
   (right-click the wrapper → Show Package Contents to get inside it).
3. Launch the game through StarMap.

To check that it worked, search the wrapper's Wine log for `MoltenVK version`:
it should no longer say 1.4.2.

## Uninstalling

Remove the `MacOSMoltenPatcher` folder. Then copy the originals back:

- in `KSA.app/Contents/Frameworks/`: `libMoltenVK.dylib.orig` over `libMoltenVK.dylib`
- in the game's `Content/Core/Shaders/Common/`: `TextureSet.glsl.orig` over `TextureSet.glsl`

## Building

**On GitHub (recommended):** the workflow in `.github/workflows/build.yml` builds
MoltenVK and the mod on GitHub's Macs. It only runs when you start it:
Actions → Build → Run workflow. In that form you can:

- choose the MoltenVK and SPIRV-Cross versions (by default, the latest of each)
- fill in a release tag like `v1.0.0` to also publish a GitHub release

The zip is attached to the run. It contains the mod, the licenses and `versions.txt`,
which records the MoltenVK and SPIRV-Cross commits it was built from. The workflow
also writes those commits and the dylib's SHA-256 into `patches/0001-update-moltenvk.patch`.

**On your Mac:** needs full Xcode and the .NET 10 SDK.

1. Build MoltenVK with the latest SPIRV-Cross:
   ```bash
   git clone https://github.com/KhronosGroup/MoltenVK.git && cd MoltenVK
   git ls-remote https://github.com/KhronosGroup/SPIRV-Cross.git HEAD | cut -f1 > ExternalRevisions/SPIRV-Cross_repo_revision
   ./fetchDependencies --macos
   make macos
   ```
2. Copy `Package/Release/MoltenVK/dynamic/dylib/macOS/libMoltenVK.dylib` into this
   repo's `files/wrapper/Frameworks/` and sign it there:
   `codesign --force --sign - files/wrapper/Frameworks/libMoltenVK.dylib`
3. Put its hash in the `sha256` line of `patches/0001-update-moltenvk.patch`
   (`shasum -a 256 files/wrapper/Frameworks/libMoltenVK.dylib`), and update the commits in its message.
   Otherwise the mod skips the MoltenVK update.
4. Run `dotnet build -c Release`. The mod is `MacOSMoltenPatcher.dll`, `mod.toml`,
   `patches/` and `files/` in `bin/Release/net10.0/`.

## Licenses

The mod's code is MIT-licensed. The bundled `libMoltenVK.dylib` is under its own
licenses: MoltenVK and SPIRV-Cross (Apache 2.0), cereal (BSD 3-clause) and
Vulkan-Headers. They are included in the `licenses` folder of each release.
MoltenVK is used unmodified; only the SPIRV-Cross version it is built against is newer.

## Adding a patch

For a game file: in a scratch git repo holding the original file (LF line endings, same
path as in the game folder), edit it and save the diff with `git diff --stat -p > patches/NNNN-name.patch`.
Then write a title and the reason at the top of that file, followed by a line with
just `---`. Keep git's default 3 lines of context, and check the result with
`git apply --check`. The
number in front of the name sets the order.

To replace a whole file (for binaries; prefer a diff for text files): put the new file in
`files/`, at the target's path: `files/wrapper/<path in KSA.app/Contents>` or
`files/game/<path in the game folder>`. Then add a patch with a title and the reason, a
`---` line, and:

```
replace wrapper:Frameworks/libMoltenVK.dylib
sha256  <shasum -a 256 of the new file>
```

(no `wrapper:` for a game file). A file in `files/` without a patch is ignored. Replaced
files lose their executable bit, so don't replace programs this way.

Each patch changes one file, and two patches must not change the same file.

To remove a patch, delete its file (installs
that already have it stay patched until their `.orig` is copied back).
