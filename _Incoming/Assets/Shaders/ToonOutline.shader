// 手描きイラスト風のトゥーンシェーダー（Built-in レンダーパイプライン用）
// ・光の当たり方を2段階（明るい／影）にくっきり分ける
// ・黒っぽい輪郭線を描く（裏面を画面上で一定ピクセル太らせる方式）
// ・_TriplanarScale > 0 のとき、ワールド座標でテクスチャを貼る（床・壁・家具でタイリング調整が不要）
// ・GPUインスタンシング対応（同じ形・同じマテリアルの物をまとめて描く）。輪郭線が物体の中心を使うため、静的バッチングは使わない
Shader "PotionFrontier/ToonOutline"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _TriplanarScale ("World UV Scale (0 = mesh UV)", Float) = 0
        _ShadeColor ("Shade Color", Color) = (0.55, 0.45, 0.52, 1)
        _ShadeThreshold ("Shade Threshold", Range(-1, 1)) = 0.1
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.5)) = 0.03
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _RimColor ("Rim Color (A = strength)", Color) = (1, 0.92, 0.75, 0)
        _OutlineColor ("Outline Color", Color) = (0.12, 0.07, 0.04, 1)
        _OutlineWidth ("Outline Width (px)", Range(0, 12)) = 2.4
        _OutlineUsePosition ("Outline Uses Position Dir (box)", Range(0, 1)) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_ST;
    float _TriplanarScale;
    fixed4 _Color;
    fixed4 _ShadeColor;
    half _ShadeThreshold;
    half _ShadeSoftness;
    fixed4 _EmissionColor;
    fixed4 _RimColor;

    float2 ToonUV(float2 meshUV, float3 worldPos, float3 worldNormal)
    {
        if (_TriplanarScale <= 0) return TRANSFORM_TEX(meshUV, _MainTex);
        float3 an = abs(worldNormal);
        float2 uv;
        if (an.x > an.y && an.x > an.z) uv = worldPos.zy;
        else if (an.y > an.z) uv = worldPos.xz;
        else uv = worldPos.xy;
        return uv * _TriplanarScale;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        // ---- メインライト（太陽・環境光） ----
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap
            #pragma multi_compile_fog
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                UNITY_SHADOW_COORDS(3)
                UNITY_FOG_COORDS(4)
            };

            v2f vert (appdata_full v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_SHADOW(o, v.texcoord1.xy);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                fixed4 albedo = tex2D(_MainTex, ToonUV(i.uv, i.worldPos, n)) * _Color;

                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float ndl = dot(n, l);
                float lit = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, ndl);
                lit *= smoothstep(0.35, 0.65, atten);

                float3 lightColor = _LightColor0.rgb * lerp(_ShadeColor.rgb, float3(1, 1, 1), lit);
                float3 ambient = ShadeSH9(float4(n, 1));

                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = smoothstep(0.62, 0.72, 1 - saturate(dot(n, viewDir))) * _RimColor.a;

                float3 col = albedo.rgb * (ambient + lightColor) + _RimColor.rgb * rim + _EmissionColor.rgb;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return fixed4(col, 1);
            }
            ENDCG
        }

        // ---- 追加ライト（ランタン・ろうそく等のポイントライト） ----
        Pass
        {
            Name "FORWARD_ADD"
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                UNITY_SHADOW_COORDS(3)
                UNITY_FOG_COORDS(4)
            };

            v2f vert (appdata_full v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_SHADOW(o, v.texcoord1.xy);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                fixed4 albedo = tex2D(_MainTex, ToonUV(i.uv, i.worldPos, n)) * _Color;

                float3 l = normalize(UnityWorldSpaceLightDir(i.worldPos));
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float ndl = dot(n, l);
                float lit = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, ndl);

                // 距離による減衰も段階的にして、アニメ塗りっぽくする
                float band = smoothstep(0.02, 0.10, atten) * 0.55 + smoothstep(0.25, 0.45, atten) * 0.45;

                float3 col = albedo.rgb * _LightColor0.rgb * lerp(_ShadeColor.rgb * 0.6, float3(1, 1, 1), lit) * band;
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0));
                return fixed4(col, 1);
            }
            ENDCG
        }

        // ---- 輪郭線 ----
        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode" = "Always" }
            Cull Front
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineUsePosition;

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_FOG_COORDS(0)
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                // 箱は面ごとに法線が分かれていて角に隙間ができるので、中心からの方向を使えるようにする
                float3 dir = normalize(lerp(v.normal, normalize(v.vertex.xyz + 1e-5), _OutlineUsePosition));
                o.pos = UnityObjectToClipPos(v.vertex);

                float3 viewNormal = mul((float3x3)UNITY_MATRIX_IT_MV, dir);
                float2 clipNormal = mul((float2x2)UNITY_MATRIX_P, viewNormal.xy);
                float len = length(clipNormal);
                if (len > 1e-5)
                {
                    // 遠くのものほど少し細くする
                    float width = _OutlineWidth * clamp(3.0 / max(o.pos.w, 0.01), 0.35, 1.0);
                    o.pos.xy += (clipNormal / len) * width * 2.0 / _ScreenParams.xy * o.pos.w;
                }
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = _OutlineColor;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }

    // 影を落とすための ShadowCaster パスを借りる
    Fallback "VertexLit"
}
