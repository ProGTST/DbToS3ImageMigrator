# BodyImageMigrator - Clean Publish (Console + Runner)
# Close BodyImageMigrator.Runner before running (files may be locked).

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$consoleDir = Join-Path $root "Console"
$runnerDir = Join-Path $root "Runner"

Write-Host "=== Clean Publish ===" -ForegroundColor Cyan
Write-Host "Console: $consoleDir"
Write-Host "Runner:  $runnerDir"
Write-Host ""

if (Test-Path $consoleDir) {
    Remove-Item -Path "$consoleDir\*" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "[1/3] Console folder cleared."
}
if (Test-Path $runnerDir) {
    Remove-Item -Path "$runnerDir\*" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "[2/3] Runner folder cleared."
}

$src = Split-Path $root -Parent
$consoleProj = Join-Path $src "src\BodyImageMigrator\BodyImageMigrator.csproj"
$runnerProj = Join-Path $src "src\BodyImageMigrator.Runner\BodyImageMigrator.Runner.csproj"

Write-Host ""
Write-Host "[3/3] Publishing Console..." -ForegroundColor Yellow
dotnet publish $consoleProj -p:PublishProfile=FolderProfile
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Console: Done -> $consoleDir\BodyImageMigrator.exe" -ForegroundColor Green

Write-Host ""
Write-Host "Publishing Runner..." -ForegroundColor Yellow
dotnet publish $runnerProj -c Release -r win-x64 --self-contained true -o $runnerDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "Runner publish failed. Close BodyImageMigrator.Runner and run again." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "Runner: Done -> $runnerDir\BodyImageMigrator.Runner.exe" -ForegroundColor Green

Write-Host ""
Write-Host "=== Publish Complete ===" -ForegroundColor Cyan
