using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    /// <summary>
    /// Runtime-only collision data for the temporary glass drawings. Coordinates are
    /// normalized against the complete source sprite, so transparent padding and the
    /// original 310 x 590 image dimensions remain untouched.
    /// </summary>
    public sealed class GlassCollisionProfileDefinition
    {
        private readonly Vector2[] edgePathNormalized;
        private readonly Rect[] contentTriggersNormalized;

        public GlassCollisionProfileDefinition(
            string sourceSpriteName,
            string glassId,
            float capacityMl,
            Vector2Int sourcePixelSize,
            Vector2[] edgePathNormalized,
            Rect[] contentTriggersNormalized)
        {
            SourceSpriteName = sourceSpriteName?.Trim() ?? string.Empty;
            GlassId = glassId?.Trim() ?? string.Empty;
            CapacityMl = Mathf.Max(0f, capacityMl);
            SourcePixelSize = sourcePixelSize;
            this.edgePathNormalized = edgePathNormalized ?? Array.Empty<Vector2>();
            this.contentTriggersNormalized = contentTriggersNormalized ?? Array.Empty<Rect>();
        }

        public string SourceSpriteName { get; }
        public string GlassId { get; }
        public float CapacityMl { get; }
        public Vector2Int SourcePixelSize { get; }
        public IReadOnlyList<Vector2> EdgePathNormalized => edgePathNormalized;
        public IReadOnlyList<Rect> ContentTriggersNormalized => contentTriggersNormalized;

        public Vector2[] BuildEdgePath(Sprite sprite)
        {
            if (sprite == null)
                return Array.Empty<Vector2>();

            Vector2[] result = new Vector2[edgePathNormalized.Length];
            for (int i = 0; i < edgePathNormalized.Length; i++)
                result[i] = NormalizedToSpriteLocal(sprite, edgePathNormalized[i]);
            return result;
        }

        public Rect BuildContentTrigger(Sprite sprite, int index)
        {
            if (sprite == null || index < 0 || index >= contentTriggersNormalized.Length)
                return default;

            Rect normalized = contentTriggersNormalized[index];
            Vector2 min = NormalizedToSpriteLocal(sprite, normalized.min);
            Vector2 max = NormalizedToSpriteLocal(sprite, normalized.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
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

            if (edgePathNormalized.Length < 3)
            {
                error = SourceSpriteName + " needs at least three U-edge points.";
                return false;
            }

            for (int i = 0; i < edgePathNormalized.Length; i++)
            {
                Vector2 point = edgePathNormalized[i];
                if (point.x < 0f || point.x > 1f || point.y < 0f || point.y > 1f)
                {
                    error = SourceSpriteName + " has an edge point outside the source sprite.";
                    return false;
                }
            }

            if (contentTriggersNormalized.Length == 0)
            {
                error = SourceSpriteName + " has no content trigger.";
                return false;
            }

            for (int i = 0; i < contentTriggersNormalized.Length; i++)
            {
                Rect rect = contentTriggersNormalized[i];
                if (rect.width <= 0f
                    || rect.height <= 0f
                    || rect.xMin < 0f
                    || rect.yMin < 0f
                    || rect.xMax > 1f
                    || rect.yMax > 1f)
                {
                    error = SourceSpriteName + " has an invalid normalized content trigger.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static Vector2 NormalizedToSpriteLocal(Sprite sprite, Vector2 normalized)
        {
            Bounds bounds = sprite.bounds;
            return new Vector2(
                Mathf.Lerp(bounds.min.x, bounds.max.x, normalized.x),
                Mathf.Lerp(bounds.min.y, bounds.max.y, normalized.y));
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
                new[]
                {
                    new Vector2(0.108f, 0.414f),
                    new Vector2(0.116f, 0.290f),
                    new Vector2(0.145f, 0.086f),
                    new Vector2(0.270f, 0.063f),
                    new Vector2(0.500f, 0.060f),
                    new Vector2(0.710f, 0.063f),
                    new Vector2(0.835f, 0.086f),
                    new Vector2(0.858f, 0.290f),
                    new Vector2(0.888f, 0.414f)
                },
                new[]
                {
                    new Rect(0.165f, 0.082f, 0.655f, 0.305f)
                }),
            new(
                "200coc",
                "martini",
                200f,
                TemporarySourcePixelSize,
                new[]
                {
                    new Vector2(0.026f, 0.966f),
                    new Vector2(0.160f, 0.790f),
                    new Vector2(0.290f, 0.595f),
                    new Vector2(0.410f, 0.405f),
                    new Vector2(0.500f, 0.282f),
                    new Vector2(0.590f, 0.405f),
                    new Vector2(0.710f, 0.595f),
                    new Vector2(0.840f, 0.790f),
                    new Vector2(0.974f, 0.966f)
                },
                new[]
                {
                    new Rect(0.180f, 0.800f, 0.640f, 0.125f),
                    new Rect(0.300f, 0.620f, 0.400f, 0.180f),
                    new Rect(0.405f, 0.455f, 0.190f, 0.165f),
                    new Rect(0.482f, 0.330f, 0.036f, 0.125f)
                }),
            new(
                "400high",
                "highball",
                400f,
                TemporarySourcePixelSize,
                new[]
                {
                    new Vector2(0.198f, 0.653f),
                    new Vector2(0.199f, 0.350f),
                    new Vector2(0.210f, 0.016f),
                    new Vector2(0.500f, 0.012f),
                    new Vector2(0.790f, 0.016f),
                    new Vector2(0.799f, 0.350f),
                    new Vector2(0.800f, 0.653f)
                },
                new[]
                {
                    new Rect(0.230f, 0.040f, 0.540f, 0.590f)
                }),
            new(
                "400hurricane",
                "hurricane",
                400f,
                TemporarySourcePixelSize,
                new[]
                {
                    new Vector2(0.139f, 0.947f),
                    new Vector2(0.213f, 0.830f),
                    new Vector2(0.245f, 0.700f),
                    new Vector2(0.218f, 0.555f),
                    new Vector2(0.168f, 0.455f),
                    new Vector2(0.171f, 0.360f),
                    new Vector2(0.230f, 0.275f),
                    new Vector2(0.335f, 0.210f),
                    new Vector2(0.500f, 0.180f),
                    new Vector2(0.665f, 0.210f),
                    new Vector2(0.775f, 0.275f),
                    new Vector2(0.832f, 0.360f),
                    new Vector2(0.832f, 0.455f),
                    new Vector2(0.782f, 0.555f),
                    new Vector2(0.755f, 0.700f),
                    new Vector2(0.787f, 0.830f),
                    new Vector2(0.868f, 0.947f)
                },
                new[]
                {
                    new Rect(0.270f, 0.780f, 0.460f, 0.125f),
                    new Rect(0.285f, 0.610f, 0.430f, 0.170f),
                    new Rect(0.245f, 0.440f, 0.510f, 0.170f),
                    new Rect(0.285f, 0.300f, 0.430f, 0.140f),
                    new Rect(0.385f, 0.215f, 0.230f, 0.085f)
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
