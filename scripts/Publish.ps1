param(
    [ValidateSet('win-arm64', 'win-x64')][string]$Runtime = 'win-arm64'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    dotnet restore ARMpulse.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet test tests/ARMpulse.Tests -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $output = Join-Path $repo "artifacts/ARMpulse-$Runtime"
    dotnet publish src/ARMpulse.Windows -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
        -p:RestoreLockedMode=true -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item README.md $output
    $zip = "$output.zip"
    Compress-Archive -Path "$output/*" -DestinationPath $zip -Force
    (Get-FileHash $zip -Algorithm SHA256).Hash + '  ' + (Split-Path $zip -Leaf) | Set-Content "$zip.sha256"
    Write-Host "Built $zip"
} finally { Pop-Location }
