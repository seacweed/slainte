Shader "Hidden/Slainte/FluidExperimentEffects"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varying { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            Varying Vert(Input v) { Varying o; o.position=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            half4 Frag(Varying i) : SV_Target
            {
                float radial=length(i.uv*2-1);
                float coverage=1-smoothstep(.55,1,radial);
                return half4(i.color.rgb,i.color.a*coverage);
            }
            ENDHLSL
        }
    }
}
