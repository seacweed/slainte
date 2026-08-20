Shader "Slainte/LiquidMetaballAccumulation"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _ParticleColor ("Particle Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        UNITY_INSTANCING_BUFFER_START(ParticleProperties)
            UNITY_DEFINE_INSTANCED_PROP(float4, _ParticleColor)
        UNITY_INSTANCING_BUFFER_END(ParticleProperties)

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            output.color = UNITY_ACCESS_INSTANCED_PROP(
                ParticleProperties, _ParticleColor);
            return output;
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
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragMrt
            #pragma multi_compile_instancing

            struct AccumulationOutput
            {
                half4 density : SV_Target0;
                half4 color : SV_Target1;
            };

            AccumulationOutput FragMrt(Varyings input)
            {
                half coverage = SAMPLE_TEXTURE2D(
                    _MainTex, sampler_MainTex, input.uv).a;
                half4 particleColor = saturate(input.color);

                AccumulationOutput output;
                output.density = coverage.xxxx;
                output.color = half4(
                    particleColor.rgb * coverage,
                    particleColor.a * coverage);
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
            #pragma vertex Vert
            #pragma fragment FragDensity
            #pragma multi_compile_instancing

            half4 FragDensity(Varyings input) : SV_Target
            {
                half coverage = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                return coverage.xxxx;
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
            #pragma vertex Vert
            #pragma fragment FragColor
            #pragma multi_compile_instancing

            half4 FragColor(Varyings input) : SV_Target
            {
                half coverage = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half weightedAlpha = coverage * saturate(input.color.a);
                half3 weightedColor = input.color.rgb * coverage;
                return half4(weightedColor, weightedAlpha);
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
            #pragma vertex Vert
            #pragma fragment FragMaximumCoverage
            #pragma multi_compile_instancing

            half4 FragMaximumCoverage(Varyings input) : SV_Target
            {
                half coverage = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                return coverage.xxxx;
            }
            ENDHLSL
        }
    }
}
