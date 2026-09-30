using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuImprovedSurfaceParticle
    {
        public Vector2 StartPosition, EndPosition, Direction, Radii;
        public float CoverageWeight, BirthFraction;
        public uint Active, VesselId;
        public float BirthTime;
        public uint Token;
        public float NeighborhoodWeight;
        public float OpacityScale;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuImprovedSurfaceHistory
    {
        public Vector2 Position;
        public float BirthTime;
        public uint Token, Active, Pending;
        public Vector2 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuImprovedSurfaceHull
    {
        public Vector4 Bounds;
        public uint First, Count, VesselId, Held;
    }

    public sealed partial class FluidExperimentGpuLiquid
    {
        [Header("Improved surface (D / E only)")]
        public bool useImprovedSurface;
        public ComputeShader improvedSurfaceCompute;
        public Shader improvedAccumulationShader, improvedCompositeShader;
        [Range(1, 4)] public float improvedMaximumAspect = 3;
        [Range(0, .4f)] public float improvedOpticalAbsorption = .18f;
        [NonSerialized] public float ImprovedSurfaceInterpolationOverride = -1;

        private ComputeShader improvedSurfaceProgram;
        private GraphicsBuffer improvedSurfaceHistoryBuffer, improvedSurfaceParticleBuffer;
        private GraphicsBuffer improvedSurfaceHullBuffer, improvedSurfaceVertexBuffer, improvedSurfaceVesselBuffer;
        private GraphicsBuffer improvedSurfaceMaterialBuffer;
        private readonly Vector4[] improvedSurfaceMaterials = new Vector4[32];
        private readonly List<GpuImprovedSurfaceHull> improvedSurfaceHulls = new List<GpuImprovedSurfaceHull>(64);
        private readonly List<Vector2> improvedSurfaceVertices = new List<Vector2>(MaximumBoundarySegments);
        private readonly List<Vector4> improvedSurfaceVessels = new List<Vector4>(64);
        private readonly GpuImprovedSurfaceHull[] improvedUploadedHulls = new GpuImprovedSurfaceHull[MaximumBoundarySegments];
        private readonly Vector2[] improvedUploadedVertices = new Vector2[MaximumBoundarySegments];
        private readonly Vector4[] improvedUploadedVessels = new Vector4[MaximumBoundarySegments];
        private readonly List<Vector2> improvedPolygonPath = new List<Vector2>(64);
        private readonly Vector2[] improvedBoxPath = new Vector2[4];
        private int improvedUploadedHullCount = -1, improvedUploadedVertexCount = -1;
        private int improvedUploadedVesselCount = -1, improvedUploadedMaterialCount = -1;
        private int improvedCaptureKernel, improvedBuildKernel, improvedResetKernel, improvedRebaseKernel;
        private float improvedTickDuration = .02f, improvedTickStart, improvedLastTickTime;
        private bool improvedSurfaceTickReady;
        public bool ImprovedSurfaceReady => improvedSurfaceProgram != null && improvedSurfaceTickReady;
        public string ImprovedSurfaceError { get; private set; }
        public int ImprovedSurfaceMaskHullCount => improvedSurfaceHulls.Count;
        public float ImprovedSurfaceInterpolation => ImprovedSurfaceInterpolationOverride >= 0
            ? Mathf.Clamp01(ImprovedSurfaceInterpolationOverride)
            : Mathf.Clamp01((Time.time - improvedLastTickTime) / Mathf.Max(.00001f, improvedTickDuration));

        // Called after queued spawns are dispatched, before the first substep. This captures a
        // full fixed-tick endpoint, including exact positions of not-yet-born spawn commands.
        internal void BeginImprovedSurfaceTick(float dt)
        {
            if (!useImprovedSurface || !EnsureImprovedSurfaceResources()) return;
            improvedTickDuration = dt;
            improvedTickStart = simulationTime;
            // Capture reads only the particle/stream buffers and capacity. Parameters,
            // composition and material metadata are needed once at the final build.
            improvedSurfaceProgram.Dispatch(improvedCaptureKernel, SurfaceDispatchGroups, 1, 1);
        }

        // The final solver grid is read only. Rendering never writes a physical particle,
        // changes volume/ownership, or changes the physics time step.
        internal void EndImprovedSurfaceTick(float dt)
        {
            if (!useImprovedSurface || improvedSurfaceProgram == null) return;
            UploadImprovedSurfaceVessels();
            BindImprovedSurfaceTick();
            improvedSurfaceProgram.Dispatch(improvedBuildKernel, SurfaceDispatchGroups, 1, 1);
            improvedLastTickTime = Time.inFixedTimeStep ? Time.fixedTime : Time.time;
            improvedSurfaceTickReady = true;
        }

        internal void ResetImprovedSurfaceHistory()
        {
            improvedSurfaceTickReady = false;
            if (improvedSurfaceProgram == null) return;
            improvedSurfaceProgram.Dispatch(improvedResetKernel, SurfaceDispatchGroups, 1, 1);
        }

        // Rebase both display endpoints after an explicit owner translation. A reset clears
        // every endpoint; this targeted path leaves unrelated fluid interpolation untouched.
        internal void InvalidateImprovedSurfaceHistory(uint vesselId)
        {
            if (improvedSurfaceProgram == null || vesselId == 0) return;
            improvedSurfaceProgram.SetInt("_RebaseVessel", unchecked((int)vesselId));
            improvedSurfaceProgram.Dispatch(improvedRebaseKernel, SurfaceDispatchGroups, 1, 1);
        }

        private int SurfaceDispatchGroups => Mathf.CeilToInt(particleCapacity / 64f);

        private bool EnsureImprovedSurfaceResources()
        {
            if (improvedSurfaceProgram != null) return true;
            if (improvedSurfaceCompute == null)
            {
                ImprovedSurfaceError = "Improved surface compute shader is not assigned.";
                return false;
            }
            try
            {
                improvedSurfaceProgram = Instantiate(improvedSurfaceCompute);
                improvedSurfaceHistoryBuffer = CreateStructured<GpuImprovedSurfaceHistory>(particleCapacity);
                improvedSurfaceParticleBuffer = CreateStructured<GpuImprovedSurfaceParticle>(particleCapacity);
                improvedSurfaceHullBuffer = CreateStructured<GpuImprovedSurfaceHull>(MaximumBoundarySegments);
                improvedSurfaceVertexBuffer = CreateStructured<Vector2>(MaximumBoundarySegments);
                improvedSurfaceVesselBuffer = CreateStructured<Vector4>(MaximumBoundarySegments);
                improvedSurfaceMaterialBuffer = CreateStructured<Vector4>(32);
                improvedCaptureKernel = ImprovedSurfaceKernel("CaptureTickStart");
                improvedBuildKernel = ImprovedSurfaceKernel("BuildSurfaceEllipses");
                improvedResetKernel = ImprovedSurfaceKernel("ResetSurfaceHistory");
                improvedRebaseKernel = ImprovedSurfaceKernel("RebaseSurfaceOwner");
                improvedSurfaceProgram.SetInt("_ParticleCapacity", particleCapacity);
                foreach (int kernel in new[] { improvedCaptureKernel, improvedBuildKernel, improvedResetKernel, improvedRebaseKernel })
                {
                    improvedSurfaceProgram.SetBuffer(kernel, "_Particles", particleBuffer);
                    improvedSurfaceProgram.SetBuffer(kernel, "_Streams", streamParticleBuffer);
                    improvedSurfaceProgram.SetBuffer(kernel, "_History", improvedSurfaceHistoryBuffer);
                    improvedSurfaceProgram.SetBuffer(kernel, "_Ellipses", improvedSurfaceParticleBuffer);
                    improvedSurfaceProgram.SetBuffer(kernel, "_GridHeads", gridHeadBuffer);
                    improvedSurfaceProgram.SetBuffer(kernel, "_GridNext", gridNextBuffer);
                    improvedSurfaceProgram.SetBuffer(kernel, "_SurfaceVessels", improvedSurfaceVesselBuffer);
                    improvedSurfaceProgram.SetBuffer(kernel, "_SurfaceMaterials", improvedSurfaceMaterialBuffer);
                }
                ResetImprovedSurfaceHistory();
                ImprovedSurfaceError = null;
                return true;
            }
            catch (Exception exception)
            {
                DisposeImprovedSurface();
                ImprovedSurfaceError = exception.Message;
                Debug.LogException(exception, this);
                return false;
            }
        }

        private int ImprovedSurfaceKernel(string name)
        {
            if (!improvedSurfaceProgram.HasKernel(name)) throw new InvalidOperationException("Missing improved surface kernel " + name);
            int kernel = improvedSurfaceProgram.FindKernel(name);
            if (!improvedSurfaceProgram.IsSupported(kernel)) throw new InvalidOperationException("Unsupported improved surface kernel " + name);
            return kernel;
        }

        private void BindImprovedSurfaceTick()
        {
            improvedSurfaceProgram.SetFloat("_TickStartTime", improvedTickStart);
            improvedSurfaceProgram.SetFloat("_TickDuration", Mathf.Max(.00001f, improvedTickDuration));
            improvedSurfaceProgram.SetFloat("_ParticleRadius", Radius);
            improvedSurfaceProgram.SetFloat("_NominalVolume", Mathf.Max(.00001f, ParticleVolumeMl));
            improvedSurfaceProgram.SetFloat("_SupportMultiplier", Mathf.Clamp(referenceSupportRadius, 1, 3));
            improvedSurfaceProgram.SetFloat("_MaximumAspect", Mathf.Clamp(improvedMaximumAspect, 1, 4));
            improvedSurfaceProgram.SetFloat("_CellSize", SolverSmoothingRadius);
            improvedSurfaceProgram.SetVector("_GridOrigin", settings.gpuLiquidWorldMin);
            improvedSurfaceProgram.SetInt("_GridWidth", gridWidth);
            improvedSurfaceProgram.SetInt("_GridHeight", gridHeight);
            improvedSurfaceProgram.SetInt("_UseAreaCalibration", useImprovedPhysics ? 1 : 0);
            improvedSurfaceProgram.SetInt("_IngredientCount", ingredients.Count);
            improvedSurfaceProgram.SetInt("_MaximumIngredients", maximumIngredients);
            improvedSurfaceProgram.SetBuffer(improvedBuildKernel, "_CompositionRead", compositionAIsCurrent ? compositionA : compositionB);
            bool materialsChanged = improvedUploadedMaterialCount != ingredients.Count;
            for (int i = 0; i < ingredients.Count; i++)
            {
                float opacity = useImprovedPhysics ? FluidExperimentMaterials.Resolve(ingredients[i], improvedMaterial).OpacityScale : 1;
                if (improvedSurfaceMaterials[i].x != opacity) materialsChanged = true;
                improvedSurfaceMaterials[i] = new Vector4(opacity, 0, 0, 0);
            }
            if (materialsChanged && ingredients.Count > 0)
                improvedSurfaceMaterialBuffer.SetData(improvedSurfaceMaterials, 0, 0, ingredients.Count);
            improvedUploadedMaterialCount = ingredients.Count;
        }

        private void UploadImprovedSurfaceVessels()
        {
            improvedSurfaceVessels.Clear();
            // Entry zero also supplies the area convention for unowned falling liquid.
            float fallbackArea = useImprovedPhysics ? AreaPerMl(0) : 0;
            improvedSurfaceVessels.Add(new Vector4(0, fallbackArea,
                useImprovedPhysics ? ImprovedKernelRadiusForArea(fallbackArea) : SolverSmoothingRadius, 0));
            for (int bodyIndex = 0; bodyIndex < world.Items.Count; bodyIndex++)
            {
                FluidExperimentBody body = world.Items[bodyIndex];
                if (body == null || !body.IsVessel) continue;
                if (improvedSurfaceVessels.Count >= MaximumBoundarySegments)
                    throw new InvalidOperationException("Improved surface vessel budget exceeded.");
                float area = useImprovedPhysics ? AreaPerMl(body) : 0;
                improvedSurfaceVessels.Add(new Vector4(body.Id, area,
                    useImprovedPhysics ? ImprovedKernelRadiusForArea(area) : SolverSmoothingRadius, body.IsHeld ? 1 : 0));
            }
            bool changed = improvedUploadedVesselCount != improvedSurfaceVessels.Count;
            for (int i = 0; i < improvedSurfaceVessels.Count; i++)
            {
                if (!improvedUploadedVessels[i].Equals(improvedSurfaceVessels[i])) changed = true;
                improvedUploadedVessels[i] = improvedSurfaceVessels[i];
            }
            if (changed) improvedSurfaceVesselBuffer.SetData(improvedSurfaceVessels);
            if (improvedUploadedVesselCount != improvedSurfaceVessels.Count)
                improvedSurfaceProgram.SetInt("_SurfaceVesselCount", improvedSurfaceVessels.Count);
            improvedUploadedVesselCount = improvedSurfaceVessels.Count;
        }

        private bool PrepareImprovedSurface()
        {
            if (!EnsureImprovedSurfaceResources() || !improvedSurfaceTickReady) return false;
            UploadImprovedSurfaceVessels();
            UploadImprovedSurfaceHulls();
            surfaceParticleProperties.SetBuffer("_ImprovedEllipses", improvedSurfaceParticleBuffer);
            surfaceParticleProperties.SetBuffer("_ImprovedHullVertices", improvedSurfaceVertexBuffer);
            surfaceParticleProperties.SetBuffer("_ImprovedHulls", improvedSurfaceHullBuffer);
            surfaceParticleProperties.SetBuffer("_SurfaceVessels", improvedSurfaceVesselBuffer);
            surfaceParticleProperties.SetInt("_ImprovedHullCount", improvedSurfaceHulls.Count);
            surfaceParticleProperties.SetInt("_SurfaceVesselCount", improvedSurfaceVessels.Count);
            surfaceParticleProperties.SetFloat("_SurfaceInterpolation", ImprovedSurfaceInterpolation);
            surfaceCompositeMaterial.SetFloat("_OpticalAbsorption", Mathf.Clamp(improvedOpticalAbsorption, 0, .4f));
            return true;
        }

        // The same authored solid hulls used by CPU/GPU contact provide the visual mask.
        // A virtual open-rim ownership edge is never added, nor are table/screen boundaries.
        // TransformPoint follows Rigidbody's displayed interpolation rather than its future pose.
        private void UploadImprovedSurfaceHulls()
        {
            improvedSurfaceHulls.Clear(); improvedSurfaceVertices.Clear();
            for (int bodyIndex = 0; bodyIndex < world.Items.Count; bodyIndex++)
            {
                FluidExperimentBody body = world.Items[bodyIndex];
                if (body == null) continue;
                if (body.collisionProfile != null)
                {
                    foreach (FluidExperimentHull hull in body.collisionProfile.solids)
                        AddImprovedSurfaceHull(body, hull.points, body.transform, Vector2.zero);
                    if (body.sealedVessel) AddImprovedSurfaceHull(body, body.collisionProfile.lid, body.transform, Vector2.zero);
                }
                else foreach (Collider2D collider in body.solidColliders)
                {
                    if (collider == null || !collider.enabled || collider.isTrigger) continue;
                    if (collider is PolygonCollider2D polygon)
                        for (int path = 0; path < polygon.pathCount; path++)
                        {
                            polygon.GetPath(path, improvedPolygonPath);
                            AddImprovedSurfaceHull(body, improvedPolygonPath, polygon.transform, polygon.offset);
                        }
                    else if (collider is BoxCollider2D box)
                    {
                        Vector2 half = box.size * .5f;
                        improvedBoxPath[0] = -half; improvedBoxPath[1] = new Vector2(half.x, -half.y);
                        improvedBoxPath[2] = half; improvedBoxPath[3] = new Vector2(-half.x, half.y);
                        AddImprovedSurfaceHull(body, improvedBoxPath, box.transform, box.offset);
                    }
                }
            }
            bool hullsChanged = improvedUploadedHullCount != improvedSurfaceHulls.Count;
            for (int i = 0; i < improvedSurfaceHulls.Count; i++)
            {
                GpuImprovedSurfaceHull value = improvedSurfaceHulls[i], previous = improvedUploadedHulls[i];
                if (!value.Bounds.Equals(previous.Bounds) || value.First != previous.First || value.Count != previous.Count
                    || value.VesselId != previous.VesselId || value.Held != previous.Held) hullsChanged = true;
                improvedUploadedHulls[i] = value;
            }
            bool verticesChanged = improvedUploadedVertexCount != improvedSurfaceVertices.Count;
            for (int i = 0; i < improvedSurfaceVertices.Count; i++)
            {
                if (!improvedUploadedVertices[i].Equals(improvedSurfaceVertices[i])) verticesChanged = true;
                improvedUploadedVertices[i] = improvedSurfaceVertices[i];
            }
            if (hullsChanged && improvedSurfaceHulls.Count > 0) improvedSurfaceHullBuffer.SetData(improvedSurfaceHulls);
            if (verticesChanged && improvedSurfaceVertices.Count > 0) improvedSurfaceVertexBuffer.SetData(improvedSurfaceVertices);
            improvedUploadedHullCount = improvedSurfaceHulls.Count;
            improvedUploadedVertexCount = improvedSurfaceVertices.Count;
        }

        private void AddImprovedSurfaceHull(FluidExperimentBody body, IReadOnlyList<Vector2> points, Transform basis, Vector2 offset)
        {
            if (points == null || points.Count < 3) return;
            if (improvedSurfaceVertices.Count + points.Count > MaximumBoundarySegments)
                throw new InvalidOperationException("Improved surface wall-mask vertex budget exceeded.");
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            int first = improvedSurfaceVertices.Count;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 p = basis.TransformPoint(points[i] + offset);
                improvedSurfaceVertices.Add(p); min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            improvedSurfaceHulls.Add(new GpuImprovedSurfaceHull { Bounds = new Vector4(min.x, min.y, max.x, max.y),
                First = (uint)first, Count = (uint)points.Count, VesselId = body.Id, Held = body.IsHeld ? 1u : 0u });
        }

        public GpuImprovedSurfaceParticle[] ReadImprovedSurfaceParticles()
        {
            if (!ImprovedSurfaceReady) return Array.Empty<GpuImprovedSurfaceParticle>();
            var particles = new GpuImprovedSurfaceParticle[particleCapacity];
            improvedSurfaceParticleBuffer.GetData(particles);
            return particles;
        }

        private void DisposeImprovedSurface()
        {
            foreach (GraphicsBuffer buffer in new[] { improvedSurfaceHistoryBuffer, improvedSurfaceParticleBuffer,
                improvedSurfaceHullBuffer, improvedSurfaceVertexBuffer, improvedSurfaceVesselBuffer, improvedSurfaceMaterialBuffer }) buffer?.Dispose();
            improvedSurfaceHistoryBuffer = improvedSurfaceParticleBuffer = improvedSurfaceHullBuffer = null;
            improvedSurfaceVertexBuffer = improvedSurfaceVesselBuffer = null;
            improvedSurfaceMaterialBuffer = null;
            if (improvedSurfaceProgram != null) Destroy(improvedSurfaceProgram);
            improvedSurfaceProgram = null; improvedSurfaceTickReady = false;
            improvedSurfaceHulls.Clear(); improvedSurfaceVertices.Clear(); improvedSurfaceVessels.Clear();
            improvedUploadedHullCount = improvedUploadedVertexCount = -1;
            improvedUploadedVesselCount = improvedUploadedMaterialCount = -1;
            ImprovedSurfaceError = null;
        }
    }
}
