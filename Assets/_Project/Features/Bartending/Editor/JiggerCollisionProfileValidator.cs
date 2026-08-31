using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Slainte.Bartending;
using UnityEditor;
using UnityEngine;

namespace Slainte.Editor
{
    public static class JiggerCollisionProfileValidator
    {
        private const string SpritePath =
            BartendingAssetPaths.ToolArtRoot + "jigger_front.png";
        private const string DefinitionPath =
            "Assets/Resources/Bartending/ToolCabinet/Jigger.asset";
        private const string SettingsPath =
            "Assets/Resources/Bartending/BusinessBartendingSettings.asset";
        private const float FloatTolerance = 0.01f;

        [MenuItem("Tools/Slainte/Validate 30 ml Jigger Collision Profile")]
        public static void ValidateFromMenu()
        {
            ValidateAll();
            Debug.Log(
                "[JiggerCollisionProfileValidator] 30 ml sprite collision profile passed.");
        }

        public static void RunCommandLine()
        {
            try
            {
                ValidateAll();
                Debug.Log(
                    "[JiggerCollisionProfileValidator] 30 ml sprite collision profile passed.");
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
            JiggerCollisionProfileDefinition profile =
                JiggerCollisionProfiles.Standard30Ml;
            Require(profile.IsValid(out string error), error);
            Require(
                profile.SourcePixelSize == JiggerCollisionProfiles.SourcePixelSize,
                "Jigger profile must retain the 310 x 590 source canvas.");
            Require(
                Mathf.Approximately(
                    profile.CapacityMl,
                    JiggerCollisionProfiles.FixedCapacityMl),
                "Jigger profile must remain fixed at 30 ml.");

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            Require(sprite != null && sprite.texture != null,
                "Jigger collision reference sprite is missing: " + SpritePath);
            Require(
                sprite.texture.width == profile.SourcePixelSize.x
                    && sprite.texture.height == profile.SourcePixelSize.y
                    && sprite.rect.size == profile.SourcePixelSize,
                "Jigger collision reference must retain the full 310 x 590 canvas.");

            ValidateEdgeAgainstSourcePixels(profile);
            for (int i = 0; i < profile.ContentTriggerPixels.Count; i++)
            {
                ValidateTriggerInsideOpenEdge(
                    profile,
                    profile.ContentTriggerPixels[i],
                    i);
            }
            ValidateFactoryOutput(profile, sprite);
        }

        private static void ValidateFactoryOutput(
            JiggerCollisionProfileDefinition profile,
            Sprite expectedSprite)
        {
            ToolDef definition = AssetDatabase.LoadAssetAtPath<ToolDef>(DefinitionPath);
            BusinessBartendingSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessBartendingSettings>(SettingsPath);
            Require(definition != null, "Jigger definition is missing: " + DefinitionPath);
            Require(settings != null, "Business bartending settings are missing: " + SettingsPath);
            Require(definition.kind == ToolKind.Jigger,
                "Jigger definition has the wrong tool kind.");
            Require(
                Mathf.Approximately(
                    definition.primaryCapacityMl,
                    JiggerCollisionProfiles.FixedCapacityMl)
                    && definition.secondaryCapacityMl <= 0f,
                "Jigger definition must expose one fixed 30 ml capacity.");
            Require(definition.worldLayers != null
                    && definition.worldLayers.Length == 2
                    && definition.worldLayers[1] == expectedSprite,
                "Jigger front sprite is not the collision reference layer.");

            GameObject parent = new GameObject("JiggerCollisionValidationRoot");
            try
            {
                IBartendingItem item = ToolCabinetWorldFactory.CreateTool(
                    definition,
                    parent.transform,
                    settings,
                    0,
                    1f,
                    new ToolCabinetShiftState(),
                    out GameObject instance);
                Require(item is BeakerController && instance != null,
                    "Tool cabinet factory did not create the 30 ml jigger.");
                try
                {
                    Require(instance.name == "Jigger_30ml",
                        "Jigger runtime name still exposes a second capacity.");
                    JiggerMeasureController jigger =
                        instance.GetComponent<JiggerMeasureController>();
                    Require(jigger != null,
                        "Jigger measure controller was not created.");
                    Require(
                        Mathf.Approximately(
                            jigger.ActiveCapacityMl,
                            JiggerCollisionProfiles.FixedCapacityMl),
                        "Jigger runtime capacity is not fixed at 30 ml.");
                    Require(jigger.ActiveCollisionProfile == profile,
                        "Jigger did not receive its sprite collision profile.");
                    Require(jigger.CollisionVisual != null
                            && jigger.CollisionVisual.sprite == expectedSprite,
                        "Jigger collision profile uses the wrong renderer.");

                    ValidateRuntimeEdge(instance, jigger, profile);
                    ValidateRuntimeTriggers(instance, jigger, profile);
                    ValidateRuntimeInteraction(jigger, profile);
                    InvokeNonPublic(jigger, "Start");
                    ValidateCapacityStop(instance, jigger, profile);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static void ValidateRuntimeEdge(
            GameObject instance,
            JiggerMeasureController jigger,
            JiggerCollisionProfileDefinition profile)
        {
            EdgeCollider2D edge = instance.GetComponent<EdgeCollider2D>();
            Require(edge != null && edge.enabled && !edge.isTrigger,
                "Jigger physical edge collider is missing.");

            Vector2[] spritePoints = profile.BuildEdgePath(
                jigger.CollisionVisual.sprite);
            Require(edge.pointCount == spritePoints.Length,
                "Jigger physical edge collider has the wrong point count.");
            for (int i = 0; i < spritePoints.Length; i++)
            {
                Vector2 expected = instance.transform.InverseTransformPoint(
                    jigger.CollisionVisual.transform.TransformPoint(spritePoints[i]));
                Require(Vector2.Distance(expected, edge.points[i]) <= FloatTolerance,
                    "Jigger physical edge drifted at point " + i + ".");
            }

            BoxCollider2D[] rootBoxes = instance.GetComponents<BoxCollider2D>();
            for (int i = 0; i < rootBoxes.Length; i++)
            {
                Require(!rootBoxes[i].enabled,
                    "Legacy beaker box collider is still active on the jigger.");
            }
        }

        private static void ValidateRuntimeTriggers(
            GameObject instance,
            JiggerMeasureController jigger,
            JiggerCollisionProfileDefinition profile)
        {
            int triggerCount = 0;
            BoxCollider2D[] boxes = instance.GetComponentsInChildren<BoxCollider2D>(true);
            for (int i = 0; i < boxes.Length; i++)
            {
                BoxCollider2D box = boxes[i];
                if (box == null
                    || !box.name.StartsWith("__JiggerContentTrigger_", StringComparison.Ordinal))
                {
                    continue;
                }

                Require(box.enabled && box.isTrigger,
                    "Jigger content collider is not an active trigger.");
                triggerCount++;
            }
            Require(triggerCount == profile.ContentTriggerPixels.Count,
                "Jigger must create only the authored 30 ml content triggers.");
        }

        private static void ValidateRuntimeInteraction(
            JiggerMeasureController jigger,
            JiggerCollisionProfileDefinition profile)
        {
            Sprite sprite = jigger.CollisionVisual.sprite;
            Vector2 upperCup = jigger.CollisionVisual.transform.TransformPoint(
                PixelToSpriteLocal(profile, sprite, new Vector2(155f, 160f)));
            Vector2 lowerCup = jigger.CollisionVisual.transform.TransformPoint(
                PixelToSpriteLocal(profile, sprite, new Vector2(155f, 50f)));
            Vector2 transparentPadding = jigger.CollisionVisual.transform.TransformPoint(
                PixelToSpriteLocal(profile, sprite, new Vector2(20f, 300f)));
            Require(profile.ContainsInteractionPoint(
                    sprite,
                    jigger.CollisionVisual.transform,
                    upperCup),
                "Jigger upper cup is outside the interaction silhouette.");
            Require(profile.ContainsInteractionPoint(
                    sprite,
                    jigger.CollisionVisual.transform,
                    lowerCup),
                "Jigger lower visual is outside the interaction silhouette.");
            Require(!profile.ContainsInteractionPoint(
                    sprite,
                    jigger.CollisionVisual.transform,
                    transparentPadding),
                "Jigger transparent canvas padding is clickable.");
        }

        private static void ValidateCapacityStop(
            GameObject instance,
            JiggerMeasureController jigger,
            JiggerCollisionProfileDefinition profile)
        {
            Transform stopTransform = instance.transform.Find("__JiggerCapacityStop");
            BoxCollider2D stop = stopTransform != null
                ? stopTransform.GetComponent<BoxCollider2D>()
                : null;
            Require(stop != null && !stop.isTrigger,
                "Jigger 30 ml capacity stop is missing.");
            Require(!stop.gameObject.activeSelf,
                "Empty jigger capacity stop must start open.");

            Rect spriteRect = profile.BuildCapacityStop(jigger.CollisionVisual.sprite);
            Vector2 rootMin = instance.transform.InverseTransformPoint(
                jigger.CollisionVisual.transform.TransformPoint(spriteRect.min));
            Vector2 rootMax = instance.transform.InverseTransformPoint(
                jigger.CollisionVisual.transform.TransformPoint(spriteRect.max));
            Vector2 expectedCenter = (rootMin + rootMax) * 0.5f;
            Vector2 expectedSize = new Vector2(
                Mathf.Abs(rootMax.x - rootMin.x),
                Mathf.Abs(rootMax.y - rootMin.y));
            Require(Vector2.Distance(expectedCenter, stop.transform.localPosition)
                    <= FloatTolerance,
                "Jigger capacity stop is not aligned to the 30 ml rim.");
            Require(Vector2.Distance(expectedSize, stop.size) <= FloatTolerance,
                "Jigger capacity stop has the wrong sprite-relative size.");
        }

        private static void ValidateTriggerInsideOpenEdge(
            JiggerCollisionProfileDefinition profile,
            Rect trigger,
            int triggerIndex)
        {
            Vector2[] samples =
            {
                trigger.center,
                new Vector2(trigger.xMin, trigger.yMin),
                new Vector2(trigger.xMax, trigger.yMin),
                new Vector2(trigger.xMin, trigger.yMax),
                new Vector2(trigger.xMax, trigger.yMax)
            };
            for (int i = 0; i < samples.Length; i++)
            {
                Require(IsInsideOpenEdge(profile.EdgePathPixels, samples[i], 3f),
                    "Jigger content trigger " + triggerIndex
                        + " extends outside the 30 ml cup edge.");
            }
        }

        private static void ValidateEdgeAgainstSourcePixels(
            JiggerCollisionProfileDefinition profile)
        {
            byte[] bytes = File.ReadAllBytes(Path.GetFullPath(SpritePath));
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Require(source.LoadImage(bytes, false),
                    "Jigger source pixels could not be decoded.");
                Color32[] pixels = source.GetPixels32();
                IReadOnlyList<Vector2> edge = profile.EdgePathPixels;
                for (int segment = 0; segment < edge.Count - 1; segment++)
                {
                    Vector2 a = edge[segment];
                    Vector2 b = edge[segment + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                    for (int step = 0; step <= steps; step++)
                    {
                        Vector2 sample = Vector2.Lerp(a, b, step / (float)steps);
                        Require(HasVisiblePixelNear(
                                pixels,
                                source.width,
                                source.height,
                                sample,
                                3f),
                            "Jigger collision edge is more than 3 px from its sprite near "
                                + sample + ".");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static bool HasVisiblePixelNear(
            Color32[] pixels,
            int width,
            int height,
            Vector2 sample,
            float maximumDistance)
        {
            int radius = Mathf.CeilToInt(maximumDistance);
            float maximumDistanceSquared = maximumDistance * maximumDistance;
            for (int y = Mathf.Max(0, Mathf.RoundToInt(sample.y) - radius);
                y <= Mathf.Min(height - 1, Mathf.RoundToInt(sample.y) + radius);
                y++)
            {
                for (int x = Mathf.Max(0, Mathf.RoundToInt(sample.x) - radius);
                    x <= Mathf.Min(width - 1, Mathf.RoundToInt(sample.x) + radius);
                    x++)
                {
                    if ((new Vector2(x, y) - sample).sqrMagnitude
                            <= maximumDistanceSquared
                        && pixels[y * width + x].a >= 100)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsInsideOpenEdge(
            IReadOnlyList<Vector2> edge,
            Vector2 point,
            float tolerance)
        {
            float left = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            int intersections = 0;
            for (int i = 0; i < edge.Count - 1; i++)
            {
                Vector2 a = edge[i];
                Vector2 b = edge[i + 1];
                if (point.y < Mathf.Min(a.y, b.y) - tolerance
                    || point.y > Mathf.Max(a.y, b.y) + tolerance
                    || Mathf.Abs(a.y - b.y) <= 0.0001f)
                {
                    continue;
                }

                float x = Mathf.Lerp(a.x, b.x, Mathf.InverseLerp(a.y, b.y, point.y));
                left = Mathf.Min(left, x);
                right = Mathf.Max(right, x);
                intersections++;
            }
            return intersections >= 2
                && point.x >= left + tolerance
                && point.x <= right - tolerance;
        }

        private static Vector2 PixelToSpriteLocal(
            JiggerCollisionProfileDefinition profile,
            Sprite sprite,
            Vector2 pixel)
        {
            Bounds bounds = sprite.bounds;
            return new Vector2(
                Mathf.Lerp(
                    bounds.min.x,
                    bounds.max.x,
                    pixel.x / profile.SourcePixelSize.x),
                Mathf.Lerp(
                    bounds.min.y,
                    bounds.max.y,
                    pixel.y / profile.SourcePixelSize.y));
        }

        private static void InvokeNonPublic(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null,
                target.GetType().Name + "." + methodName + " was not found.");
            method.Invoke(target, null);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
