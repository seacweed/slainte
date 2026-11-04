$ErrorActionPreference = 'Stop'
$workspacePath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidencePath = [System.IO.Path]::GetFullPath((Join-Path $workspacePath 'FluidGpuExperimentEvidence'))
$targetPath = [System.IO.Path]::GetFullPath((Join-Path $evidencePath 'HarnessProject'))
if ((Split-Path -Parent $targetPath) -ne $evidencePath -or (Split-Path -Leaf $targetPath) -ne 'HarnessProject') {
    throw 'Retired project resolved outside the explicitly authorized target.'
}
if ((Get-Content -LiteralPath (Join-Path $PSScriptRoot '04-authoring/cleanup-authoring-result.txt') -Raw).Trim() -ne 'PASS') {
    throw 'Current isolated runtime regeneration must pass before removing old generated copies.'
}
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'retired-harness-manifest.json') -Raw | ConvertFrom-Json
if (-not $manifest.crc_ok -or -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'retired-harness-source.zip'))) {
    throw 'Verified retired-project source archive is required.'
}
foreach ($entry in $manifest.sources.PSObject.Properties) {
    $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $targetPath $entry.Name))
    if (-not $sourcePath.StartsWith($targetPath + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unsafe source archive manifest path.'
    }
    if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value.sha256) {
        throw ('Retired project source changed after archival: ' + $entry.Name)
    }
}
$allFiles = @(Get-ChildItem -LiteralPath $targetPath -Recurse -File)
if ($allFiles.Count -ne $manifest.files -or ($allFiles | Measure-Object Length -Sum).Sum -ne $manifest.bytes) {
    throw 'Retired project changed after archival.'
}
Remove-Item -LiteralPath $targetPath -Recurse -Force

# tmp contained five task-specific runner/source copies at the snapshot boundary.
# Remove only those exact archived files, retaining any newly created or changed file.
$before = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'before-manifest.json') -Raw | ConvertFrom-Json
$removedTemporary = @()
foreach ($entry in $before.PSObject.Properties) {
    if (-not $entry.Name.StartsWith('tmp/')) { continue }
    $temporaryPath = [System.IO.Path]::GetFullPath((Join-Path $workspacePath $entry.Name))
    $temporaryRoot = [System.IO.Path]::GetFullPath((Join-Path $workspacePath 'tmp'))
    if (-not $temporaryPath.StartsWith($temporaryRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unsafe temporary manifest path.'
    }
    if ((Test-Path -LiteralPath $temporaryPath -PathType Leaf) -and
        (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $entry.Value.sha256) {
        Remove-Item -LiteralPath $temporaryPath -Force
        $removedTemporary += $entry.Name
    }
}
@{ removedProject = $targetPath; removedProjectFiles = $manifest.files; removedProjectBytes = $manifest.bytes;
   removedTemporaryFiles = $removedTemporary; sourceArchivePreserved = $true; finished = (Get-Date).ToString('o') } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'generated-copies-removed.json') -Encoding utf8
Write-Output ('Removed retired generated project: ' + $manifest.files + ' files, ' + $manifest.bytes + ' bytes; archived tmp files: ' + $removedTemporary.Count)
