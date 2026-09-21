Shader "Hidden/Slainte/PhysicsLab/StreamAccumulation"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "PhysicsLabStreamTypes.hlsl"
        StructuredBuffer<StreamSegment> _StreamSegments;
        StructuredBuffer<float4> _GpuLiquidColors;
        struct Varyings
        {
            float4 positionCS:SV_POSITION;
            float2 world:TEXCOORD0;
            nointerpolation float4 ends:TEXCOORD1;
            nointerpolation float2 radii:TEXCOORD2;
            nointerpolation float4 color:TEXCOORD3;
            nointerpolation uint active:TEXCOORD4;
        };
        Varyings Vert(uint vertex:SV_VertexID, uint instance:SV_InstanceID)
        {
            StreamSegment s = _StreamSegments[instance];
            float2 corner = vertex == 0u || vertex == 3u ? float2(-1,-1)
                : vertex == 1u ? float2(-1,1) : vertex == 2u || vertex == 4u ? float2(1,1) : float2(1,-1);
            float len = distance(s.a,s.b);
            float2 forward = len > .00001 ? (s.b-s.a)/len : float2(0,1);
            float2 side = float2(-forward.y,forward.x);
            float radius = max(s.radiusA,s.radiusB);
            Varyings o;
            o.world = (s.a+s.b)*.5 + forward*corner.y*(len*.5+radius) + side*corner.x*radius;
            o.positionCS = TransformWorldToHClip(float3(o.world,0));
            o.ends = float4(s.a,s.b); o.radii = float2(s.radiusA,s.radiusB);
            o.color = _GpuLiquidColors[s.particleIndex]; o.active = s.active;
            return o;
        }
        float Coverage(Varyings i)
        {
            if (i.active == 0u) return 0;
            float2 segment = i.ends.zw-i.ends.xy;
            float t = saturate(dot(i.world-i.ends.xy,segment)/max(.0000001,dot(segment,segment)));
            float2 delta = i.world-lerp(i.ends.xy,i.ends.zw,t);
            float radius = max(.0001,lerp(i.radii.x,i.radii.y,t));
            return smoothstep(0,.72,saturate(1-dot(delta,delta)/(radius*radius)));
        }
        struct Mrt { float4 density:SV_Target0; float4 color:SV_Target1; };
        Mrt FragMrt(Varyings i) { Mrt o; float c=Coverage(i); o.density=c.xxxx; o.color=i.color*c; return o; }
        float4 FragDensity(Varyings i):SV_Target { return Coverage(i).xxxx; }
        float4 FragColor(Varyings i):SV_Target { return i.color*Coverage(i); }
        ENDHLSL
        Pass
        {
            Blend One One Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragMrt
            ENDHLSL
        }
        Pass
        {
            Blend One One Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragDensity
            ENDHLSL
        }
        Pass
        {
            Blend One One Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragColor
            ENDHLSL
        }
        Pass
        {
            Blend One One BlendOp Max Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragDensity
            ENDHLSL
        }
    }
}
