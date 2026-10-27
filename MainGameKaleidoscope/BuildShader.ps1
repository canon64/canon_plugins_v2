param([string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2019.4.9f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'ShaderBuild'
$log = Join-Path $PSScriptRoot 'obj\shader-build.log'
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
$p = Start-Process -FilePath $UnityEditor -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-nographics','-quit','-projectPath',('"'+$project+'"'),'-executeMethod','KaleidoscopeShaderBuild.Build','-logFile',('"'+$log+'"'))
$p.WaitForExit()
if ($p.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'KALEIDOSCOPE_SHADER_BUILD_OK' -Quiet)) { throw "Shader build failed: $log" }
Write-Output 'Kaleidoscope shader built.'
