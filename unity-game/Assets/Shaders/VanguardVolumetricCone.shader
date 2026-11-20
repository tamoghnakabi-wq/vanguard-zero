Shader "VanguardZero/VolumetricLightCone"
{
    Properties
    {
        _Color ("Beam Color", Color) = (1.0, 0.72, 0.42, 0.16)
        _FresnelPower ("Edge Softness (Fresnel)", Range(0.5, 6.0)) = 2.6
        _FadePower ("Length Falloff Power", Range(0.5, 4.0)) = 1.65
        _SoftNear ("Near Source Fade", Range(0.01, 0.35)) = 0.08
        _NoiseStrength ("Atmospheric Mist Strength", Range(0.0, 0.6)) = 0.22
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VolumetricConeForward"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _FresnelPower;
                half _FadePower;
                half _SoftNear;
                half _NoiseStrength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = normInputs.normalWS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 normalWS = normalize(IN.normalWS);

                // Soft Fresnel edge fade so cone silhouette never shows hard polygon edges
                float ndotv = abs(dot(normalWS, viewDirWS));
                float fresnel = pow(saturate(ndotv), _FresnelPower);

                // uv.y = 1 at lamp source, 0 at wide ground end
                float along = saturate(IN.uv.y);
                float lengthFade = pow(along, _FadePower) * smoothstep(1.0, 1.0 - _SoftNear, along);

                // Subtle animated rain-mist shimmer inside beam
                float t = _Time.y * 0.65;
                float mist = 0.5 + 0.5 * sin(IN.positionWS.y * 1.8 + t * 3.1 + sin(IN.positionWS.x * 0.9 - t));
                float modVal = lerp(1.0, 0.72 + 0.28 * mist, _NoiseStrength);

                half alpha = _Color.a * fresnel * lengthFade * modVal;
                return half4(_Color.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
