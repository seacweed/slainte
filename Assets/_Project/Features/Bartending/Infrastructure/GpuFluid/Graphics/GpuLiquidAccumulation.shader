Shader "Hidden/Slainte/GpuLiquidAccumulation"
{
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
        float _GpuContainedParticleRadius;
        float _GpuAirborneParticleRadius;
        float _GpuParticleZ;
        float _GpuAirborneStretchMultiplier;
        float _GpuAirborneFullStretchSpeed;

        struct Attributes
        {
            uint vertexId : SV_VertexID;
            uint instanceId : SV_InstanceID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR0;
            half active : TEXCOORD1;
        };

        float2 GetCorner(uint vertexId)
        {
            if (vertexId == 0u || vertexId == 3u)
                return float2(-1.0, -1.0);
            if (vertexId == 1u)
                return float2(-1.0, 1.0);
            if (vertexId == 2u || vertexId == 4u)
                return float2(1.0, 1.0);
            return float2(1.0, -1.0);
        }

        Varyings Vert(Attributes input)
        {
            Varyings output;
            LiquidParticle particle = _GpuLiquidParticles[input.instanceId];
            float2 corner = GetCorner(input.vertexId);
            float speed = length(particle.velocity);
            float2 forward = speed > 0.01
                ? particle.velocity / speed
                : float2(0.0, -1.0);
            float2 side = float2(-forward.y, forward.x);
            float stretchProgress = particle.vesselId == 0u
                ? saturate(speed / max(0.1, _GpuAirborneFullStretchSpeed))
                : 0.0;
            float stretch = lerp(
                1.0,
                max(1.0, _GpuAirborneStretchMultiplier),
                stretchProgress);
            float inverseAreaScale = rsqrt(stretch);
            float particleRadius = particle.vesselId == 0u
                ? _GpuAirborneParticleRadius
                : _GpuContainedParticleRadius;
            float2 worldPosition = particle.position
                + side * corner.x * particleRadius * inverseAreaScale
                + forward * corner.y * particleRadius / inverseAreaScale;
            output.positionCS = TransformWorldToHClip(
                float3(worldPosition, _GpuParticleZ));
            output.uv = corner * 0.5 + 0.5;
            output.color = saturate(_GpuLiquidColors[input.instanceId]);
            output.active = particle.active != 0u
                && (particle.stateFlags & 4u) == 0u
                ? 1.0h
                : 0.0h;
            return output;
        }

        half EvaluateCoverage(Varyings input)
        {
            half2 centered = input.uv * 2.0h - 1.0h;
            half radial = saturate(1.0h - dot(centered, centered));
            return smoothstep(0.0h, 0.72h, radial) * input.active;
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

            struct AccumulationOutput
            {
                half4 density : SV_Target0;
                half4 color : SV_Target1;
            };

            AccumulationOutput FragMrt(Varyings input)
            {
                half coverage = EvaluateCoverage(input);
                AccumulationOutput output;
                output.density = coverage.xxxx;
                output.color = half4(
                    input.color.rgb * coverage,
                    input.color.a * coverage);
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

            half4 FragDensity(Varyings input) : SV_Target
            {
                return EvaluateCoverage(input).xxxx;
            }
            ENDHLSL
        }

        Pass
        {
            Name "PremultipliedColorFallback"
            Blend One One
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragColor

            half4 FragColor(Varyings input) : SV_Target
            {
                half coverage = EvaluateCoverage(input);
                return half4(
                    input.color.rgb * coverage,
                    input.color.a * coverage);
            }
            ENDHLSL
        }

        Pass
        {
            Name "MaximumCoverage"
            Blend One One
            BlendOp Max
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragMaximumCoverage

            half4 FragMaximumCoverage(Varyings input) : SV_Target
            {
                return EvaluateCoverage(input).xxxx;
            }
            ENDHLSL
        }
    }
}
