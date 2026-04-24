# Build the CLI in-repo, stage output + IFC models to C:\temp\ifcenvmapper\,
# and run from there. Workaround: xBIM native DLLs crash when loaded from
# Google Drive Streaming paths (see memory/project_gdrive_native_dll_crash.md).

$ErrorActionPreference = "Stop"

$repoRoot     = Resolve-Path (Join-Path $PSScriptRoot "..")
$cliProj      = Join-Path $repoRoot "src\Cli\Cli.csproj"
$cliOutDir    = Join-Path $repoRoot "src\Cli\bin\Debug\net8.0"
$modelsSrcDir = Join-Path $repoRoot "data\models"

$runDir       = "C:\temp\ifcenvmapper"
$runModelsDir = Join-Path $runDir "data\models"

Write-Host "==> Building CLI" -ForegroundColor Cyan
dotnet build $cliProj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Staging to $runDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $runModelsDir | Out-Null
Copy-Item (Join-Path $cliOutDir "*") $runDir -Recurse -Force
Copy-Item (Join-Path $modelsSrcDir "*.ifc") $runModelsDir -Force

# xBIM caches processed geometry in sidecar files; stale caches from a crashed
# run can poison the next run. Wipe them before each execution.
Get-ChildItem -Path $runDir -Filter *.xbim -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $runDir -Filter *.xbimGC -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "==> Running ifcenvmapper.dll from $runDir" -ForegroundColor Cyan
Push-Location $runDir
try {
    dotnet ifcenvmapper.dll
}
finally {
    Pop-Location
}
