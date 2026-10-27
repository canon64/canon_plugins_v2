param([string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2019.4.9f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$shaderProject = Join-Path $PSScriptRoot 'ShaderBuild'
$shaderLog = Join-Path $PSScriptRoot 'obj\shader-build.log'
New-Item -ItemType Directory -Force -Path (Split-Path $shaderLog) | Out-Null
$editorProcess = Start-Process -FilePath $UnityEditor -WindowStyle Hidden -PassThru -ArgumentList @(
    '-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $shaderProject + '"'),
    '-executeMethod', 'LiquidShaderBuild.Build', '-logFile', ('"' + $shaderLog + '"')
)
$editorProcess.WaitForExit()
if ($editorProcess.ExitCode -ne 0 -or
    (Select-String -LiteralPath $shaderLog -Pattern 'is not supported because the module' -Quiet) -or
    !(Select-String -LiteralPath $shaderLog -Pattern 'MILK_LIQUID_SHADER_BUILD_OK' -Quiet)) {
    throw ('Shader build failed. See ' + $shaderLog)
}
Write-Output 'Liquid shader built with Unity 2019.4.9f1.'
