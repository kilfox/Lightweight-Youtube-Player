#!/usr/bin/env sh
set -eu

repository_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
project="$repository_root/src/YtMusicTerminal/YtMusicTerminal.csproj"
tests="$repository_root/tests/YtMusicTerminal.Tests/YtMusicTerminal.Tests.csproj"

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

output="$repository_root/artifacts/$runtime"

case "$output" in
    "$repository_root/artifacts/"*) rm -rf -- "$output" ;;
    *) echo "Refusing to replace an output directory outside the repository artifacts directory." >&2; exit 2 ;;
esac

dotnet build "$repository_root/YtMusicTerminal.slnx" -c Release
dotnet run --project "$tests" -c Release --no-build
dotnet publish "$project" \
    -c Release \
    -r "$runtime" \
    --self-contained true \
    -o "$output" \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:TrimMode=full \
    -p:EnableCompressionInSingleFile=true

for document in README.md MANUAL.md HOTKEYS.md LICENSE THIRD_PARTY_NOTICES.md; do
    cp "$repository_root/$document" "$output/$document"
done
cp "$repository_root/scripts/install.sh" "$output/install.sh"
chmod +x "$output/ytmusic" "$output/install.sh"

echo "Published to $output"
