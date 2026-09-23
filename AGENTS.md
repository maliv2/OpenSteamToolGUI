# OpenSteamTool GUI — project handoff for future agents

2026-09-23 addition: The GUI now has a separate GitHub Release based self update in App Settings. It verifies the portable ZIP SHA-256 digest, stages a flat archive, and runs an embedded PowerShell helper after shutdown to replace app files with rollback on copy failure. `Assets/app.ico` is the app/window icon. `.github/workflows/build-release.yml` builds on main pushes and publishes both ZIPs for `v*` tags. Check live workflow and release status before claiming publication. The application still separately manages upstream OpenSteamTool DLL releases.

This file records the **current source state as inspected on 2026-09-23**, the original product intent, and the operational rules that must survive future sessions. Read `README.md` for user instructions and `COMPATIBILITY.md` for upstream evidence. This workspace currently has **no `.git` directory**; file timestamps and prior-session context can indicate subsequent work, but cannot prove authorship or an exact change history. Do not overwrite newer user work based on an older plan.

## Product and upstream scope

- Windows 10/11 x64 desktop manager written in C# / .NET 10 / WPF. It manages the **official OpenSteamTool release**; it does not compile the upstream C++ project.
- Seven pages: Dashboard, Library, Import, OpenSteamTool Settings, Backups, Diagnostics, App Settings. The current UI is code-behind plus separate core services, **not a full MVVM implementation** despite the original plan.
- Upstream research snapshot: latest observed stable release was **1.4.8 on 2026-09-23**. Runtime release lookup uses the GitHub latest-release API. Recheck upstream before changing compatibility logic; do not assume the snapshot is still current. Sources and the release/main settings matrix are in `COMPATIBILITY.md`.
- Installed DLLs belong beside `steam.exe`: `dwmapi.dll`, `xinput1_4.dll`, `OpenSteamTool.dll`. Lua goes to `<Steam>\config\lua`. This GUI imports `.manifest` into `<Steam>\depotcache`; upstream's README does not itself define a local manifest-import path. Diagnostics flags an existing `<Steam>\config\depotcache` for manual review.
- A saved file is **not proof that Steam or OpenSteamTool applied it**. Do not claim live activation from file state alone. `pinApp` is not an active supported feature. In release 1.4.8 the main-branch `stats.enable_api`, CloudRedirect settings, and manifest-size behavior are unsupported; the GUI must not present them as effective there. `ToolCapabilities.ForVersion` currently returns all three flags as false for every version, pending verified version-specific implementation.

## Current behavior and later UI work

- The user requested English on first launch. `AppPreferences.Language` and `UiText.Language` now default to **`en`**; an existing saved language choice remains in effect. Current appearance choices are **Dark, Light, System**; legacy `Fluent` preference is interpreted as Light. The original plan's Fluent option remains unimplemented.
- Ten language choices exist: English, Turkish, German, French, Spanish, Brazilian Portuguese, Russian, Simplified Chinese, Japanese, Korean. `Core/LanguageService.cs` translates navigation and field labels; `Core/UiText.cs` loads complete built-in message catalogs from `Core/Turkish.json` and the eight locale JSON files plus `Core/Errors.tsv` for application-defined errors. Keep new user-facing strings in all catalogs, preserve placeholders and technical names, and remember that Windows, .NET, network and upstream error text may still come from outside these catalogs.
- The later UI changes visible in the workspace include `Theme.xaml`, `ThemeService.cs`, `MessageDialog.cs`, `LocalizedValueConverter.cs`, `Core/UiText.cs`, `Core/Turkish.json`, and `tests/UiChecks`. They add shared dark/light/system brushes, immediate theming of secondary windows, themed confirmation dialogs, broader Turkish UI text, and UI rendering checks. System theme listens to Windows preference changes and unregisters its event handler when the window closes.
- `MainWindow.xaml.cs` has an offline constructor path for UI checks. Library text cells use ellipsis and a full-text tooltip. `Core/GameNames.cs` reads local Steam app manifests/library folders, caches names in `%LocalAppData%\OpenSteamToolGUI\game-names.json`, and falls back to Steam Store app details. Recent main-window code limits simultaneous remote name lookups to four and tracks pending/failed IDs until refresh. AppID remains the fallback name when offline. Never interpret a depot ID as a game AppID.
- Library Refresh clears search and filter to show all packages. Empty results distinguish no Lua files from an active search/filter with no matches. `tests/UiChecks --diagnose-library` uses a temporary data root and reads the detected Steam installation to check live library rows without writing to Steam.
- `GameSetupWindow.cs`, `AppIdWindow.cs`, and `TextEditorWindow.cs` are auxiliary dialogs; preserve their theme and language behavior when modifying them.
- The Dashboard installation card has a compact Steam start/restart button. Its label follows the detected Steam process state; restarting asks for confirmation, sends Steam's `-shutdown` request, waits up to 45 seconds, then launches the validated `steam.exe`. It never force-terminates Steam. The state label and button refresh while Dashboard is visible.

