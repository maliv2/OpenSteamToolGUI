# OpenSteamTool GUI 1.2.7

- Discovers DLC AppIDs from Steam Store and stages verified base-game and DLC downloads in one import preview.
- Shows each online import as one Library package, with grouped Lua enable/disable and grouped removal of tracked Lua and manifests while restoring replaced originals.
- Requires all package files and resolved replacement conflicts before importing; identical existing files are backed up and adopted into the package.
- Clarifies Windows support, features, downloads, and common questions in the repository README.
- Adds a Turkish project overview and a reusable repository social preview image.
- Points README links and future application self-update checks to `maliv2/OpenSteamToolGUI`.

Existing binaries continue using their embedded update address until replaced with a build from this version. Earlier imports without bundle metadata keep their per-Lua Library rows. Local fake-Steam checks do not prove live Steam DLC entitlement or activation. A published release must be verified separately from local builds.
