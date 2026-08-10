# Cocktail CSV Evaluation Handoff

Last updated: 2026-07-08

This document is for continuing the cocktail evaluation work from another Codex session or device.

## Goal

The current target is to support cocktail recipe evaluation in `Sample_Scene` before connecting it to the full business/customer flow.

The chosen direction is:

- Keep `ItemDef` as the runtime identity for bottles and liquid particles.
- Use CSV rows with `ingredientId` values that match `ItemDef.id`.
- Evaluate the actual remaining liquid in a submitted glass through `VesselLiquidTracker.BuildComposition()`.
- Test the flow in `Assets/Scenes/Sample_Scene.unity` with a simple key-driven tester.

## Current State

### Liquid Payload

Liquid particles carry semantic payload data.

Relevant files:

- `Assets/Scripts/Bartending/LiquidParticleData.cs`
- `Assets/MetaballFluid/Scripts/LiquidReaction.cs`
- `Assets/Scripts/Bartending/VesselLiquidTracker.cs`
- `Assets/Scripts/Bartending/BottleController.cs`

Important behavior:

- `BottleController` spawns pooled liquid particles and calls `LiquidParticleData.SetPayload(bottleData, 1f)`.
- Each particle payload contains one or more `LiquidPortion` entries.
- When particles mix, their payload ratios and visible colors are mixed together.
- A container does not own the liquid. The liquid remains as physical particles.
- `VesselLiquidTracker` tracks particles currently inside a glass/beaker trigger.
- `VesselLiquidTracker.BuildComposition()` returns a `CocktailComposition` grouped by `ItemDef`.

### Mixing Logic

`LiquidReaction` currently mixes particles through:

- weak passive mixing via `passiveMixSpeed`
- collision/contact mixing via `mixSpeed`
- relative-velocity agitation mixing via `GetRelativeVelocityMixStrength`

Important decision:

- Absolute-speed-only agitation was removed.
- Vessel acceleration/rotation based extra mixing was also removed.
- Moving a glass at a steady speed should not by itself trigger strong mixing.

Relevant code:

- `Assets/MetaballFluid/Scripts/LiquidReaction.cs`
  - `passiveMixSpeed`
  - `agitationVelocityThreshold`
  - `agitationFullMixRelativeSpeed`
  - `GetRelativeVelocityMixStrength`

### Vessel Tracking

`VesselLiquidTracker` is attached automatically at runtime by:

- `GlassController`
- `BeakerController`

It uses trigger colliders to find liquid particles. For robust evaluation, `BuildComposition()` refreshes the overlap state before aggregating.

It also has a debug view:

- Game view label with particle count, total ml, ingredient composition
- Gizmo trigger outlines and particle markers

Current limitation:

- Tracking precision depends on the trigger collider shape. For production, add a child trigger that better matches the actual interior of the glass/beaker if needed.

## CSV Evaluation Implementation

### Added Runtime Files

- `Assets/Scripts/Bartending/CsvTable.cs`
  - Small runtime CSV parser.
  - Handles quoted fields and comments.

- `Assets/Scripts/Bartending/ItemDefCatalog.cs`
  - Builds a lookup from `ItemDef.id` to `ItemDef`.
  - Loads `ItemDef` assets from `Resources.LoadAll<ItemDef>("Items")`.
  - Can also accept extra `ItemDef[]` from the tester inspector.

- `Assets/Scripts/Bartending/CocktailRecipeCatalog.cs`
  - Runtime recipe data classes:
    - `CocktailRecipe`
    - `CocktailRecipeIngredient`
    - `CocktailRecipeCatalog`

- `Assets/Scripts/Bartending/CocktailRecipeCsvLoader.cs`
  - Loads recipe CSV data from `Application.streamingAssetsPath/Data`.
  - Resolves recipe ingredient ids to `ItemDef` through `ItemDefCatalog`.

