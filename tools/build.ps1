param([Parameter(Mandatory=$true)][string]$KspPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$managed = Join-Path $KspPath 'KSP_x64_Data/Managed'
if (!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw "KSP libraries not found: $managed" }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler is required' }
$output = Join-Path $projectRoot 'build/GameData/KerbalDoom'
New-Item -ItemType Directory -Force -Path (Join-Path $output 'Plugins'),(Join-Path $output 'PluginData') | Out-Null
$refs = @('Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll','UnityEngine.dll','UnityEngine.UI.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.AudioModule.dll','UnityEngine.SpriteMaskModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.AnimationModule.dll')
$arguments = @('/nologo','/target:library','/optimize+','/warn:4',('/out:' + (Join-Path $output 'Plugins/KerbalDoom.dll')))
foreach ($ref in $refs) { $arguments += '/reference:' + (Join-Path $managed $ref) }
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'settings.example.cfg') -Destination (Join-Path $output 'PluginData/settings.cfg')
& (Join-Path $PSScriptRoot 'generate-cargo.ps1') -OutputDirectory (Join-Path $output 'Parts')
Get-FileHash -LiteralPath (Join-Path $output 'Plugins/KerbalDoom.dll') -Algorithm SHA256 | Format-List
Write-Output "Build ready: $output"
