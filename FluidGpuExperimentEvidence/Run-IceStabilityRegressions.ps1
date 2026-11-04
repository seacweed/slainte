param([string]$Prefix = '20261021-ice-final')
$ErrorActionPreference = 'Stop'
# Each run owns one hidden Editor and records its real completion code. Keep the
# project serial so import/resource state cannot overlap between suites.
$suites = @(
    @('crowded-a', 'CrowdedIceValidation.Begin'),
    @('crowded-b', 'CrowdedIceValidation.Begin'),
    @('native', 'CrowdedIcePlayLoopValidation.Begin'),
    @('modes', 'FloatingContentsValidation.BeginModes'),
    @('moving-contact', 'ExperimentIceContactValidation.BeginMovingOnly'),
    @('contents', 'FloatingContentsValidation.Begin'),
    @('rest', 'FloatingContentsValidation.BeginRest'),
    @('garnish-lemon', 'FloatingContentsPlayLoopValidation.Begin'),
    @('garnish-nananga', 'FloatingContentsPlayLoopValidation.BeginNananga'),
    @('garnish-motion', 'ExperimentGarnishMotionValidation.Begin'),
    @('mixed-long', 'MixedFloatSettleValidation.Begin'),
    @('pickup', 'ExperimentGlassPickupLiquidValidation.Begin'),
    @('input', 'ExperimentInputShakerValidation.Begin'),
    @('dispenser', 'ExperimentDispenserValidation.Begin'),
    @('breakage', 'ExperimentGlassBreakageValidation.Begin')
)
$failedSuites = @()
foreach ($suite in $suites) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-FloatingContents.ps1') -Attempt ($Prefix + '-' + $suite[0]) -Method $suite[1] -TimeoutSeconds 300
    if ($LASTEXITCODE -ne 0) { $failedSuites += $suite[0] }
}
Write-Output ('Completed regression suites; failed: ' + ($failedSuites -join ', '))
if ($failedSuites.Count -gt 0) { exit 1 }
exit 0
