using System.Collections.Generic;
using Slainte.Bartending.FluidGpuExperiment;
using UnityEngine;

namespace Slainte.Bartending
{
    // Draw scaled copies so the held body's collider, grip and liquid volume keep their authored size.
    internal sealed class CraftingReturnPreview
    {
        private const float PreviewScale = .86f;
        private const float FallbackOpacity = .4f;
        private readonly Transform parent;
        private readonly FluidExperimentWorld world;
        private readonly List<SpriteRenderer> scratch = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> originals = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> copies = new List<SpriteRenderer>();
        private int copiesInUse;
        private bool visible;

        public CraftingReturnPreview(Transform parent, FluidExperimentWorld world)
        {
            this.parent = parent;
            this.world = world;
        }

        public void Show(FluidExperimentBody held, Sprite shelfDisabledSprite = null)
        {
            if (held == null && !visible) return;
            visible = held != null;
            int previousCount = copiesInUse;
            copiesInUse = 0;
            foreach (var original in originals)
                if (original != null) original.forceRenderingOff = false;
            originals.Clear();

            if (held != null)
            {
                Vector3 pivot = held.transform.position;
                CopyArt(held, pivot, shelfDisabledSprite, fade: true);
                if (held.IsVessel)
                    foreach (var item in world.Items)
                        if (item != null && item.ContainingVessel == held) CopyArt(item, pivot);
                world.Liquid.SetReturnPreview(held.Id, pivot, PreviewScale);
            }
            else
            {
                world.Liquid.SetReturnPreview(0, Vector2.zero, 1);
            }
            for (int i = copiesInUse; i < previousCount; i++)
                if (copies[i] != null) copies[i].forceRenderingOff = true;
        }

        private void CopyArt(FluidExperimentBody body, Vector3 pivot, Sprite replacement = null, bool fade = false)
        {
            body.GetComponentsInChildren(false, scratch);
            bool drewReplacement = false;
            foreach (var source in scratch)
            {
                if (!source.enabled || source.forceRenderingOff || source.sprite == null) continue;
                if (!drewReplacement) DrawSprite(source, pivot, replacement, fade);
                drewReplacement = replacement != null;
                source.forceRenderingOff = true;
                originals.Add(source);
            }
        }

        private void DrawSprite(SpriteRenderer source, Vector3 pivot, Sprite replacement, bool fade)
        {
            if (copiesInUse == copies.Count)
            {
                var copyArt = new GameObject("ShelfReturnPreview") { hideFlags = HideFlags.DontSave };
                copyArt.transform.SetParent(parent, false);
                copies.Add(copyArt.AddComponent<SpriteRenderer>());
            }

            var copy = copies[copiesInUse++];
            var pose = source.transform;
            Vector3 position = Vector3.Lerp(pivot, pose.position, PreviewScale);
            float scale = PreviewScale;
            Color color = source.color;
            if (replacement != null)
            {
                // Shelf artwork can use a different sprite frame and pivot. Match the held art's height and center.
                Bounds from = source.sprite.bounds;
                Bounds to = replacement.bounds;
                float ratio = from.size.y / Mathf.Max(.0001f, to.size.y);
                Vector3 centerOffset = from.center - to.center * ratio;
                if (source.flipX) centerOffset.x = -centerOffset.x;
                if (source.flipY) centerOffset.y = -centerOffset.y;
                position += pose.TransformVector(centerOffset) * PreviewScale;
                scale *= ratio;
                color = Color.white; // The dedicated sprite already contains its opacity.
            }
            else if (fade)
            {
                color.a *= FallbackOpacity;
            }

            copy.gameObject.layer = source.gameObject.layer;
            copy.sprite = replacement != null ? replacement : source.sprite;
            copy.color = color;
            copy.sharedMaterial = source.sharedMaterial;
            copy.flipX = source.flipX;
            copy.flipY = source.flipY;
            copy.sortingLayerID = source.sortingLayerID;
            copy.sortingOrder = source.sortingOrder;
            copy.transform.SetPositionAndRotation(position, pose.rotation);
            copy.transform.localScale = pose.lossyScale * scale;
            copy.forceRenderingOff = false;
        }
    }
}
