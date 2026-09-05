param([Parameter(Mandatory=$true)][string]$BackupDirectory)
$ErrorActionPreference = 'Stop'
$backup = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
$metadata = Get-Content -LiteralPath (Join-Path $backup 'backup.json') -Raw | ConvertFrom-Json
$target = [IO.Path]::GetFullPath($metadata.TargetDirectory).TrimEnd('\')
$exe = Join-Path $target 'QuickLook.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Original installation no longer exists' }
foreach ($file in $metadata.Files) {
    $from = [IO.Path]::GetFullPath((Join-Path $backup $file))
    $to = [IO.Path]::GetFullPath((Join-Path $target $file))
    if (!$from.StartsWith($backup+'\', [StringComparison]::OrdinalIgnoreCase) -or !$to.StartsWith($target+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid backup path' }
    if (!(Test-Path -LiteralPath $from)) { throw "Missing backup: $file" }
}
Get-Process QuickLook -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process
try {
    foreach ($file in $metadata.Files) { Copy-Item -LiteralPath (Join-Path $backup $file) -Destination (Join-Path $target $file) -Force }
} finally { Start-Process -FilePath $exe -WorkingDirectory $target -WindowStyle Hidden }
Write-Output 'Original binaries restored. Current settings and saved files were preserved.'
