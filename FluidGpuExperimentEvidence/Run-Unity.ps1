param(
    [Parameter(Mandatory=$true)][string]$Attempt,
    [Parameter(Mandatory=$true)][string]$Method,
    [string]$ProjectPath,
    [string]$UnityExecutable,
    [switch]$QuitAfterMethod,
    [ValidateRange(30,1800)][int]$TimeoutSeconds = 240
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
if (-not $ProjectPath) { $ProjectPath = Join-Path $PSScriptRoot 'HarnessProject' }
if (-not $UnityExecutable) {
    $version = (Get-Content -LiteralPath (Join-Path $workspace 'ProjectSettings/ProjectVersion.txt') | Select-String '^m_EditorVersion: (.+)$').Matches.Groups[1].Value
    $UnityExecutable = Join-Path ${env:ProgramFiles} "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityExecutable)) { throw "Unity executable not found: $UnityExecutable" }
$attemptDirectory = Join-Path $workspace ('FluidGpuExperimentEvidence/' + $Attempt)
if (Test-Path -LiteralPath $attemptDirectory) { throw "Attempt already exists; logs must never be overwritten: $Attempt" }
New-Item -ItemType Directory -Path $attemptDirectory | Out-Null
$arguments = @('-batchmode','-force-d3d11','-projectPath',$ProjectPath,'-executeMethod',$Method,'-logFile',(Join-Path $attemptDirectory 'Editor.log'))
if ($QuitAfterMethod) { $arguments += '-quit' }
$started = Get-Date
$env:PHYSICSLAB_EVIDENCE_DIR = $attemptDirectory
@{executable=$unityExecutable;arguments=$arguments;started=$started.ToString('o');workingDirectory=$workspace;timeoutSeconds=$TimeoutSeconds} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $attemptDirectory 'command.json') -Encoding utf8
$quotedArguments = $arguments | ForEach-Object { '"' + $_ + '"' }
$process = Start-Process -FilePath $UnityExecutable -ArgumentList $quotedArguments -WorkingDirectory $workspace -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $attemptDirectory 'stdout.txt') -RedirectStandardError (Join-Path $attemptDirectory 'stderr.txt')
@{pid=$process.Id;started=$started.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $attemptDirectory 'process.json') -Encoding utf8
$timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
if ($timedOut) { $process.Kill(); $process.WaitForExit() }
$process.Refresh()
$exitCode = $process.ExitCode
@{exitCode=$exitCode;timedOut=$timedOut;pid=$process.Id;started=$started.ToString('o');finished=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $attemptDirectory 'exit.json') -Encoding utf8
$crashRoot = Join-Path $env:LOCALAPPDATA 'Temp/Unity/Editor/Crashes'
if (Test-Path -LiteralPath $crashRoot) {
    $crashes = Get-ChildItem -LiteralPath $crashRoot -Directory | Where-Object LastWriteTime -ge $started
    foreach ($crash in $crashes) { Copy-Item -LiteralPath $crash.FullName -Destination $attemptDirectory -Recurse }
}
Write-Output "Unity attempt $Attempt finished with exit code $exitCode. Evidence: $attemptDirectory"
exit $exitCode
