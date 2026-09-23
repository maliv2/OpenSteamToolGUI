param(
    [Parameter(Mandatory)][string]$Stage,
    [Parameter(Mandatory)][string]$InstallDirectory,
    [Parameter(Mandatory)][int]$ProcessId
)
$ErrorActionPreference = 'Stop'
$files = Join-Path $Stage 'files'
$backup = Join-Path $Stage 'backup'
$log = Join-Path $Stage 'update.log'
try {
    $oldProcess = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($oldProcess) { $oldProcess.WaitForExit(60000) | Out-Null }
    if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) { throw 'The application did not exit.' }
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $updates = @(Get-ChildItem -LiteralPath $files -File)
    if (-not ($updates | Where-Object Name -EQ 'OpenSteamToolGUI.exe')) { throw 'Update executable is missing.' }
    foreach ($file in $updates) {
        $target = Join-Path $InstallDirectory $file.Name
        if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup $file.Name) -Force }
    }
    try {
        foreach ($file in $updates) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $InstallDirectory $file.Name) -Force }
    } catch {
        foreach ($file in $updates) {
            $target = Join-Path $InstallDirectory $file.Name
            $original = Join-Path $backup $file.Name
            if (Test-Path -LiteralPath $original) { Copy-Item -LiteralPath $original -Destination $target -Force }
            elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
        }
        throw
    }
    Start-Process -FilePath (Join-Path $InstallDirectory 'OpenSteamToolGUI.exe') -WorkingDirectory $InstallDirectory
    Remove-Item -LiteralPath $Stage -Recurse -Force
} catch {
    $_ | Out-File -LiteralPath $log -Encoding UTF8
}
