# OpenSteamTool GUI — Windows desktop manager

**OpenSteamTool GUI (OpenSteamToolGUI)** is a free, open-source Windows 10/11 x64 desktop application for managing official [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) releases. Built with C# and .NET 10 WPF, it provides a graphical interface for installation and updates, Steam Lua and depot manifest imports, backups, and settings.

[Windows downloads](https://github.com/maliv2/OpenSteamToolGUI/releases) · [Getting started](#use) · [Compatibility](COMPATIBILITY.md) · [Report an issue](https://github.com/maliv2/OpenSteamToolGUI/issues) · [Türkçe](docs/README.tr.md)

This GUI is an independent project; it manages upstream releases rather than building the upstream C++ project.

## What it does

- Installs, updates, disables, and uninstalls the official OpenSteamTool release.
- Searches for games and previews Lua or manifest imports before changing Steam files.
- Keeps backups of managed changes and provides settings and diagnostics in one Windows app.
- Offers English, Turkish, German, French, Spanish, Brazilian Portuguese, Russian, Simplified Chinese, Japanese, and Korean, with Dark, Light, and System appearance.

## Download

Current source version: **1.2.9**. See the [1.2.9 release notes](docs/release-notes-1.2.9.md) for the elevated helper security fixes.

Get published Windows builds from [OpenSteamTool GUI Releases](https://github.com/maliv2/OpenSteamToolGUI/releases). If no release is listed, use the [source build instructions](#build). Both build formats contain one `OpenSteamToolGUI.exe` per ZIP:

- **Portable:** includes .NET 10.
- **Lightweight:** requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Extract a ZIP and run the EXE. The interface starts in English; App Settings offers nine other languages and Dark, Light, or System appearance.
App updates keep your chosen build type: lightweight installations download the lightweight ZIP, and portable installations download the portable ZIP. The lightweight build still requires the .NET 10 Desktop Runtime after an update.

## Screenshots

Dashboard in English (offline preview):

![OpenSteamTool GUI Windows dashboard with installation status, Steam controls, and backup information](docs/screenshots/dashboard.png)

Game finder (offline preview):

![OpenSteamTool GUI game finder showing Steam game search and download source statuses](docs/screenshots/game-finder.png)

## Use

1. Select the Steam folder containing `steam.exe` if it is not detected automatically.
2. Use the Dashboard or App Settings to install, update, repair, disable, enable, or uninstall OpenSteamTool. If Steam is running, the app asks once before closing it, performs the file change, and restarts Steam afterward. It first requests Steam's normal shutdown; if Steam remains open after 3 seconds, it force closes the verified Steam process. Steam stays closed if it was closed before the operation. Running games may be interrupted.
   If Windows denies replacement of a managed file after the initial folder access check, the app verifies that the original files are intact and requests administrator approval for a scoped file operation. Canceling that prompt leaves the original files in place.
   The administrator helper does not run automatic backup recovery. Recovery still runs during normal application startup. The helper verifies that its request and file contents match what the app staged, rejects linked inputs, and checks the actual paths of opened files. Checked directories are held against renaming and input files against modification during reads; a sharing conflict stops the operation safely.
   Disabling removes the three managed DLLs from the Steam folder and keeps them in backups for re-enabling. Any DLLs that existed before the first managed install remain backed up for uninstall; restoring those files during disable could leave an older OpenSteamTool copy active. Disabling does not remove Lua, manifests, or the managed installation. Enable OpenSteamTool again before updating or uninstalling it.
3. Review file destinations and conflicts before importing a ZIP, Lua file, or manifest. Existing files are kept unless you choose to replace them.

Use **Game Search** in the left menu to search by game name or AppID. Results show a small Steam cover image when available, followed by the AppID and game name. The image tries standard Steam image addresses first, then the image URL supplied by Steam's store data when needed; it may still be blank when Steam provides no usable artwork. The Library's **Find Games Online** button opens the same page. On app launch, the page checks Steam Store, SteamManifest.com, and Remlua automatically, then refreshes their status every 15 seconds without delaying searches. Each source check times out after 8 seconds, so the labels can recover when an internet connection returns. Their side-by-side status labels are yellow before the first result, green when reachable, and red when unavailable. Select a game and choose **Add to Library** to find its DLC AppIDs from Steam Store and fetch available Lua and manifest files for the main game and every listed DLC from Remlua or SteamManifest.com. All downloaded files start selected in one import preview. The preview reports how many DLCs could not be downloaded; hover over the summary to see their AppIDs. Review the targets and conflicts, then confirm before the app writes files. Steam Store provides search and DLC metadata only; the app does not invent depot IDs or manifest files. Availability depends on the selected game and third-party servers.

An online game import appears as one package in Library, with the main game and downloaded DLC AppIDs listed together. Enabling or disabling that package changes all of its Lua files, and removing it removes all files written by that import, including manifests; replaced originals are restored. The package cannot be imported partially. Existing identical files are backed up and included in the package. For differing existing files, select the conflicting rows and choose **Replace Selected** before importing, or leave the preview without writing anything. A package operation stops if a managed file changed outside the app.

The app can back up and restore managed file changes. Saving a file does not confirm that Steam or OpenSteamTool applied it. For version-specific behavior, see [COMPATIBILITY.md](COMPATIBILITY.md).

## Frequently asked questions

### Is OpenSteamTool GUI the official OpenSteamTool project?

No. This is an independent graphical manager for the official upstream releases. Upstream code and release notes are available in [OpenSteam001/OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool).

### Which Windows versions are supported?

The GUI targets Windows 10 and Windows 11 on x64. It is a WPF desktop application; Linux and macOS builds are not provided.

### Does the portable build need .NET installed?

The portable ZIP bundles .NET 10. The smaller lightweight ZIP needs the .NET 10 Desktop Runtime. Both contain a single executable.

### Can I review Lua and manifest changes before importing?

Yes. ZIP, Lua, and manifest imports show a preview of destinations and conflicts before confirmation. Existing differing files are kept unless you choose replacement; managed changes use transactions and backups.

## Support and contributions

Use [GitHub Issues](https://github.com/maliv2/OpenSteamToolGUI/issues) for bug reports and feature requests. Include the GUI version, Windows version, build type, steps to reproduce, and relevant diagnostics, with personal information removed. See [COMPATIBILITY.md](COMPATIBILITY.md) before reporting upstream behavior. Contributions should follow the build and verification workflow in [AGENTS.md](AGENTS.md).

## Build

Clone the current repository:

```powershell
git clone https://github.com/maliv2/OpenSteamToolGUI.git
cd OpenSteamToolGUI
```

Run `powershell -ExecutionPolicy Bypass -File .\build.ps1`. The script uses the .NET 10 SDK and creates portable and lightweight ZIPs in `dist/`; each contains one EXE.

For maintainers: increase `<Version>` in `OpenSteamToolGUI.csproj` before pushing to `main`. A successful push publishes both ZIPs in a GitHub Release with the matching `v` tag. Pushing an already released version fails the release step.

GitHub Actions must be enabled on the repository's **Actions** page after copying or migrating the repository. If a push happened while Actions was disabled, open **Build and release → Run workflow**, select `main`, and run it to build and publish that version. Manual runs on other branches build and test without publishing a Release. Version-specific notes in `docs/release-notes-<version>.md` are used when present.

## License

The GUI is licensed under [MIT](LICENSE). OpenSteamTool and other third-party components retain their own licenses.

## Disclaimer

This is an unofficial project, not affiliated with or endorsed by Valve or the OpenSteamTool maintainers. Use it only with software, accounts, and files you are authorized to manage, and follow applicable laws, platform terms, and third-party licenses. The app changes files in a Steam installation; keep independent backups. It is provided "as is" without warranty or liability to the extent permitted by law. Saving a file does not prove that Steam applied it.
