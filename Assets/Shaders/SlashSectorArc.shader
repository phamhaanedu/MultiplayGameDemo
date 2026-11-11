Shader "CapsuleArena/VFX/SlashSectorArc"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 0.62, 0.11, 0.88)
        [HDR] _EmissionColor ("Emission Color", Color) = (2.2, 1.35, 0.24, 1)
        _SweepProgress ("Sweep Progress", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SlashSectorUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _EmissionColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _SweepProgress)
            UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float sweepProgress = saturate(UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _SweepProgress));
                if (sweepProgress <= 0.001)
                {
                    discard;
                }

                clip(sweepProgress - input.uv.x);

                float4 baseCol = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
                float4 emisCol = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _EmissionColor);

                // Radial gradient (stronger toward outer blade arc) + crisp leading-edge highlight
                float radialMask = lerp(0.35, 1.0, smoothstep(0.0, 0.85, input.uv.y));
                float distFromLeadingEdge = max(0.0, sweepProgress - input.uv.x);
                float leadingEdgeBoost = 1.0 + 0.85 * (1.0 - smoothstep(0.0, 0.14, distFromLeadingEdge));
                float trailSoftness = lerp(0.45, 1.0, saturate(input.uv.x / max(0.05, sweepProgress)));

                half3 rgb = (baseCol.rgb * 0.45 + emisCol.rgb * 0.65) * leadingEdgeBoost;
                half alpha = saturate(baseCol.a * radialMask * trailSoftness);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
