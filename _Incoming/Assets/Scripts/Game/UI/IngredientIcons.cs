using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 素材のアイコン。素材データに「アイコン」の絵があればそれを使い、なければ容器の形と素材の色から仮の絵を描く。
    /// 絵を描いて用意したら、素材データのアイコン（CSV の「アイコン」列）に入れるだけで差し替わる。
    /// </summary>
    public static class IngredientIcons
    {
        private const int Size = 128;
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static Sprite Get(IngredientData data)
        {
            if (data == null) return null;
            if (data.Icon != null) return data.Icon;
            return Draw(data.Container, data.Color);
        }

        public static Sprite Draw(ContainerStyle style, Color color)
        {
            if (style == ContainerStyle.RoundFlask) return UIFactory.PotionIcon(color);
            string key = style + "_" + ColorUtility.ToHtmlStringRGB(color);
            if (cache.TryGetValue(key, out Sprite cached)) return cached;

            var pixels = new Color[Size * Size];
            Color dark = color * 0.68f;
            dark.a = 1f;
            Color light = Color.Lerp(color, Color.white, 0.4f);
            light.a = 1f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    pixels[y * Size + x] = Shade(style, p, color, dark, light);
                }
            }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(pixels);
            tex.Apply();
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "ingredient_" + key;
            cache[key] = sprite;
            return sprite;
        }

        private static Color Shade(ContainerStyle style, Vector2 p, Color c, Color dark, Color light)
        {
            switch (style)
            {
                case ContainerStyle.TallBottle: return TallBottle(p, c, dark, light);
                case ContainerStyle.Pouch: return Pouch(p, c, dark, light);
                case ContainerStyle.Crystal: return Crystal(p, c, dark, light);
                default: return Jar(p, c, dark, light);
            }
        }

        // ------------------------------------------------------------------
        // 形ごとの絵（距離関数で描く。d < 0 が内側）
        // ------------------------------------------------------------------

        private static readonly Color Glass = new Color(0.9f, 0.96f, 1f, 0.35f);
        private static readonly Color Cork = new Color(0.72f, 0.52f, 0.32f, 1f);
        private static readonly Color Cloth = new Color(0.78f, 0.64f, 0.44f, 1f);
        private static readonly Color Outline = UIColors.Outline;

        private static Color TallBottle(Vector2 p, Color c, Color dark, Color light)
        {
            float body = RoundBox(p - new Vector2(64f, 46f), new Vector2(24f, 34f), 10f);
            float neck = RoundBox(p - new Vector2(64f, 92f), new Vector2(9f, 14f), 2f);
            float cork = RoundBox(p - new Vector2(64f, 111f), new Vector2(11f, 8f), 3f);
            float bottle = Mathf.Min(body, neck);
            const float level = 64f;
            if (cork < 0f) return cork > -3f ? Outline : Cork;
            if (bottle < 0f)
            {
                if (bottle > -4f) return Outline;
                if (body < 0f && p.y < level)
                {
                    Color liquid = Color.Lerp(dark, c, Mathf.InverseLerp(14f, level, p.y));
                    if (p.y > level - 4f) liquid = light;
                    liquid.a = 1f;
                    return Shine(p, new Vector2(52f, 54f), liquid);
                }
                return Shine(p, new Vector2(52f, 54f), Glass);
            }
            return Edge(Mathf.Min(bottle, cork));
        }

        private static Color Jar(Vector2 p, Color c, Color dark, Color light)
        {
            float body = RoundBox(p - new Vector2(64f, 50f), new Vector2(38f, 34f), 14f);
            float lid = RoundBox(p - new Vector2(64f, 94f), new Vector2(34f, 9f), 4f);
            float knob = RoundBox(p - new Vector2(64f, 108f), new Vector2(10f, 6f), 4f);
            float top = Mathf.Min(lid, knob);
            if (top < 0f) return top > -3f ? Outline : Color.Lerp(Cork * 0.85f, Cork, (p.y - 86f) / 26f);
            if (body < 0f)
            {
                if (body > -4f) return Outline;
                // 中身：粒々した素材
                float speck = Mathf.PerlinNoise(p.x * 0.18f, p.y * 0.18f);
                if (p.y < 72f)
                {
                    Color fill = Color.Lerp(dark, c, speck);
                    if (p.y > 68f) fill = light;
                    fill.a = 1f;
                    return Shine(p, new Vector2(42f, 58f), fill);
                }
                return Shine(p, new Vector2(42f, 58f), Glass);
            }
            return Edge(Mathf.Min(body, top));
        }

        private static Color Pouch(Vector2 p, Color c, Color dark, Color light)
        {
            // ふくらんだ袋（下が丸い）＋口の結び目＋ひも
            float bag = (p - new Vector2(64f, 48f)).magnitude - 40f;
            float neck = RoundBox(p - new Vector2(64f, 90f), new Vector2(16f, 10f), 4f);
            float frill = RoundBox(p - new Vector2(64f, 106f), new Vector2(24f, 8f), 6f);
            float tie = RoundBox(p - new Vector2(64f, 94f), new Vector2(20f, 3f), 1.5f);
            float shape = Mathf.Min(Mathf.Min(bag, neck), frill);
            if (tie < 0f) return tie > -1.5f ? Outline : c;
            if (shape < 0f)
            {
                if (shape > -4f) return Outline;
                float t = Mathf.PerlinNoise(p.x * 0.09f, p.y * 0.3f);
                Color cloth = Color.Lerp(Cloth * 0.85f, Cloth, t);
                cloth = Color.Lerp(cloth, c, 0.28f);
                cloth.a = 1f;
                // 中身の色がのぞく口
                if (frill < 0f && p.y > 108f) cloth = Color.Lerp(c, light, 0.3f);
                return Shine(p, new Vector2(46f, 56f), cloth, 0.35f);
            }
            return Edge(shape);
        }

        private static Color Crystal(Vector2 p, Color c, Color dark, Color light)
        {
            // 六角形の結晶（縦長）
            Vector2 q = p - new Vector2(64f, 64f);
            q.x = Mathf.Abs(q.x);
            q.y = Mathf.Abs(q.y);
            float hex = Mathf.Max(q.x - 34f, (q.x * 0.6f + q.y * 0.8f) - 50f);
            if (hex < 0f)
            {
                if (hex > -4f) return Outline;
                // 面ごとに明るさを変える
                Vector2 r = p - new Vector2(64f, 64f);
                Color face = r.x < 0f ? (r.y > 0f ? light : c) : (r.y > 0f ? c : dark);
                if (Mathf.Abs(r.x) < 2f) face = Outline;
                face.a = 1f;
                return Shine(p, new Vector2(50f, 74f), face, 0.6f);
            }
            return Edge(hex);
        }

        private static Color Shine(Vector2 p, Vector2 center, Color baseColor, float strength = 0.85f)
        {
            Vector2 h = p - center;
            if ((h.x * h.x) / 30f + (h.y * h.y) / 160f < 1f) return Color.Lerp(baseColor, new Color(1f, 1f, 1f, 0.9f), strength);
            return baseColor;
        }

        private static Color Edge(float d)
        {
            if (d >= 1.2f) return Color.clear;
            Color o = Outline;
            o.a = Mathf.Clamp01(1.2f - d);
            return o;
        }

        private static float RoundBox(Vector2 p, Vector2 half, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(radius, radius);
            Vector2 qMax = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return qMax.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }
    }
}
