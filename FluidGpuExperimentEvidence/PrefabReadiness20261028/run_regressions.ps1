$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runnerPath = Join-Path $workspacePath 'FluidGpuExperimentEvidence/Run-FloatingContents.ps1'
$powershellPath = (Get-Command pwsh -ErrorAction Stop).Source
$suites = @(
    @{ attempt = '07-runtime-bindings'; method = 'ExperimentDispenserRuntimeValidation.Begin' },
    @{ attempt = '04-input'; method = 'ExperimentInputShakerValidation.Begin' },
    @{ attempt = '05-auto-f'; method = 'ExperimentDispenserPlayLoopValidation.Begin' },
    @{ attempt = '06-auto-g'; method = 'ExperimentDispenserPlayLoopValidation.BeginExplosive' },
    @{ attempt = '08-shard-motion'; method = 'ExperimentShardMotionValidation.Begin' }
)
foreach ($suite in $suites) {
    & $powershellPath -NoProfile -File $runnerPath -Attempt ('PrefabReadiness20261028/' + $suite.attempt) -Method $suite.method -TimeoutSeconds 240
    if ($LASTEXITCODE -ne 0) { throw ('Validation failed: ' + $suite.attempt) }
}
