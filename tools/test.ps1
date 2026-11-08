param([Parameter(Mandatory=$true)][string]$WadPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$testDirectory = Join-Path $projectRoot 'build/tests'
New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
$testExe = Join-Path $testDirectory 'WadTests.exe'
& $compiler '/nologo' '/target:exe' '/optimize+' "/out:$testExe" (Join-Path $projectRoot 'src/WadArchive.cs') (Join-Path $projectRoot 'src/CombatRules.cs') (Join-Path $projectRoot 'src/WaveRules.cs') (Join-Path $projectRoot 'src/SaveRules.cs') (Join-Path $projectRoot 'src/ContentSpecs.cs') (Join-Path $projectRoot 'tests/WadTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& $testExe $WadPath
if ($LASTEXITCODE -ne 0) { throw 'WAD tests failed' }
