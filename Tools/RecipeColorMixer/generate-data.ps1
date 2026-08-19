param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture
$numberPattern = '[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?'

function ConvertFrom-UnityScalar {
    param([string]$Value)

    if ($null -eq $Value) {
        return ""
    }

    $trimmed = $Value.Trim()
    if ($trimmed.Length -ge 2 -and $trimmed.StartsWith('"') -and $trimmed.EndsWith('"')) {
        $trimmed = $trimmed.Substring(1, $trimmed.Length - 2)
    }

    return [System.Text.RegularExpressions.Regex]::Unescape($trimmed)
}

function Get-UnityScalar {
    param(
        [string[]]$Lines,
        [string]$Field
    )

    $escapedField = [System.Text.RegularExpressions.Regex]::Escape($Field)
    foreach ($line in $Lines) {
        if ($line -match "^  ${escapedField}:\s?(.*)$") {
            return ConvertFrom-UnityScalar $Matches[1]
        }
    }

    return ""
}

function ConvertTo-InvariantDouble {
    param(
        [string]$Value,
        [double]$Fallback = 0
    )

    $parsed = 0.0
    if ([double]::TryParse(
        $Value,
        [System.Globalization.NumberStyles]::Float,
        $invariantCulture,
        [ref]$parsed)) {
        return $parsed
    }

    return $Fallback
}

function Get-RelativeSourcePath {
    param([string]$FullPath)

    $basePath = $RepositoryRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $baseUri = [Uri]::new($basePath)
    $fileUri = [Uri]::new($FullPath)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($fileUri).ToString())
}

function Read-ItemDefinitions {
    $itemsFolder = Join-Path $RepositoryRoot "Assets\Resources\Items"
    $itemsById = @{}

    if (-not (Test-Path -LiteralPath $itemsFolder)) {
        return $itemsById
    }

    $colorRegex = "^  liquidColor: \{r: ($numberPattern), g: ($numberPattern), b: ($numberPattern), a: ($numberPattern)\}$"
    foreach ($file in Get-ChildItem -LiteralPath $itemsFolder -Recurse -File -Filter "*.asset") {
        $lines = @(Get-Content -LiteralPath $file.FullName -Encoding UTF8)
        if (-not ($lines -match '^  m_EditorClassIdentifier: Assembly-CSharp::ItemDef$')) {
            continue
        }

        $id = Get-UnityScalar $lines "id"
        if ([string]::IsNullOrWhiteSpace($id)) {
            continue
        }

        $displayName = Get-UnityScalar $lines "displayName"
        $color = [ordered]@{ r = 1.0; g = 1.0; b = 1.0; a = 1.0 }
        foreach ($line in $lines) {
            $match = [regex]::Match($line, $colorRegex)
            if (-not $match.Success) {
                continue
            }

            $color = [ordered]@{
                r = ConvertTo-InvariantDouble $match.Groups[1].Value 1
                g = ConvertTo-InvariantDouble $match.Groups[2].Value 1
                b = ConvertTo-InvariantDouble $match.Groups[3].Value 1
                a = ConvertTo-InvariantDouble $match.Groups[4].Value 1
            }
            break
        }

        $itemsById[$id] = [ordered]@{
            id = $id
            displayName = $(if ([string]::IsNullOrWhiteSpace($displayName)) { $id } else { $displayName })
            color = $color
            source = Get-RelativeSourcePath $file.FullName
        }
    }

    return $itemsById
}

