Shader "Slainte/LiquidMetaballComposite"
{
    Properties
    {
        [NoScaleOffset] _DensityTex ("Density", 2D) = "black" {}
        [NoScaleOffset] _ColorTex ("Premultiplied Color", 2D) = "black" {}
        [NoScaleOffset] _ShapeTex ("Maximum Coverage", 2D) = "black" {}
        _Threshold ("Threshold", Range(0, 1)) = 0.3
        _MergeStrength ("Merge Strength", Range(0, 1)) = 0.45
        _EdgeSoftness ("Edge Softness", Range(0, 0.25)) = 0.03
        _DebugView ("Debug View", Float) = 0
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
            TEXTURE2D(_ShapeTex);
            SAMPLER(sampler_ShapeTex);

            CBUFFER_START(UnityPerMaterial)
                half _Threshold;
                half _MergeStrength;
                half _EdgeSoftness;
                half _DebugView;
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
                float maximumCoverage = max(0.0, SAMPLE_TEXTURE2D(
                    _ShapeTex, sampler_ShapeTex, input.uv).r);
                float safeDensity = max(density, 0.00001);
                float3 mixedColor = saturate(accumulated.rgb / safeDensity);
                float mixedAlpha = saturate(accumulated.a / safeDensity);
                float overlapDensity = max(0.0, density - maximumCoverage);
                float hybridDensity = maximumCoverage
                    + overlapDensity * saturate(_MergeStrength);
                float edgeSoftness = max(0.00001, _EdgeSoftness);
                float shapeMask = smoothstep(
                    _Threshold - edgeSoftness,
                    _Threshold + edgeSoftness,
                    hybridDensity);

                if (_DebugView > 0.5h && _DebugView < 1.5h)
                    return half4(saturate(density).xxx, 1.0h);
                if (_DebugView > 1.5h && _DebugView < 2.5h)
                    return half4(saturate(accumulated.rgb), 1.0h);
                if (_DebugView > 2.5h && _DebugView < 3.5h)
                    return half4(mixedColor, 1.0h);
                if (_DebugView > 3.5h)
                {
                    if (_DebugView < 4.5h)
                        return half4(mixedAlpha.xxx, 1.0h);
                    if (_DebugView < 5.5h)
                        return half4(saturate(maximumCoverage).xxx, 1.0h);
                    if (_DebugView < 6.5h)
                        return half4(saturate(hybridDensity).xxx, 1.0h);
                    return half4(shapeMask.xxx, 1.0h);
                }

                if (shapeMask <= 0.0 || accumulated.a <= 0.0)
                    return half4(0.0h, 0.0h, 0.0h, 0.0h);

                float finalAlpha = mixedAlpha * shapeMask;

                // Opacity is normalized by density so identical overlaps do not
                // darken the liquid or change its logical alpha. The separate
                // hybrid shape field limits overlap amplification at the edge.
                return half4(mixedColor * finalAlpha, finalAlpha);
            }
            ENDHLSL
        }
    }
}
