#!/bin/bash
# Builds the files that the *.replace patches install, by running the "$ " lines in each patch.
# Output goes to files/ (at the target's path) and licenses/. Then run: dotnet build -c Release
set -e
repo="$PWD"
mkdir -p licenses

for patch in patches/*.replace; do
    # "target wrapper:Frameworks/x" -> files/wrapper/Frameworks/x, "target x" -> files/game/x
    target=$(grep '^target ' "$patch" | cut -d' ' -f2)
    if [[ "$target" == wrapper:* ]]; then
        out="$repo/files/wrapper/${target#wrapper:}"
    else
        out="$repo/files/game/$target"
    fi
    mkdir -p "$(dirname "$out")"

    # Run the "$ " lines as one script, in an empty folder outside the repo.
    echo "Building $out from $patch"
    steps=$(mktemp)
    grep '^\$ ' "$patch" | cut -c3- > "$steps"
    (cd "$(mktemp -d)" && OUT="$out" LICENSES="$repo/licenses" bash -e "$steps")

    # Write the new file's hash into the patch, so the mod accepts it.
    hash=$(shasum -a 256 "$out" | cut -d' ' -f1)
    sed -i '' "s/^sha256 .*/sha256 $hash/" "$patch"
    echo "$patch: sha256 $hash"
done
