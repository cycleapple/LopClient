# API14 port

Upstream API14 Release build passed. TC API14 build and in-game verification are pending.

Requires .NET SDK 10.0.101 or newer. Use `.api14/build.ps1 -DalamudHome <isolated-api14-directory>`. The script rejects non-14 Dalamud assemblies and applies versioned patches to pinned submodules.

Reference baseline: upstream Dalamud 14.0.5.1, distribution commit `6fb2b12018922bddeef009edd26082f068f8c602`, archive SHA-256 `6a9f9aee19af4280f2d61680bbff960ae48e5dcaad70707baad857da2f9d8e48`.

Traditional Chinese resources and TC fixes remain in this branch. Shared dependencies are kept at their existing commits; patch identities are recorded in `.api14/dependencies.json`. This branch is not a verified TC release.
