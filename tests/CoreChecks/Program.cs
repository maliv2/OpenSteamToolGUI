using OpenSteamToolGUI.Core;
using System.IO.Compression;
using System.Text;

if (args.Contains("--finder-live"))
{
    using var finder = new GameFinderService();
    var servers = await finder.CheckServersAsync();
    foreach (var server in servers) Console.WriteLine($"{server.Name}: {(server.Online ? "online" : "offline")}");
    var games = await finder.SearchAsync("Counter-Strike");
    Check(games.Any(game => game.AppId == 730), "Finder search did not return AppID 730");
    var idGame = await finder.SearchAsync("730");
    Check(idGame.Count == 1 && idGame[0].AppId == 730, "Finder AppID search failed");
    string liveRoot = Path.Combine(Path.GetTempPath(), "ost-finder-check-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(liveRoot);
    try
    {
        var downloads = new[] { await finder.DownloadRemluaAsync(730), await finder.DownloadSteamManifestAsync(730) };
        foreach (var (download, index) in downloads.Select((item, index) => (item, index)))
        {
            var fakeSteam = new SteamInstallation(Path.Combine(liveRoot, "steam-" + index));
            Directory.CreateDirectory(fakeSteam.Root);
            File.WriteAllText(Path.Combine(fakeSteam.Root, "steam.exe"), "fake");
            string path = Path.Combine(liveRoot, download.FileName);
            File.WriteAllBytes(path, download.Content);
            var storage = new Storage(Path.Combine(liveRoot, "data-" + index));
            var importer = new ImportService(storage);
            var plan = importer.AnalyzePath(path, fakeSteam);
            GameFinderService.MatchDownloadedGame(plan, 730);
            Check(plan.Files.Any(file => file.Kind == "Lua" && file.AppId == 730), download.Source + " did not provide a matching Lua file");
            importer.Apply(plan, fakeSteam);
            Check(File.Exists(Path.Combine(fakeSteam.LuaDirectory, "730.lua")), download.Source + " did not add the game to the fake library");
            var libraryIds = LuaAnalyzer.Scan(fakeSteam, storage).Single().AppIds;
            Check(libraryIds.Contains(730u), download.Source + " did not list the selected game");
            if (download.Source == "Remlua") Check(libraryIds.SequenceEqual([730u]), "Remlua depot IDs were listed as games");
            Console.WriteLine($"{download.Source}: {plan.Files.Count} importable files");
        }
    }
    finally
    {
        string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(liveRoot).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) Directory.Delete(liveRoot, true);
    }
    return;
}

string root = Path.Combine(Path.GetTempPath(), "ostgui-check-" + Guid.NewGuid().ToString("N"));
var lifecycleSteps = new List<string>();
bool steamRunning = true;
var lifecycle = new SteamLifecycle(() => steamRunning,
    () => { lifecycleSteps.Add("stop"); steamRunning = false; return Task.CompletedTask; },
    () => { lifecycleSteps.Add("start"); steamRunning = true; });
await lifecycle.RunAsync(true, () => { Check(!steamRunning, "Steam lifecycle did not close Steam before work"); lifecycleSteps.Add("work"); return Task.CompletedTask; });
Check(lifecycleSteps.SequenceEqual(["stop", "work", "start"]) && steamRunning, "Steam lifecycle did not restore the running state");
lifecycleSteps.Clear(); steamRunning = false;
await lifecycle.RunAsync(false, () => { lifecycleSteps.Add("work"); return Task.CompletedTask; });
Check(lifecycleSteps.SequenceEqual(["work"]) && !steamRunning, "Steam lifecycle started a previously closed Steam session");
steamRunning = true; lifecycleSteps.Clear();
try { await lifecycle.RunAsync(false, () => { lifecycleSteps.Add("work"); return Task.CompletedTask; }); throw new Exception("Steam lifecycle accepted a newly started Steam session"); }
catch (InvalidOperationException) { Check(lifecycleSteps.Count == 0, "Steam lifecycle changed files after Steam started"); }
try { await lifecycle.RunAsync(true, () => throw new IOException("Test operation failure")); throw new Exception("Steam lifecycle hid the operation failure"); }
catch (IOException) { Check(steamRunning, "Steam lifecycle did not restart Steam after an operation failure"); }
bool stopFailed = false;
var failedStop = new SteamLifecycle(() => true,
    () => { stopFailed = true; throw new IOException("Test stop failure"); },
    () => throw new Exception("Steam was restarted after a failed stop"));
