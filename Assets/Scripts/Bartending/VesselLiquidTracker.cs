using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Slainte.Bartending
{
    public sealed class CocktailComposition
    {
        private readonly Dictionary<ItemDef, float> volumes = new();
        private float thermalVolumeMl;
        private float weightedTemperature;
        private LiquidPayload finalColorPayload;
        private Color cachedFinalColor;
        private bool finalColorDirty = true;

        public IReadOnlyDictionary<ItemDef, float> Volumes => volumes;
        public float TotalVolumeMl { get; private set; }
        public float AverageTemperatureC => thermalVolumeMl > 0f
            ? weightedTemperature / thermalVolumeMl
            : 20f;
        public string GlassId { get; private set; } = string.Empty;
        public bool HasIce { get; private set; }
        public CocktailTechnique Techniques { get; private set; } = CocktailTechnique.None;

        public void Add(ItemDef item, float volumeMl)
        {
            if (item == null || volumeMl <= 0f)
                return;

            if (!volumes.ContainsKey(item))
                volumes.Add(item, 0f);

            volumes[item] += volumeMl;
            TotalVolumeMl += volumeMl;
            finalColorDirty = true;
        }

        public float GetVolume(ItemDef item)
        {
            return item != null && volumes.TryGetValue(item, out float volumeMl)
                ? volumeMl
                : 0f;
        }

        public void AddThermalSample(float volumeMl, float temperatureC)
        {
            if (volumeMl <= 0f)
                return;

            thermalVolumeMl += volumeMl;
            weightedTemperature += temperatureC * volumeMl;
        }

        public void RecordTechnique(CocktailTechnique technique)
        {
            Techniques |= technique;
        }

        public void SetServingStyle(string glassId, bool hasIce)
        {
            GlassId = glassId?.Trim() ?? string.Empty;
            HasIce = hasIce;
        }

        public CocktailTechnique GetEffectiveTechniques()
        {
            return Techniques == CocktailTechnique.None ? CocktailTechnique.Build : Techniques;
        }

        public Color EvaluateFinalColor()
        {
            if (!finalColorDirty)
                return cachedFinalColor;

            finalColorPayload ??= new LiquidPayload();
            finalColorPayload.portions.Clear();
            foreach (KeyValuePair<ItemDef, float> pair in volumes)
            {
                if (pair.Key == null || pair.Value <= 0f)
                    continue;

                finalColorPayload.portions.Add(new LiquidPortion
                {
                    sourceItem = pair.Key,
                    volumeMl = pair.Value
                });
            }

            cachedFinalColor = finalColorPayload.EvaluateColor();
            finalColorDirty = false;
            return cachedFinalColor;
        }
    }

    [RequireComponent(typeof(Collider2D))]
    public sealed class VesselLiquidTracker : MonoBehaviour
    {
        private static readonly HashSet<VesselLiquidTracker> activeVessels = new();
        private static readonly HashSet<LiquidParticleData> activeParticles = new();
        private static readonly HashSet<IceCubeController> activeIceCubes = new();
        private readonly HashSet<LiquidParticleData> particles = new();
        private readonly HashSet<LiquidParticleData> ownedParticles = new();
        private readonly HashSet<LiquidParticleData> pendingParticleReleases = new();
        private readonly HashSet<IceCubeController> iceCubes = new();
        private readonly HashSet<IceCubeController> ownedIceCubes = new();
        private readonly HashSet<IceCubeController> pendingIceReleases = new();
        private readonly List<Collider2D> overlapResults = new();
        private readonly List<LiquidParticleData> ownerReleaseBuffer = new();
        private readonly List<IceCubeController> iceReleaseBuffer = new();
        private readonly StringBuilder debugTextBuilder = new();
        private Collider2D[] colliders;
        private ContactFilter2D scanFilter;
        private GUIStyle debugBoxStyle;
        private string servingGlassId = string.Empty;
        private bool hasIce;
        private int interactionPriority;

        [Header("Debug View")]
        [SerializeField] private bool drawDebugGizmos = false;
        [SerializeField] private bool drawOnlyWhenSelected = false;
        [SerializeField] private bool drawRuntimeLabel = false;
        [SerializeField] private Color triggerDebugColor = new Color(0.15f, 0.85f, 1f, 0.8f);
        [SerializeField] private Color particleDebugColor = new Color(1f, 0.92f, 0.25f, 0.9f);
        [SerializeField] private Color connectionDebugColor = new Color(0.5f, 1f, 0.65f, 0.45f);
        [SerializeField, Min(0.01f)] private float debugParticleRadius = 0.06f;

        public int ParticleCount
        {
            get
            {
                Cleanup();
                RefreshTrackedParticles();
                return particles.Count;
            }
        }

        public IReadOnlyCollection<LiquidParticleData> Particles
        {
            get
            {
                Cleanup();
                RefreshTrackedParticles();
                return particles;
            }
        }

        public int IceCount
        {
            get
            {
                Cleanup();
                RefreshTrackedIceCubes();
                return iceCubes.Count;
            }
        }

        public IReadOnlyCollection<IceCubeController> IceCubes
        {
            get
            {
                Cleanup();
                RefreshTrackedIceCubes();
                return iceCubes;
            }
        }

        private void Awake()
        {
            CacheColliders();
            scanFilter = ContactFilter2D.noFilter;
            scanFilter.useTriggers = true;
        }

        internal int InteractionPriority => interactionPriority;
        internal static HashSet<LiquidParticleData> ActiveParticles => activeParticles;

        internal void SetInteractionPriority(int priority)
        {
            interactionPriority = priority;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInteractionRegistry()
        {
            activeVessels.Clear();
            activeParticles.Clear();
            activeIceCubes.Clear();
        }

        private void OnEnable()
        {
            RegisterVessel(this);
        }

        private void OnDisable()
        {
            ownerReleaseBuffer.Clear();
            foreach (LiquidParticleData particle in ownedParticles)
            {
                if (particle != null)
                    ownerReleaseBuffer.Add(particle);
            }

            for (int i = 0; i < ownerReleaseBuffer.Count; i++)
                ownerReleaseBuffer[i].ReleaseVesselOwner(this);

            ownerReleaseBuffer.Clear();
            ownedParticles.Clear();
            particles.Clear();
            pendingParticleReleases.Clear();

            iceReleaseBuffer.Clear();
            foreach (IceCubeController iceCube in ownedIceCubes)
            {
                if (iceCube != null)
                    iceReleaseBuffer.Add(iceCube);
            }

            for (int i = 0; i < iceReleaseBuffer.Count; i++)
                iceReleaseBuffer[i].ReleaseVesselOwner(this);

            iceReleaseBuffer.Clear();
            ownedIceCubes.Clear();
            iceCubes.Clear();
            pendingIceReleases.Clear();
            UnregisterVessel(this);
        }

        private void FixedUpdate()
        {
            ProcessPendingOwnerReleases();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Track(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            Track(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.TryGetComponent(out LiquidParticleData particle))
            {
                particles.Remove(particle);
                if (particle.VesselOwner == this)
                    pendingParticleReleases.Add(particle);
            }

            IceCubeController iceCube = other.GetComponentInParent<IceCubeController>();
            if (iceCube == null)
                return;

            iceCubes.Remove(iceCube);
            if (iceCube.VesselOwner == this)
                pendingIceReleases.Add(iceCube);
        }

        public CocktailComposition BuildComposition()
        {
            Cleanup();
            RefreshTrackedParticles();
            RefreshTrackedIceCubes();

            CocktailComposition composition = new CocktailComposition();
            composition.SetServingStyle(servingGlassId, hasIce || iceCubes.Count > 0);
            foreach (LiquidParticleData particle in particles)
            {
                if (particle == null || particle.payload == null)
                    continue;

                for (int i = 0; i < particle.payload.portions.Count; i++)
                {
                    LiquidPortion portion = particle.payload.portions[i];
                    composition.Add(portion.sourceItem, portion.volumeMl);
                }

                composition.AddThermalSample(
                    particle.payload.TotalVolumeMl,
                    particle.payload.temperatureC);
                composition.RecordTechnique(particle.payload.techniques);
            }

            return composition;
        }

        public void ConfigureServingStyle(string glassId, bool containsIce = false)
        {
            servingGlassId = glassId?.Trim() ?? string.Empty;
            hasIce = containsIce;
        }

        public void SetHasIce(bool value)
        {
            hasIce = value;
        }

        public void TranslateTrackedParticles(Vector2 delta)
        {
            if (delta.sqrMagnitude <= 0.000001f)
                return;

            Cleanup();
            RefreshTrackedParticles();

            foreach (LiquidParticleData particle in particles)
            {
                if (particle == null)
                    continue;

                Rigidbody2D particleBody = particle.GetComponent<Rigidbody2D>();
                if (particleBody != null)
                {
                    particleBody.position += delta;
                    particleBody.WakeUp();
                }
                else
                {
                    particle.transform.position += (Vector3)delta;
                }
            }

            TranslateTrackedIceCubes(delta);
        }

        private void TranslateTrackedIceCubes(Vector2 delta)
        {
            Cleanup();
            RefreshTrackedIceCubes();
            foreach (IceCubeController iceCube in iceCubes)
                iceCube?.Translate(delta);
        }

        private void Track(Collider2D other)
        {
            if (other == null)
                return;

            if (other.TryGetComponent(out LiquidParticleData particle))
                TrackParticle(particle);

            IceCubeController iceCube = other.GetComponentInParent<IceCubeController>();
            if (iceCube != null)
                TrackIceCube(iceCube);
        }

        private void TrackParticle(LiquidParticleData particle)
        {
            if (particle == null)
                return;

            if (particle.hasBeenCollected)
                return;

            pendingParticleReleases.Remove(particle);

            VesselLiquidTracker previousOwner = particle.VesselOwner;
            if (previousOwner != null
                && previousOwner != this
                && !previousOwner.ContainsTriggerPoint(particle.transform.position))
            {
                particle.ReleaseVesselOwner(previousOwner);
            }

            if (particle.VesselOwner == null)
                particle.TryAssignVesselOwner(FindPreferredOwner(particle.transform.position));

            if (particle.VesselOwner == this)
                particles.Add(particle);
        }

        private void TrackIceCube(IceCubeController iceCube)
        {
            if (iceCube == null || iceCube.IsDragging || !iceCube.gameObject.activeInHierarchy)
                return;

            pendingIceReleases.Remove(iceCube);

            VesselLiquidTracker previousOwner = iceCube.VesselOwner;
            if (previousOwner != null
                && previousOwner != this
                && !previousOwner.ContainsTriggerPoint(iceCube.PhysicsPosition))
            {
                iceCube.ReleaseVesselOwner(previousOwner);
            }

            if (iceCube.VesselOwner == null)
                iceCube.TryAssignVesselOwner(FindPreferredOwner(iceCube.PhysicsPosition));

            if (iceCube.VesselOwner == this)
                iceCubes.Add(iceCube);
        }

        private static VesselLiquidTracker FindPreferredOwner(Vector2 worldPoint)
        {
            VesselLiquidTracker preferred = null;
            foreach (VesselLiquidTracker vessel in activeVessels)
            {
                if (vessel == null
                    || !vessel.isActiveAndEnabled
                    || !vessel.ContainsTriggerPoint(worldPoint))
                {
                    continue;
                }

                if (preferred == null
                    || vessel.interactionPriority > preferred.interactionPriority
                    || (vessel.interactionPriority == preferred.interactionPriority
                        && vessel.GetInstanceID() > preferred.GetInstanceID()))
                {
                    preferred = vessel;
                }
            }

            return preferred;
        }

        private bool ContainsTriggerPoint(Vector2 worldPoint)
        {
            CacheColliders();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D trigger = colliders[i];
                if (trigger != null
                    && trigger.enabled
                    && trigger.isTrigger
                    && trigger.gameObject.activeInHierarchy
                    && trigger.OverlapPoint(worldPoint))
                {
                    return true;
                }
            }

            return false;
        }

        private void Cleanup()
        {
            particles.RemoveWhere(IsInvalidParticle);
            ownedParticles.RemoveWhere(IsInvalidParticle);
            pendingParticleReleases.RemoveWhere(
                particle => IsInvalidParticle(particle) || particle.VesselOwner != this);
            iceCubes.RemoveWhere(IsInvalidIceCube);
            ownedIceCubes.RemoveWhere(IsInvalidIceCube);
            pendingIceReleases.RemoveWhere(
                iceCube => IsInvalidIceCube(iceCube) || iceCube.VesselOwner != this);
        }

        private void ProcessPendingOwnerReleases()
        {
            Cleanup();

            ownerReleaseBuffer.Clear();
            foreach (LiquidParticleData particle in pendingParticleReleases)
            {
                if (particle.VesselOwner != this
                    || ContainsTriggerPoint(particle.transform.position))
                {
                    continue;
                }

                ownerReleaseBuffer.Add(particle);
            }

            for (int i = 0; i < ownerReleaseBuffer.Count; i++)
            {
                LiquidParticleData particle = ownerReleaseBuffer[i];
                pendingParticleReleases.Remove(particle);
                particle.ReleaseVesselOwner(this);
            }
            ownerReleaseBuffer.Clear();

            iceReleaseBuffer.Clear();
            foreach (IceCubeController iceCube in pendingIceReleases)
            {
                if (iceCube.VesselOwner != this
                    || ContainsTriggerPoint(iceCube.PhysicsPosition))
                {
                    continue;
                }

                iceReleaseBuffer.Add(iceCube);
            }

            for (int i = 0; i < iceReleaseBuffer.Count; i++)
            {
                IceCubeController iceCube = iceReleaseBuffer[i];
                pendingIceReleases.Remove(iceCube);
                iceCube.ReleaseVesselOwner(this);
            }
            iceReleaseBuffer.Clear();
        }

        private void RefreshTrackedParticles()
        {
            particles.Clear();
            CacheColliders();

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D trigger = colliders[i];
                if (trigger == null || !trigger.enabled || !trigger.isTrigger)
                    continue;

                overlapResults.Clear();
                trigger.Overlap(scanFilter, overlapResults);

                for (int j = 0; j < overlapResults.Count; j++)
                    Track(overlapResults[j]);
            }

            overlapResults.Clear();
        }

        private void RefreshTrackedIceCubes()
        {
            iceCubes.Clear();

            foreach (IceCubeController ownedIce in ownedIceCubes)
            {
                if (ownedIce == null || ownedIce.IsDragging)
                    continue;

                if (ContainsTriggerPoint(ownedIce.PhysicsPosition))
                {
                    iceCubes.Add(ownedIce);
                }
                else
                {
                    pendingIceReleases.Add(ownedIce);
                }
            }

            foreach (IceCubeController iceCube in activeIceCubes)
            {
                if (iceCube != null
                    && !iceCube.IsDragging
                    && ContainsTriggerPoint(iceCube.PhysicsPosition))
                {
                    TrackIceCube(iceCube);
                }
            }
        }

        internal void RegisterOwnedParticle(LiquidParticleData particle)
        {
            if (particle != null)
                ownedParticles.Add(particle);
        }

        internal void UnregisterOwnedParticle(LiquidParticleData particle)
        {
            if (particle != null)
            {
                ownedParticles.Remove(particle);
                particles.Remove(particle);
                pendingParticleReleases.Remove(particle);
            }
        }

        internal void RegisterOwnedIceCube(IceCubeController iceCube)
        {
            if (iceCube != null)
                ownedIceCubes.Add(iceCube);
        }

        internal void UnregisterOwnedIceCube(IceCubeController iceCube)
        {
            if (iceCube == null)
                return;
            ownedIceCubes.Remove(iceCube);
            iceCubes.Remove(iceCube);
            pendingIceReleases.Remove(iceCube);
        }

        internal static void RegisterIceCube(IceCubeController iceCube)
        {
            if (iceCube == null || !activeIceCubes.Add(iceCube))
                return;

            RefreshIceIsolation(iceCube);
        }

        internal static void UnregisterIceCube(IceCubeController iceCube)
        {
            if (iceCube == null)
                return;

            Collider2D iceCollider = iceCube.PhysicsCollider;
            if (iceCollider != null)
            {
                foreach (IceCubeController otherIce in activeIceCubes)
                {
                    if (otherIce == null || otherIce == iceCube || otherIce.PhysicsCollider == null)
                        continue;

                    Physics2D.IgnoreCollision(iceCollider, otherIce.PhysicsCollider, false);
                }

                foreach (LiquidParticleData particle in activeParticles)
                {
                    if (particle != null && particle.ParticleCollider != null)
                        Physics2D.IgnoreCollision(iceCollider, particle.ParticleCollider, false);
                }

                foreach (VesselLiquidTracker vessel in activeVessels)
                    SetIceVesselCollision(iceCollider, vessel, false);
            }

            activeIceCubes.Remove(iceCube);
        }

        internal static void RegisterParticle(LiquidParticleData particle)
        {
            if (particle == null || !activeParticles.Add(particle))
                return;

            RefreshParticleIsolation(particle);
        }

        internal static void UnregisterParticle(LiquidParticleData particle)
        {
            if (particle == null)
                return;

            Collider2D particleCollider = particle.ParticleCollider;
            if (particleCollider != null)
            {
                foreach (LiquidParticleData other in activeParticles)
                {
                    if (other == null || other == particle || other.ParticleCollider == null)
                        continue;

                    Physics2D.IgnoreCollision(particleCollider, other.ParticleCollider, false);
                }

                foreach (VesselLiquidTracker vessel in activeVessels)
                    SetParticleVesselCollision(particleCollider, vessel, false);

                foreach (IceCubeController iceCube in activeIceCubes)
                {
                    if (iceCube != null && iceCube.PhysicsCollider != null)
                        Physics2D.IgnoreCollision(particleCollider, iceCube.PhysicsCollider, false);
                }
            }

            activeParticles.Remove(particle);
        }

        internal static void RefreshParticleIsolation(LiquidParticleData particle)
        {
            if (particle == null || !particle.isActiveAndEnabled)
                return;

            Collider2D particleCollider = particle.ParticleCollider;
            if (particleCollider == null)
                return;

            foreach (VesselLiquidTracker vessel in activeVessels)
            {
                bool ignoreVessel = particle.VesselOwner != null
                    && particle.VesselOwner != vessel;
                SetParticleVesselCollision(particleCollider, vessel, ignoreVessel);
            }

            foreach (LiquidParticleData other in activeParticles)
            {
                if (other == null || other == particle || other.ParticleCollider == null)
                    continue;

                bool ignoreParticle = particle.VesselOwner != null
                    && other.VesselOwner != null
                    && particle.VesselOwner != other.VesselOwner;
                Physics2D.IgnoreCollision(
                    particleCollider,
                    other.ParticleCollider,
                    ignoreParticle);
            }

            foreach (IceCubeController iceCube in activeIceCubes)
            {
                if (iceCube == null || iceCube.PhysicsCollider == null)
                    continue;

                bool ignoreIce = particle.VesselOwner != null
                    && iceCube.VesselOwner != null
                    && particle.VesselOwner != iceCube.VesselOwner;
                Physics2D.IgnoreCollision(
                    particleCollider,
                    iceCube.PhysicsCollider,
                    ignoreIce);
            }
        }

        internal static void RefreshIceIsolation(IceCubeController iceCube)
        {
            if (iceCube == null || !iceCube.isActiveAndEnabled)
                return;

            Collider2D iceCollider = iceCube.PhysicsCollider;
            if (iceCollider == null)
                return;

            foreach (VesselLiquidTracker vessel in activeVessels)
            {
                bool ignoreVessel = iceCube.VesselOwner != null
                    && iceCube.VesselOwner != vessel;
                SetIceVesselCollision(iceCollider, vessel, ignoreVessel);
            }

            foreach (IceCubeController otherIce in activeIceCubes)
            {
                if (otherIce == null || otherIce == iceCube || otherIce.PhysicsCollider == null)
                    continue;

                bool ignoreIce = iceCube.VesselOwner != null
                    && otherIce.VesselOwner != null
                    && iceCube.VesselOwner != otherIce.VesselOwner;
                Physics2D.IgnoreCollision(iceCollider, otherIce.PhysicsCollider, ignoreIce);
            }

            foreach (LiquidParticleData particle in activeParticles)
            {
                if (particle == null || particle.ParticleCollider == null)
                    continue;

                bool ignoreParticle = iceCube.VesselOwner != null
                    && particle.VesselOwner != null
                    && iceCube.VesselOwner != particle.VesselOwner;
                Physics2D.IgnoreCollision(iceCollider, particle.ParticleCollider, ignoreParticle);
            }
        }

        private static void RegisterVessel(VesselLiquidTracker vessel)
        {
            if (vessel == null || !activeVessels.Add(vessel))
                return;

            vessel.CacheColliders();
            foreach (LiquidParticleData particle in activeParticles)
            {
                if (particle == null || particle.ParticleCollider == null)
                    continue;

                bool ignoreVessel = particle.VesselOwner != null
                    && particle.VesselOwner != vessel;
                SetParticleVesselCollision(particle.ParticleCollider, vessel, ignoreVessel);
            }

            foreach (IceCubeController iceCube in activeIceCubes)
            {
                if (iceCube == null || iceCube.PhysicsCollider == null)
                    continue;

                bool ignoreVessel = iceCube.VesselOwner != null
                    && iceCube.VesselOwner != vessel;
                SetIceVesselCollision(iceCube.PhysicsCollider, vessel, ignoreVessel);
            }
        }

        private static void UnregisterVessel(VesselLiquidTracker vessel)
        {
            if (vessel == null)
                return;

            foreach (LiquidParticleData particle in activeParticles)
            {
                if (particle != null && particle.ParticleCollider != null)
                    SetParticleVesselCollision(particle.ParticleCollider, vessel, false);
            }

            foreach (IceCubeController iceCube in activeIceCubes)
            {
                if (iceCube != null && iceCube.PhysicsCollider != null)
                    SetIceVesselCollision(iceCube.PhysicsCollider, vessel, false);
            }

            activeVessels.Remove(vessel);
        }

        private static void SetParticleVesselCollision(
            Collider2D particleCollider,
            VesselLiquidTracker vessel,
            bool ignore)
        {
            if (particleCollider == null || vessel == null)
                return;

            vessel.CacheColliders();
            for (int i = 0; i < vessel.colliders.Length; i++)
            {
                Collider2D vesselCollider = vessel.colliders[i];
                if (vesselCollider == null || vesselCollider.isTrigger)
                    continue;

                bool iceOnlyBarrier = vesselCollider.GetComponent<IceOnlyVesselBarrier>() != null;
                Physics2D.IgnoreCollision(
                    particleCollider,
                    vesselCollider,
                    ignore || iceOnlyBarrier);
            }
        }

        private static void SetIceVesselCollision(
            Collider2D iceCollider,
            VesselLiquidTracker vessel,
            bool ignore)
        {
            if (iceCollider == null || vessel == null)
                return;

            vessel.CacheColliders();
            for (int i = 0; i < vessel.colliders.Length; i++)
            {
                Collider2D vesselCollider = vessel.colliders[i];
                if (vesselCollider == null || vesselCollider.isTrigger)
                    continue;

                Physics2D.IgnoreCollision(iceCollider, vesselCollider, ignore);
            }
        }

        internal void RefreshCollisionGeometry()
        {
            CacheColliders();

            foreach (LiquidParticleData particle in activeParticles)
                RefreshParticleIsolation(particle);

            foreach (IceCubeController iceCube in activeIceCubes)
                RefreshIceIsolation(iceCube);
        }

        private void CacheColliders()
        {
            colliders = GetComponentsInChildren<Collider2D>();
        }

        private static bool IsInvalidParticle(LiquidParticleData particle)
        {
            return particle == null
                || particle.hasBeenCollected
                || !particle.gameObject.activeInHierarchy;
        }

        private static bool IsInvalidIceCube(IceCubeController iceCube)
        {
            return iceCube == null || !iceCube.gameObject.activeInHierarchy;
        }

        public bool HasTriggerCollider()
        {
            if (colliders == null || colliders.Length == 0)
                CacheColliders();

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].isTrigger)
                    return true;
            }

            return false;
        }

        public void SetDebugViewEnabled(bool enabled)
        {
            drawDebugGizmos = enabled;
            drawRuntimeLabel = enabled;
        }

        private void OnDrawGizmos()
        {
            if (!drawDebugGizmos || drawOnlyWhenSelected)
                return;

            DrawDebugGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebugGizmos || !drawOnlyWhenSelected)
                return;

            DrawDebugGizmos();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !drawDebugGizmos || !drawRuntimeLabel)
                return;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            CocktailComposition composition = BuildComposition();
            Vector3 screenPosition = camera.WorldToScreenPoint(GetDebugLabelWorldPosition());
            if (screenPosition.z < 0f)
                return;

            EnsureDebugBoxStyle();

            const float width = 220f;
            string text = BuildDebugText(composition);
            GUIContent content = new GUIContent(text);
            float height = debugBoxStyle.CalcHeight(content, width);
            Rect rect = new Rect(
                screenPosition.x + 8f,
                Screen.height - screenPosition.y - height - 8f,
                width,
                height);

            GUI.Box(rect, content, debugBoxStyle);
        }

        private void DrawDebugGizmos()
        {
            CacheColliders();

            Color previousColor = Gizmos.color;

            Gizmos.color = triggerDebugColor;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.isTrigger)
                    continue;

                DrawColliderGizmo(collider);
            }

            if (Application.isPlaying)
            {
                RefreshTrackedParticles();
                Vector3 origin = GetDebugLabelWorldPosition();

                foreach (LiquidParticleData particle in particles)
                {
                    if (particle == null)
                        continue;

                    Vector3 position = particle.transform.position;
                    Gizmos.color = connectionDebugColor;
                    Gizmos.DrawLine(origin, position);
                    Gizmos.color = particleDebugColor;
                    Gizmos.DrawWireSphere(position, debugParticleRadius);
                }

#if UNITY_EDITOR
                UnityEditor.Handles.Label(origin, BuildDebugText(BuildComposition()));
#endif
            }

            Gizmos.color = previousColor;
        }

        private void DrawColliderGizmo(Collider2D collider)
        {
            if (collider is BoxCollider2D boxCollider)
            {
                Matrix4x4 previousMatrix = Gizmos.matrix;
                Gizmos.matrix = boxCollider.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(boxCollider.offset, boxCollider.size);
                Gizmos.matrix = previousMatrix;
                return;
            }

            if (collider is CircleCollider2D circleCollider)
            {
                Vector3 center = circleCollider.transform.TransformPoint(circleCollider.offset);
                Vector3 scale = circleCollider.transform.lossyScale;
                float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                Gizmos.DrawWireSphere(center, circleCollider.radius * radiusScale);
                return;
            }

            Bounds bounds = collider.bounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        private Vector3 GetDebugLabelWorldPosition()
        {
            CacheColliders();

            bool hasBounds = false;
            Bounds combinedBounds = default;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.isTrigger)
                    continue;

                if (!hasBounds)
                {
                    combinedBounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(collider.bounds);
                }
            }

            if (!hasBounds)
                return transform.position + Vector3.up * 0.5f;

            return new Vector3(combinedBounds.center.x, combinedBounds.max.y + 0.25f, transform.position.z);
        }

        private string BuildDebugText(CocktailComposition composition)
        {
            debugTextBuilder.Clear();
            debugTextBuilder.Append(name);
            debugTextBuilder.AppendLine(" 액체 추적기");
            debugTextBuilder.Append("입자: ");
            debugTextBuilder.Append(particles.Count);
            debugTextBuilder.Append("  총량: ");
            debugTextBuilder.Append(composition.TotalVolumeMl.ToString("0.##"));
            debugTextBuilder.AppendLine(" ml");
            debugTextBuilder.Append("온도: ");
            debugTextBuilder.Append(composition.AverageTemperatureC.ToString("0.#"));
            debugTextBuilder.Append(" C  잔: ");
            debugTextBuilder.Append(GetGlassLabel(composition.GlassId));
            debugTextBuilder.Append("  얼음: ");
            debugTextBuilder.Append(composition.HasIce ? "있음" : "없음");
            debugTextBuilder.Append(" (");
            debugTextBuilder.Append(iceCubes.Count);
            debugTextBuilder.AppendLine(")");

            int lineCount = 0;
            foreach (KeyValuePair<ItemDef, float> pair in composition.Volumes)
            {
                if (lineCount >= 4)
                {
                    debugTextBuilder.AppendLine("...");
                    break;
                }

                float ratio = composition.TotalVolumeMl > 0f
                    ? pair.Value / composition.TotalVolumeMl * 100f
                    : 0f;

                debugTextBuilder.Append(GetItemLabel(pair.Key));
                debugTextBuilder.Append(": ");
                debugTextBuilder.Append(pair.Value.ToString("0.##"));
                debugTextBuilder.Append(" ml / ");
                debugTextBuilder.Append(ratio.ToString("0.#"));
                debugTextBuilder.AppendLine("%");
                lineCount++;
            }

            if (lineCount == 0)
                debugTextBuilder.AppendLine("비어 있음");

            return debugTextBuilder.ToString();
        }

        private static string GetGlassLabel(string glassId)
        {
            if (string.IsNullOrWhiteSpace(glassId))
                return "미지정";

            return glassId.Trim().ToLowerInvariant() switch
            {
                "rock" => "락 글라스",
                "highball" => "하이볼 글라스",
                "hurricane" => "허리케인 글라스",
                "martini" => "마티니 글라스",
                _ => glassId
            };
        }

        private static string GetItemLabel(ItemDef item)
        {
            if (item == null)
                return "알 수 없음";

            if (!string.IsNullOrWhiteSpace(item.displayName))
                return item.displayName;

            if (!string.IsNullOrWhiteSpace(item.id))
                return item.id;

            return item.name;
        }

        private void EnsureDebugBoxStyle()
        {
            if (debugBoxStyle != null)
                return;

            debugBoxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                wordWrap = true,
                padding = new RectOffset(6, 6, 5, 5)
            };
            debugBoxStyle.normal.textColor = Color.white;
        }
    }
}