- `Assets/Scripts/Bartending/CocktailEvaluator.cs`
  - Evaluates a `CocktailComposition` against all loaded recipes.
  - Returns the best recipe result, score, success flag, ingredient deltas, extras, and failure reason.

- `Assets/Scripts/Bartending/CocktailEvaluationTester.cs`
  - Sample scene test component.
  - Loads CSV data on `Start`.
  - Press `Enter` to evaluate the current glass.
  - Shows result in Game view and logs it to Console.

### CSV Files

Current CSV files:

- `Assets/StreamingAssets/Data/ingredients.csv`
- `Assets/StreamingAssets/Data/recipes.csv`
- `Assets/StreamingAssets/Data/recipe_ingredients.csv`

Current sample data:

```csv
recipes.csv
id,displayName,minTotalMl,maxTotalMl,toleranceMl,allowExtraIngredients
vodka_lemon,Vodka Lemon,70,100,8,false
```

```csv
recipe_ingredients.csv
recipeId,ingredientId,targetMl,toleranceMl
vodka_lemon,breeze_vodka,50,8
vodka_lemon,lemon_juice,30,8
```

```csv
ingredients.csv
id,displayName,source
breeze_vodka,Breeze Vodka,Assets/Resources/Items/breeze_vodka.asset
lemon_juice,Lemon Juice,Assets/Resources/Items/lemon_juice.asset
```

Important limitation:

- `recipes.csv` and `recipe_ingredients.csv` are used by the runtime loader.
- `ingredients.csv` currently acts as a human-readable manifest/reference.
- Bottle/ingredient data is still actually supplied by `ItemDef` assets, matched by `ItemDef.id`.
- A future step should decide whether `ingredients.csv` should become an editor importer/sync source for `ItemDef` assets.

### Current Sample Scene Setup

`Assets/Scenes/Sample_Scene.unity` now has a root object:

- `CocktailEvaluationTester`

The tester:

- auto-finds a `GlassController` first
- falls back to any `VesselLiquidTracker`
- loads `Assets/StreamingAssets/Data/recipes.csv`
- loads `Assets/StreamingAssets/Data/recipe_ingredients.csv`
- loads `ItemDef` from `Assets/Resources/Items`
- evaluates on `Enter`

The sample scene already has bottle instances wired to:

- `Assets/Resources/Items/breeze_vodka.asset`
- `Assets/Resources/Items/lemon_juice.asset`

These ids match the sample recipe CSV.

## Evaluation Rules

The current evaluator checks:

- required ingredients exist
- ingredient volumes are within tolerance
- total volume is within min/max
- extra ingredients are allowed or within tolerance
- unresolved CSV ingredient ids fail the result

It always chooses the highest-scoring recipe, even when the final result is `Bad`.

This is intentional because failure debug output should still say which recipe the drink was closest to.

## How To Test

1. Open `Assets/Scenes/Sample_Scene.unity`.
2. Enter Play Mode.
3. Pour `breeze_vodka` and `lemon_juice` into a glass.
4. Press `Enter`.
5. Check the Game view label and Unity Console.

Expected success target:

- `breeze_vodka`: about `50 ml`
- `lemon_juice`: about `30 ml`
- total: `70-100 ml`
- tolerance: `8 ml`
- no extra ingredients

Note:

- Each spawned liquid particle currently represents `1 ml`.
- Evaluation looks at particles remaining in the glass at submit time.
- If liquid is spilled or poured out before submission, it should no longer count.

## Build Status

Last compile command:

```powershell
dotnet build Assembly-CSharp.csproj
```

Result:

- Build succeeds.
- Existing warnings remain from `RecipeSearchUI` unassigned serialized fields:
  - `OptionEntry.label`
  - `CategoryConfig.title`
  - `CategoryConfig.headerIcon`

Known note:

- `Assembly-CSharp.csproj` is Unity-generated. It was manually updated so `dotnet build` can see the new scripts, but Unity may regenerate it.

## Important Design Decisions

### Why `ItemDef.id` Matching

The project already uses key-based lookup patterns:

