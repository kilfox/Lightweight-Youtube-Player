#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
binary_directory=${LIGHTYTP_GUI_INSTALL_DIR:-"$HOME/.local/share/lightytp-gui"}
command_directory=${LIGHTYTP_BIN_DIR:-"$HOME/.local/bin"}
command_path="$command_directory/lightytp-gui"

missing=""
for tool in yt-dlp mpv deno; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        missing="$missing $tool"
    fi
done

if [ -n "$missing" ]; then
    echo "Install the missing playback tools before LightYTP GUI:$missing" >&2
    if [ "$(uname -s)" = "Darwin" ]; then
        echo "  brew install yt-dlp mpv deno" >&2
    else
        echo "  Use your Linux package manager to install yt-dlp, mpv, and preferably Deno." >&2
    fi
    exit 2
fi

mkdir -p "$command_directory"
if [ "$(uname -s)" = "Darwin" ]; then
    source_app="$script_dir/LightYTP GUI.app"
    install_app=${LIGHTYTP_GUI_APP_DIR:-"$HOME/Applications/LightYTP GUI.app"}
    if [ ! -d "$source_app" ]; then
        echo "The macOS GUI package is incomplete." >&2
        exit 2
    fi

    mkdir -p "$(dirname -- "$install_app")"
    case "$install_app" in
        "$HOME"/*.app) ;;
        *) echo "Refusing to replace an application outside your home directory: $install_app" >&2; exit 2 ;;
    esac
    rm -rf -- "$install_app"
    cp -R "$source_app" "$install_app"
    printf '%s\n' '#!/usr/bin/env sh' "exec open -a \"$install_app\" --args \"\$@\"" > "$command_path"
    chmod +x "$command_path"
    echo "Installed LightYTP GUI to $install_app"
else
    source_binary="$script_dir/lightytp-gui"
    if [ ! -f "$source_binary" ]; then
        echo "The Linux GUI package is incomplete." >&2
        exit 2
    fi

    mkdir -p "$binary_directory"
    cp -R "$script_dir/." "$binary_directory/"
    chmod +x "$binary_directory/lightytp-gui"
    printf '%s\n' '#!/usr/bin/env sh' "exec \"$binary_directory/lightytp-gui\" \"\$@\"" > "$command_path"
    chmod +x "$command_path"

    desktop_directory=${XDG_DATA_HOME:-"$HOME/.local/share"}/applications
    desktop_path="$desktop_directory/lightytp-gui.desktop"
    mkdir -p "$desktop_directory"
    printf '%s\n' \
        '[Desktop Entry]' \
        'Type=Application' \
        'Name=LightYTP GUI' \
        'Comment=Lightweight audio-only YouTube player' \
        "Exec=\"$binary_directory/lightytp-gui\"" \
        'Terminal=false' \
        'Categories=AudioVideo;Audio;Player;' > "$desktop_path"
    chmod +x "$desktop_path"
    echo "Installed LightYTP GUI to $binary_directory"
fi

case ":$PATH:" in
    *":$command_directory:"*) ;;
    *)
        echo "Add this line to your shell profile, then open a new terminal:"
        echo "  export PATH=\"$command_directory:\$PATH\""
        ;;
esac
echo "Launch with: lightytp-gui"
