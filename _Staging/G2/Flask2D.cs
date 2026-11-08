using System.Collections;
using PotionFrontier.Alchemy;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 調合フラスコの見た目（Flask の Prefab のルートに付ける）。中身は BrewSession2D から読む。
    ///
    /// 絵を差し替えるところ（Prefab の「Art」の下）：
    ///   GlassBack  … ガラスの奥側（液体の後ろに見える部分）
    ///   LiquidMask … フラスコの内側の形（白で塗りつぶした形。液体はこの形に切り抜かれる）
    ///   Liquid     … 液体（白い四角でよい。液体の色で塗られる）
    ///   Surface    … 液面（白い楕円。少し明るい色で塗られる）
    ///   GlassFront … ガラスの手前側（つや・ふち）
    /// 目印（「Anchors」の下の空のオブジェクト。絵を替えたら位置を合わせる）：
    ///   Mouth … 口（注ぐ・入れる位置）／ LiquidBottom … 液体の底 ／ LiquidTop … 満タンのときの液面
    /// </summary>
    public class Flask2D : MonoBehaviour
    {
        [Header("絵（差し替えるところ）")]
        [SerializeField, Tooltip("液体（白い四角。液体の色で塗られ、LiquidMask の形に切り抜かれる）")] private SpriteRenderer liquid;
        [SerializeField, Tooltip("液面（白い楕円。液体より少し明るい色で塗られる）")] private SpriteRenderer surface;
        [SerializeField, Min(0.1f), Tooltip("液体の幅（ワールド単位。フラスコの内側より少し広くする）")] private float liquidWidth = 2.6f;
        [SerializeField, Min(0.02f), Tooltip("液面の厚み（ワールド単位）")] private float surfaceThickness = 0.2f;

        [Header("目印（絵を替えたら位置を合わせる）")]
        [SerializeField] private Transform mouth;
        [SerializeField] private Transform liquidBottom;
        [SerializeField] private Transform liquidTop;

        [Header("液体の光（ボワッと光る）")]
        [SerializeField, Tooltip("液体の色で光る Point Light 2D")] private Light2D glow;
        [SerializeField, Min(0f), Tooltip("液体が入っているときの、いちばん弱い光")] private float glowMin = 0.2f;
        [SerializeField, Min(0f), Tooltip("効力が強いときの光（1 を超えるとブルームでにじむ）")] private float glowMax = 2.6f;
        [SerializeField, Min(1f), Tooltip("この効力（属性の合計）でいちばん強く光る")] private float potencyForFullGlow = 90f;
        [SerializeField, Min(0f), Tooltip("「輝く」特性があるときに足す光")] private float shimmerBonus = 1.2f;
        [SerializeField, Range(0f, 1f), Tooltip("未知の液体（失敗作）の光の割合")] private float unknownGlowScale = 0.35f;
        [SerializeField] private float glowRadiusMin = 1.4f;
        [SerializeField] private float glowRadiusMax = 2.8f;

        [Header("色")]
        [SerializeField, Tooltip("液体がないときの色（注いだ瞬間の色）")] private Color emptyColor = new Color(0.75f, 0.88f, 1f, 1f);
        [SerializeField, Range(0f, 1f)] private float liquidAlpha = 0.92f;

        private BrewSession2D session;
        private float shownFill;
        private Color shownColor;
        private float wobble;
        private float flashTime = 10f;
        private Color flashColor = Color.white;
        private float splash;

        public Vector3 MouthWorld => mouth != null ? mouth.position : transform.position + Vector3.up * 1.7f;
        public Vector3 BottomWorld => liquidBottom != null ? liquidBottom.position : transform.position + Vector3.down * 1.4f;
        public Vector3 TopWorld => liquidTop != null ? liquidTop.position : transform.position + Vector3.up;
        /// <summary>今の液面の真ん中（液体がなければ底）</summary>
        public Vector3 SurfaceWorld => Vector3.Lerp(BottomWorld, TopWorld, shownFill);
        public Color ShownColor => shownColor;
        /// <summary>液体の光（部屋の作業台の光がまねる）</summary>
        public Light2D Glow => glow;
        public float ShownFill => shownFill;

        private void Awake()
        {
            shownColor = emptyColor;
            if (glow != null) glow.intensity = 0f;
        }

        private void Start()
        {
            session = FindFirstObjectByType<BrewSession2D>();
            if (session != null) session.Bottled += OnBottled;
            ApplyLiquid(0f);
        }

        private void OnDestroy()
        {
            if (session != null) session.Bottled -= OnBottled;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            FlaskSimulation sim = session != null ? session.Flask : null;
            LiquidRecord preview = session != null ? session.Preview : null;
            bool hasLiquid = session != null && session.HasLiquid;

            float target = hasLiquid ? Mathf.Clamp01(sim.VolumeMl / Mathf.Max(1f, session.CapacityMl)) : 0f;
            shownFill = Mathf.MoveTowards(shownFill, target, dt * 0.8f);

            Color targetColor = preview != null ? preview.color : emptyColor;
            targetColor.a = 1f;
            shownColor = Color.Lerp(shownColor, targetColor, 1f - Mathf.Exp(-4f * dt));

            // 液面のゆれ：かき混ぜる・沸騰・物を落としたとき
            float stir = session != null ? Mathf.Clamp01(session.StirSpeed / 3f) : 0f;
            bool boiling = sim != null && sim.IsBoiling;
            wobble += dt * (2f + stir * 8f + (boiling ? 9f : 0f));
            splash = Mathf.MoveTowards(splash, 0f, dt * 2.5f);
            ApplyLiquid(stir, boiling);
            ApplyGlow(preview, hasLiquid, dt);
        }

        private void ApplyLiquid(float stir, bool boiling = false)
        {
            if (liquid == null) return;
            Transform space = liquid.transform.parent != null ? liquid.transform.parent : transform;
            Vector3 bottom = space.InverseTransformPoint(BottomWorld);
            Vector3 top = space.InverseTransformPoint(TopWorld);
            float fullHeight = Mathf.Max(0.01f, top.y - bottom.y);
            float height = fullHeight * shownFill;
            bool visible = height > 0.005f;
            liquid.enabled = visible;
            if (surface != null) surface.enabled = visible;
            if (!visible) return;

            Color c = shownColor;
            c.a = liquidAlpha;
            liquid.color = c;
            Vector2 size = liquid.sprite != null ? (Vector2)liquid.sprite.bounds.size : Vector2.one;
            liquid.transform.localScale = new Vector3(liquidWidth / Mathf.Max(0.001f, size.x), height / Mathf.Max(0.001f, size.y), 1f);
            liquid.transform.localPosition = new Vector3(bottom.x, bottom.y + height * 0.5f, 0f);

            if (surface != null)
            {
                Color sc = Color.Lerp(shownColor, Color.white, 0.35f);
                sc.a = liquidAlpha;
                surface.color = sc;
                Vector2 ss = surface.sprite != null ? (Vector2)surface.sprite.bounds.size : Vector2.one;
                float bob = (boiling ? 0.035f : 0.01f) * Mathf.Sin(wobble * 1.7f) + splash * 0.05f * Mathf.Sin(wobble * 6f);
                float thick = surfaceThickness * (1f + stir * 0.6f + splash * 0.5f);
                surface.transform.localScale = new Vector3(liquidWidth / Mathf.Max(0.001f, ss.x), thick / Mathf.Max(0.001f, ss.y), 1f);
                surface.transform.localPosition = new Vector3(bottom.x, bottom.y + height + bob, 0f);
                // かき混ぜると渦で少し傾く
                float tilt = Mathf.Sin(wobble) * (1.5f + stir * 6f + splash * 4f);
                surface.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }
        }

        private void ApplyGlow(LiquidRecord preview, bool hasLiquid, float dt)
        {
            if (glow == null) return;
            float target = 0f;
            float k = 0f;
            if (hasLiquid && preview != null)
            {
                k = Mathf.Clamp01(preview.TotalPotency / potencyForFullGlow);
                float kind = preview.kind == LiquidKind.Unknown ? unknownGlowScale : (preview.kind == LiquidKind.Intermediate ? 0.7f : 1f);
                target = (glowMin + (glowMax - glowMin) * Mathf.Pow(k, 1.3f)) * kind + preview.TraitStrength(LiquidTraits.Shimmer) * shimmerBonus;
                // ゆっくり息をするように
                target *= 1f + 0.06f * Mathf.Sin(Time.time * 2.1f) + 0.04f * Mathf.Sin(Time.time * 5.3f);
            }
            // 瓶に詰めた瞬間の光
            flashTime += dt;
            float flash = flashTime < 1.2f ? Mathf.Sin(Mathf.Clamp01(flashTime / 1.2f) * Mathf.PI) * 2.5f : 0f;

            float intensity = Mathf.Lerp(glow.intensity, target, 1f - Mathf.Exp(-3f * dt)) ;
            glow.intensity = Mathf.Max(intensity, flash);
            glow.color = flash > intensity ? flashColor : Saturate(shownColor);
            glow.pointLightOuterRadius = Mathf.Lerp(glowRadiusMin, glowRadiusMax, Mathf.Max(k, flash * 0.4f));
            glow.pointLightInnerRadius = glow.pointLightOuterRadius * 0.2f;
            glow.transform.position = Vector3.Lerp(BottomWorld, SurfaceWorld, 0.5f);
            bool on = glow.intensity > 0.01f;
            if (glow.enabled != on) glow.enabled = on;
        }

        /// <summary>物を落としたとき：上から欠片が落ちて、液面がはねる</summary>
        public void PlayDrop(Vector3 from, Sprite sprite, Color color)
        {
            StartCoroutine(DropRoutine(from, sprite, color));
        }

        private IEnumerator DropRoutine(Vector3 from, Sprite sprite, Color color)
        {
            var go = new GameObject("DropPiece");
            go.transform.position = from;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingLayerName = "Liquid";
            sr.sortingOrder = 30;
            float scale = sprite != null ? 0.3f / Mathf.Max(0.01f, sprite.bounds.size.y) : 0.3f;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            float t = 0f;
            const float duration = 0.35f;
            while (t < duration && go != null)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);
                Vector3 to = SurfaceWorld;
                go.transform.position = new Vector3(Mathf.Lerp(from.x, to.x, u), Mathf.Lerp(from.y, to.y, u * u), 0f);
                yield return null;
            }
            if (go != null) Destroy(go);
            splash = 1f;
        }

        private void OnBottled(LiquidRecord record, bool isNew, float ml, int bottles)
        {
            flashColor = record != null ? Saturate(record.color) : Color.white;
            flashTime = record != null && record.kind == LiquidKind.Unknown ? 0.6f : 0f;
        }

        private static Color Saturate(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            Color result = Color.HSVToRGB(h, Mathf.Clamp01(s * 1.2f + 0.05f), Mathf.Max(v, 0.85f));
            result.a = 1f;
            return result;
        }
    }
}
