using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    /// <summary>
    /// Jigger collision data authored in source-image pixels with a bottom-left origin.
    /// The complete 310 x 590 canvas is retained so transparent padding and the
    /// runtime visual offset cannot silently move the collision geometry.
    /// </summary>
    public sealed class JiggerCollisionProfileDefinition
    {
        private readonly Vector2[] edgePathPixels;
        private readonly Rect[] contentTriggerPixels;
        private readonly Vector2[] interactionPolygonPixels;

        public JiggerCollisionProfileDefinition(
            string sourceSpriteName,
            float capacityMl,
            Vector2Int sourcePixelSize,
            float edgeRadiusPixels,
            Vector2[] edgePathPixels,
            Rect[] contentTriggerPixels,
            Vector2[] interactionPolygonPixels,
            Rect capacityStopPixels)
        {
            SourceSpriteName = sourceSpriteName?.Trim() ?? string.Empty;
            CapacityMl = Mathf.Max(0f, capacityMl);
            SourcePixelSize = sourcePixelSize;
            EdgeRadiusPixels = Mathf.Max(0f, edgeRadiusPixels);
            this.edgePathPixels = edgePathPixels ?? Array.Empty<Vector2>();
            this.contentTriggerPixels = contentTriggerPixels ?? Array.Empty<Rect>();
            this.interactionPolygonPixels = interactionPolygonPixels ?? Array.Empty<Vector2>();
            CapacityStopPixels = capacityStopPixels;
        }

        public string SourceSpriteName { get; }
        public float CapacityMl { get; }
        public Vector2Int SourcePixelSize { get; }
        public float EdgeRadiusPixels { get; }
        public IReadOnlyList<Vector2> EdgePathPixels => edgePathPixels;
        public IReadOnlyList<Rect> ContentTriggerPixels => contentTriggerPixels;
        public IReadOnlyList<Vector2> InteractionPolygonPixels => interactionPolygonPixels;
        public Rect CapacityStopPixels { get; }

        public Vector2[] BuildEdgePath(Sprite sprite)
        {
            return BuildPointPath(sprite, edgePathPixels);
        }

        public Vector2[] BuildInteractionPolygon(Sprite sprite)
        {
            return BuildPointPath(sprite, interactionPolygonPixels);
        }

        public Rect BuildContentTrigger(Sprite sprite, int index)
        {
            return BuildSpriteRect(sprite, contentTriggerPixels, index);
        }

        public Rect BuildCapacityStop(Sprite sprite)
        {
            return BuildSpriteRect(sprite, CapacityStopPixels);
        }

        public float GetEdgeRadiusLocal(Sprite sprite)
        {
            if (sprite == null || SourcePixelSize.x <= 0)
                return 0f;
            return sprite.bounds.size.x * EdgeRadiusPixels / SourcePixelSize.x;
        }

        public bool ContainsInteractionPoint(
            Sprite sprite,
            Transform visualTransform,
            Vector2 worldPoint)
        {
            if (sprite == null || visualTransform == null)
                return false;

            Vector2 point = visualTransform.InverseTransformPoint(worldPoint);
            Vector2[] polygon = BuildInteractionPolygon(sprite);
            bool inside = false;
            for (int i = 0, previous = polygon.Length - 1;
                i < polygon.Length;
                previous = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[previous];
                bool crosses = (a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y)
                        / (b.y - a.y) + a.x;
                if (crosses)
                    inside = !inside;
            }
            return inside;
        }

        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(SourceSpriteName))
            {
                error = "A source sprite name is required.";
                return false;
            }
            if (!Mathf.Approximately(CapacityMl, JiggerCollisionProfiles.FixedCapacityMl))
            {
                error = SourceSpriteName + " must remain a 30 ml jigger.";
                return false;
            }
            if (SourcePixelSize.x <= 0 || SourcePixelSize.y <= 0)
            {
                error = SourceSpriteName + " has an invalid source image size.";
                return false;
            }
            if (edgePathPixels.Length < 3)
            {
                error = SourceSpriteName + " needs at least three open-cup edge points.";
                return false;
            }
            if (contentTriggerPixels.Length == 0)
            {
                error = SourceSpriteName + " has no 30 ml content trigger.";
                return false;
            }
            if (interactionPolygonPixels.Length < 3)
            {
                error = SourceSpriteName + " has no interaction polygon.";
                return false;
            }

            for (int i = 0; i < edgePathPixels.Length; i++)
            {
                if (!ContainsSourcePixel(edgePathPixels[i]))
                {
                    error = SourceSpriteName + " has an edge point outside the source sprite.";
                    return false;
                }
            }
            for (int i = 0; i < interactionPolygonPixels.Length; i++)
            {
                if (!ContainsSourcePixel(interactionPolygonPixels[i]))
                {
                    error = SourceSpriteName
                        + " has an interaction point outside the source sprite.";
                    return false;
                }
            }
            if (!ValidateRects(contentTriggerPixels, "content trigger", out error)
                || !ValidateRect(CapacityStopPixels, "capacity stop", out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }

        private Vector2[] BuildPointPath(Sprite sprite, Vector2[] pixels)
        {
            if (sprite == null)
                return Array.Empty<Vector2>();

            Vector2[] result = new Vector2[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                result[i] = PixelToSpriteLocal(sprite, pixels[i]);
            return result;
        }

        private Rect BuildSpriteRect(Sprite sprite, Rect[] source, int index)
        {
            return sprite == null || index < 0 || index >= source.Length
                ? default
                : BuildSpriteRect(sprite, source[index]);
        }

        private Rect BuildSpriteRect(Sprite sprite, Rect pixels)
        {
            if (sprite == null)
                return default;

            Vector2 min = PixelToSpriteLocal(sprite, pixels.min);
            Vector2 max = PixelToSpriteLocal(sprite, pixels.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Vector2 PixelToSpriteLocal(Sprite sprite, Vector2 pixel)
        {
            Bounds bounds = sprite.bounds;
            return new Vector2(
                Mathf.Lerp(bounds.min.x, bounds.max.x, pixel.x / SourcePixelSize.x),
                Mathf.Lerp(bounds.min.y, bounds.max.y, pixel.y / SourcePixelSize.y));
        }

        private bool ContainsSourcePixel(Vector2 point)
        {
            return point.x >= 0f
                && point.x <= SourcePixelSize.x
                && point.y >= 0f
                && point.y <= SourcePixelSize.y;
        }

        private bool ValidateRects(Rect[] rects, string label, out string error)
        {
            for (int i = 0; i < rects.Length; i++)
            {
                if (!ValidateRect(rects[i], label, out error))
                    return false;
            }
            error = string.Empty;
            return true;
        }

        private bool ValidateRect(Rect rect, string label, out string error)
        {
            if (rect.width <= 0f
                || rect.height <= 0f
                || !ContainsSourcePixel(rect.min)
                || !ContainsSourcePixel(rect.max))
            {
                error = SourceSpriteName + " has an invalid " + label + ".";
                return false;
            }
            error = string.Empty;
            return true;
        }
    }

    public static class JiggerCollisionProfiles
    {
        public const float FixedCapacityMl = 30f;
        public static readonly Vector2Int SourcePixelSize = new(310, 590);

        public static readonly JiggerCollisionProfileDefinition Standard30Ml = new(
            "jigger_front",
            FixedCapacityMl,
            SourcePixelSize,
            2f,
            new[]
            {
                new Vector2(101f, 184f), new Vector2(103f, 178f),
                new Vector2(106f, 170f), new Vector2(110f, 160f),
                new Vector2(114f, 150f), new Vector2(118f, 140f),
                new Vector2(123f, 130f), new Vector2(128f, 120f),
                new Vector2(133f, 112f), new Vector2(139f, 108f),
                new Vector2(155f, 106f), new Vector2(171f, 108f),
                new Vector2(177f, 112f), new Vector2(182f, 120f),
                new Vector2(187f, 130f), new Vector2(192f, 140f),
                new Vector2(196f, 150f), new Vector2(200f, 160f),
                new Vector2(204f, 170f), new Vector2(207f, 178f),
                new Vector2(209f, 184f)
            },
            new[]
            {
                new Rect(112f, 170f, 85f, 10f),
                new Rect(117f, 150f, 75f, 20f),
                new Rect(126f, 130f, 57f, 20f),
                new Rect(136f, 113f, 38f, 17f)
            },
            new[]
            {
                new Vector2(136f, 0f), new Vector2(117f, 5f),
                new Vector2(106f, 10f), new Vector2(97f, 15f),
                new Vector2(90f, 20f), new Vector2(86f, 30f),
                new Vector2(86f, 40f), new Vector2(97f, 50f),
                new Vector2(101f, 60f), new Vector2(109f, 80f),
                new Vector2(116f, 100f), new Vector2(120f, 110f),
                new Vector2(118f, 125f), new Vector2(111f, 140f),
                new Vector2(104f, 160f), new Vector2(98f, 175f),
                new Vector2(86f, 185f), new Vector2(91f, 190f),
                new Vector2(224f, 190f),
                new Vector2(222f, 185f), new Vector2(210f, 175f),
                new Vector2(204f, 160f), new Vector2(196f, 140f),
                new Vector2(190f, 125f), new Vector2(188f, 110f),
                new Vector2(192f, 100f), new Vector2(199f, 80f),
                new Vector2(207f, 60f), new Vector2(212f, 50f),
                new Vector2(222f, 40f), new Vector2(221f, 30f),
                new Vector2(218f, 20f), new Vector2(203f, 10f),
                new Vector2(191f, 5f), new Vector2(173f, 0f)
            },
            new Rect(106f, 180f, 98f, 4f));
    }
}
