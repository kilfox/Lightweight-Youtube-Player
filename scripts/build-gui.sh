#!/usr/bin/env sh
set -eu

repository_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
project="$repository_root/src/LightYTP.Gui/LightYTP.Gui.csproj"

if [ "$#" -gt 0 ]; then
    runtime=$1
else
    architecture=$(uname -m)
    case "$(uname -s):$architecture" in
        Linux:x86_64) runtime=linux-x64 ;;
        Linux:aarch64|Linux:arm64) runtime=linux-arm64 ;;
        Darwin:x86_64) runtime=osx-x64 ;;
        Darwin:arm64) runtime=osx-arm64 ;;
        *) echo "Unsupported platform: $(uname -s) $architecture" >&2; exit 2 ;;
    esac
fi

case "$runtime" in
    linux-x64|linux-arm64|osx-x64|osx-arm64) ;;
    *) echo "Unsupported runtime: $runtime" >&2; exit 2 ;;
esac

output="$repository_root/artifacts/gui-$runtime"
case "$output" in
    "$repository_root/artifacts/"*) rm -rf -- "$output" ;;
    *) echo "Refusing to replace an output directory outside the repository artifacts directory." >&2; exit 2 ;;
esac

publish_output=$output
case "$runtime" in
    osx-*) publish_output="$output/LightYTP GUI.app/Contents/MacOS" ;;
esac

dotnet publish "$project" \
    -c Release \
    -r "$runtime" \
    --self-contained true \
    -o "$publish_output" \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:TrimMode=partial \
    -p:EnableCompressionInSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true
rm -f -- "$publish_output"/*.pdb
rm -f -- "$publish_output/ytmusic.runtimeconfig.json"

case "$runtime" in
    osx-*)
        cp "$repository_root/packaging/macos/Info.plist" "$output/LightYTP GUI.app/Contents/Info.plist"
        mkdir -p "$output/LightYTP GUI.app/Contents/Resources"
        cp "$repository_root/packaging/macos/lightytp.icns" "$output/LightYTP GUI.app/Contents/Resources/lightytp.icns"
        chmod +x "$publish_output/lightytp-gui"
        ;;
    *)
        cp "$repository_root/src/LightYTP.Gui/Assets/lightytp.png" "$output/lightytp.png"
        chmod +x "$publish_output/lightytp-gui"
        ;;
esac

for document in README.md GUI_MANUAL.md GUI_HOTKEYS.md LICENSE THIRD_PARTY_NOTICES.md; do
    cp "$repository_root/$document" "$output/$document"
done
cp "$repository_root/scripts/install-gui.sh" "$output/install.sh"
chmod +x "$output/install.sh"

echo "Published LightYTP GUI to $output"
