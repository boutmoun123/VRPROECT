Shader "Hidden/IndependentFluidPreviewGpu"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.1, 0.25, 1, 1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            StructuredBuffer<float4> _ParticleData;

            float4 _BaseColor;
            float3 _PreviewSize;
            float _FillPercent;
            float _TimeValue;
            float _ParticleScale;
            float _ParticleAlpha;
            float _DensityStrength;
            float _CollisionStrength;
            float _FlowStrength;
            float4 _Slosh;
            float4x4 _LocalToWorld;

            struct appdata
            {
                float3 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR0;
            };

            float Hash(float n)
            {
                return frac(sin(n) * 43758.5453);
            }

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                float4 packed = _ParticleData[instanceID];
                float3 p = packed.xyz;
                float seed = packed.w;
                float layer = saturate((p.y + 0.46) / max(0.001, lerp(-0.44, 0.43, _FillPercent) + 0.46));

                float phase = seed * 6.28318 + _TimeValue * lerp(0.35, 1.9, layer);
                float2 slosh = _Slosh.xy * 0.95 + _Slosh.zw * 0.018;
                float swirl = sin(phase + p.x * 8.0 + p.z * 5.0) * 0.018 * _FlowStrength;
                p.x += saturate(layer + 0.18) * slosh.x + swirl;
                p.z += saturate(layer + 0.18) * slosh.y + cos(phase * 0.8 + p.z * 7.0) * 0.016 * _FlowStrength;
                p.y += sin(phase * 1.55 + p.x * 4.0) * 0.007 * layer * _FlowStrength;
                p = clamp(p, float3(-0.46, -0.46, -0.46), float3(0.46, lerp(-0.44, 0.43, _FillPercent), 0.46));

                float wallX = min(p.x + 0.46, 0.46 - p.x);
                float wallY = min(p.y + 0.46, lerp(-0.44, 0.43, _FillPercent) - p.y);
                float wallZ = min(p.z + 0.46, 0.46 - p.z);
                float wallPressure = saturate(1.0 - min(wallX, min(wallY, wallZ)) / 0.12);
                float compression = saturate(length(slosh) * 3.0 + wallPressure * 0.7 + layer * 0.25);
                float density = saturate((1.0 - layer * 0.4) * _DensityStrength + compression * 0.55);

                float3 localCentre = p * _PreviewSize;
                float3 worldCentre = mul(_LocalToWorld, float4(localCentre, 1)).xyz;
                float3 cameraRight = float3(UNITY_MATRIX_I_V._m00, UNITY_MATRIX_I_V._m01, UNITY_MATRIX_I_V._m02);
                float3 cameraUp = float3(UNITY_MATRIX_I_V._m10, UNITY_MATRIX_I_V._m11, UNITY_MATRIX_I_V._m12);
                float sizePulse = lerp(0.88, 1.18, Hash((float)instanceID * 13.17)) * lerp(1.0, 1.28, compression);
                float3 worldPos = worldCentre + (cameraRight * v.vertex.x + cameraUp * v.vertex.y) * _ParticleScale * sizePulse;

                float3 lower = lerp(_BaseColor.rgb, float3(0.02, 0.12, 0.24), 0.22);
                float3 upper = lerp(_BaseColor.rgb, float3(0.42, 0.86, 1.0), 0.30);
                float3 flowColor = lerp(lower, upper, layer);
                float3 pressureColor = lerp(flowColor, float3(1.0, 0.2, 0.46), saturate(density * 0.75));
                pressureColor = lerp(pressureColor, float3(1.0, 1.0, 1.0), saturate((compression - 0.62) * 1.6));
                float flash = _CollisionStrength * wallPressure * saturate(sin(_TimeValue * 18.0 + seed * 31.0) * 0.5 + 0.5);

                v2f o;
                o.pos = UnityWorldToClipPos(worldPos);
                o.uv = v.uv;
                o.color.rgb = lerp(pressureColor, float3(1.0, 1.0, 1.0), flash * 0.55);
                o.color.a = saturate(_ParticleAlpha * lerp(0.75, 1.7, density) + flash * 0.08);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 centered = i.uv * 2.0 - 1.0;
                float radial = dot(centered, centered);
                clip(1.0 - radial);
                float soft = saturate(1.0 - radial);
                return float4(i.color.rgb, i.color.a * soft);
            }
            ENDCG
        }
    }
}
