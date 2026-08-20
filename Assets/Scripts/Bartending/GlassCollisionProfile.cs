using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    /// <summary>
    /// Collision data authored in source-image pixels with a bottom-left origin.
    /// Pixel coordinates deliberately keep the complete 310 x 590 canvas, including
    /// transparent padding, so importer trimming can never silently change geometry.
    /// </summary>
    public sealed class GlassCollisionProfileDefinition
    {
        private readonly Vector2[] edgePathPixels;
        private readonly Rect[] contentTriggerPixels;
        private readonly Rect[] interactionRectPixels;

        public GlassCollisionProfileDefinition(
            string sourceSpriteName,
            string glassId,
            float capacityMl,
            Vector2Int sourcePixelSize,
            float visibleBottomPixel,
            Vector2[] edgePathPixels,
            Rect[] contentTriggerPixels,
            Rect[] interactionRectPixels)
        {
            SourceSpriteName = sourceSpriteName?.Trim() ?? string.Empty;
            GlassId = glassId?.Trim() ?? string.Empty;
            CapacityMl = Mathf.Max(0f, capacityMl);
            SourcePixelSize = sourcePixelSize;
            VisibleBottomPixel = visibleBottomPixel;
            this.edgePathPixels = edgePathPixels ?? Array.Empty<Vector2>();
            this.contentTriggerPixels = contentTriggerPixels ?? Array.Empty<Rect>();
            this.interactionRectPixels = interactionRectPixels ?? Array.Empty<Rect>();
        }

        public string SourceSpriteName { get; }
        public string GlassId { get; }
        public float CapacityMl { get; }
        public Vector2Int SourcePixelSize { get; }
        public float VisibleBottomPixel { get; }
        public IReadOnlyList<Vector2> EdgePathPixels => edgePathPixels;
        public IReadOnlyList<Rect> ContentTriggerPixels => contentTriggerPixels;
        public IReadOnlyList<Rect> InteractionRectPixels => interactionRectPixels;

        public Vector2[] BuildEdgePath(Sprite sprite)
        {
            if (sprite == null)
                return Array.Empty<Vector2>();

            Vector2[] result = new Vector2[edgePathPixels.Length];
            for (int i = 0; i < edgePathPixels.Length; i++)
                result[i] = PixelToSpriteLocal(sprite, edgePathPixels[i]);
            return result;
        }

        public float GetVisibleBottomLocalY(Sprite sprite)
        {
            return sprite == null
                ? 0f
                : PixelToSpriteLocal(sprite, new Vector2(0f, VisibleBottomPixel)).y;
        }

        public Rect BuildContentTrigger(Sprite sprite, int index)
        {
            return BuildSpriteRect(sprite, contentTriggerPixels, index);
        }

        public bool ContainsInteractionPoint(
            Sprite sprite,
            Transform visualTransform,
            Vector2 worldPoint)
        {
            if (sprite == null || visualTransform == null)
                return false;

            Vector2 local = visualTransform.InverseTransformPoint(worldPoint);
            for (int i = 0; i < interactionRectPixels.Length; i++)
            {
                Rect rect = BuildSpriteRect(sprite, interactionRectPixels, i);
                if (rect.Contains(local))
                    return true;
            }

            return false;
        }

        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(SourceSpriteName))
            {
                error = "A source sprite name is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(GlassId))
            {
                error = SourceSpriteName + " has no serving glass id.";
                return false;
            }

            if (CapacityMl <= 0f)
            {
                error = SourceSpriteName + " has no positive capacity.";
                return false;
            }

            if (SourcePixelSize.x <= 0 || SourcePixelSize.y <= 0)
            {
                error = SourceSpriteName + " has an invalid source image size.";
                return false;
            }

            if (VisibleBottomPixel < 0f || VisibleBottomPixel > SourcePixelSize.y)
            {
                error = SourceSpriteName + " has an invalid visible bottom pixel.";
                return false;
            }

            if (edgePathPixels.Length < 3)
            {
                error = SourceSpriteName + " needs at least three U-edge points.";
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

            if (contentTriggerPixels.Length == 0)
            {
                error = SourceSpriteName + " has no content trigger.";
                return false;
            }

            if (!ValidateRects(contentTriggerPixels, "content trigger", out error)
                || !ValidateRects(interactionRectPixels, "interaction rectangle", out error))
            {
                return false;
            }

            if (interactionRectPixels.Length == 0)
            {
                error = SourceSpriteName + " has no interaction rectangle.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private Rect BuildSpriteRect(Sprite sprite, Rect[] source, int index)
        {
            if (sprite == null || index < 0 || index >= source.Length)
                return default;

            Rect pixels = source[index];
            Vector2 min = PixelToSpriteLocal(sprite, pixels.min);
            Vector2 max = PixelToSpriteLocal(sprite, pixels.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Vector2 PixelToSpriteLocal(Sprite sprite, Vector2 pixel)
        {
            Bounds bounds = sprite.bounds;
            float normalizedX = pixel.x / SourcePixelSize.x;
            float normalizedY = pixel.y / SourcePixelSize.y;
            return new Vector2(
                Mathf.Lerp(bounds.min.x, bounds.max.x, normalizedX),
                Mathf.Lerp(bounds.min.y, bounds.max.y, normalizedY));
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
                Rect rect = rects[i];
                if (rect.width <= 0f
                    || rect.height <= 0f
                    || !ContainsSourcePixel(rect.min)
                    || !ContainsSourcePixel(rect.max))
                {
                    error = SourceSpriteName + " has an invalid " + label + ".";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
    }

    public static class GlassCollisionProfiles
    {
        public static readonly Vector2Int TemporarySourcePixelSize = new(310, 590);

        private static readonly GlassCollisionProfileDefinition[] profiles =
        {
            new(
                "200rock",
                "rock",
                200f,
                TemporarySourcePixelSize,
                2f,
                new[]
                {
                    new Vector2(33f, 247f), new Vector2(36f, 190f),
                    new Vector2(42f, 89f), new Vector2(45f, 30f),
                    new Vector2(54f, 9f), new Vector2(84f, 6f),
                    new Vector2(155f, 4f), new Vector2(219f, 6f),
                    new Vector2(255f, 9f), new Vector2(263f, 30f),
                    new Vector2(267f, 89f), new Vector2(273f, 190f),
                    new Vector2(276f, 247f)
                },
                new[] { new Rect(52f, 20f, 205f, 215f) },
                new[] { new Rect(25f, 0f, 260f, 255f) }),
            new(
                "200coc",
                "martini",
                200f,
                TemporarySourcePixelSize,
                9f,
                new[]
                {
                    new Vector2(10f, 341f), new Vector2(49f, 283f),
                    new Vector2(90f, 233f), new Vector2(127f, 193f),
                    new Vector2(154f, 159f), new Vector2(182f, 193f),
                    new Vector2(219f, 233f), new Vector2(260f, 283f),
                    new Vector2(298f, 341f)
                },
                new[]
                {
                    new Rect(50f, 290f, 210f, 35f),
                    new Rect(80f, 240f, 150f, 50f),
                    new Rect(115f, 200f, 80f, 40f),
                    new Rect(145f, 165f, 19f, 35f)
                },
                new[]
                {
                    new Rect(5f, 155f, 300f, 195f),
                    new Rect(145f, 20f, 20f, 145f),
                    new Rect(50f, 5f, 210f, 25f)
                }),
            new(
                "400high",
                "highball",
                400f,
                TemporarySourcePixelSize,
                2f,
                new[]
                {
                    new Vector2(62f, 386f), new Vector2(62f, 207f),
                    new Vector2(65f, 9f), new Vector2(155f, 2f),
                    new Vector2(244f, 9f), new Vector2(247f, 207f),
                    new Vector2(246f, 386f)
                },
                new[] { new Rect(70f, 20f, 170f, 355f) },
                new[] { new Rect(55f, 0f, 200f, 395f) }),
            new(
                "400hurricane",
                "hurricane",
                400f,
                TemporarySourcePixelSize,
                3f,
                new[]
                {
                    new Vector2(43f, 430f), new Vector2(55f, 404f),
                    new Vector2(69f, 369f), new Vector2(76f, 329f),
                    new Vector2(67f, 289f), new Vector2(51f, 249f),
                    new Vector2(50f, 209f), new Vector2(61f, 169f),
                    new Vector2(82f, 139f), new Vector2(126f, 109f),
                    new Vector2(154f, 104f), new Vector2(183f, 109f),
                    new Vector2(225f, 139f), new Vector2(247f, 169f),
                    new Vector2(258f, 209f), new Vector2(257f, 249f),
                    new Vector2(241f, 289f), new Vector2(232f, 329f),
                    new Vector2(239f, 369f), new Vector2(253f, 404f),
                    new Vector2(266f, 430f)
                },
                new[]
                {
                    new Rect(65f, 380f, 180f, 35f),
                    new Rect(85f, 330f, 140f, 50f),
                    new Rect(85f, 280f, 140f, 50f),
                    new Rect(65f, 230f, 180f, 50f),
                    new Rect(65f, 180f, 180f, 50f),
                    new Rect(85f, 140f, 140f, 40f),
                    new Rect(132f, 110f, 45f, 25f)
                },
                new[]
                {
                    new Rect(35f, 100f, 240f, 340f),
                    new Rect(125f, 20f, 60f, 95f),
                    new Rect(65f, 0f, 180f, 30f)
                })
        };

        public static IReadOnlyList<GlassCollisionProfileDefinition> All => profiles;

        public static bool TryGetBySpriteName(
            string spriteName,
            out GlassCollisionProfileDefinition profile)
        {
            string normalized = NormalizeSpriteName(spriteName);
            for (int i = 0; i < profiles.Length; i++)
            {
                if (string.Equals(
                        profiles[i].SourceSpriteName,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                {
                    profile = profiles[i];
                    return true;
                }
            }

            profile = null;
            return false;
        }

        public static bool TryGetByGlassId(
            string glassId,
            out GlassCollisionProfileDefinition profile)
        {
            string normalized = glassId?.Trim() ?? string.Empty;
            for (int i = 0; i < profiles.Length; i++)
            {
                if (string.Equals(
                        profiles[i].GlassId,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                {
                    profile = profiles[i];
                    return true;
                }
            }

            profile = null;
            return false;
        }

        private static string NormalizeSpriteName(string spriteName)
        {
            string normalized = spriteName?.Trim() ?? string.Empty;
            int extension = normalized.LastIndexOf('.');
            if (extension > 0)
                normalized = normalized.Substring(0, extension);
            return normalized;
        }
    }
}
