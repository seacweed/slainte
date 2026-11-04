// Independently implemented density splats for the isolated GPU experiment.
// Inspired by Unity2DFluidSim's particle overlap; no hardcoded water color.
Shader "Hidden/Slainte/FluidExperiment/ReferenceAccumulation"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "FluidExperimentStreamTypes.hlsl"

        struct LiquidParticle
        {
            float2 position;
            float2 previousPosition;
            float2 velocity;
            float volumeMl;
            float temperatureC;
            uint vesselId;
            uint techniqueFlags;
            uint stateFlags;
            uint active;
            uint padding;
        };
        StructuredBuffer<LiquidParticle> _GpuLiquidParticles;
        StructuredBuffer<float4> _GpuLiquidColors;
        float _GpuParticleZ;
        float _ReferenceSupportRadius;
        float _ReferenceMaximumAspect;
        float _ReferenceFullStretchSpeed;
        StructuredBuffer<StreamHead> _ReferenceHeads;
        StructuredBuffer<StreamParticle> _ReferenceStreamParticles;
        StructuredBuffer<uint2> _ReferenceTokenLookup;
        uint _ReferenceParticleCapacity;
        uint _ReferenceHeadCount;
        float _ReferenceSimulationTime;
        float _ReferenceNozzleMaximumLength;
        float _ReferenceNozzleMaximumAge;

        struct Attributes { uint vertexId:SV_VertexID; uint instanceId:SV_InstanceID; };
        struct Varyings
        {
            float4 positionCS:SV_POSITION;
            float2 local:TEXCOORD0;
            float4 color:COLOR0;
            nointerpolation uint active:TEXCOORD1;
        };
        float2 Corner(uint id)
        {
            if (id == 0u || id == 3u) return float2(-1,-1);
            if (id == 1u) return float2(-1,1);
            if (id == 2u || id == 4u) return float2(1,1);
            return float2(1,-1);
        }
        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            output.positionCS = float4(0,0,0,1);
            if (input.instanceId >= _ReferenceParticleCapacity)
            {
                uint headIndex = input.instanceId - _ReferenceParticleCapacity;
                if (headIndex >= _ReferenceHeadCount) return output;
                StreamHead head = _ReferenceHeads[headIndex];
                if (head.token == 0u || head.sourceId == 0u || head.streamId == 0u) return output;
                uint2 entry = _ReferenceTokenLookup[head.token % (_ReferenceParticleCapacity * 2u)];
                if (entry.x != head.token || entry.y >= _ReferenceParticleCapacity) return output;
                StreamParticle stream = _ReferenceStreamParticles[entry.y];
                LiquidParticle newest = _GpuLiquidParticles[entry.y];
                float age = _ReferenceSimulationTime - stream.birthTime;
                if (stream.token != head.token || stream.sourceId != head.sourceId || stream.streamId != head.streamId
                    || stream.detached != 0u || stream.pending != 0u || newest.active == 0u
                    || (newest.stateFlags & 4u) != 0u || age < 0 || age > _ReferenceNozzleMaximumAge) return output;
                float2 delta = newest.position - head.lip;
                float gap = length(delta);
                if (gap > _ReferenceNozzleMaximumLength) return output;
                float2 forward = gap > .0001 ? delta / gap : float2(0,-1);
                float2 side = float2(-forward.y, forward.x);
                float2 corner = Corner(input.vertexId);
                // One bounded ellipse at the mouth. It can cover the physical
                // emitter's clearance offset, but never follows older particles.
                float support = clamp(head.radius * 1.9, _ReferenceSupportRadius * .4, _ReferenceSupportRadius);
                float2 center = (head.lip + newest.position) * .5;
                float2 offset = side * corner.x * support
                    + forward * corner.y * (gap * .5 + _ReferenceSupportRadius);
                output.positionCS = TransformWorldToHClip(float3(center + offset, _GpuParticleZ));
                output.local = corner;
                output.color = saturate(_GpuLiquidColors[entry.y]);
                output.active = 1u;
                return output;
            }
            LiquidParticle particle = _GpuLiquidParticles[input.instanceId];
            float2 corner = Corner(input.vertexId);
            float speed = length(particle.velocity);
            float2 forward = speed > .001 ? particle.velocity / speed : float2(0,-1);
            float2 side = float2(-forward.y, forward.x);
            float progress = saturate(speed / max(.1, _ReferenceFullStretchSpeed));
            // sqrt(aspect) and its inverse preserve area. Fast droplets become
            // longer and thinner, but cannot grow an unlimited artificial bridge.
            float aspect = lerp(1, max(1, _ReferenceMaximumAspect), progress * progress);
            float scale = sqrt(aspect);
            float2 offset = (side * corner.x / scale + forward * corner.y * scale) * _ReferenceSupportRadius;
            output.positionCS = TransformWorldToHClip(float3(particle.position + offset, _GpuParticleZ));
            output.local = corner;
            output.color = saturate(_GpuLiquidColors[input.instanceId]);
            output.active = particle.active != 0u && (particle.stateFlags & 4u) == 0u ? 1u : 0u;
            return output;
        }
        float Coverage(Varyings input)
        {
            if (input.active == 0u) discard;
            float radial = 1 - dot(input.local, input.local);
            clip(radial);
            // Smooth compact support: only spatially overlapping particles join.
            return radial * radial;
        }
        ENDHLSL

        Pass
        {
            Name "AccumulateMRT"
            Blend One One
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragMrt
            struct AccumulationOutput { float4 density:SV_Target0; float4 color:SV_Target1; };
            AccumulationOutput FragMrt(Varyings input)
            {
                float coverage = Coverage(input);
                AccumulationOutput output;
                output.density = coverage.xxxx;
                output.color = input.color * coverage;
                return output;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DensityFallback"
            Blend One One
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragDensity
            float4 FragDensity(Varyings input):SV_Target { return Coverage(input).xxxx; }
            ENDHLSL
        }
        Pass
        {
            Name "ColorFallback"
            Blend One One
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragColor
            float4 FragColor(Varyings input):SV_Target { return input.color * Coverage(input); }
            ENDHLSL
        }
    }
}
