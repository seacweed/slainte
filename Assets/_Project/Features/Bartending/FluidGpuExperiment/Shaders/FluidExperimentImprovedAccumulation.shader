Shader "Hidden/Slainte/FluidExperiment/ImprovedAccumulation"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct Ellipse
        {
            float2 startPosition, endPosition, direction, radii;
            float coverageWeight, birthFraction;
            uint active, vesselId;
            float birthTime;
            uint token;
            float neighborhoodWeight;
            float opacityScale;
        };
        struct Hull
        {
            float4 bounds;
            uint first, count, vesselId, held;
        };
        StructuredBuffer<Ellipse> _ImprovedEllipses;
        StructuredBuffer<float4> _GpuLiquidColors, _SurfaceVessels;
        StructuredBuffer<Hull> _ImprovedHulls;
        StructuredBuffer<float2> _ImprovedHullVertices;
        int _ImprovedHullCount, _SurfaceVesselCount;
        float _SurfaceInterpolation, _GpuParticleZ;
        struct Attributes { uint vertexId:SV_VertexID; uint instanceId:SV_InstanceID; };
        struct Varyings
        {
            float4 positionCS:SV_POSITION;
            float2 local:TEXCOORD0;
            float2 world:TEXCOORD1;
            float4 color:TEXCOORD2;
            float weight:TEXCOORD3;
            nointerpolation uint active:TEXCOORD4;
            nointerpolation uint owner:TEXCOORD5;
            nointerpolation uint held:TEXCOORD6;
            nointerpolation float2 center:TEXCOORD7;
            nointerpolation uint4 hullMask:TEXCOORD8;
        };
        float2 Corner(uint id)
        {
            if (id == 0u) return float2(-1,-1);
            if (id == 1u) return float2(-1, 1);
            if (id == 2u || id == 4u) return float2(1, 1);
            if (id == 3u) return float2(-1,-1);
            return float2(1,-1);
        }
        // This broad phase runs for the six vertices of a splat, rather than for every
        // covered pixel. A hull is excluded only when no edge can reach the entire
        // splat rectangle and the rectangle's center is outside that hull. This also
        // excludes the empty interior of a large concave glass hull. Surviving hulls
        // retain the exact pixel containment and center-to-pixel intersection test.
        bool CompatibleHull(Hull hull, uint owner, uint held)
        {
            if ((hull.held & 4u) != 0u) return true;
            if (owner != 0u && hull.vesselId == owner) return true;
            if (owner == 0u && (hull.held & 2u) != 0u) return true;
            return (hull.held & 1u) == 0u && held == 0u;
        }
        bool HullCanReachSplat(Hull hull, float2 center, float2 extent, uint owner, uint held)
        {
            if (!CompatibleHull(hull, owner, held)) return false;
            float2 low = center - extent, high = center + extent;
            if (any(high < hull.bounds.xy) || any(low > hull.bounds.zw)) return false;
            bool inside = false;
            float2 a = _ImprovedHullVertices[hull.first + hull.count - 1u];
            for (uint v = 0u; v < hull.count; ++v)
            {
                float2 b = _ImprovedHullVertices[hull.first + v];
                if (all(max(a, b) >= low) && all(min(a, b) <= high)) return true;
                if ((a.y > center.y) != (b.y > center.y))
                {
                    float crossing = (b.x - a.x) * (center.y - a.y) / (b.y - a.y) + a.x;
                    if (center.x < crossing) inside = !inside;
                }
                a = b;
            }
            return inside;
        }
        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            Ellipse e = _ImprovedEllipses[input.instanceId];
            if (e.active == 0u || _SurfaceInterpolation + .000001 < e.birthFraction)
            {
                output.positionCS = float4(2, 2, 2, 1);
                return output;
            }
            float2 corner = Corner(input.vertexId);
            float fraction = saturate((_SurfaceInterpolation - e.birthFraction) / max(.000001, 1 - e.birthFraction));
            float2 center = lerp(e.startPosition, e.endPosition, fraction);
            output.center = center;
            float2 side = float2(-e.direction.y, e.direction.x);
            output.world = center + e.direction * corner.x * e.radii.x + side * corner.y * e.radii.y;
            output.positionCS = TransformWorldToHClip(float3(output.world, _GpuParticleZ));
            output.local = corner; output.color = saturate(_GpuLiquidColors[input.instanceId]);
            output.color.a = saturate(output.color.a * e.opacityScale);
            output.weight = e.coverageWeight;
            output.active = e.active != 0u && _SurfaceInterpolation + .000001 >= e.birthFraction ? 1u : 0u;
            output.owner = e.vesselId;
            for (int i = 1; i < _SurfaceVesselCount; ++i)
                if ((uint)_SurfaceVessels[i].x == e.vesselId) { output.held = (uint)_SurfaceVessels[i].w; break; }
            float2 extent = abs(e.direction) * e.radii.x + abs(side) * e.radii.y;
            // Conservatively cover interpolation/rounding at the rectangle boundary.
            extent += .00001 * max(1, max(extent.x, extent.y));
            for (uint hullIndex = 0u; hullIndex < (uint)min(_ImprovedHullCount, 128); ++hullIndex)
                if (HullCanReachSplat(_ImprovedHulls[hullIndex], center, extent, output.owner, output.held))
                {
                    uint bit = 1u << (hullIndex & 31u);
                    if (hullIndex < 32u) output.hullMask.x |= bit;
                    else if (hullIndex < 64u) output.hullMask.y |= bit;
                    else if (hullIndex < 96u) output.hullMask.z |= bit;
                    else output.hullMask.w |= bit;
                }
            return output;
        }
        float Cross2(float2 a, float2 b) { return a.x * b.y - a.y * b.x; }
        bool OccludedByHull(float2 samplePoint, float2 center, uint owner, uint held, uint hullIndex)
        {
            Hull hull = _ImprovedHulls[hullIndex];
            // Same receiver, owned-ice and environment policy as GPU collision.
            if (!CompatibleHull(hull, owner, held)) return false;
            // Testing only whether this pixel is inside solid glass leaves the far side
            // of a wide splat visible beyond thin walls. Trace support from its physical
            // center so a real wall blocks the entire contribution behind that wall.
            if (any(max(samplePoint, center) < hull.bounds.xy) || any(min(samplePoint, center) > hull.bounds.zw)) return false;
            bool inside = false;
            float2 a = _ImprovedHullVertices[hull.first + hull.count - 1u];
            for (uint v = 0u; v < hull.count; ++v)
            {
                float2 b = _ImprovedHullVertices[hull.first + v];
                float2 travel = samplePoint - center, edge = b - a;
                float denominator = Cross2(travel, edge);
                if (abs(denominator) > .00000001)
                {
                    float2 offset = a - center;
                    float t = Cross2(offset, edge) / denominator;
                    float u = Cross2(offset, travel) / denominator;
                    if (t > .00001 && t <= 1 && u >= 0 && u <= 1) return true;
                }
                if ((a.y > samplePoint.y) != (b.y > samplePoint.y))
                {
                    float crossing = (b.x - a.x) * (samplePoint.y - a.y) / (b.y - a.y) + a.x;
                    if (samplePoint.x < crossing) inside = !inside;
                }
                a = b;
            }
            return inside;
        }
        bool OccludedBySolid(Varyings input)
        {
            [unroll] for (uint group = 0u; group < 4u; ++group)
            {
                uint candidates = input.hullMask[group];
                while (candidates != 0u)
                {
                    uint bit = (uint)firstbitlow(candidates);
                    candidates &= candidates - 1u;
                    if (OccludedByHull(input.world, input.center, input.owner, input.held, group * 32u + bit)) return true;
                }
            }
            // The bitset is an optimization, never a geometry limit. Larger scenes keep
            // the previous exact scan for the remaining hulls without dropping a mask.
            for (uint hullIndex = 128u; hullIndex < (uint)_ImprovedHullCount; ++hullIndex)
                if (OccludedByHull(input.world, input.center, input.owner, input.held, hullIndex)) return true;
            return false;
        }
        float Coverage(Varyings input)
        {
            if (input.active == 0u) discard;
            float radial = 1 - dot(input.local, input.local);
            clip(radial);
            if (OccludedBySolid(input)) discard;
            return radial * radial * input.weight;
        }
        ENDHLSL
        Pass
        {
            Name "AccumulateMRT"
            Blend One One
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            struct Output { float4 density:SV_Target0; float4 color:SV_Target1; };
            Output Frag(Varyings input)
            {
                float coverage = Coverage(input);
                Output output; output.density = coverage.xxxx; output.color = input.color * coverage; return output;
            }
            ENDHLSL
        }
        Pass
        {
            Name "AccumulateDensity"
            Blend One One
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings input):SV_Target { return Coverage(input).xxxx; }
            ENDHLSL
        }
        Pass
        {
            Name "AccumulateColor"
            Blend One One
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings input):SV_Target { return input.color * Coverage(input); }
            ENDHLSL
        }
    }
}
