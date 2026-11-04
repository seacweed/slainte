Shader "Slainte/FluidExperiment/IceImmersion"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [PerRendererData] _IceLiquidTex ("Owner liquid", 2D) = "black" {}
        _ImmersionStrength ("Immersion tint", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            struct Attributes { COMMON_2D_INPUTS half4 color:COLOR; UNITY_SKINNED_VERTEX_INPUTS };
            struct Varyings { COMMON_2D_OUTPUTS half4 color:COLOR; float4 screenPosition:TEXCOORD1; };
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"
            TEXTURE2D(_IceLiquidTex); SAMPLER(sampler_IceLiquidTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _ImmersionStrength;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                Varyings output = CommonUnlitVertex(input);
                output.color = input.color * _Color * unity_SpriteColor;
                output.screenPosition = ComputeScreenPos(output.positionCS);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 ice = CommonUnlitFragment(input, input.color);
                float2 uv = input.screenPosition.xy / input.screenPosition.w;
                float4 liquid = SAMPLE_TEXTURE2D(_IceLiquidTex, sampler_IceLiquidTex, uv);
                float wet = smoothstep(.025, .28, liquid.a);
                float3 tint = saturate(liquid.rgb / max(liquid.a, .0001));
                ice.rgb = lerp(ice.rgb, ice.rgb * lerp(float3(.82,.9,.96), tint, .72), wet * _ImmersionStrength);
                // Keep the sprite's authored alpha (0.55); dry pixels remain unchanged.
                return ice;
            }
            ENDHLSL
        }
    }
}
