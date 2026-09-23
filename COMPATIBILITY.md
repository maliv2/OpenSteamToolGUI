# OpenSteamTool compatibility notes

Research snapshot: 2026-09-23. Official latest release observed: [1.4.8](https://github.com/OpenSteam001/OpenSteamTool/releases/tag/1.4.8). The GUI checks GitHub for the latest release at runtime rather than relying on this snapshot.

| Setting or feature | Release 1.4.8 | Current main source |
| --- | --- | --- |
| `log.level` | Yes; log files require Debug build | Yes |
| `manifest.url`, four timeout values | Yes | Yes |
| `lua.paths` | Yes | Yes |
| `inject.enabled`, x64/x86 DLL paths | Yes | Yes |
| `remote.url_template` | Yes | Yes |
| `stats.enable_api` | No | Yes |
| `cloud.enabled`, `cloud.library` | No | Yes |
| `setManifestid` optional size | Ignored by 1.4.8 | Supported by current source |
| `pinApp` | Not registered | Not registered |

Source references: [release example TOML](https://github.com/OpenSteam001/OpenSteamTool/blob/1.4.8/opensteamtool.example.toml), [current example TOML](https://github.com/OpenSteam001/OpenSteamTool/blob/main/opensteamtool.example.toml), [release Lua implementation](https://github.com/OpenSteam001/OpenSteamTool/blob/1.4.8/src/Utils/Config/LuaConfig.cpp), [official installation directions](https://github.com/OpenSteam001/OpenSteamTool/releases/tag/1.4.8).

Lua scripts belong in `<Steam>/config/lua` and the three release DLLs belong beside `steam.exe`. The upstream README does not specify a local `.manifest` import folder. This GUI uses Steam's root `depotcache`; it reports a pre-existing `config/depotcache` directory for manual review.
