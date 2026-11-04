param([Parameter(Mandatory=$true)][string]$Prefix,[switch]$Full)
$ErrorActionPreference='Stop'
$workspacePath=Split-Path -Parent $PSScriptRoot
# This synchronous preparation must finish before any Editor owns the copy.
& python (Join-Path $PSScriptRoot 'HarnessSource/prepare_experiment.py') --target (Join-Path $workspacePath 'Library/FluidModelDHarness')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$suites=@(
    @('behavior','IceBuoyancyValidation.BeginSmooth'),
    @('low','CrowdedIceValidation.BeginLow'),
    @('pour','IceFirstPourValidation.Begin'),
    @('contents','FloatingContentsValidation.Begin')
)
if($Full){$suites+=@(
    @('modes','FloatingContentsValidation.BeginModes'),
    @('crowded','CrowdedIceValidation.Begin'),
    @('native-eight','CrowdedIcePlayLoopValidation.BeginEight'),
    @('native-twelve','CrowdedIcePlayLoopValidation.Begin'),
    @('garnish-lemon','FloatingContentsPlayLoopValidation.Begin'),
    @('garnish-nananga','FloatingContentsPlayLoopValidation.BeginNananga'),
    @('garnish-motion','ExperimentGarnishMotionValidation.Begin'),
    @('pickup','ExperimentGlassPickupLiquidValidation.Begin'),
    @('input','ExperimentInputShakerValidation.Begin'),
    @('dispenser','ExperimentDispenserValidation.Begin'),
    @('breakage','ExperimentGlassBreakageValidation.Begin'),
    @('moving-contact','ExperimentIceContactValidation.BeginMovingOnly')
)}
$failedSuites=@()
foreach($suite in $suites){
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-FloatingContents.ps1') -Attempt ($Prefix+'-'+$suite[0]) -Method $suite[1] -TimeoutSeconds 300
    if($LASTEXITCODE -ne 0){$failedSuites+=$suite[0]}
}
Write-Output ('Failed suites: '+($failedSuites -join ', '))
if($failedSuites.Count -gt 0){exit 1}
