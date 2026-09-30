using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    /// <summary>E-only secondary response. Cosmetics never enter the particle or ingredient ledger.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(220)]
    public sealed class FluidExperimentEffects : MonoBehaviour
    {
        public FluidExperimentWorld world;
        public Shader cosmeticShader;
        public bool enableIceResponse = true;
        public bool enableCosmetics = true;
        [Tooltip("Procedural audio is optional and creates no resources until enabled.")]
        public bool enableAudio;
        [Range(0, 1)] public float audioVolume = .18f;
        [Min(.2f)] public float maximumSnapshotAge = .5f;
        public const int MaximumCosmetics = 128;
        public int ActiveCosmeticCount { get; private set; }
        public int LastAppliedIceCount { get; private set; }
        public int AudioSourceCount => pourAudio != null ? 2 : 0;
        private readonly Cosmetic[] cosmetics = new Cosmetic[MaximumCosmetics];
        private readonly List<IceContact> iceContacts = new List<IceContact>(64);
        private readonly Dictionary<uint, VesselSample> vessels = new Dictionary<uint, VesselSample>();
        private readonly List<CosmeticSolid> cosmeticSolids = new List<CosmeticSolid>(64);
        private readonly Vector3[] vertices = new Vector3[MaximumCosmetics * 4];
        private readonly Vector2[] uv = new Vector2[MaximumCosmetics * 4];
        private readonly Color[] colors = new Color[MaximumCosmetics * 4];
        private readonly int[] triangles = new int[MaximumCosmetics * 6];
        private Mesh mesh;
        private Material material;
        private GameObject cosmeticObject, audioObject;
        private MeshRenderer meshRenderer;
        private AudioSource pourAudio, dripAudio;
        private AudioClip pourClip, dripClip;
        private int revision = -1, generation = -1, spawnCursor;
        private float snapshotAt = float.NegativeInfinity, lastEmitted, nextDrip;
        private bool hasCosmetics;
        private struct Cosmetic
        {
            public bool active, bubble;
            public Vector2 position, velocity;
            public float age, life, radius;
            public FluidExperimentBody vessel;
        }
        private struct VesselSample
        {
            public FluidExperimentBody body;
            public float surface, density, foamLife, viscosity;
        }
        private struct IceContact
        {
            public FluidExperimentBody ice, vessel;
            public Vector2 velocity, vesselPosition;
            public float surface, coverage, density, viscosity, vesselAngle;
        }
        private struct CosmeticSolid
        {
            public FluidExperimentBody body;
            public Vector2 position, min, max;
            public float angle;
        }
        private bool IsActive => world != null && world.Liquid != null && world.Liquid.IsOperational
            && world.Liquid.useImprovedPhysics;

        private void Update()
        {
            if (!IsActive) { ClearState(); return; }
            RefreshSnapshot();
            StepCosmetics(Mathf.Min(Time.deltaTime, .05f));
            UpdateAudio(Mathf.Min(Time.unscaledDeltaTime, .05f));
        }

        private void FixedUpdate() => ApplyIceResponse(Time.fixedDeltaTime);

        /// <summary>Consumes only a coherent completed readback; never stalls the GPU for effects.</summary>
        public void RefreshSnapshot()
        {
            if (!IsActive) { ClearState(); return; }
            var gpu = world.Liquid;
            if (gpu.Ledger == null) { ClearState(); return; }
            if (generation != gpu.Ledger.Generation)
            {
                ClearState(); generation = gpu.Ledger.Generation; lastEmitted = gpu.EmittedMl;
            }
            if (revision == gpu.SnapshotRevision) return;
            revision = gpu.SnapshotRevision; snapshotAt = Time.unscaledTime;
            vessels.Clear(); iceContacts.Clear();
            foreach (var body in world.Items)
            {
                if (body == null || !body.IsVessel) continue;
                float density = 0, foam = 0, viscosity = 0, ml = 0;
                foreach (var pair in gpu.IngredientsIn(body.Id))
                {
                    float amount = (float)pair.Value;
                    var preset = PresetFor(pair.Key);
                    ml += amount; density += Mathf.Clamp(pair.Key.density, .5f, 1.8f) * amount;
                    foam += preset.FoamLifetime * amount; viscosity += preset.ViscosityRate * amount;
                }
                if (ml <= 0) continue;
                vessels.Add(body.Id, new VesselSample { body = body, density = density / ml,
                    foamLife = foam / ml, viscosity = viscosity / ml, surface = float.NegativeInfinity });
            }
            var particles = gpu.Snapshot;
            for (int i = 0; i < particles.Length; i++)
            {
                var p = particles[i];
                if (p.Active == 0 || !vessels.TryGetValue(p.VesselId, out var vessel)) continue;
                vessel.surface = Mathf.Max(vessel.surface, p.Position.y + gpu.Radius);
                vessels[p.VesselId] = vessel;
            }
            // At most 64 ice contacts are sampled, once per completed snapshot (normally 5 Hz).
            foreach (var ice in world.Items)
            {
                if (iceContacts.Count >= 64) break;
                if (ice == null || ice.kind != LabItemKind.Ice || ice.IsHeld || !ice.Body.simulated) continue;
                Bounds bounds = ice.SolidBounds;
                float reach = gpu.Radius * 2.5f;
                foreach (var pair in vessels)
                {
                    VesselSample vessel = pair.Value;
                    if ((vessel.body.IsHeld && ice.ContainingVesselId != vessel.body.Id)
                        || !vessel.body.ContainsLiquid(bounds.center)) continue;
                    float amount = 0, localSurface = float.NegativeInfinity;
                    Vector2 velocity = Vector2.zero;
                    int neighbors = 0;
                    for (int i = 0; i < particles.Length; i++)
                    {
                        var p = particles[i];
                        if (p.Active == 0 || p.VesselId != pair.Key || p.VolumeMl <= 0
                            || p.Position.x < bounds.min.x - reach || p.Position.x > bounds.max.x + reach
                            || p.Position.y < bounds.min.y - reach || p.Position.y > bounds.max.y + reach) continue;
                        amount += p.VolumeMl; velocity += p.Velocity * p.VolumeMl; neighbors++;
                        localSurface = Mathf.Max(localSurface, p.Position.y + gpu.Radius);
                    }
                    if (neighbors < 2 || amount <= 0 || bounds.size.y <= 0) continue;
                    float submerged = Mathf.Clamp01((localSurface - bounds.min.y) / bounds.size.y);
                    // Sparse spray cannot make a nearly empty glass support a whole ice cube.
                    float supportArea = Mathf.Max(.0001f, 2 * reach * (bounds.size.x + bounds.size.y));
                    float coverage = Mathf.Clamp01(amount * gpu.AreaPerMl(pair.Key) / (supportArea * .3f));
                    submerged *= coverage;
                    if (submerged <= .01f) continue;
                    iceContacts.Add(new IceContact { ice = ice, vessel = vessel.body,
                        vesselPosition = vessel.body.Position, vesselAngle = vessel.body.Angle,
                        velocity = velocity / amount, surface = localSurface, coverage = coverage, density = vessel.density,
                        viscosity = vessel.viscosity });
                    break;
                }
            }
            if (enableCosmetics) SpawnCosmetics(particles);
        }

        private FluidExperimentMaterialPreset PresetFor(ItemDef ingredient)
            => FluidExperimentMaterials.Resolve(ingredient, world.Liquid.improvedMaterial);

        public void ApplyIceResponse(float dt)
        {
            LastAppliedIceCount = 0;
            if (!IsActive || !enableIceResponse || dt <= 0) return;
            RefreshSnapshot();
            if (Time.unscaledTime - snapshotAt > maximumSnapshotAge) return;
            foreach (IceContact contact in iceContacts)
            {
                var ice = contact.ice; var vessel = contact.vessel;
                if (ice == null || vessel == null || ice.IsHeld
                    || (vessel.IsHeld && ice.ContainingVesselId != vessel.Id) || !ice.Body.simulated
                    || ice.Body.bodyType != RigidbodyType2D.Dynamic || !vessel.ContainsLiquid(ice.Position)
                    || Vector2.Distance(contact.vesselPosition, vessel.Position) > world.Liquid.Radius * 2
                    || Mathf.Abs(Mathf.DeltaAngle(contact.vesselAngle, vessel.Angle)) > 5) continue;
                Bounds bounds = ice.SolidBounds;
                float submerged = Mathf.Clamp01((contact.surface - bounds.min.y) / Mathf.Max(.001f, bounds.size.y)) * contact.coverage;
                if (submerged <= 0) continue;
                Vector2 force = IceForce(ice.Body.mass, submerged, contact.density,
                    contact.viscosity, ice.Body.linearVelocity, contact.velocity, Physics2D.gravity, dt);
                ice.Body.AddForce(force);
                float damping = Mathf.Clamp(contact.viscosity * .25f, 1, 10) * submerged;
                ice.Body.AddTorque(-ice.Body.angularVelocity * Mathf.Deg2Rad * ice.Body.inertia
                    * (1 - Mathf.Exp(-damping * dt)) / dt);
                LastAppliedIceCount++;
            }
        }

        /// <summary>Bounded Archimedes/relative-flow response; an empty contact produces exactly zero.</summary>
        public static Vector2 IceForce(float mass, float submerged, float liquidDensity, float viscosity,
            Vector2 iceVelocity, Vector2 liquidVelocity, Vector2 gravity, float dt)
        {
            if (mass <= 0 || submerged <= 0 || dt <= 0) return Vector2.zero;
            submerged = Mathf.Clamp01(submerged);
            Vector2 buoyancy = -gravity * (mass * Mathf.Clamp(liquidDensity / .917f, 0, 1.6f) * submerged);
            float drag = Mathf.Clamp(viscosity * .4f, 1, 20) * submerged;
            Vector2 acceleration = (liquidVelocity - iceVelocity) * (1 - Mathf.Exp(-drag * dt)) / dt;
            acceleration = Vector2.ClampMagnitude(acceleration, Mathf.Max(1, gravity.magnitude * 2));
            return buoyancy + acceleration * mass;
        }

        private void SpawnCosmetics(GpuLiquidParticle[] particles)
        {
            int spawned = 0;
            int stride = Mathf.Max(1, particles.Length / 128);
            for (int visited = 0; visited < particles.Length && spawned < 6; visited += stride)
            {
                int index = (spawnCursor + visited) % particles.Length;
                var p = particles[index];
                if (p.Active == 0 || p.Velocity.sqrMagnitude < .16f
                    || !vessels.TryGetValue(p.VesselId, out var vessel)) continue;
                bool bubble = p.Position.y < vessel.surface - world.Liquid.Radius * 2;
                if (bubble && p.Velocity.sqrMagnitude < 1) continue;
                float radius = world.Liquid.Radius * (bubble ? .2f : .3f);
                if (!vessel.body.ContainsLiquidDisk(vessel.body.WorldToLocal(p.Position), radius)) continue;
                for (int slot = 0; slot < MaximumCosmetics; slot++)
                {
                    if (cosmetics[slot].active) continue;
                    cosmetics[slot] = new Cosmetic { active = true, bubble = bubble, vessel = vessel.body,
                        position = p.Position, velocity = p.Velocity * .12f, radius = radius,
                        life = Mathf.Clamp(vessel.foamLife * (bubble ? .65f : 1), .2f, 5) };
                    hasCosmetics = true;
                    spawned++; break;
                }
            }
            if (particles.Length > 0) spawnCursor = (spawnCursor + 97) % particles.Length;
        }

        public void StepCosmetics(float dt)
        {
            ActiveCosmeticCount = 0;
            if (!IsActive || !enableCosmetics) { ClearCosmetics(); return; }
            if (!hasCosmetics)
            {
                if (meshRenderer != null) meshRenderer.enabled = false;
                return;
            }
            EnsureMesh();
            if (mesh == null) return;
            RefreshCosmeticSolids();
            Array.Clear(vertices, 0, vertices.Length);
            for (int i = 0; i < MaximumCosmetics; i++)
            {
                Cosmetic effect = cosmetics[i];
                if (!effect.active) continue;
                effect.age += Mathf.Max(0, dt);
                effect.velocity *= Mathf.Exp(-3 * Mathf.Max(0, dt));
                Vector2 next = effect.position + (effect.velocity + Vector2.up * (effect.bubble ? .13f : .015f)) * dt;
                if (effect.age >= effect.life || effect.vessel == null
                    || !effect.vessel.ContainsLiquidDisk(effect.vessel.WorldToLocal(next), effect.radius)
                    || BlockedBySolid(next, effect.radius, effect.vessel))
                { cosmetics[i].active = false; continue; }
                effect.position = next; cosmetics[i] = effect; ActiveCosmeticCount++;
                int v = i * 4; float radius = effect.radius;
                vertices[v] = next + new Vector2(-radius, -radius); vertices[v + 1] = next + new Vector2(radius, -radius);
                vertices[v + 2] = next + new Vector2(radius, radius); vertices[v + 3] = next + new Vector2(-radius, radius);
                float alpha = Mathf.Sin(Mathf.PI * Mathf.Clamp01(effect.age / effect.life)) * (effect.bubble ? .27f : .5f);
                for (int corner = 0; corner < 4; corner++) colors[v + corner] = new Color(.93f, .97f, 1, alpha);
            }
            hasCosmetics = ActiveCosmeticCount > 0;
            if (!hasCosmetics) { meshRenderer.enabled = false; return; }
            mesh.vertices = vertices; mesh.colors = colors;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(200, 200, 1));
            meshRenderer.enabled = ActiveCosmeticCount > 0;
        }

        private bool BlockedBySolid(Vector2 point, float radius, FluidExperimentBody owner)
        {
            if (owner.IsHeld) return false;
            foreach (CosmeticSolid solid in cosmeticSolids)
            {
                // External held objects and a held owner's environment are mutually transparent.
                var body = solid.body;
                if (body == null || body == owner || body.IsHeld) continue;
                if (point.x < solid.min.x - radius || point.x > solid.max.x + radius
                    || point.y < solid.min.y - radius || point.y > solid.max.y + radius) continue;
                if (body.SolidClearance(FluidExperimentBody.Rotate(point - solid.position, -solid.angle)) < radius) return true;
            }
            return false;
        }

        private void RefreshCosmeticSolids()
        {
            cosmeticSolids.Clear();
            var items = world.Items;
            for (int i = 0; i < items.Count; i++)
            {
                FluidExperimentBody body = items[i];
                if (body == null || body.IsHeld || body.collisionProfile == null) continue;
                Vector2 position = body.Position;
                float angle = body.Angle, radians = angle * Mathf.Deg2Rad;
                float cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
                Vector3 scale = body.transform.lossyScale;
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
                bool found = false;
                foreach (FluidExperimentHull hull in body.collisionProfile.solids)
                foreach (Vector2 local in hull.points)
                {
                    Vector2 scaled = new Vector2(local.x * scale.x, local.y * scale.y);
                    Vector2 p = position + new Vector2(scaled.x * cosine - scaled.y * sine,
                        scaled.x * sine + scaled.y * cosine);
                    min = Vector2.Min(min, p); max = Vector2.Max(max, p); found = true;
                }
                if (!found) continue;
                // Use authored solids and current physics pose, never interpolated collider bounds.
                // Disk expansion happens at query time. Extra padding only covers transform roundoff.
                float coordinate = Mathf.Max(Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(min.y)),
                    Mathf.Max(Mathf.Abs(max.x), Mathf.Abs(max.y)));
                Vector2 padding = Vector2.one * (.0001f + coordinate * .0000004f);
                cosmeticSolids.Add(new CosmeticSolid { body = body, position = position, angle = angle,
                    min = min - padding, max = max + padding });
            }
        }

        private void EnsureMesh()
        {
            if (mesh != null) return;
            Shader shader = cosmeticShader != null ? cosmeticShader : Shader.Find("Hidden/Slainte/FluidExperimentEffects");
            if (shader == null) return;
            mesh = new Mesh { name = "Experimental foam/bubbles (zero ml)" }; mesh.MarkDynamic();
            material = new Material(shader) { name = "Experimental foam/bubbles" };
            cosmeticObject = new GameObject("E liquid cosmetics"); cosmeticObject.transform.SetParent(transform, false);
            cosmeticObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            cosmeticObject.transform.localScale = Vector3.one;
            cosmeticObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = cosmeticObject.AddComponent<MeshRenderer>(); meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = 3;
            for (int i = 0; i < MaximumCosmetics; i++)
            {
                int v = i * 4, t = i * 6;
                uv[v] = Vector2.zero; uv[v + 1] = Vector2.right; uv[v + 2] = Vector2.one; uv[v + 3] = Vector2.up;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
        }

        private void UpdateAudio(float dt)
        {
            if (!enableAudio) { DisposeAudio(); return; }
            EnsureAudio();
            float flow = 0;
            foreach (var body in world.Items)
                if (body != null && body.kind == LabItemKind.Bottle) flow += body.ReservoirCurrentFlowMlPerSecond;
            float emitted = world.Liquid.EmittedMl, delta = Mathf.Max(0, emitted - lastEmitted);
            lastEmitted = emitted;
            float target = Mathf.Clamp01(flow / 30) * audioVolume;
            pourAudio.volume = Mathf.Lerp(pourAudio.volume, target, 1 - Mathf.Exp(-12 * dt));
            pourAudio.pitch = Mathf.Lerp(.75f, 1.25f, Mathf.Clamp01(flow / 40));
            if (flow > 1 && !pourAudio.isPlaying) pourAudio.Play();
            if (flow <= .01f && pourAudio.volume < .001f) pourAudio.Stop();
            if (flow < 4 && delta > 0 && Time.unscaledTime >= nextDrip)
            { dripAudio.PlayOneShot(dripClip, audioVolume * .7f); nextDrip = Time.unscaledTime + .1f; }
        }

        private void EnsureAudio()
        {
            if (pourAudio != null) return;
            const int rate = 22050;
            var pour = new float[rate / 2]; var drip = new float[rate / 8];
            uint random = 7411; float smooth = 0;
            for (int i = 0; i < pour.Length; i++)
            {
                random = random * 1664525u + 1013904223u;
                float noise = (random >> 8) / 8388608f - 1;
                smooth = Mathf.Lerp(smooth, noise, .14f);
                pour[i] = smooth * .7f * (.7f + .3f * Mathf.Sin(i * 2 * Mathf.PI / pour.Length));
            }
            // Crossfade the loop endpoints to suppress clicks; all sample generation stays off the audio thread.
            for (int i = 0; i < 128; i++) pour[pour.Length - 128 + i] = Mathf.Lerp(pour[pour.Length - 128 + i], pour[i], i / 127f);
            for (int i = 0; i < drip.Length; i++)
            {
                float t = i / (float)rate;
                drip[i] = Mathf.Sin(2 * Mathf.PI * (650 * t + 1500 * t * t)) * Mathf.Exp(-40 * t) * Mathf.Min(1, t * 1000) * .5f;
            }
            pourClip = AudioClip.Create("Procedural E pour", pour.Length, 1, rate, false); pourClip.SetData(pour, 0);
            dripClip = AudioClip.Create("Procedural E drip", drip.Length, 1, rate, false); dripClip.SetData(drip, 0);
            audioObject = new GameObject("E optional liquid audio"); audioObject.transform.SetParent(transform, false);
            pourAudio = audioObject.AddComponent<AudioSource>(); pourAudio.playOnAwake = false;
            pourAudio.loop = true; pourAudio.clip = pourClip; pourAudio.volume = 0; pourAudio.spatialBlend = 0;
            dripAudio = audioObject.AddComponent<AudioSource>(); dripAudio.playOnAwake = false; dripAudio.spatialBlend = 0;
        }

        private void ClearCosmetics()
        {
            if (hasCosmetics) Array.Clear(cosmetics, 0, cosmetics.Length);
            hasCosmetics = false; ActiveCosmeticCount = 0;
            if (meshRenderer != null) meshRenderer.enabled = false;
        }
        private void ClearState()
        {
            ClearCosmetics(); iceContacts.Clear(); vessels.Clear(); cosmeticSolids.Clear(); LastAppliedIceCount = 0;
            revision = generation = -1; snapshotAt = float.NegativeInfinity;
            if (pourAudio != null) pourAudio.Stop();
            if (dripAudio != null) dripAudio.Stop();
        }
        private void DisposeAudio()
        {
            if (audioObject != null) Destroy(audioObject);
            if (pourClip != null) Destroy(pourClip); if (dripClip != null) Destroy(dripClip);
            audioObject = null; pourAudio = dripAudio = null; pourClip = dripClip = null;
        }
        private void OnDisable()
        {
            ClearState(); DisposeAudio();
            if (cosmeticObject != null) Destroy(cosmeticObject);
            if (mesh != null) Destroy(mesh); if (material != null) Destroy(material);
            cosmeticObject = null; meshRenderer = null; mesh = null; material = null;
        }
    }
}
