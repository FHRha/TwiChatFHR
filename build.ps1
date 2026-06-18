$ErrorActionPreference = "Stop"

$projectName = "TwitchChatCore.csproj"
$outputDir = ".\artifact"

Write-Host "Building $projectName into a single executable..."

Write-Host "Stopping running instances to release file locks..."
Stop-Process -Name "TwitchChatCore" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "TwiChatUpdater" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# 1. Clean the old artifact directory
if (Test-Path $outputDir) {
    Write-Host "Removing old artifact directory..."
    cmd.exe /c "rmdir /s /q $($outputDir) 2>nul"
}

# 2. Run dotnet publish
# -c Release: Release mode
# -r win-x64: Target Windows x64
# --self-contained true: Include the .NET runtime so the user doesn't need it installed
# -p:PublishSingleFile=true: Pack everything into a single .exe
# -p:IncludeNativeLibrariesForSelfExtract=true: Include native DLLs (like WebView2) inside the single .exe
# -o $outputDir: Output to the artifact folder

Write-Host "Publishing Main App..."
dotnet publish $projectName -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $outputDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to build Main App!" -ForegroundColor Red
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit $LASTEXITCODE
}

Write-Host "Publishing Updater..."
dotnet publish .\TwiChatUpdater\TwiChatUpdater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $outputDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to build Updater!" -ForegroundColor Red
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit $LASTEXITCODE
}

Write-Host "Build pipeline completed successfully! Output is in $outputDir" -ForegroundColor Green

Write-Host "Press any key to continue..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

