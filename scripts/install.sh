#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
source_binary="$script_dir/ytmusic"
install_directory=${LIGHTYTP_INSTALL_DIR:-"$HOME/.local/bin"}
install_binary="$install_directory/lightytp"

if [ ! -f "$source_binary" ]; then
    echo "LightYTP installer files are incomplete." >&2
    echo "Download the macOS or Linux release archive, not GitHub's Source code archive." >&2
    exit 2
fi

missing=""
for tool in yt-dlp mpv deno; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        missing="$missing $tool"
    fi
done

if [ -n "$missing" ]; then
    echo "Install the missing playback tools before LightYTP:$missing" >&2
    if [ "$(uname -s)" = "Darwin" ]; then
        echo "  brew install yt-dlp mpv deno" >&2
    else
        echo "  Use your Linux package manager to install yt-dlp, mpv, and preferably Deno." >&2
    fi
    exit 2
fi

mkdir -p "$install_directory"
cp "$source_binary" "$install_binary"
chmod +x "$install_binary"

echo "Installed LightYTP to $install_binary"
case ":$PATH:" in
    *":$install_directory:"*) ;;
    *)
        echo "Add this line to your shell profile, then open a new terminal:"
        echo "  export PATH=\"$install_directory:\$PATH\""
        ;;
esac
echo "Run: lightytp"
