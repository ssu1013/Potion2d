using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>参考イラストに合わせたUIの色</summary>
    public static class UIColors
    {
        public static readonly Color Ink = new Color(0.27f, 0.16f, 0.08f);
        public static readonly Color InkLight = new Color(0.45f, 0.30f, 0.17f);
        public static readonly Color Cream = new Color(0.99f, 0.94f, 0.83f);
        public static readonly Color CreamDim = new Color(0.86f, 0.78f, 0.64f);
        public static readonly Color Gold = new Color(1.00f, 0.80f, 0.30f);
        public static readonly Color Parchment = new Color(0.95f, 0.88f, 0.72f);
        public static readonly Color ParchmentEdge = new Color(0.72f, 0.56f, 0.36f);
        public static readonly Color WoodDark = new Color(0.24f, 0.15f, 0.09f);
        public static readonly Color Wood = new Color(0.42f, 0.27f, 0.15f);
        public static readonly Color WoodLight = new Color(0.58f, 0.40f, 0.23f);
        public static readonly Color Shadow = new Color(0.06f, 0.04f, 0.03f, 0.72f);
        public static readonly Color Outline = new Color(0.12f, 0.07f, 0.04f);
    }

    /// <summary>
    /// UIをコードで組み立てるための部品集。
    /// スプライト（角丸パネル・羊皮紙・木枠・ポーションのアイコン・照準）もここで描いて作る。
    /// 本物の絵が用意できたら、スプライトを差し替えるだけで見た目を変えられる。
    /// </summary>
    public static class UIFactory
    {
        private static Font font;
        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        /// <summary>自作のUIの絵・フォント（空の欄はコードで描いた仮の絵を使う）。GameContext が起動時に設定する</summary>
        public static UISkin Skin { get; set; }

        /// <summary>
        /// 羊皮紙の UI に掛ける色（夜のランプの下の紙：少しくすんだ暖色）。UISkin の「Paper Tint」で調整する。
        /// 紙の Image を作るときに、白の代わりにこれを使う（上の文字やアイコンの色は変わらない）
        /// </summary>
        public static Color Paper
        {
            get
            {
                Color c = Skin != null ? Skin.paperTint : new Color(0.84f, 0.75f, 0.62f, 1f);
                c.a = 1f;
                return c;
            }
        }

        /// <summary>UIスキンのフォント。なければ日本語を表示できるOSのフォント（製品版ではライセンスの明確なフォントを UISkin に入れる）</summary>
        public static Font DefaultFont
        {
            get
            {
                if (Skin != null && Skin.font != null) return Skin.font;
                if (font == null)
                {
                    font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS UI Gothic", "MS Gothic", "Hiragino Sans", "Noto Sans CJK JP", "Arial" }, 32);
                }
                return font;
            }
        }

        /// <summary>太字用のフォント（UISkin の fontBold。なければ null で、ふつうのフォントを太らせる）</summary>
        public static Font BoldFont => Skin != null ? Skin.fontBold : null;

        // ------------------------------------------------------------------
        // RectTransform / 部品
        // ------------------------------------------------------------------

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image CreateImage(Transform parent, string name, Sprite sprite, Color color)
        {
            RectTransform rt = CreateRect(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            if (sprite != null && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
                                 TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal, bool shadow = false)
        {
            RectTransform rt = CreateRect(name, parent);
            var label = rt.gameObject.AddComponent<Text>();
            label.font = DefaultFont;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.fontStyle = style;
            // 太字のフォントがあれば、太らせる代わりにそれを使う（にじまず、きれいに太くなる）
            if (BoldFont != null && (style == FontStyle.Bold || style == FontStyle.BoldAndItalic))
            {
                label.font = BoldFont;
                label.fontStyle = style == FontStyle.Bold ? FontStyle.Normal : FontStyle.Italic;
            }
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.lineSpacing = 1.05f;
            label.supportRichText = true;
            label.raycastTarget = false;
            if (shadow)
            {
                var s = rt.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, 0.75f);
                s.effectDistance = new Vector2(2f, -2f);
            }
            return label;
        }

        public static VerticalLayoutGroup Vertical(GameObject go, RectOffset padding, float spacing, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childAlignment = alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static HorizontalLayoutGroup Horizontal(GameObject go, RectOffset padding, float spacing, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childAlignment = alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static ContentSizeFitter FitContent(GameObject go, bool horizontal, bool vertical)
        {
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            return fitter;
        }

        public static LayoutElement Size(GameObject go, float width = -1f, float height = -1f, float flexibleWidth = -1f)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            if (width >= 0f) { element.minWidth = width; element.preferredWidth = width; }
            if (height >= 0f) { element.minHeight = height; element.preferredHeight = height; }
            element.flexibleWidth = flexibleWidth;
            return element;
        }

        /// <summary>キー表示（例：[E]）の小さな札。札の幅は文字に合わせて伸びる</summary>
        public static Text KeyCap(Transform parent, string key, int fontSize)
        {
            Image cap = CreateImage(parent, "KeyCap", RoundedRect(new Color(0.1f, 0.07f, 0.05f, 0.85f), UIColors.Cream, 7, 2), Color.white);
            Horizontal(cap.gameObject, new RectOffset(10, 10, 3, 3), 0f, TextAnchor.MiddleCenter);
            Size(cap.gameObject, -1f, fontSize + 14f);
            cap.gameObject.GetComponent<LayoutElement>().minWidth = fontSize + 16f;
            return Label(cap.transform, "Key", key, fontSize, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        // ------------------------------------------------------------------
        // スプライトを描く
        // ------------------------------------------------------------------

        /// <summary>角丸の四角（9スライス対応）</summary>
        public static Sprite RoundedRect(Color fill, Color border, int radius, int borderWidth)
        {
            string key = $"rr_{ColorUtility.ToHtmlStringRGBA(fill)}_{ColorUtility.ToHtmlStringRGBA(border)}_{radius}_{borderWidth}";
            if (spriteCache.TryGetValue(key, out Sprite cached)) return cached;

            int size = Mathf.Max(16, radius * 2 + 6);
            var tex = NewTexture(size, size);
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float d = RoundBoxDistance(p, new Vector2(half - 0.5f, half - 0.5f), radius);
                    pixels[y * size + x] = ShapeColor(d, fill, border, borderWidth);
                }
            }
            return Finish(key, tex, pixels, radius + 2);
        }

        /// <summary>羊皮紙（ふちが焼けた紙）</summary>
        public static Sprite Parchment()
        {
            if (Skin != null && Skin.parchment != null) return Skin.parchment;
            const string key = "parchment";
            if (spriteCache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = 128;
            const int radius = 12;
            var tex = NewTexture(size, size);
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float d = RoundBoxDistance(p, new Vector2(half - 0.5f, half - 0.5f), radius);
                    float noise = Mathf.PerlinNoise(x * 0.11f, y * 0.11f);
                    float burn = Mathf.Clamp01(1f + d / 22f); // ふちほど1に近い
                    Color paper = UIColors.Parchment * (0.95f + noise * 0.08f);
                    paper = Color.Lerp(paper, UIColors.ParchmentEdge, burn * burn);
                    paper.a = 1f;
                    pixels[y * size + x] = ShapeColor(d, paper, UIColors.Outline, 3);
                }
            }
            return Finish(key, tex, pixels, 30);
        }

        /// <summary>ボタンの絵（UIスキンのボタン → 木の板）</summary>
        public static Sprite ButtonSprite()
        {
            return Skin != null && Skin.button != null ? Skin.button : WoodPanel();
        }

        /// <summary>棚や素材箱のマス（選んでいるときは selected）</summary>
        public static Sprite SlotSprite(bool selected)
        {
            if (Skin != null)
            {
                if (selected && Skin.slotSelected != null) return Skin.slotSelected;
                if (!selected && Skin.slot != null) return Skin.slot;
            }
            return selected
                ? RoundedRect(new Color(0.26f, 0.17f, 0.09f, 1f), UIColors.Gold, 12, 5)
                : RoundedRect(new Color(0.2f, 0.13f, 0.08f, 1f), new Color(0.52f, 0.36f, 0.2f, 1f), 12, 3);
        }

        /// <summary>木の板のパネル（ポーション棚の枠など）</summary>
        public static Sprite WoodPanel()
        {
            if (Skin != null && Skin.woodPanel != null) return Skin.woodPanel;
            const string key = "woodpanel";
            if (spriteCache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = 96;
            const int radius = 10;
            var tex = NewTexture(size, size);
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float d = RoundBoxDistance(p, new Vector2(half - 0.5f, half - 0.5f), radius);
                    float grain = Mathf.PerlinNoise(x * 0.04f, y * 0.5f);
                    Color wood = Color.Lerp(UIColors.Wood, UIColors.WoodLight, grain * 0.6f);
                    if (d > -9f && d < -5f) wood = Color.Lerp(wood, UIColors.WoodLight, 0.5f); // 内側の明るい縁
                    wood.a = 1f;
                    pixels[y * size + x] = ShapeColor(d, wood, UIColors.Outline, 4);
                }
            }
            return Finish(key, tex, pixels, 20);
        }

        /// <summary>ポーションのアイコン（丸底フラスコ＋コルク。液体の色を指定）</summary>
        public static Sprite PotionIcon(Color liquid)
        {
            string key = "potion_" + ColorUtility.ToHtmlStringRGB(liquid);
            if (spriteCache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = 128;
            var tex = NewTexture(size, size);
            var pixels = new Color[size * size];
            var bodyCenter = new Vector2(64f, 50f);
            const float bodyRadius = 38f;
            var neckCenter = new Vector2(64f, 94f);
            var neckHalf = new Vector2(11f, 12f);
            var corkCenter = new Vector2(64f, 112f);
            var corkHalf = new Vector2(13f, 9f);
            const float liquidLevel = 62f;
            var cork = new Color(0.72f, 0.52f, 0.32f);
            Color liquidDark = liquid * 0.7f;
            liquidDark.a = 1f;
            Color liquidLight = Color.Lerp(liquid, Color.white, 0.35f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float dBody = (p - bodyCenter).magnitude - bodyRadius;
                    float dNeck = BoxDistance(p - neckCenter, neckHalf);
                    float dBottle = Mathf.Min(dBody, dNeck);
                    float dCork = BoxDistance(p - corkCenter, corkHalf) - 2f;
                    Color c = Color.clear;

                    if (dCork < 0f)
                    {
                        c = dCork > -3f ? UIColors.Outline : Color.Lerp(cork * 0.8f, cork, (p.y - 103f) / 18f);
                        c.a = 1f;
                    }
                    else if (dBottle < 0f)
                    {
                        if (dBottle > -4f)
                        {
                            c = UIColors.Outline;
                        }
                        else if (dBody < 0f && p.y < liquidLevel)
                        {
                            float t = Mathf.InverseLerp(12f, liquidLevel, p.y);
                            c = Color.Lerp(liquidDark, liquid, t);
                            if (p.y > liquidLevel - 4f) c = liquidLight; // 液面
                            c.a = 1f;
                        }
                        else
                        {
                            c = new Color(0.9f, 0.96f, 1f, 0.35f); // ガラス
                        }

                        // ツヤ
                        Vector2 h = p - new Vector2(46f, 62f);
                        if ((h.x * h.x) / 36f + (h.y * h.y) / 196f < 1f && dBottle < -4f) c = new Color(1f, 1f, 1f, 0.85f);
                    }
                    else if (dBottle < 1.2f || dCork < 1.2f)
                    {
                        c = UIColors.Outline;
                        c.a = Mathf.Clamp01(1.2f - Mathf.Min(dBottle, dCork));
                    }
                    pixels[y * size + x] = c;
                }
            }
            return Finish(key, tex, pixels, 0);
        }

        // ------------------------------------------------------------------

        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        private static Sprite Finish(string key, Texture2D tex, Color[] pixels, int border)
        {
            tex.SetPixels(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = key;
            spriteCache[key] = sprite;
            return sprite;
        }

        private static Color ShapeColor(float distance, Color fill, Color border, int borderWidth)
        {
            if (distance > 1f) return Color.clear;
            Color c = distance > -borderWidth ? border : fill;
            // ふちと中身の境目を少しなめらかに
            if (borderWidth > 0 && distance <= -borderWidth + 1f && distance > -borderWidth - 1f)
            {
                c = Color.Lerp(border, fill, Mathf.Clamp01(-distance - borderWidth + 0.5f));
            }
            c.a *= Mathf.Clamp01(0.5f - distance);
            return c;
        }

        private static float RoundBoxDistance(Vector2 p, Vector2 halfSize, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - halfSize + new Vector2(radius, radius);
            Vector2 qMax = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return qMax.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        private static float BoxDistance(Vector2 p, Vector2 halfSize)
        {
            return RoundBoxDistance(p, halfSize, 0f);
        }
    }
}
