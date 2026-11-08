// 調べるときのハイライト用：物の形をステンシルに書くだけのシェーダー（画面には何も描かない）。
// OutlineOnly の線が物の内側に入らないようにするために、線より先に描く。
Shader "PotionFrontier/OutlineMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" "IgnoreProjector" = "True" }

        Pass
        {
            Name "MASK"
            Tags { "LightMode" = "Always" }
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask 0
            Stencil
            {
                Ref 64
                WriteMask 64
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert (appdata v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return UnityObjectToClipPos(v.vertex);
            }

            fixed4 frag () : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
