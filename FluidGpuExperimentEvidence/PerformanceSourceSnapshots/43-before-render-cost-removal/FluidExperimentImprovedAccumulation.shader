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
        };
        float2 Corner(uint id)
        {
            if (id == 0u) return float2(-1,-1);
            if (id == 1u) return float2(-1, 1);
            if (id == 2u || id == 4u) return float2(1, 1);
            if (id == 3u) return float2(-1,-1);
            return float2(1,-1);
        }
        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            Ellipse e = _ImprovedEllipses[input.instanceId];
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
            return output;
        }
        float Cross2(float2 a, float2 b) { return a.x * b.y - a.y * b.x; }
        bool OccludedBySolid(float2 samplePoint, float2 center, uint owner, uint held)
        {
            for (int i = 0; i < _ImprovedHullCount; ++i)
            {
                Hull hull = _ImprovedHulls[i];
                // Match the explicit held-object isolation policy. A held vessel's own
                // solid walls still mask its contents; unrelated ghosted bodies do not.
                if (hull.vesselId != owner && (hull.held != 0u || held != 0u)) continue;
                // Testing only whether this pixel is inside solid glass leaves the far side
                // of a wide splat visible beyond thin walls. Trace support from its physical
                // center so a real wall blocks the entire contribution behind that wall.
                if (any(max(samplePoint, center) < hull.bounds.xy) || any(min(samplePoint, center) > hull.bounds.zw)) continue;
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
                if (inside) return true;
            }
            return false;
        }
        float Coverage(Varyings input)
        {
            if (input.active == 0u) discard;
            float radial = 1 - dot(input.local, input.local);
            clip(radial);
            if (OccludedBySolid(input.world, input.center, input.owner, input.held)) discard;
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
