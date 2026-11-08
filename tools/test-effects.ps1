param([Parameter(Mandatory=$true)][string]$WadPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testDirectory = Join-Path $projectRoot 'build/tests'
New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$testExe = Join-Path $testDirectory 'EffectsWadTests.exe'
& $compiler '/nologo' '/target:exe' "/out:$testExe" (Join-Path $projectRoot 'src/WadArchive.cs') (Join-Path $projectRoot 'tests/EffectsWadTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Effect tests compilation failed' }
& $testExe $WadPath (Join-Path $projectRoot 'src/WorldActors.cs') (Join-Path $projectRoot 'src/WorldEffects.cs')
if ($LASTEXITCODE -ne 0) { throw 'Effect WAD tests failed' }