try { await failedStop.RunAsync(true, () => throw new Exception("Files changed after a failed stop")); throw new Exception("Steam lifecycle hid the stop failure"); }
catch (IOException) { Check(stopFailed, "Steam lifecycle did not attempt shutdown"); }
Check(AppUpdateService.SelectAssetName("lightweight", 100_000_000) == "OpenSteamToolGUI-lightweight-win-x64.zip", "Lightweight update asset selection");
Check(AppUpdateService.SelectAssetName("portable", 1_000_000) == "OpenSteamToolGUI-portable-win-x64.zip", "Portable update asset selection");
Check(AppUpdateService.SelectAssetName(null, 1_000_000) == "OpenSteamToolGUI-lightweight-win-x64.zip", "Legacy lightweight update asset selection");
Check(AppUpdateService.SelectAssetName(null, 100_000_000) == "OpenSteamToolGUI-portable-win-x64.zip", "Legacy portable update asset selection");
using (var artworkJson = System.Text.Json.JsonDocument.Parse("""
    {"3280350":{"success":true,"data":{"header_image":"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3280350/hash/header.jpg?t=1","capsule_image":"https://example.com/store_item_assets/steam/apps/3280350/hash/capsule.jpg"}}}
    """))
{
    var artworkUrls = SteamArtwork.StoreAssetUrls(3280350, artworkJson.RootElement);
    Check(artworkUrls.Count == 1 && artworkUrls[0].AbsolutePath.EndsWith("/hash/header.jpg"), "Steam artwork URL validation");
    Check(SteamArtwork.StoreAssetUrls(730, artworkJson.RootElement).Count == 0, "Steam artwork AppID isolation");
}
Directory.CreateDirectory(root);
try
{
    var storage = new Storage(Path.Combine(root, "appdata"));
    var steam = new SteamInstallation(Path.Combine(root, "steam"));
    Directory.CreateDirectory(steam.Root); File.WriteAllText(Path.Combine(steam.Root, "steam.exe"), "fake");
    var multiDepotLua = new ImportPlan { Files = [new ImportFile { Kind = "Lua", ArchivePath = "730.lua", Content = Encoding.UTF8.GetBytes("addappid(730)\naddappid(731, 1, \"key\")") }] };
    GameFinderService.MatchDownloadedGame(multiDepotLua, 730);
    Check(multiDepotLua.Files[0].AppId == 730, "Finder did not identify the game in a multi-depot Lua file");
    try { GameFinderService.MatchDownloadedGame(multiDepotLua, 999); throw new Exception("Finder accepted a different AppID"); }
    catch (InvalidDataException) { }
    var mismatchedLua = new ImportPlan { Files = [new ImportFile { Kind = "Lua", ArchivePath = "730.lua", AppId = 730, Content = Encoding.UTF8.GetBytes("addappid(731)") }] };
    try { GameFinderService.MatchDownloadedGame(mismatchedLua, 730); throw new Exception("Finder accepted Lua content for a different AppID"); }
    catch (InvalidDataException) { }
    var ids = LuaAnalyzer.AppIds("-- addappid(111)\nAddAppId(222)\naddappid(333, 0, \"key\")");
    Check(ids.SequenceEqual([222u, 333u]), "Lua scan: " + string.Join(",", ids));
    Check(LuaAnalyzer.AppIds("addappid(730)\naddappid(731, 1, \"key\")\nsetManifestid(731, \"123\")").SequenceEqual([730u]), "Depot ID was treated as a game AppID");
    var config = new ConfigService(); File.WriteAllText(steam.ConfigFile, "# custom\n[manifest]\nurl = \"wudrm\" # note\n[extra]\nfoo = 1\n");
    var model = config.Load(steam.ConfigFile); model.ManifestProvider = "steamrun";
    config.Save(model, ToolCapabilities.ForVersion("1.4.8"), new FileTransaction(storage));
    string saved = File.ReadAllText(steam.ConfigFile);
    Check(saved.Contains("# custom") && saved.Contains("[extra]") && saved.Contains("foo = 1") && saved.Contains("# note"), "TOML preservation");
    string zipPath = Path.Combine(root, "games.zip");
    using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
        using var w = new StreamWriter(zip.CreateEntry("folder/222.lua").Open()); w.Write("addappid(222)");
    }
    var importer = new ImportService(storage);
    var plan = importer.Analyze(zipPath, steam);
    Check(plan.Files.Count == 1 && plan.Files[0].AppId == 222 && plan.Files[0].Action == ImportAction.Add, "Import preview");
    importer.Apply(plan, steam);
    Check(File.Exists(Path.Combine(steam.LuaDirectory, "222.lua")), "Import apply");
    var again = importer.Analyze(zipPath, steam);
    Check(again.Files[0].Action == ImportAction.SkipIdentical, "Identical skip");
    File.Delete(Path.Combine(steam.LuaDirectory, "222.lua"));
    var excluded = importer.Analyze(zipPath, steam); excluded.Files[0].Include = false;
    importer.Apply(excluded, steam);
    Check(!File.Exists(Path.Combine(steam.LuaDirectory, "222.lua")), "Unselected game is not imported");
    importer.Apply(importer.Analyze(zipPath, steam), steam);
    var pkg = LuaAnalyzer.Scan(steam, storage).Single(); LuaAnalyzer.Toggle(pkg, steam, storage);
    Check(LuaAnalyzer.Scan(steam, storage).Single().Enabled == false, "Disable Lua");
    var disableRecord = storage.ListBackups().First(x => x.Description.StartsWith("Disable Lua"));
    new FileTransaction(storage).Restore(disableRecord);
    Check(LuaAnalyzer.Scan(steam, storage).Count == 1 && LuaAnalyzer.Scan(steam, storage).Single().Enabled, "Restore disabled Lua");
    LuaAnalyzer.Toggle(LuaAnalyzer.Scan(steam, storage).Single(), steam, storage);
    LuaAnalyzer.Toggle(LuaAnalyzer.Scan(steam, storage).Single(), steam, storage);
    Check(File.Exists(Path.Combine(steam.LuaDirectory, "222.lua")), "Re-enable Lua");
    string bundle = Path.Combine(root, "bundle.zip");
    Directory.CreateDirectory(steam.DepotCache);
    string manifest = Path.Combine(steam.DepotCache, "444.manifest");
    File.WriteAllText(manifest, "original manifest");
    using (var zip = ZipFile.Open(bundle, ZipArchiveMode.Create))
    {
        foreach (var (name, fileText) in new[] { ("444.lua", "addappid(444)"), ("555.lua", "addappid(555)"), ("444.manifest", "new manifest") })
            using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(fileText);
    }
    var bundlePlan = importer.Analyze(bundle, steam);
    bundlePlan.Files.Single(x => x.Kind == "Manifest").Action = ImportAction.Replace;
    var bundleRecord = importer.Apply(bundlePlan, steam);
    Check(ImportTracking.ActiveGroups(storage, steam).Any(x => x.Id == bundleRecord.Id), "ZIP group tracking");
    bundleRecord.ImportSourceName = null; storage.SaveRecord(bundleRecord);
    Check(ImportTracking.ActiveGroups(storage, steam).Any(x => x.Id == bundleRecord.Id), "Retained older ZIP record tracking");
    storage.PruneBackups(1, new AppPreferences());
    Check(storage.ListBackups().Any(x => x.Id == bundleRecord.Id), "Active import backup retention");
    var bundledLua = LuaAnalyzer.Scan(steam, storage).Single(x => x.AppIds.Contains(444));
    LuaAnalyzer.Toggle(bundledLua, steam, storage);
    ImportTracking.UpdateManagedHash(storage, steam, Path.Combine(steam.LuaDirectory, "555.lua"), FileTools.HashFile(Path.Combine(steam.LuaDirectory, "555.lua")), FileTools.Hash(Encoding.UTF8.GetBytes("edited")));
    File.WriteAllText(Path.Combine(steam.LuaDirectory, "555.lua"), "edited");
    bundleRecord = storage.ListBackups().Single(x => x.Id == bundleRecord.Id);
    var removal = ImportTracking.RemoveGroup(storage, steam, bundleRecord);
    Check(!File.Exists(Path.Combine(steam.LuaDirectory, "444.lua")) && !File.Exists(Path.Combine(storage.DisabledDirectory, "444.lua")) && !File.Exists(Path.Combine(steam.LuaDirectory, "555.lua")), "ZIP Lua removal");
    Check(File.ReadAllText(manifest) == "original manifest" && bundleRecord.ImportRemoved && removal.RemovedImportId == bundleRecord.Id, "ZIP original restoration");
    new FileTransaction(storage).Restore(removal);
    if (removal.CompanionRemovalId is not null)
        new FileTransaction(storage).Restore(storage.ListBackups().Single(x => x.Id == removal.CompanionRemovalId));
    bundleRecord.ImportRemoved = false; storage.SaveRecord(bundleRecord);
    Check(File.Exists(Path.Combine(storage.DisabledDirectory, "444.lua")) && File.ReadAllText(manifest) == "new manifest", "ZIP removal backup restore");
    File.WriteAllText(manifest, "external change");
    try { ImportTracking.RemoveGroup(storage, steam, bundleRecord); throw new Exception("Changed ZIP file removed"); } catch (IOException) { }
    Check(File.Exists(Path.Combine(storage.DisabledDirectory, "444.lua")), "Changed ZIP blocks entire removal");
    string singleLua = Path.Combine(steam.LuaDirectory, "777.lua");
    File.WriteAllText(singleLua, "addappid(777)");
    var singlePackage = LuaAnalyzer.Scan(steam, storage).Single(x => x.AppIds.Contains(777));
    var singleRemoval = ImportTracking.RemoveLua(storage, steam, singlePackage, FileTools.HashFile(singleLua));
    Check(!File.Exists(singleLua), "Standalone Lua removal");
    new FileTransaction(storage).Restore(singleRemoval);
    Check(File.Exists(singleLua), "Standalone Lua restoration");
    string oldLuaSource = Path.Combine(root, "413150.lua");
    File.WriteAllText(oldLuaSource, "addappid(413150) -- old copy");
    var oldLuaRecord = importer.Apply(importer.AnalyzePath(oldLuaSource, steam), steam);
    LuaAnalyzer.Toggle(LuaAnalyzer.Scan(steam, storage).Single(x => x.AppIds.Contains(413150)), steam, storage);
    string oldDisabled = Path.Combine(storage.DisabledDirectory, "413150.lua");
    string conflictingZip = Path.Combine(root, "413150.zip");
    using (var zip = ZipFile.Open(conflictingZip, ZipArchiveMode.Create))
    {
        using var writer = new StreamWriter(zip.CreateEntry("413150.lua").Open());
        writer.Write("addappid(413150) -- new ZIP copy");
    }
    var firstZip = importer.Apply(importer.Analyze(conflictingZip, steam), steam);
    ImportTracking.RemoveGroup(storage, steam, firstZip);
    Check(!File.Exists(Path.Combine(steam.LuaDirectory, "413150.lua")) && File.Exists(oldDisabled), "ZIP removal preserves unrelated disabled Lua");
    var secondZip = importer.Apply(importer.Analyze(conflictingZip, steam), steam);
    LuaAnalyzer.Toggle(LuaAnalyzer.Scan(steam, storage).Single(x => x.Enabled && x.AppIds.Contains(413150)), steam, storage);
    var disabledCopies = Directory.GetFiles(storage.DisabledDirectory, "413150.lua", SearchOption.AllDirectories);
    Check(disabledCopies.Length == 2 && disabledCopies.Any(x => !x.Equals(oldDisabled, StringComparison.OrdinalIgnoreCase)), "Conflicting Lua disables into separate folder");
    var sameNamePackages = LuaAnalyzer.Scan(steam, storage).Where(x => x.AppIds.Contains(413150)).ToList();
    Check(ImportTracking.ForPackage(storage, steam, sameNamePackages.Single(x => x.SourcePath.Equals(oldDisabled, StringComparison.OrdinalIgnoreCase)))?.Id == oldLuaRecord.Id, "Old disabled Lua keeps its import group");
    Check(ImportTracking.ForPackage(storage, steam, sameNamePackages.Single(x => !x.SourcePath.Equals(oldDisabled, StringComparison.OrdinalIgnoreCase)))?.Id == secondZip.Id, "New disabled Lua keeps ZIP group");
    ImportTracking.RemoveGroup(storage, steam, secondZip);
    Check(Directory.GetFiles(storage.DisabledDirectory, "413150.lua", SearchOption.AllDirectories).Single().Equals(oldDisabled, StringComparison.OrdinalIgnoreCase), "ZIP removal selects matching disabled Lua");
    string unsafeZip = Path.Combine(root, "unsafe.zip");
    using (var zip = ZipFile.Open(unsafeZip, ZipArchiveMode.Create)) { using var w = new StreamWriter(zip.CreateEntry("../escape.lua").Open()); w.Write("x"); }
    try { importer.Analyze(unsafeZip, steam); throw new Exception("Unsafe ZIP accepted"); } catch (InvalidDataException) { }
    string dllZip = Path.Combine(root, "release.zip");
    using (var zip = ZipFile.Open(dllZip, ZipArchiveMode.Create))
        foreach (var name in new[] { "dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll" })
            using (var s = zip.CreateEntry(name).Open()) s.Write(new byte[128]);
    string protectedDll = Path.Combine(steam.Root, "dwmapi.dll");
    File.WriteAllText(protectedDll, "original");
    File.SetAttributes(protectedDll, FileAttributes.ReadOnly);
    try
    {
        Check(!ElevatedFileTransaction.NeedsElevation([protectedDll]), "Directory probe missed the protected DLL scenario");
        try
        {
            new FileTransaction(storage).Apply("Protected DLL test", [(protectedDll, (byte[]?)Encoding.UTF8.GetBytes("replacement"))], steamRoot: steam.Root);
            throw new Exception("Protected DLL was replaced without elevation");
        }
        catch (IOException ex) when (ex.Message.Contains("Elevation requires the published OpenSteamToolGUI.exe.")) { }
        Check(File.ReadAllText(protectedDll) == "original", "Elevation retry changed the protected DLL before approval");
    }
    finally { File.SetAttributes(protectedDll, FileAttributes.Normal); }
    File.WriteAllText(Path.Combine(steam.Root, "dwmapi.dll"), "original");
    var prefs = new AppPreferences();
    new Installer(storage, prefs, () => false).Install(dllZip, new ReleaseInfo { Version = "test", Channel = "Release" }, steam, true);
    Check(prefs.OwnedFiles.Count == 3 && File.ReadAllBytes(Path.Combine(steam.Root, "dwmapi.dll")).Length == 128, "Install");
    var installer = new Installer(storage, prefs, () => false);
    installer.SetEnabled(steam, false);
    Check(installer.IsDisabled && File.ReadAllText(Path.Combine(steam.Root, "dwmapi.dll")) == "original" &&
        !File.Exists(Path.Combine(steam.Root, "OpenSteamTool.dll")) && prefs.InstalledVersion == "test", "Disable preserves installation and originals");
    storage.PruneBackups(1, prefs);
    Check(prefs.OwnedFiles.All(x => x.DisabledBackup is not null && File.Exists(x.DisabledBackup)), "Disabled DLL backups survive pruning");
    File.WriteAllText(Path.Combine(steam.Root, "dwmapi.dll"), "external change");
    try { installer.SetEnabled(steam, true); throw new Exception("Modified original was overwritten"); } catch (IOException) { }
    File.WriteAllText(Path.Combine(steam.Root, "dwmapi.dll"), "original");
    installer.SetEnabled(steam, true);
    Check(!installer.IsDisabled && prefs.OwnedFiles.All(x => x.DisabledBackup is null) &&
        File.ReadAllBytes(Path.Combine(steam.Root, "OpenSteamTool.dll")).Length == 128, "Re-enable managed DLLs");
    try { new Installer(storage, prefs, () => true).SetEnabled(steam, false); throw new Exception("Toggle ran while Steam was open"); } catch (IOException) { }
    string updateZip = Path.Combine(root, "update.zip");
    using (var zip = ZipFile.Open(updateZip, ZipArchiveMode.Create))
        foreach (var name in new[] { "dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll" })
            using (var s = zip.CreateEntry(name).Open()) s.Write(new byte[256]);
    new Installer(storage, prefs, () => false).Install(updateZip, new ReleaseInfo { Version = "test2", Channel = "Release" }, steam);
    new Installer(storage, prefs, () => false).Uninstall(steam);
    Check(File.ReadAllText(Path.Combine(steam.Root, "dwmapi.dll")) == "original" && !File.Exists(Path.Combine(steam.Root, "OpenSteamTool.dll")), "Uninstall restores originals");
    string stage = Path.Combine(storage.Root, "elevation", "test"); Directory.CreateDirectory(stage);
    string content = Path.Combine(stage, "0000.bin"); File.WriteAllText(content, "helper test");
    string helperTarget = Path.Combine(steam.LuaDirectory, "helper.lua");
    File.WriteAllText(Path.Combine(stage, "request.json"), System.Text.Json.JsonSerializer.Serialize(new ElevatedRequest { SteamRoot = steam.Root, Description = "Helper test", Changes = [new ElevatedChange { Target = helperTarget, ContentFile = content }] }));
    ElevatedFileTransaction.Execute(stage, storage);
    var helperResult = System.Text.Json.JsonSerializer.Deserialize<ElevatedResponse>(File.ReadAllText(Path.Combine(stage, "response.json")));
    Check(helperResult?.Error is null && helperResult?.Record is not null && File.ReadAllText(helperTarget) == "helper test", "Elevated helper protocol");
    Console.WriteLine("All core checks passed.");
}
finally { Directory.Delete(root, true); }

static void Check(bool condition, string name) { if (!condition) throw new Exception("Failed: " + name); }
