param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
if ($Runtime -ne "win-x64") { throw "The initial release supports Windows x64 and 64-bit ASIO drivers." }
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    & "$PSScriptRoot/build-soxr.ps1"
    $output = Join-Path $repo "artifacts/ChatPi-$Runtime"
    $zip = Join-Path $repo "artifacts/ChatPi-$Runtime.zip"
    if (Test-Path $output) { Remove-Item -Recurse -Force $output }
    dotnet publish src/ChatPi.App/ChatPi.App.csproj -c Release -r $Runtime --self-contained true -o $output -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
    Copy-Item README.md, LICENSE, THIRD-PARTY-NOTICES.md $output
    Copy-Item docs/windows-testing.md (Join-Path $output "WINDOWS-TESTING.md")
    Copy-Item -Recurse licenses (Join-Path $output "licenses")
    New-Item -ItemType Directory -Force (Join-Path $output "third-party") | Out-Null
    Copy-Item third-party/soxr-0.1.3.tar.gz (Join-Path $output "third-party")
    Copy-Item third-party/README.md (Join-Path $output "third-party")
    Copy-Item docs/audio-processing.md (Join-Path $output "AUDIO-PROCESSING.md")
    Copy-Item scripts/build-soxr.ps1 (Join-Path $output "third-party/build-soxr.ps1")
    if (!(Test-Path (Join-Path $output "soxr.dll"))) { throw "libsoxr is missing from publish output" }
    $cacheLine = dotnet nuget locals global-packages --list --force-english-output
    if ($LASTEXITCODE -ne 0) { throw "Cannot locate NuGet package cache" }
    $nugetCache = ($cacheLine | Where-Object { $_ -match '^global-packages:' } | Select-Object -First 1) -replace '^global-packages:\s*', ''
    $frameworks = (Get-Content (Join-Path $output "ChatPi.runtimeconfig.json") -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks
    $coreVersion = ($frameworks | Where-Object name -eq "Microsoft.NETCore.App").version
    $desktopVersion = ($frameworks | Where-Object name -eq "Microsoft.WindowsDesktop.App").version
    $corePackage = Join-Path $nugetCache "microsoft.netcore.app.runtime.$Runtime/$coreVersion"
    $desktopPackage = Join-Path $nugetCache "microsoft.windowsdesktop.app.runtime.$Runtime/$desktopVersion"
    Copy-Item (Join-Path $corePackage "LICENSE.TXT") (Join-Path $output "licenses/dotnet-runtime-LICENSE.txt")
    Copy-Item (Join-Path $corePackage "THIRD-PARTY-NOTICES.TXT") (Join-Path $output "licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt")
    Copy-Item (Join-Path $desktopPackage "LICENSE") (Join-Path $output "licenses/dotnet-windowsdesktop-LICENSE.txt")
    Compress-Archive -Path "$output/*" -DestinationPath $zip -Force
    Get-FileHash $zip -Algorithm SHA256
    Write-Host "Built $zip. Extract the whole archive before running ChatPi.exe."
}
finally { Pop-Location }
