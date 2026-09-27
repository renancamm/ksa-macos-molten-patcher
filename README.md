# MacOSMoltenPatcher

A Kitten Space Agency mod that makes the game work on macOS, where it runs through Wine
(e.g. a Sikarugir wrapper). A StarMap hook runs it before the game starts; it applies the
patches in its `patches/` folder to the wrapper and the game files.

Each patch is a plain text file that says what it changes and why. A patch is skipped if its
target file isn't as expected (for example after a game update), and applied again if an update
restores the original. On Windows or Linux the mod does nothing.

## Install

Requires StarMap.

1. Download `MacOSMoltenPatcher.zip` from the latest release.
2. Extract the `MacOSMoltenPatcher` folder into KSA's mods folder
   (`Documents/My Games/Kitten Space Agency/mods/`).
3. Start the game through StarMap.

## Warning: changes are permanent

The patches edit files in your wrapper and game folder. **Removing the mod does not undo them.**
Before changing a file, the mod saves the original next to it as `<file>.orig`. To revert, copy
the `.orig` back over the file.

## Licenses

Mod code: MIT. Bundled third-party files come with their licenses in the `licenses/` folder.