- `OrderTicketDatabase.FindByKey`
- `CustomerOrderDatabase.FindByKey`
- `CharacterDatabase.FindByKey`

The current cocktail system follows that style:

```text
CSV ingredientId -> ItemDef.id -> ItemDef -> CocktailComposition
```

This avoids rewriting:

- `BottleController`
- `LiquidParticleData`
- `VesselLiquidTracker`
- existing bottle prefabs/assets

### Why Not Direct CSV-Only Runtime Objects Yet

Direct CSV-only objects would force the liquid payload and bottle data flow to stop using `ItemDef`, or require another conversion layer everywhere.

For now, CSV is used for recipe/balance data and `ItemDef` remains the runtime identity.

## Known Gaps

1. `ingredients.csv` is not yet authoritative.
   - It does not update `ItemDef` assets.
   - It does not currently drive bottle sprites, prices, taste tags, ABV, capacity, or liquid color.

2. Sample scene testing is key-driven.
   - There is no real submit slot/button/customer handoff yet.

3. Evaluation result is not connected to `CraftingJudgeUI` or `EpisodeRunner`.
   - Current result only logs and draws debug UI.

4. Only one sample recipe exists.
   - Add more recipes once the CSV schema feels stable.

5. Recipe matching is simple.
   - It uses volume/tolerance/extra checks.
   - It does not yet score taste, ABV, category, garnish, process, shaking/stirring, or glass type.

6. Volume is particle-count based.
   - Each spawned particle is `1 ml`.
   - If pour rate or particle volume changes, recipe targets may need tuning.

## Recommended Next Work

### 1. Decide Ingredient CSV Authority

Choose one:

- Keep `ingredients.csv` as documentation only.
- Build an editor importer that creates/updates `ItemDef` assets from `ingredients.csv`.
- Build a runtime overlay that patches loaded `ItemDef` fields from `ingredients.csv`.

Recommended next step:

- Create an editor importer/sync tool for `ItemDef`.
- Keep runtime evaluation based on `ItemDef.id`.

### 2. Improve Sample Submission Flow

Replace or supplement the `Enter` key tester with an in-scene submit target:

- a submit slot
- a button
- or a customer/table trigger

Implementation shape:

```text
Glass/Beaker placed on submit slot
-> find VesselLiquidTracker
-> BuildComposition()
-> CocktailEvaluator.Evaluate()
-> show result
```

### 3. Connect Evaluation To Crafting Flow

Current manual crafting UI:

- `Assets/Scripts/Conversation/Episode/CraftingJudgeUI.cs`

Future connection:

```csharp
episodeRunner.NotifyCraftingCompleted(result.isSuccess);
```

Do this after submit flow is stable.

### 4. Add Recipe Debug UI

The result text is useful but crude. Add:

- matched recipe name
- Good/Bad state
- ingredient list with actual/target ml
- missing/extra indicators

This can stay as debug UI until the business scene UX is designed.

### 5. Add More Test Recipes

Add recipes that exercise edge cases:

- exact two-ingredient recipe
- recipe allowing extras
- recipe with very small tolerance
- recipe with same ingredients but different ratios
- empty/unknown ingredient failure case

## Files To Read First In A New Session

Read these in order:

1. `docs/liquid-payload-color-mixing-handoff.md`
2. `docs/cocktail-csv-evaluation-handoff.md`
3. `Assets/Scripts/Bartending/LiquidParticleData.cs`
4. `Assets/Scripts/Bartending/VesselLiquidTracker.cs`
5. `Assets/Scripts/Bartending/CocktailEvaluator.cs`
6. `Assets/Scripts/Bartending/CocktailEvaluationTester.cs`

## Quick Mental Model

```text
Bottle ItemDef
  -> spawned liquid particle payload
  -> particle mixing changes payload ratios/colors
  -> glass tracker aggregates remaining particles
  -> evaluator compares aggregated ItemDef volumes to recipe CSV
  -> sample tester prints Good/Bad result
```

