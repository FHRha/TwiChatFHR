$ErrorActionPreference = "Stop"

$projectName = "TwitchChatCore.csproj"
$outputDir = ".\dev_artifact"

Write-Host "Building $projectName for fast development testing..."

Write-Host "Stopping running instances to release file locks..."
Stop-Process -Name "TwitchChatCore" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "TwiChatUpdater" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# 1. Clean the old artifact directory
if (Test-Path $outputDir) {
    Write-Host "Removing old dev_artifact directory..."
    cmd.exe /c "rmdir /s /q $($outputDir) 2>nul"
}

# 2. Run dotnet build (VERY FAST)
# This does NOT compress or pack the .NET runtime into a single file.
# It uses the installed .NET runtime on your PC.
Write-Host "Building Main App..."
dotnet publish $projectName -c Debug -o $outputDir

Write-Host "Building Updater..."
dotnet publish .\TwiChatUpdater\TwiChatUpdater.csproj -c Debug -o $outputDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "Dev build completed in seconds! Output is in $outputDir" -ForegroundColor Green
} else {
    Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
