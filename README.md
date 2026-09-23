# OpenSteamTool GUI

Windows desktop manager for the official [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) release. The interface starts in Turkish. Choose a language and Dark, Light or System appearance in App Settings. Theme changes apply immediately to every application window and are saved.

The app checks its own [GitHub releases](https://github.com/muhammetaliaydin/OpenSteamToolGUI/releases) on launch when update checks are enabled. It offers to download and restart when a newer release exists. App Settings also has manual check and install buttons. Updates use the portable Windows x64 ZIP, verify its GitHub SHA-256 digest, and replace app files after the app exits. Put the app in a writable folder to use in-place updates; protected folders may require a manual installation. This is separate from the Dashboard's OpenSteamTool DLL update.

Every push to `main` runs the Windows build and checks through GitHub Actions. A `v*` tag builds both distribution ZIPs and publishes a GitHub Release. `v1.0` is the first app release.

## Build and run

Run `powershell -ExecutionPolicy Bypass -File .\build.ps1` from this folder. The script installs only the .NET 10 SDK into your user profile if needed (roughly 300 MB), then creates `dist/OpenSteamToolGUI-portable-win-x64.zip` and `dist/OpenSteamToolGUI-lightweight-win-x64.zip`. The portable build includes .NET; the lightweight build needs the .NET 10 Desktop Runtime. Visual Studio and the C++ build toolchain are not required.

Extract either ZIP and run `OpenSteamToolGUI.exe`. Select the folder containing `steam.exe` if it is not detected automatically.

## Use

* Dashboard: start Steam from the selected installation, or request a graceful restart when it is running. A restart asks for confirmation, waits up to 45 seconds for Steam to close, then launches it again; it does not force-close Steam. You can also check the current upstream release and install or repair the Release or Debug ZIP. Close Steam before changing DLLs. Existing untracked DLLs require review and are backed up when replaced. If Steam is in a protected folder, Windows asks for elevation for the file operation while the main GUI stays at standard privilege.
* Library: inspect Lua packages in `<Steam>/config/lua`, create a new game script through a form, disable or enable a package, edit its source, or remove a selected package. Refresh clears search and filter to show the full library; an empty result states whether the folder has no Lua or the current search/filter has no matches. If another disabled Lua has the same filename, disabling creates a separate copy so neither file is overwritten; enabling still requires the active destination to be free. Removing an imported ZIP package removes every file actually written by that import, including manifests and its disabled Lua, and restores any file that the import replaced. An unrelated disabled Lua with the same filename is preserved. Removing a standalone Lua package removes that whole file. The confirmation states the scope, changed files block removal, and the operation is backed up. Older imports can be grouped only while their import backup record is available; without one, the confirmation offers removal of the selected Lua file only. A Lua file may affect several apps. The app never executes Lua while inspecting it.
* Import: drop a ZIP, Lua file or `.manifest` file on the window. Review each file, select which ones to add, and check its target before importing. Conflicting files stay untouched unless you select them and choose **Replace Selected**. Unknown manifest files can be assigned an AppID for the preview. Lua files go to `<Steam>/config/lua`; depot manifests go to Steam's `<Steam>/depotcache`.
* OpenSteamTool Settings: edit supported TOML settings or open the raw editor. Existing comments and unknown keys are preserved in the form editor. The 1.4.8 release does not support the current main branch's stats API or CloudRedirect settings.
* Backups: review file operations and restore a selected operation. A restore stops if the target has changed since that operation.
* App Settings: change language or appearance, choose Steam folder, and uninstall managed DLLs. Uninstall restores any original DLLs that were backed up on first install, and leaves Lua scripts, manifests and other game data in place.

The application saves its preferences, inactive Lua packages and backup history under `%LocalAppData%/OpenSteamToolGUI`. OpenSteamTool detects watched Lua/config changes, but this GUI can confirm only that files were saved, not that Steam applied them.

## Compatibility

See [COMPATIBILITY.md](COMPATIBILITY.md) for the researched upstream behavior and version differences. OpenSteamTool itself is distributed by its maintainers; this app downloads the official release asset at install time. Follow Steam's and the upstream project's terms when using it.
