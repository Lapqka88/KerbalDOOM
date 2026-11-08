param([Parameter(Mandatory=$true)][string]$WadPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testDirectory = Join-Path $projectRoot 'build/tests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$testExe = Join-Path $testDirectory 'InventoryIconTests.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler '/nologo' '/target:exe' "/out:$testExe" (Join-Path $projectRoot 'src/WadArchive.cs') (Join-Path $projectRoot 'tests/InventoryIconTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Inventory icon test compilation failed' }
& $testExe $WadPath (Join-Path $projectRoot 'src/DoomInventoryIcons.cs')
if ($LASTEXITCODE -ne 0) { throw 'Inventory icon tests failed' }
