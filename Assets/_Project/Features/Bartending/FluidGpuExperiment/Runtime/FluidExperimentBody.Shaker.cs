using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentShakerClosure { Open, Straining, Closed }

    public sealed partial class FluidExperimentBody
    {
        public FluidExperimentShakerPart ShakerStrainer { get; private set; }
        public FluidExperimentShakerPart ShakerCap { get; private set; }
        public bool HasStrainer => kind == LabItemKind.Shaker && ShakerStrainer != null && ShakerStrainer.IsAttached;
        public bool HasCap => HasStrainer && ShakerCap != null && ShakerCap.IsAttached;
        public bool BlocksIceMouth => kind == LabItemKind.Shaker ? HasStrainer : sealedVessel;
        public FluidExperimentShakerClosure ShakerClosure => HasCap ? FluidExperimentShakerClosure.Closed
            : HasStrainer ? FluidExperimentShakerClosure.Straining : FluidExperimentShakerClosure.Open;
        public FluidExperimentHull[] ShakerStrainerLiquidHulls { get; private set; } = Array.Empty<FluidExperimentHull>();
        public Vector2 ShakerOutletLocal { get; private set; }
        public float ShakerOutletWidth { get; private set; }
        public Vector2[] LiquidInteriorPath => HasStrainer && !HasCap && strainingInterior.Length > 0
            ? strainingInterior : collisionProfile != null ? collisionProfile.interior : liquidWall;
        public event Action<FluidExperimentBody> ShakerClosureChanged;
        private Vector2[] strainingInterior = Array.Empty<Vector2>();
        private bool shakerPartsInitialized;
        private bool shakerPartsSuspended;

        // Build isolated parts from the existing two sprite layers. The shared art
        // and the original Bartending selection/slot controllers stay untouched.
        partial void InitializeShakerParts()
        {
            if (kind != LabItemKind.Shaker || shakerPartsInitialized) return;
            shakerPartsInitialized = true;
            if (capVisual != null) capVisual.SetActive(true);
            SpriteRenderer strainerArt = null, capArt = null;
            foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>(true))
            {
                string spriteName = renderer.sprite != null ? renderer.sprite.name : string.Empty;
                if (spriteName == "cobbler_strainer" || renderer.name == "ToolArt_4") strainerArt = renderer;
                if (spriteName == "cobbler_lid" || renderer.name == "ToolArt_5") capArt = renderer;
            }
            Rect strainerRect = SpritePartRect(strainerArt, new Rect(53, 271, 203, 162),
                new Rect(-.61f, -.14f, 1.22f, .96f));
            Rect capRect = SpritePartRect(capArt, new Rect(105, 370, 99, 78),
                new Rect(-.30f, .44f, .60f, .47f));
            // Compute guides before reparenting the original sprite transforms.
            BuildStrainerGeometry(strainerArt, strainerRect);
            ShakerStrainer = FluidExperimentShakerPart.Create(this,
                FluidExperimentShakerPartRole.Strainer, strainerArt, strainerRect);
            ShakerCap = FluidExperimentShakerPart.Create(this,
                FluidExperimentShakerPartRole.Cap, capArt, capRect);
            ShakerStrainer.AttachToBody();
            ShakerCap.AttachToStrainer();
        }

        private Rect SpritePartRect(SpriteRenderer renderer, Rect pixels, Rect fallback)
        {
            if (renderer == null || renderer.sprite == null) return fallback;
            Vector2 a = SpritePoint(renderer, new Vector2(pixels.xMin, pixels.yMin));
            Vector2 b = SpritePoint(renderer, new Vector2(pixels.xMax, pixels.yMax));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private Vector2 SpritePoint(SpriteRenderer renderer, Vector2 pixel)
        {
            Bounds bounds = renderer.sprite.bounds;
            Vector3 local = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, pixel.x / 310f),
                Mathf.Lerp(bounds.min.y, bounds.max.y, pixel.y / 590f), 0);
            return transform.InverseTransformPoint(renderer.transform.TransformPoint(local));
        }

        private void BuildStrainerGeometry(SpriteRenderer art, Rect fallback)
        {
            Vector2[] bowl = collisionProfile != null ? collisionProfile.interior : liquidWall;
            if (bowl == null || bowl.Length < 3) return;
            Vector2 Point(float x, float y) => art != null && art.sprite != null
                ? SpritePoint(art, new Vector2(x, y))
                : new Vector2(Mathf.Lerp(fallback.xMin, fallback.xMax, (x - 53f) / 203f),
                    Mathf.Lerp(fallback.yMin, fallback.yMax, (y - 271f) / 162f));
            Vector2[] left = { bowl[0], Point(67, 344), Point(91, 369), Point(121, 383), Point(121, 433) };
            Vector2[] right = { bowl[bowl.Length - 1], Point(243, 344), Point(219, 369), Point(189, 383), Point(189, 433) };
            var hulls = new List<FluidExperimentHull>();
            AddGuideHulls(left, hulls);
            AddGuideHulls(right, hulls);
            ShakerStrainerLiquidHulls = hulls.ToArray();
            ShakerOutletLocal = (left[left.Length - 1] + right[right.Length - 1]) * .5f;
            ShakerOutletWidth = Vector2.Distance(left[left.Length - 1], right[right.Length - 1]);
            var interior = new List<Vector2>();
            // Closing last -> first is the actual narrow outlet, not the old bowl rim.
            for (int i = left.Length - 1; i > 0; i--) interior.Add(left[i]);
            interior.AddRange(bowl);
            for (int i = 1; i < right.Length; i++) interior.Add(right[i]);
            strainingInterior = interior.ToArray();
        }

        private static void AddGuideHulls(Vector2[] guide, List<FluidExperimentHull> destination)
        {
            const float halfThickness = .018f;
            for (int i = 1; i < guide.Length; i++)
            {
                Vector2 a = guide[i - 1], b = guide[i];
                Vector2 delta = (b - a).normalized;
                Vector2 normal = new Vector2(-delta.y, delta.x) * halfThickness;
                destination.Add(new FluidExperimentHull { points = new[] { a - normal, b - normal, b + normal, a + normal } });
            }
        }

        partial void ApplyShakerSeal(bool value)
        {
            if (kind != LabItemKind.Shaker)
            {
                sealedVessel = value;
                if (capVisual != null) capVisual.SetActive(value);
                if (capCollider != null) capCollider.enabled = value;
                return;
            }
            SetShakerClosure(value ? FluidExperimentShakerClosure.Closed : FluidExperimentShakerClosure.Open);
        }

        public void CycleShakerClosure()
        {
            SetShakerClosure(ShakerClosure == FluidExperimentShakerClosure.Closed
                ? FluidExperimentShakerClosure.Straining : ShakerClosure == FluidExperimentShakerClosure.Straining
                    ? FluidExperimentShakerClosure.Open : FluidExperimentShakerClosure.Closed);
        }

        public void SetShakerClosure(FluidExperimentShakerClosure closure)
        {
            if (kind != LabItemKind.Shaker) return;
            InitializeShakerParts();
            World?.interactor?.ForgetShakerPart(ShakerCap);
            World?.interactor?.ForgetShakerPart(ShakerStrainer);
            ShakerStrainer.AttachToBody();
            ShakerCap.AttachToStrainer();
            if (closure == FluidExperimentShakerClosure.Straining)
                ShakerCap.ParkBesideOwner(new Vector2(1.1f, .3f));
            else if (closure == FluidExperimentShakerClosure.Open)
                ShakerStrainer.ParkBesideOwner(new Vector2(1.55f, .15f));
            RefreshShakerClosure();
        }

        internal void RefreshShakerClosure()
        {
            sealedVessel = HasCap;
            // CPU ice sees the assembled strainer; the GPU liquid separately sees
            // its side guides and open outlet when the cap is removed.
            if (capCollider != null) capCollider.enabled = HasStrainer;
            ShakerStrainer?.RefreshCarrierCollider();
            World?.RefreshCollisionPairs();
            ShakerClosureChanged?.Invoke(this);
        }

        partial void SuspendShakerParts()
        {
            if (!shakerPartsInitialized) return;
            shakerPartsSuspended = true;
            SuspendDetachedPart(ShakerCap);
            SuspendDetachedPart(ShakerStrainer);
        }

        private void SuspendDetachedPart(FluidExperimentShakerPart part)
        {
            if (part == null || part.IsAttached) return;
            if (part.IsHeld) part.Release(Vector2.zero);
            part.gameObject.SetActive(false);
        }

        partial void ResumeShakerParts()
        {
            if (!shakerPartsSuspended) return;
            shakerPartsSuspended = false;
            if (ShakerStrainer != null) ShakerStrainer.gameObject.SetActive(true);
            if (ShakerCap != null) ShakerCap.gameObject.SetActive(true);
        }

        private void OnDestroy()
        {
            // Detached parts live beside the owner in the world hierarchy.
            bool capOnDetachedStrainer = ShakerCap != null && ShakerStrainer != null
                && ShakerCap.transform.IsChildOf(ShakerStrainer.transform) && !ShakerStrainer.IsAttached;
            if (ShakerCap != null && !ShakerCap.transform.IsChildOf(transform) && !capOnDetachedStrainer)
                Destroy(ShakerCap.gameObject);
            if (ShakerStrainer != null && !ShakerStrainer.transform.IsChildOf(transform))
                Destroy(ShakerStrainer.gameObject);
        }
    }
}
