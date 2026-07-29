# Third-party tools

YT Music Terminal does not include third-party executables in its source tree.
The optional `scripts/bootstrap-tools.ps1` script downloads these tools from their official release repositories:

- [yt-dlp](https://github.com/yt-dlp/yt-dlp) — its Windows standalone build contains GPLv3+ components.
- [mpv](https://mpv.io/) — license terms depend on the selected Windows build.
- [Deno](https://github.com/denoland/deno) — MIT licensed.

The bootstrap script verifies published SHA-256 checksums before installing the downloads into the ignored local `tools` directory. Review and comply with each project's license before redistributing those executables.

