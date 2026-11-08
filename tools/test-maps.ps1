param([Parameter(Mandatory=$true)][string]$WadPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$testDirectory = Join-Path $projectRoot 'build/tests'
New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
$testExe = Join-Path $testDirectory 'DoomMapTests.exe'
& $compiler '/nologo' '/target:exe' '/optimize+' "/out:$testExe" (Join-Path $projectRoot 'src/DoomMapData.cs') (Join-Path $projectRoot 'tests/DoomMapTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Map test compilation failed' }
& $testExe $WadPath
if ($LASTEXITCODE -ne 0) { throw 'Map tests failed' }
