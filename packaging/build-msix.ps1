# Build MSIX package for AndroidEmulatorPlus
# Requires: Windows 10 SDK (makeappx.exe, signtool.exe)
# Usage: .\packaging\build-msix.ps1 [-CertPath <pfx>] [-CertPassword <pw>]

param(
    [string]$CertPath,
    [string]$CertPassword,
    [string]$Configuration = "Release",
    [string]$Version = "0.2.7.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "publish\msix-stage"
$outputDir = Join-Path $repoRoot "publish"
$msixPath = Join-Path $outputDir "AndroidEmulatorPlus-$Version.msix"

Write-Host "Building self-contained publish..."
dotnet publish "$repoRoot\AndroidEmulatorPlus\AndroidEmulatorPlus.csproj" `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Copying AppxManifest..."
Copy-Item "$PSScriptRoot\AppxManifest.xml" "$publishDir\AppxManifest.xml" -Force

# Update version in manifest
$manifest = Get-Content "$publishDir\AppxManifest.xml" -Raw
$manifest = $manifest -replace 'Version="[\d.]+"', "Version=`"$Version`""
Set-Content "$publishDir\AppxManifest.xml" $manifest -NoNewline

# Create placeholder logo assets if they don't exist
$assetsDir = Join-Path $publishDir "Assets"
if (-not (Test-Path $assetsDir)) { New-Item -ItemType Directory -Path $assetsDir | Out-Null }

# Generate simple placeholder PNGs using .NET
$sizes = @{
    "StoreLogo.png" = 50
    "Square44x44Logo.png" = 44
    "Square150x150Logo.png" = 150
    "Wide310x150Logo.png" = @(310, 150)
}

Add-Type -AssemblyName System.Drawing
foreach ($entry in $sizes.GetEnumerator()) {
    $filePath = Join-Path $assetsDir $entry.Key
    if (Test-Path $filePath) { continue }
    if ($entry.Value -is [array]) {
        $w = $entry.Value[0]; $h = $entry.Value[1]
    } else {
        $w = $entry.Value; $h = $entry.Value
    }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(0x1E, 0x1E, 0x2E))
    $g.Dispose()
    $bmp.Save($filePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  Created placeholder: $($entry.Key)"
}

# Find makeappx.exe
$sdkPaths = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe"
    "$env:ProgramFiles\Windows Kits\10\bin\*\x64\makeappx.exe"
)
$makeappx = $sdkPaths | Resolve-Path -ErrorAction SilentlyContinue | Sort-Object -Descending | Select-Object -First 1

if (-not $makeappx) {
    Write-Warning "makeappx.exe not found. Install the Windows 10 SDK."
    Write-Host "Staged files are in: $publishDir"
    Write-Host "Run manually: makeappx pack /d `"$publishDir`" /p `"$msixPath`""
    exit 0
}

Write-Host "Packing MSIX with $makeappx..."
& $makeappx pack /d $publishDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed" }

# Sign if certificate provided
if ($CertPath) {
    $signtool = $makeappx.ToString().Replace("makeappx.exe", "signtool.exe")
    if (Test-Path $signtool) {
        Write-Host "Signing with $CertPath..."
        $signArgs = @("sign", "/fd", "SHA256", "/f", $CertPath)
        if ($CertPassword) { $signArgs += @("/p", $CertPassword) }
        $signArgs += $msixPath
        & $signtool @signArgs
        if ($LASTEXITCODE -ne 0) { Write-Warning "Signing failed (non-fatal for sideload)" }
    }
}

Write-Host "MSIX package: $msixPath"
Write-Host "Done."
