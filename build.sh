#!/bin/bash
# Builds the files that the *.replace patches install, by running the "$ " lines in each patch.
# Output goes to files/ (at the target's path) and licenses/. Then run: dotnet build -c Release
set -e
repo="$PWD"
mkdir -p licenses

for patch in patches/*.replace; do
    # "target wrapper:Frameworks/x" -> files/wrapper/Frameworks/x, "target x" -> files/game/x
    target=$(sed -n 's/^target //p' "$patch")
    if [[ "$target" == wrapper:* ]]; then
        out="$repo/files/wrapper/${target#wrapper:}"
    else
        out="$repo/files/game/$target"
    fi
    mkdir -p "$(dirname "$out")"

    # Run the "$ " lines as one script, in an empty folder outside the repo (kept if it fails).
    echo "Building $out from $patch"
    tmp=$(mktemp -d)
    mkdir "$tmp/work"
    grep '^\$ ' "$patch" | cut -c3- > "$tmp/steps.sh"
    (cd "$tmp/work" && OUT="$out" LICENSES="$repo/licenses" bash -e "$tmp/steps.sh")
    rm -rf "$tmp"
done
