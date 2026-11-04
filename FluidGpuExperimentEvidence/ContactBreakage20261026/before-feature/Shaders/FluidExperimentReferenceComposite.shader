Shader "Hidden/Slainte/FluidExperiment/ReferenceComposite"
{
    Properties
    {
        [NoScaleOffset] _DensityTex ("Particle density", 2D) = "black" {}
        [NoScaleOffset] _ColorTex ("Density weighted mixed color", 2D) = "black" {}
        _Threshold ("Surface density threshold", Range(.05,.9)) = .42
        _EdgeSoftness ("Surface edge softness", Range(.005,.15)) = .025
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ReferenceDensityComposite"
            Blend One Zero
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_DensityTex); SAMPLER(sampler_DensityTex);
            TEXTURE2D(_ColorTex); SAMPLER(sampler_ColorTex);
            CBUFFER_START(UnityPerMaterial)
                float _Threshold;
                float _EdgeSoftness;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            float4 Frag(Varyings input):SV_Target
            {
                float density = max(0, SAMPLE_TEXTURE2D(_DensityTex, sampler_DensityTex, input.uv).r);
                float4 accumulated = SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, input.uv);
                // Threshold the common density field, rather than a maximum
                // individual-particle silhouette or a source-linked stream.
                float softness = max(_EdgeSoftness, fwidth(density) * .75);
                float mask = smoothstep(_Threshold - softness, _Threshold + softness, density);
                float4 mixed = saturate(accumulated / max(density, .00001));
                float alpha = mixed.a * mask;
                // Density changes the contour, never ingredient opacity or color.
                return float4(mixed.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