## Source map

- `OpenSteamToolGUI.csproj`: WPF `net10.0-windows`, app version 1.1.1, embedded language catalogs and MIT license; excludes test sources from the app build. `App.xaml` merges `Theme.xaml`; `App.xaml.cs` starts either the main window or the scoped `--elevated-apply` helper.
- `MainWindow.xaml` / `.xaml.cs`: page layout, event handlers, user-facing workflow, localization, theme changes, and orchestration of services. Keep potentially blocking network/disk work off the UI thread when extending it.
- `Core/Models.cs`: preferences, Steam installation paths, capability flags, game packages, import plans, file operations, backup records, release metadata.
- `Core/Storage.cs`: preferences, disabled Lua, backups, game-name cache, retention, atomic writes, and incomplete-transaction recovery. Default data root: `%LocalAppData%\OpenSteamToolGUI`; tests inject a temporary root. Original DLL backups used for uninstall must be retained.
- `Core/Services.cs`: Steam discovery, Lua scan/toggle, file transactions, import analysis/apply, official release lookup/download, install/update/uninstall. `Core/ElevatedFileTransaction.cs` handles file-only UAC escalation when needed; keep its allowed targets restricted to OpenSteamTool-related paths inside a validated Steam installation.
- `Core/ConfigService.cs`: TOML form/raw editing and validation. Form save preserves existing comments and unknown keys and rejects an externally changed file. It is a lightweight parser/validator, not a complete TOML implementation.
- `Core/GameNames.cs`: local and remote name resolution. `Core/LanguageService.cs`, `Core/UiText.cs`, `Core/*.json`, `Core/Errors.tsv`, `LocalizedValueConverter.cs`: translation paths. `Theme.xaml`, `ThemeService.cs`, `MessageDialog.cs`: visual system and confirmations.
- `tests/CoreChecks`: temporary fake-Steam file tests. `tests/UiChecks`: offline WPF rendering/interaction checks. Generated `tests/UiChecks/renders/` PNGs are ignored by `.gitignore`.

## Non-negotiable file-operation behavior

- Discover Steam from stored path/registry/process/common locations, or let the user choose a folder. Validate the chosen root by the presence of `steam.exe`. **Never aim development tests at the real Steam installation automatically.**
- Download the official release asset discovered at runtime, by default Release and optionally Debug. Review existing untracked DLLs explicitly; never overwrite them silently. Require Steam to close for DLL install/update/uninstall; do not force-terminate Steam. Track installed hashes and restore originals captured during first install when uninstalling from App Settings. Uninstall affects managed DLLs and leaves user Lua, manifests, and other game data intact. External modification to a tracked DLL blocks unsafe replacement/removal.
- File mutations should use the transaction/backup system. Restore refuses targets whose contents changed after the operation. Recover incomplete transactions where possible. Keep UAC scoped to the file helper, rather than running the GUI as administrator.
- ZIP/Lua/manifest import is **preview then apply**. Enumerate nested ZIP entries, accept only `.lua` and `.manifest`, show destination/conflict/action, keep differing existing files by default, and skip identical content. A user may opt into replacement. Check targets again immediately before apply; reject traversal, symlink/reparse paths, duplicate destinations, oversized entries/archives, and unexpected ZIP contents. Never execute imported Lua during analysis. Current limits in `ImportService`: 128 MiB per file, 512 MiB total, 1,000 importable files.
- A Lua file may affect more than one AppID; show it as one package and toggle it as one package. Disabled scripts live outside watched Lua folders and return to their original path when re-enabled. Do not split arbitrary Lua automatically or promise that one toggle makes an AppID definitively inactive when other scripts may reference it.
- Library removal groups files by a retained import transaction. Selecting any Lua from an imported ZIP removes all files actually written by that ZIP, including manifests and disabled Lua, while restoring any replaced originals; standalone Lua removal deletes the whole selected file. Hash checks block removal after external edits. Active import records are retained beyond ordinary backup retention so the grouping and original backups remain available. Older imports can be grouped only if their backup records still exist; otherwise the UI confirms single Lua removal. `Core/ImportTracking.cs` implements this behavior.
- Multiple Lua versions with the same filename may coexist when an older one is disabled and a newer one is active. Disabling the active copy uses a unique subfolder under the disabled data root; scan includes these subfolders. Import ownership and ZIP removal use the file hash and original target path to distinguish copies. Preserve unrelated copies when removing an import and block ambiguous matching copies.
- Keep the form editor and raw editors available. Preserve TOML comments/unknown keys. Surface on-disk change conflicts before save. Steam/OpenSteamTool applied status remains unknown without an upstream acknowledgment API.

