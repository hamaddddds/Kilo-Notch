$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Desktop builds and packaging are allowed only in GitHub Actions.' }
Set-Location (Split-Path $PSScriptRoot -Parent)
function Checked([scriptblock]$Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Command failed: $Command" } }
Checked { dotnet run --project tests/Notch.Smoke.csproj -c Release }
Checked { dotnet publish Notch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/app }
Checked { cmake -S installer -B installer/build -A x64 }
Checked { cmake --build installer/build --config Release --target KiloUninstall }
$payload = Join-Path $PWD 'artifacts/payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item -LiteralPath artifacts/app/KiloNotch.exe -Destination $payload
Copy-Item -LiteralPath installer/build/Release/KiloUninstall.exe -Destination $payload
$appHash = (Get-FileHash "$payload/KiloNotch.exe" -Algorithm SHA256).Hash.ToLowerInvariant()
$uninstallHash = (Get-FileHash "$payload/KiloUninstall.exe" -Algorithm SHA256).Hash.ToLowerInvariant()
@"
#pragma once
inline constexpr wchar_t AppSha256[] = L"$appHash";
inline constexpr wchar_t UninstallSha256[] = L"$uninstallHash";
"@ | Set-Content installer/payload_hashes.h -Encoding utf8
$cabOutput = Join-Path $PWD 'installer'
@"
.OPTION EXPLICIT
.Set CabinetNameTemplate=payload.cab
.Set DiskDirectoryTemplate="$cabOutput"
.Set Cabinet=on
.Set Compress=on
.Set CompressionType=LZX
.Set MaxDiskSize=0
"$payload\KiloNotch.exe" KiloNotch.exe
"$payload\KiloUninstall.exe" KiloUninstall.exe
"@ | Set-Content artifacts/payload.ddf -Encoding ascii
Checked { makecab /F artifacts/payload.ddf }
Checked { cmake -S installer -B installer/build -A x64 }
Checked { cmake --build installer/build --config Release --target KiloSetup KiloInstallerTests }
Checked { ctest --test-dir installer/build -C Release --output-on-failure }
foreach ($program in @('KiloSetup','KiloUninstall')) {
    # Only on the CI runner: render and navigate the custom window, without installing.
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PWD "installer/build/Release/$program.exe"), '--smoke')
    $start.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($start)
    if (!$process.WaitForExit(30000)) { throw "$program UI smoke test timed out." }
    if ($process.ExitCode -ne 0) { throw "$program UI smoke test failed: $($process.ExitCode)" }
}
New-Item -ItemType Directory -Path artifacts/release -Force | Out-Null
Copy-Item installer/build/Release/KiloSetup.exe artifacts/release/
Copy-Item artifacts/app/KiloNotch.exe artifacts/release/
[xml]$project = Get-Content Notch.csproj -Raw
$version = $project.Project.PropertyGroup.Version | Where-Object { $_ }
$hash = (Get-FileHash artifacts/release/KiloSetup.exe -Algorithm SHA256).Hash.ToLowerInvariant()
@{
    version = $version
    url = "https://github.com/hamaddddds/Kilo-Notch/releases/download/v$version/KiloSetup.exe"
    sha256 = $hash
    notes = '60 Hz animation pacing, lighter lyric transitions and bounded caches, reusable visualizers, and smaller camera preview and album-art buffers. Custom installer and verified updates included.'
} | ConvertTo-Json | Set-Content artifacts/release/stable.json -Encoding utf8NoBOM
"$hash  KiloSetup.exe" | Set-Content artifacts/release/SHA256SUMS.txt -Encoding ascii
