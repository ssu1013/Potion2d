using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// レシピ本のページに出す瓶の絵（2D 版）。液体の色・特性（輝く・濁った・泡立つ）から、瓶の絵をコードで描く。
    /// 3D 版は部屋から離れた所に置いた 3D の瓶をカメラで写していた（使い方は同じ：Show・Clear・Texture）。
    /// 自分で描いた瓶の絵に替えるときは、このクラスの Draw を置き換える（または RecipeBookUI の previewImage に Sprite を入れる）。
    /// </summary>
    public sealed class LiquidPreview
    {
        private readonly Texture2D texture;
        private readonly int size;
        private readonly Color background;
        private readonly Color[] pixels;

        public Texture Texture => texture;

        public LiquidPreview(int size, Color background)
        {
            this.size = Mathf.Clamp(size, 64, 512) / 2; // 描く手間を減らすため、半分の大きさで描いて拡大する
            this.background = background;
            texture = new Texture2D(this.size, this.size, TextureFormat.RGBA32, false)
            {
                name = "RecipeBookPreview",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            pixels = new Color[this.size * this.size];
            Clear();
        }

        public void Show(LiquidRecord record)
        {
            if (record == null)
            {
                Clear();
                return;
            }
            Draw(record.color, record.TraitStrength(LiquidTraits.Shimmer), record.TraitStrength(LiquidTraits.Cloudy),
                 record.TraitStrength(LiquidTraits.Foamy), record.TraitStrength(LiquidTraits.Scorched), record.id != null ? record.id.GetHashCode() : 0);
        }

        public void Clear()
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;
            texture.SetPixels(pixels);
            texture.Apply();
        }

        /// <summary>3D 版との互換用（2D 版では何もしない）</summary>
        public void SetActive(bool active) { }

        /// <summary>3D 版との互換用（2D 版では何もしない）</summary>
        public void Tick(float dt) { }

        // ------------------------------------------------------------------

        private void Draw(Color liquid, float shimmer, float cloudy, float foamy, float scorched, int seed)
        {
            liquid.a = 1f;
            if (cloudy > 0f) liquid = Color.Lerp(liquid, new Color(0.75f, 0.75f, 0.72f, 1f), cloudy * 0.45f);
            if (scorched > 0f) liquid = Color.Lerp(liquid, new Color(0.25f, 0.18f, 0.12f, 1f), scorched * 0.6f);
            float px = 2f / size;
            var rng = new System.Random(seed);
            int sparkCount = Mathf.RoundToInt(shimmer * 22f);
            var sparks = new Vector3[sparkCount];
            for (int i = 0; i < sparkCount; i++)
            {
                sparks[i] = new Vector3((float)rng.NextDouble() * 0.7f - 0.35f, (float)rng.NextDouble() * 0.6f - 0.6f, 0.012f + (float)rng.NextDouble() * 0.02f);
            }
            const float level = 0.02f;

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    Color c = background;

                    // 影
                    float shadow = Ellipse(u, v + 0.8f, 0.42f, 0.06f);
                    c = Color.Lerp(c, new Color(0.35f, 0.25f, 0.15f, 1f), Mathf.Clamp01(0.5f - shadow / px) * 0.35f);

                    // 瓶の形：丸い胴＋首＋口
                    float body = Circle(u, v + 0.25f, 0.48f);
                    float neck = Box(u, v - 0.33f, 0.14f, 0.22f, 0.03f);
                    float lip = Box(u, v - 0.57f, 0.18f, 0.035f, 0.02f);
                    float glass = Mathf.Min(body, Mathf.Min(neck, lip));
                    float inside = Mathf.Clamp01(0.5f - glass / px);

                    // 中身：胴の内側で、液面より下
                    float inner = Circle(u, v + 0.25f, 0.44f);
                    float liquidCover = Mathf.Clamp01(0.5f - inner / px) * Mathf.Clamp01(0.5f - (v - level) / px);
                    if (liquidCover > 0f)
                    {
                        float depth = Mathf.Clamp01((level - v) / 0.95f);
                        Color lc = Color.Lerp(liquid * 1.15f, liquid * 0.7f, depth);
                        lc.a = 1f;
                        // 液面のすぐ下の明るい線・泡
                        float meniscus = Mathf.Clamp01(1f - Mathf.Abs(v - (level - 0.015f)) / 0.015f);
                        lc = Color.Lerp(lc, Color.white, meniscus * 0.35f + (v > level - 0.07f ? foamy * 0.6f : 0f));
                        for (int i = 0; i < sparkCount; i++)
                        {
                            float d = Mathf.Sqrt((u - sparks[i].x) * (u - sparks[i].x) + (v - sparks[i].y) * (v - sparks[i].y));
                            lc = Color.Lerp(lc, Color.white, Mathf.Clamp01(1f - d / sparks[i].z));
                        }
                        c = Color.Lerp(c, lc, liquidCover);
                    }

                    // ガラス：うすい白＋ふち＋つや
                    c = Color.Lerp(c, Color.white, inside * 0.12f);
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(glass) / (px * 2.5f));
                    c = Color.Lerp(c, new Color(1f, 1f, 1f, 1f), edge * 0.75f);
                    float shine = Box(u + 0.24f, v + 0.18f, 0.035f, 0.2f, 0.035f);
                    c = Color.Lerp(c, Color.white, Mathf.Clamp01(0.5f - shine / px) * 0.45f * inside);

                    // 栓
                    float cork = Box(u, v - 0.66f, 0.12f, 0.07f, 0.02f);
                    c = Color.Lerp(c, new Color(0.6f, 0.42f, 0.26f, 1f), Mathf.Clamp01(0.5f - cork / px));

                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
        }

        private static float Circle(float x, float y, float r) => Mathf.Sqrt(x * x + y * y) - r;

        private static float Ellipse(float x, float y, float rx, float ry)
        {
            float v = (x * x) / (rx * rx) + (y * y) / (ry * ry);
            return (Mathf.Sqrt(v) - 1f) * Mathf.Min(rx, ry);
        }

        private static float Box(float x, float y, float hx, float hy, float radius)
        {
            float qx = Mathf.Abs(x) - hx + radius;
            float qy = Mathf.Abs(y) - hy + radius;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }
    }
}
