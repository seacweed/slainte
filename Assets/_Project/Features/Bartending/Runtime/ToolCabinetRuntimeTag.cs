using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    public sealed class ToolCabinetRuntimeTag : MonoBehaviour
    {
        public string DefinitionId { get; private set; } = string.Empty;
        public ToolKind? ToolKind { get; private set; }
        public bool IsGlass { get; private set; }
        public bool IsInCabinet { get; private set; }
        public SlotController CabinetSlot { get; private set; }

        private readonly List<Rigidbody2D> suspendedBodies = new();
        private readonly List<Collider2D> suspendedColliders = new();
        private readonly List<bool> colliderStates = new();
        private readonly List<Renderer> suspendedRenderers = new();
        private readonly List<bool> rendererStates = new();

        public void Configure(ToolDef definition)
        {
            DefinitionId = definition != null ? definition.StableId : string.Empty;
            ToolKind = definition != null ? definition.kind : null;
            IsGlass = false;
        }

        public void Configure(GlassDef definition)
        {
            DefinitionId = definition != null ? definition.StableId : string.Empty;
            ToolKind = null;
            IsGlass = true;
        }

        public void BindCabinetSlot(SlotController slot)
        {
            CabinetSlot = slot;
        }

        public bool Store(IBartendingItem item)
        {
            if (item == null
                || CabinetSlot == null
                || (CabinetSlot.IsOccupied
                    && !ReferenceEquals(CabinetSlot.OccupiedItem, item)))
            {
                return false;
            }

            CabinetSlot.Occupy(item);
            item.SnapToSlot(CabinetSlot.transform, CabinetSlot);
            SetSimulation(false);
            SetStoredPresentation(true);
            IsInCabinet = true;
            return true;
        }

        public bool TakeFromCabinet(IBartendingItem item)
        {
            if (item == null || CabinetSlot == null || !IsInCabinet)
                return false;

            if (ReferenceEquals(CabinetSlot.OccupiedItem, item))
                CabinetSlot.Vacate();

            SetStoredPresentation(false);
            SetSimulation(true);
            IsInCabinet = false;
            return true;
        }

        private void SetStoredPresentation(bool stored)
        {
            if (stored)
            {
                suspendedColliders.Clear();
                colliderStates.Clear();
                suspendedRenderers.Clear();
                rendererStates.Clear();
                CapturePresentation(gameObject);

                VesselLiquidTracker tracker = GetComponent<VesselLiquidTracker>();
                if (tracker != null)
                {
                    foreach (LiquidParticleData particle in tracker.Particles)
                    {
                        if (particle != null)
                            CapturePresentation(particle.gameObject);
                    }

                    foreach (IceCubeController ice in tracker.IceCubes)
                    {
                        if (ice != null)
                            CapturePresentation(ice.gameObject);
                    }
                }

                for (int i = 0; i < suspendedColliders.Count; i++)
                {
                    if (suspendedColliders[i] != null)
                        suspendedColliders[i].enabled = false;
                }
                for (int i = 0; i < suspendedRenderers.Count; i++)
                {
                    if (suspendedRenderers[i] != null)
                        suspendedRenderers[i].enabled = false;
                }
                return;
            }

            for (int i = 0; i < suspendedColliders.Count; i++)
            {
                if (suspendedColliders[i] != null)
                    suspendedColliders[i].enabled = colliderStates[i];
            }
            for (int i = 0; i < suspendedRenderers.Count; i++)
            {
                if (suspendedRenderers[i] != null)
                    suspendedRenderers[i].enabled = rendererStates[i];
            }
            suspendedColliders.Clear();
            colliderStates.Clear();
            suspendedRenderers.Clear();
            rendererStates.Clear();
        }

        private void CapturePresentation(GameObject root)
        {
            if (root == null)
                return;

            Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || suspendedColliders.Contains(collider))
                    continue;
                suspendedColliders.Add(collider);
                colliderStates.Add(collider.enabled);
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || suspendedRenderers.Contains(renderer))
                    continue;
                suspendedRenderers.Add(renderer);
                rendererStates.Add(renderer.enabled);
            }
        }

        private void SetSimulation(bool simulated)
        {
            VesselLiquidTracker tracker = GetComponent<VesselLiquidTracker>();
            GpuLiquidSystem.Instance?.SetVesselSuspended(tracker, !simulated);

            if (!simulated)
            {
                suspendedBodies.Clear();
                AddBodies(GetComponentsInChildren<Rigidbody2D>(true));

                if (tracker != null)
                {
                    foreach (LiquidParticleData particle in tracker.Particles)
                    {
                        if (particle != null)
                            AddBody(particle.GetComponent<Rigidbody2D>());
                    }

                    foreach (IceCubeController ice in tracker.IceCubes)
                    {
                        if (ice != null)
                            AddBody(ice.GetComponent<Rigidbody2D>());
                    }
                }

                for (int i = 0; i < suspendedBodies.Count; i++)
                {
                    Rigidbody2D body = suspendedBodies[i];
                    if (body == null)
                        continue;
                    body.linearVelocity = Vector2.zero;
                    body.angularVelocity = 0f;
                    body.simulated = false;
                }
                return;
            }

            for (int i = 0; i < suspendedBodies.Count; i++)
            {
                if (suspendedBodies[i] != null)
                    suspendedBodies[i].simulated = true;
            }
            suspendedBodies.Clear();
        }

        private void AddBodies(Rigidbody2D[] bodies)
        {
            for (int i = 0; i < bodies.Length; i++)
                AddBody(bodies[i]);
        }

        private void AddBody(Rigidbody2D body)
        {
            if (body != null && !suspendedBodies.Contains(body))
                suspendedBodies.Add(body);
        }

    }
}
