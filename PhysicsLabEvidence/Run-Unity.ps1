param(
    [Parameter(Mandatory=$true)][string]$Attempt,
    [Parameter(Mandatory=$true)][string]$Method,
    [string]$ProjectPath = 'A:/UnityHub/proj/slainte',
    [switch]$QuitAfterMethod
)
$ErrorActionPreference = 'Stop'
$workspace = 'A:/UnityHub/proj/slainte'
$attemptDirectory = Join-Path $workspace ('PhysicsLabEvidence/' + $Attempt)
if (Test-Path -LiteralPath $attemptDirectory) { throw "Attempt already exists; logs must never be overwritten: $Attempt" }
New-Item -ItemType Directory -Path $attemptDirectory | Out-Null
$unityExecutable = 'A:/UnityHub/Unity/6000.3.5f2/Editor/Unity.exe'
$arguments = @('-batchmode','-force-d3d11','-projectPath',$ProjectPath,'-executeMethod',$Method,'-logFile',(Join-Path $attemptDirectory 'Editor.log'))
if ($QuitAfterMethod) { $arguments += '-quit' }
$started = Get-Date
$env:PHYSICSLAB_EVIDENCE_DIR = $attemptDirectory
@{executable=$unityExecutable;arguments=$arguments;started=$started.ToString('o');workingDirectory=$workspace} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $attemptDirectory 'command.json') -Encoding utf8
$process = Start-Process -FilePath $unityExecutable -ArgumentList $arguments -WorkingDirectory $workspace -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $attemptDirectory 'stdout.txt') -RedirectStandardError (Join-Path $attemptDirectory 'stderr.txt')
@{pid=$process.Id;started=$started.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $attemptDirectory 'process.json') -Encoding utf8
$timedOut = -not $process.WaitForExit(240000)
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
