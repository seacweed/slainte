param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "RecipeColorMixer.html")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$indexPath = Join-Path $PSScriptRoot "index.html"
$stylesPath = Join-Path $PSScriptRoot "styles.css"
$dataPath = Join-Path $PSScriptRoot "recipe-data.js"
$mathPath = Join-Path $PSScriptRoot "color-math.js"
$appPath = Join-Path $PSScriptRoot "app.js"

$html = [System.IO.File]::ReadAllText($indexPath)
$styles = [System.IO.File]::ReadAllText($stylesPath)
$data = [System.IO.File]::ReadAllText($dataPath)
$math = [System.IO.File]::ReadAllText($mathPath)
$app = [System.IO.File]::ReadAllText($appPath)

$html = $html.Replace(
    '  <link rel="stylesheet" href="styles.css">',
    "  <style>`r`n$styles`r`n  </style>")
$html = $html.Replace('  <script src="recipe-data.js" defer></script>', "")
$html = $html.Replace('  <script src="color-math.js" defer></script>', "")
$html = $html.Replace('  <script src="app.js" defer></script>', "")

$inlineScripts = "  <script>`r`n$data`r`n$math`r`n$app`r`n  </script>"
$html = $html.Replace("</body>", "$inlineScripts`r`n</body>")

$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($OutputPath, $html, $utf8WithoutBom)

Write-Output "Generated standalone program: $OutputPath"