## Build, artifacts, and verification

- Use only the .NET 10 SDK; **do not install Visual Studio or C++ workloads**. `build.ps1` downloads the SDK into `%LocalAppData%\OpenSteamToolGUI\sdk` if needed (about 300 MB, not a tiny compiler) and publishes two win-x64 archives: `dist/OpenSteamToolGUI-portable-win-x64.zip` (self-contained) and `dist/OpenSteamToolGUI-lightweight-win-x64.zip` (needs .NET 10 Desktop Runtime). Each ZIP must contain exactly one `OpenSteamToolGUI.exe`; the portable EXE bundles WPF's native DLLs for extraction at startup. The script fails if publish emits another file. It uses NuGet only for .NET publish dependencies.
- Useful commands from the repository root:

  ```powershell
  $dotnet = Join-Path $env:LOCALAPPDATA 'OpenSteamToolGUI\sdk\dotnet.exe'
  & $dotnet build .\OpenSteamToolGUI.csproj -c Release -v:q
  & $dotnet run --project .\tests\CoreChecks\CoreChecks.csproj -c Release
  & $dotnet run --project .\tests\UiChecks\UiChecks.csproj -c Release
  powershell -ExecutionPolicy Bypass -File .\build.ps1 -SkipSdkInstall
  ```

- Run these build/test/publish commands **sequentially** in this workspace. Parallel WPF builds share `obj\Release\net10.0-windows` and produced a transient missing `MainWindow.g.cs` compiler error in this inspection; sequential reruns succeeded. Do not mistake that race for a source error.
- Verified on 2026-09-23: Release app build succeeded with **0 warnings, 0 errors**; CoreChecks passed; UiChecks passed on a separate sequential run. UiChecks renders seven pages in Dark and Light plus dialogs/dropdown/compact layout and all seven pages for every non-English language. It checks catalog coverage, selected translations, appearance persistence, language switching and retention of an unsaved field. These checks do **not** establish that every DPI/keyboard layout passes, live UAC succeeds, or real Steam/upstream integration works.
- `dist/` contains ZIPs and older output directories. **Do not assume existing archives match current source.** Re-run `build.ps1` before delivering binaries after source edits, then smoke-test both modes where feasible. Do not delete or move existing output directories casually; they may be user-owned work.

## Gaps relative to the approved plan

The current app is a substantial working implementation, not the full original specification. Future work should check these gaps before claiming completion:

- The original plan's Fluent theme differs from the current Dark/Light/System options, as noted above. English is now the first-launch default.
- Built-in navigation, descriptions, confirmations, dialogs and application-defined exceptions have all ten language variants. External Windows/.NET/network error messages are not controlled by these catalogs. High-DPI and keyboard accessibility checks are not comprehensively documented.
- Library has search/filter and package actions, but the requested card/list switch and bulk game operations are not established by the current source. It is code-behind rather than full MVVM.
- Release 1.4.8 capability gating is conservative; verified support detection for later releases is not implemented. The form does not claim `pinApp` or unsupported manifest-size behavior. CloudRedirect companion-account work remains outside this GUI.
- Tests cover fake Steam paths and helper protocol, not a real official-release install, real elevation prompt, live Steam application state, network failure matrix, or both published archives launching on a clean Windows machine. Do not call these verified until tested.

When future work changes these facts, update this file together with `README.md` and `COMPATIBILITY.md` as appropriate. Favor actual source and verification results over assumptions from the original plan.
