using System;
using System.Collections.Generic;
using Slainte.Bartending;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Editor
{
    public static class WorldCapacityLabelValidator
    {
        private const int TestFontSize = 41;

        [MenuItem("Tools/Slainte/Validate World Capacity Labels")]
        public static void ValidateFromMenu()
        {
            ValidateAll();
            Debug.Log(
                "[WorldCapacityLabelValidator] Beaker-family label filtering and styling passed.");
        }

        public static void RunCommandLine()
        {
            try
            {
                ValidateAll();
                Debug.Log(
                    "[WorldCapacityLabelValidator] Beaker-family label filtering and styling passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void ValidateAll()
        {
            GameObject root = new GameObject(
                "WorldCapacityLabelValidation",
                typeof(RectTransform),
                typeof(Canvas));
            BusinessBartendingSettings settings =
                ScriptableObject.CreateInstance<BusinessBartendingSettings>();
            try
            {
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                RectTransform rootRect = root.GetComponent<RectTransform>();
                rootRect.sizeDelta = new Vector2(1920f, 1080f);

                settings.contentsLabelFont = TMP_Settings.defaultFontAsset;
                settings.contentsLabelFontSize = TestFontSize;

                GameObject viewportObject = new GameObject(
                    "WorldCapacityLabelViewport",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage),
                    typeof(BartendingViewport));
                viewportObject.transform.SetParent(root.transform, false);
                BartendingViewport viewport = viewportObject.GetComponent<BartendingViewport>();

                BeakerController beaker = CreateBeakerFamilyItem(root.transform, "Beaker", false, false);
                BeakerController jigger = CreateBeakerFamilyItem(root.transform, "Jigger_30ml", true, false);
                BeakerController shaker = CreateBeakerFamilyItem(root.transform, "CobblerShaker", false, true);
                GlassController glass = CreateGlass(root.transform);

                List<SlotController> slots = new()
                {
                    CreateOccupiedSlot(root.transform, "BeakerSlot", beaker),
                    CreateOccupiedSlot(root.transform, "JiggerSlot", jigger),
                    CreateOccupiedSlot(root.transform, "ShakerSlot", shaker),
                    CreateOccupiedSlot(root.transform, "GlassSlot", glass)
                };

                BartendingInteractionOverlay overlay =
                    BartendingInteractionOverlay.Create(
                        viewport,
                        null,
                        glass,
                        slots,
                        settings);
                Require(overlay != null, "World capacity overlay was not created.");
                Canvas.ForceUpdateCanvases();

                Require(overlay.VisibleVesselLabelCount == 3,
                    "Only beaker, 30 ml jigger, and cobbler shaker should have labels.");
                Require(overlay.TryGetVesselContentsText(beaker, out _),
                    "Standalone beaker capacity label is missing.");
                Require(overlay.TryGetVesselContentsText(jigger, out _),
                    "30 ml jigger capacity label is missing.");
                Require(overlay.TryGetVesselContentsText(shaker, out _),
                    "Cobbler shaker capacity label is missing.");
                Require(!overlay.TryGetVesselContentsText(glass, out _),
                    "Cocktail glass incorrectly received a world capacity label.");

                TMP_FontAsset expectedFont = settings.contentsLabelFont != null
                    ? settings.contentsLabelFont
                    : TMP_Settings.defaultFontAsset;
                TextMeshProUGUI[] texts = overlay.GetComponentsInChildren<TextMeshProUGUI>(true);
                int capacityTextCount = 0;
                for (int i = 0; i < texts.Length; i++)
                {
                    TextMeshProUGUI text = texts[i];
                    if (text == null
                        || text.transform.parent == null
                        || !text.transform.parent.name.StartsWith(
                            "VesselContents_",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    capacityTextCount++;
                    Require(text.font == expectedFont,
                        "Capacity label ignored the inspector-selected TMP font.");
                    Require(Mathf.Approximately(text.fontSize, TestFontSize),
                        "Capacity label ignored the inspector-selected font size.");
                }
                Require(capacityTextCount == 3,
                    "Exactly three beaker-family capacity text objects are required.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        private static BeakerController CreateBeakerFamilyItem(
            Transform parent,
            string name,
            bool addJigger,
            bool addShaker)
        {
            GameObject item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.AddComponent<EdgeCollider2D>();
            BoxCollider2D trigger = item.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            BeakerController beaker = item.AddComponent<BeakerController>();
            item.AddComponent<VesselLiquidTracker>();
            if (addJigger)
                item.AddComponent<JiggerMeasureController>();
            if (addShaker)
                item.AddComponent<CobblerShakerTechniqueController>();
            return beaker;
        }

        private static GlassController CreateGlass(Transform parent)
        {
            GameObject item = new GameObject("CocktailGlass");
            item.transform.SetParent(parent, false);
            item.AddComponent<EdgeCollider2D>();
            BoxCollider2D trigger = item.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            GlassController glass = item.AddComponent<GlassController>();
            item.AddComponent<VesselLiquidTracker>();
            return glass;
        }

        private static SlotController CreateOccupiedSlot(
            Transform parent,
            string name,
            IBartendingItem item)
        {
            GameObject slotObject = new GameObject(name);
            slotObject.transform.SetParent(parent, false);
            slotObject.AddComponent<SpriteRenderer>();
            BoxCollider2D collider = slotObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            SlotController slot = slotObject.AddComponent<SlotController>();
            slot.Occupy(item);
            return slot;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
