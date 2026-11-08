// イラスト風のガラス（半透明）。輪郭はフチ（視線と面が平行に近い所）を暗くして描く。
Shader "PotionFrontier/ToonGlass"
{
    Properties
    {
        _Color ("Glass Color (A = opacity)", Color) = (0.85, 0.95, 1.0, 0.16)
        _RimColor ("Rim Color (A = strength)", Color) = (1, 1, 1, 0.55)
        _HighlightColor ("Highlight Color (A = strength)", Color) = (1, 1, 1, 0.75)
        _OutlineColor ("Outline Color", Color) = (0.12, 0.07, 0.04, 1)
        _EdgeWidth ("Edge Width", Range(0, 0.5)) = 0.14
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _RimColor;
            fixed4 _HighlightColor;
            fixed4 _OutlineColor;
            float _EdgeWidth;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float facing = saturate(abs(dot(n, viewDir)));
                float fresnel = 1 - facing;

                // 画面上で左上を向いた面にツヤのハイライト（イラストの白い反射）
                float3 viewNormal = mul((float3x3)UNITY_MATRIX_V, n);
                float spec = smoothstep(0.72, 0.78, dot(normalize(viewNormal.xy + 1e-5), normalize(float2(-0.55, 0.85))))
                           * smoothstep(0.25, 0.45, fresnel) * (1 - smoothstep(0.75, 0.85, fresnel));

                float rim = smoothstep(0.55, 0.7, fresnel) * _RimColor.a;

                float3 col = _Color.rgb;
                float alpha = _Color.a;
                col = lerp(col, _RimColor.rgb, rim);
                alpha = max(alpha, rim * 0.6);
                col = lerp(col, _HighlightColor.rgb, spec * _HighlightColor.a);
                alpha = max(alpha, spec * _HighlightColor.a);

                // 輪郭
                if (fresnel > 1 - _EdgeWidth)
                {
                    col = _OutlineColor.rgb;
                    alpha = _OutlineColor.a;
                }
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
