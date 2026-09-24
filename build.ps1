param([switch]$SkipSdkInstall)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sdkRoot = Join-Path $env:LOCALAPPDATA 'OpenSteamToolGUI\sdk'
$dotnetExe = Join-Path $sdkRoot 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) {
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installed -and ((& $installed.Source --version) -match '^10\.')) { $dotnetExe = $installed.Source }
}
if (-not (Test-Path -LiteralPath $dotnetExe)) {
    if ($SkipSdkInstall) { throw 'The .NET 10 SDK is missing.' }
    Write-Host 'Downloading the .NET 10 SDK to the current user profile (about 300 MB; no Visual Studio installation).'
    $installer = Join-Path $env:TEMP 'dotnet-install-ostgui.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Channel 10.0 -InstallDir $sdkRoot -NoPath
    if ($LASTEXITCODE -ne 0) { throw 'SDK installation failed.' }
}
$dist = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
foreach ($mode in @('portable','lightweight')) {
    $output = Join-Path $dist ('.staging-' + $mode + '-' + [guid]::NewGuid().ToString('N'))
    $selfContained = if ($mode -eq 'portable') { 'true' } else { 'false' }
    try {
        & $dotnetExe publish (Join-Path $projectRoot 'OpenSteamToolGUI.csproj') -c Release -r win-x64 --self-contained $selfContained "-p:DistributionVariant=$mode" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:PublishReadyToRun=false --source 'https://api.nuget.org/v3/index.json' -o $output
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $mode" }
        $published = @(Get-ChildItem -LiteralPath $output -Recurse -File -Force)
        $executable = Join-Path $output 'OpenSteamToolGUI.exe'
        if ($published.Count -ne 1 -or $published[0].FullName -ne $executable) {
            throw "Expected only OpenSteamToolGUI.exe in the $mode publish output; found: $($published.Name -join ', ')"
        }
        $archive = Join-Path $dist "OpenSteamToolGUI-$mode-win-x64.zip"
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
        Compress-Archive -LiteralPath $executable -DestinationPath $archive
        Write-Host "Created $archive"
    }
    finally {
        $resolvedDist = [IO.Path]::GetFullPath($dist).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $resolvedOutput = [IO.Path]::GetFullPath($output)
        if ($resolvedOutput.StartsWith($resolvedDist, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedOutput)) {
            Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
        }
    }
}
