# API14 port

Upstream API14 Release build passed. TC API14 build and in-game verification are pending.

Requires .NET SDK 10.0.101 or newer. Use `.api14/build.ps1 -DalamudHome <isolated-api14-directory>`. The script rejects non-14 Dalamud assemblies and applies versioned patches to pinned submodules.

Reference baseline: upstream Dalamud 14.0.4.3, distribution commit `261f5b0cd1f5746533d3d13cf01d9aa62853a746`, archive SHA-256 `81704d31a1690801f87c8fdb3c2c7956afcf7e7f56dd04fa37f9b1d3813082ca`.

Traditional Chinese resources and TC fixes remain in this branch. Shared dependencies are kept at their existing commits; patch identities are recorded in `.api14/dependencies.json`. This branch is not a verified TC release.