function Read-StreamingRecipes {
    $recipesById = @{}
    $dataFolder = Join-Path $RepositoryRoot "Assets\StreamingAssets\Data"
    $recipesPath = Join-Path $dataFolder "recipes.csv"
    $ingredientsPath = Join-Path $dataFolder "recipe_ingredients.csv"

    if (-not (Test-Path -LiteralPath $recipesPath) -or -not (Test-Path -LiteralPath $ingredientsPath)) {
        return $recipesById
    }

    $portionsByRecipe = @{}
    foreach ($row in Import-Csv -LiteralPath $ingredientsPath -Encoding UTF8) {
        $recipeId = [string]$row.recipeId
        if (-not $portionsByRecipe.ContainsKey($recipeId)) {
            $portionsByRecipe[$recipeId] = [System.Collections.Generic.List[object]]::new()
        }

        $portionsByRecipe[$recipeId].Add([ordered]@{
            itemId = [string]$row.ingredientId
            targetMl = ConvertTo-InvariantDouble ([string]$row.targetMl)
        })
    }

    foreach ($row in Import-Csv -LiteralPath $recipesPath -Encoding UTF8) {
        $id = [string]$row.id
        if ([string]::IsNullOrWhiteSpace($id) -or -not $portionsByRecipe.ContainsKey($id)) {
            continue
        }

        $recipesById[$id] = [ordered]@{
            id = $id
            displayName = $(if ([string]::IsNullOrWhiteSpace([string]$row.displayName)) { $id } else { [string]$row.displayName })
            ingredients = @($portionsByRecipe[$id])
            source = "Assets/StreamingAssets/Data/recipes.csv"
        }
    }

    return $recipesById
}

function Add-RecipeAssets {
    param([hashtable]$RecipesById)

    $recipesFolder = Join-Path $RepositoryRoot "Assets\Resources\Recipes"
    if (-not (Test-Path -LiteralPath $recipesFolder)) {
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $recipesFolder -Recurse -File -Filter "*.asset") {
        $lines = @(Get-Content -LiteralPath $file.FullName -Encoding UTF8)
        if (-not ($lines -match '^  m_EditorClassIdentifier: Assembly-CSharp::Slainte\.Bartending\.CocktailRecipeDef$')) {
            continue
        }

        if ((Get-UnityScalar $lines "isOrderable") -ne "1") {
            continue
        }

        $id = Get-UnityScalar $lines "id"
        if ([string]::IsNullOrWhiteSpace($id)) {
            continue
        }

        $portions = [System.Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt $lines.Count; $index++) {
            if ($lines[$index] -notmatch '^  - itemId:\s*(.+)$') {
                continue
            }

            $itemId = ConvertFrom-UnityScalar $Matches[1]
            $targetMl = 0.0
            for ($offset = 1; $offset -le 3 -and ($index + $offset) -lt $lines.Count; $offset++) {
                if ($lines[$index + $offset] -match '^    targetMl:\s*(.+)$') {
                    $targetMl = ConvertTo-InvariantDouble $Matches[1]
                    break
                }
            }

            $portions.Add([ordered]@{
                itemId = $itemId
                targetMl = $targetMl
            })
        }

        if ($portions.Count -eq 0) {
            continue
        }

        $displayName = Get-UnityScalar $lines "displayName"
        $RecipesById[$id] = [ordered]@{
            id = $id
            displayName = $(if ([string]::IsNullOrWhiteSpace($displayName)) { $id } else { $displayName })
            ingredients = @($portions)
            source = Get-RelativeSourcePath $file.FullName
        }
    }
}

$itemsById = Read-ItemDefinitions
$recipesById = Read-StreamingRecipes
Add-RecipeAssets $recipesById

$payload = [ordered]@{
    ingredients = @($itemsById.Values | Sort-Object @{ Expression = { $_.displayName } }, @{ Expression = { $_.id } })
    recipes = @($recipesById.Values | Sort-Object @{ Expression = { $_.displayName } }, @{ Expression = { $_.id } })
}

$json = $payload | ConvertTo-Json -Depth 8 -Compress
$outputPath = Join-Path $PSScriptRoot "recipe-data.js"
$javascript = "window.RECIPE_COLOR_DATA = $json;" + [Environment]::NewLine
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($outputPath, $javascript, $utf8WithoutBom)

Write-Output "Generated $outputPath"
Write-Output "Ingredients: $($payload.ingredients.Count)"
Write-Output "Recipes: $($payload.recipes.Count)"

$standaloneBuilder = Join-Path $PSScriptRoot "build-standalone.ps1"
if (Test-Path -LiteralPath $standaloneBuilder) {
    & $standaloneBuilder
}
