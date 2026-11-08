// 画面全体の仕上げ（Built-in レンダーパイプライン用。AtelierPostFX から使う）
// ・ブルーム：明るい所（炎・光る液体・キラキラ）がふんわりにじむ
// ・色調整：少し暖かく、少し鮮やかに、ほんの少しコントラストを上げる
// ・明るすぎる所はなめらかに頭打ち（白飛びを防ぐ）
// ・周辺減光（画面のふちを少し暗く）
// ・被写界深度（調合台に向かっているとき：手前のトレイ〜フラスコだけくっきり、奥はぼかして少し暗く）
Shader "Hidden/PotionFrontier/PostFX"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BloomTex ("Bloom", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;
    sampler2D _DofTex;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

    float4 _Threshold;      // x: しきい値, y: しきい値 - ニー, z: 2 * ニー, w: 0.25 / ニー
    float _BloomIntensity;
    float4 _Grade;          // x: 彩度, y: コントラスト, z: 周辺減光の強さ, w: 周辺減光の始まり
    float4 _Tint;
    float4 _VignetteColor;
    float4 _DofParams;      // x: くっきり見える範囲の手前（m）, y: 奥（m）, z: 手前のぼけ始めの幅, w: 奥のぼけ始めの幅
    float4 _DofAmount;      // x: ぼかしの強さ（0 で無効）, y: 奥を暗くする割合（1 = 暗くしない）, z: ぼかしの半径（縮小画像の画素）
    float4 _DofFocusA;      // くっきり見せる楕円（画面の割合）。xy: 中心, zw: 半径（0 なら無し）
    float4 _DofFocusB;

    // 画面の楕円の中は、深さに関係なくくっきり見せる（ガラスは深さを書かないので、
    // フラスコや手に持った瓶の所の深さは奥の壁になり、そのままだとぼけてしまうため）
    float FocusKeep(float4 zone, float2 uv)
    {
        if (zone.z <= 0.0 || zone.w <= 0.0) return 0.0;
        float d = length((uv - zone.xy) / zone.zw);
        return 1.0 - smoothstep(0.75, 1.0, d);
    }

    // 深さからぼけの量（手前のぼけ・奥のぼけ）を求める
    float2 DofCoc(float2 uvDepth)
    {
        float d = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uvDepth));
        float nearC = saturate((_DofParams.x - d) / max(_DofParams.z, 1e-3));
        float farC = saturate((d - _DofParams.y) / max(_DofParams.w, 1e-3));
        float keep = max(FocusKeep(_DofFocusA, uvDepth), FocusKeep(_DofFocusB, uvDepth));
        return float2(nearC, farC) * (1.0 - keep);
    }

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float2 uvBloom : TEXCOORD1;
    };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        o.uvBloom = v.texcoord;
        #if UNITY_UV_STARTS_AT_TOP
        // アンチエイリアス有効時など、元画像だけ上下が逆になる場合がある
        if (_MainTex_TexelSize.y < 0) o.uvBloom.y = 1 - o.uvBloom.y;
        #endif
        return o;
    }

    half3 Box4(float2 uv, float delta)
    {
        float4 o = _MainTex_TexelSize.xyxy * float2(-delta, delta).xxyy;
        half3 s = tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb +
                  tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb;
        return s * 0.25h;
    }

    half3 Prefilter(half3 c)
    {
        half brightness = max(c.r, max(c.g, c.b));
        half soft = clamp(brightness - _Threshold.y, 0, _Threshold.z);
        soft = _Threshold.w * soft * soft;
        half contribution = max(soft, brightness - _Threshold.x) / max(brightness, 1e-4h);
        return c * contribution;
    }

    half4 fragPrefilter(v2f i) : SV_Target
    {
        half3 c = min(Box4(i.uv, 1), 32.0h); // 極端に明るい1点がちらつかないように
        return half4(Prefilter(c), 1);
    }

    half4 fragDown(v2f i) : SV_Target
    {
        return half4(Box4(i.uv, 1), 1);
    }

    half4 fragUp(v2f i) : SV_Target
    {
        return half4(Box4(i.uv, 0.5), 1);
    }

    half3 SoftClip(half3 x)
    {
        // 0.8 までは変えず、それより明るい所をなめらかに 1.0 へ近づける
        const half k = 0.8h;
        half3 over = max(x - k, 0);
        return min(x, k) + (1 - k) * (1 - exp(-over / (1 - k)));
    }

    // 被写界深度：縮小しながら、ぼけの量を α に入れる
    half4 fragDofPrefilter(v2f i) : SV_Target
    {
        half3 c = Box4(i.uv, 0.5);
        float2 coc = DofCoc(i.uvBloom);
        return half4(c, max(coc.x, coc.y));
    }

    // 被写界深度：円盤状にぼかす（ぼけていない所の色は混ぜない）
    half4 fragDofBlur(v2f i) : SV_Target
    {
        half4 center = tex2D(_MainTex, i.uv);
        float radius = _DofAmount.z * center.a;
        half3 sum = center.rgb * 0.5h;
        half wsum = 0.5h;
        [unroll]
        for (int k = 0; k < 16; k++)
        {
            // 黄金角で並べた16点（Vogel の円盤）
            float r = sqrt((k + 0.5) / 16.0);
            float a = k * 2.39996;
            float2 o = float2(cos(a), sin(a)) * r * radius * _MainTex_TexelSize.xy;
            half4 s = tex2D(_MainTex, i.uv + o);
            half w = saturate(s.a * 1.5h) + 0.02h;
            sum += s.rgb * w;
            wsum += w;
        }
        return half4(sum / wsum, center.a);
    }

    half4 fragComposite(v2f i) : SV_Target
    {
        half4 src = tex2D(_MainTex, i.uv);
        half3 sharp = src.rgb;
        if (_DofAmount.x > 0.001)
        {
            float2 coc = DofCoc(i.uvBloom);
            half3 blurred = tex2D(_DofTex, i.uvBloom).rgb;
            float k = smoothstep(0.0, 0.6, max(coc.x, coc.y)) * _DofAmount.x;
            sharp = lerp(sharp, blurred, k);
            sharp *= lerp(1.0, _DofAmount.y, coc.y * _DofAmount.x);
        }
        half3 c = sharp + tex2D(_BloomTex, i.uvBloom).rgb * _BloomIntensity;

        c *= _Tint.rgb;
        half lum = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
        c = lerp(lum.xxx, c, _Grade.x);
        c = 0.18h * pow(max(c, 0) / 0.18h, _Grade.y);
        c = SoftClip(c);

        float2 d = (i.uv - 0.5) * float2(_ScreenParams.x / _ScreenParams.y, 1) * 1.1;
        float vig = smoothstep(_Grade.w, 1.05, length(d)) * _Grade.z;
        c = lerp(c, c * _VignetteColor.rgb, vig);
        return half4(c, src.a);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass // 0: 明るい所を取り出して縮小
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragPrefilter
            ENDCG
        }

        Pass // 1: 縮小
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDown
            ENDCG
        }

        Pass // 2: 拡大しながら足し合わせる
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUp
            ENDCG
        }

        Pass // 3: 仕上げ
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite
            ENDCG
        }

        Pass // 4: 被写界深度（縮小＋ぼけの量）
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDofPrefilter
            ENDCG
        }

        Pass // 5: 被写界深度（ぼかす）
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDofBlur
            ENDCG
        }
    }
    Fallback Off
}
