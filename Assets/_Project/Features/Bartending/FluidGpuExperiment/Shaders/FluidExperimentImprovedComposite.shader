Shader "Hidden/Slainte/FluidExperiment/ImprovedComposite"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Composite"
            Cull Off ZWrite Off ZTest Always
            Blend One Zero
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_DensityTex); SAMPLER(sampler_DensityTex);
            TEXTURE2D(_ColorTex); SAMPLER(sampler_ColorTex);
            CBUFFER_START(UnityPerMaterial)
                float _Threshold, _EdgeSoftness, _OpticalAbsorption;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output; output.positionCS = TransformObjectToHClip(input.positionOS.xyz); output.uv = input.uv; return output;
            }
            float4 Frag(Varyings input):SV_Target
            {
                float density = max(0, SAMPLE_TEXTURE2D(_DensityTex, sampler_DensityTex, input.uv).r);
                float softness = max(_EdgeSoftness, fwidth(density) * .75);
                float coverage = smoothstep(_Threshold - softness, _Threshold + softness, density);
                // Empty pixels have exactly zero premultiplied output. Avoid the color
                // fetch, normalization and optical exponential over that screen area.
                if (coverage <= 0) return 0;
                float4 accumulated = SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, input.uv);
                float4 mixed = saturate(accumulated / max(density, .00001));
                // A bounded display-only optical depth. Material composition remains the
                // GPU's logical mixture; no density-dependent recoloring enters its ledger.
                float thickness = saturate((density - _Threshold) / max(.1, _Threshold * 3));
                float3 transmission = exp(-clamp(_OpticalAbsorption, 0, .4) * thickness * (1 - mixed.rgb));
                float alpha = mixed.a * coverage;
                return float4(mixed.rgb * transmission * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
