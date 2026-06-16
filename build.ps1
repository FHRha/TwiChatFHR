$ErrorActionPreference = "Stop"

$projectName = "TwitchChatCore.csproj"
$outputDir = ".\artifact"

Write-Host "Building $projectName into a single executable..."

# 1. Clean the old artifact directory
if (Test-Path $outputDir) {
    Write-Host "Removing old artifact directory..."
    Remove-Item -Recurse -Force $outputDir
}

# 2. Run dotnet publish
# -c Release: Release mode
# -r win-x64: Target Windows x64
# --self-contained true: Include the .NET runtime so the user doesn't need it installed
# -p:PublishSingleFile=true: Pack everything into a single .exe
# -p:IncludeNativeLibrariesForSelfExtract=true: Include native DLLs (like WebView2) inside the single .exe
# -o $outputDir: Output to the artifact folder

Write-Host "Publishing..."
dotnet publish $projectName -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $outputDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build pipeline completed successfully! Output is in $outputDir" -ForegroundColor Green
} else {
    Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
