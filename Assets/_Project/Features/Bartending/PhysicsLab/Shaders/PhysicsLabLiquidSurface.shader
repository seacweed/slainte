Shader "Slainte/PhysicsLab/LiquidSurface"
{
    Properties
    {
        _SurfaceTex ("Premultiplied liquid surface", 2D) = "black" {}
        _HighlightStrength ("Highlight", Range(0,1)) = .3
        _HighlightWidth ("Highlight width in pixels", Range(.5,4)) = 1.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_SurfaceTex); SAMPLER(sampler_SurfaceTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SurfaceTex_TexelSize;
                float _HighlightStrength, _HighlightWidth;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float4 surface=SAMPLE_TEXTURE2D(_SurfaceTex,sampler_SurfaceTex,input.uv);
                if(surface.a<=.0001) return 0;
                float2 dx=float2(_SurfaceTex_TexelSize.x*_HighlightWidth,0);
                float2 dy=float2(0,_SurfaceTex_TexelSize.y*_HighlightWidth);
                float left=SAMPLE_TEXTURE2D(_SurfaceTex,sampler_SurfaceTex,input.uv-dx).a;
                float right=SAMPLE_TEXTURE2D(_SurfaceTex,sampler_SurfaceTex,input.uv+dx).a;
                float down=SAMPLE_TEXTURE2D(_SurfaceTex,sampler_SurfaceTex,input.uv-dy).a;
                float up=SAMPLE_TEXTURE2D(_SurfaceTex,sampler_SurfaceTex,input.uv+dy).a;
                float2 gradient=float2(left-right,down-up);
                float edge=saturate(length(gradient)/max(surface.a,.0001));
                float light=saturate(dot(gradient/max(length(gradient),.0001),normalize(float2(-.45,.9))));
                float shine=edge*light*_HighlightStrength;
                // Preserve logical opacity and mixed ingredient color; light only the thin contour.
                surface.rgb+=(surface.a-surface.rgb)*shine;
                return surface;
            }
            ENDHLSL
        }
    }
}
