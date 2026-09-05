param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repo
try {
    foreach ($project in @('QuickLook/QuickLook.csproj', 'QuickLook.Plugin/QuickLook.Plugin.VideoViewer/QuickLook.Plugin.VideoViewer.csproj')) {
        & $Dotnet build $project -c Release "-p:SolutionDir=$repo\" -v minimal
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    & $Dotnet run --project Tests/Enhancements/Enhancements.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Save tests failed' }
    $package = Join-Path $repo 'artifacts/package'
    $payload = Join-Path $package 'payload'
    New-Item -ItemType Directory -Force "$payload/QuickLook.Plugin/QuickLook.Plugin.VideoViewer" | Out-Null
    $files = @('QuickLook.exe', 'QuickLook.exe.config', 'QuickLook.Common.dll', 'QuickLook.Plugin/QuickLook.Plugin.VideoViewer/QuickLook.Plugin.VideoViewer.dll')
    $manifest = foreach ($file in $files) {
        Copy-Item -LiteralPath (Join-Path $repo "Build/Release/$file") -Destination (Join-Path $payload $file) -Force
        [pscustomobject]@{ Path = $file; SHA256 = (Get-FileHash -LiteralPath (Join-Path $payload $file)).Hash }
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath "$package/manifest.json" -Encoding UTF8
    Copy-Item -LiteralPath "$PSScriptRoot/install-enhancements.ps1", "$PSScriptRoot/rollback-enhancements.ps1" -Destination $package -Force
    Copy-Item -LiteralPath "$repo/ENHANCEMENTS.zh-CN.md" -Destination "$package/README.md" -Force
    Copy-Item -LiteralPath "$repo/LICENSE" -Destination $package -Force
    Compress-Archive -Path "$package/*" -DestinationPath "$repo/artifacts/QuickLook-4.5.0-enhancements.zip" -Force
} finally { Pop-Location }
