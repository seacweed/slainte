param(
    [Parameter(Mandatory=$true)][string]$Attempt,
    [Parameter(Mandatory=$true)][string]$Method,
    [string]$ProjectPath,
    [string]$UnityExecutable = 'A:\UnityHub\Unity\6000.3.5f2\Editor\Unity.exe',
    [ValidateRange(30,600)][int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent $PSScriptRoot
$evidencePath = Join-Path $workspacePath ('FluidGpuExperimentEvidence/' + $Attempt)
if (Test-Path -LiteralPath $evidencePath) { throw 'Attempt already exists.' }
New-Item -ItemType Directory -Path $evidencePath | Out-Null
$unityPath = $UnityExecutable
$testProjectPath = if ($ProjectPath) { (Resolve-Path -LiteralPath $ProjectPath).Path }
    else { Join-Path $workspacePath 'Library/FluidModelDHarness' }
$sourceManifestPath = Join-Path $testProjectPath 'source-sha256.json'
if (Test-Path -LiteralPath $sourceManifestPath) {
    Copy-Item -LiteralPath $sourceManifestPath -Destination (Join-Path $evidencePath 'source-sha256.json')
}
$harnessHashes = @{}
Get-ChildItem -LiteralPath (Join-Path $testProjectPath 'Assets/Editor') -Filter '*.cs' -File | ForEach-Object {
    $harnessHashes[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$harnessHashes | ConvertTo-Json | Set-Content (Join-Path $evidencePath 'harness-sha256.json') -Encoding UTF8
$snapshotId = (Get-FileHash -LiteralPath (Join-Path $evidencePath 'harness-sha256.json') -Algorithm SHA256).Hash.ToLowerInvariant()
$snapshotPath = Join-Path $PSScriptRoot ('HarnessSnapshots/' + $snapshotId)
New-Item -ItemType Directory -Path $snapshotPath -Force | Out-Null
foreach ($scriptName in $harnessHashes.Keys) {
    Copy-Item -LiteralPath (Join-Path $testProjectPath ('Assets/Editor/' + $scriptName)) -Destination (Join-Path $snapshotPath $scriptName)
}
$logPath = Join-Path $evidencePath 'Editor.log'
$unityArguments = @('-batchmode', '-force-d3d11', '-projectPath', $testProjectPath,
    '-executeMethod', 'PhysicsLabValidationDispatch.Run', '-logFile', $logPath)
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $unityPath
$startInfo.Arguments = ($unityArguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
$startInfo.WorkingDirectory = $workspacePath
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.EnvironmentVariables['PHYSICSLAB_EVIDENCE_DIR'] = $evidencePath
$startInfo.EnvironmentVariables['PHYSICSLAB_ENTRY_METHOD'] = $Method
$unityProcess = New-Object System.Diagnostics.Process
$unityProcess.StartInfo = $startInfo
$startedAt = Get-Date
@{executable=$unityPath;arguments=$unityArguments;entryMethod=$Method;forceShaderImport=$true;started=$startedAt.ToString('o');timeoutSeconds=$TimeoutSeconds} |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidencePath 'command.json') -Encoding UTF8
if (-not $unityProcess.Start()) { throw 'Unity process did not start.' }
# Retain the process handle until the exit code has been read.
$null = $unityProcess.Handle
@{pid=$unityProcess.Id;started=$startedAt.ToString('o')} |
    ConvertTo-Json | Set-Content (Join-Path $evidencePath 'process.json') -Encoding UTF8
$timedOut = -not $unityProcess.WaitForExit($TimeoutSeconds * 1000)
if ($timedOut) { $unityProcess.Kill() }
$unityProcess.WaitForExit()
$actualExitCode = $unityProcess.ExitCode
if ($null -eq $actualExitCode) { throw 'Unity exit code unavailable.' }
@{exitCode=[int]$actualExitCode;timedOut=$timedOut;pid=$unityProcess.Id;
    started=$startedAt.ToString('o');finished=(Get-Date).ToString('o')} |
    ConvertTo-Json | Set-Content (Join-Path $evidencePath 'exit.json') -Encoding UTF8
$unityProcess.Dispose()
Write-Output "Unity attempt $Attempt completed: exit=$actualExitCode; timeout=$timedOut"
exit $actualExitCode
