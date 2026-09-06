param(
    [Parameter(Mandatory=$true)][string]$TargetDirectory,
    [string]$DefaultSaveRoot,
    [string]$BackupRoot
)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($TargetDirectory).TrimEnd('\')
$exe = Join-Path $target 'QuickLook.exe'
if (!(Test-Path -LiteralPath $exe) -or !(Test-Path -LiteralPath (Join-Path $target 'portable.lock'))) { throw 'Target must be an existing portable QuickLook installation.' }
if ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -notlike '4.5.0*') { throw 'This patch requires QuickLook 4.5.0. Merge and rebuild for other versions.' }
if (!$BackupRoot) { $BackupRoot = Join-Path (Split-Path $target -Parent) 'QuickLook-backups' }
$backup = Join-Path ([IO.Path]::GetFullPath($BackupRoot)) ('enhancements-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $destination = [IO.Path]::GetFullPath((Join-Path $target $entry.Path))
    if (!$destination.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path' }
    if ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('payload/' + $entry.Path))).Hash -ne $entry.SHA256) { throw "Payload checksum failed: $($entry.Path)" }
}
New-Item -ItemType Directory -Path $backup | Out-Null
$running = @(Get-Process QuickLook -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
foreach ($process in $running) { Stop-Process -Id $process.Id; $process.WaitForExit() }
try {
    Copy-Item -LiteralPath (Join-Path $target 'UserData') -Destination (Join-Path $backup 'UserData') -Recurse
    foreach ($entry in $manifest) {
        $old = Join-Path $backup $entry.Path
        New-Item -ItemType Directory -Force (Split-Path $old -Parent) | Out-Null
        Copy-Item -LiteralPath (Join-Path $target $entry.Path) -Destination $old
    }
    @{ TargetDirectory=$target; Files=@($manifest.Path); Created=(Get-Date).ToString('o') } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $backup 'backup.json') -Encoding UTF8
    foreach ($entry in $manifest) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('payload/' + $entry.Path)) -Destination (Join-Path $target $entry.Path) -Force
        if ((Get-FileHash -LiteralPath (Join-Path $target $entry.Path)).Hash -ne $entry.SHA256) { throw 'Deployed checksum mismatch' }
    }
    $settings = Join-Path $target 'UserData/QuickLook.Enhancements.config'
    if ($DefaultSaveRoot -and !(Test-Path -LiteralPath $settings)) {
        $doc = New-Object System.Xml.XmlDocument
        $doc.LoadXml('<Settings><SaveRoot/><StepSeconds>10</StepSeconds><HoldDelay>350</HoldDelay><RepeatInterval>200</RepeatInterval></Settings>')
        $doc.Settings.SaveRoot = [IO.Path]::GetFullPath($DefaultSaveRoot)
        $doc.Save($settings)
    }
} catch {
    foreach ($entry in $manifest) {
        $old = Join-Path $backup $entry.Path
        if (Test-Path -LiteralPath $old) { Copy-Item -LiteralPath $old -Destination (Join-Path $target $entry.Path) -Force }
    }
    throw
} finally {
    $started = Start-Process -FilePath $exe -WorkingDirectory $target -WindowStyle Hidden -PassThru
    try { [void]$started.WaitForInputIdle(10000) } catch { }
}
Write-Output "Installed. Backup: $backup"
