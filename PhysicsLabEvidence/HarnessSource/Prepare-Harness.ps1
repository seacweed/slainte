$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent | Split-Path -Parent
$project = Join-Path $workspace 'PhysicsLabEvidence/HarnessProject'
$source = Join-Path $workspace 'Assets/_Project/Features/Bartending/PhysicsLab'
foreach ($folder in @('Assets/Runtime','Assets/Editor','Assets/Shaders','Packages','ProjectSettings')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $project $folder) | Out-Null
}
Get-ChildItem -LiteralPath (Join-Path $source 'Runtime') -Filter '*.cs' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $project 'Assets/Runtime')
}
foreach ($name in @('BartendingPointerAnchor.cs','BartendingViewport.cs')) {
    Copy-Item -LiteralPath (Join-Path $workspace ('Assets/_Project/Features/Bartending/Runtime/' + $name)) -Destination (Join-Path $project 'Assets/Runtime')
}
Copy-Item -LiteralPath (Join-Path $source 'Shaders/PhysicsLabLiquid.compute') -Destination (Join-Path $project 'Assets/Shaders')
Get-ChildItem -LiteralPath (Join-Path $source 'Shaders') -Filter '*.hlsl' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $project 'Assets/Shaders')
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'IsolationHarness.cs') -Destination (Join-Path $project 'Assets/Editor')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ItemDef.cs') -Destination (Join-Path $project 'Assets/Runtime')
Copy-Item -LiteralPath (Join-Path $workspace 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings')
@'
{
"dependencies": {
  "com.unity.ugui": "2.0.0",
  "com.unity.inputsystem": "1.17.0",
  "com.unity.modules.physics2d": "1.0.0",
  "com.unity.modules.imgui": "1.0.0",
  "com.unity.modules.ui": "1.0.0"
}}
'@ | Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Encoding utf8
Write-Output "Prepared separate GPU harness at $project"
