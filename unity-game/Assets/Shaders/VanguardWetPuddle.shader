Shader "VanguardZero/WetPuddle"
{
    Properties
    {
        _BaseMap ("Puddle Mask (RGBA)", 2D) = "white" {}
        _BumpMap ("Ripple Normal Map", 2D) = "bump" {}
        _Tint ("Water Tint", Color) = (0.04, 0.055, 0.075, 1)
        _ReflectionIntensity ("Reflection Intensity", Range(0.2, 3.0)) = 1.45
        _RippleStrength ("Ripple Strength", Range(0.0, 0.5)) = 0.08
        _RippleSpeed ("Ripple Speed", Range(0.0, 3.0)) = 0.85
        _Opacity ("Max Opacity", Range(0.1, 1.0)) = 0.88
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "WetPuddleForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float fogFactor   : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _ReflectionIntensity;
                half _RippleStrength;
                half _RippleSpeed;
                half _Opacity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 mask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                // Hard radial ellipse falloff guarantees ZERO quad border artifacts at any angle
                float2 centered = (IN.uv - 0.5) * 2.0;
                half radialFade = smoothstep(0.92h, 0.35h, length(centered));
                half alpha = saturate(mask.a * radialFade * _Opacity);
                if (alpha < 0.015h) discard;

                // Dual-scrolling subtle rain ripples in world space
                float2 ruv1 = IN.positionWS.xz * 0.42 + float2(_Time.y * 0.05 * _RippleSpeed, _Time.y * 0.03 * _RippleSpeed);
                float2 ruv2 = IN.positionWS.xz * 0.78 - float2(_Time.y * 0.04 * _RippleSpeed, _Time.y * 0.06 * _RippleSpeed);
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, ruv1));
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, ruv2));
                half3 rippleN = normalize(half3((n1.x + n2.x) * _RippleStrength, 1.0h, (n1.y + n2.y) * _RippleStrength));

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 reflDir = reflect(-viewDirWS, rippleN);

                // Sample real scene Reflection Probe / 4K HDRI Skybox!
                half4 envSample = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflDir, 0.35h);
                half3 envCol = DecodeHDREnvironment(envSample, unity_SpecCube0_HDR) * _ReflectionIntensity;

                // Main directional light crisp specular glint
                Light mainLight = GetMainLight();
                float3 halfVec = normalize(mainLight.direction + viewDirWS);
                half spec = pow(saturate(dot(rippleN, halfVec)), 96.0h) * 0.75h;
                half3 specCol = mainLight.color * spec;

                // Physical Schlick Fresnel (grazing angles = mirror reflection; top-down = dark wet asphalt)
                half ndotv = saturate(dot(rippleN, viewDirWS));
                half fresnel = lerp(0.18h, 0.96h, pow(1.0h - ndotv, 3.2h));

                // Dark damp outer rim -> mirror glossy puddle core
                half coreMask = smoothstep(0.08h, 0.48h, mask.a);
                half3 waterCol = lerp(_Tint.rgb, envCol + specCol, fresnel * coreMask);
                waterCol = MixFog(waterCol, IN.fogFactor);

                return half4(waterCol, alpha);
            }
            ENDHLSL
        }
    }
}
