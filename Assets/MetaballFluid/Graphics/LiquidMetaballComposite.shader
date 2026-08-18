Shader "Slainte/LiquidMetaballComposite"
{
    Properties
    {
        [NoScaleOffset] _DensityTex ("Density", 2D) = "black" {}
        [NoScaleOffset] _ColorTex ("Premultiplied Color", 2D) = "black" {}
        _Threshold ("Threshold", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "LiquidMetaballComposite"
            Blend One OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DensityTex);
            SAMPLER(sampler_DensityTex);
            TEXTURE2D(_ColorTex);
            SAMPLER(sampler_ColorTex);

            CBUFFER_START(UnityPerMaterial)
                half _Threshold;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float density = max(0.0, SAMPLE_TEXTURE2D(
                    _DensityTex, sampler_DensityTex, input.uv).r);
                float4 accumulated = SAMPLE_TEXTURE2D(
                    _ColorTex, sampler_ColorTex, input.uv);

                if (density < _Threshold || accumulated.a <= 0.0)
                    return half4(0.0h, 0.0h, 0.0h, 0.0h);

                float mixedAlpha = saturate(accumulated.a / density);
                float3 mixedColor = saturate(accumulated.rgb / density);
                float finalAlpha = mixedAlpha;

                // Apply opacity after density normalization so overlap count does not
                // make otherwise identical liquid particles darker or more opaque.
                return half4(mixedColor * finalAlpha, finalAlpha);
            }
            ENDHLSL
        }
    }
}
