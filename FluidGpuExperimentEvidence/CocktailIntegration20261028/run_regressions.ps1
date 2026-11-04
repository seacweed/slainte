$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runnerPath = Join-Path $workspacePath 'FluidGpuExperimentEvidence/Run-FloatingContents.ps1'
$powershellPath = (Get-Command pwsh -ErrorAction Stop).Source
$integrationExit = Join-Path $PSScriptRoot '09-integration/exit.json'
$deadline = (Get-Date).AddMinutes(6)
while (-not (Test-Path -LiteralPath $integrationExit)) {
    if ((Get-Date) -gt $deadline) { throw 'Integration result did not arrive.' }
    Start-Sleep -Seconds 2
}
$integration = Get-Content -LiteralPath $integrationExit -Raw | ConvertFrom-Json
if ($integration.exitCode -ne 0) { throw 'Integration validation failed; regressions were not launched.' }
$suites = @(
    @{ attempt = '10-authoring'; method = 'CocktailSetupValidation.Begin' },
    @{ attempt = '11-input'; method = 'ExperimentInputShakerValidation.Begin' },
    @{ attempt = '12-runtime'; method = 'ExperimentDispenserRuntimeValidation.Begin' },
    @{ attempt = '13-auto-f'; method = 'ExperimentDispenserPlayLoopValidation.Begin' },
    @{ attempt = '14-auto-g'; method = 'ExperimentDispenserPlayLoopValidation.BeginExplosive' },
    @{ attempt = '15-shard-motion'; method = 'ExperimentShardMotionValidation.Begin' }
)
foreach ($suite in $suites) {
    & $powershellPath -NoProfile -File $runnerPath -Attempt ('CocktailIntegration20261028/' + $suite.attempt) -Method $suite.method -TimeoutSeconds 300
    if ($LASTEXITCODE -ne 0) { throw ('Validation failed: ' + $suite.attempt) }
}
