# OpenSteamTool GUI 1.2.8

- Restores automated Windows builds and Release publication in the new `maliv2/OpenSteamToolGUI` repository after enabling GitHub Actions.
- Allows a manual Build and release workflow run on `main` to publish a version missed while Actions was disabled. Manual runs on other branches do not publish Releases.
- Uses version-specific release notes when available.

Both Windows x64 ZIPs contain one `OpenSteamToolGUI.exe`. The portable build includes .NET 10; the lightweight build requires .NET 10 Desktop Runtime.
