# Third-party tools

YT Music Terminal does not include third-party executables in its source tree.
The optional `scripts/bootstrap-tools.ps1` script downloads these tools from their official release repositories:

- [yt-dlp](https://github.com/yt-dlp/yt-dlp) — its Windows standalone build contains GPLv3+ components.
- [mpv](https://mpv.io/) — license terms depend on the selected platform build or package.
- [Deno](https://github.com/denoland/deno) — MIT licensed.

The Windows bootstrap script verifies published SHA-256 checksums before installing the downloads into the ignored local `tools` directory. macOS and Linux users normally install these tools through their system package manager. Review and comply with each project's license before redistributing those executables.
