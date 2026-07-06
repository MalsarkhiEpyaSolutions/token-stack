# publish.ps1 — builds the distributable zip: dist/token-stack-v<version>.zip
param([string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
dotnet test TokenStack.sln
if ($LASTEXITCODE -ne 0) { throw "tests failed - not publishing" }
dotnet publish src/TokenStack.Cli -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:Version=$Version -o publish-out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
New-Item -ItemType Directory -Force dist | Out-Null
Compress-Archive -Force -DestinationPath "dist/token-saver-v$Version.zip" `
  -Path "publish-out/token-saver.exe", "README.md"
Remove-Item -Recurse -Force publish-out
Write-Host "dist/token-saver-v$Version.zip ready" -ForegroundColor Green
