Shader "QuantumChildren/Portal/Portal Display"
{
    Properties
    {
        [NoScaleOffset] _PortalTex("Portal Texture", 2D) = "black" {}
        _Tint("Tint", Color) = (1, 1, 1, 1)
        [IntRange] _StencilRef("Stencil Reference", Range(1, 255)) = 1
        [IntRange] _StencilReadMask("Stencil Read Mask", Range(0, 255)) = 255
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        [Toggle] _FlipY("Flip Y", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "PortalDisplay"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilReadMask]
                Comp Equal
                Pass Keep
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PortalTex);
            SAMPLER(sampler_PortalTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _FlipY;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                uv.y = lerp(uv.y, 1.0 - uv.y, saturate(_FlipY));
                half4 portalColor = SAMPLE_TEXTURE2D(_PortalTex, sampler_PortalTex, uv);
                return half4(portalColor.rgb * _Tint.rgb, _Tint.a);
            }
            ENDHLSL
        }
    }
}
