# OpenSteamTool GUI 1.2.9

- Fixes two local security issues in the administrator file helper: automatic recovery of unrelated backup records and reads through linked staged inputs.
- Binds elevated requests and staged content to SHA-256 digests, verifies opened paths, rejects reparse points and multiply-linked inputs, and holds checked directories against renaming during the operation.
- Keeps incomplete-transaction recovery in normal application startup. The elevated helper performs only the scoped requested transaction, with rollback on failure.

Release builds, CoreChecks, UiChecks, and both ZIP builds are checked locally. Security regression checks use temporary fake Steam folders; live UAC and real Steam integration require separate verification.

Both Windows x64 ZIPs contain one `OpenSteamToolGUI.exe`. The portable build includes .NET 10; the lightweight build requires .NET 10 Desktop Runtime.
