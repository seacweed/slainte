Shader "Slainte/PhysicsLab/Liquid"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Particle
            {
                float2 position; float2 previousPosition; float2 velocity;
                float volumeMl; float temperatureC;
                uint vesselId; uint techniqueFlags; uint stateFlags; uint active; uint padding;
            };
            StructuredBuffer<Particle> _GpuLiquidParticles;
            StructuredBuffer<float4> _GpuLiquidColors;
            float _Radius;
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR0; };
            Varyings Vert(uint vertex:SV_VertexID, uint instance:SV_InstanceID)
            {
                float2 corners[6] = { float2(-1,-1),float2(-1,1),float2(1,1),float2(-1,-1),float2(1,1),float2(1,-1) };
                Particle p = _GpuLiquidParticles[instance];
                Varyings o;
                float2 corner = corners[vertex];
                o.positionCS = TransformWorldToHClip(float3(p.position + corner * _Radius, -.01));
                o.uv = corner;
                o.color = _GpuLiquidColors[instance];
                o.color.a *= p.active;
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float radius = length(i.uv);
                clip(1-radius);
                i.color.a *= 1-smoothstep(.7,1,radius);
                return i.color;
            }
            ENDHLSL
        }
    }
}
