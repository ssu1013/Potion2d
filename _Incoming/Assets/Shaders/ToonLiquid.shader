// 容器の中の液体（Built-in レンダーパイプライン用）
// ・液体のメッシュは「容器の内側いっぱいの閉じた形」でよい。液面の高さ（ワールド座標の Y）より上を切り取る
//   → 自作のフラスコや瓶でも、液面が上下する／瓶を傾けても液面は水平のまま
// ・切り口（液面）は、メッシュの内側の面に「視線と液面の交わる所」の色を塗って表現する
//   （内側かどうかは「面の向き（法線）が視線と逆か」で判定する。VFACE はポストエフェクト用の
//     レンダーテクスチャに描くときに表裏が逆になる環境があるため使わない）
// ・色の再現：光の色・強さで液体の色相が変わらないよう、明るさだけを光から受け取る（結果画面の色と同じに見える）。
//   スポットライトなどの追加の灯りは、少しの明るさと液面のつやだけを足す（白飛びしない）
// ・液面の動き（_SurfaceFx の半径が 0 より大きいときだけ。調合フラスコ用）：
//   かき混ぜた渦・落とした物の波紋・沸騰の泡とうねりを、液面の凹凸として計算して陰影とつやを付ける。
//   液面の縁（容器の壁との境目）も凹凸に合わせて上下する
Shader "PotionFrontier/ToonLiquid"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.75, 0.72, 0.8, 1)
        _ShadeThreshold ("Shade Threshold", Range(-1, 1)) = 0.1
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.5)) = 0.03
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _RimColor ("Rim Color (A = strength)", Color) = (1, 1, 1, 0.45)
        _SurfaceLighten ("Surface Lighten", Range(0, 1)) = 0.3
        _LightTint ("Light Tint (0 = 液体の色そのまま)", Range(0, 1)) = 0.25
        _AddStrength ("Additional Light Strength", Range(0, 1)) = 0.22
        _FillWorldY ("Fill Height (world Y)", Float) = 100000
        _SurfaceFx ("Surface FX (xz center, w radius)", Vector) = (0, 0, 0, 0)
        _Swirl ("Swirl (x phase, y strength, z direction)", Vector) = (0, 0, 1, 0)
        _Boil ("Boil (x strength, y scorch)", Vector) = (0, 0, 0, 0)
        _Ripple0 ("Ripple 0 (x, z, age, strength)", Vector) = (0, 0, 0, 0)
        _Ripple1 ("Ripple 1", Vector) = (0, 0, 0, 0)
        _Ripple2 ("Ripple 2", Vector) = (0, 0, 0, 0)
        _Ripple3 ("Ripple 3", Vector) = (0, 0, 0, 0)
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    fixed4 _Color;
    fixed4 _ShadeColor;
    half _ShadeThreshold;
    half _ShadeSoftness;
    fixed4 _EmissionColor;
    fixed4 _RimColor;
    half _SurfaceLighten;
    half _LightTint;
    half _AddStrength;
    float _FillWorldY;
    float4 _SurfaceFx;
    float4 _Swirl;
    float4 _Boil;
    float4 _Ripple0, _Ripple1, _Ripple2, _Ripple3;

    float Hash21(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    // 落とした物の波紋：広がる輪と、落ちた所のくぼみ
    float RippleH(float4 rp, float2 xz)
    {
        if (rp.w <= 0.001) return 0;
        float d = distance(xz, rp.xy);
        float x = d - (rp.z * 0.16 + 0.004);
        float ring = sin(x * 140.0) * exp(-abs(x) * 32.0) * exp(-rp.z * 1.8);
        float dip = -exp(-d * 45.0) * exp(-rp.z * 9.0) * 1.5;
        return (ring + dip) * rp.w * 0.0026;
    }

    // 沸騰の泡：格子のひとつずつに、ふくらんで弾ける泡（density が小さいと泡の数が減る）
    float BubbleLayer(float2 xz, float cell, float t, float density)
    {
        float2 g = xz / cell;
        float2 id = floor(g);
        float2 f = frac(g) - 0.5;
        float rnd = Hash21(id);
        if (rnd > density) return 0;
        float2 o = (float2(Hash21(id + 3.7), Hash21(id + 9.1)) - 0.5) * 0.45;
        float phase = frac(t * (0.7 + rnd) + rnd * 7.0);
        float rad = 0.12 + 0.3 * phase;
        float d = length(f - o);
        float dome = sqrt(max(0.0, rad * rad - d * d));
        return dome * (1.0 - smoothstep(0.82, 1.0, phase)) * cell * 0.55;
    }

    float SwirlArms(float2 q, float rr)
    {
        float theta = atan2(q.y, q.x);
        return sin(3.0 * (theta - _Swirl.x) - _Swirl.z * rr * 9.0);
    }

    // 液面の高さ（平らな液面からの差、m）
    float SurfaceH(float2 xz)
    {
        if (_SurfaceFx.w <= 0.0) return 0;
        float2 q = xz - _SurfaceFx.xz;
        float rr = saturate(length(q) / _SurfaceFx.w);
        float h = 0;
        float s = _Swirl.y;
        if (s > 0.001)
        {
            // 渦：まん中がへこみ、壁ぎわが上がる。腕の形のうねりが回る
            h += s * 0.011 * (rr * rr - 0.45);
            h += s * 0.0016 * SwirlArms(q, rr) * smoothstep(0.08, 0.35, rr) * (1.0 - rr * 0.6);
        }
        h += RippleH(_Ripple0, xz) + RippleH(_Ripple1, xz) + RippleH(_Ripple2, xz) + RippleH(_Ripple3, xz);
        float b = _Boil.x;
        if (b > 0.001)
        {
            float t = _Time.y;
            h += b * 0.0018 * (sin(q.x * 45.0 + t * 5.1) * sin(q.y * 41.0 - t * 4.3) + 0.5 * sin((q.x + q.y) * 70.0 + t * 7.7));
            h += BubbleLayer(xz, 0.020, t * 1.4, b * 0.7);
            h += BubbleLayer(xz + 0.37, 0.032, t * 0.95 + 3.1, b * 0.5);
        }
        return h;
    }

    float3 SurfaceNormal(float2 xz)
    {
        const float e = 0.0015;
        float h = SurfaceH(xz);
        float hx = SurfaceH(xz + float2(e, 0));
        float hz = SurfaceH(xz + float2(0, e));
        return normalize(float3(-(hx - h) / e, 1.0, -(hz - h) / e));
    }

    // 視線と液面（水平面）の交わる所
    float3 SurfacePoint(float3 worldPos)
    {
        float3 v = worldPos - _WorldSpaceCameraPos;
        if (v.y > -1e-4) return worldPos;
        float t = (_FillWorldY - _WorldSpaceCameraPos.y) / v.y;
        return _WorldSpaceCameraPos + v * saturate(t);
    }

    // 液面の高さ（壁ぎわの切り口も、液面の凹凸に合わせて上下させる）
    float FillAt(float3 worldPos)
    {
        return _FillWorldY + SurfaceH(worldPos.xz);
    }

    float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

    // 光の色はほんの少しだけ受け取る（色相はほぼ液体の色のまま）
    float3 LightTint(float3 light)
    {
        float m = max(max(light.r, light.g), max(light.b, 1e-3));
        return lerp(float3(1, 1, 1), light / m, _LightTint);
    }

    // 光の明るさから、液体の明るさ（0.6〜1）を決める。暗い所でも色がつぶれない
    float Exposure(float3 light)
    {
        return lerp(0.6, 1.0, saturate(Luma(light) * 1.25));
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap
            #pragma multi_compile_fog
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_SHADOW_COORDS(2)
                UNITY_FOG_COORDS(3)
            };

            v2f vert (appdata_full v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_SHADOW(o, v.texcoord1.xy);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float fill = FillAt(i.worldPos);
                clip(fill - i.worldPos.y);
                float3 ambientUp = ShadeSH9(float4(0, 1, 0, 1));
                float3 col;
                float3 toCamera = _WorldSpaceCameraPos - i.worldPos;
                if (dot(i.worldNormal, toCamera) < 0)
                {
                    // 内側の面 = 液面。視線と液面の交わる所の凹凸で、陰影とつやを付ける
                    float3 p = SurfacePoint(i.worldPos);
                    float3 n = _SurfaceFx.w > 0.0 ? SurfaceNormal(p.xz) : float3(0, 1, 0);
                    float3 viewDir = normalize(p - _WorldSpaceCameraPos);
                    float3 light = ambientUp + _LightColor0.rgb * 0.85;
                    float3 baseCol = lerp(_Color.rgb, float3(1, 1, 1), _SurfaceLighten * 0.2);
                    float3 key = normalize(float3(0.25, 1.0, -0.45));
                    float slope = dot(n, key) - key.y;
                    col = baseCol * LightTint(light) * Exposure(light) * (1.04 + slope * 2.4);

                    // つや：灯りの映り込みと、波立った所のきらめき
                    float3 r = reflect(viewDir, n);
                    float3 l = normalize(_WorldSpaceLightPos0.xyz + float3(0, 0.3, 0));
                    float disturbed = saturate(length(n.xz) * 20.0);
                    float spec = pow(saturate(dot(r, l)), 48.0) * 0.28 + pow(saturate(dot(r, key)), 80.0) * 0.5 * disturbed;
                    col += spec * (LightTint(light) * 0.6 + 0.4);

                    // 浅い角度から見ると、部屋の明かりが映る
                    float fres = pow(1.0 - saturate(dot(n, -viewDir)), 5.0) * 0.3;
                    col = lerp(col, ambientUp * 1.1 + baseCol * 0.25, fres);

                    if (_SurfaceFx.w > 0.0)
                    {
                        float2 q = p.xz - _SurfaceFx.xz;
                        float rr = saturate(length(q) / _SurfaceFx.w);
                        // 渦の筋（明るい泡の線）
                        if (_Swirl.y > 0.001)
                        {
                            float arms = SwirlArms(q, rr);
                            col += baseCol * 0.18 * _Swirl.y * smoothstep(0.55, 1.0, arms) * smoothstep(0.1, 0.4, rr);
                        }
                        // 焦げ：全体が暗く茶色くなり、壁ぎわが特に焦げつく
                        if (_Boil.y > 0.001)
                        {
                            float burn = _Boil.y * (0.35 + 0.65 * smoothstep(0.5, 1.0, rr));
                            col = lerp(col, col * 0.4 + float3(0.09, 0.045, 0.015), saturate(burn));
                        }
                    }
                }
                else
                {
                    float3 n = normalize(i.worldNormal);
                    float3 l = normalize(_WorldSpaceLightPos0.xyz);
                    UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                    float lit = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, dot(n, l));
                    lit *= smoothstep(0.35, 0.65, atten);
                    float3 light = ShadeSH9(float4(n, 1)) + _LightColor0.rgb * lit;
                    float shade = lerp(0.74, 1.0, lit);
                    // 深い所ほど少し濃く（液体の厚み）。液面のすぐ下は明るい線（メニスカス）
                    float below = fill - i.worldPos.y;
                    float depthShade = lerp(0.8, 1.0, exp(-below * 8.0));
                    col = _Color.rgb * LightTint(light) * Exposure(light) * shade * depthShade;
                    col += _Color.rgb * 0.22 * smoothstep(0.006, 0.0, below) + 0.06 * smoothstep(0.0025, 0.0, below);
                    float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                    float rim = smoothstep(0.62, 0.72, 1 - saturate(dot(n, viewDir))) * _RimColor.a * 0.6;
                    col += _RimColor.rgb * rim;
                    if (_Boil.y > 0.001) col = lerp(col, col * 0.45 + float3(0.08, 0.04, 0.012), saturate(_Boil.y * 0.6));
                }
                col += _EmissionColor.rgb;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return fixed4(col, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "FORWARD_ADD"
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_SHADOW_COORDS(2)
                UNITY_FOG_COORDS(3)
            };

            v2f vert (appdata_full v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_SHADOW(o, v.texcoord1.xy);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                clip(FillAt(i.worldPos) - i.worldPos.y);
                float3 n = normalize(i.worldNormal);
                bool surface = dot(n, _WorldSpaceCameraPos - i.worldPos) < 0;
                float3 p = i.worldPos;
                if (surface)
                {
                    p = SurfacePoint(i.worldPos);
                    n = _SurfaceFx.w > 0.0 ? SurfaceNormal(p.xz) : float3(0, 1, 0); // 液面は上向きの面として照らす
                }
                float3 l = normalize(UnityWorldSpaceLightDir(i.worldPos));
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float lit = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, dot(n, l));
                float band = smoothstep(0.02, 0.10, atten) * 0.55 + smoothstep(0.25, 0.45, atten) * 0.45;
                // 明るさだけを少し足す（色相は液体の色のまま）
                float3 col = _Color.rgb * Luma(_LightColor0.rgb) * lerp(0.35, 1.0, lit) * band * _AddStrength;
                if (surface)
                {
                    float3 viewDir = normalize(p - _WorldSpaceCameraPos);
                    float spec = pow(saturate(dot(reflect(viewDir, n), l)), 40.0);
                    col += spec * _LightColor0.rgb * band * 0.3;
                }
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0));
                return fixed4(col, 1);
            }
            ENDCG
        }

        // 影も液面より上は落とさない
        Pass
        {
            Name "SHADOWCASTER"
            Tags { "LightMode" = "ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_shadowcaster

            struct v2f
            {
                V2F_SHADOW_CASTER;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                clip(_FillWorldY - i.worldPos.y);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
}
