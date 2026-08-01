# Third-party software

YT Music Terminal does not include third-party executables in its source tree.
The optional `scripts/bootstrap-tools.ps1` script downloads these tools from their official release repositories:

- [yt-dlp](https://github.com/yt-dlp/yt-dlp) — its Windows standalone build contains GPLv3+ components.
- [mpv](https://mpv.io/) — license terms depend on the selected platform build or package.
- [Deno](https://github.com/denoland/deno) — MIT licensed.

The Windows bootstrap script verifies published SHA-256 checksums before installing the downloads into the ignored local `tools` directory. macOS and Linux users normally install these tools through their system package manager. Review and comply with each project's license before redistributing those executables.

The optional LightYTP GUI packages also use:

- [Avalonia](https://github.com/AvaloniaUI/Avalonia) — Copyright 2013–2026 The AvaloniaUI Project; MIT licensed.
- [SkiaSharp](https://github.com/mono/SkiaSharp) — Copyright Microsoft Corporation; MIT licensed and used by Avalonia for rendering.
- [HarfBuzzSharp](https://github.com/mono/SkiaSharp) — Copyright Microsoft Corporation; MIT licensed and used by Avalonia for text shaping.

The MIT permission and warranty terms are reproduced in [LICENSE](LICENSE). Package-specific notices remain available from the linked source repositories and the corresponding NuGet packages. The terminal edition does not depend on Avalonia or SkiaSharp.
