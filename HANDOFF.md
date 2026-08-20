# Slainte planning CSV / economy handoff

## Repository state

- Branch: `main`
- Base before this work: `cfc494f` (`everything good!`)
- Unity: 6.3 LTS (`6000.3.5f2`)
- Unity Play Mode was stopped manually before handoff.
- The commit containing this document is the handoff commit to pull on the next device.

## Implemented

- The three planning CSV files are now the canonical data source and are stored in `Assets/Editor/Data/Planning/`.
- `PlanningCsvAssetImporter` imports items, recipes, recipe ingredients, generated recipe variants, shelf definitions, shop data, and legacy shelf links.
- Recipe prices feed directly into completed business-order revenue.
- Normal customers default to regular money, while each order can select another payment currency such as Strange Coin.
- Money and Strange Coin are kept separate through order results, save data, settlement data/UI, wallets, and shop purchase paths.
- Zero-price shop products remain purchasable.
- `item_1005` is treated as slop until final slop data is supplied.
- Shelf liquor definitions resolve to production `ItemDef` inventory IDs. Fourteen legacy shelf assets are linked directly to their canonical planning items and retain compatibility aliases.
- Legacy inventory amounts are migrated to canonical item IDs.
- The liquor and delivery catalogs no longer contain the stale `item_1009` bottle GUID.
- Import/validation code checks item links, legacy shelf links, recipe data, payout behavior, currency separation, and zero-price purchasing.

## Important files

- `Assets/Editor/PlanningCsvAssetImporter.cs`
- `Assets/Editor/Data/Planning/items.csv`
- `Assets/Editor/Data/Planning/recipes.csv`
- `Assets/Editor/Data/Planning/recipe_ingredients.csv`
- `Assets/Scripts/LiquorShelf/LiquorBottleDef.cs`
- `Assets/Scripts/LiquorShelf/LiquorBottleSlotUI.cs`
- `Assets/Scripts/Bartending/BusinessBartendingBootstrap.cs`
- `Assets/Scripts/Business/BusinessOrderSessionController.cs`
- `Assets/Scripts/Business/BusinessSalePayoutPolicy.cs`
- `Assets/Scripts/Economy/`
- `Assets/Scripts/GameProgress.cs`
- `Assets/RestScene/Scripts/ShopUIManager.cs`

## Validation performed

- Runtime and editor C# project builds passed with zero errors in the final implementation pass.
- `Slainte > Bartending > Validate Business Shelf Spawn` passed on `BusinessScene`:
  - one shelf click spawned exactly one bottle;
  - a duplicate click was rejected.
- Static checks found all fourteen legacy shelf mappings linked and no dangling bottle references in the liquor/delivery catalogs.

## Follow-up checks

1. Pull this handoff commit and let Unity finish importing/compiling.
2. Run `Slainte > Bartending > Validate Planning Content Links`.
3. Run `Slainte > Bartending > Validate Business Shelf Spawn`.
4. In a clean Play Mode session, enter Crafting Mode, open a liquor category, and physically click a bottle. The handler test passes, but the final mouse/EventSystem path was interrupted before completion.
5. Complete one normal-money order and one Strange-Coin order and confirm the correct wallet changes immediately on completion.

## Known unrelated/runtime issues observed

- Stopping Play Mode during an active crafting order can log `MissingReferenceException` from `OrderTicketUI.BeginClose()` (`Assets/Scripts/OrderTicket/OrderTicketUI.cs:108`) after the ticket object is destroyed.
- The stress-test scene can log that the bartending session ended before crafting completed when Play Mode or script reload interrupts the session.
- URP may warn that it failed to create a material drawer for an `Enum`; this did not block the shelf validator.

