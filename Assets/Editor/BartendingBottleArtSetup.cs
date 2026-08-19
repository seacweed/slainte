using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Slainte.Editor
{
    /// <summary>
    /// Applies only the bottle mappings approved in
    /// docs/bartending-art-data-mismatches.md. Missing art/data is intentional
    /// and is never filled by guessing from a similar item.
    /// </summary>
    public static class BartendingBottleArtSetup
    {
        private const string LiquorRoot = "Assets/Data/LiquorBottle";
        private const string PlanningItemRoot = "Assets/Resources/Items/Planning/";
        private const string BottleArtRoot = BartendingArtImportPostprocessor.BottleRoot;

        private readonly struct ArtMapping
        {
            public ArtMapping(string artName, params string[] liquorIds)
            {
                ArtName = artName;
                LiquorIds = liquorIds;
            }

            public string ArtName { get; }
            public string[] LiquorIds { get; }
        }

        private static readonly ArtMapping[] TripletMappings =
        {
            new ArtMapping("tropicaljuice", "tropical_juice", "item_1001"),
            new ArtMapping("siltrop", "siltrop", "item_1002"),
            new ArtMapping("syntheticlemon", "synthetic_lemon", "item_1003"),
            new ArtMapping("slop", "item_1005"),
            new ArtMapping("nanangna", "nanangna", "item_1007"),
            new ArtMapping("cotton", "cotton", "item_1008"),
            new ArtMapping("hectar", "hectar", "item_1009"),
            new ArtMapping("bless", "bless", "item_1010"),
            new ArtMapping("breezevodka", "breeze_vodka", "item_1011"),
            new ArtMapping("johnnydogs", "johnny_dogs", "item_1012"),
            new ArtMapping("burnhambourbon", "burnham_bourbon", "item_1013"),
            new ArtMapping("beatha", "beatha", "item_1014"),
            new ArtMapping("coffeepowder", "item_1025")
        };

        private static readonly HashSet<string> IntentionalDataOnlyIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "lemon_juice",
                "item_1023"
            };

        [MenuItem("Tools/Slainte/Apply Bartending Bottle Art")]
        public static void ApplyFromMenu()
        {
            ApplyAndValidate();
            Debug.Log("[BartendingArt] Applied shop=blank, shelf=lid, bar=base mappings without filling documented gaps.");
        }

        public static void RunCommandLineValidation()
        {
            try
            {
                ApplyAndValidate();
                GlassCollisionProfileValidator.ValidateAll();
                Debug.Log("[BartendingImplementation] Bottle art and temporary-glass E2E validation passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void ApplyAndValidate()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BartendingArtContractValidator.ValidateAllOrThrow();

            Dictionary<string, LiquorBottleDef> definitions = LoadLiquorDefinitions();
            Dictionary<string, ExpectedSprites> expected = BuildExpectedSprites();

            foreach (KeyValuePair<string, ExpectedSprites> pair in expected)
            {
                Require(definitions.TryGetValue(pair.Key, out LiquorBottleDef definition),
                    "LiquorBottleDef is missing for mapped id " + pair.Key + ".");
                ApplyContextSprites(definition, pair.Value);
            }

            foreach (string id in IntentionalDataOnlyIds)
            {
                Require(definitions.TryGetValue(id, out LiquorBottleDef definition),
                    "Documented data-only LiquorBottleDef is missing: " + id + ".");

                definition.useContextImages = true;
                definition.shopBlankSprite = null;
                definition.shelfLidSprite = null;
                definition.barSprite = null;
                EditorUtility.SetDirty(definition);
            }

            foreach (KeyValuePair<string, ExpectedSprites> pair in expected)
                ApplyItemIconIfPresent(pair.Key, pair.Value.Bar);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateMappings(definitions, expected);
        }

        private static Dictionary<string, ExpectedSprites> BuildExpectedSprites()
        {
            Dictionary<string, ExpectedSprites> result =
                new Dictionary<string, ExpectedSprites>(StringComparer.OrdinalIgnoreCase);

            foreach (ArtMapping mapping in TripletMappings)
            {
                ExpectedSprites sprites = new ExpectedSprites(
                    LoadSprite(mapping.ArtName + "_blank.png"),
                    LoadSprite(mapping.ArtName + "_lid.png"),
                    LoadSprite(mapping.ArtName + ".png"));

                foreach (string id in mapping.LiquorIds)
                    result.Add(id, sprites);
            }

            Sprite hotWater = LoadSprite("hotwater.png");
            result.Add("item_1024", new ExpectedSprites(hotWater, hotWater, hotWater));
            return result;
        }

        private static Dictionary<string, LiquorBottleDef> LoadLiquorDefinitions()
        {
            Dictionary<string, LiquorBottleDef> result =
                new Dictionary<string, LiquorBottleDef>(StringComparer.OrdinalIgnoreCase);

            foreach (string guid in AssetDatabase.FindAssets("t:LiquorBottleDef", new[] { LiquorRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                LiquorBottleDef definition = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(path);
                if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                    continue;

                Require(!result.ContainsKey(definition.id),
                    "Duplicate LiquorBottleDef id: " + definition.id + ".");
                result.Add(definition.id, definition);
            }

            return result;
        }

        private static void ApplyContextSprites(LiquorBottleDef definition, ExpectedSprites sprites)
        {
            definition.useContextImages = true;
            definition.shopBlankSprite = sprites.Shop;
            definition.shelfLidSprite = sprites.Shelf;
            definition.barSprite = sprites.Bar;
            EditorUtility.SetDirty(definition);
        }

        private static void ApplyItemIconIfPresent(string id, Sprite barSprite)
        {
            string path = id.StartsWith("item_", StringComparison.OrdinalIgnoreCase)
                ? PlanningItemRoot + id + ".asset"
                : "Assets/Resources/Items/" + id + ".asset";

            ItemDef item = AssetDatabase.LoadAssetAtPath<ItemDef>(path);
            if (item == null)
                return;

            item.icon = barSprite;
            EditorUtility.SetDirty(item);
        }

        private static void ValidateMappings(
            Dictionary<string, LiquorBottleDef> definitions,
            Dictionary<string, ExpectedSprites> expected)
        {
            foreach (KeyValuePair<string, ExpectedSprites> pair in expected)
            {
                LiquorBottleDef definition = definitions[pair.Key];
                Require(definition.useContextImages, pair.Key + " is not in strict context-image mode.");
                Require(definition.GetShopSprite() == pair.Value.Shop,
                    pair.Key + " does not use the blank image in the shop.");
                Require(definition.GetShelfSprite() == pair.Value.Shelf,
                    pair.Key + " does not use the lid image on the shelf.");
                Require(definition.GetBarSprite() == pair.Value.Bar,
                    pair.Key + " does not use the uncapped base image on the bar.");
            }

            foreach (string id in IntentionalDataOnlyIds)
            {
                LiquorBottleDef definition = definitions[id];
                Require(definition.useContextImages
                    && definition.GetShopSprite() == null
                    && definition.GetShelfSprite() == null
                    && definition.GetBarSprite() == null,
                    id + " must retain intentionally empty context-image fields.");
            }
        }

        private static Sprite LoadSprite(string file)
        {
            string path = BottleArtRoot + file;
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Require(sprite != null, "Bottle sprite failed to import: " + path + ".");
            return sprite;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private readonly struct ExpectedSprites
        {
            public ExpectedSprites(Sprite shop, Sprite shelf, Sprite bar)
            {
                Shop = shop;
                Shelf = shelf;
                Bar = bar;
            }

            public Sprite Shop { get; }
            public Sprite Shelf { get; }
            public Sprite Bar { get; }
        }
    }
}
