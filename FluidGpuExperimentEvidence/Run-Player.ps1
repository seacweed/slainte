param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Attempt,
    [string]$PlayerExecutable,
    [ValidateRange(16, 600)][int]$WarmupFrames = 30,
    [ValidateRange(30, 1200)][int]$SampleFrames = 90,
    [ValidateRange(30, 3600)][int]$TimeoutSeconds = 900,
    [ValidateRange(320, 3840)][int]$Width = 1280,
    [ValidateRange(240, 2160)][int]$Height = 720,
    [ValidatePattern('^[A-Za-z,]+$')][string]$Modes = 'CReferenceSurface,DImprovedSurface,ECalibratedLiquid'
)
$ErrorActionPreference = 'Stop'
$playerWorkspace = Split-Path -Parent $PSScriptRoot
if (-not $PlayerExecutable) {
    $PlayerExecutable = Join-Path $playerWorkspace 'Library/FluidSwapHarness/PlayerBenchmark/FluidExperimentBenchmark.exe'
}
$PlayerExecutable = [IO.Path]::GetFullPath($PlayerExecutable)
if (-not (Test-Path -LiteralPath $PlayerExecutable -PathType Leaf)) { throw "Player not found: $PlayerExecutable" }
$playerAttemptDirectory = Join-Path $PSScriptRoot $Attempt
if (Test-Path -LiteralPath $playerAttemptDirectory) { throw "Attempt already exists; evidence must not be overwritten: $Attempt" }
New-Item -ItemType Directory -Path $playerAttemptDirectory | Out-Null
$playerArguments = @('-fluid-benchmark', '-force-d3d11', '-fluid-output', $playerAttemptDirectory,
    '-fluid-warmup', $WarmupFrames, '-fluid-samples', $SampleFrames, '-fluid-max-seconds', $TimeoutSeconds,
    '-fluid-width', $Width, '-fluid-height', $Height, '-fluid-modes', $Modes,
    '-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height,
    '-logFile', (Join-Path $playerAttemptDirectory 'Player.log'))
$playerStarted = Get-Date
@{ executable = $PlayerExecutable; sha256 = (Get-FileHash -LiteralPath $PlayerExecutable -Algorithm SHA256).Hash;
    arguments = $playerArguments; started = $playerStarted.ToString('o'); workingDirectory = $playerWorkspace;
    timeoutSeconds = $TimeoutSeconds; batchMode = $false } | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $playerAttemptDirectory 'command.json') -Encoding utf8
$playerQuotedArguments = $playerArguments | ForEach-Object { '"' + $_ + '"' }
$playerProcess = $null
try {
    $playerProcess = Start-Process -FilePath $PlayerExecutable -ArgumentList $playerQuotedArguments -WorkingDirectory $playerWorkspace `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $playerAttemptDirectory 'stdout.txt') `
        -RedirectStandardError (Join-Path $playerAttemptDirectory 'stderr.txt')
    @{ pid = $playerProcess.Id; started = $playerStarted.ToString('o') } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $playerAttemptDirectory 'process.json') -Encoding utf8
    $playerTimedOut = -not $playerProcess.WaitForExit($TimeoutSeconds * 1000)
    if ($playerTimedOut) { $playerProcess.Kill(); $playerProcess.WaitForExit() }
    $playerProcess.Refresh()
    $playerExitCode = if ($playerTimedOut) { 124 } else { $playerProcess.ExitCode }
    @{ exitCode = $playerExitCode; processExitCode = $playerProcess.ExitCode; timedOut = $playerTimedOut;
        pid = $playerProcess.Id; started = $playerStarted.ToString('o'); finished = (Get-Date).ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $playerAttemptDirectory 'exit.json') -Encoding utf8
    Write-Output "Player attempt $Attempt finished with exit code $playerExitCode. Evidence: $playerAttemptDirectory"
    exit $playerExitCode
}
catch {
    @{ exitCode = 1; launchOrWaitError = $_.Exception.Message; started = $playerStarted.ToString('o');
        finished = (Get-Date).ToString('o') } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $playerAttemptDirectory 'exit.json') -Encoding utf8
    throw
}
