// 輪郭線だけを描くシェーダー（調べるときのハイライト用）
// 対象の物の上に、同じ形の「殻」をこのマテリアルで重ねて描く。
// 対象のマテリアル（自作のものでも）に手を加えないので、どんなシェーダーの物でも光らせられる。
// 先に OutlineMask が物の形をステンシルに書いておき、その外側だけに線を描く
// （ガラスのような透ける物でも、中に線が見えない）。
Shader "PotionFrontier/OutlineOnly"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.82, 0.3, 1)
        _OutlineWidth ("Outline Width (px)", Range(0, 12)) = 4.5
        _OutlineUsePosition ("Outline Uses Position Dir (box)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+2" "IgnoreProjector" = "True" }

        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode" = "Always" }
            Cull Front
            ZWrite Off
            Stencil
            {
                Ref 64
                ReadMask 64
                Comp NotEqual
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

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
                float3 dir = normalize(lerp(v.normal, normalize(v.vertex.xyz + 1e-5), _OutlineUsePosition));
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 viewNormal = mul((float3x3)UNITY_MATRIX_IT_MV, dir);
                float2 clipNormal = mul((float2x2)UNITY_MATRIX_P, viewNormal.xy);
                float len = length(clipNormal);
                if (len > 1e-5)
                {
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
}
