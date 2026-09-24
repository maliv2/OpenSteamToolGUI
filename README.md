# OpenSteamTool GUI

An unofficial Windows 10/11 x64 desktop manager for the official [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) releases. It installs and updates upstream DLLs, manages Lua and manifest files, and keeps backups of changes it makes.

## What it does

- Installs, updates, disables, and uninstalls the official OpenSteamTool release.
- Searches for games and previews Lua or manifest imports before changing Steam files.
- Keeps backups of managed changes and provides settings and diagnostics in one Windows app.

## Download

Get the [latest release](https://github.com/muhammetaliaydin/OpenSteamToolGUI/releases/latest). Both ZIPs contain one `OpenSteamToolGUI.exe`:

- **Portable:** includes .NET 10.
- **Lightweight:** requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Extract a ZIP and run the EXE. The interface starts in English; App Settings offers nine other languages and Dark, Light, or System appearance.
App updates keep your chosen build type: lightweight installations download the lightweight ZIP, and portable installations download the portable ZIP. The lightweight build still requires the .NET 10 Desktop Runtime after an update.

## Screenshots

Dashboard in English (offline preview):

![OpenSteamTool GUI dashboard](docs/screenshots/dashboard.png)

## Use

1. Select the Steam folder containing `steam.exe` if it is not detected automatically.
2. Use the Dashboard or App Settings to install, update, repair, disable, enable, or uninstall OpenSteamTool. If Steam is running, the app asks once before closing it, performs the file change, and restarts Steam afterward. It first requests Steam's normal shutdown; if Steam remains open after 3 seconds, it force closes the verified Steam process. Steam stays closed if it was closed before the operation. Running games may be interrupted.
   If Windows denies replacement of a managed file after the initial folder access check, the app verifies that the original files are intact and requests administrator approval for a scoped file operation. Canceling that prompt leaves the original files in place.
   Disabling removes the three managed DLLs from the Steam folder and keeps them in backups for re-enabling. Any DLLs that existed before the first managed install remain backed up for uninstall; restoring those files during disable could leave an older OpenSteamTool copy active. Disabling does not remove Lua, manifests, or the managed installation. Enable OpenSteamTool again before updating or uninstalling it.
3. Review file destinations and conflicts before importing a ZIP, Lua file, or manifest. Existing files are kept unless you choose to replace them.

Use **Game Search** in the left menu to search by game name or AppID. Results show a small Steam cover image when available, followed by the AppID and game name. The image tries standard Steam image addresses first, then the image URL supplied by Steam's store data when needed; it may still be blank when Steam provides no usable artwork. The Library's **Find Games Online** button opens the same page. On app launch, the page checks Steam Store, SteamManifest.com, and Remlua automatically, then refreshes their status every 15 seconds without delaying searches. Each source check times out after 8 seconds, so the labels can recover when an internet connection returns. Their side-by-side status labels are yellow before the first result, green when reachable, and red when unavailable. Select a game and choose **Add to Library** to fetch available Lua and manifest files from Remlua or SteamManifest.com. Review the import preview and confirm before the app writes files. Steam Store provides search data only; the app does not invent depot IDs or manifest files. Availability depends on the selected game and third-party servers.

The app can back up and restore managed file changes. Saving a file does not confirm that Steam or OpenSteamTool applied it. For version-specific behavior, see [COMPATIBILITY.md](COMPATIBILITY.md).

## Build

Run `powershell -ExecutionPolicy Bypass -File .\build.ps1`. The script uses the .NET 10 SDK and creates portable and lightweight ZIPs in `dist/`; each contains one EXE.

For maintainers: increase `<Version>` in `OpenSteamToolGUI.csproj` before pushing to `main`. A successful push publishes both ZIPs in a GitHub Release with the matching `v` tag. Pushing an already released version fails the release step.

## License

The GUI is licensed under [MIT](LICENSE). OpenSteamTool and other third-party components retain their own licenses.

## Disclaimer

This is an unofficial project, not affiliated with or endorsed by Valve or the OpenSteamTool maintainers. Use it only with software, accounts, and files you are authorized to manage, and follow applicable laws, platform terms, and third-party licenses. The app changes files in a Steam installation; keep independent backups. It is provided "as is" without warranty or liability to the extent permitted by law. Saving a file does not prove that Steam applied it.
